"""Shared NIF, texture, material, interaction, and loadout scene preparation."""

from __future__ import annotations

import hashlib
import os
from pathlib import Path

from bsa_archive import BsaArchive
from cell_catalog import (
    ITEM_RECORD_TYPES,
    BaseObject,
    CellCatalog,
    PlacedReference,
)
from export_static_nif_gltf import export_static_nif
from actor_material import nif_material_roughness
from runtime_configuration import ContentCompilerConfiguration
from texture_pipeline import TexturePipeline


FORM_ID_RADIX = 16
ENVIRONMENT_TEXTURE_SLOT = 4
ENVIRONMENT_MASK_TEXTURE_SLOT = 5


def form_id(value: int) -> str:
    return f"{value:08x}"


def reference_selection_reason(base: BaseObject, recipe: dict[str, object]) -> str:
    selection = recipe["selection"]
    prefixes = tuple(str(value).lower() for value in selection["modelPrefixes"])
    record_types = {str(value) for value in selection["includeBaseRecordTypes"]}
    excluded_editor_ids = {str(value) for value in selection.get("excludeBaseEditorIds", [])}
    excluded_model_prefixes = tuple(
        str(value).lower() for value in selection.get("excludeModelPrefixes", [])
    )
    if not base.model_path:
        return "no-model"
    if not base.model_path.endswith(".nif"):
        return "unsupported-model-format"
    if base.editor_id in excluded_editor_ids:
        return "editor-only-base"
    if base.model_path.startswith(excluded_model_prefixes):
        return "special-effect-shader-required"
    if base.model_path.startswith(prefixes) or base.record_type in record_types:
        return "selected"
    return "outside-recipe"


def environment_texture_paths(surface: dict[str, object]) -> tuple[str | None, str | None]:
    material = surface["material"]
    if "sf_environment_mapping" not in set(material.get("shaderFlags1Enabled", [])):
        return None, None
    if "sf_2_envmap_light_fade" in set(material.get("shaderFlags2Enabled", [])):
        return None, None
    textures = surface["textures"]
    environment = (
        textures[ENVIRONMENT_TEXTURE_SLOT]
        if len(textures) > ENVIRONMENT_TEXTURE_SLOT and textures[ENVIRONMENT_TEXTURE_SLOT]
        else None
    )
    mask = (
        textures[ENVIRONMENT_MASK_TEXTURE_SLOT]
        if len(textures) > ENVIRONMENT_MASK_TEXTURE_SLOT and textures[ENVIRONMENT_MASK_TEXTURE_SLOT]
        else None
    )
    return environment, mask


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
                        "itemRecordType": item.record_type if item is not None else "",
                        "count": entry.count,
                        "resolved": item is not None,
                    }
                )
        return {"type": "container", "items": items}
    if base.record_type == "DOOR":
        return {"type": "door"}
    return None


def vr_smoke_loadout_manifest(
    recipe: dict[str, object],
    catalog: CellCatalog,
) -> dict[str, object]:
    configured = recipe["vrSmokeLoadout"]
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
        "modelPath": weapon_base.model_path,
        "ammoFormId": form_id(ammo_form_id),
        "ammoEditorId": ammo.editor_id,
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


