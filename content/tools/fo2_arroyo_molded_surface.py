"""Bake disposable continuous rock surfaces from exact owned Map 3 FRMs."""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path
from typing import Any

from PIL import Image, ImageEnhance, ImageFilter, ImageOps, ImageStat

from corpus_io import atomic_json
from fo2_frm_relief import RELIEF_MODE, RELIEF_SCHEMA, derive_relief
from fo1_profile import Fo1ProfileError
from plugin_stack import file_sha256


RECIPE_SCHEMA = "opennv-fo2-arroyo-molded-surface-recipe/v2"
SURFACE_SCHEMA = "opennv-fo2-arroyo-molded-surface-cache/v2"
SURFACE_STATUS = "source-wall-frm-derived-disposable-local-surface"
SURFACE_MODE = "source-frm-albedo-normal-overlap-tile-bake-v2"
BYTE_CHANNEL_MAXIMUM = 255
NORMAL_CHANNEL_NEUTRAL = 128
NORMAL_REMAP_HALF = 0.5
DIGEST_COORDINATE_BYTES = 4
FLOOR_ID_MASK = 0x0FFF
MISC_OBJECT_TYPE = 5
NORMAL_MAP_MODE = "source-luminance-periodic-height-gradient-v1"


def _load_recipe(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict) or value.get("schema") != RECIPE_SCHEMA:
        raise Fo1ProfileError(f"Fallout 2 molded-surface recipe is invalid: {path}")
    return value


def _wall_objects(source_manifest: dict[str, Any], elevation: int, object_type: int) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    elevations = source_manifest.get("map", {}).get("objects", {}).get("elevations", [])
    for elevation_row in elevations:
        if int(elevation_row.get("elevation", -1)) != elevation:
            continue
        for value in elevation_row.get("objects", []):
            prototype = value.get("prototype", {})
            if int(prototype.get("object_type", -1)) == object_type:
                rows.append(value)
    return sorted(rows, key=lambda row: int(row["serial"]))


def _source_average(images: list[Image.Image]) -> tuple[int, int, int, int]:
    totals = [0.0, 0.0, 0.0]
    weight = 0.0
    for image in images:
        rgba = image.convert("RGBA")
        alpha = rgba.getchannel("A")
        if alpha.getbbox() is None:
            continue
        statistics = ImageStat.Stat(rgba.convert("RGB"), alpha)
        image_weight = float(sum(alpha.getdata()))
        for channel in range(3):
            totals[channel] += statistics.mean[channel] * image_weight
        weight += image_weight
    if weight <= 0.0:
        raise Fo1ProfileError("Fallout 2 wall FRM artifacts contain no admitted opaque pixels")
    return tuple(int(round(channel / weight)) for channel in totals) + (
        BYTE_CHANNEL_MAXIMUM,
    )


def _patch(image: Image.Image, size: int, opacity: float) -> Image.Image:
    rgba = image.convert("RGBA")
    bounds = rgba.getchannel("A").getbbox()
    if bounds is None:
        raise Fo1ProfileError("Fallout 2 wall FRM artifact has no opaque bounds")
    admitted = rgba.crop(bounds)
    admitted = ImageOps.fit(admitted, (size, size), method=Image.Resampling.LANCZOS)
    result = Image.new("RGBA", (size, size), _source_average([admitted]))
    result.alpha_composite(admitted)
    result.putalpha(round(BYTE_CHANNEL_MAXIMUM * opacity))
    return result


