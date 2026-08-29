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
from bsa_archive import BsaArchive, canonical_member_path
from cell_catalog import cell_parent_form_id
from environment_catalog import parse_image_space_modifier
from facegen import compose_facegen_coordinates
from opening_catalog import _prepare_runtime_video
from plugin_records import iter_plugin_records, iter_subrecords, zstring
from plugin_stack import build_plugin_stack, file_sha256, find_case_insensitive_file
from prepare_fo3_opening_slice import (
    compile_opening_slice,
    default_recipe_path as default_opening_slice_recipe_path,
)
from runtime_configuration import load_runtime_configuration
from texture_pipeline import decode_dds


RECIPE_SCHEMA = "opennv-fo3-owned-profile-recipe/v1"
PROFILE_SCHEMA = "opennv-owned-game-profile/v1"
PROFILE_STATUS = "registered-owned-profile"
PROFILE_ID_HEX_CHARACTERS = 20
FORM_ID_HEX_CHARACTERS = 8
FORM_ID_BYTES = 4
FORM_ID_RADIX = 16
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
PLACED_REFERENCE_RECORD = "REFR"
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
SET_OBJECTIVE_DISPLAYED_PATTERN = re.compile(
    r"^setObjectiveDisplayed\s+(?P<quest>[A-Za-z_][A-Za-z0-9_]*)\s+"
    r"(?P<index>\d+)\s+(?P<value>\d+)$",
    re.IGNORECASE,
)
AUTOSAVE_PATTERN = re.compile(r"^autosave$", re.IGNORECASE)
SET_NO_ACTIVATION_SOUND_PATTERN = re.compile(
    r"^SetNoActivationSound\s+(?P<sound>[A-Za-z_][A-Za-z0-9_]*)$",
    re.IGNORECASE,
)
SET_PC_TODDLER_PATTERN = re.compile(r"^SetPCToddler\s+(?P<value>\d+)$", re.IGNORECASE)
PLAY_BINK_COMMAND_PATTERN = re.compile(
    r'^playBink\s+"(?P<path>[^"]+\.bik)"\s+'
    r"(?P<arguments>\d+(?:\s+\d+){3})$",
    re.IGNORECASE,
)
REFERENCE_TRANSFORM_FLOATS = 6
REFERENCE_TRANSFORM_BYTES = REFERENCE_TRANSFORM_FLOATS * 4
REFERENCE_SCALE_BYTES = 4
DEFAULT_REFERENCE_SCALE = 1.0
CG00_TIMER_CHAIN_PATTERN = re.compile(
    r"\bif\s+runTimer\s*==\s*1\b.*?"
    r"\bif\s+timer\s*>\s*0\b\s*"
    r"set\s+timer\s+to\s+timer\s*-\s*GetSecondsPassed\b.*?"
    r"(?P<stage_branches>.*?)"
    r"\bendif\b\s*\bendif\b\s*\bif\s+chooseSex\b",
    re.IGNORECASE | re.DOTALL,
)
CG00_TIMER_STAGE_PATTERN = re.compile(
    r"\b(?:if|elseif)\s+getstage\s+CG00\s*==\s*(?P<source>\d+)\b\s*"
    r"setstage\s+CG00\s+(?P<target>\d+)\b",
    re.IGNORECASE,
)
SET_REFERENCE_VARIABLE_PATTERN = re.compile(
    r"^set\s+(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\."
    r"(?P<variable>[A-Za-z_][A-Za-z0-9_]*)\s+to\s+(?P<value>-?\d+(?:\.\d+)?)$",
    re.IGNORECASE,
)
REFERENCE_COMMAND_PATTERN = re.compile(
    r"^(?P<subject>[A-Za-z_][A-Za-z0-9_]*)\."
    r"(?P<command>evp|enable)$",
    re.IGNORECASE,
)
CONDITION_BYTES = 28
CONDITION_FUNCTION_OFFSET = 8
CONDITION_PARAMETER_1_OFFSET = 12
CONDITION_PARAMETER_2_OFFSET = 16
CONDITION_RUN_ON_OFFSET = 20
CONDITION_REFERENCE_OFFSET = 24
GET_IS_SEX_FUNCTION = 70
GET_STAGE_FUNCTION = 58
GET_IS_VOICE_TYPE_FUNCTION = 427
GET_PC_IS_SEX_FUNCTION = 131
GET_IS_ID_FUNCTION = 72
DIALOGUE_CHILD_GROUP_TYPE = 7
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


