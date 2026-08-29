#!/usr/bin/env python3
"""Export one static Gamebryo NIF directly to glTF plus an OpenNV sidecar."""

from __future__ import annotations

import argparse
import json
import math
import struct
import sys
import time
from dataclasses import dataclass
from pathlib import Path

if not hasattr(time, "clock"):
    time.clock = time.perf_counter  # PyFFI 2.2.3 compatibility on Python 3.8+

from pyffi.formats.nif import NifFormat  # type: ignore  # noqa: E402

from actor_material import actor_alpha_contract, nif_material_roughness
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
from compiler_provenance import (
    compiler_provenance as family_compiler_provenance,
    compiler_provenance_source_paths as family_compiler_provenance_source_paths,
)
from havok_collision_gltf import (
    collision_contract,
    dynamic_physics_contract,
    write_collision_gltf,
)
from nif_decoder import decode_nif
from runtime_configuration import (
    ContentCompilerConfiguration,
    configured_recipe_path,
    load_runtime_configuration,
)


SCHEMA = "opennv-static-nif-gltf/v3"
DOOR_ARTICULATION_SCHEMA = "opennv-controller-door-articulation/v1"
GENERATOR = "OpenNV direct static NIF exporter v1"
SUPPORTED_SHAPE_PROPERTIES = {
    "BSShaderPPLightingProperty",
    "NiMaterialProperty",
    "NiStencilProperty",
}
ATTACHMENT_MARKER_NAMES = {"ProjectileNode", "ShellCasingNode"}
NORMALIZATION_EPSILON = 1.0e-12
NON_PRESENTATION_SCHEMA = "opennv-nif-non-presentation/v1"


class NoStaticPresentationGeometryError(ValueError):
    """An owned NIF structurally contains only non-presentation surfaces."""

    def __init__(self, evidence: dict[str, object]) -> None:
        super().__init__("NIF contains only structurally classified non-presentation geometry")
        self.evidence = evidence


@dataclass(frozen=True)
class _PresentationVertex:
    position: tuple[float, float, float]
    normal: tuple[float, float, float]
    uvs: tuple[tuple[float, float], ...]
    color: tuple[float, float, float, float] | None


def _source_axis(vertex: _PresentationVertex, axis: int) -> float:
    if axis == 0:
        return vertex.position[0]
    if axis == 1:
        # Static export converts Gamebryo (X, Y, Z) to Godot (X, Z, -Y).
        return -vertex.position[2]
    raise ValueError(f"Unsupported source-space clip axis: {axis}")


def _interpolate_vertex(
    first: _PresentationVertex,
    second: _PresentationVertex,
    amount: float,
) -> _PresentationVertex:
    amount = min(1.0, max(0.0, amount))

    def interpolate(
        left: tuple[float, ...],
        right: tuple[float, ...],
    ) -> tuple[float, ...]:
        return tuple(a + (b - a) * amount for a, b in zip(left, right))

    normal = interpolate(first.normal, second.normal)
    normal_length = math.sqrt(sum(component * component for component in normal))
    if normal_length <= NORMALIZATION_EPSILON:
        normal = first.normal
    else:
        normal = tuple(component / normal_length for component in normal)
    if len(first.uvs) != len(second.uvs):
        raise ValueError("Presentation clip vertices have different UV set counts")
    if (first.color is None) != (second.color is None):
        raise ValueError("Presentation clip vertices have different color contracts")
    return _PresentationVertex(
        position=interpolate(first.position, second.position),
        normal=normal,
        uvs=tuple(interpolate(left, right) for left, right in zip(first.uvs, second.uvs)),
        color=(
            interpolate(first.color, second.color)
            if first.color is not None and second.color is not None
            else None
        ),
    )


def _clip_polygon_half_plane(
    polygon: list[_PresentationVertex],
    axis: int,
    boundary: float,
    *,
    retain_greater: bool,
    inclusive: bool,
) -> list[_PresentationVertex]:
    if not polygon:
        return []

    def inside(vertex: _PresentationVertex) -> bool:
        coordinate = _source_axis(vertex, axis)
        if retain_greater:
            return coordinate >= boundary if inclusive else coordinate > boundary
        return coordinate <= boundary if inclusive else coordinate < boundary

    result: list[_PresentationVertex] = []
    previous = polygon[-1]
    previous_inside = inside(previous)
    for current in polygon:
        current_inside = inside(current)
        if current_inside != previous_inside:
            previous_coordinate = _source_axis(previous, axis)
            current_coordinate = _source_axis(current, axis)
            denominator = current_coordinate - previous_coordinate
            if abs(denominator) <= NORMALIZATION_EPSILON:
                raise ValueError("Presentation clip crossed a zero-length source-space edge")
            result.append(
                _interpolate_vertex(
                    previous,
                    current,
                    (boundary - previous_coordinate) / denominator,
                )
            )
        if current_inside:
            result.append(current)
        previous = current
        previous_inside = current_inside
    return result


def _triangle_has_area(vertices: tuple[_PresentationVertex, ...]) -> bool:
    first, second, third = (vertex.position for vertex in vertices)
    edge_one = tuple(second[axis] - first[axis] for axis in range(3))
    edge_two = tuple(third[axis] - first[axis] for axis in range(3))
    cross = (
        edge_one[1] * edge_two[2] - edge_one[2] * edge_two[1],
        edge_one[2] * edge_two[0] - edge_one[0] * edge_two[2],
        edge_one[0] * edge_two[1] - edge_one[1] * edge_two[0],
    )
    return sum(component * component for component in cross) > NORMALIZATION_EPSILON


def clip_triangles_outside_source_rectangle(
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    uv_sets: list[list[tuple[float, float]]],
    colors: list[tuple[float, float, float, float]] | None,
    triangles: list[tuple[int, int, int]],
    rectangle: tuple[float, float, float, float],
) -> tuple[
    list[tuple[float, float, float]],
    list[tuple[float, float, float]],
    list[list[tuple[float, float]]],
    list[tuple[float, float, float, float]] | None,
    list[tuple[int, int, int]],
    dict[str, int],
]:
    """Retain triangle fragments outside one source X/Y rectangle.

    LOD block vertices are baked in world game units.  The output partition is
    disjoint by construction: left, right, lower-middle, and upper-middle.
    Intersections interpolate every authored per-vertex presentation channel.
    """

    min_x, max_x, min_y, max_y = rectangle
    if not min_x < max_x or not min_y < max_y:
        raise ValueError(f"Presentation clip rectangle is invalid: {rectangle}")
    vertex_count = len(positions)
    if len(normals) != vertex_count or any(len(uvs) != vertex_count for uvs in uv_sets):
        raise ValueError("Presentation clip attribute counts differ")
    if colors is not None and len(colors) != vertex_count:
        raise ValueError("Presentation clip color count differs")

    vertices = [
        _PresentationVertex(
            position=position,
            normal=normals[index],
            uvs=tuple(uvs[index] for uvs in uv_sets),
            color=colors[index] if colors is not None else None,
        )
        for index, position in enumerate(positions)
    ]
    # The strict/inclusive choices assign every boundary to exactly one region.
    regions = (
        ((0, min_x, False, False),),
        ((0, max_x, True, True),),
        (
            (0, min_x, True, True),
            (0, max_x, False, False),
            (1, min_y, False, False),
        ),
        (
            (0, min_x, True, True),
            (0, max_x, False, False),
            (1, max_y, True, True),
        ),
    )
    output_vertices: list[_PresentationVertex] = []
    output_triangles: list[tuple[int, int, int]] = []
    fully_removed = 0
    clipped = 0
    unchanged = 0
    for triangle in triangles:
        source_polygon = [vertices[index] for index in triangle]
        source_x = [_source_axis(vertex, 0) for vertex in source_polygon]
        source_y = [_source_axis(vertex, 1) for vertex in source_polygon]
        if (
            max(source_x) < min_x
            or min(source_x) >= max_x
            or max(source_y) < min_y
            or min(source_y) >= max_y
        ):
            clipped_triangles = [tuple(source_polygon)]
        else:
            clipped_triangles: list[tuple[_PresentationVertex, ...]] = []
            for region in regions:
                polygon = list(source_polygon)
                for axis, boundary, retain_greater, inclusive in region:
                    polygon = _clip_polygon_half_plane(
                        polygon,
                        axis,
                        boundary,
                        retain_greater=retain_greater,
                        inclusive=inclusive,
                    )
                    if not polygon:
                        break
                for index in range(1, len(polygon) - 1):
                    candidate = (polygon[0], polygon[index], polygon[index + 1])
                    if _triangle_has_area(candidate):
                        clipped_triangles.append(candidate)
        if not clipped_triangles:
            fully_removed += 1
            continue
        if (
            len(clipped_triangles) == 1
            and tuple(vertex.position for vertex in clipped_triangles[0])
            == tuple(vertex.position for vertex in source_polygon)
        ):
            unchanged += 1
        else:
            clipped += 1
        for output_triangle in clipped_triangles:
            first = len(output_vertices)
            output_vertices.extend(output_triangle)
            output_triangles.append((first, first + 1, first + 2))

    return (
        [vertex.position for vertex in output_vertices],
        [vertex.normal for vertex in output_vertices],
        [
            [vertex.uvs[uv_index] for vertex in output_vertices]
            for uv_index in range(len(uv_sets))
        ],
        (
            [vertex.color for vertex in output_vertices if vertex.color is not None]
            if colors is not None
            else None
        ),
        output_triangles,
        {
            "sourceTriangles": len(triangles),
            "unchangedSourceTriangles": unchanged,
            "clippedSourceTriangles": clipped,
            "fullyRemovedSourceTriangles": fully_removed,
            "outputTriangles": len(output_triangles),
            "outputVertices": len(output_vertices),
        },
    )


