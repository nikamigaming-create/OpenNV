"""Decode bounded Fallout LAND geometry and texture-layer contracts."""

from __future__ import annotations

import math
import struct
from dataclasses import dataclass
from pathlib import Path

from cell_static_contract import default_profile_path, load_profile
from plugin_records import Record, iter_plugin_records, iter_subrecords, zstring


LANDSCAPE_RECORD_TYPES = frozenset({"LAND", "LTEX", "TXST"})
LAND_VERTEX_SIDE = 33
LAND_VERTEX_COUNT = LAND_VERTEX_SIDE * LAND_VERTEX_SIDE
LAND_LAYER_HEADER_BYTES = 8
LAND_OPACITY_ROW_BYTES = 8
LAND_QUADRANT_VERTEX_SIDE = 17
LAND_HEIGHT_SCALE = 8.0
LAND_HEIGHT_HEADER_BYTES = 4
LAND_HEIGHT_TRAILER_BYTES = 3
LAND_NORMAL_COMPONENTS = 3
LAND_VERTEX_DATA_FLAG = 0x00000001
NORMAL_LENGTH_EPSILON = 1.0e-6
BYTE_CHANNEL_MAXIMUM = 255.0
CELL_CHILDREN_GROUP_TYPE = 6
WORLDSPACE_CHILDREN_GROUP_TYPE = 1
FORM_ID_BYTES = 4
NULL_FORM_ID = 0
CONFIGURED_MISSING_BASE_SOURCE = "configured-owned-game-default-ltex"
FORM_ID_RADIX = 16
LAND_BASE_LAYER_INDEX = 0xFFFF


@dataclass(frozen=True)
class LandscapeTextureSet:
    form_id: int
    editor_id: str
    diffuse_path: str
    normal_path: str | None


@dataclass(frozen=True)
class LandscapeTexture:
    form_id: int
    editor_id: str
    texture_set_form_id: int


@dataclass(frozen=True)
class LandscapeOpacity:
    vertex_index: int
    unknown: int
    opacity: float


@dataclass(frozen=True)
class LandscapeLayer:
    texture_form_id: int
    quadrant: int
    layer_index: int
    unknown: int
    opacities: tuple[LandscapeOpacity, ...]
    source: str = "authored-subrecord"


@dataclass(frozen=True)
class Landscape:
    form_id: int
    cell_form_id: int
    worldspace_form_id: int
    flags: int
    compression_checksum_valid: bool | None
    heights: tuple[float, ...]
    normals: tuple[tuple[float, float, float], ...]
    colors: tuple[tuple[float, float, float, float], ...]
    base_layers: tuple[LandscapeLayer, ...]
    alpha_layers: tuple[LandscapeLayer, ...]
    source_bytes: bytes


@dataclass(frozen=True)
class NonGeometricLandscape:
    form_id: int
    cell_form_id: int
    worldspace_form_id: int
    flags: int
    compression_checksum_valid: bool | None
    base_texture_form_ids: tuple[int, ...]
    alpha_layers: tuple[LandscapeLayer, ...]
    source_bytes: bytes


@dataclass(frozen=True)
class LandscapeIdentity:
    form_key: str
    cell_form_key: str
    worldspace_form_key: str
    source_plugin: str
    source_local_form_id: str


