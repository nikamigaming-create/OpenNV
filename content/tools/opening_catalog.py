"""Compile the owned Fallout opening route and UI into one private manifest."""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import shutil
import struct
import subprocess
import urllib.request
from collections import defaultdict, deque
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Iterator

from actor_gltf import (
    animation_sequence_manifest,
    authored_rigid_attachment_node,
    furniture_marker_manifest,
    sample_root_motion,
    sample_transform_animation,
)
from bsa_archive import BsaArchive, ExtractedMember, canonical_member_path
from cell_scene import godot_rotation_quaternion
from environment_catalog import parse_image_space_modifier
from export_static_nif_gltf import export_static_nif
from material_contract import material_bindings, texture_binding_requests
from owned_archive_stack import OwnedArchiveStack
from plugin_records import Record, iter_plugin_records, iter_subrecords, zstring
from runtime_configuration import RuntimeConfiguration
from texture_pipeline import OwnedTexturePipeline
from compiler_provenance import compiler_provenance


OPENING_RECIPE_SCHEMA = "opennv-owned-opening-recipe/v1"
OPENING_MANIFEST_SCHEMA = "opennv-owned-opening-manifest/v1"
OPENING_MANIFEST_STATUS = "compiled-owned-opening-graph"
GAMEPLAY_VITALS_SCHEMA = "opennv-owned-gameplay-vitals/v1"
PLAYER_BASE_EDITOR_ID = "Player"
PLAYER_BASE_LEVEL_OFFSET = 8
PLAYER_BASE_HEALTH_OFFSET = 0
PLAYER_BASE_ACBS_BYTES = 24
PLAYER_BASE_DATA_MINIMUM_BYTES = 11
RACE_DATA_BYTES = 36
RACE_FLAGS_OFFSET = 32
RACE_PLAYABLE_FLAG = 0x01
APPEARANCE_PART_DATA_BYTES = 1
APPEARANCE_PART_PLAYABLE_FLAG = 0x01
HAIR_FEMALE_FLAG = 0x02
HAIR_MALE_FLAG = 0x04
FACEGEN_SYMMETRIC_GEOMETRY_FLOATS = 50
FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS = 30
FACEGEN_SYMMETRIC_TEXTURE_FLOATS = 50
FNV_ENGINE_BUILD = "1.4.0.525"
FNV_ENGINE_DEFAULT_XP_BASE_EVIDENCE = "fnv-1.4.0.525-gmst-ixpbase-v1"
# FalloutNV.exe owns this default; FalloutNV.esm intentionally has no GMST override.
# The value was recovered from the exact 1.4.0.525 engine setting and is emitted with
# explicit engine-default provenance, never as a recipe/runtime fallback.
FNV_ENGINE_DEFAULT_XP_BASE = 200
REQUIRED_VITAL_GAME_SETTINGS = (
    "fAVDHealthEnduranceMult",
    "fAVDHealthLevelMult",
    "fAVDActionPointsBase",
    "fAVDActionPointsMult",
    "iXPBumpBase",
)
REQUIRED_VITAL_ACTOR_VALUES = (
    "AVHealth",
    "AVActionPoints",
    "AVXP",
    "AVEndurance",
    "AVAgility",
)
FORM_ID_BYTES = 4
QUEST_STAGE_INDEX_BYTES = 2
FORM_ID_HEX_CHARACTERS = 8
FORM_ID_RADIX = 16
MENU_TEXT_ENCODING = "cp1252"
COMMENT_PATTERN = re.compile(r"<!--.*?-->", re.DOTALL)
TAG_PATTERN = re.compile(
    r"<(?P<closing>/)?(?P<name>[A-Za-z_][A-Za-z0-9_-]*)(?P<attributes>[^>]*)>",
    re.DOTALL,
)
ATTRIBUTE_PATTERN = re.compile(
    r"(?P<name>[A-Za-z_][A-Za-z0-9_-]*)\s*=\s*([\"'])(?P<value>.*?)\2",
    re.DOTALL,
)
ENTITY_PATTERN = re.compile(r"&(?P<name>[^;]+);")
PLAY_BINK_PATTERN = re.compile(
    r"\bPlayBink\s+[\"'](?P<path>[^\"']+\.bik)[\"']",
    re.IGNORECASE,
)
PLAIN_ASSET_PATTERN = re.compile(r"^[^<>&]+\.(?:dds|nif|kf)$", re.IGNORECASE)
TEXT_SUBRECORDS = frozenset(
    {
        "EDID",
        "FULL",
        "DESC",
        "SCTX",
        "NAM1",
        "NAM2",
        "RNAM",
        "ITXT",
        "ICON",
        "NNAM",
    }
)
MANIFEST_FORM_LINK_SUBRECORDS = frozenset(
    {"SCRO", "SCRI", "NAME", "QSTI", "PKID", "TCLT", "TCLF"}
)
CONDITION_BYTES = 28
DIALOGUE_DATA_BYTES = 4
CONDITION_FUNCTION_OFFSET = 8
CONDITION_PARAMETER_1_OFFSET = 12
CONDITION_PARAMETER_2_OFFSET = 16
CONDITION_RUN_ON_OFFSET = 20
CONDITION_REFERENCE_OFFSET = 24
DIALOGUE_TOPIC_GROUP_TYPE = 7
FONT_GLYPH_WIDTH_INDEX = 9
FONT_GLYPH_HEIGHT_INDEX = 10
FONT_GLYPH_HORIZONTAL_OFFSET_INDEX = 11
FONT_GLYPH_ADVANCE_EXTRA_INDEX = 12
FONT_GLYPH_VERTICAL_BEARING_INDEX = 13
FONT_GLYPH_U0_INDEX = 1
FONT_GLYPH_V0_INDEX = 2
FONT_GLYPH_U1_INDEX = 3
FONT_GLYPH_V1_INDEX = 6
DIALOGUE_FLAG_GOODBYE = 0x0001
DIALOGUE_FLAG_SAY_ONCE = 0x0004
VOICE_MEMBER_ROOT = "sound\\voice"
VOICE_AUDIO_EXTENSION = ".ogg"
VOICE_LIP_EXTENSION = ".lip"
VOICE_RESPONSE_SUFFIX_PATTERN = re.compile(
    r"_(?P<form>[0-9a-f]{8})_(?P<index>[1-9][0-9]*)\.ogg$",
    re.IGNORECASE,
)
GAMEBRYO_FONT_HEADER_BYTES = 296
GAMEBRYO_FONT_GLYPH_COUNT = 256
GAMEBRYO_FONT_ATLAS_NAME_OFFSET = 12
GAMEBRYO_FONT_GLYPH = struct.Struct("<14f")
BYTE_CHANNEL_MAXIMUM = 255
PACKAGE_DATA_MINIMUM_BYTES = 4
PACKAGE_DATA_FNV_BYTES = 12
PACKAGE_PROCEDURE_FLAGS_OFFSET = 6
PACKAGE_TYPE_SPECIFIC_FLAGS_OFFSET = 8
PACKAGE_IDLE_FLAGS_BYTES = 1
PACKAGE_IDLE_COUNT_BYTES = 1
PACKAGE_IDLE_TIMER_BYTES = 4
PACKAGE_IDLE_FORM_BYTES = 4
PACKAGE_LOCATION_BYTES = 12
PACKAGE_TARGET_BYTES = 16
REFERENCE_TRANSFORM_BYTES = 24
DOC_INITIAL_CHAIR_MARKER_ID = 14
FURNITURE_MARKER_PLACEMENT_SEMANTICS = (
    "replace-marker-offset-for-actor-placement"
)
FURNITURE_MARKER_PLACEMENT_AXES = ("x", "y", "z")
CONDITION_OPERATOR_GREATER_OR_EQUAL = 0x60
PLAYER_CONTROL_ARGUMENTS = (
    "movement",
    "pipBoy",
    "fighting",
    "pointOfView",
    "looking",
    "rolloverText",
    "sneaking",
)
OPENING_COMMAND_CONTRACT_SCHEMA = "opennv-owned-opening-command-contract/v1"
OPENING_COMMAND_KINDS = frozenset(
    {
        "achievement",
        "actorIntent",
        "actorValueDelta",
        "additem",
        "addScriptPackage",
        "autoDisplayObjectives",
        "autosave",
        "deferredStage",
        "equipitem",
        "imageSpaceModifier",
        "objective",
        "playerControls",
        "playIdle",
        "referenceEnabled",
        "removeitem",
        "removeScriptPackage",
        "sayTo",
        "setDestroyed",
        "setGlobal",
        "setQuestVariable",
        "setStage",
        "setTimer",
        "showMenu",
        "startQuest",
        "stopQuest",
    }
)
COMMAND_RECORD_FIELDS = (
    ("itemEditorId", "itemFormId", "itemRecordType", None),
    ("questEditorId", "questFormId", "questRecordType", frozenset({"QUST"})),
    ("globalEditorId", "globalFormId", "globalRecordType", frozenset({"GLOB"})),
    ("ownerEditorId", "ownerFormId", "ownerRecordType", frozenset({"QUST"})),
    (
        "referenceEditorId",
        "referenceFormId",
        "referenceRecordType",
        frozenset({"REFR", "ACHR", "ACRE"}),
    ),
)


@dataclass(frozen=True)
class IndexedRecord:
    signature: str
    form_id: int
    editor_id: str | None
    links: tuple[tuple[str, int], ...]
    groups: tuple[tuple[int, int], ...]
    data_sha256: str


@dataclass(frozen=True)
class IdleAnimationSource:
    form_id: int
    editor_id: str
    logical_path: str
    record_sha256: str = ""


@dataclass(frozen=True)
class AnimationObjectSource:
    form_id: int
    editor_id: str
    logical_path: str
    idle_form_id: int
    record_sha256: str


@dataclass(frozen=True)
class ReferenceTransformSource:
    form_id: int
    editor_id: str | None
    record_type: str
    position_game_units: tuple[float, float, float]
    rotation_radians: tuple[float, float, float]
    base_form_id: int | None = None
    record_sha256: str = ""

    def manifest(self) -> dict[str, object]:
        return {
            "formId": form_id_text(self.form_id),
            "editorId": self.editor_id,
            "recordType": self.record_type,
            "positionGameUnits": list(self.position_game_units),
            "rotationRadians": list(self.rotation_radians),
            "rotationGodotQuaternion": godot_rotation_quaternion(
                self.rotation_radians
            ),
        }


@dataclass(frozen=True)
class FlowSourceCatalog:
    actor_values: list[dict[str, object]]
    traits: list[dict[str, object]]
    scripts: dict[str, tuple[int, str]]
    idle_animations_by_editor: dict[str, IdleAnimationSource]
    idle_animations_by_form: dict[int, IdleAnimationSource]
    packages_by_editor: dict[str, Record]
    packages_by_form: dict[int, Record]
    actors_by_form: dict[int, Record]
    voice_types_by_form: dict[int, str]
    references_by_form: dict[int, ReferenceTransformSource]
    image_space_modifiers_by_editor: dict[str, Record]
    needed: dict[int, dict[str, object]]
    animation_objects_by_idle_form: dict[
        int, tuple[AnimationObjectSource, ...]
    ] = field(default_factory=dict)
    game_settings_by_editor: dict[str, Record] = field(default_factory=dict)
    player_base: Record | None = None
    furniture_by_form: dict[int, Record] = field(default_factory=dict)
    appearance_records_by_form: dict[int, Record] = field(default_factory=dict)


@dataclass
class TileNode:
    tag: str
    attributes: dict[str, str]
    text_parts: list[str] = field(default_factory=list)
    children: list["TileNode"] = field(default_factory=list)

    @property
    def text(self) -> str:
        return "".join(self.text_parts).strip()

    @property
    def name(self) -> str:
        return self.attributes.get("name", "")

    def child(self, tag: str) -> "TileNode | None":
        return next((value for value in self.children if value.tag == tag), None)

    def walk(self) -> Iterator["TileNode"]:
        yield self
        for child in self.children:
            yield from child.walk()


