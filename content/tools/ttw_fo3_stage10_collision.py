"""Compile exact non-packed TTW Vault 101 static collision contracts."""

from __future__ import annotations

import hashlib
import math
from dataclasses import dataclass

from pyffi.formats.nif import NifFormat  # type: ignore

from havok_collision_gltf import HAVOK_TO_GAME_UNITS
from nif_decoder import decode_nif


SCHEMA = "opennv-ttw-fo3-stage10-collision/v1"
STATUS = "exact-owned-havok-collision-shapes-compiled"
SOURCE_AUTHORITY = "owned-effective-ttw-nif-havok-graph"
RIGID_BODY_POLICY = (
    "target-root-local;bhkRigidBody-pose-evidence-only;godot-axis-converted"
)
RIGID_BODY_T_POLICY = (
    "target-root-local;bhkRigidBodyT-pose-applied;godot-axis-converted"
)
DYNAMIC_BODY_POLICY = (
    "reference-transform-authoritative;body-pose-retained-as-source-evidence;"
    "godot-axis-converted"
)
VECTOR_COMPONENTS = 3
QUATERNION_COMPONENTS = 4
MINIMUM_CONVEX_POINTS = 4
MATRIX_SCALE_TOLERANCE = 1.0e-5
VOLUME_EPSILON = 1.0e-9


@dataclass(frozen=True)
class _Affine:
    basis: tuple[tuple[float, float, float], ...]
    translation: tuple[float, float, float]


IDENTITY_AFFINE = _Affine(
    ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)),
    (0.0, 0.0, 0.0),
)


def _decode_text(value: object) -> str:
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    try:
        return bytes(value).decode("utf-8", errors="replace")
    except (TypeError, ValueError):
        return str(value)


def _finite(values: tuple[float, ...], label: str) -> tuple[float, ...]:
    if not all(math.isfinite(value) for value in values):
        raise ValueError(f"TTW stage-10 collision {label} is not finite")
    return values


def _matrix_affine(matrix: object) -> _Affine:
    return _Affine(
        (
            (float(matrix.m_11), float(matrix.m_21), float(matrix.m_31)),
            (float(matrix.m_12), float(matrix.m_22), float(matrix.m_32)),
            (float(matrix.m_13), float(matrix.m_23), float(matrix.m_33)),
        ),
        (float(matrix.m_41), float(matrix.m_42), float(matrix.m_43)),
    )


def _multiply_basis(
    left: tuple[tuple[float, float, float], ...],
    right: tuple[tuple[float, float, float], ...],
) -> tuple[tuple[float, float, float], ...]:
    return tuple(
        tuple(
            sum(left[row][axis] * right[axis][column] for axis in range(3))
            for column in range(3)
        )
        for row in range(3)
    )


def _apply_basis(
    basis: tuple[tuple[float, float, float], ...],
    value: tuple[float, float, float],
) -> tuple[float, float, float]:
    return tuple(
        sum(basis[row][axis] * value[axis] for axis in range(3))
        for row in range(3)
    )


def _compose(parent: _Affine, child: _Affine) -> _Affine:
    translated = _apply_basis(parent.basis, child.translation)
    return _Affine(
        _multiply_basis(parent.basis, child.basis),
        tuple(translated[index] + parent.translation[index] for index in range(3)),
    )


def _apply_affine(
    affine: _Affine,
    value: tuple[float, float, float],
    *,
    direction: bool,
) -> tuple[float, float, float]:
    result = _apply_basis(affine.basis, value)
    if direction:
        return result
    return tuple(result[index] + affine.translation[index] for index in range(3))


def _quaternion_rotate(
    value: tuple[float, float, float],
    quaternion: tuple[float, float, float, float],
) -> tuple[float, float, float]:
    x, y, z = value
    qx, qy, qz, qw = quaternion
    temporary_x = 2.0 * (qy * z - qz * y)
    temporary_y = 2.0 * (qz * x - qx * z)
    temporary_z = 2.0 * (qx * y - qy * x)
    return (
        x + qw * temporary_x + qy * temporary_z - qz * temporary_y,
        y + qw * temporary_y + qz * temporary_x - qx * temporary_z,
        z + qw * temporary_z + qx * temporary_y - qy * temporary_x,
    )


