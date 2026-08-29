#!/usr/bin/env python3
"""Compile the bounded TTW Fallout 3 opening command surface.

This is deliberately not the standalone Fallout 3 profile producer.  It reads
the validated TTW base-plus-generated plugin stack, resolves effective records
by stable FormKey, and emits a separate local contract for CG00 through the
synchronously nested CG01 stage-5 result.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import struct
import sys
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

from cell_catalog import cell_parent_form_id
from corpus_io import atomic_json
from plugin_records import Record, iter_plugin_records, iter_subrecords, read_plugin_masters, zstring
from plugin_stack import (
    PluginContext,
    file_sha256,
    load_order_indices,
    parse_form_key,
    runtime_form_id,
)
from ttw_profile import (
    SCHEMA as TTW_PROFILE_SCHEMA,
    load_requirements,
    plugin_stack_id,
    read_active_load_order,
)
from ttw_source_namespace import (
    RESOLUTION_POLICY as SOURCE_NAMESPACE_RESOLUTION_POLICY,
    SCHEMA as SOURCE_NAMESPACE_SCHEMA,
    STATUS as SOURCE_NAMESPACE_STATUS,
)


SCHEMA = "opennv-ttw-fo3-opening-profile/v1"
RECIPE_SCHEMA = "opennv-ttw-fo3-opening-recipe/v1"
DEFAULT_RECIPE = Path(__file__).resolve().parents[1] / "recipes" / "ttw-fo3-opening-profile-v1.json"
PROFILE_STATUS = "validated-generated-plugin-profile"
DELETED_RECORD_FLAG = 0x00000020
ADMITTED_SIGNATURES = frozenset({"ACHR", "CELL", "IMAD", "NPC_", "QUST", "REFR", "SCPT", "SOUN"})
SHA256_HEX_CHARACTERS = 64
NUMBER = r"[-+]?(?:\d+(?:\.\d*)?|\.\d+)"
CACHE_COMPATIBILITY_PREFIX = b"opennv-ttw-fo3-opening-cache-v1\0"
CACHE_COMPATIBILITY_NAMESPACE = "ttw-fo3-opening"


@dataclass(frozen=True)
class RecordVersion:
    context: PluginContext
    source_root_index: int
    record: Record


def _is_sha256(value: object) -> bool:
    return (
        isinstance(value, str)
        and len(value) == SHA256_HEX_CHARACTERS
        and all(character in "0123456789abcdef" for character in value)
    )


def _load_recipe(path: Path) -> dict[str, object]:
    recipe = json.loads(path.read_text(encoding="utf-8"))
    if recipe.get("schema") != RECIPE_SCHEMA or recipe.get("id") != path.stem:
        raise ValueError(f"Unexpected TTW Fallout 3 opening recipe: {path}")
    if (
        recipe.get("campaign") != "Fallout3"
        or recipe.get("edition") != "TTW"
        or recipe.get("sourceProfileSchema") != TTW_PROFILE_SCHEMA
        or not isinstance(recipe.get("forms"), dict)
        or not isinstance(recipe.get("operands"), dict)
        or not isinstance(recipe.get("commandDialects"), dict)
        or not isinstance(recipe.get("movies"), dict)
    ):
        raise ValueError(f"Incomplete TTW Fallout 3 opening recipe: {path}")
    return recipe


def _case_insensitive_file(root: Path, name: str) -> Path:
    matches = [path for path in root.iterdir() if path.is_file() and path.name.casefold() == name.casefold()]
    if len(matches) != 1:
        raise FileNotFoundError(f"Expected exactly one {name!r} in {root}, found {len(matches)}")
    return matches[0]


def _validated_stack(profile_path: Path) -> tuple[dict[str, object], tuple[Path, ...], tuple[PluginContext, ...], dict[str, int]]:
    resolved_profile = profile_path.resolve()
    profile = json.loads(resolved_profile.read_text(encoding="utf-8"))
    if (
        profile.get("schema") != TTW_PROFILE_SCHEMA
        or profile.get("status") != PROFILE_STATUS
        or profile.get("kind") != "ttw"
    ):
        raise ValueError(f"Not a validated TTW profile: {resolved_profile}")

    root_rows = profile.get("sourceRoots")
    if not isinstance(root_rows, list) or not root_rows or not all(isinstance(row, str) for row in root_rows):
        raise ValueError("TTW profile has no source roots")
    roots = tuple(Path(row).resolve() for row in root_rows)
    if len({str(root).casefold() for root in roots}) != len(roots):
        raise ValueError("TTW profile contains duplicate source roots")
    for root in roots:
        if not root.is_dir():
            raise FileNotFoundError(f"TTW profile source root is missing: {root}")

    source = profile.get("loadOrderSource")
    if not isinstance(source, dict) or not isinstance(source.get("file"), str) or not _is_sha256(source.get("sha256")):
        raise ValueError("TTW profile load-order source is invalid")
    load_order_path = Path(str(source["file"])).resolve()
    if not load_order_path.is_file() or file_sha256(load_order_path) != source["sha256"]:
        raise ValueError("TTW profile load-order source changed")
    load_order = read_active_load_order(load_order_path)

    rows = profile.get("plugins")
    if not isinstance(rows, list) or not rows or not all(isinstance(row, dict) for row in rows):
        raise ValueError("TTW profile contains no plugin rows")
    if tuple(str(row.get("file", "")) for row in rows) != load_order:
        raise ValueError("TTW profile plugin rows differ from its load-order source")
    configured_names = {str(row["file"]).casefold(): str(row["file"]) for row in rows}
    if len(configured_names) != len(rows):
        raise ValueError("TTW profile contains duplicate plugin rows")
    missing_markers = [name for name in load_requirements() if name.casefold() not in configured_names]
    if missing_markers:
        raise ValueError("TTW profile is missing required generated plugins: " + ", ".join(missing_markers))

    contexts: list[PluginContext] = []
    validated_rows: list[dict[str, object]] = []
    for expected_index, row in enumerate(rows):
        name = str(row["file"])
        root_index = row.get("sourceRootIndex")
        if not isinstance(root_index, int) or not 0 <= root_index < len(roots):
            raise ValueError(f"TTW plugin has an invalid source root: {name}")
        if row.get("loadOrderIndex") != expected_index:
            raise ValueError(f"TTW plugin load-order index changed: {name}")
        winners = []
        for index, root in enumerate(roots):
            matches = [path for path in root.iterdir() if path.is_file() and path.name.casefold() == name.casefold()]
            if len(matches) > 1:
                raise ValueError(f"TTW root contains duplicate case-insensitive plugin files: {name}")
            if matches:
                winners.append((index, matches[0]))
        if not winners or winners[-1][0] != root_index:
            raise ValueError(f"TTW plugin effective source changed: {name}")
        path = winners[-1][1]
        if path.stat().st_size != row.get("bytes") or file_sha256(path) != row.get("sha256"):
            raise ValueError(f"TTW plugin bytes or hash changed: {name}")
        declared_masters = read_plugin_masters(path)
        masters = tuple(configured_names.get(master.casefold(), "") for master in declared_masters)
        recorded_masters = row.get("masters")
        if (
            any(not master for master in masters)
            or not isinstance(recorded_masters, list)
            or [master.casefold() for master in masters]
            != [str(master).casefold() for master in recorded_masters]
        ):
            raise ValueError(f"TTW plugin master list changed: {name}")
        if any(load_order.index(master) >= expected_index for master in masters):
            raise ValueError(f"TTW plugin master order changed: {name}")
        context = PluginContext(
            name=name,
            path=path,
            load_order_index=expected_index,
            masters=masters,
            namespaces=(*masters, name),
            sha256=str(row["sha256"]),
            bytes=int(row["bytes"]),
        )
        contexts.append(context)
        validated_rows.append(
            {
                "file": name,
                "loadOrderIndex": expected_index,
                "sourceRootIndex": root_index,
                "bytes": context.bytes,
                "sha256": context.sha256,
                "masters": list(recorded_masters),
            }
        )
    expected_stack_id = plugin_stack_id(validated_rows)
    if profile.get("pluginStackId") != expected_stack_id:
        raise ValueError("TTW plugin-stack identity changed")
    if profile.get("saveCompatibilityId") != f"ttw:{expected_stack_id}":
        raise ValueError("TTW save-compatibility identity changed")
    return profile, roots, tuple(contexts), {name.casefold(): index for index, name in enumerate(load_order)}


def _validated_source_namespace(
    namespace_path: Path,
    profile_path: Path,
    profile: dict[str, object],
) -> dict[str, object]:
    resolved_namespace = namespace_path.resolve()
    namespace = json.loads(resolved_namespace.read_text(encoding="utf-8"))
    if (
        namespace.get("schema") != SOURCE_NAMESPACE_SCHEMA
        or namespace.get("status") != SOURCE_NAMESPACE_STATUS
        or namespace.get("resolutionPolicy") != SOURCE_NAMESPACE_RESOLUTION_POLICY
    ):
        raise ValueError(f"Not a validated TTW effective-source namespace: {resolved_namespace}")

    source = namespace.get("sourceProfile")
    if not isinstance(source, dict):
        raise ValueError("TTW effective-source namespace has no source-profile binding")
    resolved_profile = profile_path.resolve()
    if (
        not isinstance(source.get("file"), str)
        or Path(str(source["file"])).resolve() != resolved_profile
        or source.get("sha256") != file_sha256(resolved_profile)
        or source.get("pluginStackId") != profile.get("pluginStackId")
        or source.get("saveCompatibilityId") != profile.get("saveCompatibilityId")
    ):
        raise ValueError("TTW effective-source namespace profile binding differs")

    expected_roots = [str(Path(row).resolve()) for row in profile["sourceRoots"]]
    if namespace.get("sourceRoots") != expected_roots:
        raise ValueError("TTW effective-source namespace roots differ from its profile")
    if namespace.get("plugins") != profile.get("plugins"):
        raise ValueError("TTW effective-source namespace plugin stack differs from its profile")
    compatibility = namespace.get("runtimeCompatibility")
    if not isinstance(compatibility, dict) or compatibility.get("ready") is not False:
        raise ValueError("TTW effective-source namespace overstates runtime compatibility")
    return namespace


def _cache_compatibility_id(document: dict[str, object]) -> str:
    payload = {
        "schema": document["schema"],
        "sourceProfile": document["sourceProfile"],
        "sourceNamespace": document["sourceNamespace"],
        "recipe": document["recipe"],
        "forms": document["forms"],
        "operands": document["operands"],
        "stages": document["stages"],
        "movies": document["movies"],
    }
    encoded = json.dumps(
        payload,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=False,
    ).encode("utf-8")
    digest = hashlib.sha256(CACHE_COMPATIBILITY_PREFIX + encoded).hexdigest()
    return f"{CACHE_COMPATIBILITY_NAMESPACE}:{digest}"


def _editor_id(record: Record) -> str | None:
    values = [zstring(row.data) for row in iter_subrecords(record) if row.signature == "EDID"]
    if len(values) > 1:
        raise ValueError(f"{record.signature} {record.form_id:08x} has duplicate EDID values")
    return values[0] if values else None


def _single_subrecord(record: Record, signature: str) -> bytes:
    values = [row.data for row in iter_subrecords(record) if row.signature == signature]
    if len(values) != 1:
        raise ValueError(f"{record.signature} {record.form_id:08x} has {len(values)} {signature} values")
    return values[0]


def _stage_sources(record: Record) -> dict[int, str]:
    stages: dict[int, list[str]] = {}
    current: int | None = None
    for row in iter_subrecords(record):
        if row.signature == "INDX":
            if len(row.data) not in {2, 4}:
                raise ValueError(f"QUST {record.form_id:08x} has an invalid INDX size")
            current = int.from_bytes(row.data, "little")
        elif row.signature == "SCTX" and current is not None:
            stages.setdefault(current, []).append(zstring(row.data))
    ambiguous = [stage for stage, values in stages.items() if len(values) != 1]
    if ambiguous:
        raise ValueError("TTW quest has ambiguous stage result sources: " + ", ".join(str(stage) for stage in ambiguous))
    return {stage: values[0] for stage, values in stages.items()}


def _source_commands(source: str) -> list[str]:
    return [command for line in source.splitlines() if (command := line.split(";", 1)[0].strip())]


def _number(value: str) -> int | float:
    parsed = float(value)
    if not math.isfinite(parsed):
        raise ValueError(f"Non-finite command number: {value}")
    return int(parsed) if parsed.is_integer() else parsed


def _match(pattern: str, text: str) -> re.Match[str] | None:
    return re.fullmatch(pattern, text, re.IGNORECASE)


def parse_command(text: str) -> dict[str, object]:
    """Parse only the command dialect admitted by the bounded TTW recipe."""

    if match := _match(r'playbink\s+"(?P<path>[^"]+)"\s+(?P<args>(?:\d+\s+){3}\d+)', text):
        return {"kind": "playBink", "logicalPath": match.group("path"), "arguments": [int(value) for value in match.group("args").split()]}
    if match := _match(r"setlocationspecificloadscreensonly\s+(?P<value>\d+)", text):
        return {"kind": "setLocationSpecificLoadScreensOnly", "value": int(match.group("value"))}
    if match := _match(r"setinchargen\s+(?P<value>\d+)", text):
        return {"kind": "setInCharGen", "value": int(match.group("value"))}
    if match := _match(r"(?P<subject>[A-Za-z0-9_]+)\.moveto\s+(?P<target>[A-Za-z0-9_]+)", text):
        return {"kind": "moveToReference", "subject": match.group("subject"), "target": match.group("target")}
    if match := _match(r"setstage\s+(?P<quest>[A-Za-z0-9_]+)\s+(?P<stage>\d+)", text):
        return {"kind": "setStage", "questEditorId": match.group("quest"), "stage": int(match.group("stage"))}
    if match := _match(rf"setnumericgamesetting\s+(?P<setting>[A-Za-z0-9_]+)\s+(?P<value>{NUMBER})", text):
        return {"kind": "setNumericGameSetting", "setting": match.group("setting"), "value": _number(match.group("value"))}
    if _match(r"ttw_showgeneprojector", text):
        return {"kind": "showTtwGeneProjector"}
    if match := _match(rf"set\s+(?P<subject>[A-Za-z0-9_]+)\.(?P<variable>[A-Za-z0-9_]+)\s+to\s+(?P<value>{NUMBER})", text):
        return {"kind": "setScriptVariable", "subject": match.group("subject"), "variable": match.group("variable"), "value": _number(match.group("value"))}
    if match := _match(r"(?P<subject>[A-Za-z0-9_]+)\.removescriptpackage", text):
        return {"kind": "removeScriptPackage", "subject": match.group("subject")}
    if match := _match(r"rimod\s+(?P<modifier>[A-Za-z0-9_]+)", text):
        return {"kind": "removeImageSpaceModifier", "modifierEditorId": match.group("modifier")}
    if match := _match(r"(?P<subject>[A-Za-z0-9_]+)\.(?P<command>enable|disable)", text):
        return {"kind": match.group("command").casefold(), "subject": match.group("subject")}
    if match := _match(r"stopquest\s+(?P<quest>[A-Za-z0-9_]+)", text):
        return {"kind": "stopQuest", "questEditorId": match.group("quest")}
    if match := _match(r"setpcyoung\s+(?P<value>\d+)", text):
        return {"kind": "setPlayerYoung", "value": int(match.group("value"))}
    if match := _match(r'setsoundsourcefile\s+(?P<sound>[A-Za-z0-9_]+)\s+"(?P<path>[^"]+)"', text):
        return {"kind": "setSoundSourceFile", "soundEditorId": match.group("sound"), "logicalPath": match.group("path")}
    if match := _match(rf"(?P<subject>[A-Za-z0-9_]+)\.setscale\s+(?P<value>{NUMBER})", text):
        if match.group("subject").casefold() != "player":
            raise ValueError(f"Unsupported TTW scale subject: {text}")
        return {"kind": "setPlayerScale", "value": _number(match.group("value"))}
    if match := _match(r"(?P<command>enableplayercontrols|disableplayercontrols)\s+(?P<args>(?:\d+\s+)*\d+)", text):
        return {"kind": "enablePlayerControls" if match.group("command").casefold().startswith("enable") else "disablePlayerControls", "arguments": [int(value) for value in match.group("args").split()]}
    if match := _match(r"autodisplayobjectives\s+(?P<value>\d+)", text):
        return {"kind": "autoDisplayObjectives", "value": int(match.group("value"))}
    if match := _match(r"setnoactivationsound\s+(?P<sound>[A-Za-z0-9_]+)", text):
        return {"kind": "setNoActivationSound", "soundEditorId": match.group("sound")}
    if match := _match(r"setpctoddler\s+(?P<value>\d+)", text):
        return {"kind": "setPlayerToddler", "value": int(match.group("value"))}
    raise ValueError(f"Unsupported TTW Fallout 3 opening command: {text}")


def parse_stage(source: str, expected_kinds: list[str], label: str) -> list[dict[str, object]]:
    commands = [parse_command(text) for text in _source_commands(source)]
    kinds = [str(command["kind"]) for command in commands]
    if kinds != expected_kinds:
        raise ValueError(f"TTW {label} command dialect differs: expected {expected_kinds}, found {kinds}")
    return commands


class EffectiveRecords:
    def __init__(self, contexts: tuple[PluginContext, ...], source_root_indices: dict[str, int], indices: dict[str, int]):
        self.contexts = contexts
        self.source_root_indices = source_root_indices
        self.indices = indices
        self.versions: dict[str, list[RecordVersion]] = {}
        for context in contexts:
            local: set[str] = set()
            for record in iter_plugin_records(context.path, ADMITTED_SIGNATURES):
                key = context.form_key(record.form_id)
                folded = key.text.casefold()
                if folded in local:
                    raise ValueError(f"{context.name} contains duplicate admitted record {key.text}")
                local.add(folded)
                self.versions.setdefault(folded, []).append(
                    RecordVersion(context, source_root_indices[context.name.casefold()], record)
                )
        self.winners = {key: rows[-1] for key, rows in self.versions.items()}

    def contract(self, definition: dict[str, object], *, expected_editor_id: str | None = None) -> dict[str, object]:
        key = parse_form_key(str(definition["formKey"]))
        rows = self.versions.get(key.text.casefold())
        if not rows:
            raise ValueError(f"TTW opening FormKey is absent: {key.text}")
        winner = rows[-1]
        record = winner.record
        editor_id = _editor_id(record)
        expected_editor = expected_editor_id or definition.get("editorId")
        if record.flags & DELETED_RECORD_FLAG:
            raise ValueError(f"TTW opening FormKey is deleted: {key.text}")
        if record.signature != definition.get("recordType"):
            raise ValueError(f"TTW opening record type differs: {key.text}")
        if expected_editor is not None and (editor_id or "").casefold() != str(expected_editor).casefold():
            raise ValueError(f"TTW opening editor identity differs: {key.text}")
        expected_winner = definition.get("winnerPlugin")
        if expected_winner is not None and winner.context.name.casefold() != str(expected_winner).casefold():
            raise ValueError(f"TTW opening effective winner differs: {key.text}")
        parent_key = None
        raw_parent = cell_parent_form_id(record)
        if raw_parent is not None:
            parent_key = winner.context.form_key(raw_parent).text
        expected_parent = definition.get("parentCell")
        if expected_parent is not None and (parent_key or "").casefold() != str(expected_parent).casefold():
            raise ValueError(f"TTW opening parent CELL differs: {key.text}")
        return {
            "formKey": key.text,
            "runtimeFormId": runtime_form_id(key, self.indices),
            "recordType": record.signature,
            "editorId": editor_id,
            "parentCellFormKey": parent_key,
            "winner": self._version_row(winner),
            "overriddenVersions": [self._version_row(row) for row in rows[:-1]],
        }

    def _version_row(self, version: RecordVersion) -> dict[str, object]:
        return {
            "plugin": version.context.name,
            "loadOrderIndex": version.context.load_order_index,
            "sourceRootIndex": version.source_root_index,
            "pluginSha256": version.context.sha256,
            "recordSha256": hashlib.sha256(version.record.data).hexdigest(),
            "flags": version.record.flags,
        }

    def winner(self, form_key: str) -> RecordVersion:
        row = self.winners.get(form_key.casefold())
        if row is None:
            raise ValueError(f"TTW opening FormKey is absent: {form_key}")
        return row


def _operand_link(contract: dict[str, object]) -> dict[str, object]:
    return {
        "editorId": contract["editorId"],
        "formKey": contract["formKey"],
        "runtimeFormId": contract["runtimeFormId"],
        "winnerPlugin": dict(contract["winner"])["plugin"],
    }


def _movie_source(roots: tuple[Path, ...], logical_path: str) -> dict[str, object]:
    parts = PurePosixPath(logical_path.replace("\\", "/")).parts
    if not parts or any(part in {"", ".", ".."} for part in parts):
        raise ValueError(f"Invalid TTW movie path: {logical_path}")
    versions = []
    for root_index, root in enumerate(roots):
        current = root
        found = True
        for index, part in enumerate(parts):
            matches = [path for path in current.iterdir() if path.name.casefold() == part.casefold()]
            if len(matches) > 1:
                raise ValueError(f"Ambiguous case-insensitive TTW movie path: {logical_path}")
            if not matches:
                found = False
                break
            current = matches[0]
            if index < len(parts) - 1 and not current.is_dir():
                found = False
                break
        if found:
            if not current.is_file():
                raise FileNotFoundError(f"TTW movie source is not a file: {current}")
            versions.append(
                {
                    "sourceRootIndex": root_index,
                    "bytes": current.stat().st_size,
                    "sha256": file_sha256(current),
                }
            )
    if not versions:
        raise FileNotFoundError(f"TTW movie source is absent: {logical_path}")
    return {"logicalPath": "/".join(parts), "winner": versions[-1], "overriddenVersions": versions[:-1]}


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def compile_ttw_fo3_opening(
    profile_path: Path,
    source_namespace_path: Path,
    recipe_path: Path = DEFAULT_RECIPE,
) -> dict[str, object]:
    recipe = _load_recipe(recipe_path.resolve())
    profile, roots, contexts, indices = _validated_stack(profile_path)
    _validated_source_namespace(source_namespace_path, profile_path, profile)
    source_root_indices = {str(row["file"]).casefold(): int(row["sourceRootIndex"]) for row in profile["plugins"]}
    effective = EffectiveRecords(contexts, source_root_indices, indices)

    forms = {name: effective.contract(dict(definition)) for name, definition in dict(recipe["forms"]).items()}
    operands = {
        editor_id: effective.contract(dict(definition), expected_editor_id=editor_id)
        for editor_id, definition in dict(recipe["operands"]).items()
    }

    cg00_quest = effective.winner(str(forms["cg00Quest"]["formKey"]))
    cg01_quest = effective.winner(str(forms["cg01Quest"]["formKey"]))
    for label, quest, script_name in (
        ("CG00", cg00_quest, "cg00Script"),
        ("CG01", cg01_quest, "cg01Script"),
    ):
        raw_script = struct.unpack("<I", _single_subrecord(quest.record, "SCRI"))[0]
        script_key = quest.context.form_key(raw_script).text
        _require(script_key.casefold() == str(forms[script_name]["formKey"]).casefold(), f"TTW {label} script link differs")

    dialects = dict(recipe["commandDialects"])
    cg00_sources = _stage_sources(cg00_quest.record)
    cg01_sources = _stage_sources(cg01_quest.record)
    required_sources = ((cg00_sources, (0, 60, 100), "CG00"), (cg01_sources, (0, 5), "CG01"))
    for sources, stages, label in required_sources:
        missing = [stage for stage in stages if stage not in sources]
        if missing:
            raise ValueError(f"TTW {label} required stage sources are absent: {missing}")

    parsed = {
        "cg00Stage0": parse_stage(cg00_sources[0], list(dialects["cg00Stage0"]), "CG00 stage 0"),
        "cg00Stage60": parse_stage(cg00_sources[60], list(dialects["cg00Stage60"]), "CG00 stage 60"),
        "cg00Stage100": parse_stage(cg00_sources[100], list(dialects["cg00Stage100"]), "CG00 stage 100"),
        "cg01Stage0": parse_stage(cg01_sources[0], list(dialects["cg01Stage0"]), "CG01 stage 0"),
        "cg01Stage5": parse_stage(cg01_sources[5], list(dialects["cg01Stage5"]), "CG01 stage 5"),
    }

    def operand(editor_id: str) -> dict[str, object]:
        match = next((row for name, row in operands.items() if name.casefold() == editor_id.casefold()), None)
        if match is None:
            raise ValueError(f"TTW opening command operand is outside the recipe: {editor_id}")
        return _operand_link(match)

    def quest(editor_id: str) -> dict[str, object]:
        name = "cg00Quest" if editor_id.casefold() == "cg00" else "cg01Quest" if editor_id.casefold() == "cg01" else ""
        if not name:
            raise ValueError(f"TTW opening command quest is outside the recipe: {editor_id}")
        return _operand_link(forms[name])

    def enrich(command: dict[str, object]) -> dict[str, object]:
        result = dict(command)
        kind = str(command["kind"])
        if kind == "moveToReference":
            result["subject"] = {"role": "player"} if str(command["subject"]).casefold() == "player" else operand(str(command["subject"]))
            result["target"] = operand(str(command["target"]))
        elif kind in {"setScriptVariable", "removeScriptPackage", "enable", "disable"}:
            subject = str(command["subject"])
            if subject.casefold() == "player":
                result["subject"] = {"role": "player"}
            elif subject.casefold() in {"cg00", "cg01"}:
                result["subject"] = quest(subject)
            else:
                result["subject"] = operand(subject)
        elif kind in {"setStage", "stopQuest"}:
            result["quest"] = quest(str(command["questEditorId"]))
            del result["questEditorId"]
        elif kind == "removeImageSpaceModifier":
            result["modifier"] = operand(str(command["modifierEditorId"]))
            del result["modifierEditorId"]
        elif kind in {"setSoundSourceFile", "setNoActivationSound"}:
            result["sound"] = operand(str(command["soundEditorId"]))
            del result["soundEditorId"]
        return result

    enriched = {name: [dict(enrich(command), index=index) for index, command in enumerate(commands)] for name, commands in parsed.items()}

    c00 = parsed["cg00Stage0"]
    _require(c00[0]["logicalPath"].casefold() == "fallout intro vsk.bik" and c00[0]["arguments"] == [1, 1, 0, 1], "TTW CG00 intro movie command differs")
    _require(c00[1]["value"] == 1 and c00[2]["value"] == 1, "TTW CG00 stage-0 front-end flags differ")
    _require([c00[index]["subject"].casefold() for index in (3, 4, 5, 7)] == ["cg00dadref", "cg00doctorliref", "cg00momref", "player"], "TTW CG00 stage-0 move subjects differ")
    _require([c00[index]["target"].casefold() for index in (3, 4, 5, 7)] == ["cg00dadstartmarker", "cg00doctorlistartmarker", "cg00momstartmarker", "cg00playerstartmarker"], "TTW CG00 stage-0 move targets differ")
    _require(c00[6]["questEditorId"].casefold() == "cg00" and c00[6]["stage"] == 5, "TTW CG00 stage-0 nested stage differs")
    _require([(c00[index]["setting"], c00[index]["value"]) for index in (8, 9)] == [("fKarmaModMurderingNonEvilNPC", -100), ("fKarmaModMurderingNonEvilCreature", -25)], "TTW CG00 numeric-game-setting commands differ")
    c60 = parsed["cg00Stage60"]
    _require(c60[1]["subject"].casefold() == "cg00" and c60[1]["variable"].casefold() == "runtimer" and c60[1]["value"] == 1, "TTW CG00 gene-projector continuation differs")
    c100 = parsed["cg00Stage100"]
    _require(c100[0]["subject"].casefold() == "player", "TTW CG00 stage-100 package subject differs")
    _require([(c100[index]["subject"].casefold(), c100[index]["variable"].casefold(), c100[index]["value"]) for index in (1, 2)] == [("cg00momref", "dotalk", 0), ("cg00dadref", "dotalk", 0)], "TTW CG00 stage-100 actor variables differ")
    _require(c100[3]["modifierEditorId"].casefold() == "cg00birthbaseisfx" and c100[4]["subject"].casefold() == "cg00dadref", "TTW CG00 stage-100 presentation/actor boundary differs")
    _require(c100[5]["questEditorId"].casefold() == "cg00" and c100[6]["value"] == 1 and c100[7]["questEditorId"].casefold() == "cg01" and c100[7]["stage"] == 0, "TTW CG00-to-CG01 boundary differs")
    c10 = parsed["cg01Stage0"]
    _require(c10[0]["soundEditorId"].casefold() == "phybabyrattle" and c10[0]["logicalPath"].replace("\\", "/").casefold() == "fx/phy/babyrattle/", "TTW CG01 stage-0 sound-source override differs")
    _require(c10[1]["subject"].casefold() == "cg01dadref" and c10[1]["target"].casefold() == "cg01dadstartmarker", "TTW CG01 Dad move differs")
    _require(c10[2]["questEditorId"].casefold() == "cg01" and c10[2]["stage"] == 5 and c10[3]["value"] == 0.4 and c10[4]["subject"].casefold() == "player" and c10[4]["target"].casefold() == "cg01playerstartmarker", "TTW CG01 stage-0 nested application differs")
    c15 = parsed["cg01Stage5"]
    _require(c15[0]["value"] == 1 and c15[1]["value"] == 1, "TTW CG01 stage-5 front-end flags differ")
    _require([c15[index]["subject"].casefold() for index in (2, 3)] == ["cg01dadref", "cg02dadref"], "TTW CG01 stage-5 enabled actors differ")
    _require([(c15[index]["subject"].casefold(), c15[index]["variable"].casefold(), c15[index]["value"]) for index in (4, 5)] == [("cg01dadref", "dotalk", 1), ("cg01dadref", "talking", 0)], "TTW CG01 stage-5 Dad variables differ")
    _require(c15[6]["arguments"] == [0, 0, 0, 0, 1] and c15[7]["arguments"] == [1, 1, 1, 1, 0, 0, 1], "TTW CG01 stage-5 control masks differ")
    _require(c15[8]["value"] == 1 and c15[9]["soundEditorId"].casefold() == "qstbabybabble" and c15[10]["value"] == 1 and c15[11]["value"] == 1, "TTW CG01 stage-5 objective/sound/player-age state differs")
    _require(c15[12]["logicalPath"].casefold() == "1 year later.bik" and c15[12]["arguments"] == [0, 0, 1, 0], "TTW CG01 stage-5 movie command differs")

    movies = {name: _movie_source(roots, str(path)) for name, path in dict(recipe["movies"]).items()}
    recipe_hash = file_sha256(recipe_path.resolve())
    profile_hash = file_sha256(profile_path.resolve())
    document = {
        "schema": SCHEMA,
        "status": "transported-bounded-ttw-fo3-opening-command-contract",
        "campaign": "Fallout3",
        "edition": "TTW",
        "saveCompatibilityId": profile["saveCompatibilityId"],
        "sourceProfile": {
            "file": str(profile_path.resolve()),
            "sha256": profile_hash,
            "pluginStackId": profile["pluginStackId"],
            "saveCompatibilityId": profile["saveCompatibilityId"],
        },
        "sourceNamespace": {
            "file": str(source_namespace_path.resolve()),
            "sha256": file_sha256(source_namespace_path.resolve()),
            "schema": SOURCE_NAMESPACE_SCHEMA,
            "status": SOURCE_NAMESPACE_STATUS,
        },
        "recipe": {"file": recipe_path.resolve().name, "sha256": recipe_hash},
        "cacheBoundary": {
            "kind": "dedicated-ttw-opening-profile",
            "standaloneFallout3ProfileAccepted": False,
            "standaloneFallout3CacheReused": False,
            "standaloneNewVegasProfileAccepted": False,
            "standaloneNewVegasCacheReused": False,
        },
        "scope": {
            "effectiveRecordMerge": "admitted-closure-only-last-active-plugin-wins",
            "admittedStages": {"CG00": [0, 60, 100], "CG01": [0, 5]},
            "nestedStageApplication": "CG01-stage-0-synchronously-admits-stage-5-result",
            "openingContractReady": True,
        },
        "forms": forms,
        "operands": operands,
        "stages": {
            "CG00": {
                "quest": _operand_link(forms["cg00Quest"]),
                "script": _operand_link(forms["cg00Script"]),
                "results": {"0": enriched["cg00Stage0"], "60": enriched["cg00Stage60"], "100": enriched["cg00Stage100"]},
            },
            "CG01": {
                "quest": _operand_link(forms["cg01Quest"]),
                "script": _operand_link(forms["cg01Script"]),
                "results": {"0": enriched["cg01Stage0"], "5": enriched["cg01Stage5"]},
            },
        },
        "movies": movies,
        "runtimeCompatibility": {
            "ready": False,
            "reason": "The TTW effective CG00-to-CG01-stage-5 command contract has a dedicated state executor, but Vault 101 world and movie presentation are not connected yet.",
        },
        "unsupportedSemantics": [
            "cg00-unlisted-stage-results-and-dialogue-package-ai",
            "cg01-stage-10-and-later-gameplay",
            "ttw-vault101-cell-resource-compilation",
            "ttw-save-runtime-and-world-transition",
            "xnvse-and-jam-native-plugin-execution",
        ],
    }
    document["cacheBoundary"]["compatibilityId"] = _cache_compatibility_id(document)
    return document


def validate_ttw_fo3_opening(profile_path: Path) -> dict[str, object]:
    """Validate one compiled TTW opening contract without compiling owned records."""

    resolved = profile_path.resolve()
    document = json.loads(resolved.read_text(encoding="utf-8"))
    if (
        document.get("schema") != SCHEMA
        or document.get("status")
        != "transported-bounded-ttw-fo3-opening-command-contract"
        or document.get("campaign") != "Fallout3"
        or document.get("edition") != "TTW"
    ):
        raise ValueError(f"Not a compiled TTW Fallout 3 opening profile: {resolved}")

    source = document.get("sourceProfile")
    if not isinstance(source, dict) or not isinstance(source.get("file"), str):
        raise ValueError("TTW Fallout 3 opening profile has no source-profile binding")
    source_profile_path = Path(str(source["file"])).resolve()
    if not source_profile_path.is_file() or source.get("sha256") != file_sha256(source_profile_path):
        raise ValueError("TTW Fallout 3 opening source profile changed")
    source_profile, roots, _, _ = _validated_stack(source_profile_path)
    if (
        source.get("pluginStackId") != source_profile.get("pluginStackId")
        or source.get("saveCompatibilityId") != source_profile.get("saveCompatibilityId")
        or document.get("saveCompatibilityId") != source_profile.get("saveCompatibilityId")
    ):
        raise ValueError("TTW Fallout 3 opening save or plugin-stack identity changed")

    namespace_source = document.get("sourceNamespace")
    if not isinstance(namespace_source, dict) or not isinstance(namespace_source.get("file"), str):
        raise ValueError("TTW Fallout 3 opening profile has no effective-source binding")
    namespace_path = Path(str(namespace_source["file"])).resolve()
    if (
        not namespace_path.is_file()
        or namespace_source.get("sha256") != file_sha256(namespace_path)
        or namespace_source.get("schema") != SOURCE_NAMESPACE_SCHEMA
        or namespace_source.get("status") != SOURCE_NAMESPACE_STATUS
    ):
        raise ValueError("TTW Fallout 3 opening effective-source namespace changed")
    _validated_source_namespace(namespace_path, source_profile_path, source_profile)

    cache = document.get("cacheBoundary")
    if (
        not isinstance(cache, dict)
        or cache.get("kind") != "dedicated-ttw-opening-profile"
        or cache.get("standaloneFallout3ProfileAccepted") is not False
        or cache.get("standaloneFallout3CacheReused") is not False
        or cache.get("standaloneNewVegasProfileAccepted") is not False
        or cache.get("standaloneNewVegasCacheReused") is not False
        or cache.get("compatibilityId") != _cache_compatibility_id(document)
    ):
        raise ValueError("TTW Fallout 3 opening cache isolation changed")

    movies = document.get("movies")
    if not isinstance(movies, dict):
        raise ValueError("TTW Fallout 3 opening movie boundary is absent")
    for name, recorded in movies.items():
        if not isinstance(recorded, dict) or not isinstance(recorded.get("logicalPath"), str):
            raise ValueError(f"TTW Fallout 3 opening movie binding is invalid: {name}")
        current = _movie_source(roots, str(recorded["logicalPath"]))
        if current != recorded:
            raise ValueError(f"TTW Fallout 3 opening movie source changed: {name}")

    compatibility = document.get("runtimeCompatibility")
    if not isinstance(compatibility, dict) or compatibility.get("ready") is not False:
        raise ValueError("TTW Fallout 3 opening profile overstates runtime compatibility")
    unsupported = document.get("unsupportedSemantics")
    if not isinstance(unsupported, list) or "ttw-save-runtime-and-world-transition" not in unsupported:
        raise ValueError("TTW Fallout 3 opening unsupported save boundary is absent")
    return document


def main() -> int:
    parser = argparse.ArgumentParser(description="Compile a dedicated bounded TTW Fallout 3 CG00-to-CG01-stage-5 owned-data profile.")
    parser.add_argument("--ttw-profile", type=Path)
    parser.add_argument("--source-namespace", type=Path)
    parser.add_argument("--validate-profile", type=Path)
    parser.add_argument("--recipe", type=Path, default=DEFAULT_RECIPE)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        if args.validate_profile is not None:
            if args.ttw_profile is not None or args.source_namespace is not None or args.output is not None:
                raise ValueError("--validate-profile cannot be combined with compile inputs")
            document = validate_ttw_fo3_opening(args.validate_profile)
            print(
                "OPENNV_TTW_FO3_OPENING_VALIDATED "
                + json.dumps(
                    {
                        "manifest": str(args.validate_profile.resolve()),
                        "pluginStackId": document["sourceProfile"]["pluginStackId"],
                        "saveCompatibilityId": document["saveCompatibilityId"],
                        "cacheCompatibilityId": document["cacheBoundary"]["compatibilityId"],
                        "runtimeReady": document["runtimeCompatibility"]["ready"],
                    },
                    sort_keys=True,
                )
            )
            return 0
        if args.ttw_profile is None or args.source_namespace is None or args.output is None:
            raise ValueError("compile requires --ttw-profile, --source-namespace, and --output")
        output = args.output.resolve()
        document = compile_ttw_fo3_opening(
            args.ttw_profile,
            args.source_namespace,
            args.recipe,
        )
        roots = tuple(Path(row).resolve() for row in json.loads(args.ttw_profile.resolve().read_text(encoding="utf-8"))["sourceRoots"])
        if any(output.is_relative_to(root) for root in roots):
            raise ValueError("TTW opening-profile output must be outside every owned data root")
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
        validate_ttw_fo3_opening(output)
    except Exception as error:
        print(f"OPENNV_TTW_FO3_OPENING_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_TTW_FO3_OPENING "
        + json.dumps(
            {
                "manifest": str(output),
                "pluginStackId": document["sourceProfile"]["pluginStackId"],
                "saveCompatibilityId": document["saveCompatibilityId"],
                "cacheCompatibilityId": document["cacheBoundary"]["compatibilityId"],
                "cg00Stages": list(document["stages"]["CG00"]["results"]),
                "cg01Stages": list(document["stages"]["CG01"]["results"]),
                "runtimeReady": document["runtimeCompatibility"]["ready"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
