import asyncio
import unittest

import numpy as np

from app.models.schemas import StructuredRequirement
from app.services.extract_service import _parse_structured_extraction_json
from app.services.matching_service import assign_one_to_one
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
