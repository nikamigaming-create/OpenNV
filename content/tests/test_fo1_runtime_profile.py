from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
RECIPES = ROOT / "content" / "recipes"
RUNTIME = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1RuntimeProfileTest(unittest.TestCase):
    def test_scene_recipe_hash_pins_complete_runtime_profile(self) -> None:
        scene = json.loads(
            (RECIPES / "fo1-v13ent-hex-slice-v1.json").read_text(encoding="utf-8")
        )
        reference = scene["runtimeProfile"]
        self.assertEqual(set(reference), {"path", "sha256"})
        profile_path = (RECIPES / reference["path"]).resolve()
        self.assertTrue(profile_path.is_relative_to(RECIPES.resolve()))
        payload = profile_path.read_bytes()
        self.assertEqual(hashlib.sha256(payload).hexdigest(), reference["sha256"])

        profile = json.loads(payload)
        self.assertEqual(
            profile["schema"], "opennv-fo1-runtime-profile-recipe/v1"
        )
        self.assertEqual(
            set(profile),
            {
                "schema",
                "id",
                "authority",
                "generationAdaptation",
                "scenePresentation",
                "camera",
                "gameplayAdaptation",
                "combatPresentation",
                "mobPresentation",
                "cutaway",
                "showcase",
            },
        )
        self.assertIn("MAP/FRM/PRO/GCD/DAT", profile["authority"]["fallout1"])
        self.assertIn("presentation donors only", profile["authority"]["falloutNewVegas"])
        self.assertIn("configurable adaptations", profile["authority"]["openNvAdaptation"])
        self.assertGreater(profile["combatPresentation"]["impactRadiusMeters"], 0.0)
        self.assertLessEqual(profile["combatPresentation"]["impactRadiusMeters"], 0.015)
        self._assert_finite(profile)

    def test_equipment_and_hud_are_source_symbol_driven(self) -> None:
        session = (RUNTIME / "Fo1TacticalSession.cs").read_text(encoding="utf-8")
        hud = (RUNTIME / "Fo1ClassicHud.cs").read_text(encoding="utf-8")
        inventory = (RUNTIME / "Fo1ClassicInventoryScreen.cs").read_text(
            encoding="utf-8"
        )
        contract = (RUNTIME / "Fo1CharacterStartContract.cs").read_text(
            encoding="utf-8"
        )
        creator = (RUNTIME / "Fo1CharacterCreator.cs").read_text(encoding="utf-8")

        self.assertNotIn("SetHeldWeapon", session)
        self.assertIn("equippedWeaponSymbol", session)
        self.assertIn("SwapEquippedWeapon", session)
        self.assertIn("EquipInventoryWeapon", session)
        self.assertIn("_playerProfile.Inventory.EquippedRangedSymbol", session)
        self.assertIn("_playerProfile.Inventory.EquippedMeleeSymbol", session)
        self.assertIn("equippedWeaponSymbol = EquippedWeaponSymbol", session)
        self.assertIn('root.TryGetProperty("equippedWeaponSymbol"', session)
        self.assertEqual(session.count("ApplyOwnedPlayerLighting("), 2)
        self.assertEqual(session.count("ApplyOwnedWeaponLighting("), 3)
        self.assertEqual(session.count("CountStandardLitMaterials("), 2)
        self.assertIn("RuntimeMaterialLoader.ApplyRetailActorLighting(", session)
        self.assertIn(
            "RuntimeMaterialLoader.ApplyRetailAmbientDirectionalLighting(", session
        )
        self.assertIn("atmosphere.AmbientEnergy", session)
        self.assertEqual(
            session.count(
                "_runtimeProfile.Camera.Tactical.FarClipMeters / unitsToMeters"
            ),
            2,
        )
        self.assertNotIn(
            "atmosphere.VolumetricFogLengthMeters / unitsToMeters", session
        )
        self.assertEqual(session.count("BindOwnedPlayerMaterialTextures("), 2)
        self.assertIn('shader.SetShaderParameter("base_map"', session)
        self.assertIn('shader.SetShaderParameter("normal_map"', session)
        self.assertIn("shader.GetShaderParameter(parameter).AsGodotObject()", session)
        self.assertIn("readback.GetRid() != expected.GetRid()", session)
        self.assertIn("SHA256.HashData(image.GetData())", session)
        self.assertIn("OPENNV_FO1_OWNED_ACTOR_TEXTURE_BINDING_PASS", session)
        self.assertIn("VerifiedGltfLoader.VerifyHash(path", session)
        self.assertIn("litMaterials = _ownedPlayerLitMaterials", session)
        self.assertIn("equipmentChangedCount = EquipmentChangedCount", inventory)
        self.assertIn('gameplayMutation = "source-symbol-equipment-only"', inventory)
        self.assertNotIn('ItemInventory("PID_10MM_PISTOL")', inventory)
        self.assertNotIn('ItemInventory("PID_KNIFE")', inventory)
        self.assertIn("WeaponInventoryBySymbol", hud + contract)
        self.assertNotIn('_images["weaponInventory"]', hud)
        self.assertIn("UpdateCreatorNumbers", creator)
        self.assertNotIn("_proofBadge", creator)

    def test_adaptation_values_do_not_return_to_core_consumers(self) -> None:
        camera = (RUNTIME / "Fo1TacticalCamera.cs").read_text(encoding="utf-8")
        session = (RUNTIME / "Fo1TacticalSession.cs").read_text(encoding="utf-8")
        cutaway = (RUNTIME / "Fo1CaveCutaway.cs").read_text(encoding="utf-8")
        mob = (RUNTIME / "Fo1Mob.cs").read_text(encoding="utf-8")
        loader = (RUNTIME / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")
        generator = (ROOT / "content" / "tools" / "prepare_fo1_hex_scene.py").read_text(
            encoding="utf-8"
        )

        for forbidden in (
            "internal const float FirstPerson",
            "internal const float MinimumSizeMeters",
            "private const float EdgeMarginPixels",
            "MathF.Min(distanceMeters, 0.30f)",
            "_fpsShotCooldownSeconds = 0.22",
            "var movement = Math.Min(3",
            "TacticalEnvelopeCutHeightMeters =",
            "return role switch",
            "FO1_V13ENT_HEX_ROOT",
            'playerActor.Value.FormId != "00104f09"',
        ):
            self.assertNotIn(forbidden, camera + session + cutaway + mob + loader)

        for forbidden in (
            '(int(row["serial"]) * 47)',
            '"name": "Giant Rat"',
            '"boundaryHeightMeters": 3.6',
            '"staticWorldYawDegrees": -45.0',
            '"homeSizeMeters": 22.0',
        ):
            self.assertNotIn(forbidden, generator)

    def test_cutaway_uses_only_source_labelled_roof_visibility(self) -> None:
        cutaway = (RUNTIME / "Fo1CaveCutaway.cs").read_text(encoding="utf-8")
        cave = (RUNTIME / "Fo1OwnedCaveKit.cs").read_text(encoding="utf-8")

        self.assertIn('"fo1_source_tactical_visibility"', cutaway)
        self.assertIn('"hide-roof-envelope"', cutaway)
        self.assertIn("_camera.Projection == Camera3D.ProjectionType.Orthogonal", cutaway)
        self.assertIn("internal int MeltMaterials => 0;", cutaway)
        self.assertNotIn("ShaderMaterial", cutaway)
        self.assertNotIn("BuildMeltShader", cutaway)
        self.assertIn("BuildCaveEnvelopeMeshes", cave)
        self.assertIn("CAVE_terrain-envelope-source-roof", cave)
        self.assertIn("CAVE_terrain-envelope-source-boundary", cave)
        self.assertIn('"preserve-boundary-envelope"', cave)

    def test_owned_player_and_continuous_cave_surface_use_source_bound_floor_contracts(self) -> None:
        session = (RUNTIME / "Fo1TacticalSession.cs").read_text(encoding="utf-8")
        loader = (RUNTIME / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")
        cave = (RUNTIME / "Fo1OwnedCaveKit.cs").read_text(encoding="utf-8")

        self.assertIn("ownedCave.GroundingFloorHeightMeters", loader)
        self.assertIn("_ownedPlayerFloorHeightMeters", session)
        self.assertIn("sourceBoundFloorHeightMeters", session)
        self.assertGreaterEqual(cave.count("TextureRepeat = true"), 3)
        self.assertIn("ApplyRetailAmbientDirectionalLighting(", loader)
        self.assertIn("ownedCave = ownedCave with { LitMaterials = litMaterials }", loader)

    def test_runtime_consumers_have_one_profile_owner(self) -> None:
        owners = {
            path.name
            for path in RUNTIME.glob("Fo1*.cs")
            if "opennv-fo1-runtime-profile-recipe/v1"
            in path.read_text(encoding="utf-8")
        }
        self.assertEqual(owners, {"Fo1RuntimeProfile.cs"})
        consumers = {
            "Fo1HexSceneLoader.cs": "Fo1RuntimeProfile",
            "Fo1TacticalCamera.cs": "Fo1CameraProfile",
            "Fo1TacticalSession.cs": "Fo1RuntimeProfile",
            "Fo1Mob.cs": "Fo1RuntimeProfile",
            "Fo1CaveCutaway.cs": "Fo1CutawayProfile",
        }
        for consumer, expected_type in consumers.items():
            self.assertIn(
                expected_type,
                (RUNTIME / consumer).read_text(encoding="utf-8"),
            )

    def _assert_finite(self, value: object) -> None:
        if isinstance(value, dict):
            for child in value.values():
                self._assert_finite(child)
        elif isinstance(value, list):
            for child in value:
                self._assert_finite(child)
        elif isinstance(value, float):
            self.assertTrue(math.isfinite(value))


if __name__ == "__main__":
    unittest.main()
