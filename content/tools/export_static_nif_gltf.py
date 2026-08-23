#!/usr/bin/env python3
"""Export one static Gamebryo NIF directly to glTF plus an OpenNV sidecar."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import struct
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable

if not hasattr(time, "clock"):
    time.clock = time.perf_counter  # PyFFI 2.2.3 compatibility on Python 3.8+

from pyffi.formats.nif import NifFormat  # type: ignore  # noqa: E402


SCHEMA = "opennv-static-nif-gltf/v1"
GENERATOR = "OpenNV direct static NIF exporter v1"
SUPPORTED_SHAPE_PROPERTIES = {
    "BSShaderPPLightingProperty",
    "NiMaterialProperty",
    "NiStencilProperty",
}
ATTACHMENT_MARKER_NAMES = {"ProjectileNode", "ShellCasingNode"}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def decode_text(value: object) -> str:
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    try:
        return bytes(value).decode("utf-8", errors="replace")
    except (TypeError, ValueError):
        return str(value)


def canonical_asset_path(value: object) -> str:
    path = decode_text(value).replace("/", "\\").lstrip("\\").lower()
    return path[5:] if path.startswith("data\\") else path


def is_editor_marker(value: object) -> bool:
    return decode_text(value).casefold().startswith("editormarker")


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
    if length <= 1.0e-12:
        raise ValueError("NIF contains a zero-length direction vector")
    return tuple(component / length for component in result)


def texture_uv(value: object) -> tuple[float, float]:
    return float(value.u), 1.0 - float(value.v)


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
        flags = int(alpha.flags)
        blend_enabled = bool(flags & 0x0001)
        test_enabled = bool(flags & 0x0200)
        mode = "BLEND" if blend_enabled else "MASK" if test_enabled else "OPAQUE"
        return {
            "mode": mode,
            "cutoff": float(alpha.threshold) / 255.0 if test_enabled else None,
            "flags": flags,
            "blendEnabled": blend_enabled,
            "testEnabled": test_enabled,
            "sourceBlendMode": (flags >> 1) & 0xF,
            "destinationBlendMode": (flags >> 5) & 0xF,
            "testFunction": (flags >> 10) & 0x7,
            "noSorter": bool(flags & 0x2000),
            "source": "NiAlphaProperty",
        }
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


@dataclass
class BufferBuilder:
    data: bytearray = field(default_factory=bytearray)
    views: list[dict[str, object]] = field(default_factory=list)
    accessors: list[dict[str, object]] = field(default_factory=list)

    def add(
        self,
        payload: bytes,
        *,
        component_type: int,
        count: int,
        value_type: str,
        target: int | None,
        minimum: list[float] | None = None,
        maximum: list[float] | None = None,
    ) -> int:
        while len(self.data) % 4:
            self.data.append(0)
        offset = len(self.data)
        self.data.extend(payload)
        view_index = len(self.views)
        view: dict[str, object] = {"buffer": 0, "byteOffset": offset, "byteLength": len(payload)}
        if target is not None:
            view["target"] = target
        self.views.append(view)
        accessor: dict[str, object] = {
            "bufferView": view_index,
            "componentType": component_type,
            "count": count,
            "type": value_type,
        }
        if minimum is not None:
            accessor["min"] = minimum
        if maximum is not None:
            accessor["max"] = maximum
        self.accessors.append(accessor)
        return len(self.accessors) - 1


def pack_floats(rows: Iterable[Iterable[float]]) -> bytes:
    flat = [float(value) for row in rows for value in row]
    return struct.pack(f"<{len(flat)}f", *flat)


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
        if abs(determinant) <= 1.0e-12:
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
        if length <= 1.0e-12:
            axis = (1.0, 0.0, 0.0) if abs(normal[0]) < 0.9 else (0.0, 1.0, 0.0)
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


def atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def compiler_provenance() -> dict[str, str]:
    if getattr(sys, "frozen", False):
        executable = Path(sys.executable)
        return {"name": "OpenNV.Content packaged direct exporter v1", "sha256": sha256_bytes(executable.read_bytes())}
    return {"name": GENERATOR, "sha256": sha256_bytes(Path(__file__).read_bytes())}


def export_static_nif(
    source: Path,
    logical_path: str,
    gltf_path: Path,
    sidecar_path: Path,
    *,
    strict: bool = True,
    include_shape_prefixes: tuple[str, ...] | None = None,
) -> dict[str, object]:
    source_bytes = source.read_bytes()
    source_hash = sha256_bytes(source_bytes)
    data = NifFormat.Data()
    with source.open("rb") as stream:
        data.read(stream)
    if len(data.roots) != 1:
        raise ValueError(f"Expected one NIF root, found {len(data.roots)}")

    blocks = list(data.get_global_iterator())
    block_index = {id(block): index for index, block in enumerate(blocks)}
    controllers = [type(block).__name__ for block in blocks if isinstance(block, NifFormat.NiTimeController)]
    all_shapes = []
    shape_visits: dict[int, int] = {}
    for block in blocks:
        if not isinstance(block, (NifFormat.NiTriShape, NifFormat.NiTriStrips)) or block.data is None:
            continue
        shape_visits[id(block)] = shape_visits.get(id(block), 0) + 1
        if shape_visits[id(block)] == 1:
            all_shapes.append(block)
    duplicate_shape_visits = [
        {
            "sourceBlockIndex": block_index[block_id],
            "name": decode_text(next(shape for shape in all_shapes if id(shape) == block_id).name),
            "visits": visits,
        }
        for block_id, visits in shape_visits.items()
        if visits > 1
    ]
    excluded_editor_markers = [
        {"sourceBlockIndex": block_index[id(shape)], "name": decode_text(shape.name)}
        for shape in all_shapes
        if is_editor_marker(shape.name)
    ]
    candidate_shapes = [shape for shape in all_shapes if not is_editor_marker(shape.name)]
    excluded_by_shape_filter = []
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
        raise ValueError("NIF contains no supported static geometry")
    if strict and controllers:
        raise ValueError(f"Static slice rejects controller blocks: {sorted(set(controllers))}")

    builder = BufferBuilder()
    primitives: list[dict[str, object]] = []
    materials: list[dict[str, object]] = []
    surface_rows: list[dict[str, object]] = []
    root = data.roots[0]
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
        if getattr(shape, "skin_instance", None) is not None:
            raise ValueError(f"Static slice rejects skinned geometry: {decode_text(shape.name)}")
        property_types = [type(prop).__name__ for prop in shape.properties]
        unsupported = sorted(set(property_types) - SUPPORTED_SHAPE_PROPERTIES)
        if strict and unsupported:
            raise ValueError(f"Static slice rejects properties {unsupported} on {decode_text(shape.name)}")
        if strict and any(isinstance(prop, NifFormat.NiAlphaProperty) for prop in shape.properties):
            raise ValueError(f"Opaque slice rejects alpha property on {decode_text(shape.name)}")

        mesh = shape.data
        vertex_count = len(mesh.vertices)
        if vertex_count == 0 or len(mesh.normals) != vertex_count or not mesh.uv_sets:
            raise ValueError(f"Shape lacks positions, normals, or UV0: {decode_text(shape.name)}")
        if strict and len(mesh.uv_sets) > 2:
            raise ValueError(f"Static slice supports at most two UV sets: {decode_text(shape.name)}")

        matrix = shape.get_transform(root)
        positions = [transform_xyz(value, matrix, direction=False) for value in mesh.vertices]
        normals = [transform_xyz(value, matrix, direction=True) for value in mesh.normals]
        triangles = [tuple(int(index) for index in triangle) for triangle in mesh.get_triangles()]
        if not triangles:
            raise ValueError(f"Shape has no triangles: {decode_text(shape.name)}")
        surface_material = material_metadata(shape)

        attributes: dict[str, int] = {}
        minimum = [min(row[index] for row in positions) for index in range(3)]
        maximum = [max(row[index] for row in positions) for index in range(3)]
        attributes["POSITION"] = builder.add(
            pack_floats(positions), component_type=5126, count=vertex_count, value_type="VEC3",
            target=34962, minimum=minimum, maximum=maximum,
        )
        attributes["NORMAL"] = builder.add(
            pack_floats(normals), component_type=5126, count=vertex_count, value_type="VEC3", target=34962,
        )
        converted_uv_sets = [
            [texture_uv(value) for value in uv_set]
            for uv_set in mesh.uv_sets[:2]
        ]
        for uv_index, uvs in enumerate(converted_uv_sets):
            attributes[f"TEXCOORD_{uv_index}"] = builder.add(
                pack_floats(uvs), component_type=5126, count=vertex_count, value_type="VEC2", target=34962,
            )
        if len(mesh.vertex_colors) == vertex_count:
            colors = [(float(v.r), float(v.g), float(v.b), float(v.a)) for v in mesh.vertex_colors]
            attributes["COLOR_0"] = builder.add(
                pack_floats(colors), component_type=5126, count=vertex_count, value_type="VEC4", target=34962,
            )
        tangent_source = "absent"
        if converted_uv_sets:
            tangents = []
            tangent_source = "nif"
            if len(mesh.tangents) == vertex_count and len(mesh.bitangents) == vertex_count:
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
                pack_floats(tangents), component_type=5126, count=vertex_count, value_type="VEC4", target=34962,
            )

        index_component = 5123 if vertex_count <= 65535 else 5125
        index_format = "H" if index_component == 5123 else "I"
        indices = [value for triangle in triangles for value in triangle]
        index_accessor = builder.add(
            struct.pack(f"<{len(indices)}{index_format}", *indices), component_type=index_component,
            count=len(indices), value_type="SCALAR", target=34963,
        )
        shape_index = block_index[id(shape)]
        original_name = decode_text(shape.name)
        surface_name = f"{original_name}@{shape_index}"
        material_index = len(materials)
        base_color = [float(value) for value in surface_material["baseColor"]]
        alpha = float(surface_material.get("alpha", 1.0))
        glossiness = float(surface_material.get("glossiness", 10.0))
        gltf_material: dict[str, object] = {
            "name": f"{surface_name} material",
            "doubleSided": shape_double_sided(shape),
            "pbrMetallicRoughness": {
                "baseColorFactor": [*base_color, alpha],
                "metallicFactor": 0.0,
                "roughnessFactor": max(0.25, min(0.95, 1.0 - glossiness / 128.0)),
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
        primitives.append({"attributes": attributes, "indices": index_accessor, "material": material_index})

        stable_id = sha256_bytes(f"{source_hash}:{shape_index}:{original_name}".encode())[:24]
        surface_rows.append({
            "stableId": stable_id,
            "sourceBlockIndex": shape_index,
            "name": surface_name,
            "originalName": original_name,
            "vertices": vertex_count,
            "triangles": len(triangles),
            "attributes": sorted(attributes),
            "propertyTypes": property_types,
            "textures": texture_paths(shape),
            "material": surface_material,
            "transformBakedToRoot": True,
            "tangentSource": tangent_source,
        })

    binary_name = gltf_path.with_suffix(".bin").name
    gltf = {
        "asset": {"version": "2.0", "generator": GENERATOR},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": Path(logical_path).stem, "mesh": 0}],
        "meshes": [{"name": Path(logical_path).stem, "primitives": primitives}],
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

    collision_types = sorted({type(block).__name__ for block in blocks if type(block).__name__.startswith("bhk")})
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
        },
        "compiler": compiler_provenance(),
        "outputs": {
            "gltf": {"file": gltf_path.name, "bytes": len(gltf_bytes), "sha256": sha256_bytes(gltf_bytes)},
            "buffer": {"file": binary_name, "bytes": len(binary_bytes), "sha256": sha256_bytes(binary_bytes)},
        },
        "coverage": {
            "surfaces": len(surface_rows),
            "collisionExported": False,
            "collisionBlockTypes": collision_types,
            "controllers": sorted(set(controllers)),
            "excludedEditorMarkerSurfaces": excluded_editor_markers,
            "includedShapePrefixes": list(include_shape_prefixes or ()),
            "excludedByShapeFilter": excluded_by_shape_filter,
            "duplicateShapeVisits": duplicate_shape_visits,
        },
        "attachmentMarkers": attachment_markers,
        "surfaces": surface_rows,
    }
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
    result = export_static_nif(
        args.input,
        args.logical_path,
        args.output,
        args.sidecar,
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
