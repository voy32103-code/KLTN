import importlib.util
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SCORER_PATH = REPOSITORY_ROOT / "tools" / "score_evaluation_run.py"
SPEC = importlib.util.spec_from_file_location("reqsim_evaluation_scorer", SCORER_PATH)
SCORER = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
SPEC.loader.exec_module(SCORER)


class EvaluationScorerTests(unittest.TestCase):
    def test_micro_f1_is_derived_from_saved_requirement_ids(self):
        metrics = SCORER.score_extraction_matching([
            {"gold_requirement_ids": ["R1", "R2"], "predicted_requirement_ids": ["R1", "R2"]},
            {"gold_requirement_ids": ["R3"], "predicted_requirement_ids": ["R4"]},
        ])
        self.assertEqual(metrics["truePositive"], 2)
        self.assertEqual(metrics["falsePositive"], 1)
        self.assertEqual(metrics["falseNegative"], 1)
        self.assertAlmostEqual(metrics["microF1"], 2 / 3)

    def test_gating_accuracy_uses_exact_reveal_sets(self):
        cases = [
            {"expected_reveal_ids": [f"R{index}"], "actual_reveal_ids": [f"R{index}"]}
            for index in range(9)
        ]
        cases.append({"expected_reveal_ids": ["R9"], "actual_reveal_ids": []})
        metrics = SCORER.score_gating(cases)
        self.assertEqual(metrics["exactSetMatches"], 9)
        self.assertAlmostEqual(metrics["exactSetAccuracy"], 0.9)

    def test_ab_score_reports_ties_and_wilson_interval(self):
        cases = ([{"winner": "B"}] * 18) + ([{"winner": "A"}] * 12) + ([{"winner": "tie"}] * 2)
        metrics = SCORER.score_feedback_ab(cases, minimum_pairs=30)
        self.assertEqual(metrics["ties"], 2)
        self.assertAlmostEqual(metrics["preferenceRateBExcludingTies"], 0.6)
        self.assertTrue(metrics["minimumPairsMet"])
        self.assertLess(metrics["confidenceInterval95"][0], 0.6)
        self.assertGreater(metrics["confidenceInterval95"][1], 0.6)


if __name__ == "__main__":
    unittest.main()
