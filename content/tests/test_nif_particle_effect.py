import os
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "content" / "tools"
sys.path.insert(0, str(TOOLS))

from bsa_archive import BsaArchive  # noqa: E402
from export_nif_particle_effect import (  # noqa: E402
    PARTICLE_SCHEMA,
    compile_particle_effect,
    export_particle_nif,
    particle_scene_gltf,
)


class NifParticleEffectTests(unittest.TestCase):
    def test_particle_carrier_gltf_does_not_reference_zero_byte_buffer(self):
        scene = particle_scene_gltf(
            r"meshes\effects\nv\sanddust\sanddust02.nif",
            "0" * 64,
        )

        self.assertNotIn("buffers", scene)
        self.assertEqual(scene["nodes"], [{"name": "sanddust02"}])
        self.assertEqual(scene["scenes"], [{"nodes": [0]}])

    def test_runtime_uses_only_compiled_particle_fields(self):
        source = (ROOT / "runtime" / "src" / "Presentation" / "Rendering" /
                  "OwnedNifParticleEffect.cs").read_text(encoding="utf-8")
        self.assertIn('"opennv-owned-nif-particle-effect/v1"', source)
        self.assertIn('GetProperty("birthRatePerSecond")', source)
        self.assertIn('GetProperty("textureAssetId")', source)
        self.assertIn('GetProperty("modifiers")', source)
        self.assertNotIn("Goodsprings", source)
        self.assertNotIn("FXDust", source)
        exterior = (TOOLS / "exterior_scene.py").read_text(encoding="utf-8")
        self.assertIn('selection_reason == "special-effect-shader-required"', exterior)
        self.assertIn("particle_effect_model_paths=particle_effect_model_paths", exterior)

    @unittest.skipUnless(os.environ.get("FNV_INSTALL_ROOT"), "owned FNV install not configured")
    def test_owned_dust_graphs_compile_without_identity_cases(self):
        install = Path(os.environ["FNV_INSTALL_ROOT"])
        archive = BsaArchive(install / "Data" / "Fallout - Meshes.bsa")
        paths = (
            r"meshes\effects\ambient\fxdustwhirlwind01.nif",
            r"meshes\effects\nv\sanddust\sanddust02.nif",
        )
        contracts = [compile_particle_effect(archive.extract(path).data, path) for path in paths]
        self.assertTrue(all(row["schema"] == PARTICLE_SCHEMA for row in contracts))
        self.assertEqual([len(row["systems"]) for row in contracts], [2, 1])
        self.assertEqual(
            [system["emitter"]["shape"] for row in contracts for system in row["systems"]],
            ["mesh-surface", "mesh-surface", "box"],
        )
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            member = archive.extract(paths[0])
            source = root / "effect.nif"
            source.write_bytes(member.data)
            sidecar = export_particle_nif(source, paths[0], root / "effect.gltf",
                                          root / "effect.opennv.json")
            self.assertEqual(sidecar["coverage"]["surfaces"], 0)
            self.assertEqual(sidecar["coverage"]["sourcePoseBakedSkinSurfaces"], 0)
            self.assertEqual(sidecar["coverage"]["excludedEditorMarkerSurfaces"], [])
            self.assertEqual(sidecar["coverage"]["excludedNonPresentationSurfaces"], [])
            self.assertEqual(sidecar["particleEffect"]["systems"][0]["texturePath"],
                             r"textures\effects\fxwisps02.dds")


if __name__ == "__main__":
    unittest.main()
