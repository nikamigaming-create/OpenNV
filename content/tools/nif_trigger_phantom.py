"""Strict collision-only admission for owned Fallout trigger NIFs.

This is deliberately separate from the ordinary static NIF decoder.  It only
accepts a Bethesda stream-21 graph whose sole triangle subtree is an editor
marker and whose collision root is a simple shape phantom on FOL_TRIGGER.
"""

from __future__ import annotations

import hashlib
import json
import re
import struct
from dataclasses import dataclass
from functools import cache
from pathlib import Path


SCHEMA = "opennv-nif-trigger-phantom-decoder-contract/v1"
EVIDENCE_SCHEMA = "opennv-nif-trigger-phantom-evidence/v1"
RECIPE_PATH = (
    Path(__file__).resolve().parents[1]
    / "recipes"
    / ("fnv-trigger-" + "phantom-nif-v1.json")
)
HEX_RADIX = 16
SHA256_HEX_CHARACTERS = hashlib.sha256().digest_size * 2
UINT16 = struct.Struct("<H")
UINT32 = struct.Struct("<I")
INT32 = struct.Struct("<i")
FLOAT32 = struct.Struct("<f")
VECTOR3 = struct.Struct("<3f")
MATRIX33 = struct.Struct("<9f")
MATRIX44 = struct.Struct("<16f")
COLLISION_OBJECT = struct.Struct("<iHi")
HAVOK_FILTER = struct.Struct("<BBH")
BOX_SHAPE = struct.Struct("<If8s3ff")
TRANSFORM_SHAPE_PREFIX = struct.Struct("<IIf8s")
WORLD_OBJECT_PROPERTY = struct.Struct("<III")
NODE_FLAG_BYTES = UINT16.size
NIF_TRANSFORM_BYTES = VECTOR3.size + MATRIX33.size + FLOAT32.size
PHANTOM_UNUSED_BYTES = 8
MATRIX_AFFINE_INDICES = (0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14)


@dataclass(frozen=True)
class TriggerPhantomDecoderContract:
    document: dict[str, object]
    canonical_sha256: str
    version: int
    endian: int
    user_version: int
    user_version_2: int
    export_info_string_count: int
    footer_bytes: int
    graph: dict[str, object]
    disposition: dict[str, str]


@dataclass(frozen=True)
class BlockEntry:
    index: int
    type_name: str
    offset: int
    size: int


@dataclass(frozen=True)
class TriggerPhantomDecodeResult:
    contract: TriggerPhantomDecoderContract
    source_sha256: str
    block_types: tuple[str, ...]
    collision: dict[str, object]
    presentation: dict[str, object]

    def evidence(self) -> dict[str, object]:
        return {
            "schema": EVIDENCE_SCHEMA,
            "status": "owned-trigger-phantom-admitted-without-pyffi",
            "contract": {
                "schema": str(self.contract.document["schema"]),
                "id": str(self.contract.document["id"]),
                "status": str(self.contract.document["status"]),
                "file": RECIPE_PATH.name,
                "canonicalSha256": self.contract.canonical_sha256,
            },
            "sourceSha256": self.source_sha256,
            "formatMatched": True,
            "runtimeMaterializationAdmission": True,
            "sourceBytesModified": False,
            "presentation": self.presentation,
            "collision": self.collision,
        }


