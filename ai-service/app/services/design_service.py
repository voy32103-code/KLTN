import os
import json
import threading
from google import genai
from app.models.schemas import DesignSuggestionsData
from app.services.mermaid_validation_service import (
    deterministic_design,
    validate_and_repair,
)

_client: genai.Client | None = None
_client_lock = threading.Lock()
MODEL = os.getenv("MODEL_NAME", "gemini-2.5-flash")


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


def generate_design_models(
    extracted_requirements: list,
    scenario_description: str | None = None,
) -> DesignSuggestionsData:
    """
    Sử dụng Gemini API để phân tích các yêu cầu đã trích xuất, 
    xác định actors, entities và sinh mã Mermaid.js cho Use Case & ERD.
    """
    if not extracted_requirements:
        return DesignSuggestionsData(
            useCaseMermaid="graph TD\n    Student(\"Student\") --> AskQuestion([\"Ask questions to reveal requirements\"])\n",
            erdMermaid="erDiagram\n    SYSTEM-GOAL ||--|| REQUIREMENT : satisfies\n",
            mainActors=["Student"],
            mainEntities=["Requirement"]
        )

    client = _get_client()
    functional = []
    for req in extracted_requirements:
        req_type = getattr(req, "type", None)
        if req_type and req_type != "FR":
            continue
        functional.append({
            "actor": getattr(req, "actor", None),
            "action": getattr(req, "action", None),
            "object": getattr(req, "object", None),
            "condition": getattr(req, "condition", None),
            "text": getattr(req, "text", str(req)),
        })
    requirements_text = json.dumps(functional, ensure_ascii=False, indent=2)

    prompt = f"""You are an expert Software Architect and Business Analyst.
Based on the following software requirements collected from a stakeholder interview, generate a preliminary system design:

Requirements list:
{requirements_text}

Scenario context:
{scenario_description or "Not supplied"}

Identify the main actors, main database entities, and generate the corresponding Use Case Diagram and Entity-Relationship Diagram (ERD) in Mermaid.js syntax.
Only structured functional requirements (FR) may become use cases. Map actor to actor,
combine action + object into the use-case label, merge duplicates, and use condition only
as supporting context. Derive ERD entities from objects and scenario context. Do not invent
include/extend relationships.

CRITICAL MERMAID SYNTAX RULES:
1. Use Case Diagram:
   - Use 'graph TD' or 'graph LR'.
   - Format actors as nodes: actorId("Actor Name")
   - Format use cases as stadium shapes: useCaseId(["Use Case Description"])
   - Example syntax:
     graph TD
         Customer("Customer") --> BookRoom(["Book Room"])
         Staff("Hotel Staff") --> CheckIn(["Check-In Guest"])

2. Entity-Relationship Diagram (ERD):
   - Use 'erDiagram'.
   - Format relations using standard cardinality, e.g., CUSTOMER ||--o{{ BOOKING : "places"
   - Format entities with attributes inside brackets.
   - Example syntax:
     erDiagram
         CUSTOMER ||--o{{ BOOKING : "places"
         CUSTOMER {{
             int id
             string name
         }}
         BOOKING {{
             int id
             date bookingDate
         }}
   - Keep attribute types simple (int, string, date, boolean) and do not use special characters in entity names.

3. IMPORTANT:
   - Do NOT wrap the Mermaid code in markdown blocks (like ```mermaid ... ```) inside the JSON output. Provide the raw string directly.
   - Ensure the generated Mermaid syntax compiles without errors.
"""

    try:
        response = client.models.generate_content(
            model=MODEL,
            contents=prompt,
            config={
                "response_mime_type": "application/json",
                "response_schema": DesignSuggestionsData,
                "temperature": 0.2,
            }
        )
        
        # Thư viện google-genai trả về chuỗi JSON ở response.text
        # Pydantic có thể load trực tiếp từ JSON string hoặc dictionary
        data = json.loads(response.text)
        return validate_and_repair(DesignSuggestionsData(**data), functional)

    except Exception as e:
        return deterministic_design(functional, ["AI generation or JSON parsing failed"])
        # Fallback an toàn nếu có lỗi gọi LLM hoặc parse JSON
        return DesignSuggestionsData(
            useCaseMermaid="graph TD\n    System(\"System\") --> Error([\"Failed to generate Use Case diagram\"])\n",
            erdMermaid="erDiagram\n    SYSTEM ||--|| ERROR-LOG : logs\n",
            mainActors=["System"],
            mainEntities=["ErrorLog"]
        )
