"""Compute reproducible micro/macro Precision, Recall and F1 for pilot predictions."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

POSITIVE_LABELS = {"exact", "semantic", "partial", "matched"}


def labels_by_id(payload: dict) -> dict[str, str]:
    rows = payload.get("hidden_requirement_labels", payload.get("matches", []))
    result = {}
    for row in rows:
        key = str(row.get("hidden_id", row.get("hiddenId", ""))).strip()
        label = str(row.get("predicted_match_type", row.get(
            "expected_match_type", row.get("matchType", "missed")))).lower()
        if key:
            result[key] = label
    return result


def score(expected: dict[str, str], predicted: dict[str, str]) -> dict:
    tp = fp = fn = 0
    for requirement_id in set(expected) | set(predicted):
        gold = expected.get(requirement_id, "missed") in POSITIVE_LABELS
        guess = predicted.get(requirement_id, "missed") in POSITIVE_LABELS
        tp += int(gold and guess)
        fp += int(not gold and guess)
        fn += int(gold and not guess)
    precision = tp / (tp + fp) if tp + fp else 0.0
    recall = tp / (tp + fn) if tp + fn else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {"tp": tp, "fp": fp, "fn": fn, "precision": round(precision, 4),
            "recall": round(recall, 4), "f1": round(f1, 4)}


def run(
    annotation_dir: Path,
    prediction_dir: Path,
    prediction_suffix: str = ".prediction.json",
) -> dict:
    cases = []
    for annotation_path in sorted(annotation_dir.glob("*.annotation.json")):
        case_id = annotation_path.name.removesuffix(".annotation.json")
        prediction_path = prediction_dir / f"{case_id}{prediction_suffix}"
        if not prediction_path.exists():
            cases.append({"id": case_id, "status": "missing_prediction"})
            continue
        expected = labels_by_id(json.loads(annotation_path.read_text(encoding="utf-8")))
        predicted = labels_by_id(json.loads(prediction_path.read_text(encoding="utf-8")))
        cases.append({"id": case_id, "status": "scored", **score(expected, predicted)})
    scored = [case for case in cases if case["status"] == "scored"]
    tp = sum(case["tp"] for case in scored)
    fp = sum(case["fp"] for case in scored)
    fn = sum(case["fn"] for case in scored)
    precision = tp / (tp + fp) if tp + fp else 0.0
    recall = tp / (tp + fn) if tp + fn else 0.0
    micro_f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {
        "datasetCases": len(cases), "scoredCases": len(scored),
        "missingPredictions": len(cases) - len(scored),
        "micro": {"tp": tp, "fp": fp, "fn": fn, "precision": round(precision, 4),
                  "recall": round(recall, 4), "f1": round(micro_f1, 4)},
        "macro": {metric: round(sum(case[metric] for case in scored) / len(scored), 4)
                  if scored else 0.0 for metric in ("precision", "recall", "f1")},
        "cases": cases,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--annotations", type=Path, required=True)
    parser.add_argument("--predictions", type=Path, required=True)
    parser.add_argument("--prediction-suffix", default=".prediction.json")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = run(args.annotations, args.predictions, args.prediction_suffix)
    rendered = json.dumps(report, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)


if __name__ == "__main__":
    main()
