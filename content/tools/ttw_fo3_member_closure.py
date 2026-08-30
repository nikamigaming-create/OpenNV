"""Resolve the asset-free TTW CG00 early-birth member identity closure."""

from __future__ import annotations

import json
from pathlib import Path

from bsa_archive import canonical_member_path
from plugin_records import iter_subrecords
from prepare_fo3_profile import (
    TTW_INPUT_SIGNATURES,
    _text_values,
    default_recipe_path,
    enumerate_ttw_fo3_profile_inputs,
    load_recipe,
)
from ttw_effective_source import load_ttw_effective_source, parse_form_key
from ttw_fo3_opening import DEFAULT_RECIPE as DEFAULT_TTW_OPENING_RECIPE
from ttw_fo3_semantic_differential import (
    _closure_contracts,
    _terminal_package_change_links,
)
from ttw_profile import DEFAULT_REQUIREMENTS_PATH as DEFAULT_TTW_SOURCE_RECIPE


def _case_insensitive_directory(root: Path, parts: tuple[str, ...]) -> Path | None:
    current = root
    for part in parts:
        if not current.is_dir():
            return None
        matches = [
            path
            for path in current.iterdir()
            if path.is_dir() and path.name.casefold() == part.casefold()
        ]
        if len(matches) > 1:
            raise ValueError(
                "TTW loose member directory is ambiguous: " + "\\".join(parts)
            )
        if not matches:
            return None
        current = matches[0]
    return current


def _candidate_members(
    source: object,
    logical_prefix: str,
    logical_suffix: str = "",
) -> list[str]:
    prefix = canonical_member_path(logical_prefix).rstrip("\\")
    suffix = logical_suffix.casefold()
    candidates = {
        path
        for archive in source.members.archives
        for path in archive.archive.members
        if path.startswith(prefix + "\\") and path.casefold().endswith(suffix)
    }
    prefix_parts = tuple(prefix.replace("\\", "/").split("/"))
    for root in source.members.roots:
        directory = _case_insensitive_directory(root, prefix_parts)
        if directory is None:
            continue
        for path in directory.rglob("*"):
            if not path.is_file() or not path.name.casefold().endswith(suffix):
                continue
            relative = path.relative_to(root).as_posix()
            candidates.add(canonical_member_path(relative))
    return sorted(candidates)


def _resolve_member(source: object, logical_path: str) -> dict[str, object]:
    resolved = source.members.resolve(logical_path)
    contract = resolved.contract()
    if (
        not contract["logicalPath"]
        or not contract["sha256"]
        or not isinstance(contract["bytes"], int)
        or contract["bytes"] <= 0
        or not isinstance(contract["winner"], dict)
    ):
        raise ValueError(f"TTW member identity is incomplete: {logical_path}")
    return contract


def _idle_animation_member(source: object, idle: dict[str, object]) -> dict[str, object]:
    version = source.records.winner(str(idle["formKey"]))
    models = _text_values(version.record, "MODL")
    if len(models) != 1 or not models[0].casefold().endswith(".kf"):
        raise ValueError(f"TTW IDLE animation path is incomplete: {idle['formKey']}")
    return _resolve_member(
        source,
        canonical_member_path(f"meshes\\{models[0]}"),
    )


def _dialogue_members(
    source: object,
    closure: dict[str, object],
) -> list[dict[str, object]]:
    dialogue = dict(closure["dialogue"])
    voice_types = dict(dialogue["voiceTypes"])
    infos: dict[str, dict[str, object]] = {}
    for stage in ("stage10", "stage22Male", "stage22Female", "stage42"):
        for raw_info in dialogue[stage]:
            info = dict(raw_info)
            infos[str(info["formKey"]).casefold()] = info
    rows = []
    for info in infos.values():
        role = str(info["speakerRole"])
        voice_type = dict(voice_types[role])
        if str(info["voiceTypeFormKey"]).casefold() != str(
            voice_type["formKey"]
        ).casefold():
            raise ValueError("TTW INFO voice-type provenance differs")
        form_key = parse_form_key(str(info["formKey"]))
        namespace = canonical_member_path(
            f"sound\\voice\\{form_key.owner_plugin}\\{voice_type['editorId']}"
        )
        suffix = f"_{form_key.object_id:08x}_1.ogg"
        matches = _candidate_members(source, namespace, suffix)
        if len(matches) != 1:
            raise ValueError(
                f"TTW INFO voice member is absent or ambiguous: {info['formKey']}"
            )
        lip_path = matches[0].removesuffix(".ogg") + ".lip"
        rows.append(
            {
                "info": info,
                "voiceType": voice_type,
                "voice": _resolve_member(source, matches[0]),
                "lip": _resolve_member(source, lip_path),
            }
        )
    return rows


