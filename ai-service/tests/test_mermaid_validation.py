import unittest

from app.models.schemas import DesignSuggestionsData
from app.services.mermaid_validation_service import validate_and_repair, validate_mermaid


class MermaidValidationTests(unittest.TestCase):
    def test_rejects_forbidden_directive(self):
        errors = validate_mermaid("graph TD\n%%{init: {}}%%\nA-->B", "flowchart")
        self.assertTrue(errors)

    def test_repairs_invalid_generated_diagrams(self):
        result = DesignSuggestionsData(
            useCaseMermaid="not mermaid",
            erdMermaid="broken",
            mainActors=[],
            mainEntities=[],
        )
        repaired = validate_and_repair(result, [{
            "actor": "Student", "action": "Register", "object": "Course"
        }])
        self.assertEqual(repaired.validationStatus, "repaired")
        self.assertEqual(validate_mermaid(repaired.useCaseMermaid, "flowchart"), [])
        self.assertEqual(validate_mermaid(repaired.erdMermaid, "erd"), [])


if __name__ == "__main__":
    unittest.main()
