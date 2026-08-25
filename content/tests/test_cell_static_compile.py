from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from io import BytesIO
from pathlib import Path

from PIL import Image


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from bsa_archive import ExtractedMember, canonical_member_path  # noqa: E402
from cell_static_compile import compile_model, texture_row  # noqa: E402
from cell_static_contract import (  # noqa: E402
    cell_origin,
    child_transform,
    compiled_light_contract,
    default_profile_path,
    load_profile,
    mesh_member_path,
    stable_exception_detail,
)
from content.tests.test_static_nif_gltf import write_synthetic_nif  # noqa: E402
from corpus_io import atomic_json, output_descriptor  # noqa: E402
from plugin_stack import file_sha256  # noqa: E402
from runtime_configuration import load_runtime_configuration  # noqa: E402
from material_contract import material_bindings  # noqa: E402
from texture_pipeline import OwnedTexturePipeline  # noqa: E402
from cell_static_resource_validate import validate_relative_file  # noqa: E402
from validate_cell_static_compile import validate_json_descriptor  # noqa: E402


class SingleMemberArchive:
    def __init__(self, logical_path: str, payload: bytes):
        self.logical_path = canonical_member_path(logical_path)
        self.payload = payload

    def extract(self, logical_path: str) -> ExtractedMember:
        requested = canonical_member_path(logical_path)
        if requested != self.logical_path:
            raise FileNotFoundError(requested)
        return ExtractedMember(
            requested,
            self.payload,
            False,
            0,
            len(self.payload),
            "Synthetic.bsa",
            "a" * 64,
        )