@dataclass(frozen=True)
class LandscapeCatalog:
    landscapes: dict[int, Landscape]
    non_geometric_landscapes: dict[int, NonGeometricLandscape]
    textures: dict[int, LandscapeTexture]
    texture_sets: dict[int, LandscapeTextureSet]

    def landscape_for_cell(self, cell_form_id: int) -> Landscape:
        matches = [value for value in self.landscapes.values() if value.cell_form_id == cell_form_id]
        if len(matches) != 1:
            raise ValueError(f"Expected one LAND for CELL {cell_form_id:08x}, found {len(matches)}")
        return matches[0]

    def optional_landscape_for_cell(self, cell_form_id: int) -> Landscape | None:
        landscape = self.landscapes.get(cell_form_id)
        non_geometric = self.non_geometric_landscapes.get(cell_form_id)
        if (landscape is None) == (non_geometric is None):
            raise ValueError(
                f"Expected one LAND classification for CELL {cell_form_id:08x}"
            )
        return landscape

    def diffuse_path(self, texture_form_id: int) -> str:
        texture = self.textures.get(texture_form_id)
        if texture is None:
            raise ValueError(f"LAND references unresolved LTEX {texture_form_id:08x}")
        texture_set = self.texture_sets.get(texture.texture_set_form_id)
        if texture_set is None or not texture_set.diffuse_path:
            raise ValueError(
                f"LTEX {texture.form_id:08x} references unresolved TXST {texture.texture_set_form_id:08x}"
            )
        return texture_set.diffuse_path

    def texture_contract(self, texture_form_id: int) -> dict[str, object]:
        texture = self.textures.get(texture_form_id)
        if texture is None:
            raise ValueError(f"LAND references unresolved LTEX {texture_form_id:08x}")
        texture_set = self.texture_sets.get(texture.texture_set_form_id)
        if texture_set is None:
            raise ValueError(
                f"LTEX {texture.form_id:08x} references unresolved TXST "
                f"{texture.texture_set_form_id:08x}"
            )
        return {
            "ltexFormId": f"{texture.form_id:08x}",
            "ltexEditorId": texture.editor_id,
            "txstFormId": f"{texture_set.form_id:08x}",
            "txstEditorId": texture_set.editor_id,
            "diffusePath": texture_set.diffuse_path,
            "normalPath": texture_set.normal_path,
        }


def landscape_missing_base_policy() -> dict[str, object]:
    return dict(load_profile(default_profile_path())["landscapeMissingBasePolicy"])


def _configured_missing_base_layer(quadrant: int) -> LandscapeLayer:
    policy = landscape_missing_base_policy()
    return LandscapeLayer(
        int(str(policy["ltexRawFormId"]), FORM_ID_RADIX),
        quadrant,
        LAND_BASE_LAYER_INDEX,
        0,
        (),
        CONFIGURED_MISSING_BASE_SOURCE,
    )


def resolved_layer_texture_form_id(
    landscape: Landscape,
    layer: LandscapeLayer,
) -> int:
    if layer.texture_form_id != NULL_FORM_ID:
        return layer.texture_form_id
    if layer in landscape.base_layers:
        raise ValueError(
            f"LAND {landscape.form_id:08x} has a null base texture in quadrant {layer.quadrant}"
        )
    matches = [
        base.texture_form_id
        for base in landscape.base_layers
        if base.quadrant == layer.quadrant
    ]
    if len(matches) != 1:
        raise ValueError(
            f"LAND {landscape.form_id:08x} cannot resolve its quadrant {layer.quadrant} default"
        )
    return matches[0]


def _values(record: Record) -> dict[str, list[bytes]]:
    result: dict[str, list[bytes]] = {}
    for subrecord in iter_subrecords(record):
        result.setdefault(subrecord.signature, []).append(subrecord.data)
    return result


def _first_text(values: dict[str, list[bytes]], signature: str) -> str:
    matches = values.get(signature, [])
    return zstring(matches[0]).replace("/", "\\").lower() if matches else ""


def _parent(record: Record, group_type: int) -> int | None:
    return next(
        (group.label_u32 for group in reversed(record.groups) if group.group_type == group_type),
        None,
    )


def _layer_header(data: bytes, record: Record, signature: str) -> tuple[int, int, int, int]:
    if len(data) != LAND_LAYER_HEADER_BYTES:
        raise ValueError(f"{signature} must be eight bytes in LAND {record.form_id:08x}")
    texture_form_id, quadrant, unknown, layer_index = struct.unpack("<IBBH", data)
    if quadrant > 3:
        raise ValueError(f"{signature} has invalid quadrant {quadrant} in LAND {record.form_id:08x}")
    return texture_form_id, quadrant, layer_index, unknown


