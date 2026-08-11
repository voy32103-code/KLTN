'''Actor-Action-Object-Condition matching with documented weights.'''
from dataclasses import dataclass
from difflib import SequenceMatcher
from typing import Any

from app.services.normalization_service import normalize_component

MATCH_THRESHOLD = 0.80
PARTIAL_THRESHOLD = 0.60
WEIGHTS = {'actor': .20, 'action': .30, 'object': .30, 'condition': .20}


@dataclass(frozen=True)
class AaocPairScore:
    extracted_index: int
    hidden_index: int
    score: float
    component_scores: dict[str, float]


def _field(item: Any, name: str) -> str | None:
    return getattr(item, name, None)


def _norm(
    item: Any,
    name: str,
    glossary: dict[str, dict[str, str]] | None = None,
) -> str | None:
    return normalize_component(_field(item, name), name, glossary)


def has_aaoc(item: Any) -> bool:
    return bool(_field(item, 'type') and _field(item, 'action') and _field(item, 'object'))


def _condition_score(
    left: str | None,
    right: str | None,
    glossary: dict[str, dict[str, str]] | None = None,
) -> float:
    left = normalize_component(left, 'condition', glossary)
    right = normalize_component(right, 'condition', glossary)
    if not left and not right:
        return 1.0
    if not left or not right:
        return 0.0
    if left == right:
        return 1.0
    left_tokens, right_tokens = set(left.split()), set(right.split())
    union = left_tokens | right_tokens
    jaccard = len(left_tokens & right_tokens) / len(union) if union else 0.0
    return round(max(jaccard, SequenceMatcher(None, left, right).ratio()), 4)


def score_pair(
    extracted: Any,
    hidden: Any,
    glossary: dict[str, dict[str, str]] | None = None,
) -> AaocPairScore | None:
    extracted_type = str(_field(extracted, 'type') or '').upper()
    hidden_type = str(_field(hidden, 'type') or '').upper()
    if not extracted_type or extracted_type != hidden_type:
        return None
    if not _norm(extracted, 'action', glossary) or _norm(extracted, 'action', glossary) != _norm(hidden, 'action', glossary):
        return None
    if not _norm(extracted, 'object', glossary) or _norm(extracted, 'object', glossary) != _norm(hidden, 'object', glossary):
        return None
    components = {
        'actor': float(bool(_norm(extracted, 'actor', glossary)) and _norm(extracted, 'actor', glossary) == _norm(hidden, 'actor', glossary)),
        'action': 1.0,
        'object': 1.0,
        'condition': _condition_score(_field(extracted, 'condition'), _field(hidden, 'condition'), glossary),
    }
    score = sum(components[name] * weight for name, weight in WEIGHTS.items())
    return AaocPairScore(-1, -1, round(score, 4), components)


def assign_weighted_one_to_one(
    extracted: list[Any],
    hidden: list[Any],
    glossary: dict[str, dict[str, str]] | None = None,
) -> dict[int, AaocPairScore]:
    candidates = []
    for extracted_index, extracted_item in enumerate(extracted):
        for hidden_index, hidden_item in enumerate(hidden):
            pair = score_pair(extracted_item, hidden_item, glossary)
            if pair and pair.score >= PARTIAL_THRESHOLD:
                candidates.append(AaocPairScore(extracted_index, hidden_index, pair.score, pair.component_scores))
    candidates.sort(key=lambda item: (-item.score, item.extracted_index, item.hidden_index))
    used_extracted, used_hidden, assignments = set(), set(), {}
    for candidate in candidates:
        if candidate.extracted_index in used_extracted or candidate.hidden_index in used_hidden:
            continue
        assignments[candidate.hidden_index] = candidate
        used_extracted.add(candidate.extracted_index)
        used_hidden.add(candidate.hidden_index)
    return assignments


def classify_aaoc(score: float) -> str:
    return 'exact' if score >= MATCH_THRESHOLD else 'partial' if score >= PARTIAL_THRESHOLD else 'missed'


def explain_aaoc(components: dict[str, float], score: float, match_type: str) -> str:
    detail = ', '.join(f'{name}={value:.0%}' for name, value in components.items())
    suffix = 'vượt ngưỡng Match 80%' if match_type == 'exact' else 'đúng Action/Object nhưng thiếu ngữ cảnh'
    return f'Khớp AAOC đạt {score:.0%} ({detail}); {suffix}.'
