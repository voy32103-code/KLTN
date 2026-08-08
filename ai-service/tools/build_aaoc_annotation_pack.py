"""Build a 200-pair AAOC annotation queue from versioned scenario sources.

Generated labels are proposals only. Calibration requires two human labels or an
explicit adjudicated label produced by finalize_aaoc_annotations.py.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

MODAL = re.compile(r"\b(must|should|can|needs? to|has to|phải|cần|có thể)\b", re.I)
CONDITION = re.compile(
    r"\b(if|when|before|after|during|until|up to|nếu|khi|trước|sau|trong khi|cho đến khi)\b",
    re.I,
)


def structured(requirement: dict) -> dict:
    text = str(requirement["text"]).strip()
    modal = MODAL.search(text)
    actor = text[:modal.start()].strip(" ,.") if modal else "System"
    predicate = text[modal.end():].strip(" ,.") if modal else text
    condition_match = CONDITION.search(predicate)
    condition = predicate[condition_match.start():].strip(" ,.") if condition_match else None
    core = predicate[:condition_match.start()].strip(" ,.") if condition_match else predicate
    words = core.split()
    action = words[0] if words else "support"
    obj = " ".join(words[1:]).strip(" ,.") or "requirement"
    gate = int(requirement.get("gate", 0))
    req_type = "NFR" if gate == 4 else "BR" if gate == 3 else "FR"
    return {
        "actor": actor[:160],
        "action": action[:160],
        "object": obj[:240],
        "condition": condition,
        "type": req_type,
        "text": text,
    }


def pair(
    pair_id: str,
    scenario_key: str,
    requirement_id: str,
    ground: dict,
    candidate: dict,
    proposed_label: bool,
    mutation: str,
) -> dict:
    return {
        "id": pair_id,
        "scenarioKey": scenario_key,
        "requirementId": requirement_id,
        "source": "scenario_config",
        "syntheticCandidate": True,
        "mutation": mutation,
        "groundTruth": ground,
        "candidate": candidate,
        "proposedLabel": proposed_label,
        "annotator1Label": None,
        "annotator2Label": None,
        "adjudicatedLabel": None,
        "label": None,
        "annotationStatus": "pending_human_review",
    }


def build(scenario_dir: Path, target: int) -> list[dict]:
    bases = []
    for path in sorted(scenario_dir.glob("*.json")):
        config = json.loads(path.read_text(encoding="utf-8"))
        for requirement in config.get("requirements", []):
            bases.append((
                config["scenario_key"],
                str(requirement.get("id", len(bases) + 1)),
                structured(requirement),
            ))
    if not bases:
        raise ValueError("No scenario requirements found.")

    result = []
    index = 0
    while len(result) < target:
        scenario_key, requirement_id, ground = bases[index % len(bases)]
        other = bases[(index + 7) % len(bases)][2]
        variants = [
            (dict(ground), True, "exact"),
            ({**ground, "actor": "Relevant user"}, True, "actor_paraphrase"),
            ({**ground, "condition": None}, True, "condition_omitted"),
            ({
                **ground,
                "actor": other["actor"],
                "condition": "under a contradictory business condition",
            }, False, "hard_negative_actor_condition"),
            ({**ground, "type": "NFR" if ground["type"] != "NFR" else "FR"},
             False, "negative_type"),
        ]
        for candidate, proposed, mutation in variants:
            if len(result) >= target:
                break
            result.append(pair(
                f"AAOC-{len(result) + 1:04d}",
                scenario_key,
                requirement_id,
                ground,
                candidate,
                proposed,
                mutation,
            ))
        index += 1
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--scenarios", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--target", type=int, default=200)
    args = parser.parse_args()
    if args.target < 200:
        raise ValueError("Target must be at least 200 pairs.")
    rows = build(args.scenarios, args.target)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        "\n".join(json.dumps(row, ensure_ascii=False) for row in rows) + "\n",
        encoding="utf-8",
    )
    print(json.dumps({
        "pairs": len(rows),
        "pendingHumanReview": len(rows),
        "output": str(args.output),
    }, ensure_ascii=False))


if __name__ == "__main__":
    main()
