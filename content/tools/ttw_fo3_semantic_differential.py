"""Compare TTW's effective CG00 birth records with standalone Fallout 3.

This module deliberately stops at the record/compiler boundary.  It gives the
existing standalone early-birth compiler an in-memory stable-local-ID view of
the exact TTW winner records, then compares neutral semantics while retaining
the TTW FormKey/runtime/winner provenance separately.  It never opens a BSA or
writes a profile/cache.
"""

from __future__ import annotations

import hashlib
import json
import struct
from pathlib import Path

from plugin_records import (
    BITS_PER_BYTE,
    GroupContext,
    Record,
    iter_plugin_records,
    iter_subrecords,
    zstring,
)
from plugin_stack import file_sha256
from prepare_fo3_profile import (
    CONDITION_FUNCTION_OFFSET,
    CONDITION_PARAMETER_1_OFFSET,
    DIALOGUE_CHILD_GROUP_TYPE,
    FORM_ID_BYTES,
    GET_IS_VOICE_TYPE_FUNCTION,
    STAGE_INDEX_BYTES,
    TTW_INPUT_SIGNATURES,
    _compile_cg00_early_birth_sequence,
    _editor_id,
    _script_source,
    _single_subrecord,
    default_recipe_path,
    enumerate_ttw_fo3_profile_inputs,
    load_recipe,
)
from ttw_effective_source import load_ttw_effective_record_source, parse_form_key
from ttw_fo3_opening import DEFAULT_RECIPE as DEFAULT_TTW_OPENING_RECIPE
from ttw_profile import DEFAULT_REQUIREMENTS_PATH as DEFAULT_TTW_SOURCE_RECIPE


MAXIMUM_SUBRECORD_PAYLOAD_BYTES = (
    1 << (BITS_PER_BYTE * struct.calcsize("<H"))
) - 1
NORMALIZED_LINK_SUBRECORDS = {
    "PACK": frozenset({"IDLA", "INAM"}),
    "DIAL": frozenset({"QSTI"}),
    "INFO": frozenset({"QSTI"}),
}


def _stage_sources(quest: Record) -> dict[int, list[str]]:
    rows: dict[int, list[str]] = {}
    stage: int | None = None
    for subrecord in iter_subrecords(quest):
        if subrecord.signature == "INDX":
            if len(subrecord.data) not in STAGE_INDEX_BYTES:
                raise ValueError("CG00 stage index has an unexpected size")
            stage = int.from_bytes(subrecord.data, "little")
        elif subrecord.signature == "SCTX" and stage is not None:
            rows.setdefault(stage, []).append(zstring(subrecord.data))
    return rows


def _standalone_selection(recipe_path: Path) -> tuple[dict[str, object], dict[str, object]]:
    resolved = recipe_path.resolve()
    recipe = load_recipe(resolved)
    opening = recipe.get("opening")
    if not isinstance(opening, dict):
        raise ValueError("Standalone Fallout 3 recipe has no opening definition")
    selection = opening.get("characterSelection")
    if not isinstance(selection, dict) or not isinstance(
        selection.get("earlyBirthSequence"), dict
    ):
        raise ValueError("Standalone Fallout 3 recipe has no early-birth definition")
    return dict(selection), {
        "file": str(resolved),
        "sha256": file_sha256(resolved),
        "id": str(recipe.get("id", "")),
    }


def _compile_standalone(
    master_path: Path,
    selection: dict[str, object],
) -> dict[str, object]:
    resolved = master_path.resolve()
    records = tuple(iter_plugin_records(resolved, TTW_INPUT_SIGNATURES))
    quest_editor_id = str(selection["questEditorId"])
    quests = [
        record
        for record in records
        if record.signature == "QUST"
        and (_editor_id(record) or "").casefold() == quest_editor_id.casefold()
    ]
    if len(quests) != 1:
        raise ValueError("Standalone Fallout 3 CG00 quest does not resolve uniquely")
    quest = quests[0]
    by_form = {record.form_id: record for record in records}
    script_form_id = struct.unpack("<I", _single_subrecord(quest, "SCRI"))[0]
    script = by_form.get(script_form_id)
    if script is None or script.signature != "SCPT":
        raise ValueError("Standalone Fallout 3 CG00 script does not resolve")
    return _compile_cg00_early_birth_sequence(
        records,
        selection,
        quest.form_id,
        script,
        _script_source(script),
        _stage_sources(quest),
    )


