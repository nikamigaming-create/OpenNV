#!/usr/bin/env python3
"""Validate CELL compile-plan coverage, partitions, capabilities, and pending state."""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

from cell_compile_plan import (
    CAPABILITIES_FILE_NAME,
    CAPABILITY_SETS_FILE_NAME,
    COMPILE_OUTPUT_ABSENT_STATUS,
    JOB_PENDING_STATUS,
    JOB_READY_STATUS,
    MANIFEST_FILE_NAME,
    PARTITION_DIRECTORY_NAME,
    PARTITIONS_FILE_NAME,
    PLAN_SCHEMA,
    PRODUCER_SOURCE_NAMES,
    add_child_capabilities,
    canonical_sha256,
    capability_key,
    capability_set_id,
    default_recipe_path,
    job_status,
    load_recipe,
    partition_file_name,
    partition_key,
    stable_parent_cell_key,
    stage_statuses,
)
from corpus_io import read_jsonl
from plugin_stack import file_sha256
from validate_cell_parity_corpus import validate_corpus as validate_cell_corpus


EXIT_VALIDATION_ERROR = 2


def unique_rows(rows: list[dict[str, object]], field: str, label: str) -> set[str]:
    values = [str(row[field]) for row in rows]
    if len(values) != len(set(values)):
        raise ValueError(f"CELL compile plan repeats {label}")
    return set(values)


def validate_descriptor(path: Path, descriptor: dict[str, object]) -> list[dict[str, object]]:
    if not path.is_file():
        raise ValueError(f"CELL compile-plan output is missing: {path}")
    if path.stat().st_size != int(descriptor["bytes"]):
        raise ValueError(f"CELL compile-plan byte count differs: {path.name}")
    if file_sha256(path) != str(descriptor["sha256"]).lower():
        raise ValueError(f"CELL compile-plan hash differs: {path.name}")
    rows = read_jsonl(path)
    if len(rows) != int(descriptor["rows"]):
        raise ValueError(f"CELL compile-plan row count differs: {path.name}")
    return rows


def validate_producer_sources(manifest: dict[str, object]) -> None:
    tools_root = Path(__file__).resolve().parent
    sources = manifest.get("producerSources")
    if not isinstance(sources, list):
        raise ValueError("CELL compile plan has no producer-source ledger")
    expected_names = {f"tools/{name}" for name in PRODUCER_SOURCE_NAMES}
    names = {str(row["file"]) for row in sources}
    if names != expected_names or len(sources) != len(names):
        raise ValueError("CELL compile-plan producer-source set differs")
    for row in sources:
        relative = Path(str(row["file"]))
        path = tools_root / relative.name
        if not path.is_file() or file_sha256(path) != str(row["sha256"]).lower():
            raise ValueError(f"CELL compile-plan producer source changed: {relative}")


def source_output_path(
    corpus_root: Path,
    source_manifest: dict[str, object],
    output_name: str,
) -> Path:
    return corpus_root / str(source_manifest["outputs"][output_name]["file"])


def expected_source_graph(
    corpus_root: Path,
    source_manifest: dict[str, object],
) -> tuple[
    dict[str, dict[str, object]],
    dict[str, dict[str, object]],
    dict[str, list[str]],
    dict[str, list[str]],
    dict[str, set[str]],
]:
    cells = {
        str(row["formKey"]): row
        for row in read_jsonl(source_output_path(corpus_root, source_manifest, "cells"))
    }
    reviews = {
        str(row["cellFormKey"]): row
        for row in read_jsonl(source_output_path(corpus_root, source_manifest, "cellReview"))
    }
    linked = {
        str(row["formKey"]): row
        for row in read_jsonl(source_output_path(corpus_root, source_manifest, "linkedRecords"))
    }
    implicit = {
        str(row["formKey"]): row
        for row in read_jsonl(source_output_path(corpus_root, source_manifest, "implicitBases"))
    }
    capabilities = {
        key: {
            capability_key(
                "cell-class",
                "interior" if cell["interior"] else "exterior",
            ),
            *(
                capability_key("cell-subrecord", signature)
                for signature in cell["subrecordSignatureCounts"]
            ),
        }
        for key, cell in cells.items()
    }
    for key, cell in cells.items():
        if isinstance(cell.get("worldspace"), dict):
            capabilities[key].add(capability_key("relationship", "worldspace"))

    children_by_cell: dict[str, list[str]] = defaultdict(list)
    child_path = source_output_path(corpus_root, source_manifest, "children")
    with child_path.open("r", encoding="utf-8") as stream:
        for line in stream:
            child = json.loads(line)
            cell_key = str(child["cell"]["key"])
            if cell_key not in cells:
                raise ValueError(f"Source CELL child has no parent: {child['formKey']}")
            children_by_cell[cell_key].append(str(child["formKey"]))
            add_child_capabilities(capabilities[cell_key], child, linked, implicit)

    plugin_inputs = {
        str(row["file"]).casefold(): row for row in source_manifest["inputs"]
    }
    anomalies_by_cell: dict[str, list[str]] = defaultdict(list)
    for anomaly in read_jsonl(
        source_output_path(corpus_root, source_manifest, "sourceAnomalies")
    ):
        cell_key = stable_parent_cell_key(anomaly, plugin_inputs)
        if cell_key not in cells:
            raise ValueError(f"Source anomaly has no effective parent CELL: {cell_key}")
        anomaly_key = (
            f"{anomaly['sourcePlugin']}@{anomaly['rawFormId']}"
            f"#{anomaly['classification']}"
        )
        anomalies_by_cell[cell_key].append(anomaly_key)
        capabilities[cell_key].add(
            capability_key("source-anomaly", str(anomaly["classification"]))
        )
    return cells, reviews, children_by_cell, anomalies_by_cell, capabilities


