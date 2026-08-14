#!/usr/bin/env python3
"""Validate and lock the versioned ReqSimulator evaluation catalog."""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
from collections import Counter
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SCENARIO_DIRECTORY = REPOSITORY_ROOT / "ai-service" / "app" / "scenarios"
ARTIFACT_DIRECTORY = REPOSITORY_ROOT / "evaluation-artifacts"
REVIEW_PATH = ARTIFACT_DIRECTORY / "ground_truth_reviews_v1.csv"
LOCK_PATH = ARTIFACT_DIRECTORY / "scenario_catalog_v1.lock.json"
CLAIMS_PATH = ARTIFACT_DIRECTORY / "claims_registry_v1.json"
AB_PROTOCOL_PATH = ARTIFACT_DIRECTORY / "ab_feedback_protocol_v1.json"
GROUND_TRUTH_PROTOCOL_PATH = ARTIFACT_DIRECTORY / "ground_truth_review_protocol_v1.json"
RUN_SCHEMA_PATH = ARTIFACT_DIRECTORY / "run_manifest_schema_v1.json"
SCORER_PATH = REPOSITORY_ROOT / "tools" / "score_evaluation_run.py"
REVIEW_COLUMNS = (
    "scenario_key",
    "requirement_id",
    "status",
    "reviewer_id",
    "reviewed_at_utc",
    "notes",
)