def _opacities(data: bytes, record: Record) -> tuple[LandscapeOpacity, ...]:
    if len(data) % LAND_OPACITY_ROW_BYTES:
        raise ValueError(f"VTXT must contain eight-byte rows in LAND {record.form_id:08x}")
    rows = []
    indices = set()
    for offset in range(0, len(data), LAND_OPACITY_ROW_BYTES):
        vertex_index, unknown, opacity = struct.unpack_from("<HHf", data, offset)
        if (
            vertex_index >= LAND_QUADRANT_VERTEX_SIDE * LAND_QUADRANT_VERTEX_SIDE
            or vertex_index in indices
            or not math.isfinite(opacity)
        ):
            raise ValueError(f"VTXT contains an invalid vertex row in LAND {record.form_id:08x}")
        indices.add(vertex_index)
        rows.append(LandscapeOpacity(vertex_index, unknown, max(0.0, min(1.0, opacity))))
    return tuple(rows)


def _heights(data: bytes, record: Record) -> tuple[float, ...]:
    if len(data) != LAND_HEIGHT_HEADER_BYTES + LAND_VERTEX_COUNT + LAND_HEIGHT_TRAILER_BYTES:
        raise ValueError(f"VHGT must be 1096 bytes in LAND {record.form_id:08x}")
    offset = struct.unpack_from("<f", data)[0] * LAND_HEIGHT_SCALE
    deltas = struct.unpack_from(f"<{LAND_VERTEX_COUNT}b", data, LAND_HEIGHT_HEADER_BYTES)
    rows: list[list[float]] = []
    for y in range(LAND_VERTEX_SIDE):
        row = []
        for x in range(LAND_VERTEX_SIDE):
            delta = float(deltas[y * LAND_VERTEX_SIDE + x]) * LAND_HEIGHT_SCALE
            if x > 0:
                row.append(row[x - 1] + delta)
            elif y > 0:
                row.append(rows[y - 1][0] + delta)
            else:
                row.append(offset + delta)
        rows.append(row)
    return tuple(value for row in rows for value in row)


def _normals(data: bytes, record: Record) -> tuple[tuple[float, float, float], ...]:
    if len(data) != LAND_VERTEX_COUNT * LAND_NORMAL_COMPONENTS:
        raise ValueError(f"VNML must be 3267 bytes in LAND {record.form_id:08x}")
    values = struct.unpack(f"<{LAND_VERTEX_COUNT * LAND_NORMAL_COMPONENTS}b", data)
    rows = []
    for offset in range(0, len(values), LAND_NORMAL_COMPONENTS):
        x, y, z = (
            float(value) for value in values[offset : offset + LAND_NORMAL_COMPONENTS]
        )
        length = math.sqrt(x * x + y * y + z * z)
        if length <= NORMAL_LENGTH_EPSILON:
            raise ValueError(f"VNML contains a zero normal in LAND {record.form_id:08x}")
        rows.append((x / length, y / length, z / length))
    return tuple(rows)


def _colors(data: bytes | None, record: Record) -> tuple[tuple[float, float, float, float], ...]:
    if data is None:
        return tuple((1.0, 1.0, 1.0, 1.0) for _ in range(LAND_VERTEX_COUNT))
    if len(data) != LAND_VERTEX_COUNT * LAND_NORMAL_COMPONENTS:
        raise ValueError(f"VCLR must be 3267 bytes in LAND {record.form_id:08x}")
    return tuple(
        (
            data[offset] / BYTE_CHANNEL_MAXIMUM,
            data[offset + 1] / BYTE_CHANNEL_MAXIMUM,
            data[offset + 2] / BYTE_CHANNEL_MAXIMUM,
            1.0,
        )
        for offset in range(0, len(data), LAND_NORMAL_COMPONENTS)
    )


