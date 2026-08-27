from __future__ import annotations

import hashlib
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from exterior_scene import (  # noqa: E402
    environment_sky_models,
    loaded_grid_coordinates,
    reference_grid,
    single_master_landscape_identity,
)
from prepare_exterior_cell import _verified_recipe_file, _verified_streaming  # noqa: E402
from runtime_configuration import load_runtime_configuration  # noqa: E402


class PrepareExteriorCellTest(unittest.TestCase):
    def test_cloud_layers_reuse_the_authored_cloud_dome_geometry(self) -> None:
        models = environment_sky_models(
            {
                "skyModels": {
                    "atmosphere": {
                        "path": "sky\\atmosphere.nif",
                        "surface": "AtmosphereDome:0",
                    },
                    "clouds": {
                        "path": "sky\\clouds.nif",
                        "cloudLayerSurface": "CloudDome:0",
                        "horizonClearSurface": "HorizonLayerClear:1",
                        "horizonOvercastSurface": "HorizonLayerOvercast:1",
                        "lowerLayerSurface": "LowerLayer:0",
                    },
                }
            }
        )

        self.assertEqual(
            models["clouds"]["surfaceRoutes"],
            [
                {"name": "CloudDome:0", "semantic": "weather-cloud-layer-geometry"},
                {"name": "HorizonLayerClear:1", "semantic": "horizon-clear"},
                {
                    "name": "HorizonLayerOvercast:1",
                    "semantic": "horizon-overcast",
                },
                {"name": "LowerLayer:0", "semantic": "lower-layer"},
            ],
        )

    def test_cloud_surface_routes_must_be_unique(self) -> None:
        with self.assertRaisesRegex(ValueError, "unique and nonempty"):
            environment_sky_models(
                {
                    "skyModels": {
                        "atmosphere": {
                            "path": "sky\\atmosphere.nif",
                            "surface": "AtmosphereDome:0",
                        },
                        "clouds": {
                            "path": "sky\\clouds.nif",
                            "cloudLayerSurface": "CloudDome:0",
                            "horizonClearSurface": "CloudDome:0",
                            "horizonOvercastSurface": "HorizonLayerOvercast:1",
                            "lowerLayerSurface": "LowerLayer:0",
                        },
                    }
                }
            )

    def test_retail_loaded_grid_is_centered_and_odd(self) -> None:
        coordinates = loaded_grid_coordinates((-18, 0), 5)
        self.assertEqual(len(coordinates), 25)
        self.assertEqual(coordinates[0], (-20, -2))
        self.assertEqual(coordinates[-1], (-16, 2))
        self.assertIn((-18, 0), coordinates)
        with self.assertRaisesRegex(ValueError, "positive odd"):
            loaded_grid_coordinates((-18, 0), 4)

    def test_reference_grid_uses_floor_for_negative_world_coordinates(self) -> None:
        cell_size = (
            load_runtime_configuration().content_compiler.exterior_cell_size_game_units
        )
        self.assertEqual(reference_grid((-69632.0, 0.0, 0.0), cell_size), (-17, 0))
        self.assertEqual(reference_grid((-69632.1, -0.1, 0.0), cell_size), (-18, -1))

    def test_single_master_landscape_identity_is_stable_and_source_owned(self) -> None:
        identity = single_master_landscape_identity(
            Path("FalloutNV.esm"),
            SimpleNamespace(
                form_id=0x00123456,
                cell_form_id=0x00000040,
                worldspace_form_id=0x00000050,
            ),
        )
        self.assertEqual(identity.form_key, "FalloutNV.esm:123456")
        self.assertEqual(identity.cell_form_key, "FalloutNV.esm:000040")
        self.assertEqual(identity.worldspace_form_key, "FalloutNV.esm:000050")
        self.assertEqual(identity.source_plugin, "FalloutNV.esm")
        self.assertEqual(identity.source_local_form_id, "00123456")
        with self.assertRaisesRegex(ValueError, "non-local plugin namespace"):
            single_master_landscape_identity(
                Path("FalloutNV.esm"),
                SimpleNamespace(
                    form_id=0x01123456,
                    cell_form_id=0x00000040,
                    worldspace_form_id=0x00000050,
                ),
            )

    def test_streaming_setting_is_verified_from_hash_pinned_retail_ini(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            ini = root / "Fallout_default.ini"
            ini.write_text("[General]\nuGridsToLoad=5\n", encoding="utf-8")
            streaming = {
                "mode": "retail-ini",
                "loadedGridDiameter": 5,
                "source": {
                    "file": ini.name,
                    "sha256": hashlib.sha256(ini.read_bytes()).hexdigest(),
                },
                "section": "General",
                "key": "uGridsToLoad",
            }
            evidence = _verified_streaming(root, streaming)
            self.assertEqual(evidence["observedValue"], 5)
            mismatched = dict(streaming)
            mismatched["loadedGridDiameter"] = 3
            with self.assertRaisesRegex(ValueError, "setting mismatch"):
                _verified_streaming(root, mismatched)

    def test_source_identity_is_case_insensitive_and_hash_verified(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "FalloutNV.esm"
            source.write_bytes(b"owned-test-source")
            expected = hashlib.sha256(source.read_bytes()).hexdigest()

            path, actual = _verified_recipe_file(
                root,
                {"file": "falloutnv.ESM", "sha256": expected},
            )

            self.assertEqual(path, source)
            self.assertEqual(actual, expected)

    def test_source_identity_rejects_hash_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "FalloutNV.esm").write_bytes(b"owned-test-source")

            with self.assertRaisesRegex(ValueError, "Owned source hash mismatch"):
                _verified_recipe_file(
                    root,
                    {"file": "FalloutNV.esm", "sha256": "0" * 64},
                )


if __name__ == "__main__":
    unittest.main()
