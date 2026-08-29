"""Export the bounded authored Fallout packed-triangle collision contract."""

from __future__ import annotations

import json
import math
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
SCHEMA_V1 = "opennv-authored-collision-gltf/v1"
SCHEMA_V2 = "opennv-authored-collision-gltf/v2"
PLACED_BODY_TRANSFORM_POLICY = (
    "reference-transform-authoritative;body-pose-retained-as-source-evidence"
)
ARTICULATED_RIGID_BODY_CONVEX_TRANSFORM_POLICY = (
    "articulation-target-local;bhkRigidBody-pose-evidence-only;godot-axis-converted"
)
ARTICULATED_RIGID_BODY_T_CONVEX_TRANSFORM_POLICY = (
    "articulation-target-local;bhkRigidBodyT-pose-applied;godot-axis-converted"
)


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


def _static_convex_position(
    vertex: object,
    body: object,
    target_matrix: object,
) -> tuple[float, float, float]:
    value = (
        float(vertex.x) * HAVOK_TO_GAME_UNITS,
        float(vertex.y) * HAVOK_TO_GAME_UNITS,
        float(vertex.z) * HAVOK_TO_GAME_UNITS,
    )
    if isinstance(body, NifFormat.bhkRigidBodyT):
        rotation = body.rotation
        value = _quaternion_rotate(
            value,
            (float(rotation.x), float(rotation.y), float(rotation.z), float(rotation.w)),
        )
        translation = body.translation
        value = (
            value[0] + float(translation.x) * HAVOK_TO_GAME_UNITS,
            value[1] + float(translation.y) * HAVOK_TO_GAME_UNITS,
            value[2] + float(translation.z) * HAVOK_TO_GAME_UNITS,
        )
    x, y, z = value
    game_x = x * target_matrix.m_11 + y * target_matrix.m_21 + z * target_matrix.m_31 + target_matrix.m_41
    game_y = x * target_matrix.m_12 + y * target_matrix.m_22 + z * target_matrix.m_32 + target_matrix.m_42
    game_z = x * target_matrix.m_13 + y * target_matrix.m_23 + z * target_matrix.m_33 + target_matrix.m_43
    return float(game_x), float(game_z), -float(game_y)


def _valid_convex_points(points: list[tuple[float, float, float]]) -> bool:
    if len(points) < 4 or not all(math.isfinite(value) for point in points for value in point):
        return False
    unique = list(dict.fromkeys(points))
    if len(unique) < 4:
        return False
    origin = unique[0]
    vectors = [
        (point[0] - origin[0], point[1] - origin[1], point[2] - origin[2])
        for point in unique[1:]
    ]
    for first_index, first in enumerate(vectors):
        for second_index in range(first_index + 1, len(vectors)):
            second = vectors[second_index]
            cross = (
                first[1] * second[2] - first[2] * second[1],
                first[2] * second[0] - first[0] * second[2],
                first[0] * second[1] - first[1] * second[0],
            )
            for third in vectors[second_index + 1:]:
                volume = cross[0] * third[0] + cross[1] * third[1] + cross[2] * third[2]
                if abs(volume) > 1.0e-9:
                    return True
    return False


def _target_local_transform(target: object, parent: object) -> object:
    if target is parent:
        matrix = NifFormat.Matrix44()
        matrix.set_identity()
        return matrix
    return target.get_transform(parent)


def _godot_game_units(vertex: object) -> tuple[float, float, float]:
    """Convert one Havok-local point without applying serialized body state."""

    return (
        float(vertex.x) * HAVOK_TO_GAME_UNITS,
        float(vertex.z) * HAVOK_TO_GAME_UNITS,
        -float(vertex.y) * HAVOK_TO_GAME_UNITS,
    )


def _convex_hull_contract(
    shape: object,
    block_index: dict[int, int],
) -> dict[str, object] | None:
    if not isinstance(shape, NifFormat.bhkConvexVerticesShape):
        return None
    points = [_godot_game_units(vertex) for vertex in shape.vertices]
    if len(points) < 4:
        return None
    return {
        "shapeBlock": block_index[id(shape)],
        "radiusHavokUnits": float(shape.radius),
        "radiusGameUnits": float(shape.radius) * HAVOK_TO_GAME_UNITS,
        "pointsGodotGameUnits": points,
    }


