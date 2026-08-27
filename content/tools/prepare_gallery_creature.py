#!/usr/bin/env python3
"""Compile one authored CREA placement for a non-parity owned-data gallery shot."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from dataclasses import dataclass
from pathlib import Path, PureWindowsPath

from actor_catalog import ActorCatalog, ActorReference, CreatureActor, scan_actor_catalog
from actor_gltf import (
    ActorAnimation,
    ActorComponent,
    ActorGltfInput,
    actor_component_geometry_inventory,
    export_actor_gltf,
    retail_render_parts_from_snapshot,
)
from cell_scene import godot_position, godot_rotation_quaternion, godot_yaw_radians
from gallery_actor_presentation import load_gallery_actor_presentation
from owned_archive_stack import OwnedArchiveStack, load_owned_archive_stack
from prepare_creature_review import (
    NO_SOURCE_SLOT,
    _asset_row,
    _creature_model_member,
    _mesh_member,
    default_archive_recipe_path,
)
from runtime_configuration import RuntimeConfiguration, load_runtime_configuration


RECIPE_SCHEMA = "opennv-gallery-creature-recipe/v1"
SCENE_SCHEMA = "opennv-actor-scene/v5"
SCENE_STATUS = "skinned-animated"
FORM_ID_RADIX = 16
EXIT_DATA_ERROR = 2


def _sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _load_recipe(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if (
        not isinstance(document, dict)
        or document.get("schema") != RECIPE_SCHEMA
        or not str(document.get("id", "")).strip()
    ):
        raise ValueError(f"Unexpected OpenNV gallery creature recipe: {path}")
    return document


def _form_id(value: object) -> int:
    return int(str(value), FORM_ID_RADIX)


def _resolve_creature(
    catalog: ActorCatalog,
    reference_form_id: int,
    cell_form_id: int,
    expected_base_form_id: int,
) -> tuple[ActorReference, CreatureActor]:
    references = [
        reference
        for reference in catalog.references_for(cell_form_id)
        if reference.form_id == reference_form_id and reference.record_type == "ACRE"
    ]
    if len(references) != 1:
        raise ValueError(
            f"Expected one authored ACRE {reference_form_id:08x}, found {len(references)}"
        )
    reference = references[0]
    if reference.actor_form_id != expected_base_form_id:
        raise ValueError(
            "Gallery creature reference resolves another base: "
            f"expected={expected_base_form_id:08x} actual={reference.actor_form_id:08x}"
        )
    creature = catalog.creatures.get(reference.actor_form_id)
    if creature is None:
        raise ValueError(f"ACRE has no CREA base: {reference.actor_form_id:08x}")
    if creature.skeleton_path is None or not creature.model_paths:
        raise ValueError(f"CREA has no complete skeleton/model assembly: {creature.editor_id}")
    return reference, creature


def _atomic_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    os.replace(temporary, path)


@dataclass(frozen=True)
class GalleryCreaturePreparationContext:
    configuration: RuntimeConfiguration
    master: Path
    master_sha256: str
    archive_recipe: Path
    archive_recipe_sha256: str
    catalog: ActorCatalog
    archives: OwnedArchiveStack


def create_gallery_creature_preparation_context(
    data_root: Path,
    master_row: dict[str, object],
    archive_recipe_path: Path,
    configuration: RuntimeConfiguration | None = None,
    catalog: ActorCatalog | None = None,
    verified_master_sha256: str | None = None,
) -> GalleryCreaturePreparationContext:
    master = (data_root / str(master_row["file"])).resolve()
    archive_recipe = archive_recipe_path.resolve()
    if not master.is_file():
        raise FileNotFoundError(master)
    if not archive_recipe.is_file():
        raise FileNotFoundError(archive_recipe)
    master_sha256 = verified_master_sha256 or _sha256(master)
    if master_sha256.lower() != str(master_row["sha256"]).lower():
        raise ValueError("Gallery creature master hash differs from its recipe")
    resolved_configuration = configuration or load_runtime_configuration()
    resolved_catalog = catalog or scan_actor_catalog(master)
    return GalleryCreaturePreparationContext(
        resolved_configuration,
        master,
        master_sha256,
        archive_recipe,
        _sha256(archive_recipe),
        resolved_catalog,
        load_owned_archive_stack(data_root, archive_recipe),
    )


def prepare_gallery_creature(
    data_root: Path,
    cache_root: Path,
    recipe_path: Path,
    archive_recipe_path: Path,
    preparation_context: GalleryCreaturePreparationContext | None = None,
) -> dict[str, object]:
    recipe = _load_recipe(recipe_path)
    context = preparation_context or create_gallery_creature_preparation_context(
        data_root,
        recipe["master"],
        archive_recipe_path,
    )
    expected_master = (data_root / str(recipe["master"]["file"])).resolve()
    expected_archive_recipe = archive_recipe_path.resolve()
    if (
        context.master != expected_master
        or context.master_sha256.lower() != str(recipe["master"]["sha256"]).lower()
        or context.archive_recipe != expected_archive_recipe
    ):
        raise ValueError(
            "Gallery creature preparation context belongs to another owned-data recipe"
        )
    master = context.master
    master_sha256 = context.master_sha256
    catalog = context.catalog
    reference, creature = _resolve_creature(
        catalog,
        _form_id(recipe["proofActorReferenceFormId"]),
        _form_id(recipe["cellFormId"]),
        _form_id(recipe["expectedBaseFormId"]),
    )
    origin = tuple(float(value) for value in recipe["originGameUnits"])
    if len(origin) != 3:
        raise ValueError("Gallery creature originGameUnits must contain three values")

    configuration = context.configuration
    actor_rig = configuration.actor_rig
    rig_profile = actor_rig.profiles["CREA"]
    archives = context.archives
    skeleton = archives.extract(_mesh_member(creature.skeleton_path))
    model_members = [
        archives.extract(_creature_model_member(creature.skeleton_path, model_path))
        for model_path in creature.model_paths
    ]
    allowed_unsupported_geometry_types = {
        str(value) for value in recipe.get("allowedUnsupportedGeometryTypes", [])
    }
    rendered_model_members: list[tuple[int, object]] = []
    omitted_particle_models: list[dict[str, object]] = []
    for index, (model_path, member) in enumerate(
        zip(creature.model_paths, model_members)
    ):
        inventory = actor_component_geometry_inventory(member.data)
        unsupported = inventory["unsupported"]
        if not unsupported:
            rendered_model_members.append((index, member))
            continue
        unsupported_types = {geometry_type for geometry_type, _name in unsupported}
        if (
            inventory["supported"]
            or not unsupported_types.issubset(allowed_unsupported_geometry_types)
        ):
            rendered = ", ".join(
                f"{geometry_type}:{name!r}"
                for geometry_type, name in unsupported
            )
            raise ValueError(
                f"Gallery creature model {model_path} contains unsupported geometry "
                f"[{rendered}] and cannot be omitted as a particle-only component"
            )
        omitted_particle_models.append(
            {
                "authoredModelPath": model_path,
                "asset": _asset_row(member),
                "geometry": [
                    {"type": geometry_type, "name": name}
                    for geometry_type, name in unsupported
                ],
                "reason": "unsupported-particle-only-model-no-proxy-substitution",
            }
        )
    if not rendered_model_members:
        raise ValueError("Gallery creature retained no renderable model components")
    retail_presentation = load_gallery_actor_presentation(
        recipe["retailEvidence"],
        str(recipe["proofActorReferenceFormId"]),
        str(recipe["expectedBaseFormId"]),
    )
    animation_members = [
        archives.extract(_mesh_member(sequence.logical_path))
        for sequence in retail_presentation.animations
    ]
    idle_animation = animation_members[0]
    weapon_attachment = retail_presentation.visible_weapon
    weapon_member = (
        None
        if weapon_attachment is None
        else archives.extract(_mesh_member(weapon_attachment.model_path))
    )
    output_root = cache_root / "generated" / "actors" / str(recipe["id"])
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite gallery creature cache: {output_root}")
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
                for index, member in rendered_model_members
            )
            + (() if weapon_attachment is None or weapon_member is None else (
                ActorComponent(
                    weapon_attachment.role,
                    weapon_member.logical_path,
                    weapon_member.data,
                    source_form_id=weapon_attachment.source_form_id,
                    source_slot=weapon_attachment.source_slot,
                ),
            )),
            idle_animation.logical_path,
            idle_animation.data,
            skeleton_root_node=rig_profile.skeleton_root_node,
            rigid_attachment_node=rig_profile.unparented_rigid_node,
            biped_head_node=actor_rig.biped_head_node,
            additional_animations=tuple(
                ActorAnimation(member.logical_path, member.data)
                for member in animation_members[1:]
            ),
            retail_render_parts=retail_render_parts_from_snapshot(
                retail_presentation.appearance
            ),
        ),
        [archives],
        gltf_path,
        sidecar_path,
        configuration.content_compiler,
    )
    scene = {
        "schema": SCENE_SCHEMA,
        "status": SCENE_STATUS,
        "recipe": str(recipe["id"]),
        "configuration": configuration.manifest(),
        "cellFormId": f"{_form_id(recipe['cellFormId']):08x}",
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
            "name": creature.name or creature.editor_id,
            "editorId": creature.editor_id,
            "female": False,
            "raceFormId": "00000000",
            "hairFormId": "00000000",
            "eyesFormId": "00000000",
            "headPartFormIds": [],
            "outfitFormIds": [],
            "recordType": "CREA",
            "modelPaths": list(creature.model_paths),
        },
        "idleAnimation": idle_animation.logical_path,
        "retailPresentation": {
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
            "presentationSurfaceReportPath": str(
                retail_presentation.presentation_surface_report_path
            ),
            "presentationSurfaceReportSha256": (
                retail_presentation.presentation_surface_report_sha256
            ),
            "presentationSurfaceGeometryNames": list(
                retail_presentation.presentation_surface_geometry_names
            ),
            "weaponForm": retail_presentation.weapon_form,
            "weaponOut": retail_presentation.weapon_out,
            "visibleWeapon": (
                None
                if weapon_attachment is None
                else {
                    "sourceFormId": weapon_attachment.source_form_id,
                    "sourceSlot": weapon_attachment.source_slot,
                    "modelPath": weapon_attachment.model_path,
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
        },
        "appearanceResolution": {
            "source": "effective owned CREA skeleton/model fields",
            "placement": "authored ACRE transform",
            "status": "gallery-compiled-non-parity",
        },
        "source": {
            "master": str(master.resolve()),
            "masterSha256": master_sha256,
            "skeleton": _asset_row(skeleton),
            "models": [_asset_row(member) for member in model_members],
            "omittedParticleOnlyModels": omitted_particle_models,
            "animations": [_asset_row(member) for member in animation_members],
            "visibleWeapon": (
                None if weapon_member is None else _asset_row(weapon_member)
            ),
            "archiveStack": archives.manifest(),
        },
        "outputs": {
            "gltf": gltf_path.name,
            "sidecar": sidecar_path.name,
            "gltfSha256": sidecar["outputs"]["gltf"]["sha256"],
            "sidecarSha256": _sha256(sidecar_path),
            "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
        },
        "coverage": sidecar["coverage"],
        "capabilityGaps": {
            "omittedParticleOnlyModels": omitted_particle_models,
        },
        "evidencePolicy": {
            "compiledCacheIsNotVisualEvidence": True,
            "galleryCaptureIsNotParityEvidence": True,
        },
    }
    manifest_path = output_root / "actor-scene.json"
    _atomic_json(manifest_path, scene)
    scene["manifest"] = str(manifest_path.resolve())
    return scene


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--recipe-file", type=Path, required=True)
    parser.add_argument(
        "--archive-recipe",
        type=Path,
        default=default_archive_recipe_path(),
    )
    args = parser.parse_args()
    try:
        scene = prepare_gallery_creature(
            args.data_root.resolve(),
            args.cache_root.resolve(),
            args.recipe_file.resolve(),
            args.archive_recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_GALLERY_CREATURE_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_GALLERY_CREATURE "
        + json.dumps(
            {
                "manifest": scene["manifest"],
                "referenceFormId": scene["reference"]["formId"],
                "baseFormId": scene["reference"]["baseFormId"],
                "status": scene["appearanceResolution"]["status"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
