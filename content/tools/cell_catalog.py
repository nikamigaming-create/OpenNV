"""Build a neutral cell/base-object/placed-reference graph from plugin records."""

from __future__ import annotations

import argparse
import json
import struct
from dataclasses import asdict, dataclass
from pathlib import Path

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
    "STAT",
    "TREE",
}
CATALOG_RECORD_TYPES = frozenset(BASE_RECORD_TYPES | {"CELL", "REFR"})


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

    @property
    def interior(self) -> bool:
        return bool(self.flags & 1)


@dataclass(frozen=True)
class BaseObject:
    form_id: int
    record_type: str
    editor_id: str
    model_path: str | None


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
class PlacedReference:
    form_id: int
    cell_form_id: int
    base_form_id: int
    flags: int
    transform: Transform
    teleport_destination_form_id: int | None
    teleport_destination_transform: Transform | None


@dataclass
class CellCatalog:
    cells: dict[int, Cell]
    base_objects: dict[int, BaseObject]
    lights: dict[int, LightObject]
    containers: dict[int, ContainerObject]
    weapons: dict[int, WeaponObject]
    references: list[PlacedReference]

    def references_for(self, cell_form_id: int) -> list[PlacedReference]:
        return [reference for reference in self.references if reference.cell_form_id == cell_form_id]


def _subrecords(record: Record) -> dict[str, list[bytes]]:
    result: dict[str, list[bytes]] = {}
    for subrecord in iter_subrecords(record):
        result.setdefault(subrecord.signature, []).append(subrecord.data)
    return result


def _first_text(values: dict[str, list[bytes]], signature: str) -> str:
    matches = values.get(signature, [])
    return zstring(matches[0]) if matches else ""


def _model_path(data: bytes) -> str:
    value = zstring(data).replace("/", "\\").lstrip("\\").lower()
    if value.startswith("data\\meshes\\"):
        return value[len("data\\meshes\\") :]
    if value.startswith("meshes\\"):
        return value[len("meshes\\") :]
    return value


def _cell_parent(record: Record) -> int | None:
    for group in reversed(record.groups):
        if group.group_type == 6:
            return group.label_u32
    return None


def _worldspace_parent(record: Record) -> int | None:
    for group in reversed(record.groups):
        if group.group_type == 1:
            return group.label_u32
    return None


def _transform(data: bytes, record: Record) -> Transform:
    if len(data) != 24:
        raise ValueError(f"DATA transform must be 24 bytes in {record.signature} {record.form_id:08x}")
    values = struct.unpack("<6f", data)
    return Transform(tuple(values[:3]), tuple(values[3:]))


def _form_id(data: bytes, record: Record, signature: str) -> int:
    if len(data) < 4:
        raise ValueError(f"{signature} must contain a form ID in {record.signature} {record.form_id:08x}")
    return struct.unpack_from("<I", data)[0]


def _cell_lighting(data: bytes, record: Record) -> CellLighting:
    if len(data) != 40:
        raise ValueError(f"XCLL must be 40 bytes in CELL {record.form_id:08x}")
    return CellLighting(
        tuple(data[0:3]),
        tuple(data[4:7]),
        tuple(data[8:11]),
        struct.unpack_from("<f", data, 12)[0],
        struct.unpack_from("<f", data, 16)[0],
        struct.unpack_from("<ii", data, 20),
        struct.unpack_from("<f", data, 28)[0],
        struct.unpack_from("<f", data, 32)[0],
        struct.unpack_from("<f", data, 36)[0],
    )


def _light_object(record: Record, values: dict[str, list[bytes]]) -> LightObject | None:
    matches = values.get("DATA", [])
    if not matches:
        return None
    data = matches[0]
    if len(data) != 32:
        raise ValueError(f"LIGH DATA must be 32 bytes in {record.form_id:08x}")
    intensity_values = values.get("FNAM", [])
    intensity = struct.unpack("<f", intensity_values[0])[0] if intensity_values else 1.0
    return LightObject(
        record.form_id,
        _first_text(values, "EDID"),
        struct.unpack_from("<I", data, 4)[0],
        tuple(data[8:11]),
        struct.unpack_from("<I", data, 12)[0],
        struct.unpack_from("<f", data, 16)[0],
        struct.unpack_from("<f", data, 20)[0],
        intensity,
    )


