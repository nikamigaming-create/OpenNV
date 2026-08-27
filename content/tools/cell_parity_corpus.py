#!/usr/bin/env python3
"""Build the effective whole-game CELL graph and pending review corpus."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

from cell_parity_records import (
    DOOR_RECORD_TYPE,
    LANDSCAPE_RECORD_TYPE,
    NAVIGATION_RECORD_TYPE,
    WORLDSPACE_RECORD_TYPE,
    build_cell_merge_state,
    resolve_linked_records,
)
from corpus_io import atomic_bytes, atomic_json, jsonl_bytes, output_descriptor
from plugin_stack import (
    FormKey,
    build_plugin_stack,
    file_sha256,
    load_order_indices as plugin_load_order_indices,
    parse_form_key,
    runtime_form_id,
)
from runtime_configuration import configured_recipe_path


RECIPE_SCHEMA = "opennv-cell-parity-corpus-recipe/v1"
CORPUS_SCHEMA = "opennv-cell-parity-corpus/v1"
MANIFEST_FILE_NAME = "manifest.json"
EXIT_DATA_ERROR = 2
OUTPUT_FILE_NAMES = {
    "cells": "cells.jsonl",
    "children": "cell-children.jsonl",
    "linkedRecords": "linked-records.jsonl",
    "implicitBases": "engine-implicit-bases.jsonl",
    "sourceAnomalies": "source-anomalies.jsonl",
    "portals": "portal-edges.jsonl",
    "cellReview": "cell-review.jsonl",
    "gaps": "relationship-gaps.jsonl",
}
PRODUCER_SOURCE_NAMES = (
    "cell_parity_corpus.py",
    "cell_parity_records.py",
    "cell_catalog.py",
    "corpus_io.py",
    "plugin_records.py",
    "plugin_stack.py",
)


def load_recipe(recipe_path: Path) -> dict[str, object]:
    document = json.loads(recipe_path.read_text(encoding="utf-8"))
    if document.get("schema") != RECIPE_SCHEMA:
        raise ValueError(f"Unexpected CELL parity corpus recipe schema: {recipe_path}")
    plugins = document.get("plugins")
    if not isinstance(plugins, list) or not plugins:
        raise ValueError("CELL parity corpus recipe must declare a non-empty plugin order")
    names = [str(row["file"]) for row in plugins]
    if len({name.casefold() for name in names}) != len(names):
        raise ValueError("CELL parity corpus recipe contains duplicate plugin names")
    implicit_bases = document.get("engineImplicitBases", [])
    if not isinstance(implicit_bases, list):
        raise ValueError("CELL parity corpus recipe engineImplicitBases must be a list")
    implicit_keys: list[str] = []
    for row in implicit_bases:
        if not isinstance(row, dict):
            raise ValueError("CELL parity corpus recipe has an invalid implicit base")
        for field in ("formKey", "recordType", "kind", "runtimeSemanticsStatus"):
            if not isinstance(row.get(field), str) or not str(row[field]).strip():
                raise ValueError(f"CELL implicit base has invalid {field}")
        parse_form_key(str(row["formKey"]))
        for field in ("requiredReferenceRecordTypes", "requiredReferenceSubrecords"):
            values = row.get(field)
            if (
                not isinstance(values, list)
                or not values
                or any(not isinstance(value, str) or not value for value in values)
                or len(values) != len(set(values))
            ):
                raise ValueError(f"CELL implicit base has invalid {field}")
        implicit_keys.append(str(row["formKey"]))
    if len(implicit_keys) != len(set(implicit_keys)):
        raise ValueError("CELL parity corpus recipe repeats an implicit base")
    source_anomalies = document.get("sourceAnomalies", [])
    if not isinstance(source_anomalies, list):
        raise ValueError("CELL parity corpus recipe sourceAnomalies must be a list")
    anomaly_keys: list[tuple[str, str, str]] = []
    for row in source_anomalies:
        if not isinstance(row, dict):
            raise ValueError("CELL parity corpus recipe has an invalid source record")
        for field in (
            "sourcePlugin",
            "rawFormId",
            "recordType",
            "recordFlags",
            "parentCellRawFormId",
            "recordDataSha256",
            "classification",
            "runtimeSemanticsStatus",
        ):
            if not isinstance(row.get(field), str) or not str(row[field]).strip():
                raise ValueError(f"CELL invalid source record has invalid {field}")
        anomaly_keys.append(
            (
                str(row["sourcePlugin"]),
                str(row["rawFormId"]),
                str(row["classification"]),
            )
        )
    if len(anomaly_keys) != len(set(anomaly_keys)):
        raise ValueError("CELL parity corpus recipe repeats a source anomaly")
    review = document.get("review")
    if not isinstance(review, dict):
        raise ValueError("CELL parity corpus recipe must declare review policy")
    for name in (
        "commonGates",
        "landscapeGates",
        "navigationGates",
        "portalGates",
        "interiorShots",
        "exteriorShots",
    ):
        values = review.get(name)
        if (
            not isinstance(values, list)
            or not values
            or any(not isinstance(value, str) or not value.strip() for value in values)
            or len(values) != len(set(values))
        ):
            raise ValueError(f"CELL parity corpus recipe has invalid review {name}")
    return document


def canonical_sha256(document: object) -> str:
    payload = json.dumps(document, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def add_requirement(
    requirements: dict[FormKey, Counter[str]],
    link: object,
    kind: str,
) -> None:
    if not isinstance(link, dict) or not link.get("key"):
        return
    requirements[parse_form_key(str(link["key"]))][kind] += 1


def implicit_base_rows(
    recipe: dict[str, object],
    load_order_indices: dict[str, int],
) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for configured in recipe.get("engineImplicitBases", []):
        key = parse_form_key(str(configured["formKey"]))
        rows.append(
            {
                **configured,
                "runtimeFormId": runtime_form_id(key, load_order_indices),
                "sourceKind": "engine-implicit-base-contract",
            }
        )
    return sorted(rows, key=lambda row: str(row["formKey"]))


def classify_source_anomalies(
    actual_rows: list[dict[str, object]],
    configured_rows: list[dict[str, object]],
) -> tuple[list[dict[str, object]], list[dict[str, object]], bool]:
    identity_fields = (
        "sourcePlugin",
        "rawFormId",
        "recordType",
        "recordFlags",
        "parentCellRawFormId",
        "recordDataSha256",
        "classification",
    )
    expected = {
        (
            str(row["sourcePlugin"]),
            str(row["rawFormId"]),
            str(row["classification"]),
        ): row
        for row in configured_rows
    }
    actual = {
        (
            str(row["sourcePlugin"]),
            str(row["rawFormId"]),
            str(row["classification"]),
        ): row
        for row in actual_rows
    }
    rows: list[dict[str, object]] = []
    gaps: list[dict[str, object]] = []
    if len(actual) != len(actual_rows):
        gaps.append(
            gap_row(
                "duplicate-source-anomaly",
                "source-anomaly-ledger",
            )
        )
    for key, observed in sorted(actual.items()):
        configured = expected.get(key)
        owner = f"{key[0]}@{key[1]}#{key[2]}"
        if configured is None:
            gaps.append(
                gap_row(
                    "unexpected-source-anomaly",
                    owner,
                    detail=json.dumps(observed, sort_keys=True, separators=(",", ":")),
                )
            )
            continue
        if any(observed.get(field) != configured.get(field) for field in identity_fields):
            gaps.append(
                gap_row(
                    "invalid-source-record-contract-mismatch",
                    owner,
                    detail=json.dumps(
                        {"configured": configured, "observed": observed},
                        sort_keys=True,
                        separators=(",", ":"),
                    ),
                )
            )
            continue
        rows.append(
            {
                **observed,
                "runtimeSemanticsStatus": configured["runtimeSemanticsStatus"],
                "accountingStatus": "exact-source-anomaly",
            }
        )
    for key, configured in sorted(expected.items()):
        if key not in actual:
            gaps.append(
                gap_row(
                    "configured-source-anomaly-not-found",
                    f"{key[0]}@{key[1]}#{key[2]}",
                    detail=json.dumps(configured, sort_keys=True, separators=(",", ":")),
                )
            )
    complete = len(rows) == len(actual) == len(expected)
    return rows, gaps, complete


def gap_row(
    reason: str,
    owner_form_key: str,
    *,
    target_form_key: str | None = None,
    detail: str | None = None,
) -> dict[str, object]:
    return {
        "reason": reason,
        "ownerFormKey": owner_form_key,
        "targetFormKey": target_form_key,
        "detail": detail,
    }


def build_portals(
    children: list[dict[str, object]],
    children_by_key: dict[str, dict[str, object]],
    linked_by_key: dict[str, dict[str, object]],
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    portals: list[dict[str, object]] = []
    gaps: list[dict[str, object]] = []
    for source in children:
        teleport = source.get("teleport")
        if not isinstance(teleport, dict):
            continue
        source_key = str(source["formKey"])
        source_cell = str(source["cell"]["key"])
        source_base = source.get("baseOrActor")
        destination_link = teleport.get("destinationReference")
        destination_key = (
            str(destination_link["key"])
            if isinstance(destination_link, dict) and destination_link.get("key")
            else ""
        )
        destination = children_by_key.get(destination_key)
        source_base_key = (
            str(source_base["key"])
            if isinstance(source_base, dict) and source_base.get("key")
            else ""
        )
        source_base_record = linked_by_key.get(source_base_key)
        if source_base_record is None:
            gaps.append(
                gap_row(
                    "portal-source-base-missing",
                    source_key,
                    target_form_key=source_base_key or None,
                )
            )
        elif source_base_record["recordType"] != DOOR_RECORD_TYPE:
            gaps.append(
                gap_row(
                    "portal-source-base-is-not-door",
                    source_key,
                    target_form_key=source_base_key,
                    detail=str(source_base_record["recordType"]),
                )
            )
        if destination is None:
            gaps.append(
                gap_row(
                    "portal-destination-missing",
                    source_key,
                    target_form_key=destination_key or None,
                )
            )
            destination_cell = None
            reciprocal_status = "destination-missing"
        else:
            destination_cell = destination["cell"]
            reverse = destination.get("teleport")
            reverse_link = reverse.get("destinationReference") if isinstance(reverse, dict) else None
            reverse_key = (
                str(reverse_link["key"])
                if isinstance(reverse_link, dict) and reverse_link.get("key")
                else ""
            )
            reciprocal_status = (
                "reciprocal"
                if reverse_key == source_key
                else "different-destination"
                if reverse_key
                else "not-authored"
            )
        portals.append(
            {
                "sourceReference": source["formKey"],
                "sourceRuntimeFormId": source["runtimeFormId"],
                "sourceCell": source["cell"],
                "sourceBase": source_base,
                "destinationReference": destination_link,
                "destinationCell": destination_cell,
                "destinationTransformGameUnits": teleport["destinationTransformGameUnits"],
                "reciprocalStatus": reciprocal_status,
                "runtimeContinuityStatus": "pending",
                "projectileContinuityStatus": "pending",
            }
        )
    return portals, gaps


def review_rows(
    cells: list[dict[str, object]],
    children: list[dict[str, object]],
    portals: list[dict[str, object]],
    review: dict[str, object],
) -> list[dict[str, object]]:
    child_counts: dict[str, Counter[str]] = defaultdict(Counter)
    for child in children:
        child_counts[str(child["cell"]["key"])][str(child["recordType"])] += 1
    portal_counts = Counter(str(portal["sourceCell"]["key"]) for portal in portals)
    rows = []
    for cell in cells:
        key = str(cell["formKey"])
        counts = child_counts[key]
        gates = list(review["commonGates"])
        if counts[LANDSCAPE_RECORD_TYPE] > 0:
            gates.extend(review["landscapeGates"])
        if counts[NAVIGATION_RECORD_TYPE] > 0:
            gates.extend(review["navigationGates"])
        if portal_counts[key] > 0:
            gates.extend(review["portalGates"])
        if len(gates) != len(set(gates)):
            raise ValueError(f"CELL review gates overlap for {key}")
        rows.append(
            {
                "cellFormKey": key,
                "cellRuntimeFormId": cell["runtimeFormId"],
                "editorId": cell["editorId"],
                "cellClass": "interior" if cell["interior"] else "exterior",
                "worldspace": cell["worldspace"],
                "coordinates": cell["coordinates"],
                "childRecordCounts": dict(sorted(counts.items())),
                "portalEdges": portal_counts[key],
                "requiredGates": gates,
                "requiredShots": list(
                    review["interiorShots"] if cell["interior"] else review["exteriorShots"]
                ),
                "gateStatus": {gate: "pending" for gate in gates},
                "retailEvidenceStatus": "pending",
                "godotEvidenceStatus": "pending",
                "matchedComparisonStatus": "pending",
                "humanReviewStatus": "pending",
                "lookedAt": False,
            }
        )
    return rows


def producer_sources() -> list[dict[str, object]]:
    tools_root = Path(__file__).resolve().parent
    return [
        {
            "file": f"tools/{name}",
            "sha256": file_sha256(tools_root / name),
        }
        for name in PRODUCER_SOURCE_NAMES
    ]


def build_corpus(
    data_root: Path,
    output_root: Path,
    recipe: dict[str, object],
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite CELL parity corpus: {output_root}")
    configured_names = [str(row["file"]) for row in recipe["plugins"]]
    contexts = build_plugin_stack(data_root, configured_names)
    load_order_indices = plugin_load_order_indices(contexts)
    state = build_cell_merge_state(contexts, load_order_indices)
    cells = sorted(state.cells.values(), key=lambda row: str(row["formKey"]))
    children = sorted(state.children.values(), key=lambda row: str(row["formKey"]))
    cells_by_key = {str(row["formKey"]): row for row in cells}
    children_by_key = {str(row["formKey"]): row for row in children}

    requirements: dict[FormKey, Counter[str]] = defaultdict(Counter)
    for cell in cells:
        add_requirement(requirements, cell.get("worldspace"), "cell-worldspace")
    for child in children:
        add_requirement(requirements, child.get("baseOrActor"), "cell-child-base")
    linked = resolve_linked_records(contexts, load_order_indices, requirements)
    linked_rows = sorted(linked.values(), key=lambda row: str(row["formKey"]))
    linked_by_key = {str(row["formKey"]): row for row in linked_rows}
    implicit_rows = implicit_base_rows(recipe, load_order_indices)
    implicit_by_key = {str(row["formKey"]): row for row in implicit_rows}
    if set(implicit_by_key) & set(linked_by_key):
        raise ValueError("Engine-implicit base collides with a plugin record")
    source_anomaly_rows, source_anomaly_gaps, source_anomalies_accounted = (
        classify_source_anomalies(
            state.source_anomalies,
            list(recipe.get("sourceAnomalies", [])),
        )
    )

    gaps = list(source_anomaly_gaps)
    for cell in cells:
        key = str(cell["formKey"])
        for parse_gap in cell["parseGaps"]:
            gaps.append(gap_row("cell-parse-gap", key, detail=str(parse_gap)))
        worldspace = cell.get("worldspace")
        if isinstance(worldspace, dict):
            target_key = str(worldspace["key"])
            target = linked_by_key.get(target_key)
            if target is None:
                gaps.append(gap_row("worldspace-missing", key, target_form_key=target_key))
            elif target["recordType"] != WORLDSPACE_RECORD_TYPE:
                gaps.append(
                    gap_row(
                        "worldspace-link-is-not-wrld",
                        key,
                        target_form_key=target_key,
                        detail=str(target["recordType"]),
                    )
                )
    for child in children:
        key = str(child["formKey"])
        cell_key = str(child["cell"]["key"])
        if cell_key not in cells_by_key:
            gaps.append(gap_row("parent-cell-missing", key, target_form_key=cell_key))
        for parse_gap in child["parseGaps"]:
            gaps.append(gap_row("cell-child-parse-gap", key, detail=str(parse_gap)))
        base = child.get("baseOrActor")
        if isinstance(base, dict):
            base_key = str(base["key"])
            implicit = implicit_by_key.get(base_key)
            if base_key not in linked_by_key and implicit is None:
                gaps.append(gap_row("cell-child-base-missing", key, target_form_key=base_key))
            elif implicit is not None:
                required_types = set(implicit["requiredReferenceRecordTypes"])
                required_subrecords = set(implicit["requiredReferenceSubrecords"])
                present_subrecords = set(child["subrecordSignatureCounts"])
                if child["recordType"] not in required_types:
                    gaps.append(
                        gap_row(
                            "engine-implicit-base-record-type-mismatch",
                            key,
                            target_form_key=base_key,
                            detail=str(child["recordType"]),
                        )
                    )
                if not required_subrecords <= present_subrecords:
                    gaps.append(
                        gap_row(
                            "engine-implicit-base-subrecords-missing",
                            key,
                            target_form_key=base_key,
                            detail=json.dumps(
                                sorted(required_subrecords - present_subrecords),
                                separators=(",", ":"),
                            ),
                        )
                    )
    portals, portal_gaps = build_portals(children, children_by_key, linked_by_key)
    gaps.extend(portal_gaps)
    portals.sort(key=lambda row: str(row["sourceReference"]))
    reviews = review_rows(cells, children, portals, recipe["review"])
    gaps.sort(key=lambda row: json.dumps(row, sort_keys=True, separators=(",", ":")))

    output_root.mkdir(parents=True)
    output_rows = {
        "cells": cells,
        "children": children,
        "linkedRecords": linked_rows,
        "implicitBases": implicit_rows,
        "sourceAnomalies": source_anomaly_rows,
        "portals": portals,
        "cellReview": reviews,
        "gaps": gaps,
    }
    descriptors: dict[str, dict[str, object]] = {}
    for name, rows in output_rows.items():
        path = output_root / OUTPUT_FILE_NAMES[name]
        atomic_bytes(path, jsonl_bytes(rows))
        descriptors[name] = output_descriptor(path, len(rows))

    child_type_counts = Counter(str(row["recordType"]) for row in children)
    raw_child_type_counts: Counter[str] = Counter()
    for counts in state.raw_counts.values():
        raw_child_type_counts.update(counts["cellChildren"])
    raw_cell_count = sum(int(counts["cells"]) for counts in state.raw_counts.values())
    status = (
        "inventory-built-with-relationship-gaps"
        if gaps
        else "inventory-complete-source-anomalies-accounted-implementation-review-pending"
        if source_anomaly_rows
        else "inventory-complete-implementation-review-pending"
    )
    manifest = {
        "schema": CORPUS_SCHEMA,
        "recipeId": recipe["id"],
        "recipeCanonicalSha256": canonical_sha256(recipe),
        "status": status,
        "scope": {
            "officialPluginsOnly": True,
            "modsIncluded": False,
            "everyEffectiveCellScheduled": (
                {str(row["cellFormKey"]) for row in reviews}
                == {str(row["formKey"]) for row in cells}
            ),
            "everyEffectiveCellChildAccounted": (
                sum(child_type_counts.values()) == len(children)
                and source_anomalies_accounted
            ),
            "sourceAnomaliesAreNotRuntimeSemanticsEvidence": True,
            "inventoryIsNotRuntimeOrParityEvidence": True,
        },
        "inputs": [
            {
                "file": context.name,
                "loadOrderIndex": context.load_order_index,
                "masters": list(context.masters),
                "bytes": context.bytes,
                "sha256": context.sha256,
                "rawCellGraphCounts": state.raw_counts[context.name],
            }
            for context in contexts
        ],
        "rawCounts": {
            "cells": raw_cell_count,
            "cellChildren": sum(raw_child_type_counts.values()),
            "cellChildrenByType": dict(sorted(raw_child_type_counts.items())),
        },
        "producerSources": producer_sources(),
        "effectiveCounts": {
            "cells": len(cells),
            "interiorCells": sum(bool(row["interior"]) for row in cells),
            "exteriorCells": sum(not bool(row["interior"]) for row in cells),
            "cellChildren": len(children),
            "cellChildrenByType": dict(sorted(child_type_counts.items())),
            "linkedRecords": len(linked_rows),
            "engineImplicitBases": len(implicit_rows),
            "sourceAnomalies": len(source_anomaly_rows),
            "portalEdges": len(portals),
            "cellReviewRows": len(reviews),
            "relationshipGaps": len(gaps),
            "undeclaredNamespaceCellGraphRecords": sum(
                row["classification"] == "undeclared-form-namespace"
                for row in state.source_anomalies
            ),
            "invalidCompressionChecksums": sum(
                state.invalid_compression_checksums.values()
            ),
        },
        "loadOrderMerge": {
            "overridesApplied": dict(sorted(state.override_counts.items())),
            "deletionsApplied": dict(sorted(state.deletion_counts.items())),
        },
        "evidencePolicy": {
            "allReviewStatusesStartPending": True,
            "matchedRetailAndGodotStateRequired": True,
            "unsupportedChildSemanticsRemainVisible": True,
            "engineImplicitBaseSemanticsRemainPending": True,
            "sourceAnomaliesRequireExactRecipeContracts": True,
            "noRuntimeOrParityClaimFromInventoryAlone": True,
        },
        "outputs": descriptors,
    }
    atomic_json(output_root / MANIFEST_FILE_NAME, manifest)
    return manifest


def default_recipe_path() -> Path:
    return configured_recipe_path("cellParityCorpus")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    args = parser.parse_args()
    try:
        recipe = load_recipe(args.recipe.resolve())
        manifest = build_corpus(
            args.data_root.resolve(),
            args.output_root.resolve(),
            recipe,
        )
    except Exception as error:
        print(f"OPENNV_CELL_PARITY_CORPUS_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_CELL_PARITY_CORPUS "
        + json.dumps(
            {
                "manifest": str((args.output_root / MANIFEST_FILE_NAME).resolve()),
                "status": manifest["status"],
                "effectiveCounts": manifest["effectiveCounts"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
