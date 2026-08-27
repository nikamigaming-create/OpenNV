#!/usr/bin/env python3
"""Compile one classified NPC_ review from owned records and BSA assets."""

from __future__ import annotations

import hashlib
import json
import os
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path, PureWindowsPath

from actor_catalog import (
    FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
    FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
    FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
)
from actor_gltf import (
    ActorAnimation,
    ActorComponent,
    ActorGltfInput,
    export_actor_gltf,
    retail_render_parts_from_snapshot,
)
from actor_parity_records import float_array_contract
from actor_source_stack import (
    ActorSourceStack,
    SourcedRecord,
    build_actor_source_stack,
    parse_form_key,
    require_humanoid,
    require_part,
    require_race,
)
from bsa_archive import ExtractedMember, canonical_member_path
from facegen import (
    compose_body_albedo,
    compose_facegen_coordinates,
    synthesize_texture_detail,
)
from owned_archive_stack import OwnedArchiveStack, load_owned_archive_stack
from plugin_stack import (
    FORM_ID_OBJECT_BITS,
    FORM_ID_OBJECT_MASK,
    FORM_ID_RADIX,
    FormKey,
    file_sha256,
)
from prepare_actor import model_companion, texture_member
from prepare_creature_review import (
    ACTOR_REVIEW_SCENE_SCHEMA,
    COMPILED_PENDING_STATUS,
    EXIT_DATA_ERROR,
    MESH_ROOT,
    _asset_row,
    _load_json,
    _mesh_member,
    _retail_animation_paths,
    _validate_contract_sources,
    default_archive_recipe_path,
)
from runtime_configuration import load_runtime_configuration
from texture_pipeline import decode_dds


HUMANOID_RECORD_TYPE = "NPC_"
RACE_REQUIRED_HEAD_MODEL_COUNT = 8
RACE_REQUIRED_BODY_MODEL_COUNT = 3
RACE_HEAD_MODEL_INDEX = 0
RACE_LEFT_HAND_MODEL_INDEX = 1
RACE_RIGHT_HAND_MODEL_INDEX = 2
BYTE_CHANNEL_MAXIMUM = 255.0
NO_SOURCE_SLOT = 0xFFFFFFFF
NIF_SUFFIX = ".nif"
FACEGEN_TEXTURE_SUFFIX = "_0.dds"
NORMAL_TEXTURE_SUFFIX = "_n"
BODY_MOD_PREFIX = "modbody"
DATA_RESOLVED_MODELLESS_ROLES = frozenset({"face", "hair", "eyes", "headPart"})
BASE_COLOR_SEMANTIC = "baseColor"
FACEGEN_DETAIL_SEMANTIC = "faceGenDetail"
FACEGEN_SKIN_PARENT_NAME = "BSFaceGenNiNodeSkinned"
CAPTURED_SKIN_STATUS = "captured"
RACE_HEAD_COMPONENT_ROLES = {
    2: "mouth",
    3: "teeth-lower",
    4: "teeth-upper",
    5: "tongue",
    6: "eye-left",
    7: "eye-right",
}


@dataclass(frozen=True)
class RetailAttachmentModel:
    role: str
    source_form_id: str
    source_slot: int
    model_path: str
    base_color_paths: tuple[str, ...]
    skin_diffuse_paths: tuple[str, ...]


def _runtime_source_form(runtime_form_id: str) -> str:
    return f"0x{int(runtime_form_id, FORM_ID_RADIX):08X}"


def _linked_text(source: SourcedRecord, raw_form_id: int | None) -> str | None:
    key = source.linked_key(raw_form_id)
    return None if key is None else key.text


def _runtime_form_key(
    source_form_id: str,
    plugin_rows: list[dict[str, object]],
) -> FormKey | None:
    value = int(source_form_id, FORM_ID_RADIX)
    plugin_index = value >> FORM_ID_OBJECT_BITS
    if plugin_index >= len(plugin_rows):
        return None
    return FormKey(str(plugin_rows[plugin_index]["file"]), value & FORM_ID_OBJECT_MASK)


