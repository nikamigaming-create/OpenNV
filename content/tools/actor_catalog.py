"""Resolve authored humanoid actor records and placements from a TES4 master."""

from __future__ import annotations

import hashlib
import struct
from dataclasses import dataclass, field
from functools import cache
from pathlib import Path

from plugin_records import Record, iter_plugin_records, iter_subrecords, zstring


ACTOR_RECORD_TYPES = frozenset(
    {
        "ACHR",
        "ACRE",
        "ARMO",
        "CREA",
        "EYES",
        "HAIR",
        "HDPT",
        "LVLC",
        "LVLI",
        "LVLN",
        "NPC_",
        "RACE",
    }
)

FORM_ID_BYTES = 4
NPC_ACBS_BYTES = 24
ACTOR_TEMPLATE_FLAGS_BYTES = 2
NPC_INVENTORY_ENTRY_BYTES = 8
REFERENCE_TRANSFORM_BYTES = 24
REFERENCE_SCALE_BYTES = 4
ENABLE_PARENT_BYTES = 8
ENABLE_PARENT_OPPOSITE_OFFSET = 4
ENABLE_PARENT_OPPOSITE_FLAG = 1
REFERENCE_TRANSFORM_FLOATS = 6
ARMOR_BIPED_DATA_BYTES = 8
LEVELED_LIST_ENTRY_BYTES = 12
FACEGEN_SYMMETRIC_GEOMETRY_FLOATS = 50
FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS = 30
FACEGEN_SYMMETRIC_TEXTURE_FLOATS = 50
DEFAULT_REFERENCE_SCALE = 1.0
INITIALLY_DISABLED_RECORD_FLAG = 0x00000800
DELETED_RECORD_FLAG = 0x00000020
FEMALE_ACTOR_FLAG = 0x00000001
BIPED_HAIR_SLOT_FLAG = 0x00000002
CELL_CHILDREN_GROUP_TYPE = 6


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
    actor_flags: int
    female: bool
    race_form_id: int | None
    hair_form_id: int | None
    eyes_form_id: int | None
    head_part_form_ids: tuple[int, ...]
    hair_length: float
    hair_color_rgba: tuple[int, int, int, int]
    inventory: tuple[ActorItem, ...]
    template_form_id: int | None
    template_flags: int
    face_symmetric_geometry: tuple[float, ...]
    face_asymmetric_geometry: tuple[float, ...]
    face_symmetric_texture: tuple[float, ...]
    package_form_ids: tuple[int, ...] = ()


@dataclass(frozen=True)
class ActorReference:
    form_id: int
    record_type: str
    cell_form_id: int
    actor_form_id: int
    flags: int
    position: tuple[float, float, float]
    rotation_radians: tuple[float, float, float]
    scale: float
    enable_parent_form_id: int | None
    enable_parent_opposite: bool = False

    @property
    def initially_disabled(self) -> bool:
        return bool(self.flags & INITIALLY_DISABLED_RECORD_FLAG)


@dataclass(frozen=True)
class RaceAppearance:
    form_id: int
    editor_id: str
    male_head_models: tuple[str | None, ...]
    male_head_textures: tuple[str | None, ...]
    male_body_models: tuple[str | None, ...]
    male_body_textures: tuple[str | None, ...]
    male_face_symmetric_geometry: tuple[float, ...]
    male_face_asymmetric_geometry: tuple[float, ...]
    male_face_symmetric_texture: tuple[float, ...]
    female_head_models: tuple[str | None, ...]
    female_head_textures: tuple[str | None, ...]
    female_body_models: tuple[str | None, ...]
    female_body_textures: tuple[str | None, ...]
    female_face_symmetric_geometry: tuple[float, ...]
    female_face_asymmetric_geometry: tuple[float, ...]
    female_face_symmetric_texture: tuple[float, ...]
    valid_eye_form_ids: tuple[int, ...]


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
    biped_flags: int

    @property
    def hides_hair(self) -> bool:
        return bool(self.biped_flags & BIPED_HAIR_SLOT_FLAG)


