"""Emit TTW Vault 101 stage-10 runtime world input node descriptors.

The expanded resource closure owns the effective TTW record/member graph.  This
producer adds exact authored transforms for non-actor CELL references and emits
compact, hash-bound resource pointers rather than Bethesda payloads.  Actor and
camera live state remains absent until the private TTW stage-10 observer emits
all seven required fields.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import struct
from pathlib import Path

from cell_catalog import parse_reference_scale, parse_transform
from plugin_stack import file_sha256
from ttw_effective_source import load_ttw_effective_source
from ttw_fo3_stage10_resource_closure import (
    ADMITTED_RECORD_TYPES,
    LIVE_ONLY_FIELDS,
    SCHEMA as RESOURCE_CLOSURE_SCHEMA,
    STATUS as RESOURCE_CLOSURE_STATUS,
    _canonical_sha256,
    _record_identity,
    _values,
)
from ttw_fo3_stage10_world_materialization import (
    EXPECTED_BASE_EDITOR_IDS,
    EXPECTED_CELL_FORM_KEY,
    EXPECTED_SEQUENCE_NAMES,
    NPC_ROLE_ORDER,
    PROJECTION_SCHEMA,
    PROJECTION_STATUS,
    ROLE_ORDER,
    TARGET_STAGE,
    _section_one,
)


SCHEMA = "opennv-ttw-fo3-cg00-stage10-runtime-world-input/v1"
STATUS = "source-node-inputs-emitted-live-stage10-observation-required"
SOURCE_AUTHORITY = "owned-ttw-effective-resource-closure-no-standalone-substitution"
COORDINATE_SOURCE = "Gamebryo X-right/Y-forward/Z-up, radians, game units"
NODE_TRANSFORM_AUTHORITY = "effective-reference-DATA-and-XSCL-authored-not-live"
ACTOR_LIVE_AUTHORITY = "exact-ttw-stage10-live-observation-required"
RESOURCE_POINTER_AUTHORITY = "hash-bound-json-pointer-into-expanded-resource-closure"
RUNTIME_ARTIFACT_BLOCKER = "runtime-gltf-and-godot-scene-artifacts-not-materialized"
LIVE_OBSERVATION_BLOCKER = "exact-ttw-stage10-live-observation-contract-absent"
EXPECTED_CLOSURE_BLOCKER = (
    "runtime-cell-actor-and-camera-nodes-not-emitted-by-identity-closure"
)
PLAYER_ROLE = "player"
PLAYER_BASE_EDITOR_ID = "Player"
CELL_REFERENCE_RECORD_TYPE = "REFR"
ACTOR_REFERENCE_RECORD_TYPE = "ACHR"
INLINE_PRIMITIVE_STRUCT = struct.Struct("<7fI")
INLINE_MULTIBOUND_STRUCT = struct.Struct("<3f")
INLINE_OCCLUSION_STRUCT = struct.Struct("<9f")
INLINE_DIMENSION_COUNT = 3
INLINE_COLOR_END_INDEX = 7


def _sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _pointer_token(value: object) -> str:
    return str(value).replace("~", "~0").replace("/", "~1")


def _finite_list(values: object, label: str, count: int) -> list[float]:
    if not isinstance(values, (list, tuple)) or len(values) != count:
        raise ValueError(f"TTW stage-10 {label} length differs")
    result = [float(value) for value in values]
    if not all(math.isfinite(value) for value in result):
        raise ValueError(f"TTW stage-10 {label} contains a non-finite value")
    return result


def _record_map(closure: dict[str, object]) -> dict[str, dict[str, object]]:
    expanded = closure.get("expandedClosure")
    if not isinstance(expanded, dict):
        raise ValueError("TTW stage-10 expanded closure is absent")
    records = expanded.get("records")
    if (
        not isinstance(records, list)
        or expanded.get("recordCount") != len(records)
        or closure.get("identity", {}).get("expandedRecordClosureSha256")
        != _canonical_sha256(records)
    ):
        raise ValueError("TTW stage-10 expanded record closure identity differs")
    result: dict[str, dict[str, object]] = {}
    for row in records:
        if not isinstance(row, dict) or not str(row.get("formKey", "")):
            raise ValueError("TTW stage-10 expanded record identity is malformed")
        folded = str(row["formKey"]).casefold()
        if folded in result:
            raise ValueError("TTW stage-10 expanded record identity repeats")
        result[folded] = row
    return result


def _validate_closure(
    closure: dict[str, object],
    *,
    closure_path: Path,
    closure_sha256: str,
) -> tuple[dict[str, dict[str, object]], list[dict[str, object]]]:
    if (
        closure.get("schema") != RESOURCE_CLOSURE_SCHEMA
        or closure.get("status") != RESOURCE_CLOSURE_STATUS
        or closure.get("campaign") != "Fallout3"
        or closure.get("edition") != "TTW"
        or closure.get("stage")
        != {"questEditorId": "CG00", "stage": TARGET_STAGE}
        or closure.get("resourceClosureReady") is not True
        or closure.get("identityOnlyIntrospectionModels") != []
        or closure.get("runtimeMaterializationBlockers")
        != [EXPECTED_CLOSURE_BLOCKER]
        or closure.get("liveOnlyFields") != list(LIVE_ONLY_FIELDS)
        or closure.get("ownedPayloadsEmitted") is not False
        or closure.get("authoredTransformsAcceptedAsLive") is not False
        or closure.get("standaloneArtifactsAccepted") is not False
        or closure.get("runtimeNodesMaterialized") is not False
        or closure.get("runtimeReady") is not False
    ):
        raise ValueError("TTW stage-10 expanded resource closure gate differs")
    identity = closure.get("identity")
    cell = closure.get("cell")
    actors = closure.get("actors")
    camera = closure.get("camera1st")
    expanded = closure.get("expandedClosure")
    if not all(
        isinstance(value, dict) for value in (identity, cell, actors, camera, expanded)
    ):
        raise ValueError("TTW stage-10 expanded resource closure shape differs")
    members = expanded.get("members")
    if (
        not isinstance(members, list)
        or expanded.get("memberCount") != len(members)
        or identity.get("expandedMemberClosureSha256") != _canonical_sha256(members)
        or file_sha256(closure_path) != closure_sha256
    ):
        raise ValueError("TTW stage-10 expanded member/contract identity differs")
    records = _record_map(closure)
    if set(actors) != set(ROLE_ORDER):
        raise ValueError("TTW stage-10 actor resource role set differs")
    return records, members


def _validate_projection(
    closure: dict[str, object],
    projection: dict[str, object],
    projection_path: Path,
) -> dict[str, dict[str, object]]:
    projection_identity = closure["identity"]["projection"]
    if (
        projection.get("schema") != PROJECTION_SCHEMA
        or projection.get("status") != PROJECTION_STATUS
        or projection.get("campaign") != "Fallout3"
        or projection.get("edition") != "TTW"
        or projection.get("ownedPayloadsEmitted") is not False
        or projection.get("archiveMembersIndexed") is not True
        or projection.get("runtimeReady") is not False
        or Path(str(projection_identity.get("path", ""))).resolve()
        != projection_path.resolve()
        or projection_identity.get("sha256") != file_sha256(projection_path)
    ):
        raise ValueError("TTW stage-10 projection binding differs")
    sequence = projection.get("earlyBirthSequence")
    if not isinstance(sequence, dict):
        raise ValueError("TTW stage-10 early-birth sequence is absent")
    raw_participants = sequence.get("sceneParticipants")
    if not isinstance(raw_participants, list):
        raise ValueError("TTW stage-10 scene participant set is absent")
    participants = {
        str(row.get("role")): row
        for row in raw_participants
        if isinstance(row, dict)
    }
    if set(participants) != set(NPC_ROLE_ORDER):
        raise ValueError("TTW stage-10 scene participant set differs")
    return participants


def _source_placement_rows(
    closure: dict[str, object],
) -> list[dict[str, object]]:
    identity = closure["identity"]
    profile = identity["sourceProfile"]
    namespace = identity["sourceNamespace"]
    profile_path = Path(str(profile["file"])).resolve()
    namespace_path = Path(str(namespace["file"])).resolve()
    if (
        not profile_path.is_file()
        or file_sha256(profile_path) != profile.get("sha256")
        or not namespace_path.is_file()
        or file_sha256(namespace_path) != namespace.get("sha256")
    ):
        raise ValueError("TTW stage-10 effective source profile/namespace changed")
    source = load_ttw_effective_source(
        profile_path,
        namespace_path,
        ADMITTED_RECORD_TYPES,
    )
    compiler = source.compiler_contract()
    if (
        compiler.get("pluginStackId") != identity.get("pluginStackId")
        or compiler.get("saveCompatibilityId") != identity.get("saveCompatibilityId")
        or compiler.get("standaloneFallout3ProfileAccepted") is not False
        or compiler.get("standaloneFallout3CacheReused") is not False
        or compiler.get("standaloneNewVegasProfileAccepted") is not False
        or compiler.get("standaloneNewVegasCacheReused") is not False
    ):
        raise ValueError("TTW stage-10 effective source isolation differs")

    rows = []
    for reference in closure["cell"]["references"]:
        source_identity = reference["reference"]
        version = source.records.winner(str(source_identity["formKey"]))
        if _record_identity(source, str(source_identity["formKey"])) != source_identity:
            raise ValueError("TTW stage-10 effective reference winner changed")
        values = _values(version)
        data_rows = values.get("DATA", [])
        if len(data_rows) != 1:
            raise ValueError(
                "TTW stage-10 reference DATA transform is absent or repeated"
            )
        transform = parse_transform(data_rows[0], version.record)
        position = _finite_list(transform.position, "reference position", 3)
        rotation = _finite_list(transform.rotation_radians, "reference rotation", 3)
        scale = float(parse_reference_scale(values, version.record))
        if not math.isfinite(scale) or scale <= 0.0:
            raise ValueError("TTW stage-10 reference scale is invalid")
        row: dict[str, object] = {
            "formKey": source_identity["formKey"],
            "recordType": source_identity["recordType"],
            "transform": {
                "authority": NODE_TRANSFORM_AUTHORITY,
                "positionGameUnits": position,
                "rotationRadians": rotation,
                "scale": scale,
                "dataSha256": _sha256_bytes(data_rows[0]),
                "xsclSha256": (
                    _sha256_bytes(values["XSCL"][0]) if values.get("XSCL") else None
                ),
            },
        }
        if reference.get("baseDisposition") == (
            "inline-reference-primitive-no-plugin-base-record"
        ):
            primitive_rows = values.get("XPRM", [])
            if (
                len(primitive_rows) != 1
                or len(primitive_rows[0]) != INLINE_PRIMITIVE_STRUCT.size
            ):
                raise ValueError("TTW stage-10 inline XPRM primitive differs")
            primitive = INLINE_PRIMITIVE_STRUCT.unpack(primitive_rows[0])
            dimensions = _finite_list(
                primitive[:INLINE_DIMENSION_COUNT],
                "inline primitive dimensions",
                INLINE_DIMENSION_COUNT,
            )
            if not all(value > 0.0 for value in dimensions):
                raise ValueError("TTW stage-10 inline primitive dimensions are invalid")
            row["inlinePrimitive"] = {
                "dimensionsGameUnits": dimensions,
                "colorRgba": _finite_list(
                    primitive[INLINE_DIMENSION_COUNT:INLINE_COLOR_END_INDEX],
                    "inline primitive color",
                    INLINE_COLOR_END_INDEX - INLINE_DIMENSION_COUNT,
                ),
                "primitiveType": int(primitive[-1]),
                "xprmSha256": _sha256_bytes(primitive_rows[0]),
                "multiboundDimensionsGameUnits": _optional_float_payload(
                    values,
                    "XMBO",
                    INLINE_MULTIBOUND_STRUCT,
                ),
                "occlusionPlane": _optional_float_payload(
                    values,
                    "XOCP",
                    INLINE_OCCLUSION_STRUCT,
                ),
                "physicsCollisionAuthority": False,
            }
        rows.append(row)
    return rows


def _optional_float_payload(
    values: dict[str, list[bytes]],
    signature: str,
    layout: struct.Struct,
) -> dict[str, object] | None:
    rows = values.get(signature, [])
    if not rows:
        return None
    if len(rows) != 1 or len(rows[0]) != layout.size:
        raise ValueError(f"TTW stage-10 inline {signature} layout differs")
    unpacked = _finite_list(
        layout.unpack(rows[0]),
        f"inline {signature}",
        len(layout.unpack(rows[0])),
    )
    return {"values": unpacked, "sha256": _sha256_bytes(rows[0])}


def _actor_resource(
    closure: dict[str, object],
    role: str,
) -> dict[str, object]:
    actor = closure["actors"][role]
    base = actor.get("base")
    skeleton = actor.get("skeleton", {}).get("member")
    if (
        actor.get("role") != role
        or not isinstance(base, dict)
        or not isinstance(skeleton, dict)
        or base.get("recordType") != "NPC_"
        or (
            role == PLAYER_ROLE
            and base.get("editorId") != PLAYER_BASE_EDITOR_ID
        )
        or (
            role in NPC_ROLE_ORDER
            and base.get("editorId") != EXPECTED_BASE_EDITOR_IDS[role]
        )
    ):
        raise ValueError(f"TTW stage-10 {role} actor resource identity differs")
    return {
        "id": f"actor:{role}",
        "authority": RESOURCE_POINTER_AUTHORITY,
        "closureJsonPointer": f"/actors/{_pointer_token(role)}",
        "resourceGraphSha256": _canonical_sha256(actor),
        "base": copy.deepcopy(base),
        "female": bool(actor.get("female")),
        "skeletonMember": copy.deepcopy(skeleton),
        "raceFormKey": actor.get("race", {}).get("formKey"),
        "hairFormKey": actor.get("hair", {}).get("record", {}).get("formKey"),
        "eyesFormKey": actor.get("eyes", {}).get("record", {}).get("formKey"),
        "headPartCount": len(actor.get("headParts", [])),
        "outfitPartCount": len(actor.get("outfit", [])),
        "raceModelCount": len(actor.get("raceModels", [])),
        "faceGenCompanionCount": len(
            actor.get("faceGen", {}).get("modelCompanions", [])
        ),
    }


def project_ttw_fo3_stage10_runtime_world_input(
    closure: dict[str, object],
    projection: dict[str, object],
    placement_rows: list[dict[str, object]],
    *,
    closure_path: Path,
    closure_sha256: str,
    projection_path: Path,
) -> dict[str, object]:
    """Project strict source node descriptors while retaining the live gate."""

    records, members = _validate_closure(
        closure,
        closure_path=closure_path,
        closure_sha256=closure_sha256,
    )
    observed_participants = _validate_projection(closure, projection, projection_path)
    cell = closure["cell"]
    if cell.get("identity", {}).get("formKey") != EXPECTED_CELL_FORM_KEY:
        raise ValueError("TTW stage-10 Vault101d CELL identity differs")
    references = cell.get("references")
    bases = cell.get("baseObjects")
    if not isinstance(references, list) or not isinstance(bases, list):
        raise ValueError("TTW stage-10 CELL reference/base closure is absent")
    reference_by_key: dict[str, tuple[int, dict[str, object]]] = {}
    for index, row in enumerate(references):
        key = str(row.get("reference", {}).get("formKey", "")).casefold()
        if not key or key in reference_by_key:
            raise ValueError("TTW stage-10 CELL reference identity repeats")
        reference_by_key[key] = (index, row)
    placements = {str(row.get("formKey", "")).casefold(): row for row in placement_rows}
    if (
        set(placements) != set(reference_by_key)
        or len(placements) != len(placement_rows)
    ):
        raise ValueError("TTW stage-10 authored reference transform closure differs")

    model_resources: dict[str, dict[str, object]] = {}
    model_by_base: dict[str, str] = {}
    for index, row in enumerate(bases):
        record = row.get("record")
        model = row.get("model")
        if not isinstance(record, dict):
            raise ValueError("TTW stage-10 base identity is malformed")
        base_key = str(record.get("formKey", ""))
        if not base_key or base_key.casefold() not in records:
            raise ValueError(
                "TTW stage-10 base identity is outside the expanded closure"
            )
        if model is None:
            continue
        if (
            not isinstance(model, dict)
            or model.get("runtimeDecoderContractAdmitted") is not True
        ):
            raise ValueError("TTW stage-10 model lacks an admitted decoder contract")
        member = model.get("member")
        if not isinstance(member, dict):
            raise ValueError("TTW stage-10 model member identity is absent")
        resource_id = f"model:{base_key}"
        model_by_base[base_key.casefold()] = resource_id
        collision = model.get("collision")
        model_resources[resource_id] = {
            "id": resource_id,
            "authority": RESOURCE_POINTER_AUTHORITY,
            "closureJsonPointer": f"/cell/baseObjects/{index}/model",
            "resourceGraphSha256": _canonical_sha256(model),
            "base": copy.deepcopy(record),
            "member": copy.deepcopy(member),
            "materialCount": len(model.get("materials", [])),
            "collision": copy.deepcopy(collision),
            "presentation": copy.deepcopy(model.get("presentation")),
            "collisionInputPresent": bool(
                isinstance(collision, dict)
                and (
                    int(collision.get("blockCount", 0)) > 0
                    or bool(collision.get("semantics"))
                )
            ),
        }

    shell_nodes = []
    phantom_nodes = []
    inline_nodes = []
    nonpresentation = []
    for folded, (index, reference) in reference_by_key.items():
        source_identity = reference["reference"]
        placement = placements[folded]
        if placement.get("recordType") == ACTOR_REFERENCE_RECORD_TYPE:
            continue
        if placement.get("recordType") != CELL_REFERENCE_RECORD_TYPE:
            raise ValueError("TTW stage-10 CELL node reference type is unsupported")
        common = {
            "reference": copy.deepcopy(source_identity),
            "baseFormKey": reference.get("baseFormKey"),
            "closureJsonPointer": f"/cell/references/{index}",
            "authoredTransform": copy.deepcopy(placement["transform"]),
            "liveTransformAuthority": False,
        }
        inline = placement.get("inlinePrimitive")
        if inline is not None:
            inline_nodes.append(
                {
                    **common,
                    "nodeKind": "source-inline-volume",
                    "primitive": copy.deepcopy(inline),
                    "runtimeDisposition": (
                        "owned-multibound-or-occlusion-input-not-physics-collision"
                    ),
                }
            )
            continue
        resource_id = model_by_base.get(
            str(reference.get("baseFormKey", "")).casefold()
        )
        if resource_id is None:
            nonpresentation.append(
                {
                    "reference": copy.deepcopy(source_identity),
                    "baseFormKey": reference.get("baseFormKey"),
                    "reason": "effective-base-has-no-model-member",
                }
            )
            continue
        resource = model_resources[resource_id]
        collision = resource.get("collision")
        if isinstance(collision, dict) and collision.get("semantics") == (
            "retain-non-blocking-overlap-trigger"
        ):
            phantom_nodes.append(
                {
                    **common,
                    "nodeKind": "owned-nif-phantom",
                    "resourceId": resource_id,
                    "presentationDisposition": (
                        resource.get("presentation") or {}
                    ).get("disposition"),
                    "collision": copy.deepcopy(collision),
                }
            )
            continue
        shell_nodes.append(
            {
                **common,
                "nodeKind": "owned-nif-cell-reference",
                "resourceId": resource_id,
                "presentationInput": True,
                "collisionInput": bool(resource["collisionInputPresent"]),
            }
        )

    sections = {
        role: _section_one(projection["earlyBirthSequence"], role)
        for role in ROLE_ORDER
    }
    actor_resources = {
        role: _actor_resource(closure, role) for role in ROLE_ORDER
    }
    actor_nodes: dict[str, dict[str, object]] = {}
    for role in ROLE_ORDER:
        reference_identity = None
        if role in NPC_ROLE_ORDER:
            source = observed_participants[role].get("reference", {}).get(
                "sourceIdentity"
            )
            if not isinstance(source, dict):
                raise ValueError(f"TTW stage-10 {role} reference identity is absent")
            reference_identity = copy.deepcopy(source)
            closure_reference = reference_by_key.get(
                str(source.get("formKey", "")).casefold()
            )
            if (
                closure_reference is None
                or closure_reference[1].get("baseFormKey")
                != actor_resources[role]["base"]["formKey"]
            ):
                raise ValueError(f"TTW stage-10 {role} actor/reference join differs")
        section = sections[role]
        if section["expectedSequenceName"] != EXPECTED_SEQUENCE_NAMES[role]:
            raise ValueError(f"TTW stage-10 {role} sequence name differs")
        actor_nodes[role] = {
            "nodeKind": "owned-actor-resource-live-state-required",
            "resourceId": actor_resources[role]["id"],
            "reference": reference_identity,
            "package": copy.deepcopy(section["package"]),
            "idle": copy.deepcopy(section["idle"]),
            "animationMember": copy.deepcopy(section["animationMember"]),
            "expectedSequenceName": section["expectedSequenceName"],
            "renderedRootTransform": None,
            "visible": None,
            "controllerPhase": None,
            "liveAuthority": ACTOR_LIVE_AUTHORITY,
        }

    camera = closure["camera1st"]
    camera_resource = {
        "id": "camera1st:player",
        "authority": RESOURCE_POINTER_AUTHORITY,
        "closureJsonPointer": "/camera1st",
        "resourceGraphSha256": _canonical_sha256(camera),
        "targetNode": camera.get("targetNode"),
        "skeletonMember": copy.deepcopy(camera.get("skeleton")),
        "section1Animation": copy.deepcopy(camera.get("section1Animation")),
    }
    if camera_resource["targetNode"] != "Camera1st":
        raise ValueError("TTW stage-10 Camera1st resource identity differs")

    identity = closure["identity"]
    return {
        "schema": SCHEMA,
        "status": STATUS,
        "campaign": "Fallout3",
        "edition": "TTW",
        "stage": {"questEditorId": "CG00", "stage": TARGET_STAGE},
        "sourceAuthority": SOURCE_AUTHORITY,
        "identity": {
            "resourceClosure": {
                "path": str(closure_path.resolve()),
                "sha256": closure_sha256,
                "schema": RESOURCE_CLOSURE_SCHEMA,
            },
            "projection": copy.deepcopy(identity["projection"]),
            "sourceProfile": copy.deepcopy(identity["sourceProfile"]),
            "sourceNamespace": copy.deepcopy(identity["sourceNamespace"]),
            "pluginStackId": identity["pluginStackId"],
            "saveCompatibilityId": identity["saveCompatibilityId"],
            "expandedRecordClosureSha256": identity["expandedRecordClosureSha256"],
            "expandedMemberClosureSha256": identity["expandedMemberClosureSha256"],
        },
        "coordinates": {
            "source": COORDINATE_SOURCE,
            "nodeTransformAuthority": NODE_TRANSFORM_AUTHORITY,
            "authoredStaticReferenceTransformsPublished": True,
            "authoredActorOrCameraTransformsPublished": False,
            "authoredTransformsAcceptedAsLive": False,
        },
        "resources": {
            "modelCount": len(model_resources),
            "models": model_resources,
            "actorCount": len(actor_resources),
            "actors": actor_resources,
            "camera1st": camera_resource,
            "effectiveMemberCount": len(members),
        },
        "nodes": {
            "cellRoot": {
                "nodeKind": "owned-interior-cell-root",
                "sourceIdentity": copy.deepcopy(cell["identity"]),
                "transform": None,
                "transformDisposition": "identity-root-no-scene-specific-rebase",
            },
            "cellShell": shell_nodes,
            "collisionNodeCount": sum(
                1 for row in shell_nodes if row["collisionInput"]
            ),
            "phantoms": phantom_nodes,
            "inlineVolumes": inline_nodes,
            "actors": actor_nodes,
            "camera1st": {
                "nodeKind": "owned-camera1st-resource-live-state-required",
                "resourceId": camera_resource["id"],
                "playerReferenceRuntimeIdentity": None,
                "worldTransform": None,
                "projectionFrustumAndFov": None,
                "controllerPhase": None,
                "liveAuthority": ACTOR_LIVE_AUTHORITY,
            },
        },
        "coverage": {
            "sourceCellReferences": len(references),
            "sourceActorReferences": sum(
                1
                for row in references
                if row.get("reference", {}).get("recordType")
                == ACTOR_REFERENCE_RECORD_TYPE
            ),
            "cellShellNodes": len(shell_nodes),
            "cellShellCollisionNodes": sum(
                1 for row in shell_nodes if row["collisionInput"]
            ),
            "phantomNodes": len(phantom_nodes),
            "inlineVolumeNodes": len(inline_nodes),
            "actorResourceNodes": len(actor_nodes),
            "camera1stResourceNodes": 1,
            "nonPresentationReferences": len(nonpresentation),
            "nonPresentationReferenceRows": nonpresentation,
        },
        "liveObservationGate": {
            "schema": "opennv.ttw-fo3-retail-cg00-stage10-camera-contract/v1",
            "requiredFields": list(LIVE_ONLY_FIELDS),
            "resolvedFields": [],
            "unresolvedFields": list(LIVE_ONLY_FIELDS),
            "allFieldsResolved": False,
            "standaloneFallout3ContractAccepted": False,
            "standaloneNewVegasContractAccepted": False,
        },
        "runtimeWorldInputReady": True,
        "runtimeNodeDescriptorsEmitted": True,
        "runtimeArtifactsMaterialized": False,
        "adapterSceneIdentityReady": False,
        "runtimeBlockers": [RUNTIME_ARTIFACT_BLOCKER, LIVE_OBSERVATION_BLOCKER],
        "ownedPayloadsEmitted": False,
        "standaloneArtifactsAccepted": False,
        "runtimeReady": False,
    }


def compile_ttw_fo3_stage10_runtime_world_input(
    closure_path: Path,
) -> dict[str, object]:
    resolved_closure = closure_path.resolve()
    closure = json.loads(resolved_closure.read_text(encoding="utf-8"))
    projection_path = Path(str(closure["identity"]["projection"]["path"])).resolve()
    projection = json.loads(projection_path.read_text(encoding="utf-8"))
    placements = _source_placement_rows(closure)
    return project_ttw_fo3_stage10_runtime_world_input(
        closure,
        projection,
        placements,
        closure_path=resolved_closure,
        closure_sha256=file_sha256(resolved_closure),
        projection_path=projection_path,
    )


def _main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--resource-closure", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    if arguments.output.exists():
        raise FileExistsError(
            f"Refusing to overwrite TTW stage-10 world input: {arguments.output}"
        )
    result = compile_ttw_fo3_stage10_runtime_world_input(arguments.resource_closure)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(
        json.dumps(result, indent=2) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(_main())