def _retail_visible_attachments(
    contract: dict[str, object],
    body_texture_paths: tuple[str, ...],
) -> tuple[RetailAttachmentModel, ...]:
    snapshot = contract["retail"]["appearance"]["snapshot"]
    authored_body_textures = {
        texture_member(path)
        for path in body_texture_paths
    }
    grouped: dict[tuple[str, str, int, str], tuple[set[str], set[str]]] = {}
    for part in snapshot["renderParts"]:
        if not (
            bool(part["required"])
            and bool(part["attached"])
            and bool(part["drawable"])
            and bool(part["visible"])
        ):
            continue
        role = str(part["role"])
        model_path = canonical_member_path(str(part["modelPath"])) if part["modelPath"] else ""
        if not model_path:
            if role not in DATA_RESOLVED_MODELLESS_ROLES:
                raise ValueError(f"Required retail {role} surface has no owned model identity")
            continue
        if PureWindowsPath(model_path).suffix.lower() != NIF_SUFFIX:
            raise ValueError(f"Retail attachment model is not a NIF: {model_path}")
        source_form_id = _runtime_source_form(str(part["sourceFormId"]))
        source_slot = int(part["sourceSlot"])
        if source_slot < 0 or source_slot > NO_SOURCE_SLOT:
            raise ValueError(f"Retail attachment has invalid source slot: {source_slot}")
        key = (role, source_form_id, source_slot, model_path)
        base_paths, skin_paths = grouped.setdefault(key, (set(), set()))
        bindings = tuple(part["textureBindings"])
        has_facegen_detail = any(
            str(binding["semantic"]) == FACEGEN_DETAIL_SEMANTIC
            for binding in bindings
        )
        for binding in bindings:
            if str(binding["semantic"]) != BASE_COLOR_SEMANTIC or not str(binding["path"]):
                continue
            path = canonical_member_path(str(binding["path"]))
            base_paths.add(path)
            if has_facegen_detail or path in authored_body_textures:
                skin_paths.add(path)
    return tuple(
        RetailAttachmentModel(
            *key,
            tuple(sorted(base_paths)),
            tuple(sorted(skin_paths)),
        )
        for key, (base_paths, skin_paths) in sorted(grouped.items())
    )


def _validate_humanoid_sources(
    contract: dict[str, object],
    sources: ActorSourceStack,
) -> tuple[SourcedRecord, SourcedRecord, SourcedRecord, SourcedRecord]:
    assembly = contract["assembly"]
    if assembly["recordType"] != HUMANOID_RECORD_TYPE:
        raise ValueError("Humanoid compiler received a non-NPC_ review contract")
    categories = assembly["categorySources"]
    base_source = sources.base(parse_form_key(str(assembly["baseFormKey"])))
    traits_source = sources.base(parse_form_key(str(categories["traits"])))
    model_source = sources.base(parse_form_key(str(categories["model"])))
    inventory_source = sources.base(parse_form_key(str(categories["inventory"])))
    base = require_humanoid(base_source)
    traits = require_humanoid(traits_source)
    model = require_humanoid(model_source)
    inventory = require_humanoid(inventory_source)
    if model.skeleton_path != assembly["skeletonPath"]:
        raise ValueError("Effective NPC_ skeleton differs from the review contract")
    expected_traits = {
        "sex": "female" if traits.female else "male",
        "race": _linked_text(traits_source, traits.race_form_id),
        "hair": _linked_text(traits_source, traits.hair_form_id),
        "eyes": _linked_text(traits_source, traits.eyes_form_id),
        "headParts": [
            _linked_text(traits_source, form_id) for form_id in traits.head_part_form_ids
        ],
        "hairColorRgba": list(traits.hair_color_rgba),
        "faceGen": {
            "symmetricGeometry": float_array_contract(traits.face_symmetric_geometry),
            "asymmetricGeometry": float_array_contract(traits.face_asymmetric_geometry),
            "symmetricTexture": float_array_contract(traits.face_symmetric_texture),
        },
    }
    actual_traits = {
        "sex": assembly["sex"],
        "race": assembly["race"]["key"],
        "hair": assembly["hair"]["key"],
        "eyes": assembly["eyes"]["key"],
        "headParts": [row["key"] for row in assembly["headParts"]],
        "hairColorRgba": assembly["hairColorRgba"],
        "faceGen": assembly["faceGen"],
    }
    if actual_traits != expected_traits:
        raise ValueError("Effective NPC_ traits differ from the review contract")
    expected_inventory = [
        {
            "item": {"key": _linked_text(inventory_source, row.form_id)},
            "count": row.count,
        }
        for row in inventory.inventory
    ]
    actual_inventory = [
        {"item": {"key": row["item"]["key"]}, "count": row["count"]}
        for row in assembly["inventory"]
    ]
    if actual_inventory != expected_inventory:
        raise ValueError("Effective NPC_ inventory differs from the review contract")
    if not base.name and not base.editor_id:
        raise ValueError("NPC_ review base has no display identity")
    return base_source, traits_source, model_source, inventory_source


