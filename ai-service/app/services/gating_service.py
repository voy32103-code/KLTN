"""
Pure gating/state helpers for persona-driven stakeholder chat.

This module intentionally avoids LLM/client imports so it can be tested without
network access or API keys.
"""
from __future__ import annotations

import json
from typing import Any

from app.services.scenario_config_service import (
    ScenarioConfig,
    ScenarioRequirementRule,
    normalize_text,
)


TECHNICAL_KEYWORDS = (
    "api", "apis", "database", "schema", "table", "sql", "endpoint",
    "backend", "frontend", "server", "algorithm", "code", "implementation",
    "microservice", "json", "jwt", "postgres", "prompt"
)


def load_persona_state(req: Any) -> dict[str, Any]:
    default_state = {
        "mood": req.persona.mood or "neutral_busy",
        "patience": float(req.persona.patience),
        "turn_count": 0,
        "revealed_requirements": [],
    }

    if not req.personaStateJson:
        return default_state

    try:
        raw_state = json.loads(req.personaStateJson)
    except json.JSONDecodeError:
        return default_state

    mood = raw_state.get("mood") or raw_state.get("Mood") or default_state["mood"]
    
    patience_val = raw_state.get("patience") if raw_state.get("patience") is not None else raw_state.get("Patience")
    patience = float(patience_val) if patience_val is not None else default_state["patience"]
    
    turn_count_val = (
        raw_state.get("turn_count") 
        if raw_state.get("turn_count") is not None 
        else (raw_state.get("turnCount") if raw_state.get("turnCount") is not None else raw_state.get("TurnCount"))
    )
    turn_count = int(turn_count_val) if turn_count_val is not None else 0
    
    revealed_reqs_val = (
        raw_state.get("revealed_requirements") 
        or raw_state.get("revealedRequirements") 
        or raw_state.get("RevealedRequirements")
    )
    revealed_requirements = list(revealed_reqs_val) if revealed_reqs_val is not None else []

    return {
        "mood": mood,
        "patience": patience,
        "turn_count": turn_count,
        "revealed_requirements": revealed_requirements,
    }



def detect_question_type(message: str) -> str | None:
    msg = message.lower().strip()

    if any(phrase in msg for phrase in ["so that means", "so basically", "isn't it true", "right?", "correct?"]):
        return "Leading"
    if any(phrase in msg for phrase in ["what if", "how about", "what happens when", "exception", "special case"]):
        return "ExceptionOriented"
    if any(phrase in msg for phrase in ["can you explain", "what do you mean", "could you clarify", "clarify"]):
        return "Clarifying"
    if any(phrase in msg for phrase in ["why", "how exactly", "tell me more", "elaborate", "walk me through"]):
        return "Probing"
    if any(phrase in msg for phrase in ["must", "should", "require", "limit", "constraint", "rule", "blocked"]):
        return "ConstraintOriented"
    if msg.endswith("?") and any(phrase in msg for phrase in ["is it", "do you", "are there", "does it", "can it", "will it"]):
        return "Closed"
    if msg.endswith("?") or any(phrase in msg for phrase in ["what", "how", "describe", "tell me about"]):
        return "OpenEnded"

    return None


def is_overly_technical(message: str) -> bool:
    msg = normalize_text(message)
    return any(keyword in msg for keyword in TECHNICAL_KEYWORDS)


def is_repeated_question(message: str, history) -> bool:
    normalized = normalize_text(message)
    prior_student_messages = [
        normalize_text(msg.content)
        for msg in history
        if msg.role == "Student"
    ]
    return normalized in prior_student_messages


def detect_triggered_gates(message: str, question_type: str | None, config: ScenarioConfig | None) -> set[int]:
    if config is None:
        return {0} if question_type == "OpenEnded" else set()

    msg = normalize_text(message)
    triggered: set[int] = set()

    if any(keyword in msg for keyword in config.general_keywords):
        triggered.add(0)

    for gate, keywords in config.gate_keyword_groups.items():
        if any(keyword in msg for keyword in keywords):
            triggered.add(gate)

    triggered.update(config.question_type_gate_map.get(question_type or "", ()))

    if question_type == "OpenEnded" and not triggered:
        triggered.add(0)

    return triggered


def is_vague_question(message: str, triggered_gates: set[int], question_type: str | None) -> bool:
    msg = normalize_text(message)
    if len(msg.split()) <= 4:
        return True
    if question_type in {None, "OpenEnded"} and not triggered_gates:
        return True
    return any(phrase in msg for phrase in ["tell me more", "anything else", "what about that", "how about it"])


def score_requirement(rule: ScenarioRequirementRule, message: str, question_type: str | None) -> int:
    msg = normalize_text(message)
    keyword_hits = sum(1 for keyword in rule.keywords if keyword in msg)
    type_bonus = 2 if question_type in rule.question_types else 0
    return keyword_hits * 10 + type_bonus - int(rule.gate)