def _body_quaternion(body: object) -> tuple[float, float, float, float]:
    source = body.rotation
    quaternion = _finite(
        (
            float(source.x),
            float(source.y),
            float(source.z),
            float(source.w),
        ),
        "body rotation",
    )
    if isinstance(body, NifFormat.bhkRigidBodyT) and float(body.mass) == 0.0:
        length_squared = sum(value * value for value in quaternion)
        if abs(length_squared - 1.0) > MATRIX_SCALE_TOLERANCE:
            raise ValueError("TTW stage-10 bhkRigidBodyT quaternion is not normalized")
    return quaternion


def _body_translation(body: object) -> tuple[float, float, float]:
    source = body.translation
    return _finite(
        (float(source.x), float(source.y), float(source.z)),
        "body translation",
    )


def _point_godot_game_units(
    point_havok: tuple[float, float, float],
    shape_affine: _Affine,
    body: object,
    target_affine: _Affine,
) -> tuple[float, float, float]:
    source = _apply_affine(shape_affine, point_havok, direction=False)
    if isinstance(body, NifFormat.bhkRigidBodyT) and float(body.mass) == 0.0:
        source = _quaternion_rotate(source, _body_quaternion(body))
        translation = _body_translation(body)
        source = tuple(source[index] + translation[index] for index in range(3))
    game = tuple(value * HAVOK_TO_GAME_UNITS for value in source)
    game = _apply_affine(target_affine, game, direction=False)
    return float(game[0]), float(game[2]), -float(game[1])


def _direction_godot_game_units(
    direction_havok: tuple[float, float, float],
    shape_affine: _Affine,
    body: object,
    target_affine: _Affine,
) -> tuple[float, float, float]:
    source = _apply_affine(shape_affine, direction_havok, direction=True)
    if isinstance(body, NifFormat.bhkRigidBodyT):
        source = _quaternion_rotate(source, _body_quaternion(body))
    game = tuple(value * HAVOK_TO_GAME_UNITS for value in source)
    game = _apply_affine(target_affine, game, direction=True)
    return float(game[0]), float(game[2]), -float(game[1])


def _uniform_radius_scale(
    shape_affine: _Affine,
    body: object,
    target_affine: _Affine,
) -> float:
    lengths = []
    for axis in range(VECTOR_COMPONENTS):
        direction = tuple(1.0 if index == axis else 0.0 for index in range(3))
        converted = _direction_godot_game_units(
            direction,
            shape_affine,
            body,
            target_affine,
        )
        lengths.append(math.sqrt(sum(value * value for value in converted)))
    if (
        not all(math.isfinite(value) and value > 0.0 for value in lengths)
        or max(lengths) - min(lengths) > MATRIX_SCALE_TOLERANCE * max(lengths)
    ):
        raise ValueError("TTW stage-10 analytic collision has non-uniform scale")
    return sum(lengths) / VECTOR_COMPONENTS


def _has_volume(points: list[tuple[float, float, float]]) -> bool:
    if len(points) < MINIMUM_CONVEX_POINTS:
        return False
    origin = points[0]
    for first in range(1, len(points) - 2):
        first_edge = tuple(points[first][axis] - origin[axis] for axis in range(3))
        for second in range(first + 1, len(points) - 1):
            second_edge = tuple(points[second][axis] - origin[axis] for axis in range(3))
            cross = (
                first_edge[1] * second_edge[2] - first_edge[2] * second_edge[1],
                first_edge[2] * second_edge[0] - first_edge[0] * second_edge[2],
                first_edge[0] * second_edge[1] - first_edge[1] * second_edge[0],
            )
            for third in range(second + 1, len(points)):
                third_edge = tuple(
                    points[third][axis] - origin[axis] for axis in range(3)
                )
                if abs(sum(cross[axis] * third_edge[axis] for axis in range(3))) > VOLUME_EPSILON:
                    return True
    return False


