"""
Pure evaluation policy helpers.

This module centralizes threshold classification, coverage calculation, and
reason/feedback generation so they can be tested without loading embedding
models or external services.
"""
from __future__ import annotations

import os
import re
from collections.abc import Mapping
from typing import Any


THRESHOLD_PRESETS = {
    "current": {"exact": 0.92, "semantic": 0.75, "partial": 0.55},
    "strict": {"exact": 0.95, "semantic": 0.82, "partial": 0.65},
    "lenient": {"exact": 0.88, "semantic": 0.68, "partial": 0.45},
    "very_lenient": {"exact": 0.84, "semantic": 0.62, "partial": 0.38},
}

STOPWORDS = {
    "a",
    "an",
    "and",
    "are",
    "as",
    "at",
    "be",
    "before",
    "by",
    "for",
    "from",
    "in",
    "is",
    "it",
    "must",
    "of",
    "on",
    "or",
    "should",
    "system",
    "the",
    "to",
    "with",
}

SYNONYM_GROUPS = (
    {"accessibility", "accessible", "wcag"},
    {"advisor", "approval", "approve"},
    {"eligibility", "eligible", "prerequisite", "prerequisites"},
    {"fee", "fees", "financial", "payment", "tuition"},
    {"offline", "outage", "outages", "sync"},
    {"stock", "inventory", "product", "products"},
    {"waitlist", "wait-list", "waiting"},
)


def _read_threshold(source: Mapping[str, str], name: str, default: float) -> float:
    raw = source.get(name)
    if raw is None:
        return default
    try:
        value = float(raw)
    except ValueError:
        return default
    return value if 0 <= value <= 1 else default


def _read_bool(source: Mapping[str, str], name: str, default: bool) -> bool:
    raw = source.get(name)
    if raw is None:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


def resolve_thresholds(source: Mapping[str, str] | None = None) -> dict[str, float | str]:
    env = source if source is not None else os.environ
    requested_preset = env.get("MATCH_THRESHOLD_PRESET", "current").strip().lower()
    preset_name = requested_preset if requested_preset in THRESHOLD_PRESETS else "current"
    defaults = THRESHOLD_PRESETS[preset_name]

    return {
        "preset": preset_name,
        "exact": _read_threshold(env, "MATCH_EXACT_THRESHOLD", defaults["exact"]),
        "semantic": _read_threshold(env, "MATCH_SEMANTIC_THRESHOLD", defaults["semantic"]),
        "partial": _read_threshold(env, "MATCH_PARTIAL_THRESHOLD", defaults["partial"]),
    }


ACTIVE_THRESHOLDS = resolve_thresholds()
MATCH_THRESHOLD_PRESET = str(ACTIVE_THRESHOLDS["preset"])
EXACT_THRESHOLD = float(ACTIVE_THRESHOLDS["exact"])
SEMANTIC_THRESHOLD = float(ACTIVE_THRESHOLDS["semantic"])
PARTIAL_THRESHOLD = float(ACTIVE_THRESHOLDS["partial"])
ENABLE_RUBRIC_PARTIAL_MATCHER = _read_bool(os.environ, "ENABLE_RUBRIC_PARTIAL_MATCHER", False)


def build_scoring_policy_metadata(
    embedding_model: str,
    source: Mapping[str, str] | None = None,
) -> dict[str, float | str | bool]:
    if source is None:
        thresholds = ACTIVE_THRESHOLDS
        rubric_partial_matcher = ENABLE_RUBRIC_PARTIAL_MATCHER
    else:
        thresholds = resolve_thresholds(source)
        rubric_partial_matcher = _read_bool(source, "ENABLE_RUBRIC_PARTIAL_MATCHER", False)

    return {
        "preset": str(thresholds["preset"]),
        "exactThreshold": float(thresholds["exact"]),
        "semanticThreshold": float(thresholds["semantic"]),
        "partialThreshold": float(thresholds["partial"]),
        "rubricPartialMatcher": rubric_partial_matcher,
        "embeddingModel": embedding_model,
    }


