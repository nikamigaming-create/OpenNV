#!/usr/bin/env python3
"""Dispatch one classified whole-game actor review through NPC_ or CREA assembly."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from prepare_creature_review import (
    CREATURE_RECORD_TYPE,
    EXIT_DATA_ERROR,
    _load_json,
    default_archive_recipe_path,
    prepare_creature_review,
)
from prepare_humanoid_review import HUMANOID_RECORD_TYPE, prepare_humanoid_review


def prepare_actor_review(
    data_root: Path,
    contract_path: Path,
    cache_root: Path,
    archive_recipe_path: Path,
) -> dict[str, object]:
    contract = _load_json(contract_path)
    record_type = str(contract.get("assembly", {}).get("recordType", ""))
    if record_type == HUMANOID_RECORD_TYPE:
        return prepare_humanoid_review(data_root, contract_path, cache_root, archive_recipe_path)
    if record_type == CREATURE_RECORD_TYPE:
        return prepare_creature_review(data_root, contract_path, cache_root, archive_recipe_path)
    raise ValueError(f"Unsupported actor review record type: {record_type}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--review-contract", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--archive-recipe", type=Path, default=default_archive_recipe_path())
    args = parser.parse_args()
    try:
        scene = prepare_actor_review(
            args.data_root.resolve(),
            args.review_contract.resolve(),
            args.cache_root.resolve(),
            args.archive_recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_ACTOR_REVIEW_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_ACTOR_REVIEW "
        + json.dumps(
            {
                "manifest": scene["manifest"],
                "reviewKey": scene["reviewKey"],
                "recordType": scene["recordType"],
                "status": scene["status"],
                "coverage": scene["coverage"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
