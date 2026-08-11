"""Generate evidence-bounded design diagrams from extracted requirements."""

from app.models.schemas import DesignSuggestionsData
from app.services.mermaid_validation_service import deterministic_design


async def generate_design_models(
    extracted_requirements: list,
    scenario_description: str | None = None,
    selected_model: str | None = None,
) -> DesignSuggestionsData:
    """Build reproducible UCD/ERD drafts without inferring missing requirements.

    The previous provider-first design could produce syntactically valid but
    unsupported include/extend or ERD cardinality claims. For a teaching system,
    an auditable draft from FR AAO fields is safer than a richer invented diagram.
    """
    _ = scenario_description, selected_model
    if not extracted_requirements:
        return deterministic_design([], [])

    functional = []
    for requirement in extracted_requirements:
        requirement_type = getattr(requirement, "type", None)
        if requirement_type and requirement_type != "FR":
            continue
        functional.append(
            {
                "actor": getattr(requirement, "actor", None),
                "action": getattr(requirement, "action", None),
                "object": getattr(requirement, "object", None),
                "condition": getattr(requirement, "condition", None),
                "text": getattr(requirement, "text", str(requirement)),
            }
        )
    if not functional:
        return deterministic_design([], [])

    return deterministic_design(functional, [
        "Deterministic draft: FR-only, actor/action/object evidence only; no inferred cardinality or include/extend."
    ])
