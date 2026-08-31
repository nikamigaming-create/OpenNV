"""Build a neutral cell/base-object/placed-reference graph from plugin records."""

from __future__ import annotations

import argparse
import json
import math
import struct
from dataclasses import asdict, dataclass, field
from functools import cache
from pathlib import Path

from crafting_catalog import (
    CraftingCategory,
    CraftingRecipe,
    decode_category,
    decode_recipe,
)
from plugin_records import Record, iter_plugin_records, iter_subrecords, zstring


ITEM_RECORD_TYPES = {
    "ALCH",
    "AMMO",
    "ARMO",
    "BOOK",
    "IMOD",
    "KEYM",
    "MISC",
    "NOTE",
    "WEAP",
}
BASE_RECORD_TYPES = ITEM_RECORD_TYPES | {
    "ACTI",
    "CONT",
    "DOOR",
    "FURN",
    "LIGH",
    "MSTT",
    "SCOL",
    "STAT",
    "TERM",
    "TREE",
}
CATALOG_RECORD_TYPES = frozenset(
    BASE_RECORD_TYPES
    | {"CELL", "LGTM", "NAVM", "QUST", "RCCT", "RCPE", "REFR", "SCPT", "TES4"}
)
PLUGIN_LOCALIZED_FLAG = 0x00000080

REFERENCE_TRANSFORM_BYTES = 24
REFERENCE_SCALE_BYTES = 4
REFERENCE_TRANSFORM_FLOATS = 6
DEFAULT_REFERENCE_SCALE = 1.0
INITIALLY_DISABLED_RECORD_FLAG = 0x00000800
CELL_CHILDREN_GROUP_TYPE = 6
WORLDSPACE_CHILDREN_GROUP_TYPE = 1
FORM_ID_BYTES = 4
CELL_LIGHTING_BYTES = 40
CELL_LIGHTING_AMBIENT_SLICE = slice(0, 3)
CELL_LIGHTING_DIRECTIONAL_SLICE = slice(4, 7)
CELL_LIGHTING_FOG_SLICE = slice(8, 11)
CELL_LIGHTING_FOG_NEAR_OFFSET = 12
CELL_LIGHTING_FOG_FAR_OFFSET = 16
CELL_LIGHTING_DIRECTION_OFFSET = 20
CELL_LIGHTING_DIRECTIONAL_FADE_OFFSET = 28
CELL_LIGHTING_FOG_CLIP_OFFSET = 32
CELL_LIGHTING_FOG_POWER_OFFSET = 36
CELL_LIGHTING_TEMPLATE_AMBIENT_COLOR = 0x00000001
CELL_LIGHTING_TEMPLATE_DIRECTIONAL_COLOR = 0x00000002
CELL_LIGHTING_TEMPLATE_FOG_COLOR = 0x00000004
CELL_LIGHTING_TEMPLATE_FOG_NEAR = 0x00000008
CELL_LIGHTING_TEMPLATE_FOG_FAR = 0x00000010
CELL_LIGHTING_TEMPLATE_DIRECTIONAL_ROTATION = 0x00000020
CELL_LIGHTING_TEMPLATE_DIRECTIONAL_FADE = 0x00000040
CELL_LIGHTING_TEMPLATE_FOG_CLIP_DISTANCE = 0x00000080
CELL_LIGHTING_TEMPLATE_FOG_POWER = 0x00000100
CELL_LIGHTING_TEMPLATE_KNOWN_FLAGS = (
    CELL_LIGHTING_TEMPLATE_AMBIENT_COLOR
    | CELL_LIGHTING_TEMPLATE_DIRECTIONAL_COLOR
    | CELL_LIGHTING_TEMPLATE_FOG_COLOR
    | CELL_LIGHTING_TEMPLATE_FOG_NEAR
    | CELL_LIGHTING_TEMPLATE_FOG_FAR
    | CELL_LIGHTING_TEMPLATE_DIRECTIONAL_ROTATION
    | CELL_LIGHTING_TEMPLATE_DIRECTIONAL_FADE
    | CELL_LIGHTING_TEMPLATE_FOG_CLIP_DISTANCE
    | CELL_LIGHTING_TEMPLATE_FOG_POWER
)
LIGHT_DATA_BYTES = 32
LIGHT_RADIUS_OFFSET = 4
LIGHT_COLOR_SLICE = slice(8, 11)
LIGHT_FLAGS_OFFSET = 12
LIGHT_FALLOFF_OFFSET = 16
LIGHT_FIELD_OF_VIEW_OFFSET = 20
LIGHT_INTENSITY_BYTES = 4
DEFAULT_LIGHT_INTENSITY = 1.0
CONTAINER_ITEM_BYTES = 8
WEAPON_DATA_BYTES = 15
WEAPON_DAMAGE_OFFSET = 12
WEAPON_CLIP_SIZE_OFFSET = 14
ITEM_VALUE_OFFSET = 0
ITEM_WEIGHT_OFFSET = 4
ARMO_WEAP_ITEM_WEIGHT_OFFSET = 8
SIMPLE_ITEM_DATA_BYTES = 8
ARMOR_ITEM_DATA_BYTES = 12
ITEM_ECONOMICS_RECORD_TYPES = frozenset({"ARMO", "IMOD", "KEYM", "MISC", "WEAP"})
TELEPORT_DESTINATION_TRANSFORM_OFFSET = 4
TELEPORT_DESTINATION_BYTES = 28
NAVM_DATA_BYTES = 24
NAVM_VERTEX = struct.Struct("<3f")
NAVM_TRIANGLE = struct.Struct("<3H3hI")
NAVM_TRIANGLE_VERTEX_SLICE = slice(0, 3)
NAVM_TRIANGLE_ADJACENCY_SLICE = slice(3, 6)
NAVM_TRIANGLE_FLAGS_INDEX = 6
NAVM_DOOR_PORTAL = struct.Struct("<II")
NAVM_EXTERNAL_CONNECTION = struct.Struct("<IIH")
NAVM_COVER_TRIANGLE_BYTES = 2
NAVM_VERSION_BYTES = 4
NAVM_GRID_HEADER_BYTES = 36
NAVM_GRID_BOUNDS_FLOATS = 8
NAVM_GRID_SEGMENT_COUNT_BYTES = 2
NAVM_NO_ADJACENT_TRIANGLE = -1
NAVM_SHARED_EDGE_VERTEX_COUNT = 2


