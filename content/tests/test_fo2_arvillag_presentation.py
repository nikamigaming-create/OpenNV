from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class Fo2ArvillagPresentationTest(unittest.TestCase):
    def test_recipe_is_exact_map_bound_and_fail_closed_on_roofs(self) -> None:
        recipe = json.loads(
            (ROOT / "content/recipes/fo2-arvillag-relief-v1.json").read_text(
                encoding="utf-8"
            )
        )
        self.assertEqual(
            recipe["schema"], "opennv-fo2-arvillag-relief-recipe/v1"
        )
        self.assertEqual(recipe["map"]["index"], 4)
        self.assertEqual(recipe["map"]["name"], "ARVILLAG.MAP")
        self.assertEqual(
            recipe["map"]["sha256"],
            "0edcdff2afb6fac7e8203ce9eae8ba4663d37f3be112d3ef4713af3093d8d52a",
        )
        self.assertEqual(
            recipe["objectRelief3d"]["mode"],
            "exact-frm-alpha-island-molded-relief-v2",
        )
        self.assertFalse(recipe["roofCutaway"]["rendered"])
        self.assertFalse(recipe["policy"]["visualParityClaim"])
        self.assertFalse(recipe["policy"]["distributionAllowed"])

    def test_compiler_and_runtime_require_complete_owned_placement_closure(self) -> None:
        compiler = (
            ROOT / "content/tools/prepare_fo2_arvillag_presentation.py"
        ).read_text(encoding="utf-8")
        source = (ROOT / "content/tools/fo2_arvillag_slice.py").read_text(
            encoding="utf-8"
        )
        catalog = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArvillagPresentationContract.cs"
        ).read_text(encoding="utf-8")
        scene = (
            ROOT / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArvillagScene.cs"
        ).read_text(encoding="utf-8")
        player = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArroyoCavesPlayerRuntime.cs"
        ).read_text(encoding="utf-8")
        int_runtime = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArvillagIntRuntime.cs"
        ).read_text(encoding="utf-8")
        interaction = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArvillagInteractionRuntime.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("placement_serials | transparent != set(top_level)", compiler)
        self.assertIn("derive_relief(", compiler)
        self.assertIn("floorMaterialDepth3d", compiler)
        self.assertIn("expected_floor_ids", compiler)
        self.assertIn("roofCutawayBoundary", source)
        self.assertIn("arrivalWalkContract", source)
        self.assertIn("compile_map_int_initialization", source)
        self.assertIn('"villageIntRoles": village_int_roles', source)
        self.assertIn('"initialGlobalVariables"', source)
        self.assertIn('"critterStats"', source)
        self.assertIn('"mapEnterMetarules"', source)
        self.assertIn('"sha256": file_sha256(route_path)', source)
        self.assertIn("visibleSerials.Union(transparent)", catalog)
        self.assertIn("allowOwnedRoofCutaway: true", scene)
        self.assertIn("Fo2FrmReliefMesh.Build(", scene)
        self.assertIn("ApplyFloorMaterialDepth", scene)
        self.assertIn("BuildMoldedFloorPatch", scene)
        self.assertIn("BuildSharedFloorVertexHeights", scene)
        self.assertIn("SetInstanceCustomData", scene)
        self.assertIn("INSTANCE_CUSTOM", scene)
        self.assertIn("adjacent-owned-map-floor-average-luma-v1", scene)
        self.assertIn("source_floor_collision_unchanged", scene)
        self.assertIn("MaximumInteriorDistancePixels / sourcePixelsPerMeter", scene)
        self.assertIn("owned-frm-normal-depth-plus-exact-map-light", scene)
        self.assertIn("MAP4_EXACT_SOURCE_LIGHT_FIELDS", scene)
        self.assertIn("row.LightDistance", scene)
        self.assertIn("row.LightIntensity", scene)
        self.assertIn("OmniLight3D", scene)
        self.assertIn("MAP4_VERSIONED_CLASSIC_3D_PRESENTATION_ENVIRONMENT", scene)
        self.assertIn("var atmosphere = profile.Atmosphere", scene)
        self.assertIn("DirectionalLight3D", scene)
        self.assertIn("Fo2HumanoidVisual", player)
        self.assertIn("ApplyPresentationLighting", player)
        self.assertIn("_presentation.Visible = false", player)
        self.assertIn("destination_presentation_loaded", player)
        self.assertIn("ApplyVillageFirstAction", player)
        self.assertIn("ClassicIntEventDispatcher.Execute", int_runtime)
        self.assertIn("catalog.IntInitialization.ScriptSlots", int_runtime)
        self.assertIn("readRandomState", int_runtime)
        self.assertIn("commitRandomState", int_runtime)
        self.assertIn("Fo1HexMath.TileInDirection", interaction)
        self.assertIn("_scripts.Talk(role)", interaction)
        self.assertIn("_scripts.Choose(_activeRole", interaction)
        self.assertNotIn("proxy", scene.casefold())
        self.assertNotIn("capsule", scene.casefold())


if __name__ == "__main__":
    unittest.main()