def _sound_members(
    source: object,
    closure: dict[str, object],
) -> list[dict[str, object]]:
    rows = []
    for raw_sound in closure["sounds"]:
        sound = dict(raw_sound)
        version = source.records.winner(str(sound["formKey"]))
        paths = _text_values(version.record, "FNAM")
        sound_data = [
            subrecord.data
            for subrecord in iter_subrecords(version.record)
            if subrecord.signature in {"SNDD", "SNDX"}
        ]
        if len(paths) != 1 or len(sound_data) != 1:
            raise ValueError(f"TTW SOUN layout is incomplete: {sound['formKey']}")
        logical_path = canonical_member_path(f"sound\\{paths[0]}")
        if Path(logical_path).suffix:
            selection_policy = "exact-file"
            candidates = [logical_path]
        else:
            selection_policy = "source-folder-variant-set"
            candidates = _candidate_members(source, logical_path)
        if not candidates:
            raise ValueError(f"TTW SOUN member closure is empty: {sound['formKey']}")
        rows.append(
            {
                "sound": sound,
                "selectionPolicy": selection_policy,
                "members": [_resolve_member(source, path) for path in candidates],
            }
        )
    return rows


def compile_ttw_fo3_cg00_member_closure(
    profile_path: Path,
    source_namespace_path: Path,
    *,
    ttw_opening_recipe_path: Path = DEFAULT_TTW_OPENING_RECIPE,
    ttw_source_recipe_path: Path = DEFAULT_TTW_SOURCE_RECIPE,
    standalone_recipe_path: Path | None = None,
) -> dict[str, object]:
    """Resolve source identities only; no owned member bytes escape this function."""

    enumeration = enumerate_ttw_fo3_profile_inputs(
        profile_path,
        source_namespace_path,
        ttw_opening_recipe_path,
        ttw_source_recipe_path,
    )
    source = load_ttw_effective_source(
        profile_path,
        source_namespace_path,
        TTW_INPUT_SIGNATURES,
        ttw_source_recipe_path,
    )
    if source.members is None:
        raise ValueError("TTW effective member index is unavailable")
    closure = dict(enumeration["cg00SceneClosure"])
    contracts = _closure_contracts(closure, dict(enumeration["records"]))
    if len(contracts) != int(closure["recordCount"]):
        raise ValueError("TTW CG00 record closure changed during member resolution")
    opening_recipe = json.loads(ttw_opening_recipe_path.read_text(encoding="utf-8"))
    schema = opening_recipe.get("memberClosureSchema")
    terminal_disposition = opening_recipe.get("terminalPackageChangeDisposition")
    if not isinstance(schema, str) or not schema or not isinstance(
        terminal_disposition, str
    ):
        raise ValueError("TTW opening recipe has no member-closure contract")

    package_animations = []
    for role, raw_sections in dict(closure["packageSections"]).items():
        for raw_section in raw_sections:
            section = dict(raw_section)
            idle = dict(section["idle"])
            package_animations.append(
                {
                    "role": str(role),
                    "section": int(section["section"]),
                    "package": section["package"],
                    "idle": idle,
                    "member": _idle_animation_member(source, idle),
                }
            )

    external_links = _terminal_package_change_links(
        source,
        closure,
        terminal_disposition,
    )
    external_animations = [
        {
            **link,
            "member": _idle_animation_member(source, dict(link["toIdle"])),
        }
        for link in external_links
    ]

    resolved_standalone_recipe = standalone_recipe_path or default_recipe_path()
    standalone_recipe = load_recipe(resolved_standalone_recipe)
    player_camera = dict(
        dict(dict(standalone_recipe["opening"])["characterSelection"])[
            "earlyBirthSequence"
        ]
    )["playerCamera"]
    camera_path = canonical_member_path(str(dict(player_camera)["skeletonLogicalPath"]))
    camera_skeleton = _resolve_member(source, camera_path)
    dialogue = _dialogue_members(source, closure)
    sounds = _sound_members(source, closure)
    members = [
        *(row["member"] for row in package_animations),
        *(row["member"] for row in external_animations),
        camera_skeleton,
        *(row["voice"] for row in dialogue),
        *(row["lip"] for row in dialogue),
        *(member for row in sounds for member in row["members"]),
    ]
    by_path = {str(member["logicalPath"]).casefold(): member for member in members}
    if len(by_path) != len(members):
        raise ValueError("TTW CG00 required member closure contains duplicate roles")

    return {
        "schema": schema,
        "status": "validated-member-identities-profile-emission-pending",
        "source": source.compiler_contract(),
        "recordClosure": {
            "recordCount": closure["recordCount"],
            "recordTypeCounts": closure["recordTypeCounts"],
        },
        "packageAnimations": package_animations,
        "externalSection5Animations": external_animations,
        "playerCameraSkeleton": camera_skeleton,
        "dialogue": dialogue,
        "sounds": sounds,
        "memberCount": len(members),
        "memberLogicalPaths": sorted(
            str(member["logicalPath"]) for member in members
        ),
        "ownedPayloadsEmitted": False,
        "archiveMembersIndexed": True,
        "profileEmissionReady": False,
        "runtimeReady": False,
    }
