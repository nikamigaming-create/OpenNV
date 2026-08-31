"""Project TTW CG00 records and members into the FO3 early-birth shape.

The result is an in-memory identity projection, not an emitted profile.  It
retains the standalone compiler's local FormIDs for the shared CG00 semantic
shape while binding every record to its stable TTW FormKey/runtime FormID and
every required member to its effective path/hash winner.  Bethesda payloads
are never returned.
"""

from __future__ import annotations

import copy
import hashlib
import json
import re
from pathlib import Path

from bsa_archive import canonical_member_path
from plugin_stack import file_sha256
from ttw_effective_source import parse_form_key
from ttw_fo3_member_closure import compile_ttw_fo3_cg00_member_closure
from ttw_fo3_opening import (
    DEFAULT_RECIPE as DEFAULT_TTW_OPENING_RECIPE,
    compile_ttw_fo3_opening,
)
from ttw_fo3_semantic_differential import (
    _closure_contracts,
    compile_ttw_fo3_cg00_semantic_differential,
)
from ttw_profile import DEFAULT_REQUIREMENTS_PATH as DEFAULT_TTW_SOURCE_RECIPE


STANDALONE_CG00_SCHEMA = "opennv-fo3-cg00-early-birth-sequence/v1"
TTW_STAGE_ZERO = 0
TTW_GENE_PROJECTOR_STAGE = 60
EXPECTED_RECORD_CLOSURE_COUNT = 76
EXPECTED_MEMBER_CLOSURE_COUNT = 57
FORM_ID_HEX_CHARACTERS = 8
SHA256_HEX_CHARACTERS = 64
PROJECTION_IDENTITY_SCHEMA = "opennv-ttw-fo3-cg00-projection-identity/v1"
PROJECTION_CACHE_PREFIX = b"opennv-ttw-fo3-cg00-projection-cache-v1\0"
PROJECTION_CACHE_NAMESPACE = "ttw-fo3-opening"
TTW_PLAY_BINK_PATTERN = re.compile(
    r'^PlayBink\s+"(?P<logical_path>[^"]+\.bik)"(?P<arguments>(?:\s+[-+]?\d+)+)$',
    re.IGNORECASE,
)
TTW_NUMERIC_SETTING_PATTERN = re.compile(
    r"^SetNumericGameSetting\s+(?P<setting>\S+)\s+(?P<value>[-+]?\d+(?:\.\d+)?)$",
    re.IGNORECASE,
)


def _validated_member_identity(value: object) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError("TTW member identity is not an object")
    member = copy.deepcopy(value)
    logical_path = canonical_member_path(str(member.get("logicalPath", "")))
    winner = member.get("winner")
    byte_count = member.get("bytes")
    sha256 = str(member.get("sha256", ""))
    winner_kind = winner.get("kind") if isinstance(winner, dict) else None
    winner_bytes = (
        winner.get("memberBytes")
        if winner_kind == "bsa"
        else winner.get("bytes") if winner_kind == "loose" else None
    )
    winner_sha256 = (
        winner.get("memberSha256")
        if winner_kind == "bsa"
        else winner.get("sha256") if winner_kind == "loose" else None
    )
    if (
        not logical_path
        or not isinstance(byte_count, int)
        or byte_count <= 0
        or len(sha256) != SHA256_HEX_CHARACTERS
        or any(character not in "0123456789abcdef" for character in sha256)
        or not isinstance(winner, dict)
        or winner_bytes != byte_count
        or winner_sha256 != sha256
    ):
        raise ValueError(f"TTW member path/hash identity differs: {logical_path}")
    member["logicalPath"] = logical_path
    return member


def _canonical_sha256(value: object) -> str:
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()


