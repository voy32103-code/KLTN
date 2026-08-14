"""
Chat service for persona-driven stakeholder replies with scenario-config gating.
"""
import os
import threading
import logging

from fastapi import APIRouter, HTTPException
from google import genai

logger = logging.getLogger(__name__)

from app.models.schemas import ChatRequest, ChatResponse
from app.services.consistency_checker import (
    check_response_consistency,
    normalize_reply_after_consistency_check,
)
from app.services.gating_service import (
    build_state_update,
    classify_question_quality,
    detect_question_type,
    detect_topic,
    disclosure_view,
    is_overly_technical,
    load_persona_state,
    select_gated_requirements,
)
from app.services.scenario_config_service import ScenarioConfig, get_scenario_config

router = APIRouter()

from app.services.api_client_manager import client_manager
MODEL = os.getenv("MODEL_NAME", "gemini-2.5-flash")


def build_system_prompt(
    req: ChatRequest,
    state: dict[str, object],
    question_type: str | None,
    allowed_requirements: list[str],
    previously_revealed: list[str],
    newly_revealed: list[str],
    config: ScenarioConfig | None,
    question_quality: str = "vague",
    detected_topic: str | None = None,
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
CRITICAL: You must ALWAYS respond in Vietnamese. Even if the student uses some English terms, respond naturally in Vietnamese in accordance with your persona traits.

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
- If patience is low, give shorter answers, but always use complete grammatical sentences.
- If mood is rushed or irritated, sound busy and less detailed without being rude, dismissive, or sarcastic.
- Never punish the student for asking a clarifying question; answer the business question first.
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
Detected topic: {detected_topic or "Unknown"}
Question quality: {question_quality}
Overly technical question: {technical_question}

Response guidelines:
- Respond naturally as a real person would.
- Keep responses to 1-4 sentences unless clarification truly needs more detail.
- Prioritize business language over system design language.
- Every response must contain at least one complete Vietnamese sentence and must not end as a fragment.
- Do not output internal labels, JSON, markdown headings, or analysis such as "Topic", "Quality", "OpenEnded", or "Probing".
- Do not comment on the student's manner, workload, or question quality; express urgency only through concise professional wording.
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
        
        config = None
        if req.scenarioConfig:
            try:
                from app.services.scenario_config_service import parse_config_from_dict
                config = parse_config_from_dict(req.scenarioConfig)
            except Exception as ex_parse:
                logger.error("Failed to parse scenarioConfig from request: %s", str(ex_parse))
        if config is None:
            config = get_scenario_config(req.scenarioTitle, req.availableRequirements)

        question_quality = classify_question_quality(req.studentMessage, config)
        detected_topic = detect_topic(req.studentMessage, config)

        allowed_requirements, previously_revealed, newly_revealed = select_gated_requirements(
            req,
            state,
            question_type,
            config,
        )
        displayed_allowed = [disclosure_view(item, question_quality, config) for item in allowed_requirements]
        displayed_new = [disclosure_view(item, question_quality, config) for item in newly_revealed]
        system_prompt = build_system_prompt(
            req,
            state,
            question_type,
            displayed_allowed,
            previously_revealed,
            displayed_new,
            config,
            question_quality,
            detected_topic,
        )

        contents = []
        for msg in req.history:
            role = "user" if msg.role == "Student" else "model"
            contents.append({"role": role, "parts": [{"text": msg.content}]})

        contents.append({"role": "user", "parts": [{"text": req.studentMessage}]})

        selected_model = req.selectedModel or MODEL
        response = await client_manager.generate_content(
            model=selected_model,
            contents=contents,
            system_instruction=system_prompt,
            temperature=0.65,
            max_output_tokens=350,
        )

        reply = apply_consistency_guard(
            response.text.strip(),
            req,
            question_type,
            allowed_requirements,
            displayed_new,
            config,
        )
        state_update = build_state_update(req, state, question_type, newly_revealed, config)

        return ChatResponse(
            stakeholderReply=reply,
            detectedQuestionType=question_type,
            detectedTopic=detected_topic,
            questionQuality=question_quality,
            stateUpdate=state_update,
        )

    except Exception as e:
        logger.warning("Primary AI chat failed, attempting fallback. Error: %s", str(e), exc_info=True)
        try:
            question_type = detect_question_type(req.studentMessage)
            state = load_persona_state(req)
            
            config = None
            if req.scenarioConfig:
                try:
                    from app.services.scenario_config_service import parse_config_from_dict
                    config = parse_config_from_dict(req.scenarioConfig)
                except Exception as ex_parse:
                    logger.error("Failed to parse scenarioConfig from request fallback: %s", str(ex_parse))
            if config is None:
                config = get_scenario_config(req.scenarioTitle, req.availableRequirements)

            question_quality = classify_question_quality(req.studentMessage, config)
            detected_topic = detect_topic(req.studentMessage, config)

            allowed_requirements, previously_revealed, newly_revealed = select_gated_requirements(
                req,
                state,
                question_type,
                config,
            )
            displayed_allowed = [disclosure_view(item, question_quality, config) for item in allowed_requirements]
            displayed_new = [disclosure_view(item, question_quality, config) for item in newly_revealed]
            state_update = build_state_update(req, state, question_type, newly_revealed, config)
            return ChatResponse(
                stakeholderReply=build_fallback_reply(
                    req,
                    question_type,
                    displayed_allowed,
                    displayed_new,
                ),
                detectedQuestionType=question_type,
                detectedTopic=detected_topic,
                questionQuality=question_quality,
                stateUpdate=state_update,
                isFallback=True,
            )
        except Exception as fallback_error:
            logger.exception("AI chat error and fallback failed.")
            raise HTTPException(
                status_code=500,
                detail="An error occurred during chat processing and fallback generation.",
            ) from fallback_error


def build_fallback_reply(
    req: ChatRequest,
    question_type: str | None,
    allowed_requirements: list[str],
    newly_revealed: list[str],
) -> str:
    if newly_revealed:
        return (
            "Từ góc độ nghiệp vụ, điểm quan trọng cần lưu ý là: "
            f"{newly_revealed[0]} Tôi có thể làm rõ thêm nếu bạn muốn tập trung vào phần này."
        )

    if is_overly_technical(req.studentMessage):
        return (
            "Tôi không muốn đi sâu vào các chi tiết triển khai kỹ thuật. "
            "Ở giai đoạn này, tôi chỉ có thể mô tả các nhu cầu nghiệp vụ và ràng buộc vận hành."
        )

    if allowed_requirements:
        return (
            "Nhìn chung, chủ đề đó có liên quan đến nội dung chúng ta đã thảo luận trước đây: "
            f"{allowed_requirements[0]}"
        )

    if question_type == "OpenEnded":
        return (
            "Mục tiêu chính là làm cho quy trình trở nên rõ ràng và dễ dàng hơn cho những người sử dụng, "
            "nhưng tôi cần các câu hỏi cụ thể hơn trước khi có thể chia sẻ thêm chi tiết."
        )

    return "Tôi chưa có đủ thông tin chi tiết để trả lời câu hỏi đó. Bạn có thể hỏi về một quy tắc nghiệp vụ hoặc nhu cầu người dùng cụ thể không?"