def _shape_material(shape: object) -> int:
    material = getattr(getattr(shape, "material", None), "material", None)
    if material is None:
        raise ValueError("TTW stage-10 collision shape material is absent")
    return int(material)


def _convex_shape(
    shape: object,
    shape_affine: _Affine,
    body: object,
    target_affine: _Affine,
    block_index: dict[int, int],
    containers: tuple[dict[str, object], ...],
) -> dict[str, object]:
    vertices = list(shape.vertices)
    if any(float(getattr(vertex, "w", 0.0)) != 0.0 for vertex in vertices):
        raise ValueError("TTW stage-10 convex collision vertex W differs")
    points = [
        _point_godot_game_units(
            (float(vertex.x), float(vertex.y), float(vertex.z)),
            shape_affine,
            body,
            target_affine,
        )
        for vertex in vertices
    ]
    if not _has_volume(points):
        raise ValueError("TTW stage-10 convex collision has no volume")
    radius = float(shape.radius)
    if not math.isfinite(radius) or radius < 0.0:
        raise ValueError("TTW stage-10 convex collision radius is invalid")
    return {
        "shapeBlock": block_index[id(shape)],
        "sourceShapeType": "bhkConvexVerticesShape",
        "godotShapeType": "ConvexPolygonShape3D",
        "material": _shape_material(shape),
        "containerShapes": list(containers),
        "radiusHavokUnits": radius,
        "pointsGodotGameUnits": [list(point) for point in points],
    }


def _box_shape(
    shape: object,
    shape_affine: _Affine,
    body: object,
    target_affine: _Affine,
    block_index: dict[int, int],
    containers: tuple[dict[str, object], ...],
) -> dict[str, object]:
    half_extents = _finite(
        (
            float(shape.dimensions.x),
            float(shape.dimensions.y),
            float(shape.dimensions.z),
        ),
        "box half extents",
    )
    if not all(value > 0.0 for value in half_extents):
        raise ValueError("TTW stage-10 box half extents are invalid")
    points = [
        _point_godot_game_units(
            (source_x, source_y, source_z),
            shape_affine,
            body,
            target_affine,
        )
        for source_x in (-half_extents[0], half_extents[0])
        for source_y in (-half_extents[1], half_extents[1])
        for source_z in (-half_extents[2], half_extents[2])
    ]
    if not _has_volume(points):
        raise ValueError("TTW stage-10 box collision has no volume")
    radius = float(shape.radius)
    minimum_size = float(shape.minimum_size)
    if (
        not math.isfinite(radius)
        or radius < 0.0
        or not math.isfinite(minimum_size)
        or minimum_size <= 0.0
    ):
        raise ValueError("TTW stage-10 box collision metadata is invalid")
    return {
        "shapeBlock": block_index[id(shape)],
        "sourceShapeType": "bhkBoxShape",
        "godotShapeType": "ConvexPolygonShape3D",
        "material": _shape_material(shape),
        "containerShapes": list(containers),
        "radiusHavokUnits": radius,
        "minimumSizeHavokUnits": minimum_size,
        "halfExtentsHavokUnits": list(half_extents),
        "pointsGodotGameUnits": [list(point) for point in points],
    }


def _sphere_shape(
    shape: object,
    shape_affine: _Affine,
    body: object,
    target_affine: _Affine,
    block_index: dict[int, int],
    containers: tuple[dict[str, object], ...],
) -> dict[str, object]:
    source_radius = float(shape.radius)
    if not math.isfinite(source_radius) or source_radius <= 0.0:
        raise ValueError("TTW stage-10 sphere collision radius is invalid")
    scale = _uniform_radius_scale(shape_affine, body, target_affine)
    return {
        "shapeBlock": block_index[id(shape)],
        "sourceShapeType": "bhkSphereShape",
        "godotShapeType": "SphereShape3D",
        "material": _shape_material(shape),
        "containerShapes": list(containers),
        "centerGodotGameUnits": list(
            _point_godot_game_units(
                (0.0, 0.0, 0.0),
                shape_affine,
                body,
                target_affine,
            )
        ),
        "radiusHavokUnits": source_radius,
        "radiusGodotGameUnits": source_radius * scale,
    }


