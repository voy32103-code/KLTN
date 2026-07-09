"""
Post-generation consistency checks for stakeholder replies.

The checker is intentionally conservative: it only flags likely out-of-gate
disclosure when a reply mentions multiple distinctive keywords from a hidden
requirement that was not allowed in the current turn.
"""
from dataclasses import dataclass

from app.services.scenario_config_service import ScenarioConfig, normalize_text


IMPLEMENTATION_KEYWORDS = (
    "database",
    "sql",
    "api",
    "endpoint",
    "jwt",
    "frontend",
    "backend",
    "microservice",
)

DEFAULT_CONSISTENCY_FALLBACK = (
    "At this stage, I should stay within the scope of your current question. "
    "Please ask a more specific business question if you want to explore another rule."
)


@dataclass(frozen=True)
class ConsistencyViolation:
    code: str
    detail: str


@dataclass(frozen=True)
class ConsistencyCheckResult:
    passed: bool
    violations: tuple[ConsistencyViolation, ...]


def check_response_consistency(
    reply: str,
    allowed_requirements: list[str],
    config: ScenarioConfig | None,
) -> ConsistencyCheckResult:
    violations: list[ConsistencyViolation] = []
    normalized_reply = normalize_text(reply)
    allowed_norm = {normalize_text(item) for item in allowed_requirements}

    if config is not None:
        for rule in config.requirements:
            if rule.normalized_text in allowed_norm:
                continue

            distinctive_keywords = [
                keyword for keyword in rule.keywords
                if len(keyword) >= 5 and keyword in normalized_reply
            ]
            if len(distinctive_keywords) >= 2:
                violations.append(ConsistencyViolation(
                    "out_of_gate_disclosure",
                    f"Reply appears to disclose {rule.requirement_id}: {', '.join(distinctive_keywords[:4])}",
                ))

    implementation_hits = [
        keyword for keyword in IMPLEMENTATION_KEYWORDS
        if keyword in normalized_reply
    ]
    if len(implementation_hits) >= 2:
        violations.append(ConsistencyViolation(
            "implementation_leakage",
            f"Reply includes implementation details: {', '.join(implementation_hits[:4])}",
        ))

    return ConsistencyCheckResult(
        passed=len(violations) == 0,
        violations=tuple(violations),
    )


def normalize_reply_after_consistency_check(
    reply: str,
    check: ConsistencyCheckResult,
    safe_fallback: str | None = None,
) -> str:
    if check.passed:
        return reply

    return safe_fallback or DEFAULT_CONSISTENCY_FALLBACK
