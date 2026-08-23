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

from export_static_nif_gltf import (  # noqa: E402
    alpha_contract,
    export_static_nif,
    generate_tangents,
    is_editor_marker,
    material_metadata,
    shape_double_sided,
    texture_uv,
    texture_paths,
    vertex_color_mode,
)
from bsa_archive import canonical_member_path, decode_member_payload, strip_embedded_name  # noqa: E402
from gltf_io import compiler_sources_sha256  # noqa: E402
from texture_pipeline import decode_dds, decode_dds_cubemap  # noqa: E402


def identity_transform(target: object) -> None:
    matrix = NifFormat.Matrix44()
    matrix.set_identity()
    target.set_transform(matrix)


def write_synthetic_nif(path: Path) -> None:
    root = NifFormat.NiNode()
    root.name = "Synthetic Root"
    identity_transform(root)

    marker = NifFormat.NiNode()
    marker.name = "ProjectileNode"
    identity_transform(marker)
    marker.translation.x = 10.0
    marker.translation.y = 20.0
    marker.translation.z = 30.0
    root.add_child(marker)

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
    def test_compiler_source_hash_accounts_for_every_owned_module(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            first = Path(directory) / "first.py"
            second = Path(directory) / "second.py"
            first.write_bytes(b"first")
            second.write_bytes(b"second")
            baseline = compiler_sources_sha256([first, second])
            self.assertEqual(baseline, compiler_sources_sha256([second, first]))
            second.write_bytes(b"changed")
            self.assertNotEqual(baseline, compiler_sources_sha256([first, second]))

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
            self.assertFalse(first_result["coverage"]["collisionExported"])
            self.assertIsNone(first_result["coverage"]["collisionUnsupportedReason"])
            self.assertEqual(first_result["surfaces"][0]["triangles"], 1)
            self.assertEqual(first_result["surfaces"][0]["vertices"], 3)
            self.assertEqual(
                first_result["surfaces"][0]["attributes"],
                ["COLOR_0", "NORMAL", "POSITION", "TANGENT", "TEXCOORD_0"],
            )
            self.assertEqual(first_result["source"]["logicalPath"], "meshes\\open-nv-tests\\opaque-triangle.nif")
            self.assertEqual(
                first_result["attachmentMarkers"],
                [{"name": "ProjectileNode", "positionGodotUnits": [10.0, 30.0, -20.0]}],
            )
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

    def test_complete_dds_cubemap_faces_are_retained(self) -> None:
        source = Image.new("RGBA", (4, 4), (10, 20, 30, 255))
        encoded = BytesIO()
        source.save(encoded, format="DDS")
        flat = encoded.getvalue()
        header = bytearray(flat[:128])
        struct.pack_into("<I", header, 112, 0xFE00)
        cube = bytes(header) + flat[128:] * 6
        faces = decode_dds_cubemap(cube)
        self.assertEqual(len(faces), 6)
        self.assertEqual([face.getpixel((0, 0)) for face in faces], [(10, 20, 30, 255)] * 6)

    def test_generated_tangent_fallback_is_normalized(self) -> None:
        tangents = generate_tangents(
            [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)],
            [(0.0, 0.0, 1.0)] * 3,
            [(0.0, 0.0), (1.0, 0.0), (0.0, 1.0)],
            [(0, 1, 2)],
        )
        self.assertEqual(tangents, [(1.0, 0.0, 0.0, 1.0)] * 3)

    def test_direct3d_texture_v_coordinate_is_converted_for_png(self) -> None:
        value = NifFormat.TexCoord()
        value.u = 0.25
        value.v = 0.125
        self.assertEqual(texture_uv(value), (0.25, 0.875))

    def test_editor_marker_surface_identity_is_explicit(self) -> None:
        self.assertTrue(is_editor_marker(b"EditorMarker:0"))
        self.assertFalse(is_editor_marker(b"DinerBooth"))

    def test_double_sided_material_requires_retail_stencil_draw_both(self) -> None:
        shape = NifFormat.NiTriShape()
        self.assertFalse(shape_double_sided(shape))
        stencil = NifFormat.NiStencilProperty()
        stencil.draw_mode = 1
        shape.add_property(stencil)
        self.assertFalse(shape_double_sided(shape))
        stencil.draw_mode = 3
        self.assertTrue(shape_double_sided(shape))

    def test_static_alpha_and_vertex_color_flags_are_not_guessed(self) -> None:
        shape = NifFormat.NiTriShape()
        shader = NifFormat.BSShaderNoLightingProperty()
        shader.shader_flags.sf_vertex_alpha = 1
        shape.add_property(shader)
        self.assertEqual(alpha_contract(shape)["source"], "BSShaderFlags")
        self.assertEqual(alpha_contract(shape)["mode"], "BLEND")
        self.assertEqual(vertex_color_mode(shape), "alpha")

        alpha = NifFormat.NiAlphaProperty()
        alpha.flags = 0x12EC
        alpha.threshold = 20
        shape.add_property(alpha)
        contract = alpha_contract(shape)
        self.assertEqual(contract["source"], "NiAlphaProperty")
        self.assertEqual(contract["mode"], "MASK")
        self.assertAlmostEqual(contract["cutoff"], 20 / 255)

    def test_no_lighting_texture_and_neutral_shader_base_color_survive(self) -> None:
        shape = NifFormat.NiTriShape()
        material = NifFormat.NiMaterialProperty()
        material.diffuse_color.r = 0.0
        material.diffuse_color.g = 0.0
        material.diffuse_color.b = 0.0
        material.alpha = 0.75
        shader = NifFormat.BSShaderNoLightingProperty()
        shader.file_name = r"Textures\Architecture\Barracks\White.dds"
        shape.add_property(material)
        shape.add_property(shader)
        self.assertEqual(
            texture_paths(shape),
            [r"textures\architecture\barracks\white.dds"],
        )
        metadata = material_metadata(shape)
        self.assertEqual(metadata["baseColor"], [1.0, 1.0, 1.0])
        self.assertEqual(metadata["alpha"], 0.75)

    def test_self_illum_requires_the_material_color_controller(self) -> None:
        shape = NifFormat.NiTriShape()
        material = NifFormat.NiMaterialProperty()
        material.emissive_color.r = 1.0
        material.emissive_color.g = 1.0
        material.emissive_color.b = 1.0
        shape.add_property(material)
        self.assertFalse(material_metadata(shape)["emissiveControlled"])
        controller = NifFormat.NiMaterialColorController()
        controller.target_color = 3
        material.controller = controller
        self.assertTrue(material_metadata(shape)["emissiveControlled"])


if __name__ == "__main__":
    unittest.main()
