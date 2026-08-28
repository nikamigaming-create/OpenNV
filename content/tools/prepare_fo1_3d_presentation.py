#!/usr/bin/env python3
"""Prepare the owned FNV presentation kit for the exact Fallout 1 V13ENT slice."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import shutil
import sys
import tempfile
from pathlib import Path

from actor_gltf import (
    ActorAnimation,
    ActorComponent,
    ActorGltfInput,
    export_actor_gltf,
)
from bsa_archive import BsaArchive
from cell_scene import environment_texture_paths
from export_static_nif_gltf import export_static_nif
from prepare_legal_assets import file_sha256, find_required_file
from prepare_actor import prepare_actor
from runtime_configuration import load_runtime_configuration
from texture_pipeline import TexturePipeline


RECIPE_SCHEMA = "opennv-fo1-3d-presentation-recipe/v1"
MANIFEST_SCHEMA = "opennv-fo1-3d-presentation/v1"


def _read_json(path: Path) -> dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def _write_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    os.replace(temporary, path)


def _verified_member(archive: BsaArchive, row: dict[str, object]):
    member = archive.extract(str(row["path"]))
    if member.sha256 != str(row["sha256"]):
        raise ValueError(f"Owned FNV member hash drift: {row['path']}")
    return member


def _verify_export(asset: dict[str, object]) -> dict[str, object]:
    model = Path(str(asset["model"]))
    sidecar_path = Path(str(asset["sidecar"]))
    sidecar = _read_json(sidecar_path)
    outputs = sidecar["outputs"]
    if file_sha256(model) != outputs["gltf"]["sha256"]:
        raise ValueError(f"Cave donor model hash drift: {asset['logicalPath']}")
    buffer = model.with_name(str(outputs["buffer"]["file"]))
    if file_sha256(buffer) != outputs["buffer"]["sha256"]:
        raise ValueError(f"Cave donor buffer hash drift: {asset['logicalPath']}")
    if int(asset["surfaces"]) != int(sidecar["coverage"]["surfaces"]):
        raise ValueError(f"Cave donor surface count drift: {asset['logicalPath']}")
    return sidecar


def _static_gltf_bounds(model_path: Path, units_to_meters: float) -> dict[str, list[float]]:
    document = _read_json(model_path)
    nodes = document.get("nodes", [])
    if any(
        any(field in node for field in ("matrix", "translation", "rotation", "scale"))
        for node in nodes
    ):
        raise ValueError(f"Cave donor glTF has unsupported node transforms: {model_path}")
    accessors = document.get("accessors", [])
    minimum = [math.inf, math.inf, math.inf]
    maximum = [-math.inf, -math.inf, -math.inf]
    position_accessors: set[int] = set()
    for mesh in document.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            attributes = primitive.get("attributes", {})
            if "POSITION" in attributes:
                position_accessors.add(int(attributes["POSITION"]))
    for index in position_accessors:
        accessor = accessors[index]
        if accessor.get("type") != "VEC3" or "min" not in accessor or "max" not in accessor:
            raise ValueError(f"Cave donor POSITION accessor lacks bounds: {model_path}#{index}")
        for axis in range(3):
            minimum[axis] = min(minimum[axis], float(accessor["min"][axis]))
            maximum[axis] = max(maximum[axis], float(accessor["max"][axis]))
    if not position_accessors or not all(math.isfinite(value) for value in minimum + maximum):
        raise ValueError(f"Cave donor glTF has no finite bounds: {model_path}")
    size = [maximum[axis] - minimum[axis] for axis in range(3)]
    if min(size) <= 0.0 or units_to_meters <= 0.0:
        raise ValueError(f"Cave donor glTF has invalid bounds: {model_path}")
    return {
        "positionGodotUnits": minimum,
        "sizeGodotUnits": size,
        "positionMeters": [value * units_to_meters for value in minimum],
        "sizeMeters": [value * units_to_meters for value in size],
    }


def _select_cave_kit(
    donor: dict[str, object], recipe: dict[str, object]
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    if donor.get("schema") != "opennv-cell-scene/v6":
        raise ValueError("Unexpected donor cave scene schema")
    cave_recipe = recipe["caveKit"]
    if donor.get("recipe") != cave_recipe["donorRecipe"]:
        raise ValueError("Unexpected donor cave recipe")
    assets_by_path = {str(row["logicalPath"]).lower(): row for row in donor["assets"]}
    selected = []
    texture_ids: set[str] = set()
    for expected in cave_recipe["assets"]:
        path = str(expected["path"])
        asset = assets_by_path.get(path.lower())
        if asset is None or str(asset["sourceSha256"]) != str(expected["sha256"]):
            raise ValueError(f"Cave donor asset identity drift: {path}")
        _verify_export(asset)
        units_to_meters = float(cave_recipe["unitsToMeters"])
        row = {
            **asset,
            "role": expected["role"],
            "unitsToMeters": units_to_meters,
            "bounds": _static_gltf_bounds(Path(str(asset["model"])), units_to_meters),
        }
        selected.append(row)
        for material in asset["materials"]:
            for field in (
                "diffuseTextureId",
                "normalTextureId",
                "emissiveTextureId",
                "environmentTextureId",
                "environmentMaskTextureId",
            ):
                value = material.get(field)
                if value:
                    texture_ids.add(str(value))
    textures_by_id = {str(row["id"]): row for row in donor["textures"]}
    if not texture_ids.issubset(textures_by_id):
        raise ValueError("Cave donor is missing selected material textures")
    textures = [textures_by_id[key] for key in sorted(texture_ids)]
    for texture in textures:
        if file_sha256(Path(str(texture["png"]))) != str(texture["pngSha256"]):
            raise ValueError(f"Cave donor texture hash drift: {texture['id']}")
    return selected, textures


def _player_attachment_transform(
    weapon_recipe: dict[str, object], player_model_path: Path
) -> dict[str, object]:
    player_model = _read_json(player_model_path)
    player_nodes = player_model.get("nodes", [])
    source_marker_name = str(weapon_recipe["attachment"]["sourceMarkerNode"])
    source_markers = [
        (index, row)
        for index, row in enumerate(player_nodes)
        if str(row.get("name")) == source_marker_name
    ]
    if len(source_markers) != 1:
        raise ValueError(
            f"Owned Vault Dweller requires one {source_marker_name} attachment marker"
        )
    source_marker_index, source_marker = source_markers[0]
    marker_parents = [
        row
        for row in player_nodes
        if source_marker_index in [int(value) for value in row.get("children", [])]
    ]
    skeleton_bone = str(weapon_recipe["attachment"]["skeletonBone"])
    if len(marker_parents) != 1 or str(marker_parents[0].get("name")) != skeleton_bone:
        raise ValueError(
            f"Owned Vault Dweller {source_marker_name} parent is not {skeleton_bone}"
        )
    marker_translation = [float(value) for value in source_marker.get("translation", [])]
    marker_rotation = [float(value) for value in source_marker.get("rotation", [])]
    marker_scale = [float(value) for value in source_marker.get("scale", [])]
    if (
        len(marker_translation) != 3
        or len(marker_rotation) != 4
        or len(marker_scale) != 3
        or not all(
            math.isfinite(value)
            for value in marker_translation + marker_rotation + marker_scale
        )
    ):
        raise ValueError("Owned Vault Dweller weapon marker transform is incomplete")
    return {
        **weapon_recipe["attachment"],
        "positionGodotUnits": marker_translation,
        "rotationQuaternion": marker_rotation,
        "scale": marker_scale,
    }


def _select_player_weapon(
    donor: dict[str, object], recipe: dict[str, object], player_model_path: Path
) -> dict[str, object]:
    if donor.get("schema") != "opennv-cell-scene/v6":
        raise ValueError("Unexpected donor weapon scene schema")
    weapon_recipe = recipe["player"]["thirdPersonWeapon"]
    if weapon_recipe.get("schema") != (
        "opennv-fo1-third-person-held-weapon-recipe/v1"
    ):
        raise ValueError("Unexpected third-person weapon recipe schema")
    loadout = donor.get("vr", {}).get("startingLoadout", {})
    expected_model_path = _texture_key(str(weapon_recipe["path"]))
    loadout_model_path = _texture_key(str(loadout.get("modelPath", "")))
    if not loadout_model_path.startswith("meshes\\"):
        loadout_model_path = "meshes\\" + loadout_model_path
    if (
        str(loadout.get("weaponFormId")) != str(weapon_recipe["weaponFormId"])
        or str(loadout.get("weaponEditorId")) != str(weapon_recipe["weaponEditorId"])
        or loadout_model_path != expected_model_path
    ):
        raise ValueError("Owned FNV 10mm loadout identity drift")

    assets_by_path = {
        _texture_key(str(row["logicalPath"])): row for row in donor["assets"]
    }
    asset = assets_by_path.get(expected_model_path)
    if asset is None or str(asset["sourceSha256"]) != str(weapon_recipe["sha256"]):
        raise ValueError("Owned FNV 10mm presentation asset identity drift")
    sidecar = _verify_export(asset)
    if int(asset["surfaces"]) != int(weapon_recipe["expected"]["surfaces"]):
        raise ValueError("Owned FNV 10mm surface coverage drift")
    markers = sidecar.get("attachmentMarkers", [])
    marker_names = {str(row["name"]) for row in markers}
    expected_markers = {
        str(value) for value in weapon_recipe["expected"]["attachmentMarkers"]
    }
    if not expected_markers.issubset(marker_names):
        raise ValueError("Owned FNV 10mm attachment-marker coverage drift")
    muzzle_name = str(weapon_recipe["attachment"]["muzzleMarker"])
    muzzle = [row for row in markers if str(row["name"]) == muzzle_name]
    if len(muzzle) != 1:
        raise ValueError("Owned FNV 10mm must expose exactly one muzzle marker")

    attachment_transform = _player_attachment_transform(weapon_recipe, player_model_path)

    texture_ids = {
        str(material[field])
        for material in asset["materials"]
        for field in (
            "diffuseTextureId",
            "normalTextureId",
            "emissiveTextureId",
            "environmentTextureId",
            "environmentMaskTextureId",
        )
        if material.get(field)
    }
    textures_by_id = {str(row["id"]): row for row in donor["textures"]}
    if not texture_ids.issubset(textures_by_id):
        raise ValueError("Owned FNV 10mm presentation is missing material textures")
    textures = [textures_by_id[key] for key in sorted(texture_ids)]
    for texture in textures:
        if file_sha256(Path(str(texture["png"]))) != str(texture["pngSha256"]):
            raise ValueError(f"Owned FNV 10mm texture hash drift: {texture['id']}")

    return {
        "schema": "opennv-fo1-third-person-held-weapon/v1",
        "role": weapon_recipe["role"],
        "visibility": weapon_recipe["visibility"],
        "weaponFormId": weapon_recipe["weaponFormId"],
        "weaponEditorId": weapon_recipe["weaponEditorId"],
        "gameplayPid": weapon_recipe["gameplayPid"],
        "unitsToMeters": weapon_recipe["unitsToMeters"],
        "asset": asset,
        "textures": textures,
        "attachment": {
            **attachment_transform,
            "muzzlePositionGodotUnits": muzzle[0]["positionGodotUnits"],
            "shellPositionGodotUnits": next(
                row["positionGodotUnits"]
                for row in markers
                if str(row["name"]) == str(weapon_recipe["attachment"]["shellMarker"])
            ),
        },
        "coverage": {
            "surfaces": asset["surfaces"],
            "attachmentMarkers": sorted(marker_names),
            "materialTextures": len(textures),
        },
    }


def _texture_key(value: str) -> str:
    return value.replace("/", "\\").lstrip("\\").lower()


def _material_bindings(
    sidecar: dict[str, object], texture_ids: dict[str, str]
) -> list[dict[str, object]]:
    def texture_id(value: str | None) -> str | None:
        if value is None:
            return None
        key = _texture_key(value)
        if key not in texture_ids:
            raise ValueError(f"Direct cave asset texture was not prepared: {value}")
        return texture_ids[key]

    bindings = []
    for surface_index, surface in enumerate(sidecar["surfaces"]):
        textures = surface["textures"]
        diffuse = textures[0] if len(textures) > 0 and textures[0] else None
        normal = textures[1] if len(textures) > 1 and textures[1] else None
        emissive = textures[2] if len(textures) > 2 and textures[2] else None
        material = surface["material"]
        environment, environment_mask = environment_texture_paths(surface)
        glossiness = float(material.get("glossiness", 10.0))
        specular = [float(value) for value in material.get("specular", [0.0, 0.0, 0.0])]
        roughness = (
            1.0
            if max(specular) <= 1.0e-6
            else max(0.08, min(1.0, math.sqrt(2.0 / (glossiness + 2.0))))
        )
        unshaded = "BSShaderNoLightingProperty" in surface["propertyTypes"]
        emissive_color = [
            float(value) for value in material.get("emissive", [0.0, 0.0, 0.0])
        ]
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
                "diffuseTextureId": texture_id(diffuse),
                "normalTextureId": texture_id(normal),
                "emissiveTextureId": texture_id(emission_texture),
                "environmentTextureId": texture_id(environment),
                "environmentMaskTextureId": texture_id(environment_mask),
                "environmentMapScale": float(material.get("environmentMapScale", 1.0)),
                "emissiveColor": emissive_color,
                "emissiveReplace": emissive_controlled and emissive is None,
                "baseColorFactor": [
                    *[float(value) for value in material.get("baseColor", [1.0, 1.0, 1.0])],
                    alpha,
                ],
                "roughness": roughness,
                "alphaContract": material["alphaContract"],
                "vertexColorMode": material["vertexColorMode"],
                "doubleSided": int(material.get("stencilDrawMode", 1)) == 3,
                "unshaded": unshaded,
            }
        )
    return bindings


def _relocated_texture_manifest(
    artifact: object, staging: Path, output_root: Path
) -> dict[str, object]:
    row = artifact.manifest()
    row["png"] = str((output_root / Path(row["png"]).relative_to(staging)).resolve())
    if "cubeFaces" in row:
        row["cubeFaces"] = [
            {
                **face,
                "png": str(
                    (output_root / Path(face["png"]).relative_to(staging)).resolve()
                ),
            }
            for face in row["cubeFaces"]
        ]
    return row


def _prepare_direct_cave_assets(
    meshes: BsaArchive,
    texture_archive_paths: list[Path],
    cave_recipe: dict[str, object],
    donor_textures: list[dict[str, object]],
    staging: Path,
    output_root: Path,
    generated_subdirectory: str = "cave-kit",
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    direct_recipe = cave_recipe.get("directAssets", [])
    if not direct_recipe:
        return [], donor_textures
    units_to_meters = float(cave_recipe["unitsToMeters"])
    sidecars: dict[str, dict[str, object]] = {}
    staging_assets: dict[str, tuple[dict[str, object], object, Path, Path]] = {}
    for expected in direct_recipe:
        member = meshes.extract(str(expected["path"]))
        if member.sha256 != str(expected["sha256"]):
            raise ValueError(f"Direct cave asset identity drift: {expected['path']}")
        asset_id = hashlib.sha256(member.logical_path.encode()).hexdigest()[:20]
        source_path = staging / "source" / Path(member.logical_path.replace("\\", "/"))
        source_path.parent.mkdir(parents=True, exist_ok=True)
        source_path.write_bytes(member.data)
        generated = staging / "generated" / generated_subdirectory / "assets"
        model_path = generated / f"{asset_id}.gltf"
        sidecar_path = generated / f"{asset_id}.opennv.json"
        sidecar = export_static_nif(
            source_path,
            member.logical_path,
            model_path,
            sidecar_path,
            load_runtime_configuration().content_compiler,
            strict=False,
        )
        sidecars[asset_id] = sidecar
        staging_assets[asset_id] = (expected, member, model_path, sidecar_path)

    textures_by_requested = {
        _texture_key(str(row["requestedPath"])): row for row in donor_textures
    }
    requested = sorted(
        {
            _texture_key(str(texture))
            for sidecar in sidecars.values()
            for surface in sidecar["surfaces"]
            for texture in surface["textures"]
            if texture
        }
    )
    pipeline = TexturePipeline(texture_archive_paths, staging, {})
    generated_texture_rows = []
    for texture in requested:
        if texture in textures_by_requested:
            continue
        artifact = pipeline.prepare(texture)
        row = _relocated_texture_manifest(artifact, staging, output_root)
        textures_by_requested[texture] = row
        generated_texture_rows.append(row)
    texture_ids = {
        requested_path: str(row["id"])
        for requested_path, row in textures_by_requested.items()
    }

    assets = []
    for asset_id, (expected, member, model_path, sidecar_path) in staging_assets.items():
        sidecar = sidecars[asset_id]
        final_generated = output_root / "generated" / generated_subdirectory / "assets"
        assets.append(
            {
                "id": asset_id,
                "logicalPath": member.logical_path,
                "sourceSha256": member.sha256,
                "model": str((final_generated / model_path.name).resolve()),
                "sidecar": str((final_generated / sidecar_path.name).resolve()),
                "surfaces": sidecar["coverage"]["surfaces"],
                "materials": _material_bindings(sidecar, texture_ids),
                "role": expected["role"],
                "unitsToMeters": units_to_meters,
                "bounds": _static_gltf_bounds(model_path, units_to_meters),
            }
        )
    textures = [*donor_textures, *generated_texture_rows]
    if len({str(row["id"]) for row in textures}) != len(textures):
        raise ValueError("Direct and donor cave textures have duplicate identities")
    return assets, textures


def _prepare_combat_presentation(
    meshes: BsaArchive,
    sound_archive: BsaArchive,
    texture_archive_paths: list[Path],
    recipe: dict[str, object],
    player_model_path: Path,
    known_textures: list[dict[str, object]],
    staging: Path,
    output_root: Path,
) -> tuple[dict[str, object], dict[str, object], list[dict[str, object]]]:
    combat_recipe = recipe["combatPresentation"]
    if combat_recipe.get("schema") != "opennv-fo1-combat-presentation-recipe/v1":
        raise ValueError("Unexpected Fallout combat-presentation recipe")
    melee_recipe = recipe["player"]["thirdPersonMeleeWeapon"]
    direct_recipe = {
        "unitsToMeters": combat_recipe["unitsToMeters"],
        "directAssets": [
            *combat_recipe["staticAssets"],
            {
                "role": "held-melee-weapon",
                "path": melee_recipe["path"],
                "sha256": melee_recipe["sha256"],
            },
        ],
    }
    assets, all_textures = _prepare_direct_cave_assets(
        meshes,
        texture_archive_paths,
        direct_recipe,
        known_textures,
        staging,
        output_root,
        generated_subdirectory="combat",
    )
    by_role = {str(row["role"]): row for row in assets}
    if len(by_role) != len(assets):
        raise ValueError("Fallout combat presentation contains duplicate asset roles")
    melee_asset = by_role.get("held-melee-weapon")
    if melee_asset is None or int(melee_asset["surfaces"]) != int(
        melee_recipe["expected"]["surfaces"]
    ):
        raise ValueError("Owned FNV combat-knife presentation coverage drift")
    casing_recipe = combat_recipe["staticAssets"][0]
    casing_asset = by_role.get(str(casing_recipe["role"]))
    if casing_asset is None or int(casing_asset["surfaces"]) != int(
        casing_recipe["expectedSurfaces"]
    ):
        raise ValueError("Owned FNV pistol-casing presentation coverage drift")

    textures_by_id = {str(row["id"]): row for row in all_textures}

    def selected_textures(asset: dict[str, object]) -> list[dict[str, object]]:
        texture_ids = {
            str(material[field])
            for material in asset["materials"]
            for field in (
                "diffuseTextureId",
                "normalTextureId",
                "emissiveTextureId",
                "environmentTextureId",
                "environmentMaskTextureId",
            )
            if material.get(field)
        }
        if not texture_ids.issubset(textures_by_id):
            raise ValueError("Fallout combat asset texture coverage drift")
        return [textures_by_id[key] for key in sorted(texture_ids)]

    melee_textures = selected_textures(melee_asset)
    melee_weapon = {
        "schema": "opennv-fo1-third-person-held-weapon/v1",
        "role": melee_recipe["role"],
        "visibility": melee_recipe["visibility"],
        "weaponFormId": melee_recipe["weaponFormId"],
        "weaponEditorId": melee_recipe["weaponEditorId"],
        "gameplayPid": melee_recipe["gameplayPid"],
        "unitsToMeters": melee_recipe["unitsToMeters"],
        "asset": melee_asset,
        "textures": melee_textures,
        "attachment": _player_attachment_transform(melee_recipe, player_model_path),
        "coverage": {
            "surfaces": melee_asset["surfaces"],
            "attachmentMarkers": [],
            "materialTextures": len(melee_textures),
        },
    }

    audio_rows = []
    audio_root = staging / "generated" / "combat" / "audio"
    final_audio_root = output_root / "generated" / "combat" / "audio"
    for row in combat_recipe["audio"]:
        member = _verified_member(sound_archive, row)
        audio_id = hashlib.sha256(member.logical_path.encode()).hexdigest()[:20]
        output = audio_root / f"{audio_id}.wav"
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(member.data)
        audio_rows.append(
            {
                "id": audio_id,
                "role": row["role"],
                "logicalPath": member.logical_path,
                "sourceSha256": member.sha256,
                "wav": str((final_audio_root / output.name).resolve()),
                "wavSha256": file_sha256(output),
            }
        )
    if len({str(row["role"]) for row in audio_rows}) != len(audio_rows):
        raise ValueError("Fallout combat audio contains duplicate roles")

    combat_manifest = {
        "schema": "opennv-fo1-combat-presentation/v1",
        "unitsToMeters": combat_recipe["unitsToMeters"],
        "casing": {
            "asset": casing_asset,
            "textures": selected_textures(casing_asset),
            "adaptation": casing_recipe["adaptation"],
        },
        "audio": {
            "archiveSha256": recipe["source"]["soundArchive"]["sha256"],
            "events": audio_rows,
        },
    }
    return melee_weapon, combat_manifest, all_textures


def prepare(
    recipe_path: Path,
    fnv_data_root: Path,
    donor_scene_path: Path,
    output_root: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise ValueError(f"refusing to overwrite Fallout 3D presentation cache: {output_root}")
    recipe = _read_json(recipe_path)
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise ValueError(f"Unexpected Fallout 3D presentation recipe: {recipe_path}")
    source = recipe["source"]
    required = [
        source["master"],
        source["meshesArchive"],
        source["soundArchive"],
        *source["textureArchives"],
    ]
    owned_paths: dict[str, Path] = {}
    for expected in required:
        path = find_required_file(fnv_data_root, str(expected["file"]))
        if file_sha256(path) != str(expected["sha256"]):
            raise ValueError(f"Owned FNV file hash drift: {expected['file']}")
        owned_paths[str(expected["file"])] = path

    donor = _read_json(donor_scene_path)
    if donor["source"]["masterSha256"] != source["master"]["sha256"]:
        raise ValueError("Donor cave master identity drift")
    cave_assets, cave_textures = _select_cave_kit(donor, recipe)

    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=output_root.name + ".", dir=output_root.parent))
    try:
        meshes = BsaArchive(owned_paths[str(source["meshesArchive"]["file"])])
        sounds = BsaArchive(owned_paths[str(source["soundArchive"]["file"])])
        texture_archives = [
            BsaArchive(owned_paths[str(row["file"])]) for row in source["textureArchives"]
        ]
        direct_cave_assets, cave_textures = _prepare_direct_cave_assets(
            meshes,
            [owned_paths[str(row["file"])] for row in source["textureArchives"]],
            recipe["caveKit"],
            cave_textures,
            staging,
            output_root,
        )
        cave_assets.extend(direct_cave_assets)
        creature = recipe["creature"]
        skeleton = _verified_member(meshes, creature["skeleton"])
        model = _verified_member(meshes, creature["model"])
        clips = [_verified_member(meshes, row) for row in creature["animations"]]
        generated = staging / "generated" / "creatures" / "nvgiantrat"
        model_path = generated / "nvgiantrat.gltf"
        sidecar_path = generated / "nvgiantrat.opennv.json"
        sidecar = export_actor_gltf(
            ActorGltfInput(
                actor_form_id=str(creature["formId"]),
                actor_name=str(creature["editorId"]),
                skeleton_path=skeleton.logical_path,
                skeleton_payload=skeleton.data,
                symmetric_geometry=(),
                asymmetric_geometry=(),
                components=(
                    ActorComponent(
                        role="body",
                        model_path=model.logical_path,
                        model_payload=model.data,
                    ),
                ),
                idle_animation_path=clips[0].logical_path,
                idle_animation_payload=clips[0].data,
                additional_animations=tuple(
                    ActorAnimation(clip.logical_path, clip.data) for clip in clips[1:]
                ),
            ),
            texture_archives,
            model_path,
            sidecar_path,
        )
        expected = creature["expected"]
        actual = sidecar["coverage"]
        for field in ("surfaces", "skins", "animations"):
            if int(actual[field]) != int(expected[field]):
                raise ValueError(f"Giant-rat {field} drift: expected={expected[field]} actual={actual[field]}")
        if int(sidecar["skeleton"]["nodes"]) != int(expected["skeletonNodes"]):
            raise ValueError("Giant-rat skeleton node count drift")

        player_recipe = recipe["player"]
        player_scene = prepare_actor(
            fnv_data_root,
            staging,
            str(player_recipe["actorRecipe"]),
        )
        player_expected = player_recipe["expected"]
        if (
            player_scene["reference"]["baseFormId"] != player_expected["sourceActorBaseFormId"]
            or player_scene["actor"]["outfitFormId"] != player_expected["outfitFormId"]
            or bool(player_scene["actor"]["female"]) != bool(player_expected["female"])
        ):
            raise ValueError("Vault Dweller owned actor identity drift")
        for field in ("components", "surfaces", "skins", "animations", "textures"):
            if int(player_scene["coverage"][field]) != int(player_expected[field]):
                raise ValueError(
                    f"Vault Dweller {field} drift: "
                    f"expected={player_expected[field]} actual={player_scene['coverage'][field]}"
                )
        player_staging = (
            staging
            / "generated"
            / "actors"
            / str(player_recipe["actorRecipe"])
        )
        player_sidecar_path = player_staging / str(player_scene["outputs"]["sidecar"])
        player_sidecar = _read_json(player_sidecar_path)
        if (
            file_sha256(player_staging / str(player_scene["outputs"]["gltf"]))
            != player_scene["outputs"]["gltfSha256"]
            or file_sha256(player_sidecar_path)
            != player_scene["outputs"]["sidecarSha256"]
        ):
            raise ValueError("Vault Dweller generated actor hash drift")
        player_weapon = _select_player_weapon(
            donor,
            recipe,
            player_staging / str(player_scene["outputs"]["gltf"]),
        )
        player_melee_weapon, combat_presentation, cave_textures = (
            _prepare_combat_presentation(
                meshes,
                sounds,
                [owned_paths[str(row["file"])] for row in source["textureArchives"]],
                recipe,
                player_staging / str(player_scene["outputs"]["gltf"]),
                cave_textures,
                staging,
                output_root,
            )
        )

        final_generated = output_root / "generated" / "creatures" / "nvgiantrat"
        final_player = (
            output_root
            / "generated"
            / "actors"
            / str(player_recipe["actorRecipe"])
        )
        manifest = {
            "schema": MANIFEST_SCHEMA,
            "status": "transported-owned-presentation",
            "recipe": {"id": recipe["id"], "sha256": file_sha256(recipe_path)},
            "ownedSource": {
                "masterSha256": source["master"]["sha256"],
                "meshesArchiveSha256": source["meshesArchive"]["sha256"],
                "soundArchiveSha256": source["soundArchive"]["sha256"],
                "textureArchiveSha256": [row["sha256"] for row in source["textureArchives"]],
            },
            "creature": {
                "role": creature["role"],
                "formId": creature["formId"],
                "editorId": creature["editorId"],
                "unitsToMeters": creature["unitsToMeters"],
                "model": str((final_generated / model_path.name).resolve()),
                "sidecar": str((final_generated / sidecar_path.name).resolve()),
                "modelSha256": sidecar["outputs"]["gltf"]["sha256"],
                "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
                "animations": [
                    {**animation, "role": source_row["role"]}
                    for animation, source_row in zip(sidecar["animations"], creature["animations"])
                ],
                "coverage": sidecar["coverage"],
                "skeleton": sidecar["skeleton"],
            },
            "player": {
                "role": player_recipe["role"],
                "displayName": player_recipe["displayName"],
                "actorRecipe": player_recipe["actorRecipe"],
                "unitsToMeters": player_recipe["unitsToMeters"],
                "model": str(
                    (final_player / str(player_scene["outputs"]["gltf"])).resolve()
                ),
                "sidecar": str(
                    (final_player / str(player_scene["outputs"]["sidecar"])).resolve()
                ),
                "modelSha256": player_scene["outputs"]["gltfSha256"],
                "sidecarSha256": player_scene["outputs"]["sidecarSha256"],
                "bufferSha256": player_scene["outputs"]["bufferSha256"],
                "sourceActor": {
                    "baseFormId": player_scene["reference"]["baseFormId"],
                    "editorId": player_scene["actor"]["editorId"],
                    "retailName": player_scene["actor"]["name"],
                    "female": player_scene["actor"]["female"],
                },
                "outfit": {
                    "formId": player_scene["actor"]["outfitFormId"],
                    "identity": "owned Classic Pack Vault 13 armored-jumpsuit model",
                },
                "coverage": player_scene["coverage"],
                "skeleton": player_sidecar["skeleton"],
                "animations": player_sidecar["animations"],
                "thirdPersonWeapon": player_weapon,
                "thirdPersonMeleeWeapon": player_melee_weapon,
            },
            "combatPresentation": combat_presentation,
            "caveKit": {
                "donorScene": str(donor_scene_path.resolve()),
                "donorSceneSha256": file_sha256(donor_scene_path),
                "unitsToMeters": recipe["caveKit"]["unitsToMeters"],
                "assets": cave_assets,
                "textures": cave_textures,
            },
            "composition": recipe["composition"],
            "supported": recipe["supported"],
            "unsupported": recipe["unsupported"],
            "retailOrDerivedAssetsPackaged": False,
        }
        manifest_path = staging / "fo1-3d-presentation.json"
        _write_json(manifest_path, manifest)
        os.replace(staging, output_root)
        return {**manifest, "manifest": str((output_root / manifest_path.name).resolve())}
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--fnv-data-root", type=Path, required=True)
    parser.add_argument("--donor-scene", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = prepare(
            args.recipe.resolve(),
            args.fnv_data_root.resolve(),
            args.donor_scene.resolve(),
            args.output_root.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_FO1_3D_PRESENTATION_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO1_3D_PRESENTATION "
        + json.dumps(
            {
                "manifest": result["manifest"],
                "ratAnimations": result["creature"]["coverage"]["animations"],
                "playerSurfaces": result["player"]["coverage"]["surfaces"],
                "caveAssets": len(result["caveKit"]["assets"]),
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
