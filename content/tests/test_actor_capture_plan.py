from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_capture_plan import build_capture_plan  # noqa: E402
from plugin_stack import file_sha256  # noqa: E402
from validate_actor_capture_plan import validate_plan  # noqa: E402


def signature(category_sources: dict[str, str]) -> str:
    return hashlib.sha256(
        json.dumps(
            category_sources,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
    ).hexdigest()


def review(
    base_key: str,
    runtime_form_id: str,
    source_key: str,
    outcome: str,
) -> dict[str, object]:
    category_sources = {"model": source_key, "traits": source_key}
    return {
        "reviewKey": f"{base_key}@{outcome}",
        "baseFormKey": base_key,
        "baseRuntimeFormId": runtime_form_id,
        "recordType": "NPC_" if base_key.endswith("1") else "CREA",
        "editorId": f"Actor{base_key[-1]}",
        "appearanceSignatureSha256": signature(category_sources),
        "categorySources": category_sources,
        "categorySourceRuntimeFormIds": {
            "model": runtime_form_id,
            "traits": runtime_form_id,
        },
        "templateSelectionPaths": [[base_key, source_key]],
        "requiredShots": ["front", "profile"],
        "retailEvidenceStatus": "pending",
        "godotEvidenceStatus": "pending",
        "matchedComparisonStatus": "pending",
    }


def write_jsonl(path: Path, rows: list[dict[str, object]]) -> None:
    path.write_text(
        "".join(
            json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n"
            for row in rows
        ),
        encoding="utf-8",
    )


def write_corpus(root: Path) -> None:
    rows = [
        review("FalloutNV.esm:000001", "00000001", "FalloutNV.esm:000001", "fixed"),
        review("FalloutNV.esm:000002", "00000002", "FalloutNV.esm:000003", "alpha"),
        review("FalloutNV.esm:000002", "00000002", "FalloutNV.esm:000004", "beta"),
    ]
    appearance_path = root / "appearance-review.jsonl"
    write_jsonl(appearance_path, rows)
    manifest = {
        "schema": "opennv-actor-parity-corpus/v1",
        "recipeId": "synthetic-corpus",
        "status": "inventory-complete-review-pending",
        "inputs": [
            {
                "file": "FalloutNV.esm",
                "loadOrderIndex": 0,
                "bytes": 1,
                "sha256": "synthetic",
            }
        ],
        "outputs": {
            "appearanceReview": {
                "file": appearance_path.name,
                "rows": len(rows),
                "bytes": appearance_path.stat().st_size,
                "sha256": file_sha256(appearance_path),
            }
        },
    }
    (root / "manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def recipe() -> dict[str, object]:
    return {
        "schema": "opennv-actor-capture-plan-recipe/v1",
        "id": "synthetic-capture-plan",
        "sourceCorpusRecipeId": "synthetic-corpus",
        "batching": {"baseJobsPerBatch": 1},
        "observation": {
            "fixedBaseStrategy": "observe-once",
            "dynamicBaseStrategy": "observe-until-covered",
            "partialCoveragePolicy": "pending-never-pass",
            "framingPolicy": "runtime-bounds",
            "enginesSequential": True,
            "cameraConstantsAllowed": False,
            "requiredTelemetryFields": ["identity", "bounds"],
        },
    }


def read_jsonl(path: Path) -> list[dict[str, object]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]


class ActorCapturePlanTest(unittest.TestCase):
    def test_plan_covers_fixed_and_dynamic_outcomes_exactly(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            corpus_root = root / "corpus"
            corpus_root.mkdir()
            write_corpus(corpus_root)
            first_root = root / "first"
            second_root = root / "second"

            manifest = build_capture_plan(corpus_root, first_root, recipe())
            counts = validate_plan(first_root, corpus_root)
            build_capture_plan(corpus_root, second_root, recipe())
            jobs = read_jsonl(first_root / "capture-jobs.jsonl")
            batches = read_jsonl(first_root / "capture-batches.jsonl")

            self.assertEqual(manifest["status"], "capture-plan-complete-evidence-pending")
            self.assertEqual(counts["baseJobs"], 2)
            self.assertEqual(counts["fixedBaseJobs"], 1)
            self.assertEqual(counts["dynamicBaseJobs"], 1)
            self.assertEqual(counts["expectedOutcomes"], 3)
            self.assertEqual(counts["requiredShots"], 6)
            self.assertEqual(len(batches), 2)
            self.assertEqual(jobs[0]["observationStrategy"], "observe-once")
            self.assertEqual(jobs[1]["observationStrategy"], "observe-until-covered")
            self.assertEqual(
                jobs[1]["expectedReviewKeys"],
                [
                    "FalloutNV.esm:000002@alpha",
                    "FalloutNV.esm:000002@beta",
                ],
            )
            self.assertFalse(jobs[1]["completionContract"]["partialCoverageMayPass"])
            for name in ("capture-jobs.jsonl", "capture-batches.jsonl"):
                self.assertEqual(
                    (first_root / name).read_bytes(),
                    (second_root / name).read_bytes(),
                )

            jobs_path = first_root / "capture-jobs.jsonl"
            jobs_path.write_bytes(jobs_path.read_bytes() + b" ")
            with self.assertRaisesRegex(ValueError, "byte count mismatch"):
                validate_plan(first_root, corpus_root)

    def test_plan_refuses_to_overwrite_output(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            corpus_root = root / "corpus"
            corpus_root.mkdir()
            write_corpus(corpus_root)
            output_root = root / "plan"
            output_root.mkdir()
            with self.assertRaisesRegex(FileExistsError, "Refusing to overwrite"):
                build_capture_plan(corpus_root, output_root, recipe())


if __name__ == "__main__":
    unittest.main()
