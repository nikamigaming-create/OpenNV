#!/usr/bin/env python3
"""Export one static Gamebryo NIF directly to glTF plus an OpenNV sidecar."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import struct
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


def texture_paths(shape: object) -> list[str]:
    for prop in getattr(shape, "properties", []):
        texture_set = getattr(prop, "texture_set", None)
        if texture_set is not None:
            return [canonical_asset_path(value) for value in texture_set.textures]
    return []


def material_metadata(shape: object) -> dict[str, object]:
    result: dict[str, object] = {}
    for prop in getattr(shape, "properties", []):
        if isinstance(prop, NifFormat.NiMaterialProperty):
            result["alpha"] = float(prop.alpha)
            result["glossiness"] = float(prop.glossiness)
            result["emissive"] = [
                float(prop.emissive_color.r),
                float(prop.emissive_color.g),
                float(prop.emissive_color.b),
            ]
            result["specular"] = [
                float(prop.specular_color.r),
                float(prop.specular_color.g),
                float(prop.specular_color.b),
            ]
        elif isinstance(prop, NifFormat.BSShaderPPLightingProperty):
            result["shaderType"] = int(prop.shader_type)
            result["shaderFlags1"] = int(getattr(prop, "shader_flags_1", prop.shader_flags))
            result["shaderFlags2"] = int(prop.shader_flags_2)
            result["textureClampMode"] = int(prop.texture_clamp_mode)
    return result


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
        target: int,
        minimum: list[float] | None = None,
        maximum: list[float] | None = None,
    ) -> int:
        while len(self.data) % 4:
            self.data.append(0)
        offset = len(self.data)
        self.data.extend(payload)
        view_index = len(self.views)
        self.views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(payload), "target": target})
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


def atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def export_static_nif(
    source: Path,
    logical_path: str,
    gltf_path: Path,
    sidecar_path: Path,
    *,
    strict: bool = True,
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
    shapes = [
        block
        for block in blocks
        if isinstance(block, (NifFormat.NiTriShape, NifFormat.NiTriStrips)) and block.data is not None
    ]
    if not shapes:
        raise ValueError("NIF contains no supported static geometry")
    if strict and controllers:
        raise ValueError(f"Static slice rejects controller blocks: {sorted(set(controllers))}")

    builder = BufferBuilder()
    primitives: list[dict[str, object]] = []
    materials: list[dict[str, object]] = []
    surface_rows: list[dict[str, object]] = []
    root = data.roots[0]

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
        for uv_index, uv_set in enumerate(mesh.uv_sets[:2]):
            uvs = [(float(value.u), 1.0 - float(value.v)) for value in uv_set]
            attributes[f"TEXCOORD_{uv_index}"] = builder.add(
                pack_floats(uvs), component_type=5126, count=vertex_count, value_type="VEC2", target=34962,
            )
        if len(mesh.vertex_colors) == vertex_count:
            colors = [(float(v.r), float(v.g), float(v.b), float(v.a)) for v in mesh.vertex_colors]
            attributes["COLOR_0"] = builder.add(
                pack_floats(colors), component_type=5126, count=vertex_count, value_type="VEC4", target=34962,
            )
        if len(mesh.tangents) == vertex_count and len(mesh.bitangents) == vertex_count:
            tangents = []
            for normal, tangent, bitangent in zip(normals, mesh.tangents, mesh.bitangents):
                tangent_xyz = transform_xyz(tangent, matrix, direction=True)
                bitangent_xyz = transform_xyz(bitangent, matrix, direction=True)
                cross = (
                    normal[1] * tangent_xyz[2] - normal[2] * tangent_xyz[1],
                    normal[2] * tangent_xyz[0] - normal[0] * tangent_xyz[2],
                    normal[0] * tangent_xyz[1] - normal[1] * tangent_xyz[0],
                )
                handedness = 1.0 if sum(a * b for a, b in zip(cross, bitangent_xyz)) >= 0.0 else -1.0
                tangents.append((*tangent_xyz, handedness))
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
        material_index = len(materials)
        materials.append({
            "name": f"{decode_text(shape.name)} material",
            "doubleSided": False,
            "pbrMetallicRoughness": {
                "baseColorFactor": [0.65, 0.65, 0.65, 1.0],
                "metallicFactor": 0.0,
                "roughnessFactor": 1.0,
            },
        })
        primitives.append({"attributes": attributes, "indices": index_accessor, "material": material_index})

        shape_index = block_index[id(shape)]
        stable_id = sha256_bytes(f"{source_hash}:{shape_index}:{decode_text(shape.name)}".encode())[:24]
        surface_rows.append({
            "stableId": stable_id,
            "sourceBlockIndex": shape_index,
            "name": decode_text(shape.name),
            "vertices": vertex_count,
            "triangles": len(triangles),
            "attributes": sorted(attributes),
            "propertyTypes": property_types,
            "textures": texture_paths(shape),
            "material": material_metadata(shape),
            "transformBakedToRoot": True,
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
        "compiler": {
            "name": GENERATOR,
            "sha256": sha256_bytes(Path(__file__).read_bytes()),
        },
        "outputs": {
            "gltf": {"file": gltf_path.name, "bytes": len(gltf_bytes), "sha256": sha256_bytes(gltf_bytes)},
            "buffer": {"file": binary_name, "bytes": len(binary_bytes), "sha256": sha256_bytes(binary_bytes)},
        },
        "coverage": {
            "surfaces": len(surface_rows),
            "collisionExported": False,
            "collisionBlockTypes": collision_types,
            "controllers": sorted(set(controllers)),
        },
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
    args = parser.parse_args()
    result = export_static_nif(
        args.input,
        args.logical_path,
        args.output,
        args.sidecar,
        strict=not args.allow_synthetic_minimal,
    )
    print("OPENNV_STATIC_NIF_GLTF " + json.dumps({
        "source": result["source"]["sha256"],
        "surfaces": result["coverage"]["surfaces"],
        "gltf": result["outputs"]["gltf"]["sha256"],
    }, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
