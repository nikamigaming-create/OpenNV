"""Resolve authored humanoid actor records and placements from a TES4 master."""

from __future__ import annotations

import struct
from dataclasses import dataclass
from pathlib import Path

from plugin_records import Record, iter_plugin_records, iter_subrecords, zstring


ACTOR_RECORD_TYPES = frozenset({"ACHR", "ACRE", "ARMO", "CREA", "EYES", "HAIR", "HDPT", "NPC_", "RACE"})


@dataclass(frozen=True)
class ActorItem:
    form_id: int
    count: int


@dataclass(frozen=True)
class HumanoidActor:
    form_id: int
    editor_id: str
    name: str
    skeleton_path: str | None
    female: bool
    race_form_id: int | None
    hair_form_id: int | None
    eyes_form_id: int | None
    head_part_form_ids: tuple[int, ...]
    hair_length: float
    hair_color_rgba: tuple[int, int, int, int]
    inventory: tuple[ActorItem, ...]
    template_form_id: int | None
    face_symmetric_geometry: tuple[float, ...]
    face_asymmetric_geometry: tuple[float, ...]
    face_symmetric_texture: tuple[float, ...]


@dataclass(frozen=True)
class ActorReference:
    form_id: int
    record_type: str
    cell_form_id: int
    actor_form_id: int
    flags: int
    position: tuple[float, float, float]
    rotation_radians: tuple[float, float, float]

    @property
    def initially_disabled(self) -> bool:
        return bool(self.flags & 0x00000800)


@dataclass(frozen=True)
class RaceAppearance:
    form_id: int
    editor_id: str
    female_head_models: tuple[str | None, ...]
    female_head_textures: tuple[str | None, ...]
    female_body_models: tuple[str | None, ...]
    female_body_textures: tuple[str | None, ...]
    female_face_symmetric_geometry: tuple[float, ...]
    female_face_asymmetric_geometry: tuple[float, ...]
    female_face_symmetric_texture: tuple[float, ...]


@dataclass(frozen=True)
class AppearancePart:
    form_id: int
    record_type: str
    editor_id: str
    name: str
    model_path: str | None
    texture_path: str | None


@dataclass(frozen=True)
class Armor:
    form_id: int
    editor_id: str
    name: str
    male_model_path: str | None
    male_ground_model_path: str | None
    female_model_path: str | None
    female_ground_model_path: str | None


@dataclass(frozen=True)
class CreatureActor:
    form_id: int
    editor_id: str
    name: str
    skeleton_path: str | None


@dataclass
class ActorCatalog:
    actors: dict[int, HumanoidActor]
    creatures: dict[int, CreatureActor]
    references: list[ActorReference]
    races: dict[int, RaceAppearance]
    parts: dict[int, AppearancePart]
    armor: dict[int, Armor]

    def references_for(self, cell_form_id: int) -> list[ActorReference]:
        return [reference for reference in self.references if reference.cell_form_id == cell_form_id]


def _subrecords(record: Record) -> list[tuple[str, bytes]]:
    return [(subrecord.signature, subrecord.data) for subrecord in iter_subrecords(record)]


def _values(subrecords: list[tuple[str, bytes]]) -> dict[str, list[bytes]]:
    result: dict[str, list[bytes]] = {}
    for signature, data in subrecords:
        result.setdefault(signature, []).append(data)
    return result


def _first_text(values: dict[str, list[bytes]], signature: str) -> str:
    matches = values.get(signature, [])
    return zstring(matches[0]) if matches else ""


def _form_id(data: bytes, record: Record, signature: str) -> int:
    if len(data) != 4:
        raise ValueError(f"{signature} must be four bytes in {record.signature} {record.form_id:08x}")
    return struct.unpack("<I", data)[0]


def _optional_form(values: dict[str, list[bytes]], record: Record, signature: str) -> int | None:
    matches = values.get(signature, [])
    return _form_id(matches[0], record, signature) if matches else None


