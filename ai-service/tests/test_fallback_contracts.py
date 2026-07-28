import unittest
from unittest.mock import AsyncMock, patch

from app.models.schemas import ChatRequest, ExtractRequest, PersonaProfile
from app.services.chat_service import chat
from app.services.extract_service import extract_requirements


def _chat_request() -> ChatRequest:
    return ChatRequest(
        sessionId="session",
        scenarioTitle="Inventory System",
        studentMessage="What does the system need to do?",
        history=[],
        persona=PersonaProfile(
            name="Stakeholder",
            roleTitle="Manager",
            traits="{}",
            style="neutral",
            mood="neutral",
            patience=1,
        ),
        availableRequirements=["Staff need to view stock levels."],
    )


class FallbackContractTests(unittest.IsolatedAsyncioTestCase):
    async def test_chat_marks_local_recovery_as_fallback(self):
        with patch(
            "app.services.chat_service.client_manager.generate_content",
            new=AsyncMock(side_effect=RuntimeError("provider unavailable")),
        ):
            response = await chat(_chat_request())

        self.assertTrue(response.isFallback)

    async def test_extract_marks_regex_recovery_as_fallback(self):
        request = ExtractRequest(sessionId="session", history=[])
        with (
            patch(
                "app.services.extract_service.client_manager.generate_content",
                new=AsyncMock(side_effect=RuntimeError("provider unavailable")),
            ),
            patch("app.services.extract_service.asyncio.sleep", new=AsyncMock()),
        ):
            response = await extract_requirements(request)

        self.assertTrue(response.isFallback)


if __name__ == "__main__":
    unittest.main()