def _extract_texture(archives: OwnedArchiveStack, logical_path: str) -> bytes:
    return archives.extract(texture_member(logical_path)).data


def _texture_companion(path: str, name_suffix: str) -> str:
    source = PureWindowsPath(canonical_member_path(path))
    if source.suffix.casefold() != ".dds":
        raise ValueError(f"Actor texture has no DDS suffix: {path}")
    return str(source.with_name(f"{source.stem}{name_suffix}{source.suffix}"))


def _has_texture(archives: OwnedArchiveStack, logical_path: str) -> bool:
    return texture_member(logical_path) in archives.members


def _hair_shape(
    attachments: tuple[RetailAttachmentModel, ...],
    sources: ActorSourceStack,
    plugin_rows: list[dict[str, object]],
) -> str:
    for attachment in attachments:
        if attachment.role != "equipment":
            continue
        key = _runtime_form_key(attachment.source_form_id, plugin_rows)
        armor_source = None if key is None else sources.armor.get(key)
        if armor_source is not None and armor_source.value.hides_hair:
            return "Hat"
    return "NoHat"


def _retail_face_geometry_name(contract: dict[str, object]) -> str:
    names: list[str] = []
    for shot in contract["retail"]["shots"]:
        for sample in shot["samples"]:
            matches = [
                str(instance["geometryName"])
                for instance in sample["skinPalette"]["instances"]
                if str(instance["status"]) == CAPTURED_SKIN_STATUS
                and str(instance["rootParentName"]) == FACEGEN_SKIN_PARENT_NAME
            ]
            if len(matches) != 1:
                raise ValueError(
                    "Retail NPC_ sample has no unique captured FaceGen skin geometry: "
                    f"frame={sample['frame']} matches={matches}"
                )
            names.extend(matches)
    if not names or len(set(names)) != 1:
        raise ValueError(f"Retail NPC_ FaceGen geometry identity changes across samples: {names}")
    return names[0]


