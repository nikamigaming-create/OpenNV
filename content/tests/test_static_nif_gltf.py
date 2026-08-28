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
    NoStaticPresentationGeometryError,
    alpha_contract,
    clip_triangles_outside_source_rectangle,
    export_static_nif,
    generate_tangents,
    generate_vertex_normals,
    has_presentation_property,
    is_editor_marker,
    material_metadata,
    shape_double_sided,
    texture_uv,
    texture_paths,
    vertex_color_mode,
)
from runtime_configuration import load_runtime_configuration  # noqa: E402
from bsa_archive import canonical_member_path, decode_member_payload, strip_embedded_name  # noqa: E402
from gltf_io import compiler_sources_sha256  # noqa: E402
from havok_collision_gltf import dynamic_physics_contract  # noqa: E402
from nif_decoder import (  # noqa: E402
    _block_directory,
    decode_nif,
    load_nif_decoder_contract,
)
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
    shape.add_property(NifFormat.NiTexturingProperty())

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

    helper = NifFormat.NiTriShape()
    helper.name = "Rig Helper"
    identity_transform(helper)
    helper_mesh = NifFormat.NiTriShapeData()
    helper.data = helper_mesh
    helper_mesh.num_vertices = 3
    helper_mesh.has_vertices = True
    helper_mesh.vertices.update_size()
    for vertex, values in zip(
        helper_mesh.vertices,
        ((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)),
    ):
        vertex.x, vertex.y, vertex.z = values
    helper_mesh.has_normals = True
    helper_mesh.normals.update_size()
    for normal in helper_mesh.normals:
        normal.x, normal.y, normal.z = (0.0, 0.0, 1.0)
    helper_mesh.set_triangles([(0, 1, 2)])
    helper.add_property(NifFormat.NiMaterialProperty())
    root.add_child(helper)

    # PyFFI 2.2.3's writer cannot round-trip Bethesda's 20.x header on modern
    # Python. The synthetic contract uses an older NIF container; the local
    # authored-data gate separately reads the real Fallout 20.2.0.7 format.
    document = NifFormat.Data(version=0x0A020000)
    document.roots = [root]
    with path.open("wb") as stream:
        document.write(stream)


def write_editor_marker_only_nif(path: Path) -> None:
    root = NifFormat.NiNode()
    root.name = "FurnitureMarker"
    identity_transform(root)
    shape = NifFormat.NiTriShape()
    shape.name = "EditorMarker:0"
    identity_transform(shape)
    shape.add_property(NifFormat.NiTexturingProperty())
    mesh = NifFormat.NiTriShapeData()
    shape.data = mesh
    mesh.num_vertices = 3
    mesh.has_vertices = True
    mesh.vertices.update_size()
    for vertex, values in zip(
        mesh.vertices,
        ((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)),
    ):
        vertex.x, vertex.y, vertex.z = values
    mesh.set_triangles([(0, 1, 2)])
    root.add_child(shape)
    document = NifFormat.Data(version=0x0A020000)
    document.roots = [root]
    with path.open("wb") as stream:
        document.write(stream)


def write_synthetic_fallout_packed_uv_nif(path: Path) -> int:
    contract = load_nif_decoder_contract()
    root = NifFormat.NiNode()
    root.name = "Synthetic Fallout Root"
    identity_transform(root)
    shape = NifFormat.NiTriStrips()
    shape.name = "Synthetic Fallout Strip"
    identity_transform(shape)
    shape.add_property(NifFormat.NiTexturingProperty())
    root.add_child(shape)
    mesh = NifFormat.NiTriStripsData()
    shape.data = mesh
    mesh.num_vertices = 4
    mesh.has_vertices = True
    mesh.vertices.update_size()
    for vertex, values in zip(
        mesh.vertices,
        ((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 1.0, 0.0), (0.0, 1.0, 0.0)),
    ):
        vertex.x, vertex.y, vertex.z = values
    mesh.has_normals = True
    mesh.normals.update_size()
    for normal in mesh.normals:
        normal.x, normal.y, normal.z = (0.0, 0.0, 1.0)
    mesh.num_uv_sets = 1
    mesh.uv_sets.update_size()
    for uv, values in zip(
        mesh.uv_sets[0],
        ((0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)),
    ):
        uv.u, uv.v = values
    mesh.set_strips([[0, 1, 2, 3]])
    mesh.update_center_radius()
    document = NifFormat.Data(
        version=contract.version,
        user_version=contract.user_version,
        user_version_2=contract.user_version_2,
    )
    document.header.endian_type = contract.endian
    document.roots = [root]
    encoded = BytesIO()
    document.write(encoded)
    payload = bytearray(encoded.getvalue())
    blocks, matched = _block_directory(bytes(payload), contract)
    if not matched:
        raise AssertionError("Synthetic Fallout NIF did not match its decoder contract")
    data_block = next(row for row in blocks if row.type_name == "NiTriStripsData")
    vertex_count = struct.unpack_from(
        "<H",
        payload,
        data_block.offset + contract.vertex_count_offset_bytes,
    )[0]
    uv_count_offset = (
        data_block.offset
        + contract.uv_count_prefix_bytes
        + vertex_count * contract.vertex_stride_bytes
    )
    payload[uv_count_offset] = contract.maximum_uv_sets + 1
    path.write_bytes(payload)
    return uv_count_offset


