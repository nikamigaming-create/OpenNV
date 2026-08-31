#!/usr/bin/env python3
"""Compile the owned Fallout 2 Temple of Trials source graph.

The emitted JSON is asset-free: it contains authored numeric placement data and
hash-bound identities, never DAT2 member payloads or decoded images.
"""

from __future__ import annotations

import argparse
from dataclasses import asdict
import hashlib
import json
import re
import struct
import sys
from pathlib import Path
from typing import Any

from corpus_io import atomic_json
from fo1_frm import decode_frm
from fo1_map_objects import (
    FO1_MAP_OBJECTS_FORMAT_CONTRACT_HEX_01000000,
    OBJECT_TYPE_NAMES,
    TYPE_DIRECTORIES,
    Fo1ResourceResolver,
    parse_map_objects,
    parse_script_section,
)
from fo1_profile import Fo1ProfileError, map_layout_manifest, parse_map_layout
from plugin_stack import file_sha256


SCHEMA = "opennv-fo2-first-slice/v2"
RECIPE_SCHEMA = "opennv-fo2-first-slice-recipe/v2"
PROFILE_SCHEMA = "opennv-fo2-owned-profile/v1"
FORM_ID_RADIX = 16
FRM_PALETTE_SIZE = 256
MAP_WIDTH_TILES = 200
CONFRONTATION_SCHEMA = "opennv-fo2-temple-confrontation/v1"
CONFRONTATION_RECIPE_SCHEMA = "opennv-fo2-temple-confrontation-recipe/v1"
MESSAGE_ROW = re.compile(r"^\{(-?[0-9]+)\}\{[^}]*\}\{(.*)\}$")
CRITTER_PRO_SUPPORTED_SIZES = frozenset({0x19C, 0x1A0})
CRITTER_PRO_HEADER_OFFSET = 0x20
CRITTER_PRO_HEADER_FIELD_COUNT = 3
CRITTER_PRO_BASE_STATS_OFFSET = 0x30
CRITTER_PRO_BONUS_STATS_OFFSET = 0xBC
CRITTER_PRO_STAT_COUNT = 35
CRITTER_STAT_STRENGTH = 0
CRITTER_STAT_PERCEPTION = 1
CRITTER_STAT_ENDURANCE = 2
CRITTER_STAT_CHARISMA = 3
CRITTER_STAT_INTELLIGENCE = 4
CRITTER_STAT_AGILITY = 5
CRITTER_STAT_LUCK = 6
CRITTER_STAT_HIT_POINTS = 7
CRITTER_STAT_ACTION_POINTS = 8
CRITTER_STAT_ARMOR_CLASS = 9
CRITTER_STAT_UNARMED_DAMAGE = 10
CRITTER_STAT_MELEE_DAMAGE = 11
CRITTER_STAT_SEQUENCE = 13
CRITTER_STAT_CRITICAL_CHANCE = 15
CRITTER_OBJECT_TYPE = 1
CRITTER_INSTANCE_VALUE_COUNT = 11
CRITTER_INSTANCE_CURRENT_AP = 3
CRITTER_INSTANCE_AI_PACKET = 5
CRITTER_INSTANCE_TEAM = 6
CRITTER_INSTANCE_CURRENT_HP = 8
WEAPON_PRO_SIZE = 122
WEAPON_PRO_SUBTYPE_OFFSET = 0x20
WEAPON_PRO_SUBTYPE = 3
WEAPON_PRO_OBJECT_TYPE_SHIFT = 24
WEAPON_PRO_VALUES_OFFSET = 0x39
WEAPON_PRO_VALUE_COUNT = 16
WEAPON_PRO_SOUND_CODE_OFFSET = 0x79


def default_recipe_path() -> Path:
    recipes = Path(__file__).resolve().parents[1] / "recipes"
    matches = []
    for path in recipes.glob("*.json"):
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if document.get("schema") == RECIPE_SCHEMA:
            matches.append(path)
    if len(matches) != 1:
        raise Fo1ProfileError(f"Expected one Fallout 2 first-slice recipe, found {len(matches)}")
    return matches[0]


def _load_json(path: Path) -> dict[str, Any]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise Fo1ProfileError(f"JSON document is not an object: {path}")
    return document


