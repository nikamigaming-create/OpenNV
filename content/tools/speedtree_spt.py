#!/usr/bin/env python3
"""Read the owned FNV SpeedTree contract and emit its authored billboard form.

FNV ships SpeedTree ``.spt`` generator inputs rather than a renderable NIF.  The
runtime also ships a resolved billboard atlas for these assets.  This module
keeps the source contract strict (unknown sections fail closed), then emits a
crossed-card glTF using that owned atlas.  It is deliberately a content
compiler step: the cell manifest retains the SPT hash, parsed version, size,
and resolved texture path so the presentation cannot silently become generic
placeholder geometry.
"""

from __future__ import annotations

import json
import math
import struct
from dataclasses import dataclass, field
from pathlib import Path

from gltf_io import (
    GL_ARRAY_BUFFER,
    GL_ELEMENT_ARRAY_BUFFER,
    GL_FLOAT,
    GL_UNSIGNED_SHORT,
    BufferBuilder,
    atomic_write,
    pack_floats,
    sha256_bytes,
)
from export_static_nif_gltf import compiler_provenance
from runtime_configuration import ContentCompilerConfiguration


SPEEDTREE_VERSION = "__IdvSpt_02_"
GENERATOR = "OpenNV direct SpeedTree billboard exporter v1"
SPEEDTREE_MAX_STRING_BYTES = 1 << 20
SPEEDTREE_LEVEL_BEGIN_SECTION = 1016
SPEEDTREE_LEVEL_END_SECTION = 1017
SPEEDTREE_LEAF_BEGIN_SECTION = 1007
SPEEDTREE_TEXTURE_LAYER_BEGIN_SECTION = 50002
SPEEDTREE_TEXTURE_LAYER_END_SECTION = 50003
SPEEDTREE_VERSION_SECTION = 1000
SPEEDTREE_LEVEL_COUNT_SECTION = 1014
SPEEDTREE_SIZE_SECTION = 2006
SPEEDTREE_SIZE_VARIANCE_SECTION = 2007
SPEEDTREE_LEAF_QUADS_SECTION = 10002
SPEEDTREE_BILLBOARD_QUADS_SECTION = 10003
SPEEDTREE_DISCARDED_QUADS_SECTION = 10004
SPEEDTREE_MAX_QUAD_COUNT = 100000
SPEEDTREE_QUAD_FLOAT_COUNT = 8
SPEEDTREE_BILLBOARD_HALF_WIDTH_FACTOR = 0.5


class SpeedTreeParseError(ValueError):
    """Raised when an owned SPT stream cannot be decoded completely."""


@dataclass(frozen=True)
class SpeedTreeLeafMap:
    texture: str = ""
    origin: tuple[float, float, float] = (0.0, 0.0, 0.0)
    size: tuple[float, float, float] = (0.0, 0.0, 0.0)
    world_size: tuple[float, float, float] = (0.0, 0.0, 0.0)


@dataclass
class SpeedTreeContract:
    version: str = ""
    size: float = 0.0
    size_variance: float = 0.0
    num_levels: int = 0
    leaf_maps: list[SpeedTreeLeafMap] = field(default_factory=list)
    leaf_quads: list[tuple[float, ...]] = field(default_factory=list)
    billboard_quads: list[tuple[float, ...]] = field(default_factory=list)


