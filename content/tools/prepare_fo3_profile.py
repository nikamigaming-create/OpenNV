#!/usr/bin/env python3
"""Register a legal Fallout 3 installation as a hash-bound local profile."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import struct
import sys
from io import BytesIO
from pathlib import Path

from actor_catalog import scan_actor_catalog
from actor_gltf import (
    animation_sequence_manifest,
    sample_root_motion,
    sample_transform_animation,
)
from bsa_archive import BsaArchive, canonical_member_path
from cell_catalog import cell_parent_form_id, parse_navmesh
from cell_scene import godot_rotation_quaternion, navmesh_manifest
from environment_catalog import parse_image_space_modifier
from facegen import compose_facegen_coordinates
from opening_catalog import (
    _compile_gamebryo_font,
    _compile_facegen_control_space,
    _display_entity,
    _ini_index,
    dialogue_menu_tile_contract,
    _prepare_runtime_video,
    _race_sex_menu_tile_contract,
    _text_edit_menu_tile_contract,
    _document_index,
    parse_tile_document,
)
from owned_archive_stack import OwnedArchive, OwnedArchiveStack
from player_facegen_preview import prepare_default_player_facegen_preview
import prepare_actor as actor_preparation
from plugin_records import iter_plugin_records, iter_subrecords, zstring
from plugin_stack import build_plugin_stack, file_sha256, find_case_insensitive_file
from prepare_fo3_opening_slice import (
    compile_opening_slice,
    default_recipe_path as default_opening_slice_recipe_path,
)
from runtime_configuration import load_runtime_configuration
from texture_pipeline import OwnedTexturePipeline, decode_dds
from ttw_effective_source import load_ttw_effective_record_source
from ttw_fo3_opening import DEFAULT_RECIPE as DEFAULT_TTW_FO3_OPENING_RECIPE
from ttw_profile import DEFAULT_REQUIREMENTS_PATH as DEFAULT_TTW_PROFILE_RECIPE


RECIPE_SCHEMA = "opennv-fo3-owned-profile-recipe/v1"
PROFILE_SCHEMA = "opennv-owned-game-profile/v1"
PROFILE_STATUS = "registered-owned-profile"
PROFILE_ID_HEX_CHARACTERS = 20
FORM_ID_HEX_CHARACTERS = 8
FORM_ID_BYTES = 4
FORM_ID_RADIX = 16
CG02_DIALOGUE_INFO_DATA_BYTES = 4
CG02_DIALOGUE_INFO_RESPONSE_TYPE = 1
CG02_DIALOGUE_INFO_SAY_ONCE_FLAG = 4
CG02_INTRO_SAY_TO_COUNT = 6
CG02_INTRO_ACTOR_RECIPE_COUNT = 7
CG02_INTRO_SOURCE_STAGE = 5
CG02_INTRO_TARGET_STAGE = 6
CG02_INTRO_FINAL_TIMER_PHASE = 5
CG02_STAGE10_STOP_LOOK_COUNT = 8
CG02_STAGE10_PREFIX_COMMAND_COUNT = 3
FALLOUT_CAMERA_REFERENCE_ASPECT_HEIGHT_OVER_WIDTH = 0.75
PNG_MAXIMUM_COMPRESSION_LEVEL = 9
STAGE_INDEX_BYTES = frozenset({2, 4})
QUEST_RECORD = "QUST"
SCRIPT_RECORD = "SCPT"
MESSAGE_RECORD = "MESG"
PLUGIN_HEADER_RECORD = "TES4"
PACKAGE_RECORD = "PACK"
IDLE_RECORD = "IDLE"
GLOBAL_RECORD = "GLOB"
ACTOR_REFERENCE_RECORD = "ACHR"
CREATURE_REFERENCE_RECORD = "ACRE"
PLACED_REFERENCE_RECORD = "REFR"
ACTIVATOR_RECORD = "ACTI"
DOOR_RECORD = "DOOR"
ACTOR_VALUE_RECORD = "AVIF"
NPC_RECORD = "NPC_"
ACTOR_BASE_RECORD = "NPC_"
STATIC_RECORD = "STAT"
DIALOGUE_TOPIC_RECORD = "DIAL"
DIALOGUE_INFO_RECORD = "INFO"
VOICE_TYPE_RECORD = "VTYP"
IMAGE_SPACE_MODIFIER_RECORD = "IMAD"
SOUND_RECORD = "SOUN"
PLAY_BINK_PATTERN = re.compile(r'\bplayBink\s+"(?P<path>[^"]+\.bik)"', re.IGNORECASE)
SEX_CHANGE_PATTERN = re.compile(
    r"\b(?:if|elseif)\s+button\s*==\s*(?P<index>\d+)\s+"
    r"player\.sexChange\s+(?P<sex>male|female)\s+1\b",
    re.IGNORECASE | re.DOTALL,
)
ADD_SCRIPT_PACKAGE_PATTERN = re.compile(
    r"\bplayer\.addScriptPackage\s+(?P<package>[A-Za-z_][A-Za-z0-9_]*)\b",
    re.IGNORECASE,
)
CG00_NEXT_STAGE_PATTERN = re.compile(
    r"\bif\s+getStage\s+CG00\s*>=\s*(?P<source>\d+)\s*&&\s*"
    r"GetStageDone\s+CG00\s+(?P<target>\d+)\s*==\s*0\b"
    r"(?P<body>.*?)\bendif\b",
    re.IGNORECASE | re.DOTALL,
)
SET_CG00_STAGE_PATTERN = re.compile(r"\bsetstage\s+CG00\s+(?P<stage>\d+)\b", re.IGNORECASE)
MATCH_RACE_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.MatchRace\s+player$",
    re.IGNORECASE,
)
MATCH_FACE_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.MatchFaceGeometry\s+"
    r"player\s+(?P<template>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
PACKAGE_DATA_BYTES = 12
PACKAGE_LOCATION_BYTES = 12
PACKAGE_IDLE_FLAG_BYTES = frozenset({1, 4})
PACKAGE_IDLE_COUNT_BYTES = frozenset({1, 4})
PACKAGE_IDLE_TIMER_BYTES = 4
PACKAGE_EVENT_NAMES = {"POBA": "begin", "POEA": "end", "POCA": "change"}
SET_STAGE_PATTERN = re.compile(
    r"^setstage\s+(?P<quest>[A-Za-z_][A-Za-z0-9_]*)\s+(?P<stage>\d+)$",
    re.IGNORECASE,
)
IMAGE_SPACE_MODIFIER_PATTERN = re.compile(
    r"^imod\s+(?P<modifier>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
PLAY_SOUND_PATTERN = re.compile(
    r"^playSound\s+(?P<sound>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
REMOVE_SCRIPT_PACKAGE_PATTERN = re.compile(
    r"^(?P<subject>player)\.removeScriptPackage$",
    re.IGNORECASE,
)
REMOVE_IMAGE_SPACE_MODIFIER_PATTERN = re.compile(
    r"^rimod\s+(?P<modifier>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
DISABLE_REFERENCE_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.disable$",
    re.IGNORECASE,
)
STOP_QUEST_PATTERN = re.compile(
    r"^stopQuest\s+(?P<quest>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
SET_PC_YOUNG_PATTERN = re.compile(r"^SetPCYoung\s+(?P<value>\d+)$", re.IGNORECASE)
MOVE_TO_REFERENCE_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.moveto\s+"
    r"(?P<target>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
SET_PLAYER_SCALE_PATTERN = re.compile(
    r"^player\.setscale\s+(?P<value>(?:\d+(?:\.\d*)?|\.\d+))$",
    re.IGNORECASE,
)
SET_LOCATION_LOAD_SCREENS_PATTERN = re.compile(
    r"^SetLocationSpecificLoadScreensOnly\s+(?P<value>\d+)$",
    re.IGNORECASE,
)
SET_IN_CHAR_GEN_PATTERN = re.compile(
    r"^SetInCharGen\s+(?P<value>\d+)$",
    re.IGNORECASE,
)
PLAYER_CONTROLS_PATTERN = re.compile(
    r"^(?P<command>EnablePlayerControls|DisablePlayerControls)\s+"
    r"(?P<arguments>\d+(?:\s+\d+)*)$",
    re.IGNORECASE,
)
AUTO_DISPLAY_OBJECTIVES_PATTERN = re.compile(
    r"^AutoDisplayObjectives\s+(?P<value>\d+)$",
    re.IGNORECASE,
)
COMPLETE_ALL_OBJECTIVES_PATTERN = re.compile(
    r"^completeAllObjectives\s+(?P<quest>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
KILL_QUEST_UPDATES_PATTERN = re.compile(r"^KillQuestUpdates$", re.IGNORECASE)
CLEAR_NO_ACTIVATION_SOUND_PATTERN = re.compile(
    r"^ClearNoActivationSound$", re.IGNORECASE
)
SET_OBJECTIVE_DISPLAYED_PATTERN = re.compile(
    r"^setObjectiveDisplayed\s+(?P<quest>[A-Za-z_][A-Za-z0-9_]*)\s+"
    r"(?P<index>\d+)\s+(?P<value>\d+)$",
    re.IGNORECASE,
)
AUTOSAVE_PATTERN = re.compile(r"^autosave$", re.IGNORECASE)
SET_OBJECTIVE_COMPLETED_PATTERN = re.compile(
    r"^setObjectiveCompleted\s+(?P<quest>[A-Za-z_][A-Za-z0-9_]*)\s+"
    r"(?P<index>\d+)\s+(?P<value>\d+)$",
    re.IGNORECASE,
)
SPECIAL_BOOK_MENU_PATTERN = re.compile(r"^ssbmp\s+(?P<points>\d+)$", re.IGNORECASE)
GAMEBRYO_SPECIAL_MINIMUM_VALUE = 1
GAMEBRYO_SPECIAL_MAXIMUM_VALUE = 10
GAMEBRYO_SPECIAL_EDITOR_IDS = (
    "AVStrength",
    "AVPerception",
    "AVEndurance",
    "AVCharisma",
    "AVIntelligence",
    "AVAgility",
    "AVLuck",
)
SET_NO_ACTIVATION_SOUND_PATTERN = re.compile(
    r"^SetNoActivationSound\s+(?P<sound>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
SET_PC_TODDLER_PATTERN = re.compile(r"^SetPCToddler\s+(?P<value>\d+)$", re.IGNORECASE)
SET_GAME_TIME_PATTERN = re.compile(
    r"^set\s+(?P<variable>gameyear|gamemonth|gameday|gamehour)\s+to\s+"
    r"(?P<value>-?\d+(?:\.\d+)?)$",
    re.IGNORECASE,
)
AGE_RACE_PATTERN = re.compile(
    r"^(?P<subject>player)\.AgeRace\s+(?P<value>-?\d+)$", re.IGNORECASE
)
REMOVE_ALL_ITEMS_PATTERN = re.compile(
    r"^(?P<subject>player)\.removeallitems$", re.IGNORECASE
)
ADD_ITEM_PATTERN = re.compile(
    r"^(?P<subject>player)\.additem\s+(?P<item>[A-Za-z_][A-Za-z0-9_]*)\s+"
    r"(?P<count>\d+)\s+(?P<silent>\d+)$",
    re.IGNORECASE,
)
EQUIP_ITEM_PATTERN = re.compile(
    r"^(?P<subject>player)\.equipitem\s+(?P<item>[A-Za-z_][A-Za-z0-9_]*)\s+"
    r"(?P<prevent>\d+)\s+(?P<silent>\d+)$",
    re.IGNORECASE,
)
LOOK_AT_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.look\s+(?P<target>player)$",
    re.IGNORECASE,
)
IGNORE_CRIME_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.IgnoreCrime\s+(?P<value>\d+)$",
    re.IGNORECASE,
)
PLAY_BINK_COMMAND_PATTERN = re.compile(
    r'^playBink\s+"(?P<path>[^"]+\.bik)"\s+'
    r"(?P<arguments>\d+(?:\s+\d+){3})$",
    re.IGNORECASE,
)
REFERENCE_TRANSFORM_FLOATS = 6
REFERENCE_TRANSFORM_BYTES = REFERENCE_TRANSFORM_FLOATS * 4
REFERENCE_SCALE_BYTES = 4
DEFAULT_REFERENCE_SCALE = 1.0
TRIGGER_PRIMITIVE_FLOATS = 7
TRIGGER_PRIMITIVE_BYTES = TRIGGER_PRIMITIVE_FLOATS * 4 + 4
TRIGGER_PRIMITIVE_BOX_TYPE = 2
TRIGGER_PRIMITIVE_ORIENTED_BOX_TYPE = 1
TRIGGER_PRIMITIVE_COLOR_START = 3
TRIGGER_PRIMITIVE_TYPE_INDEX = TRIGGER_PRIMITIVE_FLOATS
CG01_WALK_OBJECTIVE_INDEX = 10
CG02_INTERCOM_STAGE_COMMAND_COUNT = 6
CG01_WALK_TARGET_STAGE = 12
CG01_DAD_COMPLETION_CONDITIONAL_SOURCE_STAGE = 75
CG01_DAD_COMPLETION_CONDITIONAL_TARGET_STAGE = 80
CG01_POST_STAGE16_COMMAND_COUNT = 2
CG01_POST_STAGE18_COMMAND_COUNT = 5
CG01_POST_STAGE20_COMMAND_COUNT = 3
PERSPECTIVE_MAXIMUM_DEGREES = 180.0
CG00_TIMER_CHAIN_PATTERN = re.compile(
    r"\bif\s+runTimer\s*==\s*1\b.*?"
    r"\bif\s+timer\s*>\s*0\b\s*"
    r"set\s+timer\s+to\s+timer\s*-\s*GetSecondsPassed\b.*?"
    r"(?P<stage_branches>.*?)"
    r"\bendif\b\s*\bendif\b\s*\bif\s+chooseSex\b",
    re.IGNORECASE | re.DOTALL,
)
CG00_TIMER_STAGE_PATTERN = re.compile(
    r"\b(?:if|elseif)\s+getstage\s+CG00\s*==\s*(?P<source>\d+)\b.*?"
    r"setstage\s+CG00\s+(?P<target>\d+)\b",
    re.IGNORECASE | re.DOTALL,
)
SET_REFERENCE_VARIABLE_PATTERN = re.compile(
    r"^set\s+(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\."
    r"(?P<variable>[A-Za-z_][A-Za-z0-9_]*)\s+to\s+(?P<value>-?\d+(?:\.\d+)?)$",
    re.IGNORECASE,
)
CG01_PLAYER_TRIGGER_BLOCK_PATTERN = re.compile(
    r"\bbegin\s+onTriggerEnter\s+player\b(?P<body>.*?)\bend\b",
    re.IGNORECASE | re.DOTALL,
)
GET_STAGE_DONE_PATTERN = re.compile(
    r"\bgetStageDone\s+(?P<quest>[A-Za-z_][A-Za-z0-9_]*)\s+"
    r"(?P<stage>\d+)\s*==\s*0\b",
    re.IGNORECASE,
)
IS_ACTION_REF_PLAYER_PATTERN = re.compile(
    r"\bIsActionRef\s+player\s*==\s*1\b",
    re.IGNORECASE,
)
REFERENCE_COMMAND_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\."
    r"(?P<command>evp|enable)$",
    re.IGNORECASE,
)
SET_OPEN_STATE_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.setOpenState\s+(?P<value>\d+)$",
    re.IGNORECASE,
)
LOCK_REFERENCE_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.Lock\s+(?P<value>\d+)$",
    re.IGNORECASE,
)
UNLOCK_REFERENCE_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\.Unlock$", re.IGNORECASE
)
CONDITION_BYTES = 28
CONDITION_FUNCTION_OFFSET = 8
CONDITION_PARAMETER_1_OFFSET = 12
CONDITION_PARAMETER_2_OFFSET = 16
CONDITION_RUN_ON_OFFSET = 20
CONDITION_REFERENCE_OFFSET = 24
GET_IS_SEX_FUNCTION = 70
GET_STAGE_FUNCTION = 58
GET_STAGE_DONE_FUNCTION = 59
CONDITION_EQUAL_OPERATOR_FLAGS = 0x60
GET_IS_VOICE_TYPE_FUNCTION = 427
GET_PC_IS_SEX_FUNCTION = 131
GET_IS_ID_FUNCTION = 72
GET_ITEM_COUNT_FUNCTION = 47
GET_QUEST_VARIABLE_FUNCTION = 79
GET_IS_CURRENT_PACKAGE_FUNCTION = 161
DIALOGUE_CHILD_GROUP_TYPE = 7
DIALOGUE_RESPONSE_METADATA_BYTES = frozenset((16, 20, 24))
DIALOGUE_RESPONSE_NUMBER_OFFSET = 12
CREATURE_DATA_BYTES = 17
CREATURE_HEALTH_OFFSET = 4
WEAPON_DATA_BYTES = 15
WEAPON_DAMAGE_OFFSET = 12
WEAPON_CLIP_SIZE_OFFSET = 14
DIALOGUE_INFO_DATA_BYTES = 4
DIALOGUE_INFO_SAY_ONCE_FLAG = 0x0004
INITIALLY_DISABLED_RECORD_FLAG = 0x00000800
RACE_DATA_BYTES = 36

FO3_VIDEO_IMPORT_POLICY = {
    "transcoderKind": "ffmpeg2theora",
    "transcoderExecutable": "ffmpeg2theora",
    "transcoderSha256": "a1e0f97bde8b1b8874480a2f153651258e0f35b86d1d24a8a911bd4a841b8308",
    "outputExtension": ".ogv",
    "videoQuality": 7,
    "audioQuality": 5,
    "disableSkeleton": True,
    "stripMetadata": True,
    "validatorExecutable": "ffmpeg",
    "windowsBootstrap": {
        "sourceUrl": "http://v2v.cc/~j/ffmpeg2theora/ffmpeg2theora-0.29.exe",
        "cacheRelativePath": (
            "OpenNV/tools/ffmpeg2theora/0.29-a1e0f97b/ffmpeg2theora.exe"
        ),
    },
}
RACE_FLAGS_OFFSET = 32
RACE_PLAYABLE_FLAG = 0x01
HAIR_PLAYABLE_FLAG = 0x01
HAIR_FEMALE_FLAG = 0x02
HAIR_MALE_FLAG = 0x04
FACEGEN_SYMMETRIC_GEOMETRY_FLOATS = 50
FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS = 30
FACEGEN_SYMMETRIC_TEXTURE_FLOATS = 50
TTW_INPUT_ENUMERATION_SCHEMA = "opennv-ttw-fo3-profile-input-enumeration/v1"
TTW_INPUT_SIGNATURES = frozenset(
    {
        "ACHR",
        "CELL",
        "DIAL",
        "IDLE",
        "IMAD",
        "INFO",
        "NPC_",
        "PACK",
        "QUST",
        "REFR",
        "SCPT",
        "SOUN",
        "VTYP",
    }
)
TTW_INPUT_FORM_NAMES = ("vault101d", "cg00Quest", "cg00Script")


def default_recipe_path() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    candidates = []
    for path in (root / "recipes").glob("*.json"):
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if document.get("schema") == RECIPE_SCHEMA and document.get("campaign") == "Fallout3":
            candidates.append(path)
    if len(candidates) != 1:
        raise ValueError(
            "Expected exactly one Fallout 3 owned-profile recipe, "
            f"found {len(candidates)}"
        )
    return candidates[0]


def enumerate_ttw_fo3_profile_inputs(
    profile_path: Path,
    source_namespace_path: Path,
    opening_recipe_path: Path = DEFAULT_TTW_FO3_OPENING_RECIPE,
    source_recipe_path: Path = DEFAULT_TTW_PROFILE_RECIPE,
) -> dict[str, object]:
    """Enumerate the TTW Vault101d/CG00 input boundary without profile output."""

    resolved_opening_recipe = opening_recipe_path.resolve()
    opening_recipe = json.loads(resolved_opening_recipe.read_text(encoding="utf-8"))
    if (
        opening_recipe.get("schema") != "opennv-ttw-fo3-opening-recipe/v1"
        or opening_recipe.get("id") != resolved_opening_recipe.stem
    ):
        raise ValueError(
            f"Unexpected TTW Fallout 3 opening recipe: {resolved_opening_recipe}"
        )
    forms = opening_recipe.get("forms")
    if not isinstance(forms, dict):
        raise ValueError("TTW Fallout 3 opening recipe has no form inventory")
    missing = [name for name in TTW_INPUT_FORM_NAMES if name not in forms]
    if missing:
        raise ValueError(
            "TTW Fallout 3 profile input forms are absent: " + ", ".join(missing)
        )

    source = load_ttw_effective_record_source(
        profile_path,
        source_namespace_path,
        TTW_INPUT_SIGNATURES,
        source_recipe_path,
    )
    records = {
        name: source.records.contract(dict(forms[name]))
        for name in TTW_INPUT_FORM_NAMES
    }
    raw_closure = opening_recipe.get("cg00SceneClosure")
    if not isinstance(raw_closure, dict):
        raise ValueError("TTW Fallout 3 opening recipe has no CG00 scene closure")
    closure = dict(raw_closure)
    vault_form_key = str(records["vault101d"]["formKey"])
    if str(closure.get("cellFormKey", "")).casefold() != vault_form_key.casefold():
        raise ValueError("TTW CG00 scene CELL differs from the profile input CELL")

    def record_contract(raw_definition: object) -> dict[str, object]:
        if not isinstance(raw_definition, dict):
            raise ValueError("TTW CG00 scene record definition is invalid")
        return source.records.contract(dict(raw_definition))

    def joined_form_key(contract: dict[str, object], signature: str) -> str:
        version = source.records.winner(str(contract["formKey"]))
        payload = _single_subrecord(version.record, signature)
        if len(payload) != FORM_ID_BYTES:
            raise ValueError(
                f"TTW CG00 {contract['recordType']} {contract['formKey']} "
                f"has a malformed {signature} link"
            )
        return version.context.form_key(struct.unpack("<I", payload)[0]).text

    player = record_contract(closure.get("player"))
    raw_participants = closure.get("participants")
    if not isinstance(raw_participants, list):
        raise ValueError("TTW CG00 scene participants are absent")
    participants = []
    placed_references = []
    for raw_participant in raw_participants:
        if not isinstance(raw_participant, dict):
            raise ValueError("TTW CG00 scene participant is invalid")
        participant = dict(raw_participant)
        reference = record_contract(participant.get("reference"))
        base = record_contract(participant.get("base"))
        start_marker = record_contract(participant.get("startMarker"))
        if joined_form_key(reference, "NAME").casefold() != str(
            base["formKey"]
        ).casefold():
            raise ValueError("TTW CG00 participant ACHR-to-NPC join differs")
        participants.append(
            {
                "role": str(participant.get("role", "")),
                "reference": reference,
                "base": base,
                "startMarker": start_marker,
            }
        )
        placed_references.extend((reference, start_marker))
    if {row["role"] for row in participants} != {"father", "doctor", "mother"}:
        raise ValueError("TTW CG00 scene participant roles differ")

    raw_placed = closure.get("placedReferences")
    if not isinstance(raw_placed, dict):
        raise ValueError("TTW CG00 placed-reference closure is absent")
    explicit_placed = {
        str(name): record_contract(definition)
        for name, definition in raw_placed.items()
    }
    if set(explicit_placed) != {"playerStartMarker", "geneProjector"}:
        raise ValueError("TTW CG00 explicit placed-reference roles differ")
    placed_references.extend(explicit_placed.values())
    if any(
        str(row.get("parentCellFormKey", "")).casefold()
        != vault_form_key.casefold()
        for row in placed_references
    ):
        raise ValueError("TTW CG00 placed reference is outside Vault101d")

    raw_package_sections = closure.get("packageSections")
    if not isinstance(raw_package_sections, dict):
        raise ValueError("TTW CG00 package sections are absent")
    package_sections: dict[str, list[dict[str, object]]] = {}
    for role, raw_rows in raw_package_sections.items():
        if not isinstance(raw_rows, list):
            raise ValueError("TTW CG00 package-section rows are invalid")
        rows = []
        for raw_row in raw_rows:
            if not isinstance(raw_row, dict):
                raise ValueError("TTW CG00 package-section row is invalid")
            row = dict(raw_row)
            package = record_contract(row.get("package"))
            idle = record_contract(row.get("idle"))
            package_version = source.records.winner(str(package["formKey"]))
            idle_form_keys = {
                package_version.context.form_key(raw_form_id).text.casefold()
                for raw_form_id in _form_id_list(package_version.record, "IDLA")
            }
            if str(idle["formKey"]).casefold() not in idle_form_keys:
                raise ValueError("TTW CG00 PACK-to-IDLE join differs")
            rows.append(
                {
                    "section": int(row["section"]),
                    "package": package,
                    "idle": idle,
                }
            )
        if [row["section"] for row in rows] != list(range(len(rows))):
            raise ValueError("TTW CG00 package sections are not contiguous")
        package_sections[str(role)] = rows
    if set(package_sections) != {"player", "father", "doctor", "mother"}:
        raise ValueError("TTW CG00 package-section roles differ")

    raw_dialogue = closure.get("dialogue")
    if not isinstance(raw_dialogue, dict):
        raise ValueError("TTW CG00 dialogue closure is absent")
    dialogue_definition = dict(raw_dialogue)
    quest_form_key = str(dialogue_definition.get("questFormKey", ""))
    if quest_form_key.casefold() != str(records["cg00Quest"]["formKey"]).casefold():
        raise ValueError("TTW CG00 dialogue quest identity differs")
    raw_topics = dialogue_definition.get("topics")
    raw_voice_types = dialogue_definition.get("voiceTypes")
    if not isinstance(raw_topics, dict) or not isinstance(raw_voice_types, dict):
        raise ValueError("TTW CG00 dialogue owner definitions are absent")
    topics = {str(role): record_contract(value) for role, value in raw_topics.items()}
    voice_types = {
        str(role): record_contract(value) for role, value in raw_voice_types.items()
    }
    if set(topics) != {"father", "mother"} or set(voice_types) != set(topics):
        raise ValueError("TTW CG00 dialogue owner roles differ")
    for topic in topics.values():
        if joined_form_key(topic, "QSTI").casefold() != quest_form_key.casefold():
            raise ValueError("TTW CG00 DIAL-to-QUST join differs")

    topic_roles = {
        str(contract["formKey"]).casefold(): role for role, contract in topics.items()
    }

    def compile_info(raw_form_key: object) -> dict[str, object]:
        definition = {"formKey": str(raw_form_key), "recordType": "INFO"}
        info = record_contract(definition)
        version = source.records.winner(str(info["formKey"]))
        topic_keys = {
            version.context.form_key(group.label_u32).text.casefold()
            for group in version.record.groups
            if group.group_type == DIALOGUE_CHILD_GROUP_TYPE
        }
        roles = {topic_roles[key] for key in topic_keys if key in topic_roles}
        if len(roles) != 1:
            raise ValueError("TTW CG00 INFO-to-DIAL join differs")
        role = roles.pop()
        if joined_form_key(info, "QSTI").casefold() != quest_form_key.casefold():
            raise ValueError("TTW CG00 INFO-to-QUST join differs")
        voice_links = []
        for subrecord in iter_subrecords(version.record):
            if subrecord.signature != "CTDA":
                continue
            condition = _dialogue_condition(subrecord.data)
            if int(condition["function"]) == GET_IS_VOICE_TYPE_FUNCTION:
                voice_links.append(
                    version.context.form_key(int(condition["parameter1"])).text
                )
        if len(voice_links) != 1 or voice_links[0].casefold() != str(
            voice_types[role]["formKey"]
        ).casefold():
            raise ValueError("TTW CG00 INFO-to-VTYP join differs")
        return {
            **info,
            "speakerRole": role,
            "topicFormKey": str(topics[role]["formKey"]),
            "voiceTypeFormKey": str(voice_types[role]["formKey"]),
        }

    dialogue_rows = {}
    for stage_name in ("stage10", "stage22Male", "stage22Female", "stage42"):
        raw_info_rows = dialogue_definition.get(stage_name)
        if not isinstance(raw_info_rows, list):
            raise ValueError(f"TTW CG00 dialogue {stage_name} rows are absent")
        dialogue_rows[stage_name] = [compile_info(value) for value in raw_info_rows]

    raw_effects = closure.get("imageSpaceModifiers")
    raw_sounds = closure.get("sounds")
    if not isinstance(raw_effects, list) or not isinstance(raw_sounds, list):
        raise ValueError("TTW CG00 effect or sound closure is absent")
    image_space_modifiers = [record_contract(value) for value in raw_effects]
    sounds = [record_contract(value) for value in raw_sounds]

    unique_contracts: dict[str, dict[str, object]] = {}

    def remember(contract: dict[str, object]) -> None:
        unique_contracts[str(contract["formKey"]).casefold()] = contract

    for contract in (
        *records.values(),
        player,
        *placed_references,
        *image_space_modifiers,
        *sounds,
        *topics.values(),
        *voice_types.values(),
    ):
        remember(contract)
    for participant in participants:
        remember(participant["base"])
    for rows in package_sections.values():
        for row in rows:
            remember(row["package"])
            remember(row["idle"])
    for rows in dialogue_rows.values():
        for row in rows:
            remember(row)
    record_type_counts: dict[str, int] = {}
    for contract in unique_contracts.values():
        record_type = str(contract["recordType"])
        record_type_counts[record_type] = record_type_counts.get(record_type, 0) + 1

    cg00_scene_closure = {
        "cell": records["vault101d"],
        "player": player,
        "participants": participants,
        "placedReferences": explicit_placed,
        "packageSections": package_sections,
        "dialogue": {
            "questFormKey": quest_form_key,
            "topics": topics,
            "voiceTypes": voice_types,
            **dialogue_rows,
        },
        "imageSpaceModifiers": image_space_modifiers,
        "sounds": sounds,
        "recordCount": len(unique_contracts),
        "recordTypeCounts": dict(sorted(record_type_counts.items())),
        "recordClosureReady": True,
        "archiveMembersIndexed": False,
        "profileEmissionReady": False,
        "runtimeReady": False,
    }
    return {
        "schema": TTW_INPUT_ENUMERATION_SCHEMA,
        "status": "validated-record-inputs-not-profile-emission",
        "source": source.compiler_contract(),
        "openingRecipe": {
            "file": str(resolved_opening_recipe),
            "sha256": file_sha256(resolved_opening_recipe),
        },
        "records": records,
        "cg00SceneClosure": cg00_scene_closure,
        "profileEmissionReady": False,
        "runtimeReady": False,
    }


def atomic_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    os.replace(temporary, path)


def atomic_bytes(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def _case_insensitive_directory(root: Path, expected_name: str) -> Path | None:
    matches = [
        path
        for path in root.iterdir()
        if path.is_dir() and path.name.casefold() == expected_name.casefold()
    ]
    if len(matches) > 1:
        raise ValueError(f"Fallout 3 installation contains duplicate {expected_name} directories")
    return matches[0] if matches else None


def resolve_installation(selected_root: Path, recipe: dict[str, object]) -> tuple[Path, Path]:
    selected = selected_root.resolve()
    if not selected.is_dir():
        raise FileNotFoundError(f"Selected Fallout 3 folder does not exist: {selected}")
    install = dict(recipe["install"])
    master_name = str(install["masterFile"])
    try:
        find_case_insensitive_file(selected, master_name)
        return selected.parent, selected
    except FileNotFoundError:
        data_root = _case_insensitive_directory(selected, str(install["dataDirectoryName"]))
        if data_root is None:
            raise FileNotFoundError(
                "Select the Fallout 3 installation folder or its Data folder; "
                f"{master_name} was not found."
            )
        find_case_insensitive_file(data_root, master_name)
        return selected, data_root


def load_recipe(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != RECIPE_SCHEMA or document.get("id") != path.stem:
        raise ValueError(f"Unexpected Fallout 3 owned-profile recipe: {path}")
    install = document.get("install")
    menu = document.get("mainMenu")
    opening = document.get("opening")
    if (
        document.get("campaign") != "Fallout3"
        or not isinstance(install, dict)
        or not isinstance(menu, dict)
        or not isinstance(opening, dict)
        or not isinstance(install.get("requiredArchives"), list)
        or not install["requiredArchives"]
        or not isinstance(opening.get("quests"), list)
        or not opening["quests"]
    ):
        raise ValueError(f"Fallout 3 owned-profile recipe is incomplete: {path}")
    configured_video_import = document.get("videoImport")
    if (
        not isinstance(configured_video_import, dict)
        or json.dumps(configured_video_import, sort_keys=True, separators=(",", ":"))
        != json.dumps(FO3_VIDEO_IMPORT_POLICY, sort_keys=True, separators=(",", ":"))
    ):
        raise ValueError(
            "Fallout 3 owned-profile recipe video-import policy differs from its "
            "pinned ffmpeg2theora contract"
        )
    return document


def _file_row(path: Path, *, role: str | None = None) -> dict[str, object]:
    row: dict[str, object] = {
        "file": path.name,
        "source": str(path.resolve()),
        "bytes": path.stat().st_size,
        "sha256": file_sha256(path),
    }
    if role is not None:
        row["role"] = role
    return row


def _editor_id(record: object) -> str | None:
    return next(
        (
            zstring(subrecord.data)
            for subrecord in iter_subrecords(record)
            if subrecord.signature == "EDID"
        ),
        None,
    )


def _text_values(record: object, signature: str) -> list[str]:
    return [
        zstring(subrecord.data)
        for subrecord in iter_subrecords(record)
        if subrecord.signature == signature
    ]


def _form_id(value: int) -> str:
    return f"{value:0{FORM_ID_HEX_CHARACTERS}x}"


def _single_subrecord(record: object, signature: str) -> bytes:
    matches = [
        subrecord.data
        for subrecord in iter_subrecords(record)
        if subrecord.signature == signature
    ]
    if len(matches) != 1:
        raise ValueError(
            f"Fallout 3 {record.signature} {_form_id(record.form_id)} "
            f"has {len(matches)} {signature} values"
        )
    return matches[0]


def _reference_transform_contract(record: object) -> dict[str, object]:
    raw = _single_subrecord(record, "DATA")
    if len(raw) != REFERENCE_TRANSFORM_BYTES:
        raise ValueError(
            f"Fallout 3 {record.signature} {_form_id(record.form_id)} "
            "has a malformed reference transform"
        )
    values = struct.unpack(f"<{REFERENCE_TRANSFORM_FLOATS}f", raw)
    if not all(math.isfinite(value) for value in values):
        raise ValueError(
            f"Fallout 3 {record.signature} {_form_id(record.form_id)} "
            "has a non-finite reference transform"
        )
    scale_values = [
        subrecord.data
        for subrecord in iter_subrecords(record)
        if subrecord.signature == "XSCL"
    ]
    if len(scale_values) > 1 or (
        scale_values and len(scale_values[0]) != REFERENCE_SCALE_BYTES
    ):
        raise ValueError(
            f"Fallout 3 {record.signature} {_form_id(record.form_id)} has malformed XSCL"
        )
    scale = struct.unpack("<f", scale_values[0])[0] if scale_values else DEFAULT_REFERENCE_SCALE
    if not math.isfinite(scale) or scale <= 0:
        raise ValueError(
            f"Fallout 3 {record.signature} {_form_id(record.form_id)} has invalid scale"
        )
    return {
        "positionGameUnits": list(values[:3]),
        "rotationRadians": list(values[3:]),
        "scale": scale,
    }


def _form_id_list(record: object, signature: str) -> list[int]:
    payload = _single_subrecord(record, signature)
    if not payload or len(payload) % FORM_ID_BYTES:
        raise ValueError(
            f"Fallout 3 {record.signature} {_form_id(record.form_id)} "
            f"has an invalid {signature} list"
        )
    return list(struct.unpack(f"<{len(payload) // FORM_ID_BYTES}I", payload))


def _cg00_package_playback_contract(
    package: object,
    by_form: dict[int, object],
) -> dict[str, object]:
    idle_flags_data = _single_subrecord(package, "IDLF")
    idle_count_data = _single_subrecord(package, "IDLC")
    idle_timer_data = _single_subrecord(package, "IDLT")
    if (
        len(idle_flags_data) not in PACKAGE_IDLE_FLAG_BYTES
        or len(idle_count_data) not in PACKAGE_IDLE_COUNT_BYTES
        or len(idle_timer_data) != PACKAGE_IDLE_TIMER_BYTES
    ):
        raise ValueError("Fallout 3 early CG00 package idle selection differs")
    idle_form_ids = _form_id_list(package, "IDLA")
    idle_count = int.from_bytes(idle_count_data, "little")
    idle_timer = struct.unpack("<f", idle_timer_data)[0]
    if idle_count != len(idle_form_ids) or idle_count == 0 or not math.isfinite(idle_timer):
        raise ValueError("Fallout 3 early CG00 package idle clock differs")

    events: dict[str, str | None] = {}
    pending_event: str | None = None
    for subrecord in iter_subrecords(package):
        if subrecord.signature in PACKAGE_EVENT_NAMES:
            pending_event = PACKAGE_EVENT_NAMES[subrecord.signature]
            if pending_event in events:
                raise ValueError("Fallout 3 early CG00 package event is duplicated")
        elif subrecord.signature == "INAM" and pending_event is not None:
            if len(subrecord.data) != FORM_ID_BYTES:
                raise ValueError("Fallout 3 early CG00 package event IDLE is invalid")
            event_form_id = struct.unpack("<I", subrecord.data)[0]
            event_idle = by_form.get(event_form_id) if event_form_id else None
            if event_idle is not None and event_idle.signature != IDLE_RECORD:
                raise ValueError("Fallout 3 early CG00 package event target differs")
            events[pending_event] = (
                _form_id(event_idle.form_id) if event_idle is not None else None
            )
            pending_event = None
    if pending_event is not None or set(events) != set(PACKAGE_EVENT_NAMES.values()):
        raise ValueError("Fallout 3 early CG00 package events are incomplete")
    return {
        "idleSelection": {
            "flags": int.from_bytes(idle_flags_data, "little"),
            "count": idle_count,
            "timerSeconds": idle_timer,
        },
        "events": events,
    }


def _cg00_package_stage_condition(
    package: object,
    quest_form_id: int,
) -> dict[str, object]:
    conditions = [
        _dialogue_condition(subrecord.data)
        for subrecord in iter_subrecords(package)
        if subrecord.signature == "CTDA"
    ]
    if len(conditions) != 1:
        raise ValueError("Fallout 3 early CG00 actor package stage condition differs")
    condition = conditions[0]
    comparison = float(condition["comparisonValue"])
    stage = int(comparison)
    if (
        condition["operatorFlags"] != CONDITION_EQUAL_OPERATOR_FLAGS
        or condition["function"] != GET_STAGE_FUNCTION
        or condition["parameter1"] != quest_form_id
        or condition["parameter2"] != 0
        or condition["runOn"] != 0
        or condition["reference"] != 0
        or not comparison.is_integer()
        or stage < 0
    ):
        raise ValueError("Fallout 3 early CG00 actor package stage condition differs")
    return {
        "function": "GetStage",
        "functionId": GET_STAGE_FUNCTION,
        "operator": "equal",
        "operatorFlags": CONDITION_EQUAL_OPERATOR_FLAGS,
        "questFormId": _form_id(quest_form_id),
        "stage": stage,
        "runOn": int(condition["runOn"]),
    }


def _float_contract(values: tuple[float, ...], expected_count: int) -> dict[str, object]:
    if len(values) != expected_count or not all(math.isfinite(value) for value in values):
        raise ValueError("Fallout 3 FaceGen default coordinates are incomplete")
    payload = struct.pack(f"<{len(values)}f", *values)
    return {
        "count": len(values),
        "values": list(values),
        "sha256": hashlib.sha256(payload).hexdigest(),
    }


def _fallout_default_fov_projection(horizontal_fov_degrees: float) -> dict[str, object]:
    if not math.isfinite(horizontal_fov_degrees) or not (
        0.0 < horizontal_fov_degrees < PERSPECTIVE_MAXIMUM_DEGREES
    ):
        raise ValueError("Fallout 3 default FOV is invalid")
    tangent_half_angle = math.tan(math.radians(horizontal_fov_degrees) / 2.0)
    vertical_fov_degrees = math.degrees(
        2.0
        * math.atan(
            tangent_half_angle * FALLOUT_CAMERA_REFERENCE_ASPECT_HEIGHT_OVER_WIDTH
        )
    )
    return {
        "sourceHorizontalFovDegrees": horizontal_fov_degrees,
        "referenceAspectHeightOverWidth": (
            FALLOUT_CAMERA_REFERENCE_ASPECT_HEIGHT_OVER_WIDTH
        ),
        "verticalFovDegrees": vertical_fov_degrees,
        "godotKeepAspect": "keep-height",
    }


def _interpolate_facegen_geometry(
    current: tuple[float, ...],
    source: tuple[float, ...],
    percent: float,
) -> tuple[float, ...]:
    if len(current) != len(source) or not 0.0 <= percent <= 100.0:
        raise ValueError("Fallout 3 MatchFaceGeometry inputs are invalid")
    fraction = percent / 100.0
    result = tuple(
        current_value + (source_value - current_value) * fraction
        for current_value, source_value in zip(current, source)
    )
    if not all(math.isfinite(value) for value in result):
        raise ValueError("Fallout 3 MatchFaceGeometry produced a non-finite value")
    return result


def _stage65_command_pairs(
    commands: list[dict[str, object]],
) -> list[tuple[str, str]]:
    subjects: list[str] = []
    matches: dict[str, dict[str, object]] = {}
    for command in commands:
        kind = str(command["kind"])
        subject = str(command["subject"])
        key = subject.casefold()
        if str(command["target"]).casefold() != "player":
            raise ValueError("Fallout 3 CG00 stage 65 command target is unsupported")
        pair = matches.setdefault(key, {"subject": subject})
        if kind in pair or kind not in {"matchRace", "matchFaceGeometry"}:
            raise ValueError("Fallout 3 CG00 stage 65 command pairing is ambiguous")
        pair[kind] = command
        if kind == "matchRace":
            subjects.append(key)
    if len(subjects) * 2 != len(commands) or len(subjects) != len(set(subjects)):
        raise ValueError("Fallout 3 CG00 stage 65 commands are incomplete")
    pairs = []
    for key in subjects:
        pair = matches[key]
        if set(pair) != {"subject", "matchRace", "matchFaceGeometry"}:
            raise ValueError("Fallout 3 CG00 stage 65 commands are incomplete")
        face_command = dict(pair["matchFaceGeometry"])
        pairs.append((str(pair["subject"]), str(face_command["template"])))
    if len({template.casefold() for _, template in pairs}) != 1:
        raise ValueError("Fallout 3 CG00 stage 65 match-percentage source is ambiguous")
    return pairs


def _compile_stage65_appearance_contract(
    catalog: object,
    records: list[object],
    character_selection: dict[str, object],
    races: list[dict[str, object]],
) -> dict[str, object]:
    transition = dict(character_selection["section4Transition"])
    stage_result = dict(transition["nextStageResult"])
    commands = [dict(command) for command in stage_result["commands"]]
    command_pairs = _stage65_command_pairs(commands)
    stage = int(stage_result["stage"])

    records_by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            records_by_editor.setdefault(editor_id.casefold(), []).append(record)

    percentage_editor_id = command_pairs[0][1]
    percentage_records = [
        record
        for record in records_by_editor.get(percentage_editor_id.casefold(), [])
        if record.signature == GLOBAL_RECORD
    ]
    if len(percentage_records) != 1:
        raise ValueError("Fallout 3 CG00 MatchFaceGeometry global does not resolve uniquely")
    percentage_record = percentage_records[0]
    global_type = _single_subrecord(percentage_record, "FNAM")
    global_value = _single_subrecord(percentage_record, "FLTV")
    if len(global_type) != 1 or len(global_value) != 4:
        raise ValueError("Fallout 3 CG00 MatchFaceGeometry global layout is unsupported")
    percentage = struct.unpack("<f", global_value)[0]
    if not math.isfinite(percentage) or not 0.0 <= percentage <= 100.0:
        raise ValueError("Fallout 3 CG00 MatchFaceGeometry percentage is invalid")

    parent_sources = []
    for reference_editor_id, template_editor_id in command_pairs:
        if template_editor_id.casefold() != percentage_editor_id.casefold():
            raise ValueError("Fallout 3 CG00 MatchFaceGeometry global identity differs")
        references = [
            record
            for record in records_by_editor.get(reference_editor_id.casefold(), [])
            if record.signature == ACTOR_REFERENCE_RECORD
        ]
        if len(references) != 1:
            raise ValueError(
                f"Fallout 3 CG00 parent reference does not resolve: {reference_editor_id}"
            )
        reference = references[0]
        base_form_id = struct.unpack("<I", _single_subrecord(reference, "NAME"))[0]
        parent = catalog.actors.get(base_form_id)
        if (
            parent is None
            or parent.female
            or parent.race_form_id not in catalog.races
            or len(parent.face_symmetric_geometry) != FACEGEN_SYMMETRIC_GEOMETRY_FLOATS
            or len(parent.face_asymmetric_geometry) != FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS
        ):
            raise ValueError("Fallout 3 CG00 parent FaceGen identity is incomplete")
        parent_sources.append(
            {
                "referenceFormId": _form_id(reference.form_id),
                "referenceEditorId": reference_editor_id,
                "referenceRecordSha256": hashlib.sha256(reference.data).hexdigest(),
                "baseFormId": _form_id(parent.form_id),
                "baseEditorId": parent.editor_id,
                "baseRecordSha256": catalog.record_data_sha256["NPC_"][parent.form_id],
                "originalRaceFormId": _form_id(parent.race_form_id),
                "faceGenIdentity": {
                    "symmetricGeometry": _float_contract(
                        parent.face_symmetric_geometry,
                        FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
                    ),
                    "asymmetricGeometry": _float_contract(
                        parent.face_asymmetric_geometry,
                        FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
                    ),
                },
            }
        )

    selection_results = []
    for race_row in races:
        race_form_id = int(str(race_row["formId"]), FORM_ID_RADIX)
        race = catalog.races.get(race_form_id)
        if (
            race is None
            or len(race.male_face_symmetric_geometry)
            != FACEGEN_SYMMETRIC_GEOMETRY_FLOATS
            or len(race.male_face_asymmetric_geometry)
            != FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS
            or len(race.male_face_symmetric_texture)
            != FACEGEN_SYMMETRIC_TEXTURE_FLOATS
        ):
            raise ValueError("Fallout 3 CG00 matched RACE FaceGen identity is incomplete")
        race_sexes = dict(race_row["sex"])
        for player_sex in ("male", "female"):
            player_facegen = dict(dict(race_sexes[player_sex])["faceGenDefaults"])
            player_symmetric = tuple(
                float(value)
                for value in dict(player_facegen["symmetricGeometry"])["values"]
            )
            player_asymmetric = tuple(
                float(value)
                for value in dict(player_facegen["asymmetricGeometry"])["values"]
            )
            parent_results = []
            for parent_source in parent_sources:
                parent = catalog.actors[int(str(parent_source["baseFormId"]), FORM_ID_RADIX)]
                matched_race_symmetric = compose_facegen_coordinates(
                    parent.face_symmetric_geometry,
                    race.male_face_symmetric_geometry,
                )
                matched_race_asymmetric = compose_facegen_coordinates(
                    parent.face_asymmetric_geometry,
                    race.male_face_asymmetric_geometry,
                )
                parent_results.append(
                    {
                        "referenceFormId": parent_source["referenceFormId"],
                        "referenceEditorId": parent_source["referenceEditorId"],
                        "baseFormId": parent_source["baseFormId"],
                        "raceFormId": race_row["formId"],
                        "faceGen": {
                            "preMatchSymmetricGeometry": _float_contract(
                                matched_race_symmetric,
                                FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
                            ),
                            "preMatchAsymmetricGeometry": _float_contract(
                                matched_race_asymmetric,
                                FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
                            ),
                            "symmetricGeometry": _float_contract(
                                _interpolate_facegen_geometry(
                                    matched_race_symmetric,
                                    player_symmetric,
                                    percentage,
                                ),
                                FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
                            ),
                            "asymmetricGeometry": _float_contract(
                                _interpolate_facegen_geometry(
                                    matched_race_asymmetric,
                                    player_asymmetric,
                                    percentage,
                                ),
                                FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
                            ),
                            "symmetricTexture": _float_contract(
                                race.male_face_symmetric_texture,
                                FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
                            ),
                            "texturePolicy": "matched-race-default-not-face-geometry-morphed",
                        },
                    }
                )
            selection_results.append(
                {
                    "playerRaceFormId": race_row["formId"],
                    "playerSex": player_sex,
                    "playerFaceGen": {
                        "symmetricGeometrySha256": dict(
                            player_facegen["symmetricGeometry"]
                        )["sha256"],
                        "asymmetricGeometrySha256": dict(
                            player_facegen["asymmetricGeometry"]
                        )["sha256"],
                        "symmetricTextureSha256": dict(
                            player_facegen["symmetricTexture"]
                        )["sha256"],
                    },
                    "parents": parent_results,
                }
            )

    contract = {
        "schema": "opennv-fo3-cg00-stage-65-appearance/v1",
        "status": "source-backed-command-application",
        "sourceStage": int(transition["sourceStage"]),
        "stage": stage,
        "stageSourceSha256": stage_result["stageSourceSha256"],
        "accountedCommandCount": len(commands),
        "commands": commands,
        "semantics": {
            "matchRace": "target-race-equals-source-current-race-with-default-face-texture",
            "matchFaceGeometry": "linear-current-to-source-geometry-percent",
            "matchFaceTexture": "unchanged-by-match-face-geometry",
        },
        "matchPercentage": {
            "formId": _form_id(percentage_record.form_id),
            "editorId": percentage_editor_id,
            "recordSha256": hashlib.sha256(percentage_record.data).hexdigest(),
            "type": global_type.decode("ascii"),
            "value": percentage,
        },
        "parentSources": parent_sources,
        "selectionResults": selection_results,
        "nextBoundary": "fo3-cg00-post-stage-65-dialogue-playback-not-implemented",
    }
    stage_result["runtimeReady"] = True
    stage_result.pop("blocker", None)
    stage_result["contractSchema"] = contract["schema"]
    transition["nextStageResult"] = stage_result
    character_selection["section4Transition"] = transition
    return contract


def _compile_stage80_transition(
    catalog: object,
    records: list[object],
    character_selection: dict[str, object],
) -> dict[str, object]:
    dialogue = dict(character_selection["postStage65Dialogue"])
    stage_result = dict(dialogue["stageResult"])
    commands = [dict(command) for command in stage_result["commands"]]
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    package_commands = [command for command in commands if command["kind"] == "addScriptPackage"]
    if len(package_commands) != 1:
        raise ValueError("Fallout 3 CG00 stage 80 player package is ambiguous")
    package_editor_id = str(package_commands[0]["packageEditorId"])
    packages = [
        record
        for record in by_editor.get(package_editor_id.casefold(), [])
        if record.signature == PACKAGE_RECORD
    ]
    if len(packages) != 1:
        raise ValueError("Fallout 3 CG00 stage 80 player package does not resolve")
    package = packages[0]
    package_data = _single_subrecord(package, "PKDT")
    location_data = _single_subrecord(package, "PLDT")
    if len(package_data) != PACKAGE_DATA_BYTES or len(location_data) != PACKAGE_LOCATION_BYTES:
        raise ValueError("Fallout 3 CG00 stage 80 player package layout is unsupported")
    flags, package_type, _unused, procedure_flags, type_flags, _unknown = struct.unpack(
        "<IBBHHH", package_data
    )
    location_type, location_form_id, radius = struct.unpack("<III", location_data)
    idle_flags_data = _single_subrecord(package, "IDLF")
    idle_count_data = _single_subrecord(package, "IDLC")
    idle_timer_data = _single_subrecord(package, "IDLT")
    if (
        len(idle_flags_data) not in PACKAGE_IDLE_FLAG_BYTES
        or len(idle_count_data) not in PACKAGE_IDLE_COUNT_BYTES
        or len(idle_timer_data) != PACKAGE_IDLE_TIMER_BYTES
    ):
        raise ValueError("Fallout 3 CG00 stage 80 package idle layout is unsupported")
    idle_count = int.from_bytes(idle_count_data, "little")
    idle_form_ids = _form_id_list(package, "IDLA")
    idle_timer = struct.unpack("<f", idle_timer_data)[0]
    if idle_count != len(idle_form_ids) or idle_count == 0 or not math.isfinite(idle_timer):
        raise ValueError("Fallout 3 CG00 stage 80 package idle selection differs")

    def idle_row(form_id: int) -> dict[str, object]:
        record = by_form.get(form_id)
        if record is None or record.signature != IDLE_RECORD:
            raise ValueError("Fallout 3 CG00 stage 80 package IDLE is absent")
        models = _text_values(record, "MODL")
        if len(models) != 1 or not models[0].casefold().endswith(".kf"):
            raise ValueError("Fallout 3 CG00 stage 80 package IDLE model is unsupported")
        return {
            "formId": _form_id(record.form_id),
            "editorId": _editor_id(record),
            "modelPath": canonical_member_path(f"meshes\\{models[0]}"),
            "recordSha256": hashlib.sha256(record.data).hexdigest(),
        }

    events: dict[str, dict[str, object] | None] = {}
    pending_event: str | None = None
    for subrecord in iter_subrecords(package):
        if subrecord.signature in PACKAGE_EVENT_NAMES:
            pending_event = PACKAGE_EVENT_NAMES[subrecord.signature]
            if pending_event in events:
                raise ValueError("Fallout 3 CG00 stage 80 package event is duplicated")
        elif subrecord.signature == "INAM" and pending_event is not None:
            if len(subrecord.data) != FORM_ID_BYTES:
                raise ValueError("Fallout 3 CG00 stage 80 package event IDLE is invalid")
            form_id = struct.unpack("<I", subrecord.data)[0]
            events[pending_event] = idle_row(form_id) if form_id else None
            pending_event = None
    if pending_event is not None or set(events) != set(PACKAGE_EVENT_NAMES.values()):
        raise ValueError("Fallout 3 CG00 stage 80 package events are incomplete")

    def resolve_reference(editor_id: str) -> tuple[object, object]:
        matches = [
            record
            for record in by_editor.get(editor_id.casefold(), [])
            if record.signature == ACTOR_REFERENCE_RECORD
        ]
        if len(matches) != 1:
            raise ValueError(f"Fallout 3 CG00 stage 80 actor reference differs: {editor_id}")
        reference = matches[0]
        base_form_id = struct.unpack("<I", _single_subrecord(reference, "NAME"))[0]
        actor = catalog.actors.get(base_form_id)
        if actor is None:
            raise ValueError("Fallout 3 CG00 stage 80 actor base is absent")
        return reference, actor

    resolved_commands = []
    for index, command in enumerate(commands):
        kind = str(command["kind"])
        if kind == "addScriptPackage":
            resolved_commands.append(
                {
                    "index": index,
                    "kind": kind,
                    "packageFormId": _form_id(package.form_id),
                    "packageEditorId": package_editor_id,
                }
            )
            continue
        subject = str(command["subject"])
        reference, actor = resolve_reference(subject)
        resolved = {
            "index": index,
            "kind": kind,
            "referenceFormId": _form_id(reference.form_id),
            "referenceEditorId": subject,
            "referenceRecordSha256": hashlib.sha256(reference.data).hexdigest(),
            "baseFormId": _form_id(actor.form_id),
            "baseEditorId": actor.editor_id,
            "baseRecordSha256": catalog.record_data_sha256["NPC_"][actor.form_id],
        }
        if kind == "setScriptVariable":
            base_record = by_form.get(actor.form_id)
            if base_record is None or base_record.signature != ACTOR_BASE_RECORD:
                raise ValueError("Fallout 3 CG00 stage 80 variable owner base differs")
            script_form_id = struct.unpack("<I", _single_subrecord(base_record, "SCRI"))[0]
            script = by_form.get(script_form_id)
            if script is None or script.signature != SCRIPT_RECORD:
                raise ValueError("Fallout 3 CG00 stage 80 variable script is absent")
            variable_name = str(command["variable"])
            declarations = [
                match.group("type").casefold()
                for match in re.finditer(
                    rf"^\s*(?P<type>short|float)\s+{re.escape(variable_name)}\b",
                    _script_source(script),
                    re.IGNORECASE | re.MULTILINE,
                )
            ]
            if len(declarations) != 1:
                raise ValueError("Fallout 3 CG00 stage 80 script variable is ambiguous")
            resolved.update(
                {
                    "scriptFormId": _form_id(script.form_id),
                    "scriptEditorId": _editor_id(script),
                    "scriptSourceSha256": hashlib.sha256(
                        _script_source(script).encode("cp1252")
                    ).hexdigest(),
                    "variable": variable_name,
                    "variableType": declarations[0],
                    "value": command["value"],
                }
            )
        elif kind == "enable":
            if not reference.flags & INITIALLY_DISABLED_RECORD_FLAG:
                raise ValueError("Fallout 3 CG00 stage 80 enable target is not initially disabled")
            resolved["initiallyDisabled"] = True
        elif kind != "evaluatePackage":
            raise ValueError(f"Fallout 3 CG00 stage 80 command kind is unsupported: {kind}")
        resolved_commands.append(resolved)

    contract = {
        "schema": "opennv-fo3-cg00-stage-80-transition/v1",
        "status": "source-backed-stage-result-application",
        "sourceStage": int(dialogue["sourceStage"]),
        "stage": int(dialogue["targetStage"]),
        "dialogueTriggerSchema": dialogue["schema"],
        "stageSourceSha256": stage_result["stageSourceSha256"],
        "accountedCommandCount": len(resolved_commands),
        "commands": resolved_commands,
        "addedPlayerPackage": {
            "formId": _form_id(package.form_id),
            "editorId": package_editor_id,
            "recordSha256": hashlib.sha256(package.data).hexdigest(),
            "flags": flags,
            "type": package_type,
            "procedureFlags": procedure_flags,
            "typeSpecificFlags": type_flags,
            "location": {
                "type": location_type,
                "referenceFormId": _form_id(location_form_id),
                "radius": radius,
            },
            "idleSelection": {
                "flags": int.from_bytes(idle_flags_data, "little"),
                "count": idle_count,
                "timerSeconds": idle_timer,
                "idles": [idle_row(form_id) for form_id in idle_form_ids],
            },
            "events": events,
        },
        "nextBoundary": "fo3-cg00-post-stage-80-dialogue-playback-not-implemented",
    }
    stage_result["runtimeReady"] = True
    stage_result.pop("blocker", None)
    stage_result["contractSchema"] = contract["schema"]
    dialogue["stageResult"] = stage_result
    character_selection["postStage65Dialogue"] = dialogue
    return contract


def _extract_profile_texture(
    archive: BsaArchive,
    archive_sha256: str,
    logical_path: str,
    profile_root: Path,
    cache: dict[str, dict[str, object]],
) -> dict[str, object]:
    canonical = canonical_member_path(logical_path)
    if not canonical.startswith("textures\\"):
        canonical = f"textures\\{canonical}"
    if canonical in cache:
        return cache[canonical]
    member = archive.extract(canonical)
    suffix = Path(canonical.replace("\\", "/")).suffix.casefold()
    if not suffix:
        raise ValueError(f"Fallout 3 appearance texture has no extension: {canonical}")
    output = (
        profile_root.resolve()
        / "generated"
        / "fallout3"
        / "appearance"
        / "textures"
        / f"{member.sha256[:PROFILE_ID_HEX_CHARACTERS]}{suffix}"
    )
    if output.exists():
        if not output.is_file() or file_sha256(output) != member.sha256:
            raise ValueError(f"Fallout 3 appearance texture cache differs: {output}")
    else:
        atomic_bytes(output, member.data)
    preview = output.with_suffix(".png")
    image = decode_dds(member.data, False)
    encoded = BytesIO()
    image.save(encoded, format="PNG", compress_level=PNG_MAXIMUM_COMPRESSION_LEVEL)
    preview_payload = encoded.getvalue()
    preview_sha256 = hashlib.sha256(preview_payload).hexdigest()
    if preview.exists():
        if not preview.is_file() or file_sha256(preview) != preview_sha256:
            raise ValueError(f"Fallout 3 appearance preview cache differs: {preview}")
    else:
        atomic_bytes(preview, preview_payload)
    row = {
        "logicalPath": member.logical_path,
        "sourceArchive": archive.archive.name,
        "sourceArchiveSha256": archive_sha256,
        "sourceBytes": len(member.data),
        "sourceSha256": member.sha256,
        "output": str(output),
        "outputBytes": output.stat().st_size,
        "outputSha256": file_sha256(output),
        "previewOutput": str(preview),
        "previewOutputBytes": preview.stat().st_size,
        "previewOutputSha256": file_sha256(preview),
        "previewWidth": image.width,
        "previewHeight": image.height,
    }
    cache[canonical] = row
    return row


def _appearance_ui_contract(
    recipe: dict[str, object],
    menu_members: dict[str, object],
    texture_archive: BsaArchive,
    texture_archive_sha256: str,
    profile_root: Path,
    texture_cache: dict[str, dict[str, object]],
) -> dict[str, object]:
    definition = dict(dict(recipe["opening"])["appearanceUi"])
    document_path = canonical_member_path(str(definition["document"]))
    member = menu_members.get(document_path)
    if member is None:
        raise ValueError("Fallout 3 appearance menu XML was not admitted")
    text = member.data.decode("cp1252")
    menu_name = str(definition["menuName"])
    panel_name = str(definition["panelName"])
    if f'<menu name="{menu_name}">' not in text:
        raise ValueError("Fallout 3 appearance menu identity differs")
    panel = re.search(
        rf'<rect\s+name="{re.escape(panel_name)}">(?P<body>.*?)<image\s+name="RSM_Background">',
        text,
        re.DOTALL,
    )
    list_item = re.search(
        r'<template\s+name="RSM_list_item_template">(?P<body>.*?)</template>',
        text,
        re.DOTALL,
    )
    slider = re.search(
        r'<template\s+name="RSM_slider_option_template">(?P<body>.*?)</template>',
        text,
        re.DOTALL,
    )
    face_grab = re.search(
        r'<hotrect\s+name="RSM_Face_Grab">(?P<body>.*?)</hotrect>',
        text,
        re.DOTALL,
    )
    if panel is None or list_item is None or slider is None or face_grab is None:
        raise ValueError("Fallout 3 appearance menu layout owners are absent")

    def dimension(body: str, name: str) -> int:
        match = re.search(rf'<{name}>\s*(?P<value>\d+)\s*</{name}>', body)
        if match is None:
            raise ValueError(f"Fallout 3 appearance menu {name} is absent")
        return int(match.group("value"))

    def visibility(body: str) -> str:
        match = re.search(
            r"<visible>\s*(?:&(?P<entity>true|false);|(?P<number>[01]))\s*</visible>",
            body,
        )
        if match is None:
            if "<visible>" in body:
                raise ValueError("Fallout 3 appearance tile visibility is unsupported")
            return "inherited"
        value = match.group("entity") or match.group("number")
        return "visible" if value in {"true", "1"} else "hidden"

    observed = {
        "panelX": dimension(text, "x"),
        "panelY": dimension(text, "y"),
        "panelWidth": dimension(panel.group("body"), "width"),
        "panelHeight": dimension(panel.group("body"), "height"),
        "faceGrabX": dimension(face_grab.group("body"), "x"),
        "faceGrabY": dimension(face_grab.group("body"), "y"),
        "faceGrabWidth": dimension(face_grab.group("body"), "width"),
        "faceGrabHeight": dimension(face_grab.group("body"), "height"),
        "listItemWidth": dimension(list_item.group("body"), "width"),
        "listItemHeight": dimension(list_item.group("body"), "height"),
        "sliderWidth": dimension(slider.group("body"), "width"),
        "sliderHeight": dimension(slider.group("body"), "height"),
    }
    for key, value in observed.items():
        if value != int(definition[key]):
            raise ValueError(
                f"Fallout 3 appearance menu {key} differs: "
                f"expected={definition[key]} actual={value}"
            )
    text_box_path = canonical_member_path("menus\\prefabs\\text_box.xml")
    text_box_member = menu_members.get(text_box_path)
    if text_box_member is None:
        raise ValueError("Fallout 3 RaceSex text-box prefab was not admitted")
    document, race_tree = _document_index(document_path, member.data)
    _, text_box_tree = _document_index(text_box_path, text_box_member.data)
    document["sha256"] = member.sha256
    race_sex_tiles = _race_sex_menu_tile_contract(
        race_tree,
        document,
        {
            "width": float(definition["sourceCanvasWidth"]),
            "height": float(definition["sourceCanvasHeight"]),
        },
        {document_path: race_tree, text_box_path: text_box_tree},
    )
    navigation = dict(race_sex_tiles["navigation"])
    for role, entity_key, source_path in (
        ("back", "appearanceBackEntity", "menus\\levelup_menu.xml"),
        ("next", "appearanceNextEntity", "menus\\tutorial_menu.xml"),
    ):
        entity = str(definition[entity_key])
        source_member = menu_members.get(canonical_member_path(source_path))
        if source_member is None or f"&{entity};" not in source_member.data.decode("cp1252"):
            raise ValueError(f"Fallout 3 RaceSex {role} string source differs")
        button = dict(navigation[role])
        button["stringEntity"] = entity
        button["label"] = _display_entity(f"&{entity};")
        button["stringSourceDocuments"] = [
            {"path": source_path, "sha256": source_member.sha256}
        ]
        navigation[role] = button
    race_sex_tiles["navigation"] = navigation
    background_path = canonical_member_path(str(definition["backgroundTexture"]))
    if background_path.removeprefix("textures\\") not in text.casefold():
        raise ValueError("Fallout 3 appearance menu background identity differs")
    name_document_path = canonical_member_path(str(definition["nameDocument"]))
    name_member = menu_members.get(name_document_path)
    if name_member is None:
        raise ValueError("Fallout 3 name menu XML was not admitted")
    name_text = name_member.data.decode("cp1252")
    name_menu_name = str(definition["nameMenuName"])
    name_panel_name = str(definition["namePanelName"])
    if f'<menu name="{name_menu_name}">' not in name_text:
        raise ValueError("Fallout 3 name menu identity differs")
    name_panel = re.search(
        rf'<rect\s+name="{re.escape(name_panel_name)}">(?P<body>.*?)</rect>',
        name_text,
        re.DOTALL,
    )
    if name_panel is None:
        raise ValueError("Fallout 3 name menu panel is absent")
    text_edit_tiles = _text_edit_menu_tile_contract(
        parse_tile_document(name_member.data),
        {
            "path": name_document_path,
            "sha256": name_member.sha256,
        },
        {
            "width": float(definition["sourceCanvasWidth"]),
            "height": float(definition["sourceCanvasHeight"]),
        },
        {
            "menuName": name_menu_name,
            "panelTile": name_panel_name,
            "promptTile": definition["namePromptTile"],
            "promptEntity": definition["namePromptEntity"],
            "inputTile": definition["nameInputTile"],
            "acceptTile": definition["nameAcceptTile"],
            "acceptEntity": definition["nameAcceptEntity"],
        },
    )
    name_observed = {
        "panelWidth": dimension(name_panel.group("body"), "width"),
        "panelHeight": dimension(name_panel.group("body"), "height"),
    }
    for key, value in name_observed.items():
        if value != int(definition[f"name{key[0].upper()}{key[1:]}"]):
            raise ValueError(
                f"Fallout 3 name menu {key} differs: "
                f"expected={definition[f'name{key[0].upper()}{key[1:]}']} actual={value}"
            )
    name_background_path = canonical_member_path(str(definition["nameBackgroundTexture"]))
    if name_background_path.removeprefix("textures\\") not in name_text.casefold():
        raise ValueError("Fallout 3 name menu background identity differs")
    return {
        "document": document_path,
        "documentSha256": member.sha256,
        "menuName": menu_name,
        "panelName": panel_name,
        "panelVisibility": visibility(panel.group("body")),
        "raceSexMenuTiles": race_sex_tiles,
        **observed,
        "backgroundTexture": _extract_profile_texture(
            texture_archive,
            texture_archive_sha256,
            background_path,
            profile_root,
            texture_cache,
        ),
        "name": {
            "document": name_document_path,
            "documentSha256": name_member.sha256,
            "menuName": name_menu_name,
            "panelName": name_panel_name,
            "panelVisibility": visibility(name_panel.group("body")),
            **name_observed,
            "textEditMenuTiles": text_edit_tiles,
            "backgroundTexture": _extract_profile_texture(
                texture_archive,
                texture_archive_sha256,
                name_background_path,
                profile_root,
                texture_cache,
            ),
        },
    }


def _special_book_menu_tile_contract(member: object) -> dict[str, object]:
    document = canonical_member_path("menus\\chargen\\specialbookmenu.xml")
    root = parse_tile_document(member.data)
    menus = [node for node in root.children if node.tag == "menu"]
    if len(menus) != 1 or menus[0].name != "SPECIALBookMenu":
        raise ValueError("Fallout 3 SPECIALBookMenu identity differs")
    menu = menus[0]

    def scalar(node: object, trait: str) -> str:
        value = node.child(trait)
        if value is None or value.children or not value.text:
            raise ValueError(f"Fallout 3 SPECIALBookMenu trait is absent: {trait}")
        return value.text

    bindings = []
    for action in ("xbuttonrt", "xbuttonlt", "xright", "xleft", "xup", "xdown", "xbuttonx"):
        node = menu.child(action)
        if node is None or len(node.children) != 1 or node.children[0].tag != "ref":
            raise ValueError(f"Fallout 3 SPECIALBookMenu binding differs: {action}")
        ref = node.children[0]
        if set(ref.attributes) != {"src", "trait"} or ref.attributes["trait"] != "clicked":
            raise ValueError(f"Fallout 3 SPECIALBookMenu binding target differs: {action}")
        bindings.append({"action": action, "tile": ref.attributes["src"], "trait": "clicked"})

    controls = []
    for node in menu.children:
        if node.tag not in {"hotrect", "image"}:
            continue
        if not node.name:
            raise ValueError("Fallout 3 SPECIALBookMenu control identity is absent")
        control = {
            "kind": node.tag,
            "tile": node.name,
            "id": int(float(scalar(node, "id"))),
            "width": float(scalar(node, "width")) if node.child("width") else None,
            "height": float(scalar(node, "height")) if node.child("height") else None,
            "x": float(scalar(node, "_x")) if node.child("_x") else None,
            "y": float(scalar(node, "_y")) if node.child("_y") else None,
            "visible": scalar(node, "visible") if node.child("visible") else None,
            "target": scalar(node, "target") if node.child("target") else None,
            "repeatHorizontal": scalar(node, "repeathorizontal") if node.child("repeathorizontal") else None,
        }
        controls.append(control)
    targets = {str(row["tile"]) for row in controls}
    if len(controls) != len(bindings) + 1 or any(
        str(row["tile"]) not in targets for row in bindings
    ):
        raise ValueError("Fallout 3 SPECIALBookMenu control coverage differs")
    return {
        "schema": "opennv-owned-special-book-menu-tiles/v1",
        "document": document,
        "documentSha256": member.sha256,
        "menuName": menu.name,
        "classEntity": scalar(menu, "class"),
        "stackingTypeEntity": scalar(menu, "stackingtype"),
        "alpha": float(scalar(menu, "alpha")),
        "locusEntity": scalar(menu, "locus"),
        "menuFadeSeconds": float(scalar(menu, "menufade")),
        "systemColorEntity": scalar(menu, "systemcolor"),
        "bindings": bindings,
        "controls": controls,
    }


def _appearance_inventory(
    master: Path,
    recipe: dict[str, object],
    character_selection: dict[str, object],
    menu_member_payloads: dict[str, object],
    texture_archive: BsaArchive,
    texture_archive_sha256: str,
    profile_root: Path,
    ui_archive_path: Path,
    owned_archives: OwnedArchiveStack,
    configuration: object,
) -> dict[str, object]:
    selection_recipe = dict(dict(recipe["opening"])["characterSelection"])
    player_form_id = int(str(selection_recipe["playerBaseFormId"]), FORM_ID_RADIX)
    player_editor_id = str(selection_recipe["playerEditorId"])
    catalog = scan_actor_catalog(master)
    player = catalog.actors.get(player_form_id)
    if player is None or player.editor_id != player_editor_id:
        raise ValueError("Fallout 3 player appearance source identity differs")
    records = list(
        iter_plugin_records(
            master,
            frozenset(
                {
                    "RACE",
                    "HAIR",
                    "EYES",
                    GLOBAL_RECORD,
                    ACTOR_REFERENCE_RECORD,
                    CREATURE_REFERENCE_RECORD,
                    "CREA",
                    ACTOR_BASE_RECORD,
                    SCRIPT_RECORD,
                    PACKAGE_RECORD,
                    IDLE_RECORD,
                }
            ),
        )
    )
    record_by_form = {record.form_id: record for record in records}
    texture_cache: dict[str, dict[str, object]] = {}

    def appearance_option(form_id: int, expected_type: str) -> dict[str, object]:
        record = record_by_form.get(form_id)
        part = catalog.parts.get(form_id)
        if record is None or record.signature != expected_type or part is None:
            raise ValueError(
                f"Fallout 3 appearance part does not resolve: {expected_type} {_form_id(form_id)}"
            )
        if part.texture_path is None:
            raise ValueError(
                f"Fallout 3 appearance part has no texture: {expected_type} {_form_id(form_id)}"
            )
        return {
            "formId": _form_id(form_id),
            "recordType": expected_type,
            "editorId": part.editor_id,
            "label": part.name,
            "modelPath": part.model_path,
            "recordSha256": catalog.record_data_sha256[expected_type][form_id],
            "texture": _extract_profile_texture(
                texture_archive,
                texture_archive_sha256,
                part.texture_path,
                profile_root,
                texture_cache,
            ),
        }

    races = []
    for record in records:
        if record.signature != "RACE":
            continue
        data = _single_subrecord(record, "DATA")
        if len(data) != RACE_DATA_BYTES:
            raise ValueError(f"Fallout 3 RACE DATA size differs: {_form_id(record.form_id)}")
        flags = struct.unpack_from("<I", data, RACE_FLAGS_OFFSET)[0]
        if not flags & RACE_PLAYABLE_FLAG:
            continue
        race = catalog.races.get(record.form_id)
        if race is None:
            raise ValueError(f"Fallout 3 playable RACE was not decoded: {_form_id(record.form_id)}")
        hair_form_ids = _form_id_list(record, "HNAM")
        eye_form_ids = _form_id_list(record, "ENAM")
        younger_form_id = struct.unpack("<I", _single_subrecord(record, "YNAM"))[0]
        younger = record_by_form.get(younger_form_id)
        if younger is None or younger.signature != "RACE":
            raise ValueError("Fallout 3 playable race has no child-race join")
        sex_contracts: dict[str, object] = {}
        for sex, sex_flag in (("male", HAIR_MALE_FLAG), ("female", HAIR_FEMALE_FLAG)):
            hair_options = []
            for form_id in hair_form_ids:
                part_record = record_by_form.get(form_id)
                if part_record is None or part_record.signature != "HAIR":
                    raise ValueError("Fallout 3 race HNAM does not resolve to HAIR")
                part_flags = _single_subrecord(part_record, "DATA")
                if len(part_flags) != 1:
                    raise ValueError("Fallout 3 HAIR DATA size differs")
                if part_flags[0] & HAIR_PLAYABLE_FLAG and part_flags[0] & sex_flag:
                    hair_options.append(appearance_option(form_id, "HAIR"))
            eye_options = []
            for form_id in eye_form_ids:
                part_record = record_by_form.get(form_id)
                if part_record is None or part_record.signature != "EYES":
                    raise ValueError("Fallout 3 race ENAM does not resolve to EYES")
                part_flags = _single_subrecord(part_record, "DATA")
                if len(part_flags) != 1:
                    raise ValueError("Fallout 3 EYES DATA size differs")
                if part_flags[0] & HAIR_PLAYABLE_FLAG:
                    eye_options.append(appearance_option(form_id, "EYES"))
            if not hair_options or not eye_options:
                raise ValueError("Fallout 3 playable race has no sex-aware hair or eye defaults")
            baseline_symmetric = (
                race.male_face_symmetric_geometry
                if sex == "male"
                else race.female_face_symmetric_geometry
            )
            baseline_asymmetric = (
                race.male_face_asymmetric_geometry
                if sex == "male"
                else race.female_face_asymmetric_geometry
            )
            head_textures = (
                race.male_head_textures if sex == "male" else race.female_head_textures
            )
            if not head_textures or head_textures[0] is None:
                raise ValueError("Fallout 3 playable race has no sex-aware head texture")
            composed_symmetric = compose_facegen_coordinates(
                player.face_symmetric_geometry,
                baseline_symmetric,
            )
            composed_asymmetric = compose_facegen_coordinates(
                player.face_asymmetric_geometry,
                baseline_asymmetric,
            )
            sex_contracts[sex] = {
                "headTexture": _extract_profile_texture(
                    texture_archive,
                    texture_archive_sha256,
                    str(head_textures[0]),
                    profile_root,
                    texture_cache,
                ),
                "hairOptions": hair_options,
                "eyeOptions": eye_options,
                "defaultHairFormId": hair_options[0]["formId"],
                "defaultEyesFormId": eye_options[0]["formId"],
                "faceGenDefaults": {
                    "symmetricGeometry": _float_contract(
                        composed_symmetric,
                        FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
                    ),
                    "asymmetricGeometry": _float_contract(
                        composed_asymmetric,
                        FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
                    ),
                    "symmetricTexture": _float_contract(
                        player.face_symmetric_texture,
                        FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
                    ),
                },
            }
        races.append(
            {
                "formId": _form_id(record.form_id),
                "editorId": _editor_id(record),
                "label": _text_values(record, "FULL")[0],
                "flags": flags,
                "childRaceFormId": _form_id(younger_form_id),
                "recordSha256": catalog.record_data_sha256["RACE"][record.form_id],
                "sex": sex_contracts,
            }
        )
    if not races or player.race_form_id not in {
        int(row["formId"], FORM_ID_RADIX) for row in races
    }:
        raise ValueError("Fallout 3 player default does not join the playable race set")
    default_race = next(
        row
        for row in races
        if int(row["formId"], FORM_ID_RADIX) == player.race_form_id
    )
    male_default = dict(default_race["sex"])["male"]
    if (
        male_default["defaultHairFormId"] != _form_id(player.hair_form_id or 0)
        or male_default["defaultEyesFormId"] != _form_id(player.eyes_form_id or 0)
    ):
        raise ValueError("Fallout 3 source-order appearance defaults differ from Player NPC_")
    character_selection["stage65Appearance"] = _compile_stage65_appearance_contract(
        catalog,
        records,
        character_selection,
        races,
    )
    character_selection["stage80Transition"] = _compile_stage80_transition(
        catalog,
        records,
        character_selection,
    )
    appearance = dict(character_selection["appearance"])
    control_policy = dict(dict(recipe["opening"])["faceGenControlSpace"])
    executable_path = master.parent.parent / str(control_policy["sourceExecutable"])
    expected_executable_sha256 = str(
        dict(control_policy["nativeGeometryExposure"])["sourceExecutableSha256"]
    ).casefold()
    if file_sha256(executable_path) != expected_executable_sha256:
        raise ValueError("Owned Fallout 3 FaceGen source executable hash differs")
    executable_payload = executable_path.read_bytes()
    expected_settings = [
        str(dict(control_policy["nativeGeometryExposure"])["settingEntityTemplate"]).format(
            oneBasedIndex=int(index) + 1
        ).encode("ascii")
        for index in dict(control_policy["nativeGeometryExposure"])["controlIndices"]
    ]
    texture_exposure = dict(control_policy["nativeTextureExposure"])
    if (
        str(texture_exposure["sourceExecutableSha256"]).casefold()
        != expected_executable_sha256
    ):
        raise ValueError("Owned Fallout 3 FaceGen tone executable identity differs")
    expected_settings.extend(
        str(texture_exposure["settingEntityTemplate"]).format(
            oneBasedOrdinal=ordinal + 1
        ).encode("ascii")
        for ordinal, _index in enumerate(texture_exposure["controlIndices"])
    )
    if any(executable_payload.count(setting) != 1 for setting in expected_settings):
        raise ValueError("Owned Fallout 3 FaceGen setting exposure differs")
    result = {
        **appearance,
        "schema": "opennv-fo3-cg00-appearance/v1",
        "status": "source-backed-native-creator-all-native-geometry-controls",
        "player": {
            "formId": _form_id(player.form_id),
            "editorId": player.editor_id,
            "recordSha256": catalog.record_data_sha256["NPC_"][player.form_id],
            "defaultRaceFormId": _form_id(player.race_form_id or 0),
            "defaultHairColorRgba": list(player.hair_color_rgba),
            "defaultHairLength": player.hair_length,
            "faceGen": {
                "controlSpace": _compile_facegen_control_space(
                    ui_archive_path,
                    control_policy,
                ),
            },
        },
        "ui": _appearance_ui_contract(
            recipe,
            menu_member_payloads,
            texture_archive,
            texture_archive_sha256,
            profile_root,
            texture_cache,
        ),
        "races": races,
        "preview": (
            "owned-playable-race-male-and-female-valid-hair-eye-full-body-live-previews-"
            "all-native-geometry-controls"
        ),
    }
    result["player"]["faceGen"]["previewHead"] = prepare_default_player_facegen_preview(
        master,
        owned_archives,
        profile_root,
        result,
        configuration,
        include_full_body=True,
        include_all_playable_race_selections=True,
    )
    return result


def _compile_fo3_ui_fonts(
    dialogue_menu_tiles: dict[str, object],
    appearance_contract: dict[str, object],
    ini: dict[str, dict[str, str]],
    font_settings: dict[str, object],
    owned_archives: OwnedArchiveStack,
    profile_root: Path,
    font_pipeline: OwnedTexturePipeline,
) -> list[dict[str, object]]:
    font_ids = {
        int(dialogue_menu_tiles["speakerName"]["font"]),
        int(dialogue_menu_tiles["speakerText"]["font"]),
        int(dialogue_menu_tiles["topics"]["template"]["font"]),
        int(
            dict(dict(appearance_contract["ui"])["raceSexMenuTiles"])[
                "fontId"
            ]
        ),
    }
    rows = []
    for font_id in sorted(font_ids):
        font, _ = _compile_gamebryo_font(
            font_id,
            ini,
            font_settings,
            owned_archives,
            profile_root,
            font_pipeline,
        )
        rows.append({"fontId": font_id, **font})
    return rows


def _script_source(record: object) -> str:
    sources = _text_values(record, "SCTX")
    if len(sources) != 1:
        raise ValueError(f"Fallout 3 script {_form_id(record.form_id)} has ambiguous source")
    return sources[0]


def _source_commands(source: str) -> list[str]:
    return [
        command
        for raw_line in source.splitlines()
        if (command := raw_line.split(";", 1)[0].strip())
    ]


def _dialogue_condition(data: bytes) -> dict[str, object]:
    if len(data) != CONDITION_BYTES:
        raise ValueError("Fallout 3 post-stage-65 dialogue condition layout is unsupported")
    return {
        "operatorFlags": data[0],
        "comparisonValue": struct.unpack_from("<f", data, 4)[0],
        "function": struct.unpack_from("<H", data, CONDITION_FUNCTION_OFFSET)[0],
        "parameter1": struct.unpack_from("<I", data, CONDITION_PARAMETER_1_OFFSET)[0],
        "parameter2": struct.unpack_from("<I", data, CONDITION_PARAMETER_2_OFFSET)[0],
        "runOn": struct.unpack_from("<I", data, CONDITION_RUN_ON_OFFSET)[0],
        "reference": struct.unpack_from("<I", data, CONDITION_REFERENCE_OFFSET)[0],
    }


def _parse_stage80_commands(source: str) -> list[dict[str, object]]:
    commands = []
    for text in _source_commands(source):
        if match := ADD_SCRIPT_PACKAGE_PATTERN.fullmatch(text):
            commands.append(
                {"kind": "addScriptPackage", "packageEditorId": match.group("package")}
            )
            continue
        if match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(text):
            raw_value = match.group("value")
            value: int | float = float(raw_value) if "." in raw_value else int(raw_value)
            commands.append(
                {
                    "kind": "setScriptVariable",
                    "subject": match.group("subject"),
                    "variable": match.group("variable"),
                    "value": value,
                }
            )
            continue
        if match := REFERENCE_COMMAND_PATTERN.fullmatch(text):
            command = match.group("command").casefold()
            commands.append(
                {
                    "kind": "evaluatePackage" if command == "evp" else "enable",
                    "subject": match.group("subject"),
                }
            )
            continue
        raise ValueError(f"Fallout 3 CG00 stage 80 uses an unsupported command: {text}")
    if not commands:
        raise ValueError("Fallout 3 CG00 stage 80 result is empty")
    return commands


def _parse_stage90_commands(source: str) -> list[dict[str, object]]:
    commands = []
    for text in _source_commands(source):
        if match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(text):
            raw_value = match.group("value")
            value: int | float = float(raw_value) if "." in raw_value else int(raw_value)
            commands.append(
                {
                    "kind": "setQuestVariable",
                    "subject": match.group("subject"),
                    "variable": match.group("variable"),
                    "value": value,
                }
            )
            continue
        if match := IMAGE_SPACE_MODIFIER_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "applyImageSpaceModifier",
                    "modifierEditorId": match.group("modifier"),
                }
            )
            continue
        if match := PLAY_SOUND_PATTERN.fullmatch(text):
            commands.append(
                {"kind": "playSound", "soundEditorId": match.group("sound")}
            )
            continue
        raise ValueError(f"Fallout 3 CG00 stage 90 uses an unsupported command: {text}")
    if len(commands) != 4:
        raise ValueError("Fallout 3 CG00 stage 90 command count differs")
    return commands


def _parse_stage100_commands(source: str) -> list[dict[str, object]]:
    commands = []
    for text in _source_commands(source):
        if match := REMOVE_SCRIPT_PACKAGE_PATTERN.fullmatch(text):
            commands.append({"kind": "removeScriptPackage", "subject": match.group("subject")})
            continue
        if match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(text):
            raw_value = match.group("value")
            value: int | float = float(raw_value) if "." in raw_value else int(raw_value)
            commands.append(
                {
                    "kind": "setScriptVariable",
                    "subject": match.group("subject"),
                    "variable": match.group("variable"),
                    "value": value,
                }
            )
            continue
        if match := REMOVE_IMAGE_SPACE_MODIFIER_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "removeImageSpaceModifier",
                    "modifierEditorId": match.group("modifier"),
                }
            )
            continue
        if match := DISABLE_REFERENCE_PATTERN.fullmatch(text):
            commands.append({"kind": "disable", "subject": match.group("subject")})
            continue
        if match := STOP_QUEST_PATTERN.fullmatch(text):
            commands.append({"kind": "stopQuest", "questEditorId": match.group("quest")})
            continue
        if match := SET_PC_YOUNG_PATTERN.fullmatch(text):
            commands.append({"kind": "setPlayerYoung", "value": int(match.group("value"))})
            continue
        if match := SET_STAGE_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "setStage",
                    "questEditorId": match.group("quest"),
                    "stage": int(match.group("stage")),
                }
            )
            continue
        raise ValueError(f"Fallout 3 CG00 stage 100 uses an unsupported command: {text}")
    expected = [
        "removeScriptPackage",
        "setScriptVariable",
        "setScriptVariable",
        "removeImageSpaceModifier",
        "disable",
        "stopQuest",
        "setPlayerYoung",
        "setStage",
    ]
    if [str(command["kind"]) for command in commands] != expected:
        raise ValueError("Fallout 3 CG00 stage 100 command order differs")
    return commands


def _parse_cg01_stage90_commands(source: str) -> list[dict[str, object]]:
    commands = []
    for text in _source_commands(source):
        if match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(text):
            raw_value = match.group("value")
            commands.append(
                {
                    "kind": "setQuestVariable",
                    "subject": match.group("subject"),
                    "variable": match.group("variable"),
                    "value": float(raw_value) if "." in raw_value else int(raw_value),
                }
            )
            continue
        if match := COMPLETE_ALL_OBJECTIVES_PATTERN.fullmatch(text):
            commands.append(
                {"kind": "completeAllObjectives", "questEditorId": match.group("quest")}
            )
            continue
        if match := AUTO_DISPLAY_OBJECTIVES_PATTERN.fullmatch(text):
            commands.append(
                {"kind": "autoDisplayObjectives", "value": int(match.group("value"))}
            )
            continue
        if KILL_QUEST_UPDATES_PATTERN.fullmatch(text):
            commands.append({"kind": "killQuestUpdates"})
            continue
        if match := IMAGE_SPACE_MODIFIER_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "applyImageSpaceModifier",
                    "modifierEditorId": match.group("modifier"),
                }
            )
            continue
        if match := PLAY_SOUND_PATTERN.fullmatch(text):
            commands.append(
                {"kind": "playSound", "soundEditorId": match.group("sound")}
            )
            continue
        raise ValueError(f"Fallout 3 CG01 stage 90 uses an unsupported command: {text}")
    expected = [
        "setQuestVariable",
        "setQuestVariable",
        "completeAllObjectives",
        "autoDisplayObjectives",
        "killQuestUpdates",
        "applyImageSpaceModifier",
        "playSound",
    ]
    if [str(command["kind"]) for command in commands] != expected:
        raise ValueError("Fallout 3 CG01 stage 90 command order differs")
    return commands


def _parse_cg01_stage100_commands(source: str) -> list[dict[str, object]]:
    commands = []
    for text in _source_commands(source):
        if match := STOP_QUEST_PATTERN.fullmatch(text):
            commands.append({"kind": "stopQuest", "questEditorId": match.group("quest")})
            continue
        if match := DISABLE_REFERENCE_PATTERN.fullmatch(text):
            commands.append({"kind": "disable", "subject": match.group("subject")})
            continue
        if match := SET_PLAYER_SCALE_PATTERN.fullmatch(text):
            commands.append({"kind": "setPlayerScale", "value": float(match.group("value"))})
            continue
        if match := SET_PC_TODDLER_PATTERN.fullmatch(text):
            commands.append({"kind": "setPlayerToddler", "value": int(match.group("value"))})
            continue
        if CLEAR_NO_ACTIVATION_SOUND_PATTERN.fullmatch(text):
            commands.append({"kind": "clearNoActivationSound"})
            continue
        if match := SET_STAGE_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "setStage",
                    "questEditorId": match.group("quest"),
                    "stage": int(match.group("stage")),
                }
            )
            continue
        raise ValueError(f"Fallout 3 CG01 stage 100 uses an unsupported command: {text}")
    expected = [
        "stopQuest",
        "disable",
        "setPlayerScale",
        "setPlayerToddler",
        "clearNoActivationSound",
        "setStage",
    ]
    if [str(command["kind"]) for command in commands] != expected:
        raise ValueError("Fallout 3 CG01 stage 100 command order differs")
    return commands


def _parse_cg02_stage5_commands(sources: list[str]) -> list[dict[str, object]]:
    commands = []
    for text in (command for source in sources for command in _source_commands(source)):
        if match := SET_LOCATION_LOAD_SCREENS_PATTERN.fullmatch(text):
            commands.append({"kind": "setLocationSpecificLoadScreensOnly", "value": int(match.group("value"))})
        elif match := SET_IN_CHAR_GEN_PATTERN.fullmatch(text):
            commands.append({"kind": "setInCharGen", "value": int(match.group("value"))})
        elif match := SET_GAME_TIME_PATTERN.fullmatch(text):
            value = match.group("value")
            commands.append({"kind": "setGameTime", "variable": match.group("variable"), "value": float(value) if "." in value else int(value)})
        elif match := PLAYER_CONTROLS_PATTERN.fullmatch(text):
            commands.append({"kind": "disablePlayerControls", "arguments": [int(value) for value in match.group("arguments").split()]})
        elif match := SET_PC_YOUNG_PATTERN.fullmatch(text):
            commands.append({"kind": "setPlayerYoung", "value": int(match.group("value"))})
        elif match := AGE_RACE_PATTERN.fullmatch(text):
            commands.append({"kind": "ageRace", "subject": match.group("subject"), "value": int(match.group("value"))})
        elif match := REMOVE_ALL_ITEMS_PATTERN.fullmatch(text):
            commands.append({"kind": "removeAllItems", "subject": match.group("subject")})
        elif match := ADD_ITEM_PATTERN.fullmatch(text):
            commands.append({"kind": "addItem", "subject": match.group("subject"), "itemEditorId": match.group("item"), "count": int(match.group("count")), "silent": int(match.group("silent"))})
        elif match := EQUIP_ITEM_PATTERN.fullmatch(text):
            commands.append({"kind": "equipItem", "subject": match.group("subject"), "itemEditorId": match.group("item"), "preventUnequip": int(match.group("prevent")), "silent": int(match.group("silent"))})
        elif match := REFERENCE_COMMAND_PATTERN.fullmatch(text):
            if match.group("command").casefold() != "enable":
                raise ValueError(f"Fallout 3 CG02 stage 5 uses an unsupported command: {text}")
            commands.append({"kind": "enable", "subject": match.group("subject")})
        elif match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(text):
            raw = match.group("value")
            commands.append({"kind": "setQuestVariable", "subject": match.group("subject"), "variable": match.group("variable"), "value": float(raw) if "." in raw else int(raw)})
        elif match := PLAY_BINK_COMMAND_PATTERN.fullmatch(text):
            commands.append({"kind": "playBink", "logicalPath": match.group("path"), "arguments": [int(value) for value in match.group("arguments").split()]})
        elif match := LOOK_AT_PATTERN.fullmatch(text):
            commands.append({"kind": "lookAt", "subject": match.group("subject"), "target": match.group("target")})
        elif match := IGNORE_CRIME_PATTERN.fullmatch(text):
            commands.append({"kind": "ignoreCrime", "subject": match.group("subject"), "value": int(match.group("value"))})
        else:
            raise ValueError(f"Fallout 3 CG02 stage 5 uses an unsupported command: {text}")
    return commands


def _parse_cg01_stage0_commands(source: str) -> list[dict[str, object]]:
    commands = []
    for text in _source_commands(source):
        if match := MOVE_TO_REFERENCE_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "moveToReference",
                    "subject": match.group("subject"),
                    "target": match.group("target"),
                }
            )
            continue
        if match := SET_STAGE_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "setStage",
                    "questEditorId": match.group("quest"),
                    "stage": int(match.group("stage")),
                }
            )
            continue
        if match := SET_PLAYER_SCALE_PATTERN.fullmatch(text):
            commands.append({"kind": "setPlayerScale", "value": float(match.group("value"))})
            continue
        raise ValueError(f"Fallout 3 CG01 stage 0 uses an unsupported command: {text}")
    expected = ["moveToReference", "setStage", "setPlayerScale", "moveToReference"]
    if [str(command["kind"]) for command in commands] != expected:
        raise ValueError("Fallout 3 CG01 stage 0 command order differs")
    return commands


def _parse_cg01_stage5_commands(source: str) -> list[dict[str, object]]:
    commands = []
    for text in _source_commands(source):
        if match := SET_LOCATION_LOAD_SCREENS_PATTERN.fullmatch(text):
            commands.append(
                {"kind": "setLocationSpecificLoadScreensOnly", "value": int(match.group("value"))}
            )
            continue
        if match := SET_IN_CHAR_GEN_PATTERN.fullmatch(text):
            commands.append({"kind": "setInCharGen", "value": int(match.group("value"))})
            continue
        if match := REFERENCE_COMMAND_PATTERN.fullmatch(text):
            if match.group("command").casefold() != "enable":
                raise ValueError(f"Fallout 3 CG01 stage 5 uses an unsupported command: {text}")
            commands.append({"kind": "enable", "subject": match.group("subject")})
            continue
        if match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(text):
            raw_value = match.group("value")
            value: int | float = float(raw_value) if "." in raw_value else int(raw_value)
            commands.append(
                {
                    "kind": "setScriptVariable",
                    "subject": match.group("subject"),
                    "variable": match.group("variable"),
                    "value": value,
                }
            )
            continue
        if match := PLAYER_CONTROLS_PATTERN.fullmatch(text):
            command = match.group("command").casefold()
            commands.append(
                {
                    "kind": (
                        "enablePlayerControls"
                        if command == "enableplayercontrols"
                        else "disablePlayerControls"
                    ),
                    "arguments": [int(value) for value in match.group("arguments").split()],
                }
            )
            continue
        if match := AUTO_DISPLAY_OBJECTIVES_PATTERN.fullmatch(text):
            commands.append(
                {"kind": "autoDisplayObjectives", "value": int(match.group("value"))}
            )
            continue
        if match := SET_NO_ACTIVATION_SOUND_PATTERN.fullmatch(text):
            commands.append(
                {"kind": "setNoActivationSound", "soundEditorId": match.group("sound")}
            )
            continue
        if match := SET_PC_TODDLER_PATTERN.fullmatch(text):
            commands.append({"kind": "setPlayerToddler", "value": int(match.group("value"))})
            continue
        if match := SET_PC_YOUNG_PATTERN.fullmatch(text):
            commands.append({"kind": "setPlayerYoung", "value": int(match.group("value"))})
            continue
        if match := PLAY_BINK_COMMAND_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "playBink",
                    "logicalPath": match.group("path"),
                    "arguments": [int(value) for value in match.group("arguments").split()],
                }
            )
            continue
        raise ValueError(f"Fallout 3 CG01 stage 5 uses an unsupported command: {text}")
    expected = [
        "setLocationSpecificLoadScreensOnly",
        "setInCharGen",
        "enable",
        "enable",
        "setScriptVariable",
        "setScriptVariable",
        "enablePlayerControls",
        "disablePlayerControls",
        "autoDisplayObjectives",
        "setNoActivationSound",
        "setPlayerToddler",
        "setPlayerYoung",
        "playBink",
    ]
    if [str(command["kind"]) for command in commands] != expected:
        raise ValueError("Fallout 3 CG01 stage 5 command order differs")
    if commands[6]["arguments"] != [0, 0, 0, 0, 1]:
        raise ValueError("Fallout 3 CG01 stage 5 enabled-control mask differs")
    if commands[7]["arguments"] != [1, 1, 1, 1, 0, 0, 1]:
        raise ValueError("Fallout 3 CG01 stage 5 disabled-control mask differs")
    if commands[12]["arguments"] != [0, 0, 1, 0]:
        raise ValueError("Fallout 3 CG01 stage 5 movie arguments differ")
    return commands


def _parse_cg01_stage10_commands(source: str) -> list[dict[str, object]]:
    commands = []
    for text in _source_commands(source):
        if match := SET_OBJECTIVE_DISPLAYED_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "setObjectiveDisplayed",
                    "questEditorId": match.group("quest"),
                    "index": int(match.group("index")),
                    "value": int(match.group("value")),
                }
            )
            continue
        if match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "setScriptVariable",
                    "subject": match.group("subject"),
                    "variable": match.group("variable"),
                    "value": float(match.group("value")),
                }
            )
            continue
        if match := PLAYER_CONTROLS_PATTERN.fullmatch(text):
            if match.group("command").casefold() != "enableplayercontrols":
                raise ValueError(
                    f"Fallout 3 CG01 stage 10 uses an unsupported command: {text}"
                )
            commands.append(
                {
                    "kind": "enablePlayerControls",
                    "arguments": [int(value) for value in match.group("arguments").split()],
                }
            )
            continue
        if AUTOSAVE_PATTERN.fullmatch(text):
            commands.append({"kind": "autosave"})
            continue
        raise ValueError(f"Fallout 3 CG01 stage 10 uses an unsupported command: {text}")
    expected = [
        "setObjectiveDisplayed",
        "setScriptVariable",
        "enablePlayerControls",
        "autosave",
    ]
    if [str(command["kind"]) for command in commands] != expected:
        raise ValueError("Fallout 3 CG01 stage 10 command order differs")
    if commands[2]["arguments"] != [1, 0, 0, 0, 1, 1, 0]:
        raise ValueError("Fallout 3 CG01 stage 10 enabled-control mask differs")
    return commands


def _quest_objectives(quest: object) -> dict[int, str]:
    objectives: dict[int, str] = {}
    pending_index: int | None = None
    for subrecord in iter_subrecords(quest):
        if subrecord.signature == "QOBJ":
            if len(subrecord.data) != FORM_ID_BYTES or pending_index is not None:
                raise ValueError("Fallout 3 quest objective layout is unsupported")
            pending_index = struct.unpack("<I", subrecord.data)[0]
        elif subrecord.signature == "NNAM" and pending_index is not None:
            text = zstring(subrecord.data)
            if not text or pending_index in objectives:
                raise ValueError("Fallout 3 quest objective is absent or repeated")
            objectives[pending_index] = text
            pending_index = None
    if pending_index is not None:
        raise ValueError("Fallout 3 quest objective text is absent")
    return objectives


def _parse_cg01_stage12_commands(source: str) -> list[dict[str, object]]:
    commands = []
    for text in _source_commands(source):
        if match := SET_OBJECTIVE_COMPLETED_PATTERN.fullmatch(text):
            commands.append(
                {
                    "kind": "setObjectiveCompleted",
                    "questEditorId": match.group("quest"),
                    "index": int(match.group("index")),
                    "value": int(match.group("value")),
                }
            )
            continue
        if match := PLAYER_CONTROLS_PATTERN.fullmatch(text):
            if match.group("command").casefold() != "disableplayercontrols":
                raise ValueError(
                    f"Fallout 3 CG01 stage 12 uses an unsupported command: {text}"
                )
            commands.append(
                {
                    "kind": "disablePlayerControls",
                    "arguments": [int(value) for value in match.group("arguments").split()],
                }
            )
            continue
        if match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(text):
            raw_value = match.group("value")
            value: int | float = float(raw_value) if "." in raw_value else int(raw_value)
            commands.append(
                {
                    "kind": "setScriptVariable",
                    "subject": match.group("subject"),
                    "variable": match.group("variable"),
                    "value": value,
                }
            )
            continue
        raise ValueError(f"Fallout 3 CG01 stage 12 uses an unsupported command: {text}")
    expected = [
        "setObjectiveCompleted",
        "disablePlayerControls",
        "setScriptVariable",
        "setScriptVariable",
    ]
    if [str(command["kind"]) for command in commands] != expected:
        raise ValueError("Fallout 3 CG01 stage 12 command order differs")
    if commands[1]["arguments"] != [1, 1, 1, 1, 0, 0, 1]:
        raise ValueError("Fallout 3 CG01 stage 12 disabled-control mask differs")
    return commands


def _compile_cg01_walk_to_dad_transition(
    records: tuple[object, ...],
    quest: object,
    stage_sources: dict[int, list[str]],
    dad_reference: object,
    dad_script: object,
    source_stage: int,
) -> dict[str, object]:
    quest_editor_id = _editor_id(quest)
    dad_editor_id = _editor_id(dad_reference)
    if not quest_editor_id or not dad_editor_id:
        raise ValueError("Fallout 3 CG01 walk-to-Dad identities are absent")

    trigger_scripts = []
    for record in records:
        if record.signature != SCRIPT_RECORD:
            continue
        script_source = _script_source(record)
        block = CG01_PLAYER_TRIGGER_BLOCK_PATTERN.search(script_source)
        if block is None or IS_ACTION_REF_PLAYER_PATTERN.search(block.group("body")) is None:
            continue
        stage_done = GET_STAGE_DONE_PATTERN.search(block.group("body"))
        stage_effects = [
            match
            for command in _source_commands(block.group("body"))
            if (match := SET_STAGE_PATTERN.fullmatch(command)) is not None
        ]
        if (
            stage_done is None
            or stage_done.group("quest").casefold() != quest_editor_id.casefold()
            or len(stage_effects) != 1
            or stage_effects[0].group("quest").casefold() != quest_editor_id.casefold()
            or int(stage_done.group("stage")) != int(stage_effects[0].group("stage"))
        ):
            continue
        trigger_scripts.append(
            (record, script_source, int(stage_effects[0].group("stage")))
        )
    if len(trigger_scripts) != 1:
        raise ValueError("Fallout 3 CG01 walk-to-Dad trigger script is ambiguous")
    trigger_script, trigger_source, target_stage = trigger_scripts[0]
    if target_stage != CG01_WALK_TARGET_STAGE or target_stage <= source_stage:
        raise ValueError("Fallout 3 CG01 walk-to-Dad target stage differs")

    by_form = {record.form_id: record for record in records}
    activators = [
        record
        for record in records
        if record.signature == ACTIVATOR_RECORD
        and [
            struct.unpack("<I", subrecord.data)[0]
            for subrecord in iter_subrecords(record)
            if subrecord.signature == "SCRI" and len(subrecord.data) == FORM_ID_BYTES
        ]
        == [trigger_script.form_id]
    ]
    if len(activators) != 1:
        raise ValueError("Fallout 3 CG01 walk-to-Dad activator is ambiguous")
    activator = activators[0]
    trigger_references = [
        record
        for record in records
        if record.signature == PLACED_REFERENCE_RECORD
        and [
            struct.unpack("<I", subrecord.data)[0]
            for subrecord in iter_subrecords(record)
            if subrecord.signature == "NAME" and len(subrecord.data) == FORM_ID_BYTES
        ]
        == [activator.form_id]
    ]
    if len(trigger_references) != 1:
        raise ValueError("Fallout 3 CG01 walk-to-Dad reference is ambiguous")
    trigger_reference = trigger_references[0]
    parent_cell = cell_parent_form_id(trigger_reference)
    if parent_cell is None:
        raise ValueError("Fallout 3 CG01 walk-to-Dad reference has no CELL")

    primitive_data = _single_subrecord(trigger_reference, "XPRM")
    if len(primitive_data) != TRIGGER_PRIMITIVE_BYTES:
        raise ValueError("Fallout 3 CG01 walk-to-Dad primitive layout differs")
    primitive = struct.unpack(f"<{TRIGGER_PRIMITIVE_FLOATS}fI", primitive_data)
    if (
        not all(math.isfinite(value) and value > 0 for value in primitive[:3])
        or not all(
            math.isfinite(value)
            for value in primitive[
                TRIGGER_PRIMITIVE_COLOR_START:TRIGGER_PRIMITIVE_TYPE_INDEX
            ]
        )
        or primitive[TRIGGER_PRIMITIVE_TYPE_INDEX] != TRIGGER_PRIMITIVE_BOX_TYPE
    ):
        raise ValueError("Fallout 3 CG01 walk-to-Dad primitive differs")
    collision_layer_data = _single_subrecord(trigger_reference, "XTRI")
    if len(collision_layer_data) != FORM_ID_BYTES:
        raise ValueError("Fallout 3 CG01 walk-to-Dad collision layers differ")
    collision_layers = struct.unpack("<I", collision_layer_data)[0]

    objective_text = _quest_objectives(quest).get(CG01_WALK_OBJECTIVE_INDEX)
    if not objective_text:
        raise ValueError("Fallout 3 CG01 walk-to-Dad objective is absent")
    target_sources = stage_sources.get(target_stage, [])
    if len(target_sources) != 1:
        raise ValueError("Fallout 3 CG01 stage 12 result is ambiguous")
    target_source = target_sources[0]
    commands = _parse_cg01_stage12_commands(target_source)
    resolved_commands = []
    for index, command in enumerate(commands):
        kind = str(command["kind"])
        resolved: dict[str, object] = {"index": index, "kind": kind}
        if kind == "setObjectiveCompleted":
            if (
                str(command["questEditorId"]).casefold() != quest_editor_id.casefold()
                or int(command["index"]) != CG01_WALK_OBJECTIVE_INDEX
                or int(command["value"]) != 1
            ):
                raise ValueError("Fallout 3 CG01 stage 12 objective differs")
            resolved.update(
                {
                    "questFormId": _form_id(quest.form_id),
                    "questEditorId": quest_editor_id,
                    "objectiveIndex": CG01_WALK_OBJECTIVE_INDEX,
                    "completed": True,
                }
            )
        elif kind == "disablePlayerControls":
            resolved["arguments"] = list(command["arguments"])
        elif kind == "setScriptVariable":
            variable = str(command["variable"])
            value = command["value"]
            expected_value = 1 if variable.casefold() == "dotalk" else 0
            if (
                str(command["subject"]).casefold() != dad_editor_id.casefold()
                or variable.casefold() not in {"dotalk", "timer"}
                or value != expected_value
            ):
                raise ValueError("Fallout 3 CG01 stage 12 Dad variable differs")
            resolved.update(
                {
                    "referenceFormId": _form_id(dad_reference.form_id),
                    "referenceEditorId": dad_editor_id,
                    "scriptFormId": _form_id(dad_script.form_id),
                    "scriptEditorId": _editor_id(dad_script),
                    "variable": "doTalk" if variable.casefold() == "dotalk" else "timer",
                    "variableType": "short" if variable.casefold() == "dotalk" else "float",
                    "value": value,
                }
            )
        else:
            raise ValueError(f"Fallout 3 CG01 stage 12 command is unresolved: {kind}")
        resolved_commands.append(resolved)

    activator_editor_id = _editor_id(activator)
    script_editor_id = _editor_id(trigger_script)
    if not activator_editor_id or not script_editor_id:
        raise ValueError("Fallout 3 CG01 walk-to-Dad trigger identities are absent")
    return {
        "schema": "opennv-fo3-cg01-stage-10-to-12-trigger-transition/v1",
        "status": "source-backed-player-trigger-and-stage-result-runtime-unapplied",
        "sourceStage": source_stage,
        "targetStage": target_stage,
        "objective": {
            "questFormId": _form_id(quest.form_id),
            "questEditorId": quest_editor_id,
            "index": CG01_WALK_OBJECTIVE_INDEX,
            "text": objective_text,
            "textSha256": hashlib.sha256(objective_text.encode("utf-8")).hexdigest(),
        },
        "trigger": {
            "event": "onTriggerEnter",
            "actionReference": "player",
            "scriptFormId": _form_id(trigger_script.form_id),
            "scriptEditorId": script_editor_id,
            "scriptRecordSha256": hashlib.sha256(trigger_script.data).hexdigest(),
            "scriptSourceSha256": hashlib.sha256(
                trigger_source.encode("cp1252")
            ).hexdigest(),
            "activatorFormId": _form_id(activator.form_id),
            "activatorEditorId": activator_editor_id,
            "activatorRecordSha256": hashlib.sha256(activator.data).hexdigest(),
            "referenceFormId": _form_id(trigger_reference.form_id),
            "referenceRecordSha256": hashlib.sha256(trigger_reference.data).hexdigest(),
            "cellFormId": _form_id(parent_cell),
            "sourceTransform": _reference_transform_contract(trigger_reference),
            "collisionLayers": collision_layers,
            "primitive": {
                "shape": "box",
                "dimensionsGameUnits": list(primitive[:3]),
                "colorRgba": list(
                    primitive[
                        TRIGGER_PRIMITIVE_COLOR_START:TRIGGER_PRIMITIVE_TYPE_INDEX
                    ]
                ),
                "type": primitive[TRIGGER_PRIMITIVE_TYPE_INDEX],
            },
        },
        "stageResult": {
            "stageSourceSha256": hashlib.sha256(
                target_source.encode("cp1252")
            ).hexdigest(),
            "accountedCommandCount": len(resolved_commands),
            "commands": resolved_commands,
        },
        "nextBoundary": {
            "applied": False,
            "blocker": "awaiting-source-owned-dad-response-completion",
        },
    }


def _compile_cg01_stage12_dad_response(
    records: tuple[object, ...],
    definition: dict[str, object],
    quest: object,
    stage_sources: dict[int, list[str]],
    dad_reference: object,
    dad_base: object,
    dad_script: object,
    topic: object,
) -> dict[str, object]:
    source_stage = CG01_WALK_TARGET_STAGE
    target_stage = int(definition["stage12DadResponseTargetStage"])
    expected_forms = [
        int(str(value), FORM_ID_RADIX)
        for value in definition["stage12DadResponseInfoFormIds"]
    ]
    if len(expected_forms) != 2 or len(set(expected_forms)) != len(expected_forms):
        raise ValueError("Fallout 3 CG01 stage-12 Dad response selection differs")
    by_form = {record.form_id: record for record in records}
    voice_links = [
        struct.unpack("<I", subrecord.data)[0]
        for subrecord in iter_subrecords(dad_base)
        if subrecord.signature == "VTCK" and len(subrecord.data) == FORM_ID_BYTES
    ]
    if len(voice_links) != 1:
        raise ValueError("Fallout 3 CG01 stage-12 Dad voice type is ambiguous")
    voice = by_form.get(voice_links[0])
    if voice is None or voice.signature != VOICE_TYPE_RECORD:
        raise ValueError("Fallout 3 CG01 stage-12 Dad voice type is absent")
    infos = [by_form.get(form_id) for form_id in expected_forms]
    if any(info is None or info.signature != DIALOGUE_INFO_RECORD for info in infos):
        raise ValueError("Fallout 3 CG01 stage-12 Dad response INFO is absent")
    record_order = {record.form_id: index for index, record in enumerate(records)}
    if [record_order[info.form_id] for info in infos] != sorted(
        record_order[info.form_id] for info in infos
    ):
        raise ValueError("Fallout 3 CG01 stage-12 Dad response order differs")

    cues = []
    shared_idle_form_id: int | None = None
    for sequence, info in enumerate(infos):
        if not any(
            group.group_type == DIALOGUE_CHILD_GROUP_TYPE
            and group.label_u32 == topic.form_id
            for group in info.groups
        ) or struct.unpack("<I", _single_subrecord(info, "QSTI"))[0] != quest.form_id:
            raise ValueError("Fallout 3 CG01 stage-12 Dad response ownership differs")
        dialogue_data = _single_subrecord(info, "DATA")
        if len(dialogue_data) != DIALOGUE_INFO_DATA_BYTES:
            raise ValueError("Fallout 3 CG01 stage-12 Dad response flags differ")
        response_type, unused, flags = struct.unpack("<BBH", dialogue_data)
        if response_type != 1 or unused != 0 or flags != DIALOGUE_INFO_SAY_ONCE_FLAG:
            raise ValueError("Fallout 3 CG01 stage-12 Dad response is not exact say-once")
        conditions = [
            _dialogue_condition(subrecord.data)
            for subrecord in iter_subrecords(info)
            if subrecord.signature == "CTDA"
        ]
        by_function = {int(row["function"]): row for row in conditions}
        if len(conditions) != 2 or set(by_function) != {
            GET_IS_ID_FUNCTION,
            GET_STAGE_FUNCTION,
        }:
            raise ValueError("Fallout 3 CG01 stage-12 Dad response conditions differ")
        identity = by_function[GET_IS_ID_FUNCTION]
        stage = by_function[GET_STAGE_FUNCTION]
        if (
            identity["operatorFlags"] != 0
            or identity["comparisonValue"] != 1.0
            or identity["parameter1"] != dad_base.form_id
            or identity["parameter2"] != 0
            or identity["runOn"] != 0
            or identity["reference"] != 0
            or stage["operatorFlags"] != 0
            or stage["comparisonValue"] != float(source_stage)
            or stage["parameter1"] != quest.form_id
            or stage["parameter2"] != 0
            or stage["runOn"] != 0
            or stage["reference"] != 0
        ):
            raise ValueError("Fallout 3 CG01 stage-12 Dad response condition values differ")
        idle_links = [
            struct.unpack("<I", subrecord.data)[0]
            for subrecord in iter_subrecords(info)
            if subrecord.signature == "SNAM" and len(subrecord.data) == FORM_ID_BYTES
        ]
        if len(idle_links) != 1:
            raise ValueError("Fallout 3 CG01 stage-12 Dad response idle is ambiguous")
        if shared_idle_form_id is None:
            shared_idle_form_id = idle_links[0]
        elif shared_idle_form_id != idle_links[0]:
            raise ValueError("Fallout 3 CG01 stage-12 Dad response idle differs")
        idle = by_form.get(idle_links[0])
        idle_models = [] if idle is None else _text_values(idle, "MODL")
        if idle is None or idle.signature != IDLE_RECORD or len(idle_models) != 1:
            raise ValueError("Fallout 3 CG01 stage-12 Dad response idle is absent")

        source_rows = _text_values(info, "SCTX")
        commands = [command for source in source_rows for command in _source_commands(source)]
        effects = []
        if sequence == 0:
            if commands:
                raise ValueError("Fallout 3 CG01 first stage-12 Dad response has effects")
        else:
            if len(commands) != 1:
                raise ValueError("Fallout 3 CG01 final stage-12 Dad response result differs")
            stage_match = SET_STAGE_PATTERN.fullmatch(commands[0])
            if (
                stage_match is None
                or stage_match.group("quest").casefold()
                != (_editor_id(quest) or "").casefold()
                or int(stage_match.group("stage")) != target_stage
            ):
                raise ValueError("Fallout 3 CG01 stage-12 Dad response target differs")
            effects.append(
                {
                    "kind": "setStage",
                    "questFormId": _form_id(quest.form_id),
                    "questEditorId": _editor_id(quest),
                    "stage": target_stage,
                }
            )
        response_lines = [value for value in _text_values(info, "NAM1") if value]
        if len(response_lines) != 1:
            raise ValueError("Fallout 3 CG01 stage-12 Dad response text differs")
        response_text = response_lines[0]
        result_source = "\n".join(source_rows)
        cues.append(
            {
                "sequence": sequence,
                "infoFormId": _form_id(info.form_id),
                "recordSha256": hashlib.sha256(info.data).hexdigest(),
                "resultSourceSha256": hashlib.sha256(
                    result_source.encode("cp1252")
                ).hexdigest(),
                "sayOnce": True,
                "conditions": [
                    {
                        **row,
                        "parameter1": _form_id(int(row["parameter1"])),
                        "reference": _form_id(int(row["reference"])),
                    }
                    for row in conditions
                ],
                "effects": effects,
                "speakerIdle": {
                    "formId": _form_id(idle.form_id),
                    "editorId": _editor_id(idle),
                    "recordSha256": hashlib.sha256(idle.data).hexdigest(),
                    "modelPath": canonical_member_path(f"meshes\\{idle_models[0]}"),
                },
                "response": {
                    "index": 1,
                    "text": response_text,
                    "textSha256": hashlib.sha256(
                        response_text.encode("utf-8")
                    ).hexdigest(),
                },
            }
        )

    dad_script_source = _script_source(dad_script)
    completion = re.search(
        rf"\bbegin\s+SayToDone\s+{re.escape(_editor_id(topic) or '')}\b"
        r"(?P<body>.*?)\bend\b",
        dad_script_source,
        re.IGNORECASE | re.DOTALL,
    )
    if completion is None:
        raise ValueError("Fallout 3 CG01 Dad SayToDone block is absent")
    completion_commands = _source_commands(completion.group("body"))
    if [command.casefold() for command in completion_commands[:2]] != [
        "set talking to 0",
        "look player",
    ]:
        raise ValueError("Fallout 3 CG01 Dad SayToDone completion differs")

    target_sources = stage_sources.get(target_stage, [])
    if len(target_sources) != 1:
        raise ValueError("Fallout 3 CG01 stage-14 result is ambiguous")
    target_commands = _source_commands(target_sources[0])
    if len(target_commands) != 1:
        raise ValueError("Fallout 3 CG01 stage-14 command count differs")
    package_match = REFERENCE_COMMAND_PATTERN.fullmatch(target_commands[0])
    if (
        package_match is None
        or package_match.group("subject").casefold()
        != (_editor_id(dad_reference) or "").casefold()
        or package_match.group("command").casefold() != "evp"
    ):
        raise ValueError("Fallout 3 CG01 stage-14 package command differs")
    return {
        "schema": "opennv-fo3-cg01-stage-12-to-14-dad-response/v1",
        "status": "source-backed-say-once-dad-response-runtime-unapplied",
        "sourceStage": source_stage,
        "targetStage": target_stage,
        "topicFormId": _form_id(topic.form_id),
        "topicEditorId": _editor_id(topic),
        "dadReferenceFormId": _form_id(dad_reference.form_id),
        "dadScriptFormId": _form_id(dad_script.form_id),
        "dadScriptSourceSha256": hashlib.sha256(
            dad_script_source.encode("cp1252")
        ).hexdigest(),
        "sayToDone": {
            "talking": 0,
            "lookAt": "player",
            "conditionalStageSource": CG01_DAD_COMPLETION_CONDITIONAL_SOURCE_STAGE,
            "conditionalStageTarget": CG01_DAD_COMPLETION_CONDITIONAL_TARGET_STAGE,
        },
        "dialogue": {
            "topic": {
                "formId": _form_id(topic.form_id),
                "editorId": _editor_id(topic),
                "recordSha256": hashlib.sha256(topic.data).hexdigest(),
                "questFormId": _form_id(quest.form_id),
            },
            "voiceType": {
                "formId": _form_id(voice.form_id),
                "editorId": _editor_id(voice),
                "recordSha256": hashlib.sha256(voice.data).hexdigest(),
            },
            "branches": cues,
            "dialoguePlaybackPrepared": False,
            "dialoguePlaybackImplemented": False,
        },
        "stageResult": {
            "stageSourceSha256": hashlib.sha256(
                target_sources[0].encode("cp1252")
            ).hexdigest(),
            "accountedCommandCount": 1,
            "commands": [
                {
                    "index": 0,
                    "kind": "evaluatePackage",
                    "referenceFormId": _form_id(dad_reference.form_id),
                    "referenceEditorId": _editor_id(dad_reference),
                }
            ],
        },
        "nextBoundary": {
            "applied": False,
            "blocker": "awaiting-source-owned-post-stage-14-package-completion",
        },
    }


def _compile_cg02_intro_runtime(
    records: tuple[object, ...],
    quest: object,
    quest_script: object,
    stage_sources: dict[int, list[str]],
    actor_recipe_ids: list[str],
) -> dict[str, object]:
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)
    source = _script_source(quest_script)
    timer_match = re.search(
        r"if\s+runTimer\s*==\s*1\s*"
        r"if\s+timer\s*>\s*0\s*"
        r"set\s+timer\s+to\s+timer\s*-\s*GetSecondsPassed\s*"
        r"else\s*.*?if\s+intro\s*>\s*0\s*"
        r"if\s+intro\s*==\s*1(?P<body>.*?)elseif\s+intro\s*==\s*2",
        source,
        re.IGNORECASE | re.DOTALL,
    )
    if timer_match is None:
        raise ValueError("Fallout 3 CG02 intro timer block differs")
    body_commands = _source_commands(timer_match.group("body"))
    say_pattern = re.compile(
        r"(?P<subject>\w+)\.SayTo\s+player\s+(?P<topic>\w+)(?:\s+.*)?",
        re.IGNORECASE,
    )
    sound_pattern = re.compile(r"playSound\s+(?P<sound>\w+)", re.IGNORECASE)
    say_rows = []
    sound_rows = []
    for command in body_commands:
        say = say_pattern.fullmatch(command)
        sound = sound_pattern.fullmatch(command)
        if say is not None:
            say_rows.append((say.group("subject"), say.group("topic")))
        elif sound is not None:
            sound_rows.append(sound.group("sound"))
        elif command.casefold() != "set runtimer to 0":
            raise ValueError(f"Fallout 3 CG02 intro command differs: {command}")
    if len(say_rows) != CG02_INTRO_SAY_TO_COUNT or len(sound_rows) != 2 or len({topic.casefold() for _, topic in say_rows}) != 1:
        raise ValueError("Fallout 3 CG02 intro SayTo/sound chain differs")
    topic_matches = by_editor.get(say_rows[0][1].casefold(), [])
    if len(topic_matches) != 1 or topic_matches[0].signature != DIALOGUE_TOPIC_RECORD:
        raise ValueError("Fallout 3 CG02 intro topic differs")
    topic = topic_matches[0]
    infos = sorted(
        (
            record for record in records
            if record.signature == DIALOGUE_INFO_RECORD
            and any(group.group_type == DIALOGUE_CHILD_GROUP_TYPE and group.label_u32 == topic.form_id for group in record.groups)
        ),
        key=lambda record: record.form_id,
    )
    recipes = [actor_preparation.load_recipe(recipe_id) for recipe_id in actor_recipe_ids]
    recipe_by_reference = {
        str(recipe["proofActorReferenceFormId"]).casefold(): recipe for recipe in recipes
    }
    if len(recipe_by_reference) != CG02_INTRO_ACTOR_RECIPE_COUNT:
        raise ValueError("Fallout 3 CG02 intro actor recipe count differs")
    participants = []
    common_info_id = None
    dad_info_id = None
    for sequence, (reference_editor_id, _) in enumerate(say_rows):
        reference_matches = [
            row for row in by_editor.get(reference_editor_id.casefold(), [])
            if row.signature == ACTOR_REFERENCE_RECORD
        ]
        if len(reference_matches) != 1:
            raise ValueError(f"Fallout 3 CG02 intro reference differs: {reference_editor_id}")
        reference = reference_matches[0]
        base_id = struct.unpack("<I", _single_subrecord(reference, "NAME"))[0]
        base = by_form.get(base_id)
        if base is None or base.signature != NPC_RECORD:
            raise ValueError(f"Fallout 3 CG02 intro base differs: {reference_editor_id}")
        recipe = recipe_by_reference.get(_form_id(reference.form_id))
        if recipe is None or str(recipe.get("expectedBaseFormId", "")).casefold() != _form_id(base.form_id):
            raise ValueError(f"Fallout 3 CG02 intro actor recipe differs: {reference_editor_id}")
        matching_infos = []
        for info in infos:
            identities = [
                int(_dialogue_condition(row.data)["parameter1"])
                for row in iter_subrecords(info)
                if row.signature == "CTDA" and int(_dialogue_condition(row.data)["function"]) == GET_IS_ID_FUNCTION
            ]
            if base.form_id in identities:
                matching_infos.append(info)
        if len(matching_infos) != 1:
            raise ValueError(f"Fallout 3 CG02 intro INFO differs: {reference_editor_id}")
        info = matching_infos[0]
        effects = []
        result_sources = _text_values(info, "SCTX")
        result_commands = [command for row in result_sources for command in _source_commands(row)]
        if reference_editor_id.casefold() == "cg02dadref":
            if [row.casefold() for row in result_commands] != [
                "set cg02.intro to 2", "set cg02.runtimer to 1"
            ]:
                raise ValueError("Fallout 3 CG02 Dad intro result differs")
            effects = [
                {"kind": "setQuestVariable", "variable": "intro", "value": 2},
                {"kind": "setQuestVariable", "variable": "runTimer", "value": 1},
            ]
            dad_info_id = info.form_id
        else:
            if result_commands:
                raise ValueError("Fallout 3 CG02 non-Dad intro result differs")
            common_info_id = info.form_id if common_info_id is None else common_info_id
            if common_info_id != info.form_id:
                raise ValueError("Fallout 3 CG02 shared intro INFO differs")
        response_lines = [value for value in _text_values(info, "NAM1") if value]
        idle_ids = [
            struct.unpack("<I", row.data)[0] for row in iter_subrecords(info)
            if row.signature == "SNAM" and len(row.data) == FORM_ID_BYTES
        ]
        voice_ids = [
            struct.unpack("<I", row.data)[0] for row in iter_subrecords(base)
            if row.signature == "VTCK" and len(row.data) == FORM_ID_BYTES
        ]
        if len(response_lines) != 1 or len(idle_ids) != 1 or len(voice_ids) != 1:
            raise ValueError(f"Fallout 3 CG02 intro response differs: {reference_editor_id}")
        idle = by_form.get(idle_ids[0])
        voice = by_form.get(voice_ids[0])
        idle_models = [] if idle is None else _text_values(idle, "MODL")
        if idle is None or idle.signature != IDLE_RECORD or len(idle_models) != 1 or voice is None or voice.signature != VOICE_TYPE_RECORD:
            raise ValueError(f"Fallout 3 CG02 intro animation/voice differs: {reference_editor_id}")
        participants.append({
            "phase": 0,
            "sequenceInPhase": sequence,
            "sequence": sequence,
            "referenceFormId": _form_id(reference.form_id),
            "referenceEditorId": reference_editor_id,
            "referenceRecordSha256": hashlib.sha256(reference.data).hexdigest(),
            "baseFormId": _form_id(base.form_id),
            "baseRecordSha256": hashlib.sha256(base.data).hexdigest(),
            "actorRecipeId": str(recipe["id"]),
            "infoFormId": _form_id(info.form_id),
            "infoRecordSha256": hashlib.sha256(info.data).hexdigest(),
            "response": {"index": 1, "text": response_lines[0], "textSha256": hashlib.sha256(response_lines[0].encode("utf-8")).hexdigest()},
            "speakerIdle": {"formId": _form_id(idle.form_id), "editorId": _editor_id(idle), "recordSha256": hashlib.sha256(idle.data).hexdigest(), "modelPath": canonical_member_path(f"meshes\\{idle_models[0]}")},
            "voiceType": {"formId": _form_id(voice.form_id), "editorId": _editor_id(voice), "recordSha256": hashlib.sha256(voice.data).hexdigest()},
            "effects": effects,
        })
    def compile_additional_cue(
        reference_editor_id: str,
        topic_editor_id: str,
        phase: int,
        sequence_in_phase: int,
        engine_sex: str | None = None,
    ) -> dict[str, object]:
        references = [
            row for row in by_editor.get(reference_editor_id.casefold(), [])
            if row.signature == ACTOR_REFERENCE_RECORD
        ]
        topics = [
            row for row in by_editor.get(topic_editor_id.casefold(), [])
            if row.signature == DIALOGUE_TOPIC_RECORD
        ]
        if len(references) != 1 or len(topics) != 1:
            raise ValueError("Fallout 3 CG02 continuation identity differs")
        reference = references[0]
        topic_row = topics[0]
        base_id = struct.unpack("<I", _single_subrecord(reference, "NAME"))[0]
        base = by_form.get(base_id)
        recipe = recipe_by_reference.get(_form_id(reference.form_id))
        if base is None or base.signature != NPC_RECORD or recipe is None:
            raise ValueError("Fallout 3 CG02 continuation actor differs")
        candidates = []
        for info in records:
            if info.signature != DIALOGUE_INFO_RECORD or not any(
                group.group_type == DIALOGUE_CHILD_GROUP_TYPE
                and group.label_u32 == topic_row.form_id
                for group in info.groups
            ):
                continue
            identities = [
                int(_dialogue_condition(row.data)["parameter1"])
                for row in iter_subrecords(info)
                if row.signature == "CTDA"
                and int(_dialogue_condition(row.data)["function"]) == GET_IS_ID_FUNCTION
            ]
            result_commands = [
                command for row in _text_values(info, "SCTX")
                for command in _source_commands(row)
            ]
            effect_carrier = (
                phase == 1 and reference_editor_id.casefold() == "cg02vault101security04ref"
                or phase == 2 and reference_editor_id.casefold() == "cg02dadref"
                or phase == 3 and reference_editor_id.casefold() == "cg02oldladypalmerref"
            )
            has_intro_effect = any(".intro" in command.casefold() for command in result_commands)
            sex_conditions = [
                int(_dialogue_condition(row.data)["parameter1"])
                for row in iter_subrecords(info)
                if row.signature == "CTDA"
                and int(_dialogue_condition(row.data)["function"]) == GET_PC_IS_SEX_FUNCTION
            ]
            sex_matches = engine_sex is None and not sex_conditions or (
                engine_sex == "male" and sex_conditions == [0]
            ) or (engine_sex == "female" and sex_conditions == [1])
            if base.form_id in identities and has_intro_effect == effect_carrier and sex_matches:
                candidates.append(info)
        if len(candidates) != 1:
            raise ValueError(
                f"Fallout 3 CG02 continuation INFO differs: {reference_editor_id}"
            )
        info = candidates[0]
        response_lines = [value for value in _text_values(info, "NAM1") if value]
        idle_ids = [
            struct.unpack("<I", row.data)[0] for row in iter_subrecords(info)
            if row.signature == "SNAM" and len(row.data) == FORM_ID_BYTES
        ]
        voice_ids = [
            struct.unpack("<I", row.data)[0] for row in iter_subrecords(base)
            if row.signature == "VTCK" and len(row.data) == FORM_ID_BYTES
        ]
        if len(response_lines) != 1 or len(idle_ids) > 1 or len(voice_ids) != 1:
            raise ValueError("Fallout 3 CG02 continuation response differs")
        idle = None if not idle_ids else by_form.get(idle_ids[0])
        voice = by_form.get(voice_ids[0])
        idle_models = [] if idle is None else _text_values(idle, "MODL")
        if (idle is not None and (idle.signature != IDLE_RECORD or len(idle_models) != 1)) or voice is None or voice.signature != VOICE_TYPE_RECORD:
            raise ValueError("Fallout 3 CG02 continuation voice/idle differs")
        effects = []
        for command in [
            command for row in _text_values(info, "SCTX")
            for command in _source_commands(row)
        ]:
            match = SET_REFERENCE_VARIABLE_PATTERN.fullmatch(command)
            if match is not None and match.group("subject").casefold() == "cg02":
                effects.append({"kind": "setQuestVariable", "variable": match.group("variable"), "value": int(float(match.group("value")))})
            elif command.casefold() == "setav variable01 1":
                effects.append({"kind": "setActorValue", "actorValue": "variable01", "value": 1})
            elif command.casefold() == "evp":
                effects.append({"kind": "evaluatePackage"})
            else:
                raise ValueError(f"Fallout 3 CG02 continuation result differs: {command}")
        return {
            "phase": phase,
            "sequenceInPhase": sequence_in_phase,
            "sequence": len(participants),
            "engineSex": engine_sex,
            "referenceFormId": _form_id(reference.form_id),
            "referenceEditorId": reference_editor_id,
            "referenceRecordSha256": hashlib.sha256(reference.data).hexdigest(),
            "baseFormId": _form_id(base.form_id),
            "baseRecordSha256": hashlib.sha256(base.data).hexdigest(),
            "actorRecipeId": str(recipe["id"]),
            "topicFormId": _form_id(topic_row.form_id),
            "topicEditorId": _editor_id(topic_row),
            "infoFormId": _form_id(info.form_id),
            "infoRecordSha256": hashlib.sha256(info.data).hexdigest(),
            "response": {"index": 1, "text": response_lines[0], "textSha256": hashlib.sha256(response_lines[0].encode("utf-8")).hexdigest()},
            "speakerIdle": None if idle is None else {"formId": _form_id(idle.form_id), "editorId": _editor_id(idle), "recordSha256": hashlib.sha256(idle.data).hexdigest(), "modelPath": canonical_member_path(f"meshes\\{idle_models[0]}")},
            "voiceType": {"formId": _form_id(voice.form_id), "editorId": _editor_id(voice), "recordSha256": hashlib.sha256(voice.data).hexdigest()},
            "effects": effects,
        }

    continuation_specs = (
        ("CG02Vault101Security04REF", "CG02OfficerGomezSpeech", 1, 0),
        ("CG02AmataREF", "CG02IntroYell", 2, 0),
        ("CG02BeatriceREF", "CG02IntroYell", 2, 1),
        ("CG02DadREF", "CG02IntroYell", 2, 2),
        ("CG02Vault101Security04REF", "CG02IntroYell", 2, 3),
        ("CG02StanleyREF", "CG02IntroYell", 2, 4),
        ("CG02PaulHannonREF", "CG02IntroYell", 2, CG02_INTRO_SAY_TO_COUNT - 1),
        ("CG02OldLadyPalmerREF", "CG02OldLadyPalmerSpeech", 3, 0, "male"),
        ("CG02OldLadyPalmerREF", "CG02OldLadyPalmerSpeech", 3, 0, "female"),
    )
    for spec in continuation_specs:
        participants.append(compile_additional_cue(*spec))
    expected_phase_effects = {
        0: {"intro": 2, "runTimer": 1},
        1: {"intro": 3, "runTimer": 1},
        2: {"intro": 4, "runTimer": 1},
        3: {"intro": CG02_INTRO_FINAL_TIMER_PHASE, "runTimer": 1},
    }
    for phase, expected in expected_phase_effects.items():
        actual = {
            str(effect["variable"]): int(effect["value"])
            for row in participants if int(row["phase"]) == phase
            for effect in row["effects"]
            if effect["kind"] == "setQuestVariable"
        }
        if actual != expected:
            raise ValueError(f"Fallout 3 CG02 phase {phase} results differ")
    final_match = re.search(
        r"elseif\s+intro\s*==\s*5(?P<body>.*?)endif",
        source,
        re.IGNORECASE | re.DOTALL,
    )
    final_source_commands = [] if final_match is None else _source_commands(
        final_match.group("body")
    )
    if [command.casefold() for command in final_source_commands] != [
        "setstage cg02 6", "set intro to 0", "set runtimer to 0"
    ]:
        raise ValueError("Fallout 3 CG02 intro completion source differs")

    sounds = []
    for sequence, sound_editor_id in enumerate(sound_rows):
        matches = by_editor.get(sound_editor_id.casefold(), [])
        if len(matches) != 1 or matches[0].signature != "SOUN":
            raise ValueError(f"Fallout 3 CG02 intro sound differs: {sound_editor_id}")
        sound = matches[0]
        paths = _text_values(sound, "FNAM")
        if len(paths) != 1:
            raise ValueError(f"Fallout 3 CG02 intro sound path differs: {sound_editor_id}")
        sounds.append({"phase": 0, "sequence": sequence, "formId": _form_id(sound.form_id), "editorId": _editor_id(sound), "recordSha256": hashlib.sha256(sound.data).hexdigest(), "logicalPath": canonical_member_path(f"sound\\{paths[0]}")})
    clapping = next(row for row in sounds if str(row["editorId"]).casefold() == "qstpartyclapping2d")
    sounds.append({**clapping, "phase": 2, "sequence": 0})
    stage6 = stage_sources.get(CG02_INTRO_TARGET_STAGE, [])
    if len(stage6) != 2:
        raise ValueError("Fallout 3 CG02 stage-6 result rows differ")
    stage6_commands = [
        command for row in stage6 for command in _source_commands(row)
    ]
    resolved_stage6 = []
    for index, command in enumerate(stage6_commands):
        if match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(command):
            references = by_editor.get(match.group("subject").casefold(), [])
            if len(references) != 1 or references[0].signature != ACTOR_REFERENCE_RECORD:
                raise ValueError("Fallout 3 CG02 stage-6 Dad result differs")
            resolved_stage6.append({"index": index, "kind": "setActorVariable", "referenceFormId": _form_id(references[0].form_id), "referenceEditorId": _editor_id(references[0]), "variable": match.group("variable"), "value": int(float(match.group("value")))})
        elif match := SET_OPEN_STATE_PATTERN.fullmatch(command):
            references = by_editor.get(match.group("subject").casefold(), [])
            if len(references) != 1 or references[0].signature != PLACED_REFERENCE_RECORD:
                raise ValueError("Fallout 3 CG02 stage-6 door result differs")
            resolved_stage6.append({"index": index, "kind": "setOpenState", "referenceFormId": _form_id(references[0].form_id), "referenceEditorId": _editor_id(references[0]), "value": int(match.group("value"))})
        elif match := LOOK_AT_PATTERN.fullmatch(command):
            references = by_editor.get(match.group("subject").casefold(), [])
            if len(references) != 1 or references[0].signature != ACTOR_REFERENCE_RECORD or match.group("target").casefold() != "player":
                raise ValueError("Fallout 3 CG02 stage-6 look result differs")
            resolved_stage6.append({"index": index, "kind": "lookAt", "referenceFormId": _form_id(references[0].form_id), "referenceEditorId": _editor_id(references[0]), "target": "player"})
        else:
            raise ValueError(f"Fallout 3 CG02 stage-6 result differs: {command}")
    return {
        "schema": "opennv-fo3-cg02-stage-5-intro-runtime/v1",
        "sourceStage": CG02_INTRO_SOURCE_STAGE,
        "targetStage": CG02_INTRO_TARGET_STAGE,
        "timer": {"decrementSource": "GetSecondsPassed", "initialVariable": "timer", "runVariable": "runTimer", "requiredIntro": 1},
        "topicFormId": _form_id(topic.form_id),
        "topicEditorId": _editor_id(topic),
        "participants": participants,
        "sounds": sounds,
        "stage6SourceSha256": hashlib.sha256("\n".join(stage6).encode("cp1252")).hexdigest(),
        "stage6Commands": resolved_stage6,
        "finalCommands": [
            {"index": 0, "kind": "setStage", "questFormId": _form_id(quest.form_id), "questEditorId": _editor_id(quest), "stage": CG02_INTRO_TARGET_STAGE},
            {"index": 1, "kind": "setQuestVariable", "variable": "intro", "value": 0},
            {"index": 2, "kind": "setQuestVariable", "variable": "runTimer", "value": 0},
        ],
        "nextBoundary": {"applied": False, "blocker": "fo3-cg02-stage-6-dad-speech-runtime-not-implemented"},
    }


def _compile_cg02_dad_speech_runtime(
    records: tuple[object, ...],
    quest: object,
    stage_sources: dict[int, list[str]],
    definition: dict[str, object],
) -> dict[str, object]:
    config = dict(definition["cg02DadSpeech"])
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    def exact_form(name: str, signature: str) -> object:
        record = by_form.get(int(str(config[name]), FORM_ID_RADIX))
        if record is None or record.signature != signature:
            raise ValueError(f"Fallout 3 CG02 Dad speech {name} differs")
        return record

    dad_reference = exact_form("dadReferenceFormId", ACTOR_REFERENCE_RECORD)
    dad_base = exact_form("dadBaseFormId", NPC_RECORD)
    dad_script = exact_form("dadScriptFormId", SCRIPT_RECORD)
    topic = exact_form("topicFormId", DIALOGUE_TOPIC_RECORD)
    if (
        _editor_id(topic).casefold() != str(config["topicEditorId"]).casefold()
        or struct.unpack("<I", _single_subrecord(dad_reference, "NAME"))[0]
        != dad_base.form_id
        or struct.unpack("<I", _single_subrecord(dad_base, "SCRI"))[0]
        != dad_script.form_id
    ):
        raise ValueError("Fallout 3 CG02 Dad speech identity differs")
    script_source = _script_source(dad_script)
    if not re.search(
        r"if\s+doTalk\s*==\s*1\s*&&\s*talking\s*==\s*0.*?"
        r"set\s+timer\s+to\s+timer\s*-\s*GetSecondsPassed.*?"
        r"SayTo\s+player\s+CG02DadSpeech\s+1.*?set\s+talking\s+to\s+1.*?"
        r"begin\s+SayToDone\s+CG02DadSpeech\s*set\s+talking\s+to\s+0",
        script_source,
        re.IGNORECASE | re.DOTALL,
    ):
        raise ValueError("Fallout 3 CG02 Dad speech script differs")
    voice_ids = [
        struct.unpack("<I", row.data)[0]
        for row in iter_subrecords(dad_base)
        if row.signature == "VTCK" and len(row.data) == FORM_ID_BYTES
    ]
    voice = by_form.get(voice_ids[0]) if len(voice_ids) == 1 else None
    if voice is None or voice.signature != VOICE_TYPE_RECORD:
        raise ValueError("Fallout 3 CG02 Dad voice differs")

    configured_ids = [int(str(value), FORM_ID_RADIX) for value in config["infoFormIds"]]
    branches = []
    for sequence, info_id in enumerate(configured_ids):
        info = by_form.get(info_id)
        if info is None or info.signature != DIALOGUE_INFO_RECORD or not any(
            group.group_type == DIALOGUE_CHILD_GROUP_TYPE
            and group.label_u32 == topic.form_id
            for group in info.groups
        ):
            raise ValueError("Fallout 3 CG02 Dad INFO identity differs")
        data_rows = [row.data for row in iter_subrecords(info) if row.signature == "DATA"]
        responses = [value for value in _text_values(info, "NAM1") if value]
        idle_ids = [
            struct.unpack("<I", row.data)[0]
            for row in iter_subrecords(info)
            if row.signature == "SNAM" and len(row.data) == FORM_ID_BYTES
        ]
        idle = by_form.get(idle_ids[0]) if len(idle_ids) == 1 else None
        idle_models = [] if idle is None else _text_values(idle, "MODL")
        if (
            len(data_rows) != 1
            or len(data_rows[0]) != CG02_DIALOGUE_INFO_DATA_BYTES
            or data_rows[0][0] != CG02_DIALOGUE_INFO_RESPONSE_TYPE
            or struct.unpack("<H", data_rows[0][2:])[0] !=
                CG02_DIALOGUE_INFO_SAY_ONCE_FLAG
            or len(responses) != 1
            or idle is None
            or idle.signature != IDLE_RECORD
            or len(idle_models) != 1
        ):
            raise ValueError("Fallout 3 CG02 Dad response differs")
        conditions = [
            _dialogue_condition(row.data)
            for row in iter_subrecords(info)
            if row.signature == "CTDA"
        ]
        if not any(
            int(condition["function"]) == GET_IS_ID_FUNCTION
            and int(condition["parameter1"]) == dad_base.form_id
            for condition in conditions
        ):
            raise ValueError("Fallout 3 CG02 Dad response speaker differs")
        sex_values = [
            int(condition["parameter1"])
            for condition in conditions
            if int(condition["function"]) == GET_PC_IS_SEX_FUNCTION
        ]
        engine_sex = (
            None if not sex_values
            else "male" if sex_values == [0]
            else "female" if sex_values == [1]
            else "unsupported"
        )
        if engine_sex == "unsupported":
            raise ValueError("Fallout 3 CG02 Dad response sex differs")
        commands = [
            command
            for source in _text_values(info, "SCTX")
            for command in _source_commands(source)
        ]
        effects = []
        for command in commands:
            match = SET_STAGE_PATTERN.fullmatch(command)
            if (
                match is None
                or match.group("quest").casefold() != _editor_id(quest).casefold()
            ):
                raise ValueError("Fallout 3 CG02 Dad result differs")
            effects.append({
                "kind": "setStage",
                "questFormId": _form_id(quest.form_id),
                "questEditorId": _editor_id(quest),
                "stage": int(match.group("stage")),
            })
        branches.append({
            "sequence": 0 if engine_sex is not None else 1,
            "engineSex": engine_sex,
            "infoFormId": _form_id(info.form_id),
            "infoRecordSha256": hashlib.sha256(info.data).hexdigest(),
            "sayOnce": True,
            "response": {
                "index": 1,
                "text": responses[0],
                "textSha256": hashlib.sha256(responses[0].encode("utf-8")).hexdigest(),
            },
            "speakerIdle": {
                "formId": _form_id(idle.form_id),
                "editorId": _editor_id(idle),
                "recordSha256": hashlib.sha256(idle.data).hexdigest(),
                "modelPath": canonical_member_path(f"meshes\\{idle_models[0]}"),
            },
            "effects": effects,
        })
    if [row["sequence"] for row in branches] != [0, 0, 1] or {
        row["engineSex"] for row in branches[:2]
    } != {"male", "female"} or branches[2]["engineSex"] is not None:
        raise ValueError("Fallout 3 CG02 Dad speech branch order differs")

    target_stage = int(config["targetStage"])
    if branches[2]["effects"] != [{
        "kind": "setStage",
        "questFormId": _form_id(quest.form_id),
        "questEditorId": _editor_id(quest),
        "stage": target_stage,
    }]:
        raise ValueError("Fallout 3 CG02 Dad speech target differs")
    stage_commands = []
    for index, command in enumerate(
        command for source in stage_sources.get(target_stage, [])
        for command in _source_commands(source)
    ):
        if match := re.fullmatch(
            r"set\s+(?P<subject>\w+)\.(?P<variable>\w+)\s+to\s+"
            r"(?P<value>[+-]?(?:\d+(?:\.\d*)?|\.\d+))",
            command,
            re.IGNORECASE,
        ):
            references = by_editor.get(match.group("subject").casefold(), [])
            if len(references) != 1 or references[0].signature != ACTOR_REFERENCE_RECORD:
                raise ValueError("Fallout 3 CG02 stage-7 actor differs")
            stage_commands.append({
                "index": index,
                "kind": "setActorVariable",
                "referenceFormId": _form_id(references[0].form_id),
                "referenceEditorId": _editor_id(references[0]),
                "variable": match.group("variable"),
                "value": float(match.group("value")),
            })
        elif match := re.fullmatch(r"(?P<subject>\w+)\.evp", command, re.IGNORECASE):
            references = by_editor.get(match.group("subject").casefold(), [])
            if len(references) != 1 or references[0].signature != ACTOR_REFERENCE_RECORD:
                raise ValueError("Fallout 3 CG02 stage-7 package actor differs")
            stage_commands.append({
                "index": index,
                "kind": "evaluatePackage",
                "referenceFormId": _form_id(references[0].form_id),
                "referenceEditorId": _editor_id(references[0]),
            })
        else:
            raise ValueError(f"Fallout 3 CG02 stage-7 command differs: {command}")
    if [row["kind"] for row in stage_commands] != [
        "setActorVariable", "evaluatePackage", "setActorVariable", "setActorVariable"
    ]:
        raise ValueError("Fallout 3 CG02 stage-7 result order differs")
    return {
        "schema": "opennv-fo3-cg02-stage-6-dad-speech-runtime/v1",
        "sourceStage": CG02_INTRO_TARGET_STAGE,
        "targetStage": target_stage,
        "dadReferenceFormId": _form_id(dad_reference.form_id),
        "dadBaseFormId": _form_id(dad_base.form_id),
        "dadScriptFormId": _form_id(dad_script.form_id),
        "dadScriptEditorId": _editor_id(dad_script),
        "dadScriptSourceSha256": hashlib.sha256(script_source.encode("cp1252")).hexdigest(),
        "topicFormId": _form_id(topic.form_id),
        "topicEditorId": _editor_id(topic),
        "dialogue": {
            "voiceType": {
                "formId": _form_id(voice.form_id),
                "editorId": _editor_id(voice),
                "recordSha256": hashlib.sha256(voice.data).hexdigest(),
            },
            "branches": branches,
            "dialoguePlaybackPrepared": False,
            "dialoguePlaybackImplemented": False,
        },
        "stageResult": {
            "sourceSha256": hashlib.sha256(
                "\n".join(stage_sources[target_stage]).encode("cp1252")
            ).hexdigest(),
            "commands": stage_commands,
        },
        "nextBoundary": {
            "applied": False,
            "blocker": "fo3-cg02-stage-7-overseer-speech-runtime-not-implemented",
        },
    }


def _compile_cg02_overseer_speech_runtime(
    records: tuple[object, ...],
    quest: object,
    stage_sources: dict[int, list[str]],
    definition: dict[str, object],
) -> dict[str, object]:
    config = dict(definition["cg02OverseerSpeech"])
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    def exact(name: str, signature: str) -> object:
        record = by_form.get(int(str(config[name]), FORM_ID_RADIX))
        if record is None or record.signature != signature:
            raise ValueError(f"Fallout 3 CG02 Overseer {name} differs")
        return record

    reference = exact("overseerReferenceFormId", ACTOR_REFERENCE_RECORD)
    base = exact("overseerBaseFormId", NPC_RECORD)
    script = exact("overseerScriptFormId", SCRIPT_RECORD)
    topic = exact("topicFormId", DIALOGUE_TOPIC_RECORD)
    if (
        struct.unpack("<I", _single_subrecord(reference, "NAME"))[0] != base.form_id
        or struct.unpack("<I", _single_subrecord(base, "SCRI"))[0] != script.form_id
        or _editor_id(topic).casefold() != str(config["topicEditorId"]).casefold()
    ):
        raise ValueError("Fallout 3 CG02 Overseer identity differs")
    script_source = _script_source(script)
    if not re.search(
        r"if\s+doTalk\s*==\s*1\s*&&\s*talking\s*==\s*0.*?"
        r"set\s+timer\s+to\s+timer\s*-\s*GetSecondsPassed.*?"
        r"SayTo\s+player\s+CG02OverseerSpeech\s+1.*?"
        r"begin\s+SayToDone\s+CG02OverseerSpeech\s*set\s+talking\s+to\s+0",
        script_source,
        re.IGNORECASE | re.DOTALL,
    ):
        raise ValueError("Fallout 3 CG02 Overseer script differs")
    voice_ids = [
        struct.unpack("<I", row.data)[0] for row in iter_subrecords(base)
        if row.signature == "VTCK" and len(row.data) == FORM_ID_BYTES
    ]
    voice = by_form.get(voice_ids[0]) if len(voice_ids) == 1 else None
    if voice is None or voice.signature != VOICE_TYPE_RECORD:
        raise ValueError("Fallout 3 CG02 Overseer voice differs")
    player_reference_form_id = _form_id(
        int(str(config["playerReferenceFormId"]), FORM_ID_RADIX)
    )

    def resolve_command(command: str, index: int) -> dict[str, object]:
        if match := SET_STAGE_PATTERN.fullmatch(command):
            if match.group("quest").casefold() != _editor_id(quest).casefold():
                raise ValueError("Fallout 3 CG02 Overseer stage quest differs")
            return {"index": index, "kind": "setStage", "stage": int(match.group("stage"))}
        if match := re.fullmatch(
            r"set\s+(?P<subject>\w+)\.(?P<variable>\w+)\s+to\s+"
            r"(?P<value>[+-]?(?:\d+(?:\.\d*)?|\.\d+))", command, re.IGNORECASE
        ):
            targets = by_editor.get(match.group("subject").casefold(), [])
            if len(targets) != 1 or targets[0].signature != ACTOR_REFERENCE_RECORD:
                raise ValueError("Fallout 3 CG02 Overseer variable target differs")
            return {"index": index, "kind": "setActorVariable",
                    "referenceFormId": _form_id(targets[0].form_id),
                    "variable": match.group("variable"), "value": float(match.group("value"))}
        if match := re.fullmatch(
            r"(?:(?P<subject>\w+)\.)?look\s+(?P<target>\w+)",
            command,
            re.IGNORECASE,
        ):
            subject_name = match.group("subject")
            targets = [reference] if subject_name is None else by_editor.get(subject_name.casefold(), [])
            if len(targets) != 1 or targets[0].signature != ACTOR_REFERENCE_RECORD:
                raise ValueError("Fallout 3 CG02 Overseer look subject differs")
            target_name = match.group("target")
            look_targets = [] if target_name.casefold() == "player" else by_editor.get(target_name.casefold(), [])
            if target_name.casefold() != "player" and (
                len(look_targets) != 1 or look_targets[0].signature != ACTOR_REFERENCE_RECORD
            ):
                raise ValueError("Fallout 3 CG02 Overseer look target differs")
            return {"index": index, "kind": "lookAt",
                    "referenceFormId": _form_id(targets[0].form_id),
                    "targetReferenceFormId": player_reference_form_id if not look_targets else _form_id(look_targets[0].form_id)}
        if match := re.fullmatch(r"(?P<subject>\w+)\.evp", command, re.IGNORECASE):
            targets = by_editor.get(match.group("subject").casefold(), [])
            if len(targets) != 1 or targets[0].signature != ACTOR_REFERENCE_RECORD:
                raise ValueError("Fallout 3 CG02 Overseer package target differs")
            return {"index": index, "kind": "evaluatePackage",
                    "referenceFormId": _form_id(targets[0].form_id)}
        add_item = re.fullmatch(
            r"player\.additem\s+(?P<item>\w+)\s+(?P<count>\d+)", command, re.IGNORECASE)
        if add_item is not None:
            items = by_editor.get(add_item.group("item").casefold(), [])
            if len(items) != 1:
                raise ValueError("Fallout 3 CG02 Overseer item differs")
            return {"index": index, "kind": "addItem",
                    "itemFormId": _form_id(items[0].form_id),
                    "itemEditorId": _editor_id(items[0]),
                    "count": int(add_item.group("count"))}
        if command.casefold() == "resetpipboymanager":
            return {"index": index, "kind": "resetPipboyManager"}
        if match := re.fullmatch(r"(?P<subject>\w+)\.stoplook", command, re.IGNORECASE):
            targets = by_editor.get(match.group("subject").casefold(), [])
            if len(targets) != 1 or targets[0].signature != ACTOR_REFERENCE_RECORD:
                raise ValueError("Fallout 3 CG02 Overseer stop-look target differs")
            return {"index": index, "kind": "stopLook",
                    "referenceFormId": _form_id(targets[0].form_id)}
        if match := re.fullmatch(r"addachievement\s+(?P<value>\d+)", command, re.IGNORECASE):
            return {"index": index, "kind": "addAchievement",
                    "value": int(match.group("value"))}
        raise ValueError(f"Fallout 3 CG02 Overseer result differs: {command}")

    source_stage = int(config["sourceStage"])
    pipboy_stage = int(config["pipBoyStage"])
    target_stage = int(config["targetStage"])
    branches = []
    for info_id in [int(str(value), FORM_ID_RADIX) for value in config["infoFormIds"]]:
        info = by_form.get(info_id)
        if info is None or info.signature != DIALOGUE_INFO_RECORD or not any(
            group.group_type == DIALOGUE_CHILD_GROUP_TYPE and group.label_u32 == topic.form_id
            for group in info.groups
        ):
            raise ValueError("Fallout 3 CG02 Overseer INFO differs")
        conditions = [_dialogue_condition(row.data) for row in iter_subrecords(info) if row.signature == "CTDA"]
        if not any(int(row["function"]) == GET_IS_ID_FUNCTION and int(row["parameter1"]) == base.form_id for row in conditions):
            raise ValueError("Fallout 3 CG02 Overseer INFO speaker differs")
        sex_values = [int(row["parameter1"]) for row in conditions if int(row["function"]) == GET_PC_IS_SEX_FUNCTION]
        engine_sex = None if not sex_values else "male" if sex_values == [0] else "female" if sex_values == [1] else "unsupported"
        if engine_sex == "unsupported":
            raise ValueError("Fallout 3 CG02 Overseer INFO sex differs")
        responses = [value for value in _text_values(info, "NAM1") if value]
        idle_ids = [struct.unpack("<I", row.data)[0] for row in iter_subrecords(info) if row.signature == "SNAM" and len(row.data) == FORM_ID_BYTES]
        if len(responses) != 1 or len(idle_ids) > 1:
            raise ValueError("Fallout 3 CG02 Overseer response differs")
        idle = None if not idle_ids else by_form.get(idle_ids[0])
        idle_models = [] if idle is None else _text_values(idle, "MODL")
        if idle is not None and (idle.signature != IDLE_RECORD or len(idle_models) != 1):
            raise ValueError("Fallout 3 CG02 Overseer idle differs")
        raw_commands = [command for source in _text_values(info, "SCTX") for command in _source_commands(source)]
        effects = [resolve_command(command, index) for index, command in enumerate(raw_commands)]
        stage_effects = [row for row in effects if row["kind"] == "setStage"]
        stage = None if not stage_effects else int(stage_effects[0]["stage"])
        if len(stage_effects) > 1 or stage not in (None, pipboy_stage, target_stage):
            raise ValueError("Fallout 3 CG02 Overseer INFO stage differs")
        branches.append({
            "sequence": 0 if engine_sex is not None else len(branches) - 1,
            "engineSex": engine_sex,
            "infoFormId": _form_id(info.form_id),
            "infoRecordSha256": hashlib.sha256(info.data).hexdigest(),
            "sayOnce": True,
            "response": {"index": 1, "text": responses[0],
                         "textSha256": hashlib.sha256(responses[0].encode("utf-8")).hexdigest()},
            "speakerIdle": None if idle is None else {
                "formId": _form_id(idle.form_id), "editorId": _editor_id(idle),
                "recordSha256": hashlib.sha256(idle.data).hexdigest(),
                "modelPath": canonical_member_path(f"meshes\\{idle_models[0]}")},
            "effects": effects,
        })
    if [row["sequence"] for row in branches] != [0, 0, 1, 2, 3]:
        raise ValueError("Fallout 3 CG02 Overseer order differs")

    stage_results = {}
    for stage in (pipboy_stage, target_stage):
        commands = [command for source in stage_sources.get(stage, []) for command in _source_commands(source)]
        stage_results[str(stage)] = {
            "sourceSha256": hashlib.sha256("\n".join(stage_sources.get(stage, [])).encode("cp1252")).hexdigest(),
            "commands": [resolve_command(command, index) for index, command in enumerate(commands)],
        }
    target_kinds = [row["kind"] for row in stage_results[str(target_stage)]["commands"]]
    stop_look_end = CG02_STAGE10_PREFIX_COMMAND_COUNT + CG02_STAGE10_STOP_LOOK_COUNT
    if [row["kind"] for row in stage_results[str(pipboy_stage)]["commands"]] != ["evaluatePackage"] or target_kinds[:CG02_STAGE10_PREFIX_COMMAND_COUNT] != ["addItem", "resetPipboyManager", "evaluatePackage"] or target_kinds[CG02_STAGE10_PREFIX_COMMAND_COUNT:stop_look_end] != ["stopLook"] * CG02_STAGE10_STOP_LOOK_COUNT or target_kinds[stop_look_end:] != ["addAchievement"]:
        raise ValueError("Fallout 3 CG02 Overseer stage results differ")
    recipe = actor_preparation.load_recipe(str(config["actorRecipeId"]))
    if str(recipe["proofActorReferenceFormId"]).casefold() != _form_id(reference.form_id):
        raise ValueError("Fallout 3 CG02 Overseer actor recipe differs")
    return {
        "schema": "opennv-fo3-cg02-stage-7-overseer-speech-runtime/v1",
        "sourceStage": source_stage,
        "targetStage": target_stage,
        "overseerReferenceFormId": _form_id(reference.form_id),
        "overseerBaseFormId": _form_id(base.form_id),
        "overseerScriptFormId": _form_id(script.form_id),
        "overseerScriptSourceSha256": hashlib.sha256(script_source.encode("cp1252")).hexdigest(),
        "playerReferenceFormId": player_reference_form_id,
        "actorRecipeId": str(recipe["id"]),
        "dialogue": {"voiceType": {"formId": _form_id(voice.form_id), "editorId": _editor_id(voice),
                                      "recordSha256": hashlib.sha256(voice.data).hexdigest()},
                     "branches": branches, "dialoguePlaybackPrepared": False,
                     "dialoguePlaybackImplemented": False},
        "stageResults": stage_results,
        "nextBoundary": {"applied": False,
                         "blocker": "fo3-cg02-stage-10-dad-party-speech-runtime-not-implemented"},
    }


def _compile_cg02_dad_party_runtime(
    records: tuple[object, ...],
    quest: object,
    stage_sources: dict[int, list[str]],
    definition: dict[str, object],
) -> dict[str, object]:
    config = dict(definition["cg02DadPartySpeech"])
    by_form = {record.form_id: record for record in records}
    by_editor = {_editor_id(record).casefold(): record for record in records if _editor_id(record)}
    def exact(name: str, signature: str) -> object:
        record = by_form.get(int(str(config[name]), FORM_ID_RADIX))
        if record is None or record.signature != signature:
            raise ValueError(f"Fallout 3 CG02 Dad party {name} differs")
        return record
    package = exact("packageFormId", PACKAGE_RECORD)
    dad_reference = exact("dadReferenceFormId", ACTOR_REFERENCE_RECORD)
    dad_base = exact("dadBaseFormId", NPC_RECORD)
    marker = exact("playerMarkerFormId", PLACED_REFERENCE_RECORD)
    topic = exact("topicFormId", DIALOGUE_TOPIC_RECORD)
    info = exact("infoFormId", DIALOGUE_INFO_RECORD)
    if struct.unpack("<I", _single_subrecord(dad_reference, "NAME"))[0] != dad_base.form_id:
        raise ValueError("Fallout 3 CG02 Dad party actor differs")
    package_ids = [struct.unpack("<I", row.data)[0] for row in iter_subrecords(dad_base)
                   if row.signature == "PKID" and len(row.data) == FORM_ID_BYTES]
    conditions = [_dialogue_condition(row.data) for row in iter_subrecords(package)
                  if row.signature == "CTDA"]
    source_stage = int(config["sourceStage"])
    if package.form_id not in package_ids or len(conditions) != 1 or conditions[0]["function"] != GET_STAGE_FUNCTION or conditions[0]["parameter1"] != quest.form_id or conditions[0]["comparisonValue"] != float(source_stage):
        raise ValueError("Fallout 3 CG02 Dad party package selection differs")
    targets = [row.data for row in iter_subrecords(package) if row.signature == "PTDT"]
    if len(targets) != 1 or len(targets[0]) != struct.calcsize("<IIiI"):
        raise ValueError("Fallout 3 CG02 Dad party package target differs")
    target_kind, target_form_id, radius, target_unknown = struct.unpack("<IIiI", targets[0])
    player_form_id = int(str(config["playerReferenceFormId"]), FORM_ID_RADIX)
    if target_kind != 0 or target_form_id != player_form_id or radius < 0 or target_unknown != 0:
        raise ValueError("Fallout 3 CG02 Dad party player target differs")
    dad_position = _reference_transform_contract(dad_reference)["positionGameUnits"]
    player_position = _reference_transform_contract(marker)["positionGameUnits"]
    initial_distance = math.dist(dad_position, player_position)
    if not any(group.group_type == DIALOGUE_CHILD_GROUP_TYPE and group.label_u32 == topic.form_id for group in info.groups):
        raise ValueError("Fallout 3 CG02 Dad party INFO differs")
    responses = [value for value in _text_values(info, "NAM1") if value]
    idle_ids = [struct.unpack("<I", row.data)[0] for row in iter_subrecords(info)
                if row.signature == "SNAM" and len(row.data) == FORM_ID_BYTES]
    voice_ids = [struct.unpack("<I", row.data)[0] for row in iter_subrecords(dad_base)
                 if row.signature == "VTCK" and len(row.data) == FORM_ID_BYTES]
    idle = by_form.get(idle_ids[0]) if len(idle_ids) == 1 else None
    voice = by_form.get(voice_ids[0]) if len(voice_ids) == 1 else None
    idle_models = [] if idle is None else _text_values(idle, "MODL")
    result_commands = [command for source in _text_values(info, "SCTX")
                       for command in _source_commands(source)]
    target_stage = int(config["targetStage"])
    if len(responses) != 1 or idle is None or len(idle_models) != 1 or voice is None or result_commands != [f"setstage {_editor_id(quest)} {target_stage}"]:
        raise ValueError("Fallout 3 CG02 Dad party response differs")

    stage_commands = []
    for index, command in enumerate(command for source in stage_sources.get(target_stage, []) for command in _source_commands(source)):
        if match := re.fullmatch(r"EnablePlayerControls\s+(?P<arguments>[\d\s]+)", command, re.IGNORECASE):
            stage_commands.append({"index": index, "kind": "enablePlayerControls",
                                   "arguments": [int(value) for value in match.group("arguments").split()]})
        elif AUTOSAVE_PATTERN.fullmatch(command):
            stage_commands.append({"index": index, "kind": "autosave"})
        elif match := SET_OBJECTIVE_DISPLAYED_PATTERN.fullmatch(command):
            stage_commands.append({"index": index, "kind": "setObjectiveDisplayed",
                                   "objectiveIndex": int(match.group("index")), "value": int(match.group("value"))})
        elif match := re.fullmatch(r"(?P<subject>\w+)\.enable", command, re.IGNORECASE):
            target = by_editor.get(match.group("subject").casefold())
            if target is None: raise ValueError("Fallout 3 CG02 Dad party enable differs")
            stage_commands.append({"index": index, "kind": "enable", "referenceFormId": _form_id(target.form_id)})
        elif match := re.fullmatch(r"(?P<subject>\w+)\.evp", command, re.IGNORECASE):
            target = by_editor.get(match.group("subject").casefold())
            if target is None: raise ValueError("Fallout 3 CG02 Dad party package actor differs")
            stage_commands.append({"index": index, "kind": "evaluatePackage", "referenceFormId": _form_id(target.form_id)})
        elif match := SET_STAGE_PATTERN.fullmatch(command):
            target = by_editor.get(match.group("quest").casefold())
            if target is None or target.signature != QUEST_RECORD: raise ValueError("Fallout 3 CG02 Dad party tutorial differs")
            stage_commands.append({"index": index, "kind": "setStage", "questFormId": _form_id(target.form_id), "stage": int(match.group("stage"))})
        elif command.casefold() == "forceradiostationupdate":
            stage_commands.append({"index": index, "kind": "forceRadioStationUpdate"})
        else:
            raise ValueError(f"Fallout 3 CG02 Dad party stage command differs: {command}")
    return {
        "schema": "opennv-fo3-cg02-stage-10-dad-party-runtime/v1",
        "sourceStage": source_stage, "targetStage": target_stage,
        "dadReferenceFormId": _form_id(dad_reference.form_id),
        "playerReferenceFormId": _form_id(player_form_id),
        "playerMarker": {"formId": _form_id(marker.form_id), "sourceTransform": _reference_transform_contract(marker)},
        "package": {"formId": _form_id(package.form_id), "editorId": _editor_id(package),
                    "recordSha256": hashlib.sha256(package.data).hexdigest(), "radiusGameUnits": radius,
                    "initialDistanceGameUnits": initial_distance,
                    "arrivedAtStart": initial_distance <= radius},
        "dialogue": {"voiceType": {"formId": _form_id(voice.form_id), "editorId": _editor_id(voice)},
                     "branches": [{"sequence": 0, "engineSex": None, "infoFormId": _form_id(info.form_id),
                                   "sayOnce": True, "response": {"index": 1, "text": responses[0], "textSha256": hashlib.sha256(responses[0].encode()).hexdigest()},
                                   "speakerIdle": {"formId": _form_id(idle.form_id), "modelPath": canonical_member_path(f"meshes\\{idle_models[0]}")}, "effects": [{"kind": "setStage", "stage": target_stage}]}],
                     "dialoguePlaybackPrepared": False, "dialoguePlaybackImplemented": False},
        "stageResult": {"commands": stage_commands},
        "nextBoundary": {"applied": False, "blocker": "fo3-cg02-stage-12-birthday-interactions-runtime-not-implemented"},
    }


def _compile_cg02_birthday_interactions_runtime(
    records: tuple[object, ...],
    quest: object,
    stage_sources: dict[int, list[str]],
    definition: dict[str, object],
) -> dict[str, object]:
    config = dict(definition["cg02BirthdayInteractions"])
    by_form = {record.form_id: record for record in records}
    info_by_topic: dict[int, list[object]] = {}
    for record in records:
        if record.signature != DIALOGUE_INFO_RECORD:
            continue
        groups = [group for group in record.groups
                  if group.group_type == DIALOGUE_CHILD_GROUP_TYPE]
        if len(groups) == 1:
            info_by_topic.setdefault(groups[0].label_u32, []).append(record)

    failsafe = by_form.get(int(str(config["failsafeTimerFormId"]), FORM_ID_RADIX))
    if failsafe is None or failsafe.signature != "GLOB":
        raise ValueError("Fallout 3 CG02 birthday failsafe timer differs")
    timer_rows = [row.data for row in iter_subrecords(failsafe) if row.signature == "FLTV"]
    if len(timer_rows) != 1 or len(timer_rows[0]) != struct.calcsize("<f"):
        raise ValueError("Fallout 3 CG02 birthday failsafe value differs")
    failsafe_seconds = struct.unpack("<f", timer_rows[0])[0]

    participants = []
    reached_stages: set[int] = set()
    for raw_participant in config["participants"]:
        participant = dict(raw_participant)
        reference = by_form.get(int(str(participant["referenceFormId"]), FORM_ID_RADIX))
        base = by_form.get(int(str(participant["baseFormId"]), FORM_ID_RADIX))
        if reference is None or reference.signature != ACTOR_REFERENCE_RECORD or \
                base is None or base.signature != NPC_RECORD or \
                struct.unpack("<I", _single_subrecord(reference, "NAME"))[0] != base.form_id:
            raise ValueError("Fallout 3 CG02 birthday participant differs")
        voice_ids = [struct.unpack("<I", row.data)[0] for row in iter_subrecords(base)
                     if row.signature == "VTCK" and len(row.data) == FORM_ID_BYTES]
        voice = by_form.get(voice_ids[0]) if len(voice_ids) == 1 else None
        if voice is None or voice.signature != "VTYP":
            raise ValueError("Fallout 3 CG02 birthday voice type differs")
        greeting_ids = [int(str(value), FORM_ID_RADIX)
                        for value in participant["greetingInfoFormIds"]]
        pending = list(greeting_ids)
        seen_infos: set[int] = set()
        seen_topics: set[int] = set()
        nodes: list[dict[str, object]] = []
        branches: list[dict[str, object]] = []
        while pending:
            info_id = pending.pop(0)
            if info_id in seen_infos:
                continue
            info = by_form.get(info_id)
            if info is None or info.signature != DIALOGUE_INFO_RECORD:
                raise ValueError("Fallout 3 CG02 birthday INFO differs")
            seen_infos.add(info_id)
            conditions = [_dialogue_condition(row.data) for row in iter_subrecords(info)
                          if row.signature == "CTDA"]
            if not any(int(row["function"]) == GET_IS_ID_FUNCTION and
                       int(row["parameter1"]) == base.form_id for row in conditions):
                raise ValueError("Fallout 3 CG02 birthday speaker condition differs")
            sex_values = [int(row["parameter1"]) for row in conditions
                          if int(row["function"]) == GET_PC_IS_SEX_FUNCTION]
            if len(sex_values) > 1 or any(value not in (0, 1) for value in sex_values):
                raise ValueError("Fallout 3 CG02 birthday sex condition differs")
            linked_topics = [struct.unpack("<I", row.data)[0]
                             for row in iter_subrecords(info)
                             if row.signature == "TCLT" and len(row.data) == FORM_ID_BYTES]
            results = [command for source in _text_values(info, "SCTX")
                       for command in _source_commands(source)]
            effects = []
            for command in results:
                if match := SET_STAGE_PATTERN.fullmatch(command):
                    if match.group("quest").casefold() != (_editor_id(quest) or "").casefold():
                        raise ValueError("Fallout 3 CG02 birthday result quest differs")
                    stage = int(match.group("stage"))
                    reached_stages.add(stage)
                    effects.append({"kind": "setStage", "stage": stage})
                elif match := re.fullmatch(
                    r"set\s+CG02\.timer\s+to\s+CG02FailsafeTimer", command,
                    re.IGNORECASE):
                    effects.append({"kind": "setTimer", "seconds": failsafe_seconds})
                elif match := re.fullmatch(
                    r"set\s+CG02\.(?P<variable>\w+)\s+to\s+(?P<value>-?\d+)",
                    command, re.IGNORECASE):
                    effects.append({"kind": "setQuestVariable",
                                    "variable": match.group("variable"),
                                    "value": int(match.group("value"))})
                elif match := re.fullmatch(
                    r"set\s+(?P<subject>\w+)\.(?P<variable>\w+)\s+to\s+"
                    r"(?P<value>-?\d+)", command, re.IGNORECASE):
                    references = [record for record in records
                                  if (_editor_id(record) or "").casefold() ==
                                  match.group("subject").casefold()]
                    if len(references) != 1 or references[0].signature != ACTOR_REFERENCE_RECORD:
                        raise ValueError("Fallout 3 CG02 birthday actor variable differs")
                    effects.append({"kind": "setActorVariable",
                                    "referenceFormId": _form_id(references[0].form_id),
                                    "variable": match.group("variable"),
                                    "value": int(match.group("value"))})
                elif match := re.fullmatch(
                    r"player\.removeitem\s+(?P<item>\w+)\s+(?P<count>\d+)",
                    command, re.IGNORECASE):
                    items = [record for record in records
                             if (_editor_id(record) or "").casefold() ==
                             match.group("item").casefold()]
                    if len(items) != 1:
                        raise ValueError("Fallout 3 CG02 birthday removed item differs")
                    effects.append({"kind": "removeItem",
                                    "formId": _form_id(items[0].form_id),
                                    "count": int(match.group("count"))})
                elif command.casefold() == "startcombat player":
                    security = [record for record in records
                                if (_editor_id(record) or "").casefold() ==
                                "cg02vault101security04ref"]
                    if len(security) != 1 or security[0].signature != ACTOR_REFERENCE_RECORD:
                        raise ValueError("Fallout 3 CG02 birthday combat response differs")
                    effects.append({"kind": "startCombat", "target": "player",
                                    "referenceFormId": _form_id(security[0].form_id)})
                elif command.casefold().startswith("if ") or command.casefold() == "endif":
                    effects.append({"kind": "sourceConditional", "source": command})
                else:
                    raise ValueError(
                        f"Fallout 3 CG02 birthday INFO result differs: {command}")
            response_rows = []
            response_index = None
            for response_row in iter_subrecords(info):
                if response_row.signature == "TRDT":
                    if len(response_row.data) not in DIALOGUE_RESPONSE_METADATA_BYTES:
                        raise ValueError(
                            "Fallout 3 CG02 birthday response metadata differs")
                    response_index = int(response_row.data[
                        DIALOGUE_RESPONSE_NUMBER_OFFSET])
                elif response_row.signature == "NAM1" and response_index is not None:
                    response_rows.append((response_index, zstring(response_row.data)))
                    response_index = None
            if not response_rows or any(not text for _, text in response_rows):
                raise ValueError("Fallout 3 CG02 birthday response differs")
            branch_lines = []
            for response_index, response in response_rows:
                line = {"infoFormId": _form_id(info.form_id),
                        "response": {"index": response_index, "text": response,
                                     "textSha256": hashlib.sha256(response.encode()).hexdigest()}}
                branches.append(line)
                branch_lines.append(response_index)
            nodes.append({
                "infoFormId": _form_id(info.form_id),
                "topicFormId": _form_id(next(group.label_u32 for group in info.groups
                                              if group.group_type == DIALOGUE_CHILD_GROUP_TYPE)),
                "engineSex": None if not sex_values else ("female" if sex_values[0] else "male"),
                "responseIndexes": branch_lines,
                "linkedTopicFormIds": [_form_id(value) for value in linked_topics],
                "conditions": ([condition for condition in conditions
                                if int(condition["function"]) not in
                                {GET_IS_ID_FUNCTION, GET_PC_IS_SEX_FUNCTION}]
                               if "actorRecipeId" in participant else []),
                "effects": effects,
            })
            for topic_id in linked_topics:
                topic = by_form.get(topic_id)
                if topic is None or topic.signature != DIALOGUE_TOPIC_RECORD:
                    raise ValueError("Fallout 3 CG02 birthday choice topic differs")
                if topic_id not in seen_topics:
                    seen_topics.add(topic_id)
                    for child in info_by_topic.get(topic_id, []):
                        pending.append(child.form_id)
        topics = []
        for topic_id in sorted(seen_topics):
            topic = by_form[topic_id]
            labels = [value for value in _text_values(topic, "FULL") if value]
            if len(labels) != 1:
                raise ValueError("Fallout 3 CG02 birthday choice label differs")
            topics.append({"formId": _form_id(topic_id), "text": labels[0],
                           "textSha256": hashlib.sha256(labels[0].encode()).hexdigest()})
        participants.append({
            "referenceFormId": _form_id(reference.form_id),
            "baseFormId": _form_id(base.form_id),
            "displayName": _text_values(base, "FULL")[0],
            **({"actorRecipeId": str(participant["actorRecipeId"])}
               if "actorRecipeId" in participant else {}),
            "greetingInfoFormIds": [_form_id(value) for value in greeting_ids],
            "dialogue": {"voiceType": {"formId": _form_id(voice.form_id),
                                         "editorId": _editor_id(voice)},
                         "branches": branches,
                         "nodes": nodes,
                         "topics": topics,
                         "dialoguePlaybackPrepared": False,
                         "dialoguePlaybackImplemented": False},
        })
    gift_stages = {int(value) for value in config["giftStages"]}
    if reached_stages & gift_stages != gift_stages:
        raise ValueError("Fallout 3 CG02 birthday gift stages differ")
    stage_results = {}
    aggregate_stages: set[int] = set()
    for stage in sorted(gift_stages):
        commands = [command for source in stage_sources.get(stage, [])
                    for command in _source_commands(source)]
        if not commands:
            raise ValueError("Fallout 3 CG02 birthday gift result differs")
        if match := re.fullmatch(r"player\.additem\s+(\w+)\s+(\d+)", commands[0], re.IGNORECASE):
            item = next((record for record in records
                         if (_editor_id(record) or "").casefold() == match.group(1).casefold()), None)
            if item is None:
                raise ValueError("Fallout 3 CG02 birthday gift item differs")
            stage_results[str(stage)] = {"kind": "addItem", "formId": _form_id(item.form_id),
                                         "count": int(match.group(2)),
                                         "commandCount": len(commands)}
        elif match := re.fullmatch(r"player\.addnote\s+(\w+)", commands[0], re.IGNORECASE):
            note = next((record for record in records
                         if (_editor_id(record) or "").casefold() == match.group(1).casefold()), None)
            if note is None:
                raise ValueError("Fallout 3 CG02 birthday gift note differs")
            stage_results[str(stage)] = {"kind": "addNote", "formId": _form_id(note.form_id),
                                         "count": 1, "commandCount": len(commands)}
        else:
            raise ValueError("Fallout 3 CG02 birthday gift command differs")
        for command in commands[1:]:
            match = SET_STAGE_PATTERN.fullmatch(command)
            if match is None or match.group("quest").casefold() != \
                    (_editor_id(quest) or "").casefold():
                raise ValueError("Fallout 3 CG02 birthday aggregate result differs")
            aggregate_stages.add(int(match.group("stage")))
            stage_results[str(stage)]["aggregateStage"] = int(match.group("stage"))
    if len(aggregate_stages) != 1:
        raise ValueError("Fallout 3 CG02 birthday aggregate stage differs")
    result = {
        "schema": "opennv-fo3-cg02-stage-12-birthday-interactions-runtime/v1",
        "sourceStage": int(config["sourceStage"]),
        "failsafeTimer": {"formId": _form_id(failsafe.form_id),
                          "seconds": failsafe_seconds},
        "participants": participants,
        "stageResults": stage_results,
        "aggregateStage": aggregate_stages.pop(),
        "nextBoundary": {"applied": False,
                         "blocker": "fo3-cg02-stage-12-cake-trigger-runtime-not-implemented"},
    }
    if "cake" in config:
        result["cakeRuntime"] = _compile_cg02_cake_runtime(
            records, quest, stage_sources, dict(config["cake"]))
        result["cakeRuntime"]["failsafeSeconds"] = failsafe_seconds
        result["nextBoundary"] = {"applied": True, "blocker": None}
    if "butch" in config:
        result["butchRuntime"] = _compile_cg02_butch_runtime(
            records, quest, stage_sources, dict(config["butch"]))
        result["nextBoundary"] = {"applied": True, "blocker": None}
    return result


def _compile_cg02_butch_runtime(
    records: tuple[object, ...],
    quest: object,
    stage_sources: dict[int, list[str]],
    config: dict[str, object],
) -> dict[str, object]:
    by_form = {record.form_id: record for record in records}
    by_editor = {(_editor_id(record) or "").casefold(): record for record in records
                 if _editor_id(record)}
    def exact(name: str, signature: str) -> object:
        record = by_form.get(int(str(config[name]), FORM_ID_RADIX))
        if record is None or record.signature != signature:
            raise ValueError(f"Fallout 3 CG02 Butch {name} differs")
        return record
    reference = exact("referenceFormId", ACTOR_REFERENCE_RECORD)
    base = exact("baseFormId", NPC_RECORD)
    script = exact("scriptFormId", SCRIPT_RECORD)
    package = exact("findPlayerPackageFormId", PACKAGE_RECORD)
    sweetroll = exact("sweetrollFormId", "ALCH")
    if (struct.unpack("<I", _single_subrecord(reference, "NAME"))[0] != base.form_id or
        struct.unpack("<I", _single_subrecord(base, "SCRI"))[0] != script.form_id or
        package.form_id not in [struct.unpack("<I", row.data)[0]
                                for row in iter_subrecords(base)
                                if row.signature == "PKID" and
                                len(row.data) == FORM_ID_BYTES]):
        raise ValueError("Fallout 3 CG02 Butch actor/package join differs")
    script_source = _script_source(script)
    if not re.search(
        r"begin\s+OnStartCombat\s+player.*?CG02Vault101Security04REF\.evp.*?"
        r"SayTo\s+player\s+CG02ButchSpeech",
        script_source, re.IGNORECASE | re.DOTALL):
        raise ValueError("Fallout 3 CG02 Butch script differs")
    targets = [row.data for row in iter_subrecords(package) if row.signature == "PTDT"]
    conditions = [_dialogue_condition(row.data) for row in iter_subrecords(package)
                  if row.signature == "CTDA"]
    if len(targets) != 1 or len(targets[0]) != struct.calcsize("<IiII"):
        raise ValueError("Fallout 3 CG02 Butch package target differs")
    target_kind, target_form_id, radius, target_count = struct.unpack("<IiII", targets[0])
    if (target_kind != 0 or
        target_form_id != int(str(config["playerReferenceFormId"]), FORM_ID_RADIX) or
        target_count != 0):
        raise ValueError("Fallout 3 CG02 Butch package player target differs")
    expected_stage_values = {
        int(config["sourceStage"]): 1.0,
        int(config["requiredCakeStage"]): 1.0,
        int(config["sceneDoneStage"]): 0.0,
        int(config["intercomStage"]): 0.0,
    }
    actual_stage_values = {
        int(row["parameter2"]): float(row["comparisonValue"])
        for row in conditions
        if int(row["function"]) == GET_STAGE_DONE_FUNCTION and
        int(row["parameter1"]) == quest.form_id
    }
    if actual_stage_values != expected_stage_values:
        raise ValueError("Fallout 3 CG02 Butch package conditions differ")
    package_commands = [command for source in _text_values(package, "SCTX")
                        for command in _source_commands(source)]
    if [command.casefold() for command in package_commands] != [
        "cg02paulhannonref.evp"]:
        raise ValueError("Fallout 3 CG02 Butch package result differs")
    stage34 = [command for source in stage_sources.get(int(config["aggregateStage"]), [])
               for command in _source_commands(source)]
    stage35 = [command for source in stage_sources.get(int(config["intercomStage"]), [])
               for command in _source_commands(source)]
    stage34_timer = (re.fullmatch(
        r"set\s+CG02\.timer\s+to\s+(?P<seconds>\d+)", stage34[0], re.IGNORECASE)
        if len(stage34) == 1 else None)
    if stage34_timer is None or len(stage35) != CG02_INTERCOM_STAGE_COMMAND_COUNT:
        raise ValueError("Fallout 3 CG02 Butch aggregation stages differ")
    resolved_stage35 = []
    for command in stage35:
        if match := re.fullmatch(r"(?P<subject>\w+)\.evp", command,
                                 re.IGNORECASE):
            actor = by_editor.get(match.group("subject").casefold())
            if actor is None or actor.signature != ACTOR_REFERENCE_RECORD:
                raise ValueError("Fallout 3 CG02 intercom package actor differs")
            resolved_stage35.append({"kind": "evaluatePackage",
                                     "referenceFormId": _form_id(actor.form_id)})
        elif match := re.fullmatch(
                r"(?P<subject>\w+)\.setTalkingActivatorActor\s+(?P<actor>\w+)",
                command, re.IGNORECASE):
            subject = by_editor.get(match.group("subject").casefold())
            actor = by_editor.get(match.group("actor").casefold())
            if subject is None or actor is None:
                raise ValueError("Fallout 3 CG02 intercom actor join differs")
            resolved_stage35.append({"kind": "setTalkingActivatorActor",
                                     "referenceFormId": _form_id(subject.form_id),
                                     "actorReferenceFormId": _form_id(actor.form_id)})
        elif match := re.fullmatch(
                r"set\s+CG02\.(?P<variable>\w+)\s+to\s+(?P<value>\d+)",
                command, re.IGNORECASE):
            resolved_stage35.append({"kind": "setQuestVariable",
                                     "variable": match.group("variable"),
                                     "value": int(match.group("value"))})
        else:
            raise ValueError(f"Fallout 3 CG02 intercom command differs: {command}")
    result = {
        "schema": "opennv-fo3-cg02-stage-20-butch-runtime/v1",
        "sourceStage": int(config["sourceStage"]),
        "requiredCakeStage": int(config["requiredCakeStage"]),
        "sceneDoneStage": int(config["sceneDoneStage"]),
        "aggregateStage": int(config["aggregateStage"]),
        "intercomStage": int(config["intercomStage"]),
        "referenceFormId": _form_id(reference.form_id),
        "baseFormId": _form_id(base.form_id),
        "actorRecipeId": str(config["actorRecipeId"]),
        "sweetrollFormId": _form_id(sweetroll.form_id),
        "findPlayerPackage": {
            "formId": _form_id(package.form_id), "target": "player",
            "radiusGameUnits": radius, "conditions": conditions,
            "resultCommands": package_commands,
        },
        "stage34": {"timerSeconds": int(stage34_timer.group("seconds")),
                    "commands": stage34},
        "stage35": {"commands": resolved_stage35},
        "nextBoundary": {"applied": False,
                         "blocker": "fo3-cg02-butch-combat-runtime-not-implemented"},
    }
    if "postIntercom" in config:
        result["postIntercomRuntime"] = _compile_cg02_post_intercom_runtime(
            records, quest, stage_sources, dict(config["postIntercom"]))
    return result


def _compile_cg02_post_intercom_runtime(
    records: tuple[object, ...], quest: object,
    stage_sources: dict[int, list[str]], config: dict[str, object],
) -> dict[str, object]:
    by_form = {record.form_id: record for record in records}
    by_editor = {(_editor_id(record) or "").casefold(): record for record in records
                 if _editor_id(record)}
    def exact(name: str, signature: str) -> object:
        record = by_form.get(int(str(config[name]), FORM_ID_RADIX))
        if record is None or record.signature != signature:
            raise ValueError(f"Fallout 3 CG02 post-intercom {name} differs")
        return record
    dad_ref, dad_base = exact("dadReferenceFormId", ACTOR_REFERENCE_RECORD), exact("dadBaseFormId", NPC_RECORD)
    jonas_ref, jonas_base = exact("jonasReferenceFormId", ACTOR_REFERENCE_RECORD), exact("jonasBaseFormId", NPC_RECORD)
    intercom, marker = exact("intercomReferenceFormId", PLACED_REFERENCE_RECORD), exact("intercomMarkerFormId", PLACED_REFERENCE_RECORD)
    exact("conversationTopicFormId", DIALOGUE_TOPIC_RECORD)
    if (struct.unpack("<I", _single_subrecord(dad_ref, "NAME"))[0] != dad_base.form_id or
        struct.unpack("<I", _single_subrecord(jonas_ref, "NAME"))[0] != jonas_base.form_id):
        raise ValueError("Fallout 3 CG02 post-intercom actor join differs")
    parents = [struct.unpack("<I", row.data[:4])[0] for row in iter_subrecords(jonas_ref)
               if row.signature == "XESP" and len(row.data) == FORM_ID_BYTES * 2]
    if parents != [dad_ref.form_id]:
        raise ValueError("Fallout 3 CG02 Jonas enable-parent differs")
    package_ids = {struct.unpack("<I", row.data)[0] for row in iter_subrecords(dad_base)
                   if row.signature == "PKID" and len(row.data) == FORM_ID_BYTES}
    def package(name: str, target: object, target_kind: str) -> dict[str, object]:
        record = exact(name, PACKAGE_RECORD)
        targets = [(row.signature, row.data) for row in iter_subrecords(record)
                   if row.signature in {"PTDT", "PLDT"}]
        if record.form_id not in package_ids or len(targets) != 1:
            raise ValueError("Fallout 3 CG02 post-intercom package differs")
        signature, payload = targets[0]
        if signature == "PTDT" and len(payload) == struct.calcsize("<IiII"):
            kind, target_id, radius, count = struct.unpack("<IiII", payload)
        elif signature == "PLDT" and len(payload) == struct.calcsize("<IiI"):
            kind, target_id, radius = struct.unpack("<IiI", payload)
            count = 0
        else:
            raise ValueError("Fallout 3 CG02 post-intercom package target layout differs")
        expected_id = int(target) if target_kind == "player" else target.form_id
        if kind != 0 or target_id != expected_id or count != 0 or radius < 0:
            raise ValueError("Fallout 3 CG02 post-intercom package target differs")
        result = {"formId": _form_id(record.form_id), "editorId": _editor_id(record),
                  "targetKind": target_kind, "targetFormId": _form_id(expected_id),
                  "radiusGameUnits": radius, "recordSha256": hashlib.sha256(record.data).hexdigest()}
        if target_kind == "reference":
            result["targetTransform"] = _reference_transform_contract(target)
        return result
    packages = {
        "toIntercom": package("dadToIntercomPackageFormId", marker, "reference"),
        "talkToJonas": package("dadTalkToJonasPackageFormId", intercom, "reference"),
        "toPlayer": package("dadToPlayerPackageFormId",
                            int(str(config["playerReferenceFormId"]), FORM_ID_RADIX),
                            "player"),
    }
    voice_by_base = {}
    for base in (dad_base, jonas_base):
        voice_id = struct.unpack("<I", _single_subrecord(base, "VTCK"))[0]
        voice = by_form.get(voice_id)
        if voice is None or voice.signature != VOICE_TYPE_RECORD:
            raise ValueError("Fallout 3 CG02 post-intercom voice differs")
        voice_by_base[base.form_id] = voice
    def cue(name: str, speaker: object, target_stage: int | None,
            sex: str | None = None) -> dict[str, object]:
        info = exact(name, DIALOGUE_INFO_RECORD)
        responses = [value for value in _text_values(info, "NAM1") if value]
        conditions = [_dialogue_condition(row.data) for row in iter_subrecords(info) if row.signature == "CTDA"]
        commands = [command for source in _text_values(info, "SCTX") for command in _source_commands(source)]
        if not responses or not any(int(row["function"]) == GET_IS_ID_FUNCTION and int(row["parameter1"]) == speaker.form_id for row in conditions):
            raise ValueError("Fallout 3 CG02 post-intercom INFO differs")
        if target_stage is None:
            if commands: raise ValueError("Fallout 3 CG02 Jonas reply result differs")
        else:
            match = SET_STAGE_PATTERN.fullmatch(commands[0]) if len(commands) == 1 else None
            if match is None or int(match.group("stage")) != target_stage:
                raise ValueError("Fallout 3 CG02 post-intercom INFO stage differs")
        voice = voice_by_base[speaker.form_id]
        return {"infoFormId": _form_id(info.form_id), "engineSex": sex,
                "speakerBaseFormId": _form_id(speaker.form_id),
                "voiceType": {"formId": _form_id(voice.form_id), "editorId": _editor_id(voice)},
                "responses": [{"index": index + 1, "text": text,
                               "textSha256": hashlib.sha256(text.encode()).hexdigest()}
                              for index, text in enumerate(responses)],
                "targetStage": target_stage}
    answer, goodbye, target = (int(config[name]) for name in ("answerStage", "goodbyeStage", "targetStage"))
    cues = [cue("dadCallInfoFormId", dad_base, answer),
            cue("jonasReplyInfoFormId", jonas_base, None),
            cue("maleGoodbyeInfoFormId", dad_base, goodbye, "male"),
            cue("femaleGoodbyeInfoFormId", dad_base, goodbye, "female"),
            cue("dadGreetingInfoFormId", dad_base, target)]
    def commands(stage: int) -> list[dict[str, object]]:
        resolved = []
        source_commands = [command for source in stage_sources.get(stage, [])
                           for command in _source_commands(source)]
        for index, command in enumerate(source_commands):
            if match := re.fullmatch(r"set\s+CG02\.(?P<variable>\w+)\s+to\s+(?P<value>\d+)", command, re.IGNORECASE):
                row = {"index": index, "kind": "setQuestVariable", "variable": match.group("variable"), "value": int(match.group("value"))}
            elif match := re.fullmatch(r"(?P<subject>\w+)\.evp", command, re.IGNORECASE):
                row = {"index": index, "kind": "evaluatePackage", "referenceFormId": _form_id(by_editor[match.group("subject").casefold()].form_id)}
            elif match := re.fullmatch(r"(?P<subject>\w+)\.setTalkingActivatorActor", command, re.IGNORECASE):
                row = {"index": index, "kind": "clearTalkingActivatorActor", "referenceFormId": _form_id(by_editor[match.group("subject").casefold()].form_id)}
            elif match := re.fullmatch(r"(?P<subject>\w+)\.enable", command, re.IGNORECASE):
                row = {"index": index, "kind": "enable", "referenceFormId": _form_id(by_editor[match.group("subject").casefold()].form_id)}
            elif match := re.fullmatch(r"(?P<subject>\w+)\.IgnoreCrime\s+(?P<value>\d+)", command, re.IGNORECASE):
                row = {"index": index, "kind": "ignoreCrime", "referenceFormId": _form_id(by_editor[match.group("subject").casefold()].form_id), "value": int(match.group("value"))}
            elif match := SET_OBJECTIVE_DISPLAYED_PATTERN.fullmatch(command):
                row = {"index": index, "kind": "setObjectiveDisplayed", "objectiveIndex": int(match.group("index")), "value": int(match.group("value"))}
            elif match := re.fullmatch(r"setObjectiveCompleted\s+CG02\s+(?P<index>\d+)\s+(?P<value>\d+)", command, re.IGNORECASE):
                row = {"index": index, "kind": "setObjectiveCompleted", "objectiveIndex": int(match.group("index")), "value": int(match.group("value"))}
            elif match := SET_STAGE_PATTERN.fullmatch(command):
                target_quest = by_editor.get(match.group("quest").casefold())
                if target_quest is None or target_quest.signature != QUEST_RECORD:
                    raise ValueError("Fallout 3 CG02 post-intercom tutorial differs")
                row = {"index": index, "kind": "setStage", "questFormId": _form_id(target_quest.form_id), "stage": int(match.group("stage"))}
            else:
                raise ValueError(f"Fallout 3 CG02 post-intercom stage result differs: {command}")
            resolved.append(row)
        return resolved
    recipe = actor_preparation.load_recipe(str(config["jonasActorRecipeId"]))
    if str(recipe["proofActorReferenceFormId"]).casefold() != _form_id(jonas_ref.form_id):
        raise ValueError("Fallout 3 CG02 Jonas recipe differs")
    result = {"schema": "opennv-fo3-cg02-stage-35-post-intercom-runtime/v1",
            "sourceStage": int(config["sourceStage"]), "answerStage": answer,
            "goodbyeStage": goodbye, "targetStage": target,
            "dadReferenceFormId": _form_id(dad_ref.form_id), "dadBaseFormId": _form_id(dad_base.form_id),
            "jonasReferenceFormId": _form_id(jonas_ref.form_id), "jonasBaseFormId": _form_id(jonas_base.form_id),
            "jonasActorRecipeId": str(recipe["id"]), "intercomReferenceFormId": _form_id(intercom.form_id),
            "packages": packages, "dialogue": {"cues": cues, "dialoguePlaybackPrepared": False,
                "dialoguePlaybackImplemented": False},
            "stageResults": {str(stage): {"commands": commands(stage)} for stage in (answer, goodbye, target)},
            "nextBoundary": {"applied": False, "blocker": "fo3-cg02-stage-40-reactor-gift-runtime-not-implemented"}}
    if "reactorGift" in config:
        result["reactorGiftRuntime"] = _compile_cg02_reactor_gift_runtime(
            records, quest, stage_sources, dict(config["reactorGift"]))
    return result


def _compile_cg02_reactor_gift_runtime(
    records: tuple[object, ...], quest: object,
    stage_sources: dict[int, list[str]], config: dict[str, object],
) -> dict[str, object]:
    by_form = {record.form_id: record for record in records}
    by_editor = {(_editor_id(record) or "").casefold(): record for record in records
                 if _editor_id(record)}
    info_by_topic: dict[int, list[object]] = {}
    for record in records:
        if record.signature == DIALOGUE_INFO_RECORD:
            for group in record.groups:
                if group.group_type == DIALOGUE_CHILD_GROUP_TYPE:
                    info_by_topic.setdefault(group.label_u32, []).append(record)
    def exact(name: str, signature: str) -> object:
        record = by_form.get(int(str(config[name]), FORM_ID_RADIX))
        if record is None or record.signature != signature:
            raise ValueError(f"Fallout 3 CG02 reactor gift {name} differs")
        return record
    source_stage, jonas_stage, target_stage = (
        int(config[name]) for name in ("sourceStage", "jonasStage", "targetStage"))
    range_stage = int(config["rangeStage"])
    hit_stage = int(config["hitStage"])
    combat_stage = int(config["combatStage"])
    death_stage = int(config["deathStage"])
    completion_stage = int(config["completionStage"])
    participant_specs = (
        (exact("jonasReferenceFormId", ACTOR_REFERENCE_RECORD),
         exact("jonasBaseFormId", NPC_RECORD),
         [int(str(value), FORM_ID_RADIX) for value in config["jonasGreetingInfoFormIds"]]),
        (exact("dadReferenceFormId", ACTOR_REFERENCE_RECORD),
         exact("dadBaseFormId", NPC_RECORD),
         [int(str(value), FORM_ID_RADIX)
          for value in config["dadGreetingInfoFormIds"]]),
    )
    marker = exact("jonasMarkerFormId", PLACED_REFERENCE_RECORD)
    exact("dadMoveMarkerFormId", PLACED_REFERENCE_RECORD)
    exact("reactorStairDoorFormId", PLACED_REFERENCE_RECORD)
    exact("targetRangeDoorFormId", PLACED_REFERENCE_RECORD)
    exact("partyHatFormId", "ARMO")
    exact("bbGunFormId", "WEAP")
    exact("bbAmmoFormId", "AMMO")
    player_form_id = int(str(config["playerReferenceFormId"]), FORM_ID_RADIX)
    participants = []
    for reference, base, greetings in participant_specs:
        if struct.unpack("<I", _single_subrecord(reference, "NAME"))[0] != base.form_id:
            raise ValueError("Fallout 3 CG02 reactor gift actor join differs")
        voice_id = struct.unpack("<I", _single_subrecord(base, "VTCK"))[0]
        voice = by_form.get(voice_id)
        if voice is None or voice.signature != VOICE_TYPE_RECORD:
            raise ValueError("Fallout 3 CG02 reactor gift voice differs")
        pending, seen_infos, seen_topics = list(greetings), set(), set()
        nodes, branches = [], []
        while pending:
            info_id = pending.pop(0)
            if info_id in seen_infos:
                continue
            info = by_form.get(info_id)
            if info is None or info.signature != DIALOGUE_INFO_RECORD:
                raise ValueError("Fallout 3 CG02 reactor gift INFO differs")
            seen_infos.add(info_id)
            conditions = [_dialogue_condition(row.data) for row in iter_subrecords(info)
                          if row.signature == "CTDA"]
            if not any(int(row["function"]) == GET_IS_ID_FUNCTION and
                       int(row["parameter1"]) == base.form_id for row in conditions):
                raise ValueError("Fallout 3 CG02 reactor gift speaker differs")
            sex_values = [int(row["parameter1"]) for row in conditions
                          if int(row["function"]) == GET_PC_IS_SEX_FUNCTION]
            engine_sex = None if not sex_values else "male" if sex_values == [0] else "female" if sex_values == [1] else "unsupported"
            if engine_sex == "unsupported":
                raise ValueError("Fallout 3 CG02 reactor gift sex differs")
            linked = [struct.unpack("<I", row.data)[0] for row in iter_subrecords(info)
                      if row.signature == "TCLT" and len(row.data) == FORM_ID_BYTES]
            effects = []
            for command in [command for source in _text_values(info, "SCTX")
                            for command in _source_commands(source)]:
                match = SET_STAGE_PATTERN.fullmatch(command)
                package_match = re.fullmatch(
                    r"(?P<subject>\w+)\.evp", command, re.IGNORECASE)
                if match is not None and int(match.group("stage")) in {
                        jonas_stage, target_stage, range_stage, combat_stage,
                        completion_stage}:
                    effects.append({"kind": "setStage",
                                    "stage": int(match.group("stage"))})
                    continue
                if package_match is not None:
                    effects.append({"kind": "evaluatePackage",
                                    "referenceFormId": _form_id(by_editor[
                                        package_match.group("subject").casefold()].form_id)})
                    continue
                if match is None:
                    raise ValueError(
                        f"Fallout 3 CG02 reactor gift INFO result differs: "
                        f"{_form_id(info.form_id)} {command}")
                raise ValueError(
                    f"Fallout 3 CG02 reactor gift INFO stage differs: "
                    f"{_form_id(info.form_id)} {command}")
            response_rows = []
            response_index = None
            for row in iter_subrecords(info):
                if row.signature == "TRDT":
                    if len(row.data) not in DIALOGUE_RESPONSE_METADATA_BYTES:
                        raise ValueError(
                            "Fallout 3 CG02 reactor gift TRDT differs")
                    response_index = int(row.data[DIALOGUE_RESPONSE_NUMBER_OFFSET])
                elif row.signature == "NAM1" and response_index is not None:
                    response_rows.append((response_index, zstring(row.data)))
                    response_index = None
            if not response_rows or any(not text for _, text in response_rows):
                raise ValueError("Fallout 3 CG02 reactor gift response differs")
            topic_id = next((group.label_u32 for group in info.groups
                             if group.group_type == DIALOGUE_CHILD_GROUP_TYPE), 0)
            nodes.append({"infoFormId": _form_id(info.form_id),
                          "topicFormId": _form_id(topic_id), "engineSex": engine_sex,
                          "responseIndexes": [index for index, _ in response_rows],
                          "linkedTopicFormIds": [_form_id(value) for value in linked],
                          "conditions": conditions, "effects": effects})
            branches.extend({"infoFormId": _form_id(info.form_id),
                             "response": {"index": index, "text": text,
                                          "textSha256": hashlib.sha256(text.encode()).hexdigest()}}
                            for index, text in response_rows)
            for topic_id in linked:
                if topic_id not in seen_topics:
                    seen_topics.add(topic_id)
                    pending.extend(child.form_id for child in info_by_topic.get(topic_id, []))
        topics = []
        for topic_id in sorted(seen_topics):
            topic = by_form.get(topic_id)
            labels = [] if topic is None else [value for value in _text_values(topic, "FULL") if value]
            if topic is None or topic.signature != DIALOGUE_TOPIC_RECORD or len(labels) != 1:
                raise ValueError("Fallout 3 CG02 reactor gift topic differs")
            topics.append({"formId": _form_id(topic_id), "text": labels[0]})
        participants.append({"referenceFormId": _form_id(reference.form_id),
                             "baseFormId": _form_id(base.form_id),
                             "displayName": _text_values(base, "FULL")[0],
                             "greetingInfoFormIds": [_form_id(value) for value in greetings],
                             "dialogue": {"voiceType": {"formId": _form_id(voice.form_id), "editorId": _editor_id(voice)},
                                          "branches": branches, "nodes": nodes, "topics": topics,
                                          "dialoguePlaybackPrepared": False,
                                          "dialoguePlaybackImplemented": False}})
    dad_base = participant_specs[1][1]
    package_ids = {struct.unpack("<I", row.data)[0] for row in iter_subrecords(dad_base)
                   if row.signature == "PKID" and len(row.data) == FORM_ID_BYTES}
    jonas_base = participant_specs[0][1]
    package_ids.update(struct.unpack("<I", row.data)[0] for row in iter_subrecords(jonas_base)
                       if row.signature == "PKID" and len(row.data) == FORM_ID_BYTES)
    packages = {}
    for name in ("jonasGreetPackageFormId", "dadGreetPackageFormId",
                 "dadToRangePackageFormId", "dadWaitPackageFormId",
                 "jonasWaitPackageFormId"):
        package = exact(name, PACKAGE_RECORD)
        if package.form_id not in package_ids:
            raise ValueError("Fallout 3 CG02 reactor gift package ownership differs")
        targets = [(row.signature, row.data) for row in iter_subrecords(package)
                   if row.signature in {"PLDT", "PTDT"}]
        point_targets = [struct.unpack("<IiII", data) for signature, data in targets
                         if signature == "PTDT" and len(data) == struct.calcsize("<IiII")]
        if name in {"jonasGreetPackageFormId", "dadGreetPackageFormId",
                    "dadToRangePackageFormId"} and not any(
                kind == 0 and form_id == player_form_id and count == 0
                for kind, form_id, _, count in point_targets):
            raise ValueError("Fallout 3 CG02 reactor gift player target differs")
        if name == "jonasGreetPackageFormId":
            location_targets = [struct.unpack("<IiI", data)
                                for signature, data in targets
                                if signature == "PLDT" and len(data) == struct.calcsize("<IiI")]
            if not any(kind == 0 and form_id == marker.form_id
                       for kind, form_id, _ in location_targets):
                raise ValueError("Fallout 3 CG02 Jonas greet marker differs")
        package_key = {
            "jonasGreetPackageFormId": "jonasGreet",
            "dadGreetPackageFormId": "dadGreet",
            "dadToRangePackageFormId": "dadToRange",
            "dadWaitPackageFormId": "dadWait",
            "jonasWaitPackageFormId": "jonasWait",
        }[name]
        packages[package_key] = {
            "formId": _form_id(package.form_id), "editorId": _editor_id(package)}
    target_base = exact("targetBaseFormId", ACTIVATOR_RECORD)
    target_script = exact("targetScriptFormId", SCRIPT_RECORD)
    if struct.unpack("<I", _single_subrecord(target_base, "SCRI"))[0] != target_script.form_id:
        raise ValueError("Fallout 3 CG02 target script join differs")
    target_source = _script_source(target_script)
    hit_match = re.search(
        r"begin\s+OnHitWith.*?playgroup\s+(?P<group>\w+)\s+1.*?"
        r"targetCount\s+to\s+CG02\.targetCount\s*\+\s*1.*?"
        r"setstage\s+CGTutorial\s+(?P<tutorial>\d+).*?"
        r"targetCount\s*>=\s*(?P<count>\d+).*?setstage\s+CG02\s+(?P<stage>\d+)",
        target_source, re.IGNORECASE | re.DOTALL)
    if hit_match is None or int(hit_match.group("stage")) != hit_stage:
        raise ValueError("Fallout 3 CG02 target hit script differs")
    target_references = []
    for value in config["targetReferenceFormIds"]:
        reference = by_form.get(int(str(value), FORM_ID_RADIX))
        if reference is None or reference.signature != PLACED_REFERENCE_RECORD or \
                struct.unpack("<I", _single_subrecord(reference, "NAME"))[0] != target_base.form_id:
            raise ValueError("Fallout 3 CG02 target reference differs")
        target_references.append({"referenceFormId": _form_id(reference.form_id),
                                  "sourceTransform": _reference_transform_contract(reference)})
    radroach_reference = exact("radroachReferenceFormId", CREATURE_REFERENCE_RECORD)
    radroach_base = exact("radroachBaseFormId", "CREA")
    radroach_script = exact("radroachScriptFormId", SCRIPT_RECORD)
    radroach_package = exact("radroachGuardPackageFormId", PACKAGE_RECORD)
    radroach_guard_marker = exact("radroachGuardMarkerFormId",
                                  PLACED_REFERENCE_RECORD)
    if struct.unpack("<I", _single_subrecord(radroach_reference, "NAME"))[0] != \
            radroach_base.form_id or \
            struct.unpack("<I", _single_subrecord(radroach_base, "SCRI"))[0] != \
            radroach_script.form_id:
        raise ValueError("Fallout 3 CG02 Radroach identity differs")
    creature_data = _single_subrecord(radroach_base, "DATA")
    if len(creature_data) != CREATURE_DATA_BYTES:
        raise ValueError("Fallout 3 CG02 Radroach DATA layout differs")
    base_health = struct.unpack_from("<H", creature_data, CREATURE_HEALTH_OFFSET)[0]
    if base_health <= 0:
        raise ValueError("Fallout 3 CG02 Radroach health differs")
    if radroach_package.form_id not in {
            struct.unpack("<I", row.data)[0] for row in iter_subrecords(radroach_base)
            if row.signature == "PKID" and len(row.data) == FORM_ID_BYTES}:
        raise ValueError("Fallout 3 CG02 Radroach package ownership differs")
    package_locations = [
        struct.unpack("<IiI", row.data) for row in iter_subrecords(radroach_package)
        if row.signature == "PLDT" and len(row.data) == struct.calcsize("<IiI")]
    if not any(kind == 0 and target == radroach_guard_marker.form_id and radius > 0
               for kind, target, radius in package_locations):
        raise ValueError("Fallout 3 CG02 Radroach guard target differs")
    guard_radius = next(radius for kind, target, radius in package_locations
                        if kind == 0 and target == radroach_guard_marker.form_id)
    death_match = re.search(
        r"begin\s+OnDeath.*?setstage\s+CG02\s+(?P<stage>\d+)",
        _script_source(radroach_script), re.IGNORECASE | re.DOTALL)
    if death_match is None or int(death_match.group("stage")) != death_stage:
        raise ValueError("Fallout 3 CG02 Radroach death result differs")
    weapon = exact("bbGunFormId", "WEAP")
    ammo = exact("bbAmmoFormId", "AMMO")
    weapon_data = _single_subrecord(weapon, "DATA")
    if len(weapon_data) != WEAPON_DATA_BYTES or \
            struct.unpack("<I", _single_subrecord(weapon, "NAM0"))[0] != ammo.form_id:
        raise ValueError("Fallout 3 CG02 BB-gun combat data differs")
    weapon_damage = struct.unpack_from("<H", weapon_data, WEAPON_DAMAGE_OFFSET)[0]
    clip_size = weapon_data[WEAPON_CLIP_SIZE_OFFSET]
    if weapon_damage <= 0 or clip_size <= 0:
        raise ValueError("Fallout 3 CG02 BB-gun damage contract differs")
    picture_stage = int(config["pictureStage"])
    picture_timer_stage = int(config["pictureTimerStage"])
    picture_source_commands = [command for source in stage_sources.get(
        completion_stage, []) for command in _source_commands(source)]
    picture_stage_commands = [command for source in stage_sources.get(
        picture_stage, []) for command in _source_commands(source)]
    objective_match = next((re.fullmatch(
        r"setObjectiveDisplayed\s+CG02\s+(?P<objective>\d+)\s+1",
        command, re.IGNORECASE) for command in picture_source_commands
        if command.casefold().startswith("setobjectivedisplayed")), None)
    dad_say = next((re.fullmatch(
        r"CG02DadREF\.SayTo\s+CG02JonasREF\s+CG02DadSpeech\s+1",
        command, re.IGNORECASE) for command in picture_source_commands
        if ".sayto" in command.casefold()), None)
    objective_done = next((re.fullmatch(
        r"setObjectiveCompleted\s+CG02\s+(?P<objective>\d+)\s+1",
        command, re.IGNORECASE) for command in picture_stage_commands
        if command.casefold().startswith("setobjectivecompleted")), None)
    disable_controls = any(re.fullmatch(
        r"DisablePlayerControls", command, re.IGNORECASE)
        for command in picture_stage_commands)
    dad_talk = next((re.fullmatch(
        r"set\s+CG02DadREF\.doTalk\s+to\s+(?P<value>\d+)",
        command, re.IGNORECASE) for command in picture_stage_commands
        if ".dotalk" in command.casefold()), None)
    jonas_say = next((re.fullmatch(
        r"CG02JonasREF\.SayTo\s+player\s+CG02JonasSpeech\s+1",
        command, re.IGNORECASE) for command in picture_stage_commands
        if ".sayto" in command.casefold()), None)
    if objective_match is None or dad_say is None or objective_done is None or \
            not disable_controls or dad_talk is None or jonas_say is None or \
            objective_match.group("objective") != objective_done.group("objective"):
        raise ValueError("Fallout 3 CG02 picture stage results differ")
    picture_packages = []
    for package_name, marker_name, actor_index in (
            ("dadPicturePackageFormId", "dadPictureMarkerFormId", 1),
            ("jonasPicturePackageFormId", "jonasPictureMarkerFormId", 0)):
        package = exact(package_name, PACKAGE_RECORD)
        marker = exact(marker_name, PLACED_REFERENCE_RECORD)
        actor_base = participant_specs[actor_index][1]
        actor_packages = {struct.unpack("<I", row.data)[0]
                          for row in iter_subrecords(actor_base)
                          if row.signature == "PKID" and
                          len(row.data) == FORM_ID_BYTES}
        if package.form_id not in actor_packages:
            raise ValueError("Fallout 3 CG02 picture package ownership differs")
        location_targets = [struct.unpack("<IiI", row.data)
                            for row in iter_subrecords(package)
                            if row.signature == "PLDT" and
                            len(row.data) == struct.calcsize("<IiI")]
        matched_targets = [(form_id, radius) for kind, form_id, radius
                           in location_targets
                           if kind == 0 and form_id == marker.form_id]
        if len(matched_targets) != 1:
            raise ValueError("Fallout 3 CG02 picture package target differs")
        picture_packages.append({
            "formId": _form_id(package.form_id),
            "actorReferenceFormId": _form_id(participant_specs[actor_index][0].form_id),
            "targetMarkerFormId": _form_id(marker.form_id),
            "targetTransform": _reference_transform_contract(marker),
            "radiusGameUnits": matched_targets[0][1],
        })
    dad_script = exact("dadScriptFormId", SCRIPT_RECORD)
    jonas_script = exact("jonasScriptFormId", SCRIPT_RECORD)
    dad_package = int(str(config["dadPicturePackageFormId"]), FORM_ID_RADIX)
    jonas_package = int(str(config["jonasPicturePackageFormId"]), FORM_ID_RADIX)
    dad_source = _script_source(dad_script)
    jonas_source = _script_source(jonas_script)
    dad_package_block = re.search(
        rf"begin\s+OnPackageDone\s+{re.escape(_editor_id(by_form[dad_package]) or '')}"
        r"(?P<body>.*?)\bend\b", dad_source, re.IGNORECASE | re.DOTALL)
    jonas_package_block = re.search(
        rf"begin\s+OnPackageDone\s+{re.escape(_editor_id(by_form[jonas_package]) or '')}"
        r"(?P<body>.*?)\bend\b", jonas_source, re.IGNORECASE | re.DOTALL)
    dad_ready = re.search(
        rf"begin\s+OnPackageDone\s+{re.escape(_editor_id(by_form[dad_package]) or '')}"
        rf".*?set\s+CG02\.DadReady\s+to\s+(?P<ready>\d+)"
        rf".*?set\s+doTalk\s+to\s+(?P<talk>\d+)"
        rf".*?set\s+timer\s+to\s+(?P<timer>[\d.]+)",
        dad_source, re.IGNORECASE | re.DOTALL)
    jonas_ready = re.search(
        rf"begin\s+OnPackageDone\s+{re.escape(_editor_id(by_form[jonas_package]) or '')}"
        rf".*?set\s+CG02\.JonasReady\s+to\s+(?P<ready>\d+)",
        jonas_source, re.IGNORECASE | re.DOTALL)
    jonas_info = int(str(config["jonasPictureInfoFormId"]), FORM_ID_RADIX)
    jonas_topic = next((group.label_u32 for group in by_form[jonas_info].groups
                        if group.group_type == DIALOGUE_CHILD_GROUP_TYPE), 0)
    jonas_say_done = re.search(
        rf"begin\s+SayToDone\s+{re.escape(_editor_id(by_form[jonas_topic]) or '')}"
        rf".*?getStage\s+CG02\s*==\s*(?P<stage>\d+)"
        rf".*?setstage\s+CG02\s+(?P<target>\d+)",
        jonas_source, re.IGNORECASE | re.DOTALL)
    if dad_package_block is None or jonas_package_block is None or \
            dad_ready is None or jonas_ready is None or jonas_say_done is None or \
            int(dad_ready.group("ready")) != 1 or \
            int(dad_ready.group("talk")) != 1 or \
            float(dad_ready.group("timer")) <= 0 or \
            int(jonas_ready.group("ready")) != 1 or \
            int(jonas_say_done.group("stage")) != picture_stage or \
            int(jonas_say_done.group("target")) != picture_timer_stage:
        raise ValueError("Fallout 3 CG02 picture package result differs")
    picture_packages[0]["completionCommandCount"] = len(re.findall(
        r"\bset\s+", dad_package_block.group("body"), re.IGNORECASE))
    picture_packages[1]["completionCommandCount"] = len(re.findall(
        r"\bset\s+", jonas_package_block.group("body"), re.IGNORECASE))
    if any(int(value["completionCommandCount"]) <= 0
           for value in picture_packages):
        raise ValueError("Fallout 3 CG02 picture package command inventory differs")
    flash_stage = int(config["photoFlashStage"])
    completion_target_stage = int(config["finalStage"])
    stage95_commands = [command for source in stage_sources.get(
        picture_timer_stage, []) for command in _source_commands(source)]
    stage98_commands = [command for source in stage_sources.get(
        flash_stage, []) for command in _source_commands(source)]
    stage100_commands = [command for source in stage_sources.get(
        completion_target_stage, []) for command in _source_commands(source)]
    stage95_timer = next((re.fullmatch(
        r"set\s+CG02\.timer\s+to\s+(?P<value>[\d.]+)", command,
        re.IGNORECASE) for command in stage95_commands
        if ".timer" in command.casefold()), None)
    stage95_run = next((re.fullmatch(
        r"set\s+CG02\.runTimer\s+to\s+(?P<value>\d+)", command,
        re.IGNORECASE) for command in stage95_commands
        if ".runtimer" in command.casefold()), None)
    unequip = next((re.fullmatch(
        r"player\.unequipitem\s+(?P<item>\w+)\s+(?P<prevent>\d+)\s+(?P<silent>\d+)",
        command, re.IGNORECASE) for command in stage95_commands
        if ".unequipitem" in command.casefold()), None)
    if len(stage95_commands) != 4 or \
            re.fullmatch(r"CompleteAllObjectives\s+CG02", stage95_commands[0],
                         re.IGNORECASE) is None or \
            stage95_timer is None or stage95_run is None or unequip is None or \
            int(stage95_run.group("value")) != 1:
        raise ValueError("Fallout 3 CG02 stage 95 result differs")
    unequip_item = by_editor.get(unequip.group("item").casefold())
    if unequip_item is None or unequip_item.form_id != weapon.form_id:
        raise ValueError("Fallout 3 CG02 stage 95 weapon identity differs")
    modifier = exact("cameraFlashImageSpaceModifierFormId",
                     IMAGE_SPACE_MODIFIER_RECORD)
    fade_sound = exact("fadeSoundFormId", SOUND_RECORD)
    modifier_command = next((IMAGE_SPACE_MODIFIER_PATTERN.fullmatch(command)
                             for command in stage98_commands
                             if command.casefold().startswith("imod ")), None)
    sound_command = next((PLAY_SOUND_PATTERN.fullmatch(command)
                          for command in stage98_commands
                          if command.casefold().startswith("playsound ")), None)
    stage98_timer = next((re.fullmatch(
        r"set\s+CG02\.timer\s+to\s+(?P<value>[\d.]+)", command,
        re.IGNORECASE) for command in stage98_commands
        if ".timer" in command.casefold()), None)
    stage98_run = next((re.fullmatch(
        r"set\s+CG02\.runTimer\s+to\s+(?P<value>\d+)", command,
        re.IGNORECASE) for command in stage98_commands
        if ".runtimer" in command.casefold()), None)
    if len(stage98_commands) != 4 or modifier_command is None or \
            sound_command is None or stage98_timer is None or stage98_run is None or \
            int(stage98_run.group("value")) != 1 or \
            by_editor[modifier_command.group("modifier").casefold()].form_id != \
                modifier.form_id or \
            by_editor[sound_command.group("sound").casefold()].form_id != \
                fade_sound.form_id:
        raise ValueError("Fallout 3 CG02 stage 98 result differs")
    sound_paths = _text_values(fade_sound, "FNAM")
    sound_data = [row.data for row in iter_subrecords(fade_sound)
                  if row.signature == "SNDD"]
    if len(sound_paths) != 1 or len(sound_data) != 1:
        raise ValueError("Fallout 3 CG02 camera sound layout differs")
    quest_script = by_form[struct.unpack("<I", _single_subrecord(quest, "SCRI"))[0]]
    quest_script_source = _script_source(quest_script)
    timer_targets = re.findall(
        r"(?:if|elseif)\s+getStage\s+CG02\s*==\s*(?P<source>\d+)\s*"
        r"setstage\s+CG02\s+(?P<target>\d+)",
        quest_script_source, re.IGNORECASE | re.DOTALL)
    if (str(picture_timer_stage), str(flash_stage)) not in timer_targets or \
            (str(flash_stage), str(completion_target_stage)) not in timer_targets or \
            re.search(r"if\s+runTimer\s*==\s*1.*?timer\s*>\s*0.*?"
                      r"timer\s+to\s+timer\s*-\s*GetSecondsPassed",
                      quest_script_source, re.IGNORECASE | re.DOTALL) is None:
        raise ValueError("Fallout 3 CG02 completion timer chain differs")
    next_quest = exact("nextQuestFormId", QUEST_RECORD)
    next_marker = exact("nextQuestStartMarkerFormId", PLACED_REFERENCE_RECORD)
    next_stage_sources: dict[int, list[str]] = {}
    next_stage = None
    for row in iter_subrecords(next_quest):
        if row.signature == "INDX":
            next_stage = int.from_bytes(row.data, "little")
        elif row.signature == "SCTX" and next_stage is not None:
            next_stage_sources.setdefault(next_stage, []).append(zstring(row.data))
    next_entry_stage = int(config["nextQuestEntryStage"])
    next_target_stage = int(config["nextQuestTargetStage"])
    next_commands = [command for source in next_stage_sources.get(next_entry_stage, [])
                     for command in _source_commands(source)]
    if len(next_commands) != 2 or \
            (next_set := SET_STAGE_PATTERN.fullmatch(next_commands[0])) is None or \
            int(next_set.group("stage")) != next_target_stage or \
            by_editor[next_set.group("quest").casefold()].form_id != next_quest.form_id or \
            (next_move := MOVE_TO_REFERENCE_PATTERN.fullmatch(next_commands[1])) is None or \
            next_move.group("subject").casefold() != "player" or \
            by_editor[next_move.group("target").casefold()].form_id != next_marker.form_id:
        raise ValueError("Fallout 3 CG03 stage 0 transition differs")
    dad_reference = participant_specs[1][0]
    beatrice = exact("beatriceReferenceFormId", ACTOR_REFERENCE_RECORD)
    skill_book = exact("skillBookFormId", "BOOK")
    dresser = exact("nextDresserReferenceFormId", PLACED_REFERENCE_RECORD)
    vault_suit = exact("adultVaultSuitFormId", "ARMO")
    stage100_text = "\n".join(stage100_commands)
    required_stage100_joins = {
        "stopquest CG02": quest.form_id,
        "CG02DadRef.disable": dad_reference.form_id,
        "CG02BeatriceREF.disable": beatrice.form_id,
        "CG04PlayerDresserREF.additem BookSkillMelee 1": dresser.form_id,
        "player.additem vaultsuit101 1 1": vault_suit.form_id,
        "player.equipitem vaultsuit101 0 1": vault_suit.form_id,
        "setstage CG03 0": next_quest.form_id,
    }
    required_stage100_commands = {
        "enablePlayerControls", "PipBoyRadioOff", "endif",
        "player.removeallitems", "player.AgeRace 1",
    }
    if any(
            expected.casefold() not in {value.casefold()
                                       for value in stage100_commands}
            for expected in (*required_stage100_joins,
                             *required_stage100_commands)) or \
            "if player.getItemCount BookSkillMelee > 0".casefold() not in \
                stage100_text.casefold() or \
            by_editor["bookskillmelee"].form_id != skill_book.form_id:
        raise ValueError("Fallout 3 CG02 stage 100 result differs")
    completion_runtime = {
        "schema": "opennv-fo3-cg02-stage-95-completion-runtime/v1",
        "timerStage": picture_timer_stage,
        "flashStage": flash_stage,
        "completionStage": completion_target_stage,
        "stage95TimerSeconds": float(stage95_timer.group("value")),
        "stage98TimerSeconds": float(stage98_timer.group("value")),
        "stage95CommandCount": len(stage95_commands),
        "stage98CommandCount": len(stage98_commands),
        "stage100CommandCount": len(stage100_commands),
        "unequipItemFormId": _form_id(unequip_item.form_id),
        "unequipPrevent": int(unequip.group("prevent")),
        "unequipSilent": int(unequip.group("silent")),
        "imageSpaceModifier": {
            **parse_image_space_modifier(modifier).manifest(),
            "formId": _form_id(modifier.form_id),
        },
        "sound": {
            "formId": _form_id(fade_sound.form_id),
            "editorId": _editor_id(fade_sound),
            "logicalPath": canonical_member_path(f"sound\\{sound_paths[0]}"),
            "recordSha256": hashlib.sha256(fade_sound.data).hexdigest(),
            "soundDataSha256": hashlib.sha256(sound_data[0]).hexdigest(),
        },
        "dadReferenceFormId": _form_id(dad_reference.form_id),
        "beatriceReferenceFormId": _form_id(beatrice.form_id),
        "skillBookFormId": _form_id(skill_book.form_id),
        "nextDresserReferenceFormId": _form_id(dresser.form_id),
        "adultVaultSuitFormId": _form_id(vault_suit.form_id),
        "nextQuestFormId": _form_id(next_quest.form_id),
        "nextQuestEditorId": _editor_id(next_quest),
        "nextQuestEntryStage": next_entry_stage,
        "nextQuestTargetStage": next_target_stage,
        "nextQuestStage0CommandCount": len(next_commands),
        "nextQuestStartMarkerFormId": _form_id(next_marker.form_id),
        "nextQuestStartTransform": _reference_transform_contract(next_marker),
        "nextBoundary": {"applied": False,
                         "blocker": "fo3-cg03-stage-5-runtime-not-implemented"},
    }
    trigger_base = exact("playerPictureTriggerBaseFormId", ACTIVATOR_RECORD)
    trigger_script = exact("playerPictureTriggerScriptFormId", SCRIPT_RECORD)
    if struct.unpack("<I", _single_subrecord(trigger_base, "SCRI"))[0] != \
            trigger_script.form_id:
        raise ValueError("Fallout 3 CG02 picture trigger script join differs")
    trigger_source = _script_source(trigger_script)
    trigger_match = re.search(
        r"getHeadingAngle\s+CG02JonasREF.*?angle\s*>=\s*(?P<minimum>-?\d+)"
        r"\s*&&\s*angle\s*<=\s*(?P<maximum>-?\d+).*?"
        r"set\s+CG02\.PlayerReady\s+to\s+(?P<ready>\d+).*?"
        r"begin\s+OnTriggerLeave\s+player.*?"
        r"set\s+CG02\.PlayerReady\s+to\s+(?P<leave>\d+)",
        trigger_source, re.IGNORECASE | re.DOTALL)
    if trigger_match is None or int(trigger_match.group("ready")) != 1 or \
            int(trigger_match.group("leave")) != 0:
        raise ValueError("Fallout 3 CG02 picture trigger result differs")
    picture_triggers = []
    for value in config["playerPictureTriggerReferenceFormIds"]:
        reference = by_form.get(int(str(value), FORM_ID_RADIX))
        if reference is None or reference.signature != PLACED_REFERENCE_RECORD or \
                struct.unpack("<I", _single_subrecord(reference, "NAME"))[0] != \
                trigger_base.form_id:
            raise ValueError("Fallout 3 CG02 picture trigger identity differs")
        primitive_data = _single_subrecord(reference, "XPRM")
        if len(primitive_data) != TRIGGER_PRIMITIVE_BYTES:
            raise ValueError("Fallout 3 CG02 picture trigger primitive differs")
        primitive = struct.unpack(f"<{TRIGGER_PRIMITIVE_FLOATS}fI", primitive_data)
        if not all(math.isfinite(component) and component > 0
                   for component in primitive[:3]):
            raise ValueError("Fallout 3 CG02 picture trigger dimensions differ")
        picture_triggers.append({
            "referenceFormId": _form_id(reference.form_id),
            "sourceTransform": _reference_transform_contract(reference),
            "dimensionsGameUnits": {"x": primitive[0], "y": primitive[1],
                                    "z": primitive[2]},
        })
    resolved_stages = {}
    for stage in (jonas_stage, target_stage, range_stage, hit_stage,
                  combat_stage, death_stage):
        rows = []
        for index, command in enumerate(command for source in stage_sources.get(stage, [])
                                        for command in _source_commands(source)):
            row: dict[str, object] = {"index": index}
            if match := re.fullmatch(r"(?P<subject>\w+)\.removeitem\s+(?P<item>\w+)\s+(?P<count>\d+)", command, re.IGNORECASE):
                row.update(kind="removeItem", referenceFormId=_form_id(by_editor[match.group("subject").casefold()].form_id), itemFormId=_form_id(by_editor[match.group("item").casefold()].form_id), count=int(match.group("count")))
            elif match := re.fullmatch(r"(?P<subject>\w+)\.moveto\s+(?P<marker>\w+)", command, re.IGNORECASE):
                marker = by_editor[match.group("marker").casefold()]
                row.update(kind="moveToReference", referenceFormId=_form_id(by_editor[match.group("subject").casefold()].form_id), targetFormId=_form_id(marker.form_id), targetTransform=_reference_transform_contract(marker))
            elif match := re.fullmatch(r"(?P<subject>\w+)\.SetOpenState\s+(?P<value>\d+)", command, re.IGNORECASE):
                row.update(kind="setOpenState", referenceFormId=_form_id(by_editor[match.group("subject").casefold()].form_id), value=int(match.group("value")))
            elif match := re.fullmatch(r"(?P<subject>\w+)\.Lock\s+(?P<value>\d+)", command, re.IGNORECASE):
                row.update(kind="lock", referenceFormId=_form_id(by_editor[match.group("subject").casefold()].form_id), value=int(match.group("value")))
            elif match := re.fullmatch(r"player\.additem\s+(?P<item>\w+)\s+(?P<count>\d+)", command, re.IGNORECASE):
                row.update(kind="addItem", itemFormId=_form_id(by_editor[match.group("item").casefold()].form_id), count=int(match.group("count")))
            elif match := re.fullmatch(r"player\.equipitem\s+(?P<item>\w+)", command, re.IGNORECASE):
                row.update(kind="equipItem", itemFormId=_form_id(by_editor[match.group("item").casefold()].form_id))
            elif match := re.fullmatch(r"(?P<subject>\w+)\.unlock", command, re.IGNORECASE):
                row.update(kind="unlock", referenceFormId=_form_id(by_editor[match.group("subject").casefold()].form_id))
            elif match := re.fullmatch(r"EnablePlayerControls\s+(?P<args>[\d\s]+)", command, re.IGNORECASE):
                row.update(kind="enablePlayerControls", arguments=[int(value) for value in match.group("args").split()])
            elif match := re.fullmatch(r"setObjectiveCompleted\s+CG02\s+(?P<objective>\d+)\s+(?P<value>\d+)", command, re.IGNORECASE):
                row.update(kind="setObjectiveCompleted", objectiveIndex=int(match.group("objective")), value=int(match.group("value")))
            elif match := SET_OBJECTIVE_DISPLAYED_PATTERN.fullmatch(command):
                row.update(kind="setObjectiveDisplayed", objectiveIndex=int(match.group("index")), value=int(match.group("value")))
            elif match := SET_STAGE_PATTERN.fullmatch(command):
                target_quest = by_editor.get(match.group("quest").casefold())
                if target_quest is None or target_quest.signature != QUEST_RECORD:
                    raise ValueError("Fallout 3 CG02 target tutorial quest differs")
                row.update(kind="setStage", questFormId=_form_id(target_quest.form_id),
                           stage=int(match.group("stage")))
            elif match := re.fullmatch(r"(?P<subject>\w+)\.enable", command, re.IGNORECASE):
                row.update(kind="enable", referenceFormId=_form_id(by_editor[match.group("subject").casefold()].form_id))
            elif match := re.fullmatch(r"(?P<subject>\w+)\.evp", command, re.IGNORECASE):
                row.update(kind="evaluatePackage", referenceFormId=_form_id(by_editor[match.group("subject").casefold()].form_id))
            elif match := re.fullmatch(r"SetQuestObject\s+(?P<item>\w+)\s+(?P<value>\d+)", command, re.IGNORECASE):
                row.update(kind="setQuestObject",
                           itemFormId=_form_id(by_editor[match.group("item").casefold()].form_id),
                           value=int(match.group("value")))
            else:
                raise ValueError(f"Fallout 3 CG02 reactor gift stage result differs: {command}")
            rows.append(row)
        resolved_stages[str(stage)] = {"commands": rows}
    return {"schema": "opennv-fo3-cg02-stage-40-reactor-gift-runtime/v1",
            "sourceStage": source_stage, "jonasStage": jonas_stage,
            "targetStage": target_stage, "rangeStage": range_stage,
            "hitStage": hit_stage, "combatStage": combat_stage,
            "deathStage": death_stage, "completionStage": completion_stage,
            "participants": participants,
            "packages": packages, "stageResults": resolved_stages,
            "targets": {"baseFormId": _form_id(target_base.form_id),
                        "scriptFormId": _form_id(target_script.form_id),
                        "requiredWeaponFormId": _form_id(
                            int(str(config["bbGunFormId"]), FORM_ID_RADIX)),
                        "references": target_references,
                        "animationGroup": hit_match.group("group"),
                        "requiredHitCount": int(hit_match.group("count")),
                        "tutorialStage": int(hit_match.group("tutorial"))},
            "combat": {"referenceFormId": _form_id(radroach_reference.form_id),
                       "playerReferenceFormId": _form_id(player_form_id),
                       "baseFormId": _form_id(radroach_base.form_id),
                       "scriptFormId": _form_id(radroach_script.form_id),
                       "packageFormId": _form_id(radroach_package.form_id),
                       "packageTargetFormId": _form_id(radroach_guard_marker.form_id),
                       "packageRadiusGameUnits": guard_radius,
                       "maximumHealth": base_health,
                       "weaponFormId": _form_id(weapon.form_id),
                       "ammunitionFormId": _form_id(ammo.form_id),
                       "weaponDamage": weapon_damage,
                       "clipSize": clip_size,
                       "deathStage": death_stage},
            "pictureRuntime": {
                "schema": "opennv-fo3-cg02-stage-80-picture-runtime/v1",
                "sourceStage": completion_stage,
                "pictureStage": picture_stage,
                "timerStage": picture_timer_stage,
                "dadInfoFormId": str(config["dadPictureInfoFormId"]),
                "jonasInfoFormId": str(config["jonasPictureInfoFormId"]),
                "packages": picture_packages,
                "triggers": picture_triggers,
                "minimumHeadingDegrees": int(trigger_match.group("minimum")),
                "maximumHeadingDegrees": int(trigger_match.group("maximum")),
                "dadReadyValue": int(dad_ready.group("ready")),
                "jonasReadyValue": int(jonas_ready.group("ready")),
                "playerReadyValue": int(trigger_match.group("ready")),
                "dadTalkValue": int(dad_ready.group("talk")),
                "dadTimerSeconds": float(dad_ready.group("timer")),
                "objectiveIndex": int(objective_match.group("objective")),
                "pictureDadTalkValue": int(dad_talk.group("value")),
                "sourceStageCommandCount": len(picture_source_commands),
                "pictureStageCommandCount": len(picture_stage_commands),
                "completionRuntime": completion_runtime,
                "nextBoundary": {"applied": False,
                                 "blocker": "fo3-cg03-stage-5-runtime-not-implemented"},
            },
            "nextBoundary": {"applied": False,
                             "blocker": "fo3-cg03-stage-5-runtime-not-implemented"}}


def _compile_cg02_cake_runtime(
    records: tuple[object, ...],
    quest: object,
    stage_sources: dict[int, list[str]],
    config: dict[str, object],
) -> dict[str, object]:
    by_form = {record.form_id: record for record in records}
    def exact(name: str, signature: str) -> object:
        record = by_form.get(int(str(config[name]), FORM_ID_RADIX))
        if record is None or record.signature != signature:
            raise ValueError(f"Fallout 3 CG02 cake {name} differs")
        return record
    trigger = exact("triggerReferenceFormId", PLACED_REFERENCE_RECORD)
    trigger_base = exact("triggerBaseFormId", ACTIVATOR_RECORD)
    trigger_script = exact("triggerScriptFormId", SCRIPT_RECORD)
    if (struct.unpack("<I", _single_subrecord(trigger, "NAME"))[0] != trigger_base.form_id or
        struct.unpack("<I", _single_subrecord(trigger_base, "SCRI"))[0] != trigger_script.form_id):
        raise ValueError("Fallout 3 CG02 cake trigger join differs")
    trigger_stage = int(config["triggerStage"])
    trigger_source = _script_source(trigger_script)
    trigger_match = re.search(
        rf"begin\s+OnTriggerEnter\s+player\s+SetStage\s+{re.escape(_editor_id(quest) or '')}\s+{trigger_stage}\s+end",
        trigger_source, re.IGNORECASE | re.DOTALL)
    if trigger_match is None:
        raise ValueError("Fallout 3 CG02 cake trigger script differs")
    primitive_data = _single_subrecord(trigger, "XPRM")
    if len(primitive_data) != TRIGGER_PRIMITIVE_BYTES:
        raise ValueError("Fallout 3 CG02 cake trigger primitive differs")
    primitive = struct.unpack(f"<{TRIGGER_PRIMITIVE_FLOATS}fI", primitive_data)
    if (not all(math.isfinite(value) and value > 0 for value in primitive[:3]) or
        primitive[TRIGGER_PRIMITIVE_TYPE_INDEX] !=
            TRIGGER_PRIMITIVE_ORIENTED_BOX_TYPE):
        raise ValueError("Fallout 3 CG02 cake trigger dimensions differ")

    andy_reference = exact("andyReferenceFormId", CREATURE_REFERENCE_RECORD)
    andy_base = exact("andyBaseFormId", "CREA")
    package = exact("packageFormId", PACKAGE_RECORD)
    marker = exact("targetMarkerFormId", PLACED_REFERENCE_RECORD)
    cake_reference = exact("cakeReferenceFormId", PLACED_REFERENCE_RECORD)
    topic = exact("topicFormId", DIALOGUE_TOPIC_RECORD)
    if struct.unpack("<I", _single_subrecord(andy_reference, "NAME"))[0] != andy_base.form_id:
        raise ValueError("Fallout 3 CG02 cake Andy actor differs")
    package_ids = [struct.unpack("<I", row.data)[0] for row in iter_subrecords(andy_base)
                   if row.signature == "PKID" and len(row.data) == FORM_ID_BYTES]
    locations = [row.data for row in iter_subrecords(package) if row.signature == "PLDT"]
    if len(locations) != 1 or len(locations[0]) != struct.calcsize("<IiI"):
        raise ValueError("Fallout 3 CG02 cake package location differs")
    location_kind, location_form_id, location_radius = struct.unpack("<IiI", locations[0])
    if package.form_id not in package_ids or location_kind != 0 or \
            location_form_id != marker.form_id or location_radius < 0:
        raise ValueError("Fallout 3 CG02 cake package target differs")
    package_sources = _text_values(package, "SCTX")
    package_commands = [command for source in package_sources for command in _source_commands(source)]
    target_stage = int(config["targetStage"])
    if package_commands[-1].casefold() != f"setstage {_editor_id(quest)} {target_stage}".casefold():
        raise ValueError("Fallout 3 CG02 cake package result differs")
    idle_ids = [struct.unpack("<I", row.data)[0] for row in iter_subrecords(package)
                if row.signature == "INAM" and len(row.data) == FORM_ID_BYTES and
                struct.unpack("<I", row.data)[0] != 0]
    idle = by_form.get(idle_ids[0]) if len(idle_ids) == 1 else None
    idle_models = [] if idle is None else _text_values(idle, "MODL")
    if idle is None or idle.signature != IDLE_RECORD or len(idle_models) != 1:
        raise ValueError("Fallout 3 CG02 cake package idle differs")
    recipe = actor_preparation.load_recipe(str(config["andyActorRecipeId"]))
    if (str(recipe["proofActorReferenceFormId"]).casefold() != _form_id(andy_reference.form_id) or
        str(recipe["expectedBaseFormId"]).casefold() != _form_id(andy_base.form_id)):
        raise ValueError("Fallout 3 CG02 cake Andy recipe differs")

    info_ids = [int(str(value), FORM_ID_RADIX) for value in config["infoFormIds"]]
    infos = [exact_info for info_id in info_ids
             for exact_info in [by_form.get(info_id)]
             if exact_info is not None and exact_info.signature == DIALOGUE_INFO_RECORD]
    if len(infos) != len(info_ids) or any(not any(
        group.group_type == DIALOGUE_CHILD_GROUP_TYPE and group.label_u32 == topic.form_id
        for group in info.groups) for info in infos):
        raise ValueError("Fallout 3 CG02 cake dialogue identity differs")
    cues = []
    for sequence, info in enumerate(infos):
        conditions = [_dialogue_condition(row.data) for row in iter_subrecords(info)
                      if row.signature == "CTDA"]
        identities = [int(row["parameter1"]) for row in conditions
                      if int(row["function"]) == GET_IS_ID_FUNCTION]
        if len(identities) != 1:
            raise ValueError("Fallout 3 CG02 cake dialogue speaker differs")
        speaker = by_form.get(identities[0])
        if speaker is None or speaker.signature not in {NPC_RECORD, "CREA"}:
            raise ValueError("Fallout 3 CG02 cake speaker base differs")
        voice_ids = [struct.unpack("<I", row.data)[0] for row in iter_subrecords(speaker)
                     if row.signature == "VTCK" and len(row.data) == FORM_ID_BYTES]
        voice = by_form.get(voice_ids[0]) if len(voice_ids) == 1 else None
        responses = [value for value in _text_values(info, "NAM1") if value]
        if voice is None or voice.signature != VOICE_TYPE_RECORD or len(responses) != 1:
            raise ValueError("Fallout 3 CG02 cake dialogue response differs")
        response = responses[0]
        cues.append({
            "sequence": sequence,
            "speakerBaseFormId": _form_id(speaker.form_id),
            "voiceType": {"formId": _form_id(voice.form_id), "editorId": _editor_id(voice)},
            "infoFormId": _form_id(info.form_id),
            "response": {"index": 1, "text": response,
                         "textSha256": hashlib.sha256(response.encode()).hexdigest()},
            "effects": [command for source in _text_values(info, "SCTX")
                        for command in _source_commands(source)],
        })
    stage15_commands = [command for source in stage_sources.get(trigger_stage, [])
                        for command in _source_commands(source)]
    stage16_commands = [command for source in stage_sources.get(target_stage, [])
                        for command in _source_commands(source)]
    if len(stage15_commands) != 2 or len(stage16_commands) != 3:
        raise ValueError("Fallout 3 CG02 cake stage command count differs")
    return {
        "schema": "opennv-fo3-cg02-stage-12-cake-runtime/v1",
        "sourceStage": int(config["sourceStage"]), "triggerStage": trigger_stage,
        "targetStage": target_stage,
        "trigger": {"referenceFormId": _form_id(trigger.form_id),
                    "baseFormId": _form_id(trigger_base.form_id),
                    "scriptFormId": _form_id(trigger_script.form_id),
                    "sourceTransform": _reference_transform_contract(trigger),
                    "dimensionsGameUnits": list(primitive[:3]),
                    "primitiveType": int(primitive[TRIGGER_PRIMITIVE_TYPE_INDEX])},
        "andy": {"referenceFormId": _form_id(andy_reference.form_id),
                 "baseFormId": _form_id(andy_base.form_id),
                 "actorRecipeId": str(config["andyActorRecipeId"])},
        "package": {"formId": _form_id(package.form_id),
                    "targetMarkerFormId": _form_id(marker.form_id),
                    "targetTransform": _reference_transform_contract(marker),
                    "radiusGameUnits": location_radius,
                    "locomotion": {"logicalPath": canonical_member_path(
                        str(config["locomotionPath"])),
                        "rootNode": str(recipe["locomotionRootNode"])},
                    "idle": {"formId": _form_id(idle.form_id),
                             "modelPath": canonical_member_path(
                                 f"meshes\\{idle_models[0]}")},
                    "resultCommands": package_commands},
        "cakeReferenceFormId": _form_id(cake_reference.form_id),
        "dialogue": {"cues": cues, "dialoguePlaybackPrepared": False,
                     "dialoguePlaybackImplemented": False},
        "stage15Commands": stage15_commands,
        "stage16Commands": stage16_commands,
        "nextBoundary": {"applied": False,
                         "blocker": "fo3-cg02-stage-20-butch-sweetroll-runtime-not-implemented"},
    }


def _compile_cg01_post_stage14_transition(
    records: tuple[object, ...],
    definition: dict[str, object],
    quest: object,
    stage_sources: dict[int, list[str]],
    dad_reference: object,
    dad_base: object,
    dad_script: object,
    topic: object,
) -> dict[str, object]:
    config = dict(definition["postStage14Transition"])
    stages = [
        int(definition["stage12DadResponseTargetStage"]),
        int(config["stage16"]),
        int(config["stage18"]),
        int(config["stage20"]),
    ]
    if stages != sorted(set(stages)):
        raise ValueError("Fallout 3 CG01 post-stage-14 stage order differs")
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    def exact(raw: object, signature: str, label: str) -> object:
        form_id = int(str(raw), FORM_ID_RADIX)
        record = by_form.get(form_id)
        if record is None or record.signature != signature:
            raise ValueError(f"Fallout 3 CG01 {label} is absent")
        return record

    def package_contract(package_key: str, target_key: str, stage: int) -> dict[str, object]:
        package = exact(config[package_key], PACKAGE_RECORD, package_key)
        target = exact(config[target_key], PLACED_REFERENCE_RECORD, target_key)
        package_ids = [
            struct.unpack("<I", row.data)[0]
            for row in iter_subrecords(dad_base)
            if row.signature == "PKID" and len(row.data) == FORM_ID_BYTES
        ]
        if package.form_id not in package_ids:
            raise ValueError(f"Fallout 3 CG01 {package_key} is not owned by Dad")
        conditions = [
            _dialogue_condition(row.data)
            for row in iter_subrecords(package)
            if row.signature == "CTDA"
        ]
        if len(conditions) != 1:
            raise ValueError(f"Fallout 3 CG01 {package_key} condition differs")
        condition = conditions[0]
        if (
            condition["operatorFlags"] != CONDITION_EQUAL_OPERATOR_FLAGS
            or condition["comparisonValue"] != float(stage)
            or condition["function"] != GET_STAGE_FUNCTION
            or condition["parameter1"] != quest.form_id
            or condition["parameter2"] != 0
            or condition["runOn"] != 0
            or condition["reference"] != 0
        ):
            raise ValueError(f"Fallout 3 CG01 {package_key} stage condition differs")
        location = _single_subrecord(package, "PLDT")
        if len(location) != PACKAGE_LOCATION_BYTES:
            raise ValueError(f"Fallout 3 CG01 {package_key} location differs")
        location_type, target_form_id, radius = struct.unpack("<iIi", location)
        if location_type != 0 or target_form_id != target.form_id or radius < 0:
            raise ValueError(f"Fallout 3 CG01 {package_key} target differs")
        return {
            "formId": _form_id(package.form_id),
            "editorId": _editor_id(package),
            "recordSha256": hashlib.sha256(package.data).hexdigest(),
            "condition": {
                **condition,
                "parameter1": _form_id(int(condition["parameter1"])),
                "reference": _form_id(int(condition["reference"])),
            },
            "target": {
                "kind": "referenceMarker",
                "formId": _form_id(target.form_id),
                "editorId": _editor_id(target),
                "recordSha256": hashlib.sha256(target.data).hexdigest(),
                "sourceTransform": _reference_transform_contract(target),
                "radiusGameUnits": radius,
            },
        }

    close_gate = package_contract("closeGatePackageFormId", "closeGateTargetFormId", stages[0])
    close_door = package_contract("closeDoorPackageFormId", "closeDoorTargetFormId", stages[1])
    leave_room = package_contract("leaveRoomPackageFormId", "leaveRoomTargetFormId", stages[3])

    close_gate_record = by_form[int(str(config["closeGatePackageFormId"]), FORM_ID_RADIX)]
    pending_event: str | None = None
    close_gate_end_sources: list[str] = []
    for row in iter_subrecords(close_gate_record):
        if row.signature in PACKAGE_EVENT_NAMES:
            pending_event = PACKAGE_EVENT_NAMES[row.signature]
        elif row.signature == "SCTX" and pending_event == "end":
            close_gate_end_sources.append(zstring(row.data))
    close_gate_commands = [
        command for source in close_gate_end_sources for command in _source_commands(source)
    ]
    if close_gate_commands != [f"setstage {_editor_id(quest)} {stages[1]}"]:
        raise ValueError("Fallout 3 CG01 close-gate completion differs")

    dad_source = _script_source(dad_script)
    close_door_done = re.search(
        rf"\bbegin\s+OnPackageDone\s+{re.escape(str(close_door['editorId']))}\b"
        r"(?P<body>.*?)\bend\b",
        dad_source,
        re.IGNORECASE | re.DOTALL,
    )
    if close_door_done is None or _source_commands(close_door_done.group("body")) != [
        f"setstage {_editor_id(quest)} {stages[2]}"
    ]:
        raise ValueError("Fallout 3 CG01 close-door completion differs")

    def stage_commands(stage: int) -> list[str]:
        sources = stage_sources.get(stage, [])
        if len(sources) != 1:
            raise ValueError(f"Fallout 3 CG01 stage {stage} result is ambiguous")
        return _source_commands(sources[0])

    stage16_commands = stage_commands(stages[1])
    stage18_commands = stage_commands(stages[2])
    stage20_commands = stage_commands(stages[3])
    if (
        len(stage16_commands) != CG01_POST_STAGE16_COMMAND_COUNT
        or len(stage18_commands) != CG01_POST_STAGE18_COMMAND_COUNT
        or len(stage20_commands) != CG01_POST_STAGE20_COMMAND_COUNT
    ):
        raise ValueError("Fallout 3 CG01 post-stage-14 command coverage differs")

    def reference(editor_id: str) -> object:
        matches = [
            record
            for record in by_editor.get(editor_id.casefold(), [])
            if record.signature in {PLACED_REFERENCE_RECORD, ACTOR_REFERENCE_RECORD}
        ]
        if len(matches) != 1:
            raise ValueError(f"Fallout 3 CG01 reference is absent: {editor_id}")
        return matches[0]

    def compile_command(text: str, index: int) -> dict[str, object]:
        if match := SET_REFERENCE_VARIABLE_PATTERN.fullmatch(text):
            if match.group("subject").casefold() == (_editor_id(quest) or "").casefold():
                value_text = match.group("value")
                return {
                    "index": index,
                    "kind": "setQuestVariable",
                    "questFormId": _form_id(quest.form_id),
                    "questEditorId": _editor_id(quest),
                    "variable": match.group("variable"),
                    "value": float(value_text) if "." in value_text else int(value_text),
                }
            subject = reference(match.group("subject"))
            value_text = match.group("value")
            return {
                "index": index,
                "kind": "setScriptVariable",
                "referenceFormId": _form_id(subject.form_id),
                "referenceEditorId": _editor_id(subject),
                "variable": match.group("variable"),
                "value": float(value_text) if "." in value_text else int(value_text),
            }
        if match := SET_OPEN_STATE_PATTERN.fullmatch(text):
            subject = reference(match.group("subject"))
            return {
                "index": index,
                "kind": "setOpenState",
                "referenceFormId": _form_id(subject.form_id),
                "referenceEditorId": _editor_id(subject),
                "value": int(match.group("value")),
            }
        if match := LOCK_REFERENCE_PATTERN.fullmatch(text):
            subject = reference(match.group("subject"))
            return {
                "index": index,
                "kind": "lock",
                "referenceFormId": _form_id(subject.form_id),
                "referenceEditorId": _editor_id(subject),
                "value": int(match.group("value")),
            }
        if match := UNLOCK_REFERENCE_PATTERN.fullmatch(text):
            subject = reference(match.group("subject"))
            return {
                "index": index,
                "kind": "unlock",
                "referenceFormId": _form_id(subject.form_id),
                "referenceEditorId": _editor_id(subject),
            }
        if match := SET_STAGE_PATTERN.fullmatch(text):
            if match.group("quest").casefold() != (_editor_id(quest) or "").casefold():
                raise ValueError("Fallout 3 CG01 nested stage quest differs")
            return {
                "index": index,
                "kind": "setStage",
                "questFormId": _form_id(quest.form_id),
                "questEditorId": _editor_id(quest),
                "stage": int(match.group("stage")),
            }
        if match := REFERENCE_COMMAND_PATTERN.fullmatch(text):
            subject = reference(match.group("subject"))
            if match.group("command").casefold() != "evp":
                raise ValueError("Fallout 3 CG01 post-stage-14 reference command differs")
            return {
                "index": index,
                "kind": "evaluatePackage",
                "referenceFormId": _form_id(subject.form_id),
                "referenceEditorId": _editor_id(subject),
            }
        if match := PLAYER_CONTROLS_PATTERN.fullmatch(text):
            if match.group("command").casefold() != "enableplayercontrols":
                raise ValueError("Fallout 3 CG01 stage-20 control command differs")
            return {
                "index": index,
                "kind": "enablePlayerControls",
                "arguments": [int(value) for value in match.group("arguments").split()],
            }
        if match := SET_OBJECTIVE_DISPLAYED_PATTERN.fullmatch(text):
            return {
                "index": index,
                "kind": "setObjectiveDisplayed",
                "questFormId": _form_id(quest.form_id),
                "questEditorId": _editor_id(quest),
                "objectiveIndex": int(match.group("index")),
                "value": int(match.group("value")),
            }
        if match := SET_OBJECTIVE_COMPLETED_PATTERN.fullmatch(text):
            if match.group("quest").casefold() != (_editor_id(quest) or "").casefold():
                raise ValueError("Fallout 3 CG01 completed-objective quest differs")
            return {
                "index": index,
                "kind": "setObjectiveCompleted",
                "questFormId": _form_id(quest.form_id),
                "questEditorId": _editor_id(quest),
                "objectiveIndex": int(match.group("index")),
                "value": int(match.group("value")),
            }
        raise ValueError(f"Fallout 3 CG01 post-stage-14 command is unsupported: {text}")

    info_ids = [int(str(value), FORM_ID_RADIX) for value in config["dadResponseInfoFormIds"]]
    infos = [exact(_form_id(value), DIALOGUE_INFO_RECORD, "stage-16 Dad INFO") for value in info_ids]
    if len(infos) != 3 or len(set(info_ids)) != 3:
        raise ValueError("Fallout 3 CG01 stage-16 Dad INFO selection differs")
    cues = []
    for sequence, info in enumerate(infos):
        if not any(
            group.group_type == DIALOGUE_CHILD_GROUP_TYPE and group.label_u32 == topic.form_id
            for group in info.groups
        ):
            raise ValueError("Fallout 3 CG01 stage-16 Dad INFO topic differs")
        response = _text_values(info, "NAM1")
        data = _single_subrecord(info, "DATA")
        if len(response) != 1 or len(data) != DIALOGUE_INFO_DATA_BYTES or struct.unpack("<BBH", data)[2] != DIALOGUE_INFO_SAY_ONCE_FLAG:
            raise ValueError("Fallout 3 CG01 stage-16 Dad INFO response differs")
        conditions = [_dialogue_condition(row.data) for row in iter_subrecords(info) if row.signature == "CTDA"]
        effects = [compile_command(command, index) for index, command in enumerate(
            command for source in _text_values(info, "SCTX") for command in _source_commands(source)
        )]
        cues.append({
            "sequence": sequence,
            "infoFormId": _form_id(info.form_id),
            "recordSha256": hashlib.sha256(info.data).hexdigest(),
            "sayOnce": True,
            "conditions": [{
                **row,
                "parameter1": _form_id(int(row["parameter1"])),
                "reference": _form_id(int(row["reference"])),
            } for row in conditions],
            "effects": effects,
            "response": {
                "index": 1,
                "text": response[0],
                "textSha256": hashlib.sha256(response[0].encode("utf-8")).hexdigest(),
            },
        })

    interaction_stages = [
        int(config["stage30"]),
        int(config["stage40"]),
        int(config["stage50"]),
    ]
    if interaction_stages != sorted(set(interaction_stages)) or interaction_stages[0] <= stages[-1]:
        raise ValueError("Fallout 3 CG01 playpen stage order differs")

    def scripted_reference(config_key: str, expected_script_editor: str) -> tuple[object, object, object]:
        placed = exact(config[config_key], PLACED_REFERENCE_RECORD, config_key)
        base_ids = [
            struct.unpack("<I", row.data)[0]
            for row in iter_subrecords(placed)
            if row.signature == "NAME" and len(row.data) == FORM_ID_BYTES
        ]
        if len(base_ids) != 1:
            raise ValueError(f"Fallout 3 CG01 {config_key} base differs")
        base = by_form.get(base_ids[0])
        if base is None or base.signature not in {ACTIVATOR_RECORD, DOOR_RECORD}:
            raise ValueError(f"Fallout 3 CG01 {config_key} base is absent")
        script_ids = [
            struct.unpack("<I", row.data)[0]
            for row in iter_subrecords(base)
            if row.signature == "SCRI" and len(row.data) == FORM_ID_BYTES
        ]
        if len(script_ids) != 1:
            raise ValueError(f"Fallout 3 CG01 {config_key} script differs")
        script = by_form.get(script_ids[0])
        if script is None or script.signature != SCRIPT_RECORD or _editor_id(script) != expected_script_editor:
            raise ValueError(f"Fallout 3 CG01 {config_key} script identity differs")
        return placed, base, script

    gate_ref, gate_base, gate_script = scripted_reference(
        "playpenGateReferenceFormId", "CG01PlaypenGateSCRIPT")
    exit_ref, exit_base, exit_script = scripted_reference(
        "exitCribTriggerReferenceFormId", "CG01ExitCribTriggerSCRIPT")
    book_ref, book_base, book_script = scripted_reference(
        "specialBookReferenceFormId", "CG01SpecialBookSCRIPT")
    gate_commands = _source_commands(_script_source(gate_script))
    exit_commands = _source_commands(_script_source(exit_script))
    book_commands = _source_commands(_script_source(book_script))
    if not any(SET_STAGE_PATTERN.fullmatch(row) and int(SET_STAGE_PATTERN.fullmatch(row).group("stage")) == interaction_stages[0] for row in gate_commands):
        raise ValueError("Fallout 3 CG01 playpen gate result differs")
    if not any(SET_STAGE_PATTERN.fullmatch(row) and int(SET_STAGE_PATTERN.fullmatch(row).group("stage")) == interaction_stages[1] for row in exit_commands):
        raise ValueError("Fallout 3 CG01 crib-exit result differs")
    if not any(SET_STAGE_PATTERN.fullmatch(row) and int(SET_STAGE_PATTERN.fullmatch(row).group("stage")) == interaction_stages[2] for row in book_commands):
        raise ValueError("Fallout 3 CG01 SPECIAL-book result differs")
    menu_rows = [SPECIAL_BOOK_MENU_PATTERN.fullmatch(row) for row in book_commands]
    menu_rows = [row for row in menu_rows if row is not None]
    if len(menu_rows) != 1:
        raise ValueError("Fallout 3 CG01 SPECIAL-book menu command differs")
    primitive_data = _single_subrecord(exit_ref, "XPRM")
    if len(primitive_data) != TRIGGER_PRIMITIVE_BYTES:
        raise ValueError("Fallout 3 CG01 crib-exit primitive layout differs")
    primitive = struct.unpack(f"<{TRIGGER_PRIMITIVE_FLOATS}fI", primitive_data)
    if not all(math.isfinite(value) and value > 0 for value in primitive[:3]):
        raise ValueError("Fallout 3 CG01 crib-exit primitive dimensions differ")

    def interaction_identity(placed: object, base: object, script: object) -> dict[str, object]:
        models = _text_values(base, "MODL")
        names = _text_values(base, "FULL")
        return {
            "referenceFormId": _form_id(placed.form_id),
            "referenceRecordSha256": hashlib.sha256(placed.data).hexdigest(),
            "baseFormId": _form_id(base.form_id),
            "baseEditorId": _editor_id(base),
            "baseRecordSha256": hashlib.sha256(base.data).hexdigest(),
            "scriptFormId": _form_id(script.form_id),
            "scriptEditorId": _editor_id(script),
            "scriptSourceSha256": hashlib.sha256(_script_source(script).encode("cp1252")).hexdigest(),
            "sourceTransform": _reference_transform_contract(placed),
            "modelPath": models[0] if len(models) == 1 else None,
            "displayName": names[0] if len(names) == 1 else None,
        }

    result_contracts = []
    for stage in interaction_stages:
        commands = stage_commands(stage)
        result_contracts.append({
            "stage": stage,
            "sourceSha256": hashlib.sha256(stage_sources[stage][0].encode("cp1252")).hexdigest(),
            "commands": [compile_command(text, index) for index, text in enumerate(commands)],
        })
    player_rows = [
        record for record in by_editor.get("player", []) if record.signature == NPC_RECORD
    ]
    if len(player_rows) != 1:
        raise ValueError("Fallout 3 SPECIAL player base is absent")
    player_data = _single_subrecord(player_rows[0], "DATA")
    if len(player_data) != FORM_ID_BYTES + len(GAMEBRYO_SPECIAL_EDITOR_IDS):
        raise ValueError("Fallout 3 SPECIAL player DATA layout differs")
    initial_values = list(player_data[-len(GAMEBRYO_SPECIAL_EDITOR_IDS):])
    actor_values = []
    for index, editor_id in enumerate(GAMEBRYO_SPECIAL_EDITOR_IDS):
        matches = [
            record for record in by_editor.get(editor_id.casefold(), [])
            if record.signature == ACTOR_VALUE_RECORD
        ]
        if len(matches) != 1:
            raise ValueError(f"Fallout 3 SPECIAL actor value is absent: {editor_id}")
        actor_value = matches[0]
        names = _text_values(actor_value, "FULL")
        descriptions = _text_values(actor_value, "DESC")
        if len(names) != 1 or len(descriptions) != 1:
            raise ValueError(f"Fallout 3 SPECIAL actor value text differs: {editor_id}")
        actor_values.append({
            "index": index,
            "formId": _form_id(actor_value.form_id),
            "editorId": editor_id,
            "recordSha256": hashlib.sha256(actor_value.data).hexdigest(),
            "label": names[0],
            "description": descriptions[0],
            "initialValue": initial_values[index],
            "minimumValue": GAMEBRYO_SPECIAL_MINIMUM_VALUE,
            "maximumValue": GAMEBRYO_SPECIAL_MAXIMUM_VALUE,
        })
    menu_points = int(menu_rows[0].group("points"))
    if not all(
        GAMEBRYO_SPECIAL_MINIMUM_VALUE <= value <= GAMEBRYO_SPECIAL_MAXIMUM_VALUE
        for value in initial_values
    ) or sum(initial_values) > menu_points:
        raise ValueError("Fallout 3 SPECIAL initial allocation differs")
    stage20_interaction = {
        "schema": "opennv-fo3-cg01-stage-20-special-runtime/v1",
        "status": "source-backed-physical-interaction-runtime-ready",
        "sourceStage": stages[-1],
        "gate": {**interaction_identity(gate_ref, gate_base, gate_script), "targetStage": interaction_stages[0]},
        "exitTrigger": {
            **interaction_identity(exit_ref, exit_base, exit_script),
            "targetStage": interaction_stages[1],
            "dimensionsGameUnits": list(primitive[:3]),
            "primitiveType": int(primitive[TRIGGER_PRIMITIVE_TYPE_INDEX]),
        },
        "specialBook": {
            **interaction_identity(book_ref, book_base, book_script),
            "targetStage": interaction_stages[2],
            "menuPoints": menu_points,
            "menuDocument": "menus\\chargen\\specialbookmenu.xml",
            "playerBaseFormId": _form_id(player_rows[0].form_id),
            "playerBaseRecordSha256": hashlib.sha256(player_rows[0].data).hexdigest(),
            "actorValues": actor_values,
        },
        "stageResults": result_contracts,
        "nextBoundary": {"applied": True, "blocker": None},
    }
    quest_script_ids = [
        struct.unpack("<I", row.data)[0] for row in iter_subrecords(quest)
        if row.signature == "SCRI" and len(row.data) == FORM_ID_BYTES
    ]
    if len(quest_script_ids) != 1:
        raise ValueError("Fallout 3 CG01 quest script identity differs")
    quest_script = exact(_form_id(quest_script_ids[0]), SCRIPT_RECORD, "quest script")
    quest_script_source = _script_source(quest_script)
    timer_transition_match = re.search(
        r"if\s+getstageDone\s+CG01\s+(?P<source>\d+)\s*==\s*1\s*&&\s*"
        r"getstageDone\s+CG01\s+(?P<target>\d+)\s*==\s*0\s*"
        r"setstage\s+CG01\s+(?P=target)",
        quest_script_source,
        re.IGNORECASE | re.DOTALL,
    )
    if timer_transition_match is None or int(timer_transition_match.group("source")) != interaction_stages[2]:
        raise ValueError("Fallout 3 CG01 stage-50 timer transition differs")
    timer_target_stage = int(timer_transition_match.group("target"))
    timer_decrement = re.search(
        r"if\s+runTimer\s*==\s*1.*?if\s+timer\s*>\s*0\s*"
        r"set\s+timer\s+to\s+timer\s*-\s*GetSecondsPassed",
        quest_script_source,
        re.IGNORECASE | re.DOTALL,
    )
    if timer_decrement is None:
        raise ValueError("Fallout 3 CG01 timer decrement differs")
    stage50_compiled = result_contracts[-1]["commands"]
    timer_rows = [row for row in stage50_compiled if row["kind"] == "setQuestVariable"]
    timer_by_name = {str(row["variable"]): row["value"] for row in timer_rows}
    if set(timer_by_name) != {"timer", "runTimer"} or float(timer_by_name["timer"]) <= 0 or int(timer_by_name["runTimer"]) != 1:
        raise ValueError("Fallout 3 CG01 stage-50 timer initialization differs")
    target_commands = stage_commands(timer_target_stage)
    target_compiled = [compile_command(text, index) for index, text in enumerate(target_commands)]
    stage20_interaction["timerTransition"] = {
        "schema": "opennv-fo3-cg01-stage-50-timer-runtime/v1",
        "scriptFormId": _form_id(quest_script.form_id),
        "scriptEditorId": _editor_id(quest_script),
        "scriptSourceSha256": hashlib.sha256(quest_script_source.encode("cp1252")).hexdigest(),
        "sourceStage": interaction_stages[2],
        "targetStage": timer_target_stage,
        "timerVariable": {"name": "timer", "initialSeconds": float(timer_by_name["timer"])},
        "runVariable": {"name": "runTimer", "requiredValue": int(timer_by_name["runTimer"])},
        "decrementSource": "GetSecondsPassed",
        "targetResult": {
            "sourceSha256": hashlib.sha256(stage_sources[timer_target_stage][0].encode("cp1252")).hexdigest(),
            "commands": target_compiled,
        },
        "nextBoundary": {"applied": True, "blocker": None},
    }
    dad_package_ids = [
        struct.unpack("<I", row.data)[0] for row in iter_subrecords(dad_base)
        if row.signature == "PKID" and len(row.data) == FORM_ID_BYTES
    ]
    return_packages = []
    for package_id in dad_package_ids:
        package = by_form.get(package_id)
        if package is None or package.signature != PACKAGE_RECORD:
            continue
        conditions = [_dialogue_condition(row.data) for row in iter_subrecords(package) if row.signature == "CTDA"]
        if len(conditions) == 1 and conditions[0]["function"] == GET_STAGE_FUNCTION and conditions[0]["parameter1"] == quest.form_id and conditions[0]["comparisonValue"] == float(timer_target_stage):
            return_packages.append((package, conditions[0]))
    if len(return_packages) != 1:
        raise ValueError("Fallout 3 CG01 stage-70 Dad package differs")
    return_package, return_condition = return_packages[0]
    location = _single_subrecord(return_package, "PLDT")
    if len(location) != PACKAGE_LOCATION_BYTES:
        raise ValueError("Fallout 3 CG01 Dad-return location differs")
    location_type, return_target_id, return_radius = struct.unpack("<iIi", location)
    return_target = by_form.get(return_target_id)
    if location_type != 0 or return_target is None or return_target.signature != PLACED_REFERENCE_RECORD:
        raise ValueError("Fallout 3 CG01 Dad-return marker differs")
    completion = re.search(
        rf"begin\s+OnPackageDone\s+{re.escape(_editor_id(return_package) or '')}\s*"
        r"(?P<body>.*?)\bend\b",
        _script_source(dad_script), re.IGNORECASE | re.DOTALL)
    completion_commands = _source_commands(completion.group("body")) if completion else []
    if len(completion_commands) != 1 or not (completion_stage := SET_STAGE_PATTERN.fullmatch(completion_commands[0])):
        raise ValueError("Fallout 3 CG01 Dad-return completion differs")
    completion_target = int(completion_stage.group("stage"))
    stage72_commands = stage_commands(completion_target)
    stage72_compiled = [compile_command(text, index) for index, text in enumerate(stage72_commands)]
    dialogue_delay_rows = [row for row in stage72_compiled if row["kind"] == "setScriptVariable" and row["variable"] == "timer"]
    if len(dialogue_delay_rows) != 1 or float(dialogue_delay_rows[0]["value"]) <= 0:
        raise ValueError("Fallout 3 CG01 Dad-return dialogue delay differs")
    topic_infos = [
        record for record in records if record.signature == DIALOGUE_INFO_RECORD and
        any(group.group_type == DIALOGUE_CHILD_GROUP_TYPE and group.label_u32 == topic.form_id for group in record.groups)
    ]
    dialogue_start = next((index for index, info in enumerate(topic_infos) if any(
        SET_STAGE_PATTERN.fullmatch(command) and int(SET_STAGE_PATTERN.fullmatch(command).group("stage")) > completion_target
        for source_text in _text_values(info, "SCTX") for command in _source_commands(source_text))), None)
    if dialogue_start is None:
        raise ValueError("Fallout 3 CG01 Dad-return dialogue start is absent")
    dialogue_infos = []
    dialogue_target = None
    for info in topic_infos[dialogue_start:]:
        dialogue_infos.append(info)
        stages_in_info = [int(match.group("stage")) for source_text in _text_values(info, "SCTX")
            for command in _source_commands(source_text) if (match := SET_STAGE_PATTERN.fullmatch(command))]
        if stages_in_info:
            dialogue_target = stages_in_info[-1]
            if dialogue_target > completion_target + 1:
                break
    if dialogue_target is None or len(dialogue_infos) < 2:
        raise ValueError("Fallout 3 CG01 Dad-return dialogue closure differs")
    return_cues = []
    for sequence, info in enumerate(dialogue_infos):
        responses = _text_values(info, "NAM1")
        if len(responses) != 1:
            raise ValueError("Fallout 3 CG01 Dad-return response differs")
        return_cues.append({
            "sequence": sequence,
            "infoFormId": _form_id(info.form_id),
            "recordSha256": hashlib.sha256(info.data).hexdigest(),
            "sayOnce": True,
            "conditions": [{**row, "parameter1": _form_id(int(row["parameter1"])), "reference": _form_id(int(row["reference"]))}
                for row in [_dialogue_condition(value.data) for value in iter_subrecords(info) if value.signature == "CTDA"]],
            "effects": [compile_command(command, index) for index, command in enumerate(
                command for source_text in _text_values(info, "SCTX") for command in _source_commands(source_text))],
            "targetStage": next((int(match.group("stage")) for source_text in _text_values(info, "SCTX")
                for command in _source_commands(source_text) if (match := SET_STAGE_PATTERN.fullmatch(command))), None),
            "targetQuestFormId": _form_id(quest.form_id) if any(SET_STAGE_PATTERN.fullmatch(command)
                for source_text in _text_values(info, "SCTX") for command in _source_commands(source_text)) else None,
            "response": {"index": 1, "text": responses[0], "textSha256": hashlib.sha256(responses[0].encode("utf-8")).hexdigest()},
        })

    def dad_package_for_stage(stage: int) -> tuple[object, dict[str, object]]:
        matches = []
        for package_id in dad_package_ids:
            package = by_form.get(package_id)
            if package is None or package.signature != PACKAGE_RECORD:
                continue
            conditions = [
                _dialogue_condition(row.data)
                for row in iter_subrecords(package)
                if row.signature == "CTDA"
            ]
            if (
                len(conditions) == 1
                and conditions[0]["operatorFlags"] == CONDITION_EQUAL_OPERATOR_FLAGS
                and conditions[0]["function"] == GET_STAGE_FUNCTION
                and conditions[0]["parameter1"] == quest.form_id
                and conditions[0]["comparisonValue"] == float(stage)
            ):
                matches.append((package, conditions[0]))
        if len(matches) != 1:
            raise ValueError(f"Fallout 3 CG01 Dad stage-{stage} package differs")
        return matches[0]

    def travel_package_contract(
        package: object,
        condition: dict[str, object],
    ) -> dict[str, object]:
        package_data = _single_subrecord(package, "PKDT")
        location_data = _single_subrecord(package, "PLDT")
        if len(package_data) != PACKAGE_DATA_BYTES or len(location_data) != PACKAGE_LOCATION_BYTES:
            raise ValueError("Fallout 3 CG01 Dad travel package layout differs")
        package_type = package_data[4]
        location_type, target_id, radius = struct.unpack("<III", location_data)
        target = by_form.get(target_id)
        if (
            location_type != 0
            or target is None
            or target.signature != PLACED_REFERENCE_RECORD
        ):
            raise ValueError("Fallout 3 CG01 Dad travel package target differs")
        return {
            "formId": _form_id(package.form_id),
            "editorId": _editor_id(package),
            "recordSha256": hashlib.sha256(package.data).hexdigest(),
            "packageFlags": struct.unpack_from("<I", package_data)[0],
            "packageType": package_type,
            "condition": {
                **condition,
                "parameter1": _form_id(int(condition["parameter1"])),
                "reference": _form_id(int(condition["reference"])),
            },
            "target": {
                "formId": _form_id(target.form_id),
                "editorId": _editor_id(target),
                "recordSha256": hashlib.sha256(target.data).hexdigest(),
                "sourceTransform": _reference_transform_contract(target),
                "radiusGameUnits": radius,
            },
        }

    bible_package, bible_condition = dad_package_for_stage(completion_target + 1)
    bible_contract = travel_package_contract(bible_package, bible_condition)
    bible_contract["stageResult"] = {
        "sourceSha256": hashlib.sha256(
            stage_sources[completion_target + 1][0].encode("cp1252")
        ).hexdigest(),
        "commands": [
            compile_command(text, index)
            for index, text in enumerate(stage_commands(completion_target + 1))
        ],
    }
    pending_event = None
    bible_end_sources = []
    for row in iter_subrecords(bible_package):
        if row.signature in PACKAGE_EVENT_NAMES:
            pending_event = PACKAGE_EVENT_NAMES[row.signature]
        elif row.signature == "SCTX" and pending_event == "end":
            bible_end_sources.append(zstring(row.data))
    bible_end_commands = [
        command for source in bible_end_sources for command in _source_commands(source)
    ]
    if (
        len(bible_end_commands) != 1
        or not (bible_stage_match := SET_STAGE_PATTERN.fullmatch(bible_end_commands[0]))
    ):
        raise ValueError("Fallout 3 CG01 Dad Bible package completion differs")
    bible_completion_stage = int(bible_stage_match.group("stage"))
    bible_contract["completionStage"] = bible_completion_stage
    bible_contract["completionCommands"] = [
        compile_command(text, index)
        for index, text in enumerate(stage_commands(bible_completion_stage))
    ]

    lead_package, lead_condition = dad_package_for_stage(dialogue_target)
    lead_contract = travel_package_contract(lead_package, lead_condition)
    target_data = _single_subrecord(lead_package, "PTDT")
    if len(target_data) != struct.calcsize("<IIII"):
        raise ValueError("Fallout 3 CG01 Dad lead target layout differs")
    target_type, target_form_id, target_count, target_unknown = struct.unpack(
        "<IIII", target_data
    )
    locomotion = dict(config["dadLeadLocomotion"])
    player_reference_form_id = int(str(locomotion["playerReferenceFormId"]), FORM_ID_RADIX)
    player_base_form_id = int(str(locomotion["playerBaseFormId"]), FORM_ID_RADIX)
    player = by_form.get(player_base_form_id)
    if (
        target_type != 0
        or target_form_id != player_reference_form_id
        or player is None
        or player.signature != NPC_RECORD
        or _editor_id(player) != "Player"
    ):
        raise ValueError("Fallout 3 CG01 Dad lead target is not Player")
    lead_contract["escortTarget"] = {
        "type": target_type,
        "formId": _form_id(target_form_id),
        "editorId": _editor_id(player),
        "count": target_count,
        "unknown": target_unknown,
    }
    lead_contract["stageResult"] = {
        "sourceSha256": hashlib.sha256(stage_sources[dialogue_target][0].encode("cp1252")).hexdigest(),
        "commands": [
            compile_command(text, index)
            for index, text in enumerate(stage_commands(dialogue_target))
        ],
    }
    say_done = re.search(
        rf"begin\s+SayToDone\s+{re.escape(_editor_id(topic) or '')}\s*"
        r"(?P<body>.*?)\bend\b",
        _script_source(dad_script),
        re.IGNORECASE | re.DOTALL,
    )
    say_done_commands = _source_commands(say_done.group("body")) if say_done else []
    say_done_stage = next(
        (
            int(match.group("stage"))
            for command in say_done_commands
            if (match := SET_STAGE_PATTERN.search(command))
        ),
        None,
    )
    if say_done_stage is None:
        raise ValueError("Fallout 3 CG01 Dad lead SayToDone result differs")
    lead_contract["sayToDoneStage"] = say_done_stage
    lead_contract["sayToDoneResult"] = {
        "sourceSha256": hashlib.sha256(stage_sources[say_done_stage][0].encode("cp1252")).hexdigest(),
        "commands": [
            compile_command(text, index)
            for index, text in enumerate(stage_commands(say_done_stage))
        ],
    }

    end_scripts = [
        record
        for record in records
        if record.signature == SCRIPT_RECORD and _editor_id(record) == "CG01EndQuestTriggerSCRIPT"
    ]
    if len(end_scripts) != 1:
        raise ValueError("Fallout 3 CG01 end trigger script differs")
    end_script = end_scripts[0]
    end_bases = [
        record
        for record in records
        if record.signature == ACTIVATOR_RECORD
        and any(
            row.signature == "SCRI"
            and len(row.data) == FORM_ID_BYTES
            and struct.unpack("<I", row.data)[0] == end_script.form_id
            for row in iter_subrecords(record)
        )
    ]
    if len(end_bases) != 1:
        raise ValueError("Fallout 3 CG01 end trigger base differs")
    end_base = end_bases[0]
    end_refs = [
        record
        for record in records
        if record.signature == PLACED_REFERENCE_RECORD
        and any(
            row.signature == "NAME"
            and len(row.data) == FORM_ID_BYTES
            and struct.unpack("<I", row.data)[0] == end_base.form_id
            for row in iter_subrecords(record)
        )
    ]
    if len(end_refs) != 1:
        raise ValueError("Fallout 3 CG01 end trigger reference differs")
    end_ref = end_refs[0]
    primitive_data = _single_subrecord(end_ref, "XPRM")
    if len(primitive_data) != TRIGGER_PRIMITIVE_BYTES:
        raise ValueError("Fallout 3 CG01 end trigger primitive differs")
    primitive = struct.unpack(f"<{TRIGGER_PRIMITIVE_FLOATS}fI", primitive_data)
    end_source = _script_source(end_script)
    end_stage_match = re.search(
        rf"getStage\s+{re.escape(_editor_id(quest) or '')}\s*==\s*{say_done_stage}.*?"
        rf"setstage\s+{re.escape(_editor_id(quest) or '')}\s+(?P<stage>\d+)",
        end_source,
        re.IGNORECASE | re.DOTALL,
    )
    if end_stage_match is None:
        raise ValueError("Fallout 3 CG01 end trigger stage result differs")
    end_stage = int(end_stage_match.group("stage"))
    lead_contract["endTrigger"] = {
        "referenceFormId": _form_id(end_ref.form_id),
        "referenceRecordSha256": hashlib.sha256(end_ref.data).hexdigest(),
        "baseFormId": _form_id(end_base.form_id),
        "baseEditorId": _editor_id(end_base),
        "baseRecordSha256": hashlib.sha256(end_base.data).hexdigest(),
        "scriptFormId": _form_id(end_script.form_id),
        "scriptEditorId": _editor_id(end_script),
        "scriptSourceSha256": hashlib.sha256(end_source.encode("cp1252")).hexdigest(),
        "sourceTransform": _reference_transform_contract(end_ref),
        "dimensionsGameUnits": list(primitive[:3]),
        "primitiveType": int(primitive[TRIGGER_PRIMITIVE_TYPE_INDEX]),
        "sourceStage": say_done_stage,
        "targetStage": end_stage,
    }
    stage90_sources = stage_sources.get(end_stage, [])
    if len(stage90_sources) != 1:
        raise ValueError("Fallout 3 CG01 stage 90 result source is ambiguous")
    stage90_source = stage90_sources[0]
    stage90_commands = _parse_cg01_stage90_commands(stage90_source)
    quest_script_source = _script_source(by_form[struct.unpack(
        "<I", _single_subrecord(quest, "SCRI")
    )[0]])
    timer_target = next(
        (
            int(match.group("target"))
            for match in re.finditer(
                rf"if\s+getstage\s+{re.escape(_editor_id(quest) or '')}\s*==\s*{end_stage}\s*"
                rf"setstage\s+{re.escape(_editor_id(quest) or '')}\s+(?P<target>\d+)",
                quest_script_source,
                re.IGNORECASE | re.DOTALL,
            )
        ),
        None,
    )
    if timer_target is None:
        raise ValueError("Fallout 3 CG01 stage 90 timer target differs")
    timer_source = re.search(
        r"if\s+runTimer\s*==\s*1.*?if\s+timer\s*>\s*0\s*"
        r"set\s+timer\s+to\s+timer\s*-\s*GetSecondsPassed",
        quest_script_source,
        re.IGNORECASE | re.DOTALL,
    )
    if timer_source is None:
        raise ValueError("Fallout 3 CG01 stage 90 timer decrement differs")
    quest_script = by_form[struct.unpack("<I", _single_subrecord(quest, "SCRI"))[0]]
    script_hash = hashlib.sha256(quest_script_source.encode("cp1252")).hexdigest()
    resolved_stage90 = []
    for index, command in enumerate(stage90_commands):
        kind = str(command["kind"])
        resolved: dict[str, object] = {"index": index, "kind": kind}
        if kind == "setQuestVariable":
            variable = str(command["variable"])
            declarations = [
                match.group("type").casefold()
                for match in re.finditer(
                    rf"^\s*(?P<type>short|float)\s+{re.escape(variable)}\b",
                    quest_script_source,
                    re.IGNORECASE | re.MULTILINE,
                )
            ]
            if str(command["subject"]).casefold() != (_editor_id(quest) or "").casefold() or len(declarations) != 1:
                raise ValueError("Fallout 3 CG01 stage 90 quest variable differs")
            resolved.update(
                {
                    "questFormId": _form_id(quest.form_id),
                    "questEditorId": _editor_id(quest),
                    "scriptFormId": _form_id(quest_script.form_id),
                    "scriptEditorId": _editor_id(quest_script),
                    "scriptSourceSha256": script_hash,
                    "variable": variable,
                    "variableType": declarations[0],
                    "value": command["value"],
                }
            )
        elif kind == "completeAllObjectives":
            if str(command["questEditorId"]).casefold() != (_editor_id(quest) or "").casefold():
                raise ValueError("Fallout 3 CG01 objective completion quest differs")
            resolved.update({"questFormId": _form_id(quest.form_id), "questEditorId": _editor_id(quest)})
        elif kind == "autoDisplayObjectives":
            resolved["value"] = command["value"]
        elif kind == "killQuestUpdates":
            pass
        elif kind == "applyImageSpaceModifier":
            editor_id = str(command["modifierEditorId"])
            matches = [record for record in by_editor.get(editor_id.casefold(), []) if record.signature == IMAGE_SPACE_MODIFIER_RECORD]
            if len(matches) != 1:
                raise ValueError("Fallout 3 CG01 stage 90 image-space modifier differs")
            resolved["modifier"] = {
                **parse_image_space_modifier(matches[0]).manifest(),
                "formId": _form_id(matches[0].form_id),
            }
        elif kind == "playSound":
            editor_id = str(command["soundEditorId"])
            matches = [record for record in by_editor.get(editor_id.casefold(), []) if record.signature == SOUND_RECORD]
            if len(matches) != 1:
                raise ValueError("Fallout 3 CG01 stage 90 sound differs")
            sound = matches[0]
            paths = _text_values(sound, "FNAM")
            data = [row.data for row in iter_subrecords(sound) if row.signature == "SNDD"]
            if len(paths) != 1 or len(data) != 1:
                raise ValueError("Fallout 3 CG01 stage 90 sound layout differs")
            resolved["sound"] = {
                "formId": _form_id(sound.form_id),
                "editorId": _editor_id(sound),
                "logicalPath": canonical_member_path(f"sound\\{paths[0]}"),
                "recordSha256": hashlib.sha256(sound.data).hexdigest(),
                "soundDataSha256": hashlib.sha256(data[0]).hexdigest(),
            }
        resolved_stage90.append(resolved)

    stage100_sources = stage_sources.get(timer_target, [])
    if len(stage100_sources) != 1:
        raise ValueError("Fallout 3 CG01 stage 100 result source is ambiguous")
    stage100_source = stage100_sources[0]
    stage100_commands = _parse_cg01_stage100_commands(stage100_source)
    resolved_stage100 = []
    next_quest = None
    for index, command in enumerate(stage100_commands):
        kind = str(command["kind"])
        resolved = {"index": index, "kind": kind}
        if kind == "stopQuest":
            if str(command["questEditorId"]).casefold() != (_editor_id(quest) or "").casefold():
                raise ValueError("Fallout 3 CG01 stopped quest differs")
            resolved.update({"questFormId": _form_id(quest.form_id), "questEditorId": _editor_id(quest)})
        elif kind == "disable":
            if str(command["subject"]).casefold() != (_editor_id(dad_reference) or "").casefold():
                raise ValueError("Fallout 3 CG01 disabled Dad differs")
            resolved.update(
                {
                    "referenceFormId": _form_id(dad_reference.form_id),
                    "referenceEditorId": _editor_id(dad_reference),
                    "referenceRecordSha256": hashlib.sha256(dad_reference.data).hexdigest(),
                }
            )
        elif kind in {"setPlayerScale", "setPlayerToddler"}:
            resolved["value"] = command["value"]
        elif kind == "clearNoActivationSound":
            pass
        elif kind == "setStage":
            matches = [record for record in by_editor.get(str(command["questEditorId"]).casefold(), []) if record.signature == QUEST_RECORD]
            if len(matches) != 1:
                raise ValueError("Fallout 3 CG01 next quest differs")
            next_quest = matches[0]
            resolved.update(
                {
                    "questFormId": _form_id(next_quest.form_id),
                    "questEditorId": _editor_id(next_quest),
                    "questRecordSha256": hashlib.sha256(next_quest.data).hexdigest(),
                    "stage": command["stage"],
                }
            )
        resolved_stage100.append(resolved)
    if next_quest is None:
        raise ValueError("Fallout 3 CG01 completion has no next quest")
    next_subrecords = tuple(iter_subrecords(next_quest))
    next_stage_sources: dict[int, list[str]] = {}
    next_stage = None
    for row in next_subrecords:
        if row.signature == "INDX":
            next_stage = int.from_bytes(row.data, "little")
        elif row.signature == "SCTX" and next_stage is not None:
            next_stage_sources.setdefault(next_stage, []).append(zstring(row.data))
    next_entry_stage = int(stage100_commands[-1]["stage"])
    entry_sources = next_stage_sources.get(next_entry_stage, [])
    if len(entry_sources) != 1:
        raise ValueError("Fallout 3 CG02 stage 0 result source is ambiguous")
    entry_commands = _source_commands(entry_sources[0])
    if len(entry_commands) != 2:
        raise ValueError("Fallout 3 CG02 stage 0 command count differs")
    nested_stage_match = SET_STAGE_PATTERN.fullmatch(entry_commands[0])
    move_match = MOVE_TO_REFERENCE_PATTERN.fullmatch(entry_commands[1])
    if (
        nested_stage_match is None
        or nested_stage_match.group("quest").casefold() != (_editor_id(next_quest) or "").casefold()
        or move_match is None
        or move_match.group("subject").casefold() != "player"
    ):
        raise ValueError("Fallout 3 CG02 stage 0 command order differs")
    nested_stage = int(nested_stage_match.group("stage"))
    nested_sources = next_stage_sources.get(nested_stage, [])
    if not nested_sources:
        raise ValueError("Fallout 3 CG02 stage 5 result is absent")
    nested_commands = _parse_cg02_stage5_commands(nested_sources)
    marker_matches = [
        record
        for record in by_editor.get(move_match.group("target").casefold(), [])
        if record.signature == PLACED_REFERENCE_RECORD
    ]
    if len(marker_matches) != 1:
        raise ValueError("Fallout 3 CG02 player marker differs")
    next_marker = marker_matches[0]
    next_script_id = struct.unpack("<I", _single_subrecord(next_quest, "SCRI"))[0]
    next_script = by_form.get(next_script_id)
    if next_script is None or next_script.signature != SCRIPT_RECORD:
        raise ValueError("Fallout 3 CG02 quest script differs")
    next_script_source = _script_source(next_script)
    resolved_nested = []
    for index, command in enumerate(nested_commands):
        resolved = {"index": index, **command}
        kind = str(command["kind"])
        if kind in {"enable", "lookAt", "ignoreCrime"}:
            matches = [
                record
                for record in by_editor.get(str(command["subject"]).casefold(), [])
                if record.signature in {ACTOR_REFERENCE_RECORD, CREATURE_REFERENCE_RECORD}
            ]
            if len(matches) != 1:
                raise ValueError(f"Fallout 3 CG02 stage 5 actor differs: {command['subject']}")
            resolved.update(
                {
                    "referenceFormId": _form_id(matches[0].form_id),
                    "referenceRecordSha256": hashlib.sha256(matches[0].data).hexdigest(),
                }
            )
        elif kind in {"addItem", "equipItem"}:
            matches = by_editor.get(str(command["itemEditorId"]).casefold(), [])
            if len(matches) != 1:
                raise ValueError(f"Fallout 3 CG02 stage 5 item differs: {command['itemEditorId']}")
            resolved.update(
                {
                    "itemFormId": _form_id(matches[0].form_id),
                    "itemRecordType": matches[0].signature,
                    "itemRecordSha256": hashlib.sha256(matches[0].data).hexdigest(),
                }
            )
        elif kind == "setQuestVariable":
            variable = str(command["variable"])
            declarations = [
                match.group("type").casefold()
                for match in re.finditer(
                    rf"^\s*(?P<type>short|float)\s+{re.escape(variable)}\b",
                    next_script_source,
                    re.IGNORECASE | re.MULTILINE,
                )
            ]
            if str(command["subject"]).casefold() != (_editor_id(next_quest) or "").casefold() or len(declarations) != 1:
                raise ValueError("Fallout 3 CG02 stage 5 quest variable differs")
            resolved.update(
                {
                    "questFormId": _form_id(next_quest.form_id),
                    "questEditorId": _editor_id(next_quest),
                    "variableType": declarations[0],
                }
            )
        resolved_nested.append(resolved)
    cg02_stage0 = {
        "schema": "opennv-fo3-cg02-stage-0-to-5-runtime/v1",
        "questFormId": _form_id(next_quest.form_id),
        "questEditorId": _editor_id(next_quest),
        "questRecordSha256": hashlib.sha256(next_quest.data).hexdigest(),
        "questScriptFormId": _form_id(next_script.form_id),
        "questScriptEditorId": _editor_id(next_script),
        "questScriptSourceSha256": hashlib.sha256(next_script_source.encode("cp1252")).hexdigest(),
        "sourceStage": next_entry_stage,
        "targetStage": nested_stage,
        "stage0CommandCount": len(entry_commands),
        "stage0SourceSha256": hashlib.sha256(entry_sources[0].encode("cp1252")).hexdigest(),
        "stage5SourceSha256": hashlib.sha256("\n".join(nested_sources).encode("cp1252")).hexdigest(),
        "stage5Commands": resolved_nested,
        "playerMove": {
            "index": 1,
            "kind": "moveToReference",
            "referenceFormId": _form_id(next_marker.form_id),
            "referenceEditorId": _editor_id(next_marker),
            "referenceRecordSha256": hashlib.sha256(next_marker.data).hexdigest(),
            "sourceTransform": _reference_transform_contract(next_marker),
        },
        "nextBoundary": {
            "applied": "cg02IntroActorRecipes" in definition,
            "blocker": (
                None if "cg02IntroActorRecipes" in definition
                else "fo3-cg02-stage-5-intro-timer-dialogue-runtime-not-implemented"
            ),
        },
    }
    if "cg02IntroActorRecipes" in definition:
        intro_runtime = _compile_cg02_intro_runtime(
            records,
            next_quest,
            next_script,
            next_stage_sources,
            [str(value) for value in definition["cg02IntroActorRecipes"]],
        )
        intro_timer = next(
            row for row in resolved_nested
            if row["kind"] == "setQuestVariable" and row["variable"] == "timer"
        )
        intro_runtime["timer"] = {
            **dict(intro_runtime["timer"]),
            "initialSeconds": float(intro_timer["value"]),
        }
        if "cg02DadSpeech" in definition:
            dad_speech = _compile_cg02_dad_speech_runtime(
                records,
                next_quest,
                next_stage_sources,
                definition,
            )
            if "cg02OverseerSpeech" in definition:
                overseer_speech = _compile_cg02_overseer_speech_runtime(
                        records,
                        next_quest,
                        next_stage_sources,
                        definition,
                    )
                if "cg02DadPartySpeech" in definition:
                    party_runtime = _compile_cg02_dad_party_runtime(
                        records, next_quest, next_stage_sources, definition)
                    if "cg02BirthdayInteractions" in definition:
                        party_runtime["birthdayInteractionsRuntime"] = (
                            _compile_cg02_birthday_interactions_runtime(
                                records, next_quest, next_stage_sources, definition))
                        party_runtime["nextBoundary"] = {"applied": True, "blocker": None}
                    overseer_speech["dadPartyRuntime"] = party_runtime
                    overseer_speech["nextBoundary"] = {"applied": True, "blocker": None}
                dad_speech["overseerSpeechRuntime"] = overseer_speech
                dad_speech["nextBoundary"] = {"applied": True, "blocker": None}
            intro_runtime["dadSpeechRuntime"] = dad_speech
            intro_runtime["nextBoundary"] = {
                "applied": True,
                "blocker": None,
            }
        cg02_stage0["introRuntime"] = intro_runtime
    lead_contract["completion"] = {
        "schema": "opennv-fo3-cg01-stage-90-to-cg02-runtime/v1",
        "sourceStage": end_stage,
        "timer": {
            "decrementSource": "GetSecondsPassed",
            "targetStage": timer_target,
            "scriptFormId": _form_id(quest_script.form_id),
            "scriptEditorId": _editor_id(quest_script),
            "scriptSourceSha256": script_hash,
        },
        "stage90Result": {
            "sourceSha256": hashlib.sha256(stage90_source.encode("cp1252")).hexdigest(),
            "commands": resolved_stage90,
        },
        "stage100Result": {
            "sourceSha256": hashlib.sha256(stage100_source.encode("cp1252")).hexdigest(),
            "commands": resolved_stage100,
        },
        "cg02Stage0": cg02_stage0,
        "nextBoundary": {
            "applied": True,
            "questFormId": _form_id(next_quest.form_id),
            "questEditorId": _editor_id(next_quest),
            "stage": int(stage100_commands[-1]["stage"]),
            "blocker": cg02_stage0["nextBoundary"]["blocker"],
        },
    }
    navmeshes = [
        parse_navmesh(record)
        for record in records
        if record.signature == "NAVM"
    ]
    navmeshes = [value for value in navmeshes if value.cell_form_id == int(definition["cellFormId"], FORM_ID_RADIX)]
    if not navmeshes or len({value.form_id for value in navmeshes}) != len(navmeshes):
        raise ValueError("Fallout 3 CG01 Dad lead NAVM differs")
    lead_contract["navigation"] = {
        "schema": "opennv-owned-cell-navigation/v1",
        "navmeshes": [
            navmesh_manifest(value)
            for value in sorted(navmeshes, key=lambda item: item.form_id)
        ],
    }
    lead_contract["locomotion"] = {
        "rootNode": str(locomotion["rootNode"]),
        "logicalPath": canonical_member_path(str(locomotion["walkLogicalPath"])),
    }
    lead_contract["nextBoundary"] = {
        "applied": True,
        "blocker": None,
    }
    timer_contract = dict(stage20_interaction["timerTransition"])
    timer_contract["dadReturn"] = {
        "package": {
            "formId": _form_id(return_package.form_id), "editorId": _editor_id(return_package),
            "recordSha256": hashlib.sha256(return_package.data).hexdigest(),
            "condition": {**return_condition, "parameter1": _form_id(int(return_condition["parameter1"])), "reference": _form_id(int(return_condition["reference"]))},
            "target": {"formId": _form_id(return_target.form_id), "editorId": _editor_id(return_target),
                "recordSha256": hashlib.sha256(return_target.data).hexdigest(), "sourceTransform": _reference_transform_contract(return_target),
                "radiusGameUnits": return_radius},
            "completionStage": completion_target,
        },
        "completionResult": {"sourceSha256": hashlib.sha256(stage_sources[completion_target][0].encode("cp1252")).hexdigest(), "commands": stage72_compiled},
        "dialogueDelaySeconds": float(dialogue_delay_rows[0]["value"]),
        "dialogue": {"topicFormId": _form_id(topic.form_id), "topicEditorId": _editor_id(topic),
            "voiceType": dict(_compile_cg01_stage12_dad_response(
                records, definition, quest, stage_sources, dad_reference, dad_base, dad_script, topic
            )["dialogue"]["voiceType"]), "branches": return_cues,
            "dialoguePlaybackPrepared": False, "dialoguePlaybackImplemented": False},
        "targetStage": dialogue_target,
        "bibleTravel": bible_contract,
        "dadLead": lead_contract,
        "nextBoundary": {"applied": True, "blocker": None},
    }
    stage20_interaction["timerTransition"] = timer_contract

    return {
        "schema": "opennv-fo3-cg01-stage-14-to-20-runtime/v1",
        "status": "source-backed-package-dialogue-runtime-ready",
        "sourceStage": stages[0],
        "stage16": stages[1],
        "stage18": stages[2],
        "targetStage": stages[3],
        "dadReferenceFormId": _form_id(dad_reference.form_id),
        "packages": {
            "closeGate": {**close_gate, "completionStage": stages[1]},
            "closeDoor": {**close_door, "completionStage": stages[2]},
            "leaveRoom": leave_room,
        },
        "stage16Result": {
            "sourceSha256": hashlib.sha256(stage_sources[stages[1]][0].encode("cp1252")).hexdigest(),
            "commands": [compile_command(text, index) for index, text in enumerate(stage16_commands)],
        },
        "dialogue": {
            "topicFormId": _form_id(topic.form_id),
            "topicEditorId": _editor_id(topic),
            "voiceType": dict(_compile_cg01_stage12_dad_response(
                records, definition, quest, stage_sources, dad_reference, dad_base, dad_script, topic
            )["dialogue"]["voiceType"]),
            "branches": cues,
            "dialoguePlaybackPrepared": False,
            "dialoguePlaybackImplemented": False,
        },
        "stage18Result": {
            "sourceSha256": hashlib.sha256(stage_sources[stages[2]][0].encode("cp1252")).hexdigest(),
            "commands": [compile_command(text, index) for index, text in enumerate(stage18_commands)],
        },
        "stage20Result": {
            "sourceSha256": hashlib.sha256(stage_sources[stages[3]][0].encode("cp1252")).hexdigest(),
            "commands": [compile_command(text, index) for index, text in enumerate(stage20_commands)],
        },
        "stage20Interaction": stage20_interaction,
        "nextBoundary": {
            "applied": True,
            "blocker": None,
        },
    }


def _compile_cg01_post_stage5_transition(
    records: tuple[object, ...],
    definition: dict[str, object],
    quest: object,
    stage_sources: dict[int, list[str]],
    dad_reference: object,
    dad_base: object,
    dad_script: object,
) -> dict[str, object]:
    source_stage = int(definition["nestedStage"])
    target_stage = int(definition["dialogueTargetStage"])
    if source_stage != 5 or target_stage != 10:
        raise ValueError("Fallout 3 CG01 Dad dialogue stage join differs")
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    topic_editor_id = str(definition["dadSpeechTopicEditorId"])
    topic_form_id = int(str(definition["dadSpeechTopicFormId"]), FORM_ID_RADIX)
    topics = [
        record
        for record in by_editor.get(topic_editor_id.casefold(), [])
        if record.signature == DIALOGUE_TOPIC_RECORD
    ]
    if len(topics) != 1 or topics[0].form_id != topic_form_id:
        raise ValueError("Fallout 3 CG01 Dad dialogue topic identity differs")
    topic = topics[0]
    if struct.unpack("<I", _single_subrecord(topic, "QSTI"))[0] != quest.form_id:
        raise ValueError("Fallout 3 CG01 Dad dialogue topic quest differs")

    dad_script_source = _script_source(dad_script)
    required_variables = {"doTalk": "short", "talking": "short", "timer": "float"}
    for variable, variable_type in required_variables.items():
        declarations = [
            match.group("type").casefold()
            for match in re.finditer(
                rf"^\s*(?P<type>short|float)\s+{re.escape(variable)}\b",
                dad_script_source,
                re.IGNORECASE | re.MULTILINE,
            )
        ]
        if declarations != [variable_type]:
            raise ValueError(f"Fallout 3 CG01 Dad dialogue variable differs: {variable}")
    if (
        re.search(
            r"\bif\s+doTalk\s*==\s*1\s*&&\s*talking\s*==\s*0\b",
            dad_script_source,
            re.IGNORECASE,
        )
        is None
        or re.search(
            rf"\bSayTo\s+player\s+{re.escape(topic_editor_id)}\s+1\b",
            dad_script_source,
            re.IGNORECASE,
        )
        is None
        or re.search(
            r"\bset\s+timer\s+to\s+timer\s*-\s*GetSecondsPassed\b",
            dad_script_source,
            re.IGNORECASE,
        )
        is None
    ):
        raise ValueError("Fallout 3 CG01 Dad dialogue trigger script differs")

    voice_links = [
        struct.unpack("<I", subrecord.data)[0]
        for subrecord in iter_subrecords(dad_base)
        if subrecord.signature == "VTCK" and len(subrecord.data) == FORM_ID_BYTES
    ]
    if len(voice_links) != 1:
        raise ValueError("Fallout 3 CG01 Dad voice type is ambiguous")
    voice = by_form.get(voice_links[0])
    if voice is None or voice.signature != VOICE_TYPE_RECORD:
        raise ValueError("Fallout 3 CG01 Dad voice type is absent")

    pass_definitions = (
        (0, definition["dadSpeechPreludeInfoFormIds"]),
        (1, definition["dadSpeechStageInfoFormIds"]),
    )
    branches = []
    for sequence, raw_form_ids in pass_definitions:
        expected_info_forms = {
            int(str(value), FORM_ID_RADIX) for value in raw_form_ids
        }
        if len(expected_info_forms) != 2:
            raise ValueError("Fallout 3 CG01 Dad dialogue pass is incomplete")
        for info_form_id in sorted(expected_info_forms):
            info = by_form.get(info_form_id)
            if info is None or info.signature != DIALOGUE_INFO_RECORD:
                raise ValueError(
                    f"Fallout 3 CG01 Dad INFO is absent: {_form_id(info_form_id)}"
                )
            if not any(
                group.group_type == DIALOGUE_CHILD_GROUP_TYPE
                and group.label_u32 == topic.form_id
                for group in info.groups
            ):
                raise ValueError("Fallout 3 CG01 Dad INFO topic ownership differs")
            if struct.unpack("<I", _single_subrecord(info, "QSTI"))[0] != quest.form_id:
                raise ValueError("Fallout 3 CG01 Dad INFO quest ownership differs")
            conditions = [
                _dialogue_condition(subrecord.data)
                for subrecord in iter_subrecords(info)
                if subrecord.signature == "CTDA"
            ]
            by_function = {int(row["function"]): row for row in conditions}
            if len(conditions) != 2 or set(by_function) != {
                GET_PC_IS_SEX_FUNCTION,
                GET_IS_ID_FUNCTION,
            }:
                raise ValueError("Fallout 3 CG01 Dad INFO conditions differ")
            sex_condition = by_function[GET_PC_IS_SEX_FUNCTION]
            identity_condition = by_function[GET_IS_ID_FUNCTION]
            sex_value = int(sex_condition["parameter1"])
            if (
                sex_value not in {0, 1}
                or sex_condition["operatorFlags"] != 0
                or sex_condition["comparisonValue"] != 1.0
                or sex_condition["parameter2"] != 0
                or sex_condition["runOn"] != 0
                or sex_condition["reference"] != 0
            ):
                raise ValueError("Fallout 3 CG01 Dad INFO sex condition differs")
            if (
                identity_condition["operatorFlags"] != 0
                or identity_condition["comparisonValue"] != 1.0
                or identity_condition["parameter1"] != dad_base.form_id
                or identity_condition["parameter2"] != 0
                or identity_condition["runOn"] != 0
                or identity_condition["reference"] != 0
            ):
                raise ValueError("Fallout 3 CG01 Dad INFO identity condition differs")

            source_rows = _text_values(info, "SCTX")
            if not source_rows:
                raise ValueError("Fallout 3 CG01 Dad INFO result is absent")
            source = "\n".join(source_rows)
            source_commands = _source_commands(source)
            effects = []
            if sequence == 0:
                if len(source_commands) != 1:
                    raise ValueError("Fallout 3 CG01 Dad prelude result is ambiguous")
                timer_match = SET_REFERENCE_VARIABLE_PATTERN.fullmatch(source_commands[0])
                if (
                    timer_match is None
                    or timer_match.group("subject").casefold()
                    != (_editor_id(dad_reference) or "").casefold()
                    or timer_match.group("variable").casefold() != "timer"
                    or float(timer_match.group("value")) != 1.0
                ):
                    raise ValueError("Fallout 3 CG01 Dad prelude timer differs")
                effects.append(
                    {
                        "kind": "setScriptVariable",
                        "referenceFormId": _form_id(dad_reference.form_id),
                        "referenceEditorId": _editor_id(dad_reference),
                        "variable": "timer",
                        "variableType": "float",
                        "value": 1.0,
                    }
                )
            else:
                if len(source_commands) != 2:
                    raise ValueError("Fallout 3 CG01 Dad stage result is ambiguous")
                expected_results = (
                    (
                        str(definition["questEditorId"]),
                        quest.form_id,
                        target_stage,
                    ),
                    (
                        str(definition["tutorialQuestEditorId"]),
                        int(str(definition["tutorialQuestFormId"]), FORM_ID_RADIX),
                        int(definition["tutorialQuestStage"]),
                    ),
                )
                for result_source, (editor_id, form_id, stage) in zip(
                    source_commands, expected_results
                ):
                    match = SET_STAGE_PATTERN.fullmatch(result_source)
                    target_quest = by_form.get(form_id)
                    if (
                        match is None
                        or match.group("quest").casefold() != editor_id.casefold()
                        or int(match.group("stage")) != stage
                        or target_quest is None
                        or target_quest.signature != QUEST_RECORD
                        or (_editor_id(target_quest) or "").casefold() != editor_id.casefold()
                    ):
                        raise ValueError("Fallout 3 CG01 Dad stage command differs")
                    effects.append(
                        {
                            "kind": "setStage",
                            "questFormId": _form_id(target_quest.form_id),
                            "questEditorId": _editor_id(target_quest),
                            "stage": stage,
                        }
                    )

            response_lines = [value for value in _text_values(info, "NAM1") if value]
            if len(response_lines) != 1:
                raise ValueError("Fallout 3 CG01 Dad response is absent or ambiguous")
            response_text = response_lines[0]
            speaker_idle_links = [
                struct.unpack("<I", subrecord.data)[0]
                for subrecord in iter_subrecords(info)
                if subrecord.signature == "SNAM"
                and len(subrecord.data) == FORM_ID_BYTES
            ]
            if len(speaker_idle_links) != 1:
                raise ValueError("Fallout 3 CG01 Dad speaker idle is absent or ambiguous")
            speaker_idle = by_form.get(speaker_idle_links[0])
            if speaker_idle is None or speaker_idle.signature != IDLE_RECORD:
                raise ValueError("Fallout 3 CG01 Dad speaker idle does not resolve")
            speaker_idle_models = _text_values(speaker_idle, "MODL")
            if len(speaker_idle_models) != 1:
                raise ValueError("Fallout 3 CG01 Dad speaker idle model is unsupported")
            branches.append(
                {
                    "sequence": sequence,
                    "engineSex": "female" if sex_value == 1 else "male",
                    "infoFormId": _form_id(info.form_id),
                    "recordSha256": hashlib.sha256(info.data).hexdigest(),
                    "resultSourceSha256": hashlib.sha256(
                        source.encode("cp1252")
                    ).hexdigest(),
                    "effects": effects,
                    "conditions": [
                        {
                            **row,
                            "parameter1": _form_id(int(row["parameter1"])),
                            "reference": _form_id(int(row["reference"])),
                        }
                        for row in conditions
                    ],
                    "speakerIdle": {
                        "formId": _form_id(speaker_idle.form_id),
                        "editorId": _editor_id(speaker_idle),
                        "recordSha256": hashlib.sha256(speaker_idle.data).hexdigest(),
                        "modelPath": canonical_member_path(
                            f"meshes\\{speaker_idle_models[0]}"
                        ),
                    },
                    "response": {
                        "index": 1,
                        "text": response_text,
                        "textSha256": hashlib.sha256(
                            response_text.encode("utf-8")
                        ).hexdigest(),
                    },
                }
            )
    if {
        (int(row["sequence"]), str(row["engineSex"])) for row in branches
    } != {(0, "male"), (0, "female"), (1, "male"), (1, "female")}:
        raise ValueError("Fallout 3 CG01 Dad dialogue branches are incomplete")

    target_sources = stage_sources.get(target_stage, [])
    if len(target_sources) != 1:
        raise ValueError("Fallout 3 CG01 stage 10 result is ambiguous")
    target_source = target_sources[0]
    stage_commands = _parse_cg01_stage10_commands(target_source)
    resolved_commands = []
    for index, command in enumerate(stage_commands):
        kind = str(command["kind"])
        resolved: dict[str, object] = {"index": index, "kind": kind}
        if kind == "setObjectiveDisplayed":
            if (
                str(command["questEditorId"]).casefold()
                != (_editor_id(quest) or "").casefold()
                or int(command["index"]) != 10
                or int(command["value"]) != 1
            ):
                raise ValueError("Fallout 3 CG01 stage 10 objective differs")
            resolved.update(
                {
                    "questFormId": _form_id(quest.form_id),
                    "questEditorId": _editor_id(quest),
                    "objectiveIndex": 10,
                    "displayed": True,
                }
            )
        elif kind == "setScriptVariable":
            if (
                str(command["subject"]).casefold()
                != (_editor_id(dad_reference) or "").casefold()
                or str(command["variable"]).casefold() != "timer"
                or float(command["value"]) != 5.0
            ):
                raise ValueError("Fallout 3 CG01 stage 10 Dad timer differs")
            resolved.update(
                {
                    "referenceFormId": _form_id(dad_reference.form_id),
                    "referenceEditorId": _editor_id(dad_reference),
                    "scriptFormId": _form_id(dad_script.form_id),
                    "scriptEditorId": _editor_id(dad_script),
                    "scriptRecordSha256": hashlib.sha256(dad_script.data).hexdigest(),
                    "scriptSourceSha256": hashlib.sha256(
                        dad_script_source.encode("cp1252")
                    ).hexdigest(),
                    "variable": "timer",
                    "variableType": "float",
                    "value": 5.0,
                }
            )
        elif kind == "enablePlayerControls":
            resolved["arguments"] = list(command["arguments"])
        elif kind == "autosave":
            resolved["requestCount"] = 1
        else:
            raise ValueError(f"Fallout 3 CG01 stage 10 command is not resolved: {kind}")
        resolved_commands.append(resolved)

    walk_to_dad = _compile_cg01_walk_to_dad_transition(
        records,
        quest,
        stage_sources,
        dad_reference,
        dad_script,
        target_stage,
    )
    stage12_dad_response = _compile_cg01_stage12_dad_response(
        records,
        definition,
        quest,
        stage_sources,
        dad_reference,
        dad_base,
        dad_script,
        topic,
    )
    post_stage14_transition = _compile_cg01_post_stage14_transition(
        records,
        definition,
        quest,
        stage_sources,
        dad_reference,
        dad_base,
        dad_script,
        topic,
    )
    return {
        "schema": "opennv-fo3-cg01-stage-5-to-10-transition/v1",
        "status": "source-backed-dad-dialogue-and-stage-result-runtime-unapplied",
        "sourceStage": source_stage,
        "targetStage": target_stage,
        "dadScript": {
            "formId": _form_id(dad_script.form_id),
            "editorId": _editor_id(dad_script),
            "recordSha256": hashlib.sha256(dad_script.data).hexdigest(),
            "sourceSha256": hashlib.sha256(dad_script_source.encode("cp1252")).hexdigest(),
            "requiredVariables": [
                {"name": "doTalk", "type": "short", "value": 1},
                {"name": "talking", "type": "short", "value": 0},
            ],
            "timerVariable": {"name": "timer", "type": "float"},
            "decrementFunction": "GetSecondsPassed",
        },
        "dialogue": {
            "topic": {
                "formId": _form_id(topic.form_id),
                "editorId": _editor_id(topic),
                "recordSha256": hashlib.sha256(topic.data).hexdigest(),
                "questFormId": _form_id(quest.form_id),
            },
            "voiceType": {
                "formId": _form_id(voice.form_id),
                "editorId": _editor_id(voice),
                "recordSha256": hashlib.sha256(voice.data).hexdigest(),
            },
            "branches": sorted(
                branches,
                key=lambda row: (int(row["sequence"]), str(row["engineSex"])),
            ),
            "dialoguePlaybackPrepared": False,
            "dialoguePlaybackImplemented": False,
        },
        "stageResult": {
            "stageSourceSha256": hashlib.sha256(
                target_source.encode("cp1252")
            ).hexdigest(),
            "accountedCommandCount": len(resolved_commands),
            "commands": resolved_commands,
        },
        "postStage10TriggerTransition": walk_to_dad,
        "postStage12DadResponse": stage12_dad_response,
        "postStage14Transition": post_stage14_transition,
        "nextBoundary": {
            "applied": False,
            "blocker": "awaiting-source-owned-player-trigger-entry",
        },
    }


def _compile_post_stage65_dialogue(
    records: tuple[object, ...],
    selection: dict[str, object],
    quest_form_id: int,
    stage_sources: dict[int, list[str]],
) -> dict[str, object]:
    definition = dict(selection["postStage65Dialogue"])
    topic_editor_id = str(definition["topicEditorId"])
    topic_form_id = int(str(definition["topicFormId"]), FORM_ID_RADIX)
    target_stage = int(definition["targetStage"])
    expected_info_forms = {
        int(str(value), FORM_ID_RADIX) for value in definition["resultInfoFormIds"]
    }
    by_form = {record.form_id: record for record in records}
    topics = [
        record
        for record in records
        if record.signature == DIALOGUE_TOPIC_RECORD
        and (_editor_id(record) or "").casefold() == topic_editor_id.casefold()
    ]
    if len(topics) != 1 or topics[0].form_id != topic_form_id:
        raise ValueError("Fallout 3 post-stage-65 dialogue topic identity differs")
    topic = topics[0]
    if struct.unpack("<I", _single_subrecord(topic, "QSTI"))[0] != quest_form_id:
        raise ValueError("Fallout 3 post-stage-65 dialogue topic quest differs")

    branch_rows = []
    voice_form_ids = set()
    for info_form_id in sorted(expected_info_forms):
        info = by_form.get(info_form_id)
        if info is None or info.signature != DIALOGUE_INFO_RECORD:
            raise ValueError(
                f"Fallout 3 post-stage-65 INFO is absent: {_form_id(info_form_id)}"
            )
        if not any(
            group.group_type == DIALOGUE_CHILD_GROUP_TYPE
            and group.label_u32 == topic.form_id
            for group in info.groups
        ):
            raise ValueError("Fallout 3 post-stage-65 INFO topic ownership differs")
        if struct.unpack("<I", _single_subrecord(info, "QSTI"))[0] != quest_form_id:
            raise ValueError("Fallout 3 post-stage-65 INFO quest ownership differs")
        source = _script_source(info)
        source_commands = _source_commands(source)
        if len(source_commands) != 1:
            raise ValueError("Fallout 3 post-stage-65 INFO result is ambiguous")
        stage_match = SET_STAGE_PATTERN.fullmatch(source_commands[0])
        if (
            stage_match is None
            or stage_match.group("quest").casefold() != "cg00"
            or int(stage_match.group("stage")) != target_stage
        ):
            raise ValueError("Fallout 3 post-stage-65 INFO stage result differs")

        conditions = [
            _dialogue_condition(subrecord.data)
            for subrecord in iter_subrecords(info)
            if subrecord.signature == "CTDA"
        ]
        if len(conditions) != 3:
            raise ValueError("Fallout 3 post-stage-65 INFO conditions are incomplete")
        by_function = {int(row["function"]): row for row in conditions}
        if set(by_function) != {
            GET_IS_SEX_FUNCTION,
            GET_STAGE_FUNCTION,
            GET_IS_VOICE_TYPE_FUNCTION,
        }:
            raise ValueError("Fallout 3 post-stage-65 INFO condition functions differ")
        sex_condition = by_function[GET_IS_SEX_FUNCTION]
        stage_condition = by_function[GET_STAGE_FUNCTION]
        voice_condition = by_function[GET_IS_VOICE_TYPE_FUNCTION]
        sex_value = int(sex_condition["parameter1"])
        if (
            sex_value not in {0, 1}
            or sex_condition["operatorFlags"] != 0
            or sex_condition["comparisonValue"] != 1.0
            or sex_condition["runOn"] != 1
            or sex_condition["parameter2"] != 0
            or sex_condition["reference"] != 0
        ):
            raise ValueError("Fallout 3 post-stage-65 INFO sex condition differs")
        if (
            stage_condition["operatorFlags"] != 0x80
            or stage_condition["comparisonValue"] != float(target_stage)
            or stage_condition["parameter1"] != quest_form_id
            or stage_condition["parameter2"] != 0
            or stage_condition["runOn"] != 0
            or stage_condition["reference"] != 0
        ):
            raise ValueError("Fallout 3 post-stage-65 INFO quest-stage condition differs")
        voice_form_id = int(voice_condition["parameter1"])
        voice = by_form.get(voice_form_id)
        if (
            voice is None
            or voice.signature != VOICE_TYPE_RECORD
            or voice_condition["operatorFlags"] != 0
            or voice_condition["comparisonValue"] != 1.0
            or voice_condition["parameter2"] != 0
            or voice_condition["runOn"] != 0
            or voice_condition["reference"] != 0
        ):
            raise ValueError("Fallout 3 post-stage-65 INFO voice condition differs")
        voice_form_ids.add(voice_form_id)
        response_lines = [value for value in _text_values(info, "NAM1") if value]
        if len(response_lines) != 1:
            raise ValueError(
                "Fallout 3 post-stage-65 INFO response text is absent or ambiguous"
            )
        response_text = response_lines[0]
        branch_rows.append(
            {
                "engineSex": "female" if sex_value == 1 else "male",
                "infoFormId": _form_id(info.form_id),
                "recordSha256": hashlib.sha256(info.data).hexdigest(),
                "resultSourceSha256": hashlib.sha256(source.encode("cp1252")).hexdigest(),
                "targetStage": target_stage,
                "response": {
                    "index": 1,
                    "text": response_text,
                    "textSha256": hashlib.sha256(
                        response_text.encode("utf-8")
                    ).hexdigest(),
                },
                "conditions": [
                    {
                        **row,
                        "parameter1": _form_id(int(row["parameter1"])),
                        "reference": _form_id(int(row["reference"])),
                    }
                    for row in conditions
                ],
            }
        )
    if (
        {row["engineSex"] for row in branch_rows} != {"male", "female"}
        or len(voice_form_ids) != 1
    ):
        raise ValueError("Fallout 3 post-stage-65 dialogue branches are incomplete")
    voice = by_form[voice_form_ids.pop()]

    target_sources = stage_sources.get(target_stage, [])
    if len(target_sources) != 1:
        raise ValueError("Fallout 3 CG00 stage 80 result source is ambiguous")
    target_source = target_sources[0]
    return {
        "schema": "opennv-fo3-cg00-post-stage-65-dialogue/v1",
        "status": "source-backed-info-result-trigger",
        "sourceStage": 65,
        "topic": {
            "formId": _form_id(topic.form_id),
            "editorId": topic_editor_id,
            "recordSha256": hashlib.sha256(topic.data).hexdigest(),
            "questFormId": _form_id(quest_form_id),
        },
        "voiceType": {
            "formId": _form_id(voice.form_id),
            "editorId": _editor_id(voice),
            "recordSha256": hashlib.sha256(voice.data).hexdigest(),
        },
        "branches": sorted(branch_rows, key=lambda row: str(row["engineSex"])),
        "dialoguePlaybackImplemented": False,
        "targetStage": target_stage,
        "stageResult": {
            "stageSourceSha256": hashlib.sha256(target_source.encode("cp1252")).hexdigest(),
            "commands": _parse_stage80_commands(target_source),
            "runtimeReady": False,
            "blocker": "fo3-cg00-stage-80-state-commands-not-compiled",
        },
    }


def _compile_post_stage80_dialogue(
    records: tuple[object, ...],
    selection: dict[str, object],
    quest_form_id: int,
    stage_sources: dict[int, list[str]],
) -> tuple[dict[str, object], dict[str, object]]:
    definition = dict(selection["postStage80Dialogue"])
    source_stage = 80
    target_stage = int(definition["targetStage"])
    topic_form_id = int(str(definition["topicFormId"]), FORM_ID_RADIX)
    info_form_id = int(str(definition["resultInfoFormId"]), FORM_ID_RADIX)
    by_form = {record.form_id: record for record in records}
    topic = by_form.get(topic_form_id)
    if (
        topic is None
        or topic.signature != DIALOGUE_TOPIC_RECORD
        or (_editor_id(topic) or "").casefold()
        != str(definition["topicEditorId"]).casefold()
        or struct.unpack("<I", _single_subrecord(topic, "QSTI"))[0] != quest_form_id
    ):
        raise ValueError("Fallout 3 post-stage-80 dialogue topic identity differs")
    info = by_form.get(info_form_id)
    if info is None or info.signature != DIALOGUE_INFO_RECORD:
        raise ValueError("Fallout 3 post-stage-80 result INFO is absent")
    if not any(
        group.group_type == DIALOGUE_CHILD_GROUP_TYPE
        and group.label_u32 == topic_form_id
        for group in info.groups
    ):
        raise ValueError("Fallout 3 post-stage-80 INFO topic ownership differs")
    if struct.unpack("<I", _single_subrecord(info, "QSTI"))[0] != quest_form_id:
        raise ValueError("Fallout 3 post-stage-80 INFO quest ownership differs")
    source = _script_source(info)
    source_commands = _source_commands(source)
    if len(source_commands) != 1:
        raise ValueError("Fallout 3 post-stage-80 INFO result is ambiguous")
    match = SET_STAGE_PATTERN.fullmatch(source_commands[0])
    if (
        match is None
        or match.group("quest").casefold() != "cg00"
        or int(match.group("stage")) != target_stage
    ):
        raise ValueError("Fallout 3 post-stage-80 INFO result differs")

    conditions = [
        _dialogue_condition(subrecord.data)
        for subrecord in iter_subrecords(info)
        if subrecord.signature == "CTDA"
    ]
    if len(conditions) != 2:
        raise ValueError("Fallout 3 post-stage-80 INFO conditions are incomplete")
    by_function = {int(row["function"]): row for row in conditions}
    if set(by_function) != {GET_STAGE_FUNCTION, GET_IS_VOICE_TYPE_FUNCTION}:
        raise ValueError("Fallout 3 post-stage-80 INFO condition functions differ")
    stage_condition = by_function[GET_STAGE_FUNCTION]
    if (
        stage_condition["operatorFlags"] != 0x60
        or stage_condition["comparisonValue"] != float(source_stage)
        or stage_condition["parameter1"] != quest_form_id
        or stage_condition["parameter2"] != 0
        or stage_condition["runOn"] != 0
        or stage_condition["reference"] != 0
    ):
        raise ValueError("Fallout 3 post-stage-80 INFO quest-stage condition differs")
    voice_condition = by_function[GET_IS_VOICE_TYPE_FUNCTION]
    voice_form_id = int(voice_condition["parameter1"])
    voice = by_form.get(voice_form_id)
    if (
        voice is None
        or voice.signature != VOICE_TYPE_RECORD
        or voice_condition["operatorFlags"] != 0
        or voice_condition["comparisonValue"] != 1.0
        or voice_condition["parameter2"] != 0
        or voice_condition["runOn"] != 0
        or voice_condition["reference"] != 0
    ):
        raise ValueError("Fallout 3 post-stage-80 INFO voice condition differs")

    target_sources = stage_sources.get(target_stage, [])
    if len(target_sources) != 1:
        raise ValueError("Fallout 3 CG00 stage 85 result source is ambiguous")
    target_source = target_sources[0]
    target_commands = _source_commands(target_source)
    if target_commands:
        raise ValueError("Fallout 3 CG00 stage 85 result unexpectedly contains commands")
    transition_schema = "opennv-fo3-cg00-stage-85-transition/v1"
    stage_source_sha256 = hashlib.sha256(target_source.encode("cp1252")).hexdigest()
    dialogue = {
        "schema": "opennv-fo3-cg00-post-stage-80-dialogue/v1",
        "status": "source-backed-info-result-trigger",
        "sourceStage": source_stage,
        "topic": {
            "formId": _form_id(topic.form_id),
            "editorId": _editor_id(topic),
            "recordSha256": hashlib.sha256(topic.data).hexdigest(),
            "questFormId": _form_id(quest_form_id),
        },
        "voiceType": {
            "formId": _form_id(voice.form_id),
            "editorId": _editor_id(voice),
            "recordSha256": hashlib.sha256(voice.data).hexdigest(),
        },
        "info": {
            "formId": _form_id(info.form_id),
            "recordSha256": hashlib.sha256(info.data).hexdigest(),
            "resultSourceSha256": hashlib.sha256(source.encode("cp1252")).hexdigest(),
            "conditions": [
                {
                    **row,
                    "parameter1": _form_id(int(row["parameter1"])),
                    "reference": _form_id(int(row["reference"])),
                }
                for row in conditions
            ],
        },
        "dialoguePlaybackImplemented": False,
        "targetStage": target_stage,
        "stageResult": {
            "stageSourceSha256": stage_source_sha256,
            "commands": [],
            "runtimeReady": True,
            "contractSchema": transition_schema,
        },
    }
    transition = {
        "schema": transition_schema,
        "status": "source-backed-empty-stage-result-application",
        "sourceStage": source_stage,
        "stage": target_stage,
        "dialogueTriggerSchema": dialogue["schema"],
        "stageSourceSha256": stage_source_sha256,
        "accountedCommandCount": 0,
        "commands": [],
        "nextBoundary": "fo3-cg00-post-stage-85-dialogue-trigger-not-compiled",
    }
    return dialogue, transition


def _compile_post_stage85_dialogue(
    records: tuple[object, ...],
    selection: dict[str, object],
    quest_form_id: int,
    quest_script: object,
    stage_sources: dict[int, list[str]],
) -> tuple[dict[str, object], dict[str, object]]:
    definition = dict(selection["postStage85Dialogue"])
    source_stage = 85
    minimum_stage = int(definition["minimumStage"])
    target_stage = int(definition["targetStage"])
    topic_form_id = int(str(definition["topicFormId"]), FORM_ID_RADIX)
    info_form_id = int(str(definition["resultInfoFormId"]), FORM_ID_RADIX)
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    topic = by_form.get(topic_form_id)
    if (
        topic is None
        or topic.signature != DIALOGUE_TOPIC_RECORD
        or (_editor_id(topic) or "").casefold()
        != str(definition["topicEditorId"]).casefold()
        or struct.unpack("<I", _single_subrecord(topic, "QSTI"))[0] != quest_form_id
    ):
        raise ValueError("Fallout 3 post-stage-85 dialogue topic identity differs")
    info = by_form.get(info_form_id)
    if info is None or info.signature != DIALOGUE_INFO_RECORD:
        raise ValueError("Fallout 3 post-stage-85 result INFO is absent")
    if not any(
        group.group_type == DIALOGUE_CHILD_GROUP_TYPE
        and group.label_u32 == topic_form_id
        for group in info.groups
    ):
        raise ValueError("Fallout 3 post-stage-85 INFO topic ownership differs")
    if struct.unpack("<I", _single_subrecord(info, "QSTI"))[0] != quest_form_id:
        raise ValueError("Fallout 3 post-stage-85 INFO quest ownership differs")
    continuation_count = sum(
        1 for subrecord in iter_subrecords(info) if subrecord.signature == "NEXT"
    )
    if continuation_count != 1:
        raise ValueError("Fallout 3 post-stage-85 INFO continuation marker differs")
    source = _script_source(info)
    source_commands = _source_commands(source)
    if len(source_commands) != 1:
        raise ValueError("Fallout 3 post-stage-85 INFO result is ambiguous")
    match = SET_STAGE_PATTERN.fullmatch(source_commands[0])
    if (
        match is None
        or match.group("quest").casefold() != "cg00"
        or int(match.group("stage")) != target_stage
    ):
        raise ValueError("Fallout 3 post-stage-85 INFO result differs")

    conditions = [
        _dialogue_condition(subrecord.data)
        for subrecord in iter_subrecords(info)
        if subrecord.signature == "CTDA"
    ]
    if len(conditions) != 2:
        raise ValueError("Fallout 3 post-stage-85 INFO conditions are incomplete")
    by_function = {int(row["function"]): row for row in conditions}
    if set(by_function) != {GET_STAGE_FUNCTION, GET_IS_VOICE_TYPE_FUNCTION}:
        raise ValueError("Fallout 3 post-stage-85 INFO condition functions differ")
    stage_condition = by_function[GET_STAGE_FUNCTION]
    if (
        stage_condition["operatorFlags"] != 0x60
        or stage_condition["comparisonValue"] != float(minimum_stage)
        or stage_condition["parameter1"] != quest_form_id
        or stage_condition["parameter2"] != 0
        or stage_condition["runOn"] != 0
        or stage_condition["reference"] != 0
        or minimum_stage >= source_stage
    ):
        raise ValueError("Fallout 3 post-stage-85 INFO quest-stage condition differs")
    voice_condition = by_function[GET_IS_VOICE_TYPE_FUNCTION]
    voice_form_id = int(voice_condition["parameter1"])
    voice = by_form.get(voice_form_id)
    if (
        voice is None
        or voice.signature != VOICE_TYPE_RECORD
        or voice_condition["operatorFlags"] != 0
        or voice_condition["comparisonValue"] != 1.0
        or voice_condition["parameter2"] != 0
        or voice_condition["runOn"] != 0
        or voice_condition["reference"] != 0
    ):
        raise ValueError("Fallout 3 post-stage-85 INFO voice condition differs")
    response_lines = [value for value in _text_values(info, "NAM1") if value]
    if len(response_lines) != 1:
        raise ValueError("Fallout 3 post-stage-85 response text is absent or ambiguous")
    response_text = response_lines[0]

    target_sources = stage_sources.get(target_stage, [])
    if len(target_sources) != 1:
        raise ValueError("Fallout 3 CG00 stage 90 result source is ambiguous")
    target_source = target_sources[0]
    source_stage_commands = _parse_stage90_commands(target_source)
    expected_kinds = [
        "setQuestVariable",
        "setQuestVariable",
        "applyImageSpaceModifier",
        "playSound",
    ]
    if [str(command["kind"]) for command in source_stage_commands] != expected_kinds:
        raise ValueError("Fallout 3 CG00 stage 90 command order differs")

    quest_script_source = _script_source(quest_script)
    quest_script_hash = hashlib.sha256(quest_script_source.encode("cp1252")).hexdigest()
    resolved_commands = []
    for index, command in enumerate(source_stage_commands):
        kind = str(command["kind"])
        if kind == "setQuestVariable":
            if str(command["subject"]).casefold() != "cg00":
                raise ValueError("Fallout 3 stage 90 variable owner differs")
            variable = str(command["variable"])
            declarations = [
                declaration.group("type").casefold()
                for declaration in re.finditer(
                    rf"^\s*(?P<type>short|float)\s+{re.escape(variable)}\b",
                    quest_script_source,
                    re.IGNORECASE | re.MULTILINE,
                )
            ]
            if len(declarations) != 1:
                raise ValueError("Fallout 3 stage 90 quest variable is ambiguous")
            resolved_commands.append(
                {
                    "index": index,
                    "kind": kind,
                    "questFormId": _form_id(quest_form_id),
                    "questEditorId": "CG00",
                    "scriptFormId": _form_id(quest_script.form_id),
                    "scriptEditorId": _editor_id(quest_script),
                    "scriptSourceSha256": quest_script_hash,
                    "variable": variable,
                    "variableType": declarations[0],
                    "value": command["value"],
                }
            )
            continue
        if kind == "applyImageSpaceModifier":
            editor_id = str(command["modifierEditorId"])
            matches = [
                record
                for record in by_editor.get(editor_id.casefold(), [])
                if record.signature == IMAGE_SPACE_MODIFIER_RECORD
            ]
            if len(matches) != 1:
                raise ValueError("Fallout 3 stage 90 image-space modifier is ambiguous")
            parsed = parse_image_space_modifier(matches[0]).manifest()
            resolved_commands.append(
                {
                    "index": index,
                    "kind": kind,
                    "modifier": {
                        **parsed,
                        "formId": _form_id(matches[0].form_id),
                    },
                }
            )
            continue
        editor_id = str(command["soundEditorId"])
        matches = [
            record
            for record in by_editor.get(editor_id.casefold(), [])
            if record.signature == SOUND_RECORD
        ]
        if len(matches) != 1:
            raise ValueError("Fallout 3 stage 90 sound identity is ambiguous")
        sound = matches[0]
        sound_path = _text_values(sound, "FNAM")
        sound_data = [
            subrecord.data
            for subrecord in iter_subrecords(sound)
            if subrecord.signature == "SNDD"
        ]
        if len(sound_path) != 1 or len(sound_data) != 1:
            raise ValueError("Fallout 3 stage 90 sound record layout is unsupported")
        resolved_commands.append(
            {
                "index": index,
                "kind": kind,
                "sound": {
                    "formId": _form_id(sound.form_id),
                    "editorId": editor_id,
                    "logicalPath": canonical_member_path(f"sound\\{sound_path[0]}"),
                    "recordSha256": hashlib.sha256(sound.data).hexdigest(),
                    "soundDataSha256": hashlib.sha256(sound_data[0]).hexdigest(),
                },
            }
        )

    stage_schema = "opennv-fo3-cg00-stage-90-transition/v1"
    stage_source_hash = hashlib.sha256(target_source.encode("cp1252")).hexdigest()
    dialogue = {
        "schema": "opennv-fo3-cg00-post-stage-85-dialogue/v1",
        "status": "source-backed-info-result-trigger",
        "sourceStage": source_stage,
        "minimumQuestStage": minimum_stage,
        "topic": {
            "formId": _form_id(topic.form_id),
            "editorId": _editor_id(topic),
            "recordSha256": hashlib.sha256(topic.data).hexdigest(),
            "questFormId": _form_id(quest_form_id),
        },
        "voiceType": {
            "formId": _form_id(voice.form_id),
            "editorId": _editor_id(voice),
            "recordSha256": hashlib.sha256(voice.data).hexdigest(),
        },
        "branches": [
            {
                "infoFormId": _form_id(info.form_id),
                "recordSha256": hashlib.sha256(info.data).hexdigest(),
                "resultSourceSha256": hashlib.sha256(source.encode("cp1252")).hexdigest(),
                "targetStage": target_stage,
                "continuationMarkerCount": continuation_count,
                "response": {
                    "index": 1,
                    "text": response_text,
                    "textSha256": hashlib.sha256(response_text.encode("utf-8")).hexdigest(),
                },
                "conditions": [
                    {
                        **row,
                        "parameter1": _form_id(int(row["parameter1"])),
                        "reference": _form_id(int(row["reference"])),
                    }
                    for row in conditions
                ],
            }
        ],
        "dialoguePlaybackImplemented": False,
        "targetStage": target_stage,
        "stageResult": {
            "stageSourceSha256": stage_source_hash,
            "commands": source_stage_commands,
            "runtimeReady": True,
            "contractSchema": stage_schema,
        },
    }
    transition = {
        "schema": stage_schema,
        "status": "source-backed-stage-result-contract",
        "sourceStage": source_stage,
        "stage": target_stage,
        "dialogueTriggerSchema": dialogue["schema"],
        "stageSourceSha256": stage_source_hash,
        "accountedCommandCount": len(resolved_commands),
        "commands": resolved_commands,
        "nextBoundary": "fo3-cg00-stage-90-timer-to-stage-100-not-implemented",
    }
    return dialogue, transition


def _compile_stage100_transition(
    records: tuple[object, ...],
    selection: dict[str, object],
    quest_form_id: int,
    quest_script: object,
    quest_script_source: str,
    stage_sources: dict[int, list[str]],
) -> dict[str, object]:
    definition = dict(selection["stage100Transition"])
    source_stage = int(definition["sourceStage"])
    target_stage = int(definition["targetStage"])
    target_sources = stage_sources.get(target_stage, [])
    if len(target_sources) != 1:
        raise ValueError("Fallout 3 CG00 stage 100 result source is ambiguous")
    target_source = target_sources[0]
    commands = _parse_stage100_commands(target_source)

    timer_chains = list(CG00_TIMER_CHAIN_PATTERN.finditer(quest_script_source))
    if len(timer_chains) != 1:
        raise ValueError("Fallout 3 CG00 stage 90 timer trigger is ambiguous")
    timer_matches = [
        match
        for match in CG00_TIMER_STAGE_PATTERN.finditer(
            timer_chains[0].group("stage_branches")
        )
        if int(match.group("source")) == source_stage
        and int(match.group("target")) == target_stage
    ]
    if len(timer_matches) != 1:
        raise ValueError("Fallout 3 CG00 stage 90 timer target differs")
    declarations = {
        name: [
            match.group("type").casefold()
            for match in re.finditer(
                rf"^\s*(?P<type>short|float)\s+{name}\b",
                quest_script_source,
                re.IGNORECASE | re.MULTILINE,
            )
        ]
        for name in ("timer", "runTimer")
    }
    if declarations != {"timer": ["float"], "runTimer": ["short"]}:
        raise ValueError("Fallout 3 CG00 timer variable declarations differ")

    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    def unique_record(editor_id: str, signature: str, label: str) -> object:
        matches = [
            record
            for record in by_editor.get(editor_id.casefold(), [])
            if record.signature == signature
        ]
        if len(matches) != 1:
            raise ValueError(f"Fallout 3 stage 100 {label} is ambiguous: {editor_id}")
        return matches[0]

    def actor_reference(editor_id: str) -> tuple[object, object]:
        reference = unique_record(editor_id, ACTOR_REFERENCE_RECORD, "actor reference")
        base_form_id = struct.unpack("<I", _single_subrecord(reference, "NAME"))[0]
        base = by_form.get(base_form_id)
        if base is None or base.signature != ACTOR_BASE_RECORD:
            raise ValueError("Fallout 3 stage 100 actor base is absent")
        return reference, base

    resolved_commands = []
    for index, command in enumerate(commands):
        kind = str(command["kind"])
        resolved: dict[str, object] = {"index": index, "kind": kind}
        if kind == "removeScriptPackage":
            if str(command["subject"]).casefold() != "player":
                raise ValueError("Fallout 3 stage 100 package-removal subject differs")
            resolved["subject"] = "player"
        elif kind == "setScriptVariable":
            reference, base = actor_reference(str(command["subject"]))
            script_form_id = struct.unpack("<I", _single_subrecord(base, "SCRI"))[0]
            script = by_form.get(script_form_id)
            if script is None or script.signature != SCRIPT_RECORD:
                raise ValueError("Fallout 3 stage 100 actor script is absent")
            variable = str(command["variable"])
            variable_declarations = [
                match.group("type").casefold()
                for match in re.finditer(
                    rf"^\s*(?P<type>short|float)\s+{re.escape(variable)}\b",
                    _script_source(script),
                    re.IGNORECASE | re.MULTILINE,
                )
            ]
            if variable_declarations != ["short"] or command["value"] != 0:
                raise ValueError("Fallout 3 stage 100 actor variable differs")
            resolved.update(
                {
                    "referenceFormId": _form_id(reference.form_id),
                    "referenceEditorId": _editor_id(reference),
                    "referenceRecordSha256": hashlib.sha256(reference.data).hexdigest(),
                    "baseFormId": _form_id(base.form_id),
                    "baseEditorId": _editor_id(base),
                    "baseRecordSha256": hashlib.sha256(base.data).hexdigest(),
                    "scriptFormId": _form_id(script.form_id),
                    "scriptEditorId": _editor_id(script),
                    "scriptSourceSha256": hashlib.sha256(
                        _script_source(script).encode("cp1252")
                    ).hexdigest(),
                    "variable": variable,
                    "variableType": "short",
                    "value": 0,
                }
            )
        elif kind == "removeImageSpaceModifier":
            editor_id = str(command["modifierEditorId"])
            modifier = unique_record(
                editor_id,
                IMAGE_SPACE_MODIFIER_RECORD,
                "image-space modifier",
            )
            if (
                editor_id.casefold()
                != str(definition["removedImageSpaceModifierEditorId"]).casefold()
                or modifier.form_id
                != int(str(definition["removedImageSpaceModifierFormId"]), FORM_ID_RADIX)
            ):
                raise ValueError("Fallout 3 stage 100 removed modifier identity differs")
            resolved["modifier"] = {
                "formId": _form_id(modifier.form_id),
                "editorId": _editor_id(modifier),
                "recordSha256": hashlib.sha256(modifier.data).hexdigest(),
            }
        elif kind == "disable":
            reference, base = actor_reference(str(command["subject"]))
            if reference.flags & INITIALLY_DISABLED_RECORD_FLAG:
                raise ValueError("Fallout 3 stage 100 disable target starts disabled")
            resolved.update(
                {
                    "referenceFormId": _form_id(reference.form_id),
                    "referenceEditorId": _editor_id(reference),
                    "referenceRecordSha256": hashlib.sha256(reference.data).hexdigest(),
                    "baseFormId": _form_id(base.form_id),
                    "baseEditorId": _editor_id(base),
                    "baseRecordSha256": hashlib.sha256(base.data).hexdigest(),
                    "initiallyDisabled": False,
                }
            )
        elif kind == "stopQuest":
            if str(command["questEditorId"]).casefold() != "cg00":
                raise ValueError("Fallout 3 stage 100 stopped quest differs")
            quest = by_form.get(quest_form_id)
            if quest is None or quest.signature != QUEST_RECORD:
                raise ValueError("Fallout 3 stage 100 CG00 identity is absent")
            resolved.update(
                {
                    "questFormId": _form_id(quest.form_id),
                    "questEditorId": _editor_id(quest),
                    "questRecordSha256": hashlib.sha256(quest.data).hexdigest(),
                }
            )
        elif kind == "setPlayerYoung":
            if command["value"] != 1:
                raise ValueError("Fallout 3 stage 100 player-young value differs")
            resolved["value"] = 1
        else:
            next_editor_id = str(definition["nextQuestEditorId"])
            next_stage = int(definition["nextQuestStage"])
            if (
                str(command["questEditorId"]).casefold() != next_editor_id.casefold()
                or int(command["stage"]) != next_stage
            ):
                raise ValueError("Fallout 3 stage 100 next-quest boundary differs")
            next_quest = unique_record(next_editor_id, QUEST_RECORD, "next quest")
            if next_quest.form_id != int(
                str(definition["nextQuestFormId"]), FORM_ID_RADIX
            ):
                raise ValueError("Fallout 3 stage 100 next-quest FormID differs")
            next_sources: dict[int, list[str]] = {}
            current_stage = None
            for subrecord in iter_subrecords(next_quest):
                if subrecord.signature == "INDX":
                    current_stage = int.from_bytes(subrecord.data, "little")
                elif subrecord.signature == "SCTX" and current_stage is not None:
                    next_sources.setdefault(current_stage, []).append(zstring(subrecord.data))
            stage_zero_sources = next_sources.get(next_stage, [])
            if len(stage_zero_sources) != 1:
                raise ValueError("Fallout 3 CG01 stage-zero result is ambiguous")
            stage_zero_source = stage_zero_sources[0]
            resolved.update(
                {
                    "questFormId": _form_id(next_quest.form_id),
                    "questEditorId": _editor_id(next_quest),
                    "questRecordSha256": hashlib.sha256(next_quest.data).hexdigest(),
                    "stage": next_stage,
                    "stageResultSourceSha256": hashlib.sha256(
                        stage_zero_source.encode("cp1252")
                    ).hexdigest(),
                    "stageResultCommandCount": len(_source_commands(stage_zero_source)),
                    "applied": False,
                }
            )
        resolved_commands.append(resolved)

    if resolved_commands[4]["referenceFormId"] != resolved_commands[2]["referenceFormId"]:
        raise ValueError("Fallout 3 stage 100 Dad disable/variable identity differs")
    quest_script_hash = hashlib.sha256(quest_script_source.encode("cp1252")).hexdigest()
    return {
        "schema": "opennv-fo3-cg00-stage-100-transition/v1",
        "status": "source-backed-timer-stage-result-through-next-quest-boundary",
        "sourceStage": source_stage,
        "stage": target_stage,
        "trigger": {
            "questFormId": _form_id(quest_form_id),
            "questEditorId": "CG00",
            "scriptFormId": _form_id(quest_script.form_id),
            "scriptEditorId": _editor_id(quest_script),
            "scriptSourceSha256": quest_script_hash,
            "runVariable": {"name": "runTimer", "type": "short", "requiredValue": 1},
            "timerVariable": {"name": "timer", "type": "float", "initialValue": 2.2},
            "decrementFunction": "GetSecondsPassed",
            "sourceStage": source_stage,
            "targetStage": target_stage,
        },
        "stageSourceSha256": hashlib.sha256(target_source.encode("cp1252")).hexdigest(),
        "accountedCommandCount": len(resolved_commands),
        "appliedCommandCount": len(resolved_commands) - 1,
        "commands": resolved_commands,
        "nextBoundary": {
            "commandIndex": len(resolved_commands) - 1,
            "kind": "setStage",
            "questFormId": resolved_commands[-1]["questFormId"],
            "questEditorId": resolved_commands[-1]["questEditorId"],
            "stage": resolved_commands[-1]["stage"],
            "stageResultSourceSha256": resolved_commands[-1]["stageResultSourceSha256"],
            "stageResultCommandCount": resolved_commands[-1]["stageResultCommandCount"],
            "applied": False,
            "blocker": "fo3-cg01-stage-0-runtime-application-not-implemented",
        },
    }


def _compile_cg01_stage0_transition(
    records: tuple[object, ...],
    selection: dict[str, object],
    stage100_transition: dict[str, object],
) -> dict[str, object]:
    definition = dict(selection["cg01Stage0Transition"])
    quest_editor_id = str(definition["questEditorId"])
    quest_form_id = int(str(definition["questFormId"]), FORM_ID_RADIX)
    entry_stage = int(definition["entryStage"])
    nested_stage = int(definition["nestedStage"])
    cell_form_id = int(str(definition["cellFormId"]), FORM_ID_RADIX)
    expected_reference_forms = {
        "dad": int(str(definition["dadReferenceFormId"]), FORM_ID_RADIX),
        "dadMarker": int(str(definition["dadStartMarkerFormId"]), FORM_ID_RADIX),
        "playerMarker": int(str(definition["playerStartMarkerFormId"]), FORM_ID_RADIX),
        "nextDad": int(str(definition["nextDadReferenceFormId"]), FORM_ID_RADIX),
    }
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    def unique_record(editor_id: str, signature: str, label: str) -> object:
        matches = [
            record
            for record in by_editor.get(editor_id.casefold(), [])
            if record.signature == signature
        ]
        if len(matches) != 1:
            raise ValueError(f"Fallout 3 CG01 {label} is ambiguous: {editor_id}")
        return matches[0]

    def reference_contract(
        editor_id: str,
        signature: str,
        expected_form_id: int,
        label: str,
    ) -> tuple[object, object, dict[str, object]]:
        reference = unique_record(editor_id, signature, label)
        if reference.form_id != expected_form_id:
            raise ValueError(f"Fallout 3 CG01 {label} FormID differs")
        parent_cell = cell_parent_form_id(reference)
        if parent_cell != cell_form_id:
            raise ValueError(f"Fallout 3 CG01 {label} CELL differs")
        base_form_id = struct.unpack("<I", _single_subrecord(reference, "NAME"))[0]
        base = by_form.get(base_form_id)
        expected_base_type = (
            ACTOR_BASE_RECORD if signature == ACTOR_REFERENCE_RECORD else STATIC_RECORD
        )
        if base is None or base.signature != expected_base_type:
            raise ValueError(f"Fallout 3 CG01 {label} base is absent")
        contract = {
            "recordType": reference.signature,
            "formId": _form_id(reference.form_id),
            "editorId": _editor_id(reference),
            "recordSha256": hashlib.sha256(reference.data).hexdigest(),
            "base": {
                "recordType": base.signature,
                "formId": _form_id(base.form_id),
                "editorId": _editor_id(base),
                "recordSha256": hashlib.sha256(base.data).hexdigest(),
            },
            "cellFormId": _form_id(parent_cell),
            "flags": reference.flags,
            "initiallyDisabled": bool(reference.flags & INITIALLY_DISABLED_RECORD_FLAG),
            "sourceTransform": _reference_transform_contract(reference),
        }
        return reference, base, contract

    quest = unique_record(quest_editor_id, QUEST_RECORD, "quest")
    if quest.form_id != quest_form_id:
        raise ValueError("Fallout 3 CG01 quest FormID differs")
    script_form_id = struct.unpack("<I", _single_subrecord(quest, "SCRI"))[0]
    quest_script = by_form.get(script_form_id)
    if quest_script is None or quest_script.signature != SCRIPT_RECORD:
        raise ValueError("Fallout 3 CG01 quest script is absent")
    quest_script_source = _script_source(quest_script)
    stage_sources: dict[int, list[str]] = {}
    current_stage = None
    for subrecord in iter_subrecords(quest):
        if subrecord.signature == "INDX":
            if len(subrecord.data) not in STAGE_INDEX_BYTES:
                raise ValueError("Fallout 3 CG01 stage index has an unexpected size")
            current_stage = int.from_bytes(subrecord.data, "little")
        elif subrecord.signature == "SCTX" and current_stage is not None:
            stage_sources.setdefault(current_stage, []).append(zstring(subrecord.data))
    entry_sources = stage_sources.get(entry_stage, [])
    nested_sources = stage_sources.get(nested_stage, [])
    if len(entry_sources) != 1:
        raise ValueError("Fallout 3 CG01 stage 0 result is ambiguous")
    if len(nested_sources) != 1:
        raise ValueError("Fallout 3 CG01 stage 5 result is ambiguous")
    entry_source = entry_sources[0]
    nested_source = nested_sources[0]
    stage0_commands = _parse_cg01_stage0_commands(entry_source)
    stage5_commands = _parse_cg01_stage5_commands(nested_source)

    dad_reference, dad_base, dad = reference_contract(
        str(stage0_commands[0]["subject"]),
        ACTOR_REFERENCE_RECORD,
        expected_reference_forms["dad"],
        "Dad reference",
    )
    dad_script_form_id = struct.unpack("<I", _single_subrecord(dad_base, "SCRI"))[0]
    dad_script = by_form.get(dad_script_form_id)
    if dad_script is None or dad_script.signature != SCRIPT_RECORD:
        raise ValueError("Fallout 3 CG01 Dad script is absent")
    _, _, dad_marker = reference_contract(
        str(stage0_commands[0]["target"]),
        PLACED_REFERENCE_RECORD,
        expected_reference_forms["dadMarker"],
        "Dad start marker",
    )
    if str(stage0_commands[1]["questEditorId"]).casefold() != quest_editor_id.casefold():
        raise ValueError("Fallout 3 CG01 stage 0 nested quest differs")
    if int(stage0_commands[1]["stage"]) != nested_stage:
        raise ValueError("Fallout 3 CG01 stage 0 nested stage differs")
    player_scale = float(stage0_commands[2]["value"])
    if not math.isfinite(player_scale) or player_scale != 0.4:
        raise ValueError("Fallout 3 CG01 stage 0 player scale differs")
    if str(stage0_commands[3]["subject"]).casefold() != "player":
        raise ValueError("Fallout 3 CG01 stage 0 player MoveTo subject differs")
    _, _, player_marker = reference_contract(
        str(stage0_commands[3]["target"]),
        PLACED_REFERENCE_RECORD,
        expected_reference_forms["playerMarker"],
        "player start marker",
    )

    resolved_stage5 = []
    enabled_references: list[dict[str, object]] = []
    for index, command in enumerate(stage5_commands):
        kind = str(command["kind"])
        resolved: dict[str, object] = {"index": index, "kind": kind}
        if kind in {
            "setLocationSpecificLoadScreensOnly",
            "setInCharGen",
            "autoDisplayObjectives",
            "setPlayerToddler",
            "setPlayerYoung",
        }:
            value = int(command["value"])
            if value != 1:
                raise ValueError(f"Fallout 3 CG01 stage 5 {kind} value differs")
            resolved["value"] = value
        elif kind == "enable":
            expected_key = "dad" if index == 2 else "nextDad"
            _, _, actor = reference_contract(
                str(command["subject"]),
                ACTOR_REFERENCE_RECORD,
                expected_reference_forms[expected_key],
                f"enabled actor command {index}",
            )
            if not actor["initiallyDisabled"]:
                raise ValueError("Fallout 3 CG01 enabled actor does not start disabled")
            resolved["reference"] = actor
            enabled_references.append(actor)
        elif kind == "setScriptVariable":
            reference = unique_record(
                str(command["subject"]), ACTOR_REFERENCE_RECORD, "variable actor reference"
            )
            if reference.form_id != dad_reference.form_id:
                raise ValueError("Fallout 3 CG01 variable actor differs from moved Dad")
            base_form_id = struct.unpack("<I", _single_subrecord(reference, "NAME"))[0]
            base = by_form.get(base_form_id)
            if base is None or base.form_id != dad_base.form_id:
                raise ValueError("Fallout 3 CG01 variable actor base differs")
            actor_script_form_id = struct.unpack("<I", _single_subrecord(base, "SCRI"))[0]
            actor_script = by_form.get(actor_script_form_id)
            if actor_script is None or actor_script.signature != SCRIPT_RECORD:
                raise ValueError("Fallout 3 CG01 Dad script is absent")
            variable = str(command["variable"])
            declarations = [
                match.group("type").casefold()
                for match in re.finditer(
                    rf"^\s*(?P<type>short|float)\s+{re.escape(variable)}\b",
                    _script_source(actor_script),
                    re.IGNORECASE | re.MULTILINE,
                )
            ]
            if declarations != ["short"]:
                raise ValueError(f"Fallout 3 CG01 Dad variable differs: {variable}")
            resolved.update(
                {
                    "reference": dad,
                    "script": {
                        "formId": _form_id(actor_script.form_id),
                        "editorId": _editor_id(actor_script),
                        "recordSha256": hashlib.sha256(actor_script.data).hexdigest(),
                        "sourceSha256": hashlib.sha256(
                            _script_source(actor_script).encode("cp1252")
                        ).hexdigest(),
                    },
                    "variable": variable,
                    "variableType": "short",
                    "value": int(command["value"]),
                }
            )
        elif kind in {"enablePlayerControls", "disablePlayerControls"}:
            resolved["arguments"] = list(command["arguments"])
        elif kind == "setNoActivationSound":
            sound_editor_id = str(command["soundEditorId"])
            sound = unique_record(sound_editor_id, SOUND_RECORD, "no-activation sound")
            expected_sound_form = int(str(definition["noActivationSoundFormId"]), FORM_ID_RADIX)
            if sound.form_id != expected_sound_form:
                raise ValueError("Fallout 3 CG01 no-activation sound FormID differs")
            sound_paths = _text_values(sound, "FNAM")
            sound_data = [
                subrecord.data
                for subrecord in iter_subrecords(sound)
                if subrecord.signature == "SNDD"
            ]
            if len(sound_paths) != 1 or len(sound_data) != 1:
                raise ValueError("Fallout 3 CG01 no-activation sound layout is unsupported")
            resolved["sound"] = {
                "formId": _form_id(sound.form_id),
                "editorId": _editor_id(sound),
                "recordSha256": hashlib.sha256(sound.data).hexdigest(),
                "soundDataSha256": hashlib.sha256(sound_data[0]).hexdigest(),
                "logicalPath": canonical_member_path(f"sound\\{sound_paths[0]}"),
                "selectionPolicy": "source-folder-variant-set-not-yet-bound",
            }
        elif kind == "playBink":
            expected_video = str(definition["transitionVideo"])
            if str(command["logicalPath"]).casefold() != expected_video.casefold():
                raise ValueError("Fallout 3 CG01 transition movie differs")
            resolved.update(
                {
                    "logicalPath": str(command["logicalPath"]),
                    "arguments": list(command["arguments"]),
                }
            )
        else:
            raise ValueError(f"Fallout 3 CG01 stage 5 command is not resolved: {kind}")
        resolved_stage5.append(resolved)

    if [actor["formId"] for actor in enabled_references] != [
        _form_id(expected_reference_forms["dad"]),
        _form_id(expected_reference_forms["nextDad"]),
    ]:
        raise ValueError("Fallout 3 CG01 enabled actor order differs")
    stage5_result = {
        "schema": "opennv-fo3-cg01-stage-5-result/v1",
        "questFormId": _form_id(quest.form_id),
        "questEditorId": _editor_id(quest),
        "stage": nested_stage,
        "stageSourceSha256": hashlib.sha256(nested_source.encode("cp1252")).hexdigest(),
        "accountedCommandCount": len(resolved_stage5),
        "commands": resolved_stage5,
    }
    resolved_stage0 = [
        {
            "index": 0,
            "kind": "moveToReference",
            "subject": dad,
            "target": dad_marker,
        },
        {
            "index": 1,
            "kind": "setStage",
            "questFormId": _form_id(quest.form_id),
            "questEditorId": _editor_id(quest),
            "stage": nested_stage,
            "stageResult": stage5_result,
        },
        {"index": 2, "kind": "setPlayerScale", "value": player_scale},
        {
            "index": 3,
            "kind": "moveToReference",
            "subject": {"role": "player"},
            "target": player_marker,
        },
    ]
    stage0_result = {
        "stage": entry_stage,
        "stageSourceSha256": hashlib.sha256(entry_source.encode("cp1252")).hexdigest(),
        "accountedCommandCount": len(resolved_stage0),
        "commands": resolved_stage0,
    }
    post_stage5_transition = _compile_cg01_post_stage5_transition(
        records,
        definition,
        quest,
        stage_sources,
        dad_reference,
        dad_base,
        dad_script,
    )
    if (
        int(stage100_transition["stage"]) != 100
        or int(dict(stage100_transition["nextBoundary"])["commandIndex"]) != 7
        or bool(dict(stage100_transition["nextBoundary"])["applied"])
    ):
        raise ValueError("Fallout 3 CG01 transition trigger boundary differs")
    return {
        "schema": "opennv-fo3-cg01-stage-0-to-5-transition/v1",
        "status": "source-backed-nested-stage-result-runtime-unapplied",
        "trigger": {
            "sourceSchema": str(stage100_transition["schema"]),
            "commandIndex": 7,
        },
        "quest": {
            "formId": _form_id(quest.form_id),
            "editorId": _editor_id(quest),
            "recordSha256": hashlib.sha256(quest.data).hexdigest(),
            "scriptFormId": _form_id(quest_script.form_id),
            "scriptEditorId": _editor_id(quest_script),
            "scriptRecordSha256": hashlib.sha256(quest_script.data).hexdigest(),
            "scriptSourceSha256": hashlib.sha256(
                quest_script_source.encode("cp1252")
            ).hexdigest(),
        },
        "cellFormId": _form_id(cell_form_id),
        "entryStage": entry_stage,
        "resultingStage": nested_stage,
        "accountedCommandCount": len(resolved_stage0) + len(resolved_stage5),
        "stage0Result": stage0_result,
        "postStage5Transition": post_stage5_transition,
        "nestedExecution": {
            "stage0CommandIndex": 1,
            "stage": nested_stage,
            "resultSchema": stage5_result["schema"],
        },
        "nextBoundary": {
            "applied": False,
            "blocker": "fo3-cg01-stage-0-runtime-application-not-implemented",
        },
    }


def _compile_cg00_section4_transition(
    records: tuple[object, ...],
    selection: dict[str, object],
    accepted_stage: int,
    accepted_source: str,
    accepted_stage_sources: dict[int, list[str]],
) -> dict[str, object]:
    package_names = [
        match.group("package")
        for match in ADD_SCRIPT_PACKAGE_PATTERN.finditer(accepted_source)
    ]
    if len(package_names) != 1:
        raise ValueError("Fallout 3 owned appearance acceptance package is ambiguous")
    package_name = package_names[0]
    package_recipe = dict(selection["section4Package"])
    if package_name.casefold() != str(package_recipe["editorId"]).casefold():
        raise ValueError("Fallout 3 CG00 Section 4 package identity differs")

    package_records = [
        record
        for record in records
        if record.signature == PACKAGE_RECORD
        and (_editor_id(record) or "").casefold() == package_name.casefold()
    ]
    if len(package_records) != 1:
        raise ValueError("Fallout 3 CG00 Section 4 package does not resolve uniquely")
    package = package_records[0]
    expected_package_form = int(str(package_recipe["formId"]), FORM_ID_RADIX)
    if package.form_id != expected_package_form:
        raise ValueError("Fallout 3 CG00 Section 4 package FormID differs")

    package_data = _single_subrecord(package, "PKDT")
    if len(package_data) != PACKAGE_DATA_BYTES:
        raise ValueError("Fallout 3 CG00 Section 4 PKDT layout is unsupported")
    flags, package_type, _unused, procedure_flags, type_flags, _unknown = struct.unpack(
        "<IBBHHH", package_data
    )
    location_data = _single_subrecord(package, "PLDT")
    if len(location_data) != PACKAGE_LOCATION_BYTES:
        raise ValueError("Fallout 3 CG00 Section 4 PLDT layout is unsupported")
    location_type, location_form_id, radius = struct.unpack("<III", location_data)
    expected_location_form = int(
        str(package_recipe["locationReferenceFormId"]), FORM_ID_RADIX
    )
    if location_form_id != expected_location_form:
        raise ValueError("Fallout 3 CG00 Section 4 location reference differs")

    idle_flags_data = _single_subrecord(package, "IDLF")
    idle_count_data = _single_subrecord(package, "IDLC")
    idle_timer_data = _single_subrecord(package, "IDLT")
    if (
        len(idle_flags_data) not in PACKAGE_IDLE_FLAG_BYTES
        or len(idle_count_data) not in PACKAGE_IDLE_COUNT_BYTES
        or len(idle_timer_data) != PACKAGE_IDLE_TIMER_BYTES
    ):
        raise ValueError("Fallout 3 CG00 Section 4 idle layout is unsupported")
    idle_flags = int.from_bytes(idle_flags_data, "little")
    idle_count = int.from_bytes(idle_count_data, "little")
    idle_timer = struct.unpack("<f", idle_timer_data)[0]
    if not math.isfinite(idle_timer):
        raise ValueError("Fallout 3 CG00 Section 4 idle timer is invalid")
    idle_form_ids = _form_id_list(package, "IDLA")
    if idle_count != len(idle_form_ids) or idle_count == 0:
        raise ValueError("Fallout 3 CG00 Section 4 idle count differs")

    idle_records = {
        record.form_id: record for record in records if record.signature == IDLE_RECORD
    }

    def idle_row(form_id: int) -> dict[str, object]:
        idle = idle_records.get(form_id)
        if idle is None:
            raise ValueError(
                f"Fallout 3 CG00 Section 4 IDLE does not resolve: {_form_id(form_id)}"
            )
        models = _text_values(idle, "MODL")
        if len(models) != 1 or not models[0].casefold().endswith(".kf"):
            raise ValueError("Fallout 3 CG00 Section 4 IDLE model is unsupported")
        return {
            "formId": _form_id(form_id),
            "editorId": _editor_id(idle),
            "modelPath": canonical_member_path(f"meshes\\{models[0]}"),
            "recordSha256": hashlib.sha256(idle.data).hexdigest(),
        }

    events: dict[str, dict[str, object] | None] = {}
    pending_event: str | None = None
    for subrecord in iter_subrecords(package):
        if subrecord.signature in PACKAGE_EVENT_NAMES:
            pending_event = PACKAGE_EVENT_NAMES[subrecord.signature]
            if pending_event in events:
                raise ValueError("Fallout 3 CG00 Section 4 package event is duplicated")
        elif subrecord.signature == "INAM" and pending_event is not None:
            if len(subrecord.data) != FORM_ID_BYTES:
                raise ValueError("Fallout 3 CG00 Section 4 package event IDLE is invalid")
            event_form_id = struct.unpack("<I", subrecord.data)[0]
            events[pending_event] = idle_row(event_form_id) if event_form_id else None
            pending_event = None
    if pending_event is not None or set(events) != set(PACKAGE_EVENT_NAMES.values()):
        raise ValueError("Fallout 3 CG00 Section 4 package events are incomplete")

    trigger_matches: list[tuple[object, re.Match[str]]] = []
    trigger_threshold_stage = int(selection["appearanceStage"])
    for script in (record for record in records if record.signature == SCRIPT_RECORD):
        for match in CG00_NEXT_STAGE_PATTERN.finditer(_script_source(script)):
            if int(match.group("source")) != trigger_threshold_stage:
                continue
            stage_commands = [
                int(stage_match.group("stage"))
                for stage_match in SET_CG00_STAGE_PATTERN.finditer(match.group("body"))
            ]
            if stage_commands == [int(match.group("target"))]:
                trigger_matches.append((script, match))
    if len(trigger_matches) != 1:
        raise ValueError("Fallout 3 CG00 post-appearance stage trigger is ambiguous")
    trigger_script, trigger = trigger_matches[0]
    target_stage = int(trigger.group("target"))
    target_sources = accepted_stage_sources.get(target_stage, [])
    if not target_sources:
        raise ValueError("Fallout 3 CG00 post-appearance stage result is absent")
    target_source = "\n".join(target_sources)
    unsupported_commands = []
    for command in _source_commands(target_source):
        if match := MATCH_RACE_PATTERN.fullmatch(command):
            unsupported_commands.append(
                {"kind": "matchRace", "subject": match.group("subject"), "target": "player"}
            )
            continue
        if match := MATCH_FACE_PATTERN.fullmatch(command):
            unsupported_commands.append(
                {
                    "kind": "matchFaceGeometry",
                    "subject": match.group("subject"),
                    "target": "player",
                    "template": match.group("template"),
                }
            )
            continue
        raise ValueError(
            f"Fallout 3 CG00 stage {target_stage} uses an unsupported command: {command}"
        )
    if not unsupported_commands:
        raise ValueError("Fallout 3 CG00 post-appearance stage result is empty")

    return {
        "schema": "opennv-fo3-cg00-player-package-transition/v1",
        "status": "source-backed-package-activation",
        "sourceStage": accepted_stage,
        "command": f"player.addScriptPackage {package_name}",
        "package": {
            "formId": _form_id(package.form_id),
            "editorId": package_name,
            "recordSha256": hashlib.sha256(package.data).hexdigest(),
            "flags": flags,
            "type": package_type,
            "procedureFlags": procedure_flags,
            "typeSpecificFlags": type_flags,
            "location": {
                "type": location_type,
                "referenceFormId": _form_id(location_form_id),
                "referenceEditorId": str(package_recipe["locationReferenceEditorId"]),
                "radius": radius,
            },
            "idleSelection": {
                "flags": idle_flags,
                "count": idle_count,
                "timerSeconds": idle_timer,
                "idles": [idle_row(form_id) for form_id in idle_form_ids],
            },
            "events": events,
        },
        "nextStageTrigger": {
            "scriptEditorId": _editor_id(trigger_script),
            "scriptFormId": _form_id(trigger_script.form_id),
            "scriptSourceSha256": hashlib.sha256(
                _script_source(trigger_script).encode("cp1252")
            ).hexdigest(),
            "condition": (
                f"getStage CG00 >= {trigger_threshold_stage} && "
                f"GetStageDone CG00 {target_stage} == 0"
            ),
            "thresholdStage": trigger_threshold_stage,
            "command": f"setstage CG00 {target_stage}",
            "targetStage": target_stage,
        },
        "nextStageResult": {
            "stage": target_stage,
            "stageSourceSha256": hashlib.sha256(target_source.encode("cp1252")).hexdigest(),
            "commands": unsupported_commands,
            "runtimeReady": False,
            "blocker": "fo3-cg00-stage-65-parent-race-face-runtime-not-implemented",
        },
    }


def _bind_cg00_package_animations(
    transition: dict[str, object],
    meshes_archive: BsaArchive,
    meshes_archive_sha256: str,
    package_key: str = "package",
) -> None:
    package = dict(transition[package_key])
    idle_selection = dict(package["idleSelection"])
    events = dict(package["events"])
    idle_rows = [
        *list(idle_selection["idles"]),
        *(row for row in events.values() if row is not None),
    ]
    assets: dict[str, dict[str, object]] = {}
    for row in idle_rows:
        idle = dict(row)
        form_id = str(idle["formId"])
        if form_id in assets:
            continue
        member = meshes_archive.extract(str(idle["modelPath"]))
        assets[form_id] = {
            **idle,
            "sourceArchive": meshes_archive.archive.name,
            "sourceArchiveSha256": meshes_archive_sha256,
            "sourceBytes": len(member.data),
            "sourceSha256": member.sha256,
        }
    package["animationSources"] = list(assets.values())
    transition[package_key] = package


def _bind_owned_dad_dialogue_audio(
    dialogue: dict[str, object],
    voices_archive: BsaArchive,
    voices_archive_sha256: str,
    profile_root: Path,
    secondary_voices_archive: BsaArchive | None = None,
    secondary_voices_archive_sha256: str | None = None,
) -> None:
    voice_type = dict(dialogue["voiceType"])
    voice_editor_id = str(voice_type["editorId"])
    if not voice_editor_id:
        raise ValueError("Fallout 3 owned Dad voice type has no editor ID")
    namespace = canonical_member_path(
        f"sound\\voice\\fallout3.esm\\{voice_editor_id}"
    )
    prepared = []
    for raw_branch in dialogue["branches"]:
        branch = dict(raw_branch)
        response = dict(branch["response"])
        response_index = int(response["index"])
        info_form_id = str(branch["infoFormId"]).casefold()
        suffix = f"_{info_form_id}_{response_index}.ogg"
        archive_stack = [(voices_archive, voices_archive_sha256)]
        if secondary_voices_archive is not None:
            if secondary_voices_archive_sha256 is None:
                raise ValueError("Fallout 3 secondary voice archive hash is absent")
            archive_stack.append((secondary_voices_archive,
                                  secondary_voices_archive_sha256))
        matches = [(archive, archive_sha256, path)
                   for archive, archive_sha256 in archive_stack
                   for path in archive.members
                   if path.startswith(namespace + "\\") and path.endswith(suffix)]
        if len(matches) != 1:
            raise ValueError(
                "Fallout 3 owned Dad voice is absent or ambiguous: "
                f"info={info_form_id} response={response_index}"
            )
        selected_archive, selected_archive_sha256, voice_path = matches[0]
        lip_path = voice_path.removesuffix(".ogg") + ".lip"
        if lip_path not in selected_archive.members:
            raise ValueError(
                "Fallout 3 owned Dad lip data is absent: "
                f"info={info_form_id} response={response_index}"
            )

        def prepare_asset(logical_path: str) -> dict[str, object]:
            member = selected_archive.extract(logical_path)
            output = profile_root / "generated" / "fallout3" / "dialogue" / Path(
                logical_path.replace("\\", "/")
            )
            if not output.is_file() or file_sha256(output) != member.sha256:
                atomic_bytes(output, member.data)
            return {
                "logicalPath": member.logical_path,
                "source": str(output.resolve()),
                "bytes": len(member.data),
                "sha256": member.sha256,
                "sourceArchive": selected_archive.archive.name,
                "sourceArchiveSha256": selected_archive_sha256,
            }

        response["voice"] = prepare_asset(voice_path)
        response["lip"] = prepare_asset(lip_path)
        branch["response"] = response
        prepared.append(branch)
    dialogue["branches"] = prepared
    dialogue["dialoguePlaybackPrepared"] = True
    dialogue["dialoguePlaybackImplemented"] = True
    dialogue["voiceType"] = {**voice_type, "memberNamespace": namespace}


def _bind_cg00_player_camera_asset(
    sequence: dict[str, object],
    meshes_archive: BsaArchive,
    meshes_archive_sha256: str,
    profile_root: Path,
    animation_samples_per_second: float,
) -> None:
    package_sections = dict(sequence["actorPackageSections"])
    camera = dict(sequence["playerCamera"])
    player_camera_rows = [
        row
        for row in package_sections["player"]
        if int(row["section"]) == int(camera["section"])
    ]
    if len(player_camera_rows) != 1:
        raise ValueError("Fallout 3 early CG00 player camera package is ambiguous")
    player_camera_row = player_camera_rows[0]
    animation_member = meshes_archive.extract(
        str(player_camera_row["animationLogicalPath"])
    )
    skeleton_member = meshes_archive.extract(str(camera["skeletonLogicalPath"]))
    animation_output = (
        profile_root
        / "generated"
        / "fallout3"
        / "camera"
        / Path(animation_member.logical_path.replace("\\", "/"))
    )
    skeleton_output = (
        profile_root
        / "generated"
        / "fallout3"
        / "camera"
        / Path(skeleton_member.logical_path.replace("\\", "/"))
    )
    for output, member in (
        (animation_output, animation_member),
        (skeleton_output, skeleton_member),
    ):
        if not output.is_file() or file_sha256(output) != member.sha256:
            atomic_bytes(output, member.data)
    sampled = sample_transform_animation(
        animation_member.data,
        skeleton_member.data,
        str(camera["targetNode"]),
        animation_samples_per_second,
        include_animated_parent_tracks=True,
    ).manifest()
    if not sampled.get("animatedParentTracks"):
        raise ValueError(
            "Fallout 3 early CG00 player camera has no animated parent transform"
        )
    sample_contract_sha256 = hashlib.sha256(
        json.dumps(sampled, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    camera.update(
        {
            "schema": "opennv-fo3-cg00-player-camera-transform/v1",
            "status": "source-backed-sampled-player-camera-root-transform",
            "packageFormId": player_camera_row["packageFormId"],
            "packageEditorId": player_camera_row["packageEditorId"],
            "idleFormId": player_camera_row["idleFormId"],
            "animation": {
                "logicalPath": animation_member.logical_path,
                "source": str(animation_output.resolve()),
                "bytes": len(animation_member.data),
                "sha256": animation_member.sha256,
                "sourceArchive": meshes_archive.archive.name,
                "sourceArchiveSha256": meshes_archive_sha256,
            },
            "skeleton": {
                "logicalPath": skeleton_member.logical_path,
                "source": str(skeleton_output.resolve()),
                "bytes": len(skeleton_member.data),
                "sha256": skeleton_member.sha256,
                "sourceArchive": meshes_archive.archive.name,
                "sourceArchiveSha256": meshes_archive_sha256,
            },
            "sampleContractSha256": sample_contract_sha256,
            "track": sampled,
        }
    )
    sequence["playerCamera"] = camera


def _bind_cg00_early_birth_assets(
    sequence: dict[str, object],
    meshes_archive: BsaArchive,
    meshes_archive_sha256: str,
    voices_archive: BsaArchive,
    voices_archive_sha256: str,
    sound_archive: BsaArchive,
    sound_archive_sha256: str,
    profile_root: Path,
    animation_samples_per_second: float,
) -> None:
    package_sections = dict(sequence["actorPackageSections"])
    for role, raw_rows in package_sections.items():
        rows = []
        for raw_row in raw_rows:
            row = dict(raw_row)
            member = meshes_archive.extract(str(row["animationLogicalPath"]))
            row["animationSource"] = {
                "sourceArchive": meshes_archive.archive.name,
                "sourceArchiveSha256": meshes_archive_sha256,
                "sourceBytes": len(member.data),
                "sourceSha256": member.sha256,
            }
            row["animationPlayback"] = animation_sequence_manifest(member.data)
            rows.append(row)
        package_sections[str(role)] = rows
    sequence["actorPackageSections"] = package_sections
    _bind_cg00_player_camera_asset(
        sequence,
        meshes_archive,
        meshes_archive_sha256,
        profile_root,
        animation_samples_per_second,
    )

    def prepare_member(
        archive: BsaArchive,
        archive_sha256: str,
        logical_path: str,
        output_group: str,
    ) -> dict[str, object]:
        member = archive.extract(logical_path)
        output = profile_root / "generated" / "fallout3" / output_group / Path(
            member.logical_path.replace("\\", "/")
        )
        if not output.is_file() or file_sha256(output) != member.sha256:
            atomic_bytes(output, member.data)
        return {
            "logicalPath": member.logical_path,
            "source": str(output.resolve()),
            "bytes": len(member.data),
            "sha256": member.sha256,
            "sourceArchive": archive.archive.name,
            "sourceArchiveSha256": archive_sha256,
        }

    dialogue = dict(sequence["dialogue"])
    dialogue_rows = [
        *list(dialogue["stage10"]),
        *list(dict(dialogue["stage22"])["male"]),
        *list(dict(dialogue["stage22"])["female"]),
        *list(dialogue["stage42"]),
    ]
    prepared_by_info: dict[str, dict[str, object]] = {}
    for raw_info in dialogue_rows:
        info = dict(raw_info)
        info_form_id = str(info["infoFormId"]).casefold()
        voice_editor_id = str(dict(info["voiceType"])["editorId"])
        namespace = canonical_member_path(
            f"sound\\voice\\fallout3.esm\\{voice_editor_id}"
        )
        suffix = f"_{info_form_id}_1.ogg"
        matches = [
            path for path in voices_archive.members
            if path.startswith(namespace + "\\") and path.endswith(suffix)
        ]
        if len(matches) != 1:
            raise ValueError("Fallout 3 early CG00 INFO voice is absent or ambiguous")
        lip_path = matches[0].removesuffix(".ogg") + ".lip"
        if lip_path not in voices_archive.members:
            raise ValueError("Fallout 3 early CG00 INFO lip data is absent")
        prepared_by_info[info_form_id] = {
            "voice": prepare_member(
                voices_archive, voices_archive_sha256, matches[0], "dialogue"
            ),
            "lip": prepare_member(
                voices_archive, voices_archive_sha256, lip_path, "dialogue"
            ),
        }

    def bind_info(raw_info: object) -> dict[str, object]:
        info = dict(raw_info)
        info["preparedAudio"] = prepared_by_info[str(info["infoFormId"]).casefold()]
        return info

    dialogue["stage10"] = [bind_info(value) for value in dialogue["stage10"]]
    stage22 = dict(dialogue["stage22"])
    stage22["male"] = [bind_info(value) for value in stage22["male"]]
    stage22["female"] = [bind_info(value) for value in stage22["female"]]
    dialogue["stage22"] = stage22
    dialogue["stage42"] = [bind_info(value) for value in dialogue["stage42"]]
    sequence["dialogue"] = dialogue

    prepared_sounds = []
    for raw_sound in sequence["sounds"]:
        sound = dict(raw_sound)
        logical_path = str(sound["logicalPath"])
        if sound["selectionPolicy"] == "exact-file":
            members = [logical_path]
        else:
            prefix = logical_path.rstrip("\\") + "\\"
            members = sorted(
                path for path in sound_archive.members if path.startswith(prefix)
            )
        if not members:
            raise ValueError("Fallout 3 early CG00 sound source is absent")
        sound["preparedSources"] = [
            prepare_member(sound_archive, sound_archive_sha256, path, "sound")
            for path in members
        ]
        prepared_sounds.append(sound)
    sequence["sounds"] = prepared_sounds
    sequence["assetsPrepared"] = True


def _bind_owned_dad_dialogue_animations(
    dialogue: dict[str, object],
    meshes_archive: BsaArchive,
    meshes_archive_sha256: str,
) -> None:
    prepared = []
    for raw_branch in dialogue["branches"]:
        branch = dict(raw_branch)
        if branch["speakerIdle"] is None:
            prepared.append(branch)
            continue
        speaker_idle = dict(branch["speakerIdle"])
        member = meshes_archive.extract(str(speaker_idle["modelPath"]))
        speaker_idle.update(
            {
                "sourceArchive": meshes_archive.archive.name,
                "sourceArchiveSha256": meshes_archive_sha256,
                "sourceBytes": len(member.data),
                "sourceSha256": member.sha256,
            }
        )
        branch["speakerIdle"] = speaker_idle
        prepared.append(branch)
    dialogue["branches"] = prepared


def _bind_stage90_sound(
    transition: dict[str, object],
    sound_archive: BsaArchive,
    sound_archive_sha256: str,
    profile_root: Path,
) -> None:
    commands = [dict(command) for command in transition["commands"]]
    sound_commands = [command for command in commands if command["kind"] == "playSound"]
    if len(sound_commands) != 1:
        raise ValueError("Fallout 3 stage 90 sound command is ambiguous")
    command = sound_commands[0]
    sound = dict(command["sound"])
    member = sound_archive.extract(str(sound["logicalPath"]))
    output = profile_root / "generated" / "fallout3" / "sound" / Path(
        member.logical_path.replace("\\", "/")
    )
    if not output.is_file() or file_sha256(output) != member.sha256:
        atomic_bytes(output, member.data)
    sound["asset"] = {
        "logicalPath": member.logical_path,
        "source": str(output.resolve()),
        "bytes": len(member.data),
        "sha256": member.sha256,
        "sourceArchive": sound_archive.archive.name,
        "sourceArchiveSha256": sound_archive_sha256,
    }
    command["sound"] = sound
    transition["commands"] = commands


def _bind_cg02_intro_assets(
    intro: dict[str, object],
    configuration: object,
    data_root: Path,
    profile_root: Path,
    meshes_archive: BsaArchive,
    meshes_archive_sha256: str,
    voices_archive: BsaArchive,
    voices_archive_sha256: str,
    sound_archive: BsaArchive,
    sound_archive_sha256: str,
) -> None:
    participants = [dict(row) for row in intro["participants"]]
    animation_paths: dict[str, tuple[str, ...]] = {}
    for row in participants:
        reference_id = str(row["referenceFormId"]).casefold()
        if row["speakerIdle"] is None:
            continue
        animation_paths[reference_id] = tuple(dict.fromkeys((
            *animation_paths.get(reference_id, ()),
            str(dict(row["speakerIdle"])["modelPath"]),
        )))
    if "dadSpeechRuntime" in intro:
        dad_speech = dict(intro["dadSpeechRuntime"])
        dad_reference_id = str(dad_speech["dadReferenceFormId"]).casefold()
        dad_branches = list(dict(dad_speech["dialogue"])["branches"])
        animation_paths[dad_reference_id] = tuple(dict.fromkeys((
            *animation_paths.get(dad_reference_id, ()),
            *(str(dict(branch)["speakerIdle"]["modelPath"])
              for branch in dad_branches),
        )))
        if "overseerSpeechRuntime" in dad_speech:
            overseer = dict(dad_speech["overseerSpeechRuntime"])
            overseer_reference_id = str(overseer["overseerReferenceFormId"]).casefold()
            overseer_branches = list(dict(overseer["dialogue"])["branches"])
            animation_paths[overseer_reference_id] = tuple(dict.fromkeys(
                str(dict(branch)["speakerIdle"]["modelPath"])
                for branch in overseer_branches
                if dict(branch)["speakerIdle"] is not None
            ))
            if "dadPartyRuntime" in overseer:
                party = dict(overseer["dadPartyRuntime"])
                party_branches = list(dict(party["dialogue"])["branches"])
                animation_paths[dad_reference_id] = tuple(dict.fromkeys((
                    *animation_paths.get(dad_reference_id, ()),
                    *(str(dict(branch)["speakerIdle"]["modelPath"])
                      for branch in party_branches),
                )))
    actor_recipe_ids = list(dict.fromkeys(
        str(row["actorRecipeId"]) for row in participants
    ))
    if "dadSpeechRuntime" in intro:
        dad_speech = dict(intro["dadSpeechRuntime"])
        if "overseerSpeechRuntime" in dad_speech:
            actor_recipe_ids.append(str(dict(
                dad_speech["overseerSpeechRuntime"])["actorRecipeId"]))
            party = dict(dict(dad_speech["overseerSpeechRuntime"]).get(
                "dadPartyRuntime", {}))
            birthday = dict(party.get("birthdayInteractionsRuntime", {}))
            for participant in birthday.get("participants", []):
                actor_recipe_id = dict(participant).get("actorRecipeId")
                if actor_recipe_id is not None:
                    actor_recipe_ids.append(str(actor_recipe_id))
            cake = dict(birthday.get("cakeRuntime", {}))
            if cake:
                andy = dict(cake["andy"])
                actor_recipe_ids.append(str(andy["actorRecipeId"]))
                animation_paths[str(andy["referenceFormId"]).casefold()] = (
                    str(dict(dict(cake["package"])["locomotion"])["logicalPath"]),
                    str(dict(cake["package"])["idle"]["modelPath"]),
                )
            butch = dict(birthday.get("butchRuntime", {}))
            post_intercom = dict(butch.get("postIntercomRuntime", {}))
            if post_intercom:
                actor_recipe_ids.append(str(post_intercom["jonasActorRecipeId"]))
    actor_set = actor_preparation.prepare_actor_set(
        data_root,
        profile_root,
        actor_recipe_ids,
        animation_paths,
    )
    actor_set_document = json.loads(
        Path(str(actor_set["manifest"])).read_text(encoding="utf-8")
    )
    scenes = {
        str(row["referenceFormId"]).casefold(): dict(row)
        for row in actor_set_document["actors"]
    }

    def prepare_member(
        archive: BsaArchive,
        archive_sha256: str,
        logical_path: str,
        category: str,
    ) -> dict[str, object]:
        member = archive.extract(logical_path)
        output = profile_root / "generated" / "fallout3" / category / Path(
            member.logical_path.replace("\\", "/")
        )
        if not output.is_file() or file_sha256(output) != member.sha256:
            atomic_bytes(output, member.data)
        return {
            "logicalPath": member.logical_path,
            "source": str(output.resolve()),
            "bytes": len(member.data),
            "sha256": member.sha256,
            "sourceArchive": archive.archive.name,
            "sourceArchiveSha256": archive_sha256,
        }

    prepared = []
    for row in participants:
        reference_id = str(row["referenceFormId"]).casefold()
        actor_scene = scenes.get(reference_id)
        if actor_scene is None:
            raise ValueError(f"Fallout 3 CG02 actor scene is absent: {reference_id}")
        row["actorScene"] = actor_scene
        if row["speakerIdle"] is not None:
            idle = dict(row["speakerIdle"])
            idle_member = meshes_archive.extract(str(idle["modelPath"]))
            idle.update({
                "sourceBytes": len(idle_member.data),
                "sourceSha256": idle_member.sha256,
                "sourceArchive": meshes_archive.archive.name,
                "sourceArchiveSha256": meshes_archive_sha256,
            })
            row["speakerIdle"] = idle
        response = dict(row["response"])
        namespace = canonical_member_path(
            f"sound\\voice\\fallout3.esm\\{dict(row['voiceType'])['editorId']}"
        )
        suffix = f"_{str(row['infoFormId']).casefold()}_{int(response['index'])}.ogg"
        matches = [
            path for path in voices_archive.members
            if path.startswith(namespace + "\\") and path.endswith(suffix)
        ]
        if len(matches) != 1:
            raise ValueError(f"Fallout 3 CG02 intro voice differs: {reference_id}")
        voice_path = matches[0]
        lip_path = voice_path.removesuffix(".ogg") + ".lip"
        if lip_path not in voices_archive.members:
            raise ValueError(f"Fallout 3 CG02 intro lip differs: {reference_id}")
        response["voice"] = prepare_member(
            voices_archive, voices_archive_sha256, voice_path, "dialogue"
        )
        response["lip"] = prepare_member(
            voices_archive, voices_archive_sha256, lip_path, "dialogue"
        )
        row["response"] = response
        prepared.append(row)
    intro["participants"] = prepared
    if "dadSpeechRuntime" in intro:
        dad_speech = dict(intro["dadSpeechRuntime"])
        if "overseerSpeechRuntime" in dad_speech:
            overseer = dict(dad_speech["overseerSpeechRuntime"])
            overseer_reference_id = str(overseer["overseerReferenceFormId"]).casefold()
            actor_scene = scenes.get(overseer_reference_id)
            if actor_scene is None:
                raise ValueError("Fallout 3 CG02 Overseer actor scene is absent")
            overseer["actorScene"] = actor_scene
            party = dict(overseer.get("dadPartyRuntime", {}))
            birthday = dict(party.get("birthdayInteractionsRuntime", {}))
            birthday_participants = []
            for raw_participant in birthday.get("participants", []):
                participant = dict(raw_participant)
                if "actorRecipeId" in participant:
                    actor_scene = scenes.get(
                        str(participant["referenceFormId"]).casefold())
                    if actor_scene is None:
                        raise ValueError(
                            "Fallout 3 CG02 birthday actor scene is absent")
                    participant["actorScene"] = actor_scene
                birthday_participants.append(participant)
            birthday["participants"] = birthday_participants
            cake = dict(birthday.get("cakeRuntime", {}))
            if cake:
                andy = dict(cake["andy"])
                andy_scene = scenes.get(str(andy["referenceFormId"]).casefold())
                if andy_scene is None:
                    raise ValueError("Fallout 3 CG02 Andy actor scene is absent")
                andy["actorScene"] = andy_scene
                cake["andy"] = andy
                package = dict(cake["package"])
                locomotion = dict(package["locomotion"])
                locomotion_member = meshes_archive.extract(
                    str(locomotion["logicalPath"]))
                locomotion["sha256"] = locomotion_member.sha256
                locomotion["rootMotion"] = sample_root_motion(
                    locomotion_member.data,
                    str(locomotion["rootNode"]),
                    configuration.content_compiler.animation_samples_per_second,
                ).manifest()
                package["locomotion"] = locomotion
                cake["package"] = package
                birthday["cakeRuntime"] = cake
            butch = dict(birthday.get("butchRuntime", {}))
            post_intercom = dict(butch.get("postIntercomRuntime", {}))
            if post_intercom:
                jonas_scene = scenes.get(
                    str(post_intercom["jonasReferenceFormId"]).casefold())
                if jonas_scene is None:
                    raise ValueError("Fallout 3 CG02 Jonas actor scene is absent")
                post_intercom["jonasActorScene"] = jonas_scene
                reactor_gift = dict(post_intercom.get("reactorGiftRuntime", {}))
                if reactor_gift:
                    prepared_reactor_participants = []
                    for raw_participant in reactor_gift["participants"]:
                        participant = dict(raw_participant)
                        if str(participant["referenceFormId"]).casefold() == str(
                                post_intercom["jonasReferenceFormId"]).casefold():
                            participant["actorScene"] = jonas_scene
                        prepared_reactor_participants.append(participant)
                    reactor_gift["participants"] = prepared_reactor_participants
                    picture = dict(reactor_gift.get("pictureRuntime", {}))
                    completion_runtime = dict(picture.get("completionRuntime", {}))
                    if completion_runtime:
                        completion_sound = dict(completion_runtime["sound"])
                        completion_sound["asset"] = prepare_member(
                            sound_archive,
                            sound_archive_sha256,
                            str(completion_sound["logicalPath"]),
                            "sound",
                        )
                        completion_runtime["sound"] = completion_sound
                        picture["completionRuntime"] = completion_runtime
                        reactor_gift["pictureRuntime"] = picture
                    post_intercom["reactorGiftRuntime"] = reactor_gift
                butch["postIntercomRuntime"] = post_intercom
                birthday["butchRuntime"] = butch
            party["birthdayInteractionsRuntime"] = birthday
            overseer["dadPartyRuntime"] = party
            dad_speech["overseerSpeechRuntime"] = overseer
            intro["dadSpeechRuntime"] = dad_speech
    intro["sounds"] = [
        {
            **dict(row),
            "asset": prepare_member(
                sound_archive,
                sound_archive_sha256,
                str(dict(row)["logicalPath"]),
                "sound",
            ),
        }
        for row in intro["sounds"]
    ]
    intro["assetsPrepared"] = True


def _bind_cg01_transition_video(
    character_selection: dict[str, object],
    transition_video: dict[str, object],
) -> None:
    transition = dict(character_selection["cg01Stage0Transition"])
    if transition.get("schema") != "opennv-fo3-cg01-stage-0-to-5-transition/v1":
        raise ValueError("Fallout 3 CG01 transition contract is unsupported")
    stage0 = dict(transition["stage0Result"])
    stage0_commands = [dict(command) for command in stage0["commands"]]
    if (
        len(stage0_commands) != 4
        or stage0_commands[1].get("index") != 1
        or stage0_commands[1].get("kind") != "setStage"
    ):
        raise ValueError("Fallout 3 CG01 nested stage command is absent")
    nested = dict(stage0_commands[1]["stageResult"])
    if nested.get("schema") != "opennv-fo3-cg01-stage-5-result/v1":
        raise ValueError("Fallout 3 CG01 nested stage result is unsupported")
    nested_commands = [dict(command) for command in nested["commands"]]
    movie_commands = [
        command for command in nested_commands if command.get("kind") == "playBink"
    ]
    if len(movie_commands) != 1:
        raise ValueError("Fallout 3 CG01 transition movie command is ambiguous")
    movie = movie_commands[0]
    if movie.get("index") != 12:
        raise ValueError("Fallout 3 CG01 transition movie command order differs")

    runtime = transition_video.get("runtime")
    if not isinstance(runtime, dict):
        raise ValueError("Fallout 3 CG01 runtime transition movie is absent")
    runtime_inputs = runtime.get("inputs")
    if not isinstance(runtime_inputs, dict):
        raise ValueError("Fallout 3 CG01 runtime transition movie inputs are absent")
    if (
        str(movie["logicalPath"]).casefold() != str(transition_video["file"]).casefold()
        or runtime.get("schema") != "opennv-owned-opening-video/v1"
        or runtime.get("status") != "deterministic-owned-video-transcode"
        or runtime_inputs.get("source") != transition_video.get("source")
        or runtime_inputs.get("sourceSha256") != transition_video.get("sha256")
    ):
        raise ValueError("Fallout 3 CG01 runtime transition movie identity differs")

    movie["video"] = transition_video
    nested["commands"] = nested_commands
    stage0_commands[1]["stageResult"] = nested
    stage0["commands"] = stage0_commands
    transition["stage0Result"] = stage0
    transition_sha256 = hashlib.sha256(
        json.dumps(transition, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()

    stage100 = dict(character_selection["stage100Transition"])
    stage100_commands = [dict(command) for command in stage100["commands"]]
    if (
        len(stage100_commands) != 8
        or stage100_commands[7].get("index") != 7
        or stage100_commands[7].get("kind") != "setStage"
    ):
        raise ValueError("Fallout 3 CG00 stage-100 transition trigger differs")
    transition_identity = {
        "schema": transition["schema"],
        "sha256": transition_sha256,
    }
    stage100_commands[7]["stageResultContract"] = transition_identity
    stage100["commands"] = stage100_commands
    next_boundary = dict(stage100["nextBoundary"])
    next_boundary["transitionContract"] = transition_identity
    stage100["nextBoundary"] = next_boundary
    character_selection["stage100Transition"] = stage100
    character_selection["cg01Stage0Transition"] = transition


def _bind_cg01_toddler_world(
    character_selection: dict[str, object],
    definition: dict[str, object],
    default_ini: Path,
    configuration: object,
) -> None:
    transition = dict(character_selection["cg01Stage0Transition"])
    stage0 = dict(transition["stage0Result"])
    stage0_commands = [dict(command) for command in stage0["commands"]]
    player_scale_commands = [
        command for command in stage0_commands if command.get("kind") == "setPlayerScale"
    ]
    player_move_commands = [
        command
        for command in stage0_commands
        if command.get("kind") == "moveToReference"
        and dict(command.get("subject", {})).get("role") == "player"
    ]
    if len(player_scale_commands) != 1 or len(player_move_commands) != 1:
        raise ValueError("Fallout 3 CG01 toddler player source state is ambiguous")
    player_scale = float(player_scale_commands[0]["value"])
    if not math.isfinite(player_scale) or player_scale <= 0.0:
        raise ValueError("Fallout 3 CG01 toddler player scale is invalid")
    player_marker = dict(player_move_commands[0]["target"])

    post_stage5 = dict(transition["postStage5Transition"])
    post_stage10 = dict(post_stage5["postStage10TriggerTransition"])
    trigger = dict(post_stage10["trigger"])
    if trigger.get("cellFormId") != transition.get("cellFormId"):
        raise ValueError("Fallout 3 CG01 toddler trigger CELL differs")

    world_definition = dict(definition["toddlerWorld"])
    camera_rows = _ini_settings(
        default_ini,
        [dict(row) for row in world_definition["cameraIniSettings"]],
    )
    camera_by_key = {str(row["key"]): row for row in camera_rows}
    if set(camera_by_key) != {"fDefaultFOV", "fNearDistance"}:
        raise ValueError("Fallout 3 CG01 toddler camera settings differ")
    default_horizontal_fov = float(camera_by_key["fDefaultFOV"]["value"])
    near_game_units = float(camera_by_key["fNearDistance"]["value"])
    if (
        not math.isfinite(default_horizontal_fov)
        or not 0.0 < default_horizontal_fov < PERSPECTIVE_MAXIMUM_DEGREES
        or not math.isfinite(near_game_units)
        or near_game_units <= 0.0
    ):
        raise ValueError("Fallout 3 CG01 toddler camera values are invalid")

    runtime_document = dict(configuration.document)
    player_policy = dict(runtime_document["player"])
    simulation_policy = dict(runtime_document["simulation"])
    expected_policy = "open-nv-player-policy-scaled-by-owned-player-scale"
    if str(world_definition["physicsPolicy"]) != expected_policy:
        raise ValueError("Fallout 3 CG01 toddler physics policy differs")
    transition["toddlerWorld"] = {
        "schema": "opennv-fo3-cg01-toddler-world/v1",
        "status": "source-marker-camera-and-open-nv-physics-policy-runtime-ready",
        "cellFormId": str(transition["cellFormId"]),
        "player": {
            "role": "player",
            "scale": player_scale,
            "startMarker": player_marker,
            "visualBodyPrepared": False,
        },
        "camera": {
            **_fallout_default_fov_projection(default_horizontal_fov),
            "nearGameUnits": near_game_units,
            "settings": camera_rows,
        },
        "physicsPolicy": {
            "authority": expected_policy,
            "runtimeConfiguration": configuration.manifest(),
            "spawnCenterHeightMeters": float(player_policy["spawnCenterHeightMeters"]),
            "capsuleRadiusMeters": float(player_policy["capsuleRadiusMeters"]),
            "capsuleHeightMeters": float(player_policy["capsuleHeightMeters"]),
            "moveSpeedMetersPerSecond": float(player_policy["moveSpeedMetersPerSecond"]),
            "mouseSensitivityRadiansPerPixel": float(
                player_policy["mouseSensitivityRadiansPerPixel"]
            ),
            "verticalLookLimitRadians": float(player_policy["verticalLookLimitRadians"]),
            "desktopCameraOffsetMeters": list(player_policy["desktopCameraOffsetMeters"]),
            "cameraFarMeters": float(player_policy["cameraFarMeters"]),
            "collisionLayer": int(player_policy["collisionLayer"]),
            "collisionMask": int(player_policy["collisionMask"]),
            "gravityMetersPerSecondSquared": float(
                simulation_policy["gravityMetersPerSecondSquared"]
            ),
            "desktopInput": {
                key: dict(dict(player_policy["desktopInput"])[key])
                for key in (
                    "moveLeft",
                    "moveRight",
                    "moveForward",
                    "moveBackward",
                )
            },
        },
        "triggerReferenceFormId": str(trigger["referenceFormId"]),
        "targetStage": int(post_stage10["targetStage"]),
        "runtimeReady": True,
        "blocker": None,
    }
    character_selection["cg01Stage0Transition"] = transition


def _compile_cg00_early_birth_sequence(
    records: tuple[object, ...],
    selection: dict[str, object],
    quest_form_id: int,
    quest_script: object,
    quest_script_source: str,
    stage_sources: dict[int, list[str]],
) -> dict[str, object]:
    """Compile the exact owned CG00 stage-0 through RaceSex-menu source closure."""
    definition = dict(selection["earlyBirthSequence"])
    expected_stages = [int(value) for value in definition["stages"]]
    if len(expected_stages) != len(set(expected_stages)) or any(
        len(stage_sources.get(stage, [])) != 1 for stage in expected_stages
    ):
        raise ValueError("Fallout 3 early CG00 stage source closure is incomplete")

    timer_match = CG00_TIMER_CHAIN_PATTERN.search(quest_script_source)
    if timer_match is None:
        raise ValueError("Fallout 3 early CG00 timer chain is absent")
    observed_timer_transitions = [
        {"sourceStage": int(match.group("source")), "targetStage": int(match.group("target"))}
        for match in CG00_TIMER_STAGE_PATTERN.finditer(timer_match.group("stage_branches"))
        if int(match.group("source")) <= int(selection["appearanceMenuEnteredStage"])
    ]
    expected_timer_transitions = [dict(row) for row in definition["timerTransitions"]]
    if observed_timer_transitions != expected_timer_transitions:
        raise ValueError("Fallout 3 early CG00 timer transitions differ")

    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    def exact_record(raw_form_id: object, editor_id: object, signature: str) -> object:
        form_id = int(str(raw_form_id), FORM_ID_RADIX)
        record = by_form.get(form_id)
        if (
            record is None
            or record.signature != signature
            or (_editor_id(record) or "").casefold() != str(editor_id).casefold()
        ):
            raise ValueError(
                f"Fallout 3 early CG00 {signature} identity differs: {editor_id}"
            )
        return record

    stage_rows = []
    for stage in expected_stages:
        source = stage_sources[stage][0]
        commands = _source_commands(source)
        stage_rows.append(
            {
                "stage": stage,
                "sourceSha256": hashlib.sha256(source.encode("cp1252")).hexdigest(),
                "commandCount": len(commands),
                "commands": commands,
            }
        )

    stage0_commands = [value.casefold() for value in stage_rows[0]["commands"]]
    participants = []
    for raw_participant in definition["sceneParticipants"]:
        participant = dict(raw_participant)
        reference = exact_record(
            participant["referenceFormId"],
            participant["referenceEditorId"],
            ACTOR_REFERENCE_RECORD,
        )
        marker = exact_record(
            participant["startMarkerFormId"],
            participant["startMarkerEditorId"],
            PLACED_REFERENCE_RECORD,
        )
        expected_move = (
            f"{participant['referenceEditorId']}.moveto "
            f"{participant['startMarkerEditorId']}"
        ).casefold()
        if expected_move not in stage0_commands:
            raise ValueError("Fallout 3 early CG00 participant MoveTo command is absent")
        participants.append(
            {
                "role": str(participant["role"]),
                "reference": {
                    "formId": _form_id(reference.form_id),
                    "editorId": _editor_id(reference),
                    "recordSha256": hashlib.sha256(reference.data).hexdigest(),
                    "authoredTransform": _reference_transform_contract(reference),
                },
                "startMarker": {
                    "formId": _form_id(marker.form_id),
                    "editorId": _editor_id(marker),
                    "recordSha256": hashlib.sha256(marker.data).hexdigest(),
                    "authoredTransform": _reference_transform_contract(marker),
                },
            }
        )
    if {row["role"] for row in participants} != {"father", "doctor", "mother"}:
        raise ValueError("Fallout 3 early CG00 scene participant roles differ")

    player_marker = exact_record(
        definition["playerStartMarkerFormId"],
        definition["playerStartMarkerEditorId"],
        PLACED_REFERENCE_RECORD,
    )
    player_move = f"player.moveto {definition['playerStartMarkerEditorId']}".casefold()
    if player_move not in stage0_commands:
        raise ValueError("Fallout 3 early CG00 player MoveTo command is absent")

    package_roles = {}
    for role, raw_rows in dict(definition["actorPackages"]).items():
        rows = []
        for raw_row in raw_rows:
            row = dict(raw_row)
            package = by_form.get(int(str(row["formId"]), FORM_ID_RADIX))
            idle_form_id = int(str(row["idleFormId"]), FORM_ID_RADIX)
            idle = by_form.get(idle_form_id)
            if package is None or package.signature != PACKAGE_RECORD:
                raise ValueError("Fallout 3 early CG00 actor package is absent")
            if idle is None or idle.signature != IDLE_RECORD:
                raise ValueError("Fallout 3 early CG00 actor package IDLE is absent")
            package_idle_ids = _form_id_list(package, "IDLA")
            if idle_form_id not in package_idle_ids:
                raise ValueError("Fallout 3 early CG00 package-to-IDLE join differs")
            package_playback = _cg00_package_playback_contract(package, by_form)
            models = _text_values(idle, "MODL")
            if len(models) != 1 or not models[0].casefold().endswith(".kf"):
                raise ValueError("Fallout 3 early CG00 package animation is absent")
            rows.append(
                {
                    "section": int(row["section"]),
                    "packageFormId": _form_id(package.form_id),
                    "packageEditorId": _editor_id(package),
                    "packageRecordSha256": hashlib.sha256(package.data).hexdigest(),
                    "idleFormId": _form_id(idle.form_id),
                    "idleEditorId": _editor_id(idle),
                    "idleRecordSha256": hashlib.sha256(idle.data).hexdigest(),
                    "activationCondition": (
                        None
                        if str(role) == "player"
                        else _cg00_package_stage_condition(package, quest_form_id)
                    ),
                    **package_playback,
                    "animationLogicalPath": canonical_member_path(f"meshes\\{models[0]}"),
                }
            )
        sections = [int(row["section"]) for row in rows]
        if sections != list(range(len(sections))):
            raise ValueError("Fallout 3 early CG00 package sections are not contiguous")
        if any(
            row["events"]["change"] != rows[index + 1]["idleFormId"]
            for index, row in enumerate(rows[:-1])
        ):
            raise ValueError("Fallout 3 early CG00 package change-idle chain differs")
        package_roles[str(role)] = rows

    player_camera_definition = dict(definition["playerCamera"])
    player_camera_section = int(player_camera_definition["section"])
    player_camera_packages = [
        row
        for row in package_roles["player"]
        if int(row["section"]) == player_camera_section
    ]
    player_camera_target = str(player_camera_definition["targetNode"])
    if len(player_camera_packages) != 1 or not player_camera_target:
        raise ValueError("Fallout 3 early CG00 player camera owner differs")
    player_camera = {
        "section": player_camera_section,
        "skeletonLogicalPath": canonical_member_path(
            str(player_camera_definition["skeletonLogicalPath"])
        ),
        "targetNode": player_camera_target,
        "playerStartMarkerFormId": _form_id(player_marker.form_id),
        "playerStartMarkerRotationGodotQuaternion": godot_rotation_quaternion(
            tuple(
                float(value)
                for value in _reference_transform_contract(player_marker)["rotationRadians"]
            )
        ),
    }

    effect_rows = []
    for raw_effect in definition["imageSpaceModifiers"]:
        effect = dict(raw_effect)
        record = exact_record(effect["formId"], effect["editorId"], IMAGE_SPACE_MODIFIER_RECORD)
        effect_rows.append(
            {
                "formId": _form_id(record.form_id),
                "editorId": _editor_id(record),
                "recordSha256": hashlib.sha256(record.data).hexdigest(),
                "parameters": parse_image_space_modifier(record).manifest(),
            }
        )

    sound_rows = []
    for raw_sound in definition["sounds"]:
        sound = dict(raw_sound)
        record = exact_record(sound["formId"], sound["editorId"], SOUND_RECORD)
        paths = _text_values(record, "FNAM")
        sound_data = [
            subrecord.data for subrecord in iter_subrecords(record)
            if subrecord.signature in {"SNDD", "SNDX"}
        ]
        if len(paths) != 1 or len(sound_data) != 1:
            raise ValueError("Fallout 3 early CG00 sound layout differs")
        logical_path = canonical_member_path(f"sound\\{paths[0]}")
        sound_rows.append(
            {
                "formId": _form_id(record.form_id),
                "editorId": _editor_id(record),
                "recordSha256": hashlib.sha256(record.data).hexdigest(),
                "soundDataSha256": hashlib.sha256(sound_data[0]).hexdigest(),
                "logicalPath": logical_path,
                "selectionPolicy": (
                    "exact-file" if Path(logical_path).suffix else "source-folder-variant-set"
                ),
            }
        )

    dialogue_definition = dict(definition["dialogue"])
    dad_topic = exact_record(
        dialogue_definition["dadTopicFormId"],
        dialogue_definition["dadTopicEditorId"],
        DIALOGUE_TOPIC_RECORD,
    )
    mom_topic = exact_record(
        dialogue_definition["momTopicFormId"],
        dialogue_definition["momTopicEditorId"],
        DIALOGUE_TOPIC_RECORD,
    )
    if any(
        struct.unpack("<I", _single_subrecord(topic, "QSTI"))[0] != quest_form_id
        for topic in (dad_topic, mom_topic)
    ):
        raise ValueError("Fallout 3 early CG00 dialogue quest ownership differs")

    info_roles = {
        dad_topic.form_id: "father",
        mom_topic.form_id: "mother",
    }

    def compile_info(raw_form_id: object) -> dict[str, object]:
        info = by_form.get(int(str(raw_form_id), FORM_ID_RADIX))
        if info is None or info.signature != DIALOGUE_INFO_RECORD:
            raise ValueError("Fallout 3 early CG00 INFO is absent")
        owner_forms = {
            int(group.label_u32)
            for group in info.groups
            if group.group_type == DIALOGUE_CHILD_GROUP_TYPE
        }
        owners = owner_forms & set(info_roles)
        if len(owners) != 1 or struct.unpack("<I", _single_subrecord(info, "QSTI"))[0] != quest_form_id:
            raise ValueError("Fallout 3 early CG00 INFO ownership differs")
        responses = [text for text in _text_values(info, "NAM1") if text]
        data = _single_subrecord(info, "DATA")
        if len(data) != DIALOGUE_INFO_DATA_BYTES:
            raise ValueError("Fallout 3 early CG00 INFO response layout differs")
        response_type, unused, flags = struct.unpack("<BBH", data)
        if (
            len(responses) != 1
            or response_type != 1
            or unused != 0
            or flags != DIALOGUE_INFO_SAY_ONCE_FLAG
        ):
            raise ValueError("Fallout 3 early CG00 INFO response contract differs")
        result_sources = _text_values(info, "SCTX")
        result_source = "\n".join(result_sources)
        conditions = [
            _dialogue_condition(subrecord.data)
            for subrecord in iter_subrecords(info)
            if subrecord.signature == "CTDA"
        ]
        voice_conditions = [
            row for row in conditions if int(row["function"]) == GET_IS_VOICE_TYPE_FUNCTION
        ]
        if len(voice_conditions) != 1:
            raise ValueError("Fallout 3 early CG00 INFO voice ownership differs")
        voice = by_form.get(int(voice_conditions[0]["parameter1"]))
        if voice is None or voice.signature != VOICE_TYPE_RECORD:
            raise ValueError("Fallout 3 early CG00 INFO voice type is absent")
        return {
            "infoFormId": _form_id(info.form_id),
            "speakerRole": info_roles[owners.pop()],
            "recordSha256": hashlib.sha256(info.data).hexdigest(),
            "sayOnce": True,
            "continuation": sum(
                1 for subrecord in iter_subrecords(info) if subrecord.signature == "NEXT"
            ) == 1,
            "voiceType": {
                "formId": _form_id(voice.form_id),
                "editorId": _editor_id(voice),
                "recordSha256": hashlib.sha256(voice.data).hexdigest(),
            },
            "response": {
                "index": 1,
                "text": responses[0],
                "textSha256": hashlib.sha256(responses[0].encode("utf-8")).hexdigest(),
            },
            "resultSourceSha256": hashlib.sha256(result_source.encode("cp1252")).hexdigest(),
            "resultCommands": _source_commands(result_source),
            "conditions": conditions,
        }

    dialogue = {
        "stage10": [compile_info(value) for value in dialogue_definition["stage10InfoFormIds"]],
        "stage22": {
            "male": [compile_info(value) for value in dialogue_definition["stage22MaleInfoFormIds"]],
            "female": [compile_info(value) for value in dialogue_definition["stage22FemaleInfoFormIds"]],
        },
        "stage42": [compile_info(value) for value in dialogue_definition["stage42InfoFormIds"]],
    }
    gene_projector = exact_record(
        definition["geneProjectorReferenceFormId"],
        definition["geneProjectorReferenceEditorId"],
        PLACED_REFERENCE_RECORD,
    )
    return {
        "schema": "opennv-fo3-cg00-early-birth-sequence/v1",
        "status": "source-backed-complete-contract-runtime-pending",
        "questFormId": _form_id(quest_form_id),
        "questScript": {
            "formId": _form_id(quest_script.form_id),
            "editorId": _editor_id(quest_script),
            "recordSha256": hashlib.sha256(quest_script.data).hexdigest(),
            "sourceSha256": hashlib.sha256(quest_script_source.encode("cp1252")).hexdigest(),
        },
        "stages": stage_rows,
        "timerTransitions": observed_timer_transitions,
        "sceneParticipants": participants,
        "playerStartMarker": {
            "formId": _form_id(player_marker.form_id),
            "editorId": _editor_id(player_marker),
            "recordSha256": hashlib.sha256(player_marker.data).hexdigest(),
            "authoredTransform": _reference_transform_contract(player_marker),
        },
        "actorPackageSections": package_roles,
        "playerCamera": player_camera,
        "imageSpaceModifiers": effect_rows,
        "sounds": sound_rows,
        "dialogue": dialogue,
        "geneProjectorReference": {
            "formId": _form_id(gene_projector.form_id),
            "editorId": _editor_id(gene_projector),
            "recordSha256": hashlib.sha256(gene_projector.data).hexdigest(),
        },
        "sourceClosure": {
            "accounted": [
                "quest-stage-results",
                "timer-stage-transitions",
                "scene-participants",
                "actor-package-idle-animation-joins",
                "image-space-modifiers",
                "opening-sounds",
                "dad-and-mom-dialogue",
                "name-and-race-sex-menu-commands",
            ],
            "unaccounted": [],
            "unaccountedCount": 0,
        },
        "runtimeReady": False,
        "nextBoundary": "fo3-cg00-early-sequence-runtime-executor-not-yet-bound",
    }


def _quest_inventory(master: Path, opening: dict[str, object]) -> tuple[list[dict[str, object]], dict[str, object]]:
    records = tuple(
        iter_plugin_records(
            master,
            frozenset(
                {
                    QUEST_RECORD,
                    SCRIPT_RECORD,
                    MESSAGE_RECORD,
                    PLUGIN_HEADER_RECORD,
                    PACKAGE_RECORD,
                    IDLE_RECORD,
                    DIALOGUE_TOPIC_RECORD,
                    DIALOGUE_INFO_RECORD,
                    VOICE_TYPE_RECORD,
                    IMAGE_SPACE_MODIFIER_RECORD,
                    SOUND_RECORD,
                    ACTOR_REFERENCE_RECORD,
                    CREATURE_REFERENCE_RECORD,
                    "CREA",
                    PLACED_REFERENCE_RECORD,
                    ACTIVATOR_RECORD,
                    DOOR_RECORD,
                    ACTOR_VALUE_RECORD,
                    NPC_RECORD,
                    ACTOR_BASE_RECORD,
                    STATIC_RECORD,
                    GLOBAL_RECORD,
                    "ALCH",
                    "AMMO",
                    "ARMO",
                    "BOOK",
                    "NOTE",
                    "WEAP",
                    "NAVM",
                }
            ),
        )
    )
    by_form = {record.form_id: record for record in records}
    by_editor: dict[str, list[object]] = {}
    for record in records:
        editor_id = _editor_id(record)
        if editor_id is not None:
            by_editor.setdefault(editor_id.casefold(), []).append(record)

    quest_rows = []
    scripts_by_quest: dict[str, tuple[object, str, dict[int, list[str]]]] = {}
    for expected in opening["quests"]:
        definition = dict(expected)
        editor_id = str(definition["editorId"])
        matches = [
            record
            for record in by_editor.get(editor_id.casefold(), [])
            if record.signature == QUEST_RECORD
        ]
        if len(matches) != 1:
            raise ValueError(f"Fallout 3 opening quest does not resolve uniquely: {editor_id}")
        quest = matches[0]
        expected_form_id = str(definition["formId"]).casefold()
        if _form_id(quest.form_id) != expected_form_id:
            raise ValueError(
                f"Fallout 3 opening quest FormID differs: {editor_id} "
                f"expected={expected_form_id} actual={_form_id(quest.form_id)}"
            )
        subrecords = tuple(iter_subrecords(quest))
        script_links = [
            struct.unpack_from("<I", value.data)[0]
            for value in subrecords
            if value.signature == "SCRI" and len(value.data) >= FORM_ID_BYTES
        ]
        if len(script_links) != 1 or script_links[0] not in by_form:
            raise ValueError(f"Fallout 3 opening quest script is ambiguous: {editor_id}")
        quest_script = by_form[script_links[0]]
        if quest_script.signature != SCRIPT_RECORD:
            raise ValueError(f"Fallout 3 opening quest script has the wrong record type: {editor_id}")
        quest_script_source = _script_source(quest_script)

        stage_sources: dict[int, list[str]] = {}
        stage = None
        for subrecord in subrecords:
            if subrecord.signature == "INDX":
                if len(subrecord.data) not in STAGE_INDEX_BYTES:
                    raise ValueError(f"Fallout 3 opening stage index has an unexpected size: {editor_id}")
                stage = int.from_bytes(subrecord.data, "little")
            elif subrecord.signature == "SCTX" and stage is not None:
                stage_sources.setdefault(stage, []).append(zstring(subrecord.data))

        all_stage_source = "\n".join(
            source
            for stage_number in sorted(stage_sources)
            for source in stage_sources[stage_number]
        )
        next_quest = definition.get("nextQuest")
        if next_quest is not None and re.search(
            rf"\bsetstage\s+{re.escape(str(next_quest))}\s+0\b",
            all_stage_source,
            re.IGNORECASE,
        ) is None:
            raise ValueError(f"Fallout 3 opening quest transition is absent: {editor_id}->{next_quest}")
        transition_video = definition.get("transitionVideo")
        authored_videos = sorted(
            {match.group("path") for match in PLAY_BINK_PATTERN.finditer(all_stage_source)},
            key=str.casefold,
        )
        if transition_video is not None and not any(
            value.casefold() == str(transition_video).casefold()
            for value in authored_videos
        ):
            raise ValueError(f"Fallout 3 opening transition movie is absent: {editor_id}")

        title = _text_values(quest, "FULL")
        script_source_bytes = quest_script_source.encode("cp1252")
        stage_source_bytes = all_stage_source.encode("cp1252")
        quest_rows.append(
            {
                "editorId": editor_id,
                "formId": _form_id(quest.form_id),
                "title": title[0] if len(title) == 1 else "",
                "script": {
                    "editorId": _editor_id(quest_script),
                    "formId": _form_id(quest_script.form_id),
                    "sourceSha256": hashlib.sha256(script_source_bytes).hexdigest(),
                },
                "stages": sorted(stage_sources),
                "stageSourceSha256": hashlib.sha256(stage_source_bytes).hexdigest(),
                "nextQuest": next_quest,
                "transitionVideos": authored_videos,
            }
        )
        scripts_by_quest[editor_id.casefold()] = (
            quest_script,
            quest_script_source,
            stage_sources,
        )

    selection = dict(opening["characterSelection"])
    selection_quest = str(selection["questEditorId"])
    quest_script, quest_script_source, stage_sources = scripts_by_quest[
        selection_quest.casefold()
    ]
    sex_message_id = str(selection["sexMessageEditorId"])
    sex_messages = [
        record
        for record in by_editor.get(sex_message_id.casefold(), [])
        if record.signature == MESSAGE_RECORD
    ]
    if len(sex_messages) != 1 or re.search(
        rf"\bShowMessage\s+{re.escape(sex_message_id)}\b",
        quest_script_source,
        re.IGNORECASE,
    ) is None:
        raise ValueError("Fallout 3 owned sex-selection contract does not resolve")
    sex_title = _text_values(sex_messages[0], "FULL")
    sex_choices = _text_values(sex_messages[0], "ITXT")
    if len(sex_title) != 1 or not sex_choices:
        raise ValueError("Fallout 3 owned sex-selection message has no choices")
    sex_mapping = {
        int(match.group("index")): match.group("sex").casefold()
        for match in SEX_CHANGE_PATTERN.finditer(quest_script_source)
    }
    if (
        set(sex_mapping) != set(range(len(sex_choices)))
        or set(sex_mapping.values()) != {"male", "female"}
    ):
        raise ValueError("Fallout 3 owned sex choices do not map uniquely to engine sexes")

    name_stage = int(selection["nameStage"])
    appearance_stage = int(selection["appearanceStage"])
    appearance_entered_stage = int(selection["appearanceMenuEnteredStage"])
    appearance_accepted_stage = int(selection["appearanceAcceptedStage"])
    if re.search(
        r"\bGetPlayerName\b",
        "\n".join(stage_sources.get(name_stage, [])),
        re.IGNORECASE,
    ) is None:
        raise ValueError("Fallout 3 owned name-selection command is absent")
    if re.search(
        r"\bShowRaceMenu\b",
        "\n".join(stage_sources.get(appearance_stage, [])),
        re.IGNORECASE,
    ) is None:
        raise ValueError("Fallout 3 owned appearance-selection command is absent")
    if not all(
        stage in stage_sources
        for stage in (appearance_entered_stage, appearance_accepted_stage)
    ):
        raise ValueError("Fallout 3 owned appearance menu convergence stages are absent")
    accepted_source = "\n".join(stage_sources[appearance_accepted_stage])
    transition = _compile_cg00_section4_transition(
        records,
        selection,
        appearance_accepted_stage,
        accepted_source,
        stage_sources,
    )
    selection_quest_form_id = int(
        next(
            str(row["formId"])
            for row in quest_rows
            if str(row["editorId"]).casefold() == selection_quest.casefold()
        ),
        FORM_ID_RADIX,
    )
    post_stage65_dialogue = _compile_post_stage65_dialogue(
        records,
        selection,
        selection_quest_form_id,
        stage_sources,
    )
    post_stage80_dialogue, stage85_transition = _compile_post_stage80_dialogue(
        records,
        selection,
        selection_quest_form_id,
        stage_sources,
    )
    post_stage85_dialogue, stage90_transition = _compile_post_stage85_dialogue(
        records,
        selection,
        selection_quest_form_id,
        quest_script,
        stage_sources,
    )
    stage100_transition = _compile_stage100_transition(
        records,
        selection,
        selection_quest_form_id,
        quest_script,
        quest_script_source,
        stage_sources,
    )
    cg01_stage0_transition = _compile_cg01_stage0_transition(
        records,
        selection,
        stage100_transition,
    )
    early_birth_sequence = _compile_cg00_early_birth_sequence(
        records,
        selection,
        selection_quest_form_id,
        quest_script,
        quest_script_source,
        stage_sources,
    )
    stage90_transition["nextBoundary"] = stage100_transition["schema"]

    character_selection = {
        "questEditorId": selection_quest,
        "sex": {
            "messageEditorId": sex_message_id,
            "messageFormId": _form_id(sex_messages[0].form_id),
            "choiceCount": len(sex_choices),
            "title": sex_title[0],
            "choices": [
                {"label": label, "engineSex": sex_mapping[index]}
                for index, label in enumerate(sex_choices)
            ],
        },
        "name": {"stage": name_stage, "command": "GetPlayerName"},
        "earlyBirthSequence": early_birth_sequence,
        "appearance": {
            "stage": appearance_stage,
            "command": "ShowRaceMenu",
            "menuEnteredStage": appearance_entered_stage,
            "acceptedStage": appearance_accepted_stage,
            "acceptedStageCommand": str(transition["command"]),
            "stageSourceSha256": {
                str(stage): hashlib.sha256(
                    "\n".join(stage_sources[stage]).encode("cp1252")
                ).hexdigest()
                for stage in (
                    appearance_stage,
                    appearance_entered_stage,
                    appearance_accepted_stage,
                )
            },
        },
        "section4Transition": transition,
        "postStage65Dialogue": post_stage65_dialogue,
        "postStage80Dialogue": post_stage80_dialogue,
        "stage85Transition": stage85_transition,
        "postStage85Dialogue": post_stage85_dialogue,
        "stage90Transition": stage90_transition,
        "stage100Transition": stage100_transition,
        "cg01Stage0Transition": cg01_stage0_transition,
    }
    return quest_rows, character_selection


def _ini_settings(path: Path, rows: list[object]) -> list[dict[str, str]]:
    section = ""
    values: dict[tuple[str, str], list[str]] = {}
    for raw_line in path.read_text(encoding="cp1252").splitlines():
        line = raw_line.strip()
        if not line or line.startswith((";", "#")):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1].strip()
            continue
        if "=" not in line:
            continue
        key, value = (part.strip() for part in line.split("=", 1))
        values.setdefault((section.casefold(), key.casefold()), []).append(value)
    result = []
    for raw in rows:
        row = dict(raw)
        section = str(row["section"])
        key = str(row["key"])
        matches = values.get((section.casefold(), key.casefold()), [])
        if len(matches) != 1 or not matches[0]:
            raise ValueError(f"Fallout 3 INI setting does not resolve uniquely: {section}.{key}")
        result.append({"section": section, "key": key, "value": matches[0]})
    return result


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
                f"Fallout 3 loose path does not resolve uniquely: {current} / {part}"
            )
        current = matches[0]
    if not current.is_file():
        raise FileNotFoundError(f"Fallout 3 loose asset is not a file: {current}")
    return current


def prepare_profile(data_root: Path, profile_root: Path, recipe_path: Path) -> dict[str, object]:
    recipe = load_recipe(recipe_path)
    video_import_policy = dict(recipe["videoImport"])
    configuration = load_runtime_configuration()
    install_root, resolved_data_root = resolve_installation(data_root, recipe)
    install = dict(recipe["install"])
    master = find_case_insensitive_file(resolved_data_root, str(install["masterFile"]))
    default_ini = find_case_insensitive_file(install_root, str(install["defaultIniFile"]))

    base_stack = build_plugin_stack(resolved_data_root, [master.name])
    if len(base_stack) != 1 or base_stack[0].masters:
        raise ValueError("Fallout 3 master identity is invalid")

    archives = []
    archive_by_role: dict[str, Path] = {}
    for raw in install["requiredArchives"]:
        row = dict(raw)
        role = str(row["role"])
        archive = find_case_insensitive_file(resolved_data_root, str(row["file"]))
        BsaArchive(archive)
        archives.append(_file_row(archive, role=role))
        archive_by_role[role] = archive

    dlc_rows = []
    plugin_names = [master.name]
    for raw in install["optionalDlc"]:
        definition = dict(raw)
        names = [str(definition["plugin"]), *(str(value) for value in definition["archives"])]
        present = []
        for name in names:
            try:
                present.append(find_case_insensitive_file(resolved_data_root, name))
            except FileNotFoundError:
                pass
        if present and len(present) != len(names):
            raise ValueError(f"Fallout 3 DLC installation is partial: {definition['id']}")
        if not present:
            dlc_rows.append({"id": definition["id"], "available": False})
            continue
        plugin_names.append(present[0].name)
        for archive in present[1:]:
            BsaArchive(archive)
        dlc_rows.append(
            {
                "id": definition["id"],
                "available": True,
                "plugin": _file_row(present[0]),
                "archives": [_file_row(path) for path in present[1:]],
            }
        )
    plugin_stack = build_plugin_stack(resolved_data_root, plugin_names)

    menu = dict(recipe["mainMenu"])
    ui_archive = BsaArchive(archive_by_role[str(menu["uiArchiveRole"])])
    menu_members = []
    menu_member_payloads: dict[str, object] = {}
    for logical_path in menu["requiredMembers"]:
        canonical = canonical_member_path(str(logical_path))
        member = ui_archive.extract(canonical)
        menu_member_payloads[member.logical_path] = member
        menu_members.append(
            {
                "logicalPath": member.logical_path,
                "bytes": len(member.data),
                "sha256": member.sha256,
                "sourceArchive": archive_by_role[str(menu["uiArchiveRole"])].name,
            }
        )
    dialogue_path = canonical_member_path("menus\\dialog\\dialog_menu.xml")
    dialogue_member = menu_member_payloads.get(dialogue_path)
    if dialogue_member is None:
        raise ValueError("Fallout 3 DialogueMenu XML was not admitted")
    dialogue_document, dialogue_tree = _document_index(
        dialogue_path, dialogue_member.data
    )
    dialogue_template_path = canonical_member_path(
        "menus\\prefabs\\list_box_template.xml"
    )
    dialogue_template_member = menu_member_payloads.get(dialogue_template_path)
    if dialogue_template_member is None:
        raise ValueError("Fallout 3 DialogueMenu topic template was not admitted")
    dialogue_template_tree = parse_tile_document(dialogue_template_member.data)
    appearance_ui = dict(dict(recipe["opening"])["appearanceUi"])
    dialogue_menu_tiles = dialogue_menu_tile_contract(
        dialogue_tree,
        dialogue_document,
        {
            "width": float(appearance_ui["sourceCanvasWidth"]),
            "height": float(appearance_ui["sourceCanvasHeight"]),
        },
        dialogue_template_tree,
    )
    texture_archive = BsaArchive(archive_by_role[str(menu["textureArchiveRole"])])
    menu_textures = []
    for logical_path in menu["requiredTextureMembers"]:
        member = texture_archive.extract(canonical_member_path(str(logical_path)))
        menu_textures.append(
            {
                "logicalPath": member.logical_path,
                "bytes": len(member.data),
                "sha256": member.sha256,
                "sourceArchive": archive_by_role[str(menu["textureArchiveRole"])].name,
            }
        )
    ini_rows = _ini_settings(default_ini, list(menu["iniSettings"]))
    music_setting = dict(menu["musicSetting"])
    music_values = [
        row["value"]
        for row in ini_rows
        if row["section"].casefold() == str(music_setting["section"]).casefold()
        and row["key"].casefold() == str(music_setting["key"]).casefold()
    ]
    if len(music_values) != 1:
        raise ValueError("Fallout 3 main-menu music setting is ambiguous")
    music_root = _case_insensitive_directory(resolved_data_root, str(menu["musicDirectoryName"]))
    if music_root is None:
        raise FileNotFoundError("Fallout 3 Music directory is absent")
    main_menu_music = _file_row(
        _case_insensitive_descendant(music_root, music_values[0]),
        role="mainMenuMusic",
    )

    opening = dict(recipe["opening"])
    quests, character_selection = _quest_inventory(master, opening)
    special_path = canonical_member_path("menus\\chargen\\specialbookmenu.xml")
    special_member = menu_member_payloads.get(special_path)
    if special_member is None:
        raise ValueError("Fallout 3 SPECIALBookMenu XML was not admitted")
    cg01 = dict(character_selection["cg01Stage0Transition"])
    post_stage5 = dict(cg01["postStage5Transition"])
    post_stage14 = dict(post_stage5["postStage14Transition"])
    stage20_interaction = dict(post_stage14["stage20Interaction"])
    special_book = dict(stage20_interaction["specialBook"])
    special_book["tiles"] = _special_book_menu_tile_contract(special_member)
    stage20_interaction["specialBook"] = special_book
    post_stage14["stage20Interaction"] = stage20_interaction
    post_stage5["postStage14Transition"] = post_stage14
    cg01["postStage5Transition"] = post_stage5
    character_selection["cg01Stage0Transition"] = cg01
    meshes_role = "meshes"
    meshes_archive_path = archive_by_role[meshes_role]
    section4_transition = dict(character_selection["section4Transition"])
    meshes_archive = BsaArchive(meshes_archive_path)
    meshes_archive_sha256 = next(
        str(row["sha256"]) for row in archives if row["role"] == meshes_role
    )
    voices_role = "voices"
    voices_archive = BsaArchive(archive_by_role[voices_role])
    voices_archive_sha256 = next(
        str(row["sha256"]) for row in archives if row["role"] == voices_role
    )
    menu_voices_role = "menuVoices"
    menu_voices_archive = BsaArchive(archive_by_role[menu_voices_role])
    menu_voices_archive_sha256 = next(
        str(row["sha256"]) for row in archives if row["role"] == menu_voices_role
    )
    sound_role = "sound"
    sound_archive = BsaArchive(archive_by_role[sound_role])
    sound_archive_sha256 = next(
        str(row["sha256"]) for row in archives if row["role"] == sound_role
    )
    early_birth_sequence = dict(character_selection["earlyBirthSequence"])
    _bind_cg00_early_birth_assets(
        early_birth_sequence,
        meshes_archive,
        meshes_archive_sha256,
        voices_archive,
        voices_archive_sha256,
        sound_archive,
        sound_archive_sha256,
        profile_root,
        configuration.content_compiler.animation_samples_per_second,
    )
    character_selection["earlyBirthSequence"] = early_birth_sequence
    _bind_cg00_package_animations(
        section4_transition,
        meshes_archive,
        meshes_archive_sha256,
    )
    character_selection["section4Transition"] = section4_transition
    post_stage65_dialogue = dict(character_selection["postStage65Dialogue"])
    _bind_owned_dad_dialogue_audio(
        post_stage65_dialogue,
        voices_archive,
        voices_archive_sha256,
        profile_root,
    )
    character_selection["postStage65Dialogue"] = post_stage65_dialogue
    post_stage85_dialogue = dict(character_selection["postStage85Dialogue"])
    _bind_owned_dad_dialogue_audio(
        post_stage85_dialogue,
        voices_archive,
        voices_archive_sha256,
        profile_root,
    )
    character_selection["postStage85Dialogue"] = post_stage85_dialogue
    cg01_transition = dict(character_selection["cg01Stage0Transition"])
    post_stage5_transition = dict(cg01_transition["postStage5Transition"])
    cg01_dad_dialogue = dict(post_stage5_transition["dialogue"])
    _bind_owned_dad_dialogue_animations(
        cg01_dad_dialogue,
        meshes_archive,
        meshes_archive_sha256,
    )
    _bind_owned_dad_dialogue_audio(
        cg01_dad_dialogue,
        voices_archive,
        voices_archive_sha256,
        profile_root,
    )
    post_stage5_transition["dialogue"] = cg01_dad_dialogue
    stage12_dad_response = dict(post_stage5_transition["postStage12DadResponse"])
    stage12_dad_dialogue = dict(stage12_dad_response["dialogue"])
    _bind_owned_dad_dialogue_animations(
        stage12_dad_dialogue,
        meshes_archive,
        meshes_archive_sha256,
    )
    _bind_owned_dad_dialogue_audio(
        stage12_dad_dialogue,
        voices_archive,
        voices_archive_sha256,
        profile_root,
    )
    stage12_dad_response["dialogue"] = stage12_dad_dialogue
    post_stage5_transition["postStage12DadResponse"] = stage12_dad_response
    post_stage14_transition = dict(post_stage5_transition["postStage14Transition"])
    post_stage14_dialogue = dict(post_stage14_transition["dialogue"])
    _bind_owned_dad_dialogue_audio(
        post_stage14_dialogue,
        voices_archive,
        voices_archive_sha256,
        profile_root,
    )
    post_stage14_transition["dialogue"] = post_stage14_dialogue
    stage20_interaction = dict(post_stage14_transition["stage20Interaction"])
    timer_transition = dict(stage20_interaction["timerTransition"])
    dad_return = dict(timer_transition["dadReturn"])
    dad_return_dialogue = dict(dad_return["dialogue"])
    _bind_owned_dad_dialogue_audio(
        dad_return_dialogue,
        voices_archive,
        voices_archive_sha256,
        profile_root,
    )
    dad_return["dialogue"] = dad_return_dialogue
    dad_lead = dict(dad_return["dadLead"])
    locomotion = dict(dad_lead["locomotion"])
    locomotion_member = meshes_archive.extract(str(locomotion["logicalPath"]))
    locomotion_playback = animation_sequence_manifest(locomotion_member.data)
    locomotion_root = sample_root_motion(
        locomotion_member.data,
        str(locomotion["rootNode"]),
        configuration.content_compiler.animation_samples_per_second,
    ).manifest()
    if any(
        locomotion_root[key] != locomotion_playback[key]
        for key in ("sequenceName", "startSeconds", "stopSeconds", "cycleType")
    ):
        raise ValueError("Fallout 3 CG01 Dad lead locomotion playback differs")
    dad_lead["locomotion"] = {
        **locomotion,
        "bytes": len(locomotion_member.data),
        "sha256": locomotion_member.sha256,
        "sourceArchive": meshes_archive_path.name,
        "sourceArchiveSha256": meshes_archive_sha256,
        **locomotion_playback,
        "rootMotion": locomotion_root,
    }
    completion = dict(dad_lead["completion"])
    stage90_result = dict(completion["stage90Result"])
    _bind_stage90_sound(
        stage90_result,
        sound_archive,
        sound_archive_sha256,
        profile_root,
    )
    completion["stage90Result"] = stage90_result
    cg02_stage0 = dict(completion["cg02Stage0"])
    cg02_intro = dict(cg02_stage0["introRuntime"])
    _bind_cg02_intro_assets(
        cg02_intro,
        configuration,
        resolved_data_root,
        profile_root,
        meshes_archive,
        meshes_archive_sha256,
        voices_archive,
        voices_archive_sha256,
        sound_archive,
        sound_archive_sha256,
    )
    if "dadSpeechRuntime" in cg02_intro:
        dad_speech = dict(cg02_intro["dadSpeechRuntime"])
        dad_dialogue = dict(dad_speech["dialogue"])
        _bind_owned_dad_dialogue_animations(
            dad_dialogue,
            meshes_archive,
            meshes_archive_sha256,
        )
        _bind_owned_dad_dialogue_audio(
            dad_dialogue,
            menu_voices_archive,
            menu_voices_archive_sha256,
            profile_root, voices_archive, voices_archive_sha256,
        )
        dad_speech["dialogue"] = dad_dialogue
        if "overseerSpeechRuntime" in dad_speech:
            overseer = dict(dad_speech["overseerSpeechRuntime"])
            overseer_dialogue = dict(overseer["dialogue"])
            _bind_owned_dad_dialogue_animations(
                overseer_dialogue,
                meshes_archive,
                meshes_archive_sha256,
            )
            _bind_owned_dad_dialogue_audio(
                overseer_dialogue,
                menu_voices_archive,
                menu_voices_archive_sha256,
                profile_root, voices_archive, voices_archive_sha256,
            )
            overseer["dialogue"] = overseer_dialogue
            if "dadPartyRuntime" in overseer:
                party = dict(overseer["dadPartyRuntime"])
                party_dialogue = dict(party["dialogue"])
                _bind_owned_dad_dialogue_animations(
                    party_dialogue, meshes_archive, meshes_archive_sha256)
                _bind_owned_dad_dialogue_audio(
                    party_dialogue, menu_voices_archive,
                    menu_voices_archive_sha256,
                    profile_root, voices_archive, voices_archive_sha256)
                party["dialogue"] = party_dialogue
                if "birthdayInteractionsRuntime" in party:
                    birthday = dict(party["birthdayInteractionsRuntime"])
                    prepared_participants = []
                    for raw_participant in birthday["participants"]:
                        participant = dict(raw_participant)
                        participant_dialogue = dict(participant["dialogue"])
                        _bind_owned_dad_dialogue_audio(
                            participant_dialogue, menu_voices_archive,
                            menu_voices_archive_sha256, profile_root,
                            voices_archive, voices_archive_sha256)
                        participant["dialogue"] = participant_dialogue
                        prepared_participants.append(participant)
                    birthday["participants"] = prepared_participants
                    if "cakeRuntime" in birthday:
                        cake = dict(birthday["cakeRuntime"])
                        cake_dialogue = dict(cake["dialogue"])
                        prepared_cues = []
                        for raw_cue in cake_dialogue["cues"]:
                            cue = dict(raw_cue)
                            branch_dialogue = {
                                "voiceType": dict(cue["voiceType"]),
                                "branches": [{
                                    "infoFormId": cue["infoFormId"],
                                    "response": dict(cue["response"]),
                                }],
                            }
                            _bind_owned_dad_dialogue_audio(
                                branch_dialogue, menu_voices_archive,
                                menu_voices_archive_sha256, profile_root,
                                voices_archive, voices_archive_sha256)
                            cue["response"] = dict(
                                list(branch_dialogue["branches"])[0])["response"]
                            prepared_cues.append(cue)
                        cake_dialogue["cues"] = prepared_cues
                        cake_dialogue["dialoguePlaybackPrepared"] = True
                        cake_dialogue["dialoguePlaybackImplemented"] = True
                        cake["dialogue"] = cake_dialogue
                        birthday["cakeRuntime"] = cake
                    if "butchRuntime" in birthday:
                        butch = dict(birthday["butchRuntime"])
                        if "postIntercomRuntime" in butch:
                            post_intercom = dict(butch["postIntercomRuntime"])
                            post_dialogue = dict(post_intercom["dialogue"])
                            prepared_cues = []
                            for raw_cue in post_dialogue["cues"]:
                                cue = dict(raw_cue)
                                branches = [{"infoFormId": cue["infoFormId"],
                                             "response": dict(response)}
                                            for response in cue["responses"]]
                                cue_dialogue = {"voiceType": dict(cue["voiceType"]),
                                                "branches": branches}
                                _bind_owned_dad_dialogue_audio(
                                    cue_dialogue, menu_voices_archive,
                                    menu_voices_archive_sha256, profile_root,
                                    voices_archive, voices_archive_sha256)
                                cue["responses"] = [dict(row)["response"]
                                                    for row in cue_dialogue["branches"]]
                                prepared_cues.append(cue)
                            post_dialogue["cues"] = prepared_cues
                            post_dialogue["dialoguePlaybackPrepared"] = True
                            post_dialogue["dialoguePlaybackImplemented"] = True
                            post_intercom["dialogue"] = post_dialogue
                            if "reactorGiftRuntime" in post_intercom:
                                reactor_gift = dict(post_intercom["reactorGiftRuntime"])
                                prepared_participants = []
                                for raw_participant in reactor_gift["participants"]:
                                    participant = dict(raw_participant)
                                    participant_dialogue = dict(participant["dialogue"])
                                    _bind_owned_dad_dialogue_audio(
                                        participant_dialogue, menu_voices_archive,
                                        menu_voices_archive_sha256, profile_root,
                                        voices_archive, voices_archive_sha256)
                                    participant_dialogue["dialoguePlaybackPrepared"] = True
                                    participant_dialogue["dialoguePlaybackImplemented"] = True
                                    participant["dialogue"] = participant_dialogue
                                    prepared_participants.append(participant)
                                reactor_gift["participants"] = prepared_participants
                                post_intercom["reactorGiftRuntime"] = reactor_gift
                            butch["postIntercomRuntime"] = post_intercom
                        birthday["butchRuntime"] = butch
                    party["birthdayInteractionsRuntime"] = birthday
                overseer["dadPartyRuntime"] = party
            dad_speech["overseerSpeechRuntime"] = overseer
        cg02_intro["dadSpeechRuntime"] = dad_speech
    cg02_stage0["introRuntime"] = cg02_intro
    completion["cg02Stage0"] = cg02_stage0
    dad_lead["completion"] = completion
    dad_return["dadLead"] = dad_lead
    timer_transition["dadReturn"] = dad_return
    stage20_interaction["timerTransition"] = timer_transition
    post_stage14_transition["stage20Interaction"] = stage20_interaction
    post_stage5_transition["postStage14Transition"] = post_stage14_transition
    cg01_transition["postStage5Transition"] = post_stage5_transition
    character_selection["cg01Stage0Transition"] = cg01_transition
    _bind_cg01_toddler_world(
        character_selection,
        dict(dict(opening["characterSelection"])["cg01Stage0Transition"]),
        default_ini,
        configuration,
    )
    owned_archive_stack = OwnedArchiveStack(
        tuple(
            OwnedArchive(
                path.name,
                path,
                file_sha256(path),
                path.stat().st_size,
                BsaArchive(path),
            )
            for path in archive_by_role.values()
        )
    )
    appearance_contract = _appearance_inventory(
        master,
        recipe,
        character_selection,
        menu_member_payloads,
        texture_archive,
        next(
            str(row["sha256"])
            for row in archives
            if row["role"] == str(menu["textureArchiveRole"])
        ),
        profile_root,
        archive_by_role[str(menu["uiArchiveRole"])],
        owned_archive_stack,
        configuration,
    )
    character_selection["appearance"] = appearance_contract
    stage80_transition = dict(character_selection["stage80Transition"])
    _bind_cg00_package_animations(
        stage80_transition,
        meshes_archive,
        meshes_archive_sha256,
        "addedPlayerPackage",
    )
    character_selection["stage80Transition"] = stage80_transition
    stage90_transition = dict(character_selection["stage90Transition"])
    _bind_stage90_sound(
        stage90_transition,
        sound_archive,
        sound_archive_sha256,
        profile_root,
    )
    character_selection["stage90Transition"] = stage90_transition
    video_root = _case_insensitive_directory(resolved_data_root, str(opening["videoDirectoryName"]))
    if video_root is None:
        raise FileNotFoundError("Fallout 3 opening Video directory is absent")
    video_names = [
        str(opening["introVideo"]),
        *(
            str(dict(row)["transitionVideo"])
            for row in opening["quests"]
            if "transitionVideo" in dict(row)
        ),
    ]
    videos = [_file_row(find_case_insensitive_file(video_root, name)) for name in video_names]
    runtime_intro_video = _prepare_runtime_video(
        Path(str(videos[0]["source"])),
        profile_root,
        configuration,
        video_import_policy,
    )
    cg01_transition = dict(character_selection["cg01Stage0Transition"])
    stage0_commands = [
        dict(command)
        for command in dict(cg01_transition["stage0Result"])["commands"]
    ]
    nested_commands = [
        dict(command)
        for command in dict(stage0_commands[1]["stageResult"])["commands"]
    ]
    transition_movie_commands = [
        command for command in nested_commands if command.get("kind") == "playBink"
    ]
    if len(transition_movie_commands) != 1:
        raise ValueError("Fallout 3 CG01 transition movie command is ambiguous")
    transition_movie = transition_movie_commands[0]
    transition_source_matches = [
        row
        for row in videos[1:]
        if str(row["file"]).casefold() == str(transition_movie["logicalPath"]).casefold()
    ]
    if len(transition_source_matches) != 1:
        raise ValueError("Fallout 3 CG01 owned transition movie is ambiguous")
    transition_source = transition_source_matches[0]
    runtime_transition_video = _prepare_runtime_video(
        Path(str(transition_source["source"])),
        profile_root,
        configuration,
        video_import_policy,
    )
    prepared_transition_video = {
        **transition_source,
        "runtime": runtime_transition_video,
    }
    _bind_cg01_transition_video(character_selection, prepared_transition_video)
    cg01_transition = dict(character_selection["cg01Stage0Transition"])
    post_stage5 = dict(cg01_transition["postStage5Transition"])
    post_stage14 = dict(post_stage5["postStage14Transition"])
    interaction = dict(post_stage14["stage20Interaction"])
    timer = dict(interaction["timerTransition"])
    dad_return = dict(timer["dadReturn"])
    dad_lead = dict(dad_return["dadLead"])
    completion = dict(dad_lead["completion"])
    cg02 = dict(completion["cg02Stage0"])
    cg02_commands = [dict(command) for command in cg02["stage5Commands"]]
    cg02_movies = [command for command in cg02_commands if command["kind"] == "playBink"]
    if len(cg02_movies) != 1:
        raise ValueError("Fallout 3 CG02 stage 5 movie command is ambiguous")
    cg02_movie = cg02_movies[0]
    cg02_sources = [
        row for row in videos[1:]
        if str(row["file"]).casefold() == str(cg02_movie["logicalPath"]).casefold()
    ]
    if len(cg02_sources) != 1:
        raise ValueError("Fallout 3 CG02 owned transition movie is ambiguous")
    cg02_source = cg02_sources[0]
    prepared_cg02_video = {
        **cg02_source,
        "runtime": _prepare_runtime_video(
            Path(str(cg02_source["source"])),
            profile_root,
            configuration,
            video_import_policy,
        ),
    }
    cg02_movie["video"] = prepared_cg02_video
    cg02["stage5Commands"] = cg02_commands
    completion["cg02Stage0"] = cg02
    dad_lead["completion"] = completion
    dad_return["dadLead"] = dad_lead
    timer["dadReturn"] = dad_return
    interaction["timerTransition"] = timer
    post_stage14["stage20Interaction"] = interaction
    post_stage5["postStage14Transition"] = post_stage14
    cg01_transition["postStage5Transition"] = post_stage5
    character_selection["cg01Stage0Transition"] = cg01_transition
    transition_videos = [
        prepared_transition_video if row is transition_source
        else prepared_cg02_video if row is cg02_source
        else row
        for row in videos[1:]
    ]

    source_rows = [
        _file_row(master, role="master"),
        _file_row(default_ini, role="defaultIni"),
        *archives,
        main_menu_music,
        *videos,
    ]
    identity_payload = json.dumps(
        [(row["file"], row["sha256"]) for row in source_rows],
        separators=(",", ":"),
    ).encode("utf-8")
    profile_id = hashlib.sha256(identity_payload).hexdigest()[:PROFILE_ID_HEX_CHARACTERS]
    registrar_path = Path(sys.executable) if getattr(sys, "frozen", False) else Path(__file__)
    opening_slice_compiler_path = (
        Path(sys.executable)
        if getattr(sys, "frozen", False)
        else Path(compile_opening_slice.__code__.co_filename)
    )
    opening_slice_result = compile_opening_slice(
        resolved_data_root,
        profile_root,
        default_opening_slice_recipe_path(),
    )
    opening_slice = opening_slice_result["manifest"]
    font_pipeline = OwnedTexturePipeline(
        owned_archive_stack,
        profile_root,
        {},
        configuration.content_compiler,
    )
    font_settings = dict(menu["fonts"])
    ini = _ini_index(default_ini)
    ui_fonts = _compile_fo3_ui_fonts(
        dialogue_menu_tiles,
        appearance_contract,
        ini,
        font_settings,
        owned_archive_stack,
        profile_root,
        font_pipeline,
    )

    manifest = {
        "schema": PROFILE_SCHEMA,
        "status": PROFILE_STATUS,
        "campaign": recipe["campaign"],
        "profileId": profile_id,
        "recipe": {"id": recipe["id"], "sha256": file_sha256(recipe_path)},
        "registrar": {
            "name": "OpenNV Fallout 3 owned-profile registrar v1",
            "sha256": file_sha256(registrar_path),
            "openingSliceCompilerSha256": file_sha256(opening_slice_compiler_path),
            "runtimeConfiguration": configuration.manifest(),
        },
        "install": {
            "root": str(install_root),
            "dataRoot": str(resolved_data_root),
            "master": source_rows[0],
            "defaultIni": source_rows[1],
            "archives": archives,
            "pluginStack": [
                {
                    "file": context.name,
                    "bytes": context.bytes,
                    "sha256": context.sha256,
                    "masters": list(context.masters),
                }
                for context in plugin_stack
            ],
            "dlc": dlc_rows,
        },
        "mainMenu": {
            "members": menu_members,
            "dialogueMenuTiles": dialogue_menu_tiles,
            "fonts": ui_fonts,
            "textures": menu_textures,
            "music": main_menu_music,
            "iniSettings": ini_rows,
        },
        "opening": {
            "introVideo": {**videos[0], "runtime": runtime_intro_video},
            "transitionVideos": transition_videos,
            "quests": quests,
            "characterSelection": character_selection,
            "birthSlice": {
                "schema": opening_slice["schema"],
                "output": opening_slice_result["output"],
                "sha256": opening_slice_result["outputSha256"],
                "cellFormId": opening_slice["cell"]["formId"],
                "playerSpawnReferenceFormId": opening_slice["startGraph"][
                    "playerSpawn"
                ]["formId"],
                "doctorActorReferenceFormId": opening_slice["doctorActor"][
                    "reference"
                ]["formId"],
            },
        },
        "capabilities": {
            "profileSelectable": True,
            "mainMenuInputsResolved": True,
            "mainMenuRuntimeReady": True,
            "introVideoRuntimeReady": True,
            "openingQuestInventoryResolved": True,
            "characterSelectionContractResolved": True,
            "cg00SexAndNameRuntimeReady": True,
            "cg00AppearanceRuntimeReady": True,
            "cg00Section4PackageContractReady": True,
            "cg00Stage65AppearanceContractReady": True,
            "cg00Stage80ContractReady": True,
            "cg00Stage85ContractReady": True,
            "cg00Stage90ContractReady": True,
            "cg00Stage100ContractReady": True,
            "cg01Stage0ContractReady": True,
            "cg01Stage10ContractReady": True,
            "cg01Stage12ContractReady": True,
            "cg01ToddlerWorldRuntimeReady": True,
            "vault101BirthGraphCompiled": True,
            "runtimeBootReady": True,
        },
        "blockers": [
            "fo3-cg01-stage-20-playpen-special-runtime-not-implemented",
            "fo3-cg01-toddler-visual-body-not-prepared",
            "fo3-opening-command-interpreter-after-cg00-not-implemented",
            "fo3-vault101-godot-scene-not-compiled",
        ],
    }
    output = profile_root.resolve() / "fallout3-profile.json"
    atomic_json(output, manifest)
    return {"output": str(output), "manifest": manifest}


def refresh_cg00_player_camera(
    data_root: Path,
    profile_root: Path,
    recipe_path: Path,
) -> dict[str, object]:
    """Refresh only the owned CG00 section-1 player-camera transform closure."""
    recipe = load_recipe(recipe_path)
    configuration = load_runtime_configuration()
    _install_root, resolved_data_root = resolve_installation(data_root, recipe)
    profile_path = profile_root.resolve() / "fallout3-profile.json"
    if not profile_path.is_file():
        raise FileNotFoundError("Fallout 3 owned profile is absent for camera refresh")
    manifest = json.loads(profile_path.read_text(encoding="utf-8"))
    if (
        manifest.get("schema") != PROFILE_SCHEMA
        or manifest.get("status") != PROFILE_STATUS
        or dict(manifest.get("recipe", {})).get("id") != recipe["id"]
        or Path(str(dict(manifest["install"])["dataRoot"])).resolve()
        != resolved_data_root.resolve()
    ):
        raise ValueError("Fallout 3 owned profile camera refresh identity differs")

    mesh_rows = [
        dict(row)
        for row in dict(recipe["install"])["requiredArchives"]
        if dict(row)["role"] == "meshes"
    ]
    if len(mesh_rows) != 1:
        raise ValueError("Fallout 3 owned meshes archive recipe is ambiguous")
    meshes_path = find_case_insensitive_file(
        resolved_data_root,
        str(mesh_rows[0]["file"]),
    )
    meshes_sha256 = file_sha256(meshes_path)
    registered_meshes = [
        dict(row)
        for row in dict(manifest["install"])["archives"]
        if dict(row)["role"] == "meshes"
    ]
    if (
        len(registered_meshes) != 1
        or str(registered_meshes[0]["sha256"]).casefold() != meshes_sha256
    ):
        raise ValueError("Fallout 3 owned meshes archive changed before camera refresh")

    character_selection = dict(dict(manifest["opening"])["characterSelection"])
    sequence = dict(character_selection["earlyBirthSequence"])
    definition = dict(dict(dict(recipe["opening"])["characterSelection"])[
        "earlyBirthSequence"
    ])
    camera_definition = dict(definition["playerCamera"])
    player_marker = dict(sequence["playerStartMarker"])
    marker_transform = dict(player_marker["authoredTransform"])
    if str(player_marker["formId"]).casefold() != str(
        definition["playerStartMarkerFormId"]
    ).casefold():
        raise ValueError("Fallout 3 player camera start marker changed")
    sequence["playerCamera"] = {
        "section": int(camera_definition["section"]),
        "skeletonLogicalPath": canonical_member_path(
            str(camera_definition["skeletonLogicalPath"])
        ),
        "targetNode": str(camera_definition["targetNode"]),
        "playerStartMarkerFormId": str(player_marker["formId"]),
        "playerStartMarkerRotationGodotQuaternion": godot_rotation_quaternion(
            tuple(float(value) for value in marker_transform["rotationRadians"])
        ),
    }
    _bind_cg00_player_camera_asset(
        sequence,
        BsaArchive(meshes_path),
        meshes_sha256,
        profile_root,
        configuration.content_compiler.animation_samples_per_second,
    )
    character_selection["earlyBirthSequence"] = sequence
    manifest["opening"]["characterSelection"] = character_selection
    manifest["recipe"] = {"id": recipe["id"], "sha256": file_sha256(recipe_path)}
    registrar_path = Path(sys.executable) if getattr(sys, "frozen", False) else Path(__file__)
    manifest["registrar"]["sha256"] = file_sha256(registrar_path)
    manifest["capabilities"]["cg00PlayerCameraRuntimeReady"] = True
    atomic_json(profile_path, manifest)
    camera = dict(sequence["playerCamera"])
    return {
        "output": str(profile_path),
        "manifest": manifest,
        "camera": camera,
        "outputSha256": file_sha256(profile_path),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--profile-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    parser.add_argument("--refresh-cg00-player-camera-only", action="store_true")
    args = parser.parse_args()
    try:
        result = (
            refresh_cg00_player_camera(
                args.data_root.resolve(),
                args.profile_root.resolve(),
                args.recipe.resolve(),
            )
            if args.refresh_cg00_player_camera_only
            else prepare_profile(
                args.data_root.resolve(),
                args.profile_root.resolve(),
                args.recipe.resolve(),
            )
        )
    except Exception as error:
        print(f"OPENNV_FO3_PROFILE_ERROR {error}", file=sys.stderr)
        return 2
    manifest = result["manifest"]
    if args.refresh_cg00_player_camera_only:
        camera = dict(result["camera"])
        print(
            "OPENNV_FO3_CG00_PLAYER_CAMERA "
            + json.dumps(
                {
                    "profile": result["output"],
                    "profileSha256": result["outputSha256"],
                    "animationSha256": dict(camera["animation"])["sha256"],
                    "skeletonSha256": dict(camera["skeleton"])["sha256"],
                    "sampleContractSha256": camera["sampleContractSha256"],
                    "sampleCount": len(dict(camera["track"])["samples"]),
                },
                sort_keys=True,
            )
        )
        return 0
    print(
        "OPENNV_FO3_PROFILE "
        + json.dumps(
            {
                "profile": result["output"],
                "profileId": manifest["profileId"],
                "dlc": [row["id"] for row in manifest["install"]["dlc"] if row["available"]],
                "runtimeBootReady": manifest["capabilities"]["runtimeBootReady"],
                "vault101BirthGraphCompiled": manifest["capabilities"][
                    "vault101BirthGraphCompiled"
                ],
                "openingSlice": manifest["opening"]["birthSlice"]["output"],
                "blockers": manifest["blockers"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
