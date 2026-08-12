"""Run raw LLM extraction followed by ReqSimulator matching on a locked dataset.

The output records fallback use, per-session predictions, a four-class confusion
matrix, and binary extraction/matching Precision/Recall/F1. It does not convert
single-review pilot labels into a final research result.
"""
from __future__ import annotations

import argparse
import asyncio
import json
import os
import platform
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from app.models.schemas import ChatMessage, EvaluateRequest, ExtractRequest, HiddenReq
from app.services.evaluate_service import evaluate
from app.services.extract_service import extract_requirements

LABELS = ("exact", "semantic", "partial", "missed")
POSITIVE = {"exact", "semantic", "partial"}


def binary_metrics(expected: dict[str, str], predicted: dict[str, str]) -> dict:
    tp = fp = fn = tn = 0
    for hidden_id in sorted(set(expected) | set(predicted)):
        actual = expected.get(hidden_id, "missed") in POSITIVE
        guess = predicted.get(hidden_id, "missed") in POSITIVE
        tp += int(actual and guess)
        fp += int(not actual and guess)
        fn += int(actual and not guess)
        tn += int(not actual and not guess)
    precision = tp / (tp + fp) if tp + fp else 0.0
    recall = tp / (tp + fn) if tp + fn else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {"tp": tp, "fp": fp, "fn": fn, "tn": tn,
            "precision": round(precision, 4), "recall": round(recall, 4), "f1": round(f1, 4)}


async def run_case(transcript_path: Path, annotation_path: Path, scenario: dict, model: str) -> dict:
    transcript = json.loads(transcript_path.read_text(encoding="utf-8"))
    annotation = json.loads(annotation_path.read_text(encoding="utf-8"))
    history = [ChatMessage(role=item["role"], content=item["content"],
                           timestamp=datetime(2026, 8, 9, tzinfo=timezone.utc))
               for item in transcript["messages"]]
    extraction = await extract_requirements(ExtractRequest(
        sessionId=transcript["transcript_id"], history=history, selectedModel=model,
    ))
    hidden = [HiddenReq(
        id=item["id"], text=item["text"], category=str(item.get("gate", "unknown")),
    ) for item in scenario["requirements"]]
    result = await evaluate(EvaluateRequest(
        extracted=extraction.requirements,
        hiddenRequirements=hidden,
        selectedModel=None,
        scenarioDescription=scenario.get("context"),
        feedbackVariant="A",
    ))
    expected = {item["hidden_id"]: item["expected_match_type"].lower()
                for item in annotation["hidden_requirement_labels"]}
    predicted = {item.hiddenId: item.matchType.lower() for item in result.matches}
    return {
        "id": transcript["transcript_id"],
        "scenarioKey": transcript["scenario_key"],
        "status": "scored",
        "isFallback": extraction.isFallback,
        "extractedRequirementCount": len(extraction.requirements),
        "coverageScore": result.coverageScore,
        "expectedLabels": expected,
        "predictedLabels": predicted,
        "matches": [match.model_dump() for match in result.matches],
        "metrics": binary_metrics(expected, predicted),
        "scoringPolicy": result.scoringPolicy.model_dump() if result.scoringPolicy else None,
    }


async def main_async(args) -> None:
    load_dotenv(ROOT / ".env")
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    scenario = json.loads(args.scenario.read_text(encoding="utf-8"))
    model = args.model or os.getenv("MODEL_NAME", "gemini-2.5-flash")
    if not (os.getenv("GEMINI_API_KEY") or os.getenv("GEMINI_API_KEYS") or os.getenv("GROQ_API_KEY")):
        raise RuntimeError("No LLM provider is configured; refusing to substitute fallback output for a raw LLM run.")

    session_map = {row["sessionId"]: row for row in manifest["sessions"]}
    dataset_root = args.manifest.parent
    cases = []
    for session_id in manifest["split"]["holdoutSessionIds"]:
        row = session_map[session_id]
        cases.append(await run_case(
            dataset_root / row["transcript"], dataset_root / row["annotation"], scenario, model,
        ))

    aggregate_expected, aggregate_predicted = {}, {}
    confusion = {actual: {predicted: 0 for predicted in LABELS} for actual in LABELS}
    fallback_cases = []
    for case in cases:
        if case["isFallback"]:
            fallback_cases.append(case["id"])
        for hidden_id, actual in case["expectedLabels"].items():
            predicted = case["predictedLabels"].get(hidden_id, "missed")
            confusion[actual][predicted] += 1
            aggregate_expected[f"{case['id']}:{hidden_id}"] = actual
            aggregate_predicted[f"{case['id']}:{hidden_id}"] = predicted

    report = {
        "runAtUtc": datetime.now(timezone.utc).replace(microsecond=0).isoformat(),
        "kind": "exploratory_locked_pilot_end_to_end",
        "datasetVersion": manifest["datasetVersion"],
        "datasetSha256": manifest["datasetSha256"],
        "holdoutSessions": manifest["split"]["holdoutSessionIds"],
        "model": model,
        "python": platform.python_version(),
        "rawLlmExtractionRequired": True,
        "fallbackCases": fallback_cases,
        "fallbackFree": not fallback_cases,
        "binaryMetrics": binary_metrics(aggregate_expected, aggregate_predicted),
        "fourClassConfusionMatrix": confusion,
        "caseCount": len(cases),
        "cases": cases,
        "limitations": [
            "Pilot contains synthetic transcripts from one scenario only.",
            "Reference labels are annotation version 1 and have not undergone dual independent review/adjudication.",
            "Do not use this run as a final generalization or A/B effectiveness claim.",
        ],
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: report[key] for key in (
        "kind", "datasetVersion", "datasetSha256", "holdoutSessions", "model",
        "fallbackFree", "binaryMetrics", "fourClassConfusionMatrix", "caseCount",
    )}, ensure_ascii=False, indent=2))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--scenario", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--model")
    args = parser.parse_args()
    asyncio.run(main_async(args))


if __name__ == "__main__":
    main()
