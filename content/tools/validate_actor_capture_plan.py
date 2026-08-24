#!/usr/bin/env python3
"""Validate exact actor-review coverage in an OpenNV capture plan."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from actor_capture_plan import (
    BATCH_INDEX_FILE_NAME,
    CAPTURE_JOBS_FILE_NAME,
    CAPTURE_PLAN_SCHEMA,
    MANIFEST_FILE_NAME,
    expected_outcome_from_review,
    load_json,
    load_jsonl,
    require_nonempty_string,
    verify_corpus_input,
)
from plugin_stack import file_sha256


EXIT_DATA_ERROR = 2


def validate_descriptor(root: Path, descriptor: object) -> list[dict[str, object]]:
    if not isinstance(descriptor, dict):
        raise ValueError("Capture-plan output descriptor must be an object")
    path = root / require_nonempty_string(descriptor.get("file"), "output file")
    if not path.is_file():
        raise ValueError(f"Missing capture-plan output: {path}")
    if path.stat().st_size != descriptor.get("bytes"):
        raise ValueError(f"Capture-plan output byte count mismatch: {path.name}")
    if file_sha256(path) != descriptor.get("sha256"):
        raise ValueError(f"Capture-plan output SHA-256 mismatch: {path.name}")
    rows = load_jsonl(path)
    if len(rows) != descriptor.get("rows"):
        raise ValueError(f"Capture-plan output row count mismatch: {path.name}")
    return rows


def validate_plan(plan_root: Path, corpus_root: Path) -> dict[str, int]:
    manifest_path = plan_root / MANIFEST_FILE_NAME
    manifest = load_json(manifest_path)
    if manifest.get("schema") != CAPTURE_PLAN_SCHEMA:
        raise ValueError(f"Unexpected actor capture plan schema: {manifest_path}")
    outputs = manifest.get("outputs")
    if not isinstance(outputs, dict):
        raise ValueError("Actor capture plan has no output descriptors")
    jobs = validate_descriptor(plan_root, outputs.get("jobs"))
    batches = validate_descriptor(plan_root, outputs.get("batches"))
    if outputs["jobs"].get("file") != CAPTURE_JOBS_FILE_NAME:
        raise ValueError("Actor capture plan uses a non-canonical jobs file name")
    if outputs["batches"].get("file") != BATCH_INDEX_FILE_NAME:
        raise ValueError("Actor capture plan uses a non-canonical batch-index file name")

    corpus_manifest, appearance_reviews = verify_corpus_input(corpus_root)
    source = manifest.get("sourceCorpus")
    if not isinstance(source, dict):
        raise ValueError("Actor capture plan has no sourceCorpus contract")
    if source.get("manifestSha256") != file_sha256(corpus_root / MANIFEST_FILE_NAME):
        raise ValueError("Actor capture plan source corpus manifest SHA-256 mismatch")
    if source.get("appearanceReviewSha256") != corpus_manifest["outputs"][
        "appearanceReview"
    ]["sha256"]:
        raise ValueError("Actor capture plan source appearance ledger mismatch")
    if source.get("appearanceReviewFile") != corpus_manifest["outputs"][
        "appearanceReview"
    ]["file"]:
        raise ValueError("Actor capture plan source appearance file mismatch")
    if source.get("inputs") != corpus_manifest.get("inputs"):
        raise ValueError("Actor capture plan source plugin stack mismatch")
    if source.get("recipeId") != corpus_manifest.get("recipeId"):
        raise ValueError("Actor capture plan source recipe mismatch")

    source_reviews_by_key = {
        str(row["reviewKey"]): row for row in appearance_reviews
    }
    source_outcomes = {
        key: expected_outcome_from_review(row)
        for key, row in source_reviews_by_key.items()
    }
    if len(source_outcomes) != len(appearance_reviews):
        raise ValueError("Source appearance review keys are not unique")
    source_base_keys = {str(row["baseFormKey"]) for row in appearance_reviews}
    job_keys: set[str] = set()
    planned_review_keys: set[str] = set()
    dynamic_jobs = 0
    required_shots = 0
    observation = manifest.get("observation")
    if not isinstance(observation, dict):
        raise ValueError("Actor capture plan has no observation contract")
    for job in jobs:
        job_key = require_nonempty_string(job.get("captureJobKey"), "capture job key")
        if job_key in job_keys:
            raise ValueError(f"Duplicate capture job key: {job_key}")
        job_keys.add(job_key)
        if job.get("baseFormKey") != job_key:
            raise ValueError(f"Capture job/base identity mismatch: {job_key}")
        expected_review_keys = job.get("expectedReviewKeys")
        if not isinstance(expected_review_keys, list) or not expected_review_keys:
            raise ValueError(f"Capture job has no expected outcomes: {job_key}")
        if job.get("expectedOutcomeCount") != len(expected_review_keys):
            raise ValueError(f"Capture job outcome count mismatch: {job_key}")
        dynamic = len(expected_review_keys) > 1
        dynamic_jobs += int(dynamic)
        expected_strategy = observation[
            "dynamicBaseStrategy" if dynamic else "fixedBaseStrategy"
        ]
        if job.get("observationStrategy") != expected_strategy:
            raise ValueError(f"Capture job observation strategy mismatch: {job_key}")
        completion = job.get("completionContract")
        if not isinstance(completion, dict) or completion != {
            "everyExpectedSignatureObserved": True,
            "everyRequiredShotCapturedPerSignature": True,
            "partialCoverageMayPass": False,
        }:
            raise ValueError(f"Capture job completion contract mismatch: {job_key}")
        source_rows = []
        for value in expected_review_keys:
            review_key = require_nonempty_string(
                value,
                f"capture job outcome review key for {job_key}",
            )
            if review_key in planned_review_keys:
                raise ValueError(f"Duplicate planned appearance outcome: {review_key}")
            if review_key not in source_outcomes:
                raise ValueError(f"Capture job references an unknown review: {review_key}")
            planned_review_keys.add(review_key)
            source_row = source_reviews_by_key[review_key]
            source_rows.append(source_row)
            required_shots += len(source_row["requiredShots"])
        first_source = source_rows[0]
        identity = {
            "baseFormKey": first_source["baseFormKey"],
            "baseRuntimeFormId": first_source["baseRuntimeFormId"],
            "recordType": first_source["recordType"],
            "editorId": first_source.get("editorId", ""),
        }
        if any(job.get(field) != value for field, value in identity.items()):
            raise ValueError(f"Capture job identity differs from source review: {job_key}")
        if any(row["baseFormKey"] != job_key for row in source_rows):
            raise ValueError(f"Capture job contains another base's review: {job_key}")

    if job_keys != source_base_keys:
        raise ValueError("Capture jobs do not exactly cover source actor bases")
    if planned_review_keys != set(source_outcomes):
        raise ValueError("Capture jobs do not exactly cover source appearance reviews")

    batching = manifest.get("batching")
    if not isinstance(batching, dict):
        raise ValueError("Actor capture plan has no batching contract")
    base_jobs_per_batch = batching.get("baseJobsPerBatch")
    if isinstance(base_jobs_per_batch, bool) or not isinstance(base_jobs_per_batch, int):
        raise ValueError("Actor capture batch size is not an integer")
    jobs_by_key = {str(job["captureJobKey"]): job for job in jobs}
    batched_job_keys: list[str] = []
    for batch_index, batch in enumerate(batches):
        if batch.get("batchIndex") != batch_index:
            raise ValueError(f"Capture batch index is not contiguous: {batch_index}")
        key_values = batch.get("jobKeys")
        if (
            not isinstance(key_values, list)
            or not key_values
            or len(key_values) > base_jobs_per_batch
        ):
            raise ValueError(f"Capture batch size is invalid: {batch_index}")
        keys = [str(key) for key in key_values]
        if any(key not in jobs_by_key for key in keys):
            raise ValueError(f"Capture batch references an unknown job: {batch_index}")
        if batch.get("baseJobCount") != len(keys):
            raise ValueError(f"Capture batch job count mismatch: {batch_index}")
        batch_jobs = [jobs_by_key[key] for key in keys]
        expected_outcomes = sum(int(job["expectedOutcomeCount"]) for job in batch_jobs)
        required_batch_shots = sum(
            len(source_reviews_by_key[review_key]["requiredShots"])
            for job in batch_jobs
            for review_key in job["expectedReviewKeys"]
        )
        record_type_counts: dict[str, int] = {}
        for job in batch_jobs:
            record_type = str(job["recordType"])
            record_type_counts[record_type] = record_type_counts.get(record_type, 0) + 1
        if batch.get("expectedOutcomeCount") != expected_outcomes:
            raise ValueError(f"Capture batch outcome count mismatch: {batch_index}")
        if batch.get("requiredShotCount") != required_batch_shots:
            raise ValueError(f"Capture batch shot count mismatch: {batch_index}")
        if batch.get("recordTypeCounts") != dict(sorted(record_type_counts.items())):
            raise ValueError(f"Capture batch record-type counts mismatch: {batch_index}")
        batched_job_keys.extend(keys)
    if len(batched_job_keys) != len(set(batched_job_keys)):
        raise ValueError("Capture batches contain duplicate job keys")
    if batched_job_keys != [str(job["captureJobKey"]) for job in jobs]:
        raise ValueError("Capture batches do not preserve exact job order and coverage")

    counts = {
        "baseJobs": len(jobs),
        "fixedBaseJobs": len(jobs) - dynamic_jobs,
        "dynamicBaseJobs": dynamic_jobs,
        "expectedOutcomes": len(planned_review_keys),
        "requiredShots": required_shots,
        "batches": len(batches),
    }
    if manifest.get("counts") != counts:
        raise ValueError("Actor capture plan manifest counts do not match its rows")
    policy = manifest.get("evidencePolicy")
    if not isinstance(policy, dict) or policy != {
        "captureKeyIsStableBaseAndObservedSignature": True,
        "retailAndGodotMustRunSequentially": True,
        "runtimeBoundsOrHeadMarkersOwnFraming": True,
        "partialDynamicCoverageMayPass": False,
        "planGenerationIsNotVisualEvidence": True,
    }:
        raise ValueError("Actor capture plan evidence policy is incomplete")
    return counts


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan-root", type=Path, required=True)
    parser.add_argument("--corpus-root", type=Path, required=True)
    args = parser.parse_args()
    try:
        counts = validate_plan(args.plan_root.resolve(), args.corpus_root.resolve())
    except Exception as error:
        print(f"OPENNV_ACTOR_CAPTURE_PLAN_FAIL {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_ACTOR_CAPTURE_PLAN_PASS "
        + " ".join(f"{key}={value}" for key, value in counts.items())
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
