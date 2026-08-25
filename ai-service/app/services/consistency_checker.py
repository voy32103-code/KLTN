"""
Post-generation consistency checks for stakeholder replies.

The checker is intentionally conservative: it flags likely out-of-gate
disclosure and prevents a stakeholder reply from quoting internal canonical
requirements verbatim.
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
    # Từ khóa kỹ thuật tiếng Việt
    "cơ sở dữ liệu",
    "csdl",
    "bảng dữ liệu",
    "giao diện",
    "máy chủ",
    "đường dẫn api",
    "phân quyền",
    "đăng nhập",
)

DEFAULT_CONSISTENCY_FALLBACK = (
    "Ở giai đoạn này, tôi chỉ có thể chia sẻ thông tin nghiệp vụ trong phạm vi câu hỏi hiện tại. "
    "Bạn có thể hỏi cụ thể hơn về quy trình, điều kiện hoặc trường hợp ngoại lệ."
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

    # Ground-truth requirements are an internal scoring artifact. Even a
    # requirement that is allowed for this turn must be paraphrased as natural
    # stakeholder speech, never copied into the visible reply.
    for requirement in allowed_requirements:
        canonical = normalize_text(requirement)
        if len(canonical) >= 24 and canonical in normalized_reply:
            violations.append(ConsistencyViolation(
                "canonical_requirement_leak",
                "Reply quotes an internal canonical requirement verbatim.",
            ))
            break

    if config is not None:
        for rule in config.requirements:
            if rule.normalized_text in allowed_norm:
                continue

            distinctive_keywords = [
                keyword for keyword in rule.keywords
                if len(keyword) >= 2 and keyword in normalized_reply
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