def classify_match(score: float, hidden_text: str | None = None, extracted_text: str | None = None) -> str:
    if score >= EXACT_THRESHOLD:
        return "exact"
    if score >= SEMANTIC_THRESHOLD:
        return "semantic"
    if score >= PARTIAL_THRESHOLD:
        return "partial"
    if ENABLE_RUBRIC_PARTIAL_MATCHER and rubric_partial_match(hidden_text, extracted_text, score):
        return "partial"
    return "missed"


def rubric_partial_match(
    hidden_text: str | None,
    extracted_text: str | None,
    score: float,
    partial_threshold: float = PARTIAL_THRESHOLD,
) -> bool:
    if not hidden_text or not extracted_text:
        return False
    if score < max(0.0, partial_threshold - 0.12):
        return False

    hidden_terms = _semantic_terms(hidden_text)
    extracted_terms = _semantic_terms(extracted_text)
    if not hidden_terms or not extracted_terms:
        return False

    overlap = hidden_terms & extracted_terms
    overlap_ratio = len(overlap) / min(len(hidden_terms), len(extracted_terms))
    return len(overlap) >= 2 and overlap_ratio >= 0.35


def _semantic_terms(text: str) -> set[str]:
    tokens = {
        token
        for token in re.findall(r"[a-z0-9-]+", text.lower())
        if len(token) > 2 and token not in STOPWORDS
    }

    expanded = set(tokens)
    for group in SYNONYM_GROUPS:
        if tokens & group:
            expanded.add(next(iter(group)))
    return expanded


def explain_match(hidden_text: str, extracted_text: str | None, score: float, match_type: str) -> str:
    if match_type == "exact":
        return f"The extracted requirement closely matches the hidden requirement (similarity {score:.0%})."
    if match_type == "semantic":
        return f"The wording differs, but the extracted requirement preserves the core meaning (similarity {score:.0%})."
    if match_type == "partial":
        return f"The extracted requirement is related, but it misses an important actor, condition, constraint, or specificity from the hidden requirement (similarity {score:.0%})."
    if extracted_text:
        return f"No extracted requirement reached the partial threshold for this hidden requirement; the closest candidate scored {score:.0%}."
    return "No extracted requirement was available to match this hidden requirement."


def calculate_coverage(matches: list[Any]) -> tuple[float, int, int, int]:
    total = len(matches)
    matched = sum(1 for m in matches if m.matchType in ("exact", "semantic"))
    partial = sum(1 for m in matches if m.matchType == "partial")
    missed = sum(1 for m in matches if m.matchType == "missed")
    coverage = ((matched + 0.5 * partial) / total * 100) if total > 0 else 0.0
    return round(coverage, 2), matched, partial, missed


def generate_feedback(matches: list[Any], hidden_reqs: list[Any]) -> tuple[list[str], list[str], list[str]]:
    strengths: list[str] = []
    weaknesses: list[str] = []
    suggestions: list[str] = []

    req_map = {r.id: r for r in hidden_reqs}

    for match in matches:
        hidden = req_map.get(match.hiddenId)
        if hidden is None:
            continue

        if match.matchType in ("exact", "semantic"):
            strengths.append(
                f"Successfully identified: {hidden.text} (similarity: {match.score:.0%})"
            )
        elif match.matchType == "partial":
            weaknesses.append(
                f"Partially identified: {hidden.text} — Your understanding was incomplete"
            )
            suggestions.append(
                f"Ask more detailed questions about: {hidden.category} requirements"
            )
        else:
            weaknesses.append(f"Missed requirement: {hidden.category} area")
            suggestions.append(
                f"Consider asking about {hidden.category.lower()} aspects of the system"
            )

    if not strengths:
        strengths.append("Keep practicing — try asking more open-ended questions first")

    return strengths, weaknesses, suggestions
