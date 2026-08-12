"""Create an immutable manifest for the versioned ReqSimulator pilot dataset.

This tool deliberately refuses to label the pilot as a dual-annotated final
dataset. It only records the exact files, session split, and checksums used by a
given evaluation run so that a later human-reviewed dataset can be compared to
it honestly.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", type=Path, required=True)
    parser.add_argument("--scenario", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", default="pilot-v1")
    args = parser.parse_args()

    dataset = args.dataset.resolve()
    transcripts = sorted((dataset / "transcripts").glob("*.json"))
    annotations = {path.name.removesuffix(".annotation.json"): path
                   for path in (dataset / "annotations").glob("*.annotation.json")}
    if not transcripts:
        raise ValueError("No transcript files found.")

    sessions = []
    for transcript in transcripts:
        data = json.loads(transcript.read_text(encoding="utf-8"))
        case_id = transcript.stem
        annotation = annotations.get(case_id)
        if annotation is None:
            raise ValueError(f"Missing annotation for {case_id}")
        sessions.append({
            "sessionId": data["transcript_id"],
            "studentId": None,
            "scenarioKey": data["scenario_key"],
            "transcript": str(transcript.relative_to(dataset)).replace("\\", "/"),
            "annotation": str(annotation.relative_to(dataset)).replace("\\", "/"),
            "transcriptSha256": sha256(transcript),
            "annotationSha256": sha256(annotation),
        })

    # Keep two complete sessions untouched for the holdout. There is no named
    # student identifier in this synthetic pilot, so non-overlap is checked at
    # session/transcript level only and explicitly declared in the manifest.
    calibration = [row["sessionId"] for row in sessions[:-2]]
    holdout = [row["sessionId"] for row in sessions[-2:]]
    aggregate_input = "\n".join(
        f"{row['sessionId']}:{row['transcriptSha256']}:{row['annotationSha256']}"
        for row in sessions
    ) + f"\nscenario:{sha256(args.scenario)}"
    manifest = {
        "datasetVersion": args.version,
        "createdAtUtc": datetime.now(timezone.utc).replace(microsecond=0).isoformat(),
        "purpose": "exploratory end-to-end pilot only",
        "dataOrigin": "synthetic pilot transcripts; no real student/participant records",
        "scenario": {
            "path": str(args.scenario.resolve()),
            "sha256": sha256(args.scenario),
        },
        "sessions": sessions,
        "split": {
            "unit": "session",
            "calibrationSessionIds": calibration,
            "holdoutSessionIds": holdout,
            "studentOverlapCheck": "not_applicable_no_student_ids_in_synthetic_pilot",
            "transcriptOverlap": False,
        },
        "exclusionCriteria": [
            "Exclude any transcript without a paired annotation file.",
            "Exclude real participant data without approved consent and de-identification.",
            "Exclude any case whose annotation changes after this manifest is written.",
        ],
        "annotationStatus": {
            "version": 1,
            "dualIndependentReview": False,
            "adjudication": False,
            "limitation": "Metrics from this lock are exploratory and are not final thesis claims.",
        },
        "datasetSha256": hashlib.sha256(aggregate_input.encode("utf-8")).hexdigest(),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output), "datasetVersion": args.version,
                      "sessions": len(sessions), "datasetSha256": manifest["datasetSha256"]},
                     ensure_ascii=False))


if __name__ == "__main__":
    main()