def _optional_float_array(
    values: dict[str, list[bytes]],
    record: Record,
    signature: str,
    count: int,
) -> tuple[float, ...]:
    matches = values.get(signature, [])
    if not matches:
        return ()
    if len(matches) != 1 or len(matches[0]) != count * 4:
        raise ValueError(f"{signature} must contain {count} floats in NPC_ {record.form_id:08x}")
    return struct.unpack(f"<{count}f", matches[0])


def _canonical_model(value: bytes) -> str:
    return zstring(value).replace("/", "\\").lstrip("\\").lower()


def _actor(record: Record, subrecords: list[tuple[str, bytes]]) -> HumanoidActor:
    values = _values(subrecords)
    acbs = values.get("ACBS", [])
    if len(acbs) != 1 or len(acbs[0]) != 24:
        raise ValueError(f"NPC_ ACBS must be 24 bytes in {record.form_id:08x}")
    race_form_id = _optional_form(values, record, "RNAM")
    models = values.get("MODL", [])
    if len(models) > 1:
        raise ValueError(f"NPC_ declares multiple skeleton models in {record.form_id:08x}")
    inventory = []
    for data in values.get("CNTO", []):
        if len(data) != 8:
            raise ValueError(f"NPC_ CNTO must be 8 bytes in {record.form_id:08x}")
        item_form_id, count = struct.unpack("<Ii", data)
        inventory.append(ActorItem(item_form_id, count))
    hair_color = values.get("HCLR", [bytes(4)])[0]
    if len(hair_color) != 4:
        raise ValueError(f"NPC_ HCLR must be four bytes in {record.form_id:08x}")
    hair_length_data = values.get("LNAM", [struct.pack("<f", 0.0)])[0]
    if len(hair_length_data) != 4:
        raise ValueError(f"NPC_ LNAM must be four bytes in {record.form_id:08x}")
    return HumanoidActor(
        record.form_id,
        _first_text(values, "EDID"),
        _first_text(values, "FULL"),
        _canonical_model(models[0]) if models else None,
        bool(struct.unpack_from("<I", acbs[0])[0] & 1),
        race_form_id,
        _optional_form(values, record, "HNAM"),
        _optional_form(values, record, "ENAM"),
        tuple(_form_id(data, record, "PNAM") for data in values.get("PNAM", [])),
        struct.unpack("<f", hair_length_data)[0],
        tuple(hair_color),
        tuple(inventory),
        _optional_form(values, record, "TPLT"),
        _optional_float_array(values, record, "FGGS", 50),
        _optional_float_array(values, record, "FGGA", 30),
        _optional_float_array(values, record, "FGTS", 50),
    )


def _race(record: Record, subrecords: list[tuple[str, bytes]]) -> RaceAppearance:
    values = _values(subrecords)
    group = ""
    sex = ""
    index: int | None = None
    female_models: dict[int, str] = {}
    female_textures: dict[int, str] = {}
    female_body_models: dict[int, str] = {}
    female_body_textures: dict[int, str] = {}
    female_face_symmetric_geometry: tuple[float, ...] = ()
    female_face_asymmetric_geometry: tuple[float, ...] = ()
    female_face_symmetric_texture: tuple[float, ...] = ()
    for signature, data in subrecords:
        if signature == "NAM0":
            group, sex, index = "head", "male", None
        elif signature == "NAM1":
            group, sex, index = "body", "male", None
        elif signature == "MNAM":
            sex, index = "male", None
        elif signature == "FNAM":
            sex, index = "female", None
        elif signature == "INDX":
            index = _form_id(data, record, "INDX")
        elif sex == "female" and index is not None:
            models = female_models if group == "head" else female_body_models
            textures = female_textures if group == "head" else female_body_textures
            if signature == "MODL":
                models[index] = _canonical_model(data)
            elif signature == "ICON":
                textures[index] = _canonical_model(data)
        if sex == "female" and signature in {"FGGS", "FGGA", "FGTS"}:
            count = {"FGGS": 50, "FGGA": 30, "FGTS": 50}[signature]
            if len(data) != count * 4:
                raise ValueError(f"RACE {signature} must contain {count} floats in {record.form_id:08x}")
            coordinates = struct.unpack(f"<{count}f", data)
            if signature == "FGGS":
                female_face_symmetric_geometry = coordinates
            elif signature == "FGGA":
                female_face_asymmetric_geometry = coordinates
            else:
                female_face_symmetric_texture = coordinates
    maximum = max((*female_models.keys(), *female_textures.keys()), default=-1)
    body_maximum = max((*female_body_models.keys(), *female_body_textures.keys()), default=-1)
    return RaceAppearance(
        record.form_id,
        _first_text(values, "EDID"),
        tuple(female_models.get(part) for part in range(maximum + 1)),
        tuple(female_textures.get(part) for part in range(maximum + 1)),
        tuple(female_body_models.get(part) for part in range(body_maximum + 1)),
        tuple(female_body_textures.get(part) for part in range(body_maximum + 1)),
        female_face_symmetric_geometry,
        female_face_asymmetric_geometry,
        female_face_symmetric_texture,
    )