def _closure_contracts(
    closure: dict[str, object],
    core_records: dict[str, object],
    *,
    validate_count: bool = True,
) -> dict[str, dict[str, object]]:
    contracts: dict[str, dict[str, object]] = {}

    def visit(value: object) -> None:
        if isinstance(value, dict):
            if isinstance(value.get("formKey"), str) and isinstance(
                value.get("recordType"), str
            ):
                contracts[str(value["formKey"]).casefold()] = value
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)

    visit(closure)
    visit(core_records)
    expected = int(closure["recordCount"])
    if validate_count and len(contracts) != expected:
        raise ValueError(
            f"TTW CG00 record closure count differs: expected={expected} actual={len(contracts)}"
        )
    return contracts


def _serialize_subrecords(record: Record, payloads: list[tuple[str, bytes]]) -> bytes:
    result = bytearray()
    for signature, payload in payloads:
        try:
            raw_signature = signature.encode("ascii")
        except UnicodeEncodeError as error:
            raise ValueError(
                f"TTW {record.signature} has an unsupported binary subrecord signature"
            ) from error
        if len(raw_signature) != 4 or len(payload) > MAXIMUM_SUBRECORD_PAYLOAD_BYTES:
            raise ValueError(
                f"TTW {record.signature} has an unsupported extended subrecord"
            )
        result.extend(raw_signature)
        result.extend(struct.pack("<H", len(payload)))
        result.extend(payload)
    return bytes(result)


def _normalize_record(version: object, stable_form_key: str) -> Record:
    context = version.context
    record = version.record

    def local_form_id(raw_form_id: int) -> int:
        if raw_form_id == 0:
            return 0
        return context.form_key(raw_form_id).object_id

    targets = NORMALIZED_LINK_SUBRECORDS.get(record.signature, frozenset())
    normalized_data = record.data
    if targets or record.signature == "INFO":
        transformed: list[tuple[str, bytes]] = []
        for subrecord in iter_subrecords(record):
            payload = subrecord.data
            if subrecord.signature in targets:
                if not payload or len(payload) % FORM_ID_BYTES:
                    raise ValueError(
                        f"TTW {stable_form_key} has malformed {subrecord.signature} FormIDs"
                    )
                payload = struct.pack(
                    f"<{len(payload) // FORM_ID_BYTES}I",
                    *(
                        local_form_id(value)
                        for value in struct.unpack(
                            f"<{len(payload) // FORM_ID_BYTES}I", payload
                        )
                    ),
                )
            elif subrecord.signature == "CTDA":
                function = struct.unpack_from("<H", payload, CONDITION_FUNCTION_OFFSET)[0]
                if function == GET_IS_VOICE_TYPE_FUNCTION:
                    mutable = bytearray(payload)
                    raw_voice = struct.unpack_from(
                        "<I", payload, CONDITION_PARAMETER_1_OFFSET
                    )[0]
                    struct.pack_into(
                        "<I",
                        mutable,
                        CONDITION_PARAMETER_1_OFFSET,
                        local_form_id(raw_voice),
                    )
                    payload = bytes(mutable)
            transformed.append((subrecord.signature, payload))
        normalized_data = _serialize_subrecords(record, transformed)

    groups = tuple(
        GroupContext(
            struct.pack("<I", local_form_id(group.label_u32)),
            group.group_type,
        )
        if group.group_type == DIALOGUE_CHILD_GROUP_TYPE
        else group
        for group in record.groups
    )
    return Record(
        record.signature,
        parse_form_key(stable_form_key).object_id,
        record.flags,
        normalized_data,
        groups,
        record.compression_checksum_valid,
    )


