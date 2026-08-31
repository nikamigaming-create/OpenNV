"""Compile source-owned default player-selection previews with live EGM controls."""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path

from actor_catalog import scan_actor_catalog
from actor_gltf import (
    ActorAnimation,
    ActorComponent,
    ActorGltfInput,
    NifFormat,
    _is_dismember_cap_shape,
    _text,
    export_actor_gltf,
)
from actor_material import actor_texture_paths
from bsa_archive import ExtractedMember, canonical_member_path
from facegen import (
    compose_facegen_coordinates,
    synthesize_texture_detail,
)
from nif_decoder import decode_nif
from owned_archive_stack import OwnedArchiveStack
from prepare_actor import (
    RACE_HEAD_COMPONENT_ROLES,
    RACE_HEAD_MODEL_INDEX,
    RACE_LEFT_HAND_MODEL_INDEX,
    RACE_REQUIRED_BODY_MODEL_COUNT,
    RACE_RIGHT_HAND_MODEL_INDEX,
    model_companion,
    texture_companion,
    texture_member,
)
from runtime_configuration import RuntimeConfiguration


PLAYER_FACEGEN_PREVIEW_SCHEMA = "opennv-owned-player-facegen-preview/v1"
PLAYER_FACEGEN_FULL_BODY_PREVIEW_SCHEMA = "opennv-owned-player-facegen-preview-set/v3"
PLAYER_FACEGEN_PLAYABLE_RACE_PREVIEW_SCHEMA = (
    "opennv-owned-player-facegen-preview-set/v5"
)
PLAYER_FACEGEN_PREVIEW_STATUS = (
    "compiled-default-male-head-with-ctl-egm-targets-all-native-geometry-controls-"
    "runtime-bound"
)
PLAYER_FACEGEN_FULL_BODY_PREVIEW_STATUS = (
    "compiled-default-male-and-female-full-body-live-previews-with-ctl-egm-targets-"
    "all-native-geometry-controls-runtime-bound"
)
PLAYER_FACEGEN_FULL_BODY_RUNTIME_DISPOSITION = (
    "owned-default-male-and-female-selection-preview-hosts-and-all-native-geometry-"
    "controls-bound-other-identities-fail-closed-sibling-gamebryo-slider-semantics-"
    "corroborated"
)
PLAYER_FACEGEN_PLAYABLE_RACE_PREVIEW_STATUS = (
    "compiled-playable-race-male-and-female-valid-hair-eye-full-body-live-previews-"
    "with-ctl-egm-targets-all-native-geometry-controls-runtime-bound"
)
PLAYER_FACEGEN_PLAYABLE_RACE_RUNTIME_DISPOSITION = (
    "owned-playable-race-male-and-female-valid-hair-eye-identity-preview-hosts-"
    "and-all-native-geometry-controls-bound-invalid-source-tuples-fail-closed-"
    "sibling-gamebryo-slider-semantics-corroborated"
)
PLAYER_FACEGEN_PLAYABLE_RACE_SELECTION_SCOPE = (
    "all-playable-race-sex-valid-hair-eyes-cartesian-product"
)
PLAYER_FACEGEN_PLAYABLE_RACE_UNSUPPORTED_SCOPE = (
    "invalid-race-sex-hair-eyes-source-tuple"
)
PLAYER_FACEGEN_HEAD_RUNTIME_DISPOSITION = (
    "owned-default-male-preview-host-and-all-native-geometry-controls-bound-"
    "other-identities-unimplemented-sibling-gamebryo-slider-semantics-"
    "corroborated"
)
PLAYER_FULL_BODY_COMPONENT_ROLES = ("body", "left-hand", "right-hand")
PLAYER_PREVIEW_SEXES = ("male", "female")
PLAYER_RECORD_FORM_ID = 0x00000007
PLAYER_PREVIEW_SEX = "male"
HAIR_PREVIEW_SHAPE = "NoHat"
NORMAL_TEXTURE_SUFFIX = "_n"
FACEGEN_CONTROL_AXIS_FLOATS = 50
FORM_ID_RADIX = 16
BYTE_CHANNEL_MAXIMUM = 255.0