def dynamic_physics_contract(
    blocks: list[object],
    block_index: dict[int, int],
) -> tuple[list[dict[str, object]], list[str]]:
    """Export dynamic convex bodies without inventing primitive collision."""

    bodies: list[dict[str, object]] = []
    unsupported: list[str] = []
    for collision in (
        block for block in blocks if isinstance(block, NifFormat.bhkCollisionObject)
    ):
        body = collision.body
        if not isinstance(body, (NifFormat.bhkRigidBody, NifFormat.bhkRigidBodyT)):
            unsupported.append(f"unsupported-body:{type(body).__name__}")
            continue
        if float(body.mass) <= 0.0:
            continue
        root_shape = body.shape
        if isinstance(root_shape, NifFormat.bhkConvexVerticesShape):
            shape_candidates = [root_shape]
            shape_type = "convex-hull"
        elif isinstance(root_shape, NifFormat.bhkListShape):
            shape_candidates = list(root_shape.sub_shapes)
            shape_type = "compound-convex-hulls"
        else:
            unsupported.append(f"unsupported-root-shape:{type(root_shape).__name__}")
            continue
        hulls = [
            hull
            for shape in shape_candidates
            if (hull := _convex_hull_contract(shape, block_index)) is not None
        ]
        if len(hulls) != len(shape_candidates) or not hulls:
            unsupported.append(f"unsupported-convex-child:{type(root_shape).__name__}")
            continue
        translation = body.translation
        rotation = body.rotation
        bodies.append(
            {
                "collisionObjectBlock": block_index[id(collision)],
                "bodyBlock": block_index[id(body)],
                "targetBlock": block_index[id(collision.target)],
                "targetName": _decode_text(collision.target.name),
                "shapeType": shape_type,
                "shapeTransformPolicy": PLACED_BODY_TRANSFORM_POLICY,
                "sourceBodyTranslationHavokUnits": [
                    float(translation.x),
                    float(translation.y),
                    float(translation.z),
                ],
                "sourceBodyRotation": [
                    float(rotation.x),
                    float(rotation.y),
                    float(rotation.z),
                    float(rotation.w),
                ],
                "mass": float(body.mass),
                "friction": float(body.friction),
                "restitution": float(body.restitution),
                "linearDamping": float(body.linear_damping),
                "angularDamping": float(body.angular_damping),
                "motionSystem": int(body.motion_system),
                "qualityType": int(body.quality_type),
                "layer": int(body.havok_col_filter.layer),
                "flagsAndPartNumber": int(body.havok_col_filter.flags_and_part_number),
                "unknownShort": int(body.havok_col_filter.unknown_short),
                "hulls": hulls,
            }
        )
    return bodies, sorted(set(unsupported))