def _part(record: Record, subrecords: list[tuple[str, bytes]]) -> AppearancePart:
    values = _values(subrecords)
    models = values.get("MODL", [])
    textures = values.get("ICON", [])
    return AppearancePart(
        record.form_id,
        record.signature,
        _first_text(values, "EDID"),
        _first_text(values, "FULL"),
        _canonical_model(models[0]) if models else None,
        _canonical_model(textures[0]) if textures else None,
    )


def _armor(record: Record, subrecords: list[tuple[str, bytes]]) -> Armor:
    values = _values(subrecords)
    return Armor(
        record.form_id,
        _first_text(values, "EDID"),
        _first_text(values, "FULL"),
        _canonical_model(values["MODL"][0]) if values.get("MODL") else None,
        _canonical_model(values["MOD2"][0]) if values.get("MOD2") else None,
        _canonical_model(values["MOD3"][0]) if values.get("MOD3") else None,
        _canonical_model(values["MOD4"][0]) if values.get("MOD4") else None,
    )


def _creature(record: Record, subrecords: list[tuple[str, bytes]]) -> CreatureActor:
    values = _values(subrecords)
    models = values.get("MODL", [])
    if len(models) > 1:
        raise ValueError(f"CREA declares multiple skeleton models in {record.form_id:08x}")
    return CreatureActor(
        record.form_id,
        _first_text(values, "EDID"),
        _first_text(values, "FULL"),
        _canonical_model(models[0]) if models else None,
    )


def _cell_parent(record: Record) -> int | None:
    for group in reversed(record.groups):
        if group.group_type == 6:
            return group.label_u32
    return None


def _reference(record: Record, subrecords: list[tuple[str, bytes]]) -> ActorReference | None:
    values = _values(subrecords)
    cell_form_id = _cell_parent(record)
    if cell_form_id is None or not values.get("NAME") or not values.get("DATA"):
        return None
    transform = values["DATA"][0]
    if len(transform) != 24:
        raise ValueError(f"ACHR DATA must be 24 bytes in {record.form_id:08x}")
    values6 = struct.unpack("<6f", transform)
    return ActorReference(
        record.form_id,
        record.signature,
        cell_form_id,
        _form_id(values["NAME"][0], record, "NAME"),
        record.flags,
        tuple(values6[:3]),
        tuple(values6[3:]),
    )


def scan_actor_catalog(path: Path) -> ActorCatalog:
    catalog = ActorCatalog({}, {}, [], {}, {}, {})
    for record in iter_plugin_records(path, ACTOR_RECORD_TYPES):
        subrecords = _subrecords(record)
        if record.signature == "NPC_":
            catalog.actors[record.form_id] = _actor(record, subrecords)
        elif record.signature == "CREA":
            catalog.creatures[record.form_id] = _creature(record, subrecords)
        elif record.signature in {"ACHR", "ACRE"}:
            reference = _reference(record, subrecords)
            if reference is not None:
                catalog.references.append(reference)
        elif record.signature == "RACE":
            catalog.races[record.form_id] = _race(record, subrecords)
        elif record.signature in {"EYES", "HAIR", "HDPT"}:
            catalog.parts[record.form_id] = _part(record, subrecords)
        elif record.signature == "ARMO":
            catalog.armor[record.form_id] = _armor(record, subrecords)
    return catalog