class CellStaticCompileTest(unittest.TestCase):
    def test_profile_and_coordinate_contract_are_explicit(self) -> None:
        profile = load_profile(default_profile_path())

        self.assertEqual(
            cell_origin({"interior": True, "formKey": "test"}, profile),
            (0.0, 0.0, 0.0),
        )
        self.assertEqual(
            cell_origin(
                {"interior": False, "formKey": "test", "coordinates": [2, -3]},
                profile,
            ),
            (8192.0, -12288.0, 0.0),
        )
        self.assertEqual(mesh_member_path("Dungeons/Test.NIF"), "meshes\\dungeons\\test.nif")
        self.assertEqual(
            profile["presentationPolicies"]["LIGH"]["kind"],
            "point-light",
        )
        self.assertEqual(
            profile["childPresentationPolicies"]["LAND"]["kind"],
            "landscape",
        )
        self.assertEqual(
            set(profile["supportedBaseRecordTypes"]),
            {"ACTI", "CONT", "DOOR", "LIGH", "MSTT", "SCOL", "STAT"},
        )
        for record_type in ("ACTI", "CONT", "DOOR", "MSTT", "SCOL", "STAT"):
            with self.subTest(record_type=record_type):
                policy = profile["presentationPolicies"][record_type]
                self.assertEqual(policy["kind"], "static-model")
                self.assertEqual(policy["modelPathCount"], 1)

    def test_point_light_uses_reference_radius_before_base_radius(self) -> None:
        base = {
            "formKey": "FalloutNV.esm:000020",
            "light": {
                "radiusGameUnits": 256,
                "colorRgb": [100, 80, 40],
                "lightFlags": 0,
                "falloff": 1.0,
                "fieldOfViewDegrees": 90.0,
                "intensity": 1.5,
            },
        }
        overridden = compiled_light_contract(
            base,
            {"radiusGameUnits": 96.0},
            0.01,
        )
        authored = compiled_light_contract(
            base,
            {"radiusGameUnits": None},
            0.01,
        )

        self.assertEqual(overridden["effectiveRadiusGameUnits"], 96.0)
        self.assertEqual(overridden["effectiveRadiusMeters"], 0.96)
        self.assertEqual(authored["effectiveRadiusGameUnits"], 256.0)

    def test_nested_owned_nif_compile_has_a_complete_output_ledger(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            authored = root / "authored.nif"
            write_synthetic_nif(authored)
            logical_path = "meshes\\nested\\test.nif"
            archives = SingleMemberArchive(logical_path, authored.read_bytes())
            staging = root / "staging"
            staging.mkdir()
            configuration = load_runtime_configuration().content_compiler

            asset, sidecar, blocked = compile_model(
                "nested\\test.nif",
                archives,
                staging,
                configuration,
                False,
            )

            self.assertIsNone(blocked)
            self.assertIsNotNone(asset)
            self.assertIsNotNone(sidecar)
            assert asset is not None
            assert sidecar is not None
            self.assertEqual(len(asset["assetId"]), configuration.asset_id_hex_characters)
            self.assertTrue((staging / "source" / "meshes" / "nested" / "test.nif").is_file())
            self.assertEqual(set(asset["outputs"]), {"gltf", "buffer", "sidecar"})
            for descriptor in asset["outputs"].values():
                path = staging / str(descriptor["file"])
                self.assertEqual(path.stat().st_size, descriptor["bytes"])
                self.assertEqual(file_sha256(path), descriptor["sha256"])
            bindings = material_bindings(sidecar, {}, configuration)
            self.assertEqual(len(bindings), 1)
            self.assertEqual(bindings[0]["name"], "Opaque Triangle")

    def test_owned_texture_identity_and_bytes_are_content_bound(self) -> None:
        source = Image.new("RGBA", (4, 4), (10, 20, 30, 255))
        encoded = BytesIO()
        source.save(encoded, format="DDS")
        payload = encoded.getvalue()
        requested = "textures\\test\\owned.dds"
        archives = SingleMemberArchive(requested, payload)
        configuration = load_runtime_configuration().content_compiler

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            artifact = OwnedTexturePipeline(
                archives,
                root,
                {},
                configuration,
            ).prepare(requested)
            row = texture_row(artifact, root)

        expected_id = hashlib.sha256(
            f"{requested}:{hashlib.sha256(payload).hexdigest()}".encode("utf-8")
        ).hexdigest()[:configuration.asset_id_hex_characters]
        self.assertEqual(artifact.asset_id, expected_id)
        self.assertEqual(row["sourceBytes"], len(payload))
        self.assertGreater(row["pngBytes"], 0)
        self.assertEqual(row["sourceArchive"], "Synthetic.bsa")

    def test_pretty_json_descriptor_and_relative_path_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "cell-static.json"
            document = {"schema": "test", "values": [1, 2, 3]}
            atomic_json(path, document)

            self.assertEqual(
                validate_json_descriptor(path, output_descriptor(path, 1)),
                document,
            )
            with self.assertRaises(ValueError):
                validate_relative_file(
                    root,
                    "../escape.json",
                    path.stat().st_size,
                    file_sha256(path),
                    root,
                )

    def test_transient_staging_path_is_not_persisted_in_a_blocker(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            error = FileNotFoundError(root / "source" / "missing.nif")
            detail = stable_exception_detail(error, root)

        self.assertIn("<staging>", detail)
        self.assertNotIn(str(root), detail)

    def test_unknown_profile_field_fails_closed(self) -> None:
        profile = load_profile(default_profile_path())
        profile["unownedPolicy"] = True
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "profile.json"
            path.write_text(json.dumps(profile), encoding="utf-8")

            with self.assertRaises(ValueError):
                load_profile(path)

    def test_missing_or_nonfinite_transform_is_an_explicit_blocker(self) -> None:
        self.assertEqual(child_transform({}), (None, None, "missing-transform"))
        self.assertEqual(
            child_transform(
                {
                    "transformGameUnits": {
                        "position": [0.0, float("nan"), 0.0],
                        "rotation_radians": [0.0, 0.0, 0.0],
                    }
                }
            ),
            (None, None, "invalid-transform"),
        )


if __name__ == "__main__":
    unittest.main()