def decode_text(value: object) -> str:
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    try:
        return bytes(value).decode("utf-8", errors="replace")
    except (TypeError, ValueError):
        return str(value)


def canonical_asset_path(value: object) -> str:
    path = decode_text(value).replace("/", "\\").lstrip("\\").lower()
    data_prefix = "data\\"
    return path[len(data_prefix):] if path.startswith(data_prefix) else path


def _canonical_sha256(document: dict[str, object]) -> str:
    return sha256_bytes(
        json.dumps(
            document,
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=True,
        ).encode("utf-8")
    )


def _converted_rotation(value: list[list[float]]) -> list[list[float]]:
    column = [
        [value[column_index][row_index] for column_index in range(3)]
        for row_index in range(3)
    ]
    conversion = [[1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, -1.0, 0.0]]
    inverse = [[conversion[column][row] for column in range(3)] for row in range(3)]

    def multiply(left: list[list[float]], right: list[list[float]]) -> list[list[float]]:
        return [
            [
                sum(
                    left[row][axis] * right[axis][column]
                    for axis in range(len(right))
                )
                for column in range(len(right[0]))
            ]
            for row in range(len(left))
        ]

    return multiply(conversion, multiply(column, inverse))


def _rotation_quaternion(matrix: list[list[float]]) -> list[float]:
    trace = matrix[0][0] + matrix[1][1] + matrix[2][2]
    if trace > 0.0:
        scale = math.sqrt(trace + 1.0) * 2.0
        result = [
            (matrix[2][1] - matrix[1][2]) / scale,
            (matrix[0][2] - matrix[2][0]) / scale,
            (matrix[1][0] - matrix[0][1]) / scale,
            0.25 * scale,
        ]
    else:
        axis = max(range(3), key=lambda index: matrix[index][index])
        following = (axis + 1) % 3
        remaining = (axis + 2) % 3
        scale = math.sqrt(
            1.0
            + matrix[axis][axis]
            - matrix[following][following]
            - matrix[remaining][remaining]
        ) * 2.0
        result = [0.0, 0.0, 0.0, 0.0]
        result[axis] = 0.25 * scale
        result[3] = (matrix[remaining][following] - matrix[following][remaining]) / scale
        result[following] = (matrix[following][axis] + matrix[axis][following]) / scale
        result[remaining] = (matrix[remaining][axis] + matrix[axis][remaining]) / scale
    length = math.sqrt(sum(value * value for value in result))
    if length <= NORMALIZATION_EPSILON:
        raise ValueError("NIF articulation contains a zero quaternion")
    return [value / length for value in result]


def _node_local_transform(node: object) -> dict[str, object]:
    rotation = _converted_rotation(
        [
            [float(node.rotation.m_11), float(node.rotation.m_12), float(node.rotation.m_13)],
            [float(node.rotation.m_21), float(node.rotation.m_22), float(node.rotation.m_23)],
            [float(node.rotation.m_31), float(node.rotation.m_32), float(node.rotation.m_33)],
        ]
    )
    return {
        "translationGodotUnits": [
            float(node.translation.x),
            float(node.translation.z),
            -float(node.translation.y),
        ],
        "rotationGodotQuaternion": _rotation_quaternion(rotation),
        "scale": float(node.scale),
    }


def _euler_xyz_quaternion(
    angles: tuple[float, float, float],
) -> tuple[float, float, float, float]:
    half_x, half_y, half_z = (angle / 2.0 for angle in angles)
    cx, cy, cz = math.cos(half_x), math.cos(half_y), math.cos(half_z)
    sx, sy, sz = math.sin(half_x), math.sin(half_y), math.sin(half_z)
    result = (
        cx * cy * cz - sx * sy * sz,
        sx * cy * cz + cx * sy * sz,
        cx * sy * cz - sx * cy * sz,
        cx * cy * sz + sx * sy * cz,
    )
    length = math.sqrt(sum(value * value for value in result))
    if length <= NORMALIZATION_EPSILON:
        raise ValueError("NIF articulation contains a zero Euler quaternion")
    return tuple(value / length for value in result)


def _converted_nif_quaternion(
    value: tuple[float, float, float, float],
) -> list[float]:
    w, x, y, z = value
    row = [
        [1.0 - 2.0 * (y * y + z * z), 2.0 * (x * y + z * w), 2.0 * (x * z - y * w)],
        [2.0 * (x * y - z * w), 1.0 - 2.0 * (x * x + z * z), 2.0 * (y * z + x * w)],
        [2.0 * (x * z + y * w), 2.0 * (y * z - x * w), 1.0 - 2.0 * (x * x + y * y)],
    ]
    return _rotation_quaternion(_converted_rotation(row))


def _interpolation_name(value: int) -> str:
    names = {1: "linear", 2: "quadratic", 3: "tbc", 4: "xyz-rotation"}
    if value not in names:
        raise ValueError(f"Unsupported NIF articulation key interpolation: {value}")
    return names[value]


def _scalar_key_rows(group: object) -> list[dict[str, object]]:
    rows = []
    previous_time = -math.inf
    for key in group.keys:
        row: dict[str, object] = {
            "timeSeconds": float(key.time),
            "value": float(key.value),
        }
        for field in ("forward", "backward", "tension", "bias", "continuity"):
            if hasattr(key, field):
                row[field] = float(getattr(key, field))
        if not all(
            math.isfinite(float(value))
            for value in row.values()
            if isinstance(value, (float, int))
        ):
            raise ValueError("NIF articulation scalar keys must be finite")
        if float(row["timeSeconds"]) <= previous_time:
            raise ValueError("NIF articulation scalar key times must increase")
        previous_time = float(row["timeSeconds"])
        rows.append(row)
    return rows


