# ReqSimulator evaluation artifacts

This directory is the reproducibility boundary for quantitative claims in the thesis. It deliberately separates target values copied from the report from observed, reproducible results.

## Current evidence status

- Catalog: exactly 10 scenarios and 100 candidate ground-truth requirements.
- Provenance: the current catalog is synthetic and marked `provisional`.
- Human review: not completed. Each requirement has a row in `ground_truth_reviews_v1.csv`.
- F1 values 87.18% and 88.57%: registered as unverified targets, not measured results.
- Gating 90%: registered as an unverified target, not a measured result.
- Feedback A/B: protocol frozen, experiment not run.

Do not present a claim as an experimental result while `claimable` is `false` in `scenario_catalog_v1.lock.json`.

## Reproduce the catalog lock

From the repository root:

```powershell
python tools/sync_scenario_catalog.py --check
python tools/build_evaluation_artifacts.py --check
```

The lock records a SHA-256 for every scenario and evidence file. Any edit makes the check fail until a new version is intentionally locked.

## Review the candidate ground truth

1. Two reviewers independently inspect every requirement for correctness, atomicity, domain relevance, category, priority, reveal gate, and duplicate meaning.
2. Resolve disagreements and record the final reviewer identifier, UTC timestamp, status, and notes in `ground_truth_reviews_v1.csv`.
3. Set a scenario's `review_status` to `approved` only after all its rows are approved.
4. Rebuild the lock and keep the reviewed file with the experiment release.

Synthetic requirements are valid test fixtures, but they must not be described as requirements observed from real users.

## Evidence required for each metric

For extraction/matching F1, retain the locked catalog, raw anonymized interview input, raw model extraction output, final requirement-ID mapping, model name/version, prompt hash, temperature, scorer commit, and TP/FP/FN counts. Compute micro precision, recall, and F1 from saved IDs; do not copy a rounded percentage into the registry manually without the run manifest.

For gating accuracy, retain reviewer-authored turn-level cases with expected reveal IDs, actual reveal IDs, persona state, question type, and catalog hash. Report confusion counts and failures as well as the aggregate percentage.

For A/B feedback, follow `ab_feedback_protocol_v1.json`. Counterbalance display order and use independent human reviewers. A model judge may be exploratory evidence only.

## Score a saved run

Create a JSON input that follows `run_manifest_schema_v1.json`, copy the current `catalogSha256` from the lock, and run:

```powershell
python tools/score_evaluation_run.py path/to/raw-run.json --output path/to/scored-run.json
```

The scorer derives micro-F1 from requirement ID sets, exact-set gating accuracy from expected/actual reveals, and A/B preference with a 95% Wilson interval. The result records hashes of both the raw input and scorer. `claimEligible` remains false until all ground-truth rows are approved.

## Initial setup only

The review sheet is generated once and is never overwritten by the tool:

```powershell
python tools/build_evaluation_artifacts.py --initialize-reviews
python tools/build_evaluation_artifacts.py
```
