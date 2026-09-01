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

    def test_night_sky_uses_owned_texture_and_projection_safe_geometry(self):
        renderer = (
            ROOT
            / "runtime"
            / "src"
            / "Presentation"
            / "Rendering"
            / "RetailEnvironmentRenderer.cs"
        ).read_text(encoding="utf-8")

        night = renderer[renderer.index("private const string NightSkyShaderSource") :]
        night = night[: night.index("internal static Application")]
        self.assertIn("uniform sampler2D star_map", night)
        self.assertIn("POSITION = PROJECTION_MATRIX", night)
        self.assertIn("uniform vec4 stars_encoded", night)
        self.assertIn('environment.SkyModels["nightSky"]', renderer)
        self.assertNotIn("Goodsprings", night)

    def test_climate_weather_selection_is_source_member_and_explicit_hour(self):
        source = (
            ROOT
            / "runtime"
            / "src"
            / "Presentation"
            / "Rendering"
            / "RetailExteriorEnvironment.cs"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "ResolveClimateWeather(uint weatherFormId, float gameHour)", source
        )
        self.assertIn(
            "Climate.WeatherEntries.Any(entry => entry.WeatherFormId == weatherFormId)",
            source,
        )
        self.assertNotIn("Goodsprings", source)

    def test_road_collision_consumes_compiler_face_selection_not_editor_ids(self):
        loader = (
            ROOT / "runtime" / "src" / "World" / "Cells" / "CellContentLoader.cs"
        ).read_text(encoding="utf-8")

        self.assertIn('collision.GetProperty("faceSelection")', loader)
        self.assertIn(
            'collisionFaceSelections[assetId] == "source-upward-walkable-deck"',
            loader,
        )
        self.assertNotIn('baseEditorId.StartsWith(\n                            "WastelandRoad"', loader)


if __name__ == "__main__":
    unittest.main()