def prepare_scene_assets(
    meshes_path: Path,
    texture_archive_paths: list[Path],
    cache_root: Path,
    recipe: dict[str, object],
    selected: list[tuple[PlacedReference, BaseObject]],
    compiler_configuration: ContentCompilerConfiguration,
    extra_model_paths: set[str] | None = None,
) -> tuple[
    dict[str, dict[str, object]],
    dict[str, dict[str, object]],
    dict[str, object],
    dict[str, str],
]:
    archive = BsaArchive(meshes_path)
    assets: dict[str, dict[str, object]] = {}
    asset_sidecars: dict[str, dict[str, object]] = {}
    compiler: dict[str, str] | None = None
    models = sorted(
        {base.model_path for _, base in selected if base.model_path}
        | (extra_model_paths or set())
    )
    for model_path in models:
        logical_path = "meshes\\" + model_path
        asset_id = hashlib.sha256(logical_path.encode()).hexdigest()[
            :compiler_configuration.asset_id_hex_characters
        ]
        member = archive.extract(logical_path)
        source_path = cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
        _atomic_bytes(source_path, member.data)
        output_root = cache_root / "generated" / "cells" / str(recipe["id"]) / "assets"
        gltf_path = output_root / f"{asset_id}.gltf"
        sidecar_path = output_root / f"{asset_id}.opennv.json"
        try:
            sidecar = export_static_nif(
                source_path,
                member.logical_path,
                gltf_path,
                sidecar_path,
                compiler_configuration,
                strict=False,
            )
        except Exception as error:
            raise ValueError(f"Cell asset export failed: {member.logical_path}: {error}") from error
        if compiler is None:
            compiler = sidecar["compiler"]
        elif compiler != sidecar["compiler"]:
            raise ValueError("Cell assets were produced by different compilers")
        assets[model_path] = {
            "id": asset_id,
            "logicalPath": member.logical_path,
            "sourceSha256": member.sha256,
            "model": str(gltf_path.resolve()),
            "sidecar": str(sidecar_path.resolve()),
            "surfaces": sidecar["coverage"]["surfaces"],
            "compiler": sidecar["compiler"],
            "collision": {
                "enabled": bool(sidecar["coverage"]["collisionExported"]),
                "source": (
                    "NIF-authored-bhk-packed-triangles"
                    if sidecar["coverage"]["collisionExported"]
                    else "unsupported-or-absent"
                ),
                "blockTypes": sidecar["coverage"]["collisionBlockTypes"],
                "unsupportedReason": sidecar["coverage"]["collisionUnsupportedReason"],
            },
        }
        asset_sidecars[model_path] = sidecar

    requested_textures = sorted(
        {
            texture
            for sidecar in asset_sidecars.values()
            for surface in sidecar["surfaces"]
            for texture in surface["textures"]
            if texture
        }
    )
    texture_pipeline = TexturePipeline(
        texture_archive_paths,
        cache_root,
        {str(source): str(target) for source, target in recipe.get("textureAliases", {}).items()},
        compiler_configuration,
    )
    texture_artifacts = {
        requested: texture_pipeline.prepare(requested) for requested in requested_textures
    }
    for model_path, asset in assets.items():
        bindings = []
        for surface_index, surface in enumerate(asset_sidecars[model_path]["surfaces"]):
            textures = surface["textures"]
            diffuse = textures[0] if len(textures) > 0 and textures[0] else None
            normal = textures[1] if len(textures) > 1 and textures[1] else None
            emissive = textures[2] if len(textures) > 2 and textures[2] else None
            material = surface["material"]
            environment, environment_mask = environment_texture_paths(surface)
            glossiness = float(
                material.get(
                    "glossiness",
                    compiler_configuration.default_material_glossiness,
                )
            )
            specular = [float(value) for value in material.get("specular", [0.0, 0.0, 0.0])]
            roughness, roughness_source = nif_material_roughness(
                specular,
                glossiness,
                compiler_configuration,
            )
            unshaded = "BSShaderNoLightingProperty" in surface["propertyTypes"]
            emissive_color = [float(value) for value in material.get("emissive", [0.0, 0.0, 0.0])]
            emissive_controlled = bool(material.get("emissiveControlled", False))
            emissive_active = not unshaded and (emissive is not None or emissive_controlled)
            emission_texture = emissive if emissive_active else None
            if not emissive_active:
                emissive_color = [0.0, 0.0, 0.0]
            alpha = float(material.get("alpha", 1.0))
            bindings.append(
                {
                    "surfaceIndex": surface_index,
                    "name": surface["name"],
                    "diffuseTextureId": texture_artifacts[diffuse].asset_id if diffuse else None,
                    "normalTextureId": texture_artifacts[normal].asset_id if normal else None,
                    "emissiveTextureId": (
                        texture_artifacts[emission_texture].asset_id if emission_texture else None
                    ),
                    "environmentTextureId": (
                        texture_artifacts[environment].asset_id if environment else None
                    ),
                    "environmentMaskTextureId": (
                        texture_artifacts[environment_mask].asset_id if environment_mask else None
                    ),
                    "environmentMapScale": float(material.get("environmentMapScale", 1.0)),
                    "emissiveColor": emissive_color,
                    "emissiveReplace": emissive_controlled and emissive is None,
                    "baseColorFactor": [
                        *[float(value) for value in material.get("baseColor", [1.0, 1.0, 1.0])],
                        alpha,
                    ],
                    "roughness": roughness,
                    "roughnessSource": roughness_source,
                    "alphaContract": material["alphaContract"],
                    "vertexColorMode": material["vertexColorMode"],
                    "doubleSided": int(material.get("stencilDrawMode", 1)) == 3,
                    "unshaded": unshaded,
                }
            )
        asset["materials"] = bindings
    if compiler is None:
        raise ValueError(f"Cell recipe exported no asset compiler: {recipe['id']}")
    return assets, asset_sidecars, texture_artifacts, compiler
