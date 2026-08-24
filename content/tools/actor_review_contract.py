"""Bind one whole-game review row to classified retail capture evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import struct
import sys
from pathlib import Path

from plugin_stack import file_sha256


CORPUS_SCHEMA = "opennv-actor-parity-corpus/v1"
RETAIL_REPORT_SCHEMA = "nikami-fnv-actor-observation/v1"
RETAIL_ORACLE_SCHEMA = "nikami-retail-oracle/v4"
REVIEW_CONTRACT_SCHEMA = "opennv-actor-review-contract/v4"
RETAIL_APPEARANCE_SCHEMA = "nikami-fnv-sidecar-appearance/v3"
WEAPON_STATE_NONE = "none"
WEAPON_STATE_EQUIPPED = "equipped"
WEAPON_RENDER_STATE_NOT_APPLICABLE = "not-applicable"
WEAPON_RENDER_STATE_VISIBLE_SOURCE_BOUND = "visible-source-bound"
WEAPON_RENDER_STATE_NOT_VISIBLE_AT_FRAME = "not-visible-at-frame"
CAPTURED_RETAIL_STATUS = "captured-classified-runtime-observation"
PENDING_GODOT_STATUS = "retail-observed-godot-pending"
FRAME_FILE_PATTERN = re.compile(r"frame-(?P<frame>[0-9]+)\.[^.]+$", re.IGNORECASE)
EXIT_DATA_ERROR = 2
BITMAP_SIGNATURE = b"BM"
BITMAP_DIMENSION_OFFSET = 18
BITMAP_DIMENSION_BYTES = 8
HOMOGENEOUS_MATRIX_ELEMENT_COUNT = 16
HOMOGENEOUS_SPATIAL_DIMENSIONS = 3
ROTATION_MATRIX_ELEMENT_COUNT = 9
FRUSTUM_ELEMENT_COUNT = 7
FRUSTUM_NEAR_INDEX = 4
FRUSTUM_FAR_INDEX = 5
FRUSTUM_ORTHOGRAPHIC_INDEX = 6
HOMOGENEOUS_MATRIX_SECOND_ROW_END = 8
SKIN_MATRIX_REGISTER_COUNT = 3
SKIN_REGISTER_COMPONENT_COUNT = 4
SKIN_MATRIX_ELEMENT_COUNT = SKIN_MATRIX_REGISTER_COUNT * SKIN_REGISTER_COMPONENT_COUNT
FLOAT32_BYTES = 4
FNV1A32_OFFSET_BASIS = 2166136261
FNV1A32_PRIME = 16777619
UINT32_MASK = (1 << 32) - 1
HEXADECIMAL_RADIX = 16
NDC_MINIMUM = -1.0
NDC_MAXIMUM = 1.0
NDC_DIAMETER = NDC_MAXIMUM - NDC_MINIMUM
SURFACE_CONTRACT_EVENT = "actor-surface-contract"
D3D_MATRIX_ELEMENT_COUNT = 16
D3D_PROJECTION_X_SCALE_INDEX = 0
D3D_PROJECTION_Y_SCALE_INDEX = 5
D3D_PROJECTION_X_OFFSET_INDEX = 8
D3D_PROJECTION_Y_OFFSET_INDEX = 9
D3D_PROJECTION_DEPTH_SCALE_INDEX = 10
D3D_PROJECTION_W_TERM_INDEX = 11
D3D_PROJECTION_DEPTH_TRANSLATION_INDEX = 14
D3D_PROJECTION_BOTTOM_RIGHT_INDEX = 15
D3D_PERSPECTIVE_W_TERM = 1.0
D3D_HOMOGENEOUS_BOTTOM_RIGHT = 0.0
MATRIX_ABSOLUTE_TOLERANCE = 1.0e-5
MATRIX_RELATIVE_TOLERANCE = 1.0e-5
D3D_FLOAT32_FAR_RECONSTRUCTION_RELATIVE_TOLERANCE = 0.01
D3D_FLOAT32_FAR_RECONSTRUCTION_ABSOLUTE_TOLERANCE = 1.0


def _load_json(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"Expected one JSON object: {path}")
    return document


def _load_jsonl(path: Path) -> list[dict[str, object]]:
    rows = []
    with path.open("r", encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, start=1):
            try:
                row = json.loads(line)
            except json.JSONDecodeError as error:
                raise ValueError(f"Invalid JSONL at {path}:{line_number}") from error
            if not isinstance(row, dict):
                raise ValueError(f"Expected JSON object at {path}:{line_number}")
            rows.append(row)
    return rows


def _descriptor(path: Path) -> dict[str, object]:
    return {
        "path": str(path.resolve()),
        "bytes": path.stat().st_size,
        "sha256": file_sha256(path),
    }


def _validate_descriptor(root: Path, descriptor: dict[str, object]) -> Path:
    path = root / str(descriptor["file"])
    if not path.is_file():
        raise FileNotFoundError(path)
    if path.stat().st_size != int(descriptor["bytes"]):
        raise ValueError(f"Corpus byte count mismatch: {path}")
    actual = file_sha256(path)
    if actual.lower() != str(descriptor["sha256"]).lower():
        raise ValueError(f"Corpus hash mismatch: {path}")
    return path


def _one(rows: list[dict[str, object]], label: str) -> dict[str, object]:
    if len(rows) != 1:
        raise ValueError(f"Expected one {label}, found {len(rows)}")
    return rows[0]


def _subtract(left: object, right: object, label: str) -> list[float]:
    one = [float(value) for value in left]
    two = [float(value) for value in right]
    if len(one) != len(two) or not one:
        raise ValueError(f"Retail {label} vector dimensions disagree")
    return [a - b for a, b in zip(one, two)]


def _event_hash(event: dict[str, object]) -> str:
    payload = json.dumps(event, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _fnv1a32(payload: bytes) -> int:
    value = FNV1A32_OFFSET_BASIS
    for byte in payload:
        value = ((value ^ byte) * FNV1A32_PRIME) & UINT32_MASK
    return value


def _project_retail_point(
    matrix_value: object,
    world_value: object,
    width: int,
    height: int,
    label: str,
) -> dict[str, object]:
    matrix = _finite_numbers(
        matrix_value, HOMOGENEOUS_MATRIX_ELEMENT_COUNT, f"{label} matrix"
    )
    world = _finite_numbers(
        world_value, HOMOGENEOUS_SPATIAL_DIMENSIONS, f"{label} point"
    )
    x, y, z = world
    clip = [
        matrix[row * 4] * x
        + matrix[row * 4 + 1] * y
        + matrix[row * 4 + 2] * z
        + matrix[row * 4 + 3]
        for row in range(4)
    ]
    if clip[3] <= 0.0 or any(not math.isfinite(value) for value in clip):
        raise ValueError(f"Retail {label} has an invalid homogeneous projection")
    ndc = [clip[axis] / clip[3] for axis in range(HOMOGENEOUS_SPATIAL_DIMENSIONS)]
    pixels = [
        (ndc[0] - NDC_MINIMUM) * width / NDC_DIAMETER,
        (NDC_MAXIMUM - ndc[1]) * height / NDC_DIAMETER,
    ]
    return {
        "pixels": pixels,
        "ndc": ndc,
        "clipW": clip[3],
        "insideViewport": all(
            NDC_MINIMUM <= ndc[axis] <= NDC_MAXIMUM for axis in range(2)
        ),
    }


def _finite_numbers(value: object, count: int, label: str) -> list[float]:
    values = [float(item) for item in value]
    if len(values) != count or any(not math.isfinite(item) for item in values):
        raise ValueError(f"Retail {label} must contain {count} finite values")
    return values


def _close(left: float, right: float) -> bool:
    return math.isclose(
        left,
        right,
        rel_tol=MATRIX_RELATIVE_TOLERANCE,
        abs_tol=MATRIX_ABSOLUTE_TOLERANCE,
    )


def _d3d_perspective_frustum(
    matrix_value: object, label: str
) -> tuple[list[float], float]:
    matrix = _finite_numbers(matrix_value, D3D_MATRIX_ELEMENT_COUNT, label)
    x_scale = matrix[D3D_PROJECTION_X_SCALE_INDEX]
    y_scale = matrix[D3D_PROJECTION_Y_SCALE_INDEX]
    depth_scale = matrix[D3D_PROJECTION_DEPTH_SCALE_INDEX]
    depth_translation = matrix[D3D_PROJECTION_DEPTH_TRANSLATION_INDEX]
    if (
        x_scale <= 0.0
        or y_scale <= 0.0
        or depth_scale <= D3D_PERSPECTIVE_W_TERM
        or depth_translation >= 0.0
        or not _close(
            matrix[D3D_PROJECTION_W_TERM_INDEX], D3D_PERSPECTIVE_W_TERM
        )
        or not _close(
            matrix[D3D_PROJECTION_BOTTOM_RIGHT_INDEX],
            D3D_HOMOGENEOUS_BOTTOM_RIGHT,
        )
    ):
        raise ValueError(f"Retail {label} is not a finite D3D9 perspective matrix")

    width = 2.0 / x_scale
    horizontal_sum = -matrix[D3D_PROJECTION_X_OFFSET_INDEX] * width
    left = (horizontal_sum - width) / 2.0
    right = (horizontal_sum + width) / 2.0
    height = 2.0 / y_scale
    vertical_sum = -matrix[D3D_PROJECTION_Y_OFFSET_INDEX] * height
    bottom = (vertical_sum - height) / 2.0
    top = (vertical_sum + height) / 2.0
    near = -depth_translation / depth_scale
    far = -depth_translation / (depth_scale - D3D_PERSPECTIVE_W_TERM)
    if left >= right or bottom >= top or near <= 0.0 or far <= near:
        raise ValueError(f"Retail {label} resolves to an invalid perspective frustum")
    fov_y = math.atan(top) - math.atan(bottom)
    return [left, right, top, bottom, near, far, 0.0], fov_y


def _replace_d3d_projection_xy(
    combined_value: object,
    culling_projection_value: object,
    surface_projection_value: object,
    label: str,
) -> list[float]:
    combined = _finite_numbers(
        combined_value, D3D_MATRIX_ELEMENT_COUNT, f"{label} combined matrix"
    )
    culling = _finite_numbers(
        culling_projection_value,
        D3D_MATRIX_ELEMENT_COUNT,
        f"{label} culling projection",
    )
    surface = _finite_numbers(
        surface_projection_value,
        D3D_MATRIX_ELEMENT_COUNT,
        f"{label} surface projection",
    )
    if culling[D3D_PROJECTION_X_SCALE_INDEX] <= 0.0 or culling[
        D3D_PROJECTION_Y_SCALE_INDEX
    ] <= 0.0:
        raise ValueError(f"Retail {label} culling projection has invalid XY scale")
    unchanged_indices = (
        D3D_PROJECTION_X_OFFSET_INDEX,
        D3D_PROJECTION_Y_OFFSET_INDEX,
        D3D_PROJECTION_DEPTH_SCALE_INDEX,
        D3D_PROJECTION_W_TERM_INDEX,
        D3D_PROJECTION_DEPTH_TRANSLATION_INDEX,
        D3D_PROJECTION_BOTTOM_RIGHT_INDEX,
    )
    if any(not _close(culling[index], surface[index]) for index in unchanged_indices):
        raise ValueError(
            f"Retail {label} surface and culling projections differ beyond XY scale"
        )
    result = list(combined)
    x_ratio = surface[D3D_PROJECTION_X_SCALE_INDEX] / culling[
        D3D_PROJECTION_X_SCALE_INDEX
    ]
    y_ratio = surface[D3D_PROJECTION_Y_SCALE_INDEX] / culling[
        D3D_PROJECTION_Y_SCALE_INDEX
    ]
    result[0:4] = [value * x_ratio for value in result[0:4]]
    result[4:HOMOGENEOUS_MATRIX_SECOND_ROW_END] = [
        value * y_ratio
        for value in result[4:HOMOGENEOUS_MATRIX_SECOND_ROW_END]
    ]
    return result


def _validated_surface_contract(
    event: dict[str, object],
    frame: int,
    width: int,
    height: int,
    camera: dict[str, object],
) -> dict[str, object]:
    surface = event.get("surface")
    if (
        event.get("event") != SURFACE_CONTRACT_EVENT
        or int(event["frame"]) != frame
        or int(event["sourceFrame"]) != frame
        or int(event["captureCount"]) != 1
        or not bool(event.get("targetTexturesReady"))
        or not isinstance(surface, dict)
        or int(surface["sourceFrame"]) != frame
        or int(surface["renderFrame"])
        != frame - int(event["renderFrameLead"])
    ):
        raise ValueError(f"Retail frame {frame} has no unique surface contract")

    shader = surface.get("vertexShader")
    texture = surface.get("matchedTexture")
    transforms = surface.get("fixedFunctionTransforms")
    target = surface.get("renderTarget")
    if (
        not isinstance(shader, dict)
        or int(shader["getResult"]) != 0
        or int(shader["getFunctionResult"]) != 0
        or int(shader["byteCount"]) <= 0
        or int(shader["fnv1a32"]) == 0
        or not bool(shader.get("hasBonesParameter"))
        or not bool(shader.get("hasSkinModelViewProjectionParameter"))
        or not isinstance(texture, dict)
        or not str(texture.get("path", "")).strip()
        or not isinstance(transforms, dict)
        or any(int(transforms[f"{name}Result"]) != 0 for name in ("world", "view", "projection"))
        or not isinstance(target, dict)
        or not bool(target.get("matchesBackBufferDimensions"))
    ):
        raise ValueError(f"Retail frame {frame} surface draw is incomplete")

    world_matrix = _finite_numbers(
        transforms["world"], D3D_MATRIX_ELEMENT_COUNT, f"frame {frame} D3D world"
    )
    view_matrix = _finite_numbers(
        transforms["view"], D3D_MATRIX_ELEMENT_COUNT, f"frame {frame} D3D view"
    )
    projection_matrix = _finite_numbers(
        transforms["projection"],
        D3D_MATRIX_ELEMENT_COUNT,
        f"frame {frame} D3D projection",
    )
    frustum, fov_y = _d3d_perspective_frustum(
        projection_matrix, f"frame {frame} D3D projection"
    )
    render_description = target.get("renderTargetDescription")
    back_description = target.get("backBufferDescription")
    viewport = target.get("viewport")
    scissor = target.get("scissor")
    if (
        any(
            int(target[name]) != 0
            for name in (
                "renderTargetResult",
                "renderTargetDescriptionResult",
                "backBufferResult",
                "backBufferDescriptionResult",
                "renderTargetIdentityResult",
                "backBufferIdentityResult",
            )
        )
        or not isinstance(render_description, dict)
        or not isinstance(back_description, dict)
        or int(render_description["width"]) != width
        or int(render_description["height"]) != height
        or int(back_description["width"]) != width
        or int(back_description["height"]) != height
        or not isinstance(viewport, dict)
        or int(viewport["getResult"]) != 0
        or [int(viewport[name]) for name in ("x", "y", "width", "height")]
        != [0, 0, width, height]
        or not _close(float(viewport["minimumZ"]), 0.0)
        or not _close(float(viewport["maximumZ"]), 1.0)
        or not isinstance(scissor, dict)
        or int(scissor["getResult"]) != 0
        or [int(scissor[name]) for name in ("left", "top", "right", "bottom")]
        != [0, 0, width, height]
    ):
        raise ValueError(f"Retail frame {frame} surface target is not source-resolution")

    culling_frustum = _finite_numbers(
        camera["frustum"], FRUSTUM_ELEMENT_COUNT,
        f"frame {frame} NiCamera culling frustum"
    )
    if not _close(
        frustum[FRUSTUM_NEAR_INDEX], culling_frustum[FRUSTUM_NEAR_INDEX]
    ) or not math.isclose(
        frustum[FRUSTUM_FAR_INDEX],
        culling_frustum[FRUSTUM_FAR_INDEX],
        rel_tol=D3D_FLOAT32_FAR_RECONSTRUCTION_RELATIVE_TOLERANCE,
        abs_tol=D3D_FLOAT32_FAR_RECONSTRUCTION_ABSOLUTE_TOLERANCE,
    ):
        raise ValueError(
            f"Retail frame {frame} surface and culling depth ranges disagree"
        )
    frustum[FRUSTUM_NEAR_INDEX] = culling_frustum[FRUSTUM_NEAR_INDEX]
    frustum[FRUSTUM_FAR_INDEX] = culling_frustum[FRUSTUM_FAR_INDEX]
    source_aspect = width / height
    surface_aspect = (frustum[1] - frustum[0]) / (frustum[2] - frustum[3])
    if not math.isclose(
        source_aspect,
        surface_aspect,
        rel_tol=MATRIX_RELATIVE_TOLERANCE,
        abs_tol=MATRIX_ABSOLUTE_TOLERANCE,
    ):
        raise ValueError(f"Retail frame {frame} surface projection aspect changed")
    world_to_clip = _replace_d3d_projection_xy(
        camera["viewMatrix"],
        camera["projectionMatrix"],
        projection_matrix,
        f"frame {frame}",
    )
    return {
        "eventSha256": _event_hash(event),
        "renderFrame": int(surface["renderFrame"]),
        "matchedTexture": texture,
        "vertexShader": shader,
        "renderTarget": {
            "isDirectBackBuffer": bool(target.get("isBackBuffer")),
            "matchesBackBufferDimensions": True,
            "sceneColor": render_description,
            "backBuffer": back_description,
            "viewport": viewport,
            "scissor": scissor,
        },
        "worldMatrix": world_matrix,
        "viewMatrix": view_matrix,
        "projectionMatrix": projection_matrix,
        "worldToClipMatrix": world_to_clip,
        "frustum": frustum,
        "fovYRadians": fov_y,
    }


def _validated_visual_snapshot(
    event: dict[str, object],
    frame: int,
    reference_form: int,
    base_form: int,
) -> dict[str, object]:
    if (
        int(event["frame"]) != frame
        or int(event["requestedFrame"]) != frame
        or int(event["refForm"]) != reference_form
        or int(event["baseForm"]) != base_form
    ):
        raise ValueError(f"Retail visual snapshot identity differs at frame {frame}")
    root = event.get("rootWorld")
    if not isinstance(root, dict):
        raise ValueError(f"Retail visual snapshot has no actor root at frame {frame}")
    _finite_numbers(
        root["rotation"], ROTATION_MATRIX_ELEMENT_COUNT,
        f"frame {frame} actor-root rotation"
    )
    _finite_numbers(root["translation"], 3, f"frame {frame} actor-root translation")
    root_scale = float(root["scale"])
    if not math.isfinite(root_scale) or root_scale <= 0.0:
        raise ValueError(f"Retail visual snapshot has invalid actor-root scale at frame {frame}")
    nodes = event.get("nodes")
    if not isinstance(nodes, list) or not nodes:
        raise ValueError(f"Retail visual snapshot has no named nodes at frame {frame}")
    node_paths = []
    for node in nodes:
        if (
            not isinstance(node, dict)
            or not str(node.get("name", "")).strip()
            or not str(node.get("nodePath", "")).strip()
        ):
            raise ValueError(f"Retail visual snapshot has an unnamed node at frame {frame}")
        node_paths.append(str(node["nodePath"]))
        transform = node.get("transform")
        if not isinstance(transform, dict):
            raise ValueError(f"Retail visual snapshot has no transform for {node['name']}")
        for space in ("local", "world"):
            _finite_numbers(
                transform[f"{space}Rotation"],
                ROTATION_MATRIX_ELEMENT_COUNT,
                f"frame {frame} {node['name']} {space} rotation",
            )
            _finite_numbers(
                transform[f"{space}Translation"],
                3,
                f"frame {frame} {node['name']} {space} translation",
            )
            scale = float(transform[f"{space}Scale"])
            if not math.isfinite(scale) or scale <= 0.0:
                raise ValueError(
                    f"Retail visual snapshot has invalid {space} scale for {node['name']}"
                )
    if len(set(node_paths)) != len(node_paths):
        raise ValueError(f"Retail visual snapshot node paths are ambiguous at frame {frame}")
    _validated_skin_palette(event, frame)
    return event


def _validated_skin_palette(
    event: dict[str, object], frame: int
) -> dict[str, object]:
    summary = event.get("skinPaletteCapture")
    instances = event.get("skinPalettes")
    if not isinstance(summary, dict) or not isinstance(instances, list):
        raise ValueError(f"Retail frame {frame} has no skin-palette capture")

    counts = {
        name: int(summary[name])
        for name in (
            "visitedNodes",
            "geometryCandidates",
            "skinInstances",
            "capturedPalettes",
            "notRenderCached",
            "invalidPalettes",
        )
    }
    if (
        counts["visitedNodes"] <= 0
        or counts["geometryCandidates"] < counts["skinInstances"]
        or counts["skinInstances"] <= 0
        or counts["capturedPalettes"] <= 0
        or counts["capturedPalettes"] + counts["notRenderCached"]
        != counts["skinInstances"]
        or counts["invalidPalettes"] != 0
        or bool(summary.get("traversalTruncated"))
        or len(instances) != counts["skinInstances"]
    ):
        raise ValueError(f"Retail frame {frame} skin-palette traversal is incomplete")

    canonical = []
    node_paths = []
    captured_count = 0
    uncached_count = 0
    for instance in instances:
        if not isinstance(instance, dict):
            raise ValueError(f"Retail frame {frame} has a non-object skin instance")
        node_path = str(instance.get("nodePath", "")).strip()
        geometry_name = str(instance.get("geometryName", "")).strip()
        instance_type = str(instance.get("skinInstanceType", "")).strip()
        root_parent_name = str(instance.get("rootParentName", "")).strip()
        status = str(instance.get("status", ""))
        if not node_path or not geometry_name or not instance_type or not root_parent_name:
            raise ValueError(f"Retail frame {frame} has an unidentified skin instance")
        node_paths.append(node_path)
        base = {
            "nodePath": node_path,
            "geometryName": geometry_name,
            "skinInstanceType": instance_type,
            "rootParentName": root_parent_name,
            "frameId": int(instance["frameId"]),
            "status": status,
        }
        if status == "not-render-cached":
            if any(
                int(instance[name]) != 0
                for name in (
                    "matrixCount",
                    "registersPerMatrix",
                    "allocatedBytes",
                    "matrixBytes",
                )
            ):
                raise ValueError(
                    f"Retail frame {frame} uncached skin {geometry_name} has matrix storage"
                )
            uncached_count += 1
            canonical.append(base)
            continue
        if status != "captured":
            raise ValueError(
                f"Retail frame {frame} skin {geometry_name} has unsupported status {status!r}"
            )

        matrix_count = int(instance["matrixCount"])
        registers = int(instance["registersPerMatrix"])
        components = int(instance["componentsPerRegister"])
        matrices = _finite_numbers(
            instance["matrices"],
            matrix_count * SKIN_MATRIX_ELEMENT_COUNT,
            f"frame {frame} {geometry_name} skin matrices",
        )
        bones = instance.get("bones")
        if (
            matrix_count <= 0
            or registers != SKIN_MATRIX_REGISTER_COUNT
            or components != SKIN_REGISTER_COMPONENT_COUNT
            or int(instance["matrixBytes"]) != len(matrices) * FLOAT32_BYTES
            or int(instance["allocatedBytes"]) < int(instance["matrixBytes"])
            or not bool(instance.get("matricesReadable"))
            or not bool(instance.get("matricesFinite"))
            or not bool(instance.get("bonesReadable"))
            or not isinstance(bones, list)
            or len(bones) != matrix_count
        ):
            raise ValueError(f"Retail frame {frame} skin {geometry_name} is incomplete")
        expected_hash = int(instance["fnv1a32"])
        actual_hash = _fnv1a32(struct.pack(f"<{len(matrices)}f", *matrices))
        if actual_hash != expected_hash:
            raise ValueError(
                f"Retail frame {frame} skin {geometry_name} matrix hash changed"
            )
        canonical_bones = []
        for index, bone in enumerate(bones):
            if (
                not isinstance(bone, dict)
                or int(bone.get("index", -1)) != index
                or not str(bone.get("name", "")).strip()
            ):
                raise ValueError(
                    f"Retail frame {frame} skin {geometry_name} has invalid bone order"
                )
            start = index * SKIN_MATRIX_ELEMENT_COUNT
            canonical_bones.append(
                {
                    "skinIndex": index,
                    "name": str(bone["name"]),
                    "matrixRowMajor3x4": matrices[
                        start : start + SKIN_MATRIX_ELEMENT_COUNT
                    ],
                }
            )
        base.update(
            {
                "matrixLayout": "row-major-3x4",
                "matrixStage": "retail-skin-shader-preprojection",
                "matrixSpace": "camera-origin-relative-gamebryo-world",
                "translationOrigin": "validated-nicamera-world-translation",
                "finalProjectionRequired": True,
                "registersPerMatrix": registers,
                "componentsPerRegister": components,
                "sourceFnv1a32": expected_hash,
                "bones": canonical_bones,
            }
        )
        captured_count += 1
        canonical.append(base)

    if len(set(node_paths)) != len(node_paths):
        raise ValueError(f"Retail frame {frame} skin instance paths are ambiguous")
    if captured_count != counts["capturedPalettes"] or uncached_count != counts["notRenderCached"]:
        raise ValueError(f"Retail frame {frame} skin-palette counts disagree")
    return {
        "frameBoundToSourceBackbuffer": True,
        "summary": {
            **counts,
            "traversalTruncated": False,
        },
        "instances": canonical,
    }


def _validated_camera_observation(
    event: dict[str, object],
    frame: int,
    shot_kind: str,
) -> dict[str, object]:
    if (
        int(event["frame"]) != frame
        or str(event["shotKind"]) != shot_kind
        or not bool(event.get("readable"))
        or not bool(event.get("projectionExact"))
    ):
        raise ValueError(f"Retail camera identity or exactness differs at frame {frame}")
    world = event.get("cameraWorld")
    if not isinstance(world, dict):
        raise ValueError(f"Retail camera has no world transform at frame {frame}")
    _finite_numbers(
        world["rotation"], ROTATION_MATRIX_ELEMENT_COUNT,
        f"frame {frame} camera rotation"
    )
    _finite_numbers(world["translation"], 3, f"frame {frame} camera translation")
    scale = float(world["scale"])
    if not math.isfinite(scale) or scale <= 0.0:
        raise ValueError(f"Retail camera has invalid scale at frame {frame}")
    _finite_numbers(
        event["viewMatrix"], D3D_MATRIX_ELEMENT_COUNT,
        f"frame {frame} camera view matrix"
    )
    _finite_numbers(
        event["projectionMatrix"], D3D_MATRIX_ELEMENT_COUNT,
        f"frame {frame} camera projection matrix"
    )
    frustum = _finite_numbers(
        event["frustum"], FRUSTUM_ELEMENT_COUNT,
        f"frame {frame} camera frustum"
    )
    _finite_numbers(event["viewport"], 4, f"frame {frame} camera viewport")
    fov_y = float(event["fovYRadians"])
    minimum_near = float(event["minimumNear"])
    maximum_ratio = float(event["maximumFarNearRatio"])
    if (
        not math.isfinite(fov_y)
        or fov_y <= 0.0
        or frustum[0] >= frustum[1]
        or frustum[3] >= frustum[2]
        or frustum[4] <= 0.0
        or frustum[FRUSTUM_FAR_INDEX] <= frustum[FRUSTUM_NEAR_INDEX]
        or int(frustum[FRUSTUM_ORTHOGRAPHIC_INDEX]) != 0
        or not math.isfinite(minimum_near)
        or minimum_near <= 0.0
        or not math.isfinite(maximum_ratio)
        or maximum_ratio <= 1.0
    ):
        raise ValueError(f"Retail camera perspective contract is invalid at frame {frame}")
    return event


def _appearance_contract(events: list[dict[str, object]]) -> dict[str, object]:
    snapshots = [row for row in events if row.get("event") == "actor-visual-snapshot"]
    retained = [row for row in snapshots if isinstance(row.get("appearance"), dict)]
    snapshot = _one(retained, "retail actor appearance snapshot")
    appearance = snapshot["appearance"]
    if (
        appearance.get("schema") != RETAIL_APPEARANCE_SCHEMA
        or not bool(appearance.get("complete"))
        or bool(appearance.get("truncated"))
        or not isinstance(appearance.get("renderParts"), list)
        or not appearance["renderParts"]
    ):
        raise ValueError("Retail actor appearance snapshot is incomplete or truncated")
    render_parts = appearance["renderParts"]
    if any(not isinstance(part, dict) for part in render_parts):
        raise ValueError("Retail actor appearance render parts are not objects")
    if any(
        not isinstance(part.get("textureBindings"), list)
        or any(not isinstance(binding, dict) for binding in part["textureBindings"])
        for part in render_parts
    ):
        raise ValueError("Retail actor appearance texture bindings are not object arrays")
    frame = int(snapshot["frame"])
    pose = _one(
        [
            row
            for row in events
            if row.get("event") == "actor-pose-sample" and int(row.get("frame", -1)) == frame
        ],
        f"retail actor pose at appearance frame {frame}",
    )
    weapon = appearance.get("equippedWeapon")
    if not isinstance(weapon, dict):
        raise ValueError("Retail appearance snapshot has no equipped-weapon contract")
    state = str(weapon.get("state", ""))
    render_state = str(weapon.get("renderState", ""))
    if not isinstance(weapon.get("weaponOut"), bool):
        raise ValueError("Retail equipped-weapon contract has no Boolean weaponOut state")
    weapon_out = weapon["weaponOut"]
    form_text = str(weapon.get("sourceFormId", ""))
    if re.fullmatch(r"0x[0-9A-F]{8}", form_text) is None:
        raise ValueError("Retail equipped-weapon FormID is not canonical")
    form_id = int(form_text[2:], HEXADECIMAL_RADIX)
    pose_form_id = int(pose.get("weaponForm", -1))
    if not isinstance(pose.get("weaponOut"), bool):
        raise ValueError("Retail appearance pose has no Boolean weaponOut state")
    pose_weapon_out = pose["weaponOut"]
    model_path = str(weapon.get("modelPath", ""))
    if not isinstance(weapon.get("nodePresent"), bool):
        raise ValueError("Retail equipped-weapon contract has no Boolean nodePresent state")
    node_present = weapon["nodePresent"]
    visible_weapon_parts = [
        part
        for part in render_parts
        if part.get("role") == "weapon"
        and bool(part.get("visible"))
    ]
    if state == WEAPON_STATE_NONE:
        if (
            render_state != WEAPON_RENDER_STATE_NOT_APPLICABLE
            or weapon_out
            or pose_weapon_out
            or form_id != 0
            or pose_form_id != 0
            or node_present
            or model_path
            or visible_weapon_parts
        ):
            raise ValueError("Retail no-weapon appearance disagrees with its pose or render parts")
    elif state == WEAPON_STATE_EQUIPPED:
        if form_id == 0 or pose_form_id != form_id or pose_weapon_out != weapon_out:
            raise ValueError("Retail equipped weapon disagrees with its same-frame pose")
        canonical_model_path = model_path.strip().lower().replace("\\", "/")
        canonical_model = (
            canonical_model_path == model_path
            and canonical_model_path.endswith(".nif")
            and not canonical_model_path.startswith("/")
            and "../" not in canonical_model_path
        )
        if render_state == WEAPON_RENDER_STATE_VISIBLE_SOURCE_BOUND:
            matching_parts = [
                part
                for part in visible_weapon_parts
                if part.get("sourceFormId") == form_text
                and part.get("modelPath") == model_path
                and bool(part.get("required"))
                and bool(part.get("attached"))
                and bool(part.get("drawable"))
            ]
            if not node_present or not canonical_model or not matching_parts:
                raise ValueError(
                    "Retail equipped weapon lacks an authoritative visible runtime attachment"
                )
        elif render_state == WEAPON_RENDER_STATE_NOT_VISIBLE_AT_FRAME:
            if weapon_out or visible_weapon_parts:
                raise ValueError(
                    "Retail nonvisible equipped weapon disagrees with its same-frame pose or render parts"
                )
            if model_path and not canonical_model:
                raise ValueError("Retail equipped weapon model path is not canonical")
        else:
            raise ValueError(
                f"Retail equipped-weapon render state is not evidence-capable: {render_state!r}"
            )
    else:
        raise ValueError(f"Retail equipped-weapon state is not evidence-capable: {state!r}")
    return {
        "frame": frame,
        "eventSha256": _event_hash(snapshot),
        "snapshot": appearance,
    }


def _animation_state(pose: dict[str, object]) -> list[dict[str, object]]:
    sequences = [
        (slot, row)
        for slot, row in enumerate(pose.get("animationDataSequences", []))
        if isinstance(row, dict)
        and str(row.get("file", "")).strip()
        and float(row.get("weight", 0.0)) > 0.0
    ]
    if not sequences:
        raise ValueError("Retail actor pose has no active animation sequence")
    return [
        {
            "sequenceSlot": slot,
            "file": str(sequence["file"]),
            "state": int(sequence["state"]),
            "cycle": int(sequence["cycle"]),
            "weight": float(sequence["weight"]),
            "frequency": float(sequence["frequency"]),
            "phaseSeconds": float(sequence["lastScaled"]),
            "group": int(sequence["group"]),
        }
        for slot, sequence in sequences
    ]


def _environment_contract(events: list[dict[str, object]]) -> dict[str, object]:
    event = _one(
        [row for row in events if row.get("event") == "render-environment"],
        "retail render-environment event",
    )
    return {
        "eventSha256": _event_hash(event),
        "frame": int(event["frame"]),
        "currentWeatherForm": int(event["currentWeatherForm"]),
        "defaultWeatherForm": int(event["defaultWeatherForm"]),
        "gameHour": float(event["gameHour"]),
        "weatherPercent": float(event["weatherPercent"]),
        "skyMode": int(event["skyMode"]),
        "baseImageSpace": event["baseImageSpace"],
        "sunAmbient": event["sunAmbient"],
        "sunDirectional": event["sunDirectional"],
        "sunFog": event["sunFog"],
    }


def _frame_number(path: Path) -> int:
    match = FRAME_FILE_PATTERN.search(path.name)
    if match is None:
        raise ValueError(f"Retail source frame has no frame number: {path}")
    return int(match.group("frame"))


def _bitmap_dimensions(path: Path) -> tuple[int, int]:
    with path.open("rb") as stream:
        signature = stream.read(len(BITMAP_SIGNATURE))
        stream.seek(BITMAP_DIMENSION_OFFSET)
        dimensions = stream.read(BITMAP_DIMENSION_BYTES)
    if signature != BITMAP_SIGNATURE or len(dimensions) != BITMAP_DIMENSION_BYTES:
        raise ValueError(f"Retail source frame is not a complete BMP: {path}")
    width, signed_height = struct.unpack("<ii", dimensions)
    height = abs(signed_height)
    if width <= 0 or height <= 0:
        raise ValueError(f"Retail source frame has invalid dimensions: {path}")
    return width, height


def _retail_shots(
    review: dict[str, object],
    report: dict[str, object],
    events: list[dict[str, object]],
) -> list[dict[str, object]]:
    captures = {
        str(row["kind"]): row
        for row in report["capture"]["shots"]
    }
    required = [str(kind) for kind in review["requiredShots"]]
    if set(captures) != set(required):
        raise ValueError("Retail capture shot kinds differ from the review row")
    artifact_by_path = {
        str(Path(str(row["path"])).resolve()).casefold(): row
        for row in report["artifacts"]
    }
    pose_by_frame = {
        int(row["frame"]): row
        for row in events
        if row.get("event") == "actor-pose-sample"
    }
    visual_events = [
        row for row in events if row.get("event") == "actor-visual-snapshot"
    ]
    camera_observation_events = [
        row for row in events if row.get("event") == "review-camera-observation"
    ]
    surface_contract_events = [
        row for row in events if row.get("event") == SURFACE_CONTRACT_EVENT
    ]
    template = _one(
        [row for row in events if row.get("event") == "actor-template-observation"],
        "retail actor-template observation",
    )
    reference_form = int(template["referenceForm"])
    base_form = int(template["runtimeBaseForm"])
    camera_by_kind = {
        kind: _one(
            [
                row
                for row in events
                if row.get("event") == "portrait-camera-set"
                and row.get("shotKind") == kind
            ],
            f"retail {kind} camera-set event",
        )
        for kind in required
    }
    result = []
    for kind in required:
        camera = camera_by_kind[kind]
        samples = []
        for frame_value in captures[kind]["screenshotFrames"]:
            frame = int(frame_value)
            pose = pose_by_frame.get(frame)
            if pose is None:
                raise ValueError(f"Retail {kind} has no pose sample at source frame {frame}")
            visual = _validated_visual_snapshot(
                _one(
                    [
                        row
                        for row in visual_events
                        if int(row.get("requestedFrame", -1)) == frame
                    ],
                    f"retail {kind} visual snapshot at frame {frame}",
                ),
                frame,
                reference_form,
                base_form,
            )
            camera_observation = _validated_camera_observation(_one(
                [
                    row
                    for row in camera_observation_events
                    if int(row.get("frame", -1)) == frame
                    and row.get("shotKind") == kind
                ],
                f"retail {kind} camera observation at frame {frame}",
            ), frame, kind)
            source_matches = [
                Path(str(path))
                for path in report["capture"]["sourceFrames"]
                if _frame_number(Path(str(path))) == frame
            ]
            source_path = _one(
                [{"path": str(path)} for path in source_matches],
                f"retail {kind} source frame {frame}",
            )["path"]
            resolved_source = Path(str(source_path)).resolve()
            artifact = artifact_by_path.get(str(resolved_source).casefold())
            if artifact is None:
                raise ValueError(f"Retail source frame is absent from the artifact ledger: {resolved_source}")
            if file_sha256(resolved_source).lower() != str(artifact["sha256"]).lower():
                raise ValueError(f"Retail source frame hash mismatch: {resolved_source}")
            width, height = _bitmap_dimensions(resolved_source)
            surface_contract = _validated_surface_contract(
                _one(
                    [
                        row
                        for row in surface_contract_events
                        if int(row.get("sourceFrame", -1)) == frame
                    ],
                    f"retail {kind} surface contract at frame {frame}",
                ),
                frame,
                width,
                height,
                camera_observation,
            )
            root = visual["rootWorld"]
            skin_palette = _validated_skin_palette(visual, frame)
            skin_palette["finalProjectionEventSha256"] = surface_contract[
                "eventSha256"
            ]
            projected_nodes = []
            for node in visual["nodes"]:
                projected = dict(node)
                projected["retailScreen"] = _project_retail_point(
                    surface_contract["worldToClipMatrix"],
                    node["transform"]["worldTranslation"],
                    width,
                    height,
                    f"frame {frame} node {node['name']}",
                )
                projected_nodes.append(projected)
            samples.append(
                {
                    "frame": frame,
                    "sourceFrame": {
                        "path": str(resolved_source),
                        "bytes": int(artifact["bytes"]),
                        "sha256": str(artifact["sha256"]),
                        "width": width,
                        "height": height,
                    },
                    "actorRoot": root,
                    "camera": {
                        "eventSha256": _event_hash(camera_observation),
                        "world": camera_observation["cameraWorld"],
                        "offsetGameUnits": _subtract(
                            camera_observation["cameraWorld"]["translation"],
                            root["translation"],
                            "exact camera offset",
                        ),
                        "fovYRadians": surface_contract["fovYRadians"],
                        "frustum": surface_contract["frustum"],
                        "minimumNear": camera_observation["minimumNear"],
                        "maximumFarNearRatio": camera_observation[
                            "maximumFarNearRatio"
                        ],
                        "viewport": camera_observation["viewport"],
                        "worldToClipMatrix": surface_contract["worldToClipMatrix"],
                        "projectionMatrix": surface_contract["projectionMatrix"],
                        "surfaceContract": surface_contract,
                        "cullingObservation": {
                            "eventSha256": _event_hash(camera_observation),
                            "fovYRadians": camera_observation["fovYRadians"],
                            "frustum": camera_observation["frustum"],
                            "viewport": camera_observation["viewport"],
                            "worldToClipMatrix": camera_observation["viewMatrix"],
                            "projectionMatrix": camera_observation["projectionMatrix"],
                        },
                    },
                    "stagingCamera": {
                        "camera": camera["camera"],
                        "aim": camera["aim"],
                    },
                    "animationLayers": _animation_state(pose),
                    "poseEventSha256": _event_hash(pose),
                    "nodes": projected_nodes,
                    "skinPalette": skin_palette,
                    "visualSnapshotEventSha256": _event_hash(visual),
                }
            )
        result.append(
            {
                "kind": kind,
                "setFrame": int(captures[kind]["setFrame"]),
                "focusNode": camera["focusNode"],
                "focusKind": camera["focusKind"],
                "cameraDistanceGameUnits": float(camera["cameraDistance"]),
                "worldBound": camera["worldBound"],
                "cameraEventSha256": _event_hash(camera),
                "projection": {
                    "status": "exact-retail-final-eye-d3d9-perspective",
                    "exact": True,
                    "source": "target-texture-matched skinned draw into the retail source-resolution D3D9 scene-color target",
                },
                "samples": samples,
            }
        )
    return result


def _assembly_contract(
    review: dict[str, object],
    bases: dict[str, dict[str, object]],
) -> dict[str, object]:
    categories = {str(key): str(value) for key, value in review["categorySources"].items()}
    sources = {category: bases[key] for category, key in categories.items()}
    traits = sources["traits"]
    model = sources["model"]
    inventory = sources["inventory"]
    record_type = str(review["recordType"])
    if any(str(source["recordType"]) != record_type for source in sources.values()):
        raise ValueError("Review category sources change actor record type")
    assembly = {
        "recordType": record_type,
        "baseFormKey": review["baseFormKey"],
        "categorySources": categories,
        "skeletonPath": model.get("skeletonPath"),
        "inventory": inventory.get("inventory", []),
    }
    if record_type == "CREA":
        assembly["modelPaths"] = model.get("modelPaths", [])
    elif record_type == "NPC_":
        assembly.update(
            {
                "sex": traits.get("sex"),
                "race": traits.get("race"),
                "hair": traits.get("hair"),
                "eyes": traits.get("eyes"),
                "headParts": traits.get("headParts", []),
                "hairColorRgba": traits.get("hairColorRgba"),
                "faceGen": traits.get("faceGen"),
            }
        )
    else:
        raise ValueError(f"Unsupported review actor type: {record_type}")
    return assembly


def build_actor_review_contract(
    data_root: Path,
    corpus_root: Path,
    review_key: str,
    retail_report_path: Path,
    output_path: Path,
) -> dict[str, object]:
    """Create one immutable, data-selected retail-to-Godot review contract."""

    if output_path.exists():
        raise FileExistsError(f"Refusing to overwrite actor review contract: {output_path}")
    manifest_path = corpus_root / "manifest.json"
    manifest = _load_json(manifest_path)
    if manifest.get("schema") != CORPUS_SCHEMA:
        raise ValueError(f"Unexpected actor corpus: {manifest_path}")
    appearance_path = _validate_descriptor(corpus_root, manifest["outputs"]["appearanceReview"])
    bases_path = _validate_descriptor(corpus_root, manifest["outputs"]["bases"])
    for source in manifest["inputs"]:
        plugin_path = data_root / str(source["file"])
        if not plugin_path.is_file() or file_sha256(plugin_path).lower() != str(source["sha256"]).lower():
            raise ValueError(f"Owned plugin differs from the actor corpus: {plugin_path}")
    reviews = [row for row in _load_jsonl(appearance_path) if row.get("reviewKey") == review_key]
    review = _one(reviews, f"appearance review row {review_key}")
    category_keys = {str(value) for value in review["categorySources"].values()}
    required_base_keys = {str(review["baseFormKey"]), *category_keys}
    base_rows = [row for row in _load_jsonl(bases_path) if row.get("formKey") in required_base_keys]
    bases = {str(row["formKey"]): row for row in base_rows}
    if set(bases) != required_base_keys:
        raise ValueError(f"Actor review category sources are incomplete: {required_base_keys - set(bases)}")

    report = _load_json(retail_report_path)
    if report.get("schema") != RETAIL_REPORT_SCHEMA or report.get("status") != CAPTURED_RETAIL_STATUS:
        raise ValueError(f"Retail actor report is not classified capture evidence: {retail_report_path}")
    if report.get("classifiedReviewKey") != review_key:
        raise ValueError("Retail actor report classified another review key")
    if not bool(report["classification"]["complete"]):
        raise ValueError("Retail actor classification is incomplete")
    if (
        report["runtime"].get("appearanceEvidenceStatus") != "complete"
        or len(report["runtime"].get("visualSnapshots", []))
        != len(report["capture"].get("sourceFrames", []))
        or not bool(
            report["runtime"].get("surfaceContract", {}).get(
                "finalEyeSourceResolutionSceneColorRequired"
            )
        )
        or len(
            report["runtime"].get("surfaceContract", {}).get("sourceFrames", [])
        )
        != len(report["capture"].get("sourceFrames", []))
    ):
        raise ValueError("Retail actor report lacks complete frame-bound visual telemetry")
    if str(report["provenance"]["corpusManifest"]["sha256"]).lower() != file_sha256(manifest_path).lower():
        raise ValueError("Retail report belongs to another actor corpus manifest")
    if [row["file"] for row in report["provenance"]["officialPluginStack"]] != [
        row["file"] for row in manifest["inputs"]
    ]:
        raise ValueError("Retail report official plugin order differs from the actor corpus")
    report_stack = [
        (str(row["file"]), int(row["bytes"]), str(row["sha256"]).lower())
        for row in report["provenance"]["officialPluginStack"]
    ]
    corpus_stack = [
        (str(row["file"]), int(row["bytes"]), str(row["sha256"]).lower())
        for row in manifest["inputs"]
    ]
    if report_stack != corpus_stack:
        raise ValueError("Retail report official plugin descriptors differ from the actor corpus")
    jsonl_artifacts = [
        Path(str(row["path"]))
        for row in report["artifacts"]
        if str(row["path"]).lower().endswith(".jsonl")
    ]
    jsonl_path = Path(str(_one(
        [{"path": str(path)} for path in jsonl_artifacts],
        "retail actor oracle JSONL artifact",
    )["path"]))
    events = _load_jsonl(jsonl_path)
    if any(row.get("schema") != RETAIL_ORACLE_SCHEMA for row in events):
        raise ValueError(f"Retail actor JSONL mixes oracle schemas: {jsonl_path}")
    runtime_stack = _one(
        [row for row in events if row.get("event") == "runtime-plugin-stack"],
        "runtime plugin stack event",
    )
    runtime_names = [row["name"] for row in runtime_stack["plugins"]]
    if runtime_names != [row["file"] for row in manifest["inputs"]]:
        raise ValueError("Retail runtime plugin stack differs from the actor corpus")

    contract = {
        "schema": REVIEW_CONTRACT_SCHEMA,
        "status": PENDING_GODOT_STATUS,
        "review": review,
        "assembly": _assembly_contract(review, bases),
        "retail": {
            "report": _descriptor(retail_report_path),
            "oracleJsonl": _descriptor(jsonl_path),
            "runtimePluginStackEventSha256": _event_hash(runtime_stack),
            "environment": _environment_contract(events),
            "appearance": _appearance_contract(events),
            "shots": _retail_shots(review, report, events),
        },
        "provenance": {
            "corpusManifest": _descriptor(manifest_path),
            "appearanceReview": _descriptor(appearance_path),
            "actorBases": _descriptor(bases_path),
            "officialPlugins": [
                {
                    "file": row["file"],
                    "bytes": row["bytes"],
                    "sha256": row["sha256"],
                }
                for row in manifest["inputs"]
            ],
        },
        "evidencePolicy": {
            "retailIsReferenceOnly": True,
            "inventoryIsNotVisualEvidence": True,
            "exactRetailFinalEyeSurfaceProjectionRequired": True,
            "nicameraCullingStateIsNotFinalSurfaceProjection": True,
            "godotEvidenceStatus": "pending",
            "matchedComparisonStatus": "pending",
        },
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    temporary = output_path.with_name(output_path.name + ".tmp")
    temporary.write_text(json.dumps(contract, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, output_path)
    contract["contract"] = str(output_path.resolve())
    return contract


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--review-key", required=True)
    parser.add_argument("--retail-report", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        contract = build_actor_review_contract(
            args.data_root.resolve(),
            args.corpus_root.resolve(),
            args.review_key,
            args.retail_report.resolve(),
            args.output.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_ACTOR_REVIEW_CONTRACT_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_ACTOR_REVIEW_CONTRACT "
        + json.dumps(
            {
                "contract": contract["contract"],
                "reviewKey": contract["review"]["reviewKey"],
                "recordType": contract["assembly"]["recordType"],
                "status": contract["status"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
