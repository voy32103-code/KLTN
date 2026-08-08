"""Calibrate the AAOC decision threshold on large labelled JSONL datasets.

Each line must contain:
{"label": true, "typeMatch": true, "actionMatch": true, "objectMatch": true,
 "actorScore": 1.0, "conditionScore": 0.5, "requirementType": "FR"}
"""
from __future__ import annotations

import argparse
import hashlib
import json
import random
from pathlib import Path

WEIGHTS = {"actor": 0.20, "action": 0.30, "object": 0.30, "condition": 0.20}


def pair_score(row: dict) -> float:
    if not row.get("typeMatch") or not row.get("actionMatch") or not row.get("objectMatch"):
        return 0.0
    return round(
        WEIGHTS["actor"] * float(row.get("actorScore", 0))
        + WEIGHTS["action"]
        + WEIGHTS["object"]
        + WEIGHTS["condition"] * float(row.get("conditionScore", 0)),
        6,
    )


def metrics(rows: list[dict], threshold: float) -> dict:
    tp = fp = fn = tn = 0
    for row in rows:
        predicted = pair_score(row) >= threshold
        expected = bool(row["label"])
        tp += int(predicted and expected)
        fp += int(predicted and not expected)
        fn += int(not predicted and expected)
        tn += int(not predicted and not expected)
    precision = tp / (tp + fp) if tp + fp else 0.0
    recall = tp / (tp + fn) if tp + fn else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {"tp": tp, "fp": fp, "fn": fn, "tn": tn, "precision": precision,
            "recall": recall, "f1": f1}


def stable_split(row: dict, index: int) -> str:
    key = str(row.get("id", index)).encode("utf-8")
    return "test" if int(hashlib.sha256(key).hexdigest()[:8], 16) % 5 == 0 else "train"


def confidence_interval(
    rows: list[dict], threshold: float, rounds: int, seed: int,
) -> dict[str, list[float]]:
    if not rows:
        return {name: [0.0, 0.0] for name in ("precision", "recall", "f1")}
    rng = random.Random(seed)
    values = {name: [] for name in ("precision", "recall", "f1")}
    for _ in range(rounds):
        sample = [rows[rng.randrange(len(rows))] for _ in rows]
        result = metrics(sample, threshold)
        for name in values:
            values[name].append(result[name])
    intervals = {}
    for name, samples in values.items():
        samples.sort()
        intervals[name] = [
            round(samples[int(0.025 * (len(samples) - 1))], 4),
            round(samples[int(0.975 * (len(samples) - 1))], 4),
        ]
    return intervals


def calibrate(rows: list[dict], rounds: int = 1000, seed: int = 42) -> dict:
    invalid = [row.get("id", index) for index, row in enumerate(rows)
               if not isinstance(row.get("label"), bool)]
    if invalid:
        raise ValueError(
            f"Calibration requires adjudicated boolean labels; invalid rows: {invalid[:20]}"
        )
    train = [row for index, row in enumerate(rows) if stable_split(row, index) == "train"]
    test = [row for index, row in enumerate(rows) if stable_split(row, index) == "test"]
    candidates = [round(value / 100, 2) for value in range(60, 96)]
    ranked = sorted(
        ((threshold, metrics(train, threshold)) for threshold in candidates),
        key=lambda item: (-item[1]["f1"], -item[1]["recall"], item[0]),
    )
    threshold = ranked[0][0] if ranked else 0.80
    holdout = metrics(test, threshold)
    per_type = {
        req_type: metrics(
            [row for row in test if row.get("requirementType", "Unknown") == req_type],
            threshold,
        )
        for req_type in sorted({row.get("requirementType", "Unknown") for row in test})
    }
    return {
        "sampleSize": len(rows),
        "trainSize": len(train),
        "holdoutSize": len(test),
        "weights": WEIGHTS,
        "baselineThreshold": 0.80,
        "recommendedThreshold": threshold,
        "holdout": {key: round(value, 4) if isinstance(value, float) else value
                    for key, value in holdout.items()},
        "bootstrap95CI": confidence_interval(test, threshold, rounds, seed),
        "perRequirementType": per_type,
        "warnings": ([] if len(rows) >= 200 else [
            "Dataset has fewer than 200 labelled pairs; treat the threshold as provisional."
        ]) + ([] if len(test) >= 40 else [
            "Holdout has fewer than 40 pairs; confidence intervals may be unstable."
        ]),
        "topCandidates": [
            {"threshold": value, "f1": round(result["f1"], 4),
             "precision": round(result["precision"], 4),
             "recall": round(result["recall"], 4)}
            for value, result in ranked[:5]
        ],
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--bootstrap-rounds", type=int, default=1000)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()
    rows = [
        json.loads(line) for line in args.dataset.read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]
    report = calibrate(rows, args.bootstrap_rounds, args.seed)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