def _capsule_shape(
    shape: object,
    shape_affine: _Affine,
    body: object,
    target_affine: _Affine,
    block_index: dict[int, int],
    containers: tuple[dict[str, object], ...],
) -> dict[str, object]:
    source_radii = _finite(
        (float(shape.radius), float(shape.radius_1), float(shape.radius_2)),
        "capsule radii",
    )
    if (
        source_radii[0] <= 0.0
        or any(abs(value - source_radii[0]) > MATRIX_SCALE_TOLERANCE for value in source_radii[1:])
    ):
        raise ValueError("TTW stage-10 capsule does not have one exact radius")
    first = shape.first_point
    second = shape.second_point
    first_point = _point_godot_game_units(
        (float(first.x), float(first.y), float(first.z)),
        shape_affine,
        body,
        target_affine,
    )
    second_point = _point_godot_game_units(
        (float(second.x), float(second.y), float(second.z)),
        shape_affine,
        body,
        target_affine,
    )
    segment = tuple(second_point[index] - first_point[index] for index in range(3))
    segment_length = math.sqrt(sum(value * value for value in segment))
    scale = _uniform_radius_scale(shape_affine, body, target_affine)
    radius = source_radii[0] * scale
    if not math.isfinite(segment_length) or segment_length <= 0.0:
        raise ValueError("TTW stage-10 capsule segment is invalid")
    return {
        "shapeBlock": block_index[id(shape)],
        "sourceShapeType": "bhkCapsuleShape",
        "godotShapeType": "CapsuleShape3D",
        "material": _shape_material(shape),
        "containerShapes": list(containers),
        "firstPointGodotGameUnits": list(first_point),
        "secondPointGodotGameUnits": list(second_point),
        "radiusHavokUnits": source_radii[0],
        "radiusGodotGameUnits": radius,
        "heightGodotGameUnits": segment_length + 2.0 * radius,
    }


def _mopp_shape(
    shape: object,
    shape_affine: _Affine,
    body: object,
    target_affine: _Affine,
    block_index: dict[int, int],
    containers: tuple[dict[str, object], ...],
) -> dict[str, object]:
    packed = shape.shape
    if not isinstance(packed, NifFormat.bhkPackedNiTriStripsShape) or packed.data is None:
        raise ValueError(f"unsupported-mopp-child:{type(packed).__name__}")
    data = packed.data
    scale = _finite(
        (float(packed.scale.x), float(packed.scale.y), float(packed.scale.z)),
        "packed collision scale",
    )
    if not all(value > 0.0 for value in scale):
        raise ValueError("TTW stage-10 packed collision scale is invalid")
    points = [
        _point_godot_game_units(
            (
                float(vertex.x) * scale[0],
                float(vertex.y) * scale[1],
                float(vertex.z) * scale[2],
            ),
            shape_affine,
            body,
            target_affine,
        )
        for vertex in data.vertices
    ]
    triangles = [
        [int(row.triangle.v_1), int(row.triangle.v_2), int(row.triangle.v_3)]
        for row in data.triangles
    ]
    if not points or not triangles or any(
        index < 0 or index >= len(points)
        for triangle in triangles
        for index in triangle
    ):
        raise ValueError("TTW stage-10 packed collision indices are invalid")
    sub_shapes = []
    vertex_start = 0
    for sub_shape in data.sub_shapes:
        vertex_count = int(sub_shape.num_vertices)
        sub_shapes.append(
            {
                "vertexStart": vertex_start,
                "vertexCount": vertex_count,
                "material": int(sub_shape.material.material),
                "filter": {
                    "layer": int(sub_shape.havok_col_filter.layer),
                    "flagsAndPartNumber": int(
                        sub_shape.havok_col_filter.flags_and_part_number
                    ),
                    "unknownShort": int(sub_shape.havok_col_filter.unknown_short),
                },
            }
        )
        vertex_start += vertex_count
    if sub_shapes and vertex_start != len(points):
        raise ValueError("TTW stage-10 packed collision sub-shape span differs")
    return {
        "shapeBlock": block_index[id(shape)],
        "packedShapeBlock": block_index[id(packed)],
        "sourceShapeType": "bhkMoppBvTreeShape",
        "sourcePackedShapeType": "bhkPackedNiTriStripsShape",
        "godotShapeType": "ConcavePolygonShape3D",
        "containerShapes": list(containers),
        "scaleHavokUnits": list(scale),
        "pointsGodotGameUnits": [list(point) for point in points],
        "triangles": triangles,
        "subShapes": sub_shapes,
    }


