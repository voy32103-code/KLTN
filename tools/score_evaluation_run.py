#!/usr/bin/env python3
"""Score a locked ReqSimulator evaluation run and emit an immutable result manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Any


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
LOCK_PATH = REPOSITORY_ROOT / "evaluation-artifacts" / "scenario_catalog_v1.lock.json"
PROTOCOL_PATH = REPOSITORY_ROOT / "evaluation-artifacts" / "ab_feedback_protocol_v1.json"


def sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def ratio(numerator: int, denominator: int) -> float:
    return numerator / denominator if denominator else 0.0


def score_extraction_matching(cases: list[dict[str, Any]]) -> dict[str, Any]:
    true_positive = false_positive = false_negative = 0
    for case in cases:
        gold = set(case["gold_requirement_ids"])
        predicted = set(case["predicted_requirement_ids"])
        true_positive += len(gold & predicted)
        false_positive += len(predicted - gold)
        false_negative += len(gold - predicted)
    precision = ratio(true_positive, true_positive + false_positive)
    recall = ratio(true_positive, true_positive + false_negative)
    f1 = ratio(2 * precision * recall, precision + recall)
    return {
        "caseCount": len(cases),
        "truePositive": true_positive,
        "falsePositive": false_positive,
        "falseNegative": false_negative,
        "microPrecision": precision,
        "microRecall": recall,
        "microF1": f1,
    }


def score_gating(cases: list[dict[str, Any]]) -> dict[str, Any]:
    exact = 0
    expected_total = actual_total = correct_total = 0
    for case in cases:
        expected = set(case["expected_reveal_ids"])
        actual = set(case["actual_reveal_ids"])
        exact += expected == actual
        expected_total += len(expected)
        actual_total += len(actual)
        correct_total += len(expected & actual)
    precision = ratio(correct_total, actual_total)
    recall = ratio(correct_total, expected_total)
    return {
        "caseCount": len(cases),
        "exactSetMatches": exact,
        "exactSetAccuracy": ratio(exact, len(cases)),
        "revealPrecision": precision,
        "revealRecall": recall,
        "revealF1": ratio(2 * precision * recall, precision + recall),
    }


def wilson_interval(successes: int, trials: int, z: float = 1.959963984540054) -> list[float]:
    if trials == 0:
        return [0.0, 0.0]
    proportion = successes / trials
    denominator = 1 + z * z / trials
    centre = (proportion + z * z / (2 * trials)) / denominator
    margin = z * math.sqrt((proportion * (1 - proportion) + z * z / (4 * trials)) / trials) / denominator
    return [max(0.0, centre - margin), min(1.0, centre + margin)]


def score_feedback_ab(cases: list[dict[str, Any]], minimum_pairs: int) -> dict[str, Any]:
    winners = [str(case["winner"]).upper() for case in cases]
    invalid = sorted({winner for winner in winners if winner not in {"A", "B", "TIE"}})
    if invalid:
        raise ValueError(f"Unsupported A/B winner values: {', '.join(invalid)}")
    wins_a = winners.count("A")
    wins_b = winners.count("B")
    ties = winners.count("TIE")
    decisive = wins_a + wins_b
    return {
        "caseCount": len(cases),
        "winsA": wins_a,
        "winsB": wins_b,
        "ties": ties,
        "decisiveCount": decisive,
        "preferenceRateBExcludingTies": ratio(wins_b, decisive),
        "confidenceInterval95": wilson_interval(wins_b, decisive),
        "minimumPairsMet": len(cases) >= minimum_pairs,
    }


def score_manifest(manifest: dict[str, Any], lock: dict[str, Any]) -> dict[str, Any]:
    if manifest.get("schema_version") != 1:
        raise ValueError("Only evaluation run schema_version 1 is supported")
    if manifest.get("catalog_sha256") != lock["catalogSha256"]:
        raise ValueError("Run catalog_sha256 does not match the current catalog lock")
    cases = manifest.get("cases")
    if not isinstance(cases, list) or not cases:
        raise ValueError("Evaluation run must contain at least one case")
    task = manifest.get("task")
    if task == "extraction_matching":
        metrics = score_extraction_matching(cases)
    elif task == "gating":
        metrics = score_gating(cases)
    elif task == "feedback_ab":
        protocol = json.loads(PROTOCOL_PATH.read_text(encoding="utf-8"))
        metrics = score_feedback_ab(cases, int(protocol["design"]["minimum_pairs_before_claim"]))
    else:
        raise ValueError(f"Unsupported evaluation task: {task}")
    return {
        "schemaVersion": 1,
        "runId": manifest.get("run_id"),
        "task": task,
        "catalogSha256": lock["catalogSha256"],
        "groundTruthStatus": lock["groundTruthStatus"],
        "claimEligible": lock["groundTruthStatus"] == "approved" and (
            task != "feedback_ab" or metrics["minimumPairsMet"]
        ),
        "metrics": metrics,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path, help="Raw evaluation run JSON.")
    parser.add_argument("--output", type=Path, help="Where to write the scored result JSON.")
    args = parser.parse_args()
    input_bytes = args.input.read_bytes()
    manifest = json.loads(input_bytes)
    lock = json.loads(LOCK_PATH.read_text(encoding="utf-8"))
    result = score_manifest(manifest, lock)
    result["inputSha256"] = sha256(input_bytes)
    result["scorerSha256"] = sha256(Path(__file__).read_bytes())
    encoded = (json.dumps(result, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_bytes(encoded)
    else:
        print(encoded.decode("utf-8"), end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