@dataclass(frozen=True)
class Transform:
    position: tuple[float, float, float]
    rotation_radians: tuple[float, float, float]


@dataclass(frozen=True)
class CellLighting:
    ambient_rgb: tuple[int, int, int]
    directional_rgb: tuple[int, int, int]
    fog_rgb: tuple[int, int, int]
    fog_near: float
    fog_far: float
    directional_rotation: tuple[int, int]
    directional_fade: float
    fog_clip_distance: float
    fog_power: float


@dataclass(frozen=True)
class Cell:
    form_id: int
    editor_id: str
    flags: int
    coordinates: tuple[int, int] | None
    worldspace_form_id: int | None
    lighting: CellLighting | None
    authored_lighting: CellLighting | None
    lighting_template_form_id: int | None
    lighting_template_flags: int

    @property
    def interior(self) -> bool:
        return bool(self.flags & 1)


@dataclass(frozen=True)
class BaseObject:
    form_id: int
    record_type: str
    editor_id: str
    model_path: str | None
    display_name: str | None = None
    attached_script_form_id: int | None = None


@dataclass(frozen=True)
class ScriptSource:
    form_id: int
    editor_id: str
    source: str


@dataclass(frozen=True)
class QuestIdentity:
    form_id: int
    editor_id: str


@dataclass(frozen=True)
class LightingTemplate:
    form_id: int
    editor_id: str
    lighting: CellLighting


@dataclass(frozen=True)
class LightObject:
    form_id: int
    editor_id: str
    radius: int
    color_rgb: tuple[int, int, int]
    flags: int
    falloff: float
    field_of_view: float
    intensity: float


@dataclass(frozen=True)
class ContainerItem:
    item_form_id: int
    count: int


@dataclass(frozen=True)
class ContainerObject:
    form_id: int
    items: tuple[ContainerItem, ...]


@dataclass(frozen=True)
class WeaponObject:
    form_id: int
    damage: int
    clip_size: int
    ammo_form_id: int | None


@dataclass(frozen=True)
class ItemObject:
    form_id: int
    value: int
    weight: float
    source_subrecord: str
    source_layout: str


@dataclass(frozen=True)
class NavMeshTriangle:
    vertex_indices: tuple[int, int, int]
    adjacent_triangles: tuple[int, int, int]
    flags: int


@dataclass(frozen=True)
class NavMeshDoorPortal:
    door_reference_form_id: int
    unknown: int


@dataclass(frozen=True)
class NavMeshExternalConnection:
    unknown: int
    navmesh_form_id: int
    triangle_index: int


@dataclass(frozen=True)
class NavMeshSpatialGrid:
    divisor: int
    bounds: tuple[float, float, float, float, float, float, float, float]
    triangle_segments: tuple[tuple[int, ...], ...]


