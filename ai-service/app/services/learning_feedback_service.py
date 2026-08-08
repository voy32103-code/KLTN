"""AI-assisted learning feedback with a strict no-ground-truth-text boundary."""
import json
import logging

from google.genai import types
from pydantic import BaseModel

from app.services.api_client_manager import client_manager

logger = logging.getLogger(__name__)


class SafeLearningFeedback(BaseModel):
    strengths: list[str]
    weaknesses: list[str]
    suggestions: list[str]


async def generate_learning_feedback(
    matches: list,
    hidden_requirements: list,
    selected_model: str | None,
    variant: str,
    fallback: tuple[list[str], list[str], list[str]],
) -> tuple[list[str], list[str], list[str]]:
    if variant != "B" or not selected_model:
        return fallback
    categories = {item.id: item.category for item in hidden_requirements}
    safe_summary = [
        {
            "requirementId": match.hiddenId,
            "category": categories.get(match.hiddenId, "Unknown"),
            "matchType": match.matchType,
            "score": match.score,
            "componentScores": match.componentScores,
        }
        for match in matches
    ]
    prompt = f"""You are a learning coach for requirements elicitation.
Create concise Vietnamese feedback from this evaluation metadata:
{json.dumps(safe_summary, ensure_ascii=False)}

Rules:
- Never reconstruct, guess, quote, or reveal a hidden requirement.
- Refer only to requirement ID, category, question strategy, and AAOC components.
- Give actionable next-question strategies, not an answer.
- Return strengths, weaknesses, suggestions in the requested JSON schema.
"""
    try:
        response = await client_manager.generate_content(
            model=selected_model,
            contents=prompt,
            config=types.GenerateContentConfig(
                response_mime_type="application/json",
                response_schema=SafeLearningFeedback,
                temperature=0.2,
                max_output_tokens=800,
            ),
        )
        parsed = SafeLearningFeedback.model_validate_json(response.text)
        return parsed.strengths, parsed.weaknesses, parsed.suggestions
    except Exception:
        logger.warning("Safe AI learning feedback failed; using deterministic fallback.")
        return fallback
