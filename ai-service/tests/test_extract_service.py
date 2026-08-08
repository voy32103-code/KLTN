import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from app.models.schemas import ChatMessage, ExtractRequest
from app.services.extract_service import (
    _fallback_extract_requirements,
    _parse_extraction_json,
    extract_requirements,
)


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

    def test_primary_extraction_returns_normalized_structured_contract(self):
        request = ExtractRequest(
            sessionId="session-1",
            selectedModel="gemini-2.5-flash",
            history=[
                ChatMessage(
                    role="Stakeholder",
                    content="Sinh viên có thể đăng ký học phần trực tuyến.",
                    timestamp="2026-08-08T00:00:00Z",
                )
            ],
        )
        provider_response = SimpleNamespace(text="""
        [{
          "id": "REQ001",
          "actor": "Sinh viên",
          "action": "Đăng ký",
          "object": "học phần",
          "condition": null,
          "type": "FR",
          "priority": "high",
          "confidence": 0.96,
          "raw_text": "Sinh viên có thể đăng ký học phần trực tuyến."
        }]
        """)

        with patch(
            "app.services.extract_service.client_manager.generate_content",
            AsyncMock(return_value=provider_response),
        ):
            result = __import__("asyncio").run(extract_requirements(request))

        self.assertFalse(result.isFallback)
        self.assertEqual(result.requirements[0].text, "student must register course.")
        self.assertEqual(len(result.structuredRequirements), 1)
        self.assertEqual(result.normalizedRequirements[0].canonicalKey, "student|register|course|")


if __name__ == "__main__":
    unittest.main()
