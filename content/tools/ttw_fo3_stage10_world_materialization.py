"""Compile the fail-closed TTW Vault 101 stage-10 world input contract.

The validated 76-record/57-member projection contains exact PACK, IDLE, KF,
and Camera1st skeleton identities.  It does not yet contain the cell or actor
resource graphs needed to emit runtime scenes.  This producer makes that
boundary machine-readable and never borrows standalone Fallout 3 artifacts.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path

from plugin_stack import file_sha256


PROJECTION_SCHEMA = "opennv-ttw-fo3-cg00-profile-projection/v1"
PROJECTION_STATUS = "validated-runtime-consumable-identity-projection-assets-pending"
MATERIALIZATION_SCHEMA = (
    "opennv-ttw-fo3-cg00-stage10-world-materialization-inputs/v1"
)
MATERIALIZATION_STATUS = "validated-source-joins-runtime-artifacts-not-materialized"
SOURCE_AUTHORITY = (
    "owned-ttw-effective-record-and-member-closure-no-standalone-artifacts"
)
STANDALONE_SHAPE_DISPOSITION = (
    "schema-shape-only-no-record-member-or-runtime-artifact-authority"
)
EXPECTED_RECORD_COUNT = 76
EXPECTED_MEMBER_COUNT = 57
STAGE_ONE = 1
TARGET_STAGE = 10
SHA256_HEX_CHARACTERS = 64
FORM_ID_HEX_CHARACTERS = 8
ROLE_ORDER = ("player", "father", "doctor", "mother")
NPC_ROLE_ORDER = ("father", "doctor", "mother")
EXPECTED_CELL_FORM_KEY = "Fallout3.esm:028138"
EXPECTED_TARGET_NODE = "Camera1st"
EXPECTED_SEQUENCE_NAMES = {
    "player": "SpecialIdle_CG00PlayerSection01",
    "father": "SpecialIdle_CG00DadSection01",
    "doctor": "SpecialIdle_CG00DrLiSection01",
    "mother": "SpecialIdle_CG00MomSection01",
}
EXPECTED_BASE_EDITOR_IDS = {
    "player": "Player",
    "father": "CG00Dad",
    "doctor": "CG00DoctorLi",
    "mother": "CG00Mom",
}
BLOCKERS = (
    "live-stage10-player-reference-and-rendered-transform-contract-absent",
    "vault101-cell-reference-model-material-collision-member-closure-absent",
    "player-actor-base-template-appearance-body-head-facegen-resource-closure-absent",
    "father-actor-base-template-appearance-body-head-facegen-resource-closure-absent",
    "doctor-actor-base-template-appearance-body-head-facegen-resource-closure-absent",
    "mother-actor-base-template-appearance-body-head-facegen-resource-closure-absent",
    "camera1st-skeleton-kf-identities-not-materialized-to-runtime-node",
    "participant-actor-scenes-and-sidecars-not-materialized",
)


def _canonical_sha256(value: object) -> str:
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()


def _verified_identity_file(value: object, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError(f"TTW stage-10 {label} identity is absent")
    result = copy.deepcopy(value)
    source_path = Path(str(result.get("file", ""))).resolve()
    expected_sha256 = _hex(result.get("sha256"), f"{label} file")
    if not source_path.is_file() or file_sha256(source_path) != expected_sha256:
        raise ValueError(f"TTW stage-10 {label} file identity differs")
    result["file"] = str(source_path)
    result["sha256"] = expected_sha256
    return result


def _hex(value: object, label: str, characters: int = SHA256_HEX_CHARACTERS) -> str:
    result = str(value).casefold()
    if len(result) != characters or any(
        character not in "0123456789abcdef" for character in result
    ):
        raise ValueError(f"TTW stage-10 {label} is not a {characters}-digit hex value")
    return result


def _identity(value: object, record_type: str, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError(f"TTW stage-10 {label} record identity is absent")
    result = copy.deepcopy(value)
    form_key = str(result.get("formKey", ""))
    runtime_form_id = _hex(
        result.get("runtimeFormId"),
        f"{label} runtime FormID",
        FORM_ID_HEX_CHARACTERS,
    )
    winner = result.get("winner")
    if (
        result.get("recordType") != record_type
        or ":" not in form_key
        or not isinstance(winner, dict)
        or not str(winner.get("plugin", ""))
        or len(_hex(winner.get("pluginSha256"), f"{label} winner plugin"))
        != SHA256_HEX_CHARACTERS
        or len(_hex(winner.get("recordSha256"), f"{label} winner record"))
        != SHA256_HEX_CHARACTERS
        or not isinstance(winner.get("loadOrderIndex"), int)
        or int(winner["loadOrderIndex"]) < 0
        or not isinstance(winner.get("sourceRootIndex"), int)
        or int(winner["sourceRootIndex"]) < 0
    ):
        raise ValueError(f"TTW stage-10 {label} record identity differs")
    result["runtimeFormId"] = runtime_form_id
    return result


def _member(value: object, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError(f"TTW stage-10 {label} member identity is absent")
    result = copy.deepcopy(value)
    winner = result.get("winner")
    byte_count = result.get("bytes")
    sha256 = _hex(result.get("sha256"), f"{label} member")
    if (
        not str(result.get("logicalPath", ""))
        or not isinstance(byte_count, int)
        or byte_count <= 0
        or not isinstance(winner, dict)
        or winner.get("kind") not in {"bsa", "loose"}
    ):
        raise ValueError(f"TTW stage-10 {label} member identity differs")
    winner_bytes = (
        winner.get("memberBytes") if winner["kind"] == "bsa" else winner.get("bytes")
    )
    winner_sha256 = (
        winner.get("memberSha256")
        if winner["kind"] == "bsa"
        else winner.get("sha256")
    )
    if winner_bytes != byte_count or str(winner_sha256).casefold() != sha256:
        raise ValueError(f"TTW stage-10 {label} effective member winner differs")
    result["sha256"] = sha256
    return result


def _record_by_form_key(
    records: list[dict[str, object]],
    form_key: str,
    record_type: str,
    label: str,
) -> dict[str, object]:
    matches = [
        row
        for row in records
        if str(row.get("formKey", "")).casefold() == form_key.casefold()
        and row.get("recordType") == record_type
    ]
    if len(matches) != 1:
        raise ValueError(f"TTW stage-10 {label} effective record is absent or ambiguous")
    return _identity(matches[0], record_type, label)


def _record_by_editor_id(
    records: list[dict[str, object]],
    editor_id: str,
    record_type: str,
    label: str,
) -> dict[str, object]:
    matches = [
        row
        for row in records
        if str(row.get("editorId", "")).casefold() == editor_id.casefold()
        and row.get("recordType") == record_type
    ]
    if len(matches) != 1:
        raise ValueError(f"TTW stage-10 {label} effective record is absent or ambiguous")
    return _identity(matches[0], record_type, label)


def _require_record_join(
    records: list[dict[str, object]],
    value: dict[str, object],
    label: str,
) -> dict[str, object]:
    joined = _record_by_form_key(
        records,
        str(value["formKey"]),
        str(value["recordType"]),
        label,
    )
    if joined != value:
        raise ValueError(f"TTW stage-10 {label} record closure join differs")
    return joined


def _require_member_join(
    members: list[dict[str, object]],
    value: dict[str, object],
    label: str,
) -> dict[str, object]:
    logical_path = str(value["logicalPath"]).casefold()
    matches = [
        _member(row, label)
        for row in members
        if str(row.get("logicalPath", "")).casefold() == logical_path
    ]
    if len(matches) != 1 or matches[0] != value:
        raise ValueError(f"TTW stage-10 {label} member closure join differs")
    return matches[0]


def _section_one(sequence: dict[str, object], role: str) -> dict[str, object]:
    package_sections = sequence.get("actorPackageSections")
    if not isinstance(package_sections, dict):
        raise ValueError("TTW stage-10 package-section matrix is absent")
    sections = package_sections.get(role)
    if not isinstance(sections, list):
        raise ValueError(f"TTW stage-10 {role} package sections are absent")
    matches = [row for row in sections if int(row.get("section", -1)) == STAGE_ONE]
    if len(matches) != 1:
        raise ValueError(f"TTW stage-10 {role} Section1 is absent or ambiguous")
    row = copy.deepcopy(matches[0])
    package = _identity(row.get("packageSourceIdentity"), "PACK", f"{role} package")
    idle = _identity(row.get("idleSourceIdentity"), "IDLE", f"{role} idle")
    animation = _member(row.get("animationMemberIdentity"), f"{role} animation")
    if (
        row.get("packageFormId") != package.get("stableLocalFormId")
        or row.get("idleFormId") != idle.get("stableLocalFormId")
        or str(row.get("animationLogicalPath", "")).casefold()
        != str(animation["logicalPath"]).casefold()
    ):
        raise ValueError(f"TTW stage-10 {role} PACK/IDLE/KF join differs")
    return {
        "section": STAGE_ONE,
        "package": package,
        "idle": idle,
        "animationMember": animation,
        "expectedSequenceName": EXPECTED_SEQUENCE_NAMES[role],
    }


def project_ttw_fo3_stage10_world_inputs(
    projection: dict[str, object],
    *,
    projection_path: Path,
    projection_sha256: str,
    opening_profile_binding: dict[str, object],
) -> dict[str, object]:
    """Join exact available identities and emit an explicit incomplete boundary."""

    if (
        projection.get("schema") != PROJECTION_SCHEMA
        or projection.get("status") != PROJECTION_STATUS
        or projection.get("campaign") != "Fallout3"
        or projection.get("edition") != "TTW"
        or projection.get("ownedPayloadsEmitted") is not False
        or projection.get("archiveMembersIndexed") is not True
        or projection.get("runtimeReady") is not False
    ):
        raise ValueError("TTW stage-10 profile projection identity/status differs")
    record_closure = projection.get("effectiveRecordClosure")
    member_closure = projection.get("effectiveMemberClosure")
    envelope = projection.get("identityEnvelope")
    sequence = projection.get("earlyBirthSequence")
    opening = projection.get("openingCommandContract")
    if not all(
        isinstance(value, dict)
        for value in (record_closure, member_closure, envelope, sequence, opening)
    ):
        raise ValueError("TTW stage-10 projection closure is absent")
    if (
        not isinstance(opening_profile_binding, dict)
        or _hex(
            opening_profile_binding.get("commandContractSha256"),
            "opening profile command contract",
        )
        != _canonical_sha256(opening)
    ):
        raise ValueError("TTW stage-10 opening profile binding differs")
    records = record_closure.get("records")
    members = member_closure.get("members")
    if (
        record_closure.get("recordCount") != EXPECTED_RECORD_COUNT
        or not isinstance(records, list)
        or len(records) != EXPECTED_RECORD_COUNT
        or member_closure.get("memberCount") != EXPECTED_MEMBER_COUNT
        or not isinstance(members, list)
        or len(members) != EXPECTED_MEMBER_COUNT
        or sequence.get("assetsPrepared") is not False
    ):
        raise ValueError("TTW stage-10 projection record/member counts differ")
    if (
        _hex(envelope.get("recordClosureSha256"), "record closure")
        != _canonical_sha256(record_closure)
        or _hex(envelope.get("memberClosureSha256"), "member closure")
        != _canonical_sha256(member_closure)
        or _hex(envelope.get("openingCommandContractSha256"), "opening command")
        != _canonical_sha256(opening)
    ):
        raise ValueError("TTW stage-10 projection closure hash differs")

    source_profile = _verified_identity_file(
        envelope.get("sourceProfile"),
        "source profile",
    )
    source_namespace = _verified_identity_file(
        envelope.get("sourceNamespace"),
        "source namespace",
    )
    effective_source = envelope.get("effectiveSource")
    cache = projection.get("cacheBoundary")
    if not all(isinstance(value, dict) for value in (effective_source, cache)):
        raise ValueError("TTW stage-10 projection identity envelope is incomplete")
    plugin_stack_id = _hex(source_profile.get("pluginStackId"), "plugin stack")
    save_compatibility_id = str(source_profile.get("saveCompatibilityId", ""))
    opening_source_profile = opening.get("sourceProfile")
    opening_source_namespace = opening.get("sourceNamespace")
    if (
        save_compatibility_id != f"ttw:{plugin_stack_id}"
        or not isinstance(opening_source_profile, dict)
        or not isinstance(opening_source_namespace, dict)
        or opening_source_profile != source_profile
        or opening_source_namespace.get("file") != source_namespace.get("file")
        or opening_source_namespace.get("sha256") != source_namespace.get("sha256")
        or opening.get("saveCompatibilityId") != save_compatibility_id
        or effective_source.get("pluginStackId") != plugin_stack_id
        or effective_source.get("saveCompatibilityId") != save_compatibility_id
        or effective_source.get("sourceProfileSha256") != source_profile.get("sha256")
        or effective_source.get("sourceNamespaceSha256")
        != source_namespace.get("sha256")
        or effective_source.get("standaloneFallout3ProfileAccepted") is not False
        or effective_source.get("standaloneFallout3CacheReused") is not False
        or effective_source.get("standaloneNewVegasProfileAccepted") is not False
        or effective_source.get("standaloneNewVegasCacheReused") is not False
        or cache.get("standaloneFallout3ProfileAccepted") is not False
        or cache.get("standaloneFallout3CacheReused") is not False
        or cache.get("standaloneNewVegasProfileAccepted") is not False
        or cache.get("standaloneNewVegasCacheReused") is not False
    ):
        raise ValueError("TTW stage-10 projection admits standalone profile/cache authority")

    cell = _record_by_form_key(
        records,
        EXPECTED_CELL_FORM_KEY,
        "CELL",
        "Vault101d",
    )
    player_base = _record_by_editor_id(records, "Player", "NPC_", "player base")
    sections = {role: _section_one(sequence, role) for role in ROLE_ORDER}
    for role, section in sections.items():
        _require_record_join(records, section["package"], f"{role} package")
        _require_record_join(records, section["idle"], f"{role} idle")
        _require_member_join(
            members,
            section["animationMember"],
            f"{role} animation",
        )
    raw_participants = sequence.get("sceneParticipants")
    if not isinstance(raw_participants, list):
        raise ValueError("TTW stage-10 scene participants are absent")
    participant_by_role = {
        str(row.get("role")): row for row in raw_participants if isinstance(row, dict)
    }
    if set(participant_by_role) != set(NPC_ROLE_ORDER):
        raise ValueError("TTW stage-10 NPC participant set differs")

    camera = sequence.get("playerCamera")
    if not isinstance(camera, dict) or camera.get("targetNode") != EXPECTED_TARGET_NODE:
        raise ValueError("TTW stage-10 Camera1st source join differs")
    camera_skeleton = _member(camera.get("skeletonMemberIdentity"), "Camera1st skeleton")
    camera_animation = _member(
        camera.get("animationMemberIdentity"), "Camera1st animation"
    )
    if (
        camera.get("packageSourceIdentity") != sections["player"]["package"]
        or camera.get("idleSourceIdentity") != sections["player"]["idle"]
        or camera_animation != sections["player"]["animationMember"]
    ):
        raise ValueError("TTW stage-10 Camera1st PACK/IDLE/KF join differs")
    _require_member_join(members, camera_skeleton, "Camera1st skeleton")
    _require_member_join(members, camera_animation, "Camera1st animation")

    participants: dict[str, object] = {
        "player": {
            "referenceAuthority": "live-stage10-observation-required",
            "base": player_base,
            **sections["player"],
            "runtimeNodeArtifact": None,
        }
    }
    for role in NPC_ROLE_ORDER:
        raw = participant_by_role[role]
        reference = raw.get("reference")
        start_marker = raw.get("startMarker")
        if not isinstance(reference, dict) or not isinstance(start_marker, dict):
            raise ValueError(f"TTW stage-10 {role} reference/marker is absent")
        reference_identity = _identity(
            reference.get("sourceIdentity"),
            "ACHR",
            f"{role} reference",
        )
        start_marker_identity = _identity(
            start_marker.get("sourceIdentity"),
            "REFR",
            f"{role} start marker",
        )
        base_identity = _record_by_editor_id(
            records,
            EXPECTED_BASE_EDITOR_IDS[role],
            "NPC_",
            f"{role} base",
        )
        _require_record_join(records, reference_identity, f"{role} reference")
        _require_record_join(records, start_marker_identity, f"{role} start marker")
        participants[role] = {
            "referenceAuthority": "live-stage10-observation-required",
            "reference": reference_identity,
            "base": base_identity,
            "startMarkerEvidence": {
                "sourceIdentity": start_marker_identity,
                "transformAuthority": False,
            },
            **sections[role],
            "runtimeNodeArtifact": None,
        }

    return {
        "schema": MATERIALIZATION_SCHEMA,
        "status": MATERIALIZATION_STATUS,
        "campaign": "Fallout3",
        "edition": "TTW",
        "stage": {"questEditorId": "CG00", "stage": TARGET_STAGE},
        "sourceAuthority": SOURCE_AUTHORITY,
        "identity": {
            "projection": {
                "path": str(projection_path.resolve()),
                "sha256": _hex(projection_sha256, "projection"),
                "cacheCompatibilityId": str(cache.get("compatibilityId", "")),
            },
            "sourceProfile": copy.deepcopy(source_profile),
            "sourceNamespace": copy.deepcopy(source_namespace),
            "openingProfile": copy.deepcopy(opening_profile_binding),
            "pluginStackId": plugin_stack_id,
            "saveCompatibilityId": save_compatibility_id,
            "recordClosureSha256": _canonical_sha256(record_closure),
            "memberClosureSha256": _canonical_sha256(member_closure),
        },
        "cell": {
            "sourceIdentity": cell,
            "runtimePresentationArtifact": None,
        },
        "camera1st": {
            "targetNode": EXPECTED_TARGET_NODE,
            "referenceAuthority": "live-stage10-observation-required",
            "package": sections["player"]["package"],
            "idle": sections["player"]["idle"],
            "animationMember": camera_animation,
            "skeletonMember": camera_skeleton,
            "runtimeNodeArtifact": None,
        },
        "participants": participants,
        "closure": {
            "recordCount": EXPECTED_RECORD_COUNT,
            "memberCount": EXPECTED_MEMBER_COUNT,
            "section1AnimationIdentityCount": len(sections),
            "cameraSkeletonIdentityCount": 1,
            "participantIdentityCount": len(participants),
            "runtimeArtifactCount": 0,
            "blockers": list(BLOCKERS),
        },
        "isolation": {
            "standaloneShapeDisposition": STANDALONE_SHAPE_DISPOSITION,
            "standaloneFallout3ProfileAccepted": False,
            "standaloneFallout3CacheReused": False,
            "standaloneNewVegasProfileAccepted": False,
            "standaloneNewVegasCacheReused": False,
        },
        "ownedPayloadsEmitted": False,
        "runtimeArtifactsMaterialized": False,
        "adapterSceneIdentityReady": False,
        "runtimeReady": False,
    }


def compile_ttw_fo3_stage10_world_inputs(
    projection_path: Path,
    opening_profile_path: Path | None = None,
) -> dict[str, object]:
    projection_path = projection_path.resolve()
    projection = json.loads(projection_path.read_text(encoding="utf-8"))
    embedded = projection.get("openingCommandContract")
    if not isinstance(embedded, dict):
        raise ValueError("TTW stage-10 embedded opening profile is absent")
    if opening_profile_path is None:
        opening_binding = {
            "authority": "embedded-projection-command-contract",
            "containerPath": str(projection_path),
            "containerSha256": file_sha256(projection_path),
            "jsonPointer": "/openingCommandContract",
            "commandContractSha256": _canonical_sha256(embedded),
        }
    else:
        resolved_opening_path = opening_profile_path.resolve()
        opening = json.loads(resolved_opening_path.read_text(encoding="utf-8"))
        if opening != embedded:
            raise ValueError("TTW stage-10 opening profile differs from its projection")
        opening_binding = {
            "authority": "exact-external-command-contract",
            "path": str(resolved_opening_path),
            "sha256": file_sha256(resolved_opening_path),
            "commandContractSha256": _canonical_sha256(embedded),
        }
    return project_ttw_fo3_stage10_world_inputs(
        projection,
        projection_path=projection_path,
        projection_sha256=file_sha256(projection_path),
        opening_profile_binding=opening_binding,
    )


def _main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--projection", type=Path, required=True)
    parser.add_argument("--opening-profile", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    result = compile_ttw_fo3_stage10_world_inputs(
        arguments.projection,
        arguments.opening_profile,
    )
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(
        json.dumps(result, indent=2) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(_main())
