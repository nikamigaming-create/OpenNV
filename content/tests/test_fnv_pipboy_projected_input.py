import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class FnvPipBoyProjectedInputTest(unittest.TestCase):
    def test_owned_pipboy_routes_pointer_through_source_screen_uvs(self):
        controller = (
            ROOT / "runtime" / "src" / "Presentation" / "Ui" /
            "GameplayUiController.cs"
        ).read_text(encoding="utf-8")
        router = (
            ROOT / "runtime" / "src" / "Presentation" / "Ui" /
            "ProjectedSurfaceInputRouter.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("OwnedPipBoyProjectedScreenInput", controller)
        self.assertIn("_ownedPipBoyInput?.Forward(input)", controller)
        self.assertIn("mesh.SurfaceGetArrays(_surface)", router)
        self.assertIn("_camera.UnprojectPosition", router)
        self.assertIn("textureCoordinates[first] * weights.X", router)
        self.assertIn("_target.PushInput(forwarded, true)", router)
        self.assertNotIn("Rect2(", router)

    def test_owned_pipboy_does_not_double_decode_live_crt_viewport(self):
        controller = (
            ROOT / "runtime" / "src" / "Presentation" / "Ui" /
            "GameplayUiController.cs"
        ).read_text(encoding="utf-8")

        material_start = controller.index(
            'ResourceName = "OpenNV_OwnedPipBoyDynamicCrt"'
        )
        material_end = controller.index("});", material_start)
        material = controller[material_start:material_end]
        self.assertIn("AlbedoTextureForceSrgb = false", material)
        self.assertNotIn("AlbedoTextureForceSrgb = true", material)


if __name__ == "__main__":
    unittest.main()
