from __future__ import annotations

import json
import hashlib
import struct
import sys
import tempfile
import time
import unittest
import zlib
from io import BytesIO
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

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
from gltf_io import compiler_sources_sha256, local_python_dependency_paths  # noqa: E402
from havok_collision_gltf import collision_contract, dynamic_physics_contract  # noqa: E402
from nif_decoder import (  # noqa: E402
    _block_directory,
    decode_nif,
    load_nif_decoder_contract,
)
from texture_pipeline import decode_dds, decode_dds_cubemap  # noqa: E402
from scene_asset_pipeline import (  # noqa: E402
    authored_collision_face_selection,
    authored_collision_source,
)


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


def synthetic_controller_door_document(
    *,
    gate_collision_kind: str = "packed",
    include_posts_collision: bool = True,
) -> tuple[object, object]:
    root = NifFormat.NiNode()
    root.name = "Synthetic Door Root"
    identity_transform(root)

    gate = NifFormat.NiNode()
    gate.name = "BGate"
    identity_transform(gate)
    gate.translation.x = 5.0
    gate.translation.z = 2.0
    root.add_child(gate)

    posts = NifFormat.NiNode()
    posts.name = "BPosts"
    identity_transform(posts)
    root.add_child(posts)

    def add_surface(parent: object, name: str) -> None:
        shape = NifFormat.NiTriShape()
        shape.name = name
        identity_transform(shape)
        shape.add_property(NifFormat.NiTexturingProperty())
        mesh = NifFormat.NiTriShapeData()
        shape.data = mesh
        mesh.num_vertices = 3
        mesh.has_vertices = True
        mesh.vertices.update_size()
        for vertex, values in zip(
            mesh.vertices,
            ((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, 1.0)),
        ):
            vertex.x, vertex.y, vertex.z = values
        mesh.has_normals = True
        mesh.normals.update_size()
        for normal in mesh.normals:
            normal.x, normal.y, normal.z = (0.0, -1.0, 0.0)
        mesh.num_uv_sets = 1
        mesh.has_uv = True
        mesh.uv_sets.update_size()
        for uv, values in zip(mesh.uv_sets[0], ((0.0, 0.0), (1.0, 0.0), (0.0, 1.0))):
            uv.u, uv.v = values
        mesh.set_triangles([(0, 1, 2)])
        mesh.update_center_radius()
        parent.add_child(shape)

    def add_collision(target: object, kind: str = "packed") -> object:
        collision = NifFormat.bhkCollisionObject()
        collision.target = target
        target.collision_object = collision
        body = (
            NifFormat.bhkRigidBody()
            if kind == "convex"
            else NifFormat.bhkRigidBodyT()
        )
        collision.body = body
        body.rotation.w = 1.0
        if kind in {"convex", "convex-t", "box"}:
            body.mass = 0.0
            body.translation.x = 0.25
            body.friction = 0.4
            body.restitution = 0.1
            body.linear_damping = 0.2
            body.angular_damping = 0.3
            body.motion_system = 7
            body.quality_type = 1
            body.havok_col_filter.layer = 2
            body.havok_col_filter.flags_and_part_number = 3
            body.havok_col_filter.unknown_short = 4
            if kind == "box":
                shape = NifFormat.bhkBoxShape()
                shape.radius = 0.05
                shape.material.material = 9
                shape.dimensions.x = 1.0
                shape.dimensions.y = 2.0
                shape.dimensions.z = 3.0
                shape.minimum_size = 1.0
                body.shape = shape
                return collision
            shape = NifFormat.bhkConvexVerticesShape()
            shape.radius = 0.05
            shape.material.material = 9
            shape.num_vertices = 8
            shape.vertices.update_size()
            for vertex, values in zip(
                shape.vertices,
                (
                    (-1.0, -1.0, -1.0),
                    (-1.0, -1.0, 1.0),
                    (-1.0, 1.0, -1.0),
                    (-1.0, 1.0, 1.0),
                    (1.0, -1.0, -1.0),
                    (1.0, -1.0, 1.0),
                    (1.0, 1.0, -1.0),
                    (1.0, 1.0, 1.0),
                ),
            ):
                vertex.x, vertex.y, vertex.z = values
            body.shape = shape
            return collision
        if kind != "packed":
            raise ValueError(f"Unsupported synthetic collision kind: {kind}")
        mopp = NifFormat.bhkMoppBvTreeShape()
        body.shape = mopp
        packed = NifFormat.bhkPackedNiTriStripsShape()
        mopp.shape = packed
        packed.scale.x = 1.0
        packed.scale.y = 1.0
        packed.scale.z = 1.0
        data = NifFormat.hkPackedNiTriStripsData()
        packed.data = data
        data.num_vertices = 3
        data.vertices.update_size()
        for vertex, values in zip(
            data.vertices,
            ((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, 1.0)),
        ):
            vertex.x, vertex.y, vertex.z = values
        data.num_triangles = 1
        data.triangles.update_size()
        data.triangles[0].triangle.v_1 = 0
        data.triangles[0].triangle.v_2 = 1
        data.triangles[0].triangle.v_3 = 2
        data.num_sub_shapes = 1
        data.sub_shapes.update_size()
        data.sub_shapes[0].num_vertices = 3
        return collision

    add_surface(gate, "BGate:0")
    add_surface(posts, "BPosts:0")
    gate_collision = add_collision(gate, gate_collision_kind)
    if include_posts_collision:
        add_collision(posts)

    manager = NifFormat.NiControllerManager()
    manager.target = root
    root.controller = manager
    controller = NifFormat.NiMultiTargetTransformController()
    controller.target = root
    controller.num_extra_targets = 1
    controller.extra_targets.update_size()
    controller.extra_targets[0] = gate
    manager.next_controller = controller
    manager.num_controller_sequences = 2
    manager.controller_sequences.update_size()
    for sequence_index, (name, stop, start_z, stop_z) in enumerate(
        (("Open", 1.0, 0.0, 1.0), ("Close", 0.9, 0.95, 0.0))
    ):
        sequence = NifFormat.NiControllerSequence()
        sequence.name = name
        sequence.start_time = 0.0
        sequence.stop_time = stop
        sequence.manager = manager
        sequence.num_controlled_blocks = 1
        sequence.controlled_blocks.update_size()
        controlled = sequence.controlled_blocks[0]
        controlled.node_name = "BGate"
        controlled.controller_type = "NiTransformController"
        controlled.controller = controller
        interpolator = NifFormat.NiTransformInterpolator()
        controlled.interpolator = interpolator
        transform_data = NifFormat.NiTransformData()
        interpolator.data = transform_data
        transform_data.num_rotation_keys = 1
        transform_data.rotation_type = 4
        for axis_index, group in enumerate(transform_data.xyz_rotations):
            group.interpolation = 2
            group.num_keys = 2
            group.keys.update_size()
            for key_index, (time_value, key_value) in enumerate(
                (
                    (0.0, start_z if axis_index == 2 else 0.0),
                    (stop, stop_z if axis_index == 2 else 0.0),
                )
            ):
                group.keys[key_index].time = time_value
                group.keys[key_index].value = key_value
        transform_data.translations.interpolation = 1
        transform_data.scales.interpolation = 2
        transform_data.scales.num_keys = 2
        transform_data.scales.keys.update_size()
        for key_index, time_value in enumerate((0.0, stop)):
            transform_data.scales.keys[key_index].time = time_value
            transform_data.scales.keys[key_index].value = 1.0
        manager.controller_sequences[sequence_index] = sequence

    document = NifFormat.Data(version=0x14020007, user_version=12, user_version_2=83)
    document.roots = [root]
    return document, gate_collision


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


