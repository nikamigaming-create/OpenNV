from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from area_capture_plan import build_plan  # noqa: E402
from corpus_io import jsonl_bytes, output_descriptor  # noqa: E402
from validate_area_capture_plan import validate_plan  # noqa: E402


def cell(key: str, runtime_id: str, editor_id: str, interior: bool) -> dict[str, object]:
    return {
        "formKey": key,
        "runtimeFormId": runtime_id,
        "sourcePlugin": key.split(":", 1)[0],
        "editorId": editor_id,
        "recordDataSha256": f"hash-{editor_id}",
        "interior": interior,
        "coordinates": None if interior else [1, 2],
        "worldspace": None if interior else {"key": "FalloutNV.esm:000001"},
        "lighting": {"ambient_rgb": [1, 2, 3]} if interior else None,
    }


def review(source: dict[str, object]) -> dict[str, object]:
    interior = bool(source["interior"])
    return {
        "cellFormKey": source["formKey"],
        "cellRuntimeFormId": source["runtimeFormId"],
        "editorId": source["editorId"],
        "cellClass": "interior" if interior else "exterior",
        "childRecordCounts": {"ACHR": 2, "REFR": 3} if interior else {"LAND": 1},
        "portalEdges": 1 if interior else 0,
        "requiredGates": ["record-graph", "matched-retail-presentation"],
        "requiredShots": (
            ["entry-context", "full-cell-route"]
            if interior
            else ["cell-center-context", "cardinal-route"]
        ),
    }


def write_corpus(root: Path) -> list[dict[str, object]]:
    cells = [
        cell("FalloutNV.esm:000101", "00000101", "InteriorOne", True),
        cell("FalloutNV.esm:000102", "00000102", "ExteriorOne", False),
        cell("DeadMoney.esm:000103", "01000103", "ExteriorTwo", False),
    ]
    reviews = [review(row) for row in cells]
    cells_path = root / "cells.jsonl"
    reviews_path = root / "cell-review.jsonl"
    cells_path.write_bytes(jsonl_bytes(cells))
    reviews_path.write_bytes(jsonl_bytes(reviews))
    manifest = {
        "schema": "opennv-cell-parity-corpus/v1",
        "recipeId": "synthetic-cell-corpus",
        "inputs": [{"file": "FalloutNV.esm", "sha256": "synthetic"}],
        "outputs": {
            "cells": output_descriptor(cells_path, len(cells)),
            "cellReview": output_descriptor(reviews_path, len(reviews)),
        },
    }
    (root / "manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return cells


def recipe() -> dict[str, object]:
    return {
        "schema": "opennv-area-capture-plan-recipe/v1",
        "id": "synthetic-area-plan",
        "sourceCorpusRecipeId": "synthetic-cell-corpus",
        "selectionPolicy": {
            "exactAreaCount": 3,
            "requiredCellClasses": ["interior", "exterior"],
            "requiredPlugins": ["FalloutNV.esm", "DeadMoney.esm"],
            "comparisonMode": "matched-native-frame-side-by-side",
            "retailCaptureFirst": True,
            "godotConsumesRetailCameraTelemetry": True,
            "enginesSequential": True,
            "cameraConstantsAllowed": False,
            "cropPolicy": "none",
            "missingEvidencePolicy": "blocked-never-pass",
            "requiredCameraTelemetryFields": ["camera-world-transform"],
            "requiredFrameMetadataFields": ["native-source-frame-sha256"],
        },
        "areas": [
            {
                "id": "interior-one",
                "displayName": "Interior One",
                "cellFormKey": "FalloutNV.esm:000101",
                "expectedEditorId": "InteriorOne",
                "expectedCellClass": "interior",
                "comparisonShot": "entry-context",
                "coverageTags": ["interior"],
            },
            {
                "id": "exterior-one",
                "displayName": "Exterior One",
                "cellFormKey": "FalloutNV.esm:000102",
                "expectedEditorId": "ExteriorOne",
                "expectedCellClass": "exterior",
                "comparisonShot": "cell-center-context",
                "coverageTags": ["exterior"],
            },
            {
                "id": "exterior-two",
                "displayName": "Exterior Two",
                "cellFormKey": "DeadMoney.esm:000103",
                "expectedEditorId": "ExteriorTwo",
                "expectedCellClass": "exterior",
                "comparisonShot": "cell-center-context",
                "coverageTags": ["exterior", "dlc"],
            },
        ],
    }


class AreaCapturePlanTest(unittest.TestCase):
    def test_exact_selection_is_deterministic_and_pending(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            corpus_root = root / "corpus"
            corpus_root.mkdir()
            write_corpus(corpus_root)
            recipe_path = root / "recipe.json"
            recipe_path.write_text(json.dumps(recipe()), encoding="utf-8")
            first = root / "first"
            second = root / "second"

            manifest = build_plan(corpus_root, first, recipe())
            build_plan(corpus_root, second, recipe())
            counts = validate_plan(first, corpus_root, recipe_path)

            self.assertEqual(counts["areas"], 3)
            self.assertEqual(counts["interiorAreas"], 1)
            self.assertEqual(counts["exteriorAreas"], 2)
            self.assertEqual(counts["plugins"], 2)
            self.assertEqual(counts["actorPlacements"], 2)
            self.assertEqual(counts["portalEdges"], 1)
            self.assertEqual(
                manifest["status"], "capture-plan-complete-evidence-pending"
            )
            for name in ("area-capture-jobs.jsonl", "manifest.json"):
                self.assertEqual((first / name).read_bytes(), (second / name).read_bytes())

            jobs_path = first / "area-capture-jobs.jsonl"
            jobs_path.write_bytes(jobs_path.read_bytes() + b" ")
            with self.assertRaisesRegex(ValueError, "byte count differs"):
                validate_plan(first, corpus_root, recipe_path)

    def test_identity_mismatch_and_overwrite_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            corpus_root = root / "corpus"
            corpus_root.mkdir()
            write_corpus(corpus_root)
            wrong = recipe()
            wrong["areas"][0]["expectedEditorId"] = "WrongInterior"
            with self.assertRaisesRegex(ValueError, "identity differs"):
                build_plan(corpus_root, root / "wrong", wrong)

            output = root / "existing"
            output.mkdir()
            with self.assertRaisesRegex(FileExistsError, "Refusing to overwrite"):
                build_plan(corpus_root, output, recipe())


if __name__ == "__main__":
    unittest.main()