@dataclass(frozen=True)
class PlayerBodyComponentSource:
    role: str
    model_path: str
    texture_path: str
    bake_shape_transform: bool
    use_source_materials: bool = False


@dataclass(frozen=True)
class PlayerPreviewSelection:
    sex: str
    race_form_id: int
    hair_form_id: int
    eyes_form_id: int


def _player_preview_selections(
    appearance: dict[str, object],
    player_form_id: int,
) -> tuple[PlayerPreviewSelection, ...]:
    player = dict(appearance["player"])
    if int(str(player["formId"]), FORM_ID_RADIX) != player_form_id:
        raise ValueError(
            "Owned player preview base differs from the appearance contract"
        )
    race_form_id = int(str(player["defaultRaceFormId"]), FORM_ID_RADIX)
    races = [
        dict(row)
        for row in appearance["races"]
        if int(str(row["formId"]), FORM_ID_RADIX) == race_form_id
    ]
    if len(races) != 1:
        raise ValueError("Owned player preview default race selection is not unique")
    sex_contracts = dict(races[0]["sex"])
    if set(sex_contracts) != set(PLAYER_PREVIEW_SEXES):
        raise ValueError("Owned player preview sex selections are incomplete")
    selections = tuple(
        PlayerPreviewSelection(
            sex,
            race_form_id,
            int(str(dict(sex_contracts[sex])["defaultHairFormId"]), FORM_ID_RADIX),
            int(str(dict(sex_contracts[sex])["defaultEyesFormId"]), FORM_ID_RADIX),
        )
        for sex in PLAYER_PREVIEW_SEXES
    )
    identities = {
        (row.sex, row.race_form_id, row.hair_form_id, row.eyes_form_id)
        for row in selections
    }
    if len(identities) != len(selections):
        raise ValueError("Owned player preview selection identities are not unique")
    return selections


def _playable_race_preview_selections(
    appearance: dict[str, object],
    player_form_id: int,
) -> tuple[PlayerPreviewSelection, ...]:
    player = dict(appearance["player"])
    if int(str(player["formId"]), FORM_ID_RADIX) != player_form_id:
        raise ValueError(
            "Owned player preview base differs from the appearance contract"
        )
    selections = []
    for race in sorted(
        (dict(row) for row in appearance["races"]),
        key=lambda row: int(str(row["formId"]), FORM_ID_RADIX),
    ):
        race_form_id = int(str(race["formId"]), FORM_ID_RADIX)
        sex_contracts = dict(race["sex"])
        if set(sex_contracts) != set(PLAYER_PREVIEW_SEXES):
            raise ValueError(
                "Owned player preview playable race sex selections are incomplete"
            )
        for sex in PLAYER_PREVIEW_SEXES:
            source = dict(sex_contracts[sex])
            hair_ids = sorted(
                int(str(dict(row)["formId"]), FORM_ID_RADIX)
                for row in source["hairOptions"]
            )
            eye_ids = sorted(
                int(str(dict(row)["formId"]), FORM_ID_RADIX)
                for row in source["eyeOptions"]
            )
            if (
                not hair_ids
                or not eye_ids
                or len(set(hair_ids)) != len(hair_ids)
                or len(set(eye_ids)) != len(eye_ids)
            ):
                raise ValueError(
                    "Owned player preview valid hair/eye inventory is incomplete"
                )
            selections.extend(
                PlayerPreviewSelection(sex, race_form_id, hair_form_id, eyes_form_id)
                for hair_form_id in hair_ids
                for eyes_form_id in eye_ids
            )
    identities = {
        (row.sex, row.race_form_id, row.hair_form_id, row.eyes_form_id)
        for row in selections
    }
    if not selections or len(identities) != len(selections):
        raise ValueError("Owned player preview valid identities are not unique")
    return tuple(selections)