class _Reader:
    def __init__(self, payload: bytes, source_name: str):
        self.payload = payload
        self.source_name = source_name
        self.position = 0

    def _take(self, count: int) -> bytes:
        end = self.position + count
        if end > len(self.payload):
            raise SpeedTreeParseError(
                f"{self.source_name}: truncated read at {self.position}"
            )
        result = self.payload[self.position:end]
        self.position = end
        return result

    def integer(self) -> int:
        return struct.unpack("<i", self._take(4))[0]

    def floating(self) -> float:
        return struct.unpack("<f", self._take(4))[0]

    def boolean(self) -> int:
        return self._take(1)[0]

    def string(self) -> str:
        length = self.integer()
        if length < 0 or length > SPEEDTREE_MAX_STRING_BYTES:
            raise SpeedTreeParseError(
                f"{self.source_name}: invalid string length {length} at {self.position - 4}"
            )
        return self._take(length).decode("latin-1").rstrip("\0")

    def pattern(self, value: str) -> object:
        values: list[object] = []
        for kind in value:
            if kind == "f":
                values.append(self.floating())
            elif kind == "i":
                values.append(self.integer())
            elif kind == "b":
                values.append(self.boolean())
            elif kind in {"s", "S"}:
                values.append(self.string())
            else:
                raise SpeedTreeParseError(
                    f"{self.source_name}: unsupported field pattern {value!r}"
                )
        return values[0] if len(values) == 1 else tuple(values)


# These section IDs are the typed parts of the SpeedTree stream used by the
# FNV ``__IdvSpt_02_`` assets.  Marker IDs have no payload.  Keeping the maps
# explicit is important: silently skipping an unknown field would produce a
# plausible but spatially wrong tree.
_MARKERS = {
    1001, 1002, 1003, 1004, 1005, 1008, 1009, 1010, 1011, 1012, 1015,
    7001, 8000, 8001, 9000, 9001, 9005, 9006, 10000, 10001, 11000, 11001,
    12000, 12001, 13000, 13001, 14001, 15000, 15001, 16000, 16001,
    18000, 18001, 19000, 19001, 20000, 20001, 25000, 25001,
    26000, 26001, 27000, 27001, 28000, 28001, 29000, 29001, 30000, 30001,
    40000, 40001, 40006, 40007, 40008, 50000, 50001,
    60000, 60001, 60002, 60003, 60004, 60005, 60009,
    70000, 70001,
    71000, 71005, 71011, 71014, 71015, 72000, 72004, 73000, 73001, 73003,
    74000, 74001, 75000, 75001,
}

_PATTERNS = {
    2001: "f", 2002: "b", 2003: "f", 2004: "i",
    3000: "f", 3001: "i", 3002: "f", 3003: "b", 3004: "f", 3005: "f",
    3006: "b", 3007: "f", 3008: "i", 3009: "b", 3010: "f",
    5000: "fff", 5001: "fff", 5002: "fff", 5003: "fff", 5004: "fff",
    5005: "f", 5006: "b",
    8002: "i", 8003: "f" * 13, 8004: "i", 8005: "f" * 13, 8006: "f",
    8007: "i", 8008: "i", 8009: "f" * 13,
    9002: "i", 9003: "f", 9004: "f", 9007: "i", 9008: "f", 9009: "f",
    9010: "f", 9011: "i", 9012: "f", 9013: "f", 9014: "f",
    11002: "i",
    13005: "s", 13006: "i", 13008: "i", 13009: "i", 13010: "f",
    13011: "f", 13012: "f", 13013: "f",
    14007: "i", 14008: "i",
    16014: "f",
    18002: "fff", 18003: "fff", 18004: "fff", 18005: "s",
    19002: "i",
    20002: "s", 20003: "b", 20004: "b", 20005: "f" * 8,
    22000: "b", 23002: "f", 23003: "f",
    25002: "f", 25003: "i", 25004: "i", 25005: "i", 25006: "i", 25007: "b",
    28002: "b", 28003: "f", 28004: "i",
    30002: "f", 30003: "f", 30004: "f", 30005: "f", 30006: "f",
    30007: "f", 30008: "f", 30009: "f",
    50004: "f", 50005: "f", 50006: "b", 50007: "b", 50008: "f", 50009: "b",
    50010: "f", 50011: "b", 50012: "b", 50013: "f", 50014: "f",
    50015: "f", 50016: "f", 50017: "f", 50018: "b",
    60006: "s", 60007: "i", 60008: "i",
    70002: "s", 70003: "s", 70004: "s", 70005: "s", 70006: "s",
    70007: "s", 70008: "s",
    75002: "f", 75003: "f", 75004: "b", 75005: "f",
}

