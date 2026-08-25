#!/usr/bin/env python3
"""Validate exact CELL selection and pending evidence in an area capture plan."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from area_capture_plan import (
    JOBS_FILE_NAME,
    MANIFEST_FILE_NAME,
    PLAN_SCHEMA,
    PRODUCER_SOURCE_NAMES,
    compile_jobs,
    count_jobs,
    load_json,
    load_recipe,
    verify_corpus,
)
from cell_parity_corpus import canonical_sha256
from corpus_io import read_jsonl
from plugin_stack import file_sha256


EXIT_DATA_ERROR = 2
EVIDENCE_POLICY = {
    "planGenerationIsNotVisualEvidence": True,
    "retailNativeFrameAndTelemetryRequiredFirst": True,
    "godotMustConsumeTheRetailCameraContract": True,
    "nativeFramesMayNotBeReframedOrCropped": True,
    "missingOrFailedEvidenceCannotPass": True,
    "everyPrimaryComparisonRequiresHumanReview": True,
}


def validate_plan(
    plan_root: Path,
    corpus_root: Path,
    recipe_path: Path,
) -> dict[str, int]:
    manifest_path = plan_root / MANIFEST_FILE_NAME
    manifest = load_json(manifest_path)
    recipe = load_recipe(recipe_path)
    if manifest.get("schema") != PLAN_SCHEMA:
        raise ValueError("Unexpected area capture plan schema")
    if manifest.get("recipeId") != recipe["id"]:
        raise ValueError("Area capture plan recipe id differs")
    if manifest.get("recipeCanonicalSha256") != canonical_sha256(recipe):
        raise ValueError("Area capture plan recipe hash differs")
    if manifest.get("status") != "capture-plan-complete-evidence-pending":
        raise ValueError("Area capture plan status is invalid")

    outputs = manifest.get("outputs")
    descriptor = outputs.get("jobs") if isinstance(outputs, dict) else None
    if not isinstance(descriptor, dict) or descriptor.get("file") != JOBS_FILE_NAME:
        raise ValueError("Area capture plan jobs descriptor is invalid")
    jobs_path = plan_root / JOBS_FILE_NAME
    if not jobs_path.is_file():
        raise ValueError("Area capture plan jobs file is missing")
    if jobs_path.stat().st_size != descriptor.get("bytes"):
        raise ValueError("Area capture plan jobs byte count differs")
    if file_sha256(jobs_path) != descriptor.get("sha256"):
        raise ValueError("Area capture plan jobs SHA-256 differs")
    jobs = read_jsonl(jobs_path)
    if len(jobs) != descriptor.get("rows"):
        raise ValueError("Area capture plan jobs row count differs")

    corpus_manifest, cells, reviews = verify_corpus(corpus_root)
    source = manifest.get("sourceCorpus")
    if not isinstance(source, dict):
        raise ValueError("Area capture plan source corpus is missing")
    expected_source = {
        "schema": corpus_manifest["schema"],
        "recipeId": corpus_manifest["recipeId"],
        "manifestSha256": file_sha256(corpus_root / MANIFEST_FILE_NAME),
        "cellsSha256": corpus_manifest["outputs"]["cells"]["sha256"],
        "cellReviewSha256": corpus_manifest["outputs"]["cellReview"]["sha256"],
        "inputs": corpus_manifest["inputs"],
    }
    if source != expected_source:
        raise ValueError("Area capture plan source corpus contract differs")
    if jobs != compile_jobs(cells, reviews, recipe):
        raise ValueError("Area capture jobs differ from recipe and corpus")

    source_rows = manifest.get("producerSources")
    if not isinstance(source_rows, list):
        raise ValueError("Area capture plan producer ledger is missing")
    expected_names = [f"tools/{name}" for name in PRODUCER_SOURCE_NAMES]
    if [row.get("file") for row in source_rows] != expected_names:
        raise ValueError("Area capture plan producer file list differs")
    tools_root = Path(__file__).resolve().parent
    if any(
        row.get("sha256") != file_sha256(tools_root / Path(str(row["file"])).name)
        for row in source_rows
    ):
        raise ValueError("Area capture plan producer source changed")

    counts = count_jobs(jobs)
    if manifest.get("counts") != counts:
        raise ValueError("Area capture plan counts differ")
    if manifest.get("selectionPolicy") != recipe["selectionPolicy"]:
        raise ValueError("Area capture plan selection policy differs")
    if manifest.get("evidencePolicy") != EVIDENCE_POLICY:
        raise ValueError("Area capture plan evidence policy differs")
    if any(
        job.get("retailEvidenceStatus") != "pending"
        or job.get("godotEvidenceStatus") != "pending"
        or job.get("cameraContractStatus") != "pending-retail-observation"
        or job.get("matchedComparisonStatus")
        != "blocked-missing-retail-and-godot-evidence"
        or job.get("humanReviewStatus") != "pending"
        for job in jobs
    ):
        raise ValueError("Area capture plan promoted evidence without artifacts")
    return counts


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan-root", type=Path, required=True)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, required=True)
    args = parser.parse_args()
    try:
        counts = validate_plan(
            args.plan_root.resolve(),
            args.corpus_root.resolve(),
            args.recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_AREA_CAPTURE_PLAN_FAIL {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_AREA_CAPTURE_PLAN_PASS "
        + " ".join(f"{key}={value}" for key, value in counts.items())
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