def _parse_landscape_record(record: Record) -> Landscape | NonGeometricLandscape:
    cell_form_id = _parent(record, CELL_CHILDREN_GROUP_TYPE)
    worldspace_form_id = _parent(record, WORLDSPACE_CHILDREN_GROUP_TYPE)
    if cell_form_id is None or worldspace_form_id is None:
        raise ValueError(f"LAND {record.form_id:08x} has no CELL/worldspace ownership")
    subrecords = list(iter_subrecords(record))
    values: dict[str, list[bytes]] = {}
    for subrecord in subrecords:
        values.setdefault(subrecord.signature, []).append(subrecord.data)
    data_rows = values.get("DATA", [])
    normal_rows = values.get("VNML", [])
    height_rows = values.get("VHGT", [])
    if len(data_rows) != 1 or len(data_rows[0]) != 4:
        raise ValueError(f"LAND {record.form_id:08x} lacks one four-byte DATA contract")
    flags = struct.unpack("<I", data_rows[0])[0]
    if not normal_rows and not height_rows and not (flags & LAND_VERTEX_DATA_FLAG):
        base_textures = []
        alpha_layers = []
        pending_alpha: tuple[int, int, int, int] | None = None
        for subrecord in subrecords:
            if subrecord.signature == "BTXT":
                texture, _quadrant, _layer, _unknown = _layer_header(
                    subrecord.data, record, "BTXT"
                )
                base_textures.append(texture)
            elif subrecord.signature == "ATXT":
                if pending_alpha is not None:
                    raise ValueError(
                        f"ATXT lacks its VTXT rows in LAND {record.form_id:08x}"
                    )
                pending_alpha = _layer_header(subrecord.data, record, "ATXT")
            elif subrecord.signature == "VTXT":
                if pending_alpha is None:
                    raise ValueError(
                        f"VTXT has no preceding ATXT in LAND {record.form_id:08x}"
                    )
                texture, quadrant, layer, unknown = pending_alpha
                alpha_layers.append(
                    LandscapeLayer(
                        texture,
                        quadrant,
                        layer,
                        unknown,
                        _opacities(subrecord.data, record),
                    )
                )
                pending_alpha = None
        if pending_alpha is not None:
            raise ValueError(f"ATXT lacks its VTXT rows in LAND {record.form_id:08x}")
        return NonGeometricLandscape(
            record.form_id,
            cell_form_id,
            worldspace_form_id,
            flags,
            record.compression_checksum_valid,
            tuple(base_textures),
            tuple(alpha_layers),
            record.data,
        )
    if (
        len(normal_rows) != 1
        or len(height_rows) != 1
        or not (flags & LAND_VERTEX_DATA_FLAG)
    ):
        raise ValueError(
            f"LAND {record.form_id:08x} has inconsistent DATA/VNML/VHGT geometry"
        )

    base_layers = []
    alpha_layers = []
    pending_alpha: tuple[int, int, int, int] | None = None
    for subrecord in subrecords:
        if subrecord.signature == "BTXT":
            texture, quadrant, layer, unknown = _layer_header(subrecord.data, record, "BTXT")
            base_layers.append(LandscapeLayer(texture, quadrant, layer, unknown, ()))
        elif subrecord.signature == "ATXT":
            if pending_alpha is not None:
                raise ValueError(f"ATXT lacks its VTXT rows in LAND {record.form_id:08x}")
            pending_alpha = _layer_header(subrecord.data, record, "ATXT")
        elif subrecord.signature == "VTXT":
            if pending_alpha is None:
                raise ValueError(f"VTXT has no preceding ATXT in LAND {record.form_id:08x}")
            texture, quadrant, layer, unknown = pending_alpha
            alpha_layers.append(
                LandscapeLayer(texture, quadrant, layer, unknown, _opacities(subrecord.data, record))
            )
            pending_alpha = None
    if pending_alpha is not None:
        raise ValueError(f"ATXT lacks its VTXT rows in LAND {record.form_id:08x}")
    authored_base_quadrants = {layer.quadrant for layer in base_layers}
    if len(authored_base_quadrants) != len(base_layers):
        raise ValueError(f"LAND {record.form_id:08x} duplicates a BTXT quadrant")
    base_layers.extend(
        _configured_missing_base_layer(quadrant)
        for quadrant in sorted({0, 1, 2, 3} - authored_base_quadrants)
    )
    if len({(layer.quadrant, layer.layer_index) for layer in alpha_layers}) != len(alpha_layers):
        raise ValueError(f"LAND {record.form_id:08x} duplicates an ATXT layer")
    color_rows = values.get("VCLR", [])
    if len(color_rows) > 1:
        raise ValueError(f"LAND {record.form_id:08x} declares multiple VCLR records")
    return Landscape(
        record.form_id,
        cell_form_id,
        worldspace_form_id,
        flags,
        record.compression_checksum_valid,
        _heights(height_rows[0], record),
        _normals(normal_rows[0], record),
        _colors(color_rows[0] if color_rows else None, record),
        tuple(sorted(base_layers, key=lambda value: value.quadrant)),
        tuple(sorted(alpha_layers, key=lambda value: (value.quadrant, value.layer_index))),
        record.data,
    )


