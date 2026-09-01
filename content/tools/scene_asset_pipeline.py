"""Shared NIF, texture, material, interaction, and loadout scene preparation."""

from __future__ import annotations

import hashlib
import json
import os
import re
from pathlib import Path

from bsa_archive import BsaArchive
from cell_catalog import (
    ITEM_RECORD_TYPES,
    BaseObject,
    CellCatalog,
    PlacedReference,
    ScriptSource,
)
from crafting_catalog import recipe_menu_category_editor_id
from compiler_provenance import compiler_provenance
from export_static_nif_gltf import NoStaticPresentationGeometryError, export_static_nif
from export_nif_particle_effect import (
    UnsupportedParticleEffectError,
    export_particle_nif,
)
from material_contract import (
    material_bindings,
    texture_binding_requests,
)
from owned_archive_stack import OwnedArchiveStack
from runtime_configuration import ContentCompilerConfiguration
from speedtree_spt import export_speedtree_spt
from texture_pipeline import OwnedTexturePipeline, TexturePipeline


FORM_ID_RADIX = 16
ITEM_DEFINITION_SCHEMA = "opennv-owned-item-definition/v1"


def item_definition_manifest(
    base: BaseObject,
    catalog: CellCatalog,
) -> dict[str, object]:
    item = catalog.items.get(base.form_id)
    source: dict[str, object] = {
        "recordFormId": form_id(base.form_id),
        "recordType": base.record_type,
    }
    if item is None:
        source["economicsStatus"] = "unsupported-record-layout"
    else:
        source.update(
            {
                "economicsStatus": "source-bound",
                "subrecord": item.source_subrecord,
                "layout": item.source_layout,
            }
        )
    return {
        "schema": ITEM_DEFINITION_SCHEMA,
        "formId": form_id(base.form_id),
        "editorId": base.editor_id,
        "displayName": base.display_name or "",
        "recordType": base.record_type,
        "source": source,
    }


def _item_economics_manifest(base: BaseObject, catalog: CellCatalog) -> dict[str, object]:
    item = catalog.items.get(base.form_id)
    return (
        {"itemValue": item.value, "itemWeight": item.weight}
        if item is not None
        else {}
    )


def _quest_identity(catalog: CellCatalog, editor_id: str) -> tuple[int, str]:
    matches = [
        quest
        for quest in catalog.quests.values()
        if quest.editor_id.casefold() == editor_id.casefold()
    ]
    if len(matches) != 1:
        raise ValueError(f"Scripted activator quest identity is ambiguous: {editor_id}")
    return matches[0].form_id, matches[0].editor_id


def _script_event_block(source: str, event: str) -> str | None:
    match = re.search(
        rf"\bbegin\s+{re.escape(event)}\b(?P<body>.*?)\bend\b",
        source,
        re.IGNORECASE | re.DOTALL,
    )
    return match.group("body") if match is not None else None


def _delayed_objective_event(
    source: str,
    event: str,
    catalog: CellCatalog,
) -> dict[str, object] | None:
    event_body = _script_event_block(source, event)
    if event_body is None:
        return None
    guard = re.search(
        r"if\s*\(\s*(?P<state>[A-Za-z_][A-Za-z0-9_]*)\s*==\s*0\s*"
        r"&&\s*GetObjectiveDisplayed\s+(?P<quest>[A-Za-z0-9_]+)\s+"
        r"(?P<index>\d+)\s*\)",
        event_body,
        re.IGNORECASE,
    )
    if guard is None:
        return None
    state = guard.group("state")
    if not re.search(rf"\bset\s+{re.escape(state)}\s+to\s+1\b", event_body, re.IGNORECASE):
        return None
    timer = re.search(
        r"\bset\s+[A-Za-z_][A-Za-z0-9_]*\s+to\s+"
        r"(?P<seconds>\d+(?:\.\d+)?)\b",
        event_body,
        re.IGNORECASE,
    )
    if timer is None:
        return None
    game_mode = _script_event_block(source, "gamemode")
    if game_mode is None:
        return None
    result = re.search(
        rf"\b(?:if|elseif)\s*\(?\s*{re.escape(state)}\s*==\s*1\s*\)?"
        r"(?P<body>.*?)(?=\belseif\b|\Z)",
        game_mode,
        re.IGNORECASE | re.DOTALL,
    )
    if result is None:
        return None
    result_body = result.group("body")
    if not re.search(rf"\bset\s+{re.escape(state)}\s+to\s+2\b", result_body, re.IGNORECASE):
        return None
    stages = re.findall(
        r"\bSetStage\s+(?P<quest>[A-Za-z0-9_]+)\s+(?P<stage>\d+)\b",
        result_body,
        re.IGNORECASE,
    )
    objectives = re.findall(
        r"\bSetObjectiveCompleted\s+(?P<quest>[A-Za-z0-9_]+)\s+"
        r"(?P<index>\d+)\s+1\b",
        result_body,
        re.IGNORECASE,
    )
    if len(stages) != 1 or len(objectives) > 1 or (event.casefold() == "ongrab" and len(objectives) != 1):
        return None
    guard_form_id, guard_editor_id = _quest_identity(catalog, guard.group("quest"))
    stage_form_id, stage_editor_id = _quest_identity(catalog, stages[0][0])
    commands: list[dict[str, object]] = [
        {
            "kind": "setStage",
            "questFormId": form_id(stage_form_id),
            "questEditorId": stage_editor_id,
            "stage": int(stages[0][1]),
        }
    ]
    if objectives:
        objective_form_id, objective_editor_id = _quest_identity(catalog, objectives[0][0])
        commands.append(
            {
                "kind": "objective",
                "questFormId": form_id(objective_form_id),
                "questEditorId": objective_editor_id,
                "index": int(objectives[0][1]),
                "state": "completed",
                "enabled": True,
            }
        )
    return {
        "event": event.casefold().removeprefix("on"),
        "guard": {
            "questFormId": form_id(guard_form_id),
            "questEditorId": guard_editor_id,
            "objectiveIndex": int(guard.group("index")),
            "state": "displayed",
        },
        "delaySeconds": float(timer.group("seconds")),
        "commands": commands,
    }