def collision_contract(
    blocks: list[object],
    root: object,
    block_index: dict[int, int],
    *,
    articulation_target: object | None = None,
    articulation_target_id: str | None = None,
    articulation_descendant_ids: set[int] | None = None,
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
    articulation_descendant_ids = articulation_descendant_ids or set()
    for collision in collision_objects:
        body = collision.body
        if not isinstance(body, (NifFormat.bhkRigidBody, NifFormat.bhkRigidBodyT)):
            return [], f"unsupported-body:{type(body).__name__}"
        if collision.target is None:
            return [], "collision-object-has-no-target"
        owns_articulation = id(collision.target) in articulation_descendant_ids
        if owns_articulation and articulation_target is None:
            return [], "articulation-collision-has-no-target-root"
        if owns_articulation and not articulation_target_id:
            return [], "articulation-collision-has-no-target-id"
        transform_parent = articulation_target if owns_articulation else root
        root_shape = body.shape
        if isinstance(root_shape, NifFormat.bhkConvexVerticesShape):
            if not owns_articulation:
                return [], "unsupported-static-convex-owner:non-articulated"
            mass = float(body.mass)
            radius = float(root_shape.radius)
            if not math.isfinite(mass) or mass != 0.0:
                return [], f"unsupported-static-convex-mass:{mass}"
            if not math.isfinite(radius) or radius < 0.0:
                return [], f"invalid-static-convex-radius:{radius}"
            source_vertices = list(root_shape.vertices)
            if any(
                not math.isfinite(float(getattr(vertex, "w", 0.0)))
                or float(getattr(vertex, "w", 0.0)) != 0.0
                for vertex in source_vertices
            ):
                return [], "invalid-static-convex-vertex-w"
            positions = [
                _static_convex_position(
                    vertex,
                    body,
                    _target_local_transform(collision.target, transform_parent),
                )
                for vertex in source_vertices
            ]
            if not _valid_convex_points(positions):
                return [], "invalid-static-convex-points"
            translation = body.translation
            rotation = body.rotation
            body_values = [
                float(translation.x),
                float(translation.y),
                float(translation.z),
                float(rotation.x),
                float(rotation.y),
                float(rotation.z),
                float(rotation.w),
                float(body.friction),
                float(body.restitution),
                float(body.linear_damping),
                float(body.angular_damping),
            ]
            if not all(math.isfinite(value) for value in body_values):
                return [], "invalid-static-convex-body-values"
            if isinstance(body, NifFormat.bhkRigidBodyT):
                rotation_length_squared = sum(value * value for value in body_values[3:7])
                if abs(rotation_length_squared - 1.0) > 1.0e-4:
                    return [], "invalid-static-convex-rigidbodyt-quaternion"
                transform_policy = ARTICULATED_RIGID_BODY_T_CONVEX_TRANSFORM_POLICY
            else:
                transform_policy = ARTICULATED_RIGID_BODY_CONVEX_TRANSFORM_POLICY
            bodies.append(
                {
                    "collisionObjectBlock": block_index[id(collision)],
                    "bodyBlock": block_index[id(body)],
                    "shapeBlock": block_index[id(root_shape)],
                    "targetBlock": block_index[id(collision.target)],
                    "targetName": _decode_text(collision.target.name),
                    "ownerTargetId": articulation_target_id,
                    "bodyType": type(body).__name__,
                    "shapeType": "convex-hull-points",
                    "shapeTransformPolicy": transform_policy,
                    "sourceBodyTranslationHavokUnits": body_values[:3],
                    "sourceBodyRotation": body_values[3:7],
                    "mass": mass,
                    "friction": float(body.friction),
                    "restitution": float(body.restitution),
                    "linearDamping": float(body.linear_damping),
                    "angularDamping": float(body.angular_damping),
                    "motionSystem": int(body.motion_system),
                    "qualityType": int(body.quality_type),
                    "layer": int(body.havok_col_filter.layer),
                    "flagsAndPartNumber": int(body.havok_col_filter.flags_and_part_number),
                    "unknownShort": int(body.havok_col_filter.unknown_short),
                    "material": int(root_shape.material.material),
                    "radiusHavokUnits": radius,
                    "radiusGameUnits": radius * HAVOK_TO_GAME_UNITS,
                    "pointsGodotGameUnits": positions,
                    "positions": positions,
                    "triangles": [],
                    "subShapes": [],
                }
            )
            continue
        mopp = root_shape
        if not isinstance(mopp, NifFormat.bhkMoppBvTreeShape):
            return [], f"unsupported-root-shape:{type(mopp).__name__}"
        packed = mopp.shape
        if not isinstance(packed, NifFormat.bhkPackedNiTriStripsShape) or packed.data is None:
            return [], f"unsupported-mopp-child:{type(packed).__name__}"
        data = packed.data
        positions = [
            _collision_position(
                vertex,
                packed,
                body,
                _target_local_transform(collision.target, transform_parent),
            )
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
                "ownerTargetId": articulation_target_id if owns_articulation else None,
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
    *,
    articulation: dict[str, object] | None = None,
) -> dict[str, object]:
    builder = BufferBuilder()
    meshes = []
    nodes = []
    scene_nodes = []
    articulation_children = []
    target_id = (
        str(articulation["target"]["targetId"])
        if articulation is not None
        else None
    )
    for body_index, body in enumerate(bodies):
        positions = body["positions"]
        triangles = body["triangles"]
        shape_type = str(body.get("shapeType", "packed-triangle-mesh"))
        position_accessor = builder.add(
            pack_floats(positions),
            component_type=GL_FLOAT,
            count=len(positions),
            value_type="VEC3",
            target=GL_ARRAY_BUFFER,
            minimum=[min(value[axis] for value in positions) for axis in range(3)],
            maximum=[max(value[axis] for value in positions) for axis in range(3)],
        )
        primitive: dict[str, object] = {"attributes": {"POSITION": position_accessor}}
        if shape_type == "packed-triangle-mesh":
            indices = [value for triangle in triangles for value in triangle]
            if not indices:
                raise ValueError("Authored packed collision body has no triangles")
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
            primitive.update({"indices": index_accessor, "mode": 4})
        elif shape_type == "convex-hull-points":
            if triangles:
                raise ValueError("Authored convex collision point body unexpectedly has triangles")
            primitive["mode"] = 0
        else:
            raise ValueError(f"Unsupported authored collision shape type: {shape_type}")
        body_target_id = body.get("ownerTargetId")
        if body_target_id is not None and body_target_id != target_id:
            raise ValueError("Authored collision body has an unmatched articulation target")
        node_name = (
            f"OPENNV_ARTICULATION_COLLISION_BODY_{body['bodyBlock']}"
            if body_target_id is not None
            else f"AUTHORED_COLLISION_BODY_{body_index}"
        )
        meshes.append(
            {
                "name": node_name,
                "primitives": [primitive],
            }
        )
        node: dict[str, object] = {"name": node_name, "mesh": body_index}
        if shape_type == "convex-hull-points":
            node["extras"] = {
                "openNvCollisionBodyBlock": int(body["bodyBlock"]),
                "openNvCollisionShapeType": shape_type,
            }
        if body_target_id is not None:
            node.setdefault("extras", {})["openNvArticulationTargetId"] = body_target_id
            articulation_children.append(len(nodes))
        else:
            scene_nodes.append(len(nodes))
        nodes.append(node)
    if articulation is not None:
        target = articulation["target"]
        expected_children = sorted(target["collisionDescendantNodeNames"])
        actual_children = sorted(nodes[index]["name"] for index in articulation_children)
        if actual_children != expected_children or not actual_children:
            raise ValueError("Authored collision articulation descendants do not match contract")
        closed_transform = articulation["closedLocalTransform"]
        wrapper_index = len(nodes)
        nodes.append(
            {
                "name": target["collisionNodeName"],
                "translation": closed_transform["translationGodotUnits"],
                "rotation": closed_transform["rotationGodotQuaternion"],
                "scale": [closed_transform["scale"]] * 3,
                "children": articulation_children,
                "extras": {"openNvArticulationTargetId": target_id},
            }
        )
        scene_nodes.append(wrapper_index)
    binary_name = gltf_path.with_suffix(".bin").name
    gltf = {
        "asset": {"version": "2.0", "generator": f"{generator} authored collision"},
        "scene": 0,
        "scenes": [{"nodes": scene_nodes}],
        "nodes": nodes,
        "meshes": meshes,
        "buffers": [{"uri": binary_name, "byteLength": len(builder.data)}],
        "bufferViews": builder.views,
        "accessors": builder.accessors,
        "extras": {
            "openNvSchema": (
                SCHEMA_V2
                if any(body.get("shapeType") == "convex-hull-points" for body in bodies)
                else SCHEMA_V1
            ),
            "sourceSha256": source_hash,
        },
    }
    gltf_bytes = (json.dumps(gltf, indent=2, sort_keys=True) + "\n").encode()
    binary_bytes = bytes(builder.data)
    atomic_write(gltf_path.with_suffix(".bin"), binary_bytes)
    atomic_write(gltf_path, gltf_bytes)
    return {
        "gltf": {"file": gltf_path.name, "bytes": len(gltf_bytes), "sha256": sha256_bytes(gltf_bytes)},
        "buffer": {"file": binary_name, "bytes": len(binary_bytes), "sha256": sha256_bytes(binary_bytes)},
    }