@dataclass(frozen=True)
class LeveledListEntry:
    form_id: int
    count: int


@dataclass(frozen=True)
class LeveledList:
    form_id: int
    editor_id: str
    entries: tuple[LeveledListEntry, ...]


@dataclass(frozen=True)
class LeveledActorList:
    form_id: int
    record_type: str
    editor_id: str
    entries: tuple[LeveledListEntry, ...]


@dataclass(frozen=True)
class CreatureActor:
    form_id: int
    editor_id: str
    name: str
    skeleton_path: str | None
    actor_flags: int
    model_paths: tuple[str, ...]
    inventory: tuple[ActorItem, ...]
    template_form_id: int | None
    template_flags: int


@dataclass
class ActorCatalog:
    actors: dict[int, HumanoidActor]
    creatures: dict[int, CreatureActor]
    references: list[ActorReference]
    races: dict[int, RaceAppearance]
    parts: dict[int, AppearancePart]
    armor: dict[int, Armor]
    leveled_lists: dict[int, LeveledList]
    actor_leveled_lists: dict[int, LeveledActorList] = field(default_factory=dict)
    deleted_form_ids: dict[str, set[int]] = field(default_factory=dict)
    record_flags: dict[str, dict[int, int]] = field(default_factory=dict)
    record_data_sha256: dict[str, dict[int, str]] = field(default_factory=dict)
    record_counts: dict[str, int] = field(default_factory=dict)

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
    if len(data) != FORM_ID_BYTES:
        raise ValueError(f"{signature} must be four bytes in {record.signature} {record.form_id:08x}")
    return struct.unpack("<I", data)[0]


def _optional_form(values: dict[str, list[bytes]], record: Record, signature: str) -> int | None:
    matches = values.get(signature, [])
    return _form_id(matches[0], record, signature) if matches else None


def _template_flags(values: dict[str, list[bytes]], record: Record) -> int:
    matches = values.get("EAMT", [])
    if not matches:
        return 0
    if len(matches) != 1 or len(matches[0]) != ACTOR_TEMPLATE_FLAGS_BYTES:
        raise ValueError(
            f"EAMT must contain {ACTOR_TEMPLATE_FLAGS_BYTES} bytes in "
            f"{record.signature} {record.form_id:08x}"
        )
    return struct.unpack("<H", matches[0])[0]


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
    if len(acbs) != 1 or len(acbs[0]) != NPC_ACBS_BYTES:
        raise ValueError(
            f"NPC_ ACBS must be {NPC_ACBS_BYTES} bytes in {record.form_id:08x}"
        )
    actor_flags = struct.unpack_from("<I", acbs[0])[0]
    race_form_id = _optional_form(values, record, "RNAM")
    models = values.get("MODL", [])
    if len(models) > 1:
        raise ValueError(f"NPC_ declares multiple skeleton models in {record.form_id:08x}")
    inventory = []
    for data in values.get("CNTO", []):
        if len(data) != NPC_INVENTORY_ENTRY_BYTES:
            raise ValueError(
                f"NPC_ CNTO must be {NPC_INVENTORY_ENTRY_BYTES} bytes in {record.form_id:08x}"
            )
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
        actor_flags,
        bool(actor_flags & FEMALE_ACTOR_FLAG),
        race_form_id,
        _optional_form(values, record, "HNAM"),
        _optional_form(values, record, "ENAM"),
        tuple(_form_id(data, record, "PNAM") for data in values.get("PNAM", [])),
        struct.unpack("<f", hair_length_data)[0],
        tuple(hair_color),
        tuple(inventory),
        _optional_form(values, record, "TPLT"),
        _template_flags(values, record),
        _optional_float_array(values, record, "FGGS", FACEGEN_SYMMETRIC_GEOMETRY_FLOATS),
        _optional_float_array(values, record, "FGGA", FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS),
        _optional_float_array(values, record, "FGTS", FACEGEN_SYMMETRIC_TEXTURE_FLOATS),
        tuple(_form_id(data, record, "PKID") for data in values.get("PKID", [])),
    )


