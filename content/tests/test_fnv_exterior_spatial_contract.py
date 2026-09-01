import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class FnvExteriorSpatialContractTest(unittest.TestCase):
    def test_merged_owned_land_collision_is_two_sided_for_body_motion(self):
        loader = (
            ROOT / "runtime" / "src" / "World" / "Cells" / "CellContentLoader.cs"
        ).read_text(encoding="utf-8")
        method = loader[loader.index("private static void BuildLandscapeCollision") :]
        method = method[: method.index("private static void CreateDoubleSidedTrimeshCollision")]

        self.assertIn("shape is not ConcavePolygonShape3D concave", method)
        self.assertIn("concave.BackfaceCollision = true;", method)
        self.assertNotIn("new PlaneMesh", method)
        self.assertNotIn("new BoxShape3D", method)

    def test_cloud_culling_margin_is_derived_from_owned_mesh_bounds(self):
        renderer = (
            ROOT
            / "runtime"
            / "src"
            / "Presentation"
            / "Rendering"
            / "RetailEnvironmentRenderer.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("ExtraCullMargin = SourceGeometryCullMargin(mesh.Mesh)", renderer)
        self.assertIn("var bounds = mesh.GetAabb();", renderer)
        self.assertIn("var margin = bounds.Size.Length();", renderer)
        self.assertNotIn("CustomAabb", renderer)


if __name__ == "__main__":
    unittest.main()