class StaticNifGltfTest(unittest.TestCase):
    def test_fallout_uv_count_is_recovered_only_by_exact_block_parse(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "packed-uv.nif"
            source_offset = write_synthetic_fallout_packed_uv_nif(source)
            source_bytes = source.read_bytes()
            decoded = decode_nif(source_bytes)
            self.assertEqual(source.read_bytes(), source_bytes)
            self.assertEqual(len(decoded.normalizations), 1)
            normalization = decoded.normalizations[0]
            self.assertEqual(normalization.source_byte_offset, source_offset)
            self.assertEqual(normalization.decoded_uv_sets, 1)
            self.assertEqual(normalization.candidates_tested, (0, 1))
            exported = export_static_nif(
                source,
                "meshes/open-nv-tests/packed-uv.nif",
                root / "packed-uv.gltf",
                root / "packed-uv.opennv.json",
                load_runtime_configuration().content_compiler,
                strict=False,
            )
            decoder = exported["source"]["decoder"]
            self.assertEqual(decoder["status"], "owned-format-normalized-in-memory")
            self.assertFalse(decoder["sourceBytesModified"])
            self.assertEqual(decoder["normalizations"][0]["decodedUvSets"], 1)

    def test_presentation_clip_retains_only_fragments_outside_source_rectangle(self) -> None:
        positions = [(-2.0, 0.0, 0.0), (2.0, 0.0, 0.0), (0.0, 0.0, -2.0)]
        normals = [(0.0, 1.0, 0.0)] * 3
        uvs = [[(0.0, 0.0), (1.0, 0.0), (0.5, 1.0)]]
        colors = [
            (1.0, 0.0, 0.0, 1.0),
            (0.0, 1.0, 0.0, 1.0),
            (0.0, 0.0, 1.0, 1.0),
        ]
        clipped = clip_triangles_outside_source_rectangle(
            positions,
            normals,
            uvs,
            colors,
            [(0, 1, 2)],
            (-1.0, 1.0, -1.0, 1.0),
        )
        output_positions, output_normals, output_uvs, output_colors, triangles, report = clipped
        self.assertGreater(len(triangles), 1)
        self.assertEqual(report["clippedSourceTriangles"], 1)
        self.assertEqual(report["fullyRemovedSourceTriangles"], 0)
        self.assertEqual(len(output_positions), len(output_normals))
        self.assertEqual(len(output_positions), len(output_uvs[0]))
        self.assertEqual(len(output_positions), len(output_colors))
        self.assertTrue(any(position[0] == -1.0 for position in output_positions))
        for triangle in triangles:
            centroid_x = sum(output_positions[index][0] for index in triangle) / 3.0
            centroid_y = sum(-output_positions[index][2] for index in triangle) / 3.0
            self.assertFalse(-1.0 < centroid_x < 1.0 and -1.0 < centroid_y < 1.0)

        removed = clip_triangles_outside_source_rectangle(
            [(-0.5, 0.0, 0.0), (0.5, 0.0, 0.0), (0.0, 0.0, -0.5)],
            normals,
            uvs,
            colors,
            [(0, 1, 2)],
            (-1.0, 1.0, -1.0, 1.0),
        )
        self.assertEqual(removed[4], [])
        self.assertEqual(removed[5]["fullyRemovedSourceTriangles"], 1)

    def test_dynamic_convex_body_retains_authored_physics_and_local_shape(self) -> None:
        root = NifFormat.NiNode()
        root.name = "Root"
        target = NifFormat.NiNode()
        target.name = "PoolBall"
        root.add_child(target)
        collision = NifFormat.bhkCollisionObject()
        collision.target = target
        target.collision_object = collision
        body = NifFormat.bhkRigidBody()
        collision.body = body
        body.mass = 0.45
        body.friction = 0.5
        body.restitution = 0.4
        body.linear_damping = 0.1
        body.angular_damping = 0.05
        body.translation.x = 12.0
        shape = NifFormat.bhkConvexVerticesShape()
        shape.num_vertices = 4
        shape.vertices.update_size()
        for vertex, values in zip(
            shape.vertices,
            ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (-1.0, -1.0, -1.0)),
        ):
            vertex.x, vertex.y, vertex.z = values
        body.shape = shape
        blocks = [root, target, collision, body, shape]
        bodies, unsupported = dynamic_physics_contract(
            blocks,
            {id(block): index for index, block in enumerate(blocks)},
        )

        self.assertEqual(unsupported, [])
        self.assertEqual(len(bodies), 1)
        exported = bodies[0]
        self.assertAlmostEqual(exported["mass"], 0.45)
        self.assertEqual(exported["sourceBodyTranslationHavokUnits"], [12.0, 0.0, 0.0])
        self.assertEqual(
            exported["shapeTransformPolicy"],
            "reference-transform-authoritative;body-pose-retained-as-source-evidence",
        )
        self.assertEqual(
            exported["hulls"][0]["pointsGodotGameUnits"][0],
            (7.0, 0.0, -0.0),
        )

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
                    load_runtime_configuration().content_compiler,
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
            self.assertEqual(
                first_result["coverage"]["excludedNonPresentationSurfaces"],
                [
                    {
                        "sourceBlockIndex": 6,
                        "name": "Rig Helper",
                        "propertyTypes": ["NiMaterialProperty"],
                        "reason": "no-Bethesda-shader-or-NiTexturingProperty",
                    }
                ],
            )
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

    def test_static_shape_prefix_filter_is_explicit_and_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            directory = Path(raw_directory)
            source = directory / "opaque-triangle.nif"
            write_synthetic_nif(source)
            result = export_static_nif(
                source,
                "meshes/open-nv-tests/opaque-triangle.nif",
                directory / "filtered.gltf",
                directory / "filtered.opennv.json",
                strict=False,
                include_shape_prefixes=("Opaque",),
            )
            self.assertEqual(result["coverage"]["includedShapePrefixes"], ["Opaque"])
            self.assertEqual(result["coverage"]["excludedByShapeFilter"], [])
            with self.assertRaises(ValueError):
                export_static_nif(
                    source,
                    "meshes/open-nv-tests/opaque-triangle.nif",
                    directory / "empty.gltf",
                    directory / "empty.opennv.json",
                    strict=False,
                    include_shape_prefixes=("Missing",),
                )

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

    def test_generated_terrain_lod_normals_are_area_weighted_and_normalized(self) -> None:
        normals = generate_vertex_normals(
            [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, 1.0)],
            [(0, 1, 2)],
        )
        self.assertEqual(normals, [(0.0, -1.0, 0.0)] * 3)

    def test_nif_texture_v_coordinate_is_preserved_for_godot_png(self) -> None:
        value = NifFormat.TexCoord()
        value.u = 0.25
        value.v = 0.125
        self.assertEqual(texture_uv(value), (0.25, 0.125))

    def test_editor_marker_surface_identity_is_explicit(self) -> None:
        self.assertTrue(is_editor_marker(b"EditorMarker:0"))
        self.assertFalse(is_editor_marker(b"DinerBooth"))

    def test_marker_only_owned_nif_has_explicit_non_presentation_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "marker.nif"
            write_editor_marker_only_nif(source)
            with self.assertRaises(NoStaticPresentationGeometryError) as caught:
                export_static_nif(
                    source,
                    "meshes/open-nv-tests/marker.nif",
                    root / "marker.gltf",
                    root / "marker.opennv.json",
                    load_runtime_configuration().content_compiler,
                    strict=False,
                )
            evidence = caught.exception.evidence
            self.assertEqual(evidence["schema"], "opennv-nif-non-presentation/v1")
            self.assertEqual(
                evidence["status"],
                "owned-nif-no-presentation-geometry",
            )
            classification = evidence["classification"]
            self.assertEqual(classification["triangleSurfaceCount"], 1)
            self.assertEqual(classification["classifiedSurfaceCount"], 1)
            self.assertEqual(
                classification["disposition"],
                "exclude-reference-from-presentation",
            )

    def test_presentation_surface_requires_a_retail_render_property(self) -> None:
        helper = NifFormat.NiTriShape()
        helper.add_property(NifFormat.NiMaterialProperty())
        self.assertFalse(has_presentation_property(helper))
        helper.add_property(NifFormat.NiTexturingProperty())
        self.assertTrue(has_presentation_property(helper))

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