def _compile_ttw(
    source: object,
    enumeration: dict[str, object],
    selection: dict[str, object],
) -> dict[str, object]:
    closure = dict(enumeration["cg00SceneClosure"])
    contracts = _closure_contracts(closure, dict(enumeration["records"]))
    normalized: list[Record] = []
    local_owners: dict[int, str] = {}
    for folded_form_key, contract in contracts.items():
        form_key = str(contract["formKey"])
        local_form_id = parse_form_key(form_key).object_id
        previous = local_owners.setdefault(local_form_id, form_key)
        if previous.casefold() != form_key.casefold():
            raise ValueError(
                "TTW CG00 standalone-local compiler view has a FormID collision: "
                f"{previous} and {form_key}"
            )
        normalized.append(_normalize_record(source.records.winner(form_key), form_key))

    quest_key = str(enumeration["records"]["cg00Quest"]["formKey"])
    script_key = str(enumeration["records"]["cg00Script"]["formKey"])
    quest_version = source.records.winner(quest_key)
    script_version = source.records.winner(script_key)
    raw_script = struct.unpack(
        "<I", _single_subrecord(quest_version.record, "SCRI")
    )[0]
    if quest_version.context.form_key(raw_script).text.casefold() != script_key.casefold():
        raise ValueError("TTW CG00 QUST-to-SCPT join differs")
    normalized_by_form = {record.form_id: record for record in normalized}
    quest = normalized_by_form[parse_form_key(quest_key).object_id]
    script = normalized_by_form[parse_form_key(script_key).object_id]
    return _compile_cg00_early_birth_sequence(
        tuple(normalized),
        selection,
        quest.form_id,
        script,
        _script_source(script_version.record),
        _stage_sources(quest_version.record),
    )


def _without_keys(value: object, ignored: frozenset[str]) -> object:
    if isinstance(value, dict):
        return {
            key: _without_keys(child, ignored)
            for key, child in value.items()
            if key not in ignored
        }
    if isinstance(value, list):
        return [_without_keys(child, ignored) for child in value]
    return value


def _semantic_sections(
    contract: dict[str, object],
    terminal_package_change_disposition: str,
) -> dict[str, object]:
    stages = [
        {"stage": row["stage"], "commands": row["commands"]}
        for row in contract["stages"]
    ]
    packages = _without_keys(
        contract["actorPackageSections"],
        frozenset(
            {
                "packageFormId",
                "packageRecordSha256",
                "idleFormId",
                "idleRecordSha256",
            }
        ),
    )
    for rows in packages.values():
        if not rows:
            raise ValueError("CG00 package role has no admitted sections")
        terminal_events = dict(rows[-1]["events"])
        terminal_events.pop("change", None)
        rows[-1]["events"] = terminal_events
        rows[-1]["terminalChangeDisposition"] = terminal_package_change_disposition
    actors = _without_keys(
        {
            "sceneParticipants": contract["sceneParticipants"],
            "playerStartMarker": contract["playerStartMarker"],
            "geneProjectorReference": contract["geneProjectorReference"],
        },
        frozenset({"formId", "recordSha256"}),
    )
    dialogue = _without_keys(
        contract["dialogue"],
        frozenset(
            {
                "infoFormId",
                "recordSha256",
                "formId",
                "resultSourceSha256",
                "textSha256",
            }
        ),
    )
    effects = _without_keys(
        contract["imageSpaceModifiers"],
        frozenset({"formId", "recordSha256"}),
    )
    sounds = _without_keys(
        contract["sounds"],
        frozenset({"formId", "recordSha256"}),
    )
    return {
        "stages": {
            "stageResults": stages,
            "timerTransitions": contract["timerTransitions"],
        },
        "packages": packages,
        "actorsAndMarkers": actors,
        "dialogue": dialogue,
        "imageSpaceModifiers": effects,
        "sounds": sounds,
    }


def _terminal_package_change_links(
    source: object,
    closure: dict[str, object],
    disposition: str,
) -> list[dict[str, object]]:
    package_sections = dict(closure["packageSections"])
    admitted = set(
        _closure_contracts(closure, {}, validate_count=False).keys()
    )
    rows = []
    for role, raw_sections in package_sections.items():
        sections = list(raw_sections)
        if not sections:
            raise ValueError("TTW CG00 package role has no admitted sections")
        package = dict(sections[-1]["package"])
        version = source.records.winner(str(package["formKey"]))
        pending_change = False
        target_ids = []
        for subrecord in iter_subrecords(version.record):
            if subrecord.signature == "POCA":
                pending_change = True
            elif pending_change and subrecord.signature == "INAM":
                if len(subrecord.data) != FORM_ID_BYTES:
                    raise ValueError("TTW CG00 terminal package change IDLE is malformed")
                target_ids.append(struct.unpack("<I", subrecord.data)[0])
                pending_change = False
        if pending_change or len(target_ids) != 1 or target_ids[0] == 0:
            raise ValueError("TTW CG00 terminal package change IDLE is ambiguous")
        target_form_key = version.context.form_key(target_ids[0]).text
        if target_form_key.casefold() in admitted:
            raise ValueError("TTW CG00 terminal package change unexpectedly entered closure")
        target = source.records.contract(
            {"formKey": target_form_key, "recordType": "IDLE"}
        )
        rows.append(
            {
                "role": str(role),
                "fromPackage": package,
                "toIdle": target,
                "disposition": disposition,
            }
        )
    return rows