def _race(record: Record, subrecords: list[tuple[str, bytes]]) -> RaceAppearance:
    values = _values(subrecords)
    group = ""
    sex = ""
    index: int | None = None
    male_models: dict[int, str] = {}
    male_textures: dict[int, str] = {}
    male_body_models: dict[int, str] = {}
    male_body_textures: dict[int, str] = {}
    female_models: dict[int, str] = {}
    female_textures: dict[int, str] = {}
    female_body_models: dict[int, str] = {}
    female_body_textures: dict[int, str] = {}
    male_face_symmetric_geometry: tuple[float, ...] = ()
    male_face_asymmetric_geometry: tuple[float, ...] = ()
    male_face_symmetric_texture: tuple[float, ...] = ()
    female_face_symmetric_geometry: tuple[float, ...] = ()
    female_face_asymmetric_geometry: tuple[float, ...] = ()
    female_face_symmetric_texture: tuple[float, ...] = ()
    valid_eye_form_ids: tuple[int, ...] = ()
    for signature, data in subrecords:
        if signature == "ENAM":
            if len(data) % FORM_ID_BYTES != 0:
                raise ValueError(
                    f"RACE ENAM has partial eye FormID in {record.form_id:08x}"
                )
            valid_eye_form_ids = struct.unpack(f"<{len(data) // FORM_ID_BYTES}I", data)
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
        elif sex in {"male", "female"} and index is not None:
            if sex == "male":
                models = male_models if group == "head" else male_body_models
                textures = male_textures if group == "head" else male_body_textures
            else:
                models = female_models if group == "head" else female_body_models
                textures = female_textures if group == "head" else female_body_textures
            if signature == "MODL":
                models[index] = _canonical_model(data)
            elif signature == "ICON":
                textures[index] = _canonical_model(data)
        if sex in {"male", "female"} and signature in {"FGGS", "FGGA", "FGTS"}:
            count = {
                "FGGS": FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
                "FGGA": FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
                "FGTS": FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
            }[signature]
            if len(data) != count * 4:
                raise ValueError(f"RACE {signature} must contain {count} floats in {record.form_id:08x}")
            coordinates = struct.unpack(f"<{count}f", data)
            if sex == "male" and signature == "FGGS":
                male_face_symmetric_geometry = coordinates
            elif sex == "male" and signature == "FGGA":
                male_face_asymmetric_geometry = coordinates
            elif sex == "male":
                male_face_symmetric_texture = coordinates
            elif signature == "FGGS":
                female_face_symmetric_geometry = coordinates
            elif signature == "FGGA":
                female_face_asymmetric_geometry = coordinates
            else:
                female_face_symmetric_texture = coordinates
    male_maximum = max((*male_models.keys(), *male_textures.keys()), default=-1)
    male_body_maximum = max((*male_body_models.keys(), *male_body_textures.keys()), default=-1)
    maximum = max((*female_models.keys(), *female_textures.keys()), default=-1)
    body_maximum = max((*female_body_models.keys(), *female_body_textures.keys()), default=-1)
    return RaceAppearance(
        record.form_id,
        _first_text(values, "EDID"),
        tuple(male_models.get(part) for part in range(male_maximum + 1)),
        tuple(male_textures.get(part) for part in range(male_maximum + 1)),
        tuple(male_body_models.get(part) for part in range(male_body_maximum + 1)),
        tuple(male_body_textures.get(part) for part in range(male_body_maximum + 1)),
        male_face_symmetric_geometry,
        male_face_asymmetric_geometry,
        male_face_symmetric_texture,
        tuple(female_models.get(part) for part in range(maximum + 1)),
        tuple(female_textures.get(part) for part in range(maximum + 1)),
        tuple(female_body_models.get(part) for part in range(body_maximum + 1)),
        tuple(female_body_textures.get(part) for part in range(body_maximum + 1)),
        female_face_symmetric_geometry,
        female_face_asymmetric_geometry,
        female_face_symmetric_texture,
        valid_eye_form_ids,
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
    biped = values.get("BMDT", [])
    if len(biped) != 1 or len(biped[0]) != ARMOR_BIPED_DATA_BYTES:
        raise ValueError(
            f"ARMO BMDT must contain {ARMOR_BIPED_DATA_BYTES} bytes in {record.form_id:08x}"
        )
    return Armor(
        record.form_id,
        _first_text(values, "EDID"),
        _first_text(values, "FULL"),
        _canonical_model(values["MODL"][0]) if values.get("MODL") else None,
        _canonical_model(values["MOD2"][0]) if values.get("MOD2") else None,
        _canonical_model(values["MOD3"][0]) if values.get("MOD3") else None,
        _canonical_model(values["MOD4"][0]) if values.get("MOD4") else None,
        struct.unpack_from("<I", biped[0])[0],
    )


def _leveled_list(record: Record, subrecords: list[tuple[str, bytes]]) -> LeveledList:
    values = _values(subrecords)
    entries: list[LeveledListEntry] = []
    for data in values.get("LVLO", []):
        if len(data) != LEVELED_LIST_ENTRY_BYTES:
            raise ValueError(
                f"LVLI LVLO must contain {LEVELED_LIST_ENTRY_BYTES} bytes in {record.form_id:08x}"
            )
        _level, _unused, item_form_id, count, _unused_tail = struct.unpack("<HHIHH", data)
        entries.append(LeveledListEntry(item_form_id, count))
    return LeveledList(record.form_id, _first_text(values, "EDID"), tuple(entries))


def _leveled_actor_list(
    record: Record,
    subrecords: list[tuple[str, bytes]],
) -> LeveledActorList:
    item_list = _leveled_list(record, subrecords)
    return LeveledActorList(
        item_list.form_id,
        record.signature,
        item_list.editor_id,
        item_list.entries,
    )


def _model_paths(data: bytes) -> tuple[str, ...]:
    return tuple(
        _canonical_model(value)
        for value in data.split(b"\0")
        if value
    )


def _creature(record: Record, subrecords: list[tuple[str, bytes]]) -> CreatureActor:
    values = _values(subrecords)
    acbs = values.get("ACBS", [])
    if len(acbs) != 1 or len(acbs[0]) != NPC_ACBS_BYTES:
        raise ValueError(
            f"CREA ACBS must be {NPC_ACBS_BYTES} bytes in {record.form_id:08x}"
        )
    actor_flags = struct.unpack_from("<I", acbs[0])[0]
    models = values.get("MODL", [])
    if len(models) > 1:
        raise ValueError(f"CREA declares multiple skeleton models in {record.form_id:08x}")
    inventory = []
    for data in values.get("CNTO", []):
        if len(data) != NPC_INVENTORY_ENTRY_BYTES:
            raise ValueError(
                f"CREA CNTO must be {NPC_INVENTORY_ENTRY_BYTES} bytes in {record.form_id:08x}"
            )
        item_form_id, count = struct.unpack("<Ii", data)
        inventory.append(ActorItem(item_form_id, count))
    variants = tuple(
        path
        for data in values.get("NIFZ", [])
        for path in _model_paths(data)
    )
    return CreatureActor(
        record.form_id,
        _first_text(values, "EDID"),
        _first_text(values, "FULL"),
        _canonical_model(models[0]) if models else None,
        actor_flags,
        variants,
        tuple(inventory),
        _optional_form(values, record, "TPLT"),
        _template_flags(values, record),
    )


def _cell_parent(record: Record) -> int | None:
    for group in reversed(record.groups):
        if group.group_type == CELL_CHILDREN_GROUP_TYPE:
            return group.label_u32
    return None


def _reference(record: Record, subrecords: list[tuple[str, bytes]]) -> ActorReference | None:
    values = _values(subrecords)
    cell_form_id = _cell_parent(record)
    if cell_form_id is None or not values.get("NAME") or not values.get("DATA"):
        return None
    transform = values["DATA"][0]
    if len(transform) != REFERENCE_TRANSFORM_BYTES:
        raise ValueError(
            f"{record.signature} DATA must be {REFERENCE_TRANSFORM_BYTES} bytes in {record.form_id:08x}"
        )
    values6 = struct.unpack(f"<{REFERENCE_TRANSFORM_FLOATS}f", transform)
    scales = values.get("XSCL", [])
    if scales and (len(scales) != 1 or len(scales[0]) != REFERENCE_SCALE_BYTES):
        raise ValueError(
            f"{record.signature} XSCL must contain one {REFERENCE_SCALE_BYTES}-byte scale in "
            f"{record.form_id:08x}"
        )
    scale = struct.unpack("<f", scales[0])[0] if scales else DEFAULT_REFERENCE_SCALE
    if not scale > 0.0:
        raise ValueError(f"{record.signature} XSCL must be positive in {record.form_id:08x}")
    enable_parents = values.get("XESP", [])
    if enable_parents and (
        len(enable_parents) != 1 or len(enable_parents[0]) != ENABLE_PARENT_BYTES
    ):
        raise ValueError(
            f"{record.signature} XESP must contain one enable-parent FormID in "
            f"{record.form_id:08x}"
        )
    enable_parent_form_id = (
        struct.unpack_from("<I", enable_parents[0])[0] if enable_parents else None
    )
    enable_parent_opposite = bool(
        enable_parents
        and struct.unpack_from(
            "<I", enable_parents[0], ENABLE_PARENT_OPPOSITE_OFFSET
        )[0]
        & ENABLE_PARENT_OPPOSITE_FLAG
    )
    return ActorReference(
        record.form_id,
        record.signature,
        cell_form_id,
        _form_id(values["NAME"][0], record, "NAME"),
        record.flags,
        tuple(values6[:3]),
        tuple(values6[3:]),
        scale,
        enable_parent_form_id,
        enable_parent_opposite,
    )


@cache
def scan_actor_catalog(path: Path) -> ActorCatalog:
    catalog = ActorCatalog({}, {}, [], {}, {}, {}, {})
    for record in iter_plugin_records(path, ACTOR_RECORD_TYPES):
        catalog.record_counts[record.signature] = (
            catalog.record_counts.get(record.signature, 0) + 1
        )
        catalog.record_flags.setdefault(record.signature, {})[record.form_id] = record.flags
        catalog.record_data_sha256.setdefault(record.signature, {})[
            record.form_id
        ] = hashlib.sha256(record.data).hexdigest()
        if record.flags & DELETED_RECORD_FLAG:
            catalog.deleted_form_ids.setdefault(record.signature, set()).add(record.form_id)
            continue
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
        elif record.signature == "LVLI":
            catalog.leveled_lists[record.form_id] = _leveled_list(record, subrecords)
        elif record.signature in {"LVLC", "LVLN"}:
            catalog.actor_leveled_lists[record.form_id] = _leveled_actor_list(
                record,
                subrecords,
            )
    return catalog


def resolve_actor_outfit_form_ids(catalog: ActorCatalog, actor: HumanoidActor) -> tuple[int, ...]:
    resolved: list[int] = []
    for item in actor.inventory:
        resolved.extend(_resolve_outfit_item(catalog, item.form_id, ()))
    return tuple(dict.fromkeys(resolved))


def _resolve_outfit_item(
    catalog: ActorCatalog,
    form_id: int,
    stack: tuple[int, ...],
) -> tuple[int, ...]:
    if form_id in catalog.armor:
        return (form_id,)
    leveled = catalog.leveled_lists.get(form_id)
    if leveled is None:
        return ()
    if form_id in stack:
        chain = " -> ".join(f"{value:08x}" for value in (*stack, form_id))
        raise ValueError(f"Actor outfit leveled-list cycle: {chain}")
    if not leveled.entries:
        return ()
    outcomes = {
        _resolve_outfit_item(catalog, entry.form_id, (*stack, form_id))
        for entry in leveled.entries
    }
    if len(outcomes) != 1:
        raise ValueError(
            f"Actor outfit LVLI {leveled.editor_id} has nondeterministic armor outcomes: {outcomes}"
        )
    return next(iter(outcomes))