def _load_recipe(path: Path) -> dict[str, Any]:
    recipe = _load_json(path)
    if recipe.get("schema") != RECIPE_SCHEMA or recipe.get("id") != path.stem:
        raise Fo1ProfileError(f"unexpected Fallout 2 first-slice recipe: {path}")
    if recipe.get("campaign") != "Fallout2" or recipe.get("sourceProfileSchema") != PROFILE_SCHEMA:
        raise Fo1ProfileError("Fallout 2 first-slice recipe identity changed")
    overlay = recipe.get("overlayOrderHighToLow")
    if overlay != ["patch000.dat", "critter.dat", "master.dat"]:
        raise Fo1ProfileError("Fallout 2 DAT2 overlay order changed")
    if not isinstance(recipe.get("unsupported"), list) or not recipe["unsupported"]:
        raise Fo1ProfileError("Fallout 2 first-slice unsupported boundary is missing")
    confrontation = recipe.get("boundedConfrontation")
    if (
        not isinstance(confrontation, dict)
        or confrontation.get("schema") != CONFRONTATION_RECIPE_SCHEMA
        or not isinstance(confrontation.get("critter"), dict)
        or not isinstance(confrontation.get("loot"), dict)
        or not isinstance(confrontation.get("messageCatalogs"), dict)
        or not isinstance(confrontation.get("guardianScript"), dict)
    ):
        raise Fo1ProfileError("Fallout 2 bounded confrontation recipe is incomplete")
    return recipe


def _parse_message_catalog(data: bytes) -> dict[int, str]:
    try:
        text = data.decode("cp1252")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("Fallout 2 prototype message catalog is not cp1252") from error
    result: dict[int, str] = {}
    for source_line in text.replace("\r\n", "\n").replace("\r", "\n").split("\n"):
        line = source_line.strip()
        if not line or line.startswith("#"):
            continue
        match = MESSAGE_ROW.match(line)
        if match is None:
            raise Fo1ProfileError(f"unsupported Fallout 2 MSG row: {line!r}")
        number = int(match.group(1))
        if number in result:
            raise Fo1ProfileError(f"duplicate Fallout 2 MSG number: {number}")
        result[number] = match.group(2)
    if not result:
        raise Fo1ProfileError("Fallout 2 prototype message catalog is empty")
    return result


def _script_entries(data: bytes) -> list[str]:
    try:
        lines = data.decode("cp1252").replace("\r\n", "\n").replace("\r", "\n").split("\n")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("Fallout 2 scripts.lst is not cp1252") from error
    entries = []
    for line in lines:
        program = line.split(";", 1)[0].strip()
        if program:
            entries.append(program)
    if not entries:
        raise Fo1ProfileError("Fallout 2 scripts.lst has no program entries")
    return entries


