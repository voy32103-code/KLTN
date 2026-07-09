"""
Calibrate requirement-matching thresholds against annotated pilot data.

Default mode uses a deterministic lexical similarity so the calibration loop can
run in a clean environment. The report is still useful as a baseline sanity
check; production calibration can swap in the embedding scores from
ai-service/app/services/evaluate_service.py.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from collections import Counter, defaultdict
from collections.abc import Callable
from difflib import SequenceMatcher
from pathlib import Path
from typing import Iterable


REPO_ROOT = Path(__file__).resolve().parents[1]
AI_SERVICE_DIR = REPO_ROOT / "ai-service"
ANNOTATION_DIR = REPO_ROOT / "docs" / "pilot_dataset" / "annotations"
SCENARIO_CONFIG = REPO_ROOT / "ai-service" / "app" / "scenarios" / "university_course_registration.json"

THRESHOLD_SETS = {
    "strict": {"exact": 0.95, "semantic": 0.82, "partial": 0.65},
    "current": {"exact": 0.92, "semantic": 0.75, "partial": 0.55},
    "lenient": {"exact": 0.88, "semantic": 0.68, "partial": 0.45},
    "very_lenient": {"exact": 0.84, "semantic": 0.62, "partial": 0.38},
}

FULL_MATCHES = {"exact", "semantic"}
MATCH_ORDER = {"missed": 0, "partial": 1, "semantic": 2, "exact": 3}
SimilarityScorer = Callable[[str, str], float]


def normalize(text: str) -> str:
    return " ".join(re.findall(r"[a-z0-9]+", text.lower()))


def token_set(text: str) -> set[str]:
    return set(normalize(text).split())


def lexical_similarity(left: str, right: str) -> float:
    left_norm = normalize(left)
    right_norm = normalize(right)
    if not left_norm or not right_norm:
        return 0.0

    seq = SequenceMatcher(None, left_norm, right_norm).ratio()
    left_tokens = token_set(left)
    right_tokens = token_set(right)
    jaccard = len(left_tokens & right_tokens) / len(left_tokens | right_tokens)

    return round((0.65 * seq) + (0.35 * jaccard), 4)


def load_embedding_scorer(model_name: str | None = None) -> tuple[SimilarityScorer, str]:
    from sentence_transformers import SentenceTransformer
    import numpy as np

    resolved_model = model_name or os.getenv("EMBEDDING_MODEL", "all-MiniLM-L6-v2")
    embedder = SentenceTransformer(resolved_model)
    cache: dict[str, object] = {}

    def encode(text: str):
        if text not in cache:
            cache[text] = embedder.encode(text, normalize_embeddings=True)
        return cache[text]

    def score(left: str, right: str) -> float:
        return round(float(np.dot(encode(left), encode(right))), 4)

    return score, resolved_model


def classify(
    score: float,
    thresholds: dict[str, float],
    hidden_text: str | None = None,
    extracted_text: str | None = None,
    rubric_partial: bool = False,
) -> str:
    if score >= thresholds["exact"]:
        return "exact"
    if score >= thresholds["semantic"]:
        return "semantic"
    if score >= thresholds["partial"]:
        return "partial"
    if rubric_partial and rubric_partial_match(hidden_text, extracted_text, score, thresholds["partial"]):
        return "partial"
    return "missed"


def rubric_partial_match(
    hidden_text: str | None,
    extracted_text: str | None,
    score: float,
    partial_threshold: float,
) -> bool:
    if str(AI_SERVICE_DIR) not in sys.path:
        sys.path.insert(0, str(AI_SERVICE_DIR))
    from app.services.evaluation_policy import rubric_partial_match as policy_rubric_partial_match

    return policy_rubric_partial_match(hidden_text, extracted_text, score, partial_threshold)


def load_ground_truth() -> dict[str, str]:
    raw = json.loads(SCENARIO_CONFIG.read_text(encoding="utf-8"))
    return {item["id"]: item["text"] for item in raw["requirements"]}


def load_annotations() -> list[dict]:
    return [
        json.loads(path.read_text(encoding="utf-8"))
        for path in sorted(ANNOTATION_DIR.glob("*.annotation.json"))
    ]


def best_match_score(
    hidden_text: str,
    extracted_items: Iterable[dict],
    scorer: SimilarityScorer,
) -> tuple[float, str | None]:
    best_score = 0.0
    best_text = None

    for item in extracted_items:
        extracted_text = item["text"]
        score = scorer(extracted_text, hidden_text)
        if score > best_score:
            best_score = score
            best_text = extracted_text

    return best_score, best_text


def expected_labels(annotation: dict) -> dict[str, str]:
    return {
        item["hidden_id"]: item["expected_match_type"]
        for item in annotation["hidden_requirement_labels"]
    }


def evaluate_threshold_set(
    annotations: list[dict],
    ground_truth: dict[str, str],
    thresholds: dict[str, float],
    scorer: SimilarityScorer,
    rubric_partial: bool = False,
) -> dict:
    confusion: Counter[tuple[str, str]] = Counter()
    per_transcript = []
    false_positive = 0
    false_negative = 0
    total = 0
    correct = 0
    false_positive_details: list[dict] = []
    false_negative_details: list[dict] = []

    for annotation in annotations:
        transcript_id = annotation["transcript_id"]
        expected = expected_labels(annotation)
        extracted = annotation["expected_extracted_requirements"]
        transcript_correct = 0

        for hidden_id, expected_label in expected.items():
            hidden_text = ground_truth[hidden_id]
            score, matched_by = best_match_score(hidden_text, extracted, scorer)
            predicted = classify(score, thresholds, hidden_text, matched_by, rubric_partial)
            confusion[(expected_label, predicted)] += 1
            total += 1

            if predicted == expected_label:
                correct += 1
                transcript_correct += 1

            expected_positive = expected_label != "missed"
            predicted_positive = predicted != "missed"
            if predicted_positive and not expected_positive:
                false_positive += 1
                false_positive_details.append({
                    "transcript_id": transcript_id,
                    "hidden_id": hidden_id,
                    "expected": expected_label,
                    "predicted": predicted,
                    "score": round(score, 4),
                    "matched_by": matched_by,
                    "hidden_text": hidden_text,
                })
            if expected_positive and not predicted_positive:
                false_negative += 1
                false_negative_details.append({
                    "transcript_id": transcript_id,
                    "hidden_id": hidden_id,
                    "expected": expected_label,
                    "predicted": predicted,
                    "score": round(score, 4),
                    "matched_by": matched_by,
                    "hidden_text": hidden_text,
                })

        per_transcript.append({
            "transcript_id": transcript_id,
            "correct": transcript_correct,
            "total": len(expected),
            "accuracy": transcript_correct / len(expected),
        })

    return {
        "accuracy": correct / total if total else 0.0,
        "correct": correct,
        "total": total,
        "false_positive": false_positive,
        "false_negative": false_negative,
        "false_positive_details": false_positive_details,
        "false_negative_details": false_negative_details,
        "confusion": confusion,
        "per_transcript": per_transcript,
    }


def coverage_from_labels(labels: Iterable[str]) -> float:
    labels = list(labels)
    full = sum(1 for label in labels if label in FULL_MATCHES)
    partial = sum(1 for label in labels if label == "partial")
    return round(((full + 0.5 * partial) / len(labels) * 100), 2) if labels else 0.0


def report(
    scorer_name: str = "lexical",
    embedding_model: str | None = None,
    rubric_partial: bool = False,
) -> str:
    annotations = load_annotations()
    ground_truth = load_ground_truth()
    if scorer_name == "embedding":
        scorer, backend_label = load_embedding_scorer(embedding_model)
        backend_label = f"sentence-transformers embedding ({backend_label})"
    else:
        scorer = lexical_similarity
        backend_label = "lexical similarity baseline"
    if rubric_partial:
        backend_label += " + rubric partial matcher"

    results = {
        name: evaluate_threshold_set(annotations, ground_truth, thresholds, scorer, rubric_partial)
        for name, thresholds in THRESHOLD_SETS.items()
    }

    best_name, best_result = max(
        results.items(),
        key=lambda item: (item[1]["accuracy"], -item[1]["false_positive"], -item[1]["false_negative"]),
    )

    lines = [
        "# Threshold Calibration Snapshot",
        "",
        f"Annotations: {len(annotations)} transcripts, {len(annotations) * len(ground_truth)} hidden-requirement labels.",
        f"Scoring backend: {backend_label}.",
        "",
        "## Threshold Sets",
        "",
        "| Set | Exact | Semantic | Partial | Accuracy | False Positive | False Negative |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]

    for name, thresholds in THRESHOLD_SETS.items():
        result = results[name]
        lines.append(
            f"| `{name}` | {thresholds['exact']:.2f} | {thresholds['semantic']:.2f} | {thresholds['partial']:.2f} "
            f"| {result['accuracy']:.2%} | {result['false_positive']} | {result['false_negative']} |"
        )

    lines.extend([
        "",
        f"Recommended set for this snapshot: `{best_name}`.",
        "",
        "## Per-Transcript Accuracy",
        "",
        "| Transcript | Correct / Total | Accuracy | Expected Coverage |",
        "|---|---:|---:|---:|",
    ])

    for item in best_result["per_transcript"]:
        annotation = next(a for a in annotations if a["transcript_id"] == item["transcript_id"])
        expected = expected_labels(annotation)
        lines.append(
            f"| `{item['transcript_id']}` | {item['correct']} / {item['total']} "
            f"| {item['accuracy']:.2%} | {coverage_from_labels(expected.values()):.1f}% |"
        )

    confusion_by_expected: dict[str, Counter[str]] = defaultdict(Counter)
    for (expected, predicted), count in best_result["confusion"].items():
        confusion_by_expected[expected][predicted] += count

    lines.extend([
        "",
        "## Confusion Matrix For Recommended Set",
        "",
        "| Expected \\ Predicted | exact | semantic | partial | missed |",
        "|---|---:|---:|---:|---:|",
    ])

    for expected in ("exact", "semantic", "partial", "missed"):
        row = confusion_by_expected[expected]
        lines.append(
            f"| {expected} | {row['exact']} | {row['semantic']} | {row['partial']} | {row['missed']} |"
        )

    lines.extend([
        "",
        "## Notes",
        "",
        "- This script uses annotated expected extracted requirements, not raw LLM extraction output.",
        "- Lexical similarity is a reproducible baseline; embedding mode approximates the production evaluation scorer more closely.",
        "- False positive means the system predicted a non-missed match while the human label was `missed`.",
        "- False negative means the human label was exact/semantic/partial but the system predicted `missed`.",
    ])

    if best_result["false_negative_details"]:
        lines.extend([
            "",
            "## False Negative Examples For Recommended Set",
            "",
            "| Transcript | Hidden ID | Expected | Predicted | Score | Closest Extracted |",
            "|---|---|---|---|---:|---|",
        ])
        for item in best_result["false_negative_details"][:8]:
            matched_by = item["matched_by"] or "-"
            lines.append(
                f"| `{item['transcript_id']}` | `{item['hidden_id']}` | `{item['expected']}` | `{item['predicted']}` | {item['score']:.2f} | {matched_by} |"
            )

    if best_result["false_positive_details"]:
        lines.extend([
            "",
            "## False Positive Examples For Recommended Set",
            "",
            "| Transcript | Hidden ID | Expected | Predicted | Score | Closest Extracted |",
            "|---|---|---|---|---:|---|",
        ])
        for item in best_result["false_positive_details"][:8]:
            matched_by = item["matched_by"] or "-"
            lines.append(
                f"| `{item['transcript_id']}` | `{item['hidden_id']}` | `{item['expected']}` | `{item['predicted']}` | {item['score']:.2f} | {matched_by} |"
            )

    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--format", choices=["markdown"], default="markdown")
    parser.add_argument("--scorer", choices=["lexical", "embedding"], default="lexical")
    parser.add_argument("--embedding-model", default=None)
    parser.add_argument("--rubric-partial", action="store_true")
    args = parser.parse_args()

    if args.format == "markdown":
        print(report(args.scorer, args.embedding_model, args.rubric_partial), end="")


if __name__ == "__main__":
    main()