def _vector_key_rows(group: object) -> list[dict[str, object]]:
    rows = []
    previous_time = -math.inf
    for key in group.keys:
        value = key.value
        row: dict[str, object] = {
            "timeSeconds": float(key.time),
            "value": [float(value.x), float(value.y), float(value.z)],
        }
        if not math.isfinite(float(row["timeSeconds"])) or not all(
            math.isfinite(component) for component in row["value"]
        ):
            raise ValueError("NIF articulation translation keys must be finite")
        if float(row["timeSeconds"]) <= previous_time:
            raise ValueError("NIF articulation translation key times must increase")
        previous_time = float(row["timeSeconds"])
        rows.append(row)
    return rows


def _sequence_transform_contract(
    sequence: object,
    controlled: object,
    closed_transform: dict[str, object],
    block_index: dict[int, int],
) -> dict[str, object]:
    controller = controlled.controller
    interpolator = controlled.interpolator
    if not isinstance(controller, NifFormat.NiMultiTargetTransformController):
        raise ValueError("DOOR articulation controller is not NiMultiTargetTransformController")
    if not isinstance(interpolator, NifFormat.NiTransformInterpolator):
        raise ValueError("DOOR articulation interpolator is not NiTransformInterpolator")
    data = interpolator.data
    if not isinstance(data, NifFormat.NiTransformData):
        raise ValueError("DOOR articulation interpolator has no NiTransformData")
    if decode_text(controlled.controller_type) != "NiTransformController":
        raise ValueError("DOOR articulation controlled block is not NiTransformController")
    if int(data.rotation_type) != 4 or len(data.xyz_rotations) != 3:
        raise ValueError("DOOR articulation requires authored XYZ rotation keys")

    start = float(sequence.start_time)
    stop = float(sequence.stop_time)
    if not math.isfinite(start) or not math.isfinite(stop) or stop <= start:
        raise ValueError("DOOR articulation sequence duration is invalid")
    rotation_groups = []
    rotation_endpoints: list[tuple[float, float]] = []
    for axis, group in zip("XYZ", data.xyz_rotations):
        rows = _scalar_key_rows(group)
        if not rows:
            raise ValueError("DOOR articulation XYZ rotation axis has no keys")
        if not math.isclose(float(rows[0]["timeSeconds"]), start, abs_tol=1.0e-5):
            raise ValueError("DOOR articulation rotation does not start at sequence start")
        if not math.isclose(float(rows[-1]["timeSeconds"]), stop, abs_tol=1.0e-5):
            raise ValueError("DOOR articulation rotation does not end at sequence stop")
        interpolation = _interpolation_name(int(group.interpolation))
        rotation_groups.append(
            {"axis": axis, "interpolation": interpolation, "keys": rows}
        )
        rotation_endpoints.append((float(rows[0]["value"]), float(rows[-1]["value"])))

    translation_rows = _vector_key_rows(data.translations)
    if translation_rows and (
        not math.isclose(float(translation_rows[0]["timeSeconds"]), start, abs_tol=1.0e-5)
        or not math.isclose(float(translation_rows[-1]["timeSeconds"]), stop, abs_tol=1.0e-5)
    ):
        raise ValueError("DOOR articulation translation keys do not span the sequence")
    scale_rows = _scalar_key_rows(data.scales)
    if scale_rows and (
        not math.isclose(float(scale_rows[0]["timeSeconds"]), start, abs_tol=1.0e-5)
        or not math.isclose(float(scale_rows[-1]["timeSeconds"]), stop, abs_tol=1.0e-5)
    ):
        raise ValueError("DOOR articulation scale keys do not span the sequence")

    key_contract = {
        "rotation": {"representation": "euler-xyz", "groups": rotation_groups},
        "translation": {
            "interpolation": (
                _interpolation_name(int(data.translations.interpolation))
                if translation_rows
                else "constant-closed"
            ),
            "keys": translation_rows,
        },
        "scale": {
            "interpolation": (
                _interpolation_name(int(data.scales.interpolation))
                if scale_rows
                else "constant-closed"
            ),
            "keys": scale_rows,
        },
    }

    def endpoint(index: int) -> dict[str, object]:
        source_translation = (
            translation_rows[index]["value"]
            if translation_rows
            else [
                float(closed_transform["translationGodotUnits"][0]),
                -float(closed_transform["translationGodotUnits"][2]),
                float(closed_transform["translationGodotUnits"][1]),
            ]
        )
        scale = (
            float(scale_rows[index]["value"])
            if scale_rows
            else float(closed_transform["scale"])
        )
        source_angles = tuple(values[index] for values in rotation_endpoints)
        return {
            "translationGodotUnits": [
                float(source_translation[0]),
                float(source_translation[2]),
                -float(source_translation[1]),
            ],
            "rotationGodotQuaternion": _converted_nif_quaternion(
                _euler_xyz_quaternion(source_angles)
            ),
            "scale": scale,
        }

    return {
        "sourceName": decode_text(sequence.name),
        "startSeconds": start,
        "stopSeconds": stop,
        "durationSeconds": stop - start,
        "initialLocalTransform": endpoint(0),
        "terminalLocalTransform": endpoint(-1),
        "keyInterpolation": {
            "rotation": "euler-xyz-" + "+".join(
                str(group["interpolation"]) for group in rotation_groups
            ),
            "translation": str(key_contract["translation"]["interpolation"]),
            "scale": str(key_contract["scale"]["interpolation"]),
        },
        "keySha256": _canonical_sha256(key_contract),
        "source": {
            "sequenceBlock": block_index[id(sequence)],
            "controllerBlock": block_index[id(controller)],
            "interpolatorBlock": block_index[id(interpolator)],
            "transformDataBlock": block_index[id(data)],
        },
    }


def _descendant_ids(node: object) -> set[int]:
    result = {id(node)}
    if isinstance(node, NifFormat.NiNode):
        for child in node.children:
            if child is not None:
                result.update(_descendant_ids(child))
    return result


