"""Join one static CELL job to its immutable corpus source rows."""

from __future__ import annotations

from pathlib import Path

from corpus_io import read_jsonl


def source_output_path(
    root: Path,
    manifest: dict[str, object],
    name: str,
) -> Path:
    return root / str(manifest["outputs"][name]["file"])


def find_row(path: Path, field: str, value: str) -> dict[str, object]:
    matches = [row for row in read_jsonl(path) if row.get(field) == value]
    if len(matches) != 1:
        raise ValueError(f"Expected one {field}={value!r} in {path}, found {len(matches)}")
    return matches[0]


def find_job(
    plan_root: Path,
    plan_manifest: dict[str, object],
    cell_key: str,
) -> dict[str, object]:
    matches = []
    for descriptor in plan_manifest["jobPartitions"]:
        path = plan_root / str(descriptor["file"])
        matches.extend(row for row in read_jsonl(path) if row["cellFormKey"] == cell_key)
    if len(matches) != 1:
        raise ValueError(f"Expected one compile job for {cell_key}, found {len(matches)}")
    return matches[0]


def source_rows_for_job(
    corpus_root: Path,
    corpus_manifest: dict[str, object],
    job: dict[str, object],
) -> tuple[
    dict[str, object],
    list[dict[str, object]],
    dict[str, dict[str, object]],
    list[dict[str, object]],
]:
    cell_key = str(job["cellFormKey"])
    cell = find_row(
        source_output_path(corpus_root, corpus_manifest, "cells"),
        "formKey",
        cell_key,
    )
    expected_children = set(str(value) for value in job["childFormKeys"])
    children = [
        row
        for row in read_jsonl(source_output_path(corpus_root, corpus_manifest, "children"))
        if row["formKey"] in expected_children
    ]
    if {str(row["formKey"]) for row in children} != expected_children:
        raise ValueError(f"Static CELL compile child set differs: {cell_key}")
    required_bases = {
        str(row["baseOrActor"]["key"])
        for row in children
        if isinstance(row.get("baseOrActor"), dict)
    }
    linked = {
        str(row["formKey"]): row
        for row in read_jsonl(
            source_output_path(corpus_root, corpus_manifest, "linkedRecords")
        )
        if row["formKey"] in required_bases
    }
    implicit = {
        str(row["formKey"]): row
        for row in read_jsonl(
            source_output_path(corpus_root, corpus_manifest, "implicitBases")
        )
        if row["formKey"] in required_bases
    }
    bases = {**linked, **implicit}
    if set(bases) != required_bases:
        raise ValueError(f"Static CELL compile base set differs: {cell_key}")
    child_keys = {str(row["formKey"]) for row in children}
    portals = [
        row
        for row in read_jsonl(source_output_path(corpus_root, corpus_manifest, "portals"))
        if row["sourceReference"] in child_keys
    ]
    return cell, children, bases, portals