def _shapes(
    shape: object,
    shape_affine: _Affine,
    body: object,
    target_affine: _Affine,
    block_index: dict[int, int],
    containers: tuple[dict[str, object], ...] = (),
) -> list[dict[str, object]]:
    if isinstance(shape, NifFormat.bhkConvexVerticesShape):
        return [
            _convex_shape(
                shape, shape_affine, body, target_affine, block_index, containers
            )
        ]
    if isinstance(shape, NifFormat.bhkBoxShape):
        return [
            _box_shape(
                shape, shape_affine, body, target_affine, block_index, containers
            )
        ]
    if isinstance(shape, NifFormat.bhkSphereShape):
        return [
            _sphere_shape(
                shape, shape_affine, body, target_affine, block_index, containers
            )
        ]
    if isinstance(shape, NifFormat.bhkCapsuleShape):
        return [
            _capsule_shape(
                shape, shape_affine, body, target_affine, block_index, containers
            )
        ]
    if isinstance(shape, NifFormat.bhkMoppBvTreeShape):
        return [
            _mopp_shape(
                shape, shape_affine, body, target_affine, block_index, containers
            )
        ]
    if isinstance(shape, NifFormat.bhkListShape):
        children = list(shape.sub_shapes)
        if not children:
            raise ValueError("TTW stage-10 collision list is empty")
        chain = (
            *containers,
            {
                "shapeBlock": block_index[id(shape)],
                "sourceShapeType": "bhkListShape",
                "material": _shape_material(shape),
                "childCount": len(children),
            },
        )
        return [
            child
            for source in children
            for child in _shapes(
                source,
                shape_affine,
                body,
                target_affine,
                block_index,
                chain,
            )
        ]
    if isinstance(shape, NifFormat.bhkTransformShape):
        if shape.shape is None:
            raise ValueError("TTW stage-10 transformed collision has no child")
        chain = (
            *containers,
            {
                "shapeBlock": block_index[id(shape)],
                "sourceShapeType": "bhkTransformShape",
                "material": _shape_material(shape),
            },
        )
        return _shapes(
            shape.shape,
            _compose(shape_affine, _matrix_affine(shape.transform)),
            body,
            target_affine,
            block_index,
            chain,
        )
    raise ValueError(f"unsupported-static-shape:{type(shape).__name__}")


