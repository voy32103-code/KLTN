"""Generate safe, preliminary design diagrams from extracted requirements."""
import json
import logging

from google.genai import types

from app.models.schemas import DesignSuggestionsData
from app.services.api_client_manager import client_manager
from app.services.mermaid_validation_service import deterministic_design, validate_and_repair

logger = logging.getLogger(__name__)


async def generate_design_models(
    extracted_requirements: list,
    scenario_description: str | None = None,
    selected_model: str | None = None,
) -> DesignSuggestionsData:
    """Use the shared provider manager so generation never blocks FastAPI's event loop."""
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

    prompt = f"""Create preliminary software design diagrams from these functional requirements:
{json.dumps(functional, ensure_ascii=False)}

Scenario context: {scenario_description or "Not supplied"}

Return a JSON object with mainActors, mainEntities, useCaseMermaid, and erdMermaid. Use Mermaid
graph TD/LR for use cases and erDiagram for ERD. Use only the supplied functional requirements;
do not invent relationships, implementation details, or sensitive information."""
    try:
        response = await client_manager.generate_content(
            model=selected_model or "gemini-2.5-flash",
            contents=prompt,
            config=types.GenerateContentConfig(
                response_mime_type="application/json",
                response_schema=DesignSuggestionsData,
                temperature=0.2,
                max_output_tokens=2000,
            ),
        )
        data = json.loads(response.text)
        return validate_and_repair(DesignSuggestionsData(**data), functional)
    except Exception:
        logger.exception("Design generation failed; using deterministic design.")
        return deterministic_design(functional, ["AI generation or JSON parsing failed"])