def filter_previously_revealed(
    available_requirements: list[str],
    revealed_norm: set[str],
) -> list[str]:
    allowed_previous: list[str] = []
    for requirement in available_requirements:
        if normalize_text(requirement) in revealed_norm and requirement not in allowed_previous:
            allowed_previous.append(requirement)
    return allowed_previous


def select_gated_requirements(
    req: Any,
    state: dict[str, Any],
    question_type: str | None,
    config: ScenarioConfig | None,
) -> tuple[list[str], list[str], list[str]]:
    revealed_before = list(state["revealed_requirements"])
    revealed_norm = {normalize_text(item) for item in revealed_before}
    allowed_previous = filter_previously_revealed(req.availableRequirements, revealed_norm)

    if config is None:
        return allowed_previous, allowed_previous, []

    rule_map = config.requirement_map
    known_requirements = [item for item in req.availableRequirements if normalize_text(item) in rule_map]
    if not known_requirements:
        return allowed_previous, allowed_previous, []

    # Build set of revealed IDs (case-insensitive) for prerequisite checking
    revealed_ids = set()
    for item in revealed_before:
        normalized_item = normalize_text(item)
        rule_item = rule_map.get(normalized_item)
        if rule_item is not None:
            revealed_ids.add(rule_item.requirement_id.strip().lower())

    triggered_gates = detect_triggered_gates(req.studentMessage, question_type, config)
    allowed_previous = filter_previously_revealed(known_requirements, revealed_norm)

    candidates: list[tuple[int, int, str]] = []
    for requirement in known_requirements:
        normalized = normalize_text(requirement)
        rule = rule_map.get(normalized)
        if rule is None or normalized in revealed_norm:
            continue

        if rule.gate == 0:
            if int(state["turn_count"]) > 0 and 0 not in triggered_gates:
                continue
        elif rule.gate not in triggered_gates:
            continue

        # Check prerequisites (by ID or normalized text for backward compatibility)
        if rule.requires:
            met = True
            for req_dep in rule.requires:
                dep_norm = normalize_text(req_dep)
                dep_id_lower = req_dep.strip().lower()
                if dep_id_lower not in revealed_ids and dep_norm not in revealed_norm:
                    met = False
                    break
            if not met:
                continue

        if rule.gate == 4 and float(state["patience"]) <= 0.40:
            continue

        keyword_hits = sum(1 for keyword in rule.keywords if keyword in normalize_text(req.studentMessage))
        if rule.gate != 0 and keyword_hits == 0:
            continue

        score = score_requirement(rule, req.studentMessage, question_type)
        if score > 0 or rule.gate == 0:
            candidates.append((score, rule.gate, requirement))

    candidates.sort(key=lambda item: (-item[0], item[1], item[2]))

    newly_revealed: list[str] = []
    if candidates:
        max_new = max(1, config.max_new_reveals_per_turn)
        newly_revealed = [item[2] for item in candidates[:max_new]]
    elif not allowed_previous and int(state["turn_count"]) == 0:
        opening_requirement = next(
            (
                item for item in known_requirements
                if rule_map.get(normalize_text(item), None)
                and rule_map[normalize_text(item)].gate == 0
            ),
            None,
        )
        if opening_requirement:
            newly_revealed.append(opening_requirement)

    allowed_this_turn = []
    for requirement in [*allowed_previous, *newly_revealed]:
        if requirement not in allowed_this_turn:
            allowed_this_turn.append(requirement)

    return allowed_this_turn, allowed_previous, newly_revealed


def resolve_mood(patience: float) -> str:
    if patience > 0.55:
        return "neutral_busy"
    if patience > 0.35:
        return "rushed"
    return "irritated"


def build_state_update(
    req: Any,
    state: dict[str, Any],
    question_type: str | None,
    newly_revealed: list[str],
    config: ScenarioConfig | None,
) -> dict[str, Any]:
    triggered_gates = detect_triggered_gates(req.studentMessage, question_type, config)

    if is_overly_technical(req.studentMessage):
        patience_delta = 0.12
    elif is_repeated_question(req.studentMessage, req.history):
        patience_delta = 0.10
    elif is_vague_question(req.studentMessage, triggered_gates, question_type):
        patience_delta = 0.08
    elif question_type in {"Probing", "Clarifying"} and newly_revealed:
        patience_delta = 0.02
    else:
        patience_delta = 0.03

    new_patience = max(0.05, round(float(state["patience"]) - patience_delta, 2))
    new_turn_count = int(state["turn_count"]) + 1

    return {
        "mood": resolve_mood(new_patience),
        "patience": new_patience,
        "turnCount": new_turn_count,
        "newlyRevealed": newly_revealed,
    }
