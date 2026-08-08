import unittest
from pathlib import Path

from tools.build_aaoc_annotation_pack import build
from tools.finalize_aaoc_annotations import finalize


class AaocAnnotationWorkflowTests(unittest.TestCase):
    def test_builds_200_pending_pairs_and_requires_human_labels(self):
        scenario_dir = Path(__file__).resolve().parents[1] / "app" / "scenarios"
        rows = build(scenario_dir, 200)
        self.assertEqual(len(rows), 200)
        self.assertTrue(all(row["label"] is None for row in rows))
        self.assertTrue(all(row["annotationStatus"] == "pending_human_review" for row in rows))

        with self.assertRaises(ValueError):
            finalize(rows, 200)

    def test_dual_agreement_produces_calibration_ready_pack(self):
        scenario_dir = Path(__file__).resolve().parents[1] / "app" / "scenarios"
        rows = build(scenario_dir, 200)
        for row in rows:
            row["annotator1Label"] = row["proposedLabel"]
            row["annotator2Label"] = row["proposedLabel"]
        completed, report = finalize(rows, 200)
        self.assertEqual(len(completed), 200)
        self.assertTrue(report["quotaMet"])
        self.assertEqual(report["doubleLabelAgreement"], 1.0)


if __name__ == "__main__":
    unittest.main()
