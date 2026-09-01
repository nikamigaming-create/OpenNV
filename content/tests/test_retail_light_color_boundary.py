import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
RETAIL_LIGHTING = (
    ROOT / "runtime" / "src" / "Presentation" / "Rendering" / "RetailLighting.cs"
)


class RetailLightColorBoundaryTests(unittest.TestCase):
    def test_shared_light_boundary_preserves_source_shader_constant(self):
        source = RETAIL_LIGHTING.read_text(encoding="utf-8")
        self.assertIn("internal static Color GodotLightColor", source)
        self.assertIn("sourceShaderColor.LinearToSrgb()", source)
        self.assertEqual(source.count("float.IsFinite(sourceShaderColor."), 4)

    def test_owned_gamebryo_lights_use_shared_property_boundary(self):
        expected = {
            ROOT
            / "runtime"
            / "src"
            / "Presentation"
            / "Rendering"
            / "RetailEnvironmentRenderer.cs": 1,
            ROOT
            / "runtime"
            / "src"
            / "World"
            / "Cells"
            / "CellSceneLoader.cs": 2,
            ROOT
            / "runtime"
            / "src"
            / "Content"
            / "StaticCellCompileLoader.cs": 2,
            ROOT
            / "runtime"
            / "src"
            / "Diagnostics"
            / "Capture"
            / "ActorReviewCapture.cs": 1,
            ROOT
            / "runtime"
            / "src"
            / "Presentation"
            / "CharacterCreation"
            / "OwnedGamebryoFaceGenPreviewHost.cs": 1,
            ROOT
            / "runtime"
            / "src"
            / "Campaigns"
            / "NewVegas"
            / "Opening"
            / "OpeningRaceSexRenderedDeviceHost.cs": 1,
        }
        for path, count in expected.items():
            with self.subTest(path=path):
                source = path.read_text(encoding="utf-8")
                self.assertEqual(
                    source.count("RetailLighting.GodotLightColor("), count
                )


if __name__ == "__main__":
    unittest.main()