@dataclass(frozen=True)
class CellNavMesh:
    form_id: int
    cell_form_id: int
    version: int
    data_tail: tuple[int, int, int]
    vertices: tuple[tuple[float, float, float], ...]
    triangles: tuple[NavMeshTriangle, ...]
    external_connections: tuple[NavMeshExternalConnection, ...]
    door_portals: tuple[NavMeshDoorPortal, ...]
    cover_triangle_indices: tuple[int, ...]
    spatial_grid: NavMeshSpatialGrid


@dataclass(frozen=True)
class PlacedReference:
    form_id: int
    cell_form_id: int
    base_form_id: int
    flags: int
    transform: Transform
    scale: float
    teleport_destination_form_id: int | None
    teleport_destination_transform: Transform | None


@dataclass
class CellCatalog:
    cells: dict[int, Cell]
    lighting_templates: dict[int, LightingTemplate]
    base_objects: dict[int, BaseObject]
    scripts: dict[int, ScriptSource]
    quests: dict[int, QuestIdentity]
    lights: dict[int, LightObject]
    containers: dict[int, ContainerObject]
    weapons: dict[int, WeaponObject]
    navmeshes: dict[int, list[CellNavMesh]]
    references: list[PlacedReference]
    items: dict[int, ItemObject] = field(default_factory=dict)
    crafting_categories: dict[int, CraftingCategory] = field(default_factory=dict)
    crafting_recipes: dict[int, CraftingRecipe] = field(default_factory=dict)

    def references_for(self, cell_form_id: int) -> list[PlacedReference]:
        return [reference for reference in self.references if reference.cell_form_id == cell_form_id]

    def navmeshes_for(self, cell_form_id: int) -> list[CellNavMesh]:
        return self.navmeshes.get(cell_form_id, [])


def subrecords_by_signature(record: Record) -> dict[str, list[bytes]]:
    result: dict[str, list[bytes]] = {}
    for subrecord in iter_subrecords(record):
        result.setdefault(subrecord.signature, []).append(subrecord.data)
    return result


def _first_text(values: dict[str, list[bytes]], signature: str) -> str:
    matches = values.get(signature, [])
    return zstring(matches[0]) if matches else ""


def _display_name(
    values: dict[str, list[bytes]],
    localized_plugin: bool,
    record: Record,
) -> str | None:
    matches = values.get("FULL", [])
    if not matches:
        return None
    if len(matches) != 1:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} has ambiguous FULL values"
        )
    if localized_plugin:
        if len(matches[0]) != 4:
            raise ValueError(
                f"Localized {record.signature} {record.form_id:08x} has malformed FULL ID"
            )
        string_id = struct.unpack("<I", matches[0])[0]
        raise ValueError(
            "Localized plugin FULL values require an owned STRINGS table; "
            f"refusing to decode string ID {string_id:08x} as text"
        )
    value = zstring(matches[0]).strip()
    return value or None


def normalize_model_path(data: bytes) -> str:
    value = zstring(data).replace("/", "\\").lstrip("\\").lower()
    if value.startswith("data\\meshes\\"):
        return value[len("data\\meshes\\") :]
    if value.startswith("meshes\\"):
        return value[len("meshes\\") :]
    return value


def cell_parent_form_id(record: Record) -> int | None:
    for group in reversed(record.groups):
        if group.group_type == CELL_CHILDREN_GROUP_TYPE:
            return group.label_u32
    return None


def worldspace_parent_form_id(record: Record) -> int | None:
    for group in reversed(record.groups):
        if group.group_type == WORLDSPACE_CHILDREN_GROUP_TYPE:
            return group.label_u32
    return None


def parse_transform(data: bytes, record: Record) -> Transform:
    if len(data) != REFERENCE_TRANSFORM_BYTES:
        raise ValueError(
            f"DATA transform must be {REFERENCE_TRANSFORM_BYTES} bytes in "
            f"{record.signature} {record.form_id:08x}"
        )
    values = struct.unpack(f"<{REFERENCE_TRANSFORM_FLOATS}f", data)
    return Transform(tuple(values[:3]), tuple(values[3:]))


def parse_reference_scale(values: dict[str, list[bytes]], record: Record) -> float:
    matches = values.get("XSCL", [])
    if not matches:
        return DEFAULT_REFERENCE_SCALE
    if len(matches) != 1 or len(matches[0]) != REFERENCE_SCALE_BYTES:
        raise ValueError(
            f"XSCL must contain one {REFERENCE_SCALE_BYTES}-byte scale in "
            f"{record.signature} {record.form_id:08x}"
        )
    scale = struct.unpack("<f", matches[0])[0]
    if not scale > 0.0:
        raise ValueError(f"XSCL must be positive in {record.signature} {record.form_id:08x}")
    return scale


