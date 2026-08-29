#!/usr/bin/env python3
"""Prepare one recipe-pinned retail actor through the clean direct pipeline."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

from actor_catalog import (
    ActorCatalog,
    ActorReference,
    FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
    FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
    FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
    HumanoidActor,
    resolve_actor_outfit_form_ids,
    scan_actor_catalog,
)
from actor_gltf import (
    ActorAnimation,
    ActorComponent,
    ActorGltfInput,
    actor_skin_diffuse_paths,
    export_actor_gltf,
    retail_render_parts_from_snapshot,
)
from bsa_archive import BsaArchive, canonical_member_path
from cell_catalog import scan_cell_catalog
from cell_scene import (
    arrival_transform,
    godot_position,
    godot_rotation_quaternion,
    godot_yaw_radians,
    load_spatial_recipe,
)
from facegen import (
    compose_body_albedo,
    compose_facegen_coordinates,
    synthesize_texture_detail,
)
from gallery_actor_presentation import load_gallery_actor_presentation
from texture_pipeline import decode_dds
from runtime_configuration import RuntimeConfiguration, load_runtime_configuration
from compiler_provenance import compiler_provenance


RECIPE_SCHEMA = "opennv-actor-recipe/v1"
FORM_ID_RADIX = 16
RACE_REQUIRED_HEAD_MODEL_COUNT = 8
RACE_REQUIRED_BODY_MODEL_COUNT = 3
BYTE_CHANNEL_MAXIMUM = 255.0
RACE_HEAD_MODEL_INDEX = 0
RACE_LEFT_HAND_MODEL_INDEX = 1
RACE_RIGHT_HAND_MODEL_INDEX = 2
NORMAL_TEXTURE_SUFFIX = "_n"
RACE_HEAD_COMPONENT_ROLES = {
    2: "mouth",
    3: "teeth-lower",
    4: "teeth-upper",
    5: "tongue",
    6: "eye-left",
    7: "eye-right",
}
NO_SOURCE_SLOT = 0xFFFFFFFF
RETAIL_ROLE_BY_COMPONENT_ROLE = {
    "head": "face",
    "eye-left": "eyes",
    "eye-right": "eyes",
    "mouth": "headPart",
    "teeth-lower": "headPart",
    "teeth-upper": "headPart",
    "tongue": "headPart",
    "hair": "hair",
}


def file_sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def load_recipe(recipe_id: str) -> dict[str, object]:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    path = root / "recipes" / f"{recipe_id}.json"
    recipe = json.loads(path.read_text(encoding="utf-8"))
    if recipe.get("schema") != RECIPE_SCHEMA or recipe.get("id") != recipe_id:
        raise ValueError(f"Unexpected OpenNV actor recipe: {path}")
    return recipe


def load_recipe_file(path: Path) -> dict[str, object]:
    recipe = json.loads(path.read_text(encoding="utf-8"))
    if (
        not isinstance(recipe, dict)
        or recipe.get("schema") != RECIPE_SCHEMA
        or not str(recipe.get("id", "")).strip()
    ):
        raise ValueError(f"Unexpected OpenNV actor recipe: {path}")
    return recipe


def form_id(value: str) -> int:
    return int(value, FORM_ID_RADIX)


def model_companion(path: str, suffix: str) -> str:
    if not path.lower().endswith(".nif"):
        raise ValueError(f"Actor model has no NIF suffix: {path}")
    return path[:-4] + suffix


def texture_member(path: str) -> str:
    canonical = canonical_member_path(path)
    return canonical if canonical.startswith("textures\\") else f"textures\\{canonical}"


def texture_companion(path: str, name_suffix: str) -> str:
    canonical = canonical_member_path(path)
    if not canonical.endswith(".dds"):
        raise ValueError(f"Actor texture has no DDS suffix: {path}")
    return canonical[:-4] + name_suffix + canonical[-4:]


def extract_texture(archives: Sequence[BsaArchive], logical_path: str) -> bytes:
    path = texture_member(logical_path)
    matches = [archive for archive in archives if path in archive.members]
    if len(matches) != 1:
        raise FileNotFoundError(f"Expected one actor texture {path!r}, found {len(matches)}")
    return matches[0].extract(path).data


def has_texture(archives: Sequence[BsaArchive], logical_path: str) -> bool:
    path = texture_member(logical_path)
    return sum(path in archive.members for archive in archives) == 1


def retail_component_identity(
    appearance: dict[str, object],
    component_role: str,
) -> tuple[str, int]:
    retail_role = (
        "headPart"
        if component_role.startswith("head-part-")
        else RETAIL_ROLE_BY_COMPONENT_ROLE.get(component_role, component_role)
    )
    render_parts = appearance.get("renderParts")
    if not isinstance(render_parts, list):
        raise ValueError("Retail actor appearance has no render parts")
    identities = {
        (str(part["sourceFormId"]), int(part["sourceSlot"]))
        for part in render_parts
        if isinstance(part, dict)
        and str(part.get("role", "")) == retail_role
        and bool(part.get("required"))
        and bool(part.get("attached"))
        and bool(part.get("drawable"))
        and bool(part.get("visible"))
    }
    if len(identities) != 1:
        raise ValueError(
            "Retail actor component has no unique source identity: "
            f"{component_role} -> {retail_role} ({sorted(identities)})"
        )
    source_form_id, source_slot = identities.pop()
    return f"0x{int(source_form_id, FORM_ID_RADIX):08X}", source_slot


def retail_hair_shape(appearance: dict[str, object]) -> str:
    render_parts = appearance.get("renderParts")
    if not isinstance(render_parts, list):
        raise ValueError("Retail actor appearance has no render parts")
    names = {
        str(part["geometryName"])
        for part in render_parts
        if isinstance(part, dict)
        and part.get("role") == "hair"
        and bool(part.get("required"))
        and bool(part.get("attached"))
        and bool(part.get("drawable"))
        and bool(part.get("visible"))
    }
    shapes = {
        name.removeprefix("FaceGenHair")
        for name in names
        if name in {"FaceGenHairHat", "FaceGenHairNoHat"}
    }
    if len(shapes) != 1:
        raise ValueError(f"Retail actor has no unique visible hair shape: {sorted(names)}")
    return shapes.pop()


def retail_surface_texture(
    appearance: dict[str, object],
    retail_role: str,
    runtime_geometry_name: str,
    semantic: str,
) -> str:
    render_parts = appearance.get("renderParts")
    if not isinstance(render_parts, list):
        raise ValueError("Retail actor appearance has no render parts")
    paths = {
        canonical_member_path(str(binding["path"]))
        for part in render_parts
        if isinstance(part, dict)
        and str(part.get("role", "")) == retail_role
        and str(part.get("geometryName", "")) == runtime_geometry_name
        and bool(part.get("required"))
        and bool(part.get("attached"))
        and bool(part.get("drawable"))
        and bool(part.get("visible"))
        for binding in part.get("textureBindings", [])
        if isinstance(binding, dict)
        and str(binding.get("semantic", "")) == semantic
        and str(binding.get("path", ""))
    }
    if len(paths) != 1:
        raise ValueError(
            "Retail actor surface has no unique texture binding: "
            f"{retail_role}/{runtime_geometry_name}/{semantic} ({sorted(paths)})"
        )
    return paths.pop()


def resolve_proof_actor(
    catalog: ActorCatalog,
    reference_form_id: int,
    cell_form_id: int,
) -> tuple[ActorReference, HumanoidActor]:
    references = [
        reference
        for reference in catalog.references_for(cell_form_id)
        if reference.form_id == reference_form_id and reference.record_type == "ACHR"
    ]
    if len(references) != 1:
        raise ValueError(f"Expected one proof ACHR {reference_form_id:08x}, found {len(references)}")
    actor = catalog.actors.get(references[0].actor_form_id)
    if actor is None:
        raise ValueError(f"Proof ACHR has no NPC_ base: {references[0].actor_form_id:08x}")
    if actor.race_form_id is None or actor.skeleton_path is None:
        raise ValueError("Proof actor does not contain the required race/skeleton identity")
    if (len(actor.face_symmetric_geometry), len(actor.face_asymmetric_geometry), len(actor.face_symmetric_texture)) != (
        FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
        FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
        FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
    ):
        raise ValueError("Proof actor has incomplete FaceGen coordinates")
    return references[0], actor


@dataclass(frozen=True)
class ActorPreparationContext:
    configuration: RuntimeConfiguration
    source_contract: tuple[tuple[Path, str], ...]
    master: Path
    catalog: ActorCatalog
    mesh_archives: tuple[BsaArchive, ...]
    texture_archives: tuple[BsaArchive, ...]


def _actor_source_contract(
    data_root: Path,
    recipe: dict[str, object],
) -> tuple[tuple[Path, str], ...]:
    rows = (
        recipe["master"],
        recipe["meshesArchive"],
        *recipe.get("additionalMeshesArchives", []),
        *recipe["textureArchives"],
    )
    return tuple(
        (
            (data_root / str(row["file"])).resolve(),
            str(row["sha256"]).lower(),
        )
        for row in rows
    )


def create_actor_preparation_context(
    data_root: Path,
    recipe: dict[str, object],
    verified_source_contract: tuple[tuple[Path, str], ...] | None = None,
) -> ActorPreparationContext:
    source_contract = _actor_source_contract(data_root, recipe)
    if verified_source_contract is not None:
        if verified_source_contract != source_contract:
            raise ValueError(
                "Verified actor source contract differs from the requested recipe"
            )
    else:
        for path, expected_hash in source_contract:
            if not path.is_file():
                raise FileNotFoundError(path)
            actual = file_sha256(path)
            if actual.lower() != expected_hash:
                raise ValueError(
                    f"Actor recipe source hash mismatch: {path.name} "
                    f"expected={expected_hash} actual={actual}"
                )
    master = source_contract[0][0]
    mesh_archive_count = 1 + len(recipe.get("additionalMeshesArchives", []))
    return ActorPreparationContext(
        load_runtime_configuration(),
        source_contract,
        master,
        scan_actor_catalog(master),
        tuple(
            BsaArchive(path)
            for path, _source_hash in source_contract[1 : 1 + mesh_archive_count]
        ),
        tuple(
            BsaArchive(path)
            for path, _source_hash in source_contract[1 + mesh_archive_count :]
        ),
    )


def prepare_actor(
    data_root: Path,
    cache_root: Path,
    recipe_id: str,
    recipe_document: dict[str, object] | None = None,
    preparation_context: ActorPreparationContext | None = None,
    runtime_animation_paths: Sequence[str] = (),
    family_compiler: dict[str, str] | None = None,
) -> dict[str, object]:
    recipe = load_recipe(recipe_id) if recipe_document is None else recipe_document
    if recipe.get("schema") != RECIPE_SCHEMA or not str(recipe.get("id", "")).strip():
        raise ValueError("Actor recipe document has an invalid schema or ID")
    recipe_id = str(recipe["id"])
    context = preparation_context or create_actor_preparation_context(data_root, recipe)
    if context.source_contract != _actor_source_contract(data_root, recipe):
        raise ValueError("Actor preparation context belongs to another owned-data recipe")
    configuration = context.configuration
    actor_rig = configuration.actor_rig
    rig_profile = actor_rig.profiles["NPC_"]
    master = context.master
    catalog = context.catalog
    reference, actor = resolve_proof_actor(
        catalog,
        form_id(recipe["proofActorReferenceFormId"]),
        form_id(recipe["cellFormId"]),
    )
    expected_base = recipe.get("expectedBaseFormId")
    if expected_base is not None and actor.form_id != form_id(str(expected_base)):
        raise ValueError(
            "Actor recipe reference resolves another base: "
            f"expected={form_id(str(expected_base)):08x} actual={actor.form_id:08x}"
        )
    if "actorState" in recipe:
        raise ValueError(
            "Per-actor compiler state is unsupported; use the shared owned-animation profile"
        )
    retail_presentation = (
        load_gallery_actor_presentation(
            recipe["retailEvidence"],
            str(recipe["proofActorReferenceFormId"]),
            str(recipe["expectedBaseFormId"]),
        )
        if "retailEvidence" in recipe
        else None
    )
    if retail_presentation is None:
        animation_profile = configuration.document["actorCompiler"][
            "animationProfiles"
        ]["NPC_"]
        actor_animation_path = str(animation_profile["path"])
        actor_animation_paths = (actor_animation_path,)
    else:
        actor_animation_paths = tuple(
            sequence.logical_path for sequence in retail_presentation.animations
        )
        actor_animation_path = actor_animation_paths[0]
    actor_animation_paths = tuple(
        dict.fromkeys(
            (
                *actor_animation_paths,
                *runtime_animation_paths,
                *(str(row["path"]) for row in recipe.get("additionalAnimations", [])),
            )
        )
    )
    configured_origin = recipe.get("originGameUnits")
    if configured_origin is None:
        cell_recipe = load_spatial_recipe(str(recipe["cellRecipe"]))
        cell_catalog = scan_cell_catalog(master)
        _source_door, arrival = arrival_transform(
            cell_catalog,
            form_id(cell_recipe["entryDoorReferenceFormId"]),
        )
        origin = arrival.position
    else:
        origin = tuple(float(value) for value in configured_origin)
        if len(origin) != 3:
            raise ValueError("Actor recipe originGameUnits must contain three values")
    race = catalog.races.get(actor.race_form_id)
    head_models = race.female_head_models if actor.female and race is not None else (
        race.male_head_models if race is not None else ()
    )
    head_textures = race.female_head_textures if actor.female and race is not None else (
        race.male_head_textures if race is not None else ()
    )
    body_models = race.female_body_models if actor.female and race is not None else (
        race.male_body_models if race is not None else ()
    )
    body_textures = race.female_body_textures if actor.female and race is not None else (
        race.male_body_textures if race is not None else ()
    )
    race_face_symmetric_geometry = (
        race.female_face_symmetric_geometry if actor.female and race is not None else
        race.male_face_symmetric_geometry if race is not None else ()
    )
    race_face_asymmetric_geometry = (
        race.female_face_asymmetric_geometry if actor.female and race is not None else
        race.male_face_asymmetric_geometry if race is not None else ()
    )
    race_face_symmetric_texture = (
        race.female_face_symmetric_texture if actor.female and race is not None else
        race.male_face_symmetric_texture if race is not None else ()
    )
    if (
        race is None
        or len(head_models) < RACE_REQUIRED_HEAD_MODEL_COUNT
        or len(head_textures) < 1
        or len(body_models) < RACE_REQUIRED_BODY_MODEL_COUNT
        or len(body_textures) < RACE_REQUIRED_BODY_MODEL_COUNT
    ):
        raise ValueError("Proof actor race has no complete sex-specific head/body table")
    hair = catalog.parts.get(actor.hair_form_id or 0)
    eyes = catalog.parts.get(actor.eyes_form_id or 0)
    head_parts = [catalog.parts.get(part) for part in actor.head_part_form_ids]
    if hair is None or hair.model_path is None or eyes is None or eyes.texture_path is None:
        raise ValueError("Proof actor has incomplete hair or eye records")
    if any(part is None for part in head_parts):
        raise ValueError("Proof actor has an unresolved head-part record")
    explicit_outfit_models = [str(value) for value in recipe.get("outfitModelPaths", [])]
    if explicit_outfit_models:
        outfit_models = explicit_outfit_models
        outfit_forms = [
            form_id(str(value)) for value in recipe.get("outfitIdentityFormIds", [])
        ]
        if len(outfit_forms) != len(outfit_models):
            raise ValueError("Explicit actor outfit models require one identity FormID each")
        outfits = []
    else:
        outfit_forms = list(resolve_actor_outfit_form_ids(catalog, actor))
        outfits = [catalog.armor.get(value) for value in outfit_forms]
        if not outfits or any(outfit is None for outfit in outfits):
            raise ValueError(f"Proof actor has unresolved outfit armor: {outfit_forms}")
        outfit_models = [
            outfit.female_model_path if actor.female else outfit.male_model_path
            for outfit in outfits
        ]
    if any(path is None for path in outfit_models):
        raise ValueError("Proof actor outfit lacks a sex-specific model")

    mesh_archives = context.mesh_archives
    texture_archives = context.texture_archives

    def mesh(path: str) -> bytes:
        canonical = canonical_member_path(path)
        logical_path = canonical if canonical.startswith("meshes\\") else f"meshes\\{canonical}"
        matches = [archive for archive in mesh_archives if logical_path in archive.members]
        if len(matches) != 1:
            raise FileNotFoundError(
                f"Expected one actor mesh {logical_path!r}, found {len(matches)}"
            )
        return matches[0].extract(logical_path).data

    def facegen_tri(path: str) -> dict[str, object]:
        companion = model_companion(path, ".tri")
        canonical = canonical_member_path(companion)
        logical_path = canonical if canonical.startswith("meshes\\") else f"meshes\\{canonical}"
        matches = [archive for archive in mesh_archives if logical_path in archive.members]
        if not matches:
            return {}
        if len(matches) != 1:
            raise ValueError(
                f"Expected one actor TRI {logical_path!r}, found {len(matches)}"
            )
        return {"tri_path": logical_path, "tri_payload": matches[0].extract(logical_path).data}

    outfit_payloads = [
        (str(outfit_model), mesh(str(outfit_model)))
        for outfit_model in outfit_models
    ]

    head_model = head_models[RACE_HEAD_MODEL_INDEX]
    head_texture = head_textures[RACE_HEAD_MODEL_INDEX]
    if head_model is None or head_texture is None:
        raise ValueError("Proof actor race has no sex-specific head model or texture")
    head_egm = model_companion(head_model, ".egm")
    head_egt = model_companion(head_model, ".egt")
    if (
        len(race_face_symmetric_geometry),
        len(race_face_asymmetric_geometry),
        len(race_face_symmetric_texture),
    ) != (
        FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
        FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
        FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
    ):
        raise ValueError("Proof actor race has incomplete sex-specific FaceGen baseline coordinates")
    symmetric_geometry = compose_facegen_coordinates(
        actor.face_symmetric_geometry,
        race_face_symmetric_geometry,
    )
    asymmetric_geometry = compose_facegen_coordinates(
        actor.face_asymmetric_geometry,
        race_face_asymmetric_geometry,
    )
    texture_owner = master.name.casefold()
    face_mod_path = (
        f"textures\\characters\\facemods\\{texture_owner}\\{actor.form_id:08x}_0.dds"
    )
    if has_texture(texture_archives, face_mod_path):
        face_detail_path = face_mod_path
        generated_face_detail = None
        face_detail_source = "retail-precomputed"
    else:
        face_detail_path = None
        generated_face_detail = synthesize_texture_detail(
            mesh(head_egt),
            actor.face_symmetric_texture,
        )
        face_detail_source = "direct-egt-synthesis"
    head_diffuse_path = texture_member(head_texture)
    head_normal_path = texture_member(
        texture_companion(head_texture, NORMAL_TEXTURE_SUFFIX)
    )
    body_texture = body_textures[RACE_HEAD_MODEL_INDEX]
    if (
        body_texture is None
        or body_models[RACE_LEFT_HAND_MODEL_INDEX] is None
        or body_models[RACE_RIGHT_HAND_MODEL_INDEX] is None
    ):
        raise ValueError("Proof actor race has no sex-specific upper-body texture or hand meshes")
    sex_label = "female" if actor.female else "male"
    body_mod_path = (
        f"textures\\characters\\bodymods\\{texture_owner}\\"
        f"{actor.form_id:08x}modbody{sex_label}.dds"
    )
    body_mod_policy = str(recipe.get("bodyModPolicy", "require-retail-precomputed"))
    if body_mod_policy not in {
        "require-retail-precomputed",
        "owned-race-base-diffuse-when-precomputed-absent",
    }:
        raise ValueError(f"Unsupported actor body-mod policy: {body_mod_policy}")
    if has_texture(texture_archives, body_mod_path):
        body_mod = decode_dds(extract_texture(texture_archives, body_mod_path), False)
        body_surface_texture_source = "retail-precomputed-body-mod-composite"
    elif (
        body_mod_policy == "owned-race-base-diffuse-when-precomputed-absent"
        and retail_presentation is None
    ):
        body_mod = None
        body_surface_texture_source = "owned-race-base-diffuse-no-body-mod"
    else:
        raise ValueError("Proof actor has no retail precomputed body-mod texture")
    left_hand_texture = body_textures[RACE_LEFT_HAND_MODEL_INDEX]
    right_hand_texture = body_textures[RACE_RIGHT_HAND_MODEL_INDEX]
    if left_hand_texture is None or right_hand_texture is None:
        raise ValueError("Proof actor race has no sex-specific hand textures")
    generated_left_hand = (
        compose_body_albedo(
            decode_dds(extract_texture(texture_archives, left_hand_texture), False),
            body_mod,
        )
        if body_mod is not None
        else None
    )
    generated_right_hand = (
        compose_body_albedo(
            decode_dds(extract_texture(texture_archives, right_hand_texture), False),
            body_mod,
        )
        if body_mod is not None
        else None
    )

    components = []
    if retail_presentation is None:
        for index, (outfit_model, outfit_payload) in enumerate(outfit_payloads):
            skin_paths = actor_skin_diffuse_paths(outfit_payload)
            generated_skin = (
                tuple(
                    (
                        source,
                        compose_body_albedo(
                            decode_dds(extract_texture(texture_archives, source), False),
                            body_mod,
                        ),
                    )
                    for source in skin_paths
                )
                if body_mod is not None
                else ()
            )
            components.append(
                ActorComponent(
                    f"outfit-{index}",
                    outfit_model,
                    outfit_payload,
                    excluded_shape_prefixes=tuple(
                        str(value)
                        for value in recipe.get("excludeOutfitShapePrefixes", [])
                    ),
                    generated_diffuse_by_source=generated_skin,
                )
            )
        components.extend(
            [
                ActorComponent(
                    "left-hand",
                    body_models[RACE_LEFT_HAND_MODEL_INDEX],
                    mesh(body_models[RACE_LEFT_HAND_MODEL_INDEX]),
                    generated_diffuse=generated_left_hand,
                    bake_shape_transform=not actor.female,
                ),
                ActorComponent(
                    "right-hand",
                    body_models[RACE_RIGHT_HAND_MODEL_INDEX],
                    mesh(body_models[RACE_RIGHT_HAND_MODEL_INDEX]),
                    generated_diffuse=generated_right_hand,
                    bake_shape_transform=not actor.female,
                ),
            ]
        )
    else:
        for attachment in retail_presentation.visible_attachments:
            attachment_payload = mesh(attachment.model_path)
            generated_skin = tuple(
                (
                    source,
                    compose_body_albedo(
                        decode_dds(extract_texture(texture_archives, source), False),
                        body_mod,
                    ),
                )
                for source in actor_skin_diffuse_paths(attachment_payload)
            )
            components.append(
                ActorComponent(
                    attachment.role,
                    attachment.model_path,
                    attachment_payload,
                    generated_diffuse_by_source=generated_skin,
                    source_form_id=attachment.source_form_id,
                    source_slot=attachment.source_slot,
                )
            )

    head_identity = (
        retail_component_identity(retail_presentation.appearance, "head")
        if retail_presentation is not None
        else (None, None)
    )
    components.append(
        ActorComponent(
            "head",
            head_model,
            mesh(head_model),
            egm_path=head_egm,
            egm_payload=mesh(head_egm),
            **facegen_tri(head_model),
            diffuse_override=head_diffuse_path,
            normal_override=head_normal_path,
            facegen_detail_path=face_detail_path,
            generated_facegen_detail=generated_face_detail,
            source_form_id=head_identity[0],
            source_slot=head_identity[1],
        )
    )
    for index, role in RACE_HEAD_COMPONENT_ROLES.items():
        path = head_models[index]
        if path is None:
            raise ValueError(f"Proof actor race has no sex-specific head component {index}")
        source_identity = (
            retail_component_identity(retail_presentation.appearance, role)
            if retail_presentation is not None
            else (None, None)
        )
        components.append(
            ActorComponent(
                role,
                path,
                mesh(path),
                egm_path=model_companion(path, ".egm"),
                egm_payload=mesh(model_companion(path, ".egm")),
                **facegen_tri(path),
                diffuse_override=texture_member(eyes.texture_path) if role.startswith("eye-") else None,
                source_form_id=source_identity[0],
                source_slot=source_identity[1],
            )
        )
    hair_shape = (
        retail_hair_shape(retail_presentation.appearance)
        if retail_presentation is not None
        else str(recipe["hairShape"])
        if "hairShape" in recipe
        else "Hat"
        if any(outfit.hides_hair for outfit in outfits)
        else "NoHat"
    )
    hair_egm = model_companion(hair.model_path, f"{hair_shape.lower()}.egm")
    hair_identity = (
        retail_component_identity(retail_presentation.appearance, "hair")
        if retail_presentation is not None
        else (None, None)
    )
    components.append(
        ActorComponent(
            "hair",
            hair.model_path,
            mesh(hair.model_path),
            egm_path=hair_egm,
            egm_payload=mesh(hair_egm),
            **facegen_tri(hair.model_path),
            selected_shape=hair_shape,
            tint_rgb=tuple(value / BYTE_CHANNEL_MAXIMUM for value in actor.hair_color_rgba[:3]),
            source_form_id=hair_identity[0],
            source_slot=hair_identity[1],
        )
    )
    for part in (part for part in head_parts if part.model_path is not None):
        source_identity = (
            retail_component_identity(
                retail_presentation.appearance,
                f"head-part-{part.editor_id}",
            )
            if retail_presentation is not None
            else (None, None)
        )
        components.append(
            ActorComponent(
                f"head-part-{part.editor_id}",
                part.model_path,
                mesh(part.model_path),
                egm_path=model_companion(part.model_path, ".egm"),
                egm_payload=mesh(model_companion(part.model_path, ".egm")),
                **facegen_tri(part.model_path),
                diffuse_override=(
                    retail_surface_texture(
                        retail_presentation.appearance,
                        "headPart",
                        "FaceGenAccessory",
                        "headPartColor",
                    )
                    if retail_presentation is not None
                    else None
                ),
                normal_override=(
                    retail_surface_texture(
                        retail_presentation.appearance,
                        "headPart",
                        "FaceGenAccessory",
                        "headPartNormal",
                    )
                    if retail_presentation is not None
                    else None
                ),
                tint_rgb=tuple(
                    value / BYTE_CHANNEL_MAXIMUM for value in actor.hair_color_rgba[:3]
                ),
                source_form_id=source_identity[0],
                source_slot=source_identity[1],
            )
        )

    output_root = cache_root / "generated" / "actors" / recipe_id
    gltf_path = output_root / "actor.gltf"
    sidecar_path = output_root / "actor.opennv.json"
    for row in recipe.get("additionalAnimations", []):
        payload = mesh(str(row["path"]))
        actual_hash = hashlib.sha256(payload).hexdigest()
        if actual_hash != str(row["sha256"]):
            raise ValueError(
                f"Actor animation hash mismatch: {row['path']} "
                f"expected={row['sha256']} actual={actual_hash}"
            )
    sidecar = export_actor_gltf(
        ActorGltfInput(
            f"{actor.form_id:08x}",
            actor.name,
            actor.skeleton_path,
            mesh(actor.skeleton_path),
            symmetric_geometry,
            asymmetric_geometry,
            tuple(components),
            actor_animation_path,
            mesh(actor_animation_path),
            skeleton_root_node=rig_profile.skeleton_root_node,
            rigid_attachment_node=rig_profile.unparented_rigid_node,
            biped_head_node=actor_rig.biped_head_node,
            additional_animations=tuple(
                ActorAnimation(path, mesh(path))
                for path in actor_animation_paths[1:]
            ),
            retail_render_parts=(
                retail_render_parts_from_snapshot(retail_presentation.appearance)
                if retail_presentation is not None
                else ()
            ),
        ),
        texture_archives,
        gltf_path,
        sidecar_path,
        configuration.content_compiler,
    )
    manifest = {
        "schema": "opennv-actor-scene/v5",
        "status": "skinned-animated",
        "compiler": family_compiler or compiler_provenance("actor"),
        "recipe": recipe_id,
        "configuration": configuration.manifest(),
        "cellFormId": recipe["cellFormId"],
        "reference": {
            "formId": f"{reference.form_id:08x}",
            "baseFormId": f"{reference.actor_form_id:08x}",
            "initiallyDisabled": reference.initially_disabled,
            "positionGameUnits": list(reference.position),
            "positionGodotUnits": godot_position(reference.position, origin),
            "rotationRadians": list(reference.rotation_radians),
            "yawRadians": reference.rotation_radians[2],
            "yawGodotRadians": godot_yaw_radians(reference.rotation_radians[2]),
            "rotationGodotQuaternion": godot_rotation_quaternion(reference.rotation_radians),
            "scale": reference.scale,
        },
        "actor": {
            "name": actor.name,
            "editorId": actor.editor_id,
            "female": actor.female,
            "raceFormId": f"{actor.race_form_id:08x}",
            "hairFormId": f"{actor.hair_form_id:08x}",
            "eyesFormId": f"{actor.eyes_form_id:08x}",
            "headPartFormIds": [f"{part:08x}" for part in actor.head_part_form_ids],
            "outfitFormIds": [f"{outfit_form:08x}" for outfit_form in outfit_forms],
            "recordType": "NPC_",
        },
        "idleAnimation": actor_animation_path,
        "retailPresentation": (
            {
                "evidencePath": str(retail_presentation.evidence_path),
                "evidenceSha256": retail_presentation.evidence_sha256,
                "oraclePath": str(retail_presentation.oracle_path),
                "oracleSha256": retail_presentation.oracle_sha256,
                "presentationFrame": retail_presentation.presentation_frame,
                "actorSnapshotEventSha256": (
                    retail_presentation.actor_snapshot_event_sha256
                ),
                "actorPoseEventSha256": retail_presentation.actor_pose_event_sha256,
                "appearanceFrame": retail_presentation.appearance_frame,
                "appearanceEventSha256": retail_presentation.appearance_event_sha256,
                "weaponForm": retail_presentation.weapon_form,
                "weaponOut": retail_presentation.weapon_out,
                "visibleWeapon": (
                    None
                    if retail_presentation.visible_weapon is None
                    else {
                        "sourceFormId": retail_presentation.visible_weapon.source_form_id,
                        "sourceSlot": retail_presentation.visible_weapon.source_slot,
                        "modelPath": retail_presentation.visible_weapon.model_path,
                    }
                ),
                "animationStack": [
                    {
                        "logicalPath": sequence.logical_path,
                        "state": sequence.state,
                        "cycle": sequence.cycle,
                        "weight": sequence.weight,
                        "frequency": sequence.frequency,
                        "phaseSeconds": sequence.phase_seconds,
                        "group": sequence.group,
                    }
                    for sequence in retail_presentation.animations
                ],
                "selection": "ordered-active-retail-animation-data-stack",
            }
            if retail_presentation is not None
            else None
        ),
        "appearanceResolution": {
            "outfitSource": "NPC_.CNTO recursively resolved through deterministic LVLI",
            "hairShape": hair_shape,
            "hairShapeSource": "equipped ARMO.BMDT hair-slot flag",
            "dismemberCaps": "excluded by BSDismemberBodyPartType semantics",
            "rigidAttachments": "derived from NIF skin-instance presence",
            "faceGenMaterialSource": configuration.document["actorCompiler"][
                "faceGenMaterial"
            ]["source"],
            "status": configuration.document["actorCompiler"]["provenance"]["status"],
        },
        "faceDetailSource": face_detail_source,
        "faceDetailLogicalPath": face_mod_path if face_detail_source == "retail-precomputed" else head_egt,
        "bodyModLogicalPath": body_mod_path if body_mod is not None else None,
        "bodyModPolicy": body_mod_policy,
        "bodySurfaceTextureSource": body_surface_texture_source,
        "outputs": {
            "gltf": gltf_path.name,
            "sidecar": sidecar_path.name,
            "gltfSha256": sidecar["outputs"]["gltf"]["sha256"],
            "sidecarSha256": file_sha256(sidecar_path),
            "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
        },
        "coverage": sidecar["coverage"],
    }
    manifest_path = output_root / "actor-scene.json"
    _atomic_json(manifest_path, manifest)
    manifest["manifest"] = str(manifest_path.resolve())
    return manifest


def _atomic_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def prepare_actor_set(
    data_root: Path,
    cache_root: Path,
    recipe_ids: list[str],
    runtime_animation_paths_by_reference: dict[str, Sequence[str]] | None = None,
    family_compiler: dict[str, str] | None = None,
) -> dict[str, object]:
    if len(recipe_ids) < 1 or len(set(recipe_ids)) != len(recipe_ids):
        raise ValueError("Actor-set recipes must be non-empty and unique")
    runtime_animation_paths_by_reference = runtime_animation_paths_by_reference or {}
    recipes = [load_recipe(recipe_id) for recipe_id in recipe_ids]
    actors = [
        prepare_actor(
            data_root,
            cache_root,
            recipe_id,
            recipe_document=recipe,
            runtime_animation_paths=runtime_animation_paths_by_reference.get(
                str(recipe["proofActorReferenceFormId"]).casefold(),
                (),
            ),
            family_compiler=family_compiler,
        )
        for recipe_id, recipe in zip(recipe_ids, recipes, strict=True)
    ]
    reference_form_ids = {str(actor["reference"]["formId"]) for actor in actors}
    if len(reference_form_ids) != len(actors):
        raise ValueError("Actor-set members must use unique references")
    document = {
        "schema": "opennv-world-actor-scenes/v2",
        "compiler": family_compiler or compiler_provenance("actor"),
        "actors": [
            {
                "recipe": actor["recipe"],
                "cellFormId": actor["cellFormId"],
                "referenceFormId": actor["reference"]["formId"],
                "baseFormId": actor["reference"]["baseFormId"],
                "scene": actor["manifest"],
                "sha256": file_sha256(Path(actor["manifest"])),
            }
            for actor in actors
        ],
    }
    path = cache_root / "generated" / "actors" / "actor-scenes.json"
    _atomic_json(path, document)
    document["manifest"] = str(path.resolve())
    return document


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--recipe", action="append")
    parser.add_argument("--recipe-file", type=Path)
    args = parser.parse_args()
    if args.recipe_file is not None and args.recipe:
        parser.error("--recipe-file cannot be combined with --recipe")
    if args.recipe_file is None and not args.recipe:
        parser.error("one or more --recipe values or --recipe-file is required")
    if args.recipe_file is not None:
        recipe_document = load_recipe_file(args.recipe_file.resolve())
        result = prepare_actor(
            args.data_root.resolve(),
            args.cache_root.resolve(),
            str(recipe_document["id"]),
            recipe_document,
        )
        print("OPENNV_ACTOR_SCENE " + json.dumps(result, sort_keys=True))
        return 0
    recipes = args.recipe
    if len(recipes) == 1:
        result = prepare_actor(args.data_root.resolve(), args.cache_root.resolve(), recipes[0])
        print("OPENNV_ACTOR_SCENE " + json.dumps(result, sort_keys=True))
    else:
        result = prepare_actor_set(args.data_root.resolve(), args.cache_root.resolve(), recipes)
        print("OPENNV_ACTOR_SCENE_SET " + json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
