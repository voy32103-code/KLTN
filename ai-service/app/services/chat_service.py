"""
Chat service for persona-driven stakeholder replies with scenario-config gating.
"""
import os

from fastapi import APIRouter, HTTPException
from google import genai

from app.models.schemas import ChatRequest, ChatResponse
from app.services.consistency_checker import (
    check_response_consistency,
    normalize_reply_after_consistency_check,
)
from app.services.gating_service import (
    build_state_update,
    detect_question_type,
    is_overly_technical,
    load_persona_state,
    select_gated_requirements,
)
from app.services.scenario_config_service import ScenarioConfig, get_scenario_config

router = APIRouter()

_client: genai.Client | None = None
MODEL = os.getenv("MODEL_NAME", "gemini-2.5-flash")


def _get_client() -> genai.Client:
    global _client
    if _client is None:
        api_key = os.getenv("GEMINI_API_KEY")
        if not api_key:
            raise RuntimeError("GEMINI_API_KEY is not configured.")
        _client = genai.Client(api_key=api_key)
    return _client


def build_system_prompt(
    req: ChatRequest,
    state: dict[str, object],
    question_type: str | None,
    allowed_requirements: list[str],
    previously_revealed: list[str],
    newly_revealed: list[str],
    config: ScenarioConfig | None,
) -> str:
    traits = req.persona.traits
    scenario_title = config.scenario_title if config else (req.scenarioTitle or "Unknown scenario")
    scenario_context = config.context if config else "You work at an organization that needs a new software system."
    technical_question = "yes" if is_overly_technical(req.studentMessage) else "no"

    previously_revealed_text = (
        "\n".join(f"- {item}" for item in previously_revealed)
        if previously_revealed else "- None yet"
    )
    new_reveal_text = (
        "\n".join(f"- {item}" for item in newly_revealed)
        if newly_revealed else "- None. Do not reveal a new requirement in this turn."
    )
    allowed_text = (
        "\n".join(f"- {item}" for item in allowed_requirements)
        if allowed_requirements else "- No requirement is available for disclosure in this turn."
    )

    return f"""=== LAYER 1: SYSTEM ROLE ===
You are a virtual stakeholder in a requirements elicitation exercise.
A student analyst is interviewing you to discover software requirements.
Stay in character at all times. Never mention that you are an AI.

=== LAYER 2: SCENARIO CONTEXT ===
Scenario title: {scenario_title}
{scenario_context}
The student is trying to understand business needs, rules, and constraints.

=== LAYER 3: PERSONA PROFILE ===
Your name: {req.persona.name}
Your role: {req.persona.roleTitle}
Personality traits: {traits}
Communication style: {req.persona.style}
Current mood: {state["mood"]}
Current patience level: {state["patience"]}/1.0
Turn count so far: {state["turn_count"]}

Behavior rules:
- If patience is low, give shorter answers.
- If mood is rushed or irritated, sound busy and less detailed.
- If the student asks a technical implementation question, redirect to business concerns.

=== LAYER 4: INFORMATION GATING ===
Previously revealed requirements you may reference again:
{previously_revealed_text}

Candidate new requirement for this turn:
{new_reveal_text}

Full allowed knowledge for this turn:
{allowed_text}

Disclosure rules:
- Reveal at most ONE new requirement in this turn.
- If no candidate new requirement is listed, do not reveal a new requirement.
- If the student asks a vague question, stay high-level and brief.
- If the student asks a specific, targeted question, answer with more business detail.
- Do not volunteer unrelated requirements.

=== LAYER 5: RESPONSE GUARDS ===
- Do NOT invent requirements not listed above.
- Do NOT dump all rules in one answer.
- Do NOT provide implementation details.
- Do NOT tell the student what to ask next.
- Stay consistent with earlier answers.

=== LAYER 6: TURN CONTROL ===
Detected student question type: {question_type or "Unknown"}
Overly technical question: {technical_question}

Response guidelines:
- Respond naturally as a real person would.
- Keep responses to 1-4 sentences unless clarification truly needs more detail.
- Prioritize business language over system design language.
""" 