def parse_form_id(data: bytes, record: Record, signature: str) -> int:
    if len(data) < FORM_ID_BYTES:
        raise ValueError(f"{signature} must contain a form ID in {record.signature} {record.form_id:08x}")
    return struct.unpack_from("<I", data)[0]


def parse_cell_lighting(data: bytes, record: Record) -> CellLighting:
    if len(data) != CELL_LIGHTING_BYTES:
        raise ValueError(
            f"XCLL must be {CELL_LIGHTING_BYTES} bytes in CELL {record.form_id:08x}"
        )
    return CellLighting(
        tuple(data[CELL_LIGHTING_AMBIENT_SLICE]),
        tuple(data[CELL_LIGHTING_DIRECTIONAL_SLICE]),
        tuple(data[CELL_LIGHTING_FOG_SLICE]),
        struct.unpack_from("<f", data, CELL_LIGHTING_FOG_NEAR_OFFSET)[0],
        struct.unpack_from("<f", data, CELL_LIGHTING_FOG_FAR_OFFSET)[0],
        struct.unpack_from("<ii", data, CELL_LIGHTING_DIRECTION_OFFSET),
        struct.unpack_from("<f", data, CELL_LIGHTING_DIRECTIONAL_FADE_OFFSET)[0],
        struct.unpack_from("<f", data, CELL_LIGHTING_FOG_CLIP_OFFSET)[0],
        struct.unpack_from("<f", data, CELL_LIGHTING_FOG_POWER_OFFSET)[0],
    )


def resolve_cell_lighting(
    authored: CellLighting | None,
    template: CellLighting | None,
    inheritance_flags: int,
) -> CellLighting | None:
    unknown_flags = inheritance_flags & ~CELL_LIGHTING_TEMPLATE_KNOWN_FLAGS
    if unknown_flags:
        raise ValueError(
            f"CELL lighting template has unsupported inheritance flags: 0x{unknown_flags:08x}"
        )
    if inheritance_flags == 0:
        return authored
    if template is None:
        raise ValueError("CELL lighting inheritance requires a resolved LGTM record")
    if authored is None and inheritance_flags != CELL_LIGHTING_TEMPLATE_KNOWN_FLAGS:
        raise ValueError("Partial CELL lighting inheritance requires authored XCLL lighting")
    source = authored if authored is not None else template
    return CellLighting(
        template.ambient_rgb
        if inheritance_flags & CELL_LIGHTING_TEMPLATE_AMBIENT_COLOR
        else source.ambient_rgb,
        template.directional_rgb
        if inheritance_flags & CELL_LIGHTING_TEMPLATE_DIRECTIONAL_COLOR
        else source.directional_rgb,
        template.fog_rgb
        if inheritance_flags & CELL_LIGHTING_TEMPLATE_FOG_COLOR
        else source.fog_rgb,
        template.fog_near
        if inheritance_flags & CELL_LIGHTING_TEMPLATE_FOG_NEAR
        else source.fog_near,
        template.fog_far
        if inheritance_flags & CELL_LIGHTING_TEMPLATE_FOG_FAR
        else source.fog_far,
        template.directional_rotation
        if inheritance_flags & CELL_LIGHTING_TEMPLATE_DIRECTIONAL_ROTATION
        else source.directional_rotation,
        template.directional_fade
        if inheritance_flags & CELL_LIGHTING_TEMPLATE_DIRECTIONAL_FADE
        else source.directional_fade,
        template.fog_clip_distance
        if inheritance_flags & CELL_LIGHTING_TEMPLATE_FOG_CLIP_DISTANCE
        else source.fog_clip_distance,
        template.fog_power
        if inheritance_flags & CELL_LIGHTING_TEMPLATE_FOG_POWER
        else source.fog_power,
    )


def parse_light_object(
    record: Record,
    values: dict[str, list[bytes]],
) -> LightObject | None:
    matches = values.get("DATA", [])
    if not matches:
        return None
    if len(matches) != 1:
        raise ValueError(f"LIGH must contain one DATA record in {record.form_id:08x}")
    data = matches[0]
    if len(data) != LIGHT_DATA_BYTES:
        raise ValueError(
            f"LIGH DATA must be {LIGHT_DATA_BYTES} bytes in {record.form_id:08x}"
        )
    intensity_values = values.get("FNAM", [])
    if len(intensity_values) > 1 or (
        intensity_values and len(intensity_values[0]) != LIGHT_INTENSITY_BYTES
    ):
        raise ValueError(
            f"LIGH FNAM must contain one {LIGHT_INTENSITY_BYTES}-byte value in "
            f"{record.form_id:08x}"
        )
    intensity = (
        struct.unpack("<f", intensity_values[0])[0]
        if intensity_values
        else DEFAULT_LIGHT_INTENSITY
    )
    falloff = struct.unpack_from("<f", data, LIGHT_FALLOFF_OFFSET)[0]
    field_of_view = struct.unpack_from("<f", data, LIGHT_FIELD_OF_VIEW_OFFSET)[0]
    if not all(math.isfinite(value) for value in (intensity, falloff, field_of_view)):
        raise ValueError(f"LIGH contains a non-finite value in {record.form_id:08x}")
    return LightObject(
        record.form_id,
        _first_text(values, "EDID"),
        struct.unpack_from("<I", data, LIGHT_RADIUS_OFFSET)[0],
        tuple(data[LIGHT_COLOR_SLICE]),
        struct.unpack_from("<I", data, LIGHT_FLAGS_OFFSET)[0],
        falloff,
        field_of_view,
        intensity,
    )


