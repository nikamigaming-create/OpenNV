"""Expand the owned TTW Vault 101 stage-10 record/member closure.

The output is identity-only.  It resolves effective records and BSA/loose
members in memory, records embedded NIF material/collision dependencies, and
never publishes Bethesda bytes or promotes authored transforms as live state.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import io
import json
import math
import struct
from pathlib import Path

from actor_material import actor_texture_paths
from pyffi.formats.nif import NifFormat  # type: ignore  # noqa: E402
from bsa_archive import canonical_member_path
from cell_catalog import (
    BASE_RECORD_TYPES,
    cell_parent_form_id,
    normalize_model_path,
    subrecords_by_signature,
)
from nif_decoder import decode_nif
from nif_trigger_phantom import decode_trigger_phantom_nif
from plugin_records import iter_subrecords, zstring
from plugin_stack import file_sha256, parse_form_key
from ttw_effective_source import load_ttw_effective_source
from ttw_fo3_stage10_world_materialization import (
    EXPECTED_CELL_FORM_KEY,
    EXPECTED_MEMBER_COUNT,
    EXPECTED_RECORD_COUNT,
    MATERIALIZATION_SCHEMA,
    PROJECTION_SCHEMA,
    PROJECTION_STATUS,
    ROLE_ORDER,
)


SCHEMA = "opennv-ttw-fo3-cg00-stage10-expanded-resource-closure/v1"
STATUS = "validated-effective-resources-live-stage10-observation-pending"
SOURCE_AUTHORITY = "owned-ttw-effective-plugin-and-resource-overlay"
RECORD_RESOLUTION = "stable-origin-formkey-last-active-plugin-wins"
MEMBER_RESOLUTION = "ttw-archive-marker-and-loose-root-overlay"
CELL_REFERENCE_TYPES = frozenset({"REFR", "ACHR"})
ACTOR_RECORD_TYPES = frozenset({"NPC_", "RACE", "HAIR", "EYES", "HDPT"})
ADDITIONAL_BASE_RECORD_TYPES = frozenset({"TACT", "ASPC", "SOUN"})
ADMITTED_RECORD_TYPES = frozenset(
    set(BASE_RECORD_TYPES)
    | set(ACTOR_RECORD_TYPES)
    | set(ADDITIONAL_BASE_RECORD_TYPES)
    | {"CELL", "REFR", "ACHR", "LVLI"}
)
EXPECTED_QUEST_EDITOR_ID = "CG00"
TARGET_STAGE = 10
FORM_ID_BYTES = 4
ACTOR_FLAGS_BYTES = 24
FEMALE_ACTOR_FLAG = 0x00000001
FACEGEN_COORDINATE_FIELDS = ("FGGS", "FGGA", "FGTS")
MODEL_FIELDS = ("MODL", "MOD2", "MOD3", "MOD4")
ROLE_PLAYER = "player"
JSON_POINTER_OPENING = "/openingCommandContract"
INLINE_PRIMITIVE_SIGNATURE = "XPRM"
INLINE_OCCLUSION_SIGNATURE = "XOCP"
INLINE_MULTIBOUND_SIGNATURE = "XMBO"
LIVE_ONLY_FIELDS = (
    "player-reference-runtime-identity",
    "player-camera-world-transform",
    "player-camera-projection-frustum-and-fov",
    "player-camera-controller-phase",
    "father-rendered-root-transform-visibility-and-controller-phase",
    "doctor-rendered-root-transform-visibility-and-controller-phase",
    "mother-rendered-root-transform-visibility-and-controller-phase",
)


def _canonical_sha256(value: object) -> str:
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()


def _values(version: object) -> dict[str, list[bytes]]:
    result: dict[str, list[bytes]] = {}
    for row in iter_subrecords(version.record):
        result.setdefault(row.signature, []).append(row.data)
    return result


def _one_form_key(version: object, values: dict[str, list[bytes]], field: str) -> str:
    rows = values.get(field, [])
    if len(rows) != 1 or len(rows[0]) != FORM_ID_BYTES:
        raise ValueError(
            f"TTW stage-10 {version.record.signature} has no unique {field} FormID"
        )
    return version.context.form_key(struct.unpack("<I", rows[0])[0]).text


def _optional_form_keys(
    version: object,
    values: dict[str, list[bytes]],
    field: str,
) -> list[str]:
    rows = values.get(field, [])
    if any(len(row) < FORM_ID_BYTES for row in rows):
        raise ValueError(
            f"TTW stage-10 {version.record.signature} has malformed {field} FormIDs"
        )
    return [
        version.context.form_key(struct.unpack_from("<I", row)[0]).text
        for row in rows
    ]


def _record_identity(source: object, form_key: str) -> dict[str, object]:
    key = parse_form_key(form_key)
    version = source.records.winner(key.text)
    values = _values(version)
    editor_rows = values.get("EDID", [])
    if len(editor_rows) > 1:
        raise ValueError(f"TTW stage-10 record repeats EDID: {key.text}")
    resolution = source.records.resolution(key.owner_plugin, key.object_id)
    return {
        **resolution,
        "recordType": version.record.signature,
        "editorId": zstring(editor_rows[0]) if editor_rows else None,
        "stableLocalFormId": f"{key.object_id:08x}",
    }


def _resolved_member(source: object, logical_path: str) -> dict[str, object]:
    requested = canonical_member_path(logical_path)
    resolved = source.members.resolve(requested)
    contract = resolved.contract()
    if (
        contract.get("logicalPath") != requested
        or not isinstance(contract.get("bytes"), int)
        or int(contract["bytes"]) <= 0
        or len(str(contract.get("sha256", ""))) != hashlib.sha256().digest_size * 2
        or not isinstance(contract.get("winner"), dict)
    ):
        raise ValueError(f"TTW stage-10 member identity is incomplete: {requested}")
    return contract


def _try_member(source: object, logical_path: str) -> dict[str, object] | None:
    try:
        return _resolved_member(source, logical_path)
    except FileNotFoundError:
        return None


def _mesh_path(path: str) -> str:
    canonical = canonical_member_path(path)
    return canonical if canonical.startswith("meshes\\") else f"meshes\\{canonical}"


def _texture_path(path: str) -> str:
    canonical = canonical_member_path(path)
    return canonical if canonical.startswith("textures\\") else f"textures\\{canonical}"


def _texture_binding(source: object, logical_path: str) -> dict[str, object]:
    member = _resolved_member(source, logical_path)
    requested = str(member["logicalPath"])
    normal_path = requested[:-4] + "_n.dds" if requested.casefold().endswith(".dds") else ""
    return {
        "member": member,
        "normalCompanion": _try_member(source, normal_path) if normal_path else None,
    }


def _nif_material_paths(document: object) -> tuple[str, ...]:
    paths: set[str] = set()
    for block in document.blocks:
        properties = list(getattr(block, "properties", []))
        for value in actor_texture_paths(properties):
            if value:
                paths.add(_texture_path(value))
        for prop in properties:
            descriptor_fields = ("base_texture", "normal_texture", "bump_map", "glow_texture")
            for field in descriptor_fields:
                descriptor = getattr(prop, field, None)
                source = getattr(descriptor, "source", None)
                file_name = getattr(source, "file_name", None)
                if file_name:
                    text = bytes(file_name).decode("utf-8", errors="strict") if not isinstance(file_name, str) else file_name
                    if text:
                        paths.add(_texture_path(text))
    return tuple(sorted(paths))


def _model_dependency(source: object, logical_path: str) -> dict[str, object]:
    member = _resolved_member(source, logical_path)
    suffix = Path(str(member["logicalPath"])).suffix.casefold()
    if suffix != ".nif":
        return {
            "member": member,
            "kind": "owned-non-nif-actor-resource",
            "materials": [],
            "collision": None,
            "decoder": None,
        }
    payload = source.members.resolve(str(member["logicalPath"])).data
    try:
        decoded = decode_nif(payload)
        document = decoded.document
        decoder_evidence = decoded.evidence()
        runtime_decoder_contract_admitted = True
    except ValueError as error:
        try:
            trigger = decode_trigger_phantom_nif(payload)
        except ValueError:
            document = NifFormat.Data()
            document.read(io.BytesIO(payload))
            decoder_evidence = {
                "status": "owned-nif-identity-only-pyffi-introspection",
                "runtimeMaterializationAdmission": False,
                "configuredDecoderRejection": str(error),
                "sourceBytesModified": False,
            }
            runtime_decoder_contract_admitted = False
        else:
            decoder_evidence = {
                **trigger.evidence(),
                "ordinaryStaticDecoderDisposition": "not-applicable-collision-only-contract",
            }
            return {
                "member": member,
                "kind": "owned-nif-resource-graph",
                "materials": [],
                "collision": trigger.collision,
                "presentation": trigger.presentation,
                "decoder": decoder_evidence,
                "runtimeDecoderContractAdmitted": True,
            }
    material_members = [_resolved_member(source, path) for path in _nif_material_paths(document)]
    collision_types = sorted(
        {
            type(block).__name__
            for block in document.blocks
            if type(block).__name__.startswith("bhk")
        }
    )
    return {
        "member": member,
        "kind": "owned-nif-resource-graph",
        "materials": material_members,
        "collision": {
            "source": "embedded-in-model-member",
            "blockTypes": collision_types,
            "blockCount": sum(
                type(block).__name__.startswith("bhk")
                for block in document.blocks
            ),
        },
        "decoder": decoder_evidence,
        "runtimeDecoderContractAdmitted": runtime_decoder_contract_admitted,
    }


def _race_tables(version: object, female: bool) -> dict[str, list[str | None]]:
    group = ""
    active_sex = ""
    index: int | None = None
    models: dict[tuple[str, str], dict[int, str]] = {}
    textures: dict[tuple[str, str], dict[int, str]] = {}
    for row in iter_subrecords(version.record):
        if row.signature == "NAM0":
            group, active_sex, index = "head", "male", None
        elif row.signature == "NAM1":
            group, active_sex, index = "body", "male", None
        elif row.signature == "MNAM":
            active_sex, index = "male", None
        elif row.signature == "FNAM":
            active_sex, index = "female", None
        elif row.signature == "INDX":
            if len(row.data) != FORM_ID_BYTES:
                raise ValueError("TTW stage-10 RACE INDX is malformed")
            index = struct.unpack("<I", row.data)[0]
        elif active_sex in {"male", "female"} and index is not None:
            key = (group, active_sex)
            if row.signature == "MODL":
                models.setdefault(key, {})[index] = normalize_model_path(row.data)
            elif row.signature == "ICON":
                textures.setdefault(key, {})[index] = canonical_member_path(zstring(row.data))

    sex = "female" if female else "male"

    def rows(source: dict[tuple[str, str], dict[int, str]], category: str) -> list[str | None]:
        values = source.get((category, sex), {})
        maximum = max(values, default=-1)
        return [values.get(value) for value in range(maximum + 1)]

    return {
        "headModels": rows(models, "head"),
        "headTextures": rows(textures, "head"),
        "bodyModels": rows(models, "body"),
        "bodyTextures": rows(textures, "body"),
    }


def _appearance_part(
    source: object,
    form_key: str,
    expected_type: str,
) -> dict[str, object]:
    version = source.records.winner(form_key)
    if version.record.signature != expected_type:
        raise ValueError(f"TTW stage-10 appearance part type differs: {form_key}")
    values = _values(version)
    models = [
        _model_dependency(source, _mesh_path(normalize_model_path(value)))
        for value in values.get("MODL", [])
    ]
    textures = [
        _texture_binding(source, _texture_path(zstring(value)))
        for value in values.get("ICON", [])
    ]
    return {
        "record": _record_identity(source, form_key),
        "models": models,
        "textures": textures,
    }


def _armor_resources(
    source: object,
    actor_version: object,
    female: bool,
) -> list[dict[str, object]]:
    values = _values(actor_version)
    resources = []
    seen: set[str] = set()
    for item_key in _optional_form_keys(actor_version, values, "CNTO"):
        if item_key.casefold() in seen:
            continue
        seen.add(item_key.casefold())
        try:
            item_version = source.records.winner(item_key)
        except ValueError:
            continue
        if item_version.record.signature != "ARMO":
            continue
        item_values = _values(item_version)
        selected_field = "MOD3" if female else "MODL"
        model_rows = item_values.get(selected_field, [])
        if len(model_rows) > 1:
            raise ValueError(f"TTW stage-10 ARMO repeats {selected_field}: {item_key}")
        resources.append(
            {
                "record": _record_identity(source, item_key),
                "sexSpecificModelField": selected_field,
                "model": (
                    _model_dependency(
                        source,
                        _mesh_path(normalize_model_path(model_rows[0])),
                    )
                    if model_rows
                    else None
                ),
            }
        )
    return resources


def _actor_resource_graph(
    source: object,
    role: str,
    base_form_key: str,
) -> dict[str, object]:
    version = source.records.winner(base_form_key)
    if version.record.signature != "NPC_":
        raise ValueError(f"TTW stage-10 {role} base is not NPC_: {base_form_key}")
    values = _values(version)
    acbs = values.get("ACBS", [])
    if len(acbs) != 1 or len(acbs[0]) != ACTOR_FLAGS_BYTES:
        raise ValueError(f"TTW stage-10 {role} NPC_ ACBS is incomplete")
    female = bool(struct.unpack_from("<I", acbs[0])[0] & FEMALE_ACTOR_FLAG)
    race_key = _one_form_key(version, values, "RNAM")
    hair_key = _one_form_key(version, values, "HNAM")
    eyes_key = _one_form_key(version, values, "ENAM")
    race_version = source.records.winner(race_key)
    if race_version.record.signature != "RACE":
        raise ValueError(f"TTW stage-10 {role} race type differs")
    tables = _race_tables(race_version, female)
    model_paths = [
        _mesh_path(path)
        for path in (*tables["headModels"], *tables["bodyModels"])
        if path
    ]
    texture_paths = [
        _texture_path(path)
        for path in (*tables["headTextures"], *tables["bodyTextures"])
        if path
    ]
    skeleton_rows = values.get("MODL", [])
    if len(skeleton_rows) != 1:
        raise ValueError(f"TTW stage-10 {role} skeleton identity is ambiguous")
    skeleton = _model_dependency(
        source,
        _mesh_path(normalize_model_path(skeleton_rows[0])),
    )
    race_models = [_model_dependency(source, path) for path in model_paths]
    race_textures = [_texture_binding(source, path) for path in texture_paths]
    head_parts = [
        _appearance_part(source, key, "HDPT")
        for key in _optional_form_keys(version, values, "PNAM")
    ]
    geometry = {}
    for field in FACEGEN_COORDINATE_FIELDS:
        rows = values.get(field, [])
        if len(rows) != 1 or len(rows[0]) % struct.calcsize("<f"):
            raise ValueError(f"TTW stage-10 {role} {field} coordinates are incomplete")
        if not all(math.isfinite(value) for value in struct.unpack(f"<{len(rows[0]) // 4}f", rows[0])):
            raise ValueError(f"TTW stage-10 {role} {field} contains non-finite values")
        geometry[field] = {
            "floatCount": len(rows[0]) // struct.calcsize("<f"),
            "sha256": hashlib.sha256(rows[0]).hexdigest(),
        }
    owner = parse_form_key(base_form_key)
    face_geom_path = (
        f"meshes\\characters\\facegendata\\facegeom\\{owner.owner_plugin}\\"
        f"{owner.object_id:08x}.nif"
    )
    face_mod_path = (
        f"textures\\characters\\facemods\\{owner.owner_plugin}\\"
        f"{owner.object_id:08x}_0.dds"
    )
    template_keys = _optional_form_keys(version, values, "TPLT")
    if len(template_keys) > 1:
        raise ValueError(f"TTW stage-10 {role} repeats TPLT")
    face_geom = _try_member(source, face_geom_path)
    facegen_companions = []
    for path in model_paths:
        if not path.casefold().endswith(".nif"):
            continue
        for suffix in (".tri", ".egm"):
            companion = _try_member(source, path[:-4] + suffix)
            if companion is not None:
                facegen_companions.append(companion)
    return {
        "role": role,
        "base": _record_identity(source, base_form_key),
        "female": female,
        "template": (
            _record_identity(source, template_keys[0]) if template_keys else None
        ),
        "race": _record_identity(source, race_key),
        "hair": _appearance_part(source, hair_key, "HAIR"),
        "eyes": _appearance_part(source, eyes_key, "EYES"),
        "headParts": head_parts,
        "skeleton": skeleton,
        "raceModels": race_models,
        "raceTextures": race_textures,
        "outfit": _armor_resources(source, version, female),
        "faceGen": {
            "coordinates": geometry,
            "faceMod": _try_member(source, face_mod_path),
            "faceGeom": face_geom,
            "modelCompanions": facegen_companions,
            "faceGeomDisposition": (
                "effective-member"
                if face_geom is not None
                else "absent-use-source-race-head-plus-npc-coordinates"
            ),
        },
    }


def _reference_rows(
    source: object,
) -> tuple[list[dict[str, object]], dict[str, dict[str, object]], list[dict[str, object]]]:
    references = []
    bases: dict[str, dict[str, object]] = {}
    inline_primitives = []
    for version in source.records.winners.values():
        if version.record.signature not in CELL_REFERENCE_TYPES:
            continue
        parent_raw = cell_parent_form_id(version.record)
        if parent_raw is None:
            continue
        parent_key = version.context.form_key(parent_raw).text
        if parent_key.casefold() != EXPECTED_CELL_FORM_KEY.casefold():
            continue
        values = _values(version)
        reference_key = version.context.form_key(version.record.form_id).text
        base_rows = values.get("NAME", [])
        if len(base_rows) != 1 or len(base_rows[0]) != FORM_ID_BYTES:
            raise ValueError(f"TTW stage-10 cell reference has no base: {reference_key}")
        base_key = version.context.form_key(struct.unpack("<I", base_rows[0])[0]).text
        reference = {
            "reference": _record_identity(source, reference_key),
            "baseFormKey": base_key,
            "authoredTransformAuthority": False,
        }
        try:
            base_version = source.records.winner(base_key)
        except ValueError:
            primitive_fields = {
                field: [hashlib.sha256(row).hexdigest() for row in values.get(field, [])]
                for field in (
                    INLINE_PRIMITIVE_SIGNATURE,
                    INLINE_OCCLUSION_SIGNATURE,
                    INLINE_MULTIBOUND_SIGNATURE,
                )
                if values.get(field)
            }
            if not primitive_fields:
                raise
            reference["baseDisposition"] = "inline-reference-primitive-no-plugin-base-record"
            reference["inlinePrimitiveFieldHashes"] = primitive_fields
            inline_primitives.append(reference)
            references.append(reference)
            continue
        base_identity = _record_identity(source, base_key)
        reference["baseDisposition"] = "effective-plugin-record"
        reference["base"] = base_identity
        references.append(reference)
        folded = base_key.casefold()
        if folded in bases:
            continue
        base_values = _values(base_version)
        model_rows = base_values.get("MODL", [])
        if len(model_rows) > 1:
            raise ValueError(f"TTW stage-10 base repeats MODL: {base_key}")
        bases[folded] = {
            "record": base_identity,
            "model": (
                _model_dependency(
                    source,
                    _mesh_path(normalize_model_path(model_rows[0])),
                )
                if model_rows
                else None
            ),
            "modelDisposition": (
                "effective-owned-member"
                if model_rows
                else "record-has-no-model-member"
            ),
        }
    return references, bases, inline_primitives


def _deduplicated_members(document: object) -> list[dict[str, object]]:
    members: dict[str, dict[str, object]] = {}

    def visit(value: object) -> None:
        if isinstance(value, dict):
            if {
                "logicalPath",
                "bytes",
                "sha256",
                "winner",
            } <= set(value):
                logical_path = str(value["logicalPath"])
                folded = logical_path.casefold()
                existing = members.get(folded)
                if existing is not None and existing != value:
                    raise ValueError(
                        f"TTW stage-10 member identity conflicts: {logical_path}"
                    )
                members[folded] = copy.deepcopy(value)
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)

    visit(document)
    return [members[key] for key in sorted(members)]


def _identity_only_decoder_models(document: object) -> list[dict[str, object]]:
    models: dict[str, dict[str, object]] = {}

    def visit(value: object) -> None:
        if isinstance(value, dict):
            if (
                value.get("kind") == "owned-nif-resource-graph"
                and value.get("runtimeDecoderContractAdmitted") is False
            ):
                member = dict(value["member"])
                logical_path = str(member["logicalPath"])
                models[logical_path.casefold()] = {
                    "member": member,
                    "decoder": copy.deepcopy(value["decoder"]),
                }
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)

    visit(document)
    return [models[key] for key in sorted(models)]


def compile_ttw_fo3_stage10_resource_closure(
    projection_path: Path,
) -> dict[str, object]:
    resolved_projection = projection_path.resolve()
    projection = json.loads(resolved_projection.read_text(encoding="utf-8"))
    if (
        projection.get("schema") != PROJECTION_SCHEMA
        or projection.get("status") != PROJECTION_STATUS
        or projection.get("ownedPayloadsEmitted") is not False
        or projection.get("archiveMembersIndexed") is not True
        or projection.get("runtimeReady") is not False
    ):
        raise ValueError("TTW stage-10 projection identity/status differs")
    envelope = projection.get("identityEnvelope")
    record_closure = projection.get("effectiveRecordClosure")
    member_closure = projection.get("effectiveMemberClosure")
    opening = projection.get("openingCommandContract")
    sequence = projection.get("earlyBirthSequence")
    if not all(
        isinstance(value, dict)
        for value in (envelope, record_closure, member_closure, opening, sequence)
    ):
        raise ValueError("TTW stage-10 projection closure is incomplete")
    if (
        record_closure.get("recordCount") != EXPECTED_RECORD_COUNT
        or member_closure.get("memberCount") != EXPECTED_MEMBER_COUNT
        or envelope.get("recordClosureSha256") != _canonical_sha256(record_closure)
        or envelope.get("memberClosureSha256") != _canonical_sha256(member_closure)
        or envelope.get("openingCommandContractSha256") != _canonical_sha256(opening)
    ):
        raise ValueError("TTW stage-10 projection closure identity differs")
    source_profile = dict(envelope["sourceProfile"])
    source_namespace = dict(envelope["sourceNamespace"])
    profile_path = Path(str(source_profile["file"])).resolve()
    namespace_path = Path(str(source_namespace["file"])).resolve()
    if (
        not profile_path.is_file()
        or file_sha256(profile_path) != source_profile.get("sha256")
        or not namespace_path.is_file()
        or file_sha256(namespace_path) != source_namespace.get("sha256")
    ):
        raise ValueError("TTW stage-10 source profile/namespace changed")
    source = load_ttw_effective_source(
        profile_path,
        namespace_path,
        ADMITTED_RECORD_TYPES,
    )
    if source.members is None:
        raise ValueError("TTW stage-10 effective member overlay is unavailable")
    compiler_source = source.compiler_contract()
    if (
        compiler_source["pluginStackId"] != source_profile.get("pluginStackId")
        or compiler_source["saveCompatibilityId"]
        != source_profile.get("saveCompatibilityId")
        or compiler_source["standaloneFallout3ProfileAccepted"] is not False
        or compiler_source["standaloneFallout3CacheReused"] is not False
        or compiler_source["standaloneNewVegasProfileAccepted"] is not False
        or compiler_source["standaloneNewVegasCacheReused"] is not False
    ):
        raise ValueError("TTW stage-10 effective source isolation differs")

    original_members = []
    for raw_member in member_closure["members"]:
        effective = _resolved_member(source, str(raw_member["logicalPath"]))
        if effective != raw_member:
            raise ValueError(
                f"TTW stage-10 original member winner changed: {raw_member['logicalPath']}"
            )
        original_members.append(effective)

    references, bases, inline_primitives = _reference_rows(source)
    participant_rows = sequence.get("sceneParticipants")
    if not isinstance(participant_rows, list):
        raise ValueError("TTW stage-10 participant rows are absent")
    actor_keys: dict[str, str] = {}
    for row in participant_rows:
        role = str(row["role"])
        reference_key = str(row["reference"]["sourceIdentity"]["formKey"])
        reference_version = source.records.winner(reference_key)
        actor_keys[role] = _one_form_key(
            reference_version,
            _values(reference_version),
            "NAME",
        )
    player_rows = [
        row
        for row in record_closure["records"]
        if row.get("recordType") == "NPC_" and row.get("editorId") == "Player"
    ]
    if len(player_rows) != 1:
        raise ValueError("TTW stage-10 player base is absent or ambiguous")
    actor_keys[ROLE_PLAYER] = str(player_rows[0]["formKey"])
    if set(actor_keys) != set(ROLE_ORDER):
        raise ValueError("TTW stage-10 actor participant set differs")
    actors = {
        role: _actor_resource_graph(source, role, actor_keys[role])
        for role in ROLE_ORDER
    }

    camera = dict(sequence["playerCamera"])
    camera_skeleton = _resolved_member(
        source,
        str(camera["skeletonMemberIdentity"]["logicalPath"]),
    )
    camera_animation = _resolved_member(
        source,
        str(camera["animationMemberIdentity"]["logicalPath"]),
    )
    if (
        camera_skeleton != camera["skeletonMemberIdentity"]
        or camera_animation != camera["animationMemberIdentity"]
        or camera.get("targetNode") != "Camera1st"
    ):
        raise ValueError("TTW stage-10 Camera1st effective member join differs")

    body = {
        "originalRecords": record_closure["records"],
        "originalMembers": original_members,
        "cell": {
            "identity": _record_identity(source, EXPECTED_CELL_FORM_KEY),
            "references": references,
            "baseObjects": [bases[key] for key in sorted(bases)],
            "inlinePrimitiveReferences": inline_primitives,
            "authoredReferenceTransformsPublished": False,
        },
        "camera1st": {
            "targetNode": "Camera1st",
            "skeleton": camera_skeleton,
            "section1Animation": camera_animation,
            "runtimeNodeMaterialized": False,
        },
        "actors": actors,
    }
    all_members = _deduplicated_members(body)
    identity_only_models = _identity_only_decoder_models(body)
    all_record_identities = []
    seen_records: set[str] = set()

    def visit_records(value: object) -> None:
        if isinstance(value, dict):
            if {"formKey", "runtimeFormId", "winner", "recordType"} <= set(value):
                folded = str(value["formKey"]).casefold()
                if folded not in seen_records:
                    seen_records.add(folded)
                    all_record_identities.append(copy.deepcopy(value))
            for child in value.values():
                visit_records(child)
        elif isinstance(value, list):
            for child in value:
                visit_records(child)

    visit_records(body)
    all_record_identities.sort(key=lambda row: str(row["formKey"]).casefold())
    closure_identity = {
        "recordsSha256": _canonical_sha256(all_record_identities),
        "membersSha256": _canonical_sha256(all_members),
    }
    return {
        "schema": SCHEMA,
        "status": STATUS,
        "campaign": "Fallout3",
        "edition": "TTW",
        "stage": {"questEditorId": EXPECTED_QUEST_EDITOR_ID, "stage": TARGET_STAGE},
        "sourceAuthority": SOURCE_AUTHORITY,
        "identity": {
            "projection": {
                "path": str(resolved_projection),
                "sha256": file_sha256(resolved_projection),
                "schema": PROJECTION_SCHEMA,
                "materializationInputSchema": MATERIALIZATION_SCHEMA,
                "openingJsonPointer": JSON_POINTER_OPENING,
            },
            "sourceProfile": source_profile,
            "sourceNamespace": source_namespace,
            "pluginStackId": compiler_source["pluginStackId"],
            "saveCompatibilityId": compiler_source["saveCompatibilityId"],
            "originalRecordClosureSha256": envelope["recordClosureSha256"],
            "originalMemberClosureSha256": envelope["memberClosureSha256"],
            "expandedRecordClosureSha256": closure_identity["recordsSha256"],
            "expandedMemberClosureSha256": closure_identity["membersSha256"],
        },
        "resolution": {
            "records": RECORD_RESOLUTION,
            "members": MEMBER_RESOLUTION,
            "effectiveSource": compiler_source,
        },
        **body,
        "expandedClosure": {
            "recordCount": len(all_record_identities),
            "memberCount": len(all_members),
            "records": all_record_identities,
            "members": all_members,
        },
        "resourceClosureReady": True,
        "identityOnlyIntrospectionModels": identity_only_models,
        "runtimeMaterializationBlockers": (
            (
                ["configured-runtime-nif-decoder-does-not-admit-all-closed-models"]
                if identity_only_models
                else []
            )
            + ["runtime-cell-actor-and-camera-nodes-not-emitted-by-identity-closure"]
        ),
        "liveOnlyFields": list(LIVE_ONLY_FIELDS),
        "ownedPayloadsEmitted": False,
        "authoredTransformsAcceptedAsLive": False,
        "standaloneArtifactsAccepted": False,
        "runtimeNodesMaterialized": False,
        "runtimeReady": False,
    }


def _main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--projection", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    result = compile_ttw_fo3_stage10_resource_closure(arguments.projection)
    if arguments.output.exists():
        raise FileExistsError(f"Refusing to overwrite TTW closure: {arguments.output}")
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(_main())
