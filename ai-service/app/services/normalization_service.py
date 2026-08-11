"""Deterministic requirement normalization used before matching and coverage.

The service deliberately uses a small, reviewable synonym dictionary instead of an
LLM.  This makes duplicate removal reproducible for a scenario and keeps the raw
extraction available for lecturer review.
"""
from __future__ import annotations

import re
import unicodedata
from collections.abc import Iterable

from app.models.schemas import NormalizedRequirement, StructuredRequirement


_ALIASES: dict[str, dict[str, str]] = {
    "actor": {
        "student": "student",
        "sinh vien": "student",
        "learner": "student",
        "customer": "customer",
        "khach hang": "customer",
        "client": "customer",
        "user": "user",
        "nguoi dung": "user",
        "system": "system",
        "he thong": "system",
        "administrator": "administrator",
        "admin": "administrator",
        "quan tri vien": "administrator",
    },
    "action": {
        "register": "register",
        "dang ky": "register",
        "enroll": "register",
        "cancel": "cancel",
        "delete": "cancel",
        "remove": "cancel",
        "huy": "cancel",
        "check": "check",
        "validate": "check",
        "kiem tra": "check",
        "block": "block",
        "chan": "block",
        "hold": "block",
        "approve": "approve",
        "phe duyet": "approve",
        "provide": "provide",
        "support": "provide",
        "ho tro": "provide",
    },
    "object": {
        "course registration": "course registration",
        "dang ky hoc phan": "course registration",
        "registration": "course registration",
        "course": "course",
        "hoc phan": "course",
        "wait list": "wait list",
        "waitlist": "wait list",
        "danh sach cho": "wait list",
        "tuition": "tuition",
        "fee": "tuition",
        "fees": "tuition",
        "hoc phi": "tuition",
        "financial system": "financial system",
        "he thong tai chinh": "financial system",
    },
}


def _comparison_text(value: str | None) -> str:
    if not value:
        return ""
    normalized = unicodedata.normalize("NFKD", value.casefold())
    # ``đ``/``Đ`` are letters, not combining variants, so NFKD does not turn
    # them into ``d``. Handle them before removing diacritics for Vietnamese
    # aliases such as "đăng ký" and "sinh viên".
    normalized = normalized.replace("đ", "d")
    normalized = "".join(char for char in normalized if not unicodedata.combining(char))
    normalized = re.sub(r"[^a-z0-9]+", " ", normalized)
    return " ".join(normalized.split())


def _scenario_aliases(
    glossary: dict[str, dict[str, str]] | None,
    component: str,
) -> dict[str, str]:
    """Return only reviewable, well-formed aliases supplied for this scenario."""
    raw = (glossary or {}).get(component, {})
    if not isinstance(raw, dict):
        return {}
    aliases: dict[str, str] = {}
    for source, target in raw.items():
        if not isinstance(source, str) or not isinstance(target, str):
            continue
        normalized_source = _comparison_text(source)
        normalized_target = _comparison_text(target)
        if normalized_source and normalized_target:
            aliases[normalized_source] = normalized_target
    return aliases


def normalize_component(
    value: str | None,
    component: str,
    glossary: dict[str, dict[str, str]] | None = None,
) -> str | None:
    """Return a canonical component value, or ``None`` for an empty condition."""
    clean = _comparison_text(value)
    if not clean:
        return None
    return _scenario_aliases(glossary, component).get(
        clean,
        _ALIASES.get(component, {}).get(clean, clean),
    )


def canonical_text(requirement: NormalizedRequirement) -> str:
    text = (
        f"{requirement.actorNormalized} must {requirement.actionNormalized} "
        f"{requirement.objectNormalized}"
    )
    return f"{text} when {requirement.conditionNormalized}." if requirement.conditionNormalized else f"{text}."


def normalize_requirement(
    requirement: StructuredRequirement,
    glossary: dict[str, dict[str, str]] | None = None,
) -> NormalizedRequirement:
    actor = normalize_component(requirement.actor, "actor", glossary)
    action = normalize_component(requirement.action, "action", glossary)
    object_ = normalize_component(requirement.object, "object", glossary)
    condition = normalize_component(requirement.condition, "condition", glossary)
    if not actor or not action or not object_:
        raise ValueError("A structured requirement needs actor, action, and object after normalization.")

    key = "|".join([actor, action, object_, condition or ""])
    normalized = NormalizedRequirement(
        id=requirement.id,
        actorNormalized=actor,
        actionNormalized=action,
        objectNormalized=object_,
        conditionNormalized=condition,
        type=requirement.type,
        priority=requirement.priority,
        confidence=requirement.confidence,
        canonicalKey=key,
        canonicalText="",
        original=requirement,
    )
    return normalized.model_copy(update={"canonicalText": canonical_text(normalized)})


def normalize_and_deduplicate(
    requirements: Iterable[StructuredRequirement],
    glossary: dict[str, dict[str, str]] | None = None,
) -> list[NormalizedRequirement]:
    """Normalize requirements and retain the highest-confidence item per key."""
    by_key: dict[str, NormalizedRequirement] = {}
    for requirement in requirements:
        normalized = normalize_requirement(requirement, glossary)
        existing = by_key.get(normalized.canonicalKey)
        if existing is None or normalized.confidence > existing.confidence:
            by_key[normalized.canonicalKey] = normalized
    return list(by_key.values())
