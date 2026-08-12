import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from app.services.learning_feedback_service import generate_learning_feedback


class LearningFeedbackAbTests(unittest.IsolatedAsyncioTestCase):
    async def test_variant_a_uses_deterministic_feedback_without_provider(self):
        fallback = (["strength"], ["weakness"], ["suggestion"])
        with patch(
            "app.services.learning_feedback_service.client_manager.generate_content",
            new=AsyncMock(),
        ) as provider:
            result = await generate_learning_feedback([], [], "model", "A", fallback)
        self.assertEqual(result, fallback)
        provider.assert_not_awaited()

    async def test_variant_b_prompt_excludes_hidden_requirement_text(self):
        hidden = [SimpleNamespace(
            id="R1", category="Functional", text="SECRET_GROUND_TRUTH"
        )]
        matches = [SimpleNamespace(
            hiddenId="R1", matchType="partial", score=0.7,
            componentScores={"actor": 1.0},
        )]
        response = SimpleNamespace(text=(
            '{"strengths":["ok"],"weaknesses":["w"],"suggestions":["s"]}'
        ))
        with patch(
            "app.services.learning_feedback_service.client_manager.generate_content",
            new=AsyncMock(return_value=response),
        ) as provider:
            result = await generate_learning_feedback(
                matches, hidden, "model", "B", ([], [], [])
            )
        prompt = provider.await_args.kwargs["contents"]
        self.assertNotIn("SECRET_GROUND_TRUTH", prompt)
        self.assertEqual(result, (["ok"], ["w"], ["s"]))

    async def test_variant_b_rejects_feedback_that_repeats_hidden_requirement_wording(self):
        hidden = [SimpleNamespace(
            id="R1", category="Functional", text="Customer must cancel reservation before twenty four hours"
        )]
        matches = [SimpleNamespace(
            hiddenId="R1", matchType="partial", score=0.7,
            componentScores={"actor": 1.0},
        )]
        response = SimpleNamespace(text=(
            '{"strengths":[],"weaknesses":[],"suggestions":["Customer must cancel reservation before twenty four hours."]}'
        ))
        fallback = (["safe"], ["safe"], ["safe"])
        with patch(
            "app.services.learning_feedback_service.client_manager.generate_content",
            new=AsyncMock(return_value=response),
        ):
            result = await generate_learning_feedback(matches, hidden, "model", "B", fallback)
        self.assertEqual(result, fallback)


if __name__ == "__main__":
    unittest.main()
