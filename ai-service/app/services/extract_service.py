"""
Requirement extraction service.

The primary path asks Gemini for structured JSON. If the model/API returns
invalid output, the endpoint falls back to a conservative local extractor so
ending a session never fails just because extraction was flaky.
"""
import json
import os
import re
import logging
import asyncio
import threading
from collections.abc import Iterable

from fastapi import APIRouter, HTTPException
from google import genai

from app.models.schemas import (
    ExtractRequest,
    ExtractResponse,
    ExtractedReq,
    StructuredRequirement,
)
from app.prompts.structured_extraction_prompt import build_extraction_prompt
from app.services.normalization_service import normalize_and_deduplicate
from app.utils.retry_handler import JSONParsingError, parse_json_with_retry

router = APIRouter()
logger = logging.getLogger(__name__)
from app.services.api_client_manager import client_manager
MODEL = os.getenv("MODEL_NAME", "gemini-2.5-flash")

# ... (EXTRACT_PROMPT and REQUIREMENT_CUES remain unchanged, but let's provide them so the replacement matches exactly)
EXTRACT_PROMPT = """Analyze the following conversation between a student analyst and a stakeholder.
Extract ALL software requirements mentioned or implied in the conversation.

For each requirement, provide:
- text: clear requirement statement
- confidence: 0.0 to 1.0 (how clearly it was stated)

Return ONLY valid JSON array, no markdown, no explanation:
[{"text": "requirement description", "confidence": 0.85}, ...]

Focus on:
- Functional requirements (what the system should do)
- Non-functional requirements (performance, security, usability)
- Business rules (policies, constraints)
- Data requirements (what data needs to be stored/processed)

Do NOT include:
- Implementation details
- Assumptions not discussed in the conversation
- Requirements that were explicitly rejected
"""

REQUIREMENT_CUES = (
    "must",
    "should",
    "need",
    "needs",
    "able to",
    "allow",
    "support",
    "require",
    "integrate",
    "alert",
    "report",
    "sync",
    "offline",
    # Tiếng Việt cues
    "phải",
    "cần",
    "yêu cầu",
    "cho phép",
    "hỗ trợ",
    "tích hợp",
    "cảnh báo",
    "báo cáo",
    "đồng bộ",
    "ngoại tuyến",
    "bảo mật",
)


@router.post("/extract", response_model=ExtractResponse)
async def extract_requirements(req: ExtractRequest):
    try:
        conversation_text = _format_conversation(req.history)
        requirements: list[ExtractedReq] = []
        structured_requirements: list[StructuredRequirement] = []
        normalized_requirements = []
        used_fallback = False
        max_retries = 3

        for attempt in range(max_retries):
            try:
                selected_model = req.selectedModel or MODEL
                response = await client_manager.generate_content(
                    model=selected_model,
                    contents=build_extraction_prompt([
                        {"role": message.role, "content": message.content}
                        for message in req.history
                    ]),
                    temperature=0.2,
                    max_output_tokens=2000,
                )
                structured_requirements = await _parse_structured_extraction_json(
                    getattr(response, "text", "") or ""
                )
                normalized_requirements = normalize_and_deduplicate(
                    structured_requirements,
                    req.normalizationGlossary,
                )
                requirements = [
                    ExtractedReq(
                        text=requirement.canonicalText,
                        confidence=requirement.confidence,
                        actor=requirement.actorNormalized,
                        action=requirement.actionNormalized,
                        object=requirement.objectNormalized,
                        condition=requirement.conditionNormalized,
                        type=requirement.type,
                        priority=requirement.priority,
                    )
                    for requirement in normalized_requirements
                ]
                break
            except Exception as e:
                if attempt == max_retries - 1:
                    logger.warning(
                        f"Gemini extraction failed after {max_retries} attempts: {e}. "
                        "Falling back to regex parser."
                    )
                    requirements = _fallback_extract_requirements(req.history)
                    used_fallback = True
                else:
                    await asyncio.sleep(1 * (attempt + 1))

        return ExtractResponse(
            requirements=requirements,
            isFallback=used_fallback,
            structuredRequirements=structured_requirements,
            normalizedRequirements=normalized_requirements,
        )
    except Exception as e:
        logger.exception("Requirement extraction error.")
        raise HTTPException(status_code=500, detail="An error occurred during requirement extraction.")


# _get_client removed


def _format_conversation(history: Iterable) -> str:
    return "\n".join(
        f"{'Student' if m.role == 'Student' else 'Stakeholder'}: {m.content}"
        for m in history
    )


def _parse_extraction_json(raw_text: str) -> list[ExtractedReq]:
    text = raw_text.strip()
    if text.startswith("```"):
        text = text.split("\n", 1)[1].rsplit("```", 1)[0].strip()

    parsed = json.loads(text)
    if not isinstance(parsed, list):
        raise ValueError("Extraction response must be a JSON array")

    requirements: list[ExtractedReq] = []
    for item in parsed:
        if not isinstance(item, dict):
            raise ValueError("Each extracted requirement must be an object")
        requirements.append(ExtractedReq(**item))

    return requirements


async def _parse_structured_extraction_json(raw_text: str) -> list[StructuredRequirement]:
    """Parse and validate the structured contract returned by the extraction model."""
    try:
        parsed = await parse_json_with_retry(raw_text)
    except JSONParsingError as exc:
        raise ValueError("Extraction response was not valid JSON.") from exc

    if not isinstance(parsed, list):
        raise ValueError("Extraction response must be a JSON array.")

    return [StructuredRequirement.model_validate(item) for item in parsed]


def _fallback_extract_requirements(history: Iterable) -> list[ExtractedReq]:
    candidates: list[ExtractedReq] = []
    seen: set[str] = set()

    for message in history:
        role = getattr(message, "role", "")
        content = getattr(message, "content", "")
        for sentence in _split_sentences(content):
            normalized = _normalize_candidate(sentence)
            if not normalized or normalized in seen:
                continue

            lower = normalized.lower()
            has_requirement_cue = any(cue in lower for cue in REQUIREMENT_CUES)
            is_student_question = role == "Student" and sentence.strip().endswith("?")

            if has_requirement_cue or is_student_question:
                confidence = 0.55 if has_requirement_cue else 0.35
                candidates.append(ExtractedReq(text=_to_requirement_text(sentence, role), confidence=confidence))
                seen.add(normalized)

            if len(candidates) >= 12:
                return candidates

    return candidates


def _split_sentences(text: str) -> list[str]:
    normalized = re.sub(r"\s+", " ", text).strip()
    if not normalized:
        return []
    return [part.strip() for part in re.split(r"(?<=[.!?])\s+", normalized) if part.strip()]


def _normalize_candidate(text: str) -> str:
    # Giữ lại các ký tự chữ cái Unicode (bao gồm tiếng Việt có dấu) và chữ số
    return re.sub(r"[^\w0-9]+", " ", text.lower()).strip()


def _to_requirement_text(sentence: str, role: str) -> str:
    clean = sentence.strip()
    if role == "Student" and clean.endswith("?"):
        clean = clean[:-1].strip()
        return f"The system should address: {clean}."
    return clean
