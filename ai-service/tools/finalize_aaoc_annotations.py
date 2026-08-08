"""Validate dual human annotation and emit calibration-ready JSONL."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.services.aaoc_matching_service import score_pair


class Item:
    def __init__(self, data: dict):
        self.__dict__.update(data)


def finalize(rows: list[dict], minimum: int = 200) -> tuple[list[dict], dict]:
    completed = []
    agreements = 0
    double_labeled = 0
    unresolved = []
    for row in rows:
        first = row.get("annotator1Label")
        second = row.get("annotator2Label")
        adjudicated = row.get("adjudicatedLabel")
        if isinstance(first, bool) and isinstance(second, bool):
            double_labeled += 1
            agreements += int(first == second)
        final = adjudicated if isinstance(adjudicated, bool) else (
            first if isinstance(first, bool) and first == second else None
        )
        if not isinstance(final, bool):
            unresolved.append(row["id"])
            continue
        ground = Item(row["groundTruth"])
        candidate = Item(row["candidate"])
        scored = score_pair(candidate, ground)
        components = scored.component_scores if scored else {
            "actor": 0.0, "action": 0.0, "object": 0.0, "condition": 0.0
        }
        completed.append({
            "id": row["id"],
            "label": final,
            "typeMatch": candidate.type == ground.type,
            "actionMatch": candidate.action.strip().lower() == ground.action.strip().lower(),
            "objectMatch": candidate.object.strip().lower() == ground.object.strip().lower(),
            "actorScore": components["actor"],
            "conditionScore": components["condition"],
            "requirementType": ground.type,
            "provenance": {
                "scenarioKey": row["scenarioKey"],
                "requirementId": row["requirementId"],
                "mutation": row["mutation"],
            },
        })
    report = {
        "totalRows": len(rows),
        "calibrationReady": len(completed),
        "minimumRequired": minimum,
        "quotaMet": len(completed) >= minimum,
        "doubleLabelAgreement": round(agreements / double_labeled, 4)
        if double_labeled else None,
        "unresolvedCount": len(unresolved),
        "unresolvedIds": unresolved[:50],
    }
    if len(completed) < minimum:
        raise ValueError(json.dumps(report, ensure_ascii=False))
    return completed, report


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--minimum", type=int, default=200)
    args = parser.parse_args()
    rows = [
        json.loads(line) for line in args.input.read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]
    completed, report = finalize(rows, args.minimum)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        "\n".join(json.dumps(row, ensure_ascii=False) for row in completed) + "\n",
        encoding="utf-8",
    )
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
