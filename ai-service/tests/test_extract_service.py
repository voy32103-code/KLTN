import unittest
from types import SimpleNamespace

from app.services.extract_service import _fallback_extract_requirements, _parse_extraction_json


class ExtractServiceTests(unittest.TestCase):
    def test_parse_extraction_json_accepts_markdown_fenced_array(self):
        result = _parse_extraction_json(
            """
            ```json
            [{"text": "The system should alert staff about low stock.", "confidence": 0.82}]
            ```
            """
        )

        self.assertEqual(len(result), 1)
        self.assertEqual(result[0].text, "The system should alert staff about low stock.")
        self.assertEqual(result[0].confidence, 0.82)

    def test_fallback_extracts_requirements_from_inventory_conversation(self):
        history = [
            SimpleNamespace(role="Student", content="What is the main purpose of the inventory system?"),
            SimpleNamespace(role="Stakeholder", content="Staff need to view current stock levels for each product."),
            SimpleNamespace(role="Student", content="What happens if internet goes down temporarily during daily operations?"),
            SimpleNamespace(role="Stakeholder", content="The system should keep recording transactions offline and sync later."),
        ]

        result = _fallback_extract_requirements(history)
        texts = [item.text for item in result]

        self.assertGreaterEqual(len(result), 3)
        self.assertTrue(any("stock levels" in text for text in texts))
        self.assertTrue(any("offline" in text.lower() for text in texts))
        self.assertTrue(all(0 <= item.confidence <= 1 for item in result))


if __name__ == "__main__":
    unittest.main()
