#!/usr/bin/env python3
"""Prepare one recipe-pinned retail actor through the clean direct pipeline."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import struct
import sys
from dataclasses import dataclass
from pathlib import Path, PureWindowsPath
from typing import Sequence

from actor_catalog import (
    ActorCatalog,
    ActorReference,
    CreatureActor,
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
    authored_rigid_attachment_node,
    export_actor_gltf,
    retail_render_parts_from_snapshot,
)
from bsa_archive import BsaArchive, ExtractedMember, canonical_member_path
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
CREATURE_REFERENCE_RECORD_TYPE = "ACRE"
CREATURE_BASE_RECORD_TYPE = "CREA"
HUMANOID_REFERENCE_RECORD_TYPE = "ACHR"
HUMANOID_BASE_RECORD_TYPE = "NPC_"
MESH_ROOT = "meshes"
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
SHA256_HEX_CHARACTERS = 64


@dataclass(frozen=True)
class ActorAppearanceOverride:
    """Exact source-authored runtime appearance for one actor derivative."""

    variant_id: str
    authority: str
    source_sha256: str
    reference_form_id: int
    base_form_id: int
    race_form_id: int
    symmetric_geometry: tuple[float, ...]
    asymmetric_geometry: tuple[float, ...]
    symmetric_texture: tuple[float, ...]


@dataclass(frozen=True)
class ActorRuntimeSurfaceProjection:
    """Hash-bound owned model/surface selection observed at one retail beat."""

    authority_path: str
    authority_sha256: str
    included_shapes_by_model: tuple[tuple[str, tuple[str, ...]], ...]
    left_hand_model_path: str
    left_hand_model_sha256: str
    right_hand_model_path: str
    right_hand_model_sha256: str
    include_dismember_cap_shapes: bool

    def included_shapes(self, model_path: str) -> tuple[str, ...]:
        canonical = canonical_member_path(model_path)
        matches = [
            shapes
            for path, shapes in self.included_shapes_by_model
            if canonical_member_path(path) == canonical
        ]
        if len(matches) > 1:
            raise ValueError(f"Actor runtime surface model is repeated: {canonical}")
        return matches[0] if matches else ()


def _facegen_values_sha256(values: tuple[float, ...]) -> str:
    return hashlib.sha256(struct.pack(f"<{len(values)}f", *values)).hexdigest()


def _validate_appearance_override(
    override: ActorAppearanceOverride,
    reference: ActorReference,
    actor: HumanoidActor,
    catalog: ActorCatalog,
) -> None:
    if (
        not override.variant_id
        or any(
            character not in "abcdefghijklmnopqrstuvwxyz0123456789-"
            for character in override.variant_id
        )
        or not override.authority
        or len(override.source_sha256) != SHA256_HEX_CHARACTERS
        or any(character not in "0123456789abcdef" for character in override.source_sha256)
    ):
        raise ValueError("Actor appearance override identity is invalid")
    if (
        override.reference_form_id != reference.form_id
        or override.base_form_id != actor.form_id
        or override.race_form_id not in catalog.races
    ):
        raise ValueError("Actor appearance override record identity differs")
    expected_counts = (
        FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
        FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
        FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
    )
    values = (
        override.symmetric_geometry,
        override.asymmetric_geometry,
        override.symmetric_texture,
    )
    if tuple(len(row) for row in values) != expected_counts or any(
        not math.isfinite(value) for row in values for value in row
    ):
        raise ValueError("Actor appearance override FaceGen coordinates are invalid")


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


def resolve_proof_creature(
    catalog: ActorCatalog,
    reference_form_id: int,
    cell_form_id: int,
    expected_base_form_id: int,
) -> tuple[ActorReference, CreatureActor]:
    references = [
        reference
        for reference in catalog.references_for(cell_form_id)
        if reference.form_id == reference_form_id
        and reference.record_type == CREATURE_REFERENCE_RECORD_TYPE
    ]
    if len(references) != 1:
        raise ValueError(
            f"Expected one source ACRE {reference_form_id:08x}, found {len(references)}"
        )
    reference = references[0]
    if reference.actor_form_id != expected_base_form_id:
        raise ValueError(
            "Creature reference resolves another base: "
            f"expected={expected_base_form_id:08x} actual={reference.actor_form_id:08x}"
        )
    creature = catalog.creatures.get(reference.actor_form_id)
    if creature is None:
        raise ValueError(f"ACRE has no CREA base: {reference.actor_form_id:08x}")
    if creature.skeleton_path is None or not creature.model_paths:
        raise ValueError(
            f"CREA has no complete skeleton/model assembly: {creature.editor_id}"
        )
    return reference, creature


def _mesh_logical_path(path: str) -> str:
    canonical = canonical_member_path(path)
    return canonical if canonical.startswith(f"{MESH_ROOT}\\") else f"{MESH_ROOT}\\{canonical}"


def _creature_model_logical_path(skeleton_path: str, model_path: str) -> str:
    model = PureWindowsPath(canonical_member_path(model_path))
    if model.suffix.lower() != ".nif":
        raise ValueError(f"CREA model is not a NIF: {model_path}")
    if len(model.parts) == 1:
        model = PureWindowsPath(canonical_member_path(skeleton_path)).parent / model
    return _mesh_logical_path(str(model))


def _extract_mesh_member(
    archives: Sequence[BsaArchive],
    logical_path: str,
) -> tuple[BsaArchive, ExtractedMember]:
    requested = _mesh_logical_path(logical_path)
    matches = [archive for archive in archives if requested in archive.members]
    if len(matches) != 1:
        raise FileNotFoundError(
            f"Expected one actor mesh {requested!r}, found {len(matches)}"
        )
    archive = matches[0]
    return archive, archive.extract(requested)


def _resolve_creature_animation_role(
    archives: Sequence[BsaArchive],
    skeleton_path: str,
    role: str,
    candidates: object,
) -> str:
    if not isinstance(candidates, list) or not candidates:
        raise ValueError(f"CREA animation role has no source candidates: {role}")
    skeleton_directory = PureWindowsPath(
        canonical_member_path(skeleton_path)
    ).parent
    matches = []
    for candidate in candidates:
        path = _mesh_logical_path(str(skeleton_directory / str(candidate)))
        if sum(path in archive.members for archive in archives) == 1:
            matches.append(path)
    if len(matches) != 1:
        raise ValueError(
            f"CREA animation role is ambiguous or absent: {role} matches={len(matches)}"
        )
    return matches[0]


def _member_manifest(
    archive: BsaArchive,
    member: ExtractedMember,
    source_contract: tuple[tuple[Path, str], ...],
) -> dict[str, object]:
    source_hashes = {
        path.resolve(): source_hash for path, source_hash in source_contract
    }
    return {
        "logicalPath": member.logical_path,
        "bytes": len(member.data),
        "sha256": member.sha256,
        "sourceArchive": archive.archive.name,
        "sourceArchiveSha256": source_hashes[archive.archive.resolve()],
    }


def _reference_manifest(
    catalog: ActorCatalog,
    reference: ActorReference,
    origin: tuple[float, float, float],
    enable_parent_initially_disabled: bool | None = None,
) -> dict[str, object]:
    if (
        reference.enable_parent_form_id is not None
        and enable_parent_initially_disabled is None
    ):
        raise ValueError(
            "Actor XESP parent state was not compiled: "
            f"{reference.form_id:08x} -> {reference.enable_parent_form_id:08x}"
        )
    return {
        "formId": f"{reference.form_id:08x}",
        "recordType": reference.record_type,
        "recordSha256": catalog.record_data_sha256[reference.record_type][
            reference.form_id
        ],
        "baseFormId": f"{reference.actor_form_id:08x}",
        "initiallyDisabled": reference.initially_disabled,
        "enableParentFormId": (
            None
            if reference.enable_parent_form_id is None
            else f"{reference.enable_parent_form_id:08x}"
        ),
        "enableParentInitiallyDisabled": enable_parent_initially_disabled,
        "enableParentOpposite": reference.enable_parent_opposite,
        "positionGameUnits": list(reference.position),
        "positionGodotUnits": godot_position(reference.position, origin),
        "rotationRadians": list(reference.rotation_radians),
        "yawRadians": reference.rotation_radians[2],
        "yawGodotRadians": godot_yaw_radians(reference.rotation_radians[2]),
        "rotationGodotQuaternion": godot_rotation_quaternion(
            reference.rotation_radians
        ),
        "scale": reference.scale,
    }


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


def _prepare_creature_actor(
    cache_root: Path,
    recipe: dict[str, object],
    context: ActorPreparationContext,
    runtime_animation_paths: Sequence[str],
    family_compiler: dict[str, str] | None,
) -> dict[str, object]:
    if recipe.get("referenceRecordType") != CREATURE_REFERENCE_RECORD_TYPE:
        raise ValueError("Creature recipe must require one ACRE reference")
    if recipe.get("baseRecordType") != CREATURE_BASE_RECORD_TYPE:
        raise ValueError("Creature recipe must require one CREA base")
    if "expectedBaseFormId" not in recipe:
        raise ValueError("Creature recipe must pin its expected CREA base")
    if not isinstance(recipe.get("expectedInitiallyDisabled"), bool):
        raise ValueError("Creature recipe must pin its authored initially-disabled state")
    enable_parent_policy = recipe.get("enableParentPolicy")
    if enable_parent_policy not in {"require-absent", "require-source-form-id"}:
        raise ValueError("Creature recipe must declare its enable-parent policy")

    catalog = context.catalog
    reference, creature = resolve_proof_creature(
        catalog,
        form_id(str(recipe["proofActorReferenceFormId"])),
        form_id(str(recipe["cellFormId"])),
        form_id(str(recipe["expectedBaseFormId"])),
    )
    if reference.initially_disabled != bool(recipe["expectedInitiallyDisabled"]):
        raise ValueError(
            "Creature ACRE initially-disabled state differs from its recipe: "
            f"expected={recipe['expectedInitiallyDisabled']} "
            f"actual={reference.initially_disabled}"
        )
    expected_enable_parent = (
        form_id(str(recipe["expectedEnableParentFormId"]))
        if enable_parent_policy == "require-source-form-id" and
        "expectedEnableParentFormId" in recipe
        else None
    )
    if enable_parent_policy == "require-absent" and \
            reference.enable_parent_form_id is not None:
        raise ValueError(
            "Creature ACRE has an XESP enable parent but its recipe requires none: "
            f"{reference.enable_parent_form_id:08x}"
        )
    if enable_parent_policy == "require-source-form-id" and (
        expected_enable_parent is None or
        reference.enable_parent_form_id != expected_enable_parent
    ):
        raise ValueError(
            "Creature ACRE enable parent differs from its recipe: "
            f"expected={expected_enable_parent} actual={reference.enable_parent_form_id}"
        )

    configured_origin = recipe.get("originGameUnits")
    if configured_origin is None:
        cell_recipe = load_spatial_recipe(str(recipe["cellRecipe"]))
        cell_catalog = scan_cell_catalog(context.master)
        _source_door, arrival = arrival_transform(
            cell_catalog,
            form_id(str(cell_recipe["entryDoorReferenceFormId"])),
        )
        origin = arrival.position
    else:
        origin = tuple(float(value) for value in configured_origin)
        if len(origin) != 3:
            raise ValueError("Actor recipe originGameUnits must contain three values")

    configuration = context.configuration
    actor_rig = configuration.actor_rig
    rig_profile = actor_rig.profiles[CREATURE_BASE_RECORD_TYPE]
    animation_profile = configuration.document["actorCompiler"]["animationProfiles"][
        CREATURE_BASE_RECORD_TYPE
    ]
    if animation_profile.get("mode") != "skeleton-directory":
        raise ValueError("CREA animation profile must be resolved from its skeleton directory")
    idle_name = str(animation_profile.get("fileName", ""))
    if not idle_name or PureWindowsPath(idle_name).name != idle_name:
        raise ValueError("CREA animation profile has an invalid idle file name")
    skeleton_path = str(creature.skeleton_path)
    primary_animation_path = str(
        PureWindowsPath(canonical_member_path(skeleton_path)).parent / idle_name
    )
    creature_animation_roles = {
        str(role): _resolve_creature_animation_role(
            context.mesh_archives,
            skeleton_path,
            str(role),
            candidates,
        )
        for role, candidates in dict(animation_profile.get("roles", {})).items()
    }
    if set(creature_animation_roles) != {"locomotion", "melee", "hit"}:
        raise ValueError("CREA animation profile has incomplete source roles")
    requested_animation_paths: list[str] = []
    for path in (
        primary_animation_path,
        *creature_animation_roles.values(),
        *runtime_animation_paths,
        *(str(row["path"]) for row in recipe.get("additionalAnimations", [])),
    ):
        canonical = _mesh_logical_path(path)
        if canonical.casefold() not in {
            value.casefold() for value in requested_animation_paths
        }:
            requested_animation_paths.append(canonical)

    skeleton_archive, skeleton = _extract_mesh_member(
        context.mesh_archives,
        skeleton_path,
    )
    models = [
        _extract_mesh_member(
            context.mesh_archives,
            _creature_model_logical_path(skeleton_path, model_path),
        )
        for model_path in creature.model_paths
    ]
    animations = [
        _extract_mesh_member(context.mesh_archives, path)
        for path in requested_animation_paths
    ]
    additional_animation_hashes = {
        _mesh_logical_path(str(row["path"])): str(row["sha256"]).casefold()
        for row in recipe.get("additionalAnimations", [])
    }
    for _archive, animation in animations:
        expected_hash = additional_animation_hashes.get(animation.logical_path)
        if expected_hash is not None and animation.sha256 != expected_hash:
            raise ValueError(
                f"Actor animation hash mismatch: {animation.logical_path} "
                f"expected={expected_hash} actual={animation.sha256}"
            )

    output_root = cache_root / "generated" / "actors" / str(recipe["id"])
    gltf_path = output_root / "actor.gltf"
    sidecar_path = output_root / "actor.opennv.json"
    source_form_id = f"0x{creature.form_id:08X}"
    sidecar = export_actor_gltf(
        ActorGltfInput(
            f"{creature.form_id:08x}",
            creature.name or creature.editor_id,
            creature.skeleton_path,
            skeleton.data,
            (),
            (),
            tuple(
                ActorComponent(
                    f"creature-model-{index}",
                    member.logical_path,
                    member.data,
                    source_form_id=source_form_id,
                    source_slot=NO_SOURCE_SLOT,
                )
                for index, (_archive, member) in enumerate(models)
            ),
            animations[0][1].logical_path,
            animations[0][1].data,
            skeleton_root_node=rig_profile.skeleton_root_node,
            rigid_attachment_node=rig_profile.unparented_rigid_node,
            biped_head_node=actor_rig.biped_head_node,
            additional_animations=tuple(
                ActorAnimation(
                    member.logical_path,
                    member.data,
                    role=next(
                        (
                            role
                            for role, path in creature_animation_roles.items()
                            if path.casefold() == member.logical_path.casefold()
                        ),
                        None,
                    ),
                )
                for _archive, member in animations[1:]
            ),
        ),
        context.texture_archives,
        gltf_path,
        sidecar_path,
        configuration.content_compiler,
    )
    manifest = {
        "schema": "opennv-actor-scene/v5",
        "status": "skinned-animated",
        "compiler": family_compiler or compiler_provenance("actor"),
        "recipe": str(recipe["id"]),
        "configuration": configuration.actor_artifact_manifest(),
        "cellFormId": str(recipe["cellFormId"]),
        "reference": _reference_manifest(
            catalog,
            reference,
            origin,
            recipe.get("enableParentInitiallyDisabled"),
        ),
        "actor": {
            "name": creature.name or creature.editor_id,
            "editorId": creature.editor_id,
            "recordType": CREATURE_BASE_RECORD_TYPE,
            "recordSha256": catalog.record_data_sha256[CREATURE_BASE_RECORD_TYPE][
                creature.form_id
            ],
            "female": False,
            "raceFormId": "00000000",
            "hairFormId": "00000000",
            "eyesFormId": "00000000",
            "headPartFormIds": [],
            "outfitFormIds": [],
            "modelPaths": list(creature.model_paths),
        },
        "idleAnimation": animations[0][1].logical_path,
        "retailPresentation": None,
        "appearanceResolution": {
            "source": "effective owned CREA skeleton/model fields",
            "placement": "authored ACRE transform",
            "status": "source-bound-presence-compiled-parity-pending",
        },
        "source": {
            "master": str(context.master.resolve()),
            "masterSha256": context.source_contract[0][1],
            "skeleton": _member_manifest(
                skeleton_archive,
                skeleton,
                context.source_contract,
            ),
            "models": [
                _member_manifest(archive, member, context.source_contract)
                for archive, member in models
            ],
            "animations": [
                _member_manifest(archive, member, context.source_contract)
                for archive, member in animations
            ],
        },
        "outputs": {
            "gltf": gltf_path.name,
            "sidecar": sidecar_path.name,
            "gltfSha256": sidecar["outputs"]["gltf"]["sha256"],
            "sidecarSha256": file_sha256(sidecar_path),
            "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
        },
        "coverage": sidecar["coverage"],
        "capabilityBoundary": {
            "presence": "source-bound",
            "aiPackages": "not-compiled",
            "questConditions": "not-compiled",
            "retailParity": "not-claimed",
        },
    }
    manifest_path = output_root / "actor-scene.json"
    _atomic_json(manifest_path, manifest)
    manifest["manifest"] = str(manifest_path.resolve())
    return manifest


def prepare_actor(
    data_root: Path,
    cache_root: Path,
    recipe_id: str,
    recipe_document: dict[str, object] | None = None,
    preparation_context: ActorPreparationContext | None = None,
    runtime_animation_paths: Sequence[str] = (),
    runtime_animation_objects: Sequence[dict[str, object]] = (),
    runtime_accumulation_root_animations: dict[str, str] | None = None,
    family_compiler: dict[str, str] | None = None,
    appearance_override: ActorAppearanceOverride | None = None,
    runtime_surface_projection: ActorRuntimeSurfaceProjection | None = None,
) -> dict[str, object]:
    recipe = load_recipe(recipe_id) if recipe_document is None else recipe_document
    if recipe.get("schema") != RECIPE_SCHEMA or not str(recipe.get("id", "")).strip():
        raise ValueError("Actor recipe document has an invalid schema or ID")
    recipe_id = str(recipe["id"])
    context = preparation_context or create_actor_preparation_context(data_root, recipe)
    if context.source_contract != _actor_source_contract(data_root, recipe):
        raise ValueError("Actor preparation context belongs to another owned-data recipe")
    reference_record_type = str(
        recipe.get("referenceRecordType", HUMANOID_REFERENCE_RECORD_TYPE)
    )
    base_record_type = str(recipe.get("baseRecordType", HUMANOID_BASE_RECORD_TYPE))
    if reference_record_type == CREATURE_REFERENCE_RECORD_TYPE:
        if runtime_accumulation_root_animations or appearance_override is not None:
            raise ValueError(
                "Creature runtime accumulation-root retention or appearance override is unsupported"
            )
        return _prepare_creature_actor(
            cache_root,
            recipe,
            context,
            runtime_animation_paths,
            family_compiler,
        )
    if (
        reference_record_type != HUMANOID_REFERENCE_RECORD_TYPE
        or base_record_type != HUMANOID_BASE_RECORD_TYPE
    ):
        raise ValueError(
            "Actor recipe record pair is unsupported: "
            f"{reference_record_type}/{base_record_type}"
        )
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
    if appearance_override is not None:
        _validate_appearance_override(appearance_override, reference, actor, catalog)
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
    retained_root_animations = {
        _mesh_logical_path(path).casefold(): sha256.casefold()
        for path, sha256 in (runtime_accumulation_root_animations or {}).items()
    }
    retained_paths = {
        _mesh_logical_path(path).casefold() for path in actor_animation_paths[1:]
    }
    if not set(retained_root_animations).issubset(retained_paths):
        raise ValueError(
            "Runtime accumulation-root retention names an absent additional animation"
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
    appearance_race_form_id = (
        actor.race_form_id
        if appearance_override is None
        else appearance_override.race_form_id
    )
    race = catalog.races.get(appearance_race_form_id)
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
    resolved_eyes_form_id = actor.eyes_form_id or (
        race.valid_eye_form_ids[0] if race.valid_eye_form_ids else 0
    )
    eyes = catalog.parts.get(resolved_eyes_form_id)
    head_parts = [catalog.parts.get(part) for part in actor.head_part_form_ids]
    if hair is None or hair.model_path is None or eyes is None or eyes.texture_path is None:
        raise ValueError("Proof actor has incomplete hair or eye records")
    if any(part is None for part in head_parts):
        raise ValueError("Proof actor has an unresolved head-part record")
    explicit_outfit_models = [str(value) for value in recipe.get("outfitModelPaths", [])]
    explicit_outfit_shape_names = tuple(
        str(value) for value in recipe.get("outfitShapeNames", [])
    )
    if explicit_outfit_models:
        outfit_models = explicit_outfit_models
        outfit_forms = [
            form_id(str(value)) for value in recipe.get("outfitIdentityFormIds", [])
        ]
        if len(outfit_forms) != len(outfit_models):
            raise ValueError("Explicit actor outfit models require one identity FormID each")
        if explicit_outfit_shape_names and len(explicit_outfit_models) != 1:
            raise ValueError(
                "Exact actor outfit shape selection requires one explicit outfit model"
            )
        outfits = []
    else:
        outfit_forms = list(resolve_actor_outfit_form_ids(catalog, actor))
        outfits = [catalog.armor.get(value) for value in outfit_forms]
        if not outfits or any(outfit is None for outfit in outfits):
            raise ValueError(f"Proof actor has unresolved outfit armor: {outfit_forms}")
        outfit_models = [
            (
                outfit.female_model_path or outfit.male_model_path
                if actor.female
                else outfit.male_model_path or outfit.female_model_path
            )
            for outfit in outfits
        ]
    if any(path is None for path in outfit_models):
        raise ValueError("Proof actor outfit lacks a sex-specific model")

    if runtime_surface_projection is not None:
        if (
            len(runtime_surface_projection.authority_sha256) != SHA256_HEX_CHARACTERS
            or any(
                value not in "0123456789abcdef"
                for value in runtime_surface_projection.authority_sha256
            )
        ):
            raise ValueError("Actor runtime surface projection authority hash is invalid")
        body_models = list(body_models)
        body_models[RACE_LEFT_HAND_MODEL_INDEX] = (
            runtime_surface_projection.left_hand_model_path
        )
        body_models[RACE_RIGHT_HAND_MODEL_INDEX] = (
            runtime_surface_projection.right_hand_model_path
        )

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
    symmetric_geometry = (
        compose_facegen_coordinates(
            actor.face_symmetric_geometry,
            race_face_symmetric_geometry,
        )
        if appearance_override is None
        else appearance_override.symmetric_geometry
    )
    asymmetric_geometry = (
        compose_facegen_coordinates(
            actor.face_asymmetric_geometry,
            race_face_asymmetric_geometry,
        )
        if appearance_override is None
        else appearance_override.asymmetric_geometry
    )
    symmetric_texture = (
        compose_facegen_coordinates(
            actor.face_symmetric_texture,
            race_face_symmetric_texture,
        )
        if appearance_override is None
        else appearance_override.symmetric_texture
    )
    texture_owner = master.name.casefold()
    face_mod_path = (
        f"textures\\characters\\facemods\\{texture_owner}\\{actor.form_id:08x}_0.dds"
    )
    if appearance_override is None and has_texture(texture_archives, face_mod_path):
        face_detail_path = face_mod_path
        generated_face_detail = None
        face_detail_source = "retail-precomputed"
    else:
        face_detail_path = None
        generated_face_detail = synthesize_texture_detail(
            mesh(head_egt),
            symmetric_texture,
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
    if appearance_override is None and has_texture(texture_archives, body_mod_path):
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
                    included_shape_names=(
                        runtime_surface_projection.included_shapes(outfit_model)
                        if runtime_surface_projection is not None
                        else explicit_outfit_shape_names
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
                    included_shape_names=(
                        runtime_surface_projection.included_shapes(
                            body_models[RACE_LEFT_HAND_MODEL_INDEX]
                        )
                        if runtime_surface_projection is not None
                        else ()
                    ),
                ),
                ActorComponent(
                    "right-hand",
                    body_models[RACE_RIGHT_HAND_MODEL_INDEX],
                    mesh(body_models[RACE_RIGHT_HAND_MODEL_INDEX]),
                    generated_diffuse=generated_right_hand,
                    bake_shape_transform=not actor.female,
                    included_shape_names=(
                        runtime_surface_projection.included_shapes(
                            body_models[RACE_RIGHT_HAND_MODEL_INDEX]
                        )
                        if runtime_surface_projection is not None
                        else ()
                    ),
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

    animation_object_roles: set[str] = set()
    for animation_object in runtime_animation_objects:
        role = str(animation_object["componentRole"])
        form_id_text = str(animation_object["formId"]).casefold()
        model_path = _mesh_logical_path(str(animation_object["modelLogicalPath"]))
        payload = mesh(model_path)
        if (
            animation_object.get("recordType") != "ANIO"
            or role != f"animation-object-{form_id_text}"
        ):
            raise ValueError("Owned actor animation-object identity is malformed")
        if role in animation_object_roles:
            raise ValueError(f"Owned actor animation-object role is duplicated: {role}")
        animation_object_roles.add(role)
        actual_hash = hashlib.sha256(payload).hexdigest()
        if actual_hash != str(animation_object["sha256"]).casefold():
            raise ValueError(
                f"Actor animation-object hash mismatch: {model_path} "
                f"expected={animation_object['sha256']} actual={actual_hash}"
            )
        attachment_node = authored_rigid_attachment_node(payload)
        if attachment_node != str(animation_object["attachmentNode"]):
            raise ValueError(
                f"Actor animation-object attachment mismatch: {model_path} "
                f"expected={animation_object['attachmentNode']} actual={attachment_node}"
            )
        components.append(
            ActorComponent(
                role,
                model_path,
                payload,
                bake_shape_transform=True,
                source_form_id=form_id_text,
            )
        )

    output_identity = (
        recipe_id
        if appearance_override is None
        else f"{recipe_id}-{appearance_override.variant_id}"
    )
    output_root = cache_root / "generated" / "actors" / output_identity
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
                _actor_animation(
                    path,
                    mesh(path),
                    retained_root_animations,
                )
                for path in actor_animation_paths[1:]
            ),
            retail_render_parts=(
                retail_render_parts_from_snapshot(retail_presentation.appearance)
                if retail_presentation is not None
                else ()
            ),
            include_dismember_cap_shapes=(
                runtime_surface_projection.include_dismember_cap_shapes
                if runtime_surface_projection is not None
                else False
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
        "configuration": configuration.actor_artifact_manifest(),
        "cellFormId": recipe["cellFormId"],
        "reference": _reference_manifest(
            catalog,
            reference,
            origin,
            recipe.get("enableParentInitiallyDisabled"),
        ),
        "actor": {
            "name": actor.name,
            "editorId": actor.editor_id,
            "recordSha256": catalog.record_data_sha256[HUMANOID_BASE_RECORD_TYPE][
                actor.form_id
            ],
            "female": actor.female,
            "raceFormId": f"{appearance_race_form_id:08x}",
            "hairFormId": f"{actor.hair_form_id:08x}",
            "eyesFormId": f"{resolved_eyes_form_id:08x}",
            "eyesSource": (
                "npc-enam" if actor.eyes_form_id is not None
                else "race-enam-first-engine-default"
            ),
            "headPartFormIds": [f"{part:08x}" for part in actor.head_part_form_ids],
            "outfitFormIds": [f"{outfit_form:08x}" for outfit_form in outfit_forms],
            "packageFormIds": [f"{package:08x}" for package in actor.package_form_ids],
            "templateFormId": (
                None if actor.template_form_id is None else f"{actor.template_form_id:08x}"
            ),
            "templateFlags": actor.template_flags,
            "recordType": HUMANOID_BASE_RECORD_TYPE,
        },
        **(
            {
                "appearanceOverride": {
                    "variantId": appearance_override.variant_id,
                    "authority": appearance_override.authority,
                    "sourceSha256": appearance_override.source_sha256,
                    "referenceFormId": f"{appearance_override.reference_form_id:08x}",
                    "baseFormId": f"{appearance_override.base_form_id:08x}",
                    "authoredRaceFormId": f"{actor.race_form_id:08x}",
                    "raceFormId": f"{appearance_override.race_form_id:08x}",
                    "symmetricGeometrySha256": _facegen_values_sha256(
                        appearance_override.symmetric_geometry
                    ),
                    "asymmetricGeometrySha256": _facegen_values_sha256(
                        appearance_override.asymmetric_geometry
                    ),
                    "symmetricTextureSha256": _facegen_values_sha256(
                        appearance_override.symmetric_texture
                    ),
                }
            }
            if appearance_override is not None
            else {}
        ),
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
            "dismemberCaps": (
                "included by exact live retail surface observation"
                if runtime_surface_projection is not None
                and runtime_surface_projection.include_dismember_cap_shapes
                else "excluded by BSDismemberBodyPartType semantics"
            ),
            "rigidAttachments": "derived from NIF skin-instance presence",
            "faceGenMaterialSource": configuration.document["actorCompiler"][
                "faceGenMaterial"
            ]["source"],
            "status": configuration.document["actorCompiler"]["provenance"]["status"],
        },
        "runtimeSurfaceProjection": (
            {
                "authorityPath": runtime_surface_projection.authority_path,
                "authoritySha256": runtime_surface_projection.authority_sha256,
                "includeDismemberCapShapes": (
                    runtime_surface_projection.include_dismember_cap_shapes
                ),
                "leftHandModelPath": runtime_surface_projection.left_hand_model_path,
                "leftHandModelSha256": runtime_surface_projection.left_hand_model_sha256,
                "rightHandModelPath": runtime_surface_projection.right_hand_model_path,
                "rightHandModelSha256": runtime_surface_projection.right_hand_model_sha256,
                "includedShapesByModel": [
                    {"modelPath": path, "shapeNames": list(shapes)}
                    for path, shapes in runtime_surface_projection.included_shapes_by_model
                ],
            }
            if runtime_surface_projection is not None
            else None
        ),
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


def _actor_animation(
    logical_path: str,
    payload: bytes,
    retained_root_animations: dict[str, str],
) -> ActorAnimation:
    canonical = _mesh_logical_path(logical_path)
    expected_sha256 = retained_root_animations.get(canonical.casefold())
    if expected_sha256 is not None:
        actual_sha256 = hashlib.sha256(payload).hexdigest()
        if actual_sha256 != expected_sha256:
            raise ValueError(
                "Runtime accumulation-root animation hash mismatch: "
                f"{canonical} expected={expected_sha256} actual={actual_sha256}"
            )
    return ActorAnimation(
        canonical,
        payload,
        retain_accumulation_root_translation=expected_sha256 is not None,
    )


def prepare_actor_set(
    data_root: Path,
    cache_root: Path,
    recipe_ids: list[str],
    runtime_animation_paths_by_reference: dict[str, Sequence[str]] | None = None,
    runtime_animation_objects_by_reference: dict[
        str, Sequence[dict[str, object]]
    ] | None = None,
    runtime_accumulation_root_animations_by_reference: dict[
        str, dict[str, str]
    ] | None = None,
    family_compiler: dict[str, str] | None = None,
    recipe_documents: list[dict[str, object]] | None = None,
) -> dict[str, object]:
    if len(recipe_ids) < 1 or len(set(recipe_ids)) != len(recipe_ids):
        raise ValueError("Actor-set recipes must be non-empty and unique")
    runtime_animation_paths_by_reference = runtime_animation_paths_by_reference or {}
    runtime_animation_objects_by_reference = (
        runtime_animation_objects_by_reference or {}
    )
    runtime_accumulation_root_animations_by_reference = (
        runtime_accumulation_root_animations_by_reference or {}
    )
    recipes = (
        [load_recipe(recipe_id) for recipe_id in recipe_ids]
        if recipe_documents is None
        else recipe_documents
    )
    if [str(recipe["id"]) for recipe in recipes] != recipe_ids:
        raise ValueError("Actor-set recipe documents differ from their ordered identities")
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
            runtime_animation_objects=runtime_animation_objects_by_reference.get(
                str(recipe["proofActorReferenceFormId"]).casefold(),
                (),
            ),
            runtime_accumulation_root_animations=(
                runtime_accumulation_root_animations_by_reference.get(
                    str(recipe["proofActorReferenceFormId"]).casefold(),
                    {},
                )
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