def _compose_surface(
    staging: Path,
    artifacts: list[dict[str, Any]],
    *,
    expected_opaque_artifacts: int,
    texture_size: int,
    patch_size: int,
    patch_layers: int,
    opacity: float,
    blur_radius: float,
    saturation: float,
    contrast: float,
) -> Image.Image:
    images = [Image.open(staging / row["png"]).convert("RGBA") for row in artifacts]
    opaque_rows = [
        (artifact, image)
        for artifact, image in zip(artifacts, images, strict=True)
        if image.getchannel("A").getbbox() is not None
    ]
    if len(opaque_rows) != expected_opaque_artifacts:
        raise Fo1ProfileError("Fallout 2 Arroyo opaque source FRM coverage drifted")
    working_size = texture_size * 3
    working = Image.new("RGBA", (working_size, working_size), _source_average(images))
    for artifact, image in opaque_rows:
        patch = _patch(image, patch_size, opacity)
        for layer in range(patch_layers):
            identity = f"{artifact['id']}|{layer}".encode("ascii")
            digest = bytes.fromhex(hashlib.sha256(identity).hexdigest())
            x = int.from_bytes(digest[:DIGEST_COORDINATE_BYTES], "big") % texture_size
            y = int.from_bytes(
                digest[DIGEST_COORDINATE_BYTES : DIGEST_COORDINATE_BYTES * 2],
                "big",
            ) % texture_size
            x -= patch_size // 2
            y -= patch_size // 2
            for offset_y in (-1, 0, 1):
                for offset_x in (-1, 0, 1):
                    working.alpha_composite(
                        patch,
                        (
                            texture_size + x + offset_x * texture_size,
                            texture_size + y + offset_y * texture_size,
                        ),
                    )
    base = working.crop(
        (texture_size, texture_size, texture_size * 2, texture_size * 2)
    )
    base_rgb = base.convert("RGB").filter(ImageFilter.GaussianBlur(blur_radius))
    base_rgb = ImageEnhance.Color(base_rgb).enhance(saturation)
    base_rgb = ImageEnhance.Contrast(base_rgb).enhance(contrast)
    return base_rgb


def _derive_periodic_normal_map(
    source: Image.Image,
    *,
    blur_radius: float,
    sample_radius: int,
    strength: float,
) -> Image.Image:
    """Derive a seamless tangent-space normal solely from the owned FRM albedo bake."""

    height = ImageOps.grayscale(source).filter(ImageFilter.GaussianBlur(blur_radius))
    pixels = height.load()
    output = Image.new(
        "RGB",
        height.size,
        (NORMAL_CHANNEL_NEUTRAL, NORMAL_CHANNEL_NEUTRAL, BYTE_CHANNEL_MAXIMUM),
    )
    normals = output.load()
    for y in range(height.height):
        previous_y = (y - sample_radius) % height.height
        next_y = (y + sample_radius) % height.height
        for x in range(height.width):
            previous_x = (x - sample_radius) % height.width
            next_x = (x + sample_radius) % height.width
            dx = (
                (pixels[next_x, y] - pixels[previous_x, y])
                / BYTE_CHANNEL_MAXIMUM
                * strength
            )
            dy = (
                (pixels[x, next_y] - pixels[x, previous_y])
                / BYTE_CHANNEL_MAXIMUM
                * strength
            )
            nx, ny, nz = -dx, dy, 1.0
            length = math.sqrt(nx * nx + ny * ny + nz * nz)
            normals[x, y] = tuple(
                round(
                    (channel / length * NORMAL_REMAP_HALF + NORMAL_REMAP_HALF)
                    * BYTE_CHANNEL_MAXIMUM
                )
                for channel in (nx, ny, nz)
            )
    return output