def compile_ttw_stage10_collision_document(
    document: object,
    source_sha256: str,
) -> dict[str, object]:
    """Compile exact static collision from a decoded owned NIF document."""

    blocks = list(document.get_global_iterator())
    block_index = {id(block): index for index, block in enumerate(blocks)}
    roots = list(document.roots)
    if len(roots) != 1:
        raise ValueError("TTW stage-10 collision NIF root count differs")
    root = roots[0]
    collision_objects = [
        block for block in blocks if isinstance(block, NifFormat.bhkCollisionObject)
    ]
    if not collision_objects:
        raise ValueError("TTW stage-10 collision NIF has no collision object")
    constraints = sorted(
        {
            type(block).__name__
            for block in blocks
            if type(block).__name__.startswith("bhk")
            and type(block).__name__.endswith("Constraint")
        }
    )
    bodies = []
    for collision in collision_objects:
        body = collision.body
        if not isinstance(body, (NifFormat.bhkRigidBody, NifFormat.bhkRigidBodyT)):
            raise ValueError(f"unsupported-static-body:{type(body).__name__}")
        if collision.target is None:
            raise ValueError("TTW stage-10 collision object has no target")
        mass = float(body.mass)
        if not math.isfinite(mass) or mass < 0.0:
            raise ValueError(f"TTW stage-10 collision body mass is invalid: {mass}")
        target_affine = _matrix_affine(collision.target.get_transform(root))
        shapes = _shapes(
            body.shape,
            IDENTITY_AFFINE,
            body,
            target_affine,
            block_index,
        )
        dynamic = mass > 0.0
        body_values = _finite(
            (
                float(body.friction),
                float(body.restitution),
                float(body.linear_damping),
                float(body.angular_damping),
            ),
            "body material",
        )
        bodies.append(
            {
                "collisionObjectBlock": block_index[id(collision)],
                "bodyBlock": block_index[id(body)],
                "targetBlock": block_index[id(collision.target)],
                "targetName": _decode_text(collision.target.name),
                "bodyType": type(body).__name__,
                "shapeTransformPolicy": (
                    DYNAMIC_BODY_POLICY
                    if dynamic
                    else RIGID_BODY_T_POLICY
                    if isinstance(body, NifFormat.bhkRigidBodyT)
                    else RIGID_BODY_POLICY
                ),
                "sourceBodyTranslationHavokUnits": list(_body_translation(body)),
                "sourceBodyRotation": list(_body_quaternion(body)),
                "mass": mass,
                "dynamic": dynamic,
                "godotBodyType": "RigidBody3D" if dynamic else "StaticBody3D",
                "physicsIntegrationDisposition": (
                    "exact-collision-shape-and-source-body-properties;"
                    "engine-dynamics-and-constraints-parity-not-asserted"
                    if dynamic
                    else "exact-static-collision"
                ),
                "friction": body_values[0],
                "restitution": body_values[1],
                "linearDamping": body_values[2],
                "angularDamping": body_values[3],
                "motionSystem": int(body.motion_system),
                "qualityType": int(body.quality_type),
                "filter": {
                    "layer": int(body.havok_col_filter.layer),
                    "flagsAndPartNumber": int(
                        body.havok_col_filter.flags_and_part_number
                    ),
                    "unknownShort": int(body.havok_col_filter.unknown_short),
                },
                "sourceConstraintTypes": constraints if dynamic else [],
                "shapes": shapes,
            }
        )
    return {
        "schema": SCHEMA,
        "status": STATUS,
        "sourceAuthority": SOURCE_AUTHORITY,
        "sourceSha256": source_sha256,
        "havokToGameUnits": HAVOK_TO_GAME_UNITS,
        "collisionBodyCount": len(bodies),
        "collisionShapeCount": sum(len(body["shapes"]) for body in bodies),
        "staticBodyCount": sum(not body["dynamic"] for body in bodies),
        "dynamicBodyCount": sum(body["dynamic"] for body in bodies),
        "sourceConstraintTypes": constraints,
        "engineDynamicsParityReady": not any(body["dynamic"] for body in bodies),
        "bodies": bodies,
        "sourceFiltersPreserved": True,
        "renderMeshSubstitutionUsed": False,
        "collisionReady": True,
    }


def compile_ttw_stage10_collision(payload: bytes) -> dict[str, object]:
    source_sha256 = hashlib.sha256(payload).hexdigest()
    decoded = decode_nif(payload)
    return compile_ttw_stage10_collision_document(decoded.document, source_sha256)