def parse_landscape(record: Record) -> Landscape:
    landscape = _parse_landscape_record(record)
    if isinstance(landscape, NonGeometricLandscape):
        raise ValueError(
            f"LAND {record.form_id:08x} explicitly has no authored vertex geometry"
        )
    return landscape


def scan_landscape_catalog(path: Path, cell_form_ids: set[int]) -> LandscapeCatalog:
    landscapes: dict[int, Landscape] = {}
    non_geometric_landscapes: dict[int, NonGeometricLandscape] = {}
    textures: dict[int, LandscapeTexture] = {}
    texture_sets: dict[int, LandscapeTextureSet] = {}
    for record in iter_plugin_records(path, LANDSCAPE_RECORD_TYPES):
        if record.signature == "LAND":
            cell_form_id = _parent(record, CELL_CHILDREN_GROUP_TYPE)
            if cell_form_id in cell_form_ids:
                landscape = _parse_landscape_record(record)
                if (
                    landscape.cell_form_id in landscapes
                    or landscape.cell_form_id in non_geometric_landscapes
                ):
                    raise ValueError(f"CELL {landscape.cell_form_id:08x} declares multiple LAND records")
                if isinstance(landscape, NonGeometricLandscape):
                    non_geometric_landscapes[landscape.cell_form_id] = landscape
                else:
                    landscapes[landscape.cell_form_id] = landscape
        elif record.signature == "LTEX":
            values = _values(record)
            texture_sets_found = values.get("TNAM", [])
            if len(texture_sets_found) == 1 and len(texture_sets_found[0]) == FORM_ID_BYTES:
                textures[record.form_id] = LandscapeTexture(
                    record.form_id,
                    _first_text(values, "EDID"),
                    struct.unpack("<I", texture_sets_found[0])[0],
                )
        elif record.signature == "TXST":
            values = _values(record)
            diffuse = _first_text(values, "TX00")
            if diffuse:
                texture_sets[record.form_id] = LandscapeTextureSet(
                    record.form_id,
                    _first_text(values, "EDID"),
                    diffuse,
                    _first_text(values, "TX01") or None,
                )
    catalog = LandscapeCatalog(
        landscapes,
        non_geometric_landscapes,
        textures,
        texture_sets,
    )
    policy = landscape_missing_base_policy()
    default_form_id = int(str(policy["ltexRawFormId"]), FORM_ID_RADIX)
    if any(
        layer.source == CONFIGURED_MISSING_BASE_SOURCE
        for landscape in landscapes.values()
        for layer in landscape.base_layers
    ):
        default_texture = textures.get(default_form_id)
        if (
            default_texture is None
            or default_texture.editor_id.casefold()
            != str(policy["expectedEditorId"]).casefold()
        ):
            raise ValueError(
                "Configured missing LAND base LTEX identity differs from owned data"
            )
    for landscape in landscapes.values():
        for layer in (*landscape.base_layers, *landscape.alpha_layers):
            try:
                catalog.diffuse_path(resolved_layer_texture_form_id(landscape, layer))
            except ValueError as error:
                raise ValueError(
                    f"{error} in LAND {landscape.form_id:08x} "
                    f"CELL {landscape.cell_form_id:08x} quadrant {layer.quadrant} "
                    f"layer {layer.layer_index}"
                ) from error
    return catalog
