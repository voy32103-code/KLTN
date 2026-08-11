import asyncio
import json
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import numpy as np

from app.models.schemas import EvaluateRequest, ExtractedReq, HiddenReq
from app.services.evaluate_service import evaluate
from app.services.design_service import generate_design_models


class EvaluateServiceTests(unittest.TestCase):
    def test_evaluation_keeps_matches_one_to_one_and_surfaces_extra_extraction(self):
        request = EvaluateRequest(
            extracted=[
                ExtractedReq(text="online registration", confidence=0.95),
                ExtractedReq(text="unrelated dashboard", confidence=0.60),
                ExtractedReq(text="prerequisite checking", confidence=0.90),
            ],
            hiddenRequirements=[
                HiddenReq(id="H1", text="students register online", category="Functional"),
                HiddenReq(id="H2", text="check prerequisites", category="BusinessRule"),
            ],
        )
        matrix = np.array([
            [0.96, 0.94],
            [0.20, 0.12],
            [0.10, 0.81],
        ])

        with patch(
            "app.services.evaluate_service.compute_similarity_matrix",
            AsyncMock(return_value=matrix),
        ), patch(
            "app.services.evaluate_service.generate_design_models",
            new=AsyncMock(return_value=None),
        ):
            result = asyncio.run(evaluate(request))

        matched = [match.extractedText for match in result.matches if match.extractedText]
        self.assertEqual(matched, ["online registration", "prerequisite checking"])
        self.assertEqual(len(set(matched)), 2)
        self.assertEqual(result.extraExtractedCount, 1)
        self.assertEqual(result.feedback.extractionsToReview, ["unrelated dashboard"])

    def test_design_generation_uses_shared_provider_manager(self):
        response = SimpleNamespace(text=json.dumps({
            "useCaseMermaid": "graph TD\nUser --> ViewStock",
            "erdMermaid": "erDiagram\nSTOCK ||--o{ ITEM : contains",
            "mainActors": ["User"],
            "mainEntities": ["Stock", "Item"],
        }))
        requirement = ExtractedReq(
            text="Users view stock",
            confidence=0.9,
            actor="User",
            action="View",
            object="Stock",
            type="FR",
        )
        with patch(
            "app.services.design_service.client_manager.generate_content",
            new=AsyncMock(return_value=response),
        ) as generate:
            result = asyncio.run(generate_design_models([requirement], selected_model="gemini-2.5-flash"))

        self.assertEqual(result.mainActors, ["User"])
        self.assertEqual(generate.await_args.kwargs["model"], "gemini-2.5-flash")


if __name__ == "__main__":
    unittest.main()