def file_sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def atomic_bytes(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def atomic_json(path: Path, document: object) -> None:
    atomic_bytes(
        path,
        (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8"),
    )


def form_id_text(value: int) -> str:
    return f"{value:0{FORM_ID_HEX_CHARACTERS}x}"


def canonical_ui_path(value: str) -> str:
    return canonical_member_path(value).casefold()


def load_opening_recipe(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != OPENING_RECIPE_SCHEMA:
        raise ValueError(f"Unexpected owned opening recipe: {path}")
    if document.get("id") != path.stem:
        raise ValueError(f"Owned opening recipe identity differs from its file: {path}")
    roots = document.get("rootEditorIds")
    graph = document.get("recordGraph")
    ui = document.get("ui")
    flow = document.get("newGameFlow")
    video_import = document.get("videoImport")
    if (
        not isinstance(roots, list)
        or not roots
        or len(set(str(value).casefold() for value in roots)) != len(roots)
        or not isinstance(graph, dict)
        or not isinstance(ui, dict)
        or not isinstance(flow, dict)
        or not isinstance(video_import, dict)
    ):
        raise ValueError(f"Owned opening recipe is incomplete: {path}")
    return document


def _subrecord_form_id(data: bytes) -> int | None:
    if len(data) < FORM_ID_BYTES:
        return None
    return struct.unpack_from("<I", data)[0]


def _record_editor_id(record: Record) -> str | None:
    for subrecord in iter_subrecords(record):
        if subrecord.signature == "EDID":
            return zstring(subrecord.data)
    return None


def index_records(
    master_path: Path,
    universal_link_signatures: frozenset[str],
    record_link_signatures: dict[str, frozenset[str]],
) -> tuple[
    dict[int, IndexedRecord],
    dict[str, tuple[int, ...]],
    dict[tuple[int, int], tuple[int, ...]],
    dict[tuple[str, int], tuple[int, ...]],
]:
    by_form: dict[int, IndexedRecord] = {}
    editor_rows: dict[str, list[int]] = defaultdict(list)
    group_children: dict[tuple[int, int], list[int]] = defaultdict(list)
    reverse_links: dict[tuple[str, int], list[int]] = defaultdict(list)
    for record in iter_plugin_records(master_path):
        links: list[tuple[str, int]] = []
        editor_id = None
        link_signatures = universal_link_signatures | record_link_signatures.get(
            record.signature,
            frozenset(),
        )
        for subrecord in iter_subrecords(record):
            if subrecord.signature == "EDID":
                editor_id = zstring(subrecord.data)
            if subrecord.signature in link_signatures:
                target = _subrecord_form_id(subrecord.data)
                if target:
                    links.append((subrecord.signature, target))
                    reverse_links[(subrecord.signature, target)].append(record.form_id)
        groups = tuple((group.group_type, group.label_u32) for group in record.groups)
        row = IndexedRecord(
            record.signature,
            record.form_id,
            editor_id,
            tuple(links),
            groups,
            hashlib.sha256(record.data).hexdigest(),
        )
        if record.form_id in by_form:
            raise ValueError(f"Duplicate form identity in owned master: {form_id_text(record.form_id)}")
        by_form[record.form_id] = row
        if editor_id:
            editor_rows[editor_id.casefold()].append(record.form_id)
        for group_type, label in groups:
            group_children[(group_type, label)].append(record.form_id)
    return (
        by_form,
        {key: tuple(value) for key, value in editor_rows.items()},
        {key: tuple(value) for key, value in group_children.items()},
        {key: tuple(value) for key, value in reverse_links.items()},
    )


def record_graph_closure(
    roots: Iterable[str],
    by_form: dict[int, IndexedRecord],
    by_editor_id: dict[str, tuple[int, ...]],
    group_children: dict[tuple[int, int], tuple[int, ...]],
    reverse_links: dict[tuple[str, int], tuple[int, ...]],
    reverse_signatures: frozenset[str],
    engine_forms: frozenset[int],
    parent_group_types: frozenset[int],
    child_group_types: dict[str, frozenset[int]],
) -> tuple[frozenset[int], tuple[str, ...]]:
    selected: set[int] = set()
    blockers: list[str] = []
    root_forms: list[int] = []
    for editor_id in roots:
        matches = by_editor_id.get(editor_id.casefold(), ())
        if len(matches) != 1:
            blockers.append(f"root-editor-id:{editor_id}:matches={len(matches)}")
            continue
        root_forms.append(matches[0])
        selected.add(matches[0])

    def add_links(source_forms: Iterable[int]) -> set[int]:
        added: set[int] = set()
        for source in source_forms:
            row = by_form[source]
            for signature, target in row.links:
                if target in engine_forms:
                    continue
                if target not in by_form:
                    blockers.append(
                        f"missing-form-link:source={form_id_text(source)}:"
                        f"signature={signature}:target={form_id_text(target)}"
                    )
                    continue
                selected.add(target)
                added.add(target)
        return added

    root_dependencies = add_links(root_forms)
    attached_scripts = {
        target
        for source in root_forms
        for signature, target in by_form[source].links
        if signature == "SCRI"
        and target in by_form
        and by_form[target].signature == "SCPT"
    }
    add_links(attached_scripts)

    dialogue_topics = {
        source
        for root in root_forms
        for signature in reverse_signatures
        for source in reverse_links.get((signature, root), ())
        if source in by_form and by_form[source].signature == "DIAL"
    }
    selected.update(dialogue_topics)
    dialogue_children = {
        child
        for topic in dialogue_topics
        for group_type in child_group_types.get("DIAL", frozenset())
        for child in group_children.get((group_type, topic), ())
    }
    selected.update(dialogue_children)
    add_links(dialogue_children)

    for current in tuple(selected | root_dependencies):
        row = by_form.get(current)
        if row is None:
            continue
        for group_type, label in row.groups:
            if group_type not in parent_group_types:
                continue
            if label in by_form:
                selected.add(label)
            else:
                blockers.append(
                    f"missing-parent-form:source={form_id_text(current)}:"
                    f"group={group_type}:target={form_id_text(label)}"
                )
    return frozenset(selected), tuple(sorted(set(blockers)))


def _safe_text(data: bytes) -> str | None:
    try:
        value = zstring(data)
    except (UnicodeDecodeError, ValueError):
        return None
    return value if all(character.isprintable() or character in "\r\n\t" for character in value) else None


def _condition_manifest(data: bytes) -> dict[str, object]:
    if len(data) != CONDITION_BYTES:
        raise ValueError(f"Owned dialogue condition has an unexpected size: {len(data)}")
    return {
        "operatorFlags": data[0],
        "comparisonValue": struct.unpack_from("<f", data, 4)[0],
        "function": struct.unpack_from("<H", data, CONDITION_FUNCTION_OFFSET)[0],
        "parameter1": form_id_text(
            struct.unpack_from("<I", data, CONDITION_PARAMETER_1_OFFSET)[0]
        ),
        "parameter2": struct.unpack_from(
            "<I", data, CONDITION_PARAMETER_2_OFFSET
        )[0],
        "runOn": struct.unpack_from("<I", data, CONDITION_RUN_ON_OFFSET)[0],
        "reference": form_id_text(
            struct.unpack_from("<I", data, CONDITION_REFERENCE_OFFSET)[0]
        ),
    }


def selected_record_manifest(master_path: Path, selected: frozenset[int]) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    source_order = 0
    for record in iter_plugin_records(master_path):
        if record.form_id not in selected:
            continue
        source_order += 1
        stage = None
        objective_index = None
        stage_scripts: list[dict[str, object]] = []
        quest_objectives: list[dict[str, object]] = []
        texts: list[dict[str, str]] = []
        links: list[dict[str, str]] = []
        conditions: list[dict[str, object]] = []
        dialogue_data = None
        inventory: list[dict[str, object]] = []
        for subrecord in iter_subrecords(record):
            inventory.append(
                {
                    "signature": subrecord.signature,
                    "bytes": len(subrecord.data),
                    "sha256": hashlib.sha256(subrecord.data).hexdigest(),
                }
            )
            if subrecord.signature == "INDX" and len(subrecord.data) == QUEST_STAGE_INDEX_BYTES:
                stage = struct.unpack("<H", subrecord.data)[0]
            if subrecord.signature == "QOBJ" and len(subrecord.data) == FORM_ID_BYTES:
                objective_index = struct.unpack("<I", subrecord.data)[0]
            if subrecord.signature in TEXT_SUBRECORDS:
                value = _safe_text(subrecord.data)
                if value is not None:
                    texts.append({"signature": subrecord.signature, "value": value})
                    if subrecord.signature == "SCTX" and stage is not None:
                        stage_scripts.append({"stage": stage, "source": value})
                    if subrecord.signature == "NNAM" and objective_index is not None:
                        quest_objectives.append(
                            {"index": objective_index, "text": value}
                        )
            if subrecord.signature == "CTDA":
                conditions.append(_condition_manifest(subrecord.data))
            if (
                record.signature == "INFO"
                and subrecord.signature == "DATA"
                and len(subrecord.data) == DIALOGUE_DATA_BYTES
                and dialogue_data is None
            ):
                dialogue_data = {
                    "responseType": subrecord.data[0],
                    "flags": struct.unpack_from("<H", subrecord.data, 2)[0],
                }
            if subrecord.signature in MANIFEST_FORM_LINK_SUBRECORDS:
                target = _subrecord_form_id(subrecord.data)
                if target:
                    links.append(
                        {
                            "signature": subrecord.signature,
                            "formId": form_id_text(target),
                        }
                    )
        rows.append(
            {
                "recordType": record.signature,
                "formId": form_id_text(record.form_id),
                "sourceOrder": source_order,
                "flags": f"0x{record.flags:08x}",
                "groups": [
                    {"type": group.group_type, "label": form_id_text(group.label_u32)}
                    for group in record.groups
                ],
                "dataSha256": hashlib.sha256(record.data).hexdigest(),
                "text": texts,
                "links": links,
                "conditions": conditions,
                "dialogueData": dialogue_data,
                "questStageScripts": stage_scripts,
                "questObjectives": quest_objectives,
                "subrecords": inventory,
            }
        )
    rows.sort(key=lambda row: int(str(row["formId"]), FORM_ID_RADIX))
    if len(rows) != len(selected):
        raise ValueError(
            f"Owned opening graph record join differs: selected={len(selected)} emitted={len(rows)}"
        )
    return rows


def parse_tile_document(payload: bytes) -> TileNode:
    text = payload.decode(MENU_TEXT_ENCODING)
    text = COMMENT_PATTERN.sub("", text)
    synthetic = TileNode("document", {})
    stack = [synthetic]
    cursor = 0
    for match in TAG_PATTERN.finditer(text):
        if match.start() > cursor:
            stack[-1].text_parts.append(text[cursor : match.start()])
        name = match.group("name").casefold()
        closing = bool(match.group("closing"))
        attributes_text = match.group("attributes")
        self_closing = attributes_text.rstrip().endswith("/")
        if closing:
            if len(stack) == 1:
                cursor = match.end()
                continue
            matching_index = next(
                (
                    index
                    for index in range(len(stack) - 1, 0, -1)
                    if stack[index].tag == name
                ),
                None,
            )
            if matching_index is None:
                stack.pop()
            else:
                del stack[matching_index:]
        else:
            attributes = {
                value.group("name").casefold(): value.group("value")
                for value in ATTRIBUTE_PATTERN.finditer(attributes_text)
            }
            node = TileNode(name, attributes)
            stack[-1].children.append(node)
            if not self_closing:
                stack.append(node)
        cursor = match.end()
    if cursor < len(text):
        stack[-1].text_parts.append(text[cursor:])
    return synthetic


def _direct_number(node: TileNode | None) -> float | None:
    if node is None or node.children:
        return None
    try:
        return float(node.text)
    except ValueError:
        return None


def _expression_operand(
    node: TileNode,
    screen: dict[str, float],
    traits: dict[str, float],
    entities: dict[str, float] | None = None,
) -> float:
    source = node.attributes.get("src")
    trait = node.attributes.get("trait", "")
    if source == "screen()":
        if trait not in screen:
            raise ValueError(f"Owned menu screen trait is unavailable: {trait}")
        return screen[trait]
    if source == "me()":
        if trait not in traits:
            raise ValueError(f"Owned menu self trait is unavailable: {trait}")
        return traits[trait]
    if source:
        raise ValueError(f"Owned menu expression source is unsupported: {source}")
    if node.children:
        return _evaluate_expression(node, screen, traits, entities)
    entity = _entity_name(node.text)
    if entity is not None:
        if entities is None or entity not in entities:
            raise ValueError(f"Owned menu entity is unavailable: {entity}")
        return entities[entity]
    return float(node.text)


def _evaluate_expression(
    node: TileNode,
    screen: dict[str, float],
    traits: dict[str, float],
    entities: dict[str, float] | None = None,
) -> float:
    if not node.children:
        return float(node.text)
    value = None
    for operation in node.children:
        operand = _expression_operand(operation, screen, traits, entities)
        if operation.tag == "copy":
            value = operand
        elif value is None:
            raise ValueError(f"Owned menu expression begins with {operation.tag}")
        elif operation.tag == "add":
            value += operand
        elif operation.tag == "sub":
            value -= operand
        elif operation.tag in {"mul", "mult"}:
            value *= operand
        elif operation.tag == "div":
            value /= operand
        elif operation.tag == "min":
            value = min(value, operand)
        elif operation.tag == "max":
            value = max(value, operand)
        elif operation.tag == "onlyif":
            value = value if operand != 0.0 else 0.0
        elif operation.tag == "onlyifnot":
            value = value if operand == 0.0 else 0.0
        else:
            raise ValueError(f"Owned menu arithmetic operation is unsupported: {operation.tag}")
    if value is None:
        raise ValueError("Owned menu expression is empty")
    return value


def _boot_layout(
    container: TileNode,
    buttons: list[dict[str, object]],
    title_tile: str,
    configuration: RuntimeConfiguration,
    font: dict[str, object],
    button_style: dict[str, object],
) -> dict[str, object]:
    capture = configuration.document["capture"]
    if not isinstance(capture, dict):
        raise ValueError("OpenNV capture configuration is invalid")
    screen = {
        "width": float(capture["expectedWidthPixels"]),
        "height": float(capture["expectedHeightPixels"]),
        "cropx": 0.0,
        "cropy": 0.0,
    }
    tile_child_count = sum(
        child.tag in {"image", "rect", "hotrect", "text", "nif"}
        for child in container.children
    )
    traits = {
        "childcount": float(tile_child_count),
    }
    for name in ("_spacing", "_starty", "width"):
        value = _direct_number(container.child(name))
        if value is None:
            raise ValueError(f"Owned boot menu trait is not numeric: {name}")
        traits[name] = value
    for name in ("height", "x", "y"):
        node = container.child(name)
        if node is None:
            raise ValueError(f"Owned boot menu trait is absent: {name}")
        traits[name] = _evaluate_expression(node, screen, traits)
    button_container_left = traits["x"] - traits["width"]
    button_rows = []
    for index, button in enumerate(buttons):
        width = (
            _text_width(str(button["label"]), font)
            + float(button_style["horizontalPaddingPixels"])
        )
        height = (
            float(font["lineHeightPixels"])
            + float(button_style["verticalPaddingPixels"])
        )
        button_rows.append(
            {
                **button,
                "rect": [
                    traits["x"] - width,
                    traits["y"] + traits["_starty"] + index * traits["_spacing"],
                    width,
                    height,
                ],
            }
        )
    title_nodes = [node for node in container.children if node.name == title_tile]
    if len(title_nodes) != 1:
        raise ValueError("Owned boot menu title tile does not resolve uniquely")
    title = title_nodes[0]
    title_values = {
        name: _direct_number(title.child(name))
        for name in ("x", "y", "width", "height")
    }
    if any(value is None for value in title_values.values()):
        raise ValueError("Owned boot menu title geometry is not numeric")
    return {
        "canvasSize": [screen["width"], screen["height"]],
        "buttonContainerRect": [
            button_container_left,
            traits["y"],
            traits["width"],
            traits["height"],
        ],
        "buttons": button_rows,
        "titleRect": [
            traits["x"] + float(title_values["x"]),
            traits["y"] + float(title_values["y"]),
            float(title_values["width"]),
            float(title_values["height"]),
        ],
    }
def _entity_name(value: str) -> str | None:
    match = ENTITY_PATTERN.fullmatch(value.strip())
    return None if match is None else match.group("name")


def _display_entity(value: str) -> str:
    entity = _entity_name(value)
    if entity is None:
        return value.strip()
    normalized = entity.removeprefix("-").removeprefix("s")
    return re.sub(r"(?<!^)(?=[A-Z])", " ", normalized).strip()


def _resolve_include(
    source: str,
    owner: str,
    available: frozenset[str],
    search_roots: tuple[str, ...],
) -> str | None:
    source_path = canonical_ui_path(source)
    candidates = [
        canonical_ui_path(str(Path(owner.replace("\\", "/")).parent / source_path.replace("\\", "/"))),
        source_path,
    ]
    candidates.extend(canonical_ui_path(f"{root}\\{source_path}") for root in search_roots)
    return next((candidate for candidate in candidates if candidate in available), None)


def _ini_index(path: Path) -> dict[tuple[str, str], list[str]]:
    section = ""
    values: dict[tuple[str, str], list[str]] = defaultdict(list)
    for raw_line in path.read_text(encoding=MENU_TEXT_ENCODING).splitlines():
        line = raw_line.strip()
        if not line or line.startswith((";", "#")):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1].strip()
            continue
        if "=" not in line:
            continue
        key, value = (part.strip() for part in line.split("=", 1))
        identity = (section.casefold(), key.casefold())
        values[identity].append(value)
    return dict(values)


def _ini_setting(
    values: dict[tuple[str, str], list[str]],
    section: object,
    key: object,
) -> str:
    identity = (str(section).casefold(), str(key).casefold())
    matches = values.get(identity, [])
    if len(matches) != 1 or not matches[0]:
        raise ValueError(
            f"Owned default INI setting does not resolve uniquely: [{section}] {key}"
        )
    return matches[0]


def _gameplay_system_color(
    configured: dict[str, object],
    default_ini_path: Path,
    preferences_ini_path: Path | None,
) -> dict[str, object]:
    section = str(configured["section"])
    if preferences_ini_path is not None:
        resolved = preferences_ini_path.resolve()
        if not resolved.is_file():
            raise FileNotFoundError(
                f"Owned Fallout preferences INI is unavailable: {resolved}"
            )
        packed = int(
            _ini_setting(
                _ini_index(resolved),
                section,
                configured["packedPreferenceKey"],
            ),
            10,
        )
        if packed < 0 or packed > 0xFFFFFFFF:
            raise ValueError("Owned Pip-Boy packed system color is outside uint32")
        rgba = [
            (packed >> 24) & 0xFF,
            (packed >> 16) & 0xFF,
            (packed >> 8) & 0xFF,
            packed & 0xFF,
        ]
        return {
            "rgba": rgba,
            "setting": str(configured["packedPreferenceKey"]),
            "source": str(resolved),
            "sourceSha256": file_sha256(resolved),
        }

    values = _ini_index(default_ini_path)
    rgb = [
        int(_ini_setting(values, section, configured[key]))
        for key in ("fallbackRedKey", "fallbackGreenKey", "fallbackBlueKey")
    ]
    if any(value < 0 or value > 0xFF for value in rgb):
        raise ValueError("Owned default Pip-Boy system color is outside byte range")
    return {
        "rgba": [*rgb, 0xFF],
        "setting": "fallback-components",
        "source": str(default_ini_path.resolve()),
        "sourceSha256": file_sha256(default_ini_path),
    }


def _configured_relative_path(template: object, value: str, label: str) -> str:
    template_text = str(template)
    if template_text.count("{value}") != 1:
        raise ValueError(f"Owned {label} path template must contain one {{value}} token")
    rendered = template_text.replace("{value}", value).replace("/", "\\")
    parts = tuple(part for part in rendered.split("\\") if part)
    if not parts or any(part in {".", ".."} for part in parts):
        raise ValueError(f"Owned {label} path is not a safe relative path: {rendered}")
    return "\\".join(parts)


def _case_insensitive_descendant(root: Path, relative_path: str) -> Path:
    current = root.resolve()
    for part in relative_path.replace("\\", "/").split("/"):
        matches = [
            candidate
            for candidate in current.iterdir()
            if candidate.name.casefold() == part.casefold()
        ]
        if len(matches) != 1:
            raise FileNotFoundError(
                f"Owned loose path component does not resolve uniquely: {current} / {part}"
            )
        current = matches[0]
    if not current.is_file():
        raise FileNotFoundError(f"Owned loose asset is not a file: {current}")
    return current


def _single_added_constant(node: TileNode | None, label: str) -> float:
    if node is None:
        raise ValueError(f"Owned button prefab expression is absent: {label}")
    values = [
        _direct_number(child)
        for child in node.children
        if child.tag == "add" and not child.attributes
    ]
    if len(values) != 1 or values[0] is None:
        raise ValueError(f"Owned button prefab padding is ambiguous: {label}")
    return float(values[0])


def _gamebryo_font_manifest(
    logical_path: str,
    payload: bytes,
    source_path: Path,
    source_archive: str,
    source_archive_sha256: str,
    atlas: dict[str, object],
) -> dict[str, object]:
    expected_bytes = (
        GAMEBRYO_FONT_HEADER_BYTES
        + GAMEBRYO_FONT_GLYPH_COUNT * GAMEBRYO_FONT_GLYPH.size
    )
    if len(payload) != expected_bytes:
        raise ValueError(
            f"Owned Gamebryo font size differs: expected={expected_bytes} actual={len(payload)}"
        )
    line_height, atlas_count, font_version = struct.unpack_from("<fII", payload)
    if line_height <= 0.0 or atlas_count != 1 or font_version != 1:
        raise ValueError("Owned Gamebryo font header is unsupported")
    atlas_name = payload[
        GAMEBRYO_FONT_ATLAS_NAME_OFFSET:GAMEBRYO_FONT_HEADER_BYTES
    ].split(b"\0", 1)[0].decode(MENU_TEXT_ENCODING)
    if not atlas_name:
        raise ValueError("Owned Gamebryo font atlas name is empty")
    atlas_width = int(atlas["width"])
    atlas_height = int(atlas["height"])
    glyphs = []
    for codepoint in range(GAMEBRYO_FONT_GLYPH_COUNT):
        values = GAMEBRYO_FONT_GLYPH.unpack_from(
            payload,
            GAMEBRYO_FONT_HEADER_BYTES + codepoint * GAMEBRYO_FONT_GLYPH.size,
        )
        texture_index = int(values[0])
        if float(texture_index) != values[0] or texture_index != 0:
            raise ValueError(
                f"Owned Gamebryo font glyph has an unsupported atlas index: {codepoint}"
            )
        width = values[FONT_GLYPH_WIDTH_INDEX]
        height = values[FONT_GLYPH_HEIGHT_INDEX]
        horizontal_offset = values[FONT_GLYPH_HORIZONTAL_OFFSET_INDEX]
        advance_extra = values[FONT_GLYPH_ADVANCE_EXTRA_INDEX]
        vertical_bearing = values[FONT_GLYPH_VERTICAL_BEARING_INDEX]
        if width < 0.0 or height < 0.0:
            raise ValueError(f"Owned Gamebryo font glyph is negative: {codepoint}")
        advance = width + advance_extra
        if advance <= 0.0:
            continue
        u0 = values[FONT_GLYPH_U0_INDEX]
        v0 = values[FONT_GLYPH_V0_INDEX]
        u1 = values[FONT_GLYPH_U1_INDEX]
        first_v1 = values[FONT_GLYPH_V1_INDEX]
        uv_rect = [
            round(u0 * atlas_width),
            round(v0 * atlas_height),
            round((u1 - u0) * atlas_width),
            round((first_v1 - v0) * atlas_height),
        ]
        glyphs.append(
            {
                "codepoint": codepoint,
                "textureIndex": texture_index,
                "uvRectPixels": uv_rect,
                "sizePixels": [width, height],
                "horizontalOffsetPixels": horizontal_offset,
                "verticalBearingPixels": vertical_bearing,
                "advancePixels": advance,
            }
        )
    if not glyphs:
        raise ValueError("Owned Gamebryo font has no renderable glyphs")
    ascent = max(float(value["verticalBearingPixels"]) for value in glyphs)
    descent = max(
        max(
            0.0,
            float(value["sizePixels"][1])
            - float(value["verticalBearingPixels"]),
        )
        for value in glyphs
    )
    return {
        "schema": "opennv-owned-gamebryo-bitmap-font/v1",
        "logicalPath": logical_path,
        "source": str(source_path.resolve()),
        "bytes": len(payload),
        "sha256": hashlib.sha256(payload).hexdigest(),
        "sourceArchive": source_archive,
        "sourceArchiveSha256": source_archive_sha256,
        "lineHeightPixels": line_height,
        "ascentPixels": ascent,
        "descentPixels": descent,
        "atlasName": atlas_name,
        "atlas": atlas,
        "glyphs": glyphs,
    }


def _text_width(label: str, font: dict[str, object]) -> float:
    advances = {
        int(value["codepoint"]): float(value["advancePixels"])
        for value in font["glyphs"]
    }
    missing = [character for character in label if ord(character) not in advances]
    if missing:
        raise ValueError(
            "Owned opening font has no glyphs for label characters: "
            + ", ".join(sorted(set(missing)))
        )
    return sum(advances[ord(character)] for character in label)


def _compile_gamebryo_font(
    font_id: int,
    ini: dict[str, dict[str, str]],
    fonts_settings: dict[str, object],
    owned_archives: OwnedArchiveStack,
    cache_root: Path,
    texture_pipeline: OwnedTexturePipeline,
) -> tuple[dict[str, object], dict[str, object]]:
    font_key = str(fonts_settings["keyTemplate"]).replace("{id}", str(font_id))
    font_logical = canonical_member_path(
        _ini_setting(ini, fonts_settings["section"], font_key)
    )
    font_member = owned_archives.extract(font_logical)
    font_source = cache_root / "source" / "fonts" / Path(
        font_logical.replace("\\", "/")
    )
    atomic_bytes(font_source, font_member.data)
    atlas_name = font_member.data[
        GAMEBRYO_FONT_ATLAS_NAME_OFFSET:GAMEBRYO_FONT_HEADER_BYTES
    ].split(b"\0", 1)[0].decode(MENU_TEXT_ENCODING)
    atlas_logical = canonical_member_path(
        str(Path(font_logical.replace("\\", "/")).parent / f"{atlas_name}.dds")
    )
    atlas = texture_pipeline.prepare(atlas_logical).manifest()
    return (
        _gamebryo_font_manifest(
            font_logical,
            font_member.data,
            font_source,
            font_member.source_archive,
            font_member.source_archive_sha256,
            atlas,
        ),
        atlas,
    )


def _compile_engine_presentation(
    data_root: Path,
    default_ini_path: Path,
    owned_archives: OwnedArchiveStack,
    cache_root: Path,
    configuration: RuntimeConfiguration,
    recipe_ui: dict[str, object],
    boot_document: str,
    container: TileNode,
    buttons: list[dict[str, object]],
    available: frozenset[str],
    search_roots: tuple[str, ...],
    trees: dict[str, TileNode],
    texture_pipeline: OwnedTexturePipeline,
) -> tuple[dict[str, object], list[dict[str, object]]]:
    settings = dict(recipe_ui["engineSettings"])
    ini = _ini_index(default_ini_path)
    background_settings = dict(settings["background"])
    background_name = _ini_setting(
        ini,
        background_settings["section"],
        background_settings["key"],
    )
    background_logical = canonical_member_path(
        _configured_relative_path(
            background_settings["logicalPathTemplate"],
            background_name,
            "main-menu background",
        )
    )
    background = texture_pipeline.prepare(background_logical).manifest()

    music_settings = dict(settings["music"])
    music_name = _ini_setting(ini, music_settings["section"], music_settings["key"])
    music_relative = _configured_relative_path(
        music_settings["relativePathTemplate"],
        music_name,
        "main-menu music",
    )
    music_source = _case_insensitive_descendant(data_root, music_relative)
    music_cache = cache_root / "source" / "audio" / Path(
        music_relative.replace("\\", "/").casefold()
    )
    music_payload = music_source.read_bytes()
    atomic_bytes(music_cache, music_payload)
    music_volume = float(
        _ini_setting(
            ini,
            music_settings["volumeSection"],
            music_settings["volumeKey"],
        )
    )
    if not 0.0 <= music_volume <= 1.0:
        raise ValueError("Owned main-menu music volume is outside the normalized range")

    color_settings = dict(settings["color"])
    color = [
        int(_ini_setting(ini, color_settings["section"], color_settings[key]))
        for key in ("redKey", "greenKey", "blueKey")
    ]
    if any(channel < 0 or channel > BYTE_CHANNEL_MAXIMUM for channel in color):
        raise ValueError("Owned main-menu system color is outside the byte range")

    include_sources = {
        include.attributes["src"]
        for node in container.children
        if node.name in {str(value["tile"]) for value in buttons}
        for include in node.children
        if include.tag == "include" and include.attributes.get("src")
    }
    resolved_prefabs = {
        _resolve_include(source, boot_document, available, search_roots)
        for source in include_sources
    }
    if len(resolved_prefabs) != 1 or None in resolved_prefabs:
        raise ValueError("Owned opening buttons do not resolve to one prefab")
    prefab_document = next(iter(resolved_prefabs))
    assert prefab_document is not None
    prefab = trees[prefab_document]
    font_nodes = [node for node in prefab.walk() if node.tag == "_font"]
    if len(font_nodes) != 1 or _direct_number(font_nodes[0]) is None:
        raise ValueError("Owned opening button font id is ambiguous")
    font_id = int(_direct_number(font_nodes[0]))
    fonts_settings = dict(settings["fonts"])
    font, atlas = _compile_gamebryo_font(
        font_id,
        ini,
        fonts_settings,
        owned_archives,
        cache_root,
        texture_pipeline,
    )

    width_nodes = [node for node in prefab.children if node.tag == "width"]
    height_nodes = [node for node in prefab.children if node.tag == "height"]
    button_text_nodes = [node for node in prefab.walk() if node.name == "button_text"]
    if len(width_nodes) != 1 or len(height_nodes) != 1 or len(button_text_nodes) != 1:
        raise ValueError("Owned opening button prefab geometry is ambiguous")
    text_y = _direct_number(button_text_nodes[0].child("y"))
    if text_y is None:
        raise ValueError("Owned opening button text offset is not numeric")
    button_style = {
        "prefabDocument": prefab_document,
        "fontId": font_id,
        "horizontalPaddingPixels": _single_added_constant(width_nodes[0], "width"),
        "verticalPaddingPixels": _single_added_constant(height_nodes[0], "height"),
        "textOffsetYPixels": text_y,
    }

    globals_document = canonical_ui_path(str(settings["globalsDocument"]))
    if globals_document not in trees:
        raise ValueError("Owned global menu style document is absent")
    globals_tree = trees[globals_document]
    platform_entities = {
        str(name): 1.0 if bool(value) else 0.0
        for name, value in dict(settings["platformEntities"]).items()
    }
    style_traits = {}
    for trait in (str(value).casefold() for value in settings["styleTraits"]):
        nodes = [node for node in globals_tree.walk() if node.tag == trait]
        if len(nodes) != 1:
            raise ValueError(f"Owned global menu style trait is ambiguous: {trait}")
        direct = _direct_number(nodes[0])
        style_traits[trait] = (
            direct
            if direct is not None
            else _evaluate_expression(
                nodes[0],
                {"resolutionconverter": 1.0},
                {},
                platform_entities,
            )
        )

    return (
        {
            "defaultIni": {
                "file": default_ini_path.name,
                "source": str(default_ini_path.resolve()),
                "bytes": default_ini_path.stat().st_size,
                "sha256": file_sha256(default_ini_path),
            },
            "background": background,
            "music": {
                "logicalPath": music_relative,
                "installedSource": str(music_source.resolve()),
                "source": str(music_cache.resolve()),
                "bytes": len(music_payload),
                "sha256": hashlib.sha256(music_payload).hexdigest(),
                "volume": music_volume,
            },
            "mainMenuColorRgb": color,
            "nativeCanvasScale": 1.0,
            "platformEntities": platform_entities,
            "font": font,
            "buttonStyle": button_style,
            "globalStyleTraits": style_traits,
        },
        [background, atlas],
    )


def _asset_path(value: str) -> str | None:
    normalized = value.strip().replace("/", "\\").lstrip("\\")
    if PLAIN_ASSET_PATTERN.fullmatch(normalized) is None or "\\" not in normalized:
        return None
    lowered = normalized.casefold()
    if lowered.startswith("textures\\") or lowered.startswith("meshes\\"):
        return lowered
    if lowered.endswith(".dds"):
        return "textures\\" + lowered
    if lowered.endswith(".nif"):
        return "meshes\\" + lowered
    if lowered.endswith(".kf"):
        return "meshes\\" + lowered
    return None


def _document_index(member: str, payload: bytes) -> tuple[dict[str, object], TileNode]:
    root = parse_tile_document(payload)
    menu = next((node for node in root.children if node.tag == "menu"), None)
    includes = [
        node.attributes["src"]
        for node in root.walk()
        if node.tag == "include" and node.attributes.get("src")
    ]
    assets = []
    initially_visible_assets = []
    for node in root.walk():
        if node.tag != "filename" or node.children:
            continue
        normalized = _asset_path(node.text)
        if normalized is not None:
            assets.append(normalized)
    for owner in root.walk():
        filename = owner.child("filename")
        if filename is None or filename.children:
            continue
        normalized = _asset_path(filename.text)
        if normalized is None:
            continue
        visible = owner.child("visible")
        if visible is None or _entity_name(visible.text) != "false":
            initially_visible_assets.append(normalized)
    return (
        {
            "path": member,
            "bytes": len(payload),
            "sha256": hashlib.sha256(payload).hexdigest(),
            "menuName": None if menu is None else menu.name,
            "menuClassEntity": None
            if menu is None or menu.child("class") is None
            else _entity_name(menu.child("class").text),
            "includes": includes,
            "assetReferences": sorted(set(assets)),
            "initiallyVisibleAssetReferences": sorted(set(initially_visible_assets)),
        },
        root,
    )


def _tile_parent_index(root: TileNode) -> dict[int, TileNode]:
    parents: dict[int, TileNode] = {}

    def visit(node: TileNode) -> None:
        for child in node.children:
            parents[id(child)] = node
            visit(child)

    visit(root)
    return parents


def _named_tile(root: TileNode, name: str) -> TileNode:
    matches = [node for node in root.walk() if node.name.casefold() == name.casefold()]
    if len(matches) != 1:
        raise ValueError(f"Owned flow layout tile is ambiguous: {name} matches={len(matches)}")
    return matches[0]


def _layout_operand(
    operation: TileNode,
    trait_name: str,
    owner: TileNode,
    root: TileNode,
    parents: dict[int, TileNode],
    screen: dict[str, float],
    stack: set[tuple[int, str]],
) -> float:
    source = operation.attributes.get("src")
    source_trait = operation.attributes.get("trait", trait_name)
    if source == "screen()":
        if source_trait not in screen:
            raise ValueError(f"Owned flow screen trait is unavailable: {source_trait}")
        return screen[source_trait]
    target = owner
    if source == "parent()":
        target = parents[id(owner)]
    elif source and source.startswith("sibling(") and source.endswith(")"):
        sibling_name = source[len("sibling(") : -1]
        parent = parents[id(owner)]
        target = next(
            child
            for child in parent.children
            if child.name.casefold() == sibling_name.casefold()
        )
    elif source and source.startswith("child(") and source.endswith(")"):
        child_name = source[len("child(") : -1]
        target = next(
            child
            for child in owner.children
            if child.name.casefold() == child_name.casefold()
        )
    elif source not in {None, "me()"}:
        raise ValueError(f"Owned flow layout source is unsupported: {source}")
    if source is not None:
        return _layout_trait(target, source_trait, root, parents, screen, stack)
    if operation.children:
        return _layout_expression(operation, trait_name, owner, root, parents, screen, stack)
    entity = _entity_name(operation.text)
    if entity == "true":
        return 1.0
    if entity == "false":
        return 0.0
    return float(operation.text)


def _layout_expression(
    node: TileNode,
    trait_name: str,
    owner: TileNode,
    root: TileNode,
    parents: dict[int, TileNode],
    screen: dict[str, float],
    stack: set[tuple[int, str]],
) -> float:
    value = None
    for operation in node.children:
        operand = _layout_operand(
            operation,
            trait_name,
            owner,
            root,
            parents,
            screen,
            stack,
        )
        if operation.tag == "copy":
            value = operand
        elif value is None:
            raise ValueError(f"Owned flow layout expression begins with {operation.tag}")
        elif operation.tag == "add":
            value += operand
        elif operation.tag == "sub":
            value -= operand
        elif operation.tag in {"mul", "mult"}:
            value *= operand
        elif operation.tag == "div":
            value /= operand
        else:
            raise ValueError(f"Owned flow layout operation is unsupported: {operation.tag}")
    if value is None:
        raise ValueError("Owned flow layout expression is empty")
    return value


def _layout_trait(
    node: TileNode,
    trait_name: str,
    root: TileNode,
    parents: dict[int, TileNode],
    screen: dict[str, float],
    stack: set[tuple[int, str]] | None = None,
) -> float:
    active = set() if stack is None else stack
    identity = (id(node), trait_name)
    if identity in active:
        raise ValueError(f"Owned flow layout trait is recursive: {node.name}.{trait_name}")
    trait = node.child(trait_name)
    if trait is None:
        if trait_name in {"x", "y"}:
            return 0.0
        raise ValueError(f"Owned flow layout trait is absent: {node.name}.{trait_name}")
    direct = _direct_number(trait)
    if direct is not None:
        return direct
    active.add(identity)
    try:
        return _layout_expression(trait, trait_name, node, root, parents, screen, active)
    finally:
        active.remove(identity)


def _flow_menu_contract(
    flow: dict[str, object],
    trees: dict[str, TileNode],
    documents: dict[str, dict[str, object]],
    resolved_includes: dict[str, list[str]],
) -> tuple[
    list[dict[str, object]],
    frozenset[str],
    list[float],
    dict[str, str],
]:
    configured = dict(flow["menus"])
    rows = []
    roots = []
    canvas = None
    for role, raw in configured.items():
        definition = dict(raw)
        document = canonical_ui_path(str(definition["document"]))
        if document not in trees:
            raise ValueError(f"Owned flow menu document is absent: {role}={document}")
        roots.append(document)
        tree = trees[document]
        if "canvasTile" in definition:
            tile = _named_tile(tree, str(definition["canvasTile"]))
            parents = _tile_parent_index(tree)
            candidate = [
                _layout_trait(tile, "width", tree, parents, {"width": 0.0, "height": 0.0}),
                _layout_trait(tile, "height", tree, parents, {"width": 0.0, "height": 0.0}),
            ]
            if canvas is not None and canvas != candidate:
                raise ValueError("Owned flow menus disagree on their reference canvas")
            canvas = candidate
    if canvas is None or any(value <= 0.0 for value in canvas):
        raise ValueError("Owned flow reference canvas was not derived from its menu XML")
    screen = {"width": canvas[0], "height": canvas[1]}
    for role, raw in configured.items():
        definition = dict(raw)
        document = canonical_ui_path(str(definition["document"]))
        row: dict[str, object] = {
            "role": str(role),
            "document": document,
            "source": documents[document]["source"],
            "sha256": documents[document]["sha256"],
            "menuName": documents[document]["menuName"],
        }
        if "layoutTile" in definition:
            tree = trees[document]
            tile = _named_tile(tree, str(definition["layoutTile"]))
            parents = _tile_parent_index(tree)
            row["layoutTile"] = str(definition["layoutTile"])
            row["rect"] = [
                _layout_trait(tile, "x", tree, parents, screen),
                _layout_trait(tile, "y", tree, parents, screen),
                _layout_trait(tile, "width", tree, parents, screen),
                _layout_trait(tile, "height", tree, parents, screen),
            ]
        rows.append(row)
    closure = set()
    queue = deque(roots)
    while queue:
        current = queue.popleft()
        if current in closure:
            continue
        closure.add(current)
        queue.extend(resolved_includes[current])
    available_entities = {
        entity
        for document in closure
        for node in trees[document].walk()
        for entity in [_entity_name(node.text)]
        if entity is not None
    }
    strings = {}
    for semantic, configured_entity in dict(flow["engineStringEntities"]).items():
        entity = str(configured_entity)
        if entity not in available_entities:
            raise ValueError(
                f"Owned flow string entity is absent: {semantic}={entity}"
            )
        strings[str(semantic)] = _display_entity(f"&{entity};")
    return rows, frozenset(closure), canvas, strings


def _gameplay_ui_contract(
    recipe_ui: dict[str, object],
    trees: dict[str, TileNode],
    documents: dict[str, dict[str, object]],
    resolved_includes: dict[str, list[str]],
) -> tuple[dict[str, object], frozenset[str], frozenset[int]]:
    configured = dict(recipe_ui["gameplayPresentation"])
    canvas = [float(value) for value in configured["referenceCanvasSize"]]
    if len(canvas) != 2 or any(value <= 0.0 for value in canvas):
        raise ValueError("Owned gameplay UI reference canvas is invalid")
    roles = dict(configured["roles"])
    if set(roles) != {"hud", "status", "items", "data"}:
        raise ValueError("Owned gameplay UI roles are incomplete")

    rows = []
    full_closure: set[str] = set()
    font_ids: set[int] = set()
    for role, raw in roles.items():
        definition = dict(raw)
        document = canonical_ui_path(str(definition["document"]))
        if document not in trees:
            raise ValueError(f"Owned gameplay UI document is absent: {role}={document}")
        expected_menu = str(definition["menuName"])
        if documents[document]["menuName"] != expected_menu:
            raise ValueError(
                f"Owned gameplay UI menu identity differs: {role}={document}"
            )
        closure: set[str] = set()
        queue = deque([document])
        while queue:
            current = queue.popleft()
            if current in closure:
                continue
            closure.add(current)
            queue.extend(resolved_includes[current])
        full_closure.update(closure)

        available_font_ids = {
            int(value)
            for member in closure
            for node in trees[member].walk()
            if node.tag in {"font", "_font"}
            for value in [_direct_number(node)]
            if value is not None and float(value).is_integer() and value > 0
        }
        body_font_id = int(definition["bodyFontId"])
        title_font_id = int(definition["titleFontId"])
        if body_font_id not in available_font_ids or title_font_id not in available_font_ids:
            raise ValueError(f"Owned gameplay UI configured fonts are absent: {role}")
        font_ids.update((body_font_id, title_font_id))

        tree = trees[document]
        parents = _tile_parent_index(tree)
        screen = {"width": canvas[0], "height": canvas[1]}
        layout = []
        for tile_name in definition["layoutTiles"]:
            tile = _named_tile(tree, str(tile_name))
            layout.append(
                {
                    "tile": str(tile_name),
                    "rect": [
                        _layout_trait(tile, trait, tree, parents, screen)
                        for trait in ("x", "y", "width", "height")
                    ],
                }
            )
        rows.append(
            {
                "role": role,
                "document": document,
                "source": documents[document]["source"],
                "sha256": documents[document]["sha256"],
                "menuName": expected_menu,
                "menuClassEntity": documents[document]["menuClassEntity"],
                "bodyFontId": body_font_id,
                "titleFontId": title_font_id,
                "documentClosure": sorted(closure),
                "layout": layout,
            }
        )

    background = _asset_path(str(configured["backgroundAsset"]))
    if background is None or not any(
        background in documents[member]["assetReferences"]
        for member in full_closure
    ):
        raise ValueError("Owned gameplay UI background is not referenced by its menu graph")
    return (
        {
            "schema": "opennv-owned-gameplay-ui/v1",
            "referenceCanvasSize": canvas,
            "backgroundAsset": background,
            "statusPresentation": _status_presentation_contract(
                dict(configured["statusPresentation"]),
                trees,
            ),
            "roles": sorted(rows, key=lambda value: str(value["role"])),
        },
        frozenset(full_closure),
        frozenset(font_ids),
    )


def _status_presentation_contract(
    configured: dict[str, object],
    trees: dict[str, TileNode],
) -> dict[str, object]:
    document = canonical_ui_path(str(configured["document"]))
    if document not in trees:
        raise ValueError(f"Owned Pip-Boy STATS document is absent: {document}")
    tree = trees[document]
    parents = _tile_parent_index(tree)
    screen = {"width": 1024.0, "height": 768.0}

    def number(node: TileNode, trait: str) -> float:
        return _layout_trait(node, trait, tree, parents, screen)

    def engine_id(node: TileNode, expected: int) -> int:
        actual = _direct_number(node.child("id"))
        if actual is None or not float(actual).is_integer() or int(actual) != expected:
            raise ValueError(
                f"Owned Pip-Boy STATS engine ID differs: {node.name} "
                f"expected={expected} actual={actual}"
            )
        return int(actual)

    def strings(
        values: object,
        *,
        include_position: bool,
    ) -> list[dict[str, object]]:
        rows = []
        for raw in values:
            definition = dict(raw)
            tile = _named_tile(tree, str(definition["tile"]))
            source_tile = _named_tile(
                tree,
                str(definition.get("sourceTile", definition["tile"])),
            )
            trait = source_tile.child(str(definition["trait"]))
            entity = None if trait is None else _entity_name(trait.text)
            if entity is None and trait is not None:
                loose = trait.text.strip()
                if loose.startswith("&") and ";" not in loose and len(loose) > 1:
                    entity = loose[1:]
            expected_entity = str(definition["entity"])
            if entity != expected_entity:
                raise ValueError(
                    f"Owned Pip-Boy STATS string entity differs: "
                    f"{tile.name} expected={expected_entity} actual={entity}"
                )
            row: dict[str, object] = {
                "tile": tile.name,
                "engineId": engine_id(tile, int(definition["engineId"])),
                "entity": entity,
                "fontId": int(definition["fontId"]),
                "text": str(definition["fallbackText"]),
                "textProvenance": {
                    "kind": "recipe-fallback-after-owned-entity-validation",
                    "entity": entity,
                },
            }
            if include_position:
                row["position"] = [number(tile, "x"), number(tile, "y")]
            if include_position and tile.child("width") is not None:
                row["width"] = number(tile, "width")
            rows.append(row)
        return rows

    status_container = _named_tile(tree, str(configured["statusContainerTile"]))
    tail_line = _named_tile(tree, str(configured["tailLineTile"]))
    rules = []
    for raw in configured["rules"]:
        definition = dict(raw)
        tile = _named_tile(tree, str(definition["tile"]))
        orientation = str(definition["orientation"])
        if orientation not in {"horizontal", "vertical"}:
            raise ValueError(
                f"Owned Pip-Boy STATS rule orientation is invalid: {tile.name}"
            )
        rules.append({"tile": tile.name, "orientation": orientation})
    images = []
    for raw in configured["bodyImages"]:
        definition = dict(raw)
        tile = _named_tile(tree, str(definition["tile"]))
        parent = parents.get(id(tile))
        if parent is None or not parent.name:
            raise ValueError(
                f"Owned Pip-Boy STATS body image has no named parent: {tile.name}"
            )
        asset = _asset_path(str(definition["asset"]))
        if asset is None or not asset.startswith("textures\\") or not asset.endswith(".dds"):
            raise ValueError(
                f"Owned Pip-Boy STATS body image is not a DDS texture: {tile.name}"
            )
        images.append(
            {
                "tile": tile.name,
                "parentTile": parent.name,
                "engineId": engine_id(tile, int(definition["engineId"])),
                "asset": asset,
                "rect": [
                    number(tile, "x"),
                    number(tile, "y"),
                    number(tile, "width"),
                    number(tile, "height"),
                ],
            }
        )
    return {
        "document": document,
        "statusContainer": {
            "tile": status_container.name,
            "position": [number(status_container, "x"), number(status_container, "y")],
            "width": number(status_container, "width"),
        },
        "tailLine": {
            "tile": tail_line.name,
            "position": [number(tail_line, "x"), number(tail_line, "y")],
            "width": number(tail_line, "width"),
            "stretch": number(tail_line, "_stretch"),
        },
        "rules": rules,
        "headline": strings(configured["headline"], include_position=True),
        "conditionTabs": strings(configured["conditionTabs"], include_position=False),
        "navigation": strings(configured["navigation"], include_position=False),
        "bodyImages": images,
    }


def _finalize_status_presentation_layout(
    presentation: dict[str, object],
    configured: dict[str, object],
    trees: dict[str, TileNode],
    fonts: list[dict[str, object]],
    line_thickness: float,
) -> None:
    """Resolve the bounded STATS layout from owned XML/prefabs and font metrics."""

    document = canonical_ui_path(str(configured["document"]))
    tree = trees[document]
    fonts_by_id = {int(value["fontId"]): value for value in fonts}
    super_box = trees[canonical_ui_path("menus\\prefabs\\super_text_box.xml")]
    card_info = trees[canonical_ui_path("menus\\prefabs\\card_info.xml")]
    vertical_line = trees[
        canonical_ui_path("menus\\prefabs\\vertical_fade_line.xml")
    ]

    def direct(node: TileNode, trait: str) -> float:
        value = _direct_number(node.child(trait))
        if value is None:
            raise ValueError(
                f"Owned Pip-Boy STATS numeric trait is absent: {node.name}.{trait}"
            )
        return value

    horizontal_buffer = direct(super_box, "_horbuf")
    vertical_buffer = direct(super_box, "_verbuf")
    card_height = direct(card_info, "height")
    vertical_rule_length = direct(vertical_line, "height")
    if line_thickness <= 0.0:
        raise ValueError("Owned Pip-Boy STATS line thickness is invalid")

    status_container = _named_tile(tree, str(configured["statusContainerTile"]))
    tail_line = _named_tile(tree, str(configured["tailLineTile"]))
    container_x = direct(status_container, "x")
    container_y = direct(status_container, "y")
    container_width = direct(status_container, "width")
    tail_x = direct(tail_line, "x")
    tail_y = direct(tail_line, "y")
    tail_width = direct(tail_line, "width")
    tail_stretch = direct(tail_line, "_stretch")
    presentation["statusContainer"]["rect"] = [
        container_x,
        container_y,
        container_width,
        tail_y - vertical_rule_length - container_y,
    ]

    finalized_rules = []
    for raw in configured["rules"]:
        definition = dict(raw)
        tile = _named_tile(tree, str(definition["tile"]))
        orientation = str(definition["orientation"])
        if tile is tail_line:
            x, y, length = tail_x, tail_y, tail_width
        elif orientation == "horizontal":
            x, y, length = direct(tile, "x"), direct(tile, "y"), direct(tile, "_length")
        else:
            x, y, length = direct(tile, "_x"), direct(tile, "_y"), vertical_rule_length
        finalized_rules.append(
            {
                "tile": tile.name,
                "rect": [
                    x,
                    y,
                    length if orientation == "horizontal" else line_thickness,
                    line_thickness if orientation == "horizontal" else length,
                ],
            }
        )
    presentation["rules"] = finalized_rules

    headline_config = {
        str(value["tile"]): dict(value) for value in configured["headline"]
    }
    for row in presentation["headline"]:
        definition = headline_config[str(row["tile"])]
        tile = _named_tile(tree, str(row["tile"]))
        font = fonts_by_id[int(row["fontId"])]
        width_node = tile.child("width")
        width = (
            direct(tile, "width")
            if width_node is not None
            else _text_width(str(row["text"]), font)
        )
        include = tile.child("include")
        height = (
            card_height
            if include is not None
            and canonical_ui_path(include.attributes.get("src", "")) == "card_info.xml"
            else float(font["lineHeightPixels"])
        )
        row["rect"] = [direct(tile, "x"), direct(tile, "y"), width, height]
        if int(definition["fontId"]) != int(row["fontId"]):
            raise ValueError(f"Owned Pip-Boy STATS font identity differs: {tile.name}")

    condition_height = float(fonts_by_id[2]["lineHeightPixels"]) + vertical_buffer
    condition_config = {
        str(value["tile"]): dict(value) for value in configured["conditionTabs"]
    }
    for index, row in enumerate(presentation["conditionTabs"]):
        tile = _named_tile(tree, str(row["tile"]))
        definition = condition_config[str(row["tile"])]
        fixed_width = _direct_number(tile.child("_fixedwidth"))
        if fixed_width is None:
            fixed_width = direct(_named_tile(tree, str(configured["conditionTabs"][0]["tile"])), "_fixedwidth")
        row["rect"] = [0.0, condition_height * index, fixed_width, condition_height]
        row["selected"] = index == 0
        if int(definition["fontId"]) != int(row["fontId"]):
            raise ValueError(f"Owned Pip-Boy STATS font identity differs: {tile.name}")

    navigation_count = len(presentation["navigation"])
    center_step = (tail_width + tail_stretch * 2.0) / (navigation_count + 1.0)
    navigation_config = {
        str(value["tile"]): dict(value) for value in configured["navigation"]
    }
    for index, row in enumerate(presentation["navigation"]):
        tile = _named_tile(tree, str(row["tile"]))
        definition = navigation_config[str(row["tile"])]
        font = fonts_by_id[int(row["fontId"])]
        width = _text_width(str(row["text"]), font) + horizontal_buffer
        height = float(font["lineHeightPixels"]) + vertical_buffer
        center_x = tail_x + center_step * (index + 1.0) - tail_stretch
        row["rect"] = [center_x - width * 0.5, tail_y - height * 0.5, width, height]
        row["selected"] = index == 0
        if int(definition["fontId"]) != int(row["fontId"]):
            raise ValueError(f"Owned Pip-Boy STATS font identity differs: {tile.name}")


def _prepare_gameplay_physical_device(
    configured: dict[str, object],
    owned_archives: OwnedArchiveStack,
    cache_root: Path,
    configuration: RuntimeConfiguration,
) -> dict[str, object]:
    logical_path = canonical_member_path(str(configured["modelAsset"]))
    if not logical_path.startswith("meshes\\") or not logical_path.endswith(".nif"):
        raise ValueError("Owned Pip-Boy physical device must be one NIF under meshes")
    if not isinstance(configured.get("exportStrict"), bool):
        raise ValueError("Owned Pip-Boy exportStrict policy must be explicit")
    aliases = configured.get("textureAliases")
    if not isinstance(aliases, dict):
        raise ValueError("Owned Pip-Boy textureAliases policy must be explicit")
    screen_surface = str(configured["screenSurface"])
    if not screen_surface:
        raise ValueError("Owned Pip-Boy screen surface identity is empty")
    button_glow_surfaces = {
        str(role): str(surface)
        for role, surface in dict(configured["buttonGlowSurfaces"]).items()
    }
    if set(button_glow_surfaces) != {"status", "items", "data"} or any(
        not surface for surface in button_glow_surfaces.values()
    ):
        raise ValueError("Owned Pip-Boy hardware button-glow identities are incomplete")

    member = owned_archives.extract(logical_path)
    source_path = cache_root / "source" / Path(logical_path.replace("\\", "/"))
    atomic_bytes(source_path, member.data)
    asset_id = hashlib.sha256(
        f"{logical_path}:{member.sha256}".encode("utf-8")
    ).hexdigest()[:configuration.content_compiler.asset_id_hex_characters]
    output_root = cache_root / "generated" / "opening" / "pipboy3000"
    model_path = output_root / f"{asset_id}.gltf"
    sidecar_path = output_root / f"{asset_id}.opennv.json"
    sidecar = export_static_nif(
        source_path,
        logical_path,
        model_path,
        sidecar_path,
        configuration.content_compiler,
        strict=bool(configured["exportStrict"]),
    )
    screen_matches = [
        surface for surface in sidecar["surfaces"] if surface["name"] == screen_surface
    ]
    if len(screen_matches) != 1:
        raise ValueError(
            "Owned Pip-Boy CRT surface does not resolve uniquely: "
            f"{screen_surface} matches={len(screen_matches)}"
        )
    surface_names = [str(surface["name"]) for surface in sidecar["surfaces"]]
    ambiguous_glows = {
        role: surface_names.count(name)
        for role, name in button_glow_surfaces.items()
        if surface_names.count(name) != 1
    }
    if ambiguous_glows:
        raise ValueError(
            "Owned Pip-Boy button-glow surfaces do not resolve uniquely: "
            + ", ".join(
                f"{role}={button_glow_surfaces[role]} matches={count}"
                for role, count in sorted(ambiguous_glows.items())
            )
        )

    binding_paths = sorted(
        {
            request["path"]
            for surface in sidecar["surfaces"]
            for request in texture_binding_requests(surface)
        }
    )
    texture_pipeline = OwnedTexturePipeline(
        owned_archives,
        cache_root,
        {str(source): str(target) for source, target in aliases.items()},
        configuration.content_compiler,
    )
    missing = [
        path for path in binding_paths if texture_pipeline.member_source_count(path) != 1
    ]
    if missing:
        raise FileNotFoundError(
            "Owned Pip-Boy active texture bindings are incomplete: "
            + ", ".join(missing)
        )
    textures = {path: texture_pipeline.prepare(path) for path in binding_paths}
    asset = {
        "id": asset_id,
        "logicalPath": logical_path,
        "sourceSha256": member.sha256,
        "materials": material_bindings(
            sidecar,
            {path: artifact.asset_id for path, artifact in textures.items()},
            configuration.content_compiler,
        ),
    }
    material_manifest_path = output_root / f"{asset_id}.materials.json"
    atomic_json(
        material_manifest_path,
        {
            "schema": "opennv-static-material-manifest/v1",
            "textures": [textures[path].manifest() for path in binding_paths],
            "asset": asset,
        },
    )
    model_sha256 = str(sidecar["outputs"]["gltf"]["sha256"])
    buffer_row = dict(sidecar["outputs"]["buffer"])
    buffer_path = output_root / str(buffer_row["file"])
    return {
        "schema": "opennv-owned-physical-pipboy/v1",
        "logicalPath": logical_path,
        "source": str(source_path.resolve()),
        "sourceSha256": member.sha256,
        "sourceArchive": member.source_archive,
        "sourceArchiveSha256": member.source_archive_sha256,
        "model": str(model_path.resolve()),
        "modelSha256": model_sha256,
        "sidecar": str(sidecar_path.resolve()),
        "sidecarSha256": file_sha256(sidecar_path),
        "buffer": str(buffer_path.resolve()),
        "bufferSha256": str(buffer_row["sha256"]),
        "materialManifest": str(material_manifest_path.resolve()),
        "materialManifestSha256": file_sha256(material_manifest_path),
        "screenSurface": screen_surface,
        "buttonGlowSurfaces": button_glow_surfaces,
        "surfaces": len(sidecar["surfaces"]),
        "vertices": sum(
            int(surface["vertices"]) for surface in sidecar["surfaces"]
        ),
        "textures": len(textures),
        "compiler": sidecar["compiler"],
    }


def compile_ui(
    data_root: Path,
    default_ini_path: Path,
    preferences_ini_path: Path | None,
    ui_archive_path: Path,
    owned_archives: OwnedArchiveStack,
    cache_root: Path,
    recipe_ui: dict[str, object],
    flow: dict[str, object],
    additional_texture_paths: Iterable[str],
    configuration: RuntimeConfiguration,
) -> dict[str, object]:
    archive = BsaArchive(ui_archive_path)
    member_prefix = canonical_ui_path(str(recipe_ui["memberPrefix"]))
    extensions = tuple(str(value).casefold() for value in recipe_ui["documentExtensions"])
    additional = {
        canonical_ui_path(str(value)) for value in recipe_ui["additionalDocuments"]
    }
    members = sorted(
        member
        for member in archive.members
        if member.startswith(member_prefix)
        and (member.casefold().endswith(extensions) or member in additional)
    )
    documents: dict[str, dict[str, object]] = {}
    trees: dict[str, TileNode] = {}
    for member in members:
        extracted = archive.extract(member)
        source_path = cache_root / "source" / "ui" / Path(member.replace("\\", "/"))
        atomic_bytes(source_path, extracted.data)
        if member.casefold().endswith(extensions):
            index, tree = _document_index(member, extracted.data)
            index["source"] = str(source_path.resolve())
            documents[member] = index
            trees[member] = tree
        else:
            documents[member] = {
                "path": member,
                "source": str(source_path.resolve()),
                "bytes": len(extracted.data),
                "sha256": extracted.sha256,
                "menuName": None,
                "menuClassEntity": None,
                "includes": [],
                "assetReferences": [],
                "initiallyVisibleAssetReferences": [],
            }

    available = frozenset(documents)
    search_roots = tuple(str(value) for value in recipe_ui["includeSearchRoots"])
    unresolved_includes = []
    resolved_includes: dict[str, list[str]] = {}
    for member, document in documents.items():
        resolved = []
        for include in document["includes"]:
            target = _resolve_include(str(include), member, available, search_roots)
            if target is None:
                unresolved_includes.append({"document": member, "include": include})
            else:
                resolved.append(target)
        resolved_includes[member] = sorted(set(resolved))
        document["resolvedIncludes"] = resolved_includes[member]

    boot_document = canonical_ui_path(str(recipe_ui["bootDocument"]))
    confirmation_document = canonical_ui_path(str(recipe_ui["confirmationDocument"]))
    if boot_document not in trees or confirmation_document not in trees:
        raise ValueError("Owned opening boot or confirmation menu is absent")
    flow_menus, flow_closure, flow_canvas, flow_strings = _flow_menu_contract(
        flow,
        trees,
        documents,
        resolved_includes,
    )
    gameplay, gameplay_closure, gameplay_font_ids = _gameplay_ui_contract(
        recipe_ui,
        trees,
        documents,
        resolved_includes,
    )
    gameplay["physicalDevice"] = _prepare_gameplay_physical_device(
        dict(dict(recipe_ui["gameplayPresentation"])["physicalDevice"]),
        owned_archives,
        cache_root,
        configuration,
    )
    gameplay["systemColor"] = _gameplay_system_color(
        dict(dict(recipe_ui["gameplayPresentation"])["systemColor"]),
        default_ini_path,
        preferences_ini_path,
    )
    boot_closure = set()
    queue = deque([boot_document, confirmation_document])
    while queue:
        current = queue.popleft()
        if current in boot_closure:
            continue
        boot_closure.add(current)
        queue.extend(resolved_includes[current])

    prepared_closure = boot_closure | set(flow_closure) | set(gameplay_closure)
    configured_title_asset = _asset_path(str(recipe_ui["titleAsset"]))
    if configured_title_asset is None or not configured_title_asset.endswith(".dds"):
        raise ValueError("Owned boot menu title asset is invalid")
    requested_assets = sorted(
        {
            str(asset)
            for member in prepared_closure
            for asset in documents[member]["initiallyVisibleAssetReferences"]
        }
        | {str(gameplay["backgroundAsset"])}
        | {configured_title_asset}
        | {
            str(value["asset"])
            for value in gameplay["statusPresentation"]["bodyImages"]
        }
        | {str(value) for value in additional_texture_paths}
    )
    texture_pipeline = OwnedTexturePipeline(
        owned_archives,
        cache_root,
        {},
        configuration.content_compiler,
    )
    texture_rows = []
    unresolved_assets = []
    for requested in requested_assets:
        if not requested.endswith(".dds"):
            unresolved_assets.append({"path": requested, "reason": "runtime-nif-ui-not-implemented"})
            continue
        if texture_pipeline.member_source_count(requested) != 1:
            unresolved_assets.append({"path": requested, "reason": "owned-member-not-resolved"})
            continue
        texture_rows.append(texture_pipeline.prepare(requested).manifest())

    tree = trees[boot_document]
    container_name = str(recipe_ui["buttonContainer"])
    container = next((node for node in tree.walk() if node.name == container_name), None)
    if container is None:
        raise ValueError(f"Owned boot menu button container is absent: {container_name}")
    actions = {str(key): str(value) for key, value in dict(recipe_ui["actions"]).items()}
    buttons = []
    for node in container.children:
        if node.name not in actions:
            continue
        string_node = node.child("_string")
        id_node = node.child("id")
        buttons.append(
            {
                "tile": node.name,
                "action": actions[node.name],
                "engineId": None if id_node is None else int(id_node.text),
                "stringEntity": None if string_node is None else _entity_name(string_node.text),
                "label": "" if string_node is None else _display_entity(string_node.text),
            }
        )
    if set(value["action"] for value in buttons) != set(actions.values()):
        raise ValueError("Owned boot menu actions do not join to authored tiles")
    engine_presentation, engine_textures = _compile_engine_presentation(
        data_root,
        default_ini_path,
        owned_archives,
        cache_root,
        configuration,
        recipe_ui,
        boot_document,
        container,
        buttons,
        available,
        search_roots,
        trees,
        texture_pipeline,
    )
    ini = _ini_index(default_ini_path)
    fonts_settings = dict(dict(recipe_ui["engineSettings"])["fonts"])
    gameplay_fonts = []
    gameplay_font_textures = []
    for font_id in sorted(gameplay_font_ids):
        font, atlas = _compile_gamebryo_font(
            font_id,
            ini,
            fonts_settings,
            owned_archives,
            cache_root,
            texture_pipeline,
        )
        gameplay_fonts.append({"fontId": font_id, **font})
        gameplay_font_textures.append(atlas)
    _finalize_status_presentation_layout(
        gameplay["statusPresentation"],
        dict(dict(recipe_ui["gameplayPresentation"])["statusPresentation"]),
        trees,
        gameplay_fonts,
        float(engine_presentation["globalStyleTraits"]["_line_thickness"]),
    )
    textures_by_path = {
        str(value["requestedPath"]): value
        for value in [*texture_rows, *engine_textures, *gameplay_font_textures]
    }
    background_asset = str(gameplay["backgroundAsset"])
    if background_asset not in textures_by_path:
        raise ValueError("Owned gameplay UI background texture was not prepared")
    gameplay["background"] = textures_by_path[background_asset]
    for body_image in gameplay["statusPresentation"]["bodyImages"]:
        asset = str(body_image["asset"])
        if asset not in textures_by_path:
            raise ValueError(
                f"Owned Pip-Boy STATS body texture was not prepared: {asset}"
            )
        body_image["texture"] = textures_by_path[asset]
    gameplay["fonts"] = gameplay_fonts
    texture_rows = [textures_by_path[key] for key in sorted(textures_by_path)]
    title_tile = str(recipe_ui["titleTile"])
    title_nodes = [node for node in tree.walk() if node.name == title_tile]
    authored_title_assets = sorted(
        {
            asset
            for node in title_nodes
            for filename in node.children
            if filename.tag == "filename"
            for asset in [_asset_path(filename.text)]
            if asset is not None
        }
    )
    if configured_title_asset not in textures_by_path:
        raise ValueError("Configured owned boot menu title texture was not prepared")
    title_assets = [configured_title_asset]
    layout = _boot_layout(
        container,
        buttons,
        title_tile,
        configuration,
        dict(engine_presentation["font"]),
        dict(engine_presentation["buttonStyle"]),
    )
    return {
        "archive": {
            "file": ui_archive_path.name,
            "bytes": ui_archive_path.stat().st_size,
            "sha256": file_sha256(ui_archive_path),
        },
        "documents": [documents[key] for key in sorted(documents)],
        "documentCount": len(documents),
        "unresolvedIncludes": unresolved_includes,
        "enginePresentation": engine_presentation,
        "gameplayPresentation": gameplay,
        "flow": {
            "referenceCanvasSize": flow_canvas,
            "menus": flow_menus,
            "strings": flow_strings,
            "documentClosure": sorted(flow_closure),
        },
        "boot": {
            "document": boot_document,
            "confirmationDocument": confirmation_document,
            "documentClosure": sorted(boot_closure),
            "buttonContainer": container_name,
            "buttonWidth": _direct_number(container.child("width")),
            "buttonSpacing": _direct_number(container.child("_spacing")),
            "titleTile": title_tile,
            "titleAssets": title_assets,
            "authoredTitleAssets": authored_title_assets,
            "buttons": buttons,
            "layout": layout,
        },
        "preparedTextures": texture_rows,
        "unresolvedAssets": unresolved_assets,
    }


def _record_text_values(record: dict[str, object], signature: str) -> list[str]:
    return [
        str(value["value"])
        for value in record["text"]
        if value["signature"] == signature
    ]


def _record_editor_id_from_manifest(record: dict[str, object]) -> str | None:
    values = _record_text_values(record, "EDID")
    return values[0] if len(values) == 1 else None


def _unique_manifest_record(
    records: Iterable[dict[str, object]],
    editor_id: str,
    expected_type: str | None = None,
) -> dict[str, object]:
    matches = [
        record
        for record in records
        if _record_editor_id_from_manifest(record) is not None
        and _record_editor_id_from_manifest(record).casefold() == editor_id.casefold()
        and (expected_type is None or record["recordType"] == expected_type)
    ]
    if len(matches) != 1:
        raise ValueError(
            f"Owned opening record is ambiguous: {editor_id} "
            f"type={expected_type or '*'} matches={len(matches)}"
        )
    return matches[0]


def _script_code_lines(source: str) -> list[str]:
    lines = []
    for raw in source.splitlines():
        line = raw.split(";", 1)[0].strip()
        if line:
            lines.append(line)
    return lines


def _script_commands(source: str) -> list[dict[str, object]]:
    commands: list[dict[str, object]] = []
    for line in _script_code_lines(source):
        match = re.fullmatch(r"SetStage\s+(\w+)\s+(\d+)", line, re.IGNORECASE)
        if match:
            commands.append(
                {"kind": "setStage", "questEditorId": match[1], "stage": int(match[2])}
            )
            continue
        match = re.fullmatch(
            r"([\w]+)\.SayTo\s+([\w]+)\s+([\w]+)(?:\s+.*)?",
            line,
            re.IGNORECASE,
        )
        if match:
            commands.append(
                {
                    "kind": "sayTo",
                    "speakerEditorId": match[1],
                    "targetEditorId": match[2],
                    "topicEditorId": match[3],
                }
            )
            continue
        match = re.fullmatch(r"GetPlayerName", line, re.IGNORECASE)
        if match:
            commands.append({"kind": "showMenu", "role": "name"})
            continue
        match = re.fullmatch(r"ShowRaceMenu", line, re.IGNORECASE)
        if match:
            commands.append({"kind": "showMenu", "role": "appearance"})
            continue
        match = re.fullmatch(r"SetTagSkills\s+(\d+)\s+(\d+)", line, re.IGNORECASE)
        if match:
            commands.append(
                {
                    "kind": "showMenu",
                    "role": "tagSkills",
                    "maximumSelected": int(match[1]),
                    "mode": int(match[2]),
                }
            )
            continue
        match = re.fullmatch(r"ShowTraitMenu", line, re.IGNORECASE)
        if match:
            commands.append({"kind": "showMenu", "role": "traits"})
            continue
        match = re.fullmatch(r"ShowLoveTesterMenuParams\s+(\d+)", line, re.IGNORECASE)
        if match:
            commands.append(
                {"kind": "showMenu", "role": "special", "totalPoints": int(match[1])}
            )
            continue
        match = re.fullmatch(
            r"set\s+(\w+)\.fTimer\s+to\s+(-?\d+(?:\.\d+)?)",
            line,
            re.IGNORECASE,
        )
        if match:
            commands.append(
                {
                    "kind": "setTimer",
                    "questEditorId": match[1],
                    "seconds": float(match[2]),
                }
            )
            continue
        match = re.fullmatch(
            r"SetObjective(Displayed|Completed)\s+(\w+)\s+(\d+)\s+(\d+)",
            line,
            re.IGNORECASE,
        )
        if match:
            commands.append(
                {
                    "kind": "objective",
                    "state": match[1].casefold(),
                    "questEditorId": match[2],
                    "index": int(match[3]),
                    "enabled": int(match[4]) != 0,
                }
            )
            continue
        match = re.fullmatch(
            r"(Enable|Disable)PlayerControls(?:\s+(.*))?",
            line,
            re.IGNORECASE,
        )
        if match:
            values = [] if not match[2] else [int(value) for value in match[2].split()]
            if len(values) > len(PLAYER_CONTROL_ARGUMENTS) or any(
                value not in {0, 1} for value in values
            ):
                raise ValueError(f"Owned player-control command is invalid: {line}")
            commands.append(
                {
                    "kind": "playerControls",
                    "operation": match[1].casefold(),
                    "values": values,
                    "arguments": list(PLAYER_CONTROL_ARGUMENTS[: len(values)]),
                }
            )
            continue
        match = re.fullmatch(
            r"player\.(addscriptpackage|removescriptpackage)(?:\s+(\w+))?",
            line,
            re.IGNORECASE,
        )
        if match:
            adding = match[1].casefold() == "addscriptpackage"
            if adding != (match[2] is not None):
                raise ValueError(f"Owned script-package command is invalid: {line}")
            commands.append(
                {
                    "kind": "addScriptPackage" if adding else "removeScriptPackage",
                    "packageEditorId": match[2],
                }
            )
            continue
        match = re.fullmatch(
            r"(Apply|Remove)ImageSpaceModifier\s+(\w+)(?:\s+(\*))?",
            line,
            re.IGNORECASE,
        )
        if match:
            commands.append(
                {
                    "kind": "imageSpaceModifier",
                    "operation": match[1].casefold(),
                    "modifierEditorId": match[2],
                    "crossFade": match[3] is not None,
                }
            )
            continue
        match = re.fullmatch(r"(\w+)\.(Enable|Disable)", line, re.IGNORECASE)
        if match:
            commands.append(
                {
                    "kind": "referenceEnabled",
                    "referenceEditorId": match[1],
                    "enabled": match[2].casefold() == "enable",
                }
            )
            continue
        match = re.fullmatch(r"(\w+)\.SetDestroyed\s+(\d+)", line, re.IGNORECASE)
        if match:
            commands.append(
                {
                    "kind": "setDestroyed",
                    "referenceEditorId": match[1],
                    "destroyed": int(match[2]) != 0,
                }
            )
            continue
        match = re.fullmatch(r"(\w+)\.PlayIdle\s+(\w+)", line, re.IGNORECASE)
        if match:
            commands.append(
                {
                    "kind": "playIdle",
                    "referenceEditorId": match[1],
                    "idleEditorId": match[2],
                }
            )
            continue
        match = re.fullmatch(
            r"set\s+(\w+)\.n([A-Za-z0-9_]+)\s+to\s+\1\.n\2\s*\+\s*(-?\d+)",
            line,
            re.IGNORECASE,
        )
        if match:
            commands.append(
                {
                    "kind": "actorValueDelta",
                    "ownerEditorId": match[1],
                    "value": match[2],
                    "delta": int(match[3]),
                }
            )
            continue
        match = re.fullmatch(
            r"set\s+(\w+)\.([A-Za-z0-9_]+)\s+to\s+(-?\d+(?:\.\d+)?)",
            line,
            re.IGNORECASE,
        )
        if match:
            commands.append(
                {
                    "kind": "setQuestVariable",
                    "questEditorId": match[1],
                    "variable": match[2],
                    "value": float(match[3]),
                }
            )
            continue
        match = re.fullmatch(r"StartQuest\s+(\w+)", line, re.IGNORECASE)
        if match:
            commands.append({"kind": "startQuest", "questEditorId": match[1]})
            continue
        match = re.fullmatch(
            r"player\.(additem|removeitem|equipitem)\s+(\w+)(?:\s+(\d+))?(?:\s+\d+)?",
            line,
            re.IGNORECASE,
        )
        if match:
            commands.append(
                {
                    "kind": match[1].casefold(),
                    "itemEditorId": match[2],
                    "count": 1 if match[3] is None else int(match[3]),
                }
            )
            continue
        match = re.fullmatch(
            r"(\w+)\.(ResetAI|EVP|StopLook|Look)\s*(\w+)?",
            line,
            re.IGNORECASE,
        )
        if match:
            commands.append(
                {
                    "kind": "actorIntent",
                    "referenceEditorId": match[1],
                    "operation": match[2].casefold(),
                    "targetEditorId": match[3],
                }
            )
            continue
        if re.fullmatch(r"autosave", line, re.IGNORECASE):
            commands.append({"kind": "autosave"})
            continue
        match = re.fullmatch(r"StopQuest\s+(\w+)", line, re.IGNORECASE)
        if match:
            commands.append({"kind": "stopQuest", "questEditorId": match[1]})
            continue
        match = re.fullmatch(
            r"set\s+(\w+)\s+to\s+(-?\d+(?:\.\d+)?)",
            line,
            re.IGNORECASE,
        )
        if match:
            commands.append(
                {
                    "kind": "setGlobal",
                    "globalEditorId": match[1],
                    "value": float(match[2]),
                }
            )
            continue
        match = re.fullmatch(r"AutoDisplayObjectives\s+(\d+)", line, re.IGNORECASE)
        if match:
            commands.append(
                {"kind": "autoDisplayObjectives", "enabled": int(match[1]) != 0}
            )
            continue
        match = re.fullmatch(r"AddAchievement\s+(\d+)", line, re.IGNORECASE)
        if match:
            commands.append({"kind": "achievement", "index": int(match[1])})
    return commands


def _timer_transitions(
    quest_editor_id: str,
    script_source: str,
    timer_stages: Iterable[int],
) -> list[dict[str, int]]:
    code = "\n".join(_script_code_lines(script_source))
    rows = []
    for stage in sorted(set(timer_stages)):
        pattern = re.compile(
            rf"getstage\s+{re.escape(quest_editor_id)}\s*==\s*{stage}\b"
            rf"(?:(?!elseif\s+getstage).)*?setstage\s+{re.escape(quest_editor_id)}\s+(\d+)",
            re.IGNORECASE | re.DOTALL,
        )
        targets = {int(match[1]) for match in pattern.finditer(code)}
        if len(targets) > 1:
            raise ValueError(
                f"Owned opening timer transition is ambiguous: stage={stage} targets={targets}"
            )
        if targets:
            rows.append({"fromStage": stage, "toStage": next(iter(targets))})
    return rows


def _stage_programs(
    quest_record: dict[str, object],
    quest_script_source: str,
) -> tuple[list[dict[str, object]], list[dict[str, int]], list[dict[str, int]]]:
    programs = []
    timer_stages = []
    for row in quest_record["questStageScripts"]:
        source = str(row["source"])
        commands = _script_commands(source)
        stage = int(row["stage"])
        if any(command["kind"] == "setTimer" for command in commands):
            timer_stages.append(stage)
        programs.append(
            {
                "stage": stage,
                "source": source,
                "commands": commands,
            }
        )
    programs.sort(key=lambda value: int(value["stage"]))
    timer = _timer_transitions(
        _record_editor_id_from_manifest(quest_record) or "",
        quest_script_source,
        timer_stages,
    )
    menu_close = []
    code = "\n".join(_script_code_lines(quest_script_source))
    for match in re.finditer(
        r"BEGIN\s+menumode\s+(\d+).*?getstage\s+(\w+)\s*==\s*(\d+)"
        r".*?setstage\s+\2\s+(\d+).*?\bEND\b",
        code,
        re.IGNORECASE | re.DOTALL,
    ):
        menu_close.append(
            {
                "menuMode": int(match[1]),
                "fromStage": int(match[3]),
                "toStage": int(match[4]),
            }
        )
    return programs, timer, menu_close


def _normalized_identifier(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.casefold())


def _catalog_text(subrecords: list[object], signature: str) -> str | None:
    for subrecord in subrecords:
        if subrecord.signature != signature:
            continue
        value = _safe_text(subrecord.data)
        if value is not None:
            return value
    return None


def _catalog_entry(
    record: Record,
    subrecords: list[object],
    source_order: int,
) -> dict[str, object] | None:
    editor_id = _catalog_text(subrecords, "EDID")
    name = _catalog_text(subrecords, "FULL")
    if not editor_id or not name:
        return None
    icon = _catalog_text(subrecords, "ICON")
    return {
        "recordType": record.signature,
        "formId": form_id_text(record.form_id),
        "sourceOrder": source_order,
        "dataSha256": hashlib.sha256(record.data).hexdigest(),
        "editorId": editor_id,
        "sourceName": editor_id,
        "name": name,
        "description": _catalog_text(subrecords, "DESC") or "",
        "iconLogicalPath": None if icon is None else _asset_path(icon),
    }


def _scan_flow_sources(
    master_path: Path,
    needed_form_ids: frozenset[int],
    trait_rules: dict[str, object],
) -> FlowSourceCatalog:
    actor_values = []
    traits = []
    scripts: dict[str, tuple[int, str]] = {}
    idle_animations_by_editor: dict[str, IdleAnimationSource] = {}
    idle_animations_by_form: dict[int, IdleAnimationSource] = {}
    animation_objects_by_idle_form: dict[int, list[AnimationObjectSource]] = defaultdict(list)
    packages_by_editor: dict[str, Record] = {}
    packages_by_form: dict[int, Record] = {}
    actors_by_form: dict[int, Record] = {}
    voice_types_by_form: dict[int, str] = {}
    references_by_form: dict[int, ReferenceTransformSource] = {}
    image_space_modifiers_by_editor: dict[str, Record] = {}
    game_settings_by_editor: dict[str, Record] = {}
    player_base: Record | None = None
    furniture_by_form: dict[int, Record] = {}
    appearance_records_by_form: dict[int, Record] = {}
    needed: dict[int, dict[str, object]] = {}
    selector_type = str(trait_rules["recordType"])
    selector_signature = str(trait_rules["selectorSubrecord"])
    selector = bytes.fromhex(str(trait_rules["selectorHex"]))
    source_order = 0
    for record in iter_plugin_records(master_path):
        source_order += 1
        subrecords = list(iter_subrecords(record))
        if record.signature == "AVIF":
            entry = _catalog_entry(record, subrecords, source_order)
            if entry is not None:
                actor_values.append(entry)
        if record.signature == "GMST":
            editor_id = _catalog_text(subrecords, "EDID")
            if (
                editor_id in REQUIRED_VITAL_GAME_SETTINGS
                or record.form_id in needed_form_ids
            ):
                identity = editor_id.casefold()
                if identity in game_settings_by_editor:
                    raise ValueError(
                        f"Owned vital game setting is duplicated: {editor_id}"
                    )
                game_settings_by_editor[identity] = record
        if record.signature == "NPC_":
            editor_id = _catalog_text(subrecords, "EDID")
            if editor_id == PLAYER_BASE_EDITOR_ID:
                if player_base is not None:
                    raise ValueError("Owned player base is duplicated")
                player_base = record
        if record.signature in {"RACE", "HAIR", "EYES"}:
            appearance_records_by_form[record.form_id] = record
        if record.signature == selector_type:
            selected_value = next(
                (
                    subrecord.data
                    for subrecord in subrecords
                    if subrecord.signature == selector_signature
                ),
                None,
            )
            if selected_value == selector:
                entry = _catalog_entry(record, subrecords, source_order)
                if entry is not None:
                    traits.append(entry)
        if record.signature == "SCPT":
            editor_id = _catalog_text(subrecords, "EDID")
            sources = [
                value
                for subrecord in subrecords
                if subrecord.signature == "SCTX"
                for value in [_safe_text(subrecord.data)]
                if value is not None
            ]
            if editor_id and len(sources) == 1:
                scripts[editor_id.casefold()] = (record.form_id, sources[0])
        if record.signature == "IDLE":
            editor_id = _catalog_text(subrecords, "EDID")
            model_path = _catalog_text(subrecords, "MODL")
            logical_path = None if model_path is None else _asset_path(model_path)
            if editor_id and logical_path:
                identity = editor_id.casefold()
                if identity in idle_animations_by_editor or record.form_id in idle_animations_by_form:
                    raise ValueError(
                        f"Owned opening idle animation is duplicated: {editor_id}"
                    )
                source = IdleAnimationSource(
                    record.form_id,
                    editor_id,
                    logical_path,
                    hashlib.sha256(record.data).hexdigest(),
                )
                idle_animations_by_editor[identity] = source
                idle_animations_by_form[record.form_id] = source
        if record.signature == "ANIO":
            editor_id = _catalog_text(subrecords, "EDID")
            model_path = _catalog_text(subrecords, "MODL")
            data = [
                subrecord.data
                for subrecord in subrecords
                if subrecord.signature == "DATA"
            ]
            if (
                editor_id
                and model_path
                and len(data) == 1
                and len(data[0]) == FORM_ID_BYTES
            ):
                idle_form_id = struct.unpack("<I", data[0])[0]
                animation_objects_by_idle_form[idle_form_id].append(
                    AnimationObjectSource(
                        record.form_id,
                        editor_id,
                        _asset_path(model_path),
                        idle_form_id,
                        hashlib.sha256(record.data).hexdigest(),
                    )
                )
        if record.signature == "PACK":
            editor_id = _catalog_text(subrecords, "EDID")
            if editor_id:
                identity = editor_id.casefold()
                if identity in packages_by_editor:
                    raise ValueError(f"Owned opening package is duplicated: {editor_id}")
                packages_by_editor[identity] = record
            if record.form_id in packages_by_form:
                raise ValueError(
                    f"Owned opening package form is duplicated: {record.form_id:08x}"
                )
            packages_by_form[record.form_id] = record
        if record.signature in {"NPC_", "CREA"}:
            if record.form_id in actors_by_form:
                raise ValueError(
                    f"Owned opening actor base is duplicated: {record.form_id:08x}"
                )
            actors_by_form[record.form_id] = record
        if record.signature == "FURN":
            if record.form_id in furniture_by_form:
                raise ValueError(
                    f"Owned opening furniture is duplicated: {record.form_id:08x}"
                )
            furniture_by_form[record.form_id] = record
        if record.signature == "VTYP":
            editor_id = _catalog_text(subrecords, "EDID")
            if not editor_id:
                raise ValueError(
                    f"Owned opening voice type has no editor ID: {record.form_id:08x}"
                )
            if record.form_id in voice_types_by_form:
                raise ValueError(
                    f"Owned opening voice type is duplicated: {record.form_id:08x}"
                )
            voice_types_by_form[record.form_id] = editor_id
        if record.signature in {"REFR", "ACHR", "ACRE"}:
            transform_values = [
                subrecord.data
                for subrecord in subrecords
                if subrecord.signature == "DATA"
            ]
            if len(transform_values) == 1 and len(transform_values[0]) == REFERENCE_TRANSFORM_BYTES:
                values = struct.unpack("<6f", transform_values[0])
                references_by_form[record.form_id] = ReferenceTransformSource(
                    record.form_id,
                    _catalog_text(subrecords, "EDID"),
                    record.signature,
                    tuple(values[:3]),
                    tuple(values[3:]),
                    next(
                        (
                            _subrecord_form_id(subrecord.data)
                            for subrecord in subrecords
                            if subrecord.signature == "NAME"
                        ),
                        None,
                    ),
                    hashlib.sha256(record.data).hexdigest(),
                )
        if record.signature == "IMAD":
            editor_id = _catalog_text(subrecords, "EDID")
            if editor_id:
                identity = editor_id.casefold()
                if identity in image_space_modifiers_by_editor:
                    raise ValueError(
                        f"Owned opening image-space modifier is duplicated: {editor_id}"
                    )
                image_space_modifiers_by_editor[identity] = record
        if record.form_id in needed_form_ids:
            needed[record.form_id] = {
                "recordType": record.signature,
                "formId": form_id_text(record.form_id),
                "editorId": _catalog_text(subrecords, "EDID"),
                "displayName": _catalog_text(subrecords, "FULL") or "",
                "links": [
                    {
                        "signature": subrecord.signature,
                        "formId": form_id_text(_subrecord_form_id(subrecord.data) or 0),
                    }
                    for subrecord in subrecords
                    if subrecord.signature in {"NAME", "SCRI", "VTCK"}
                    and _subrecord_form_id(subrecord.data)
                ],
            }
    if set(needed_form_ids) != set(needed):
        missing = sorted(form_id_text(value) for value in set(needed_form_ids) - set(needed))
        raise ValueError("Owned opening scene-role bases are missing: " + ", ".join(missing))
    return FlowSourceCatalog(
        actor_values=actor_values,
        traits=traits,
        scripts=scripts,
        idle_animations_by_editor=idle_animations_by_editor,
        idle_animations_by_form=idle_animations_by_form,
        packages_by_editor=packages_by_editor,
        packages_by_form=packages_by_form,
        actors_by_form=actors_by_form,
        voice_types_by_form=voice_types_by_form,
        references_by_form=references_by_form,
        image_space_modifiers_by_editor=image_space_modifiers_by_editor,
        needed=needed,
        animation_objects_by_idle_form={
            idle_form_id: tuple(sorted(values, key=lambda value: value.form_id))
            for idle_form_id, values in animation_objects_by_idle_form.items()
        },
        game_settings_by_editor=game_settings_by_editor,
        player_base=player_base,
        furniture_by_form=furniture_by_form,
        appearance_records_by_form=appearance_records_by_form,
    )


def _match_actor_values(
    actor_values: list[dict[str, object]],
    names: Iterable[str],
) -> list[dict[str, object]]:
    selected = []
    used = set()
    for source_name in dict.fromkeys(names):
        requested = _normalized_identifier(source_name)
        scored = []
        for row in actor_values:
            editor = _normalized_identifier(str(row["editorId"]))
            editor_without_prefix = editor[2:] if editor.startswith("av") else editor
            display = _normalized_identifier(str(row["name"]))
            score = (
                3
                if requested in {editor_without_prefix, display}
                else 2
                if editor_without_prefix.startswith(requested)
                or requested.startswith(editor_without_prefix)
                else 0
            )
            if score:
                scored.append((score, row))
        best = max((score for score, _ in scored), default=0)
        matches = [row for score, row in scored if score == best]
        if len(matches) != 1:
            raise ValueError(
                f"Owned actor value is ambiguous: {source_name} matches={len(matches)}"
            )
        match = matches[0]
        if match["formId"] in used:
            continue
        used.add(match["formId"])
        selected.append({**match, "sourceName": source_name})
    selected.sort(key=lambda value: int(str(value["formId"]), FORM_ID_RADIX))
    return selected


def _single_subrecord(record: Record, signature: str) -> bytes:
    values = [
        subrecord.data
        for subrecord in iter_subrecords(record)
        if subrecord.signature == signature
    ]
    if len(values) != 1:
        raise ValueError(
            f"Owned {record.signature} {form_id_text(record.form_id)} has no unique "
            f"{signature} subrecord"
        )
    return values[0]


def _game_setting_manifest(record: Record) -> dict[str, object]:
    editor_id = _catalog_text(list(iter_subrecords(record)), "EDID")
    if not editor_id:
        raise ValueError("Owned vital game setting has no editor ID")
    payload = _single_subrecord(record, "DATA")
    if len(payload) != 4:
        raise ValueError(f"Owned vital game setting has invalid DATA: {editor_id}")
    if editor_id.startswith("f"):
        value: float | int = struct.unpack("<f", payload)[0]
    elif editor_id.startswith("i"):
        value = struct.unpack("<i", payload)[0]
    else:
        raise ValueError(f"Owned vital game setting has unsupported type: {editor_id}")
    return {
        "editorId": editor_id,
        "formId": form_id_text(record.form_id),
        "recordSha256": hashlib.sha256(record.data).hexdigest(),
        "sourceKind": "owned-master-gmst",
        "value": value,
    }


def _compile_gameplay_vitals(sources: FlowSourceCatalog) -> dict[str, object]:
    if sources.player_base is None:
        raise ValueError("Owned opening player base is absent")
    player = sources.player_base
    acbs = _single_subrecord(player, "ACBS")
    data = _single_subrecord(player, "DATA")
    if len(acbs) != PLAYER_BASE_ACBS_BYTES or len(data) < PLAYER_BASE_DATA_MINIMUM_BYTES:
        raise ValueError("Owned opening player base has an unsupported ACBS/DATA layout")
    level = struct.unpack_from("<h", acbs, PLAYER_BASE_LEVEL_OFFSET)[0]
    base_health = struct.unpack_from("<I", data, PLAYER_BASE_HEALTH_OFFSET)[0]
    if level <= 0 or base_health <= 0:
        raise ValueError("Owned opening player level or base health is invalid")

    settings = []
    for editor_id in REQUIRED_VITAL_GAME_SETTINGS:
        record = sources.game_settings_by_editor.get(editor_id.casefold())
        if record is None:
            raise ValueError(f"Owned vital game setting is absent: {editor_id}")
        settings.append(_game_setting_manifest(record))
    settings.append(
        {
            "editorId": "iXPBase",
            "formId": None,
            "recordSha256": None,
            "sourceKind": "falloutnv-exact-build-engine-default",
            "engineBuild": FNV_ENGINE_BUILD,
            "evidenceId": FNV_ENGINE_DEFAULT_XP_BASE_EVIDENCE,
            "value": FNV_ENGINE_DEFAULT_XP_BASE,
        }
    )

    actor_values_by_editor = {
        str(value["editorId"]).casefold(): value for value in sources.actor_values
    }
    actor_values = []
    for editor_id in REQUIRED_VITAL_ACTOR_VALUES:
        value = actor_values_by_editor.get(editor_id.casefold())
        if value is None:
            raise ValueError(f"Owned vital actor value is absent: {editor_id}")
        actor_values.append(
            {
                "editorId": value["editorId"],
                "formId": value["formId"],
                "recordSha256": value["dataSha256"],
            }
        )

    return {
        "schema": GAMEPLAY_VITALS_SCHEMA,
        "playerBase": {
            "editorId": PLAYER_BASE_EDITOR_ID,
            "formId": form_id_text(player.form_id),
            "recordSha256": hashlib.sha256(player.data).hexdigest(),
            "initialLevel": level,
            "baseHealth": base_health,
        },
        "actorValues": actor_values,
        "gameSettings": settings,
        "initialExperiencePoints": 0,
        "derivations": {
            "maximumHitPoints": (
                "baseHealth + endurance * fAVDHealthEnduranceMult + "
                "(level - 1) * fAVDHealthLevelMult"
            ),
            "maximumActionPoints": (
                "fAVDActionPointsBase + agility * fAVDActionPointsMult"
            ),
            "experienceThreshold": (
                "(targetLevel - 1) * (((targetLevel - 2) * iXPBumpBase) / 2 + "
                "iXPBase)"
            ),
        },
    }


def _appearance_form_ids(record: Record, signature: str) -> tuple[int, ...]:
    payload = _single_subrecord(record, signature)
    if not payload or len(payload) % FORM_ID_BYTES:
        raise ValueError(
            f"Owned {record.signature} {form_id_text(record.form_id)} has an "
            f"invalid {signature} appearance list"
        )
    return struct.unpack(f"<{len(payload) // FORM_ID_BYTES}I", payload)


def _appearance_option(record: Record) -> dict[str, object]:
    subrecords = list(iter_subrecords(record))
    data = _single_subrecord(record, "DATA")
    if len(data) != APPEARANCE_PART_DATA_BYTES:
        raise ValueError(
            f"Owned {record.signature} {form_id_text(record.form_id)} has an "
            "unsupported appearance flag layout"
        )
    texture = _asset_path(_catalog_text(subrecords, "ICON") or "")
    model = _asset_path(_catalog_text(subrecords, "MODL") or "")
    if texture is None or (record.signature == "HAIR" and model is None):
        raise ValueError(
            f"Owned {record.signature} {form_id_text(record.form_id)} has no "
            "model/texture preview identity"
        )
    return {
        "formId": form_id_text(record.form_id),
        "recordType": record.signature,
        "editorId": _catalog_text(subrecords, "EDID"),
        "label": _catalog_text(subrecords, "FULL"),
        "recordSha256": hashlib.sha256(record.data).hexdigest(),
        "flags": data[0],
        "modelLogicalPath": model,
        "textureLogicalPath": texture,
    }


def _compile_player_appearance(
    sources: FlowSourceCatalog,
    quest_script_source: str,
) -> tuple[dict[str, object], tuple[str, ...]]:
    if sources.player_base is None:
        raise ValueError("Owned opening player base is absent")
    player = sources.player_base
    player_subrecords = list(iter_subrecords(player))
    default_race = _subrecord_form_id(_single_subrecord(player, "RNAM"))
    default_hair = _subrecord_form_id(_single_subrecord(player, "HNAM"))
    default_eyes = _subrecord_form_id(_single_subrecord(player, "ENAM"))
    if default_race is None or default_hair is None or default_eyes is None:
        raise ValueError("Owned opening player appearance defaults are incomplete")
    facegen = {}
    for role, signature, expected_count in (
        ("symmetricGeometry", "FGGS", FACEGEN_SYMMETRIC_GEOMETRY_FLOATS),
        ("asymmetricGeometry", "FGGA", FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS),
        ("symmetricTexture", "FGTS", FACEGEN_SYMMETRIC_TEXTURE_FLOATS),
    ):
        payload = _single_subrecord(player, signature)
        if len(payload) != expected_count * 4:
            raise ValueError(f"Owned opening player {signature} is incomplete")
        values = struct.unpack(f"<{expected_count}f", payload)
        if not all(math.isfinite(value) for value in values):
            raise ValueError(f"Owned opening player {signature} is non-finite")
        facegen[role] = {
            "count": expected_count,
            "values": list(values),
            "sha256": hashlib.sha256(payload).hexdigest(),
        }

    sex_mapping = {
        int(index): engine_sex.casefold()
        for index, engine_sex in re.findall(
            r"nButton\s*==\s*(\d+)\s*\r?\n\s*player\.sexChange\s+(male|female)\s+1",
            quest_script_source,
            re.IGNORECASE,
        )
    }
    if sex_mapping != {0: "male", 1: "female"}:
        raise ValueError("Owned opening sex-change command mapping differs")

    records = sources.appearance_records_by_form
    races = []
    texture_paths: set[str] = set()
    for race_record in sorted(
        (record for record in records.values() if record.signature == "RACE"),
        key=lambda record: record.form_id,
    ):
        subrecords = list(iter_subrecords(race_record))
        data = _single_subrecord(race_record, "DATA")
        if len(data) != RACE_DATA_BYTES:
            raise ValueError(
                f"Owned RACE {form_id_text(race_record.form_id)} DATA layout differs"
            )
        flags = struct.unpack_from("<I", data, RACE_FLAGS_OFFSET)[0]
        if not flags & RACE_PLAYABLE_FLAG:
            continue
        hair_form_ids = _appearance_form_ids(race_record, "HNAM")
        eye_form_ids = _appearance_form_ids(race_record, "ENAM")
        sex_rows = {}
        for engine_sex, sex_flag in (
            ("male", HAIR_MALE_FLAG),
            ("female", HAIR_FEMALE_FLAG),
        ):
            hair_options = []
            for form_id in hair_form_ids:
                record = records.get(form_id)
                if record is None or record.signature != "HAIR":
                    raise ValueError("Owned playable RACE hair list does not resolve")
                option = _appearance_option(record)
                if int(option["flags"]) & APPEARANCE_PART_PLAYABLE_FLAG and int(
                    option["flags"]
                ) & sex_flag:
                    hair_options.append(option)
                    texture_paths.add(str(option["textureLogicalPath"]))
            eye_options = []
            for form_id in eye_form_ids:
                record = records.get(form_id)
                if record is None or record.signature != "EYES":
                    raise ValueError("Owned playable RACE eye list does not resolve")
                option = _appearance_option(record)
                if int(option["flags"]) & APPEARANCE_PART_PLAYABLE_FLAG:
                    eye_options.append(option)
                    texture_paths.add(str(option["textureLogicalPath"]))
            if not hair_options or not eye_options:
                raise ValueError("Owned playable RACE has no sex-aware hair/eye options")
            sex_rows[engine_sex] = {
                "hairOptions": hair_options,
                "eyeOptions": eye_options,
                "defaultHairFormId": (
                    form_id_text(default_hair)
                    if race_record.form_id == default_race and any(
                        int(str(option["formId"]), FORM_ID_RADIX) == default_hair
                        for option in hair_options
                    )
                    else str(hair_options[0]["formId"])
                ),
                "defaultEyesFormId": (
                    form_id_text(default_eyes)
                    if race_record.form_id == default_race and any(
                        int(str(option["formId"]), FORM_ID_RADIX) == default_eyes
                        for option in eye_options
                    )
                    else str(eye_options[0]["formId"])
                ),
            }
        races.append(
            {
                "formId": form_id_text(race_record.form_id),
                "recordType": "RACE",
                "editorId": _catalog_text(subrecords, "EDID"),
                "label": _catalog_text(subrecords, "FULL"),
                "recordSha256": hashlib.sha256(race_record.data).hexdigest(),
                "flags": flags,
                "sex": sex_rows,
            }
        )
    if not races or default_race not in {
        int(str(row["formId"]), FORM_ID_RADIX) for row in races
    }:
        raise ValueError("Owned opening player default race is not playable")
    return (
        {
            "schema": "opennv-owned-player-appearance/v1",
            "status": "source-backed-interactive-selection",
            "player": {
                "formId": form_id_text(player.form_id),
                "editorId": _catalog_text(player_subrecords, "EDID"),
                "recordSha256": hashlib.sha256(player.data).hexdigest(),
                "defaultRaceFormId": form_id_text(default_race),
                "defaultHairFormId": form_id_text(default_hair),
                "defaultEyesFormId": form_id_text(default_eyes),
                "faceGen": facegen,
            },
            "sexEngineValues": [sex_mapping[index] for index in sorted(sex_mapping)],
            "races": races,
            "preview": "owned-hair-and-eye-source-textures-live-selection-not-3d-face-render",
        },
        tuple(sorted(texture_paths)),
    )


def compile_gameplay_vitals_from_master(master_path: Path) -> dict[str, object]:
    """Compile only the owned opening vitals contract without preparing world assets."""
    actor_values = []
    game_settings_by_editor: dict[str, Record] = {}
    player_base: Record | None = None
    source_order = 0
    for record in iter_plugin_records(master_path):
        source_order += 1
        if record.signature not in {"AVIF", "GMST", "NPC_"}:
            continue
        subrecords = list(iter_subrecords(record))
        editor_id = _catalog_text(subrecords, "EDID")
        if record.signature == "AVIF" and editor_id in REQUIRED_VITAL_ACTOR_VALUES:
            entry = _catalog_entry(record, subrecords, source_order)
            if entry is None:
                raise ValueError(f"Owned vital actor value is incomplete: {editor_id}")
            actor_values.append(entry)
        elif record.signature == "GMST" and editor_id in REQUIRED_VITAL_GAME_SETTINGS:
            identity = editor_id.casefold()
            if identity in game_settings_by_editor:
                raise ValueError(f"Owned vital game setting is duplicated: {editor_id}")
            game_settings_by_editor[identity] = record
        elif record.signature == "NPC_" and editor_id == PLAYER_BASE_EDITOR_ID:
            if player_base is not None:
                raise ValueError("Owned player base is duplicated")
            player_base = record
    return _compile_gameplay_vitals(
        FlowSourceCatalog(
            actor_values=actor_values,
            traits=[],
            scripts={},
            idle_animations_by_editor={},
            idle_animations_by_form={},
            packages_by_editor={},
            packages_by_form={},
            actors_by_form={},
            voice_types_by_form={},
            references_by_form={},
            image_space_modifiers_by_editor={},
            needed={},
            game_settings_by_editor=game_settings_by_editor,
            player_base=player_base,
        )
    )


def _deferred_stage_command(
    source: str,
    scripts: dict[str, tuple[int, str]],
    target_quest_editor_id: str,
) -> dict[str, object] | None:
    event_match = re.search(
        r"set\s+(\w+)\.nEvent\s+to\s+(\d+)", source, re.IGNORECASE
    )
    timer_match = re.search(
        r"set\s+(\w+)\.fTimer\s+to\s+(-?\d+(?:\.\d+)?)",
        source,
        re.IGNORECASE,
    )
    if event_match is None or timer_match is None:
        return None
    timer_quest = event_match[1]
    if timer_match[1].casefold() != timer_quest.casefold() or re.search(
        rf"StartQuest\s+{re.escape(timer_quest)}\b", source, re.IGNORECASE
    ) is None:
        return None
    script_row = scripts.get((timer_quest + "SCRIPT").casefold())
    if script_row is None:
        return None
    event = int(event_match[2])
    timer_source = "\n".join(_script_code_lines(script_row[1]))
    transition = re.search(
        rf"(?:if|elseif)\s*\(?\s*nEvent\s*==\s*{event}\b"
        rf"(?:(?!elseif\s*\(?\s*nEvent).)*?SetStage\s+"
        rf"{re.escape(target_quest_editor_id)}\s+(\d+)",
        timer_source,
        re.IGNORECASE | re.DOTALL,
    )
    if transition is None:
        return None
    return {
        "kind": "deferredStage",
        "questEditorId": target_quest_editor_id,
        "stage": int(transition[1]),
        "seconds": float(timer_match[2]),
        "sourceQuestEditorId": timer_quest,
        "sourceEvent": event,
    }


def _special_reaction_manifest(
    quest_script_source: str,
    special_values: list[dict[str, object]],
) -> dict[str, object]:
    average_matches = {
        float(match[1])
        for match in re.finditer(
            r"fCurrentValue\s+to\s+fCurrentValue\s*-\s*(\d+(?:\.\d+)?)",
            quest_script_source,
            re.IGNORECASE,
        )
    }
    threshold_match = re.search(
        r"fMaxDeviation\s*>=\s*(\d+(?:\.\d+)?)\s*&&\s*bLow\s*==\s*0"
        r".*?fMaxDeviation\s*>=\s*(\d+(?:\.\d+)?)\s*&&\s*bLow\s*==\s*1",
        quest_script_source,
        re.IGNORECASE | re.DOTALL,
    )
    if len(average_matches) != 1 or threshold_match is None:
        raise ValueError("Owned Vigor reaction thresholds are ambiguous")
    actor_rows = []
    actor_pattern = re.compile(
        r"set\s+fCurrentValue\s+to\s+Player\.GetActorValue\s+(\w+)"
        r"(?P<body>.*?)(?=set\s+fCurrentValue\s+to\s+Player\.GetActorValue|"
        r";\s*now choose)",
        re.IGNORECASE | re.DOTALL,
    )
    by_display = {
        _normalized_identifier(str(value["name"])): value
        for value in special_values
    }
    for order, match in enumerate(actor_pattern.finditer(quest_script_source)):
        code_match = re.search(
            r"nMostExtremeStat\s+to\s+(\d+)",
            match.group("body"),
            re.IGNORECASE,
        )
        if code_match is None:
            raise ValueError(f"Owned Vigor reaction code is absent: {match[1]}")
        code = int(code_match[1])
        reactions = [
            int(value)
            for value in re.findall(
                rf"nMostExtremeStat\s*==\s*{code}\).*?nDocReaction\s+to\s+(\d+)",
                quest_script_source,
                re.IGNORECASE | re.DOTALL,
            )
        ]
        if len(reactions) != 2:
            raise ValueError(
                f"Owned Vigor low/high reaction mapping is ambiguous: code={code}"
            )
        actor_value = by_display.get(_normalized_identifier(match[1]))
        if actor_value is None:
            raise ValueError(f"Owned Vigor actor value is absent: {match[1]}")
        actor_rows.append(
            {
                "formId": actor_value["formId"],
                "evaluationOrder": order,
                "lowReaction": reactions[0],
                "highReaction": reactions[1],
            }
        )
    if len(actor_rows) != len(special_values):
        raise ValueError("Owned Vigor reaction mapping does not cover every SPECIAL value")
    return {
        "averageValue": next(iter(average_matches)),
        "highDeviationThreshold": float(threshold_match[1]),
        "lowDeviationThreshold": float(threshold_match[2]),
        "defaultReaction": 0,
        "values": actor_rows,
    }


def _dialogue_info_manifest(
    record: dict[str, object],
    scripts: dict[str, tuple[int, str]],
    quest_editor_id: str,
) -> dict[str, object]:
    sources = _record_text_values(record, "SCTX")
    commands = [command for source in sources for command in _script_commands(source)]
    for source in sources:
        deferred = _deferred_stage_command(source, scripts, quest_editor_id)
        if deferred is not None:
            commands.append(deferred)
    dialogue_data = record["dialogueData"]
    if not isinstance(dialogue_data, dict):
        raise ValueError(f"Owned INFO has no dialogue DATA: {record['formId']}")
    flags = int(dialogue_data["flags"])
    return {
        "formId": record["formId"],
        "sourceOrder": record["sourceOrder"],
        "lines": [value for value in _record_text_values(record, "NAM1") if value],
        "scripts": sources,
        "commands": commands,
        "conditions": record["conditions"],
        "responseType": int(dialogue_data["responseType"]),
        "flags": flags,
        "goodbye": bool(flags & DIALOGUE_FLAG_GOODBYE),
        "sayOnce": bool(flags & DIALOGUE_FLAG_SAY_ONCE),
        "nextTopicFormIds": [
            link["formId"]
            for link in record["links"]
            if link["signature"] == "TCLT"
        ],
    }


def _compile_dialogue(
    records: list[dict[str, object]],
    programs: list[dict[str, object]],
    flow: dict[str, object],
    scripts: dict[str, tuple[int, str]],
    quest_editor_id: str,
) -> dict[str, object]:
    topics_by_form = {
        str(record["formId"]): record
        for record in records
        if record["recordType"] == "DIAL"
    }
    topics_by_editor = {
        _record_editor_id_from_manifest(record).casefold(): record
        for record in topics_by_form.values()
        if _record_editor_id_from_manifest(record)
    }
    infos_by_topic: dict[str, list[dict[str, object]]] = defaultdict(list)
    info_records = []
    for record in records:
        if record["recordType"] != "INFO":
            continue
        info_records.append(record)
        for group in record["groups"]:
            if int(group["type"]) == DIALOGUE_TOPIC_GROUP_TYPE:
                infos_by_topic[str(group["label"])].append(record)

    requested_editor_ids = {
        str(command["topicEditorId"])
        for program in programs
        for command in program["commands"]
        if command["kind"] == "sayTo"
    }
    requested_forms = set()
    for editor_id in requested_editor_ids:
        topic = topics_by_editor.get(editor_id.casefold())
        if topic is None:
            raise ValueError(f"Owned opening dialogue topic is missing: {editor_id}")
        requested_forms.add(str(topic["formId"]))

    discovery = dict(flow["dialogueDiscovery"])
    psychology_variable = str(discovery["psychologyStartQuestVariable"])
    psychology_start_stage = int(discovery["psychologyStartStage"])
    if psychology_start_stage not in {int(program["stage"]) for program in programs}:
        raise ValueError(
            f"Owned psychology start stage is not authored: {psychology_start_stage}"
        )
    psychology_matches = [
        record
        for record in info_records
        if any(
            re.search(
                rf"set\s+{re.escape(quest_editor_id)}\.{re.escape(psychology_variable)}"
                r"\s+to\s+1\b",
                source,
                re.IGNORECASE,
            )
            for source in _record_text_values(record, "SCTX")
        )
        and any(link["signature"] == "TCLT" for link in record["links"])
    ]
    if len(psychology_matches) != 1:
        raise ValueError(
            "Owned psychology dialogue root is ambiguous: "
            f"variable={psychology_variable} matches={len(psychology_matches)}"
        )
    psychology_root = _dialogue_info_manifest(
        psychology_matches[0], scripts, quest_editor_id
    )
    requested_forms.update(psychology_root["nextTopicFormIds"])

    outro_editor_id = str(discovery["outroTopicEditorId"])
    outro = topics_by_editor.get(outro_editor_id.casefold())
    if outro is None:
        raise ValueError(f"Owned opening outro topic is absent: {outro_editor_id}")
    requested_forms.add(str(outro["formId"]))

    closure = set()
    queue = deque(sorted(requested_forms))
    while queue:
        topic_form = queue.popleft()
        if topic_form in closure:
            continue
        if topic_form not in topics_by_form:
            raise ValueError(f"Owned linked dialogue topic is absent: {topic_form}")
        closure.add(topic_form)
        for info in infos_by_topic.get(topic_form, []):
            queue.extend(
                link["formId"]
                for link in info["links"]
                if link["signature"] == "TCLT"
            )

    topic_rows = []
    for form_id in sorted(closure, key=lambda value: int(value, FORM_ID_RADIX)):
        topic = topics_by_form[form_id]
        topic_infos = sorted(
            infos_by_topic.get(form_id, []),
            key=lambda value: int(value["sourceOrder"]),
        )
        topic_rows.append(
            {
                "formId": form_id,
                "editorId": _record_editor_id_from_manifest(topic),
                "prompt": next(iter(_record_text_values(topic, "FULL")), ""),
                "infos": [
                    _dialogue_info_manifest(info, scripts, quest_editor_id)
                    for info in topic_infos
                ],
            }
        )
    return {
        "topics": topic_rows,
        "psychologyRootInfo": psychology_root,
        "psychologyStartStage": psychology_start_stage,
        "outroTopicFormId": outro["formId"],
    }


def _prepare_dialogue_asset(
    member: ExtractedMember,
    cache_root: Path,
) -> dict[str, object]:
    source = cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
    if not source.is_file() or file_sha256(source) != member.sha256:
        atomic_bytes(source, member.data)
    if member.source_archive is None or member.source_archive_sha256 is None:
        raise ValueError(
            f"Owned dialogue asset has no archive provenance: {member.logical_path}"
        )
    return {
        "logicalPath": member.logical_path,
        "source": str(source.resolve()),
        "bytes": len(member.data),
        "sha256": member.sha256,
        "sourceArchive": member.source_archive,
        "sourceArchiveSha256": member.source_archive_sha256,
    }


def _compile_dialogue_voice(
    dialogue: dict[str, object],
    flow: dict[str, object],
    roles: list[dict[str, object]],
    sources: FlowSourceCatalog,
    audio_archives: OwnedArchiveStack,
    master_path: Path,
    cache_root: Path,
) -> None:
    rules = flow.get("dialogueVoice")
    if not isinstance(rules, dict):
        raise ValueError("Owned opening dialogue voice policy is absent")
    speaker_role = str(rules.get("speakerRole", ""))
    role_matches = [role for role in roles if role["role"] == speaker_role]
    if len(role_matches) != 1:
        raise ValueError(
            f"Owned opening dialogue speaker role is ambiguous: {speaker_role}"
        )
    role = role_matches[0]
    base_form_id = int(str(role["baseFormId"]), FORM_ID_RADIX)
    base = sources.needed[base_form_id]
    voice_links = [
        link["formId"]
        for link in base["links"]
        if link["signature"] == "VTCK"
    ]
    if len(voice_links) != 1:
        raise ValueError(
            f"Owned opening dialogue speaker has no unique voice type: {speaker_role}"
        )
    voice_type_form_id = int(str(voice_links[0]), FORM_ID_RADIX)
    voice_type_editor_id = sources.voice_types_by_form.get(voice_type_form_id)
    if voice_type_editor_id is None:
        raise ValueError(
            "Owned opening dialogue voice type record is absent: "
            + form_id_text(voice_type_form_id)
        )
    member_namespace = canonical_member_path(
        f"{VOICE_MEMBER_ROOT}\\{master_path.name}\\{voice_type_editor_id}"
    )
    member_prefix = member_namespace + "\\"
    response_members: dict[tuple[str, int], str] = {}
    for logical_path in audio_archives.members:
        if not logical_path.startswith(member_prefix):
            continue
        match = VOICE_RESPONSE_SUFFIX_PATTERN.search(logical_path)
        if match is None:
            continue
        key = (match.group("form").casefold(), int(match.group("index")))
        if key in response_members:
            raise ValueError(
                "Owned opening dialogue voice member is ambiguous: "
                f"info={key[0]} line={key[1]}"
            )
        response_members[key] = logical_path

    info_rows = [
        info
        for topic in dialogue["topics"]
        for info in topic["infos"]
    ]
    info_rows.append(dialogue["psychologyRootInfo"])
    responses_by_info: dict[str, list[dict[str, object]]] = {}
    response_count = 0
    for info in info_rows:
        info_form_id = str(info["formId"]).casefold()
        lines = [str(value) for value in info.pop("lines")]
        if info_form_id in responses_by_info:
            existing = responses_by_info[info_form_id]
            if [response["text"] for response in existing] != lines:
                raise ValueError(
                    f"Owned opening INFO response text differs: {info_form_id}"
                )
            info["responses"] = existing
            continue
        responses = []
        for line_index, line in enumerate(lines, start=1):
            key = (info_form_id, line_index)
            logical_path = response_members.get(key)
            if logical_path is None:
                raise ValueError(
                    "Owned opening dialogue voice is absent: "
                    f"info={info_form_id} line={line_index}"
                )
            lip_path = logical_path.removesuffix(VOICE_AUDIO_EXTENSION) + VOICE_LIP_EXTENSION
            if lip_path not in audio_archives.members:
                raise ValueError(
                    "Owned opening dialogue lip data is absent: "
                    f"info={info_form_id} line={line_index}"
                )
            voice_member = audio_archives.extract(logical_path)
            lip_member = audio_archives.extract(lip_path)
            responses.append(
                {
                    "index": line_index,
                    "text": line,
                    "voice": _prepare_dialogue_asset(voice_member, cache_root),
                    "lip": _prepare_dialogue_asset(lip_member, cache_root),
                }
            )
            response_count += 1
        if not responses:
            raise ValueError(f"Owned opening INFO has no responses: {info_form_id}")
        responses_by_info[info_form_id] = responses
        info["responses"] = responses
    dialogue["voice"] = {
        "speakerRole": speaker_role,
        "speakerReferenceFormId": role["referenceFormId"],
        "speakerBaseFormId": role["baseFormId"],
        "voiceTypeFormId": form_id_text(voice_type_form_id),
        "voiceTypeEditorId": voice_type_editor_id,
        "memberNamespace": member_namespace,
        "infoCount": len(responses_by_info),
        "responseCount": response_count,
        "archiveStack": audio_archives.manifest(),
    }


def _resolve_actor_animation_commands(
    programs: list[dict[str, object]],
    dialogue: dict[str, object],
    roles: list[dict[str, object]],
    idle_animations: dict[str, IdleAnimationSource],
) -> list[dict[str, object]]:
    roles_by_editor = {
        str(role["editorId"]).casefold(): role
        for role in roles
    }
    commands = [
        command
        for program in programs
        for command in program["commands"]
    ]
    commands.extend(
        command
        for topic in dialogue["topics"]
        for info in topic["infos"]
        for command in info["commands"]
    )
    commands.extend(dialogue["psychologyRootInfo"]["commands"])
    paths_by_reference: dict[str, list[str]] = defaultdict(list)
    for command in commands:
        if command["kind"] != "playIdle":
            continue
        idle_editor_id = str(command["idleEditorId"])
        source = idle_animations.get(idle_editor_id.casefold())
        if source is None:
            raise ValueError(
                f"Owned opening idle animation is unresolved: {idle_editor_id}"
            )
        logical_path = source.logical_path
        reference_editor_id = str(command["referenceEditorId"])
        role = roles_by_editor.get(reference_editor_id.casefold())
        if role is None or role["recordType"] not in {"ACHR", "ACRE"}:
            raise ValueError(
                "Owned opening idle target is not a compiled actor role: "
                + reference_editor_id
            )
        command["animationLogicalPath"] = logical_path
        command["idleFormId"] = form_id_text(source.form_id)
        command["idleRecordType"] = "IDLE"
        reference_form_id = str(role["referenceFormId"])
        if logical_path not in paths_by_reference[reference_form_id]:
            paths_by_reference[reference_form_id].append(logical_path)
    return [
        {
            "referenceFormId": reference_form_id,
            "logicalPaths": paths,
        }
        for reference_form_id, paths in sorted(
            paths_by_reference.items(),
            key=lambda value: int(value[0], FORM_ID_RADIX),
        )
    ]


def _all_flow_commands(
    programs: list[dict[str, object]],
    dialogue: dict[str, object],
) -> list[dict[str, object]]:
    commands = [
        command
        for program in programs
        for command in program["commands"]
    ]
    commands.extend(
        command
        for topic in dialogue["topics"]
        for info in topic["infos"]
        for command in info["commands"]
    )
    commands.extend(dialogue["psychologyRootInfo"]["commands"])
    return commands


def _resolve_command_record_identities(
    commands: Iterable[dict[str, object]],
    records: Iterable[dict[str, object]],
) -> dict[str, object]:
    records_by_editor: dict[str, list[dict[str, object]]] = defaultdict(list)
    for record in records:
        editor_id = _record_editor_id_from_manifest(record)
        if editor_id:
            records_by_editor[editor_id.casefold()].append(record)

    command_rows = list(commands)
    kind_counts: dict[str, int] = defaultdict(int)
    resolved_counts: dict[str, int] = defaultdict(int)
    for command in command_rows:
        kind = str(command.get("kind", ""))
        if kind not in OPENING_COMMAND_KINDS:
            raise ValueError(f"Owned opening command kind is unaccounted: {kind!r}")
        kind_counts[kind] += 1
        for editor_field, form_field, type_field, allowed_types in COMMAND_RECORD_FIELDS:
            editor_id = command.get(editor_field)
            if editor_id is None:
                continue
            matches = records_by_editor.get(str(editor_id).casefold(), [])
            if allowed_types is not None:
                matches = [
                    record
                    for record in matches
                    if str(record["recordType"]) in allowed_types
                ]
            if len(matches) != 1:
                raise ValueError(
                    "Owned opening command record is ambiguous: "
                    f"kind={kind} field={editor_field} editorId={editor_id} "
                    f"matches={len(matches)}"
                )
            record = matches[0]
            command[form_field] = record["formId"]
            command[type_field] = record["recordType"]
            resolved_counts[editor_field] += 1

    return {
        "schema": OPENING_COMMAND_CONTRACT_SCHEMA,
        "commandCount": len(command_rows),
        "kindCounts": dict(sorted(kind_counts.items())),
        "recordIdentityCounts": dict(sorted(resolved_counts.items())),
        "allEmittedKindsRuntimeBlocking": True,
        "allDeclaredRecordReferencesResolved": True,
    }


def _one_package_subrecord(
    values: dict[str, list[bytes]],
    signature: str,
    record: Record,
) -> bytes:
    matches = values.get(signature, [])
    if len(matches) != 1:
        raise ValueError(
            f"Owned PACK {record.form_id:08x} must contain one {signature}, "
            f"found {len(matches)}"
        )
    return matches[0]


def _compile_player_package(
    record: Record,
    event_subrecords: dict[str, str],
    idle_flag_bits: dict[str, object],
    idle_animations_by_form: dict[int, IdleAnimationSource],
) -> dict[str, object]:
    values: dict[str, list[bytes]] = defaultdict(list)
    event_idle_forms: dict[str, int | None] = {
        event: None for event in event_subrecords.values()
    }
    seen_event_idles = set()
    current_event = None
    for subrecord in iter_subrecords(record):
        values[subrecord.signature].append(subrecord.data)
        if subrecord.signature in event_subrecords:
            current_event = event_subrecords[subrecord.signature]
            continue
        if subrecord.signature == "INAM" and current_event is not None:
            if current_event in seen_event_idles:
                raise ValueError(
                    f"Owned PACK {record.form_id:08x} duplicates {current_event} INAM"
                )
            if len(subrecord.data) != PACKAGE_IDLE_FORM_BYTES:
                raise ValueError(
                    f"Owned PACK {record.form_id:08x} has malformed {current_event} INAM"
                )
            value = struct.unpack("<I", subrecord.data)[0]
            event_idle_forms[current_event] = value or None
            seen_event_idles.add(current_event)

    editor_id = _catalog_text(list(iter_subrecords(record)), "EDID")
    if editor_id is None:
        raise ValueError(f"Owned PACK {record.form_id:08x} has no editor ID")
    package_data = _one_package_subrecord(values, "PKDT", record)
    if len(package_data) < PACKAGE_DATA_MINIMUM_BYTES:
        raise ValueError(f"Owned PACK {record.form_id:08x} has malformed PKDT")
    package_flags = struct.unpack_from("<I", package_data)[0]
    package_type = (
        package_data[4]
        if len(package_data) >= PACKAGE_DATA_FNV_BYTES
        else struct.unpack_from("<i", package_data, PACKAGE_DATA_MINIMUM_BYTES)[0]
    )
    procedure_flags = (
        struct.unpack_from("<H", package_data, PACKAGE_PROCEDURE_FLAGS_OFFSET)[0]
        if len(package_data) >= PACKAGE_DATA_FNV_BYTES
        else None
    )
    type_specific_flags = (
        struct.unpack_from("<H", package_data, PACKAGE_TYPE_SPECIFIC_FLAGS_OFFSET)[0]
        if len(package_data) >= PACKAGE_DATA_FNV_BYTES
        else None
    )
    idle_flags_data = _one_package_subrecord(values, "IDLF", record)
    if len(idle_flags_data) not in {PACKAGE_IDLE_FLAGS_BYTES, PACKAGE_IDLE_FORM_BYTES}:
        raise ValueError(f"Owned PACK {record.form_id:08x} has malformed IDLF")
    idle_flags = idle_flags_data[0]
    idle_count_data = _one_package_subrecord(values, "IDLC", record)
    if len(idle_count_data) not in {PACKAGE_IDLE_COUNT_BYTES, PACKAGE_IDLE_FORM_BYTES}:
        raise ValueError(f"Owned PACK {record.form_id:08x} has malformed IDLC")
    idle_count = (
        idle_count_data[0]
        if len(idle_count_data) == PACKAGE_IDLE_COUNT_BYTES
        else struct.unpack("<I", idle_count_data)[0]
    )
    idle_timer_data = _one_package_subrecord(values, "IDLT", record)
    if len(idle_timer_data) != PACKAGE_IDLE_TIMER_BYTES:
        raise ValueError(f"Owned PACK {record.form_id:08x} has malformed IDLT")
    idle_timer = struct.unpack("<f", idle_timer_data)[0]
    idle_data = _one_package_subrecord(values, "IDLA", record)
    if len(idle_data) % PACKAGE_IDLE_FORM_BYTES:
        raise ValueError(f"Owned PACK {record.form_id:08x} has malformed IDLA")
    idle_forms = tuple(
        struct.unpack_from("<I", idle_data, offset)[0]
        for offset in range(0, len(idle_data), PACKAGE_IDLE_FORM_BYTES)
    )
    if idle_count != len(idle_forms):
        raise ValueError(
            f"Owned PACK {record.form_id:08x} idle count disagrees with IDLA"
        )
    referenced_forms = {
        *idle_forms,
        *(value for value in event_idle_forms.values() if value is not None),
    }
    missing = sorted(referenced_forms - set(idle_animations_by_form))
    if missing:
        raise ValueError(
            f"Owned PACK {record.form_id:08x} references missing IDLE records: "
            + ", ".join(form_id_text(value) for value in missing)
        )
    run_in_sequence_flag = int(idle_flag_bits["runInSequence"])
    do_once_flag = int(idle_flag_bits["doOnce"])
    return {
        "formId": form_id_text(record.form_id),
        "editorId": editor_id,
        "recordSha256": hashlib.sha256(record.data).hexdigest(),
        "packageFlags": package_flags,
        "packageType": package_type,
        "procedureFlags": procedure_flags,
        "typeSpecificFlags": type_specific_flags,
        "idleSelection": {
            "rawFlags": idle_flags,
            "runInSequence": bool(idle_flags & run_in_sequence_flag),
            "doOnce": bool(idle_flags & do_once_flag),
            "timerSeconds": idle_timer,
        },
        "idleAnimationFormIds": [form_id_text(value) for value in idle_forms],
        "events": {
            event: None if value is None else form_id_text(value)
            for event, value in sorted(event_idle_forms.items())
        },
    }


def _compile_player_animation_graph(
    commands: list[dict[str, object]],
    flow: dict[str, object],
    sources: FlowSourceCatalog,
    owned_archives: OwnedArchiveStack,
    configuration: RuntimeConfiguration,
) -> dict[str, object]:
    contract = dict(flow["playerAnimation"])
    requested_by_identity: dict[str, str] = {}
    for command in commands:
        if command["kind"] != "addScriptPackage":
            continue
        editor_id = str(command["packageEditorId"])
        requested_by_identity.setdefault(editor_id.casefold(), editor_id)
    requested_editor_ids = [
        requested_by_identity[identity]
        for identity in sorted(requested_by_identity)
    ]
    package_records = []
    for editor_id in requested_editor_ids:
        record = sources.packages_by_editor.get(editor_id.casefold())
        if record is None:
            raise ValueError(f"Owned opening package is unresolved: {editor_id}")
        package_records.append(record)
    event_subrecords = {
        str(signature): str(event)
        for signature, event in dict(contract["eventSubrecords"]).items()
    }
    if len(set(event_subrecords.values())) != len(event_subrecords):
        raise ValueError("Owned package-event subrecord mapping is not one-to-one")
    idle_flag_bits = dict(contract["idleFlagBits"])
    packages = [
        _compile_player_package(
            record,
            event_subrecords,
            idle_flag_bits,
            sources.idle_animations_by_form,
        )
        for record in package_records
    ]
    animation_form_ids = sorted(
        {
            int(value, FORM_ID_RADIX)
            for package in packages
            for value in [
                *package["idleAnimationFormIds"],
                *(
                    event_value
                    for event_value in package["events"].values()
                    if event_value is not None
                ),
            ]
        }
    )
    skeleton_path = canonical_member_path(str(contract["skeletonLogicalPath"]))
    skeleton = owned_archives.extract(skeleton_path)
    camera_node = str(contract["cameraNode"])
    samples_per_second = configuration.content_compiler.animation_samples_per_second
    animations = []
    for form_id in animation_form_ids:
        idle = sources.idle_animations_by_form[form_id]
        member = owned_archives.extract(idle.logical_path)
        animations.append(
            {
                "formId": form_id_text(form_id),
                "editorId": idle.editor_id,
                "logicalPath": idle.logical_path,
                "bytes": len(member.data),
                "sha256": member.sha256,
                "sourceArchive": member.source_archive,
                "sourceArchiveSha256": member.source_archive_sha256,
                "track": sample_transform_animation(
                    member.data,
                    skeleton.data,
                    camera_node,
                    samples_per_second,
                ).manifest(),
            }
        )
    return {
        "schema": "opennv-owned-player-animation-graph/v1",
        "cameraNode": camera_node,
        "skeleton": {
            "logicalPath": skeleton.logical_path,
            "bytes": len(skeleton.data),
            "sha256": skeleton.sha256,
            "sourceArchive": skeleton.source_archive,
            "sourceArchiveSha256": skeleton.source_archive_sha256,
        },
        "packages": packages,
        "animations": animations,
    }


def _configured_enum_name(
    values: dict[str, object],
    value: int,
    field: str,
) -> str:
    name = values.get(str(value))
    if not isinstance(name, str) or not name:
        raise ValueError(f"Owned opening {field} is unsupported: {value}")
    return name


def _compile_guide_package(
    record: Record,
    contract: dict[str, object],
    sources: FlowSourceCatalog,
) -> tuple[dict[str, object], tuple[str, ...]]:
    subrecords = list(iter_subrecords(record))
    values: dict[str, list[bytes]] = defaultdict(list)
    for subrecord in subrecords:
        values[subrecord.signature].append(subrecord.data)
    editor_id = _catalog_text(subrecords, "EDID")
    if editor_id is None:
        raise ValueError(f"Owned guide PACK {record.form_id:08x} has no editor ID")
    package_data = _one_package_subrecord(values, "PKDT", record)
    if len(package_data) < PACKAGE_DATA_FNV_BYTES:
        raise ValueError(f"Owned guide PACK {record.form_id:08x} has malformed PKDT")
    package_flags = struct.unpack_from("<I", package_data)[0]
    package_type = package_data[4]
    package_type_name = _configured_enum_name(
        dict(contract["packageTypeNames"]),
        package_type,
        "guide package type",
    )
    always_run_flag = int(dict(contract["packageFlagBits"])["alwaysRun"])

    locations = values.get("PLDT", [])
    if len(locations) > 1 or (locations and len(locations[0]) != PACKAGE_LOCATION_BYTES):
        raise ValueError(f"Owned guide PACK {record.form_id:08x} has malformed PLDT")
    location = None
    if locations:
        location_type, location_form, radius = struct.unpack("<III", locations[0])
        location_name = _configured_enum_name(
            dict(contract["locationTypeNames"]),
            location_type,
            "guide package location type",
        )
        destination = sources.references_by_form.get(location_form)
        if location_name == "nearReference" and destination is None:
            raise ValueError(
                f"Owned guide PACK {record.form_id:08x} references an absent "
                f"destination: {location_form:08x}"
            )
        location = {
            "type": location_type,
            "typeName": location_name,
            "formId": form_id_text(location_form),
            "radiusGameUnits": radius,
            "reference": None if destination is None else destination.manifest(),
        }

    targets = values.get("PTDT", [])
    if len(targets) > 1 or (targets and len(targets[0]) != PACKAGE_TARGET_BYTES):
        raise ValueError(f"Owned guide PACK {record.form_id:08x} has malformed PTDT")
    target = None
    if targets:
        target_type, target_form, count, target_unknown = struct.unpack(
            "<IIII", targets[0]
        )
        target = {
            "type": target_type,
            "typeName": _configured_enum_name(
                dict(contract["targetTypeNames"]),
                target_type,
                "guide package target type",
            ),
            "formId": form_id_text(target_form),
            "count": count,
            "unknown": target_unknown,
        }

    function_names = dict(contract["conditionFunctionNames"])
    conditions = []
    for condition_data in values.get("CTDA", []):
        condition = _condition_manifest(condition_data)
        condition["functionName"] = _configured_enum_name(
            function_names,
            int(condition["function"]),
            "guide package condition function",
        )
        conditions.append(condition)

    idle_forms: tuple[int, ...] = ()
    idle_paths: list[str] = []
    idle_payloads = values.get("IDLA", [])
    if idle_payloads:
        if len(idle_payloads) != 1 or len(idle_payloads[0]) % PACKAGE_IDLE_FORM_BYTES:
            raise ValueError(f"Owned guide PACK {record.form_id:08x} has malformed IDLA")
        idle_forms = tuple(
            struct.unpack_from("<I", idle_payloads[0], offset)[0]
            for offset in range(0, len(idle_payloads[0]), PACKAGE_IDLE_FORM_BYTES)
        )
        idle_counts = values.get("IDLC", [])
        if len(idle_counts) != 1 or len(idle_counts[0]) not in {
            PACKAGE_IDLE_COUNT_BYTES,
            PACKAGE_IDLE_FORM_BYTES,
        }:
            raise ValueError(f"Owned guide PACK {record.form_id:08x} has malformed IDLC")
        idle_count = (
            idle_counts[0][0]
            if len(idle_counts[0]) == PACKAGE_IDLE_COUNT_BYTES
            else struct.unpack("<I", idle_counts[0])[0]
        )
        if idle_count != len(idle_forms):
            raise ValueError(
                f"Owned guide PACK {record.form_id:08x} idle count disagrees with IDLA"
            )
        for form_id in idle_forms:
            idle = sources.idle_animations_by_form.get(form_id)
            if idle is None:
                raise ValueError(
                    f"Owned guide PACK {record.form_id:08x} references missing "
                    f"IDLE {form_id:08x}"
                )
            idle_paths.append(idle.logical_path)

    return (
        {
            "formId": form_id_text(record.form_id),
            "editorId": editor_id,
            "recordSha256": hashlib.sha256(record.data).hexdigest(),
            "packageFlags": package_flags,
            "alwaysRun": bool(package_flags & always_run_flag),
            "packageType": package_type,
            "packageTypeName": package_type_name,
            "procedureFlags": struct.unpack_from(
                "<H", package_data, PACKAGE_PROCEDURE_FLAGS_OFFSET
            )[0],
            "typeSpecificFlags": struct.unpack_from(
                "<H", package_data, PACKAGE_TYPE_SPECIFIC_FLAGS_OFFSET
            )[0],
            "conditions": conditions,
            "location": location,
            "target": target,
            "idleAnimationFormIds": [form_id_text(value) for value in idle_forms],
            "idleAnimationLogicalPaths": idle_paths,
        },
        tuple(idle_paths),
    )


def _compile_guide_animation_objects(
    idle_form_ids: Iterable[int],
    sources: FlowSourceCatalog,
    owned_archives: OwnedArchiveStack,
) -> list[dict[str, object]]:
    animation_objects = []
    for idle_form_id in sorted(idle_form_ids):
        idle = sources.idle_animations_by_form[idle_form_id]
        for animation_object in sources.animation_objects_by_idle_form.get(
            idle_form_id, ()
        ):
            member = owned_archives.extract(animation_object.logical_path)
            animation_objects.append(
                {
                    "componentRole": (
                        f"animation-object-{form_id_text(animation_object.form_id)}"
                    ),
                    "formId": form_id_text(animation_object.form_id),
                    "editorId": animation_object.editor_id,
                    "recordType": "ANIO",
                    "recordSha256": animation_object.record_sha256,
                    "idleAnimationFormId": form_id_text(idle.form_id),
                    "idleAnimationEditorId": idle.editor_id,
                    "idleAnimationLogicalPath": idle.logical_path,
                    "modelLogicalPath": member.logical_path,
                    "bytes": len(member.data),
                    "sha256": member.sha256,
                    "sourceArchive": member.source_archive,
                    "sourceArchiveSha256": member.source_archive_sha256,
                    "attachmentNode": authored_rigid_attachment_node(member.data),
                }
            )
    return animation_objects


def _compile_guide_furniture_animation(
    role: str,
    expected: dict[str, object],
    sources: FlowSourceCatalog,
    owned_archives: OwnedArchiveStack,
    root_motion_node: str | None = None,
    animation_samples_per_second: float | None = None,
) -> dict[str, object]:
    form_id = int(str(expected["formId"]), FORM_ID_RADIX)
    source = sources.idle_animations_by_form.get(form_id)
    if source is None:
        raise ValueError(
            f"Owned guide furniture {role} IDLE is absent: {form_id:08x}"
        )
    logical_path = canonical_member_path(str(expected["logicalPath"]))
    expected_identity = {
        "editorId": str(expected["editorId"]),
        "logicalPath": logical_path,
        "recordSha256": str(expected["recordSha256"]).casefold(),
    }
    actual_identity = {
        "editorId": source.editor_id,
        "logicalPath": canonical_member_path(source.logical_path),
        "recordSha256": source.record_sha256.casefold(),
    }
    if actual_identity != expected_identity:
        raise ValueError(
            f"Owned guide furniture {role} IDLE differs from the strict recipe: "
            f"expected={expected_identity} actual={actual_identity}"
        )
    member = owned_archives.extract(logical_path)
    expected_sha256 = str(expected["sha256"]).casefold()
    if member.sha256 != expected_sha256:
        raise ValueError(
            f"Owned guide furniture {role} KF hash differs: "
            f"expected={expected_sha256} actual={member.sha256}"
        )
    playback = animation_sequence_manifest(member.data)
    expected_playback = {
        "sequenceName": str(expected["sequenceName"]),
        "cycleType": int(expected["cycleType"]),
    }
    actual_playback = {
        "sequenceName": playback["sequenceName"],
        "cycleType": playback["cycleType"],
    }
    if actual_playback != expected_playback:
        raise ValueError(
            f"Owned guide furniture {role} playback differs from the strict recipe: "
            f"expected={expected_playback} actual={actual_playback}"
        )
    result = {
        "role": role,
        "formId": form_id_text(source.form_id),
        "editorId": source.editor_id,
        "recordType": "IDLE",
        "recordSha256": source.record_sha256,
        "logicalPath": logical_path,
        "bytes": len(member.data),
        "sha256": member.sha256,
        "sourceArchive": member.source_archive,
        "sourceArchiveSha256": member.source_archive_sha256,
        **playback,
    }
    if (root_motion_node is None) != (animation_samples_per_second is None):
        raise ValueError(
            f"Owned guide furniture {role} root-motion sampling is incomplete"
        )
    if root_motion_node is not None and animation_samples_per_second is not None:
        root_motion = sample_root_motion(
            member.data,
            root_motion_node,
            animation_samples_per_second,
        ).manifest()
        if {
            "sequenceName": root_motion["sequenceName"],
            "startSeconds": root_motion["startSeconds"],
            "stopSeconds": root_motion["stopSeconds"],
            "cycleType": root_motion["cycleType"],
        } != {
            "sequenceName": playback["sequenceName"],
            "startSeconds": playback["startSeconds"],
            "stopSeconds": playback["stopSeconds"],
            "cycleType": playback["cycleType"],
        }:
            raise ValueError(
                f"Owned guide furniture {role} root motion differs from playback"
            )
        result["rootMotion"] = root_motion
    return result


def _compile_guide_furniture_occupancy(
    contract: dict[str, object],
    packages: list[dict[str, object]],
    sources: FlowSourceCatalog,
    owned_archives: OwnedArchiveStack,
    quest_form_id: str,
    root_motion_node: str,
    animation_samples_per_second: float,
) -> tuple[dict[str, object], tuple[str, ...]]:
    source = dict(contract["furnitureOccupancy"])
    initial_package_form_id = str(source["initialPackageFormId"]).casefold()
    release_package_form_id = str(source["releasePackageFormId"]).casefold()
    reference_form_id = str(source["referenceFormId"]).casefold()
    release_stage = int(source["releaseStage"])
    marker_id = int(source["markerId"])
    animation_object_idle_form_id = str(
        source["animationObjectIdleFormId"]
    ).casefold()
    if marker_id != DOC_INITIAL_CHAIR_MARKER_ID:
        raise ValueError(
            "Owned initial guide furniture marker differs from the strict marker-14 evidence"
        )
    initial = next(
        (
            package
            for package in packages
            if str(package["formId"]).casefold() == initial_package_form_id
        ),
        None,
    )
    release = next(
        (
            package
            for package in packages
            if str(package["formId"]).casefold() == release_package_form_id
        ),
        None,
    )
    if initial is None or release is None:
        raise ValueError("Owned guide furniture packages are absent")
    reference = sources.references_by_form.get(int(reference_form_id, FORM_ID_RADIX))
    furniture_source = dict(source["furniture"])
    expected_base_form_id = int(str(furniture_source["baseFormId"]), FORM_ID_RADIX)
    if (
        reference is None
        or reference.record_type != "REFR"
        or reference.base_form_id != expected_base_form_id
        or reference.record_sha256.casefold()
        != str(furniture_source["referenceRecordSha256"]).casefold()
    ):
        raise ValueError(
            "Owned initial guide furniture reference/base differs from the strict recipe"
        )
    furniture_record = sources.furniture_by_form.get(expected_base_form_id)
    if furniture_record is None:
        raise ValueError("Owned initial guide FURN base is absent")
    furniture_subrecords = list(iter_subrecords(furniture_record))
    model_paths = [
        value
        for value in (
            _catalog_text(furniture_subrecords, "MODL"),
        )
        if value is not None
    ]
    if len(model_paths) != 1:
        raise ValueError("Owned initial guide FURN has no unique model")
    model_path = canonical_member_path(_asset_path(model_paths[0]) or "")
    expected_furniture_identity = {
        "editorId": str(furniture_source["editorId"]),
        "recordSha256": str(furniture_source["recordSha256"]).casefold(),
        "modelLogicalPath": canonical_member_path(
            str(furniture_source["modelLogicalPath"])
        ),
    }
    actual_furniture_identity = {
        "editorId": _catalog_text(furniture_subrecords, "EDID"),
        "recordSha256": hashlib.sha256(furniture_record.data).hexdigest(),
        "modelLogicalPath": model_path,
    }
    if actual_furniture_identity != expected_furniture_identity:
        raise ValueError(
            "Owned initial guide FURN differs from the strict recipe: "
            f"expected={expected_furniture_identity} actual={actual_furniture_identity}"
        )
    furniture_member = owned_archives.extract(model_path)
    expected_model_sha256 = str(furniture_source["modelSha256"]).casefold()
    if furniture_member.sha256 != expected_model_sha256:
        raise ValueError(
            "Owned initial guide furniture NIF hash differs: "
            f"expected={expected_model_sha256} actual={furniture_member.sha256}"
        )
    marker = furniture_marker_manifest(furniture_member.data, marker_id)
    expected_marker = dict(furniture_source["marker"])
    expected_marker_identity = {
        "extraDataName": str(expected_marker["extraDataName"]),
        "index": int(expected_marker["index"]),
        "positionRef1": int(expected_marker["positionRef1"]),
        "positionRef2": int(expected_marker["positionRef2"]),
        "offsetNifGameUnits": [
            float(value) for value in expected_marker["offsetNifGameUnits"]
        ],
        "orientation": int(expected_marker["orientation"]),
        "animationType": int(expected_marker["animationType"]),
    }
    actual_marker_identity = {
        key: marker[key] for key in expected_marker_identity
    }
    if actual_marker_identity != expected_marker_identity:
        raise ValueError(
            "Owned initial guide furniture marker differs from the strict recipe: "
            f"expected={expected_marker_identity} actual={actual_marker_identity}"
        )
    marker["rotationGodotQuaternion"] = godot_rotation_quaternion(
        (0.0, 0.0, float(marker["orientationRadians"]))
    )
    expected_placement = dict(
        expected_marker["actorPlacementOffsetGameSettings"]
    )
    placement_semantics = str(expected_placement["semantics"])
    if placement_semantics != FURNITURE_MARKER_PLACEMENT_SEMANTICS:
        raise ValueError(
            "Owned furniture marker actor-placement semantics are unsupported"
        )
    placement_settings: dict[str, object] = {
        "semantics": placement_semantics,
    }
    placement_values: list[float] = []
    for axis in FURNITURE_MARKER_PLACEMENT_AXES:
        expected_axis = dict(expected_placement[axis])
        form_id = int(str(expected_axis["formId"]), FORM_ID_RADIX)
        editor_id = str(expected_axis["editorId"])
        record = sources.game_settings_by_editor.get(editor_id.casefold())
        if record is None or record.form_id != form_id:
            raise ValueError(
                f"Owned furniture marker actor-placement {axis} setting is absent"
            )
        setting = _game_setting_manifest(record)
        expected_identity = {
            "formId": form_id_text(form_id),
            "editorId": editor_id,
            "recordSha256": str(expected_axis["recordSha256"]).casefold(),
            "value": float(expected_axis["valueGameUnits"]),
        }
        actual_identity = {key: setting[key] for key in expected_identity}
        if actual_identity != expected_identity:
            raise ValueError(
                f"Owned furniture marker actor-placement {axis} differs from "
                "the strict recipe"
            )
        placement_settings[axis] = setting
        placement_values.append(float(setting["value"]))
    placement_settings["offsetNifGameUnits"] = placement_values
    placement_settings["offsetGodotGameUnits"] = [
        placement_values[0],
        placement_values[2],
        -placement_values[1],
    ]
    marker["actorPlacementOffsetGameSettings"] = placement_settings
    expected_heading_delta = dict(
        expected_marker["actorForwardHeadingDeltaGameSetting"]
    )
    heading_delta_form_id = int(
        str(expected_heading_delta["formId"]), FORM_ID_RADIX
    )
    heading_delta_editor_id = str(expected_heading_delta["editorId"])
    heading_delta_record = sources.game_settings_by_editor.get(
        heading_delta_editor_id.casefold()
    )
    if heading_delta_record is None or heading_delta_record.form_id != heading_delta_form_id:
        raise ValueError(
            "Owned furniture marker actor-forward heading delta is absent"
        )
    heading_delta = _game_setting_manifest(heading_delta_record)
    expected_heading_delta_identity = {
        "formId": form_id_text(heading_delta_form_id),
        "editorId": heading_delta_editor_id,
        "recordSha256": str(expected_heading_delta["recordSha256"]).casefold(),
        "value": float(expected_heading_delta["valueRadians"]),
    }
    actual_heading_delta_identity = {
        key: heading_delta[key] for key in expected_heading_delta_identity
    }
    if actual_heading_delta_identity != expected_heading_delta_identity:
        raise ValueError(
            "Owned furniture marker actor-forward heading delta differs from "
            "the strict recipe"
        )
    heading_delta["rotationGodotQuaternion"] = godot_rotation_quaternion(
        (0.0, 0.0, float(heading_delta["value"]))
    )
    marker["actorForwardHeadingDeltaGameSetting"] = heading_delta
    initial_location = initial.get("location")
    if not isinstance(initial_location, dict) or str(
        initial_location["formId"]
    ).casefold() != reference_form_id:
        raise ValueError(
            "Owned initial guide furniture package does not target the strict reference"
        )
    if animation_object_idle_form_id not in {
        str(value).casefold() for value in initial["idleAnimationFormIds"]
    }:
        raise ValueError(
            "Owned initial guide furniture package does not own the animation object idle"
        )
    release_conditions = release["conditions"]
    if not isinstance(release_conditions, list) or not any(
        str(condition["functionName"]).casefold() == "getstage"
        and str(condition["parameter1"]).casefold() == quest_form_id.casefold()
        and int(condition["operatorFlags"]) == CONDITION_OPERATOR_GREATER_OR_EQUAL
        and float(condition["comparisonValue"]) == float(release_stage)
        for condition in release_conditions
    ):
        raise ValueError(
            "Owned guide furniture release package lacks the strict stage condition"
        )
    seated_loop = _compile_guide_furniture_animation(
        "seatedLoop",
        dict(source["seatedLoop"]),
        sources,
        owned_archives,
    )
    exit_animation = _compile_guide_furniture_animation(
        "exit",
        dict(source["exit"]),
        sources,
        owned_archives,
        root_motion_node,
        animation_samples_per_second,
    )
    if int(seated_loop["cycleType"]) != 0 or int(exit_animation["cycleType"]) != 2:
        raise ValueError(
            "Owned guide furniture loop/exit cycles are not loop/clamp"
        )
    return (
        {
            "schema": "opennv-owned-guide-furniture-occupancy/v2",
            "initialPackageFormId": initial_package_form_id,
            "referenceFormId": reference_form_id,
            "markerId": marker_id,
            "markerDisposition": (
                "compose-owned-furniture-reference-gmst-replacement-offset-and-heading-delta"
            ),
            "furniture": {
                "referenceFormId": reference_form_id,
                "referenceRecordSha256": reference.record_sha256,
                "baseFormId": form_id_text(expected_base_form_id),
                "editorId": actual_furniture_identity["editorId"],
                "recordType": "FURN",
                "recordSha256": actual_furniture_identity["recordSha256"],
                "modelLogicalPath": model_path,
                "modelBytes": len(furniture_member.data),
                "modelSha256": furniture_member.sha256,
                "sourceArchive": furniture_member.source_archive,
                "sourceArchiveSha256": furniture_member.source_archive_sha256,
                "marker": marker,
            },
            "releaseStage": release_stage,
            "releasePackageFormId": release_package_form_id,
            "animationObjectIdleFormId": animation_object_idle_form_id,
            "seatedLoop": seated_loop,
            "exit": exit_animation,
        },
        (str(seated_loop["logicalPath"]), str(exit_animation["logicalPath"])),
    )


def _compile_guide_actor_ai(
    flow: dict[str, object],
    roles: list[dict[str, object]],
    sources: FlowSourceCatalog,
    owned_archives: OwnedArchiveStack,
    configuration: RuntimeConfiguration,
    quest_form_id: str,
) -> tuple[dict[str, object], tuple[str, ...]]:
    contract = dict(flow["guideActorAi"])
    role_name = str(contract["role"])
    role = next((value for value in roles if value["role"] == role_name), None)
    if role is None or role["recordType"] != "ACHR":
        raise ValueError("Owned opening guide AI role is not one ACHR")
    actor_form = int(str(role["baseFormId"]), FORM_ID_RADIX)
    actor = sources.actors_by_form.get(actor_form)
    if actor is None or actor.signature != "NPC_":
        raise ValueError("Owned opening guide AI base is not one NPC_")
    package_form_ids = [
        struct.unpack("<I", subrecord.data)[0]
        for subrecord in iter_subrecords(actor)
        if subrecord.signature == "PKID" and len(subrecord.data) == FORM_ID_BYTES
    ]
    if not package_form_ids:
        raise ValueError("Owned opening guide AI base has no packages")
    packages = []
    animation_paths: list[str] = []
    idle_form_ids: set[int] = set()
    for form_id in package_form_ids:
        package_record = sources.packages_by_form.get(form_id)
        if package_record is None:
            raise ValueError(
                f"Owned opening guide AI package is absent: {form_id:08x}"
            )
        package, idle_paths = _compile_guide_package(
            package_record,
            contract,
            sources,
        )
        packages.append(package)
        animation_paths.extend(idle_paths)
        idle_form_ids.update(
            int(value, FORM_ID_RADIX)
            for value in package["idleAnimationFormIds"]
        )
    locomotion_contract = dict(contract["locomotion"])
    root_node = str(locomotion_contract["rootNode"])
    furniture_occupancy, furniture_animation_paths = (
        _compile_guide_furniture_occupancy(
            contract,
            packages,
            sources,
            owned_archives,
            quest_form_id,
            root_node,
            configuration.content_compiler.animation_samples_per_second,
        )
    )
    animation_paths.extend(furniture_animation_paths)
    animation_objects = _compile_guide_animation_objects(
        idle_form_ids,
        sources,
        owned_archives,
    )
    required_animation_objects = [
        int(str(value), FORM_ID_RADIX)
        for value in contract.get("requiredAnimationObjectFormIds", [])
    ]
    actual_animation_objects = [
        int(str(value["formId"]), FORM_ID_RADIX)
        for value in animation_objects
    ]
    if actual_animation_objects != required_animation_objects:
        raise ValueError(
            "Owned opening guide animation objects differ from the strict recipe: "
            f"expected={[form_id_text(value) for value in required_animation_objects]} "
            f"actual={[form_id_text(value) for value in actual_animation_objects]}"
        )

    locomotion = {}
    for mode, field in (("walk", "walkLogicalPath"), ("run", "runLogicalPath")):
        logical_path = canonical_member_path(str(locomotion_contract[field]))
        member = owned_archives.extract(logical_path)
        locomotion[mode] = {
            "logicalPath": logical_path,
            "bytes": len(member.data),
            "sha256": member.sha256,
            "sourceArchive": member.source_archive,
            "sourceArchiveSha256": member.source_archive_sha256,
            "rootMotion": sample_root_motion(
                member.data,
                root_node,
                configuration.content_compiler.animation_samples_per_second,
            ).manifest(),
        }
        animation_paths.append(logical_path)
    return (
        {
            "schema": "opennv-owned-guide-actor-ai/v3",
            "role": role_name,
            "referenceFormId": role["referenceFormId"],
            "baseFormId": role["baseFormId"],
            "questFormId": quest_form_id,
            "packagePriority": [form_id_text(value) for value in package_form_ids],
            "packages": packages,
            "furnitureOccupancy": furniture_occupancy,
            "animationObjects": animation_objects,
            "locomotion": locomotion,
        },
        tuple(dict.fromkeys(animation_paths)),
    )


def _merge_actor_animation_paths(
    actor_animations: list[dict[str, object]],
    reference_form_id: str,
    logical_paths: Iterable[str],
) -> None:
    row = next(
        (
            value
            for value in actor_animations
            if str(value["referenceFormId"]).casefold()
            == reference_form_id.casefold()
        ),
        None,
    )
    if row is None:
        row = {"referenceFormId": reference_form_id, "logicalPaths": []}
        actor_animations.append(row)
    paths = row["logicalPaths"]
    if not isinstance(paths, list):
        raise ValueError("Owned opening actor-animation paths are malformed")
    identities = {str(value).casefold() for value in paths}
    for path in logical_paths:
        canonical = canonical_member_path(str(path))
        if canonical.casefold() not in identities:
            paths.append(canonical)
            identities.add(canonical.casefold())
    actor_animations.sort(
        key=lambda value: int(str(value["referenceFormId"]), FORM_ID_RADIX)
    )


def _compile_image_space_modifiers(
    commands: list[dict[str, object]],
    sources: FlowSourceCatalog,
) -> list[dict[str, object]]:
    editor_ids = sorted(
        {
            str(command["modifierEditorId"])
            for command in commands
            if command["kind"] == "imageSpaceModifier"
        },
        key=str.casefold,
    )
    result = []
    for editor_id in editor_ids:
        record = sources.image_space_modifiers_by_editor.get(editor_id.casefold())
        if record is None:
            raise ValueError(
                f"Owned opening image-space modifier is unresolved: {editor_id}"
            )
        result.append(parse_image_space_modifier(record).manifest())
    return result


def _interaction_from_script(
    source: str,
    script_editor_id: str,
    role: str,
    role_form_id: str,
    quest_editor_id: str,
    authored_stages: list[int],
) -> dict[str, object]:
    code = "\n".join(_script_code_lines(source))
    targets = {
        int(match[1])
        for match in re.finditer(
            rf"SetStage\s+{re.escape(quest_editor_id)}\s+(\d+)",
            code,
            re.IGNORECASE,
        )
    }
    if len(targets) != 1:
        raise ValueError(
            f"Owned opening interaction target is ambiguous: {script_editor_id} {targets}"
        )
    target = next(iter(targets))
    from_matches = {
        int(match[1])
        for match in re.finditer(
            rf"GetStage\s+{re.escape(quest_editor_id)}\s*==\s*(\d+)",
            code,
            re.IGNORECASE,
        )
    }
    if len(from_matches) > 1:
        raise ValueError(
            f"Owned opening interaction source is ambiguous: {script_editor_id} {from_matches}"
        )
    source_stage = (
        next(iter(from_matches))
        if from_matches
        else max(stage for stage in authored_stages if stage < target)
    )
    event = (
        "activate"
        if re.search(r"BEGIN\s+OnActivate", code, re.IGNORECASE)
        else "proximity"
        if re.search(r"OnTriggerEnter", code, re.IGNORECASE)
        else None
    )
    if event is None:
        raise ValueError(f"Owned opening interaction event is unsupported: {script_editor_id}")
    commands = _script_commands(source)
    menu = next(
        (command for command in commands if command["kind"] == "showMenu"),
        None,
    )
    return {
        "event": event,
        "scriptEditorId": script_editor_id,
        "targetRole": role,
        "targetReferenceFormId": role_form_id,
        "fromStage": source_stage,
        "toStage": target,
        "menu": menu,
        "distancePolicy": "configured-player-activation-distance",
    }


def compile_new_game_flow(
    master_path: Path,
    records: list[dict[str, object]],
    flow: dict[str, object],
    owned_archives: OwnedArchiveStack,
    audio_archives: OwnedArchiveStack,
    cache_root: Path,
    configuration: RuntimeConfiguration,
) -> tuple[dict[str, object], tuple[str, ...]]:
    quest_editor_id = str(flow["questEditorId"])
    quest = _unique_manifest_record(records, quest_editor_id, "QUST")
    quest_script = _unique_manifest_record(
        records, str(flow["questScriptEditorId"]), "SCPT"
    )
    quest_script_sources = _record_text_values(quest_script, "SCTX")
    if len(quest_script_sources) != 1:
        raise ValueError("Owned opening quest script source is ambiguous")
    programs, timer_transitions, menu_close_transitions = _stage_programs(
        quest, quest_script_sources[0]
    )
    roles = []
    needed_forms = set()
    for role, editor_id in dict(flow["sceneRoles"]).items():
        record = _unique_manifest_record(records, str(editor_id))
        base_links = [
            link["formId"] for link in record["links"] if link["signature"] == "NAME"
        ]
        if len(base_links) != 1:
            raise ValueError(f"Owned opening scene role has no unique base: {role}")
        needed_forms.add(int(base_links[0], FORM_ID_RADIX))
        roles.append(
            {
                "role": str(role),
                "editorId": str(editor_id),
                "recordType": record["recordType"],
                "referenceFormId": record["formId"],
                "baseFormId": base_links[0],
            }
        )
    furniture_marker = dict(
        dict(
            dict(flow["guideActorAi"])["furnitureOccupancy"]
        )["furniture"]
    )["marker"]
    heading_delta_setting = dict(
        dict(furniture_marker)["actorForwardHeadingDeltaGameSetting"]
    )
    needed_forms.add(
        int(str(heading_delta_setting["formId"]), FORM_ID_RADIX)
    )
    placement_settings = dict(
        dict(furniture_marker)["actorPlacementOffsetGameSettings"]
    )
    for axis in FURNITURE_MARKER_PLACEMENT_AXES:
        needed_forms.add(
            int(str(dict(placement_settings[axis])["formId"]), FORM_ID_RADIX)
        )
    role_by_name = {row["role"]: row for row in roles}

    character_rules = dict(flow["characterRules"])
    sources = _scan_flow_sources(
        master_path,
        frozenset(needed_forms),
        dict(character_rules["traits"]),
    )
    for role in roles:
        base = sources.needed[int(str(role["baseFormId"]), FORM_ID_RADIX)]
        role["displayName"] = base["displayName"] or base["editorId"] or role["editorId"]
    special_names = []
    for match in re.finditer(
        r"Player\.GetActorValue\s+(\w+)",
        quest_script_sources[0],
        re.IGNORECASE,
    ):
        special_names.append(match[1])
    special_rules = dict(character_rules["special"])
    special_icon_selector = str(special_rules["catalogIconPathContains"]).casefold()
    special = _match_actor_values(
        [
            value
            for value in sources.actor_values
            if special_icon_selector in str(value["iconLogicalPath"]).casefold()
        ],
        special_names,
    )
    skill_names_by_owner: dict[str, list[str]] = defaultdict(list)
    for record in records:
        if not any(
            link["signature"] == "QSTI" and link["formId"] == quest["formId"]
            for link in record["links"]
        ):
            continue
        for source in _record_text_values(record, "SCTX"):
            for match in re.finditer(
                r"set\s+(\w+)\.n([A-Za-z]+)\s+to\s+\1\.n\2\s*\+",
                source,
                re.IGNORECASE,
            ):
                skill_names_by_owner[match[1].casefold()].append(match[2])
    if not skill_names_by_owner:
        raise ValueError("Owned opening psychology skill calculation is absent")
    skill_owner, skill_names = max(
        skill_names_by_owner.items(),
        key=lambda value: len(set(name.casefold() for name in value[1])),
    )
    if sum(
        len(set(name.casefold() for name in values))
        == len(set(name.casefold() for name in skill_names))
        for values in skill_names_by_owner.values()
    ) != 1:
        raise ValueError("Owned opening psychology skill owner is ambiguous")
    tag_skill_rules = dict(character_rules["tagSkills"])
    skill_icon_selector = str(tag_skill_rules["catalogIconPathContains"]).casefold()
    skills = _match_actor_values(
        [
            value
            for value in sources.actor_values
            if skill_icon_selector in str(value["iconLogicalPath"]).casefold()
        ],
        skill_names,
    )
    if not special or not skills or not sources.traits:
        raise ValueError(
            "Owned opening character catalogs are incomplete: "
            f"special={len(special)} skills={len(skills)} traits={len(sources.traits)}"
        )

    interaction_rows = []
    for script_editor_id, role in dict(flow["interactionBindings"]).items():
        script = sources.scripts.get(str(script_editor_id).casefold())
        if script is None or role not in role_by_name:
            raise ValueError(
                f"Owned opening interaction binding is unresolved: {script_editor_id} -> {role}"
            )
        interaction_rows.append(
            _interaction_from_script(
                script[1],
                str(script_editor_id),
                str(role),
                str(role_by_name[str(role)]["referenceFormId"]),
                quest_editor_id,
                [int(program["stage"]) for program in programs],
            )
        )
    vigor = role_by_name["vigorTester"]
    vigor_base = sources.needed[int(str(vigor["baseFormId"]), FORM_ID_RADIX)]
    vigor_scripts = [
        link["formId"] for link in vigor_base["links"] if link["signature"] == "SCRI"
    ]
    if len(vigor_scripts) != 1:
        raise ValueError("Owned Vigor tester base has no unique activation script")
    vigor_script_form = int(vigor_scripts[0], FORM_ID_RADIX)
    vigor_script = next(
        (
            (editor_id, row)
            for editor_id, row in sources.scripts.items()
            if row[0] == vigor_script_form
        ),
        None,
    )
    if vigor_script is None:
        raise ValueError("Owned Vigor tester activation script is absent")
    interaction_rows.append(
        _interaction_from_script(
            vigor_script[1][1],
            vigor_script[0],
            "vigorTester",
            str(vigor["referenceFormId"]),
            quest_editor_id,
            [int(program["stage"]) for program in programs],
        )
    )
    interaction_rows.sort(key=lambda value: (int(value["fromStage"]), str(value["event"])))

    dialogue = _compile_dialogue(
        records,
        programs,
        flow,
        sources.scripts,
        quest_editor_id,
    )
    _compile_dialogue_voice(
        dialogue,
        flow,
        roles,
        sources,
        audio_archives,
        master_path,
        cache_root,
    )
    actor_animations = _resolve_actor_animation_commands(
        programs,
        dialogue,
        roles,
        sources.idle_animations_by_editor,
    )
    guide_actor_ai, guide_animation_paths = _compile_guide_actor_ai(
        flow,
        roles,
        sources,
        owned_archives,
        configuration,
        str(quest["formId"]),
    )
    _merge_actor_animation_paths(
        actor_animations,
        str(guide_actor_ai["referenceFormId"]),
        guide_animation_paths,
    )
    flow_commands = _all_flow_commands(programs, dialogue)
    command_contract = _resolve_command_record_identities(flow_commands, records)
    player_animation = _compile_player_animation_graph(
        flow_commands,
        flow,
        sources,
        owned_archives,
        configuration,
    )
    image_space_modifiers = _compile_image_space_modifiers(
        flow_commands,
        sources,
    )
    outro_interactions = [
        interaction
        for interaction in interaction_rows
        if interaction["targetRole"] == "exitDoor"
    ]
    if len(outro_interactions) != 1:
        raise ValueError("Owned opening outro interaction is ambiguous")
    dialogue["outroStartStage"] = outro_interactions[0]["toStage"]
    sex_message = _unique_manifest_record(
        records,
        str(dict(flow["messageEditorIds"])["sex"]),
        "MESG",
    )
    sex_choices = _record_text_values(sex_message, "ITXT")
    sex_choice_indices = {
        int(match[1])
        for match in re.finditer(
            r"nButton\s*==\s*(\d+)",
            quest_script_sources[0],
            re.IGNORECASE,
        )
    }
    if len(sex_choices) != len(sex_choice_indices) or sex_choice_indices != set(
        range(len(sex_choices))
    ):
        raise ValueError(
            f"Owned opening sex message has an unexpected choice count: {len(sex_choices)}"
        )
    appearance, appearance_texture_paths = _compile_player_appearance(
        sources,
        quest_script_sources[0],
    )
    tag_menu_commands = [
        command
        for program in programs
        for command in program["commands"]
        if command["kind"] == "showMenu" and command["role"] == "tagSkills"
    ]
    special_menu_commands = [
        interaction["menu"]
        for interaction in interaction_rows
        if interaction["menu"] is not None and interaction["menu"]["role"] == "special"
    ]
    if len(tag_menu_commands) != 1 or len(special_menu_commands) != 1:
        raise ValueError("Owned opening character menu parameters are ambiguous")
    completion_stages = [
        int(program["stage"])
        for program in programs
        if re.search(
            rf"StopQuest\s+{re.escape(quest_editor_id)}\b",
            str(program["source"]),
            re.IGNORECASE,
        )
    ]
    if len(completion_stages) != 1:
        raise ValueError("Owned opening completion stage is ambiguous")
    icon_paths = tuple(
        sorted(
            {
                str(row["iconLogicalPath"])
                for row in [*special, *skills, *sources.traits]
                if row["iconLogicalPath"] is not None
            }
        )
    )
    return (
        {
            "schema": "opennv-owned-new-game-flow/v7",
            "commandContract": command_contract,
            "quest": {
                "formId": quest["formId"],
                "editorId": quest_editor_id,
                "scriptFormId": quest_script["formId"],
                "scriptEditorId": flow["questScriptEditorId"],
                "objectives": quest["questObjectives"],
                "stages": programs,
                "timerTransitions": timer_transitions,
                "menuCloseTransitions": menu_close_transitions,
                "completionStage": completion_stages[0],
            },
            "sceneRoles": roles,
            "actorAnimations": actor_animations,
            "guideActorAi": guide_actor_ai,
            "playerAnimation": player_animation,
            "imageSpaceModifiers": image_space_modifiers,
            "interactions": interaction_rows,
            "dialogue": dialogue,
            "character": {
                "appearance": appearance,
                "vitals": _compile_gameplay_vitals(sources),
                "sex": {
                    "messageFormId": sex_message["formId"],
                    "title": next(iter(_record_text_values(sex_message, "FULL")), ""),
                    "choices": sex_choices,
                },
                "special": {
                    **{
                        key: value
                        for key, value in special_rules.items()
                        if key != "catalogIconPathContains"
                    },
                    "totalPoints": int(special_menu_commands[0]["totalPoints"]),
                    "docReaction": _special_reaction_manifest(
                        quest_script_sources[0], special
                    ),
                    "values": special,
                },
                "tagSkills": {
                    "psychologyOwnerEditorId": skill_owner,
                    "maximumSelected": int(tag_menu_commands[0]["maximumSelected"]),
                    "values": skills,
                },
                "traits": {
                    "maximumSelected": int(dict(character_rules["traits"])["maximumSelected"]),
                    "values": sources.traits,
                },
            },
        },
        tuple(sorted(set(icon_paths) | set(appearance_texture_paths))),
    )


def _script_sources(record: dict[str, object]) -> Iterator[str]:
    for value in record["text"]:
        if value["signature"] == "SCTX":
            yield str(value["value"])


def _resolve_video_transcoder(policy: dict[str, object]) -> Path:
    kind = str(policy.get("transcoderKind", "ffmpeg"))
    executable_name = str(policy["transcoderExecutable"])
    resolved = shutil.which(executable_name)
    required_sha256 = str(policy.get("transcoderSha256", "")).casefold()
    if resolved is not None:
        path = Path(resolved).resolve()
        if not required_sha256 or file_sha256(path) == required_sha256:
            return path
    if kind != "ffmpeg2theora" or os.name != "nt":
        detail = "" if resolved is None else " with the required SHA-256"
        raise FileNotFoundError(
            f"Configured opening video transcoder is unavailable{detail}: {executable_name}"
        )
    bootstrap = policy.get("windowsBootstrap")
    if not isinstance(bootstrap, dict) or not required_sha256:
        raise ValueError("Pinned Windows opening-video transcoder bootstrap is incomplete")
    local_app_data = os.environ.get("LOCALAPPDATA", "").strip()
    if not local_app_data:
        raise FileNotFoundError("Windows local application-data directory is unavailable")
    relative = Path(str(bootstrap.get("cacheRelativePath", "")))
    if relative.is_absolute() or not relative.parts or ".." in relative.parts:
        raise ValueError("Opening-video transcoder bootstrap path is unsafe")
    target = (Path(local_app_data) / relative).resolve()
    if target.is_file():
        if file_sha256(target) != required_sha256:
            raise RuntimeError(
                f"Pinned opening-video transcoder hash differs at {target}"
            )
        return target
    source_url = str(bootstrap.get("sourceUrl", ""))
    if not source_url.startswith(("http://", "https://")):
        raise ValueError("Opening-video transcoder bootstrap URL is invalid")
    with urllib.request.urlopen(source_url, timeout=60) as response:
        payload = response.read()
    actual_sha256 = hashlib.sha256(payload).hexdigest()
    if actual_sha256 != required_sha256:
        raise RuntimeError(
            "Downloaded opening-video transcoder failed its pinned SHA-256 gate"
        )
    atomic_bytes(target, payload)
    return target


def _validate_runtime_video(
    output: Path,
    policy: dict[str, object],
) -> dict[str, object]:
    validator_name = str(policy.get("validatorExecutable", "ffmpeg"))
    validator = shutil.which(validator_name)
    if validator is None:
        raise FileNotFoundError(
            f"Configured opening video validator is unavailable: {validator_name}"
        )
    command = [
        validator,
        "-nostdin",
        "-v",
        "error",
        "-i",
        str(output),
        "-f",
        "null",
        "-",
    ]
    result = subprocess.run(command, check=False, capture_output=True, text=True)
    errors = result.stderr.strip()
    if result.returncode != 0 or errors:
        raise RuntimeError(
            "Opening video decode validation failed: "
            + (errors or f"exit={result.returncode}")
        )
    validator_path = Path(validator).resolve()
    return {
        "status": "decoded-without-errors",
        "validator": str(validator_path),
        "validatorSha256": file_sha256(validator_path),
        "arguments": command[1:],
    }


def _prepare_runtime_video(
    source: Path,
    cache_root: Path,
    configuration: RuntimeConfiguration,
    policy_override: dict[str, object] | None = None,
) -> dict[str, object]:
    legal_assets = configuration.document["legalAssets"]
    if not isinstance(legal_assets, dict):
        raise ValueError("OpenNV legal-assets configuration is invalid")
    policy = legal_assets["videoImport"] if policy_override is None else policy_override
    if not isinstance(policy, dict):
        raise ValueError("OpenNV opening video-import configuration is invalid")
    executable = _resolve_video_transcoder(policy)
    source_sha256 = file_sha256(source)
    identity = hashlib.sha256(
        (
            source_sha256
            + ":"
            + json.dumps(policy, sort_keys=True, separators=(",", ":"))
        ).encode("utf-8")
    ).hexdigest()[: configuration.content_compiler.asset_id_hex_characters]
    extension = str(policy["outputExtension"])
    if not extension.startswith("."):
        raise ValueError("Opening video output extension must begin with a period")
    output = cache_root / "generated" / "opening" / "video" / f"{identity}{extension}"
    sidecar = output.with_suffix(output.suffix + ".json")
    expected = {
        "source": str(source.resolve()),
        "sourceSha256": source_sha256,
        "policy": policy,
    }
    if output.is_file() and sidecar.is_file():
        existing = json.loads(sidecar.read_text(encoding="utf-8"))
        existing_inputs = existing.get("inputs")
        if (
            isinstance(existing_inputs, dict)
            and all(existing_inputs.get(key) == value for key, value in expected.items())
            and existing.get("outputSha256") == file_sha256(output)
            and isinstance(existing.get("validation"), dict)
            and existing["validation"].get("status") == "decoded-without-errors"
        ):
            if existing_inputs != expected:
                existing["inputs"] = expected
                atomic_json(sidecar, existing)
            return existing

    transcoder_kind = str(policy.get("transcoderKind", "ffmpeg"))
    version_argument = "-h" if transcoder_kind == "ffmpeg2theora" else "-version"
    version_result = subprocess.run(
        [str(executable), version_argument],
        check=False,
        capture_output=True,
        text=True,
    )
    if version_result.returncode != 0 or not version_result.stdout.strip():
        raise RuntimeError("Opening video transcoder version probe failed")
    temporary = output.with_name(output.stem + ".tmp" + output.suffix)
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary.unlink(missing_ok=True)
    if transcoder_kind == "ffmpeg2theora":
        command = [str(executable)]
        if bool(policy.get("disableSkeleton", False)):
            command.append("--no-skeleton")
        if bool(policy.get("stripMetadata", False)):
            command.append("--nometadata")
        command.extend(
            [
                "-v",
                str(policy["videoQuality"]),
                "-a",
                str(policy["audioQuality"]),
                "-o",
                str(temporary),
                str(source),
            ]
        )
    elif transcoder_kind == "ffmpeg":
        command = [
            str(executable),
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            str(policy["logLevel"]),
            "-y",
            "-fflags",
            "+bitexact",
            "-i",
            str(source),
            "-map",
            "0:v:0",
            "-map",
            "0:a?",
            "-map_metadata",
            "-1",
            "-c:v",
            str(policy["videoCodec"]),
            "-q:v",
            str(policy["videoQuality"]),
            "-pix_fmt",
            str(policy["pixelFormat"]),
            "-c:a",
            str(policy["audioCodec"]),
            "-q:a",
            str(policy["audioQuality"]),
            "-threads",
            str(policy["threads"]),
            "-flags:v",
            "+bitexact",
            "-flags:a",
            "+bitexact",
            "-f",
            str(policy["containerFormat"]),
            str(temporary),
        ]
    else:
        raise ValueError(f"Unsupported opening video transcoder kind: {transcoder_kind}")
    result = subprocess.run(command, check=False, capture_output=True, text=True)
    if result.returncode != 0 or not temporary.is_file() or not temporary.stat().st_size:
        temporary.unlink(missing_ok=True)
        raise RuntimeError(
            "Opening video transcoding failed: " + result.stderr.strip()
        )
    try:
        validation = _validate_runtime_video(temporary, policy)
    except Exception:
        temporary.unlink(missing_ok=True)
        raise
    os.replace(temporary, output)
    document = {
        "schema": "opennv-owned-opening-video/v1",
        "status": "deterministic-owned-video-transcode",
        "inputs": expected,
        "output": str(output.resolve()),
        "outputBytes": output.stat().st_size,
        "outputSha256": file_sha256(output),
        "validation": validation,
        "transcoder": {
            "path": str(executable),
            "sha256": file_sha256(executable),
            "version": version_result.stdout.splitlines()[0],
            "arguments": command[1:],
        },
    }
    atomic_json(sidecar, document)
    return document


def video_manifest(
    data_root: Path,
    records: list[dict[str, object]],
    video_directory_name: str,
    entry_point: dict[str, object],
    cache_root: Path,
    configuration: RuntimeConfiguration,
    policy_override: dict[str, object] | None = None,
) -> list[dict[str, object]]:
    entry_editor_id = str(entry_point["questEditorId"])
    entry_stage = int(entry_point["stage"])
    entry_records = [
        record
        for record in records
        if any(
            value["signature"] == "EDID"
            and value["value"] == entry_editor_id
            for value in record["text"]
        )
    ]
    if len(entry_records) != 1:
        raise ValueError("Owned opening entry quest does not resolve uniquely")
    entry_sources = [
        str(value["source"])
        for value in entry_records[0]["questStageScripts"]
        if int(value["stage"]) == entry_stage
    ]
    if len(entry_sources) != 1:
        raise ValueError("Owned opening entry quest stage does not resolve uniquely")
    required = {
        match.group("path").casefold()
        for source in entry_sources
        for match in PLAY_BINK_PATTERN.finditer(
            "\n".join(line.split(";", 1)[0] for line in source.splitlines())
        )
    }
    requested = sorted(
        {
            match.group("path")
            for record in records
            for source in _script_sources(record)
            for match in PLAY_BINK_PATTERN.finditer(
                "\n".join(line.split(";", 1)[0] for line in source.splitlines())
            )
        },
        key=str.casefold,
    )
    video_root = data_root / video_directory_name
    available = {
        path.name.casefold(): path
        for path in video_root.iterdir()
        if path.is_file()
    }
    rows = []
    for logical_path in requested:
        source = available.get(Path(logical_path).name.casefold())
        required_at_entry = logical_path.casefold() in required
        runtime = (
            None
            if source is None or not required_at_entry
            else _prepare_runtime_video(
                source,
                cache_root,
                configuration,
                policy_override,
            )
        )
        rows.append(
            {
                "logicalPath": logical_path,
                "requiredAtEntry": required_at_entry,
                "source": None if source is None else str(source.resolve()),
                "bytes": None if source is None else source.stat().st_size,
                "sha256": None if source is None else file_sha256(source),
                "runtime": runtime,
                "status": (
                    "missing-owned-entry-video"
                    if source is None and required_at_entry
                    else "authored-nonentry-video-not-installed"
                    if source is None
                    else "owned-loose-video"
                ),
            }
        )
    return rows


def prepare_opening_manifest(
    data_root: Path,
    master_path: Path,
    ui_archive_path: Path,
    owned_archives: OwnedArchiveStack,
    audio_archives: OwnedArchiveStack,
    cache_root: Path,
    recipe_path: Path,
    configuration: RuntimeConfiguration,
    video_directory_name: str,
    master_sha256: str,
    default_ini_path: Path,
    preferences_ini_path: Path | None = None,
) -> dict[str, object]:
    recipe = load_opening_recipe(recipe_path)
    graph = dict(recipe["recordGraph"])
    universal_link_signatures = frozenset(
        str(value) for value in graph["universalFormLinkSubrecords"]
    )
    record_link_signatures = {
        str(record_type): frozenset(str(value) for value in signatures)
        for record_type, signatures in dict(
            graph["formLinkSubrecordsByRecordType"]
        ).items()
    }
    reverse_signatures = frozenset(
        str(value) for value in graph["reverseFormLinkSubrecords"]
    )
    by_form, by_editor, group_children, reverse_links = index_records(
        master_path,
        universal_link_signatures,
        record_link_signatures,
    )
    selected, blockers = record_graph_closure(
        (str(value) for value in recipe["rootEditorIds"]),
        by_form,
        by_editor,
        group_children,
        reverse_links,
        reverse_signatures,
        frozenset(
            int(str(value), FORM_ID_RADIX)
            for value in dict(graph["engineForms"])
        ),
        frozenset(int(value) for value in graph["parentGroupTypes"]),
        {
            str(record_type): frozenset(int(value) for value in group_types)
            for record_type, group_types in dict(
                graph["childGroupTypesByRecordType"]
            ).items()
        },
    )
    missing_roots = [
        root
        for root in recipe["rootEditorIds"]
        if len(by_editor.get(str(root).casefold(), ())) != 1
    ]
    if missing_roots:
        raise ValueError(
            "Owned opening graph roots are incomplete: "
            + ", ".join(str(value) for value in missing_roots)
        )
    records = selected_record_manifest(master_path, selected)
    flow_definition = dict(recipe["newGameFlow"])
    new_game_flow, flow_texture_paths = compile_new_game_flow(
        master_path,
        records,
        flow_definition,
        owned_archives,
        audio_archives,
        cache_root,
        configuration,
    )
    ui = compile_ui(
        data_root,
        default_ini_path,
        preferences_ini_path,
        ui_archive_path,
        owned_archives,
        cache_root,
        dict(recipe["ui"]),
        flow_definition,
        flow_texture_paths,
        configuration,
    )
    videos = video_manifest(
        data_root,
        records,
        video_directory_name,
        dict(recipe["entryPoint"]),
        cache_root,
        configuration,
        dict(recipe["videoImport"]),
    )
    manifest = {
        "schema": OPENING_MANIFEST_SCHEMA,
        "status": OPENING_MANIFEST_STATUS,
        "compiler": compiler_provenance("opening"),
        "campaign": recipe["campaign"],
        "recipe": {
            "id": recipe["id"],
            "sha256": file_sha256(recipe_path),
        },
        "configuration": configuration.manifest(),
        "master": {
            "file": master_path.name,
            "bytes": master_path.stat().st_size,
            "sha256": master_sha256,
        },
        "roots": list(recipe["rootEditorIds"]),
        "entryPoint": recipe["entryPoint"],
        "recordGraph": {
            "recordCount": len(records),
            "records": records,
            "engineForms": graph["engineForms"],
            "algorithm": "indexed-root-script-dialogue-and-parent-group-selection",
            "complexity": "O(records+form-links+selected-group-children)",
        },
        "newGameFlow": new_game_flow,
        "ui": ui,
        "videos": videos,
        "blockers": [
            *({"reason": blocker} for blocker in blockers),
            *ui["unresolvedIncludes"],
            *ui["unresolvedAssets"],
            *(
                {"path": video["logicalPath"], "reason": video["status"]}
                for video in videos
                if video["source"] is None and video["requiredAtEntry"]
            ),
        ],
    }
    output = cache_root / "generated" / "opening" / "opening-manifest.json"
    atomic_json(output, manifest)
    return {"output": str(output.resolve()), "manifest": manifest}