def _resolve_door_articulation(
    blocks: list[object],
    root: object,
    block_index: dict[int, int],
    source_hash: str,
    stable_id_hex_characters: int,
    *,
    required: bool,
) -> dict[str, object] | None:
    if not required:
        return None
    managers = [block for block in blocks if isinstance(block, NifFormat.NiControllerManager)]
    time_controllers = [
        block for block in blocks if isinstance(block, NifFormat.NiTimeController)
    ]
    if not managers and not time_controllers:
        return None
    if len(managers) != 1:
        raise ValueError(f"Controller-bearing DOOR requires one NiControllerManager, found {len(managers)}")
    manager = managers[0]
    if manager.target is not root:
        raise ValueError("DOOR articulation manager target is not the NIF root")
    sequences_by_name: dict[str, object] = {}
    for sequence in manager.controller_sequences:
        name = decode_text(sequence.name).casefold()
        if name in sequences_by_name:
            raise ValueError(f"DOOR articulation repeats sequence {name!r}")
        sequences_by_name[name] = sequence
    if set(sequences_by_name) != {"open", "close"}:
        raise ValueError(
            f"Controller-bearing DOOR requires exact Open/Close sequences, found {sorted(sequences_by_name)}"
        )

    target = None
    sequence_contracts: dict[str, object] = {}
    for name in ("open", "close"):
        sequence = sequences_by_name[name]
        if not isinstance(sequence, NifFormat.NiControllerSequence):
            raise ValueError(f"DOOR articulation {name} block is not NiControllerSequence")
        controlled_blocks = list(sequence.controlled_blocks)
        if len(controlled_blocks) != 1:
            raise ValueError(f"DOOR articulation {name} must control exactly one target")
        controlled = controlled_blocks[0]
        controller = controlled.controller
        if not isinstance(controller, NifFormat.NiMultiTargetTransformController):
            raise ValueError(f"DOOR articulation {name} has unsupported controller")
        named_targets = [
            node
            for node in blocks
            if isinstance(node, NifFormat.NiNode)
            and decode_text(node.name) == decode_text(controlled.node_name)
        ]
        if len(named_targets) != 1:
            raise ValueError(
                f"DOOR articulation {name} target name does not resolve uniquely: "
                f"{decode_text(controlled.node_name)!r}"
            )
        sequence_target = named_targets[0]
        controller_targets = [node for node in controller.extra_targets if node is not None]
        if controller.target is not root or controller_targets != [sequence_target]:
            raise ValueError(f"DOOR articulation {name} controller target join is incomplete")
        if target is None:
            target = sequence_target
        elif target is not sequence_target:
            raise ValueError("DOOR articulation Open/Close target different nodes")

    assert target is not None
    multi_target_controllers = [
        block for block in blocks
        if isinstance(block, NifFormat.NiMultiTargetTransformController)
    ]
    for controller in multi_target_controllers:
        extra_targets = [node for node in controller.extra_targets if node is not None]
        if controller.target is not root or extra_targets != [target]:
            raise ValueError("DOOR articulation contains an unmatched transform controller")
    supported_controllers = (NifFormat.NiControllerManager, NifFormat.NiMultiTargetTransformController)
    unsupported_controllers = [
        type(block).__name__
        for block in blocks
        if isinstance(block, NifFormat.NiTimeController)
        and not isinstance(block, supported_controllers)
    ]
    if unsupported_controllers:
        raise ValueError(
            f"Controller-bearing DOOR has unsupported controller blocks: {sorted(set(unsupported_controllers))}"
        )

    target_name = decode_text(target.name)
    target_index = block_index[id(target)]
    target_id = sha256_bytes(
        f"{source_hash}:{target_index}:{target_name}".encode("utf-8")
    )[:stable_id_hex_characters]
    closed_transform = _node_local_transform(target)
    for name in ("open", "close"):
        sequence = sequences_by_name[name]
        sequence_contracts[name] = _sequence_transform_contract(
            sequence,
            list(sequence.controlled_blocks)[0],
            closed_transform,
            block_index,
        )
    return {
        "target": target,
        "descendantIds": _descendant_ids(target),
        "contract": {
            "schema": DOOR_ARTICULATION_SCHEMA,
            "status": "owned-open-close-transform-complete",
            "target": {
                "targetId": target_id,
                "sourceBlockIndex": target_index,
                "sourceName": target_name,
                "visualNodeName": f"OPENNV_ARTICULATION_{target_id}",
                "collisionNodeName": f"OPENNV_ARTICULATION_{target_id}",
            },
            "closedLocalTransform": closed_transform,
            "sequences": sequence_contracts,
        },
    }


def is_editor_marker(value: object) -> bool:
    return decode_text(value).casefold().startswith("editormarker")


def has_presentation_property(shape: object) -> bool:
    return any(
        isinstance(prop, (NifFormat.BSShaderProperty, NifFormat.NiTexturingProperty))
        for prop in getattr(shape, "properties", [])
    )


def transform_xyz(value: object, matrix: object, *, direction: bool) -> tuple[float, float, float]:
    x, y, z = float(value.x), float(value.y), float(value.z)
    tx = x * matrix.m_11 + y * matrix.m_21 + z * matrix.m_31
    ty = x * matrix.m_12 + y * matrix.m_22 + z * matrix.m_32
    tz = x * matrix.m_13 + y * matrix.m_23 + z * matrix.m_33
    if not direction:
        tx += matrix.m_41
        ty += matrix.m_42
        tz += matrix.m_43
    result = (tx, tz, -ty)
    if not direction:
        return result
    length = math.sqrt(sum(component * component for component in result))
    if length <= NORMALIZATION_EPSILON:
        raise ValueError("NIF contains a zero-length direction vector")
    return tuple(component / length for component in result)


def texture_uv(value: object) -> tuple[float, float]:
    return float(value.u), float(value.v)


def _texture_descriptor_path(prop: object, name: str) -> str:
    if not bool(getattr(prop, f"has_{name}_texture", False)):
        return ""
    descriptor = getattr(prop, f"{name}_texture", None)
    source = getattr(descriptor, "source", None)
    return canonical_asset_path(source.file_name) if source is not None else ""


def texture_paths(shape: object) -> list[str]:
    properties = list(getattr(shape, "properties", []))
    for prop in properties:
        texture_set = getattr(prop, "texture_set", None)
        if texture_set is not None:
            return [canonical_asset_path(value) for value in texture_set.textures]
    for prop in properties:
        if isinstance(prop, NifFormat.BSShaderNoLightingProperty):
            path = canonical_asset_path(prop.file_name)
            if path:
                return [path]
    for prop in properties:
        if isinstance(prop, NifFormat.NiTexturingProperty):
            normal = _texture_descriptor_path(prop, "normal") or _texture_descriptor_path(
                prop, "bump_map"
            )
            return [
                _texture_descriptor_path(prop, "base"),
                normal,
                _texture_descriptor_path(prop, "glow"),
            ]
    return []


def alpha_contract(shape: object) -> dict[str, object]:
    properties = list(getattr(shape, "properties", []))
    alpha = next(
        (prop for prop in properties if isinstance(prop, NifFormat.NiAlphaProperty)),
        None,
    )
    if alpha is not None:
        return {**actor_alpha_contract(alpha), "source": "NiAlphaProperty"}
    shader = next(
        (prop for prop in properties if isinstance(prop, NifFormat.BSShaderProperty)),
        None,
    )
    flags = getattr(shader, "shader_flags", None)
    shader_alpha = flags is not None and any(
        bool(getattr(flags, field, False))
        for field in ("sf_vertex_alpha", "sf_alpha_texture", "sf_dynamic_alpha")
    )
    return {
        "mode": "BLEND" if shader_alpha else "OPAQUE",
        "cutoff": None,
        "flags": None,
        "blendEnabled": shader_alpha,
        "testEnabled": False,
        "sourceBlendMode": None,
        "destinationBlendMode": None,
        "testFunction": None,
        "noSorter": False,
        "source": "BSShaderFlags" if shader_alpha else "none",
    }


def vertex_color_mode(shape: object) -> str:
    shader = next(
        (
            prop
            for prop in getattr(shape, "properties", [])
            if isinstance(prop, NifFormat.BSShaderProperty)
        ),
        None,
    )
    if shader is None:
        return "color-alpha"
    flags_one = getattr(shader, "shader_flags", None)
    flags_two = getattr(shader, "shader_flags_2", None)
    color = bool(getattr(flags_two, "sf_2_vertex_colors", False))
    alpha = bool(getattr(flags_one, "sf_vertex_alpha", False))
    if color and alpha:
        return "color-alpha"
    if color:
        return "color"
    if alpha:
        return "alpha"
    return "none"


