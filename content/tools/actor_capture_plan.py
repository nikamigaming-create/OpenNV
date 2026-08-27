#!/usr/bin/env python3
"""Compile a resumable whole-game retail/Godot actor capture plan."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

from actor_parity_corpus import atomic_bytes, atomic_json, jsonl_bytes, output_descriptor
from plugin_stack import file_sha256
from runtime_configuration import configured_recipe_path


CORPUS_SCHEMA = "opennv-actor-parity-corpus/v1"
CAPTURE_RECIPE_SCHEMA = "opennv-actor-capture-plan-recipe/v1"
CAPTURE_PLAN_SCHEMA = "opennv-actor-capture-plan/v1"
CAPTURE_JOBS_FILE_NAME = "capture-jobs.jsonl"
BATCH_INDEX_FILE_NAME = "capture-batches.jsonl"
MANIFEST_FILE_NAME = "manifest.json"
EXIT_DATA_ERROR = 2


def load_json(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return document


def load_jsonl(path: Path) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for line_number, line in enumerate(
        path.read_text(encoding="utf-8").splitlines(),
        start=1,
    ):
        document = json.loads(line)
        if not isinstance(document, dict):
            raise ValueError(f"Expected an object at {path}:{line_number}")
        rows.append(document)
    return rows


def require_nonempty_string(value: object, context: str) -> str:
    if not isinstance(value, str) or not value:
        raise ValueError(f"{context} must be a non-empty string")
    return value


def require_positive_integer(value: object, context: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 1:
        raise ValueError(f"{context} must be a positive integer")
    return value


def load_recipe(recipe_path: Path) -> dict[str, object]:
    document = load_json(recipe_path)
    if document.get("schema") != CAPTURE_RECIPE_SCHEMA:
        raise ValueError(f"Unexpected actor capture recipe schema: {recipe_path}")
    require_nonempty_string(document.get("id"), "capture recipe id")
    require_nonempty_string(
        document.get("sourceCorpusRecipeId"),
        "capture recipe sourceCorpusRecipeId",
    )
    batching = document.get("batching")
    if not isinstance(batching, dict):
        raise ValueError("capture recipe batching must be an object")
    require_positive_integer(
        batching.get("baseJobsPerBatch"),
        "capture recipe batching.baseJobsPerBatch",
    )
    observation = document.get("observation")
    if not isinstance(observation, dict):
        raise ValueError("capture recipe observation must be an object")
    for key in (
        "fixedBaseStrategy",
        "dynamicBaseStrategy",
        "partialCoveragePolicy",
        "framingPolicy",
    ):
        require_nonempty_string(observation.get(key), f"capture recipe observation.{key}")
    if observation.get("enginesSequential") is not True:
        raise ValueError("capture recipe must require sequential retail and Godot capture")
    if observation.get("cameraConstantsAllowed") is not False:
        raise ValueError("capture recipe must forbid camera constants")
    telemetry = observation.get("requiredTelemetryFields")
    if (
        not isinstance(telemetry, list)
        or not telemetry
        or any(not isinstance(field, str) or not field for field in telemetry)
        or len(set(telemetry)) != len(telemetry)
    ):
        raise ValueError(
            "capture recipe observation.requiredTelemetryFields must contain unique strings"
        )
    return document


def appearance_signature(category_sources: object) -> str:
    if not isinstance(category_sources, dict) or not category_sources:
        raise ValueError("appearance categorySources must be a non-empty object")
    return hashlib.sha256(
        json.dumps(
            category_sources,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
    ).hexdigest()


def expected_outcome_from_review(row: dict[str, object]) -> dict[str, object]:
    review_key = require_nonempty_string(row.get("reviewKey"), "appearance reviewKey")
    signature = require_nonempty_string(
        row.get("appearanceSignatureSha256"),
        f"appearance review {review_key} signature",
    )
    if signature != appearance_signature(row.get("categorySources")):
        raise ValueError(f"Appearance review signature mismatch: {review_key}")
    required_shots = row.get("requiredShots")
    if (
        not isinstance(required_shots, list)
        or not required_shots
        or any(not isinstance(shot, str) or not shot for shot in required_shots)
        or len(set(required_shots)) != len(required_shots)
    ):
        raise ValueError(f"Appearance review shots are invalid: {review_key}")
    category_runtime_ids = row.get("categorySourceRuntimeFormIds")
    if not isinstance(category_runtime_ids, dict) or not category_runtime_ids:
        raise ValueError(f"Appearance review runtime category sources are invalid: {review_key}")
    if set(category_runtime_ids) != set(row["categorySources"]):
        raise ValueError(f"Appearance review category source keys differ: {review_key}")
    if any(not isinstance(value, str) or not value for value in category_runtime_ids.values()):
        raise ValueError(f"Appearance review runtime category FormIDs are invalid: {review_key}")
    selection_paths = row.get("templateSelectionPaths")
    if (
        not isinstance(selection_paths, list)
        or not selection_paths
        or any(
            not isinstance(path, list)
            or not path
            or any(not isinstance(key, str) or not key for key in path)
            for path in selection_paths
        )
    ):
        raise ValueError(f"Appearance review selection paths are invalid: {review_key}")
    return {
        "reviewKey": review_key,
        "appearanceSignatureSha256": signature,
        "categorySources": row["categorySources"],
        "categorySourceRuntimeFormIds": category_runtime_ids,
        "templateSelectionPaths": selection_paths,
        "requiredShots": required_shots,
    }


def capture_jobs(
    appearance_reviews: list[dict[str, object]],
    recipe: dict[str, object],
) -> list[dict[str, object]]:
    rows_by_base: dict[str, list[dict[str, object]]] = {}
    review_keys: set[str] = set()
    for row in appearance_reviews:
        review_key = require_nonempty_string(row.get("reviewKey"), "appearance reviewKey")
        if review_key in review_keys:
            raise ValueError(f"Duplicate appearance review key: {review_key}")
        review_keys.add(review_key)
        base_key = require_nonempty_string(row.get("baseFormKey"), "appearance baseFormKey")
        rows_by_base.setdefault(base_key, []).append(row)

    observation = recipe["observation"]
    jobs: list[dict[str, object]] = []
    for base_key in sorted(rows_by_base):
        rows = sorted(rows_by_base[base_key], key=lambda row: str(row["reviewKey"]))
        first = rows[0]
        identity_fields = ("baseRuntimeFormId", "recordType", "editorId")
        for field in identity_fields:
            if any(row.get(field) != first.get(field) for row in rows[1:]):
                raise ValueError(f"Appearance reviews disagree on {field}: {base_key}")
        for row in rows:
            expected_outcome_from_review(row)
        if not isinstance(first.get("editorId"), str):
            raise ValueError(f"Appearance review editorId must be a string: {base_key}")
        strategy = (
            observation["fixedBaseStrategy"]
            if len(rows) == 1
            else observation["dynamicBaseStrategy"]
        )
        jobs.append(
            {
                "captureJobKey": base_key,
                "baseFormKey": base_key,
                "baseRuntimeFormId": require_nonempty_string(
                    first.get("baseRuntimeFormId"),
                    f"appearance base runtime FormID for {base_key}",
                ),
                "recordType": require_nonempty_string(
                    first.get("recordType"),
                    f"appearance record type for {base_key}",
                ),
                "editorId": first.get("editorId", ""),
                "observationStrategy": strategy,
                "expectedOutcomeCount": len(rows),
                "expectedReviewKeys": [str(row["reviewKey"]) for row in rows],
                "completionContract": {
                    "everyExpectedSignatureObserved": True,
                    "everyRequiredShotCapturedPerSignature": True,
                    "partialCoverageMayPass": False,
                },
            }
        )
    return jobs


def capture_batches(
    jobs: list[dict[str, object]],
    reviews_by_key: dict[str, dict[str, object]],
    base_jobs_per_batch: int,
) -> list[dict[str, object]]:
    batches: list[dict[str, object]] = []
    for offset in range(0, len(jobs), base_jobs_per_batch):
        batch_jobs = jobs[offset : offset + base_jobs_per_batch]
        record_type_counts: dict[str, int] = {}
        for job in batch_jobs:
            record_type = str(job["recordType"])
            record_type_counts[record_type] = record_type_counts.get(record_type, 0) + 1
        batches.append(
            {
                "batchIndex": len(batches),
                "batchKey": f"actor-appearance-{len(batches):05d}",
                "jobKeys": [job["captureJobKey"] for job in batch_jobs],
                "baseJobCount": len(batch_jobs),
                "expectedOutcomeCount": sum(
                    int(job["expectedOutcomeCount"]) for job in batch_jobs
                ),
                "requiredShotCount": sum(
                    len(reviews_by_key[review_key]["requiredShots"])
                    for job in batch_jobs
                    for review_key in job["expectedReviewKeys"]
                ),
                "recordTypeCounts": dict(sorted(record_type_counts.items())),
            }
        )
    return batches


def verify_corpus_input(corpus_root: Path) -> tuple[dict[str, object], list[dict[str, object]]]:
    manifest_path = corpus_root / MANIFEST_FILE_NAME
    manifest = load_json(manifest_path)
    if manifest.get("schema") != CORPUS_SCHEMA:
        raise ValueError(f"Unexpected actor parity corpus schema: {manifest_path}")
    outputs = manifest.get("outputs")
    if not isinstance(outputs, dict) or not isinstance(outputs.get("appearanceReview"), dict):
        raise ValueError("Actor parity corpus has no appearance-review descriptor")
    descriptor = outputs["appearanceReview"]
    review_path = corpus_root / require_nonempty_string(
        descriptor.get("file"),
        "appearance-review descriptor file",
    )
    if not review_path.is_file():
        raise ValueError(f"Missing actor appearance review ledger: {review_path}")
    if review_path.stat().st_size != descriptor.get("bytes"):
        raise ValueError("Actor appearance review ledger byte count mismatch")
    if file_sha256(review_path) != descriptor.get("sha256"):
        raise ValueError("Actor appearance review ledger SHA-256 mismatch")
    rows = load_jsonl(review_path)
    if len(rows) != descriptor.get("rows"):
        raise ValueError("Actor appearance review ledger row count mismatch")
    return manifest, rows


def build_capture_plan(
    corpus_root: Path,
    output_root: Path,
    recipe: dict[str, object],
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite actor capture plan: {output_root}")
    corpus_manifest, appearance_reviews = verify_corpus_input(corpus_root)
    source_recipe_id = require_nonempty_string(
        corpus_manifest.get("recipeId"),
        "actor parity corpus recipeId",
    )
    if source_recipe_id != recipe["sourceCorpusRecipeId"]:
        raise ValueError(
            "Capture recipe source corpus mismatch: "
            f"expected {recipe['sourceCorpusRecipeId']}, got {source_recipe_id}"
        )

    jobs = capture_jobs(appearance_reviews, recipe)
    reviews_by_key = {str(row["reviewKey"]): row for row in appearance_reviews}
    base_jobs_per_batch = int(recipe["batching"]["baseJobsPerBatch"])
    batches = capture_batches(jobs, reviews_by_key, base_jobs_per_batch)
    output_root.mkdir(parents=True)
    jobs_path = output_root / CAPTURE_JOBS_FILE_NAME
    batches_path = output_root / BATCH_INDEX_FILE_NAME
    atomic_bytes(jobs_path, jsonl_bytes(jobs))
    atomic_bytes(batches_path, jsonl_bytes(batches))

    dynamic_jobs = sum(int(job["expectedOutcomeCount"]) > 1 for job in jobs)
    required_shots = sum(
        len(row["requiredShots"])
        for row in appearance_reviews
    )
    manifest = {
        "schema": CAPTURE_PLAN_SCHEMA,
        "recipeId": recipe["id"],
        "status": "capture-plan-complete-evidence-pending",
        "sourceCorpus": {
            "recipeId": source_recipe_id,
            "manifestSha256": file_sha256(corpus_root / MANIFEST_FILE_NAME),
            "appearanceReviewFile": corpus_manifest["outputs"]["appearanceReview"][
                "file"
            ],
            "appearanceReviewSha256": corpus_manifest["outputs"]["appearanceReview"][
                "sha256"
            ],
            "inputs": corpus_manifest["inputs"],
        },
        "counts": {
            "baseJobs": len(jobs),
            "fixedBaseJobs": len(jobs) - dynamic_jobs,
            "dynamicBaseJobs": dynamic_jobs,
            "expectedOutcomes": len(appearance_reviews),
            "requiredShots": required_shots,
            "batches": len(batches),
        },
        "batching": {"baseJobsPerBatch": base_jobs_per_batch},
        "observation": recipe["observation"],
        "evidencePolicy": {
            "captureKeyIsStableBaseAndObservedSignature": True,
            "retailAndGodotMustRunSequentially": True,
            "runtimeBoundsOrHeadMarkersOwnFraming": True,
            "partialDynamicCoverageMayPass": False,
            "planGenerationIsNotVisualEvidence": True,
        },
        "outputs": {
            "jobs": output_descriptor(jobs_path, len(jobs)),
            "batches": output_descriptor(batches_path, len(batches)),
        },
    }
    atomic_json(output_root / MANIFEST_FILE_NAME, manifest)
    return manifest


def default_recipe_path() -> Path:
    return configured_recipe_path("actorCapturePlan")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    args = parser.parse_args()
    try:
        recipe = load_recipe(args.recipe.resolve())
        manifest = build_capture_plan(
            args.corpus_root.resolve(),
            args.output_root.resolve(),
            recipe,
        )
    except Exception as error:
        print(f"OPENNV_ACTOR_CAPTURE_PLAN_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_ACTOR_CAPTURE_PLAN "
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
