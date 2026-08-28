#!/usr/bin/env python3
"""Register an existing local JAM installation without loading native DLLs."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

from corpus_io import atomic_json
from plugin_records import iter_plugin_records, iter_subrecords, read_plugin_masters, zstring
from plugin_stack import file_sha256


SCHEMA = "opennv-jam-profile/v1"
REQUIREMENTS_SCHEMA = "opennv-jam-profile-requirements/v1"
PROFILE_ID_HEX_CHARACTERS = 20
PERCENT_SCALE = 100.0
PROFILE_ID_PREFIX = (SCHEMA + "\0").encode("ascii")
PLUGIN_SCRIPT_SIGNATURE = "SCPT"
PLUGIN_GLOBAL_SIGNATURE = "GLOB"
SCRIPT_COMMAND_TOKEN = re.compile(r"\b[A-Za-z_][A-Za-z0-9_]*\b")
DISPATCH_EVENT = re.compile(r'\bDispatchEvent\s+"([^"]+)"', re.IGNORECASE)


def default_requirements_path() -> Path:
    recipes = Path(__file__).resolve().parents[1] / "recipes"
    matches = []
    for path in recipes.glob("*.json"):
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if document.get("schema") == REQUIREMENTS_SCHEMA:
            matches.append(path)
    if len(matches) != 1:
        raise ValueError(f"Expected one JAM requirements recipe, found {len(matches)}")
    return matches[0]


def _normalize_data_root(path: Path) -> Path:
    resolved = path.resolve()
    if not resolved.is_dir():
        raise FileNotFoundError(f"JAM data layer does not exist: {resolved}")
    nested = _case_insensitive_directory(resolved, "Data")
    return nested if nested is not None else resolved


def _case_insensitive_directory(root: Path, name: str) -> Path | None:
    matches = [
        child
        for child in root.iterdir()
        if child.is_dir() and child.name.casefold() == name.casefold()
    ]
    if len(matches) > 1:
        raise ValueError(f"Ambiguous case-insensitive directory in {root}: {name}")
    return matches[0] if matches else None


def normalize_data_roots(paths: list[Path]) -> tuple[Path, ...]:
    roots = tuple(_normalize_data_root(path) for path in paths)
    folded = [str(root).casefold() for root in roots]
    if len(set(folded)) != len(folded):
        raise ValueError("JAM profile contains a duplicate data layer")
    return roots


def _relative_file(root: Path, logical_path: str) -> Path | None:
    current = root
    parts = Path(logical_path.replace("\\", "/")).parts
    if not parts or any(part in {"", ".", ".."} for part in parts):
        raise ValueError(f"Invalid JAM requirement path: {logical_path!r}")
    for index, part in enumerate(parts):
        matches = [
            child
            for child in current.iterdir()
            if child.name.casefold() == part.casefold()
        ]
        if len(matches) > 1:
            raise ValueError(
                f"Ambiguous case-insensitive JAM path in {current}: {part}"
            )
        if not matches:
            return None
        current = matches[0]
        if index < len(parts) - 1 and not current.is_dir():
            return None
    return current if current.is_file() else None


def _required_file(root: Path, logical_path: str) -> Path:
    path = _relative_file(root, logical_path)
    if path is None:
        raise FileNotFoundError(f"Required JAM dependency is absent: {logical_path}")
    if path.stat().st_size == 0:
        raise ValueError(f"Required JAM dependency is empty: {path}")
    return path


def _effective_file(
    roots: tuple[Path, ...], logical_path: str
) -> tuple[int, Path]:
    winner = None
    for root_index, root in enumerate(roots):
        path = _relative_file(root, logical_path)
        if path is not None:
            winner = (root_index, path)
    if winner is None:
        raise FileNotFoundError(
            f"Required JAM data file is absent from all layers: {logical_path}"
        )
    if winner[1].stat().st_size == 0:
        raise ValueError(f"Required JAM data file is empty: {winner[1]}")
    return winner


def _file_row(
    path: Path,
    logical_path: str,
    component: str,
    source_root_index: int | None = None,
) -> dict[str, object]:
    row: dict[str, object] = {
        "component": component,
        "logicalPath": logical_path.replace("\\", "/"),
        "source": str(path.resolve()),
        "bytes": path.stat().st_size,
        "sha256": file_sha256(path),
    }
    if source_root_index is not None:
        row["sourceRootIndex"] = source_root_index
    return row


def load_requirements(path: Path | None = None) -> dict[str, object]:
    path = (path or default_requirements_path()).resolve()
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != REQUIREMENTS_SCHEMA:
        raise ValueError(f"Unexpected JAM requirements schema: {path}")
    if document.get("id") != path.stem:
        raise ValueError(f"JAM requirements id does not match its file name: {path}")
    if not isinstance(document.get("jamPlugin"), str) or not isinstance(
        document.get("baseGamePlugin"), str
    ):
        raise ValueError(f"JAM requirements have no base/JAM plugin markers: {path}")
    for key in ("requiredDlcPlugins", "gameRootFiles", "dataFiles", "unsupportedSemantics"):
        if not isinstance(document.get(key), list) or not document[key]:
            raise ValueError(f"JAM requirements have no {key}: {path}")
    for key in ("gameRootFiles", "dataFiles"):
        rows = document[key]
        if not all(
            isinstance(row, dict)
            and isinstance(row.get("component"), str)
            and isinstance(row.get("path"), str)
            for row in rows
        ):
            raise ValueError(f"JAM requirements contain an invalid {key} row: {path}")
        paths = [str(row["path"]).casefold() for row in rows]
        if len(paths) != len(set(paths)):
            raise ValueError(f"JAM requirements contain duplicate {key} paths: {path}")
    semantics = document["unsupportedSemantics"]
    if not all(isinstance(value, str) and value for value in semantics):
        raise ValueError(f"JAM requirements contain invalid unsupported semantics: {path}")
    if len(semantics) != len(set(semantics)):
        raise ValueError(f"JAM requirements contain duplicate unsupported semantics: {path}")
    modules = document.get("scriptModules")
    if not isinstance(modules, list) or not modules or not all(
        isinstance(row, dict)
        and isinstance(row.get("id"), str)
        and isinstance(row.get("scriptPrefix"), str)
        for row in modules
    ):
        raise ValueError(f"JAM requirements contain invalid script modules: {path}")
    providers = document.get("knownScriptCommands")
    if not isinstance(providers, dict) or set(providers) != {"xnvse", "jip-ln"}:
        raise ValueError(f"JAM requirements contain invalid command providers: {path}")
    for provider, commands in providers.items():
        if not isinstance(commands, list) or not commands or not all(
            isinstance(command, str) and command for command in commands
        ):
            raise ValueError(f"JAM requirements contain invalid {provider} commands: {path}")
        folded = [command.casefold() for command in commands]
        if len(folded) != len(set(folded)):
            raise ValueError(f"JAM requirements contain duplicate {provider} commands: {path}")
    capabilities = document.get("portableCapabilities")
    if not isinstance(capabilities, list) or not capabilities:
        raise ValueError(f"JAM requirements contain no portable capabilities: {path}")
    return document


def _script_inventory(
    plugin_path: Path,
    requirements: dict[str, object],
) -> tuple[list[dict[str, object]], list[dict[str, object]], dict[str, float]]:
    providers = {
        name: {command.casefold(): command for command in commands}
        for name, commands in dict(requirements["knownScriptCommands"]).items()
    }
    configured_modules = [dict(row) for row in requirements["scriptModules"]]
    module_rows = {
        str(row["id"]): {
            "id": str(row["id"]),
            "scriptPrefix": str(row["scriptPrefix"]),
            "scripts": [],
            "commands": {"xnvse": set(), "jip-ln": set()},
            "dispatchedEvents": set(),
        }
        for row in configured_modules
    }
    scripts: list[dict[str, object]] = []
    globals_by_editor_id: dict[str, float] = {}
    signatures = frozenset({PLUGIN_SCRIPT_SIGNATURE, PLUGIN_GLOBAL_SIGNATURE})
    for record in iter_plugin_records(plugin_path, signatures):
        values = {subrecord.signature: subrecord.data for subrecord in iter_subrecords(record)}
        editor_id = zstring(values.get("EDID", b""))
        if record.signature == PLUGIN_GLOBAL_SIGNATURE:
            raw_value = values.get("FLTV")
            if editor_id and raw_value is not None and len(raw_value) == 4:
                globals_by_editor_id[editor_id] = struct.unpack("<f", raw_value)[0]
            continue
        source = values.get("SCTX")
        if not editor_id or source is None:
            continue
        try:
            source_text = source.decode("cp1252")
        except UnicodeDecodeError as error:
            raise ValueError(f"JAM script source is not cp1252: {editor_id}") from error
        module = next(
            (
                row
                for row in module_rows.values()
                if editor_id.casefold().startswith(str(row["scriptPrefix"]).casefold())
            ),
            None,
        )
        if module is None:
            raise ValueError(f"JAM script does not belong to a declared module: {editor_id}")
        executable_source = "\n".join(
            line.split(";", 1)[0] for line in source_text.splitlines()
        )
        tokens = {
            token.casefold()
            for token in SCRIPT_COMMAND_TOKEN.findall(executable_source)
        }
        for provider, commands in providers.items():
            module["commands"][provider].update(
                commands[token] for token in tokens.intersection(commands)
            )
        events = DISPATCH_EVENT.findall(executable_source)
        module["dispatchedEvents"].update(events)
        row = {
            "editorId": editor_id,
            "formId": f"{record.form_id:08x}",
            "sourceBytes": len(source),
            "sourceSha256": hashlib.sha256(source).hexdigest(),
            "module": module["id"],
        }
        scripts.append(row)
        module["scripts"].append(editor_id)

    scripts.sort(key=lambda row: str(row["editorId"]).casefold())
    emitted_modules = []
    for module in module_rows.values():
        emitted_modules.append(
            {
                "id": module["id"],
                "scriptPrefix": module["scriptPrefix"],
                "scripts": sorted(module["scripts"], key=str.casefold),
                "commands": {
                    provider: sorted(commands, key=str.casefold)
                    for provider, commands in module["commands"].items()
                },
                "dispatchedEvents": sorted(module["dispatchedEvents"], key=str.casefold),
            }
        )
    return scripts, emitted_modules, globals_by_editor_id


def _portable_capabilities(
    requirements: dict[str, object],
    scripts: list[dict[str, object]],
    modules: list[dict[str, object]],
    globals_by_editor_id: dict[str, float],
) -> list[dict[str, object]]:
    script_by_editor_id = {str(row["editorId"]): row for row in scripts}
    module_by_id = {str(row["id"]): row for row in modules}
    result = []
    for raw in requirements["portableCapabilities"]:
        capability = dict(raw)
        missing_scripts = [
            editor_id
            for editor_id in capability["sourceScripts"]
            if editor_id not in script_by_editor_id
        ]
        missing_globals = [
            editor_id
            for editor_id in capability["requiredGlobals"]
            if editor_id not in globals_by_editor_id
        ]
        module = module_by_id.get(str(capability["module"]))
        if module is None:
            raise ValueError(f"JAM portable capability has no module: {capability['id']}")
        missing_xnvse = sorted(
            set(capability["requiredXnvseCommands"]) - set(module["commands"]["xnvse"]),
            key=str.casefold,
        )
        missing_jip = sorted(
            set(capability["requiredJipCommands"]) - set(module["commands"]["jip-ln"]),
            key=str.casefold,
        )
        missing_events = sorted(
            set(capability["dispatchedEvents"]) - set(module["dispatchedEvents"]),
            key=str.casefold,
        )
        blockers = [
            *(f"missing-script:{value}" for value in missing_scripts),
            *(f"missing-global:{value}" for value in missing_globals),
            *(f"missing-xnvse-command:{value}" for value in missing_xnvse),
            *(f"missing-jip-command:{value}" for value in missing_jip),
            *(f"missing-dispatched-event:{value}" for value in missing_events),
        ]
        if blockers:
            result.append({"id": capability["id"], "status": "blocked", "blockers": blockers})
            continue
        source_globals = {
            editor_id: globals_by_editor_id[editor_id]
            for editor_id in capability["requiredGlobals"]
        }
        direct_input_key = int(source_globals["JVSKey"])
        key_map = dict(capability["desktopDirectInputKeyMap"])
        desktop_key = key_map.get(str(direct_input_key))
        if desktop_key is None:
            raise ValueError(f"JVS DirectInput key has no portable mapping: {direct_input_key}")
        speed_percent = source_globals["JVSSpeedMult"]
        result.append(
            {
                "id": capability["id"],
                "status": "transported-bounded-runtime-capability",
                "module": capability["module"],
                "sourceScripts": [
                    script_by_editor_id[editor_id] for editor_id in capability["sourceScripts"]
                ],
                "sourceGlobals": source_globals,
                "commandContracts": {
                    "xnvse": capability["requiredXnvseCommands"],
                    "jip-ln": capability["requiredJipCommands"],
                    "dispatchedEvents": capability["dispatchedEvents"],
                },
                "runtime": {
                    "enabled": source_globals["JVSEnabled"] == 1,
                    "desktopPhysicalKey": desktop_key,
                    "controllerButton": int(source_globals["JVSButton"]),
                    "toggle": source_globals["JVSToggle"] != 0,
                    "speedBonusPercent": speed_percent,
                    "speedMultiplier": 1.0 + speed_percent / PERCENT_SCALE,
                },
                "supportedSemantics": capability["supportedSemantics"],
                "unsupportedSemantics": capability["unsupportedSemantics"],
            }
        )
    return result


def inspect_jam_profile(
    game_root_path: Path,
    data_root_paths: list[Path],
    declared_version: str | None = None,
    requirements_path: Path | None = None,
) -> dict[str, object]:
    """Hash-bind one effective local JAM/MO2 profile and its native prerequisites."""

    game_root = game_root_path.resolve()
    if not game_root.is_dir():
        raise FileNotFoundError(f"Fallout New Vegas game root does not exist: {game_root}")
    roots = normalize_data_roots(data_root_paths)
    requirements_path = (requirements_path or default_requirements_path()).resolve()
    requirements = load_requirements(requirements_path)

    game_rows = []
    missing_dependencies = []
    for raw in requirements["gameRootFiles"]:
        requirement = dict(raw)
        logical_path = str(requirement["path"])
        source = _relative_file(game_root, logical_path)
        if source is None:
            missing_dependencies.append(
                {
                    "component": str(requirement["component"]),
                    "logicalPath": logical_path.replace("\\", "/"),
                    "scope": "game-root",
                }
            )
            continue
        if source.stat().st_size == 0:
            raise ValueError(f"Required JAM dependency is empty: {source}")
        game_rows.append(
            _file_row(source, logical_path, str(requirement["component"]))
        )

    data_requirements = [
        {"component": "fallout-new-vegas", "path": str(requirements["baseGamePlugin"])},
        {"component": "jam", "path": str(requirements["jamPlugin"])},
        *(
            {"component": "fallout-new-vegas-dlc", "path": str(plugin)}
            for plugin in requirements["requiredDlcPlugins"]
        ),
        *(dict(raw) for raw in requirements["dataFiles"]),
    ]
    data_rows = []
    for requirement in data_requirements:
        logical_path = str(requirement["path"])
        winner = None
        for source_root_index, root in enumerate(roots):
            source = _relative_file(root, logical_path)
            if source is not None:
                winner = (source_root_index, source)
        if winner is None:
            missing_dependencies.append(
                {
                    "component": str(requirement["component"]),
                    "logicalPath": logical_path.replace("\\", "/"),
                    "scope": "effective-data",
                }
            )
            continue
        source_root_index, source = winner
        if source.stat().st_size == 0:
            raise ValueError(f"Required JAM data file is empty: {source}")
        data_rows.append(
            _file_row(
                source,
                logical_path,
                str(requirement["component"]),
                source_root_index,
            )
        )

    jam_plugin = next(
        (row for row in data_rows if row["component"] == "jam"),
        None,
    )
    if jam_plugin is None:
        raise FileNotFoundError(
            "JustAssortedMods.esp is absent from the registered data layers"
        )
    jam_masters = list(read_plugin_masters(Path(str(jam_plugin["source"]))))
    present_plugins = {
        str(row["logicalPath"]).casefold()
        for row in data_rows
        if Path(str(row["logicalPath"])).suffix.casefold() in {".esm", ".esp"}
    }
    missing_masters = [
        master for master in jam_masters if master.casefold() not in present_plugins
    ]

    scripts, modules, globals_by_editor_id = _script_inventory(
        Path(str(jam_plugin["source"])),
        requirements,
    )
    portable_capabilities = _portable_capabilities(
        requirements,
        scripts,
        modules,
        globals_by_editor_id,
    )

    identity = {
        "present": [
            (row["component"], row["logicalPath"], row["sha256"])
            for row in [*game_rows, *data_rows]
        ],
        "missing": missing_dependencies,
        "missingMasters": missing_masters,
    }
    encoded_identity = json.dumps(
        identity, sort_keys=True, separators=(",", ":")
    ).encode("utf-8")
    profile_id = hashlib.sha256(PROFILE_ID_PREFIX + encoded_identity).hexdigest()[
        :PROFILE_ID_HEX_CHARACTERS
    ]
    unsupported = list(requirements["unsupportedSemantics"])
    dependency_ready = not missing_dependencies and not missing_masters
    transported_capabilities = [
        str(capability["id"])
        for capability in portable_capabilities
        if capability["status"] == "transported-bounded-runtime-capability"
    ]
    return {
        "schema": SCHEMA,
        "status": (
            "validated-local-dependency-profile"
            if dependency_ready
            else "incomplete-local-dependency-profile"
        ),
        "kind": "jam",
        "profileId": profile_id,
        "saveCompatibilityId": f"fallout-new-vegas+jam:{profile_id}",
        "declaredJamVersion": declared_version,
        "requirements": {
            "id": requirements["id"],
            "file": str(requirements_path),
            "sha256": file_sha256(requirements_path),
        },
        "gameRoot": str(game_root),
        "sourceRoots": [str(root) for root in roots],
        "files": {
            "gameRoot": game_rows,
            "effectiveData": data_rows,
        },
        "missingDependencies": missing_dependencies,
        "missingPluginMasters": missing_masters,
        "jamPlugin": {
            "file": jam_plugin["logicalPath"],
            "masters": jam_masters,
            "sha256": jam_plugin["sha256"],
        },
        "scriptInventory": {
            "scripts": scripts,
            "modules": modules,
        },
        "portableCapabilities": portable_capabilities,
        "runtimeCompatibility": {
            "ready": False,
            "nativeDllLoading": False,
            "transportedCapabilities": transported_capabilities,
            "unsupportedSemantics": unsupported,
            "reason": (
                "The JAM profile is missing required local packages or plugin masters; "
                "the bounded transported capabilities do not make the complete mod ready."
                if not dependency_ready
                else
                "The installed JAM dependency set is present and hash-bound, but only the "
                "listed bounded capabilities are transported; the complete xNVSE/JAM "
                "runtime remains unsupported."
            ),
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Register a local JAM/MO2 profile without copying assets or loading DLLs."
        )
    )
    parser.add_argument(
        "--game-root",
        type=Path,
        required=True,
        help="Fallout New Vegas folder containing the installed xNVSE root files.",
    )
    parser.add_argument(
        "--data-root",
        action="append",
        type=Path,
        required=True,
        help=(
            "Effective Data/MO2 mod layer in low-to-high precedence order; repeat "
            "for the base Data folder and dependency/JAM mod folders."
        ),
    )
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--jam-version")
    parser.add_argument("--requirements", type=Path, default=None)
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        game_root = args.game_root.resolve()
        data_roots = normalize_data_roots(args.data_root)
        if output.is_relative_to(game_root) or any(
            output.is_relative_to(root) for root in data_roots
        ):
            raise ValueError("JAM profile output must be outside every admitted input root")
        document = inspect_jam_profile(
            game_root,
            list(data_roots),
            args.jam_version,
            args.requirements,
        )
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_JAM_PROFILE_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_JAM_PROFILE "
        + json.dumps(
            {
                "manifest": str(output),
                "profileId": document["profileId"],
                "files": sum(len(rows) for rows in document["files"].values()),
                "runtimeReady": document["runtimeCompatibility"]["ready"],
                "unsupportedSemantics": len(
                    document["runtimeCompatibility"]["unsupportedSemantics"]
                ),
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