def synthetic_fallout_animation(user_version_2: int, root: object) -> bytes:
    contract = load_nif_decoder_contract()
    document = NifFormat.Data(
        version=contract.version,
        user_version=contract.user_version,
        user_version_2=user_version_2,
    )
    document.header.endian_type = contract.endian
    document.roots = [root]
    encoded = BytesIO()
    document.write(encoded)
    return encoded.getvalue()


class StaticNifGltfTest(unittest.TestCase):
    def test_owned_road_face_selection_is_path_and_collision_source_bound(self) -> None:
        packed = "NIF-authored-bhk-packed-triangles"
        self.assertEqual(
            authored_collision_face_selection(
                "meshes\\landscape\\roads\\roadchunkcluster01.nif",
                packed,
            ),
            "source-upward-walkable-deck",
        )
        self.assertEqual(
            authored_collision_face_selection(
                "meshes/scol/scolroadstraightlongbcapped02b.nif",
                packed,
            ),
            "source-upward-walkable-deck",
        )
        self.assertEqual(
            authored_collision_face_selection(
                "meshes\\architecture\\goodsprings\\roadsidewall.nif",
                packed,
            ),
            "all-source-faces",
        )
        with self.assertRaisesRegex(
            ValueError,
            "requires its packed-triangle source contract",
        ):
            authored_collision_face_selection(
                "meshes\\landscape\\roads\\roadchunk01.nif",
                "unsupported-or-absent",
            )

    def test_fallout_alternate_animation_identity_is_controller_sequence_only(self) -> None:
        contract = load_nif_decoder_contract()
        for alternate_user_version_2 in contract.animation_user_version_2:
            with self.subTest(user_version_2=alternate_user_version_2):
                decoded = decode_nif(
                    synthetic_fallout_animation(
                        alternate_user_version_2,
                        NifFormat.NiControllerSequence(),
                    )
                )
                self.assertTrue(decoded.format_matched)
                self.assertEqual(
                    decoded.document.user_version_2,
                    alternate_user_version_2,
                )

                with self.assertRaisesRegex(ValueError, "alternate animation identity"):
                    decode_nif(
                        synthetic_fallout_animation(
                            alternate_user_version_2,
                            NifFormat.NiNode(),
                        )
                    )

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

    def test_dynamic_box_body_retains_exact_owned_half_extents(self) -> None:
        root = NifFormat.NiNode()
        root.name = "Root"
        target = NifFormat.NiNode()
        target.name = "Book"
        root.add_child(target)
        collision = NifFormat.bhkCollisionObject()
        collision.target = target
        target.collision_object = collision
        body = NifFormat.bhkRigidBody()
        collision.body = body
        body.mass = 15.0
        shape = NifFormat.bhkBoxShape()
        shape.dimensions.x = 1.0
        shape.dimensions.y = 2.0
        shape.dimensions.z = 3.0
        shape.radius = 0.1
        shape.minimum_size = 1.0
        body.shape = shape
        blocks = [root, target, collision, body, shape]
        bodies, unsupported = dynamic_physics_contract(
            blocks,
            {id(block): index for index, block in enumerate(blocks)},
        )

        self.assertEqual(unsupported, [])
        self.assertEqual(len(bodies), 1)
        exported = bodies[0]
        self.assertEqual(exported["shapeType"], "box")
        self.assertEqual(exported["hulls"][0]["halfExtentsGodotGameUnits"], [7.0, 21.0, 14.0])
        self.assertEqual(
            exported["hulls"][0]["pointsGodotGameUnits"],
            [
                (-7.0, -21.0, 14.0),
                (-7.0, 21.0, 14.0),
                (-7.0, -21.0, -14.0),
                (-7.0, 21.0, -14.0),
                (7.0, -21.0, 14.0),
                (7.0, 21.0, 14.0),
                (7.0, -21.0, -14.0),
                (7.0, 21.0, -14.0),
            ],
        )

    def test_dynamic_sphere_body_retains_exact_owned_radius_and_body_values(self) -> None:
        root = NifFormat.NiNode()
        root.name = "Root"
        target = NifFormat.NiNode()
        target.name = "MovingStatic"
        root.add_child(target)
        collision = NifFormat.bhkCollisionObject()
        collision.target = target
        target.collision_object = collision
        body = NifFormat.bhkRigidBody()
        collision.body = body
        body.mass = 2.5
        body.friction = 0.75
        body.restitution = 0.2
        body.linear_damping = 0.15
        body.angular_damping = 0.35
        body.havok_col_filter.layer = 4
        sphere = NifFormat.bhkSphereShape()
        sphere.radius = 1.25
        body.shape = sphere
        blocks = [root, target, collision, body, sphere]

        bodies, unsupported = dynamic_physics_contract(
            blocks,
            {id(block): index for index, block in enumerate(blocks)},
        )

        self.assertEqual(unsupported, [])
        self.assertEqual(len(bodies), 1)
        exported = bodies[0]
        self.assertEqual(exported["shapeType"], "sphere")
        self.assertEqual(exported["hulls"], [])
        self.assertEqual(exported["spheres"], [{
            "shapeBlock": 4,
            "radiusHavokUnits": 1.25,
            "radiusGameUnits": 8.75,
        }])
        self.assertEqual(exported["mass"], 2.5)
        self.assertEqual(exported["friction"], 0.75)
        self.assertAlmostEqual(exported["restitution"], 0.2)
        self.assertAlmostEqual(exported["linearDamping"], 0.15)
        self.assertAlmostEqual(exported["angularDamping"], 0.35)
        self.assertEqual(exported["layer"], 4)

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

    def test_compiler_identity_is_scoped_and_transitive(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            entrypoint = root / "prepare.py"
            fnv = root / "fnv.py"
            shared = root / "shared.py"
            fallout3 = root / "fallout3.py"
            entrypoint.write_text("import fnv\nimport fallout3\n", encoding="utf-8")
            fnv.write_text("from shared import value\n", encoding="utf-8")
            shared.write_text("value = 1\n", encoding="utf-8")
            fallout3.write_text("presentation = 1\n", encoding="utf-8")

            def identity() -> str:
                paths = local_python_dependency_paths(
                    entrypoint,
                    root,
                    excluded_modules=("fallout3",),
                )
                self.assertEqual(
                    ["fnv.py", "prepare.py", "shared.py"],
                    [path.name for path in paths],
                )
                return compiler_sources_sha256(paths)

            baseline = identity()
            fallout3.write_text("presentation = 2\n", encoding="utf-8")
            self.assertEqual(baseline, identity())
            shared.write_text("value = 2\n", encoding="utf-8")
            self.assertNotEqual(baseline, identity())
            entrypoint.write_text("__import__('fnv')\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Dynamic import"):
                local_python_dependency_paths(entrypoint, root)

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

    def test_single_source_transform_sequence_exports_playable_scene_graph(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            directory = Path(raw_directory)
            source = directory / "source-loop.nif"
            source.write_bytes(b"synthetic source transform loop")
            document, _collision = synthetic_controller_door_document()
            manager = next(
                block
                for block in document.get_global_iterator()
                if isinstance(block, NifFormat.NiControllerManager)
            )
            manager.flags = 76
            manager.frequency = 1.0
            manager.phase = 0.0
            manager.num_controller_sequences = 1
            manager.controller_sequences.update_size()
            sequence = manager.controller_sequences[0]
            source_controlled = sequence.controlled_blocks[0]
            source_controlled.interpolator.data.translations.interpolation = (
                NifFormat.KeyType.LINEARKEY
            )
            source_controlled.interpolator.data.translations.num_keys = 2
            source_controlled.interpolator.data.translations.keys.update_size()
            for key_index, value in enumerate((0.0, 1.0)):
                key = source_controlled.interpolator.data.translations.keys[key_index]
                key.time = value
                key.value.x = value
            frame = NifFormat.NiNode()
            frame.name = "Frame"
            identity_transform(frame)
            document.roots[0].add_child(frame)
            sequence.num_controlled_blocks = 2
            sequence.controlled_blocks.update_size()
            empty_controlled = sequence.controlled_blocks[1]
            empty_controlled.node_name = "Frame"
            empty_controlled.controller_type = "NiTransformController"
            empty_controlled.controller = source_controlled.controller
            empty_controlled.interpolator = NifFormat.NiTransformInterpolator()
            morph_shape = next(
                block
                for block in document.get_global_iterator()
                if isinstance(block, NifFormat.NiTriShape) and block.name == b"BGate:0"
            )
            morpher = NifFormat.NiGeomMorpherController()
            morpher.flags = 76
            morpher.frequency = 1.0
            morpher.phase = 0.0
            morpher.start_time = 0.0
            morpher.stop_time = 1.0
            morpher.target = morph_shape
            morph_shape.controller = morpher
            morph_data = NifFormat.NiMorphData()
            morpher.data = morph_data
            morph_data.num_morphs = 2
            morph_data.num_vertices = len(morph_shape.data.vertices)
            morph_data.relative_targets = 1
            morph_data.morphs.update_size()
            for index, morph in enumerate(morph_data.morphs):
                morph.arg = morph_data.num_vertices
                morph.frame_name = "Base" if index == 0 else "SourceTarget"
                morph.vectors.update_size()
                for target, source_vertex in zip(
                    morph.vectors,
                    morph_shape.data.vertices,
                    strict=True,
                ):
                    if index == 0:
                        target.x, target.y, target.z = (
                            source_vertex.x,
                            source_vertex.y,
                            source_vertex.z,
                        )
                    else:
                        target.z = 0.25
            morpher.num_interpolators = 2
            morpher.interpolator_weights.update_size()
            for index, weight in enumerate(morpher.interpolator_weights):
                interpolator = NifFormat.NiFloatInterpolator()
                weight.interpolator = interpolator
                float_data = NifFormat.NiFloatData()
                interpolator.data = float_data
                group = float_data.data
                group.interpolation = NifFormat.KeyType.LINEARKEY
                group.num_keys = 2
                group.keys.update_size()
                for key_index, value in enumerate((0.0, float(index))):
                    group.keys[key_index].time = float(key_index)
                    group.keys[key_index].value = value
            self.assertEqual(
                (
                    int(morpher.flags),
                    len(morpher.interpolator_weights),
                    len(morpher.data.morphs),
                    bool(morpher.data.relative_targets),
                    type(morpher.target).__name__,
                ),
                (76, 2, 2, True, "NiTriShape"),
            )
            decoded = SimpleNamespace(
                document=document,
                evidence=lambda: {"status": "synthetic-in-memory-contract"},
            )
            with patch("export_static_nif_gltf.decode_nif", return_value=decoded):
                result = export_static_nif(
                    source,
                    "meshes/open-nv-tests/source-loop.nif",
                    directory / "source-loop.gltf",
                    directory / "source-loop.opennv.json",
                    load_runtime_configuration().content_compiler,
                    strict=False,
                )

            playback = result["coverage"]["sourceControllerPlayback"]
            self.assertEqual(playback["status"], "source-looping-controller-complete")
            self.assertEqual(
                playback["animations"],
                [
                    {
                        "name": "Open",
                        "sourceType": "NiControllerSequence",
                        "startSeconds": 0.0,
                        "stopSeconds": 1.0,
                        "frequency": 1.0,
                        "phase": 0.0,
                        "channels": 2,
                    },
                    {
                        "name": "NiGeomMorpherController",
                        "sourceType": "NiGeomMorpherController",
                        "startSeconds": 0.0,
                        "stopSeconds": 1.0,
                        "frequency": 1.0,
                        "phase": 0.0,
                        "channels": 1,
                    },
                ],
            )
            gltf = json.loads((directory / "source-loop.gltf").read_text())
            self.assertEqual(len(gltf["animations"]), 2)
            self.assertEqual(gltf["animations"][0]["name"], "Open")
            self.assertEqual(gltf["animations"][1]["name"], "NiGeomMorpherController")
            self.assertEqual(len(gltf["animations"][1]["channels"]), 1)
            self.assertEqual(
                [len(mesh.get("weights", [])) for mesh in gltf["meshes"]],
                [1, 0],
            )
            controlled = next(
                node for node in gltf["nodes"] if node["name"] == "BGate"
            )
            self.assertTrue(controlled["children"])
            self.assertEqual(len(gltf["animations"][0]["channels"]), 2)
            self.assertEqual(
                sorted(channel["target"]["path"] for channel in gltf["animations"][0]["channels"]),
                ["rotation", "translation"],
            )

    def test_controller_door_groups_only_authored_target_visual_and_collision(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            directory = Path(raw_directory)
            source = directory / "controller-door.nif"
            source.write_bytes(b"synthetic decoded controller door")
            document, gate_collision = synthetic_controller_door_document()
            decoded = SimpleNamespace(
                document=document,
                evidence=lambda: {"status": "synthetic-in-memory-contract"},
            )
            with patch("export_static_nif_gltf.decode_nif", return_value=decoded):
                result = export_static_nif(
                    source,
                    "meshes/open-nv-tests/controller-door.nif",
                    directory / "controller-door.gltf",
                    directory / "controller-door.opennv.json",
                    load_runtime_configuration().content_compiler,
                    strict=False,
                    require_door_articulation=True,
                )

            articulation = result["articulation"]
            canonical = dict(articulation)
            canonical_hash = canonical.pop("canonicalSha256")
            self.assertEqual(
                canonical_hash,
                hashlib.sha256(
                    json.dumps(
                        canonical,
                        sort_keys=True,
                        separators=(",", ":"),
                        ensure_ascii=True,
                    ).encode("utf-8")
                ).hexdigest(),
            )
            self.assertNotEqual(
                articulation["sequences"]["open"]["terminalLocalTransform"],
                articulation["sequences"]["close"]["initialLocalTransform"],
            )
            self.assertEqual(
                articulation["sequences"]["close"]["terminalLocalTransform"],
                articulation["closedLocalTransform"],
            )
            target_id = articulation["target"]["targetId"]
            self.assertEqual(
                [surface["name"] for surface in result["surfaces"] if surface["articulationTargetId"] == target_id],
                ["BGate:0"],
            )
            self.assertEqual(
                [surface["name"] for surface in result["surfaces"] if surface["articulationTargetId"] is None],
                ["BPosts:0"],
            )
            collision_rows = result["coverage"]["collisionBodies"]
            self.assertEqual(
                [row["targetName"] for row in collision_rows if row["ownerTargetId"] == target_id],
                ["BGate"],
            )
            self.assertEqual(
                [row["targetName"] for row in collision_rows if row["ownerTargetId"] is None],
                ["BPosts"],
            )

            visual = json.loads((directory / "controller-door.gltf").read_text())
            visual_wrapper = next(
                node
                for node in visual["nodes"]
                if node["name"] == articulation["target"]["visualNodeName"]
            )
            self.assertNotIn("mesh", visual_wrapper)
            self.assertEqual(
                sorted(visual["nodes"][index]["name"] for index in visual_wrapper["children"]),
                articulation["target"]["visualDescendantNodeNames"],
            )
            collision = json.loads(
                (directory / "controller-door.collision.gltf").read_text()
            )
            self.assertEqual(
                collision["extras"]["openNvSchema"],
                "opennv-authored-collision-gltf/v1",
            )
            self.assertEqual(
                authored_collision_source(result["coverage"]),
                "NIF-authored-bhk-packed-triangles",
            )
            collision_wrapper = next(
                node
                for node in collision["nodes"]
                if node["name"] == articulation["target"]["collisionNodeName"]
            )
            self.assertNotIn("mesh", collision_wrapper)
            self.assertEqual(
                sorted(
                    collision["nodes"][index]["name"]
                    for index in collision_wrapper["children"]
                ),
                articulation["target"]["collisionDescendantNodeNames"],
            )
            gate_body_block = next(
                row["bodyBlock"]
                for row in collision_rows
                if row["targetName"] == "BGate"
            )
            gate_collision_node = next(
                node
                for node in collision["nodes"]
                if node["name"] == f"OPENNV_ARTICULATION_COLLISION_BODY_{gate_body_block}"
            )
            gate_primitive = collision["meshes"][gate_collision_node["mesh"]]["primitives"][0]
            gate_position_accessor = collision["accessors"][
                gate_primitive["attributes"]["POSITION"]
            ]
            gate_position_view = collision["bufferViews"][gate_position_accessor["bufferView"]]
            gate_collision_bytes = (directory / "controller-door.collision.bin").read_bytes()
            self.assertEqual(
                struct.unpack_from("<3f", gate_collision_bytes, gate_position_view["byteOffset"]),
                (0.0, 0.0, -0.0),
            )

            gate_collision.target = next(
                block
                for block in document.get_global_iterator()
                if isinstance(block, NifFormat.NiNode) and bytes(block.name) == b"BPosts"
            )
            with patch("export_static_nif_gltf.decode_nif", return_value=decoded):
                with self.assertRaisesRegex(
                    ValueError,
                    "target has no joined authored collision",
                ):
                    export_static_nif(
                        source,
                        "meshes/open-nv-tests/controller-door.nif",
                        directory / "incomplete.gltf",
                        directory / "incomplete.opennv.json",
                        load_runtime_configuration().content_compiler,
                        strict=False,
                        require_door_articulation=True,
                    )

    def test_controller_door_exports_target_local_mass_zero_static_convex(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            directory = Path(raw_directory)
            source = directory / "controller-convex.nif"
            source.write_bytes(b"synthetic decoded controller convex door")
            document, gate_collision = synthetic_controller_door_document(
                gate_collision_kind="convex",
                include_posts_collision=False,
            )
            decoded = SimpleNamespace(
                document=document,
                evidence=lambda: {"status": "synthetic-in-memory-contract"},
            )

            def export(destination: Path) -> dict[str, object]:
                destination.mkdir()
                with patch("export_static_nif_gltf.decode_nif", return_value=decoded):
                    return export_static_nif(
                        source,
                        "meshes/open-nv-tests/controller-convex.nif",
                        destination / "controller-convex.gltf",
                        destination / "controller-convex.opennv.json",
                        load_runtime_configuration().content_compiler,
                        strict=False,
                        require_door_articulation=True,
                    )

            first_directory = directory / "first"
            second_directory = directory / "second"
            first = export(first_directory)
            second = export(second_directory)
            self.assertEqual(first, second)
            for file_name in (
                "controller-convex.gltf",
                "controller-convex.bin",
                "controller-convex.collision.gltf",
                "controller-convex.collision.bin",
                "controller-convex.opennv.json",
            ):
                self.assertEqual(
                    (first_directory / file_name).read_bytes(),
                    (second_directory / file_name).read_bytes(),
                )

            self.assertEqual(first["coverage"]["collisionBodies"], [])
            convex_rows = first["coverage"]["staticConvexBodies"]
            self.assertEqual(len(convex_rows), 1)
            convex = convex_rows[0]
            blocks = list(document.get_global_iterator())
            block_index = {id(block): index for index, block in enumerate(blocks)}
            shape = gate_collision.body.shape
            self.assertEqual(
                {
                    "collisionObjectBlock": convex["collisionObjectBlock"],
                    "bodyBlock": convex["bodyBlock"],
                    "shapeBlock": convex["shapeBlock"],
                    "targetBlock": convex["targetBlock"],
                },
                {
                    "collisionObjectBlock": block_index[id(gate_collision)],
                    "bodyBlock": block_index[id(gate_collision.body)],
                    "shapeBlock": block_index[id(shape)],
                    "targetBlock": block_index[id(gate_collision.target)],
                },
            )
            self.assertEqual(convex["targetName"], "BGate")
            self.assertEqual(convex["ownerTargetId"], first["articulation"]["target"]["targetId"])
            self.assertEqual(convex["bodyType"], "bhkRigidBody")
            self.assertEqual(convex["shapeType"], "convex-hull-points")
            self.assertEqual(
                convex["shapeTransformPolicy"],
                "articulation-target-local;bhkRigidBody-pose-evidence-only;godot-axis-converted",
            )
            self.assertEqual(convex["sourceBodyTranslationHavokUnits"], [0.25, 0.0, 0.0])
            self.assertEqual(convex["sourceBodyRotation"], [0.0, 0.0, 0.0, 1.0])
            self.assertEqual(convex["mass"], 0.0)
            self.assertEqual(convex["layer"], 2)
            self.assertEqual(convex["flagsAndPartNumber"], 3)
            self.assertEqual(convex["unknownShort"], 4)
            self.assertEqual(convex["material"], 9)
            self.assertAlmostEqual(convex["radiusHavokUnits"], 0.05)
            self.assertAlmostEqual(convex["radiusGameUnits"], 0.35)
            self.assertEqual(convex["vertices"], 8)
            self.assertEqual(convex["triangles"], 0)
            self.assertEqual(convex["pointsGodotGameUnits"][0], (-7.0, -7.0, 7.0))

            collision_gltf = json.loads(
                (first_directory / "controller-convex.collision.gltf").read_text()
            )
            self.assertEqual(
                collision_gltf["extras"]["openNvSchema"],
                "opennv-authored-collision-gltf/v2",
            )
            node_name = f"OPENNV_ARTICULATION_COLLISION_BODY_{convex['bodyBlock']}"
            node = next(row for row in collision_gltf["nodes"] if row["name"] == node_name)
            self.assertEqual(
                node["extras"],
                {
                    "openNvArticulationTargetId": convex["ownerTargetId"],
                    "openNvCollisionBodyBlock": convex["bodyBlock"],
                    "openNvCollisionShapeType": "convex-hull-points",
                },
            )
            primitive = collision_gltf["meshes"][node["mesh"]]["primitives"][0]
            self.assertEqual(primitive["mode"], 0)
            self.assertNotIn("indices", primitive)
            self.assertEqual(
                collision_gltf["accessors"][primitive["attributes"]["POSITION"]]["count"],
                8,
            )
            self.assertEqual(
                first["articulation"]["target"]["collisionDescendantNodeNames"],
                [node_name],
            )

            self.assertEqual(
                authored_collision_source(first["coverage"]),
                "NIF-authored-bhk-static-convex-points",
            )
            self.assertEqual(
                authored_collision_source(
                    {
                        "collisionExported": True,
                        "collisionBodies": [{"bodyBlock": 1}],
                        "staticConvexBodies": [{"bodyBlock": 2}],
                    }
                ),
                "NIF-authored-bhk-packed-triangles-plus-static-convex-points",
            )

    def test_articulated_static_convex_variants_fail_closed(self) -> None:
        document, gate_collision = synthetic_controller_door_document(
            gate_collision_kind="convex",
            include_posts_collision=False,
        )
        root = document.roots[0]
        target = gate_collision.target

        def resolve() -> tuple[list[dict[str, object]], str | None]:
            blocks = list(document.get_global_iterator())
            return collision_contract(
                blocks,
                root,
                {id(block): index for index, block in enumerate(blocks)},
                articulation_target=target,
                articulation_target_id="synthetic-target",
                articulation_descendant_ids={id(target)},
            )

        bodies, reason = collision_contract(
            list(document.get_global_iterator()),
            root,
            {id(block): index for index, block in enumerate(document.get_global_iterator())},
            articulation_target=target,
            articulation_target_id="synthetic-target",
            articulation_descendant_ids=set(),
        )
        self.assertEqual(bodies, [])
        self.assertEqual(reason, "unsupported-static-convex-owner:non-articulated")

        gate_collision.body.mass = 1.0
        self.assertEqual(resolve(), ([], "unsupported-static-convex-mass:1.0"))
        gate_collision.body.mass = 0.0
        for vertex in gate_collision.body.shape.vertices:
            vertex.z = 0.0
        self.assertEqual(resolve(), ([], "invalid-static-convex-points"))

        list_shape = NifFormat.bhkListShape()
        gate_collision.body.shape = list_shape
        self.assertEqual(resolve(), ([], "unsupported-root-shape:bhkListShape"))

        transformed_document, transformed_collision = synthetic_controller_door_document(
            gate_collision_kind="convex-t",
            include_posts_collision=False,
        )
        transformed_root = transformed_document.roots[0]
        transformed_target = transformed_collision.target
        transformed_blocks = list(transformed_document.get_global_iterator())
        transformed_bodies, transformed_reason = collision_contract(
            transformed_blocks,
            transformed_root,
            {
                id(block): index
                for index, block in enumerate(transformed_blocks)
            },
            articulation_target=transformed_target,
            articulation_target_id="synthetic-target",
            articulation_descendant_ids={id(transformed_target)},
        )
        self.assertIsNone(transformed_reason)
        self.assertEqual(len(transformed_bodies), 1)
        self.assertEqual(transformed_bodies[0]["bodyType"], "bhkRigidBodyT")
        self.assertEqual(
            transformed_bodies[0]["shapeTransformPolicy"],
            "articulation-target-local;bhkRigidBodyT-pose-applied;godot-axis-converted",
        )
        self.assertEqual(
            transformed_bodies[0]["pointsGodotGameUnits"][0],
            (-5.25, -7.0, 7.0),
        )

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
                load_runtime_configuration().content_compiler,
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
                    load_runtime_configuration().content_compiler,
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
