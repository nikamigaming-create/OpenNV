import unittest
import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_legal_assets import (  # noqa: E402
    discover_effective_exterior_actors,
    load_actor_recipe,
)


class EffectiveActorDiscoveryTest(unittest.TestCase):
    def test_named_actor_recipes_use_the_actor_schema_loader(self):
        recipe = load_actor_recipe("goodsprings-doc-mitchell-actor-v1")

        self.assertEqual(recipe["schema"], "opennv-actor-recipe/v1")
        self.assertEqual(recipe["cellRecipe"], "goodsprings-doc-mitchell-house-v1")

    def test_loaded_cells_and_spatially_relevant_persistent_achrs_are_discovered(self):
        persistent = 0x100
        source_cell = 0x200

        def reference(
            form_id: int,
            record_type: str,
            cell_form_id: int,
            position: tuple[float, float, float],
            parent: int | None = None,
        ) -> SimpleNamespace:
            return SimpleNamespace(
                form_id=form_id,
                record_type=record_type,
                cell_form_id=cell_form_id,
                actor_form_id=form_id + 0x1000,
                position=position,
                enable_parent_form_id=parent,
            )

        included_persistent = reference(0x10, "ACHR", persistent, (4.0, 8.0, 0.0))
        excluded_persistent = reference(0x11, "ACHR", persistent, (4100.0, 8.0, 0.0))
        included_cell = reference(0x12, "ACHR", source_cell, (9000.0, 9000.0, 0.0), 0x30)
        excluded_creature = reference(0x13, "ACRE", source_cell, (4.0, 8.0, 0.0))
        catalog = SimpleNamespace(
            references=[
                included_persistent,
                excluded_persistent,
                included_cell,
                excluded_creature,
            ],
            actors={
                row.actor_form_id: object()
                for row in (included_persistent, excluded_persistent, included_cell)
            },
        )
        recipe = {
            "id": "source-exterior",
            "actorDiscovery": {"mode": "effective-achr"},
            "persistentCellFormId": f"{persistent:08x}",
            "master": {"file": "master", "sha256": "hash"},
            "meshesArchive": {"file": "meshes", "sha256": "hash"},
            "textureArchives": [],
        }
        scene = {
            "cell": {
                "sourceCellFormIds": [f"{persistent:08x}", f"{source_cell:08x}"],
            },
            "coordinates": {
                "loadedCellGrids": [[0, 0]],
                "originGameUnits": [1.0, 2.0, 3.0],
            },
            "coverage": {"lod": {"cellSizeGameUnits": 4096.0}},
        }

        with patch(
            "prepare_legal_assets.scan_actor_catalog",
            return_value=catalog,
        ), patch(
            "prepare_legal_assets.iter_plugin_records",
            return_value=[SimpleNamespace(form_id=0x30, flags=0x00000800)],
        ):
            documents = discover_effective_exterior_actors(
                Path("master"), recipe, scene
            )

        self.assertEqual(
            [row["proofActorReferenceFormId"] for row in documents],
            ["00000010", "00000012"],
        )
        self.assertIsNone(documents[0]["enableParentInitiallyDisabled"])
        self.assertTrue(documents[1]["enableParentInitiallyDisabled"])
        self.assertEqual(documents[1]["originGameUnits"], [1.0, 2.0, 3.0])


if __name__ == "__main__":
    unittest.main()