def _float_contract(values: tuple[float, ...], expected_count: int) -> dict[str, object]:
    if len(values) != expected_count or not all(math.isfinite(value) for value in values):
        raise ValueError("Fallout 3 FaceGen default coordinates are incomplete")
    payload = struct.pack(f"<{len(values)}f", *values)
    return {
        "count": len(values),
        "values": list(values),
        "sha256": hashlib.sha256(payload).hexdigest(),
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
    if panel is None or list_item is None:
        raise ValueError("Fallout 3 appearance menu layout owners are absent")

    def dimension(body: str, name: str) -> int:
        match = re.search(rf'<{name}>\s*(?P<value>\d+)\s*</{name}>', body)
        if match is None:
            raise ValueError(f"Fallout 3 appearance menu {name} is absent")
        return int(match.group("value"))

    observed = {
        "panelWidth": dimension(panel.group("body"), "width"),
        "panelHeight": dimension(panel.group("body"), "height"),
        "listItemWidth": dimension(list_item.group("body"), "width"),
        "listItemHeight": dimension(list_item.group("body"), "height"),
    }
    for key, value in observed.items():
        if value != int(definition[key]):
            raise ValueError(
                f"Fallout 3 appearance menu {key} differs: "
                f"expected={definition[key]} actual={value}"
            )
    background_path = canonical_member_path(str(definition["backgroundTexture"]))
    if background_path.removeprefix("textures\\") not in text.casefold():
        raise ValueError("Fallout 3 appearance menu background identity differs")
    return {
        "document": document_path,
        "documentSha256": member.sha256,
        "menuName": menu_name,
        "panelName": panel_name,
        **observed,
        "backgroundTexture": _extract_profile_texture(
            texture_archive,
            texture_archive_sha256,
            background_path,
            profile_root,
            texture_cache,
        ),
    }


def _appearance_inventory(
    master: Path,
    recipe: dict[str, object],
    character_selection: dict[str, object],
    menu_member_payloads: dict[str, object],
    texture_archive: BsaArchive,
    texture_archive_sha256: str,
    profile_root: Path,
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
    return {
        **appearance,
        "schema": "opennv-fo3-cg00-appearance/v1",
        "status": "source-backed-default-selection",
        "player": {
            "formId": _form_id(player.form_id),
            "editorId": player.editor_id,
            "recordSha256": catalog.record_data_sha256["NPC_"][player.form_id],
            "defaultRaceFormId": _form_id(player.race_form_id or 0),
            "defaultHairColorRgba": list(player.hair_color_rgba),
            "defaultHairLength": player.hair_length,
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
        "preview": "owned-head-hair-eye-source-textures-not-a-3d-face-render",
    }


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
        "nextBoundary": {
            "applied": False,
            "blocker": "fo3-cg01-post-stage-10-toddler-world-interaction-not-implemented",
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
        matches = [
            path
            for path in voices_archive.members
            if path.startswith(namespace + "\\") and path.endswith(suffix)
        ]
        if len(matches) != 1:
            raise ValueError(
                "Fallout 3 owned Dad voice is absent or ambiguous: "
                f"info={info_form_id} response={response_index}"
            )
        voice_path = matches[0]
        lip_path = voice_path.removesuffix(".ogg") + ".lip"
        if lip_path not in voices_archive.members:
            raise ValueError(
                "Fallout 3 owned Dad lip data is absent: "
                f"info={info_form_id} response={response_index}"
            )

        def prepare_asset(logical_path: str) -> dict[str, object]:
            member = voices_archive.extract(logical_path)
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
                "sourceArchive": voices_archive.archive.name,
                "sourceArchiveSha256": voices_archive_sha256,
            }

        response["voice"] = prepare_asset(voice_path)
        response["lip"] = prepare_asset(lip_path)
        branch["response"] = response
        prepared.append(branch)
    dialogue["branches"] = prepared
    dialogue["dialoguePlaybackPrepared"] = True
    dialogue["dialoguePlaybackImplemented"] = True
    dialogue["voiceType"] = {**voice_type, "memberNamespace": namespace}


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
                    PLACED_REFERENCE_RECORD,
                    ACTOR_BASE_RECORD,
                    STATIC_RECORD,
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
    meshes_role = "meshes"
    meshes_archive_path = archive_by_role[meshes_role]
    section4_transition = dict(character_selection["section4Transition"])
    _bind_cg00_package_animations(
        section4_transition,
        BsaArchive(meshes_archive_path),
        next(str(row["sha256"]) for row in archives if row["role"] == meshes_role),
    )
    character_selection["section4Transition"] = section4_transition
    voices_role = "voices"
    post_stage65_dialogue = dict(character_selection["postStage65Dialogue"])
    _bind_owned_dad_dialogue_audio(
        post_stage65_dialogue,
        BsaArchive(archive_by_role[voices_role]),
        next(str(row["sha256"]) for row in archives if row["role"] == voices_role),
        profile_root,
    )
    character_selection["postStage65Dialogue"] = post_stage65_dialogue
    post_stage85_dialogue = dict(character_selection["postStage85Dialogue"])
    _bind_owned_dad_dialogue_audio(
        post_stage85_dialogue,
        BsaArchive(archive_by_role[voices_role]),
        next(str(row["sha256"]) for row in archives if row["role"] == voices_role),
        profile_root,
    )
    character_selection["postStage85Dialogue"] = post_stage85_dialogue
    cg01_transition = dict(character_selection["cg01Stage0Transition"])
    post_stage5_transition = dict(cg01_transition["postStage5Transition"])
    cg01_dad_dialogue = dict(post_stage5_transition["dialogue"])
    _bind_owned_dad_dialogue_audio(
        cg01_dad_dialogue,
        BsaArchive(archive_by_role[voices_role]),
        next(str(row["sha256"]) for row in archives if row["role"] == voices_role),
        profile_root,
    )
    post_stage5_transition["dialogue"] = cg01_dad_dialogue
    cg01_transition["postStage5Transition"] = post_stage5_transition
    character_selection["cg01Stage0Transition"] = cg01_transition
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
    )
    character_selection["appearance"] = appearance_contract
    stage80_transition = dict(character_selection["stage80Transition"])
    _bind_cg00_package_animations(
        stage80_transition,
        BsaArchive(meshes_archive_path),
        next(str(row["sha256"]) for row in archives if row["role"] == meshes_role),
        "addedPlayerPackage",
    )
    character_selection["stage80Transition"] = stage80_transition
    stage90_transition = dict(character_selection["stage90Transition"])
    sound_role = "sound"
    _bind_stage90_sound(
        stage90_transition,
        BsaArchive(archive_by_role[sound_role]),
        next(str(row["sha256"]) for row in archives if row["role"] == sound_role),
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
    transition_videos = [
        prepared_transition_video if row is transition_source else row
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
            "vault101BirthGraphCompiled": True,
            "runtimeBootReady": True,
        },
        "blockers": [
            "fo3-cg01-post-stage-10-toddler-world-interaction-not-implemented",
            "fo3-opening-command-interpreter-after-cg00-not-implemented",
            "fo3-vault101-godot-scene-not-compiled",
        ],
    }
    output = profile_root.resolve() / "fallout3-profile.json"
    atomic_json(output, manifest)
    return {"output": str(output), "manifest": manifest}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--profile-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    args = parser.parse_args()
    try:
        result = prepare_profile(
            args.data_root.resolve(),
            args.profile_root.resolve(),
            args.recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_FO3_PROFILE_ERROR {error}", file=sys.stderr)
        return 2
    manifest = result["manifest"]
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
