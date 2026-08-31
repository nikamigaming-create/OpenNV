from __future__ import annotations

import copy
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from compiler_provenance import (  # noqa: E402
    FAMILIES,
    actor_route_manifest,
    compiler_identities,
    compiler_provenance_source_paths,
)
from prepare_legal_assets import reusable_families  # noqa: E402
from gltf_io import compiler_sources_sha256  # noqa: E402
from runtime_configuration import load_runtime_configuration  # noqa: E402


def _write_json(path: Path, document: object) -> str:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document), encoding="utf-8")
    return hashlib.sha256(path.read_bytes()).hexdigest()


class CompilerProvenanceTest(unittest.TestCase):
    def test_opening_sources_do_not_invalidate_static_or_cell(self):
        paths = {
            family: {path.name for path in compiler_provenance_source_paths(family)}
            for family in FAMILIES
        }
        self.assertIn("opening_catalog.py", paths["opening"])
        self.assertIn("opening_catalog.py", paths["actor"])
        self.assertNotIn("opening_catalog.py", paths["static"])
        self.assertNotIn("opening_catalog.py", paths["cell"])
        self.assertIn("actor_catalog.py", paths["actor"])
        self.assertNotIn("actor_catalog.py", paths["cell"])
        self.assertNotIn("open-nv-runtime-v1.json", paths["actor"])
        self.assertIn("open-nv-runtime-v1.json", paths["cell"])
        self.assertNotIn("goodsprings-doc-mitchell-house-v1.json", paths["actor"])
        self.assertIn("goodsprings-doc-mitchell-actor-v1.json", paths["actor"])

    def test_actor_identity_binds_scoped_configuration(self):
        actor = compiler_identities()["families"]["actor"]
        self.assertEqual(
            actor["actorArtifactConfigurationSha256"],
            load_runtime_configuration().actor_artifact_manifest()["sha256"],
        )
        self.assertEqual(actor["actorRouteSha256"], actor_route_manifest()["sha256"])

    def test_non_actor_cell_fields_do_not_change_actor_route_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            tools = root / "tools"
            recipes = root / "recipes"
            tools.mkdir()
            recipes.mkdir()
            primary = {
                "id": "primary",
                "actorRecipes": ["actor-a", "actor-b"],
                "linkedCellRecipes": [{"recipe": "linked"}],
                "selection": {"modelPrefixes": ["architecture\\\\"]},
            }
            _write_json(recipes / "primary.json", primary)
            _write_json(
                recipes / "linked.json",
                {"id": "linked", "actorRecipes": ["actor-c"]},
            )
            for actor_id in ("actor-a", "actor-b", "actor-c"):
                _write_json(recipes / f"{actor_id}.json", {"id": actor_id})
            with mock.patch(
                "compiler_provenance._roots",
                return_value=(tools, recipes),
            ):
                baseline = actor_route_manifest("primary")
                primary["selection"] = {"modelPrefixes": ["clutter\\\\"]}
                _write_json(recipes / "primary.json", primary)
                changed = actor_route_manifest("primary")
            self.assertEqual(changed["sha256"], baseline["sha256"])

    def test_actor_route_and_recipe_changes_invalidate_actor_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            tools = root / "tools"
            recipes = root / "recipes"
            tools.mkdir()
            recipes.mkdir()
            primary = {
                "id": "primary",
                "actorRecipes": ["actor-a", "actor-b"],
                "linkedCellRecipes": [{"recipe": "linked"}],
            }
            _write_json(recipes / "primary.json", primary)
            _write_json(
                recipes / "linked.json",
                {"id": "linked", "actorRecipes": ["actor-c"]},
            )
            for actor_id in ("actor-a", "actor-b", "actor-c", "actor-d"):
                _write_json(recipes / f"{actor_id}.json", {"id": actor_id})
            with mock.patch(
                "compiler_provenance._roots",
                return_value=(tools, recipes),
            ):
                baseline = actor_route_manifest("primary")["sha256"]
                primary["actorRecipes"].reverse()
                _write_json(recipes / "primary.json", primary)
                reordered = actor_route_manifest("primary")["sha256"]
                primary["actorRecipes"].reverse()
                _write_json(recipes / "primary.json", primary)
                primary["actorRecipes"].pop()
                _write_json(recipes / "primary.json", primary)
                removed = actor_route_manifest("primary")["sha256"]
                primary["actorRecipes"] = ["actor-a", "actor-b", "actor-d"]
                _write_json(recipes / "primary.json", primary)
                added = actor_route_manifest("primary")["sha256"]
                primary["actorRecipes"] = ["actor-a", "actor-b"]
                _write_json(recipes / "primary.json", primary)
                _write_json(
                    recipes / "actor-a.json",
                    {"id": "actor-a", "artifactInput": "changed"},
                )
                recipe_changed = actor_route_manifest("primary")["sha256"]
            self.assertNotEqual(reordered, baseline)
            self.assertNotEqual(removed, baseline)
            self.assertNotEqual(added, baseline)
            self.assertNotEqual(recipe_changed, baseline)

    def test_opening_only_payload_change_changes_only_opening_and_actor_hashes(self):
        family_paths = {
            family: compiler_provenance_source_paths(family) for family in FAMILIES
        }
        opening_source = next(
            path for path in family_paths["opening"] if path.name == "opening_catalog.py"
        )
        with tempfile.TemporaryDirectory() as temporary:
            changed_opening = Path(temporary) / opening_source.name
            changed_opening.write_bytes(opening_source.read_bytes() + b"\n# opening-only proof\n")
            changed = {}
            for family, paths in family_paths.items():
                changed[family] = compiler_sources_sha256(
                    changed_opening if path == opening_source else path
                    for path in paths
                )
        baseline = {
            family: compiler_sources_sha256(paths)
            for family, paths in family_paths.items()
        }
        self.assertEqual(changed["static"], baseline["static"])
        self.assertEqual(changed["cell"], baseline["cell"])
        self.assertNotEqual(changed["opening"], baseline["opening"])
        self.assertNotEqual(changed["actor"], baseline["actor"])

    def test_stale_opening_reuses_hash_valid_world_families(self):
        identities = compiler_identities()
        install = {"dataRoot": "owned", "archiveStack": {"sha256": "a" * 64}}
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            model = root / "static.gltf"
            model.write_bytes(b"model")
            static_sidecar = root / "static.json"
            static_sidecar_hash = _write_json(
                static_sidecar,
                {"compiler": identities["families"]["static"]},
            )
            cell = root / "cell.json"
            cell_hash = _write_json(
                cell,
                {"compiler": identities["families"]["cell"], "recipe": "primary"},
            )
            linked = root / "linked.json"
            linked_hash = _write_json(
                linked,
                {"compiler": identities["families"]["cell"]},
            )
            opening = root / "opening.json"
            preview_set = root / "player-facegen-preview-set.json"
            preview_set_hash = _write_json(
                preview_set,
                {"schema": "opennv-owned-player-facegen-preview-set/v3"},
            )
            opening_hash = _write_json(
                opening,
                {
                    "compiler": identities["families"]["opening"],
                    "outputs": {
                        "playerFaceGenPreviewSet": {
                            "path": str(preview_set),
                            "sha256": preview_set_hash,
                        },
                    },
                },
            )
            actor = root / "actor.json"
            actor_hash = _write_json(
                actor,
                {"compiler": identities["families"]["actor"]},
            )
            actor_set = root / "actors.json"
            actor_set_hash = _write_json(
                actor_set,
                {
                    "compiler": identities["families"]["actor"],
                    "actors": [
                        {"recipe": "actor", "scene": str(actor), "sha256": actor_hash},
                    ],
                },
            )
            prior = {
                "schema": "opennv-legal-asset-cache/v1",
                "status": "prepared-legal-assets",
                "install": install,
                "compilerFamilies": identities["families"],
                "outputs": {
                    "model": str(model),
                    "modelSha256": hashlib.sha256(model.read_bytes()).hexdigest(),
                    "sidecar": str(static_sidecar),
                    "sidecarSha256": static_sidecar_hash,
                    "cellScene": str(cell),
                    "cellSceneSha256": cell_hash,
                    "linkedCellScenes": [
                        {"recipe": "linked", "scene": str(linked), "sha256": linked_hash},
                    ],
                    "openingManifest": str(opening),
                    "openingManifestSha256": opening_hash,
                    "openingPlayerFaceGenPreviewSet": str(preview_set),
                    "openingPlayerFaceGenPreviewSetSha256": preview_set_hash,
                    "actorScenes": str(actor_set),
                    "actorScenesSha256": actor_set_hash,
                },
            }
            changed = copy.deepcopy(identities)
            baseline = reusable_families(
                prior,
                install,
                identities,
                require_cell=True,
                require_actor=True,
                cell_recipe_id="primary",
                linked_recipe_ids=("linked",),
                actor_recipe_ids=("actor",),
            )
            self.assertTrue(all(baseline.values()))

            stale_preview = copy.deepcopy(prior)
            stale_preview["outputs"][
                "openingPlayerFaceGenPreviewSetSha256"
            ] = "0" * 64
            self.assertEqual(
                reusable_families(
                    stale_preview,
                    install,
                    identities,
                    require_cell=True,
                    require_actor=True,
                    cell_recipe_id="primary",
                    linked_recipe_ids=("linked",),
                    actor_recipe_ids=("actor",),
                ),
                {"static": True, "opening": False, "cell": True, "actor": True},
            )

            changed["families"]["opening"]["sha256"] = "b" * 64
            changed["families"]["actor"]["sha256"] = "c" * 64
            plan = reusable_families(
                prior,
                install,
                changed,
                require_cell=True,
                require_actor=True,
                cell_recipe_id="primary",
                linked_recipe_ids=("linked",),
                actor_recipe_ids=("actor",),
            )
            self.assertEqual(
                plan,
                {"static": True, "cell": True, "opening": False, "actor": False},
            )
            legacy = copy.deepcopy(prior)
            legacy.pop("compilerFamilies")
            self.assertFalse(
                any(
                    reusable_families(
                        legacy,
                        install,
                        identities,
                        require_cell=True,
                        require_actor=True,
                    ).values()
                )
            )


if __name__ == "__main__":
    unittest.main()
