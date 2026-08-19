import asyncio
import unittest

import numpy as np

from app.models.schemas import StructuredRequirement
from app.services.extract_service import _parse_structured_extraction_json
from app.services.matching_service import assign_one_to_one
from app.services.aaoc_matching_service import classify_aaoc, score_pair
from app.services.normalization_service import normalize_and_deduplicate


class NormalizationTests(unittest.TestCase):
    def test_normalization_maps_synonyms_and_keeps_highest_confidence_duplicate(self):
        requirements = [
            StructuredRequirement(
                id="REQ001",
                actor="Sinh viên",
                action="Đăng ký",
                object="học phần",
                condition=None,
                type="FR",
                confidence=0.72,
            ),
            StructuredRequirement(
                id="REQ002",
                actor="Student",
                action="Register",
                object="Course",
                condition=None,
                type="FR",
                confidence=0.94,
            ),
        ]

        normalized = normalize_and_deduplicate(requirements)

        self.assertEqual(len(normalized), 1)
        self.assertEqual(normalized[0].id, "REQ002")
        self.assertEqual(normalized[0].canonicalKey, "student|register|course|")
        self.assertEqual(normalized[0].canonicalText, "student must register course.")

    def test_structured_parser_rejects_invalid_type(self):
        invalid = '[{"id":"REQ1","actor":"Student","action":"Register","object":"Course","type":"Other"}]'

        with self.assertRaises(Exception):
            asyncio.run(_parse_structured_extraction_json(invalid))

    def test_scenario_glossary_overrides_shared_aliases_and_is_used_for_deduplication(self):
        requirement = StructuredRequirement(
            id="REQ003",
            actor="Member",
            action="Reserve",
            object="Desk pass",
            condition="At library branch A",
            type="FR",
            confidence=0.9,
        )
        glossary = {
            "actor": {"member": "library member"},
            "action": {"reserve": "book"},
            "object": {"desk pass": "study desk"},
            "condition": {"at library branch a": "branch a"},
        }
        normalized = normalize_and_deduplicate([requirement], glossary)
        self.assertEqual(
            normalized[0].canonicalKey,
            "library member|book|study desk|branch a",
        )

    def test_scenario_glossary_is_used_for_aaoc_matching(self):
        extracted = type("Requirement", (), {
            "type": "FR", "actor": "Member", "action": "Reserve",
            "object": "Desk pass", "condition": "At library branch A",
        })()
        hidden = type("Requirement", (), {
            "type": "FR", "actor": "Library member", "action": "Book",
            "object": "Study desk", "condition": "Branch A",
        })()
        glossary = {
            "actor": {"member": "library member"},
            "action": {"reserve": "book"},
            "object": {"desk pass": "study desk"},
            "condition": {"at library branch a": "branch a"},
        }
        score = score_pair(extracted, hidden, glossary)
        self.assertIsNotNone(score)
        self.assertEqual(score.score, 1.0)

    def test_aaoc_with_different_actor_is_partial_not_exact(self):
        extracted = type("Requirement", (), {
            "type": "FR", "actor": "System", "action": "Create",
            "object": "Defect report", "condition": None,
        })()
        hidden = type("Requirement", (), {
            "type": "FR", "actor": "Tester", "action": "Create",
            "object": "Defect report", "condition": None,
        })()

        score = score_pair(extracted, hidden)

        self.assertIsNotNone(score)
        self.assertEqual(score.score, 0.8)
        self.assertEqual(classify_aaoc(score.score, score.component_scores), "partial")


class OneToOneMatchingTests(unittest.TestCase):
    def test_assignment_does_not_reuse_one_extracted_requirement(self):
        matrix = np.array([
            [0.93, 0.89],
            [0.88, 0.77],
        ])

        assignments = assign_one_to_one(matrix, lambda _extracted, _hidden, score: score >= 0.75)

        self.assertEqual(assignments, {0: 0, 1: 1})
        self.assertEqual(len(set(assignments.values())), len(assignments))

    def test_assignment_leaves_low_confidence_neighbour_unmatched(self):
        matrix = np.array([
            [0.92, 0.28],
        ])

        assignments = assign_one_to_one(matrix, lambda _extracted, _hidden, score: score >= 0.55)

        self.assertEqual(assignments, {0: 0})


if __name__ == "__main__":
    unittest.main()