def _bind_runtime_identity_envelope(
    projection: dict[str, object],
    semantic: dict[str, object],
    member_closure: dict[str, object],
    opening_command_contract: dict[str, object],
    profile_path: Path,
    source_namespace_path: Path,
) -> None:
    effective_source = copy.deepcopy(semantic.get("ttwSource"))
    member_source = member_closure.get("source")
    if not isinstance(effective_source, dict) or effective_source != member_source:
        raise ValueError("TTW projection record/member effective-source identity differs")
    if (
        opening_command_contract.get("sourceProfile", {}).get("pluginStackId")
        != effective_source.get("pluginStackId")
        or opening_command_contract.get("saveCompatibilityId")
        != effective_source.get("saveCompatibilityId")
    ):
        raise ValueError("TTW projection command/effective-source identity differs")

    record_closure = projection["effectiveRecordClosure"]
    projected_members = projection["effectiveMemberClosure"]
    opening_sha256 = _canonical_sha256(opening_command_contract)
    envelope = {
        "schema": PROJECTION_IDENTITY_SCHEMA,
        "sourceProfile": {
            "file": str(profile_path.resolve()),
            "sha256": file_sha256(profile_path.resolve()),
            "pluginStackId": effective_source["pluginStackId"],
            "saveCompatibilityId": effective_source["saveCompatibilityId"],
        },
        "sourceNamespace": {
            "file": str(source_namespace_path.resolve()),
            "sha256": file_sha256(source_namespace_path.resolve()),
        },
        "effectiveSource": effective_source,
        "recordClosureSha256": _canonical_sha256(record_closure),
        "memberClosureSha256": _canonical_sha256(projected_members),
        "openingCommandContractSha256": opening_sha256,
        "compilerSemanticSha256": semantic["ttwCompilerSemanticSha256"],
        "projectionRecipe": copy.deepcopy(opening_command_contract["recipe"]),
        "standaloneContractShapeSource": copy.deepcopy(
            semantic["standaloneSource"]["master"]
        ),
    }
    compatibility_payload = {
        "schema": projection["schema"],
        "identityEnvelope": envelope,
    }
    compatibility_sha256 = hashlib.sha256(
        PROJECTION_CACHE_PREFIX
        + json.dumps(
            compatibility_payload,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
    ).hexdigest()
    projection["status"] = (
        "validated-runtime-consumable-identity-projection-assets-pending"
    )
    projection["identityEnvelope"] = envelope
    projection["openingCommandContract"] = copy.deepcopy(opening_command_contract)
    projection["cacheBoundary"] = {
        "kind": "dedicated-ttw-cg00-profile-projection",
        "compatibilityId": (
            f"{PROJECTION_CACHE_NAMESPACE}:{compatibility_sha256}"
        ),
        "standaloneFallout3ProfileAccepted": False,
        "standaloneFallout3CacheReused": False,
        "standaloneNewVegasProfileAccepted": False,
        "standaloneNewVegasCacheReused": False,
    }
    projection["runtimeLoaderCompatibility"] = {
        "loader": "TtwFo3OpeningContract.Load",
        "schemaAmbiguous": False,
        "identityEnvelopeValidated": True,
        "commandStateExecutorReady": True,
        "blockers": [
            "fo3-cg00-loader-requires-assetsPrepared-true",
            "archive-members-are-identity-only-not-materialized-runtime-source-paths",
            "player-camera-kf-has-no-sampled-transform-contract",
            "ttw-vault101-world-not-instantiated",
            "ttw-gene-projector-world-ui-not-bound",
        ],
    }
    projection["profileEmissionReady"] = True
    projection["runtimeReady"] = False


def _typed_ttw_stage_commands(stages: list[dict[str, object]]) -> dict[str, object]:
    by_stage = {int(row["stage"]): row for row in stages}
    if TTW_STAGE_ZERO not in by_stage or TTW_GENE_PROJECTOR_STAGE not in by_stage:
        raise ValueError("TTW CG00 stage-0/gene-projector command source is absent")

    stage_zero = []
    for index, raw_command in enumerate(by_stage[TTW_STAGE_ZERO]["commands"]):
        command = str(raw_command)
        play_bink = TTW_PLAY_BINK_PATTERN.fullmatch(command)
        numeric_setting = TTW_NUMERIC_SETTING_PATTERN.fullmatch(command)
        row: dict[str, object] = {
            "index": index,
            "sourceCommand": command,
            "kind": "source-command",
        }
        if play_bink is not None:
            row.update(
                {
                    "kind": "playBink",
                    "logicalPath": canonical_member_path(
                        play_bink.group("logical_path")
                    ),
                    "arguments": [
                        int(value)
                        for value in play_bink.group("arguments").split()
                    ],
                }
            )
        elif numeric_setting is not None:
            row.update(
                {
                    "kind": "setNumericGameSetting",
                    "setting": numeric_setting.group("setting"),
                    "value": float(numeric_setting.group("value")),
                }
            )
        stage_zero.append(row)
    if [row["kind"] for row in stage_zero].count("playBink") != 1 or [
        row["kind"] for row in stage_zero
    ].count("setNumericGameSetting") != 2:
        raise ValueError("TTW CG00 stage-0 command dialect differs")

    gene_commands = [
        str(value) for value in by_stage[TTW_GENE_PROJECTOR_STAGE]["commands"]
    ]
    matches = [
        index
        for index, command in enumerate(gene_commands)
        if command.casefold() == "ttw_showgeneprojector"
    ]
    if matches != [0]:
        raise ValueError("TTW_ShowGeneProjector command ownership differs")
    return {
        "schema": "opennv-ttw-fo3-cg00-command-dialect/v1",
        "stage0": stage_zero,
        "geneProjector": {
            "stage": TTW_GENE_PROJECTOR_STAGE,
            "index": matches[0],
            "kind": "showTtwGeneProjector",
            "sourceCommand": gene_commands[matches[0]],
            "standaloneEquivalent": False,
        },
    }


def _record_identities(
    source_closure: dict[str, object],
    core_records: dict[str, object],
) -> tuple[list[dict[str, object]], dict[str, dict[str, object]]]:
    contracts = _closure_contracts(source_closure, core_records)
    rows: list[dict[str, object]] = []
    by_local: dict[str, dict[str, object]] = {}
    for contract in contracts.values():
        identity = copy.deepcopy(contract)
        local_form_id = format(
            parse_form_key(str(identity["formKey"])).object_id,
            f"0{FORM_ID_HEX_CHARACTERS}x",
        )
        identity["stableLocalFormId"] = local_form_id
        previous = by_local.setdefault(local_form_id, identity)
        if str(previous["formKey"]).casefold() != str(identity["formKey"]).casefold():
            raise ValueError("TTW projection has a stable-local FormID collision")
        rows.append(identity)
    rows.sort(key=lambda row: str(row["formKey"]).casefold())
    return rows, by_local


def _identity_for(
    by_local: dict[str, dict[str, object]],
    form_id: object,
    expected_record_type: str,
) -> dict[str, object]:
    local = str(form_id).casefold().removeprefix("0x").zfill(
        FORM_ID_HEX_CHARACTERS
    )
    identity = by_local.get(local)
    if identity is None or identity.get("recordType") != expected_record_type:
        raise ValueError(
            f"TTW projection record identity differs: {expected_record_type} {local}"
        )
    return copy.deepcopy(identity)


def _member_index(member_closure: dict[str, object]) -> dict[str, dict[str, object]]:
    members = [
        *(row["member"] for row in member_closure["packageAnimations"]),
        *(row["member"] for row in member_closure["externalSection5Animations"]),
        member_closure["playerCameraSkeleton"],
        *(row["voice"] for row in member_closure["dialogue"]),
        *(row["lip"] for row in member_closure["dialogue"]),
        *(
            member
            for row in member_closure["sounds"]
            for member in row["members"]
        ),
    ]
    result: dict[str, dict[str, object]] = {}
    for value in members:
        member = _validated_member_identity(value)
        path = str(member["logicalPath"]).casefold()
        if path in result:
            raise ValueError(f"TTW projection member role is duplicated: {path}")
        result[path] = member
    if len(result) != int(member_closure["memberCount"]):
        raise ValueError("TTW projection member count differs")
    return result


def _take_member(
    members: dict[str, dict[str, object]],
    consumed: set[str],
    logical_path: object,
    *,
    allow_reuse: bool = False,
) -> dict[str, object]:
    path = canonical_member_path(str(logical_path)).casefold()
    member = members.get(path)
    if member is None:
        raise ValueError(f"TTW projection member join is absent: {logical_path}")
    if path in consumed and not allow_reuse:
        raise ValueError(f"TTW projection member is consumed twice: {logical_path}")
    consumed.add(path)
    return copy.deepcopy(member)


def project_ttw_fo3_cg00_profile(
    semantic: dict[str, object],
    member_closure: dict[str, object],
    *,
    projection_schema: str,
) -> dict[str, object]:
    """Join already compiled record/member contracts without reading payloads."""

    sequence = copy.deepcopy(semantic.get("ttwCompilerContract"))
    if not isinstance(sequence, dict) or sequence.get("schema") != STANDALONE_CG00_SCHEMA:
        raise ValueError("TTW compiler output is not the standalone CG00 contract shape")
    source_closure = semantic.get("sourceClosure")
    core_records = semantic.get("coreRecords")
    if not isinstance(source_closure, dict) or not isinstance(core_records, dict):
        raise ValueError("TTW projection record closure is absent")
    if int(source_closure.get("recordCount", 0)) != EXPECTED_RECORD_CLOSURE_COUNT:
        raise ValueError("TTW projection requires the validated 76-record closure")
    if int(member_closure.get("memberCount", 0)) != EXPECTED_MEMBER_CLOSURE_COUNT:
        raise ValueError("TTW projection requires the validated 57-member closure")
    records, by_local = _record_identities(source_closure, core_records)
    members = _member_index(member_closure)
    consumed: set[str] = set()

    sequence["assetsPrepared"] = False
    sequence["questIdentity"] = _identity_for(
        by_local, sequence["questFormId"], "QUST"
    )
    sequence["questScript"]["sourceIdentity"] = _identity_for(
        by_local, sequence["questScript"]["formId"], "SCPT"
    )
    for stage in sequence["stages"]:
        stage["sourceIdentity"] = copy.deepcopy(sequence["questIdentity"])

    for participant in sequence["sceneParticipants"]:
        participant["reference"]["sourceIdentity"] = _identity_for(
            by_local, participant["reference"]["formId"], "ACHR"
        )
        participant["startMarker"]["sourceIdentity"] = _identity_for(
            by_local, participant["startMarker"]["formId"], "REFR"
        )
    sequence["playerStartMarker"]["sourceIdentity"] = _identity_for(
        by_local, sequence["playerStartMarker"]["formId"], "REFR"
    )
    sequence["geneProjectorReference"]["sourceIdentity"] = _identity_for(
        by_local, sequence["geneProjectorReference"]["formId"], "REFR"
    )

    package_member_rows = {
        (str(row["role"]), int(row["section"])): row
        for row in member_closure["packageAnimations"]
    }
    for role, sections in sequence["actorPackageSections"].items():
        for section in sections:
            key = (str(role), int(section["section"]))
            member_row = package_member_rows.get(key)
            if member_row is None:
                raise ValueError(f"TTW package member role is absent: {key}")
            section["packageSourceIdentity"] = _identity_for(
                by_local, section["packageFormId"], "PACK"
            )
            section["idleSourceIdentity"] = _identity_for(
                by_local, section["idleFormId"], "IDLE"
            )
            if (
                str(member_row["package"]["formKey"]).casefold()
                != str(section["packageSourceIdentity"]["formKey"]).casefold()
                or str(member_row["idle"]["formKey"]).casefold()
                != str(section["idleSourceIdentity"]["formKey"]).casefold()
            ):
                raise ValueError("TTW package record/member provenance differs")
            animation_member = _take_member(
                members, consumed, section["animationLogicalPath"]
            )
            if str(animation_member["logicalPath"]).casefold() != str(
                member_row["member"]["logicalPath"]
            ).casefold():
                raise ValueError("TTW package animation member path differs")
            section["animationMemberIdentity"] = animation_member

    player_camera = sequence["playerCamera"]
    camera_key = ("player", int(player_camera["section"]))
    camera_section = next(
        row
        for row in sequence["actorPackageSections"]["player"]
        if int(row["section"]) == camera_key[1]
    )
    player_camera["packageSourceIdentity"] = copy.deepcopy(
        camera_section["packageSourceIdentity"]
    )
    player_camera["idleSourceIdentity"] = copy.deepcopy(
        camera_section["idleSourceIdentity"]
    )
    player_camera["animationMemberIdentity"] = copy.deepcopy(
        camera_section["animationMemberIdentity"]
    )
    skeleton = _validated_member_identity(member_closure["playerCameraSkeleton"])
    skeleton_path = str(skeleton["logicalPath"]).casefold()
    if skeleton_path not in members or skeleton_path in consumed:
        raise ValueError("TTW player-camera skeleton member join differs")
    consumed.add(skeleton_path)
    player_camera["skeletonMemberIdentity"] = skeleton

    for effect in sequence["imageSpaceModifiers"]:
        effect["sourceIdentity"] = _identity_for(
            by_local, effect["formId"], "IMAD"
        )

    sound_member_rows = {
        str(row["sound"]["editorId"]): row for row in member_closure["sounds"]
    }
    for sound in sequence["sounds"]:
        sound["sourceIdentity"] = _identity_for(by_local, sound["formId"], "SOUN")
        member_row = sound_member_rows.get(str(sound["editorId"]))
        if member_row is None or member_row["selectionPolicy"] != sound["selectionPolicy"]:
            raise ValueError("TTW sound member selection semantics differ")
        sound_members = []
        for raw_member in member_row["members"]:
            logical_path = str(raw_member["logicalPath"])
            canonical_sound_path = canonical_member_path(str(sound["logicalPath"]))
            if sound["selectionPolicy"] == "exact-file":
                path_matches = logical_path.casefold() == canonical_sound_path.casefold()
            else:
                path_matches = logical_path.casefold().startswith(
                    canonical_sound_path.casefold().rstrip("\\") + "\\"
                )
            if not path_matches:
                raise ValueError("TTW sound member path is outside its SOUN selection")
            sound_members.append(_take_member(members, consumed, logical_path))
        sound["memberIdentities"] = sound_members

    dialogue_rows = {
        str(row["info"]["formKey"]).casefold(): row
        for row in member_closure["dialogue"]
    }
    dialogue_groups = [
        sequence["dialogue"]["stage10"],
        sequence["dialogue"]["stage22"]["male"],
        sequence["dialogue"]["stage22"]["female"],
        sequence["dialogue"]["stage42"],
    ]
    for cues in dialogue_groups:
        for cue in cues:
            cue["sourceIdentity"] = _identity_for(
                by_local, cue["infoFormId"], "INFO"
            )
            cue["voiceType"]["sourceIdentity"] = _identity_for(
                by_local, cue["voiceType"]["formId"], "VTYP"
            )
            member_row = dialogue_rows.get(
                str(cue["sourceIdentity"]["formKey"]).casefold()
            )
            if member_row is None or str(
                member_row["voiceType"]["formKey"]
            ).casefold() != str(
                cue["voiceType"]["sourceIdentity"]["formKey"]
            ).casefold():
                raise ValueError("TTW dialogue member provenance differs")
            cue["voiceMemberIdentity"] = _take_member(
                members,
                consumed,
                member_row["voice"]["logicalPath"],
                allow_reuse=True,
            )
            cue["lipMemberIdentity"] = _take_member(
                members,
                consumed,
                member_row["lip"]["logicalPath"],
                allow_reuse=True,
            )

    external_animations = []
    for row in member_closure["externalSection5Animations"]:
        external_animations.append(
            {
                "role": row["role"],
                "fromPackage": copy.deepcopy(row["fromPackage"]),
                "toIdle": copy.deepcopy(row["toIdle"]),
                "disposition": row["disposition"],
                "memberIdentity": _take_member(
                    members, consumed, row["member"]["logicalPath"]
                ),
            }
        )
    if consumed != set(members):
        missing = sorted(set(members) - consumed)
        raise ValueError(f"TTW projection has unconsumed member identities: {missing}")

    command_dialect = _typed_ttw_stage_commands(sequence["stages"])
    blockers = [
        "fo3-cg00-loader-requires-assetsPrepared-true",
        "standalone-loader-does-not-consume-formkey-runtimeformid-winner-identities",
        "archive-members-are-identity-only-not-materialized-runtime-source-paths",
        "player-camera-kf-has-no-sampled-transform-contract",
        "standalone-cg00-runtime-does-not-execute-playbink-or-setnumericgamesetting",
        "standalone-cg00-runtime-recognizes-showracemenu-not-ttw_showgeneprojector",
        "existing-ttw-loader-consumes-command-profile-not-cg00-early-birth-sequence",
    ]
    sequence["profileProjection"] = {
        "edition": "TTW",
        "commandDialect": command_dialect,
        "externalSection5Animations": external_animations,
        "identityOnlyMemberCount": len(members),
    }
    return {
        "schema": projection_schema,
        "status": "validated-in-memory-profile-projection-loader-pending",
        "campaign": "Fallout3",
        "edition": "TTW",
        "standaloneRuntimeContractSchema": STANDALONE_CG00_SCHEMA,
        "earlyBirthSequence": sequence,
        "effectiveRecordClosure": {
            "recordCount": len(records),
            "records": records,
        },
        "effectiveMemberClosure": {
            "memberCount": len(members),
            "members": [members[path] for path in sorted(members)],
        },
        "runtimeLoaderCompatibility": {
            "loader": "Fo3Cg00EarlyBirthSequence.Load",
            "schemaAmbiguous": True,
            "blockers": blockers,
        },
        "ownedPayloadsEmitted": False,
        "archiveMembersIndexed": True,
        "profileEmissionReady": False,
        "runtimeReady": False,
    }


def compile_ttw_fo3_cg00_profile_projection(
    profile_path: Path,
    source_namespace_path: Path,
    standalone_master_path: Path,
    *,
    ttw_opening_recipe_path: Path = DEFAULT_TTW_OPENING_RECIPE,
    ttw_source_recipe_path: Path = DEFAULT_TTW_SOURCE_RECIPE,
    standalone_recipe_path: Path | None = None,
) -> dict[str, object]:
    """Compile and join the in-memory projection; write no profile or payload."""

    semantic = compile_ttw_fo3_cg00_semantic_differential(
        profile_path,
        source_namespace_path,
        standalone_master_path,
        ttw_opening_recipe_path=ttw_opening_recipe_path,
        ttw_source_recipe_path=ttw_source_recipe_path,
        standalone_recipe_path=standalone_recipe_path,
    )
    member_closure = compile_ttw_fo3_cg00_member_closure(
        profile_path,
        source_namespace_path,
        ttw_opening_recipe_path=ttw_opening_recipe_path,
        ttw_source_recipe_path=ttw_source_recipe_path,
        standalone_recipe_path=standalone_recipe_path,
    )
    recipe = json.loads(ttw_opening_recipe_path.read_text(encoding="utf-8"))
    projection_schema = recipe.get("profileProjectionSchema")
    if not isinstance(projection_schema, str) or not projection_schema:
        raise ValueError("TTW opening recipe has no profile-projection schema")
    result = project_ttw_fo3_cg00_profile(
        semantic,
        member_closure,
        projection_schema=projection_schema,
    )
    opening_command_contract = compile_ttw_fo3_opening(
        profile_path,
        source_namespace_path,
        ttw_opening_recipe_path,
    )
    _bind_runtime_identity_envelope(
        result,
        semantic,
        member_closure,
        opening_command_contract,
        profile_path,
        source_namespace_path,
    )
    result["sourceContracts"] = {
        "semanticDifferential": {
            "schema": semantic["schema"],
            "semanticSha256": semantic["ttwCompilerSemanticSha256"],
        },
        "memberClosure": {
            "schema": member_closure["schema"],
            "memberCount": member_closure["memberCount"],
        },
        "recipe": {
            "file": str(ttw_opening_recipe_path.resolve()),
            "sha256": file_sha256(ttw_opening_recipe_path.resolve()),
        },
    }
    return result