def _crafting_entry_manifest(
    item_form_id: int,
    count: int,
    catalog: CellCatalog,
) -> dict[str, object]:
    item = catalog.base_objects.get(item_form_id)
    if item is None or item.record_type not in ITEM_RECORD_TYPES or not item.display_name:
        raise ValueError(f"Crafting item identity does not resolve: {item_form_id:08x}")
    return {
        "itemFormId": form_id(item.form_id),
        "itemEditorId": item.editor_id,
        "itemDisplayName": item.display_name,
        "itemRecordType": item.record_type,
        "count": count,
        "itemDefinition": item_definition_manifest(item, catalog),
        **_item_economics_manifest(item, catalog),
    }


def _crafting_station_manifest(
    base: BaseObject,
    script: ScriptSource,
    catalog: CellCatalog,
) -> dict[str, object] | None:
    try:
        category_editor_id = recipe_menu_category_editor_id(script.source)
    except ValueError:
        return None
    categories = [
        category
        for category in catalog.crafting_categories.values()
        if category.editor_id.casefold() == category_editor_id.casefold()
    ]
    if len(categories) != 1:
        raise ValueError(
            f"ACTI {base.form_id:08x} recipe category is ambiguous: {category_editor_id}"
        )
    category = categories[0]
    supported = [
        recipe
        for recipe in catalog.crafting_recipes.values()
        if recipe.category_form_id == category.form_id
        and recipe.skill_actor_value_form_id is None
        and recipe.required_skill_level == 0
        and not recipe.condition_data
    ]
    recipes = [
        {
            "schema": "opennv-owned-crafting-recipe/v1",
            "formId": form_id(recipe.form_id),
            "editorId": recipe.editor_id,
            "displayName": recipe.display_name,
            "categoryFormId": form_id(recipe.category_form_id),
            "subcategoryFormId": form_id(recipe.subcategory_form_id),
            "requiredSkillLevel": recipe.required_skill_level,
            "ingredients": [
                _crafting_entry_manifest(entry.item_form_id, entry.count, catalog)
                for entry in recipe.ingredients
            ],
            "outputs": [
                _crafting_entry_manifest(entry.item_form_id, entry.count, catalog)
                for entry in recipe.outputs
            ],
        }
        for recipe in sorted(supported, key=lambda value: value.form_id)
    ]
    return {
        "type": "crafting-station",
        "script": {"formId": form_id(script.form_id), "editorId": script.editor_id},
        "category": {
            "formId": form_id(category.form_id),
            "editorId": category.editor_id,
            "displayName": category.display_name,
            "sourceKind": category.source_kind,
        },
        "recipes": recipes,
        "support": (
            "unconditioned-zero-skill-recipes"
            if recipes
            else "unsupported-conditioned-or-skilled-recipes"
        ),
    }