def material_metadata(shape: object) -> dict[str, object]:
    result: dict[str, object] = {}
    for prop in getattr(shape, "properties", []):
        if isinstance(prop, NifFormat.NiMaterialProperty):
            result["alpha"] = float(prop.alpha)
            result["diffuse"] = [
                float(prop.diffuse_color.r),
                float(prop.diffuse_color.g),
                float(prop.diffuse_color.b),
            ]
            result["glossiness"] = float(prop.glossiness)
            emit_multiplier = float(getattr(prop, "emit_multi", 1.0))
            result["emitMultiplier"] = emit_multiplier
            result["emissive"] = [
                float(prop.emissive_color.r) * emit_multiplier,
                float(prop.emissive_color.g) * emit_multiplier,
                float(prop.emissive_color.b) * emit_multiplier,
            ]
            result["emissiveControlled"] = (
                isinstance(prop.controller, NifFormat.NiMaterialColorController)
                and int(prop.controller.target_color) == 3
            )
            result["specular"] = [
                float(prop.specular_color.r),
                float(prop.specular_color.g),
                float(prop.specular_color.b),
            ]
        elif isinstance(prop, NifFormat.BSShaderProperty):
            result["shaderType"] = int(prop.shader_type)
            flags_one = getattr(prop, "shader_flags_1", getattr(prop, "shader_flags", 0))
            flags_two = getattr(prop, "shader_flags_2", 0)
            result["shaderFlags1"] = int(flags_one)
            result["shaderFlags2"] = int(prop.shader_flags_2)
            result["environmentMapScale"] = float(prop.environment_map_scale)
            result["textureClampMode"] = int(prop.texture_clamp_mode)
            result["shaderFlags1Enabled"] = [
                name
                for name in flags_one.get_detail_child_names()
                if bool(getattr(flags_one, name))
            ]
            result["shaderFlags2Enabled"] = [
                name
                for name in flags_two.get_detail_child_names()
                if bool(getattr(flags_two, name))
            ]
        elif isinstance(prop, NifFormat.NiStencilProperty):
            result["stencilDrawMode"] = int(prop.draw_mode)
    paths = texture_paths(shape)
    shader = any(
        isinstance(prop, NifFormat.BSShaderProperty)
        for prop in getattr(shape, "properties", [])
    )
    diffuse = result.get("diffuse", [1.0, 1.0, 1.0])
    result["baseColor"] = [1.0, 1.0, 1.0] if shader else diffuse
    result["alphaContract"] = alpha_contract(shape)
    result["vertexColorMode"] = vertex_color_mode(shape)
    result["diffuseTexturePresent"] = bool(paths and paths[0])
    return result


def shape_double_sided(shape: object) -> bool:
    return any(
        isinstance(prop, NifFormat.NiStencilProperty) and int(prop.draw_mode) == 3
        for prop in getattr(shape, "properties", [])
    )


def generate_tangents(
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    uvs: list[tuple[float, float]],
    triangles: list[tuple[int, int, int]],
) -> list[tuple[float, float, float, float]]:
    tangent_rows = [[0.0, 0.0, 0.0] for _ in positions]
    bitangent_rows = [[0.0, 0.0, 0.0] for _ in positions]
    for triangle in triangles:
        first, second, third = triangle
        edge_one = tuple(positions[second][axis] - positions[first][axis] for axis in range(3))
        edge_two = tuple(positions[third][axis] - positions[first][axis] for axis in range(3))
        delta_one = (uvs[second][0] - uvs[first][0], uvs[second][1] - uvs[first][1])
        delta_two = (uvs[third][0] - uvs[first][0], uvs[third][1] - uvs[first][1])
        determinant = delta_one[0] * delta_two[1] - delta_one[1] * delta_two[0]
        if abs(determinant) <= NORMALIZATION_EPSILON:
            continue
        reciprocal = 1.0 / determinant
        tangent = tuple(
            (edge_one[axis] * delta_two[1] - edge_two[axis] * delta_one[1]) * reciprocal
            for axis in range(3)
        )
        bitangent = tuple(
            (edge_two[axis] * delta_one[0] - edge_one[axis] * delta_two[0]) * reciprocal
            for axis in range(3)
        )
        for index in triangle:
            for axis in range(3):
                tangent_rows[index][axis] += tangent[axis]
                bitangent_rows[index][axis] += bitangent[axis]

    result = []
    for normal, tangent_row, bitangent_row in zip(normals, tangent_rows, bitangent_rows):
        projection = sum(normal[axis] * tangent_row[axis] for axis in range(3))
        tangent = tuple(tangent_row[axis] - normal[axis] * projection for axis in range(3))
        length = math.sqrt(sum(value * value for value in tangent))
        if length <= NORMALIZATION_EPSILON:
            least_aligned_axis = min(range(3), key=lambda index: abs(normal[index]))
            axis = tuple(
                1.0 if index == least_aligned_axis else 0.0
                for index in range(3)
            )
            tangent = (
                axis[1] * normal[2] - axis[2] * normal[1],
                axis[2] * normal[0] - axis[0] * normal[2],
                axis[0] * normal[1] - axis[1] * normal[0],
            )
            length = math.sqrt(sum(value * value for value in tangent))
        tangent = tuple(value / length for value in tangent)
        cross = (
            normal[1] * tangent[2] - normal[2] * tangent[1],
            normal[2] * tangent[0] - normal[0] * tangent[2],
            normal[0] * tangent[1] - normal[1] * tangent[0],
        )
        handedness = -1.0 if sum(cross[axis] * bitangent_row[axis] for axis in range(3)) < 0.0 else 1.0
        result.append((*tangent, handedness))
    return result


def generate_vertex_normals(
    positions: list[tuple[float, float, float]],
    triangles: list[tuple[int, int, int]],
) -> list[tuple[float, float, float]]:
    """Generate deterministic area-weighted normals for retail terrain LOD.

    Some FNV terrain-LOD NIFs omit their redundant vertex-normal stream while
    retaining the authored normal-map texture and LOD-landscape shader flag.
    Summed face cross products preserve the mesh's authored smoothing topology.
    """

    accumulated = [[0.0, 0.0, 0.0] for _ in positions]
    for first, second, third in triangles:
        edge_one = tuple(
            positions[second][axis] - positions[first][axis] for axis in range(3)
        )
        edge_two = tuple(
            positions[third][axis] - positions[first][axis] for axis in range(3)
        )
        cross = (
            edge_one[1] * edge_two[2] - edge_one[2] * edge_two[1],
            edge_one[2] * edge_two[0] - edge_one[0] * edge_two[2],
            edge_one[0] * edge_two[1] - edge_one[1] * edge_two[0],
        )
        for index in (first, second, third):
            for axis in range(3):
                accumulated[index][axis] += cross[axis]
    result = []
    for index, row in enumerate(accumulated):
        length = math.sqrt(sum(value * value for value in row))
        if length <= NORMALIZATION_EPSILON:
            # Retail terrain LOD can retain an unreferenced source vertex. It
            # cannot contribute pixels, but glTF still requires a complete
            # attribute stream; use the neutral terrain-up direction.
            result.append((0.0, 1.0, 0.0))
        else:
            result.append(tuple(value / length for value in row))
    return result


def is_lod_landscape_surface(shape: object) -> bool:
    return any(
        isinstance(prop, NifFormat.BSShaderProperty)
        and bool(getattr(getattr(prop, "shader_flags_2", None), "sf_2_lod_landscape", False))
        for prop in getattr(shape, "properties", [])
    )


def compiler_provenance_source_paths() -> list[Path]:
    return family_compiler_provenance_source_paths("static")


def compiler_provenance() -> dict[str, str]:
    return family_compiler_provenance("static")


