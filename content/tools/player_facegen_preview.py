"""Compile the source-owned default FNV player head with live EGM controls."""

from __future__ import annotations

import hashlib
from pathlib import Path

from actor_catalog import scan_actor_catalog
from actor_gltf import ActorComponent, ActorGltfInput, export_actor_gltf
from bsa_archive import ExtractedMember, canonical_member_path
from facegen import (
    compose_facegen_coordinates,
    synthesize_texture_detail,
)
from owned_archive_stack import OwnedArchiveStack
from prepare_actor import (
    RACE_HEAD_COMPONENT_ROLES,
    RACE_HEAD_MODEL_INDEX,
    model_companion,
    texture_companion,
    texture_member,
)
from runtime_configuration import RuntimeConfiguration


PLAYER_FACEGEN_PREVIEW_SCHEMA = "opennv-owned-player-facegen-preview/v1"
PLAYER_FACEGEN_PREVIEW_STATUS = (
    "compiled-default-male-head-with-ctl-egm-targets-one-normalized-control-runtime-bound"
)
PLAYER_RECORD_FORM_ID = 0x00000007
PLAYER_PREVIEW_SEX = "male"
HAIR_PREVIEW_SHAPE = "NoHat"
NORMAL_TEXTURE_SUFFIX = "_n"
FACEGEN_CONTROL_AXIS_FLOATS = 50
FORM_ID_RADIX = 16
BYTE_CHANNEL_MAXIMUM = 255.0


def _mesh_path(path: str) -> str:
    canonical = canonical_member_path(path)
    return canonical if canonical.startswith("meshes\\") else f"meshes\\{canonical}"


def _member_row(member: ExtractedMember) -> dict[str, object]:
    if member.source_archive is None or member.source_archive_sha256 is None:
        raise ValueError(f"Player preview member lacks archive provenance: {member.logical_path}")
    return {
        "logicalPath": member.logical_path,
        "bytes": len(member.data),
        "sha256": member.sha256,
        "sourceArchive": member.source_archive,
        "sourceArchiveSha256": member.source_archive_sha256,
    }