def _scripted_activator_manifest(base: BaseObject, catalog: CellCatalog) -> dict[str, object]:
    if base.attached_script_form_id is None:
        raise ValueError("ACTI scripted activator has no source script identity")
    script = catalog.scripts.get(base.attached_script_form_id)
    if script is None or not script.editor_id:
        raise ValueError(
            f"ACTI {base.form_id:08x} attached SCPT does not resolve: "
            f"{base.attached_script_form_id:08x}"
        )
    crafting = _crafting_station_manifest(base, script, catalog)
    if crafting is not None:
        return crafting
    events = [
        event
        for event in (
            _delayed_objective_event(script.source, "OnGrab", catalog),
            _delayed_objective_event(script.source, "OnRelease", catalog),
        )
        if event is not None
    ]
    return {
        "type": "scripted-activator",
        "script": {
            "formId": form_id(script.form_id),
            "editorId": script.editor_id,
        },
        "events": events,
        "support": "delayed-objective-events" if len(events) == 2 else "unsupported-script-source",
    }


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


def authored_collision_face_selection(
    logical_path: str,
    collision_source: str,
) -> str:
    """Select the source collision face family without reference-name heuristics."""

    canonical_path = logical_path.replace("/", "\\").lower()
    is_owned_road = canonical_path.startswith("meshes\\landscape\\roads\\") or (
        canonical_path.startswith("meshes\\scol\\")
        and canonical_path.rsplit("\\", 1)[-1].startswith("scolroad")
    )
    if is_owned_road:
        if collision_source != "NIF-authored-bhk-packed-triangles":
            raise ValueError(
                "Owned road collision requires its packed-triangle source contract"
            )
        return "source-upward-walkable-deck"
    return "all-source-faces"


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
    if base.record_type == "ACTI" and base.attached_script_form_id is not None:
        return _scripted_activator_manifest(base, catalog)
    if base.record_type in ITEM_RECORD_TYPES:
        interaction = {
            "type": "pickup",
            "itemFormId": form_id(base.form_id),
            "itemEditorId": base.editor_id,
            "itemDisplayName": base.display_name or "",
            "itemRecordType": base.record_type,
            "count": 1,
            "itemDefinition": item_definition_manifest(base, catalog),
            **_item_economics_manifest(base, catalog),
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
                        **(
                            {
                                "itemDefinition": item_definition_manifest(item, catalog),
                                **_item_economics_manifest(item, catalog),
                            }
                            if item is not None
                            else {}
                        ),
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
    particle_effect_model_paths: set[str] | None = None,
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
                if model_path in (particle_effect_model_paths or set()):
                    sidecar = export_particle_nif(
                        source_path,
                        member.logical_path,
                        gltf_path,
                        sidecar_path,
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
        except UnsupportedParticleEffectError as error:
            evidence = {
                "schema": "opennv-nif-non-presentation/v1",
                "status": "owned-nif-particle-presentation-unsupported",
                "source": {
                    "logicalPath": member.logical_path,
                    "sha256": member.sha256,
                },
                "compiler": compiler_provenance("static"),
                "classification": {
                    "source": "owned-NIF-particle-graph",
                    "reason": str(error),
                    "disposition": "exclude-reference-from-presentation",
                },
            }
            _atomic_json(sidecar_path, evidence)
            non_presentation_assets[model_path] = {
                "logicalPath": member.logical_path,
                "sourceSha256": member.sha256,
                "sourceArchive": getattr(member, "source_archive", None),
                "sourceArchiveSha256": getattr(member, "source_archive_sha256", None),
                "sidecar": str(sidecar_path.resolve()),
                "compiler": evidence["compiler"],
                "classification": evidence["classification"],
            }
            continue
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
        collision_face_selection = authored_collision_face_selection(
            member.logical_path,
            collision_source,
        )
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
            "controllerPlayback": sidecar["coverage"].get(
                "sourceControllerPlayback"
            ),
            "collision": {
                "enabled": collision_exported,
                "source": collision_source,
                "faceSelection": collision_face_selection,
                "blockTypes": sidecar["coverage"]["collisionBlockTypes"],
                "unsupportedReason": sidecar["coverage"]["collisionUnsupportedReason"],
            },
            "physics": {
                "enabled": bool(sidecar["coverage"]["dynamicPhysicsExported"]),
                "source": (
                    "NIF-authored-bhk-dynamic-rigid-body"
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
        if sidecar.get("particleEffect") is not None:
            assets[model_path]["particleEffect"] = sidecar["particleEffect"]
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
        particle_effect = sidecar.get("particleEffect")
        if isinstance(particle_effect, dict):
            for system_index, system in enumerate(particle_effect["systems"]):
                requested = str(system["texturePath"])
                binding_uses.setdefault(requested, []).append(
                    {
                        "modelPath": model_path,
                        "surfaceIndex": system_index,
                        "surfaceName": str(system["name"]),
                        "role": "particle-base-color",
                        "missingOwnedMember": "fail-closed",
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
        if "particleEffect" in asset:
            for system in asset["particleEffect"]["systems"]:
                system["textureAssetId"] = texture_ids[str(system["texturePath"])]
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