_LEVEL_SPLINES = {6000, 6001, 6002, 6003, 6004, 6005, 6006, 6007, 6017}
_LEVEL_FIELDS = {
    6008: "i", 6009: "i", 6010: "f", 6011: "f", 6012: "f", 6013: "f",
    6014: "f", 6015: "b", 6016: "b",
}
_LEAF_FIELDS = {
    4000: "b", 4001: "fff", 4002: "f", 4003: "s", 4004: "fff",
    4005: "fff", 4006: "fff", 4007: "f",
}
_TEX_LAYER_FIELDS = {
    50004: "f", 50005: "f", 50006: "b", 50007: "b", 50008: "f",
    50009: "b", 50010: "f", 50011: "b", 50012: "b", 50013: "f",
    50014: "f", 50015: "f", 50016: "f", 50017: "f", 50018: "b",
}
_PAYLOADLESS_SECTIONS = frozenset({14000, 7000, 71002, 26002, 26003})
_ADDITIONAL_PATTERNS = {
        1006: "i",
        2000: "s",
        2005: "i",
        14002: "s",
        14003: "f",
        14004: "f",
        14005: "f",
        14006: "f",
        71001: "i",
        71003: "s",
        71004: "i",
        71006: "fff",
        71007: "fff",
        71008: "fff",
        71009: "fff",
        71010: "ff",
        71012: "i",
        71013: "i",
        15002: "b",
        15003: "f",
        16002: "f",
        16003: "i",
        16004: "f",
        16005: "f",
        16006: "f",
        16007: "f",
        16008: "f",
        16009: "f",
        16010: "f",
        16011: "f",
        16012: "f",
        40002: "i",
        40003: "f",
        40004: "f",
        40005: "f",
        12002: "ffff",
        12003: "fffff",
        12004: "ffffff",
        13002: "i",
        13003: "i",
        13004: "i",
        13007: "b",
        16013: "f",
        21000: "f",
        21001: "f",
        27002: "b",
        27003: "f",
        27005: "f",
        27006: "f",
        29002: "f",
        73004: "b",
        73005: "f",
        73006: "f",
        74002: "f",
}
_PATTERNS.update(_ADDITIONAL_PATTERNS)