def _source_rows(artifacts: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [
        {
            "artifactId": row["id"],
            "logicalPath": row["logicalPath"],
            "sourceSha256": row["sourceSha256"],
            "pngSha256": row["pngSha256"],
        }
        for row in artifacts
    ]


def _elevation_objects(
    source_manifest: dict[str, Any],
    elevation: int,
) -> list[dict[str, Any]]:
    return next(
        row["objects"]
        for row in source_manifest["map"]["objects"]["elevations"]
        if int(row["elevation"]) == elevation
    )


def enrich_arroyo_cache_with_molded_surface(
    staging: Path,
    cache: dict[str, Any],
    source_manifest: dict[str, Any],
    recipe_path: Path,
) -> None:
    """Add a derived-owned surface and complete provenance while cache is still staged."""

    recipe_path = recipe_path.resolve()
    recipe = _load_recipe(recipe_path)
    selection = recipe.get("selection", {})
    output = recipe.get("output", {})
    map_contract = recipe.get("map", {})
    map_source = source_manifest.get("map", {})
    elevation = int(selection.get("elevation", -1))
    object_type = int(selection.get("prototypeObjectType", -1))
    expected_walls = int(selection.get("expectedWallObjects", -1))
    expected_artifacts = int(selection.get("expectedUniqueWallArtifacts", -1))
    expected_opaque_artifacts = int(selection.get("expectedOpaqueWallArtifacts", -1))
    wall_roles = selection.get("wallRoleMapping", {})
    stone_post_paths = {
        str(value).casefold() for value in wall_roles.get("stonePostLogicalPaths", [])
    }
    default_floor_tile_id = int(selection.get("defaultFloorTileId", -1))
    expected_floor_patches = int(selection.get("expectedNonDefaultFloorPatches", -1))
    expected_floor_artifacts = int(selection.get("expectedUniqueFloorArtifacts", -1))
    expected_relief_objects = int(selection.get("expectedReliefObjects", -1))
    expected_relief_artifacts = int(selection.get("expectedReliefArtifacts", -1))
    expected_relief_torches = int(selection.get("expectedReliefTorchObjects", -1))
    relief_recipe = recipe.get("objectRelief3d", {})
    texture_size = int(output.get("textureSizePixels", 0))
    patch_size = int(output.get("patchSizePixels", 0))
    patch_layers = int(output.get("patchLayers", 0))
    opacity = float(output.get("overlapOpacity", 0.0))
    blur_radius = float(output.get("blurRadiusPixels", -1.0))
    saturation = float(output.get("saturation", 0.0))
    contrast = float(output.get("contrast", 0.0))
    normal_map = output.get("normalMap", {})
    normal_blur_radius = float(normal_map.get("blurRadiusPixels", -1.0))
    normal_sample_radius = int(normal_map.get("sampleRadiusPixels", 0))
    normal_strength = float(normal_map.get("strength", 0.0))
    if (
        not isinstance(recipe.get("id"), str)
        or not recipe["id"]
        or recipe.get("campaign") != "Fallout2"
        or map_contract.get("index") != 3
        or map_contract.get("name") != "ARCAVES.MAP"
        or map_contract.get("logicalPath") != "maps\\arcaves.map"
        or map_source.get("logicalPath") != map_contract.get("logicalPath")
        or map_source.get("header", {}).get("mapIndex") != map_contract.get("index")
        or map_source.get("header", {}).get("name") != map_contract.get("name")
        or selection.get("topLevelOnly") is not True
        or wall_roles.get("mode") != "source-component-and-frm-identity-role-map-v1"
        or not stone_post_paths
        or output.get("mode") != SURFACE_MODE
        or relief_recipe.get("schema") != RELIEF_SCHEMA
        or relief_recipe.get("mode") != RELIEF_MODE
        or texture_size < 2
        or texture_size & (texture_size - 1)
        or patch_size <= 0
        or patch_size > texture_size // 2
        or patch_layers <= 0
        or not 0.0 < opacity <= 1.0
        or blur_radius < 0.0
        or saturation <= 0.0
        or contrast <= 0.0
        or normal_map.get("mode") != NORMAL_MAP_MODE
        or normal_blur_radius < 0.0
        or normal_sample_radius <= 0
        or normal_sample_radius >= texture_size // 2
        or normal_strength <= 0.0
    ):
        raise Fo1ProfileError("Fallout 2 Arroyo molded-surface recipe contract drifted")

    walls = _wall_objects(source_manifest, elevation, object_type)
    if len(walls) != expected_walls:
        raise Fo1ProfileError("Fallout 2 Arroyo wall-object source coverage drifted")
    wall_serials = {int(row["serial"]) for row in walls}
    stone_posts = [
        row
        for row in walls
        if f"art\\walls\\{row['artFilename']}".casefold() in stone_post_paths
    ]
    if (
        len(stone_posts) != int(wall_roles.get("expectedStonePostObjects", -1))
        or len(walls) - len(stone_posts)
        != int(wall_roles.get("expectedCaveShellObjects", -1))
    ):
        raise Fo1ProfileError("Fallout 2 Arroyo wall-role FRM mapping drifted")
    artifacts_by_id = {row["id"]: row for row in cache.get("artifacts", [])}
    selected_ids: set[str] = set()
    bound_serials: set[int] = set()
    for binding in cache.get("objectBindings", []):
        selected = {
            int(row["serial"])
            for row in binding.get("placements", [])
            if int(row["serial"]) in wall_serials
        }
        if selected:
            selected_ids.add(str(binding["artifactId"]))
            bound_serials.update(selected)
    if bound_serials != wall_serials or len(selected_ids) != expected_artifacts:
        raise Fo1ProfileError("Fallout 2 Arroyo wall FRM artifact admission drifted")

    selected_artifacts = [artifacts_by_id[artifact_id] for artifact_id in sorted(selected_ids)]
    wall_surface = _compose_surface(
        staging,
        selected_artifacts,
        expected_opaque_artifacts=expected_opaque_artifacts,
        texture_size=texture_size,
        patch_size=patch_size,
        patch_layers=patch_layers,
        opacity=opacity,
        blur_radius=blur_radius,
        saturation=saturation,
        contrast=contrast,
    )

    entries = next(
        row["rawEntries"]
        for row in map_source["layout"]["elevations"]
        if int(row["elevation"]) == elevation
    )
    floor_ids = [int(value) & FLOOR_ID_MASK for value in entries]
    non_default_floor_ids = sorted(
        {value for value in floor_ids if value != default_floor_tile_id}
    )
    tile_bindings = {int(row["id"]): row["artifactId"] for row in cache["tileBindings"]}
    floor_artifacts = [
        artifacts_by_id[tile_bindings[tile_id]] for tile_id in non_default_floor_ids
    ]
    if (
        sum(value != default_floor_tile_id for value in floor_ids) != expected_floor_patches
        or len(floor_artifacts) != expected_floor_artifacts
        or len({row["id"] for row in floor_artifacts}) != expected_floor_artifacts
    ):
        raise Fo1ProfileError("Fallout 2 Arroyo floor FRM artifact admission drifted")
    floor_surface = _compose_surface(
        staging,
        floor_artifacts,
        expected_opaque_artifacts=expected_floor_artifacts,
        texture_size=texture_size,
        patch_size=patch_size,
        patch_layers=patch_layers,
        opacity=opacity,
        blur_radius=blur_radius,
        saturation=saturation,
        contrast=contrast,
    )
    wall_normal = _derive_periodic_normal_map(
        wall_surface,
        blur_radius=normal_blur_radius,
        sample_radius=normal_sample_radius,
        strength=normal_strength,
    )
    floor_normal = _derive_periodic_normal_map(
        floor_surface,
        blur_radius=normal_blur_radius,
        sample_radius=normal_sample_radius,
        strength=normal_strength,
    )

    wall_relative = Path("assets") / "molded3d" / "map3-wall-detail.png"
    floor_relative = Path("assets") / "molded3d" / "map3-floor-detail.png"
    wall_normal_relative = Path("assets") / "molded3d" / "map3-wall-normal.png"
    floor_normal_relative = Path("assets") / "molded3d" / "map3-floor-normal.png"
    provenance_relative = wall_relative.with_suffix(".provenance.json")
    wall_path = staging / wall_relative
    floor_path = staging / floor_relative
    wall_normal_path = staging / wall_normal_relative
    floor_normal_path = staging / floor_normal_relative
    provenance_path = staging / provenance_relative
    wall_path.parent.mkdir(parents=True, exist_ok=True)
    wall_surface.save(wall_path, format="PNG", optimize=False)
    floor_surface.save(floor_path, format="PNG", optimize=False)
    wall_normal.save(wall_normal_path, format="PNG", optimize=False)
    floor_normal.save(floor_normal_path, format="PNG", optimize=False)
    wall_output = {
        "file": wall_relative.as_posix(),
        "width": wall_surface.width,
        "height": wall_surface.height,
        "bytes": wall_path.stat().st_size,
        "sha256": file_sha256(wall_path),
    }
    floor_output = {
        "file": floor_relative.as_posix(),
        "width": floor_surface.width,
        "height": floor_surface.height,
        "bytes": floor_path.stat().st_size,
        "sha256": file_sha256(floor_path),
    }
    wall_normal_output = {
        "file": wall_normal_relative.as_posix(),
        "width": wall_normal.width,
        "height": wall_normal.height,
        "bytes": wall_normal_path.stat().st_size,
        "sha256": file_sha256(wall_normal_path),
    }
    floor_normal_output = {
        "file": floor_normal_relative.as_posix(),
        "width": floor_normal.width,
        "height": floor_normal.height,
        "bytes": floor_normal_path.stat().st_size,
        "sha256": file_sha256(floor_normal_path),
    }
    provenance = {
        "schema": SURFACE_SCHEMA,
        "status": SURFACE_STATUS,
        "mode": SURFACE_MODE,
        "recipe": {
            "file": str(recipe_path),
            "schema": RECIPE_SCHEMA,
            "id": recipe["id"],
            "sha256": file_sha256(recipe_path),
        },
        "authority": {
            "mapSha256": map_source["sha256"],
            "elevation": elevation,
            "prototypeObjectType": object_type,
            "topLevelOnly": True,
            "wallObjects": len(walls),
            "uniqueWallArtifacts": len(selected_artifacts),
            "opaqueWallArtifacts": expected_opaque_artifacts,
            "wallRoleMode": wall_roles["mode"],
            "stonePostObjects": len(stone_posts),
            "caveShellObjects": len(walls) - len(stone_posts),
            "sourceSerialsSha256": hashlib.sha256(
                ("\n".join(str(value) for value in sorted(wall_serials)) + "\n").encode("ascii")
            ).hexdigest(),
            "nonDefaultFloorPatches": expected_floor_patches,
            "uniqueFloorArtifacts": len(floor_artifacts),
            "floorTileIds": non_default_floor_ids,
        },
        "sources": {
            "walls": _source_rows(selected_artifacts),
            "floors": _source_rows(floor_artifacts),
        },
        "normalDerivation": {
            "mode": NORMAL_MAP_MODE,
            "blurRadiusPixels": normal_blur_radius,
            "sampleRadiusPixels": normal_sample_radius,
            "strength": normal_strength,
            "periodic": True,
            "authority": "luminance gradients of the admitted owned FRM albedo bake",
        },
        "output": {
            "wall": wall_output,
            "floor": floor_output,
            "wallNormal": wall_normal_output,
            "floorNormal": floor_normal_output,
        },
        "generatedMesh": False,
        "distributionAllowed": False,
    }
    atomic_json(provenance_path, provenance)
    cache["molded3dSurface"] = {
        "schema": SURFACE_SCHEMA,
        "status": SURFACE_STATUS,
        "mode": SURFACE_MODE,
        "wallTexture": wall_output,
        "floorTexture": floor_output,
        "wallNormalTexture": wall_normal_output,
        "floorNormalTexture": floor_normal_output,
        "normalDerivation": provenance["normalDerivation"],
        "provenance": {
            "file": provenance_relative.as_posix(),
            "bytes": provenance_path.stat().st_size,
            "sha256": file_sha256(provenance_path),
        },
        "recipe": provenance["recipe"],
        "sourceWallObjects": len(walls),
        "sourceWallArtifacts": len(selected_artifacts),
        "sourceFloorPatches": expected_floor_patches,
        "sourceFloorArtifacts": len(floor_artifacts),
        "distributionAllowed": False,
    }
    cache["counts"]["molded3dSurfaceArtifacts"] = 4

    bindings_by_serial = {
        int(placement["serial"]): {
            "artifactId": binding["artifactId"],
            "logicalPath": binding["logicalPath"],
            **placement,
        }
        for binding in cache["objectBindings"]
        for placement in binding["placements"]
    }
    hidden_paths = {
        "art\\misc\\block.frm",
        "art\\scenery\\block.frm",
        "art\\walls\\block.frm",
        "art\\misc\\exitgrd7.frm",
    }
    relief_placements = []
    for source_object in _elevation_objects(source_manifest, elevation):
        serial = int(source_object["serial"])
        binding = bindings_by_serial[serial]
        logical_path = str(binding["logicalPath"]).casefold()
        source_object_type = int(source_object["prototype"]["object_type"])
        if logical_path in hidden_paths:
            continue
        if logical_path in {
            "art\\scenery\\atorch3.frm",
            "art\\scenery\\atorch4.frm",
            "art\\scenery\\atorch5.frm",
        }:
            role = "torch"
        elif source_object_type == object_type:
            role = "stonePost" if logical_path in stone_post_paths else "caveWall"
        else:
            role = {
                0: "item",
                1: "critter",
                2: "scenery",
                MISC_OBJECT_TYPE: "misc",
            }.get(source_object_type)
        if role is None:
            raise Fo1ProfileError(
                f"Fallout 2 relief object type is unsupported: {source_object_type}"
            )
        relief_placements.append(
            {
                "serial": serial,
                "tile": int(source_object["tile"]),
                "rotation": int(source_object["rotation"]),
                "frame": int(source_object["frame"]),
                "pixelOffset": list(source_object["pixelOffset"]),
                "fid": source_object["fid"],
                "pid": source_object["pid"],
                "objectType": source_object_type,
                "logicalPath": binding["logicalPath"],
                "artifactId": binding["artifactId"],
                "role": role,
                "depthMeters": float(relief_recipe["depthMetersByRole"][role]),
            }
        )
    relief_artifact_ids = sorted(
        {str(row["artifactId"]) for row in relief_placements}
    )
    if (
        len(relief_placements) != expected_relief_objects
        or len(relief_artifact_ids) != expected_relief_artifacts
        or sum("atorch" in str(row["logicalPath"]).casefold() for row in relief_placements)
        != expected_relief_torches
    ):
        raise Fo1ProfileError("Fallout 2 Arroyo relief-object coverage drifted")
    relief_artifacts = {
        artifact_id: {
            "artifactId": artifact_id,
            "logicalPath": artifacts_by_id[artifact_id]["logicalPath"],
            "sourceSha256": artifacts_by_id[artifact_id]["sourceSha256"],
            "png": artifacts_by_id[artifact_id]["png"],
            "pngSha256": artifacts_by_id[artifact_id]["pngSha256"],
            "width": artifacts_by_id[artifact_id]["width"],
            "height": artifacts_by_id[artifact_id]["height"],
            "frameOffset": artifacts_by_id[artifact_id]["frameOffset"],
            "relief": derive_relief(
                staging,
                artifacts_by_id[artifact_id],
                relief_recipe,
                output_folder="object-relief3d",
            ),
        }
        for artifact_id in relief_artifact_ids
    }
    relief_provenance = {
        "schema": "opennv-fo2-arroyo-object-relief-cache/v2",
        "status": "source-frm-alpha-derived-closed-relief",
        "mode": relief_recipe["mode"],
        "recipe": provenance["recipe"],
        "mapSha256": map_source["sha256"],
        "artifacts": relief_artifacts,
        "placements": relief_placements,
        "coverage": {
            "artifacts": len(relief_artifacts),
            "placements": len(relief_placements),
            "torchPlacements": expected_relief_torches,
        },
        "generatedMeshPackaged": False,
        "distributionAllowed": False,
    }
    relief_relative = (
        Path("assets") / "object-relief3d" / "map3-object-relief.provenance.json"
    )
    relief_path = staging / relief_relative
    atomic_json(relief_path, relief_provenance)
    cache["objectRelief3d"] = {
        **relief_provenance,
        "provenance": {
            "file": relief_relative.as_posix(),
            "bytes": relief_path.stat().st_size,
            "sha256": file_sha256(relief_path),
        },
    }
    cache["counts"]["objectRelief3dArtifacts"] = len(relief_artifacts)
    cache["counts"]["objectRelief3dPlacements"] = len(relief_placements)