def _player_body_component_sources(
    race: object,
    sex: str,
) -> tuple[PlayerBodyComponentSource, ...]:
    if sex not in PLAYER_PREVIEW_SEXES:
        raise ValueError(f"Unsupported owned player body sex: {sex}")
    models = tuple(getattr(race, f"{sex}_body_models"))
    textures = tuple(getattr(race, f"{sex}_body_textures"))
    if (
        len(models) < RACE_REQUIRED_BODY_MODEL_COUNT
        or len(textures) < RACE_REQUIRED_BODY_MODEL_COUNT
    ):
        raise ValueError("Owned default male player body table is incomplete")
    rows = (
        PlayerBodyComponentSource(
            PLAYER_FULL_BODY_COMPONENT_ROLES[0],
            models[RACE_HEAD_MODEL_INDEX],
            textures[RACE_HEAD_MODEL_INDEX],
            False,
        ),
        PlayerBodyComponentSource(
            PLAYER_FULL_BODY_COMPONENT_ROLES[1],
            models[RACE_LEFT_HAND_MODEL_INDEX],
            textures[RACE_LEFT_HAND_MODEL_INDEX],
            sex == "male",
        ),
        PlayerBodyComponentSource(
            PLAYER_FULL_BODY_COMPONENT_ROLES[2],
            models[RACE_RIGHT_HAND_MODEL_INDEX],
            textures[RACE_RIGHT_HAND_MODEL_INDEX],
            sex == "male",
        ),
    )
    if any(not row.model_path or not row.texture_path for row in rows):
        raise ValueError("Owned default male player body component is absent")
    return rows


def _mesh_path(path: str) -> str:
    canonical = canonical_member_path(path)
    return canonical if canonical.startswith("meshes\\") else f"meshes\\{canonical}"


def _head_only_facegen_assembly(include_full_body: bool) -> bool:
    """Keep rigid FaceGen parts in the same space as the selected actor body."""
    return not include_full_body


def _with_outfit_body(
    rows: tuple[PlayerBodyComponentSource, ...],
    model_path: str,
    texture_path: str,
) -> tuple[PlayerBodyComponentSource, ...]:
    """Replace the nude torso with one owned, skinned outfit module."""
    if tuple(row.role for row in rows) != PLAYER_FULL_BODY_COMPONENT_ROLES:
        raise ValueError("Owned player body roles cannot accept an outfit module")
    if not model_path or not texture_path:
        raise ValueError("Owned player outfit module is incomplete")
    return (
        PlayerBodyComponentSource(
            PLAYER_FULL_BODY_COMPONENT_ROLES[0],
            model_path,
            texture_path,
            False,
            True,
        ),
        *rows[1:],
    )


