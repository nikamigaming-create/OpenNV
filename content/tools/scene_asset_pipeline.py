"""Shared NIF, texture, material, interaction, and loadout scene preparation."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path

from bsa_archive import BsaArchive
from cell_catalog import (
    ITEM_RECORD_TYPES,
    BaseObject,
    CellCatalog,
    PlacedReference,
)
from export_static_nif_gltf import NoStaticPresentationGeometryError, export_static_nif
from material_contract import (
    material_bindings,
    texture_binding_requests,
)
from owned_archive_stack import OwnedArchiveStack
from runtime_configuration import ContentCompilerConfiguration
from speedtree_spt import export_speedtree_spt
from texture_pipeline import OwnedTexturePipeline, TexturePipeline


FORM_ID_RADIX = 16


def form_id(value: int) -> str:
    return f"{value:08x}"


def authored_collision_source(coverage: dict[str, object]) -> str:
    collision_exported = bool(coverage["collisionExported"])
    packed_collision_bodies = coverage["collisionBodies"]
    static_convex_bodies = coverage.get("staticConvexBodies", [])
    if collision_exported:
        if packed_collision_bodies and static_convex_bodies:
            return "NIF-authored-bhk-packed-triangles-plus-static-convex-points"
        if static_convex_bodies:
            return "NIF-authored-bhk-static-convex-points"
        if packed_collision_bodies:
            return "NIF-authored-bhk-packed-triangles"
        raise ValueError("Exported authored collision has no typed body contract")
    if packed_collision_bodies or static_convex_bodies:
        raise ValueError("Disabled authored collision contains typed body contracts")
    return "unsupported-or-absent"


def reference_selection_reason(
    base: BaseObject,
    recipe: dict[str, object],
    compiler_configuration: ContentCompilerConfiguration,
) -> str:
    selection = recipe["selection"]
    prefixes = tuple(str(value).lower() for value in selection["modelPrefixes"])
    record_types = {str(value) for value in selection["includeBaseRecordTypes"]}
    excluded_editor_ids = {str(value) for value in selection.get("excludeBaseEditorIds", [])}
    excluded_model_prefixes = tuple(
        str(value).lower() for value in selection.get("excludeModelPrefixes", [])
    )
    if not base.model_path:
        return "no-model"
    if base.form_id in compiler_configuration.non_presentation_base_form_ids:
        return "configured-non-presentation-base"
    model_suffix = Path(base.model_path).suffix.lower()
    if model_suffix not in {".nif", ".spt"}:
        return "unsupported-model-format"
    if model_suffix == ".spt" and base.record_type != "TREE":
        return "unsupported-model-format"
    if base.editor_id in excluded_editor_ids:
        return "editor-only-base"
    if base.model_path.startswith(excluded_model_prefixes):
        return "special-effect-shader-required"
    if base.model_path.startswith(prefixes) or base.record_type in record_types:
        return "selected"
    return "outside-recipe"


def interaction_manifest(
    reference: PlacedReference,
    base: BaseObject,
    catalog: CellCatalog,
) -> dict[str, object] | None:
    if base.record_type in ITEM_RECORD_TYPES:
        interaction = {
            "type": "pickup",
            "itemFormId": form_id(base.form_id),
            "itemEditorId": base.editor_id,
            "itemDisplayName": base.display_name or "",
            "itemRecordType": base.record_type,
            "count": 1,
        }
        weapon = catalog.weapons.get(base.form_id)
        if weapon is not None:
            interaction["weapon"] = {
                "damage": weapon.damage,
                "clipSize": weapon.clip_size,
                "ammoFormId": form_id(weapon.ammo_form_id) if weapon.ammo_form_id is not None else None,
            }
        return interaction
    if base.record_type == "CONT":
        container = catalog.containers.get(base.form_id)
        items = []
        if container is not None:
            for entry in container.items:
                item = catalog.base_objects.get(entry.item_form_id)
                items.append(
                    {
                        "itemFormId": form_id(entry.item_form_id),
                        "itemEditorId": item.editor_id if item is not None else "",
                        "itemDisplayName": (
                            item.display_name if item is not None and item.display_name else ""
                        ),
                        "itemRecordType": item.record_type if item is not None else "",
                        "count": entry.count,
                        "resolved": item is not None,
                    }
                )
        return {
            "type": "container",
            "displayName": base.display_name or "",
            "items": items,
        }
    if base.record_type == "DOOR":
        return {"type": "door"}
    return None


def vr_smoke_loadout_manifest(
    recipe: dict[str, object],
    catalog: CellCatalog,
) -> dict[str, object] | None:
    configured = recipe.get("vrSmokeLoadout")
    if configured is None:
        return None
    if not isinstance(configured, dict):
        raise ValueError("Cell VR smoke loadout must be an object when present")
    weapon_form_id = int(str(configured["weaponFormId"]), FORM_ID_RADIX)
    reserve_magazines = int(configured["reserveMagazines"])
    if reserve_magazines < 1:
        raise ValueError("VR smoke loadout must retain at least one reserve magazine")
    weapon = catalog.weapons.get(weapon_form_id)
    weapon_base = catalog.base_objects.get(weapon_form_id)
    if (
        weapon is None
        or weapon_base is None
        or weapon_base.record_type != "WEAP"
        or weapon_base.model_path is None
    ):
        raise ValueError(f"VR smoke weapon is not a resolved WEAP: {weapon_form_id:08x}")
    ammo_form_id = int(str(configured.get("ammoFormId", "0")), FORM_ID_RADIX)
    if ammo_form_id == 0:
        if weapon.ammo_form_id is None:
            raise ValueError(f"VR smoke weapon has no ammo form: {weapon_form_id:08x}")
        ammo_form_id = weapon.ammo_form_id
    ammo = catalog.base_objects.get(ammo_form_id)
    if ammo is None or ammo.record_type != "AMMO":
        raise ValueError(f"VR smoke ammo is not a resolved AMMO: {ammo_form_id:08x}")
    return {
        "weaponFormId": form_id(weapon_form_id),
        "weaponEditorId": weapon_base.editor_id,
        "weaponDisplayName": weapon_base.display_name or "",
        "modelPath": weapon_base.model_path,
        "ammoFormId": form_id(ammo_form_id),
        "ammoEditorId": ammo.editor_id,
        "ammoDisplayName": ammo.display_name or "",
        "damage": weapon.damage,
        "clipSize": weapon.clip_size,
        "reserveRounds": weapon.clip_size * reserve_magazines,
        "source": "recipe-identity-plus-retail-records",
    }


def _atomic_bytes(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def _atomic_json(path: Path, document: dict[str, object]) -> None:
    _atomic_bytes(
        path,
        (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8"),
    )


def prepare_scene_assets(
    meshes_path: Path,
    texture_archive_paths: list[Path],
    cache_root: Path,
    recipe: dict[str, object],
    selected: list[tuple[PlacedReference, BaseObject]],
    compiler_configuration: ContentCompilerConfiguration,
    extra_model_paths: set[str] | None = None,
    presentation_clips: dict[str, dict[str, object]] | None = None,
    fully_clipped_model_paths: set[str] | None = None,
    owned_archives: OwnedArchiveStack | None = None,
) -> tuple[
    dict[str, dict[str, object]],
    dict[str, dict[str, object]],
    dict[str, object],
    dict[str, str],
    dict[str, dict[str, object]],
    list[dict[str, object]],
]:
    if not isinstance(recipe.get("exportStrict"), bool):
        raise ValueError("Scene recipe exportStrict policy must be explicit")
    if not isinstance(recipe.get("textureAliases"), dict):
        raise ValueError("Scene recipe textureAliases policy must be explicit")
    archive = owned_archives if owned_archives is not None else BsaArchive(meshes_path)
    assets: dict[str, dict[str, object]] = {}
    asset_sidecars: dict[str, dict[str, object]] = {}
    non_presentation_assets: dict[str, dict[str, object]] = {}
    compiler: dict[str, str] | None = None
    presentation_clips = presentation_clips or {}
    unknown_clips = set(presentation_clips) - (extra_model_paths or set())
    if unknown_clips:
        raise ValueError(
            f"Presentation clips must target declared extra models: {sorted(unknown_clips)}"
        )
    models = sorted(
        {base.model_path for _, base in selected if base.model_path}
        | (extra_model_paths or set())
    )
    door_model_paths = {
        base.model_path
        for _, base in selected
        if base.record_type == "DOOR" and base.model_path
    }
    for model_path in models:
        model_suffix = Path(model_path).suffix.lower()
        logical_candidates = (
            [model_path, "trees\\" + model_path, "meshes\\" + model_path]
            if model_suffix == ".spt"
            else ["meshes\\" + model_path, model_path]
        )
        members = [candidate for candidate in logical_candidates if candidate in archive.members]
        if len(members) != 1:
            raise FileNotFoundError(
                f"Expected one owned mesh member for {model_path!r}, found {members}"
            )
        logical_path = members[0]
        presentation_clip = presentation_clips.get(model_path)
        asset_identity = logical_path
        if presentation_clip is not None:
            asset_identity += "\0" + json.dumps(
                presentation_clip,
                sort_keys=True,
                separators=(",", ":"),
            )
        asset_id = hashlib.sha256(asset_identity.encode()).hexdigest()[
            :compiler_configuration.asset_id_hex_characters
        ]
        member = archive.extract(logical_path)
        source_path = cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
        _atomic_bytes(source_path, member.data)
        output_root = cache_root / "generated" / "cells" / str(recipe["id"]) / "assets"
        gltf_path = output_root / f"{asset_id}.gltf"
        sidecar_path = output_root / f"{asset_id}.opennv.json"
        try:
            if model_suffix == ".spt":
                if presentation_clip is not None:
                    raise ValueError(
                        f"Presentation clipping is unsupported for SpeedTree assets: {model_path}"
                    )
                sidecar = export_speedtree_spt(
                    source_path,
                    member.logical_path,
                    gltf_path,
                    sidecar_path,
                    compiler_configuration,
                )
            else:
                sidecar = export_static_nif(
                    source_path,
                    member.logical_path,
                    gltf_path,
                    sidecar_path,
                    compiler_configuration,
                    strict=bool(recipe["exportStrict"]),
                    presentation_clip=presentation_clip,
                    require_door_articulation=model_path in door_model_paths,
                )
        except NoStaticPresentationGeometryError as error:
            if model_path in (extra_model_paths or set()):
                raise ValueError(
                    f"Required extra scene asset has no presentation geometry: "
                    f"{member.logical_path}"
                ) from error
            _atomic_json(sidecar_path, error.evidence)
            non_presentation_assets[model_path] = {
                "logicalPath": member.logical_path,
                "sourceSha256": member.sha256,
                "sourceArchive": getattr(member, "source_archive", None),
                "sourceArchiveSha256": getattr(
                    member, "source_archive_sha256", None
                ),
                "sidecar": str(sidecar_path.resolve()),
                "compiler": error.evidence["compiler"],
                "classification": error.evidence["classification"],
            }
            continue
        except ValueError as error:
            if (
                presentation_clip is not None
                and fully_clipped_model_paths is not None
                and str(error) == "Static presentation clip removed all supported geometry"
            ):
                # A presentation-only LOD asset can legitimately disappear
                # when the exact-reference authority tier covers every one of
                # its source triangles.  Keep that outcome explicit so the
                # caller can remove the block from its runtime ledger.
                fully_clipped_model_paths.add(model_path)
                continue
            raise ValueError(f"Cell asset export failed: {member.logical_path}: {error}") from error
        except Exception as error:
            raise ValueError(f"Cell asset export failed: {member.logical_path}: {error}") from error
        if compiler is None:
            compiler = sidecar["compiler"]
        elif compiler != sidecar["compiler"]:
            raise ValueError("Cell assets were produced by different compilers")
        collision_exported = bool(sidecar["coverage"]["collisionExported"])
        collision_source = authored_collision_source(sidecar["coverage"])
        assets[model_path] = {
            "id": asset_id,
            "logicalPath": member.logical_path,
            "sourceSha256": member.sha256,
            "sourceArchive": getattr(member, "source_archive", None),
            "sourceArchiveSha256": getattr(member, "source_archive_sha256", None),
            "model": str(gltf_path.resolve()),
            "sidecar": str(sidecar_path.resolve()),
            "surfaces": sidecar["coverage"]["surfaces"],
            "compiler": sidecar["compiler"],
            "presentationClip": sidecar["coverage"].get("presentationClip"),
            "collision": {
                "enabled": collision_exported,
                "source": collision_source,
                "blockTypes": sidecar["coverage"]["collisionBlockTypes"],
                "unsupportedReason": sidecar["coverage"]["collisionUnsupportedReason"],
            },
            "physics": {
                "enabled": bool(sidecar["coverage"]["dynamicPhysicsExported"]),
                "source": (
                    "NIF-authored-bhk-convex-rigid-body"
                    if sidecar["coverage"]["dynamicPhysicsExported"]
                    else "unsupported-or-absent"
                ),
                "bodies": len(sidecar["coverage"]["dynamicPhysicsBodies"]),
                "unsupportedReasons": sidecar["coverage"][
                    "dynamicPhysicsUnsupportedReasons"
                ],
            },
        }
        if sidecar.get("articulation") is not None:
            assets[model_path]["articulation"] = sidecar["articulation"]
        asset_sidecars[model_path] = sidecar

    binding_uses: dict[str, list[dict[str, object]]] = {}
    for model_path, sidecar in asset_sidecars.items():
        for surface_index, surface in enumerate(sidecar["surfaces"]):
            for request in texture_binding_requests(surface):
                binding_uses.setdefault(request["path"], []).append(
                    {
                        "modelPath": model_path,
                        "surfaceIndex": surface_index,
                        "surfaceName": surface["name"],
                        "role": request["role"],
                        "missingOwnedMember": request["missingOwnedMember"],
                    }
                )
    texture_aliases = {
        str(source): str(target) for source, target in recipe["textureAliases"].items()
    }
    texture_pipeline = (
        OwnedTexturePipeline(
            owned_archives,
            cache_root,
            texture_aliases,
            compiler_configuration,
        )
        if owned_archives is not None
        else TexturePipeline(
            texture_archive_paths,
            cache_root,
            texture_aliases,
            compiler_configuration,
        )
    )
    texture_artifacts = {}
    unresolved_texture_bindings: list[dict[str, object]] = []
    for requested in sorted(binding_uses):
        member_source_count = texture_pipeline.member_source_count(requested)
        if member_source_count == 1:
            texture_artifacts[requested] = texture_pipeline.prepare(requested)
            continue
        uses = binding_uses[requested]
        policies = {str(use["missingOwnedMember"]) for use in uses}
        if member_source_count == 0 and policies == {"unbound-no-substitution"}:
            unresolved_texture_bindings.append(
                {
                    "schema": "opennv-unresolved-owned-texture-binding/v1",
                    "status": "authored-binding-has-no-owned-member",
                    "requestedPath": requested,
                    "archivePath": texture_aliases.get(requested, requested),
                    "ownedMemberSources": 0,
                    "disposition": "unbound-no-substitution",
                    "uses": sorted(
                        uses,
                        key=lambda use: (
                            str(use["modelPath"]),
                            int(use["surfaceIndex"]),
                            str(use["role"]),
                        ),
                    ),
                }
            )
            continue
        raise FileNotFoundError(
            "Active authored texture binding did not resolve uniquely: "
            f"path={requested} sources={member_source_count} policies={sorted(policies)}"
        )
    texture_ids = {
        requested: artifact.asset_id
        for requested, artifact in texture_artifacts.items()
    }
    for model_path, asset in assets.items():
        asset["materials"] = material_bindings(
            asset_sidecars[model_path],
            texture_ids,
            compiler_configuration,
        )
    if compiler is None:
        raise ValueError(f"Cell recipe exported no asset compiler: {recipe['id']}")
    return (
        assets,
        asset_sidecars,
        texture_artifacts,
        compiler,
        non_presentation_assets,
        unresolved_texture_bindings,
    )