def export_static_nif(
    source: Path,
    logical_path: str,
    gltf_path: Path,
    sidecar_path: Path,
    compiler: ContentCompilerConfiguration,
    *,
    strict: bool = True,
    presentation_clip: dict[str, object] | None = None,
    include_shape_prefixes: tuple[str, ...] | None = None,
    require_door_articulation: bool = False,
) -> dict[str, object]:
    clip_rectangle: tuple[float, float, float, float] | None = None
    clip_coordinate_space = "source-world-game-units-before-scene-origin"
    if presentation_clip is not None:
        required_clip_fields = {
            "mode",
            "minXGameUnits",
            "maxXGameUnits",
            "minYGameUnits",
            "maxYGameUnits",
        }
        optional_clip_fields = {"coordinateSpace"}
        if not required_clip_fields <= set(presentation_clip) or set(
            presentation_clip
        ) - required_clip_fields - optional_clip_fields:
            raise ValueError(
                "Static presentation clip must contain "
                + ", ".join(sorted(required_clip_fields))
                + " and only optional coordinateSpace"
            )
        if presentation_clip["mode"] != "retain-outside-source-xy-rectangle":
            raise ValueError(
                f"Unsupported static presentation clip mode: {presentation_clip['mode']}"
            )
        clip_rectangle = (
            float(presentation_clip["minXGameUnits"]),
            float(presentation_clip["maxXGameUnits"]),
            float(presentation_clip["minYGameUnits"]),
            float(presentation_clip["maxYGameUnits"]),
        )
        clip_coordinate_space = str(
            presentation_clip.get("coordinateSpace", clip_coordinate_space)
        )
        if clip_coordinate_space not in {
            "source-world-game-units-before-scene-origin",
            "source-block-local-game-units-before-placement",
        }:
            raise ValueError(
                f"Unsupported static presentation clip coordinate space: {clip_coordinate_space}"
            )
        if not clip_rectangle[0] < clip_rectangle[1] or not clip_rectangle[2] < clip_rectangle[3]:
            raise ValueError(f"Static presentation clip rectangle is invalid: {clip_rectangle}")

    source_bytes = source.read_bytes()
    source_hash = sha256_bytes(source_bytes)
    decoded_nif = decode_nif(source_bytes)
    data = decoded_nif.document
    if len(data.roots) != 1:
        raise ValueError(f"Expected one NIF root, found {len(data.roots)}")

    blocks = list(data.get_global_iterator())
    block_index = {id(block): index for index, block in enumerate(blocks)}
    root = data.roots[0]
    controllers = [type(block).__name__ for block in blocks if isinstance(block, NifFormat.NiTimeController)]
    articulation = _resolve_door_articulation(
        blocks,
        root,
        block_index,
        source_hash,
        compiler.stable_id_hex_characters,
        required=require_door_articulation,
    )
    all_shapes = [
        block
        for block in blocks
        if isinstance(
            block,
            (
                NifFormat.NiTriShape,
                NifFormat.NiTriStrips,
                NifFormat.BSSegmentedTriShape,
            ),
        )
        and block.data is not None
    ]
    excluded_editor_markers = [
        {"sourceBlockIndex": block_index[id(shape)], "name": decode_text(shape.name)}
        for shape in all_shapes
        if is_editor_marker(shape.name)
    ]
    excluded_non_presentation = [
        {
            "sourceBlockIndex": block_index[id(shape)],
            "name": decode_text(shape.name),
            "propertyTypes": [type(prop).__name__ for prop in shape.properties],
            "reason": "no-Bethesda-shader-or-NiTexturingProperty",
        }
        for shape in all_shapes
        if not is_editor_marker(shape.name) and not has_presentation_property(shape)
    ]
    candidate_shapes = [
        shape
        for shape in all_shapes
        if not is_editor_marker(shape.name) and has_presentation_property(shape)
    ]
    excluded_by_shape_filter: list[dict[str, object]] = []
    if include_shape_prefixes is not None:
        if not include_shape_prefixes or any(not prefix for prefix in include_shape_prefixes):
            raise ValueError("Static NIF shape prefixes must be non-empty")
        excluded_by_shape_filter = [
            {
                "sourceBlockIndex": block_index[id(shape)],
                "name": decode_text(shape.name),
            }
            for shape in candidate_shapes
            if not decode_text(shape.name).startswith(include_shape_prefixes)
        ]
        shapes = [
            shape
            for shape in candidate_shapes
            if decode_text(shape.name).startswith(include_shape_prefixes)
        ]
    else:
        shapes = candidate_shapes
    if not shapes:
        if include_shape_prefixes is not None and candidate_shapes:
            raise ValueError("Static NIF shape filter removed all supported geometry")
        classified_shape_count = len(excluded_editor_markers) + len(excluded_non_presentation)
        if all_shapes and classified_shape_count == len(all_shapes):
            raise NoStaticPresentationGeometryError(
                {
                    "schema": NON_PRESENTATION_SCHEMA,
                    "status": "owned-nif-no-presentation-geometry",
                    "source": {
                        "logicalPath": canonical_asset_path(logical_path),
                        "sha256": source_hash,
                    },
                    "compiler": compiler_provenance(),
                    "classification": {
                        "source": "owned-NIF-surface-structure",
                        "triangleSurfaceCount": len(all_shapes),
                        "classifiedSurfaceCount": classified_shape_count,
                        "editorMarkerSurfaces": excluded_editor_markers,
                        "nonPresentationSurfaces": excluded_non_presentation,
                        "disposition": "exclude-reference-from-presentation",
                    },
                }
            )
        raise ValueError("NIF contains no supported static geometry")
    if strict and controllers and articulation is None:
        raise ValueError(f"Static slice rejects controller blocks: {sorted(set(controllers))}")

    builder = BufferBuilder()
    primitives: list[dict[str, object]] = []
    primitive_rows: list[dict[str, object]] = []
    materials: list[dict[str, object]] = []
    surface_rows: list[dict[str, object]] = []
    clipped_away_surfaces: list[dict[str, object]] = []
    clip_surface_reports: list[dict[str, int]] = []
    attachment_markers = []
    for block in blocks:
        if not isinstance(block, NifFormat.NiNode) or decode_text(block.name) not in ATTACHMENT_MARKER_NAMES:
            continue
        matrix = block.get_transform(root)
        attachment_markers.append(
            {
                "name": decode_text(block.name),
                "positionGodotUnits": [
                    float(matrix.m_41),
                    float(matrix.m_43),
                    -float(matrix.m_42),
                ],
            }
        )

    for shape in shapes:
        skin_source_pose_baked = getattr(shape, "skin_instance", None) is not None
        if strict and skin_source_pose_baked:
            raise ValueError(f"Static slice rejects skinned geometry: {decode_text(shape.name)}")
        property_types = [type(prop).__name__ for prop in shape.properties]
        unsupported = sorted(set(property_types) - SUPPORTED_SHAPE_PROPERTIES)
        if strict and unsupported:
            raise ValueError(f"Static slice rejects properties {unsupported} on {decode_text(shape.name)}")
        if strict and any(isinstance(prop, NifFormat.NiAlphaProperty) for prop in shape.properties):
            raise ValueError(f"Opaque slice rejects alpha property on {decode_text(shape.name)}")

        mesh = shape.data
        if skin_source_pose_baked:
            source_vertices, source_normals = shape.get_skin_deformation()
        else:
            source_vertices, source_normals = mesh.vertices, mesh.normals
        vertex_count = len(source_vertices)
        if vertex_count == 0 or not mesh.uv_sets:
            raise ValueError(f"Shape lacks positions or UV0: {decode_text(shape.name)}")
        if len(source_normals) not in {0, vertex_count}:
            raise ValueError(f"Shape has a partial normal stream: {decode_text(shape.name)}")
        if len(source_normals) == 0 and not is_lod_landscape_surface(shape):
            raise ValueError(f"Shape lacks normals: {decode_text(shape.name)}")
        if strict and len(mesh.uv_sets) > 2:
            raise ValueError(f"Static slice supports at most two UV sets: {decode_text(shape.name)}")

        shape_index = block_index[id(shape)]
        stable_id = sha256_bytes(
            f"{source_hash}:{shape_index}:{decode_text(shape.name)}".encode()
        )[:compiler.stable_id_hex_characters]
        articulation_target_id = None
        transform_parent = root
        if articulation is not None and id(shape) in articulation["descendantIds"]:
            articulation_target_id = articulation["contract"]["target"]["targetId"]
            transform_parent = articulation["target"]
        matrix = shape.get_transform(transform_parent)
        positions = [transform_xyz(value, matrix, direction=False) for value in source_vertices]
        triangles = [tuple(int(index) for index in triangle) for triangle in mesh.get_triangles()]
        if not triangles:
            raise ValueError(f"Shape has no triangles: {decode_text(shape.name)}")
        if len(source_normals) == vertex_count:
            normals = [transform_xyz(value, matrix, direction=True) for value in source_normals]
            normal_source = "nif"
        else:
            normals = generate_vertex_normals(positions, triangles)
            normal_source = "generated-area-weighted-triangle-lod-landscape"
        converted_uv_sets = [
            [texture_uv(value) for value in uv_set]
            for uv_set in mesh.uv_sets[:2]
        ]
        colors = (
            [(float(v.r), float(v.g), float(v.b), float(v.a)) for v in mesh.vertex_colors]
            if len(mesh.vertex_colors) == vertex_count
            else None
        )
        surface_clip_report = None
        if clip_rectangle is not None:
            (
                positions,
                normals,
                converted_uv_sets,
                colors,
                triangles,
                surface_clip_report,
            ) = clip_triangles_outside_source_rectangle(
                positions,
                normals,
                converted_uv_sets,
                colors,
                triangles,
                clip_rectangle,
            )
            clip_surface_reports.append(surface_clip_report)
            if not triangles:
                clipped_away_surfaces.append(
                    {
                        "sourceBlockIndex": block_index[id(shape)],
                        "name": decode_text(shape.name),
                        **surface_clip_report,
                    }
                )
                continue
            vertex_count = len(positions)
        surface_material = material_metadata(shape)

        attributes: dict[str, int] = {}
        minimum = [min(row[index] for row in positions) for index in range(3)]
        maximum = [max(row[index] for row in positions) for index in range(3)]
        attributes["POSITION"] = builder.add(
            pack_floats(positions), component_type=GL_FLOAT, count=vertex_count, value_type="VEC3",
            target=GL_ARRAY_BUFFER, minimum=minimum, maximum=maximum,
        )
        attributes["NORMAL"] = builder.add(
            pack_floats(normals), component_type=GL_FLOAT, count=vertex_count, value_type="VEC3", target=GL_ARRAY_BUFFER,
        )
        for uv_index, uvs in enumerate(converted_uv_sets):
            attributes[f"TEXCOORD_{uv_index}"] = builder.add(
                pack_floats(uvs), component_type=GL_FLOAT, count=vertex_count, value_type="VEC2", target=GL_ARRAY_BUFFER,
            )
        if colors is not None:
            attributes["COLOR_0"] = builder.add(
                pack_floats(colors), component_type=GL_FLOAT, count=vertex_count, value_type="VEC4", target=GL_ARRAY_BUFFER,
            )
        tangent_source = "absent"
        if converted_uv_sets:
            tangents = []
            tangent_source = "generated-clipped-uv-triangle" if clip_rectangle is not None else "nif"
            if (
                clip_rectangle is None
                and len(mesh.tangents) == vertex_count
                and len(mesh.bitangents) == vertex_count
            ):
                try:
                    for normal, tangent, bitangent in zip(normals, mesh.tangents, mesh.bitangents):
                        tangent_xyz = transform_xyz(tangent, matrix, direction=True)
                        bitangent_xyz = transform_xyz(bitangent, matrix, direction=True)
                        cross = (
                            normal[1] * tangent_xyz[2] - normal[2] * tangent_xyz[1],
                            normal[2] * tangent_xyz[0] - normal[0] * tangent_xyz[2],
                            normal[0] * tangent_xyz[1] - normal[1] * tangent_xyz[0],
                        )
                        handedness = (
                            1.0 if sum(a * b for a, b in zip(cross, bitangent_xyz)) >= 0.0 else -1.0
                        )
                        tangents.append((*tangent_xyz, handedness))
                except ValueError:
                    tangents = []
            if len(tangents) != vertex_count:
                tangents = generate_tangents(positions, normals, converted_uv_sets[0], triangles)
                tangent_source = "generated-uv-triangle"
            attributes["TANGENT"] = builder.add(
                pack_floats(tangents), component_type=GL_FLOAT, count=vertex_count, value_type="VEC4", target=GL_ARRAY_BUFFER,
            )

        index_component = (
            GL_UNSIGNED_SHORT if vertex_count <= GL_UNSIGNED_SHORT_MAX else GL_UNSIGNED_INT
        )
        index_format = "H" if index_component == GL_UNSIGNED_SHORT else "I"
        indices = [value for triangle in triangles for value in triangle]
        index_accessor = builder.add(
            struct.pack(f"<{len(indices)}{index_format}", *indices), component_type=index_component,
            count=len(indices), value_type="SCALAR", target=GL_ELEMENT_ARRAY_BUFFER,
        )
        material_index = len(materials)
        base_color = [float(value) for value in surface_material["baseColor"]]
        alpha = float(surface_material.get("alpha", 1.0))
        glossiness = float(
            surface_material.get("glossiness", compiler.default_material_glossiness)
        )
        specular = [float(value) for value in surface_material.get("specular", [0.0, 0.0, 0.0])]
        roughness, _roughness_source = nif_material_roughness(specular, glossiness, compiler)
        gltf_material: dict[str, object] = {
            "name": f"{decode_text(shape.name)} material",
            "doubleSided": shape_double_sided(shape),
            "pbrMetallicRoughness": {
                "baseColorFactor": [*base_color, alpha],
                "metallicFactor": 0.0,
                "roughnessFactor": roughness,
            },
        }
        alpha_mode = str(surface_material["alphaContract"]["mode"])
        if alpha_mode != "OPAQUE":
            gltf_material["alphaMode"] = alpha_mode
        if alpha_mode == "MASK":
            gltf_material["alphaCutoff"] = surface_material["alphaContract"]["cutoff"]
        emissive = [float(value) for value in surface_material.get("emissive", [0.0, 0.0, 0.0])]
        if any(value > 0.0 for value in emissive):
            gltf_material["emissiveFactor"] = emissive
        materials.append(gltf_material)
        primitive = {"attributes": attributes, "indices": index_accessor, "material": material_index}
        primitives.append(primitive)
        primitive_rows.append(
            {
                "primitive": primitive,
                "stableId": stable_id,
                "articulationTargetId": articulation_target_id,
            }
        )
        surface_rows.append({
            "stableId": stable_id,
            "sourceBlockIndex": shape_index,
            "name": decode_text(shape.name),
            "vertices": vertex_count,
            "triangles": len(triangles),
            "attributes": sorted(attributes),
            "propertyTypes": property_types,
            "textures": texture_paths(shape),
            "material": surface_material,
            "transformBakedToRoot": articulation_target_id is None,
            "articulationTargetId": articulation_target_id,
            "skinSourcePoseBaked": skin_source_pose_baked,
            "tangentSource": tangent_source,
            "normalSource": normal_source,
            "presentationClip": surface_clip_report,
        })

    if not primitives:
        raise ValueError("Static presentation clip removed all supported geometry")

    collision_types = sorted({type(block).__name__ for block in blocks if type(block).__name__.startswith("bhk")})
    collision_bodies, collision_unsupported = collision_contract(
        blocks,
        root,
        block_index,
        articulation_target=(articulation["target"] if articulation is not None else None),
        articulation_target_id=(
            str(articulation["contract"]["target"]["targetId"])
            if articulation is not None
            else None
        ),
        articulation_descendant_ids=(
            articulation["descendantIds"] if articulation is not None else None
        ),
    )
    physics_bodies, physics_unsupported = dynamic_physics_contract(blocks, block_index)

    articulation_contract = None
    if articulation is not None:
        articulation_contract = articulation["contract"]
        target_id = str(articulation_contract["target"]["targetId"])
        visual_surface_ids = sorted(
            str(row["stableId"])
            for row in primitive_rows
            if row["articulationTargetId"] == target_id
        )
        collision_body_blocks = sorted(
            int(body["bodyBlock"])
            for body in collision_bodies
            if body["ownerTargetId"] == target_id
        )
        if not visual_surface_ids:
            raise ValueError("Controller-bearing DOOR target has no joined visual surfaces")
        if not collision_body_blocks:
            raise ValueError("Controller-bearing DOOR target has no joined authored collision")
        target_contract = articulation_contract["target"]
        target_contract.update(
            {
                "visualSurfaceStableIds": visual_surface_ids,
                "collisionBodyBlocks": collision_body_blocks,
                "visualDescendantNodeNames": [
                    f"OPENNV_ARTICULATION_VISUAL_{stable_id}"
                    for stable_id in visual_surface_ids
                ],
                "collisionDescendantNodeNames": [
                    f"OPENNV_ARTICULATION_COLLISION_BODY_{body_block}"
                    for body_block in collision_body_blocks
                ],
            }
        )
        articulation_contract["canonicalSha256"] = _canonical_sha256(articulation_contract)

    binary_name = gltf_path.with_suffix(".bin").name
    meshes: list[dict[str, object]] = []
    nodes: list[dict[str, object]] = []
    scene_nodes: list[int] = []
    if articulation_contract is None:
        meshes.append({"name": Path(logical_path).stem, "primitives": primitives})
        nodes.append({"name": Path(logical_path).stem, "mesh": 0})
        scene_nodes.append(0)
    else:
        target_contract = articulation_contract["target"]
        target_id = str(target_contract["targetId"])
        static_primitives = [
            row["primitive"]
            for row in primitive_rows
            if row["articulationTargetId"] is None
        ]
        if static_primitives:
            meshes.append(
                {"name": f"{Path(logical_path).stem}_STATIC", "primitives": static_primitives}
            )
            nodes.append({"name": f"{Path(logical_path).stem}_STATIC", "mesh": 0})
            scene_nodes.append(0)
        target_children = []
        for row in sorted(
            (
                row
                for row in primitive_rows
                if row["articulationTargetId"] == target_id
            ),
            key=lambda value: str(value["stableId"]),
        ):
            node_name = f"OPENNV_ARTICULATION_VISUAL_{row['stableId']}"
            mesh_index = len(meshes)
            node_index = len(nodes)
            meshes.append({"name": node_name, "primitives": [row["primitive"]]})
            nodes.append(
                {
                    "name": node_name,
                    "mesh": mesh_index,
                    "extras": {"openNvArticulationTargetId": target_id},
                }
            )
            target_children.append(node_index)
        closed_transform = articulation_contract["closedLocalTransform"]
        wrapper_index = len(nodes)
        nodes.append(
            {
                "name": target_contract["visualNodeName"],
                "translation": closed_transform["translationGodotUnits"],
                "rotation": closed_transform["rotationGodotQuaternion"],
                "scale": [closed_transform["scale"]] * 3,
                "children": target_children,
                "extras": {"openNvArticulationTargetId": target_id},
            }
        )
        scene_nodes.append(wrapper_index)
    gltf = {
        "asset": {"version": "2.0", "generator": GENERATOR},
        "scene": 0,
        "scenes": [{"nodes": scene_nodes}],
        "nodes": nodes,
        "meshes": meshes,
        "materials": materials,
        "buffers": [{"uri": binary_name, "byteLength": len(builder.data)}],
        "bufferViews": builder.views,
        "accessors": builder.accessors,
        "extras": {"openNvSchema": SCHEMA, "sourceSha256": source_hash},
    }
    gltf_bytes = (json.dumps(gltf, indent=2, sort_keys=True) + "\n").encode()
    binary_bytes = bytes(builder.data)
    atomic_write(gltf_path.with_suffix(".bin"), binary_bytes)
    atomic_write(gltf_path, gltf_bytes)

    collision_outputs = (
        write_collision_gltf(
            collision_bodies,
            gltf_path.with_name(f"{gltf_path.stem}.collision.gltf"),
            source_hash,
            GENERATOR,
            articulation=articulation_contract,
        )
        if collision_bodies
        else None
    )
    output_manifest = {
        "gltf": {"file": gltf_path.name, "bytes": len(gltf_bytes), "sha256": sha256_bytes(gltf_bytes)},
        "buffer": {"file": binary_name, "bytes": len(binary_bytes), "sha256": sha256_bytes(binary_bytes)},
    }
    if collision_outputs is not None:
        output_manifest["collisionGltf"] = collision_outputs["gltf"]
        output_manifest["collisionBuffer"] = collision_outputs["buffer"]
    presentation_clip_report = None
    if presentation_clip is not None:
        presentation_clip_report = {
            **presentation_clip,
            "coordinateSpace": clip_coordinate_space,
            "collisionPolicy": "source-collision-unchanged",
            "sourceSurfaces": len(clip_surface_reports),
            "fullyRemovedSurfaces": len(clipped_away_surfaces),
            "sourceTriangles": sum(
                report["sourceTriangles"] for report in clip_surface_reports
            ),
            "unchangedSourceTriangles": sum(
                report["unchangedSourceTriangles"] for report in clip_surface_reports
            ),
            "clippedSourceTriangles": sum(
                report["clippedSourceTriangles"] for report in clip_surface_reports
            ),
            "fullyRemovedSourceTriangles": sum(
                report["fullyRemovedSourceTriangles"] for report in clip_surface_reports
            ),
            "outputTriangles": sum(
                report["outputTriangles"] for report in clip_surface_reports
            ),
            "outputVertices": sum(
                report["outputVertices"] for report in clip_surface_reports
            ),
        }
    sidecar = {
        "schema": SCHEMA,
        "status": "geometry-only",
        "source": {
            "logicalPath": canonical_asset_path(logical_path),
            "bytes": len(source_bytes),
            "sha256": source_hash,
            "nifVersion": f"0x{data.version:08x}",
            "userVersion": int(data.user_version),
            "userVersion2": int(data.user_version_2),
            "decoder": decoded_nif.evidence(),
        },
        "compiler": compiler_provenance(),
        "outputs": output_manifest,
        "coverage": {
            "surfaces": len(surface_rows),
            "sourcePoseBakedSkinSurfaces": sum(
                1 for surface in surface_rows if surface["skinSourcePoseBaked"]
            ),
            "collisionExported": bool(collision_bodies),
            "collisionBlockTypes": collision_types,
            "collisionUnsupportedReason": collision_unsupported,
            "collisionBodies": [
                {
                    **{key: value for key, value in body.items() if key not in {"positions", "triangles"}},
                    "vertices": len(body["positions"]),
                    "triangles": len(body["triangles"]),
                }
                for body in collision_bodies
            ],
            "dynamicPhysicsExported": bool(physics_bodies),
            "dynamicPhysicsUnsupportedReasons": physics_unsupported,
            "dynamicPhysicsBodies": physics_bodies,
            "controllers": sorted(set(controllers)),
            "excludedEditorMarkerSurfaces": excluded_editor_markers,
            "excludedNonPresentationSurfaces": excluded_non_presentation,
            "includedShapePrefixes": list(include_shape_prefixes or ()),
            "excludedByShapeFilter": excluded_by_shape_filter,
            "presentationClip": presentation_clip_report,
            "presentationClipRemovedSurfaces": clipped_away_surfaces,
        },
        "attachmentMarkers": attachment_markers,
        "surfaces": surface_rows,
    }
    if articulation_contract is not None:
        sidecar["articulation"] = articulation_contract
    sidecar_bytes = (json.dumps(sidecar, indent=2, sort_keys=True) + "\n").encode()
    atomic_write(sidecar_path, sidecar_bytes)
    return sidecar


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--logical-path", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--sidecar", type=Path, required=True)
    parser.add_argument("--allow-synthetic-minimal", action="store_true")
    parser.add_argument("--include-shape-prefix", action="append")
    args = parser.parse_args()
    compiler = load_runtime_configuration().content_compiler
    result = export_static_nif(
        args.input,
        args.logical_path,
        args.output,
        args.sidecar,
        compiler,
        strict=not args.allow_synthetic_minimal,
        include_shape_prefixes=(
            tuple(args.include_shape_prefix) if args.include_shape_prefix is not None else None
        ),
    )
    print("OPENNV_STATIC_NIF_GLTF " + json.dumps({
        "source": result["source"]["sha256"],
        "surfaces": result["coverage"]["surfaces"],
        "gltf": result["outputs"]["gltf"]["sha256"],
    }, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
