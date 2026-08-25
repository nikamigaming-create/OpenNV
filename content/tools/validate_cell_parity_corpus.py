#!/usr/bin/env python3
"""Validate whole-game CELL inventory, graph closure, and review coverage."""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

from cell_parity_corpus import (
    CORPUS_SCHEMA,
    MANIFEST_FILE_NAME,
    OUTPUT_FILE_NAMES,
    canonical_sha256,
    default_recipe_path,
)
from corpus_io import read_jsonl
from plugin_stack import file_sha256


EXIT_VALIDATION_ERROR = 2
ACTOR_PLACEMENT_RECORD_TYPES = frozenset({"ACHR", "ACRE"})
DELETION_SOURCE_AND_TARGET_RECORD_COUNT = 2


def unique_rows(rows: list[dict[str, object]], field: str, label: str) -> set[str]:
    values = [str(row[field]) for row in rows]
    if len(values) != len(set(values)):
        raise ValueError(f"CELL parity corpus contains duplicate {label} values")
    return set(values)


def read_outputs(
    root: Path,
    outputs: dict[str, object],
) -> dict[str, list[dict[str, object]]]:
    if set(outputs) != set(OUTPUT_FILE_NAMES):
        raise ValueError("CELL parity corpus output set is incomplete")
    documents: dict[str, list[dict[str, object]]] = {}
    for label, descriptor_source in outputs.items():
        if not isinstance(descriptor_source, dict):
            raise ValueError(f"CELL parity corpus output descriptor is invalid: {label}")
        path = root / str(descriptor_source["file"])
        if path.parent.resolve() != root.resolve() or not path.is_file():
            raise ValueError(f"CELL parity corpus output escapes or is missing: {path}")
        if path.stat().st_size != int(descriptor_source["bytes"]):
            raise ValueError(f"CELL parity corpus byte count mismatch: {path.name}")
        if file_sha256(path) != str(descriptor_source["sha256"]).lower():
            raise ValueError(f"CELL parity corpus hash mismatch: {path.name}")
        rows = read_jsonl(path)
        if len(rows) != int(descriptor_source["rows"]):
            raise ValueError(f"CELL parity corpus row count mismatch: {path.name}")
        documents[label] = rows
    return documents


def validate_producer_sources(manifest: dict[str, object]) -> None:
    tools_root = Path(__file__).resolve().parent
    sources = manifest.get("producerSources")
    if not isinstance(sources, list) or not sources:
        raise ValueError("CELL parity corpus has no producer-source ledger")
    names = [str(row["file"]) for row in sources]
    if len(names) != len(set(names)):
        raise ValueError("CELL parity corpus repeats a producer source")
    for row in sources:
        relative = Path(str(row["file"]))
        if len(relative.parts) != 2 or relative.parts[0] != "tools":
            raise ValueError(f"CELL parity producer source escapes tools: {relative}")
        path = tools_root / relative.name
        if not path.is_file() or file_sha256(path) != str(row["sha256"]).lower():
            raise ValueError(f"CELL parity producer source changed: {relative}")


def validate_actor_join(
    children: list[dict[str, object]],
    actor_corpus_root: Path,
) -> int:
    from validate_actor_parity_corpus import validate_corpus as validate_actor_corpus

    validate_actor_corpus(actor_corpus_root)
    actor_manifest = json.loads(
        (actor_corpus_root / MANIFEST_FILE_NAME).read_text(encoding="utf-8")
    )
    placement_descriptor = actor_manifest["outputs"]["placements"]
    placements = read_jsonl(actor_corpus_root / str(placement_descriptor["file"]))
    actor_children = {
        str(row["formKey"]): row
        for row in children
        if row["recordType"] in ACTOR_PLACEMENT_RECORD_TYPES
    }
    actor_placements = {str(row["formKey"]): row for row in placements}
    if set(actor_children) != set(actor_placements):
        raise ValueError("CELL children differ from the actor placement corpus")
    for key, placement in actor_placements.items():
        child = actor_children[key]
        if placement["cell"] != child["cell"]:
            raise ValueError(f"Actor placement CELL differs for {key}")
        if placement["baseOrList"] != child["baseOrActor"]:
            raise ValueError(f"Actor placement base differs for {key}")
    return len(actor_placements)


