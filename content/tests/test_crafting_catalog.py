from __future__ import annotations

import struct
import unittest

from crafting_catalog import (
    CraftingCatalog,
    decode_category,
    decode_recipe,
    recipe_menu_category_editor_id,
)
from cell_catalog import BaseObject, CellCatalog, PlacedReference, ScriptSource, Transform
from plugin_records import Record
from scene_asset_pipeline import interaction_manifest


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, rows: list[tuple[str, bytes]]) -> Record:
    return Record(
        signature,
        form_id,
        0,
        b"".join(subrecord(name, data) for name, data in rows),
        (),
    )


class CraftingCatalogTest(unittest.TestCase):
    def test_decodes_exact_healing_powder_relationship(self) -> None:
        category = decode_category(
            record(
                "RCCT",
                0x0013B2C0,
                [("EDID", b"CampfireRecipes\0"), ("FULL", b"Campfire\0"), ("DATA", b"\xfe")],
            )
        )
        recipe = decode_recipe(
            record(
                "RCPE",
                0x0013B2C2,
                [
                    ("EDID", b"RecipeHealingPowder\0"),
                    ("FULL", b"Healing Powder\0"),
                    ("DATA", struct.pack("<iiII", -1, 0, category.form_id, 0x001613CF)),
                    ("RCIL", struct.pack("<I", 0x0013B2B9)),
                    ("RCQY", struct.pack("<I", 1)),
                    ("RCIL", struct.pack("<I", 0x0013B2BA)),
                    ("RCQY", struct.pack("<I", 1)),
                    ("RCOD", struct.pack("<I", 0x00136A1D)),
                    ("RCQY", struct.pack("<I", 1)),
                ],
            )
        )
        catalog = CraftingCatalog({category.form_id: category}, {recipe.form_id: recipe})

        self.assertEqual("CampfireRecipes", category.editor_id)
        self.assertIsNone(recipe.skill_actor_value_form_id)
        self.assertEqual(0, recipe.required_skill_level)
        self.assertEqual(
            ((0x0013B2B9, 1), (0x0013B2BA, 1)),
            tuple((item.item_form_id, item.count) for item in recipe.ingredients),
        )
        self.assertEqual(
            ((0x00136A1D, 1),),
            tuple((item.item_form_id, item.count) for item in recipe.outputs),
        )
        self.assertEqual((recipe,), catalog.recipes_for_category(category.form_id))

    def test_rejects_quantity_without_preceding_item(self) -> None:
        source = record(
            "RCPE",
            1,
            [
                ("EDID", b"Recipe\0"),
                ("FULL", b"Recipe\0"),
                ("DATA", struct.pack("<iiII", -1, 0, 2, 0)),
                ("RCQY", struct.pack("<I", 1)),
            ],
        )
        with self.assertRaisesRegex(ValueError, "orphan quantity"):
            decode_recipe(source)

    def test_rejects_unknown_record_sizes(self) -> None:
        source = record(
            "RCCT",
            1,
            [("EDID", b"Category\0"), ("FULL", b"Category\0"), ("DATA", b"\0\0")],
        )
        with self.assertRaisesRegex(ValueError, "unsupported DATA layout"):
            decode_category(source)

    def test_station_script_resolves_one_source_category(self) -> None:
        source = "Begin OnActivate\n player.ShowRecipeMenu CampfireRecipes\nEnd"
        self.assertEqual("CampfireRecipes", recipe_menu_category_editor_id(source))
        with self.assertRaisesRegex(ValueError, "exactly one"):
            recipe_menu_category_editor_id("Begin OnActivate\nEnd")

    def test_station_manifest_exposes_only_supported_source_recipe(self) -> None:
        category = decode_category(
            record(
                "RCCT",
                0x0013B2C0,
                [("EDID", b"CampfireRecipes\0"), ("FULL", b"Campfire\0"), ("DATA", b"\xfe")],
            )
        )
        recipe = decode_recipe(
            record(
                "RCPE",
                0x0013B2C2,
                [
                    ("EDID", b"RecipeHealingPowder\0"),
                    ("FULL", b"Healing Powder\0"),
                    ("DATA", struct.pack("<iiII", -1, 0, category.form_id, 0)),
                    ("RCIL", struct.pack("<I", 0x0013B2B9)),
                    ("RCQY", struct.pack("<I", 1)),
                    ("RCIL", struct.pack("<I", 0x0013B2BA)),
                    ("RCQY", struct.pack("<I", 1)),
                    ("RCOD", struct.pack("<I", 0x00136A1D)),
                    ("RCQY", struct.pack("<I", 1)),
                ],
            )
        )
        station = BaseObject(0x0013E3FA, "ACTI", "CampfireCrafting01", None, "Campfire", 0x0013E3FB)
        items = {
            0x0013B2B9: BaseObject(0x0013B2B9, "ALCH", "XanderRoot", None, "Xander Root"),
            0x0013B2BA: BaseObject(0x0013B2BA, "ALCH", "BrocFlower", None, "Broc Flower"),
            0x00136A1D: BaseObject(0x00136A1D, "ALCH", "NVHealingPowder", None, "Healing Powder"),
            station.form_id: station,
        }
        catalog = CellCatalog(
            {}, {}, items, {0x0013E3FB: ScriptSource(0x0013E3FB, "CraftingCampfireRecipesScript", "player.ShowRecipeMenu CampfireRecipes")},
            {}, {}, {}, {}, {}, [], crafting_categories={category.form_id: category},
            crafting_recipes={recipe.form_id: recipe},
        )
        reference = PlacedReference(
            0x000CDB59, 1, station.form_id, 0, Transform((0, 0, 0), (0, 0, 0)), 1.0, None, None
        )

        interaction = interaction_manifest(reference, station, catalog)

        self.assertIsNotNone(interaction)
        assert interaction is not None
        self.assertEqual("crafting-station", interaction["type"])
        self.assertEqual("CampfireRecipes", interaction["category"]["editorId"])
        self.assertEqual(["RecipeHealingPowder"], [row["editorId"] for row in interaction["recipes"]])
        self.assertEqual(
            ["XanderRoot", "BrocFlower"],
            [row["itemEditorId"] for row in interaction["recipes"][0]["ingredients"]],
        )


if __name__ == "__main__":
    unittest.main()
