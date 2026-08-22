from __future__ import annotations

import json
import struct
import sys
import tempfile
import time
import unittest
import zlib
from io import BytesIO
from pathlib import Path

if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from pyffi.formats.nif import NifFormat  # type: ignore  # noqa: E402
from PIL import Image  # noqa: E402

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from export_static_nif_gltf import export_static_nif  # noqa: E402
from bsa_archive import canonical_member_path, decode_member_payload, strip_embedded_name  # noqa: E402
from texture_pipeline import decode_dds  # noqa: E402


def identity_transform(target: object) -> None:
    matrix = NifFormat.Matrix44()
    matrix.set_identity()
    target.set_transform(matrix)


def write_synthetic_nif(path: Path) -> None:
    root = NifFormat.NiNode()
    root.name = "Synthetic Root"
    identity_transform(root)

    shape = NifFormat.NiTriShape()
    shape.name = "Opaque Triangle"
    identity_transform(shape)
    root.add_child(shape)

    mesh = NifFormat.NiTriShapeData()
    shape.data = mesh
    mesh.num_vertices = 3
    mesh.has_vertices = True
    mesh.vertices.update_size()
    for vertex, values in zip(mesh.vertices, ((-1.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, 2.0))):
        vertex.x, vertex.y, vertex.z = values
    mesh.has_normals = True
    mesh.normals.update_size()
    for normal in mesh.normals:
        normal.x, normal.y, normal.z = (0.0, -1.0, 0.0)
    mesh.num_uv_sets = 1
    mesh.has_uv = True
    mesh.uv_sets.update_size()
    for uv, values in zip(mesh.uv_sets[0], ((0.0, 0.0), (1.0, 0.0), (0.5, 1.0))):
        uv.u, uv.v = values
    mesh.has_vertex_colors = True
    mesh.vertex_colors.update_size()
    for color, values in zip(
        mesh.vertex_colors,
        ((1.0, 0.0, 0.0, 1.0), (0.0, 1.0, 0.0, 1.0), (0.0, 0.0, 1.0, 1.0)),
    ):
        color.r, color.g, color.b, color.a = values
    mesh.set_triangles([(0, 1, 2)])
    mesh.update_center_radius()
    shape.update_tangent_space()

    # PyFFI 2.2.3's writer cannot round-trip Bethesda's 20.x header on modern
    # Python. The synthetic contract uses an older NIF container; the local
    # authored-data gate separately reads the real Fallout 20.2.0.7 format.
    document = NifFormat.Data(version=0x0A020000)
    document.roots = [root]
    with path.open("wb") as stream:
        document.write(stream)


class StaticNifGltfTest(unittest.TestCase):
    def test_bsa_member_path_and_compression_fail_closed(self) -> None:
        original = b"owned retail bytes"
        payload = struct.pack("<I", len(original)) + zlib.compress(original)
        self.assertEqual(decode_member_payload(payload, True), original)
        self.assertEqual(canonical_member_path("Meshes/Landscape/Test.NIF"), "meshes\\landscape\\test.nif")
        with self.assertRaises(ValueError):
            canonical_member_path("meshes/../test.nif")
        with self.assertRaises(ValueError):
            decode_member_payload(struct.pack("<I", len(original) + 1) + zlib.compress(original), True)
        logical_path = "textures\\test\\owned.dds"
        embedded = bytes([len(logical_path)]) + logical_path.encode() + payload
        self.assertEqual(strip_embedded_name(embedded, logical_path), payload)
        with self.assertRaises(ValueError):
            strip_embedded_name(embedded, "textures\\test\\different.dds")

    def test_synthetic_export_is_deterministic_and_complete(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            directory = Path(raw_directory)
            source = directory / "opaque-triangle.nif"
            write_synthetic_nif(source)
            outputs = []
            for suffix in ("a", "b"):
                output_directory = directory / suffix
                output_directory.mkdir()
                gltf = output_directory / "opaque-triangle.gltf"
                sidecar = output_directory / "opaque-triangle.opennv.json"
                result = export_static_nif(
                    source,
                    "meshes/open-nv-tests/opaque-triangle.nif",
                    gltf,
                    sidecar,
                    strict=False,
                )
                outputs.append((gltf.read_text(), gltf.with_suffix(".bin").read_bytes(), sidecar.read_text(), result))

            first_gltf = json.loads(outputs[0][0])
            self.assertEqual(outputs[0][0], outputs[1][0])
            self.assertEqual(outputs[0][1], outputs[1][1])
            self.assertEqual(outputs[0][2], outputs[1][2])
            first_result = outputs[0][3]
            self.assertEqual(first_gltf["meshes"][0]["primitives"][0].get("mode", 4), 4)
            self.assertEqual(first_result["coverage"]["surfaces"], 1)
            self.assertEqual(first_result["surfaces"][0]["triangles"], 1)
            self.assertEqual(first_result["surfaces"][0]["vertices"], 3)
            self.assertEqual(
                first_result["surfaces"][0]["attributes"],
                ["COLOR_0", "NORMAL", "POSITION", "TANGENT", "TEXCOORD_0"],
            )
            self.assertEqual(first_result["source"]["logicalPath"], "meshes\\open-nv-tests\\opaque-triangle.nif")
            self.assertEqual(first_gltf["asset"]["version"], "2.0")
            self.assertEqual(first_gltf["accessors"][0]["min"], [-1.0, 0.0, -0.0])
            self.assertEqual(first_gltf["accessors"][0]["max"], [1.0, 2.0, -0.0])

    def test_dds_decode_and_normal_green_conversion(self) -> None:
        source = Image.new("RGBA", (4, 4), (10, 20, 30, 40))
        encoded = BytesIO()
        source.save(encoded, format="DDS")
        self.assertEqual(decode_dds(encoded.getvalue(), False).getpixel((0, 0)), (10, 20, 30, 40))
        self.assertEqual(decode_dds(encoded.getvalue(), True).getpixel((0, 0)), (10, 235, 30, 40))
        wrong_format = BytesIO()
        source.save(wrong_format, format="PNG")
        with self.assertRaises(ValueError):
            decode_dds(wrong_format.getvalue(), False)


if __name__ == "__main__":
    unittest.main()
