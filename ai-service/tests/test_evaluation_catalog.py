import csv
import hashlib
import json
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SCENARIO_DIRECTORY = REPOSITORY_ROOT / "ai-service" / "app" / "scenarios"
BACKEND_DIRECTORY = REPOSITORY_ROOT / "backend" / "ReqSimulator.API" / "Data" / "ScenarioCatalog"
ARTIFACT_DIRECTORY = REPOSITORY_ROOT / "evaluation-artifacts"


class EvaluationCatalogTests(unittest.TestCase):
    def test_catalog_has_exactly_ten_scenarios_and_one_hundred_requirements(self):
        documents = [json.loads(path.read_text(encoding="utf-8")) for path in sorted(SCENARIO_DIRECTORY.glob("*.json"))]
        self.assertEqual(len(documents), 10)
        self.assertEqual(sum(len(document["requirements"]) for document in documents), 100)
        self.assertEqual(len({document["scenario_key"] for document in documents}), 10)

    def test_every_scenario_declares_provenance_and_review_status(self):
        for path in sorted(SCENARIO_DIRECTORY.glob("*.json")):
            document = json.loads(path.read_text(encoding="utf-8"))
            self.assertIn(document["source_kind"], {"synthetic", "ingested", "hybrid"}, path.name)
            self.assertIn(document["review_status"], {"provisional", "approved", "rejected"}, path.name)
            ids = [item["id"] for item in document["requirements"]]
            self.assertEqual(len(ids), len(set(ids)), path.name)

    def test_locked_evaluation_catalog_is_a_byte_identical_runtime_subset(self):
        source = {path.name: path.read_bytes() for path in SCENARIO_DIRECTORY.glob("*.json")}
        backend = {path.name: path.read_bytes() for path in BACKEND_DIRECTORY.glob("*.json")}
        self.assertTrue(set(source).issubset(backend))
        for name, content in source.items():
            self.assertEqual(content, backend[name], name)

    def test_review_sheet_covers_every_requirement_once(self):
        expected = []
        for path in sorted(SCENARIO_DIRECTORY.glob("*.json")):
            document = json.loads(path.read_text(encoding="utf-8"))
            expected.extend((document["scenario_key"], item["id"]) for item in document["requirements"])
        with (ARTIFACT_DIRECTORY / "ground_truth_reviews_v1.csv").open(encoding="utf-8", newline="") as source:
            rows = list(csv.DictReader(source))
        actual = [(row["scenario_key"], row["requirement_id"]) for row in rows]
        self.assertEqual(actual, expected)

    def test_lock_hashes_match_source_files_and_is_not_claimable_while_provisional(self):
        lock = json.loads((ARTIFACT_DIRECTORY / "scenario_catalog_v1.lock.json").read_text(encoding="utf-8"))
        self.assertEqual(lock["scenarioCount"], 10)
        self.assertEqual(lock["requirementCount"], 100)
        for entry in lock["scenarios"]:
            content = (REPOSITORY_ROOT / entry["file"]).read_bytes()
            self.assertEqual(hashlib.sha256(content).hexdigest(), entry["sha256"])
        if lock["groundTruthStatus"] != "approved":
            self.assertFalse(lock["claimable"])


if __name__ == "__main__":
    unittest.main()
