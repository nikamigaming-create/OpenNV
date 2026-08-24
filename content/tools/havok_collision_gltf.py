"""Export the bounded authored Fallout packed-triangle collision contract."""

from __future__ import annotations

import json
import struct
from pathlib import Path

from pyffi.formats.nif import NifFormat  # type: ignore

from gltf_io import (
    GL_ARRAY_BUFFER,
    GL_ELEMENT_ARRAY_BUFFER,
    GL_FLOAT,
    GL_UNSIGNED_INT,
    GL_UNSIGNED_SHORT,
    GL_UNSIGNED_SHORT_MAX,
    BufferBuilder,
    atomic_write,
    pack_floats,
    sha256_bytes,
)


HAVOK_TO_GAME_UNITS = 7.0
SCHEMA = "opennv-authored-collision-gltf/v1"


def _decode_text(value: object) -> str:
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    try:
        return bytes(value).decode("utf-8", errors="replace")
    except (TypeError, ValueError):
        return str(value)


def _quaternion_rotate(
    value: tuple[float, float, float],
    quaternion: tuple[float, float, float, float],
) -> tuple[float, float, float]:
    x, y, z = value
    qx, qy, qz, qw = quaternion
    tx = 2.0 * (qy * z - qz * y)
    ty = 2.0 * (qz * x - qx * z)
    tz = 2.0 * (qx * y - qy * x)
    return (
        x + qw * tx + qy * tz - qz * ty,
        y + qw * ty + qz * tx - qx * tz,
        z + qw * tz + qx * ty - qy * tx,
    )


def _collision_position(
    vertex: object,
    packed_shape: object,
    body: object,
    target_matrix: object,
) -> tuple[float, float, float]:
    scale = packed_shape.scale
    value = (
        float(vertex.x) * float(scale.x) * HAVOK_TO_GAME_UNITS,
        float(vertex.y) * float(scale.y) * HAVOK_TO_GAME_UNITS,
        float(vertex.z) * float(scale.z) * HAVOK_TO_GAME_UNITS,
    )
    rotation = body.rotation
    value = _quaternion_rotate(
        value,
        (float(rotation.x), float(rotation.y), float(rotation.z), float(rotation.w)),
    )
    translation = body.translation
    x = value[0] + float(translation.x) * HAVOK_TO_GAME_UNITS
    y = value[1] + float(translation.y) * HAVOK_TO_GAME_UNITS
    z = value[2] + float(translation.z) * HAVOK_TO_GAME_UNITS
    game_x = x * target_matrix.m_11 + y * target_matrix.m_21 + z * target_matrix.m_31 + target_matrix.m_41
    game_y = x * target_matrix.m_12 + y * target_matrix.m_22 + z * target_matrix.m_32 + target_matrix.m_42
    game_z = x * target_matrix.m_13 + y * target_matrix.m_23 + z * target_matrix.m_33 + target_matrix.m_43
    return float(game_x), float(game_z), -float(game_y)