def parse_speedtree(payload: bytes, source_name: str) -> SpeedTreeContract:
    reader = _Reader(payload, source_name)
    contract = SpeedTreeContract()
    current_leaf: dict[str, object] | None = None
    current_texture_layer = False
    level_count = 0
    while reader.position < len(payload):
        if len(payload) - reader.position < 4:
            raise SpeedTreeParseError(
                f"{source_name}: trailing bytes at {reader.position}"
            )
        section = reader.integer()
        if section in _MARKERS:
            if section == SPEEDTREE_LEVEL_BEGIN_SECTION:
                level_count += 1
            elif section == SPEEDTREE_LEAF_BEGIN_SECTION:
                current_leaf = {}
                contract.leaf_maps.append(SpeedTreeLeafMap())
            elif section == SPEEDTREE_TEXTURE_LAYER_BEGIN_SECTION:
                current_texture_layer = True
            elif section == SPEEDTREE_TEXTURE_LAYER_END_SECTION:
                current_texture_layer = False
            continue
        if section == SPEEDTREE_LEVEL_BEGIN_SECTION:
            level_count += 1
        elif section == SPEEDTREE_LEVEL_END_SECTION:
            continue
        elif section == SPEEDTREE_LEAF_BEGIN_SECTION:
            current_leaf = {}
            contract.leaf_maps.append(SpeedTreeLeafMap())
        elif section in _PAYLOADLESS_SECTIONS:
            continue
        elif section == SPEEDTREE_VERSION_SECTION:
            contract.version = reader.string()
        elif section == SPEEDTREE_LEVEL_COUNT_SECTION:
            contract.num_levels = reader.integer()
        elif section == SPEEDTREE_SIZE_SECTION:
            contract.size = reader.floating()
        elif section == SPEEDTREE_SIZE_VARIANCE_SECTION:
            contract.size_variance = reader.floating()
        elif section in _LEVEL_SPLINES:
            reader.string()
        elif section in _LEVEL_FIELDS:
            reader.pattern(_LEVEL_FIELDS[section])
        elif section in _LEAF_FIELDS:
            if current_leaf is None:
                raise SpeedTreeParseError(
                    f"{source_name}: leaf field {section} has no leaf map"
                )
            value = reader.pattern(_LEAF_FIELDS[section])
            current_leaf[str(section)] = value
            index = len(contract.leaf_maps) - 1
            values = contract.leaf_maps[index]
            contract.leaf_maps[index] = SpeedTreeLeafMap(
                texture=str(current_leaf.get("4003", values.texture)),
                origin=tuple(current_leaf.get("4004", values.origin)),
                size=tuple(current_leaf.get("4005", values.size)),
                world_size=tuple(current_leaf.get("4006", values.world_size)),
            )
        elif section in _TEX_LAYER_FIELDS:
            if not current_texture_layer:
                raise SpeedTreeParseError(
                    f"{source_name}: texture-layer field {section} has no layer"
                )
            reader.pattern(_TEX_LAYER_FIELDS[section])
        elif section in {
            SPEEDTREE_LEAF_QUADS_SECTION,
            SPEEDTREE_BILLBOARD_QUADS_SECTION,
            SPEEDTREE_DISCARDED_QUADS_SECTION,
        }:
            count = reader.integer()
            if count < 0 or count > SPEEDTREE_MAX_QUAD_COUNT:
                raise SpeedTreeParseError(f"{source_name}: invalid quad count {count}")
            target = (
                contract.leaf_quads if section == SPEEDTREE_LEAF_QUADS_SECTION
                else contract.billboard_quads if section == SPEEDTREE_BILLBOARD_QUADS_SECTION
                else []
            )
            for _ in range(count):
                quad = tuple(
                    float(reader.floating())
                    for _ in range(SPEEDTREE_QUAD_FLOAT_COUNT)
                )
                if section == SPEEDTREE_DISCARDED_QUADS_SECTION:
                    continue
                target.append(quad)
        elif section in _PATTERNS:
            reader.pattern(_PATTERNS[section])
        else:
            raise SpeedTreeParseError(
                f"{source_name}: unknown section {section} at {reader.position - 4}"
            )
    if contract.version != SPEEDTREE_VERSION:
        raise SpeedTreeParseError(
            f"{source_name}: unsupported SpeedTree version {contract.version!r}"
        )
    if contract.num_levels and level_count != contract.num_levels:
        raise SpeedTreeParseError(
            f"{source_name}: declared {contract.num_levels} levels, parsed {level_count}"
        )
    if not math.isfinite(contract.size) or contract.size <= 0.0:
        raise SpeedTreeParseError(f"{source_name}: invalid authored tree size {contract.size}")
    return contract


def _godot_point(x: float, y: float, z: float) -> tuple[float, float, float]:
    return (x, z, -y)