def apply_consistency_guard(
    reply: str,
    req: ChatRequest,
    question_type: str | None,
    allowed_requirements: list[str],
    newly_revealed: list[str],
    config: ScenarioConfig | None,
) -> str:
    consistency_check = check_response_consistency(reply, allowed_requirements, config)
    safe_fallback = build_fallback_reply(
        req,
        question_type,
        allowed_requirements,
        newly_revealed,
    )
    return normalize_reply_after_consistency_check(
        reply,
        consistency_check,
        safe_fallback=safe_fallback,
    )


@router.post("/chat", response_model=ChatResponse)
async def chat(req: ChatRequest):
    try:
        question_type = detect_question_type(req.studentMessage)
        state = load_persona_state(req)
        config = get_scenario_config(req.scenarioTitle, req.availableRequirements)
        allowed_requirements, previously_revealed, newly_revealed = select_gated_requirements(
            req,
            state,
            question_type,
            config,
        )
        system_prompt = build_system_prompt(
            req,
            state,
            question_type,
            allowed_requirements,
            previously_revealed,
            newly_revealed,
            config,
        )

        contents = []
        for msg in req.history:
            role = "user" if msg.role == "Student" else "model"
            contents.append({"role": role, "parts": [{"text": msg.content}]})

        contents.append({"role": "user", "parts": [{"text": req.studentMessage}]})

        response = _get_client().models.generate_content(
            model=MODEL,
            contents=contents,
            config={
                "system_instruction": system_prompt,
                "temperature": 0.65,
                "max_output_tokens": 350,
            }
        )

        reply = apply_consistency_guard(
            response.text.strip(),
            req,
            question_type,
            allowed_requirements,
            newly_revealed,
            config,
        )
        state_update = build_state_update(req, state, question_type, newly_revealed, config)

        return ChatResponse(
            stakeholderReply=reply,
            detectedQuestionType=question_type,
            stateUpdate=state_update,
        )

    except Exception as e:
        try:
            question_type = detect_question_type(req.studentMessage)
            state = load_persona_state(req)
            config = get_scenario_config(req.scenarioTitle, req.availableRequirements)
            allowed_requirements, previously_revealed, newly_revealed = select_gated_requirements(
                req,
                state,
                question_type,
                config,
            )
            state_update = build_state_update(req, state, question_type, newly_revealed, config)
            return ChatResponse(
                stakeholderReply=build_fallback_reply(
                    req,
                    question_type,
                    allowed_requirements,
                    newly_revealed,
                ),
                detectedQuestionType=question_type,
                stateUpdate=state_update,
            )
        except Exception as fallback_error:
            raise HTTPException(
                status_code=500,
                detail=f"AI chat error: {str(e)}; fallback error: {str(fallback_error)}",
            ) from fallback_error


def build_fallback_reply(
    req: ChatRequest,
    question_type: str | None,
    allowed_requirements: list[str],
    newly_revealed: list[str],
) -> str:
    if newly_revealed:
        return (
            "From a business perspective, the important point is this: "
            f"{newly_revealed[0]} I can elaborate if you want to focus on that area."
        )

    if is_overly_technical(req.studentMessage):
        return (
            "I would rather not go into implementation details. "
            "At this stage, I can only describe the business need and operating constraints."
        )

    if allowed_requirements:
        return (
            "At a high level, that topic is related to what we already discussed: "
            f"{allowed_requirements[0]}"
        )

    if question_type == "OpenEnded":
        return (
            "The main goal is to make the process clearer and easier for the people using it, "
            "but I need more specific questions before I can share more details."
        )

    return "I do not have enough detail to answer that yet. Could you ask about a specific business rule or user need?"
