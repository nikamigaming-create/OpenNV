#!/usr/bin/env python3
"""Validate whole-game actor/creature inventory and review-ledger coverage."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from actor_parity_corpus import CORPUS_SCHEMA, MANIFEST_FILE_NAME
from plugin_stack import file_sha256


EXIT_VALIDATION_ERROR = 2


def read_jsonl(path: Path) -> list[dict[str, object]]:
    return [
        json.loads(line)
        for line in path.read_text(encoding="utf-8").splitlines()
    ]


def unique_rows(rows: list[dict[str, object]], field: str, label: str) -> set[str]:
    values = [str(row[field]) for row in rows]
    if len(values) != len(set(values)):
        raise ValueError(f"Actor parity corpus contains duplicate {label} values")
    return set(values)


def validate_corpus(root: Path) -> dict[str, int]:
    manifest_path = root / MANIFEST_FILE_NAME
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("schema") != CORPUS_SCHEMA:
        raise ValueError(f"Unexpected actor parity corpus schema: {manifest_path}")
    outputs = manifest["outputs"]
    documents: dict[str, list[dict[str, object]]] = {}
    for label, descriptor in outputs.items():
        path = root / str(descriptor["file"])
        if path.parent.resolve() != root.resolve() or not path.is_file():
            raise ValueError(f"Actor parity corpus output escapes or is missing: {path}")
        if path.stat().st_size != int(descriptor["bytes"]):
            raise ValueError(f"Actor parity corpus byte count mismatch: {path.name}")
        if file_sha256(path) != str(descriptor["sha256"]).lower():
            raise ValueError(f"Actor parity corpus hash mismatch: {path.name}")
        rows = read_jsonl(path)
        if len(rows) != int(descriptor["rows"]):
            raise ValueError(f"Actor parity corpus row count mismatch: {path.name}")
        documents[label] = rows

    bases = documents["bases"]
    placements = documents["placements"]
    appearance = documents["appearanceReview"]
    placement_review = documents["placementReview"]
    gaps = documents["gaps"]
    base_keys = unique_rows(bases, "formKey", "base FormKey")
    placement_keys = unique_rows(placements, "formKey", "placement FormKey")
    bases_by_key = {str(row["formKey"]): row for row in bases}
    placements_by_key = {str(row["formKey"]): row for row in placements}
    unique_rows(appearance, "reviewKey", "appearance review key")
    review_placement_keys = unique_rows(
        placement_review,
        "placementFormKey",
        "placement review key",
    )
    if gaps:
        raise ValueError(f"Actor parity corpus contains relationship gaps: {len(gaps)}")
    if any(row["templateResolutionStatus"] == "unresolved" for row in bases):
        raise ValueError("Actor parity corpus contains an unresolved template")
    if any(row["baseResolutionStatus"] != "resolved" for row in placements):
        raise ValueError("Actor parity corpus contains an unresolved placement")
    expected_variants = {
        (
            str(base["formKey"]),
            json.dumps(variant, sort_keys=True, separators=(",", ":")),
        )
        for base in bases
        for variant in base["appearanceVariants"]
    }
    scheduled_variants = {
        (
            str(row["baseFormKey"]),
            json.dumps(
                {
                    "categorySources": row["categorySources"],
                    "selectionPaths": row["templateSelectionPaths"],
                },
                sort_keys=True,
                separators=(",", ":"),
            ),
        )
        for row in appearance
    }
    if scheduled_variants != expected_variants:
        raise ValueError("Appearance ledger does not cover every template variant exactly once")
    if {str(row["baseFormKey"]) for row in appearance} != base_keys:
        raise ValueError("Appearance ledger does not cover every effective base")
    if review_placement_keys != placement_keys:
        raise ValueError("Placement ledger does not cover every effective reference")
    for row in appearance:
        base = bases_by_key[str(row["baseFormKey"])]
        if row["baseRuntimeFormId"] != base["runtimeFormId"]:
            raise ValueError("Appearance ledger base runtime FormID mismatch")
        expected_runtime_sources = {
            category: bases_by_key[str(source)]["runtimeFormId"]
            for category, source in row["categorySources"].items()
        }
        if row["categorySourceRuntimeFormIds"] != expected_runtime_sources:
            raise ValueError("Appearance ledger category runtime FormID mismatch")
    for row in placement_review:
        placement = placements_by_key[str(row["placementFormKey"])]
        if row["placementRuntimeFormId"] != placement["runtimeFormId"]:
            raise ValueError("Placement ledger runtime FormID mismatch")
        expected_candidates = [
            bases_by_key[str(key)]["runtimeFormId"]
            for key in row["candidateBaseFormKeys"]
        ]
        if row["candidateBaseRuntimeFormIds"] != expected_candidates:
            raise ValueError("Placement ledger candidate runtime FormID mismatch")

    counts = manifest["effectiveCounts"]
    expected_counts = {
        "allBases": len(bases),
        "allPlacements": len(placements),
        "appearanceReviewRows": len(appearance),
        "placementReviewRows": len(placement_review),
        "relationshipGaps": len(gaps),
    }
    for name, value in expected_counts.items():
        if int(counts[name]) != value:
            raise ValueError(f"Actor parity corpus manifest count mismatch: {name}")
    return expected_counts


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True)
    args = parser.parse_args()
    try:
        counts = validate_corpus(args.corpus_root.resolve())
    except Exception as error:
        print(f"OPENNV_ACTOR_PARITY_CORPUS_FAIL {error}", file=sys.stderr)
        return EXIT_VALIDATION_ERROR
    print(
        "OPENNV_ACTOR_PARITY_CORPUS_PASS "
        + " ".join(f"{name}={value}" for name, value in sorted(counts.items()))
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