def sha256_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def canonical_json(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")


def load_catalog() -> tuple[list[dict], list[tuple[str, str]]]:
    scenarios: list[dict] = []
    requirement_keys: list[tuple[str, str]] = []
    for path in sorted(SCENARIO_DIRECTORY.glob("*.json")):
        data = json.loads(path.read_text(encoding="utf-8"))
        required = {
            "scenario_key", "scenario_title", "context", "domain", "difficulty",
            "source_kind", "review_status", "requirements",
        }
        missing = sorted(required - data.keys())
        if missing:
            raise ValueError(f"{path.name} is missing: {', '.join(missing)}")
        if data["source_kind"] not in {"synthetic", "ingested", "hybrid"}:
            raise ValueError(f"{path.name} has unsupported source_kind")
        if data["review_status"] not in {"provisional", "approved", "rejected"}:
            raise ValueError(f"{path.name} has unsupported review_status")
        ids = [str(item["id"]) for item in data["requirements"]]
        if len(ids) != len(set(ids)):
            raise ValueError(f"{path.name} contains duplicate requirement IDs")
        for item in data["requirements"]:
            for field in ("id", "text", "gate", "keywords", "question_types", "reveal_condition", "reveal_difficulty"):
                if field not in item:
                    raise ValueError(f"{path.name}:{item.get('id', '?')} is missing {field}")
            requirement_keys.append((str(data["scenario_key"]), str(item["id"])))
        scenarios.append({"path": path, "data": data})

    scenario_keys = [item["data"]["scenario_key"] for item in scenarios]
    if len(scenario_keys) != len(set(scenario_keys)):
        raise ValueError("Scenario keys must be unique")
    if len(scenarios) != 10 or len(requirement_keys) != 100:
        raise ValueError(
            f"Catalog v1 must contain exactly 10 scenarios/100 requirements; "
            f"found {len(scenarios)}/{len(requirement_keys)}"
        )
    return scenarios, requirement_keys


def initialize_reviews(requirement_keys: list[tuple[str, str]]) -> None:
    if REVIEW_PATH.exists():
        raise FileExistsError(f"Refusing to overwrite existing review evidence: {REVIEW_PATH}")
    ARTIFACT_DIRECTORY.mkdir(parents=True, exist_ok=True)
    with REVIEW_PATH.open("w", encoding="utf-8", newline="") as output:
        writer = csv.DictWriter(output, fieldnames=REVIEW_COLUMNS, lineterminator="\n")
        writer.writeheader()
        for scenario_key, requirement_id in requirement_keys:
            writer.writerow({
                "scenario_key": scenario_key,
                "requirement_id": requirement_id,
                "status": "provisional",
                "reviewer_id": "",
                "reviewed_at_utc": "",
                "notes": "",
            })


def load_reviews(expected_keys: list[tuple[str, str]]) -> tuple[list[dict[str, str]], Counter]:
    with REVIEW_PATH.open("r", encoding="utf-8", newline="") as source:
        reader = csv.DictReader(source)
        if tuple(reader.fieldnames or ()) != REVIEW_COLUMNS:
            raise ValueError("Ground-truth review CSV has an unexpected header")
        rows = list(reader)

    actual_keys = [(row["scenario_key"], row["requirement_id"]) for row in rows]
    if actual_keys != expected_keys:
        raise ValueError("Ground-truth review rows do not match the locked catalog order")
    for row in rows:
        status = row["status"]
        if status not in {"provisional", "approved", "rejected"}:
            raise ValueError(f"Unsupported review status: {status}")
        if status == "approved" and (not row["reviewer_id"] or not row["reviewed_at_utc"]):
            raise ValueError("Approved requirements must record reviewer_id and reviewed_at_utc")
    return rows, Counter(row["status"] for row in rows)


def build_lock() -> bytes:
    scenarios, requirement_keys = load_catalog()
    _, review_summary = load_reviews(requirement_keys)
    claims = json.loads(CLAIMS_PATH.read_text(encoding="utf-8"))
    protocol = json.loads(AB_PROTOCOL_PATH.read_text(encoding="utf-8"))

    evidence_paths = [
        REVIEW_PATH,
        CLAIMS_PATH,
        AB_PROTOCOL_PATH,
        GROUND_TRUTH_PROTOCOL_PATH,
        RUN_SCHEMA_PATH,
        SCORER_PATH,
    ]
    allowed_claim_statuses = {"unverified", "not_run", "verified", "rejected"}
    for claim in claims["claims"]:
        if claim.get("status") not in allowed_claim_statuses:
            raise ValueError(f"Unsupported claim status: {claim.get('status')}")
        if claim.get("status") != "verified":
            continue
        references = claim.get("evidence_refs") or []
        if claim.get("observed_value") is None or not references:
            raise ValueError(f"Verified claim {claim['id']} requires an observed value and evidence")
        for reference in references:
            path = (REPOSITORY_ROOT / reference).resolve()
            if REPOSITORY_ROOT.resolve() not in path.parents or not path.is_file():
                raise ValueError(f"Verified claim {claim['id']} has invalid evidence: {reference}")
            evidence_paths.append(path)

    scenario_entries = []
    for entry in scenarios:
        path = entry["path"]
        data = entry["data"]
        scenario_entries.append({
            "scenarioKey": data["scenario_key"],
            "file": path.relative_to(REPOSITORY_ROOT).as_posix(),
            "sha256": sha256_bytes(path.read_bytes()),
            "requirementCount": len(data["requirements"]),
            "sourceKind": data["source_kind"],
            "reviewStatus": data["review_status"],
        })

    evidence_files = {}
    for path in dict.fromkeys(evidence_paths):
        evidence_files[path.relative_to(REPOSITORY_ROOT).as_posix()] = sha256_bytes(path.read_bytes())
    aggregate_material = canonical_json({"scenarios": scenario_entries, "evidence": evidence_files})
    all_ground_truth_approved = review_summary.get("approved", 0) == 100
    verified_claims = [item for item in claims["claims"] if item.get("status") == "verified"]

    return canonical_json({
        "schemaVersion": 1,
        "catalogVersion": "scenario-catalog-v1",
        "catalogSha256": sha256_bytes(aggregate_material),
        "scenarioCount": len(scenarios),
        "requirementCount": len(requirement_keys),
        "groundTruthStatus": "approved" if all_ground_truth_approved else "provisional",
        "claimable": all_ground_truth_approved and len(verified_claims) == len(claims["claims"]),
        "reviewSummary": dict(sorted(review_summary.items())),
        "scenarios": scenario_entries,
        "evidenceFiles": evidence_files,
        "claims": [
            {"id": item["id"], "status": item["status"], "observedValue": item.get("observed_value")}
            for item in claims["claims"]
        ],
        "abProtocolId": protocol["protocol_id"],
    })


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--initialize-reviews", action="store_true")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    _, requirement_keys = load_catalog()
    if args.initialize_reviews:
        initialize_reviews(requirement_keys)
    expected = build_lock()
    if args.check:
        if not LOCK_PATH.exists() or LOCK_PATH.read_bytes() != expected:
            print("Evaluation lock is stale. Run: python tools/build_evaluation_artifacts.py")
            return 1
        print("Evaluation catalog lock is current (10 scenarios/100 requirements).")
        return 0
    LOCK_PATH.write_bytes(expected)
    print(f"Wrote {LOCK_PATH.relative_to(REPOSITORY_ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