def _canonical_sha256(document: object) -> str:
    payload = json.dumps(
        document,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _require_object(parent: dict[str, object], field: str) -> dict[str, object]:
    value = parent.get(field)
    if not isinstance(value, dict):
        raise ValueError(f"Trigger phantom decoder contract lacks {field}")
    return value


def _require_integer(parent: dict[str, object], field: str) -> int:
    value = parent.get(field)
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        raise ValueError(f"Trigger phantom decoder integer is invalid: {field}")
    return value


@cache
def load_trigger_phantom_decoder_contract() -> TriggerPhantomDecoderContract:
    document = json.loads(RECIPE_PATH.read_text(encoding="utf-8"))
    if not isinstance(document, dict) or set(document) != {
        "schema",
        "id",
        "status",
        "provenance",
        "format",
        "graph",
        "disposition",
    }:
        raise ValueError("Trigger phantom decoder contract fields are invalid")
    if document.get("schema") != SCHEMA:
        raise ValueError("Trigger phantom decoder contract schema differs")
    if any(
        not isinstance(document.get(field), str) or not str(document[field]).strip()
        for field in ("id", "status")
    ):
        raise ValueError("Trigger phantom decoder identity is invalid")
    provenance = _require_object(document, "provenance")
    if set(provenance) != {"classification", "status", "source", "evidence"} or any(
        not isinstance(provenance.get(field), str) or not str(provenance[field]).strip()
        for field in provenance
    ):
        raise ValueError("Trigger phantom decoder provenance is invalid")
    format_contract = _require_object(document, "format")
    if set(format_contract) != {
        "versionHex",
        "endian",
        "userVersion",
        "userVersion2",
        "exportInfoStringCount",
        "footerBytes",
    }:
        raise ValueError("Trigger phantom decoder format fields are invalid")
    version_hex = format_contract.get("versionHex")
    if not isinstance(version_hex, str) or re.fullmatch(r"0x[0-9A-Fa-f]{8}", version_hex) is None:
        raise ValueError("Trigger phantom decoder version is invalid")
    graph = _require_object(document, "graph")
    expected_graph_fields = {
        "rootBlockType",
        "collisionObjectBlockType",
        "phantomBlockType",
        "transformShapeBlockType",
        "boxShapeBlockType",
        "editorNodeBlockType",
        "editorGeometryBlockTypes",
        "editorMarkerNamePrefix",
        "collisionLayer",
        "broadPhaseType",
        "requiredBlockTypeCounts",
        "requiredBlockSizes",
    }
    if set(graph) != expected_graph_fields:
        raise ValueError("Trigger phantom decoder graph fields are invalid")
    type_counts = _require_object(graph, "requiredBlockTypeCounts")
    block_sizes = _require_object(graph, "requiredBlockSizes")
    if (
        not type_counts
        or not block_sizes
        or any(
            not isinstance(name, str)
            or not name
            or not isinstance(value, int)
            or isinstance(value, bool)
            or value <= 0
            for rows in (type_counts, block_sizes)
            for name, value in rows.items()
        )
    ):
        raise ValueError("Trigger phantom decoder block constraints are invalid")
    geometry_types = graph.get("editorGeometryBlockTypes")
    if (
        not isinstance(geometry_types, list)
        or not geometry_types
        or any(not isinstance(value, str) or not value for value in geometry_types)
        or len(set(geometry_types)) != len(geometry_types)
    ):
        raise ValueError("Trigger phantom decoder editor geometry types are invalid")
    for field in (
        "rootBlockType",
        "collisionObjectBlockType",
        "phantomBlockType",
        "transformShapeBlockType",
        "boxShapeBlockType",
        "editorNodeBlockType",
        "editorMarkerNamePrefix",
    ):
        if not isinstance(graph.get(field), str) or not str(graph[field]):
            raise ValueError(f"Trigger phantom decoder graph identity is invalid: {field}")
    _require_integer(graph, "collisionLayer")
    _require_integer(graph, "broadPhaseType")
    disposition = _require_object(document, "disposition")
    if set(disposition) != {"presentation", "collision", "coordinateSpace"} or any(
        not isinstance(disposition.get(field), str) or not str(disposition[field])
        for field in disposition
    ):
        raise ValueError("Trigger phantom decoder disposition is invalid")
    canonical_sha256 = _canonical_sha256(document)
    if len(canonical_sha256) != SHA256_HEX_CHARACTERS:
        raise ValueError("Trigger phantom decoder identity hash is invalid")
    return TriggerPhantomDecoderContract(
        document=document,
        canonical_sha256=canonical_sha256,
        version=int(version_hex[2:], HEX_RADIX),
        endian=_require_integer(format_contract, "endian"),
        user_version=_require_integer(format_contract, "userVersion"),
        user_version_2=_require_integer(format_contract, "userVersion2"),
        export_info_string_count=_require_integer(
            format_contract,
            "exportInfoStringCount",
        ),
        footer_bytes=_require_integer(format_contract, "footerBytes"),
        graph=graph,
        disposition={field: str(disposition[field]) for field in disposition},
    )


def _require_range(payload: bytes, offset: int, size: int, label: str) -> None:
    if offset < 0 or size < 0 or offset + size > len(payload):
        raise ValueError(f"Trigger phantom NIF {label} exceeds its payload")


def _unpack(payload: bytes, offset: int, shape: struct.Struct, label: str) -> tuple[object, ...]:
    _require_range(payload, offset, shape.size, label)
    return shape.unpack_from(payload, offset)


def _directory(
    payload: bytes,
    contract: TriggerPhantomDecoderContract,
) -> tuple[tuple[BlockEntry, ...], tuple[str, ...], tuple[int, ...]]:
    newline = payload.find(b"\n")
    if newline < 0:
        raise ValueError("Trigger phantom NIF has no header line")
    offset = newline + 1
    (version,) = _unpack(payload, offset, UINT32, "version")
    offset += UINT32.size
    _require_range(payload, offset, 1, "endian")
    endian = payload[offset]
    offset += 1
    user_version, block_count, user_version_2 = _unpack(
        payload,
        offset,
        struct.Struct("<III"),
        "Bethesda header",
    )
    offset += struct.calcsize("<III")
    observed_identity = (version, endian, user_version, user_version_2)
    expected_identity = (
        contract.version,
        contract.endian,
        contract.user_version,
        contract.user_version_2,
    )
    if observed_identity != expected_identity:
        raise ValueError(
            "Trigger phantom NIF format identity differs: "
            f"expected={expected_identity} actual={observed_identity}"
        )
    for _ in range(contract.export_info_string_count):
        _require_range(payload, offset, 1, "export-info length")
        length = payload[offset]
        offset += 1
        _require_range(payload, offset, length, "export-info value")
        offset += length
    (type_count,) = _unpack(payload, offset, UINT16, "block-type count")
    offset += UINT16.size
    block_types: list[str] = []
    for _ in range(int(type_count)):
        (length,) = _unpack(payload, offset, UINT32, "block-type length")
        offset += UINT32.size
        _require_range(payload, offset, int(length), "block-type value")
        try:
            block_types.append(payload[offset : offset + int(length)].decode("ascii"))
        except UnicodeDecodeError as error:
            raise ValueError("Trigger phantom NIF block type is not ASCII") from error
        offset += int(length)
    type_indices_shape = struct.Struct(f"<{block_count}H")
    type_indices = tuple(
        int(value)
        for value in _unpack(payload, offset, type_indices_shape, "block-type indices")
    )
    offset += type_indices_shape.size
    block_sizes_shape = struct.Struct(f"<{block_count}I")
    block_sizes = tuple(
        int(value)
        for value in _unpack(payload, offset, block_sizes_shape, "block sizes")
    )
    offset += block_sizes_shape.size
    string_count, _maximum_string = _unpack(
        payload,
        offset,
        struct.Struct("<II"),
        "string-table header",
    )
    offset += struct.calcsize("<II")
    strings: list[str] = []
    for _ in range(int(string_count)):
        (length,) = _unpack(payload, offset, UINT32, "string length")
        offset += UINT32.size
        _require_range(payload, offset, int(length), "string value")
        try:
            strings.append(payload[offset : offset + int(length)].decode("utf-8"))
        except UnicodeDecodeError as error:
            raise ValueError("Trigger phantom NIF string is not UTF-8") from error
        offset += int(length)
    (group_count,) = _unpack(payload, offset, UINT32, "group count")
    offset += UINT32.size
    group_shape = struct.Struct(f"<{group_count}I")
    group_sizes = tuple(
        int(value)
        for value in _unpack(payload, offset, group_shape, "group sizes")
    ) if group_count else ()
    offset += group_shape.size
    if group_sizes:
        raise ValueError("Trigger phantom NIF unexpectedly uses block groups")
    entries: list[BlockEntry] = []
    for index, (type_index, block_size) in enumerate(zip(type_indices, block_sizes)):
        if type_index >= len(block_types):
            raise ValueError("Trigger phantom NIF block type index is invalid")
        _require_range(payload, offset, block_size, f"block {index}")
        entries.append(BlockEntry(index, block_types[type_index], offset, block_size))
        offset += block_size
    if len(payload) - offset != contract.footer_bytes:
        raise ValueError("Trigger phantom NIF footer size differs")
    root_count, root_index = _unpack(
        payload,
        offset,
        struct.Struct("<Ii"),
        "footer",
    )
    if root_count != 1 or root_index < 0 or root_index >= len(entries):
        raise ValueError("Trigger phantom NIF has no unique root")
    return tuple(entries), tuple(strings), (int(root_index),)


def _entry(entries: tuple[BlockEntry, ...], index: int, expected_type: str) -> BlockEntry:
    if index < 0 or index >= len(entries) or entries[index].type_name != expected_type:
        actual = entries[index].type_name if 0 <= index < len(entries) else None
        raise ValueError(
            "Trigger phantom NIF graph link differs: "
            f"index={index} expected={expected_type} actual={actual}"
        )
    return entries[index]


def _string(strings: tuple[str, ...], index: int, label: str) -> str:
    if index < 0 or index >= len(strings):
        raise ValueError(f"Trigger phantom NIF {label} string index is invalid")
    return strings[index]


def _parse_av_object(
    payload: bytes,
    entry: BlockEntry,
    strings: tuple[str, ...],
) -> tuple[int, str, int]:
    offset = entry.offset
    block_end = entry.offset + entry.size
    (name_index,) = _unpack(payload, offset, INT32, "AV-object name")
    offset += INT32.size
    (extra_count,) = _unpack(payload, offset, UINT32, "AV-object extra-data count")
    offset += UINT32.size
    extra_shape = struct.Struct(f"<{extra_count}i")
    _unpack(payload, offset, extra_shape, "AV-object extra-data links")
    offset += extra_shape.size
    _unpack(payload, offset, INT32, "AV-object controller")
    offset += INT32.size
    _unpack(payload, offset, UINT16, "AV-object flags")
    offset += NODE_FLAG_BYTES
    _require_range(payload, offset, NIF_TRANSFORM_BYTES, "AV-object transform")
    offset += NIF_TRANSFORM_BYTES
    (property_count,) = _unpack(payload, offset, UINT32, "AV-object property count")
    offset += UINT32.size
    property_shape = struct.Struct(f"<{property_count}i")
    _unpack(payload, offset, property_shape, "AV-object properties")
    offset += property_shape.size
    (collision_index,) = _unpack(payload, offset, INT32, "AV-object collision link")
    offset += INT32.size
    if offset > block_end:
        raise ValueError("Trigger phantom NIF AV-object prefix exceeds its block")
    return offset, _string(strings, int(name_index), "AV-object name"), int(collision_index)


def _parse_node(
    payload: bytes,
    entry: BlockEntry,
    strings: tuple[str, ...],
) -> tuple[str, int, tuple[int, ...]]:
    offset, name, collision_index = _parse_av_object(payload, entry, strings)
    (child_count,) = _unpack(payload, offset, UINT32, "node child count")
    offset += UINT32.size
    child_shape = struct.Struct(f"<{child_count}i")
    children = tuple(
        int(value) for value in _unpack(payload, offset, child_shape, "node children")
    )
    offset += child_shape.size
    (effect_count,) = _unpack(payload, offset, UINT32, "node effect count")
    offset += UINT32.size
    effect_shape = struct.Struct(f"<{effect_count}i")
    _unpack(payload, offset, effect_shape, "node effects")
    offset += effect_shape.size
    if offset != entry.offset + entry.size:
        raise ValueError("Trigger phantom NIF node block was not consumed exactly")
    return name, collision_index, children


def _affine_matrix(values: tuple[float, ...]) -> list[float]:
    return [float(values[index]) for index in MATRIX_AFFINE_INDICES]


def decode_trigger_phantom_nif(payload: bytes) -> TriggerPhantomDecodeResult:
    """Decode one graph-bounded, collision-only stream-21 trigger NIF."""

    contract = load_trigger_phantom_decoder_contract()
    graph = contract.graph
    entries, strings, roots = _directory(payload, contract)
    observed_counts: dict[str, int] = {}
    for entry in entries:
        observed_counts[entry.type_name] = observed_counts.get(entry.type_name, 0) + 1
    for type_name, expected_count in _require_object(
        graph,
        "requiredBlockTypeCounts",
    ).items():
        if observed_counts.get(type_name, 0) != expected_count:
            raise ValueError(
                "Trigger phantom NIF block count differs: "
                f"type={type_name} expected={expected_count} "
                f"actual={observed_counts.get(type_name, 0)}"
            )
    for type_name, expected_size in _require_object(graph, "requiredBlockSizes").items():
        matches = [entry for entry in entries if entry.type_name == type_name]
        if len(matches) != 1 or matches[0].size != expected_size:
            raise ValueError(
                "Trigger phantom NIF block size differs: "
                f"type={type_name} expected={expected_size}"
            )

    root = _entry(entries, roots[0], str(graph["rootBlockType"]))
    root_name, collision_index, root_children = _parse_node(payload, root, strings)
    if len(root_children) != 1:
        raise ValueError("Trigger phantom NIF root does not have one editor subtree")
    editor_node = _entry(entries, root_children[0], str(graph["editorNodeBlockType"]))
    editor_name, editor_collision, editor_children = _parse_node(payload, editor_node, strings)
    marker_prefix = str(graph["editorMarkerNamePrefix"])
    if (
        not editor_name.casefold().startswith(marker_prefix.casefold())
        or editor_collision != -1
        or len(editor_children) != 1
    ):
        raise ValueError("Trigger phantom NIF editor-marker subtree differs")
    geometry_types = tuple(str(value) for value in graph["editorGeometryBlockTypes"])
    geometry = entries[editor_children[0]]
    if geometry.type_name not in geometry_types:
        raise ValueError("Trigger phantom NIF editor child is not admitted geometry")
    geometry_offset, geometry_name, geometry_collision = _parse_av_object(
        payload,
        geometry,
        strings,
    )
    (geometry_data_index,) = _unpack(
        payload,
        geometry_offset,
        INT32,
        "editor geometry data link",
    )
    if (
        not geometry_name.casefold().startswith(marker_prefix.casefold())
        or geometry_collision != -1
        or geometry_data_index < 0
        or entries[int(geometry_data_index)].type_name
        not in {"NiTriShapeData", "NiTriStripsData"}
    ):
        raise ValueError("Trigger phantom NIF editor geometry identity differs")
    all_geometry_indices = {
        entry.index for entry in entries if entry.type_name in geometry_types
    }
    if all_geometry_indices != {geometry.index}:
        raise ValueError("Trigger phantom NIF has presentation geometry outside EditorMarker")

    collision_object = _entry(
        entries,
        collision_index,
        str(graph["collisionObjectBlockType"]),
    )
    target_index, collision_flags, phantom_index = _unpack(
        payload,
        collision_object.offset,
        COLLISION_OBJECT,
        "collision object",
    )
    if target_index != root.index or collision_flags != 1:
        raise ValueError("Trigger phantom collision object target or flags differ")
    phantom = _entry(entries, int(phantom_index), str(graph["phantomBlockType"]))
    (transform_shape_index,) = _unpack(payload, phantom.offset, INT32, "phantom shape")
    layer, filter_flags, filter_group = _unpack(
        payload,
        phantom.offset + INT32.size,
        HAVOK_FILTER,
        "phantom Havok filter",
    )
    broad_phase_offset = phantom.offset + INT32.size + HAVOK_FILTER.size + UINT32.size
    _require_range(payload, broad_phase_offset, 1, "phantom broad-phase type")
    broad_phase_type = payload[broad_phase_offset]
    property_offset = broad_phase_offset + 1 + 3
    property_data, property_size, property_capacity_flags = _unpack(
        payload,
        property_offset,
        WORLD_OBJECT_PROPERTY,
        "phantom world-object property",
    )
    phantom_matrix_offset = (
        property_offset + WORLD_OBJECT_PROPERTY.size + PHANTOM_UNUSED_BYTES
    )
    phantom_matrix = tuple(
        float(value)
        for value in _unpack(
            payload,
            phantom_matrix_offset,
            MATRIX44,
            "phantom transform",
        )
    )
    if (
        layer != _require_integer(graph, "collisionLayer")
        or broad_phase_type != _require_integer(graph, "broadPhaseType")
    ):
        raise ValueError("Trigger phantom NIF collision layer or broad phase differs")

    transform_shape = _entry(
        entries,
        int(transform_shape_index),
        str(graph["transformShapeBlockType"]),
    )
    box_index, transform_material, transform_radius, _unused = _unpack(
        payload,
        transform_shape.offset,
        TRANSFORM_SHAPE_PREFIX,
        "transform shape prefix",
    )
    transform_matrix = tuple(
        float(value)
        for value in _unpack(
            payload,
            transform_shape.offset + TRANSFORM_SHAPE_PREFIX.size,
            MATRIX44,
            "shape transform",
        )
    )
    box = _entry(entries, int(box_index), str(graph["boxShapeBlockType"]))
    box_material, box_radius, _unused_box, dim_x, dim_y, dim_z, unused_w = _unpack(
        payload,
        box.offset,
        BOX_SHAPE,
        "box shape",
    )
    half_extents = (float(dim_x), float(dim_y), float(dim_z))
    if any(value <= 0.0 for value in half_extents):
        raise ValueError("Trigger phantom NIF box half extents are invalid")

    source_sha256 = hashlib.sha256(payload).hexdigest()
    collision = {
        "source": "embedded-in-model-member",
        "semantics": contract.disposition["collision"],
        "coordinateSpace": contract.disposition["coordinateSpace"],
        "blockTypes": sorted(
            entry.type_name for entry in entries if entry.type_name.startswith("bhk")
        ),
        "blockCount": sum(entry.type_name.startswith("bhk") for entry in entries),
        "graph": {
            "collisionObjectBlock": collision_object.index,
            "phantomBlock": phantom.index,
            "transformShapeBlock": transform_shape.index,
            "boxShapeBlock": box.index,
        },
        "filter": {
            "layer": int(layer),
            "layerName": "FOL_TRIGGER",
            "flags": int(filter_flags),
            "group": int(filter_group),
        },
        "broadPhase": {
            "type": int(broad_phase_type),
            "typeName": "BROAD_PHASE_PHANTOM",
        },
        "worldObjectProperty": {
            "data": int(property_data),
            "size": int(property_size),
            "capacityAndFlags": int(property_capacity_flags),
        },
        "phantomAffineMatrixColumnMajor": _affine_matrix(phantom_matrix),
        "shape": {
            "type": "box-half-extents",
            "halfExtents": list(half_extents),
            "material": int(box_material),
            "radius": float(box_radius),
            "unusedFourthExtent": float(unused_w),
            "transformMaterial": int(transform_material),
            "transformRadius": float(transform_radius),
            "affineMatrixColumnMajor": _affine_matrix(transform_matrix),
        },
    }
    presentation = {
        "disposition": contract.disposition["presentation"],
        "rootBlock": root.index,
        "rootName": root_name,
        "presentableSurfaceCount": 0,
        "editorMarkerSurfaceCount": 1,
        "editorMarkerNode": {
            "block": editor_node.index,
            "name": editor_name,
        },
        "editorMarkerGeometry": {
            "block": geometry.index,
            "name": geometry_name,
            "type": geometry.type_name,
            "dataBlock": int(geometry_data_index),
        },
    }
    return TriggerPhantomDecodeResult(
        contract=contract,
        source_sha256=source_sha256,
        block_types=tuple(entry.type_name for entry in entries),
        collision=collision,
        presentation=presentation,
    )