def compile_ttw_fo3_cg00_semantic_differential(
    profile_path: Path,
    source_namespace_path: Path,
    standalone_master_path: Path,
    *,
    ttw_opening_recipe_path: Path = DEFAULT_TTW_OPENING_RECIPE,
    ttw_source_recipe_path: Path = DEFAULT_TTW_SOURCE_RECIPE,
    standalone_recipe_path: Path | None = None,
) -> dict[str, object]:
    """Return an in-memory records-only semantic differential; write nothing."""

    enumeration = enumerate_ttw_fo3_profile_inputs(
        profile_path,
        source_namespace_path,
        ttw_opening_recipe_path,
        ttw_source_recipe_path,
    )
    source = load_ttw_effective_record_source(
        profile_path,
        source_namespace_path,
        TTW_INPUT_SIGNATURES,
        ttw_source_recipe_path,
    )
    resolved_standalone_recipe = standalone_recipe_path or default_recipe_path()
    selection, standalone_recipe = _standalone_selection(resolved_standalone_recipe)
    opening_recipe = json.loads(ttw_opening_recipe_path.read_text(encoding="utf-8"))
    terminal_disposition = opening_recipe.get("terminalPackageChangeDisposition")
    if not isinstance(terminal_disposition, str) or not terminal_disposition:
        raise ValueError("TTW opening recipe has no terminal package disposition")
    ttw_contract = _compile_ttw(source, enumeration, selection)
    standalone_contract = _compile_standalone(standalone_master_path, selection)
    ttw_semantics = _semantic_sections(ttw_contract, terminal_disposition)
    standalone_semantics = _semantic_sections(standalone_contract, terminal_disposition)
    categories = []
    for category in ttw_semantics:
        matches = ttw_semantics[category] == standalone_semantics[category]
        row: dict[str, object] = {"category": category, "matches": matches}
        if not matches:
            row["ttw"] = ttw_semantics[category]
            row["standalone"] = standalone_semantics[category]
        categories.append(row)

    schema = opening_recipe.get("semanticDifferentialSchema")
    if not isinstance(schema, str) or not schema:
        raise ValueError("TTW opening recipe has no semantic-differential schema")
    standalone_master = standalone_master_path.resolve()
    return {
        "schema": schema,
        "status": "records-only-semantic-differential-runtime-pending",
        "sourceClosure": enumeration["cg00SceneClosure"],
        "coreRecords": enumeration["records"],
        "ttwCompilerContract": ttw_contract,
        "ttwSource": source.compiler_contract(),
        "standaloneSource": {
            "master": {
                "file": str(standalone_master),
                "bytes": standalone_master.stat().st_size,
                "sha256": file_sha256(standalone_master),
            },
            "recipe": standalone_recipe,
        },
        "categories": categories,
        "postClosurePackageChangeLinks": _terminal_package_change_links(
            source,
            dict(enumeration["cg00SceneClosure"]),
            terminal_disposition,
        ),
        "matchingCategories": [
            row["category"] for row in categories if bool(row["matches"])
        ],
        "differingCategories": [
            row["category"] for row in categories if not bool(row["matches"])
        ],
        "ttwCompilerSemanticSha256": hashlib.sha256(
            json.dumps(ttw_semantics, sort_keys=True, separators=(",", ":")).encode(
                "utf-8"
            )
        ).hexdigest(),
        "standaloneCompilerSemanticSha256": hashlib.sha256(
            json.dumps(
                standalone_semantics, sort_keys=True, separators=(",", ":")
            ).encode("utf-8")
        ).hexdigest(),
        "archiveMembersIndexed": False,
        "profileEmissionReady": False,
        "runtimeReady": False,
    }