def parse_navmesh(record: Record) -> CellNavMesh:
    values = subrecords_by_signature(record)

    def one(signature: str) -> bytes:
        matches = values.get(signature, [])
        if len(matches) != 1:
            raise ValueError(
                f"NAVM {record.form_id:08x} must contain one {signature} subrecord"
            )
        return matches[0]

    version_data = one("NVER")
    data = one("DATA")
    vertices_data = one("NVVX")
    triangles_data = one("NVTR")
    grid_data = one("NVGD")
    if len(version_data) != NAVM_VERSION_BYTES:
        raise ValueError(f"NAVM {record.form_id:08x} has malformed NVER")
    if len(data) != NAVM_DATA_BYTES:
        raise ValueError(f"NAVM {record.form_id:08x} has malformed DATA")
    version = struct.unpack("<I", version_data)[0]
    cell_form_id, vertex_count, triangle_count, *data_tail = struct.unpack(
        "<6I", data
    )
    parent_cell = cell_parent_form_id(record)
    if parent_cell is None or parent_cell != cell_form_id:
        raise ValueError(
            f"NAVM {record.form_id:08x} DATA CELL disagrees with its parent"
        )
    if len(vertices_data) != vertex_count * NAVM_VERTEX.size:
        raise ValueError(
            f"NAVM {record.form_id:08x} vertex count disagrees with NVVX"
        )
    if len(triangles_data) != triangle_count * NAVM_TRIANGLE.size:
        raise ValueError(
            f"NAVM {record.form_id:08x} triangle count disagrees with NVTR"
        )
    vertices = tuple(
        NAVM_VERTEX.unpack_from(vertices_data, offset)
        for offset in range(0, len(vertices_data), NAVM_VERTEX.size)
    )
    if not vertices or not all(
        math.isfinite(component)
        for vertex in vertices
        for component in vertex
    ):
        raise ValueError(f"NAVM {record.form_id:08x} has invalid vertices")
    triangles = []
    for offset in range(0, len(triangles_data), NAVM_TRIANGLE.size):
        raw = NAVM_TRIANGLE.unpack_from(triangles_data, offset)
        vertex_indices = tuple(raw[NAVM_TRIANGLE_VERTEX_SLICE])
        adjacent = tuple(raw[NAVM_TRIANGLE_ADJACENCY_SLICE])
        if len(set(vertex_indices)) != len(vertex_indices) or any(
            index >= vertex_count for index in vertex_indices
        ):
            raise ValueError(
                f"NAVM {record.form_id:08x} triangle has invalid vertices"
            )
        if any(
            index < NAVM_NO_ADJACENT_TRIANGLE
            for index in adjacent
        ):
            raise ValueError(
                f"NAVM {record.form_id:08x} triangle has invalid adjacency"
            )
        triangles.append(
            NavMeshTriangle(vertex_indices, adjacent, raw[NAVM_TRIANGLE_FLAGS_INDEX])
        )

    external_data = values.get("NVEX", [])
    if len(external_data) > 1 or (
        external_data and len(external_data[0]) % NAVM_EXTERNAL_CONNECTION.size
    ):
        raise ValueError(f"NAVM {record.form_id:08x} has malformed NVEX")
    external_connections = tuple(
        NavMeshExternalConnection(
            *NAVM_EXTERNAL_CONNECTION.unpack_from(external_data[0], offset)
        )
        for offset in range(
            0,
            len(external_data[0]),
            NAVM_EXTERNAL_CONNECTION.size,
        )
    ) if external_data else ()
    for triangle_index, triangle in enumerate(triangles):
        source_vertices = set(triangle.vertex_indices)
        for adjacent in triangle.adjacent_triangles:
            if adjacent == NAVM_NO_ADJACENT_TRIANGLE:
                continue
            internal = (
                adjacent != triangle_index
                and adjacent < triangle_count
                and len(
                    source_vertices.intersection(
                        triangles[adjacent].vertex_indices
                    )
                )
                == NAVM_SHARED_EDGE_VERTEX_COUNT
                and triangle_index in triangles[adjacent].adjacent_triangles
            )
            external = adjacent < len(external_connections)
            if not internal and not external:
                raise ValueError(
                    f"NAVM {record.form_id:08x} adjacency cannot be resolved "
                    "as an internal edge or external connection"
                )

    door_data = values.get("NVDP", [])
    if len(door_data) > 1 or (
        door_data and len(door_data[0]) % NAVM_DOOR_PORTAL.size
    ):
        raise ValueError(f"NAVM {record.form_id:08x} has malformed NVDP")
    door_portals = tuple(
        NavMeshDoorPortal(*NAVM_DOOR_PORTAL.unpack_from(door_data[0], offset))
        for offset in range(0, len(door_data[0]), NAVM_DOOR_PORTAL.size)
    ) if door_data else ()
    cover_data = values.get("NVCA", [])
    if len(cover_data) > 1 or (
        cover_data and len(cover_data[0]) % NAVM_COVER_TRIANGLE_BYTES
    ):
        raise ValueError(f"NAVM {record.form_id:08x} has malformed NVCA")
    cover_triangle_indices = tuple(
        struct.unpack_from("<H", cover_data[0], offset)[0]
        for offset in range(0, len(cover_data[0]), NAVM_COVER_TRIANGLE_BYTES)
    ) if cover_data else ()
    if any(index >= triangle_count for index in cover_triangle_indices):
        raise ValueError(f"NAVM {record.form_id:08x} cover triangle is out of range")

    if len(grid_data) < NAVM_GRID_HEADER_BYTES:
        raise ValueError(f"NAVM {record.form_id:08x} has malformed NVGD")
    divisor = struct.unpack_from("<I", grid_data)[0]
    bounds = struct.unpack_from(
        f"<{NAVM_GRID_BOUNDS_FLOATS}f",
        grid_data,
        NAVM_VERSION_BYTES,
    )
    if divisor == 0 or not all(math.isfinite(value) for value in bounds):
        raise ValueError(f"NAVM {record.form_id:08x} has invalid NVGD bounds")
    grid_offset = NAVM_GRID_HEADER_BYTES
    segments = []
    for _ in range(divisor * divisor):
        if len(grid_data) - grid_offset < NAVM_GRID_SEGMENT_COUNT_BYTES:
            raise ValueError(f"NAVM {record.form_id:08x} truncates an NVGD segment")
        count = struct.unpack_from("<H", grid_data, grid_offset)[0]
        grid_offset += NAVM_GRID_SEGMENT_COUNT_BYTES
        segment_bytes = count * 2
        if len(grid_data) - grid_offset < segment_bytes:
            raise ValueError(f"NAVM {record.form_id:08x} truncates NVGD indices")
        segment = struct.unpack_from(f"<{count}H", grid_data, grid_offset)
        grid_offset += segment_bytes
        if any(index >= triangle_count for index in segment):
            raise ValueError(f"NAVM {record.form_id:08x} NVGD index is out of range")
        segments.append(tuple(segment))
    if grid_offset != len(grid_data):
        raise ValueError(f"NAVM {record.form_id:08x} has trailing NVGD bytes")

    return CellNavMesh(
        record.form_id,
        cell_form_id,
        version,
        tuple(data_tail),
        vertices,
        tuple(triangles),
        external_connections,
        door_portals,
        cover_triangle_indices,
        NavMeshSpatialGrid(divisor, bounds, tuple(segments)),
    )