def read_plan_outputs(
    root: Path,
    manifest: dict[str, object],
) -> tuple[
    list[dict[str, object]],
    list[dict[str, object]],
    list[dict[str, object]],
    list[dict[str, object]],
]:
    outputs = manifest.get("outputs")
    expected_files = {
        "capabilities": CAPABILITIES_FILE_NAME,
        "capabilitySets": CAPABILITY_SETS_FILE_NAME,
        "partitions": PARTITIONS_FILE_NAME,
    }
    if not isinstance(outputs, dict) or set(outputs) != set(expected_files):
        raise ValueError("CELL compile-plan output descriptor set differs")
    documents: dict[str, list[dict[str, object]]] = {}
    for name, expected_file in expected_files.items():
        descriptor = outputs[name]
        if descriptor.get("file") != expected_file:
            raise ValueError(f"CELL compile-plan output file differs: {name}")
        documents[name] = validate_descriptor(root / expected_file, descriptor)

    job_rows: list[dict[str, object]] = []
    partition_descriptors = manifest.get("jobPartitions")
    if not isinstance(partition_descriptors, list):
        raise ValueError("CELL compile plan has no job partitions")
    descriptor_keys = unique_rows(
        partition_descriptors,
        "partitionKey",
        "job-partition key",
    )
    job_root = (root / PARTITION_DIRECTORY_NAME).resolve()
    for descriptor in partition_descriptors:
        relative = Path(str(descriptor["file"]))
        path = (root / relative).resolve()
        if path.parent != job_root or relative.name != partition_file_name(
            str(descriptor["partitionKey"])
        ):
            raise ValueError("CELL compile-plan job partition path differs")
        rows = validate_descriptor(path, descriptor)
        if any(row["partitionKey"] != descriptor["partitionKey"] for row in rows):
            raise ValueError("CELL compile job is stored in the wrong partition")
        job_rows.extend(rows)
    if len(descriptor_keys) != len(partition_descriptors):
        raise ValueError("CELL compile-plan job partition keys differ")
    return (
        documents["capabilities"],
        documents["capabilitySets"],
        documents["partitions"],
        job_rows,
    )


