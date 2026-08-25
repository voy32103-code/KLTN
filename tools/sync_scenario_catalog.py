#!/usr/bin/env python3
"""Synchronize the locked AI evaluation catalog into the runtime catalog."""

from __future__ import annotations

import argparse
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIRECTORY = REPOSITORY_ROOT / "ai-service" / "app" / "scenarios"
TARGET_DIRECTORY = (
    REPOSITORY_ROOT / "backend" / "ReqSimulator.API" / "Data" / "ScenarioCatalog"
)


def catalog_files(directory: Path) -> dict[str, bytes]:
    return {path.name: path.read_bytes() for path in sorted(directory.glob("*.json"))}


def check_snapshot() -> int:
    source = catalog_files(SOURCE_DIRECTORY)
    target = catalog_files(TARGET_DIRECTORY)
    if source == target:
        print(f"Scenario catalog snapshot is current ({len(source)} files).")
        return 0

    missing = sorted(source.keys() - target.keys())
    changed = sorted(name for name in source.keys() & target.keys() if source[name] != target[name])
    if missing:
        print("Missing backend snapshots:", ", ".join(missing))
    if changed:
        print("Changed backend snapshots:", ", ".join(changed))
    # The runtime catalog may contain additional active scenarios that are not
    # part of the locked evaluation v1 bundle. Those files are intentional and
    # must not make the evaluation snapshot stale.
    if not missing and not changed:
        print(f"Evaluation catalog snapshot is current ({len(source)} locked files).")
        return 0
    print("Run: python tools/sync_scenario_catalog.py")
    return 1


def synchronize() -> int:
    source = catalog_files(SOURCE_DIRECTORY)
    TARGET_DIRECTORY.mkdir(parents=True, exist_ok=True)
    for name, content in source.items():
        (TARGET_DIRECTORY / name).write_bytes(content)
    print(f"Synchronized {len(source)} scenario files into the backend artifact.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="Fail when the snapshot is stale.")
    args = parser.parse_args()
    return check_snapshot() if args.check else synchronize()


if __name__ == "__main__":
    raise SystemExit(main())