def collision_contract(
    blocks: list[object],
    root: object,
    block_index: dict[int, int],
) -> tuple[list[dict[str, object]], str | None]:
    collision_types = sorted({type(block).__name__ for block in blocks if type(block).__name__.startswith("bhk")})
    if not collision_types:
        return [], None
    collision_objects = [
        block for block in blocks if isinstance(block, NifFormat.bhkCollisionObject)
    ]
    if not collision_objects:
        return [], "no-bhkCollisionObject-root"
    bodies = []
    for collision in collision_objects:
        body = collision.body
        if not isinstance(body, (NifFormat.bhkRigidBody, NifFormat.bhkRigidBodyT)):
            return [], f"unsupported-body:{type(body).__name__}"
        mopp = body.shape
        if not isinstance(mopp, NifFormat.bhkMoppBvTreeShape):
            return [], f"unsupported-root-shape:{type(mopp).__name__}"
        packed = mopp.shape
        if not isinstance(packed, NifFormat.bhkPackedNiTriStripsShape) or packed.data is None:
            return [], f"unsupported-mopp-child:{type(packed).__name__}"
        data = packed.data
        positions = [
            _collision_position(vertex, packed, body, collision.target.get_transform(root))
            for vertex in data.vertices
        ]
        triangles = [
            (int(row.triangle.v_1), int(row.triangle.v_2), int(row.triangle.v_3))
            for row in data.triangles
        ]
        if not positions or not triangles or any(
            index < 0 or index >= len(positions)
            for triangle in triangles
            for index in triangle
        ):
            return [], "invalid-packed-triangle-indices"
        sub_shapes = []
        vertex_start = 0
        for sub_shape in data.sub_shapes:
            vertex_count = int(sub_shape.num_vertices)
            sub_shapes.append(
                {
                    "vertexStart": vertex_start,
                    "vertexCount": vertex_count,
                    "material": int(sub_shape.material.material),
                    "layer": int(sub_shape.havok_col_filter.layer),
                    "flagsAndPartNumber": int(sub_shape.havok_col_filter.flags_and_part_number),
                    "unknownShort": int(sub_shape.havok_col_filter.unknown_short),
                }
            )
            vertex_start += vertex_count
        if sub_shapes and vertex_start != len(positions):
            return [], "sub-shape-vertex-count-mismatch"
        bodies.append(
            {
                "collisionObjectBlock": block_index[id(collision)],
                "bodyBlock": block_index[id(body)],
                "moppBlock": block_index[id(mopp)],
                "packedShapeBlock": block_index[id(packed)],
                "targetBlock": block_index[id(collision.target)],
                "targetName": _decode_text(collision.target.name),
                "motionSystem": int(body.motion_system),
                "qualityType": int(body.quality_type),
                "layer": int(body.havok_col_filter.layer),
                "flagsAndPartNumber": int(body.havok_col_filter.flags_and_part_number),
                "unknownShort": int(body.havok_col_filter.unknown_short),
                "positions": positions,
                "triangles": triangles,
                "subShapes": sub_shapes,
            }
        )
    return bodies, None


def write_collision_gltf(
    bodies: list[dict[str, object]],
    gltf_path: Path,
    source_hash: str,
    generator: str,
) -> dict[str, object]:
    builder = BufferBuilder()
    meshes = []
    nodes = []
    for body_index, body in enumerate(bodies):
        positions = body["positions"]
        triangles = body["triangles"]
        position_accessor = builder.add(
            pack_floats(positions),
            component_type=GL_FLOAT,
            count=len(positions),
            value_type="VEC3",
            target=GL_ARRAY_BUFFER,
            minimum=[min(value[axis] for value in positions) for axis in range(3)],
            maximum=[max(value[axis] for value in positions) for axis in range(3)],
        )
        indices = [value for triangle in triangles for value in triangle]
        index_component = (
            GL_UNSIGNED_SHORT if len(positions) <= GL_UNSIGNED_SHORT_MAX else GL_UNSIGNED_INT
        )
        index_format = "H" if index_component == GL_UNSIGNED_SHORT else "I"
        index_accessor = builder.add(
            struct.pack(f"<{len(indices)}{index_format}", *indices),
            component_type=index_component,
            count=len(indices),
            value_type="SCALAR",
            target=GL_ELEMENT_ARRAY_BUFFER,
        )
        meshes.append(
            {
                "name": f"AUTHORED_COLLISION_BODY_{body_index}",
                "primitives": [{
                    "attributes": {"POSITION": position_accessor},
                    "indices": index_accessor,
                    "mode": 4,
                }],
            }
        )
        nodes.append({"name": f"AUTHORED_COLLISION_BODY_{body_index}", "mesh": body_index})
    binary_name = gltf_path.with_suffix(".bin").name
    gltf = {
        "asset": {"version": "2.0", "generator": f"{generator} authored collision"},
        "scene": 0,
        "scenes": [{"nodes": list(range(len(nodes)))}],
        "nodes": nodes,
        "meshes": meshes,
        "buffers": [{"uri": binary_name, "byteLength": len(builder.data)}],
        "bufferViews": builder.views,
        "accessors": builder.accessors,
        "extras": {"openNvSchema": SCHEMA, "sourceSha256": source_hash},
    }
    gltf_bytes = (json.dumps(gltf, indent=2, sort_keys=True) + "\n").encode()
    binary_bytes = bytes(builder.data)
    atomic_write(gltf_path.with_suffix(".bin"), binary_bytes)
    atomic_write(gltf_path, gltf_bytes)
    return {
        "gltf": {"file": gltf_path.name, "bytes": len(gltf_bytes), "sha256": sha256_bytes(gltf_bytes)},
        "buffer": {"file": binary_name, "bytes": len(binary_bytes), "sha256": sha256_bytes(binary_bytes)},
    }
