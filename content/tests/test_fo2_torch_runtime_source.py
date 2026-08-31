from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MOLDED = (
    ROOT
    / "runtime"
    / "src"
    / "Campaigns"
    / "Fallout2"
    / "Temple"
    / "Fo2ArroyoCavesMoldedPresentation.cs"
)
RELIEF = MOLDED.with_name("Fo2FrmReliefMesh.cs")
PROOF = MOLDED.with_name("Fo2ArroyoCavesPlayProof.cs")
TOPOLOGY = MOLDED.with_name("Fo2TempleTopology.cs")
PROFILE = ROOT / "runtime" / "config" / "fo2-arroyo-caves-3d-v1.json"
RELIEF_PRODUCER = ROOT / "content" / "tools" / "fo2_frm_relief.py"
RELIEF_RECIPE = ROOT / "content" / "recipes" / "fo2-arroyo-caves-molded-surface-v1.json"


class Fo2TorchRuntimeSourceTest(unittest.TestCase):
    def test_torch_keeps_full_source_alpha_without_a_guessed_emitter_or_halo(self) -> None:
        molded = MOLDED.read_text(encoding="utf-8")
        relief = RELIEF.read_text(encoding="utf-8")
        proof = PROOF.read_text(encoding="utf-8")
        producer = RELIEF_PRODUCER.read_text(encoding="utf-8")
        recipe = RELIEF_RECIPE.read_text(encoding="utf-8")

        self.assertIn("placement.PixelOffset + artifact.FrameOffset", molded)
        self.assertIn("reliefPlacement.Frame != placement.Frame", molded)
        self.assertIn("reliefPlacement.Fid != placement.Fid", molded)
        self.assertIn("reliefPlacement.Pid != placement.Pid", molded)
        self.assertIn("sourcePixelsOnly: isTorch", molded)
        self.assertIn("fo2_torch_visual", molded)
        self.assertIn("source-world-relief-never-billboard", molded)
        self.assertIn("BuildSourceAlphaFaces", relief)
        self.assertIn("sourceImage.GetPixel(x, y).A <= 0.0f", relief)
        self.assertIn("BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled", relief)
        self.assertNotIn("TorchEmitter", relief)
        self.assertNotIn("Emission", relief)
        self.assertNotIn("torchEmitter", producer)
        self.assertNotIn("torchEmitter", recipe)
        self.assertNotIn("Emitter", proof)

    def test_source_map_light_field_remains_map_bound(self) -> None:
        molded = MOLDED.read_text(encoding="utf-8")
        map_lights = molded.split("private static SourceMapLightCoverage", 1)[1].split(
            "private static void BuildEnvironment", 1
        )[0]

        self.assertIn("Fo1HexMath.Distance(light.Tile, torch.Tile) == 1", map_lights)
        self.assertIn('Name = "SOURCE_MAP_LIGHT_FIELD"', map_lights)
        self.assertIn("LightEnergy = (float)placement.LightIntensity", map_lights)
        self.assertIn("OmniRange = placement.LightDistance", map_lights)
        self.assertNotIn("LightColor =", map_lights)

    def test_cave_floor_and_atmosphere_use_the_molded_fo1_quality_layers(self) -> None:
        molded = MOLDED.read_text(encoding="utf-8")
        profile = PROFILE.read_text(encoding="utf-8")

        self.assertIn("profile.SubdivisionsPerAxis", molded)
        self.assertIn("VolumetricFogEnabled = true", molded)
        self.assertIn('"subdivisionsPerAxis": 4', profile)
        self.assertIn('"volumetricFogDensity"', profile)
        self.assertIn("owned_fine_detail", molded)
        self.assertIn("owned_macro_detail", molded)
        self.assertIn("world_position * macro_detail_world_scale", molded)
        self.assertIn(
            '"opennv-world-space-owned-frm-multiscale-triplanar-albedo-normal-rock/v4"',
            profile,
        )

    def test_no_procedural_walk_overlay_disc_is_routed_into_temple_or_arroyo(self) -> None:
        molded = MOLDED.read_text(encoding="utf-8")
        topology = TOPOLOGY.read_text(encoding="utf-8")

        self.assertNotIn("CylinderMesh", molded)
        self.assertNotIn("CylinderMesh", topology)
        self.assertNotIn("BuildWalkOverlay", molded)
        self.assertNotIn("BuildWalkOverlay", topology)
        self.assertNotIn("walkOverlay", PROFILE.read_text(encoding="utf-8"))
        self.assertIn("SourceTorchAssemblies", molded)
        self.assertIn("exact-source-frm-alpha-pixels-no-halo", molded)


if __name__ == "__main__":
    unittest.main()
