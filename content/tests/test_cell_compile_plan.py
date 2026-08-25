from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from cell_compile_plan import build_plan  # noqa: E402
from cell_parity_corpus import build_corpus as build_cell_corpus  # noqa: E402
from content.tests.test_cell_parity_corpus import (  # noqa: E402
    base_plugin,
    cell_recipe,
    dlc_plugin,
    invalid_namespace_plugin,
)
from validate_cell_compile_plan import validate_plan  # noqa: E402


def compile_recipe() -> dict[str, object]:
    return {
        "schema": "opennv-cell-compile-plan-recipe/v1",
        "id": "synthetic-cell-compile-plan",
        "sourceCorpusSchema": "opennv-cell-parity-corpus/v1",
        "partitionPolicy": {
            "interior": "source-plugin",
            "exterior": "worldspace-form-key",
            "fileIdentity": "full-sha256-of-partition-key",
        },
        "capabilityFamilies": [
            "base-record",
            "cell-class",
            "cell-subrecord",
            "child-record",
            "child-subrecord",
            "relationship",
            "source-anomaly",
        ],
        "stages": [
            {"id": "inventory", "initialStatus": "pass"},
            {"id": "owned-data-compilation", "initialStatus": "pending"},
            {"id": "runtime", "initialStatus": "pending"},
            {"id": "matched-parity", "initialStatus": "pending"},
        ],
        "planningGateStage": "owned-data-compilation",
    }


def write_recipe(root: Path, recipe: dict[str, object]) -> Path:
    path = root / "compile-recipe.json"
    path.write_text(json.dumps(recipe), encoding="utf-8")
    return path


def read_jsonl(path: Path) -> list[dict[str, object]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]


class CellCompilePlanTest(unittest.TestCase):
    def test_every_cell_child_capability_and_partition_is_exact(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            (data_root / "FalloutNV.esm").write_bytes(base_plugin())
            (data_root / "Dlc.esm").write_bytes(dlc_plugin())
            corpus_root = root / "corpus"
            plan_root = root / "plan"
            recipe = compile_recipe()
            recipe_path = write_recipe(root, recipe)
            build_cell_corpus(data_root, corpus_root, cell_recipe())
            manifest = build_plan(corpus_root, plan_root, recipe)
            counts = validate_plan(plan_root, corpus_root, recipe_path)
            partitions = read_jsonl(plan_root / "partitions.jsonl")
            capabilities = read_jsonl(plan_root / "capabilities.jsonl")
            jobs = []
            for descriptor in manifest["jobPartitions"]:
                jobs.extend(read_jsonl(plan_root / descriptor["file"]))
            with self.assertRaises(FileExistsError):
                build_plan(corpus_root, plan_root, recipe)

        self.assertEqual(counts["cellJobs"], 2)
        self.assertEqual(counts["childRelationships"], 6)
        self.assertEqual(counts["partitions"], 2)
        self.assertEqual(counts["pendingJobs"], 2)
        self.assertEqual(counts["readyJobs"], 0)
        self.assertEqual({row["cellFormKey"] for row in jobs}, {
            "FalloutNV.esm:000100",
            "FalloutNV.esm:000101",
        })
        self.assertEqual(sum(int(row["childCount"]) for row in jobs), 6)
        self.assertTrue(all(row["compileOutputStatus"] == "not-built" for row in jobs))
        self.assertEqual(
            {row["partitionClass"] for row in partitions},
            {"interior", "exterior"},
        )
        capability_keys = {row["capabilityKey"] for row in capabilities}
        self.assertIn("relationship:xtel", capability_keys)
        self.assertIn("child-record:LAND", capability_keys)
        self.assertIn("child-record:NAVM", capability_keys)
        self.assertIn("base-record:NPC_", capability_keys)

    def test_source_anomaly_is_scheduled_on_its_parent_cell(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            plugin, record_data_sha256 = invalid_namespace_plugin()
            (data_root / "FalloutNV.esm").write_bytes(plugin)
            corpus_recipe = cell_recipe()
            corpus_recipe["plugins"] = [{"file": "FalloutNV.esm"}]
            corpus_recipe["sourceAnomalies"] = [
                {
                    "sourcePlugin": "FalloutNV.esm",
                    "rawFormId": "01000200",
                    "recordType": "REFR",
                    "recordFlags": "00000000",
                    "parentCellRawFormId": "00000100",
                    "recordDataSha256": record_data_sha256,
                    "classification": "undeclared-form-namespace",
                    "runtimeSemanticsStatus": "pending",
                }
            ]
            corpus_root = root / "corpus"
            plan_root = root / "plan"
            recipe = compile_recipe()
            recipe_path = write_recipe(root, recipe)
            build_cell_corpus(data_root, corpus_root, corpus_recipe)
            manifest = build_plan(corpus_root, plan_root, recipe)
            counts = validate_plan(plan_root, corpus_root, recipe_path)
            descriptor = manifest["jobPartitions"][0]
            jobs = read_jsonl(plan_root / descriptor["file"])
            capability_sets = {
                row["capabilitySetId"]: row
                for row in read_jsonl(plan_root / "capability-sets.jsonl")
            }

        self.assertEqual(counts["sourceAnomaliesScheduled"], 1)
        self.assertEqual(
            jobs[0]["sourceAnomalyKeys"],
            ["FalloutNV.esm@01000200#undeclared-form-namespace"],
        )
        self.assertIn(
            "source-anomaly:undeclared-form-namespace",
            capability_sets[jobs[0]["capabilitySetId"]]["capabilityKeys"],
        )


if __name__ == "__main__":
    unittest.main()
