"""AI-assisted learning feedback with a strict no-ground-truth-text boundary."""
import json
import logging
import re
import unicodedata

from google.genai import types
from pydantic import BaseModel

from app.services.api_client_manager import client_manager
from app.services.evaluation_policy import _unique_feedback

logger = logging.getLogger(__name__)


class SafeLearningFeedback(BaseModel):
    strengths: list[str]
    weaknesses: list[str]
    suggestions: list[str]


def _comparison_text(value: str) -> str:
    normalized = unicodedata.normalize("NFKD", value.casefold()).replace("đ", "d")
    normalized = "".join(char for char in normalized if not unicodedata.combining(char))
    return " ".join(re.sub(r"[^a-z0-9]+", " ", normalized).split())


def _contains_hidden_requirement_wording(
    feedback: SafeLearningFeedback,
    hidden_requirements: list,
) -> bool:
    """Block direct rephrasing of a hidden requirement even when the provider ignores its prompt."""
    rendered = _comparison_text(" ".join(
        [*feedback.strengths, *feedback.weaknesses, *feedback.suggestions]
    ))
    for requirement in hidden_requirements:
        hidden = _comparison_text(str(getattr(requirement, "text", "")))
        tokens = hidden.split()
        if len(tokens) < 3:
            continue
        if hidden in rendered:
            return True
        for index in range(len(tokens) - 2):
            if " ".join(tokens[index:index + 3]) in rendered:
                return True
    return False


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
- Never mention a requirement ID, UUID, or English category label in the student-facing text.
- Refer to learning areas in Vietnamese: Functional = chức năng/kết quả hệ thống; NonFunctional = chất lượng/điều kiện vận hành; BusinessRule = quy tắc/điều kiện/ngoại lệ nghiệp vụ.
- Give actionable next-question strategies, not an answer. Do not repeat an idea across lists.
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
        if _contains_hidden_requirement_wording(parsed, hidden_requirements):
            logger.warning("AI learning feedback attempted to repeat hidden requirement wording; using fallback.")
            return fallback
        return (
            _unique_feedback(parsed.strengths),
            _unique_feedback(parsed.weaknesses),
            _unique_feedback(parsed.suggestions),
        )
    except Exception:
        logger.warning("Safe AI learning feedback failed; using deterministic fallback.")
        return fallback