def prepare_default_player_facegen_preview(
    master_path: Path,
    owned_archives: OwnedArchiveStack,
    cache_root: Path,
    appearance: dict[str, object],
    configuration: RuntimeConfiguration,
) -> dict[str, object]:
    """Export the exact default male Player head and native geometry controls."""
    catalog = scan_actor_catalog(master_path)
    player = catalog.actors.get(PLAYER_RECORD_FORM_ID)
    if player is None or player.female or player.race_form_id is None:
        raise ValueError("Owned Player base is not the expected default male humanoid")
    player_contract = dict(appearance["player"])
    if int(str(player_contract["formId"]), FORM_ID_RADIX) != player.form_id:
        raise ValueError("Owned player preview base differs from the appearance contract")
    race = catalog.races.get(player.race_form_id)
    if race is None:
        raise ValueError("Owned player preview race is absent")
    race_contracts = {
        int(str(row["formId"]), FORM_ID_RADIX): dict(row)
        for row in appearance["races"]
    }
    race_contract = race_contracts.get(race.form_id)
    if race_contract is None:
        raise ValueError("Owned player preview race is not playable")
    sex_contract = dict(dict(race_contract["sex"])[PLAYER_PREVIEW_SEX])
    hair_form_id = int(str(sex_contract["defaultHairFormId"]), FORM_ID_RADIX)
    eyes_form_id = int(str(sex_contract["defaultEyesFormId"]), FORM_ID_RADIX)
    if hair_form_id != player.hair_form_id or eyes_form_id != player.eyes_form_id:
        raise ValueError("Owned default male player hair/eyes contract differs")
    hair = catalog.parts.get(hair_form_id)
    eyes = catalog.parts.get(eyes_form_id)
    if (
        hair is None
        or hair.model_path is None
        or eyes is None
        or eyes.texture_path is None
    ):
        raise ValueError("Owned player preview hair or eyes are incomplete")

    facegen = dict(player_contract["faceGen"])
    control_space = dict(facegen["controlSpace"])
    source_controls = {
        int(row["index"]): dict(row)
        for row in dict(control_space["format"])["controls"]["symmetricGeometry"]
    }
    exposed = [
        dict(row)
        for row in dict(control_space["nativeGeometryExposure"])["controls"]
    ]
    control_names = tuple(str(row["settingEntity"]) for row in exposed)
    control_axes = tuple(
        tuple(float(value) for value in source_controls[int(row["controlIndex"])]["axis"])
        for row in exposed
    )
    if (
        not control_names
        or len(control_names) != len(set(control_names))
        or any(len(axis) != FACEGEN_CONTROL_AXIS_FLOATS for axis in control_axes)
    ):
        raise ValueError("Owned player preview FaceGen controls are incomplete")

    extracted: dict[str, ExtractedMember] = {}

    def mesh(path: str) -> ExtractedMember:
        logical_path = _mesh_path(path)
        if logical_path not in extracted:
            extracted[logical_path] = owned_archives.extract(logical_path)
        return extracted[logical_path]

    def tri(path: str) -> dict[str, object]:
        logical_path = _mesh_path(model_companion(path, ".tri"))
        if logical_path not in owned_archives.members:
            return {}
        member = mesh(logical_path)
        return {"tri_path": member.logical_path, "tri_payload": member.data}

    def controlled_component(
        role: str,
        model_path: str,
        *,
        egm_path: str | None = None,
        **options: object,
    ) -> ActorComponent:
        model = mesh(model_path)
        egm = mesh(egm_path or model_companion(model_path, ".egm"))
        return ActorComponent(
            role,
            model.logical_path,
            model.data,
            egm_path=egm.logical_path,
            egm_payload=egm.data,
            egm_symmetric_control_names=control_names,
            egm_symmetric_control_axes=control_axes,
            **tri(model_path),
            **options,
        )

    head_models = race.male_head_models
    head_textures = race.male_head_textures
    head_model = head_models[RACE_HEAD_MODEL_INDEX]
    head_texture = head_textures[RACE_HEAD_MODEL_INDEX]
    if head_model is None or head_texture is None:
        raise ValueError("Owned default male player head model/texture is absent")
    symmetric_geometry = compose_facegen_coordinates(
        player.face_symmetric_geometry,
        race.male_face_symmetric_geometry,
    )
    asymmetric_geometry = compose_facegen_coordinates(
        player.face_asymmetric_geometry,
        race.male_face_asymmetric_geometry,
    )
    head_egt = mesh(model_companion(head_model, ".egt"))
    generated_face_detail = synthesize_texture_detail(
        head_egt.data,
        player.face_symmetric_texture,
    )
    components = [
        controlled_component(
            "head",
            head_model,
            diffuse_override=texture_member(head_texture),
            normal_override=texture_member(
                texture_companion(head_texture, NORMAL_TEXTURE_SUFFIX)
            ),
            generated_facegen_detail=generated_face_detail,
        )
    ]
    for index, role in RACE_HEAD_COMPONENT_ROLES.items():
        model_path = head_models[index]
        if model_path is None:
            raise ValueError(f"Owned default male player head component is absent: {role}")
        components.append(
            controlled_component(
                role,
                model_path,
                diffuse_override=(
                    texture_member(eyes.texture_path)
                    if role.startswith("eye-")
                    else None
                ),
            )
        )
    hair_egm = model_companion(hair.model_path, f"{HAIR_PREVIEW_SHAPE.lower()}.egm")
    components.append(
        controlled_component(
            "hair",
            hair.model_path,
            egm_path=hair_egm,
            selected_shape=HAIR_PREVIEW_SHAPE,
            tint_rgb=tuple(
                value / BYTE_CHANNEL_MAXIMUM for value in player.hair_color_rgba[:3]
            ),
        )
    )
    for part_form_id in player.head_part_form_ids:
        part = catalog.parts.get(part_form_id)
        if part is None or part.model_path is None:
            raise ValueError("Owned default male player head part is unresolved")
        components.append(
            controlled_component(
                f"head-part-{part.editor_id}",
                part.model_path,
            )
        )

    skeleton = mesh(player.skeleton_path or "")
    animation_path = str(
        configuration.document["actorCompiler"]["animationProfiles"]["NPC_"]["path"]
    )
    animation = mesh(animation_path)
    rig = configuration.actor_rig.profiles["NPC_"]
    output_root = cache_root / "generated" / "opening" / "player-facegen-preview" / PLAYER_PREVIEW_SEX
    gltf_path = output_root / "player-head.gltf"
    sidecar_path = output_root / "player-head.opennv.json"
    sidecar = export_actor_gltf(
        ActorGltfInput(
            f"{player.form_id:08x}",
            "PlayerPreview",
            skeleton.logical_path,
            skeleton.data,
            symmetric_geometry,
            asymmetric_geometry,
            tuple(components),
            animation.logical_path,
            animation.data,
            skeleton_root_node=rig.skeleton_root_node,
            rigid_attachment_node=rig.unparented_rigid_node,
            biped_head_node=configuration.actor_rig.biped_head_node,
        ),
        [owned_archives],
        gltf_path,
        sidecar_path,
        configuration.content_compiler,
    )
    return {
        "schema": PLAYER_FACEGEN_PREVIEW_SCHEMA,
        "status": PLAYER_FACEGEN_PREVIEW_STATUS,
        "playerFormId": f"{player.form_id:08x}",
        "raceFormId": f"{race.form_id:08x}",
        "sex": PLAYER_PREVIEW_SEX,
        "hairFormId": f"{hair_form_id:08x}",
        "eyesFormId": f"{eyes_form_id:08x}",
        "headPartFormIds": [f"{value:08x}" for value in player.head_part_form_ids],
        "geometryControlNames": list(control_names),
        "geometryControlCount": len(control_names),
        "sourceAssets": [
            _member_row(member)
            for member in sorted(extracted.values(), key=lambda value: value.logical_path)
        ],
        "outputs": {
            "gltf": str(gltf_path.resolve()),
            "gltfSha256": sidecar["outputs"]["gltf"]["sha256"],
            "sidecar": str(sidecar_path.resolve()),
            "sidecarSha256": hashlib.sha256(sidecar_path.read_bytes()).hexdigest(),
            "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
        },
        "runtimeDisposition": (
            "owned-default-male-preview-host-and-one-normalized-control-bound-"
            "other-identities-and-full-retail-slider-semantics-unimplemented"
        ),
    }