def prepare_humanoid_review(
    data_root: Path,
    contract_path: Path,
    cache_root: Path,
    archive_recipe_path: Path,
) -> dict[str, object]:
    contract = _load_json(contract_path)
    contexts, corpus_manifest = _validate_contract_sources(
        data_root,
        contract,
        HUMANOID_RECORD_TYPE,
    )
    sources = build_actor_source_stack(contexts)
    base_source, traits_source, model_source, _inventory_source = _validate_humanoid_sources(
        contract, sources
    )
    base = require_humanoid(base_source)
    traits = require_humanoid(traits_source)
    model = require_humanoid(model_source)
    if model.skeleton_path is None:
        raise ValueError("NPC_ review model source has no skeleton")
    if (
        len(traits.face_symmetric_geometry) != FACEGEN_SYMMETRIC_GEOMETRY_FLOATS
        or len(traits.face_asymmetric_geometry) != FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS
        or len(traits.face_symmetric_texture) != FACEGEN_SYMMETRIC_TEXTURE_FLOATS
    ):
        raise ValueError("NPC_ review has incomplete FaceGen coordinates")

    race = require_race(sources.races[parse_form_key(str(contract["assembly"]["race"]["key"]))])
    hair_source = sources.parts[parse_form_key(str(contract["assembly"]["hair"]["key"]))]
    eyes_source = sources.parts[parse_form_key(str(contract["assembly"]["eyes"]["key"]))]
    hair = require_part(hair_source)
    eyes = require_part(eyes_source)
    head_parts = [
        require_part(sources.parts[parse_form_key(str(row["key"]))])
        for row in contract["assembly"]["headParts"]
    ]
    female = traits.female
    head_models = race.female_head_models if female else race.male_head_models
    head_textures = race.female_head_textures if female else race.male_head_textures
    body_models = race.female_body_models if female else race.male_body_models
    body_textures = race.female_body_textures if female else race.male_body_textures
    race_face_symmetric_geometry = (
        race.female_face_symmetric_geometry if female else race.male_face_symmetric_geometry
    )
    race_face_asymmetric_geometry = (
        race.female_face_asymmetric_geometry if female else race.male_face_asymmetric_geometry
    )
    race_face_symmetric_texture = (
        race.female_face_symmetric_texture if female else race.male_face_symmetric_texture
    )
    if (
        len(head_models) < RACE_REQUIRED_HEAD_MODEL_COUNT
        or len(head_textures) < RACE_REQUIRED_HEAD_MODEL_COUNT
        or len(body_models) < RACE_REQUIRED_BODY_MODEL_COUNT
        or len(body_textures) < RACE_REQUIRED_BODY_MODEL_COUNT
        or len(race_face_symmetric_geometry) != FACEGEN_SYMMETRIC_GEOMETRY_FLOATS
        or len(race_face_asymmetric_geometry) != FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS
        or len(race_face_symmetric_texture) != FACEGEN_SYMMETRIC_TEXTURE_FLOATS
    ):
        raise ValueError("NPC_ race has no complete sex-specific FaceGen assembly")
    symmetric_geometry = compose_facegen_coordinates(
        traits.face_symmetric_geometry,
        race_face_symmetric_geometry,
    )
    asymmetric_geometry = compose_facegen_coordinates(
        traits.face_asymmetric_geometry,
        race_face_asymmetric_geometry,
    )
    if hair.model_path is None or eyes.texture_path is None:
        raise ValueError("NPC_ review has incomplete hair or eye assets")

    configuration = load_runtime_configuration()
    actor_rig = configuration.actor_rig
    rig_profile = actor_rig.profiles["NPC_"]
    archives = load_owned_archive_stack(data_root, archive_recipe_path)

    def mesh(path: str) -> ExtractedMember:
        return archives.extract(_mesh_member(path))

    def facegen_tri(path: str) -> dict[str, object]:
        companion_path = _mesh_member(model_companion(path, ".tri"))
        if companion_path not in archives.members:
            return {}
        companion = archives.extract(companion_path)
        return {"tri_path": companion.logical_path, "tri_payload": companion.data}

    skeleton = mesh(model.skeleton_path)
    head_model = head_models[RACE_HEAD_MODEL_INDEX]
    head_texture = head_textures[RACE_HEAD_MODEL_INDEX]
    if head_model is None or head_texture is None:
        raise ValueError("NPC_ race has no sex-specific head model or texture")
    head = mesh(head_model)
    head_egm = mesh(model_companion(head_model, ".egm"))
    head_egt = mesh(model_companion(head_model, ".egt"))
    texture_owner = traits_source.key.owner_plugin.casefold()
    face_mod_path = (
        f"textures\\characters\\facemods\\{texture_owner}\\"
        f"{traits_source.key.object_id:08x}{FACEGEN_TEXTURE_SUFFIX}"
    )
    if _has_texture(archives, face_mod_path):
        face_detail_path = face_mod_path
        generated_face_detail = None
        face_detail_source = "retail-precomputed"
    else:
        face_detail_path = None
        generated_face_detail = synthesize_texture_detail(
            head_egt.data,
            traits.face_symmetric_texture,
        )
        face_detail_source = "direct-egt-synthesis"
    head_diffuse_path = texture_member(head_texture)
    head_normal_path = texture_member(
        _texture_companion(head_texture, NORMAL_TEXTURE_SUFFIX)
    )

    sex_label = "female" if female else "male"
    body_mod_path = (
        f"textures\\characters\\bodymods\\{texture_owner}\\"
        f"{traits_source.key.object_id:08x}{BODY_MOD_PREFIX}{sex_label}.dds"
    )
    attachments = _retail_visible_attachments(
        contract,
        tuple(path for path in body_textures if path is not None),
    )
    needs_body_mod = any(attachment.skin_diffuse_paths for attachment in attachments)
    body_mod = None
    if needs_body_mod:
        if not _has_texture(archives, body_mod_path):
            raise ValueError(f"NPC_ retail skin surfaces require missing body mod: {body_mod_path}")
        body_mod = decode_dds(_extract_texture(archives, body_mod_path), False)

    base_runtime_form = _runtime_source_form(str(contract["review"]["baseRuntimeFormId"]))
    retail_face_geometry_name = _retail_face_geometry_name(contract)
    retail_render_parts = retail_render_parts_from_snapshot(
        contract["retail"]["appearance"]["snapshot"]
    )
    components: list[ActorComponent] = [
        ActorComponent(
            "head",
            head.logical_path,
            head.data,
            egm_path=head_egm.logical_path,
            egm_payload=head_egm.data,
            **facegen_tri(head_model),
            diffuse_override=head_diffuse_path,
            normal_override=head_normal_path,
            facegen_detail_path=face_detail_path,
            generated_facegen_detail=generated_face_detail,
            source_form_id=base_runtime_form,
            source_slot=NO_SOURCE_SLOT,
            runtime_shape_name=retail_face_geometry_name,
        )
    ]
    extracted_models: list[tuple[str, ExtractedMember]] = [("head", head)]
    for index, role in RACE_HEAD_COMPONENT_ROLES.items():
        path = head_models[index]
        if path is None:
            raise ValueError(f"NPC_ race has no sex-specific head component {index}")
        member = mesh(path)
        egm = mesh(model_companion(path, ".egm"))
        source_form = (
            _runtime_source_form(str(contract["assembly"]["eyes"]["runtimeFormId"]))
            if role.startswith("eye-")
            else base_runtime_form
        )
        components.append(
            ActorComponent(
                role,
                member.logical_path,
                member.data,
                egm_path=egm.logical_path,
                egm_payload=egm.data,
                **facegen_tri(path),
                diffuse_override=(texture_member(eyes.texture_path) if role.startswith("eye-") else None),
                source_form_id=source_form,
                source_slot=NO_SOURCE_SLOT,
            )
        )
        extracted_models.append((role, member))

    plugin_rows = list(contract["provenance"]["officialPlugins"])
    selected_hair_shape = _hair_shape(attachments, sources, plugin_rows)
    hair_member = mesh(hair.model_path)
    hair_egm = mesh(model_companion(hair.model_path, f"{selected_hair_shape.casefold()}.egm"))
    hair_color = tuple(value / BYTE_CHANNEL_MAXIMUM for value in traits.hair_color_rgba[:3])
    components.append(
        ActorComponent(
            "hair",
            hair_member.logical_path,
            hair_member.data,
            egm_path=hair_egm.logical_path,
            egm_payload=hair_egm.data,
            **facegen_tri(hair.model_path),
            selected_shape=selected_hair_shape,
            tint_rgb=hair_color,
            source_form_id=_runtime_source_form(str(contract["assembly"]["hair"]["runtimeFormId"])),
            source_slot=NO_SOURCE_SLOT,
        )
    )
    extracted_models.append(("hair", hair_member))
    for part in head_parts:
        if part.model_path is None:
            continue
        member = mesh(part.model_path)
        egm = mesh(model_companion(part.model_path, ".egm"))
        components.append(
            ActorComponent(
                "head-part",
                member.logical_path,
                member.data,
                egm_path=egm.logical_path,
                egm_payload=egm.data,
                **facegen_tri(part.model_path),
                tint_rgb=hair_color,
                source_form_id=base_runtime_form,
                source_slot=NO_SOURCE_SLOT,
            )
        )
        extracted_models.append(("head-part", member))

    runtime_attachment_assets: list[tuple[RetailAttachmentModel, ExtractedMember]] = []
    for attachment in attachments:
        member = mesh(attachment.model_path)
        generated_by_source = ()
        if attachment.skin_diffuse_paths:
            assert body_mod is not None
            generated_by_source = tuple(
                (
                    path,
                    compose_body_albedo(
                        decode_dds(_extract_texture(archives, path), False),
                        body_mod,
                    ),
                )
                for path in attachment.skin_diffuse_paths
            )
        components.append(
            ActorComponent(
                attachment.role,
                member.logical_path,
                member.data,
                generated_diffuse_by_source=generated_by_source,
                source_form_id=attachment.source_form_id,
                source_slot=attachment.source_slot,
            )
        )
        runtime_attachment_assets.append((attachment, member))
        extracted_models.append((attachment.role, member))

    primary_animation_path, additional_animation_paths = _retail_animation_paths(contract)
    primary_animation = mesh(primary_animation_path)
    additional_animations = [mesh(path) for path in additional_animation_paths]
    review_key = str(contract["review"]["reviewKey"])
    stable_id = hashlib.sha256(review_key.encode("utf-8")).hexdigest()[
        : configuration.content_compiler.stable_id_hex_characters
    ]
    final_root = cache_root / "generated" / "actor-reviews" / stable_id
    if final_root.exists():
        raise FileExistsError(f"Refusing to overwrite humanoid review cache: {final_root}")
    final_root.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=f"{stable_id}-", dir=final_root.parent) as directory:
        staging_root = Path(directory)
        gltf_path = staging_root / "actor.gltf"
        sidecar_path = staging_root / "actor.opennv.json"
        sidecar = export_actor_gltf(
            ActorGltfInput(
                review_key,
                base.name or base.editor_id,
                model.skeleton_path,
                skeleton.data,
                symmetric_geometry,
                asymmetric_geometry,
                tuple(components),
                primary_animation.logical_path,
                primary_animation.data,
                skeleton_root_node=rig_profile.skeleton_root_node,
                rigid_attachment_node=rig_profile.unparented_rigid_node,
                biped_head_node=actor_rig.biped_head_node,
                additional_animations=tuple(
                    ActorAnimation(member.logical_path, member.data)
                    for member in additional_animations
                ),
                retail_render_parts=retail_render_parts,
            ),
            [archives],
            gltf_path,
            sidecar_path,
            configuration.content_compiler,
        )
        scene = {
            "schema": ACTOR_REVIEW_SCENE_SCHEMA,
            "status": COMPILED_PENDING_STATUS,
            "reviewKey": review_key,
            "baseFormKey": contract["review"]["baseFormKey"],
            "recordType": HUMANOID_RECORD_TYPE,
            "configuration": configuration.manifest(),
            "source": {
                "plugin": base_source.context.name,
                "pluginSha256": base_source.context.sha256,
                "localFormId": f"{base_source.key.object_id:08x}",
                "categorySources": contract["assembly"]["categorySources"],
                "skeleton": _asset_row(skeleton),
                "models": [
                    {"role": role, "asset": _asset_row(member)}
                    for role, member in extracted_models
                ],
                "runtimeAttachments": [
                    {
                        "role": attachment.role,
                        "sourceFormId": attachment.source_form_id,
                        "sourceSlot": attachment.source_slot,
                        "baseColorPaths": list(attachment.base_color_paths),
                        "skinDiffusePaths": list(attachment.skin_diffuse_paths),
                        "asset": _asset_row(member),
                    }
                    for attachment, member in runtime_attachment_assets
                ],
                "faceDetail": {
                    "source": face_detail_source,
                    "logicalPath": face_mod_path if face_detail_source == "retail-precomputed" else head_egt.logical_path,
                    "materialStatus": "retail-four-sampler-contract",
                },
                "retailFaceGeometryName": retail_face_geometry_name,
                "bodyModLogicalPath": body_mod_path if needs_body_mod else None,
                "hairShape": selected_hair_shape,
                "animations": [
                    _asset_row(member)
                    for member in (primary_animation, *additional_animations)
                ],
                "archiveStack": archives.manifest(),
            },
            "retailContract": {
                "path": str(contract_path.resolve()),
                "sha256": file_sha256(contract_path),
                "projectionStatus": "exact-retail-final-eye-d3d9-perspective",
                "animationLayersRetained": True,
                "faceGenMaterialStatus": "retail-four-sampler-contract",
            },
            "corpusManifest": {
                "path": contract["provenance"]["corpusManifest"]["path"],
                "sha256": file_sha256(Path(str(contract["provenance"]["corpusManifest"]["path"]))),
                "status": corpus_manifest["status"],
            },
            "outputs": {
                "gltf": gltf_path.name,
                "gltfSha256": sidecar["outputs"]["gltf"]["sha256"],
                "sidecar": sidecar_path.name,
                "sidecarSha256": file_sha256(sidecar_path),
                "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
            },
            "coverage": sidecar["coverage"],
            "evidencePolicy": {
                "compiledCacheIsNotVisualEvidence": True,
                "faceGenMaterialParityPendingMatchedCapture": True,
                "godotEvidenceStatus": "pending",
                "matchedComparisonStatus": "pending",
            },
        }
        scene_path = staging_root / "actor-review-scene.json"
        scene_path.write_text(json.dumps(scene, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        os.replace(staging_root, final_root)
    result_path = final_root / "actor-review-scene.json"
    scene["manifest"] = str(result_path.resolve())
    return scene


def main() -> int:
    import argparse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--review-contract", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--archive-recipe", type=Path, default=default_archive_recipe_path())
    args = parser.parse_args()
    try:
        scene = prepare_humanoid_review(
            args.data_root.resolve(),
            args.review_contract.resolve(),
            args.cache_root.resolve(),
            args.archive_recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_HUMANOID_REVIEW_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_HUMANOID_REVIEW "
        + json.dumps(
            {
                "manifest": scene["manifest"],
                "reviewKey": scene["reviewKey"],
                "status": scene["status"],
                "coverage": scene["coverage"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