def _container_object(record: Record, values: dict[str, list[bytes]]) -> ContainerObject:
    items = []
    for data in values.get("CNTO", []):
        if len(data) != CONTAINER_ITEM_BYTES:
            raise ValueError(
                f"CONT CNTO must be {CONTAINER_ITEM_BYTES} bytes in {record.form_id:08x}"
            )
        item_form_id, count = struct.unpack("<Ii", data)
        items.append(ContainerItem(item_form_id, count))
    return ContainerObject(record.form_id, tuple(items))


def _single_data(
    record: Record,
    values: dict[str, list[bytes]],
    expected_bytes: int,
) -> bytes:
    matches = values.get("DATA", [])
    if len(matches) != 1 or len(matches[0]) != expected_bytes:
        raise ValueError(
            f"{record.signature} DATA must be {expected_bytes} bytes in "
            f"{record.form_id:08x}"
        )
    return matches[0]


def _item_object(record: Record, values: dict[str, list[bytes]]) -> ItemObject | None:
    if record.signature not in ITEM_ECONOMICS_RECORD_TYPES:
        return None
    if record.signature in {"IMOD", "KEYM", "MISC"}:
        data = _single_data(record, values, SIMPLE_ITEM_DATA_BYTES)
        weight_offset = ITEM_WEIGHT_OFFSET
        layout = f"fnv-{record.signature.lower()}-data-8-v1"
    elif record.signature == "ARMO":
        data = _single_data(record, values, ARMOR_ITEM_DATA_BYTES)
        weight_offset = ARMO_WEAP_ITEM_WEIGHT_OFFSET
        layout = "fnv-armo-data-12-v1"
    else:
        data = _single_data(record, values, WEAPON_DATA_BYTES)
        weight_offset = ARMO_WEAP_ITEM_WEIGHT_OFFSET
        layout = "fnv-weap-data-15-v1"
    value = struct.unpack_from("<i", data, ITEM_VALUE_OFFSET)[0]
    weight = struct.unpack_from("<f", data, weight_offset)[0]
    if value < 0 or not math.isfinite(weight) or weight < 0.0:
        raise ValueError(
            f"{record.signature} DATA has invalid item economics in {record.form_id:08x}"
        )
    return ItemObject(record.form_id, value, weight, "DATA", layout)


