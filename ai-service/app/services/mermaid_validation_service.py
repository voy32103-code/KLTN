"""Bounded Mermaid validation and deterministic repair for generated diagrams."""
from __future__ import annotations

import re

from app.models.schemas import DesignSuggestionsData

FORBIDDEN = ("%%{", "<script", "javascript:", "click ")


def validate_mermaid(source: str, expected: str) -> list[str]:
    text = (source or "").strip()
    errors = []
    if any(token in text.lower() for token in FORBIDDEN):
        errors.append("contains a forbidden directive")
    first = text.splitlines()[0].strip() if text else ""
    if expected == "flowchart" and first not in {"graph TD", "graph LR", "flowchart TD", "flowchart LR"}:
        errors.append("use-case diagram must start with graph/flowchart TD or LR")
    if expected == "erd" and first != "erDiagram":
        errors.append("ERD must start with erDiagram")
    for opening, closing in (("(", ")"), ("[", "]"), ("{", "}")):
        if text.count(opening) != text.count(closing):
            errors.append(f"unbalanced {opening}{closing}")
    if len(text) > 50_000:
        errors.append("diagram exceeds 50,000 characters")
    return errors


def _identifier(value: str, prefix: str) -> str:
    cleaned = re.sub(r"[^A-Za-z0-9_]", "_", value).strip("_")
    if not cleaned or cleaned[0].isdigit():
        cleaned = f"{prefix}_{cleaned}"
    return cleaned[:60]


def deterministic_design(functional: list[dict], original_errors: list[str]) -> DesignSuggestionsData:
    actors = list(dict.fromkeys(
        str(item.get("actor") or "User").strip() for item in functional
    )) or ["User"]
    use_case_lines = ["graph LR"]
    seen = set()
    for index, item in enumerate(functional, 1):
        actor = str(item.get("actor") or "User").strip()
        label = " ".join(filter(None, [
            str(item.get("action") or "").strip(),
            str(item.get("object") or "").strip(),
        ])).strip() or f"Use case {index}"
        key = (actor.lower(), label.lower())
        if key in seen:
            continue
        seen.add(key)
        actor_id = _identifier(actor, "Actor")
        safe_actor = actor.replace('"', "'")
        safe_label = label.replace('"', "'")
        use_case_lines.append(
            f'    {actor_id}("{safe_actor}") --> UC_{index}(["{safe_label}"])'
        )

    entities = list(dict.fromkeys(
        str(item.get("object") or "").strip() for item in functional
        if str(item.get("object") or "").strip()
    )) or ["Requirement"]
    erd_lines = ["erDiagram"]
    entity_ids = [_identifier(entity.upper(), "ENTITY") for entity in entities]
    for entity_id in entity_ids:
        erd_lines.extend([
            f"    {entity_id} {{",
            "        string id",
            "        string name",
            "    }",
        ])
    for left, right in zip(entity_ids, entity_ids[1:]):
        erd_lines.append(f'    {left} ||--o{{ {right} : "relates to"')

    return DesignSuggestionsData(
        useCaseMermaid="\n".join(use_case_lines) + "\n",
        erdMermaid="\n".join(erd_lines) + "\n",
        mainActors=actors,
        mainEntities=entities,
        validationStatus="repaired",
        validationErrors=original_errors,
    )


def validate_and_repair(
    result: DesignSuggestionsData,
    functional: list[dict],
) -> DesignSuggestionsData:
    errors = [
        *[f"useCase: {error}" for error in validate_mermaid(result.useCaseMermaid, "flowchart")],
        *[f"erd: {error}" for error in validate_mermaid(result.erdMermaid, "erd")],
    ]
    if errors:
        return deterministic_design(functional, errors)
    return result.model_copy(update={"validationStatus": "valid", "validationErrors": []})