def _compile_guardian_script(
    resolver: Fo1ResourceResolver,
    configured: dict[str, Any],
    critter_script_index: int,
) -> dict[str, Any]:
    program_rule = dict(configured["program"])
    catalog_rule = dict(configured["messageCatalog"])
    program_index = int(program_rule["scriptsListIndex"])
    scripts_list = resolver.read("scripts\\scripts.lst")
    entries = _script_entries(scripts_list.data)
    if (
        program_index != critter_script_index
        or program_index < 0
        or program_index >= len(entries)
        or entries[program_index].casefold() != "acklint.int"
    ):
        raise Fo1ProfileError("Fallout 2 guardian script-list identity drifted")
    program = resolver.read(str(program_rule["logicalPath"]))
    if (
        program.logical_path.casefold() != "scripts\\acklint.int"
        or program.sha256 != str(program_rule["sha256"]).casefold()
    ):
        raise Fo1ProfileError("Fallout 2 guardian INT identity drifted")
    message_catalog = resolver.read(str(catalog_rule["logicalPath"]))
    if (
        message_catalog.logical_path.casefold() != "text\\english\\dialog\\acklint.msg"
        or message_catalog.sha256 != str(catalog_rule["sha256"]).casefold()
        or int(catalog_rule["messageListId"]) != 751
    ):
        raise Fo1ProfileError("Fallout 2 guardian MSG identity drifted")
    messages = _parse_message_catalog(message_catalog.data)

    terminal = str(configured["terminalNode"])
    nodes = list(configured["nodes"])
    node_ids = [str(node["id"]) for node in nodes]
    if (
        str(configured["initialNode"]) != "Node001"
        or terminal != "Node999"
        or len(nodes) != 5
        or len(set(node_ids)) != len(node_ids)
        or set(node_ids) != {"Node001", "Node002", "Node003", "Node004", "Node005"}
        or sorted(str(value).casefold() for value in configured["preTrialPlayerArtFids"])
        != ["0100003d", "0100003e"]
    ):
        raise Fo1ProfileError("Fallout 2 guardian dialogue node identity drifted")

    emitted_nodes = []
    referenced_messages: set[int] = set()
    for node in nodes:
        reply = []
        for segment in node["reply"]:
            if set(segment) == {"messageId"}:
                message_id = int(segment["messageId"])
                text = messages.get(message_id, "")
                if not text:
                    raise Fo1ProfileError(
                        f"Fallout 2 guardian reply message is absent: {message_id}"
                    )
                referenced_messages.add(message_id)
                reply.append({"messageId": message_id, "text": text})
            elif segment == {"playerName": True}:
                reply.append({"playerName": True})
            else:
                raise Fo1ProfileError("Fallout 2 guardian reply segment is unsupported")
        options = []
        for option in node["options"]:
            message_id = int(option["messageId"])
            target = str(option["target"])
            minimum = option.get("minimumIntelligence")
            maximum = option.get("maximumIntelligence")
            text = messages.get(message_id, "")
            if (
                not text
                or target not in {*node_ids, terminal}
                or (minimum is None) == (maximum is None)
                or minimum is not None and int(minimum) != 4
                or maximum is not None and int(maximum) != 3
                or int(option["reaction"]) != 50
            ):
                raise Fo1ProfileError(
                    f"Fallout 2 guardian option contract drifted: {message_id}"
                )
            referenced_messages.add(message_id)
            options.append(
                {
                    "messageId": message_id,
                    "text": text,
                    "target": target,
                    "minimumIntelligence": None if minimum is None else int(minimum),
                    "maximumIntelligence": None if maximum is None else int(maximum),
                    "reaction": 50,
                }
            )
        emitted_nodes.append({"id": str(node["id"]), "reply": reply, "options": options})
    if referenced_messages != set(range(103, 121)):
        raise Fo1ProfileError("Fallout 2 guardian dialogue message coverage drifted")

    hostility = dict(configured["hostilityTrigger"])
    pickup = dict(hostility["pickupProcedure"])
    critter = dict(hostility["critterProcedure"])
    if (
        pickup != {"requiresSourcePlayer": True, "localVariable": 5, "setValue": 2}
        or critter
        != {
            "localVariable": 5,
            "requiredValue": 2,
            "requiresCanSeePlayer": True,
            "setValueBeforeAttack": 1,
            "attackPlayer": True,
        }
    ):
        raise Fo1ProfileError("Fallout 2 guardian hostility trigger drifted")
    result = {
        "schema": "opennv-fo2-acklint-guardian-script/v1",
        "authority": "hash-bound owned ACKlint.int control-flow audit plus owned ACKlint.msg rows",
        "program": {
            "scriptsListIndex": program_index,
            "scriptsListLogicalPath": scripts_list.logical_path,
            "scriptsListSha256": scripts_list.sha256,
            "logicalPath": program.logical_path,
            "source": program.source,
            "bytes": len(program.data),
            "sha256": program.sha256,
        },
        "messageCatalog": {
            "messageListId": 751,
            "logicalPath": message_catalog.logical_path,
            "source": message_catalog.source,
            "bytes": len(message_catalog.data),
            "sha256": message_catalog.sha256,
        },
        "preTrialPlayerArtFids": list(configured["preTrialPlayerArtFids"]),
        "initialNode": "Node001",
        "terminalNode": terminal,
        "nodes": emitted_nodes,
        "hostilityTrigger": hostility,
        "effectProgram": {
            "schema": "opennv-classic-script-effects/v1",
            "events": {
                "pickup_proc": [{
                    "all": [{"operation": "source-is-player"}],
                    "then": [{
                        "operation": "set-local",
                        "index": int(pickup["localVariable"]),
                        "value": int(pickup["setValue"]),
                    }],
                }],
                "critter_proc": [{
                    "all": [
                        {
                            "operation": "local-equals",
                            "index": int(critter["localVariable"]),
                            "value": int(critter["requiredValue"]),
                        },
                        {"operation": "can-see-player"},
                    ],
                    "then": [
                        {
                            "operation": "set-local",
                            "index": int(critter["localVariable"]),
                            "value": int(critter["setValueBeforeAttack"]),
                        },
                        {"operation": "set-flag", "flag": "attack-player-requested"},
                    ],
                }],
            },
        },
        "implementedBoundary": {
            "dialogueNodes": True,
            "pickupToAttackTransition": True,
            "generalIntExecution": False,
        },
    }
    result["contractSha256"] = hashlib.sha256(
        json.dumps(result, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    return result


def _parse_critter_pro(data: bytes) -> dict[str, int]:
    if len(data) not in CRITTER_PRO_SUPPORTED_SIZES:
        raise Fo1ProfileError(f"unsupported Fallout 2 critter PRO size: 0x{len(data):x}")
    base = struct.unpack_from(
        f">{CRITTER_PRO_STAT_COUNT}i", data, CRITTER_PRO_BASE_STATS_OFFSET
    )
    bonus = struct.unpack_from(
        f">{CRITTER_PRO_STAT_COUNT}i", data, CRITTER_PRO_BONUS_STATS_OFFSET
    )
    stats = [
        base[index] + bonus[index] for index in range(CRITTER_PRO_STAT_COUNT)
    ]
    head_fid, ai_packet, team = struct.unpack_from(
        f">{CRITTER_PRO_HEADER_FIELD_COUNT}i", data, CRITTER_PRO_HEADER_OFFSET
    )
    return {
        "headFid": head_fid,
        "aiPacket": ai_packet,
        "team": team,
        "strength": stats[CRITTER_STAT_STRENGTH],
        "perception": stats[CRITTER_STAT_PERCEPTION],
        "endurance": stats[CRITTER_STAT_ENDURANCE],
        "charisma": stats[CRITTER_STAT_CHARISMA],
        "intelligence": stats[CRITTER_STAT_INTELLIGENCE],
        "agility": stats[CRITTER_STAT_AGILITY],
        "luck": stats[CRITTER_STAT_LUCK],
        "hitPoints": stats[CRITTER_STAT_HIT_POINTS],
        "actionPoints": stats[CRITTER_STAT_ACTION_POINTS],
        "armorClass": stats[CRITTER_STAT_ARMOR_CLASS],
        "unarmedDamage": stats[CRITTER_STAT_UNARMED_DAMAGE],
        "meleeDamage": stats[CRITTER_STAT_MELEE_DAMAGE],
        "sequence": stats[CRITTER_STAT_SEQUENCE],
        "criticalChance": stats[CRITTER_STAT_CRITICAL_CHANCE],
    }


def _parse_weapon_pro(data: bytes) -> dict[str, int | str]:
    if len(data) != WEAPON_PRO_SIZE:
        raise Fo1ProfileError(f"unsupported Fallout 2 weapon PRO size: 0x{len(data):x}")
    pid = struct.unpack_from(">I", data, 0)[0]
    subtype = struct.unpack_from(">i", data, WEAPON_PRO_SUBTYPE_OFFSET)[0]
    if pid >> WEAPON_PRO_OBJECT_TYPE_SHIFT != 0 or subtype != WEAPON_PRO_SUBTYPE:
        raise Fo1ProfileError(f"Fallout 2 confrontation loot is not a weapon: {pid:08x}")
    values = struct.unpack_from(
        f">{WEAPON_PRO_VALUE_COUNT}i", data, WEAPON_PRO_VALUES_OFFSET
    )
    fields = (
        "animationCode",
        "minimumDamage",
        "maximumDamage",
        "damageType",
        "maximumRangePrimary",
        "maximumRangeSecondary",
        "projectilePid",
        "minimumStrength",
        "actionPointCostPrimary",
        "actionPointCostSecondary",
        "criticalFailureType",
        "perk",
        "roundsPerAttack",
        "caliber",
        "ammunitionPid",
        "ammunitionCapacity",
    )
    return {
        "pid": f"{pid:08x}",
        **dict(zip(fields, values)),
        "soundCode": data[WEAPON_PRO_SOUND_CODE_OFFSET],
    }


def _compile_bounded_confrontation(
    flat_objects: list[dict[str, Any]],
    resolver: Fo1ResourceResolver,
    configured: dict[str, Any],
) -> dict[str, Any]:
    critter_rule = dict(configured["critter"])
    loot_rule = dict(configured["loot"])
    matches = [row for row in flat_objects if row["serial"] == int(critter_rule["serial"])]
    if len(matches) != 1:
        raise Fo1ProfileError("Fallout 2 bounded confrontation critter is ambiguous")
    critter = matches[0]
    prototype = critter["prototype"]
    expected_critter = {
        "tile": int(critter_rule["tile"]),
        "pid": str(critter_rule["pid"]).casefold(),
        "sid": str(critter_rule["sid"]).casefold(),
        "prototypeSha256": str(critter_rule["prototypeSha256"]).casefold(),
    }
    actual_critter = {
        "tile": int(critter["tile"]),
        "pid": str(critter["pid"]).casefold(),
        "sid": str(critter["sid"]).casefold(),
        "prototypeSha256": str(prototype["sha256"]).casefold(),
    }
    if actual_critter != expected_critter or prototype["object_type"] != CRITTER_OBJECT_TYPE:
        raise Fo1ProfileError("Fallout 2 bounded confrontation critter identity drifted")
    inventory_matches = [
        row
        for row in critter["inventory"]
        if row["object"]["serial"] == int(loot_rule["serial"])
    ]
    if len(inventory_matches) != 1:
        raise Fo1ProfileError("Fallout 2 bounded confrontation loot is ambiguous")
    inventory = inventory_matches[0]
    loot = inventory["object"]
    loot_prototype = loot["prototype"]
    if (
        loot["pid"].casefold() != str(loot_rule["pid"]).casefold()
        or inventory["quantity"] != int(loot_rule["quantity"])
        or loot_prototype["sha256"].casefold()
        != str(loot_rule["prototypeSha256"]).casefold()
        or loot_prototype["subtype_name"] != "weapon"
    ):
        raise Fo1ProfileError("Fallout 2 bounded confrontation loot identity drifted")

    critter_pro_path = f"proto\\critters\\{prototype['filename']}".casefold()
    loot_pro_path = f"proto\\items\\{loot_prototype['filename']}".casefold()
    critter_pro = resolver.read(critter_pro_path)
    loot_pro = resolver.read(loot_pro_path)
    critter_stats = _parse_critter_pro(critter_pro.data)
    weapon_stats = _parse_weapon_pro(loot_pro.data)
    if (
        len(critter["instanceValues"]) != CRITTER_INSTANCE_VALUE_COUNT
        or critter["instanceValues"][CRITTER_INSTANCE_CURRENT_HP] <= 0
        or critter["instanceValues"][CRITTER_INSTANCE_CURRENT_AP] < 0
        or critter_stats["hitPoints"] <= 0
        or critter_stats["actionPoints"] <= 0
        or weapon_stats["minimumDamage"] <= 0
        or weapon_stats["maximumDamage"] < weapon_stats["minimumDamage"]
        or weapon_stats["actionPointCostPrimary"] <= 0
    ):
        raise Fo1ProfileError("Fallout 2 bounded confrontation gameplay values are invalid")
    catalogs = dict(configured["messageCatalogs"])
    critter_messages_resource = resolver.read(str(catalogs["critter"]))
    item_messages_resource = resolver.read(str(catalogs["item"]))
    critter_messages = _parse_message_catalog(critter_messages_resource.data)
    item_messages = _parse_message_catalog(item_messages_resource.data)
    critter_name = critter_messages.get(int(prototype["message_number"]), "").strip()
    loot_name = item_messages.get(int(loot_prototype["message_number"]), "").strip()
    if not critter_name or not loot_name:
        raise Fo1ProfileError("Fallout 2 bounded confrontation display name is absent")
    guardian_script = _compile_guardian_script(
        resolver,
        dict(configured["guardianScript"]),
        int(critter["scriptIndex"]),
    )
    return {
        "schema": CONFRONTATION_SCHEMA,
        "authority": "owned MAP object/inventory graph plus hash-bound PRO and MSG records",
        "critter": {
            "serial": critter["serial"],
            "objectId": critter["id"],
            "tile": critter["tile"],
            "elevation": critter["elevation"],
            "rotation": critter["rotation"],
            "fid": critter["fid"],
            "pid": critter["pid"],
            "sid": critter["sid"],
            "scriptIndex": critter["scriptIndex"],
            "displayName": critter_name,
            "currentHitPoints": critter["instanceValues"][CRITTER_INSTANCE_CURRENT_HP],
            "currentActionPoints": critter["instanceValues"][CRITTER_INSTANCE_CURRENT_AP],
            "runtimeAiPacket": critter["instanceValues"][CRITTER_INSTANCE_AI_PACKET],
            "runtimeTeam": critter["instanceValues"][CRITTER_INSTANCE_TEAM],
            "prototype": {
                "logicalPath": critter_pro.logical_path,
                "source": critter_pro.source,
                "sha256": critter_pro.sha256,
                "messageNumber": prototype["message_number"],
                "stats": critter_stats,
            },
            "messageCatalog": {
                "logicalPath": critter_messages_resource.logical_path,
                "source": critter_messages_resource.source,
                "sha256": critter_messages_resource.sha256,
            },
        },
        "defeatLoot": {
            "serial": loot["serial"],
            "quantity": inventory["quantity"],
            "fid": loot["fid"],
            "pid": loot["pid"],
            "displayName": loot_name,
            "prototype": {
                "logicalPath": loot_pro.logical_path,
                "source": loot_pro.source,
                "sha256": loot_pro.sha256,
                "messageNumber": loot_prototype["message_number"],
                "weapon": weapon_stats,
            },
            "messageCatalog": {
                "logicalPath": item_messages_resource.logical_path,
                "source": item_messages_resource.source,
                "sha256": item_messages_resource.sha256,
            },
        },
        "guardianScript": guardian_script,
        "scriptBoundary": {
            "sid": critter["sid"],
            "scriptIndex": critter["scriptIndex"],
            "executed": False,
            "boundedDialogueExecuted": True,
            "reason": (
                "ACKlint dialogue nodes 001-005 are implemented from a hash-bound owned "
                "control-flow audit; general INT execution and engine combat remain outside "
                "this bounded adapter"
            ),
        },
    }


def _archive_paths(profile: dict[str, Any], recipe: dict[str, Any]) -> list[Path]:
    if (
        profile.get("schema") != PROFILE_SCHEMA
        or profile.get("campaign") != "Fallout2"
        or profile.get("status") != "registered-owned-install"
    ):
        raise Fo1ProfileError("Fallout 2 owned profile is not a registered source profile")
    install = profile.get("install")
    if not isinstance(install, dict):
        raise Fo1ProfileError("Fallout 2 owned profile has no install binding")
    root = Path(str(install.get("root", ""))).resolve()
    rows = install.get("archives")
    if not root.is_dir() or not isinstance(rows, list):
        raise Fo1ProfileError("Fallout 2 owned install binding is unavailable")
    by_name = {str(row.get("file", "")).casefold(): row for row in rows if isinstance(row, dict)}
    if set(by_name) != {"master.dat", "critter.dat", "patch000.dat"}:
        raise Fo1ProfileError("Fallout 2 owned profile archive set changed")
    resolved = []
    for name in recipe["overlayOrderHighToLow"]:
        row = by_name[name.casefold()]
        source = Path(str(row.get("source", ""))).resolve()
        if source.parent != root or source.name.casefold() != name.casefold() or not source.is_file():
            raise Fo1ProfileError(f"Fallout 2 archive binding escapes the registered root: {name}")
        expected_bytes = row.get("bytes")
        if source.stat().st_size != expected_bytes:
            raise Fo1ProfileError(f"Fallout 2 archive byte size drift: {name}")
        expected_hash = str(row.get("sha256", "")).casefold()
        actual_hash = file_sha256(source)
        if actual_hash != expected_hash:
            raise Fo1ProfileError(
                f"Fallout 2 archive SHA-256 drift for {name}: expected {expected_hash}, got {actual_hash}"
            )
        resolved.append(source)
    return resolved


def _maps_section(data: bytes, section_name: str) -> dict[str, str]:
    try:
        text = data.decode("cp1252")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("Fallout 2 maps.txt is not cp1252") from error
    wanted = section_name.casefold()
    active = False
    found = False
    values: dict[str, str] = {}
    for raw_line in text.replace("\r\n", "\n").replace("\r", "\n").split("\n"):
        line = raw_line.strip()
        if not line or line.startswith(("#", ";")):
            continue
        if line.startswith("[") and line.endswith("]"):
            active = line[1:-1].strip().casefold() == wanted
            found = found or active
            continue
        if active and "=" in line:
            key, value = line.split("=", 1)
            normalized = key.strip().casefold()
            if normalized in values:
                raise Fo1ProfileError(f"duplicate maps.txt key in [{section_name}]: {key.strip()}")
            values[normalized] = value.strip()
    if not found:
        raise Fo1ProfileError(f"Fallout 2 maps.txt section is missing: [{section_name}]")
    return values


def _flatten_objects(objects: dict[str, Any]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []

    def add(obj: dict[str, Any]) -> None:
        rows.append(obj)
        for inventory in obj["inventory"]:
            add(inventory["object"])

    for elevation in objects["elevations"]:
        for obj in elevation["objects"]:
            add(obj)
    return rows


def _frm_structure(decoded: dict[str, object]) -> dict[str, object]:
    return {
        "version": decoded["version"],
        "storedFps": decoded["storedFps"],
        "fps": decoded["fps"],
        "actionFrame": decoded["actionFrame"],
        "framesPerDirection": decoded["framesPerDirection"],
        "frameAreaSize": decoded["frameAreaSize"],
        "directions": [
            {
                "rotation": direction["rotation"],
                "xOffset": direction["xOffset"],
                "yOffset": direction["yOffset"],
                "dataOffset": direction["dataOffset"],
                "frames": [
                    {
                        "index": frame["index"],
                        "width": frame["width"],
                        "height": frame["height"],
                        "x": frame["x"],
                        "y": frame["y"],
                    }
                    for frame in direction["frames"]
                ],
            }
            for direction in decoded["directions"]
        ],
    }


def compile_fo2_first_slice(
    profile_path: Path,
    recipe_path: Path | None = None,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    recipe_path = (recipe_path or default_recipe_path()).resolve()
    profile = _load_json(profile_path)
    recipe = _load_recipe(recipe_path)
    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])

    with resolver.access_scope() as accessed:
        registry_resource = resolver.read(recipe["mapRegistry"]["logicalPath"])
        registry_values = _maps_section(registry_resource.data, recipe["mapRegistry"]["section"])
        expected_registry = {
            "lookup_name": recipe["mapRegistry"]["lookupName"],
            "map_name": recipe["mapRegistry"]["mapName"],
        }
        for key, expected in expected_registry.items():
            if registry_values.get(key) != expected:
                raise Fo1ProfileError(
                    f"Fallout 2 maps.txt {key} drift: expected {expected!r}, got {registry_values.get(key)!r}"
                )

        map_resource = resolver.read(recipe["map"]["logicalPath"])
        if map_resource.sha256 != recipe["map"]["sha256"]:
            raise Fo1ProfileError("Fallout 2 Temple MAP SHA-256 drift")
        layout = parse_map_layout(map_resource.data)
        header = asdict(layout.header)
        if header != recipe["map"]["header"]:
            raise Fo1ProfileError(f"Fallout 2 Temple MAP header drift: {header}")
        if [row.elevation for row in layout.elevations] != recipe["map"]["presentElevations"]:
            raise Fo1ProfileError("Fallout 2 Temple MAP elevation presence drift")
        scripts, objects_offset = parse_script_section(map_resource.data, layout.next_offset)
        objects, end_offset = parse_map_objects(
            map_resource.data,
            objects_offset,
            layout.header.version,
            resolver,
        )
        if end_offset != len(map_resource.data):
            raise Fo1ProfileError(
                f"Fallout 2 Temple object graph leaves {len(map_resource.data) - end_offset} trailing bytes"
            )

        flat_objects = _flatten_objects(objects)
        bounded_confrontation = _compile_bounded_confrontation(
            flat_objects,
            resolver,
            dict(recipe["boundedConfrontation"]),
        )
        prototypes: dict[str, dict[str, Any]] = {}
        frm_placements: dict[str, list[dict[str, Any]]] = {}
        for obj in flat_objects:
            prototype = obj["prototype"]
            pid = obj["pid"]
            if prototype["filename"] is not None:
                directory = TYPE_DIRECTORIES[prototype["object_type"]]
                prototypes.setdefault(
                    pid,
                    {
                        **prototype,
                        "logicalPath": f"proto\\{directory}\\{prototype['filename']}".casefold(),
                        "objectTypeName": OBJECT_TYPE_NAMES[prototype["object_type"]],
                        "placedObjectSerials": [],
                    },
                )["placedObjectSerials"].append(obj["serial"])
            fid = int(obj["fid"], FORM_ID_RADIX)
            frm_path = resolver.placed_idle_frm_path(fid)
            frm_placements.setdefault(frm_path, []).append(
                {
                    "serial": obj["serial"],
                    "fid": obj["fid"],
                    "frame": obj["frame"],
                    "rotation": obj["rotation"],
                    "elevation": obj["elevation"],
                    "tile": obj["tile"],
                }
            )

        palette = [(0, 0, 0, 0)] * FRM_PALETTE_SIZE
        frms = []
        for logical_path in sorted(frm_placements):
            resource = resolver.read(logical_path)
            frms.append(
                {
                    "logicalPath": logical_path,
                    "source": resource.source,
                    "bytes": len(resource.data),
                    "sha256": resource.sha256,
                    "structure": _frm_structure(decode_frm(resource.data, palette)),
                    "placements": frm_placements[logical_path],
                }
            )

    return {
        "schema": SCHEMA,
        "status": "transported-source-manifest",
        "campaign": "Fallout2",
        "slice": "TempleOfTrials",
        "declaredRole": recipe["declaredRole"],
        "sourceProfile": {
            "file": str(profile_path),
            "sourceProfileId": profile["sourceProfileId"],
            "saveCompatibilityId": profile["saveCompatibilityId"],
            "sha256": file_sha256(profile_path),
        },
        "recipe": {
            "file": str(recipe_path),
            "id": recipe["id"],
            "sha256": file_sha256(recipe_path),
        },
        "overlayOrderHighToLow": recipe["overlayOrderHighToLow"],
        "mapRegistry": {
            "logicalPath": registry_resource.logical_path,
            "source": registry_resource.source,
            "bytes": len(registry_resource.data),
            "sha256": registry_resource.sha256,
            "section": recipe["mapRegistry"]["section"],
            "values": registry_values,
        },
        "newGameStart": {
            "mapIndex": layout.header.mapIndex,
            "lookupName": registry_values["lookup_name"],
            "mapName": registry_values["map_name"],
            "playerEntry": {
                "source": "MAP header",
                "tile": layout.header.enteringTile,
                "tileX": layout.header.enteringTile % MAP_WIDTH_TILES,
                "tileY": layout.header.enteringTile // MAP_WIDTH_TILES,
                "elevation": layout.header.enteringElevation,
                "rotation": layout.header.enteringRotation,
                "placedPlayerObject": any(
                    int(obj["pid"], FORM_ID_RADIX)
                    == FO1_MAP_OBJECTS_FORMAT_CONTRACT_HEX_01000000
                    for obj in flat_objects
                ),
            },
            "selectionAuthority": "declared recipe role; executable-owned new-game selection is not transported",
        },
        "map": {
            "logicalPath": map_resource.logical_path,
            "source": map_resource.source,
            "bytes": len(map_resource.data),
            "sha256": map_resource.sha256,
            "header": header,
            "layout": map_layout_manifest(layout),
            "scriptLists": scripts,
            "objectsOffset": objects_offset,
            "endOffset": end_offset,
            "objects": objects,
            "allObjectCount": len(flat_objects),
        },
        "prototypes": [prototypes[pid] for pid in sorted(prototypes)],
        "frms": frms,
        "boundedConfrontation": bounded_confrontation,
        "resources": [
            {
                "logicalPath": resolver.resources[path].logical_path,
                "source": resolver.resources[path].source,
                "bytes": len(resolver.resources[path].data),
                "sha256": resolver.resources[path].sha256,
            }
            for path in sorted(accessed)
        ],
        "promotion": {
            "transported": True,
            "rendered": False,
            "interactive": False,
            "parityReviewed": False,
            "headsetAccepted": False,
        },
        "runtimeCompatibility": {
            "ready": False,
            "presentations": {
                "hex-tactical": False,
                "first-person": False,
                "openxr": False,
            },
            "firstSliceBlocker": (
                "The exact Temple MAP/PRO/FRM source graph and one bounded confrontation are "
                "transported, but general script execution and campaign gameplay remain absent."
            ),
        },
        "nextRuntimeOwner": (
            "The bounded Godot Temple confrontation owner consumes this manifest; general MAP "
            "scripts, actors, combat, quests, inventory, and campaign state remain separate work."
        ),
        "unsupported": recipe["unsupported"],
        "retailOrDerivedAssetsPackaged": False,
        "generatedCaches": [],
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Compile an asset-free Fallout 2 Temple of Trials source manifest."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=None)
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        profile = _load_json(args.profile.resolve())
        install_root = Path(str(profile.get("install", {}).get("root", ""))).resolve()
        if output.is_relative_to(install_root):
            raise Fo1ProfileError("Fallout 2 first-slice output must be outside the owned install")
        document = compile_fo2_first_slice(args.profile, args.recipe)
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_FO2_FIRST_SLICE_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_FIRST_SLICE "
        + json.dumps(
            {
                "manifest": str(output),
                "map": document["map"]["logicalPath"],
                "topLevelObjects": document["map"]["objects"]["totalTopLevelObjects"],
                "allObjects": document["map"]["allObjectCount"],
                "prototypes": len(document["prototypes"]),
                "frms": len(document["frms"]),
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