def _weapon_object(record: Record, values: dict[str, list[bytes]]) -> WeaponObject:
    data = _single_data(record, values, WEAPON_DATA_BYTES)
    ammo_values = values.get("NAM0", [])
    ammo = parse_form_id(ammo_values[0], record, "NAM0") if ammo_values else None
    return WeaponObject(
        record.form_id,
        struct.unpack_from("<H", data, WEAPON_DAMAGE_OFFSET)[0],
        data[WEAPON_CLIP_SIZE_OFFSET],
        ammo,
    )


@cache
def scan_cell_catalog(path: Path) -> CellCatalog:
    catalog = CellCatalog({}, {}, {}, {}, {}, {}, {}, {}, {}, [])
    localized_plugin: bool | None = None
    for record in iter_plugin_records(path, CATALOG_RECORD_TYPES):
        if record.signature == "TES4":
            if localized_plugin is not None:
                raise ValueError(f"Plugin has duplicate TES4 headers: {path}")
            localized_plugin = bool(record.flags & PLUGIN_LOCALIZED_FLAG)
            continue
        if localized_plugin is None:
            raise ValueError(f"Plugin records precede the TES4 header: {path}")
        if record.signature == "CELL":
            values = subrecords_by_signature(record)
            data = values.get("DATA", [b"\0"])[0]
            coordinates_data = values.get("XCLC", [])
            coordinates = struct.unpack_from("<ii", coordinates_data[0]) if coordinates_data else None
            lighting_data = values.get("XCLL", [])
            template_data = values.get("LTMP", [])
            inheritance_data = values.get("LNAM", [])
            if len(template_data) > 1 or len(inheritance_data) > 1:
                raise ValueError(f"CELL {record.form_id:08x} repeats LTMP or LNAM")
            if inheritance_data and not template_data:
                raise ValueError(f"CELL {record.form_id:08x} has LNAM without LTMP")
            parsed_template_form_id = (
                parse_form_id(template_data[0], record, "LTMP") if template_data else 0
            )
            lighting_template_form_id = parsed_template_form_id or None
            lighting_template_flags = (
                parse_form_id(inheritance_data[0], record, "LNAM")
                if inheritance_data
                else 0
            )
            authored_lighting = (
                parse_cell_lighting(lighting_data[0], record) if lighting_data else None
            )
            catalog.cells[record.form_id] = Cell(
                record.form_id,
                _first_text(values, "EDID"),
                data[0] if data else 0,
                coordinates,
                worldspace_parent_form_id(record),
                authored_lighting,
                authored_lighting,
                lighting_template_form_id,
                lighting_template_flags,
            )
        elif record.signature == "LGTM":
            values = subrecords_by_signature(record)
            lighting_data = values.get("DATA", [])
            if len(lighting_data) != 1:
                raise ValueError(f"LGTM {record.form_id:08x} must contain one DATA record")
            catalog.lighting_templates[record.form_id] = LightingTemplate(
                record.form_id,
                _first_text(values, "EDID"),
                parse_cell_lighting(lighting_data[0], record),
            )
        elif record.signature in BASE_RECORD_TYPES:
            values = subrecords_by_signature(record)
            models = values.get("MODL", [])
            catalog.base_objects[record.form_id] = BaseObject(
                record.form_id,
                record.signature,
                _first_text(values, "EDID"),
                normalize_model_path(models[0]) if models else None,
                _display_name(values, localized_plugin, record),
                parse_form_id(values["SCRI"][0], record, "SCRI")
                if values.get("SCRI")
                else None,
            )
            item = _item_object(record, values)
            if item is not None:
                catalog.items[record.form_id] = item
            if record.signature == "LIGH":
                light = parse_light_object(record, values)
                if light is not None:
                    catalog.lights[record.form_id] = light
            elif record.signature == "CONT":
                catalog.containers[record.form_id] = _container_object(record, values)
            elif record.signature == "WEAP":
                catalog.weapons[record.form_id] = _weapon_object(record, values)
        elif record.signature == "SCPT":
            values = subrecords_by_signature(record)
            sources = values.get("SCTX", [])
            if len(sources) != 1:
                raise ValueError(f"SCPT {record.form_id:08x} must contain one SCTX source")
            catalog.scripts[record.form_id] = ScriptSource(
                record.form_id,
                _first_text(values, "EDID"),
                zstring(sources[0]),
            )
        elif record.signature == "RCCT":
            category = decode_category(record)
            catalog.crafting_categories[category.form_id] = category
        elif record.signature == "RCPE":
            recipe = decode_recipe(record)
            catalog.crafting_recipes[recipe.form_id] = recipe
        elif record.signature == "QUST":
            values = subrecords_by_signature(record)
            catalog.quests[record.form_id] = QuestIdentity(
                record.form_id,
                _first_text(values, "EDID"),
            )
        elif record.signature == "NAVM":
            navmesh = parse_navmesh(record)
            catalog.navmeshes.setdefault(navmesh.cell_form_id, []).append(navmesh)
        elif record.signature == "REFR":
            cell_form_id = cell_parent_form_id(record)
            if cell_form_id is None:
                continue
            values = subrecords_by_signature(record)
            if not values.get("NAME") or not values.get("DATA"):
                continue
            teleport = values.get("XTEL", [])
            teleport_data = teleport[0] if teleport else b""
            catalog.references.append(
                PlacedReference(
                    record.form_id,
                    cell_form_id,
                    parse_form_id(values["NAME"][0], record, "NAME"),
                    record.flags,
                    parse_transform(values["DATA"][0], record),
                    parse_reference_scale(values, record),
                    parse_form_id(teleport_data, record, "XTEL") if teleport_data else None,
                    parse_transform(
                        teleport_data[
                            TELEPORT_DESTINATION_TRANSFORM_OFFSET:TELEPORT_DESTINATION_BYTES
                        ],
                        record,
                    )
                    if len(teleport_data) >= TELEPORT_DESTINATION_BYTES
                    else None,
                )
            )
    if localized_plugin is None:
        raise ValueError(f"Plugin has no TES4 header: {path}")
    for form_id, cell in tuple(catalog.cells.items()):
        template = (
            catalog.lighting_templates.get(cell.lighting_template_form_id)
            if cell.lighting_template_form_id is not None
            else None
        )
        if cell.lighting_template_form_id is not None and template is None:
            raise ValueError(
                f"CELL {form_id:08x} references missing LGTM "
                f"{cell.lighting_template_form_id:08x}"
            )
        catalog.cells[form_id] = Cell(
            cell.form_id,
            cell.editor_id,
            cell.flags,
            cell.coordinates,
            cell.worldspace_form_id,
            resolve_cell_lighting(
                cell.authored_lighting,
                template.lighting if template is not None else None,
                cell.lighting_template_flags if template is not None else 0,
            ),
            cell.authored_lighting,
            cell.lighting_template_form_id,
            cell.lighting_template_flags,
        )
    return catalog


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--find", default="")
    parser.add_argument("--limit", type=int, required=True)
    args = parser.parse_args()
    catalog = scan_cell_catalog(args.input.resolve())
    needle = args.find.casefold()
    cells = [cell for cell in catalog.cells.values() if needle in cell.editor_id.casefold()]
    rows = []
    if args.limit <= 0:
        raise ValueError("Cell catalog result limit must be positive")
    for cell in cells[: args.limit]:
        references = catalog.references_for(cell.form_id)
        rows.append(
            {
                **asdict(cell),
                "interior": cell.interior,
                "references": len(references),
                "modeledReferences": sum(
                    1
                    for reference in references
                    if (base := catalog.base_objects.get(reference.base_form_id)) is not None and base.model_path
                ),
                "doors": sum(
                    1
                    for reference in references
                    if (base := catalog.base_objects.get(reference.base_form_id)) is not None
                    and base.record_type == "DOOR"
                ),
            }
        )
    print(json.dumps({"cells": rows, "matched": len(cells)}, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