def validate_plan(
    root: Path,
    corpus_root: Path,
    recipe_path: Path,
) -> dict[str, int]:
    manifest_path = root / MANIFEST_FILE_NAME
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("schema") != PLAN_SCHEMA:
        raise ValueError(f"Unexpected CELL compile-plan schema: {manifest_path}")
    recipe = load_recipe(recipe_path)
    if canonical_sha256(recipe) != manifest.get("recipeCanonicalSha256"):
        raise ValueError("CELL compile-plan recipe changed")
    validate_producer_sources(manifest)

    validate_cell_corpus(corpus_root)
    source_manifest_path = corpus_root / MANIFEST_FILE_NAME
    source_manifest = json.loads(source_manifest_path.read_text(encoding="utf-8"))
    source_contract = manifest.get("sourceCorpus")
    if (
        not isinstance(source_contract, dict)
        or source_contract.get("schema") != source_manifest.get("schema")
        or int(source_contract["manifestBytes"]) != source_manifest_path.stat().st_size
        or source_contract.get("manifestSha256") != file_sha256(source_manifest_path)
        or source_contract.get("effectiveCounts") != source_manifest.get("effectiveCounts")
    ):
        raise ValueError("CELL compile plan is not bound to the source corpus")

    capabilities, capability_sets, partitions, jobs = read_plan_outputs(root, manifest)
    cells, reviews, children_by_cell, anomalies_by_cell, expected_capabilities = (
        expected_source_graph(corpus_root, source_manifest)
    )
    job_keys = unique_rows(jobs, "cellFormKey", "CELL job")
    if job_keys != set(cells):
        raise ValueError("CELL compile jobs do not cover every source CELL")
    jobs_by_key = {str(row["cellFormKey"]): row for row in jobs}

    expected_set_usage: Counter[str] = Counter()
    expected_set_keys: dict[str, list[str]] = {}
    expected_capability_usage: Counter[str] = Counter()
    expected_job_status = job_status(recipe)
    for cell_key, cell in cells.items():
        keys = sorted(expected_capabilities[cell_key])
        set_id = capability_set_id(keys)
        expected_set_usage[set_id] += 1
        expected_set_keys[set_id] = keys
        expected_capability_usage.update(keys)
        review = reviews[cell_key]
        expected = {
            "cellRuntimeFormId": cell["runtimeFormId"],
            "cellRecordDataSha256": cell["recordDataSha256"],
            "cellClass": "interior" if cell["interior"] else "exterior",
            "partitionKey": partition_key(cell),
            "worldspace": cell["worldspace"],
            "coordinates": cell["coordinates"],
            "childFormKeys": sorted(children_by_cell[cell_key]),
            "childCount": len(children_by_cell[cell_key]),
            "sourceAnomalyKeys": sorted(anomalies_by_cell[cell_key]),
            "capabilitySetId": set_id,
            "requiredGates": review["requiredGates"],
            "requiredShots": review["requiredShots"],
            "jobStatus": expected_job_status,
            "compileOutputStatus": COMPILE_OUTPUT_ABSENT_STATUS,
        }
        job = jobs_by_key[cell_key]
        if any(job.get(field) != value for field, value in expected.items()):
            raise ValueError(f"CELL compile job differs from source graph: {cell_key}")

    capability_keys = unique_rows(capabilities, "capabilityKey", "capability")
    if capability_keys != set(expected_capability_usage):
        raise ValueError("CELL compile capability ledger is incomplete")
    expected_stage_state = stage_statuses(recipe)
    for row in capabilities:
        key = str(row["capabilityKey"])
        family, subject = key.split(":", 1)
        if (
            row.get("family") != family
            or row.get("subject") != subject
            or int(row["requiredByCells"]) != expected_capability_usage[key]
            or row.get("stageStatus") != expected_stage_state
        ):
            raise ValueError(f"CELL compile capability row differs: {key}")

    set_ids = unique_rows(capability_sets, "capabilitySetId", "capability set")
    if set_ids != set(expected_set_keys):
        raise ValueError("CELL compile capability-set ledger is incomplete")
    for row in capability_sets:
        set_id = str(row["capabilitySetId"])
        if (
            row.get("capabilityKeys") != expected_set_keys[set_id]
            or int(row["requiredByCells"]) != expected_set_usage[set_id]
        ):
            raise ValueError(f"CELL compile capability set differs: {set_id}")

    partition_keys = unique_rows(partitions, "partitionKey", "partition index key")
    expected_partition_counts = Counter(partition_key(cell) for cell in cells.values())
    if partition_keys != set(expected_partition_counts):
        raise ValueError("CELL compile partition index is incomplete")
    descriptor_by_key = {
        str(row["partitionKey"]): row for row in manifest["jobPartitions"]
    }
    for row in partitions:
        key = str(row["partitionKey"])
        if (
            row.get("partitionClass") != key.split(":", 1)[0]
            or row.get("jobFile") != descriptor_by_key[key]["file"]
            or int(row["cellJobs"]) != expected_partition_counts[key]
        ):
            raise ValueError(f"CELL compile partition row differs: {key}")

    child_relationships = sum(len(values) for values in children_by_cell.values())
    source_anomalies = sum(len(values) for values in anomalies_by_cell.values())
    pending_jobs = sum(row["jobStatus"] == JOB_PENDING_STATUS for row in jobs)
    ready_jobs = sum(row["jobStatus"] == JOB_READY_STATUS for row in jobs)
    expected_counts = {
        "cellJobs": len(jobs),
        "childRelationships": child_relationships,
        "partitions": len(partitions),
        "capabilities": len(capabilities),
        "capabilitySets": len(capability_sets),
        "sourceAnomaliesScheduled": source_anomalies,
        "pendingJobs": pending_jobs,
        "readyJobs": ready_jobs,
    }
    if manifest.get("counts") != expected_counts:
        raise ValueError("CELL compile-plan manifest counts differ")
    if (
        manifest.get("status") != "planned-all-jobs-pending-implementation"
        or pending_jobs != len(jobs)
        or ready_jobs != 0
    ):
        raise ValueError("CELL compile plan was promoted without implementation evidence")
    return expected_counts


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan-root", type=Path, required=True)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    args = parser.parse_args()
    try:
        counts = validate_plan(
            args.plan_root.resolve(),
            args.corpus_root.resolve(),
            args.recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_CELL_COMPILE_PLAN_FAIL {error}", file=sys.stderr)
        return EXIT_VALIDATION_ERROR
    print(
        "OPENNV_CELL_COMPILE_PLAN_PASS "
        + " ".join(f"{name}={value}" for name, value in sorted(counts.items()))
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
