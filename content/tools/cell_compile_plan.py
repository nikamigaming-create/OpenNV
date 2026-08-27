#!/usr/bin/env python3
"""Partition every validated CELL into a fail-closed owned-data compile plan."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

from cell_parity_corpus import CORPUS_SCHEMA as CELL_CORPUS_SCHEMA
from cell_parity_corpus import MANIFEST_FILE_NAME as CELL_MANIFEST_FILE_NAME
from corpus_io import atomic_bytes, atomic_json, jsonl_bytes, output_descriptor, read_jsonl
from plugin_stack import (
    FORM_ID_OBJECT_BITS,
    FORM_ID_OBJECT_HEX_CHARACTERS,
    FORM_ID_OBJECT_MASK,
    FORM_ID_RADIX,
    file_sha256,
)
from validate_cell_parity_corpus import validate_corpus as validate_cell_corpus
from runtime_configuration import configured_recipe_path


RECIPE_SCHEMA = "opennv-cell-compile-plan-recipe/v1"
PLAN_SCHEMA = "opennv-cell-compile-plan/v1"
MANIFEST_FILE_NAME = "manifest.json"
PARTITION_DIRECTORY_NAME = "jobs"
CAPABILITIES_FILE_NAME = "capabilities.jsonl"
CAPABILITY_SETS_FILE_NAME = "capability-sets.jsonl"
PARTITIONS_FILE_NAME = "partitions.jsonl"
PASS_STATUS = "pass"
PENDING_STATUS = "pending"
JOB_PENDING_STATUS = "scheduled-capabilities-pending"
JOB_READY_STATUS = "ready-for-owned-data-compilation"
COMPILE_OUTPUT_ABSENT_STATUS = "not-built"
EXIT_DATA_ERROR = 2
INTERIOR_PARTITION_POLICY = "source-plugin"
EXTERIOR_PARTITION_POLICY = "worldspace-form-key"
FILE_IDENTITY_POLICY = "full-sha256-of-partition-key"
PRODUCER_SOURCE_NAMES = (
    "cell_compile_plan.py",
    "cell_parity_corpus.py",
    "corpus_io.py",
    "plugin_stack.py",
    "validate_cell_parity_corpus.py",
)


def canonical_sha256(document: object) -> str:
    payload = json.dumps(document, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def load_recipe(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != RECIPE_SCHEMA:
        raise ValueError(f"Unexpected CELL compile-plan recipe schema: {path}")
    if document.get("sourceCorpusSchema") != CELL_CORPUS_SCHEMA:
        raise ValueError("CELL compile-plan recipe source schema differs")
    partition = document.get("partitionPolicy")
    if not isinstance(partition, dict) or partition != {
        "interior": INTERIOR_PARTITION_POLICY,
        "exterior": EXTERIOR_PARTITION_POLICY,
        "fileIdentity": FILE_IDENTITY_POLICY,
    }:
        raise ValueError("CELL compile-plan partition policy is unsupported")
    families = document.get("capabilityFamilies")
    if (
        not isinstance(families, list)
        or not families
        or any(not isinstance(value, str) or not value for value in families)
        or families != sorted(set(families))
    ):
        raise ValueError("CELL compile-plan capability families are invalid")
    stages = document.get("stages")
    if not isinstance(stages, list) or not stages:
        raise ValueError("CELL compile-plan stages are missing")
    stage_ids: list[str] = []
    for stage in stages:
        if not isinstance(stage, dict) or set(stage) != {"id", "initialStatus"}:
            raise ValueError("CELL compile-plan stage is invalid")
        stage_id = str(stage["id"])
        status = str(stage["initialStatus"])
        if not stage_id or status not in {PASS_STATUS, PENDING_STATUS}:
            raise ValueError("CELL compile-plan stage status is invalid")
        stage_ids.append(stage_id)
    if len(stage_ids) != len(set(stage_ids)):
        raise ValueError("CELL compile-plan stages are duplicated")
    if document.get("planningGateStage") not in stage_ids:
        raise ValueError("CELL compile-plan gate stage is not declared")
    return document


def partition_key(cell: dict[str, object]) -> str:
    if bool(cell["interior"]):
        return f"interior:{cell['sourcePlugin']}"
    worldspace = cell.get("worldspace")
    if not isinstance(worldspace, dict) or not worldspace.get("key"):
        raise ValueError(f"Exterior CELL has no worldspace: {cell['formKey']}")
    return f"exterior:{worldspace['key']}"


def partition_file_name(key: str) -> str:
    digest = hashlib.sha256(key.encode("utf-8")).hexdigest()
    return f"{digest}.jsonl"


def capability_key(family: str, subject: str) -> str:
    return f"{family}:{subject}"


def capability_set_id(keys: set[str] | list[str]) -> str:
    return canonical_sha256(sorted(keys))


def stage_statuses(recipe: dict[str, object]) -> dict[str, str]:
    return {
        str(stage["id"]): str(stage["initialStatus"])
        for stage in recipe["stages"]
    }


def job_status(recipe: dict[str, object]) -> str:
    statuses = stage_statuses(recipe)
    return (
        JOB_READY_STATUS
        if statuses[str(recipe["planningGateStage"])] == PASS_STATUS
        else JOB_PENDING_STATUS
    )


def stable_parent_cell_key(
    anomaly: dict[str, object],
    plugin_inputs: dict[str, dict[str, object]],
) -> str:
    source_plugin = str(anomaly["sourcePlugin"])
    plugin = plugin_inputs[source_plugin.casefold()]
    raw_text = anomaly.get("parentCellRawFormId")
    if not isinstance(raw_text, str) or not raw_text:
        raise ValueError(f"Source anomaly has no parent CELL: {source_plugin}")
    raw_form_id = int(raw_text, FORM_ID_RADIX)
    local_index = raw_form_id >> FORM_ID_OBJECT_BITS
    namespaces = (*plugin["masters"], str(plugin["file"]))
    if local_index >= len(namespaces):
        raise ValueError(f"Source anomaly parent CELL has undeclared namespace: {raw_text}")
    return (
        f"{namespaces[local_index]}:"
        f"{raw_form_id & FORM_ID_OBJECT_MASK:0{FORM_ID_OBJECT_HEX_CHARACTERS}x}"
    )


def cell_capabilities(cell: dict[str, object]) -> set[str]:
    classification = "interior" if cell["interior"] else "exterior"
    keys = {capability_key("cell-class", classification)}
    keys.update(
        capability_key("cell-subrecord", signature)
        for signature in cell["subrecordSignatureCounts"]
    )
    if isinstance(cell.get("worldspace"), dict):
        keys.add(capability_key("relationship", "worldspace"))
    return keys


def add_child_capabilities(
    keys: set[str],
    child: dict[str, object],
    linked_by_key: dict[str, dict[str, object]],
    implicit_by_key: dict[str, dict[str, object]],
) -> None:
    record_type = str(child["recordType"])
    keys.add(capability_key("child-record", record_type))
    keys.update(
        capability_key("child-subrecord", f"{record_type}.{signature}")
        for signature in child["subrecordSignatureCounts"]
    )
    base = child.get("baseOrActor")
    if isinstance(base, dict):
        base_key = str(base["key"])
        linked = linked_by_key.get(base_key)
        implicit = implicit_by_key.get(base_key)
        target = linked if linked is not None else implicit
        if target is None:
            raise ValueError(f"CELL child base is unresolved in source corpus: {child['formKey']}")
        keys.add(capability_key("base-record", str(target["recordType"])))
        if implicit is not None:
            keys.add(
                capability_key(
                    "relationship",
                    f"engine-implicit-base.{implicit['kind']}",
                )
            )
    if child.get("teleport") is not None:
        keys.add(capability_key("relationship", "xtel"))
    if child.get("enableParent") is not None:
        keys.add(capability_key("relationship", "enable-parent"))


def iter_jsonl(path: Path):
    with path.open("r", encoding="utf-8") as stream:
        for line in stream:
            yield json.loads(line)


def source_output_path(
    corpus_root: Path,
    manifest: dict[str, object],
    output_name: str,
) -> Path:
    return corpus_root / str(manifest["outputs"][output_name]["file"])


def producer_sources() -> list[dict[str, object]]:
    tools_root = Path(__file__).resolve().parent
    return [
        {"file": f"tools/{name}", "sha256": file_sha256(tools_root / name)}
        for name in PRODUCER_SOURCE_NAMES
    ]


def build_plan(
    corpus_root: Path,
    output_root: Path,
    recipe: dict[str, object],
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite CELL compile plan: {output_root}")
    validate_cell_corpus(corpus_root)
    source_manifest_path = corpus_root / CELL_MANIFEST_FILE_NAME
    source_manifest = json.loads(source_manifest_path.read_text(encoding="utf-8"))
    cells = read_jsonl(source_output_path(corpus_root, source_manifest, "cells"))
    reviews = {
        str(row["cellFormKey"]): row
        for row in read_jsonl(source_output_path(corpus_root, source_manifest, "cellReview"))
    }
    linked_by_key = {
        str(row["formKey"]): row
        for row in read_jsonl(source_output_path(corpus_root, source_manifest, "linkedRecords"))
    }
    implicit_by_key = {
        str(row["formKey"]): row
        for row in read_jsonl(source_output_path(corpus_root, source_manifest, "implicitBases"))
    }
    capabilities_by_cell = {
        str(cell["formKey"]): cell_capabilities(cell) for cell in cells
    }
    children_by_cell: dict[str, list[str]] = defaultdict(list)
    for child in iter_jsonl(source_output_path(corpus_root, source_manifest, "children")):
        cell_key = str(child["cell"]["key"])
        if cell_key not in capabilities_by_cell:
            raise ValueError(f"CELL compile child has no effective parent: {child['formKey']}")
        children_by_cell[cell_key].append(str(child["formKey"]))
        add_child_capabilities(
            capabilities_by_cell[cell_key],
            child,
            linked_by_key,
            implicit_by_key,
        )

    plugin_inputs = {
        str(row["file"]).casefold(): row for row in source_manifest["inputs"]
    }
    anomalies_by_cell: dict[str, list[str]] = defaultdict(list)
    for anomaly in read_jsonl(
        source_output_path(corpus_root, source_manifest, "sourceAnomalies")
    ):
        cell_key = stable_parent_cell_key(anomaly, plugin_inputs)
        if cell_key not in capabilities_by_cell:
            raise ValueError(f"Source anomaly parent CELL is absent: {cell_key}")
        anomaly_key = (
            f"{anomaly['sourcePlugin']}@{anomaly['rawFormId']}"
            f"#{anomaly['classification']}"
        )
        anomalies_by_cell[cell_key].append(anomaly_key)
        capabilities_by_cell[cell_key].add(
            capability_key("source-anomaly", str(anomaly["classification"]))
        )

    stage_state = stage_statuses(recipe)
    configured_families = set(recipe["capabilityFamilies"])
    capability_usage: Counter[str] = Counter()
    capability_set_usage: Counter[str] = Counter()
    capability_set_keys: dict[str, list[str]] = {}
    jobs_by_partition: dict[str, list[dict[str, object]]] = defaultdict(list)
    source_cells = {str(row["formKey"]): row for row in cells}
    for cell_key in sorted(source_cells):
        cell = source_cells[cell_key]
        review = reviews.get(cell_key)
        if review is None:
            raise ValueError(f"CELL compile plan has no review row: {cell_key}")
        required_capabilities = sorted(capabilities_by_cell[cell_key])
        families = {value.split(":", 1)[0] for value in required_capabilities}
        if not families <= configured_families:
            raise ValueError(f"CELL compile capability family is not configured: {cell_key}")
        capability_usage.update(required_capabilities)
        set_id = capability_set_id(required_capabilities)
        capability_set_usage[set_id] += 1
        capability_set_keys[set_id] = required_capabilities
        key = partition_key(cell)
        jobs_by_partition[key].append(
            {
                "cellFormKey": cell_key,
                "cellRuntimeFormId": cell["runtimeFormId"],
                "cellRecordDataSha256": cell["recordDataSha256"],
                "cellClass": "interior" if cell["interior"] else "exterior",
                "partitionKey": key,
                "worldspace": cell["worldspace"],
                "coordinates": cell["coordinates"],
                "childFormKeys": sorted(children_by_cell[cell_key]),
                "childCount": len(children_by_cell[cell_key]),
                "sourceAnomalyKeys": sorted(anomalies_by_cell[cell_key]),
                "capabilitySetId": set_id,
                "requiredGates": review["requiredGates"],
                "requiredShots": review["requiredShots"],
                "jobStatus": job_status(recipe),
                "compileOutputStatus": COMPILE_OUTPUT_ABSENT_STATUS,
            }
        )

    capability_rows = [
        {
            "capabilityKey": key,
            "family": key.split(":", 1)[0],
            "subject": key.split(":", 1)[1],
            "requiredByCells": capability_usage[key],
            "stageStatus": stage_state,
        }
        for key in sorted(capability_usage)
    ]
    capability_set_rows = [
        {
            "capabilitySetId": set_id,
            "capabilityKeys": capability_set_keys[set_id],
            "requiredByCells": capability_set_usage[set_id],
        }
        for set_id in sorted(capability_set_keys)
    ]

    output_root.mkdir(parents=True)
    partition_root = output_root / PARTITION_DIRECTORY_NAME
    partition_root.mkdir()
    partition_rows: list[dict[str, object]] = []
    job_descriptors: list[dict[str, object]] = []
    for key in sorted(jobs_by_partition):
        jobs = jobs_by_partition[key]
        path = partition_root / partition_file_name(key)
        atomic_bytes(path, jsonl_bytes(jobs))
        descriptor = output_descriptor(path, len(jobs))
        descriptor["file"] = f"{PARTITION_DIRECTORY_NAME}/{path.name}"
        job_descriptors.append({"partitionKey": key, **descriptor})
        partition_rows.append(
            {
                "partitionKey": key,
                "partitionClass": key.split(":", 1)[0],
                "jobFile": descriptor["file"],
                "cellJobs": len(jobs),
            }
        )

    output_rows = {
        "capabilities": (CAPABILITIES_FILE_NAME, capability_rows),
        "capabilitySets": (CAPABILITY_SETS_FILE_NAME, capability_set_rows),
        "partitions": (PARTITIONS_FILE_NAME, partition_rows),
    }
    descriptors: dict[str, dict[str, object]] = {}
    for name, (file_name, rows) in output_rows.items():
        path = output_root / file_name
        atomic_bytes(path, jsonl_bytes(rows))
        descriptors[name] = output_descriptor(path, len(rows))

    jobs = sum(len(rows) for rows in jobs_by_partition.values())
    pending_jobs = sum(
        row["jobStatus"] == JOB_PENDING_STATUS
        for rows in jobs_by_partition.values()
        for row in rows
    )
    manifest = {
        "schema": PLAN_SCHEMA,
        "recipeId": recipe["id"],
        "recipeCanonicalSha256": canonical_sha256(recipe),
        "status": "planned-all-jobs-pending-implementation",
        "sourceCorpus": {
            "schema": source_manifest["schema"],
            "manifestFile": source_manifest_path.name,
            "manifestBytes": source_manifest_path.stat().st_size,
            "manifestSha256": file_sha256(source_manifest_path),
            "effectiveCounts": source_manifest["effectiveCounts"],
        },
        "producerSources": producer_sources(),
        "partitionPolicy": recipe["partitionPolicy"],
        "stageDefaults": stage_state,
        "planningGateStage": recipe["planningGateStage"],
        "counts": {
            "cellJobs": jobs,
            "childRelationships": sum(len(values) for values in children_by_cell.values()),
            "partitions": len(partition_rows),
            "capabilities": len(capability_rows),
            "capabilitySets": len(capability_set_rows),
            "sourceAnomaliesScheduled": sum(len(values) for values in anomalies_by_cell.values()),
            "pendingJobs": pending_jobs,
            "readyJobs": jobs - pending_jobs,
        },
        "evidencePolicy": {
            "inventoryIsNotCompilationRuntimeOrParityEvidence": True,
            "everyCompileOutputStartsAbsent": True,
            "unsupportedCapabilitiesRemainPending": True,
        },
        "outputs": descriptors,
        "jobPartitions": job_descriptors,
    }
    atomic_json(output_root / MANIFEST_FILE_NAME, manifest)
    return manifest


def default_recipe_path() -> Path:
    return configured_recipe_path("cellCompilePlan")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    args = parser.parse_args()
    try:
        recipe = load_recipe(args.recipe.resolve())
        manifest = build_plan(
            args.corpus_root.resolve(),
            args.output_root.resolve(),
            recipe,
        )
    except Exception as error:
        print(f"OPENNV_CELL_COMPILE_PLAN_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_CELL_COMPILE_PLAN "
        + json.dumps(
            {
                "manifest": str((args.output_root / MANIFEST_FILE_NAME).resolve()),
                "status": manifest["status"],
                "counts": manifest["counts"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
