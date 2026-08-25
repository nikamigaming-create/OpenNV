#!/usr/bin/env python3
"""Compile exact CELL identities into a matched retail/Godot capture plan."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from cell_parity_corpus import canonical_sha256
from corpus_io import atomic_bytes, atomic_json, jsonl_bytes, output_descriptor, read_jsonl
from plugin_stack import file_sha256, parse_form_key


CELL_CORPUS_SCHEMA = "opennv-cell-parity-corpus/v1"
RECIPE_SCHEMA = "opennv-area-capture-plan-recipe/v1"
PLAN_SCHEMA = "opennv-area-capture-plan/v1"
MANIFEST_FILE_NAME = "manifest.json"
JOBS_FILE_NAME = "area-capture-jobs.jsonl"
EXIT_DATA_ERROR = 2
PRODUCER_SOURCE_NAMES = (
    "area_capture_plan.py",
    "cell_parity_corpus.py",
    "corpus_io.py",
    "plugin_stack.py",
)


def load_json(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return document


def require_string(value: object, context: str) -> str:
    if not isinstance(value, str) or not value:
        raise ValueError(f"{context} must be a non-empty string")
    return value


def require_unique_strings(value: object, context: str) -> list[str]:
    if (
        not isinstance(value, list)
        or not value
        or any(not isinstance(item, str) or not item for item in value)
        or len(value) != len(set(value))
    ):
        raise ValueError(f"{context} must contain unique non-empty strings")
    return value


def load_recipe(path: Path) -> dict[str, object]:
    recipe = load_json(path)
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise ValueError(f"Unexpected area capture recipe schema: {path}")
    require_string(recipe.get("id"), "area capture recipe id")
    require_string(recipe.get("sourceCorpusRecipeId"), "source corpus recipe id")
    policy = recipe.get("selectionPolicy")
    if not isinstance(policy, dict):
        raise ValueError("Area capture selectionPolicy must be an object")
    area_count = policy.get("exactAreaCount")
    if isinstance(area_count, bool) or not isinstance(area_count, int) or area_count < 1:
        raise ValueError("Area capture exactAreaCount must be a positive integer")
    required_classes = require_unique_strings(
        policy.get("requiredCellClasses"), "required cell classes"
    )
    if set(required_classes) != {"interior", "exterior"}:
        raise ValueError("Area capture plan must cover interior and exterior CELLs")
    require_unique_strings(policy.get("requiredPlugins"), "required plugins")
    for field in (
        "comparisonMode",
        "cropPolicy",
        "missingEvidencePolicy",
    ):
        require_string(policy.get(field), f"selectionPolicy.{field}")
    for field in (
        "retailCaptureFirst",
        "godotConsumesRetailCameraTelemetry",
        "enginesSequential",
    ):
        if policy.get(field) is not True:
            raise ValueError(f"selectionPolicy.{field} must be true")
    if policy.get("cameraConstantsAllowed") is not False:
        raise ValueError("Area capture recipe must forbid camera constants")
    require_unique_strings(
        policy.get("requiredCameraTelemetryFields"), "camera telemetry fields"
    )
    require_unique_strings(
        policy.get("requiredFrameMetadataFields"), "frame metadata fields"
    )
    areas = recipe.get("areas")
    if not isinstance(areas, list) or len(areas) != area_count:
        raise ValueError("Area capture recipe does not match exactAreaCount")
    area_ids: list[str] = []
    cell_keys: list[str] = []
    for area in areas:
        if not isinstance(area, dict):
            raise ValueError("Area capture recipe contains a non-object area")
        for field in (
            "id",
            "displayName",
            "cellFormKey",
            "expectedEditorId",
            "expectedCellClass",
            "comparisonShot",
        ):
            require_string(area.get(field), f"area.{field}")
        parse_form_key(str(area["cellFormKey"]))
        if area["expectedCellClass"] not in required_classes:
            raise ValueError(f"Area has an invalid CELL class: {area['id']}")
        require_unique_strings(area.get("coverageTags"), f"area coverage tags: {area['id']}")
        area_ids.append(str(area["id"]))
        cell_keys.append(str(area["cellFormKey"]))
    if len(area_ids) != len(set(area_ids)):
        raise ValueError("Area capture recipe repeats an area id")
    if len(cell_keys) != len(set(cell_keys)):
        raise ValueError("Area capture recipe repeats a CELL")
    return recipe


def verified_output_rows(
    corpus_root: Path,
    manifest: dict[str, object],
    output_name: str,
) -> list[dict[str, object]]:
    outputs = manifest.get("outputs")
    descriptor = outputs.get(output_name) if isinstance(outputs, dict) else None
    if not isinstance(descriptor, dict):
        raise ValueError(f"CELL corpus has no {output_name} descriptor")
    path = corpus_root / require_string(descriptor.get("file"), f"{output_name} file")
    if not path.is_file():
        raise ValueError(f"Missing CELL corpus output: {path}")
    if path.stat().st_size != descriptor.get("bytes"):
        raise ValueError(f"CELL corpus byte count mismatch: {path.name}")
    if file_sha256(path) != descriptor.get("sha256"):
        raise ValueError(f"CELL corpus SHA-256 mismatch: {path.name}")
    rows = read_jsonl(path)
    if len(rows) != descriptor.get("rows"):
        raise ValueError(f"CELL corpus row count mismatch: {path.name}")
    return rows


def verify_corpus(
    corpus_root: Path,
) -> tuple[dict[str, object], list[dict[str, object]], list[dict[str, object]]]:
    manifest = load_json(corpus_root / MANIFEST_FILE_NAME)
    if manifest.get("schema") != CELL_CORPUS_SCHEMA:
        raise ValueError("Unexpected CELL parity corpus schema")
    return (
        manifest,
        verified_output_rows(corpus_root, manifest, "cells"),
        verified_output_rows(corpus_root, manifest, "cellReview"),
    )


def compile_jobs(
    cells: list[dict[str, object]],
    reviews: list[dict[str, object]],
    recipe: dict[str, object],
) -> list[dict[str, object]]:
    cells_by_key = {str(row["formKey"]): row for row in cells}
    reviews_by_key = {str(row["cellFormKey"]): row for row in reviews}
    jobs: list[dict[str, object]] = []
    for ordinal, area in enumerate(recipe["areas"], start=1):
        key = str(area["cellFormKey"])
        cell = cells_by_key.get(key)
        review = reviews_by_key.get(key)
        if cell is None or review is None:
            raise ValueError(f"Selected CELL is absent from the corpus: {key}")
        cell_class = "interior" if bool(cell["interior"]) else "exterior"
        expected = {
            "sourcePlugin": parse_form_key(key).owner_plugin,
            "editorId": area["expectedEditorId"],
            "cellClass": area["expectedCellClass"],
        }
        observed = {
            "sourcePlugin": cell["sourcePlugin"],
            "editorId": cell["editorId"],
            "cellClass": cell_class,
        }
        if observed != expected:
            raise ValueError(f"Selected CELL identity differs: {key}")
        comparison_shot = str(area["comparisonShot"])
        if comparison_shot not in review["requiredShots"]:
            raise ValueError(f"Selected comparison shot is not required by CELL: {key}")
        if review["cellClass"] != cell_class or review["editorId"] != cell["editorId"]:
            raise ValueError(f"CELL review identity differs from CELL row: {key}")
        jobs.append(
            {
                "areaId": area["id"],
                "ordinal": ordinal,
                "displayName": area["displayName"],
                "coverageTags": area["coverageTags"],
                "cell": {
                    "formKey": key,
                    "runtimeFormId": cell["runtimeFormId"],
                    "sourcePlugin": cell["sourcePlugin"],
                    "editorId": cell["editorId"],
                    "recordDataSha256": cell["recordDataSha256"],
                    "cellClass": cell_class,
                    "coordinates": cell["coordinates"],
                    "worldspace": cell["worldspace"],
                    "lighting": cell["lighting"],
                },
                "sourceReview": {
                    "childRecordCounts": review["childRecordCounts"],
                    "portalEdges": review["portalEdges"],
                    "requiredGates": review["requiredGates"],
                    "requiredShots": review["requiredShots"],
                },
                "comparisonShot": comparison_shot,
                "retailEvidenceStatus": "pending",
                "godotEvidenceStatus": "pending",
                "cameraContractStatus": "pending-retail-observation",
                "matchedComparisonStatus": "blocked-missing-retail-and-godot-evidence",
                "humanReviewStatus": "pending",
            }
        )
    return jobs


def count_jobs(jobs: list[dict[str, object]]) -> dict[str, int]:
    return {
        "areas": len(jobs),
        "interiorAreas": sum(job["cell"]["cellClass"] == "interior" for job in jobs),
        "exteriorAreas": sum(job["cell"]["cellClass"] == "exterior" for job in jobs),
        "plugins": len({str(job["cell"]["sourcePlugin"]) for job in jobs}),
        "primaryComparisons": len(jobs),
        "requiredShots": sum(len(job["sourceReview"]["requiredShots"]) for job in jobs),
        "childRecords": sum(sum(job["sourceReview"]["childRecordCounts"].values()) for job in jobs),
        "actorPlacements": sum(
            int(job["sourceReview"]["childRecordCounts"].get("ACHR", 0))
            + int(job["sourceReview"]["childRecordCounts"].get("ACRE", 0))
            for job in jobs
        ),
        "portalEdges": sum(int(job["sourceReview"]["portalEdges"]) for job in jobs),
    }


def producer_sources() -> list[dict[str, object]]:
    root = Path(__file__).resolve().parent
    return [
        {"file": f"tools/{name}", "sha256": file_sha256(root / name)}
        for name in PRODUCER_SOURCE_NAMES
    ]


def build_plan(
    corpus_root: Path,
    output_root: Path,
    recipe: dict[str, object],
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite area capture plan: {output_root}")
    corpus_manifest, cells, reviews = verify_corpus(corpus_root)
    if corpus_manifest.get("recipeId") != recipe["sourceCorpusRecipeId"]:
        raise ValueError("Area capture recipe source corpus differs")
    jobs = compile_jobs(cells, reviews, recipe)
    policy = recipe["selectionPolicy"]
    selected_classes = {str(job["cell"]["cellClass"]) for job in jobs}
    selected_plugins = {str(job["cell"]["sourcePlugin"]) for job in jobs}
    if selected_classes != set(policy["requiredCellClasses"]):
        raise ValueError("Selected CELL classes do not satisfy the recipe")
    if selected_plugins != set(policy["requiredPlugins"]):
        raise ValueError("Selected plugins do not satisfy the recipe")

    output_root.mkdir(parents=True)
    jobs_path = output_root / JOBS_FILE_NAME
    atomic_bytes(jobs_path, jsonl_bytes(jobs))
    manifest = {
        "schema": PLAN_SCHEMA,
        "recipeId": recipe["id"],
        "recipeCanonicalSha256": canonical_sha256(recipe),
        "status": "capture-plan-complete-evidence-pending",
        "sourceCorpus": {
            "schema": corpus_manifest["schema"],
            "recipeId": corpus_manifest["recipeId"],
            "manifestSha256": file_sha256(corpus_root / MANIFEST_FILE_NAME),
            "cellsSha256": corpus_manifest["outputs"]["cells"]["sha256"],
            "cellReviewSha256": corpus_manifest["outputs"]["cellReview"]["sha256"],
            "inputs": corpus_manifest["inputs"],
        },
        "producerSources": producer_sources(),
        "selectionPolicy": policy,
        "counts": count_jobs(jobs),
        "evidencePolicy": {
            "planGenerationIsNotVisualEvidence": True,
            "retailNativeFrameAndTelemetryRequiredFirst": True,
            "godotMustConsumeTheRetailCameraContract": True,
            "nativeFramesMayNotBeReframedOrCropped": True,
            "missingOrFailedEvidenceCannotPass": True,
            "everyPrimaryComparisonRequiresHumanReview": True,
        },
        "outputs": {"jobs": output_descriptor(jobs_path, len(jobs))},
    }
    atomic_json(output_root / MANIFEST_FILE_NAME, manifest)
    return manifest


def default_recipe_path() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes" / "fnv-thirteen-area-capture-plan-v1.json"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    args = parser.parse_args()
    try:
        manifest = build_plan(
            args.corpus_root.resolve(),
            args.output_root.resolve(),
            load_recipe(args.recipe.resolve()),
        )
    except Exception as error:
        print(f"OPENNV_AREA_CAPTURE_PLAN_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_AREA_CAPTURE_PLAN "
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