def _primary_actor_diffuse_path(payload: bytes) -> str:
    document = decode_nif(payload).document
    for shape in document.get_global_iterator():
        if (
            not isinstance(shape, (NifFormat.NiTriShape, NifFormat.NiTriStrips))
            or shape.data is None
            or _is_dismember_cap_shape(shape)
        ):
            continue
        paths = actor_texture_paths(
            [
                prop
                for prop in getattr(shape, "properties", ())
                if prop is not None
            ]
        )
        if paths and paths[0]:
            return paths[0]
    raise ValueError("Owned player outfit module has no retained authored diffuse")


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
    *,
    include_full_body: bool = False,
    presentation_outfit_form_id: int | None = None,
    include_locomotion_animation: bool = False,
    include_all_playable_race_selections: bool = False,
) -> dict[str, object]:
    """Export exact Player previews for the requested owned selection scope."""
    catalog = scan_actor_catalog(master_path)
    player = catalog.actors.get(PLAYER_RECORD_FORM_ID)
    if player is None or player.female or player.race_form_id is None:
        raise ValueError("Owned Player base is not the expected default male humanoid")
    player_contract = dict(appearance["player"])
    if include_all_playable_race_selections and not include_full_body:
        raise ValueError("Playable-race FaceGen previews require full-body assembly")
    if include_all_playable_race_selections:
        selections = _playable_race_preview_selections(
            appearance,
            player.form_id,
        )
    elif include_full_body:
        selections = _player_preview_selections(appearance, player.form_id)
    else:
        if int(str(player_contract["formId"]), FORM_ID_RADIX) != player.form_id:
            raise ValueError(
                "Owned player preview base differs from the appearance contract"
            )
        race_contracts = {
            int(str(row["formId"]), FORM_ID_RADIX): dict(row)
            for row in appearance["races"]
        }
        race_contract = race_contracts.get(player.race_form_id)
        if race_contract is None:
            raise ValueError("Owned player preview race is not playable")
        sex_contract = dict(dict(race_contract["sex"])[PLAYER_PREVIEW_SEX])
        selections = (
            PlayerPreviewSelection(
                PLAYER_PREVIEW_SEX,
                player.race_form_id,
                int(str(sex_contract["defaultHairFormId"]), FORM_ID_RADIX),
                int(str(sex_contract["defaultEyesFormId"]), FORM_ID_RADIX),
            ),
        )
    expected_selection_count = (
        sum(
            len(dict(sex)["hairOptions"]) * len(dict(sex)["eyeOptions"])
            for race in appearance["races"]
            for sex in dict(dict(race)["sex"]).values()
        )
        if include_all_playable_race_selections
        else len(PLAYER_PREVIEW_SEXES) if include_full_body else 1
    )
    if len(selections) != expected_selection_count:
        raise ValueError("Owned player preview selection inventory is incomplete")
    default_race = catalog.races.get(player.race_form_id)
    if default_race is None:
        raise ValueError("Owned player preview race is absent")
    selection_races = {
        row.race_form_id: catalog.races.get(row.race_form_id)
        for row in selections
    }
    if any(race is None for race in selection_races.values()):
        raise ValueError("Owned player preview playable race is absent")
    if not any(
        row.sex == PLAYER_PREVIEW_SEX
        and row.race_form_id == player.race_form_id
        and row.hair_form_id == player.hair_form_id
        and row.eyes_form_id == player.eyes_form_id
        for row in selections
    ):
        raise ValueError("Owned default male player hair/eyes contract differs")

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
    source_texture_controls = {
        int(row["index"]): dict(row)
        for row in dict(control_space["format"])["controls"]["symmetricTexture"]
    }
    exposed_texture = [
        dict(row)
        for row in dict(control_space["nativeTextureExposure"])["controls"]
    ]
    texture_control_names = tuple(
        str(row["settingEntity"]) for row in exposed_texture
    )
    texture_control_axes = tuple(
        tuple(
            float(value)
            for value in source_texture_controls[int(row["controlIndex"])]["axis"]
        )
        for row in exposed_texture
    )
    if (
        not texture_control_names
        or len(texture_control_names) != len(set(texture_control_names))
        or any(len(axis) != FACEGEN_CONTROL_AXIS_FLOATS for axis in texture_control_axes)
    ):
        raise ValueError("Owned player preview FaceGen texture controls are incomplete")

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

    def body_source_manifest(row: PlayerBodyComponentSource) -> dict[str, object]:
        model = mesh(row.model_path)
        diffuse = owned_archives.extract(texture_member(row.texture_path))
        normal = owned_archives.extract(
            texture_member(texture_companion(row.texture_path, NORMAL_TEXTURE_SUFFIX))
        )
        document = decode_nif(model.data).document
        unsupported = [
            type(block).__name__
            for block in document.get_global_iterator()
            if isinstance(block, NifFormat.NiGeometry)
            and not isinstance(block, (NifFormat.NiTriShape, NifFormat.NiTriStrips))
        ]
        if unsupported:
            raise ValueError(
                f"Owned player {row.role} has unsupported source geometry: {unsupported}"
            )
        authored = [
            block
            for block in document.get_global_iterator()
            if isinstance(block, (NifFormat.NiTriShape, NifFormat.NiTriStrips))
            and block.data is not None
        ]
        retained = [block for block in authored if not _is_dismember_cap_shape(block)]
        if not retained:
            raise ValueError(f"Owned player {row.role} has no retained source surface")
        return {
            "role": row.role,
            "modelLogicalPath": model.logical_path,
            "modelSha256": model.sha256,
            "sourceSurfaceCount": len(authored),
            "retainedSurfaceCount": len(retained),
            "retainedSurfaceNames": [_text(value.name) for value in retained],
            "omittedDismemberCapSurfaceCount": len(authored) - len(retained),
            "diffuseLogicalPath": diffuse.logical_path,
            "diffuseSha256": diffuse.sha256,
            "normalLogicalPath": normal.logical_path,
            "normalSha256": normal.sha256,
            "shapeTransformDisposition": (
                "bake-authored-shape-transform"
                if row.bake_shape_transform
                else "preserve-authored-skinned-shape-transform"
            ),
        }

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

    body_components_by_sex = {
        sex: _player_body_component_sources(default_race, sex)
        for sex in PLAYER_PREVIEW_SEXES
    }
    for race in selection_races.values():
        if race is None:
            raise ValueError("Owned player preview playable race is absent")
        for sex in PLAYER_PREVIEW_SEXES:
            if _player_body_component_sources(race, sex) != body_components_by_sex[sex]:
                raise ValueError(
                    "Owned playable races do not share one source body contract"
                )
    if presentation_outfit_form_id is not None:
        outfit = catalog.armor.get(presentation_outfit_form_id)
        if outfit is None:
            raise ValueError(
                "Owned player presentation outfit ARMO is unresolved: "
                f"{presentation_outfit_form_id:08x}"
            )
        for sex in PLAYER_PREVIEW_SEXES:
            model_path = (
                outfit.male_model_path
                if sex == PLAYER_PREVIEW_SEX
                else outfit.female_model_path
            )
            if model_path is None:
                raise ValueError(
                    f"Owned player presentation outfit lacks its {sex} model"
                )
            outfit_model = mesh(model_path)
            body_components_by_sex[sex] = _with_outfit_body(
                body_components_by_sex[sex],
                model_path,
                _primary_actor_diffuse_path(outfit_model.data),
            )
    skeleton = mesh(player.skeleton_path or "")
    animation_path = str(
        configuration.document["actorCompiler"]["animationProfiles"]["NPC_"]["path"]
    )
    animation = mesh(animation_path)
    rig = configuration.actor_rig.profiles["NPC_"]
    preview_rows = []
    for selection in selections:
        race = selection_races[selection.race_form_id]
        if race is None:
            raise ValueError("Owned player preview playable race is absent")
        hair = catalog.parts.get(selection.hair_form_id)
        eyes = catalog.parts.get(selection.eyes_form_id)
        if (
            hair is None
            or hair.model_path is None
            or eyes is None
            or eyes.texture_path is None
        ):
            raise ValueError(
                f"Owned {selection.sex} player preview hair or eyes are incomplete"
            )
        head_models = tuple(getattr(race, f"{selection.sex}_head_models"))
        head_textures = tuple(getattr(race, f"{selection.sex}_head_textures"))
        head_model = head_models[RACE_HEAD_MODEL_INDEX]
        head_texture = head_textures[RACE_HEAD_MODEL_INDEX]
        if head_model is None or head_texture is None:
            raise ValueError(
                f"Owned {selection.sex} player head model/texture is absent"
            )
        symmetric_geometry = compose_facegen_coordinates(
            player.face_symmetric_geometry,
            tuple(getattr(race, f"{selection.sex}_face_symmetric_geometry")),
        )
        asymmetric_geometry = compose_facegen_coordinates(
            player.face_asymmetric_geometry,
            tuple(getattr(race, f"{selection.sex}_face_asymmetric_geometry")),
        )
        head_egt = mesh(model_companion(head_model, ".egt"))
        symmetric_texture = compose_facegen_coordinates(
            player.face_symmetric_texture,
            tuple(getattr(race, f"{selection.sex}_face_symmetric_texture")),
        )
        generated_face_detail = synthesize_texture_detail(
            head_egt.data,
            symmetric_texture,
        )
        body_components = (
            body_components_by_sex[selection.sex] if include_full_body else ()
        )
        components = [
            ActorComponent(
                row.role,
                mesh(row.model_path).logical_path,
                mesh(row.model_path).data,
                diffuse_override=(
                    None
                    if row.use_source_materials
                    else texture_member(row.texture_path)
                ),
                normal_override=(
                    None
                    if row.use_source_materials
                    else texture_member(
                        texture_companion(row.texture_path, NORMAL_TEXTURE_SUFFIX)
                    )
                ),
                bake_shape_transform=row.bake_shape_transform,
            )
            for row in body_components
        ]
        components.append(
            controlled_component(
                "head",
                head_model,
                diffuse_override=texture_member(head_texture),
                normal_override=texture_member(
                    texture_companion(head_texture, NORMAL_TEXTURE_SUFFIX)
                ),
                generated_facegen_detail=generated_face_detail,
            )
        )
        for index, role in RACE_HEAD_COMPONENT_ROLES.items():
            model_path = head_models[index]
            if model_path is None:
                raise ValueError(
                    f"Owned {selection.sex} player head component is absent: {role}"
                )
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
        hair_egm = model_companion(
            hair.model_path,
            f"{HAIR_PREVIEW_SHAPE.lower()}.egm",
        )
        components.append(
            controlled_component(
                "hair",
                hair.model_path,
                egm_path=hair_egm,
                selected_shape=HAIR_PREVIEW_SHAPE,
                tint_rgb=tuple(
                    value / BYTE_CHANNEL_MAXIMUM
                    for value in player.hair_color_rgba[:3]
                ),
            )
        )
        for part_form_id in player.head_part_form_ids:
            part = catalog.parts.get(part_form_id)
            if part is None or part.model_path is None:
                raise ValueError("Owned Player head part is unresolved")
            components.append(
                controlled_component(
                    f"head-part-{part.editor_id}",
                    part.model_path,
                )
            )

        output_root = (
            cache_root
            / "generated"
            / "opening"
            / "player-facegen-preview"
            / selection.sex
        )
        if include_all_playable_race_selections:
            output_root /= (
                f"{selection.race_form_id:08x}-"
                f"{selection.hair_form_id:08x}-"
                f"{selection.eyes_form_id:08x}"
            )
        output_name = "player-full-body" if include_full_body else "player-head"
        gltf_path = output_root / f"{output_name}.gltf"
        sidecar_path = output_root / f"{output_name}.opennv.json"
        egt_path = output_root / "player-head.egt"
        egt_path.parent.mkdir(parents=True, exist_ok=True)
        egt_path.write_bytes(head_egt.data)
        locomotion_animations: tuple[ActorAnimation, ...] = ()
        if include_full_body and include_locomotion_animation:
            locomotion = mesh(
                "characters\\_male\\locomotion\\"
                f"{selection.sex}\\mtforward.kf"
            )
            locomotion_animations = (
                ActorAnimation(locomotion.logical_path, locomotion.data),
            )
        sidecar = export_actor_gltf(
            ActorGltfInput(
                f"{player.form_id:08x}",
                f"PlayerPreview-{selection.sex}",
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
                additional_animations=locomotion_animations,
                head_only_facegen_preview=_head_only_facegen_assembly(
                    include_full_body
                ),
            ),
            [owned_archives],
            gltf_path,
            sidecar_path,
            configuration.content_compiler,
        )
        preview_rows.append(
            {
                "raceFormId": f"{selection.race_form_id:08x}",
                "sex": selection.sex,
                "hairFormId": f"{selection.hair_form_id:08x}",
                "eyesFormId": f"{selection.eyes_form_id:08x}",
                "headPartFormIds": [
                    f"{value:08x}" for value in player.head_part_form_ids
                ],
                "outputs": {
                    "gltf": str(gltf_path.resolve()),
                    "gltfSha256": sidecar["outputs"]["gltf"]["sha256"],
                    "sidecar": str(sidecar_path.resolve()),
                    "sidecarSha256": hashlib.sha256(
                        sidecar_path.read_bytes()
                    ).hexdigest(),
                    "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
                    "egt": str(egt_path.resolve()),
                    "egtSha256": head_egt.sha256,
                },
                "symmetricTexture": list(symmetric_texture),
                "textureControls": [
                    {
                        "controlIndex": int(row["controlIndex"]),
                        "settingEntity": str(row["settingEntity"]),
                        "sourceLabel": str(row["sourceLabel"]),
                        "axisSha256": str(row["axisSha256"]),
                        "axis": list(axis),
                    }
                    for row, axis in zip(exposed_texture, texture_control_axes)
                ],
            }
        )

    common = {
        "playerFormId": f"{player.form_id:08x}",
        "geometryControlNames": list(control_names),
        "geometryControlCount": len(control_names),
        "textureControlNames": list(texture_control_names),
        "textureControlCount": len(texture_control_names),
        "fullBody": include_full_body,
        "presentationOutfitFormId": (
            f"{presentation_outfit_form_id:08x}"
            if presentation_outfit_form_id is not None
            else None
        ),
        "bodyComponentRoles": (
            list(PLAYER_FULL_BODY_COMPONENT_ROLES) if include_full_body else []
        ),
        "bodyComponentSourcesBySex": {
            sex: [body_source_manifest(row) for row in rows]
            for sex, rows in body_components_by_sex.items()
        },
        "sourceAssets": [
            _member_row(member)
            for member in sorted(
                extracted.values(),
                key=lambda value: value.logical_path,
            )
        ],
    }
    if include_full_body:
        expanded = include_all_playable_race_selections
        return {
            "schema": (
                PLAYER_FACEGEN_PLAYABLE_RACE_PREVIEW_SCHEMA
                if expanded
                else PLAYER_FACEGEN_FULL_BODY_PREVIEW_SCHEMA
            ),
            "status": (
                PLAYER_FACEGEN_PLAYABLE_RACE_PREVIEW_STATUS
                if expanded
                else PLAYER_FACEGEN_FULL_BODY_PREVIEW_STATUS
            ),
            **common,
            **(
                {
                    "selectionScope": PLAYER_FACEGEN_PLAYABLE_RACE_SELECTION_SCOPE,
                    "unsupportedSelectionScope": (
                        PLAYER_FACEGEN_PLAYABLE_RACE_UNSUPPORTED_SCOPE
                    ),
                }
                if expanded
                else {}
            ),
            "previews": preview_rows,
            "runtimeDisposition": (
                PLAYER_FACEGEN_PLAYABLE_RACE_RUNTIME_DISPOSITION
                if expanded
                else PLAYER_FACEGEN_FULL_BODY_RUNTIME_DISPOSITION
            ),
        }
    return {
        "schema": PLAYER_FACEGEN_PREVIEW_SCHEMA,
        "status": PLAYER_FACEGEN_PREVIEW_STATUS,
        **common,
        **preview_rows[0],
        "runtimeDisposition": PLAYER_FACEGEN_HEAD_RUNTIME_DISPOSITION,
    }
