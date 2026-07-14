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

from app.models.schemas import ExtractRequest, ExtractResponse, ExtractedReq

router = APIRouter()
logger = logging.getLogger(__name__)
_client: genai.Client | None = None
_client_lock = threading.Lock()
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
)


@router.post("/extract", response_model=ExtractResponse)
async def extract_requirements(req: ExtractRequest):
    try:
        conversation_text = _format_conversation(req.history)
        requirements = []
        max_retries = 3

        for attempt in range(max_retries):
            try:
                response = _get_client().models.generate_content(
                    model=MODEL,
                    contents=f"{EXTRACT_PROMPT}\n\n--- CONVERSATION ---\n{conversation_text}",
                    config={"temperature": 0.2, "max_output_tokens": 2000},
                )
                requirements = _parse_extraction_json(getattr(response, "text", "") or "")
                break
            except Exception as e:
                if attempt == max_retries - 1:
                    logger.warning(
                        f"Gemini extraction failed after {max_retries} attempts: {e}. "
                        "Falling back to regex parser."
                    )
                    requirements = _fallback_extract_requirements(req.history)
                else:
                    await asyncio.sleep(1 * (attempt + 1))

        return ExtractResponse(requirements=requirements)
    except Exception as e:
        logger.exception("Requirement extraction error.")
        raise HTTPException(status_code=500, detail="An error occurred during requirement extraction.")


def _get_client() -> genai.Client:
    global _client
    if _client is None:
        with _client_lock:
            if _client is None:
                api_key = os.getenv("GEMINI_API_KEY")
                if not api_key:
                    raise RuntimeError("GEMINI_API_KEY is not configured.")
                _client = genai.Client(api_key=api_key)
    return _client


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
    return re.sub(r"[^a-z0-9]+", " ", text.lower()).strip()


def _to_requirement_text(sentence: str, role: str) -> str:
    clean = sentence.strip()
    if role == "Student" and clean.endswith("?"):
        clean = clean[:-1].strip()
        return f"The system should address: {clean}."
    return clean