def validate_corpus(
    root: Path,
    *,
    recipe_path: Path | None = None,
    actor_corpus_root: Path | None = None,
) -> dict[str, int]:
    manifest_path = root / MANIFEST_FILE_NAME
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("schema") != CORPUS_SCHEMA:
        raise ValueError(f"Unexpected CELL parity corpus schema: {manifest_path}")
    if recipe_path is not None:
        recipe = json.loads(recipe_path.read_text(encoding="utf-8"))
        if canonical_sha256(recipe) != manifest.get("recipeCanonicalSha256"):
            raise ValueError("CELL parity corpus recipe changed")
    validate_producer_sources(manifest)
    documents = read_outputs(root, manifest["outputs"])
    cells = documents["cells"]
    children = documents["children"]
    linked = documents["linkedRecords"]
    implicit_bases = documents["implicitBases"]
    source_anomalies = documents["sourceAnomalies"]
    portals = documents["portals"]
    reviews = documents["cellReview"]
    gaps = documents["gaps"]
    if gaps:
        raise ValueError(f"CELL parity corpus contains relationship gaps: {len(gaps)}")

    cell_keys = unique_rows(cells, "formKey", "CELL FormKey")
    child_keys = unique_rows(children, "formKey", "child FormKey")
    linked_keys = unique_rows(linked, "formKey", "linked-record FormKey")
    implicit_keys = unique_rows(implicit_bases, "formKey", "implicit-base FormKey")
    review_keys = unique_rows(reviews, "cellFormKey", "CELL review key")
    portal_source_keys = unique_rows(portals, "sourceReference", "portal source")
    if review_keys != cell_keys:
        raise ValueError("CELL review ledger does not cover every effective CELL")
    if implicit_keys & linked_keys:
        raise ValueError("CELL implicit base collides with a linked plugin record")

    anomaly_keys = [
        (
            str(row["sourcePlugin"]),
            str(row["rawFormId"]),
            str(row["classification"]),
        )
        for row in source_anomalies
    ]
    if len(anomaly_keys) != len(set(anomaly_keys)):
        raise ValueError("CELL parity corpus repeats a source anomaly")
    if any(
        row.get("accountingStatus") != "exact-source-anomaly"
        or row.get("runtimeSemanticsStatus") != "pending"
        for row in source_anomalies
    ):
        raise ValueError("CELL source anomaly was not kept fail-closed")

    implicit_by_key = {str(row["formKey"]): row for row in implicit_bases}
    if any(
        row.get("sourceKind") != "engine-implicit-base-contract"
        or row.get("runtimeSemanticsStatus") != "pending"
        for row in implicit_bases
    ):
        raise ValueError("CELL implicit-base semantics were promoted without evidence")

    cells_by_key = {str(row["formKey"]): row for row in cells}
    children_by_key = {str(row["formKey"]): row for row in children}
    linked_by_key = {str(row["formKey"]): row for row in linked}
    for cell in cells:
        worldspace = cell.get("worldspace")
        if isinstance(worldspace, dict):
            key = str(worldspace["key"])
            if key not in linked_keys or linked_by_key[key]["recordType"] != "WRLD":
                raise ValueError(f"CELL worldspace is unresolved: {cell['formKey']}")
    for child in children:
        cell_key = str(child["cell"]["key"])
        if cell_key not in cell_keys:
            raise ValueError(f"CELL child has no effective parent: {child['formKey']}")
        base = child.get("baseOrActor")
        if isinstance(base, dict):
            base_key = str(base["key"])
            if base_key not in linked_keys and base_key not in implicit_keys:
                raise ValueError(f"CELL child has no effective base: {child['formKey']}")
            implicit = implicit_by_key.get(base_key)
            if implicit is not None:
                if child["recordType"] not in implicit["requiredReferenceRecordTypes"]:
                    raise ValueError(f"CELL implicit-base record type differs: {child['formKey']}")
                required = set(implicit["requiredReferenceSubrecords"])
                if not required <= set(child["subrecordSignatureCounts"]):
                    raise ValueError(f"CELL implicit-base subrecords differ: {child['formKey']}")
        if child.get("teleport") is not None and child["formKey"] not in portal_source_keys:
            raise ValueError(f"CELL child teleport has no portal edge: {child['formKey']}")
    for portal in portals:
        source_key = str(portal["sourceReference"])
        destination_key = str(portal["destinationReference"]["key"])
        source = children_by_key[source_key]
        destination = children_by_key.get(destination_key)
        if destination is None:
            raise ValueError(f"Portal destination is unresolved: {source_key}")
        base_key = str(source["baseOrActor"]["key"])
        if linked_by_key[base_key]["recordType"] != "DOOR":
            raise ValueError(f"Portal source base is not DOOR: {source_key}")
        if portal["sourceCell"] != source["cell"] or portal["destinationCell"] != destination["cell"]:
            raise ValueError(f"Portal CELL relation differs: {source_key}")

    for review in reviews:
        gates = list(review["requiredGates"])
        gate_status = review["gateStatus"]
        if set(gates) != set(gate_status) or any(value != "pending" for value in gate_status.values()):
            raise ValueError(f"CELL review gate status is not pending: {review['cellFormKey']}")
        if (
            review["retailEvidenceStatus"] != "pending"
            or review["godotEvidenceStatus"] != "pending"
            or review["matchedComparisonStatus"] != "pending"
            or review["humanReviewStatus"] != "pending"
            or bool(review["lookedAt"])
        ):
            raise ValueError(f"CELL review was promoted without evidence: {review['cellFormKey']}")

    raw_by_type: Counter[str] = Counter()
    for plugin in manifest["inputs"]:
        plugin_counts = plugin["rawCellGraphCounts"]
        raw_by_type["CELL"] += int(plugin_counts["cells"])
        raw_by_type.update(
            {
                str(record_type): int(value)
                for record_type, value in plugin_counts["cellChildren"].items()
            }
        )
    manifest_raw = manifest["rawCounts"]
    raw_children = raw_by_type.copy()
    del raw_children["CELL"]
    if (
        int(manifest_raw["cells"]) != raw_by_type["CELL"]
        or int(manifest_raw["cellChildren"]) != sum(raw_children.values())
        or {
            str(record_type): int(value)
            for record_type, value in manifest_raw["cellChildrenByType"].items()
        }
        != dict(sorted(raw_children.items()))
    ):
        raise ValueError("CELL raw source counts do not reconcile with plugin inputs")

    effective_by_type = Counter(str(row["recordType"]) for row in children)
    effective_by_type["CELL"] = len(cells)
    merge = manifest["loadOrderMerge"]
    overrides = Counter(
        {str(record_type): int(value) for record_type, value in merge["overridesApplied"].items()}
    )
    deletions = Counter(
        {str(record_type): int(value) for record_type, value in merge["deletionsApplied"].items()}
    )
    excluded = Counter(
        str(row["recordType"])
        for row in source_anomalies
        if row["classification"] == "undeclared-form-namespace"
    )
    for record_type in sorted(raw_by_type | effective_by_type | overrides | deletions | excluded):
        expected_effective = (
            raw_by_type[record_type]
            - overrides[record_type]
            - DELETION_SOURCE_AND_TARGET_RECORD_COUNT * deletions[record_type]
            - excluded[record_type]
        )
        if effective_by_type[record_type] != expected_effective:
            raise ValueError(f"CELL raw/effective conservation failed for {record_type}")

    counts = manifest["effectiveCounts"]
    expected_counts = {
        "cells": len(cells),
        "cellChildren": len(children),
        "linkedRecords": len(linked),
        "engineImplicitBases": len(implicit_bases),
        "sourceAnomalies": len(source_anomalies),
        "portalEdges": len(portals),
        "cellReviewRows": len(reviews),
        "relationshipGaps": len(gaps),
    }
    for name, value in expected_counts.items():
        if int(counts[name]) != value:
            raise ValueError(f"CELL parity corpus manifest count mismatch: {name}")
    source_anomaly_counts = {
        classification: sum(
            row["classification"] == classification for row in source_anomalies
        )
        for classification in (
            "undeclared-form-namespace",
            "invalid-compression-checksum",
        )
    }
    if (
        int(counts["undeclaredNamespaceCellGraphRecords"])
        != source_anomaly_counts["undeclared-form-namespace"]
        or int(counts["invalidCompressionChecksums"])
        != source_anomaly_counts["invalid-compression-checksum"]
    ):
        raise ValueError("CELL source-anomaly accounting differs")
    expected_status = (
        "inventory-complete-source-anomalies-accounted-implementation-review-pending"
        if source_anomalies
        else "inventory-complete-implementation-review-pending"
    )
    if manifest.get("status") != expected_status:
        raise ValueError("CELL parity corpus status differs from its accounted inventory")
    if actor_corpus_root is not None:
        expected_counts["actorPlacementJoin"] = validate_actor_join(
            children,
            actor_corpus_root,
        )
    if set(children_by_key) != child_keys or set(cells_by_key) != cell_keys:
        raise ValueError("CELL parity corpus key index differs")
    return expected_counts


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    parser.add_argument("--actor-corpus-root", type=Path)
    args = parser.parse_args()
    try:
        counts = validate_corpus(
            args.corpus_root.resolve(),
            recipe_path=args.recipe.resolve(),
            actor_corpus_root=(
                args.actor_corpus_root.resolve() if args.actor_corpus_root else None
            ),
        )
    except Exception as error:
        print(f"OPENNV_CELL_PARITY_CORPUS_FAIL {error}", file=sys.stderr)
        return EXIT_VALIDATION_ERROR
    print(
        "OPENNV_CELL_PARITY_CORPUS_PASS "
        + " ".join(f"{name}={value}" for name, value in sorted(counts.items()))
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