def _billboard_geometry(size: float) -> tuple[list[tuple[float, float, float]], list[tuple[float, float]], list[tuple[float, float, float]], list[tuple[int, int, int]]]:
    half_width = size * SPEEDTREE_BILLBOARD_HALF_WIDTH_FACTOR
    height = size
    positions: list[tuple[float, float, float]] = []
    uvs: list[tuple[float, float]] = []
    normals: list[tuple[float, float, float]] = []
    triangles: list[tuple[int, int, int]] = []
    cards = (
        (
            (
                _godot_point(-half_width, 0.0, 0.0),
                _godot_point(half_width, 0.0, 0.0),
                _godot_point(half_width, 0.0, height),
                _godot_point(-half_width, 0.0, height),
            ),
            (0.0, 0.0, -1.0),
        ),
        (
            (
                _godot_point(0.0, -half_width, 0.0),
                _godot_point(0.0, half_width, 0.0),
                _godot_point(0.0, half_width, height),
                _godot_point(0.0, -half_width, height),
            ),
            (1.0, 0.0, 0.0),
        ),
    )
    card_uvs = ((0.0, 1.0), (1.0, 1.0), (1.0, 0.0), (0.0, 0.0))
    for card, normal in cards:
        base = len(positions)
        positions.extend(card)
        uvs.extend(card_uvs)
        normals.extend([normal] * 4)
        triangles.extend(((base, base + 1, base + 2), (base, base + 2, base + 3)))
    return positions, uvs, normals, triangles