def _container_object(record: Record, values: dict[str, list[bytes]]) -> ContainerObject:
    items = []
    for data in values.get("CNTO", []):
        if len(data) != 8:
            raise ValueError(f"CONT CNTO must be 8 bytes in {record.form_id:08x}")
        item_form_id, count = struct.unpack("<Ii", data)
        items.append(ContainerItem(item_form_id, count))
    return ContainerObject(record.form_id, tuple(items))


def _weapon_object(record: Record, values: dict[str, list[bytes]]) -> WeaponObject:
    matches = values.get("DATA", [])
    if len(matches) != 1 or len(matches[0]) != 15:
        raise ValueError(f"WEAP DATA must be 15 bytes in {record.form_id:08x}")
    data = matches[0]
    ammo_values = values.get("NAM0", [])
    ammo = _form_id(ammo_values[0], record, "NAM0") if ammo_values else None
    return WeaponObject(
        record.form_id,
        struct.unpack_from("<H", data, 12)[0],
        data[14],
        ammo,
    )


def scan_cell_catalog(path: Path) -> CellCatalog:
    catalog = CellCatalog({}, {}, {}, {}, {}, [])
    for record in iter_plugin_records(path, CATALOG_RECORD_TYPES):
        if record.signature == "CELL":
            values = _subrecords(record)
            data = values.get("DATA", [b"\0"])[0]
            coordinates_data = values.get("XCLC", [])
            coordinates = struct.unpack_from("<ii", coordinates_data[0]) if coordinates_data else None
            lighting_data = values.get("XCLL", [])
            catalog.cells[record.form_id] = Cell(
                record.form_id,
                _first_text(values, "EDID"),
                data[0] if data else 0,
                coordinates,
                _worldspace_parent(record),
                _cell_lighting(lighting_data[0], record) if lighting_data else None,
            )
        elif record.signature in BASE_RECORD_TYPES:
            values = _subrecords(record)
            models = values.get("MODL", [])
            catalog.base_objects[record.form_id] = BaseObject(
                record.form_id,
                record.signature,
                _first_text(values, "EDID"),
                _model_path(models[0]) if models else None,
            )
            if record.signature == "LIGH":
                light = _light_object(record, values)
                if light is not None:
                    catalog.lights[record.form_id] = light
            elif record.signature == "CONT":
                catalog.containers[record.form_id] = _container_object(record, values)
            elif record.signature == "WEAP":
                catalog.weapons[record.form_id] = _weapon_object(record, values)
        elif record.signature == "REFR":
            cell_form_id = _cell_parent(record)
            if cell_form_id is None:
                continue
            values = _subrecords(record)
            if not values.get("NAME") or not values.get("DATA"):
                continue
            teleport = values.get("XTEL", [])
            teleport_data = teleport[0] if teleport else b""
            catalog.references.append(
                PlacedReference(
                    record.form_id,
                    cell_form_id,
                    _form_id(values["NAME"][0], record, "NAME"),
                    record.flags,
                    _transform(values["DATA"][0], record),
                    _form_id(teleport_data, record, "XTEL") if teleport_data else None,
                    _transform(teleport_data[4:28], record) if len(teleport_data) >= 28 else None,
                )
            )
    return catalog


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--find", default="")
    parser.add_argument("--limit", type=int, default=25)
    args = parser.parse_args()
    catalog = scan_cell_catalog(args.input.resolve())
    needle = args.find.casefold()
    cells = [cell for cell in catalog.cells.values() if needle in cell.editor_id.casefold()]
    rows = []
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