def export_speedtree_spt(
    source: Path,
    logical_path: str,
    gltf_path: Path,
    sidecar_path: Path,
    compiler: ContentCompilerConfiguration,
) -> dict[str, object]:
    source_bytes = source.read_bytes()
    source_hash = sha256_bytes(source_bytes)
    contract = parse_speedtree(source_bytes, logical_path)
    billboard_texture = compiler.speed_tree.billboard_texture
    billboard_alpha_cutoff = compiler.speed_tree.billboard_alpha_cutoff
    positions, uvs, normals, triangles = _billboard_geometry(contract.size)
    builder = BufferBuilder()
    attributes = {
        "POSITION": builder.add(
            pack_floats(positions),
            component_type=GL_FLOAT,
            count=len(positions),
            value_type="VEC3",
            target=GL_ARRAY_BUFFER,
            minimum=[min(row[index] for row in positions) for index in range(3)],
            maximum=[max(row[index] for row in positions) for index in range(3)],
        ),
        "NORMAL": builder.add(
            pack_floats(normals),
            component_type=GL_FLOAT,
            count=len(normals),
            value_type="VEC3",
            target=GL_ARRAY_BUFFER,
        ),
        "TEXCOORD_0": builder.add(
            pack_floats(uvs),
            component_type=GL_FLOAT,
            count=len(uvs),
            value_type="VEC2",
            target=GL_ARRAY_BUFFER,
        ),
    }
    index_values = [value for triangle in triangles for value in triangle]
    index_accessor = builder.add(
        struct.pack(f"<{len(index_values)}H", *index_values),
        component_type=GL_UNSIGNED_SHORT,
        count=len(index_values),
        value_type="SCALAR",
        target=GL_ELEMENT_ARRAY_BUFFER,
    )
    gltf_material = {
        "name": "SpeedTree authored billboard",
        "doubleSided": True,
        "alphaMode": "MASK",
        "alphaCutoff": billboard_alpha_cutoff,
        "pbrMetallicRoughness": {
            "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
            "metallicFactor": 0.0,
            "roughnessFactor": 1.0,
        },
    }
    primitives = [{"attributes": attributes, "indices": index_accessor, "material": 0}]
    binary_name = gltf_path.with_suffix(".bin").name
    gltf = {
        "asset": {"version": "2.0", "generator": GENERATOR},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": Path(logical_path).stem, "mesh": 0}],
        "meshes": [{"name": Path(logical_path).stem, "primitives": primitives}],
        "materials": [gltf_material],
        "buffers": [{"uri": binary_name, "byteLength": len(builder.data)}],
        "bufferViews": builder.views,
        "accessors": builder.accessors,
        "extras": {
            "openNvSchema": "opennv-static-speedtree-spt/v1",
            "sourceSha256": source_hash,
            "speedTreeVersion": contract.version,
            "authoredSize": contract.size,
        },
    }
    gltf_bytes = (json.dumps(gltf, indent=2, sort_keys=True) + "\n").encode()
    binary_bytes = bytes(builder.data)
    atomic_write(gltf_path.with_suffix(".bin"), binary_bytes)
    atomic_write(gltf_path, gltf_bytes)
    source_surface = {
        "stableId": sha256_bytes(f"{source_hash}:speedtree-billboard".encode())[:compiler.stable_id_hex_characters],
        "sourceBlockIndex": 0,
        "name": "SpeedTree authored billboard",
        "vertices": len(positions),
        "triangles": len(triangles),
        "attributes": sorted(attributes),
        "propertyTypes": ["BSShaderProperty"],
        "textures": [billboard_texture],
        "material": {
            "baseColor": [1.0, 1.0, 1.0],
            "alpha": 1.0,
            "glossiness": 0.0,
            "specular": [0.0, 0.0, 0.0],
            "shaderFlags1": 0,
            "shaderFlags2": 0,
            "shaderFlags1Enabled": ["sf_alpha_texture"],
            "shaderFlags2Enabled": [],
            "stencilDrawMode": 3,
            "alphaContract": {
                "mode": "MASK",
                "cutoff": billboard_alpha_cutoff,
                "flags": None,
                "blendEnabled": False,
                "testEnabled": True,
                "sourceBlendMode": None,
                "destinationBlendMode": None,
                "testFunction": "GREATER_EQUAL",
                "noSorter": False,
                "source": "SpeedTree-authored-billboard-alpha",
            },
            "vertexColorMode": "none",
            "diffuseTexturePresent": True,
        },
        "transformBakedToRoot": True,
        "skinSourcePoseBaked": False,
        "tangentSource": "absent",
    }
    sidecar = {
        "schema": "opennv-static-nif-gltf/v2",
        "status": "geometry-only",
        "source": {
            "logicalPath": logical_path.replace("/", "\\").lower(),
            "bytes": len(source_bytes),
            "sha256": source_hash,
            "speedTreeVersion": contract.version,
        },
        "compiler": compiler_provenance(),
        "outputs": {
            "gltf": {
                "file": gltf_path.name,
                "bytes": len(gltf_bytes),
                "sha256": sha256_bytes(gltf_bytes),
            },
            "buffer": {
                "file": binary_name,
                "bytes": len(binary_bytes),
                "sha256": sha256_bytes(binary_bytes),
            },
        },
        "coverage": {
            "surfaces": 1,
            "sourcePoseBakedSkinSurfaces": 0,
            "collisionExported": False,
            "collisionBlockTypes": [],
            "collisionUnsupportedReason": "SpeedTree SPT has no authored collision block",
            "collisionBodies": [],
            "dynamicPhysicsExported": False,
            "dynamicPhysicsUnsupportedReasons": ["speedtree-spt-no-dynamic-physics"],
            "dynamicPhysicsBodies": [],
            "controllers": [],
            "excludedEditorMarkerSurfaces": [],
            "excludedNonPresentationSurfaces": [],
        },
        "speedTree": {
            "sourceFormat": "owned-spt-generator-input",
            "version": contract.version,
            "authoredSize": contract.size,
            "authoredSizeVariance": contract.size_variance,
            "declaredLevels": contract.num_levels,
            "parsedLeafMaps": len(contract.leaf_maps),
            "parsedLeafQuads": len(contract.leaf_quads),
            "parsedBillboardQuads": len(contract.billboard_quads),
            "presentation": "owned-runtime-billboard-atlas-crossed-cards",
            "billboardTexture": billboard_texture,
            "billboardAlphaCutoff": billboard_alpha_cutoff,
        },
        "attachmentMarkers": [],
        "surfaces": [source_surface],
    }
    atomic_write(sidecar_path, (json.dumps(sidecar, indent=2, sort_keys=True) + "\n").encode())
    return sidecar
