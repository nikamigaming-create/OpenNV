#!/usr/bin/env python3
"""Prepare an exact V13ENT hex/floor contract and local owned-art cache."""

from __future__ import annotations

import argparse
from collections import deque
import hashlib
import json
import math
import os
import re
import shutil
import struct
import tempfile
from pathlib import Path

from PIL import Image, ImageFilter

from fo1_frm import decode_frm, palette_rgba
from fo1_map_objects import Fo1ResourceResolver, OBJECT_TYPE_NAMES, TYPE_DIRECTORIES
from fo1_profile import Fo1ProfileError, parse_form_id, parse_map_layout, sha256_path
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT005 = 0.005
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT05 = 0.05
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT08 = 0.08
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT1 = 0.1
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT10 = 0.10
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT2 = 0.2
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT20 = 0.20
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT25 = 0.25
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT3 = 0.3
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT40 = 0.40
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT45 = 0.45
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5 = 0.5
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT50 = 0.50
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT6 = 0.6
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT75 = 0.75
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT8 = 0.8
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_00000010 = 0x00000010
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_00000800 = 0x00000800
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_07 = 0x07
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_0F = 0x0F
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_0FFF = 0x0FFF
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_19C = 0x19C
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_1A0 = 0x1A0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_20 = 0x20
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_30 = 0x30
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_39 = 0x39
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_79 = 0x79
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_BC = 0xBC
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_FF = 0xFF
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT0ENEGATIVE8 = 1.0e-8
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT0ENEGATIVE9 = 1.0e-9
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT2 = 1.2
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT5 = 1.5
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT6 = 1.6
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_10 = 10
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_10POINT0 = 10.0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_100 = 100
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_10000 = 10000
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_1024 = 1024
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_11 = 11
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_12 = 12
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_12POINT0 = 12.0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_122 = 122
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_128 = 128
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_13 = 13
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_14POINT0 = 14.0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_15 = 15
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_16 = 16
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_16POINT0 = 16.0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_2POINT5 = 2.5
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_20 = 20
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200 = 200
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_24 = 24
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_25POINT0 = 25.0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_255 = 255
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_255POINT0 = 255.0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_28 = 28
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_32 = 32
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_35 = 35
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_360 = 360
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_4POINT8 = 4.8
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_40000 = 40000
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_4752 = 4752
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_48 = 48
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_4816 = 4816
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_5 = 5
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_5POINT0 = 5.0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_6 = 6
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_60POINT0 = 60.0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_64 = 64
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_7 = 7
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_8 = 8
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_8POINT0 = 8.0
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_81 = 81
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_9 = 9
PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_99 = 99



RECIPE_SCHEMA = "opennv-fo1-hex-recipe/v1"
RUNTIME_PROFILE_RECIPE_SCHEMA = "opennv-fo1-runtime-profile-recipe/v1"
SCENE_SCHEMA = "opennv-fo1-hex-scene/v1"
CACHE_SCHEMA = "opennv-fo1-hex-cache/v1"


def read_json(path: Path) -> dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def load_runtime_profile_recipe(
    scene_recipe_path: Path,
    reference: object,
) -> dict[str, object]:
    if not isinstance(reference, dict) or set(reference) != {"path", "sha256"}:
        raise Fo1ProfileError("Fallout hex recipe runtime-profile reference is invalid")
    recipes_root = scene_recipe_path.parent.resolve()
    profile_path = (recipes_root / str(reference["path"])).resolve()
    if not profile_path.is_relative_to(recipes_root):
        raise Fo1ProfileError("Fallout runtime-profile recipe escapes the recipes root")
    actual_sha256 = sha256_path(profile_path)
    if actual_sha256 != str(reference["sha256"]).lower():
        raise Fo1ProfileError("Fallout runtime-profile recipe hash drift")
    profile = read_json(profile_path)
    required_sections = {
        "authority",
        "generationAdaptation",
        "scenePresentation",
        "camera",
        "gameplayAdaptation",
        "combatPresentation",
        "mobPresentation",
        "cutaway",
        "showcase",
    }
    if (
        profile.get("schema") != RUNTIME_PROFILE_RECIPE_SCHEMA
        or not isinstance(profile.get("id"), str)
        or not required_sections.issubset(profile)
        or any(not isinstance(profile[section], dict) for section in required_sections)
    ):
        raise Fo1ProfileError("Fallout runtime-profile recipe is incomplete")
    return {**profile, "recipeSha256": actual_sha256}


def hex_center(tile: int) -> list[float]:
    if not 0 <= tile < PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_40000:
        raise ValueError(f"Fallout hex tile is outside the 200x200 grid: {tile}")
    x = tile % PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200
    y = tile // PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200
    # Fallout's retail _dir_tile table branches on tile-column parity, not
    # row parity.  This is an even-column offset flat-top grid.
    return [x * (math.sqrt(3.0) / 2.0), 0.0, y - PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5 * (x & 1)]


def hex_neighbors(tile: int) -> tuple[int, ...]:
    if not 0 <= tile < PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_40000:
        raise ValueError(f"Fallout hex tile is outside the 200x200 grid: {tile}")
    x = tile % PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200
    y = tile // PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200
    odd = x & 1
    offsets = (
        (-1, -1 if odd else 0),
        (-1, 0 if odd else 1),
        (0, 1),
        (1, 0 if odd else 1),
        (1, -1 if odd else 0),
        (0, -1),
    )
    values = []
    for delta_x, delta_y in offsets:
        target_x = x + delta_x
        target_y = y + delta_y
        if 0 <= target_x < PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200 and 0 <= target_y < PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200:
            values.append(target_y * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200 + target_x)
    return tuple(values)


def wall_volume_components(
    placements: list[dict[str, object]],
) -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    for profile in sorted({str(row["profile"]) for row in placements}):
        rows = [row for row in placements if str(row["profile"]) == profile]
        by_tile = {int(row["tile"]): row for row in rows}
        if len(by_tile) != len(rows):
            raise Fo1ProfileError(
                f"connected wall-volume profile has duplicate source tiles: {profile}"
            )
        visited: set[int] = set()
        components: list[list[dict[str, object]]] = []
        for start in sorted(by_tile):
            if start in visited:
                continue
            visited.add(start)
            queue = deque([start])
            component: list[dict[str, object]] = []
            while queue:
                tile = queue.popleft()
                component.append(by_tile[tile])
                for neighbor in hex_neighbors(tile):
                    if neighbor in by_tile and neighbor not in visited:
                        visited.add(neighbor)
                        queue.append(neighbor)
            components.append(component)
        components.sort(key=lambda rows: min(int(row["serial"]) for row in rows))
        for index, component in enumerate(components):
            result.append(
                {
                    "id": f"{profile}-wall-component-{index:03d}",
                    "profile": profile,
                    "serials": sorted(int(row["serial"]) for row in component),
                    "tiles": sorted(int(row["tile"]) for row in component),
                }
            )
    return result


def _distance_xz(first: list[float], second: list[float]) -> float:
    return math.hypot(first[0] - second[0], first[2] - second[2])


def _wall_orientation(
    anchor: dict[str, object],
    walls: list[dict[str, object]],
    neighborhood_meters: float,
) -> tuple[float, float]:
    center = hex_center(int(anchor["tile"]))
    nearby = [
        hex_center(int(row["tile"]))
        for row in walls
        if _distance_xz(center, hex_center(int(row["tile"]))) <= neighborhood_meters
    ]
    if len(nearby) < 2:
        return -float(anchor["rotation"]) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_60POINT0, 1.0
    mean_x = sum(point[0] for point in nearby) / len(nearby)
    mean_z = sum(point[2] for point in nearby) / len(nearby)
    xx = sum((point[0] - mean_x) ** 2 for point in nearby)
    xz = sum((point[0] - mean_x) * (point[2] - mean_z) for point in nearby)
    zz = sum((point[2] - mean_z) ** 2 for point in nearby)
    total = xx + zz
    if total <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT0ENEGATIVE8:
        return -float(anchor["rotation"]) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_60POINT0, 1.0
    separation = math.sqrt((xx - zz) ** 2 + 4.0 * xz * xz)
    anisotropy = separation / total
    yaw = math.degrees(PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5 * math.atan2(2.0 * xz, xx - zz))
    return yaw, anisotropy


def _column_silhouette_contours(
    image: Image.Image,
    stride: int,
    minimum_run_width: int,
) -> list[list[list[int]]]:
    """Build deterministic low-noise side contours without changing the front mask.

    The exact FRM alpha remains the canonical face.  These contours only create
    the otherwise unknowable presentation depth seen after the camera rotates.
    """
    alpha = image.convert("RGBA").getchannel("A")
    pixels = alpha.load()
    active = [any(pixels[x, y] > 0 for y in range(alpha.height)) for x in range(alpha.width)]
    runs: list[tuple[int, int]] = []
    start = None
    for x, present in enumerate(active + [False]):
        if present and start is None:
            start = x
        elif not present and start is not None:
            if x - start >= minimum_run_width:
                runs.append((start, x - 1))
            start = None

    contours: list[list[list[int]]] = []
    for left, right in runs:
        samples = list(range(left, right + 1, stride))
        if samples[-1] != right:
            samples.append(right)
        top: list[list[int]] = []
        bottom: list[list[int]] = []
        for x in samples:
            opaque = [y for y in range(alpha.height) if pixels[x, y] > 0]
            if not opaque:
                continue
            top.append([x, min(opaque)])
            bottom.append([x, max(opaque) + 1])
        contour = top + list(reversed(bottom))
        deduplicated: list[list[int]] = []
        for point in contour:
            if not deduplicated or point != deduplicated[-1]:
                deduplicated.append(point)
        if len(deduplicated) >= 4:
            contours.append(deduplicated)
    return contours


def _relief_normal_map(
    image: Image.Image,
    blur_radius: float,
    luma_weight: float,
    silhouette_weight: float,
    strength: float,
) -> Image.Image:
    rgba = image.convert("RGBA")
    luma = rgba.convert("L")
    alpha = rgba.getchannel("A")
    silhouette = alpha.filter(ImageFilter.GaussianBlur(radius=blur_radius))
    luma_pixels = luma.load()
    silhouette_pixels = silhouette.load()
    alpha_pixels = alpha.load()
    height = [
        [
            (
                luma_pixels[x, y] / PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_255POINT0 * luma_weight
                + silhouette_pixels[x, y] / PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_255POINT0 * silhouette_weight
            )
            if alpha_pixels[x, y] > 0
            else 0.0
            for x in range(rgba.width)
        ]
        for y in range(rgba.height)
    ]
    normal = Image.new("RGBA", rgba.size, (PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_128, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_128, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_255, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_255))
    output = normal.load()
    for y in range(rgba.height):
        previous_y = max(0, y - 1)
        next_y = min(rgba.height - 1, y + 1)
        for x in range(rgba.width):
            previous_x = max(0, x - 1)
            next_x = min(rgba.width - 1, x + 1)
            dx = (height[y][next_x] - height[y][previous_x]) * strength
            dy = (height[next_y][x] - height[previous_y][x]) * strength
            nx, ny, nz = -dx, dy, 1.0
            length = math.sqrt(nx * nx + ny * ny + nz * nz)
            output[x, y] = (
                round((nx / length * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5 + PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_255POINT0),
                round((ny / length * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5 + PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_255POINT0),
                round((nz / length * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5 + PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_255POINT0),
                PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_255,
            )
    return normal


def build_owned_cave_composition(
    obstacles: list[dict[str, object]],
    sprite_placements: list[dict[str, object]],
    sprite_artifacts: dict[str, dict[str, object]],
    door: dict[str, object],
    entry: dict[str, object],
    presentation_manifest: dict[str, object],
    generation: dict[str, object],
    staging: Path,
    output_root: Path,
    pixels_per_meter: float,
) -> dict[str, object]:
    recipe = presentation_manifest.get("composition")
    if not isinstance(recipe, dict) or recipe.get("schema") != (
        "opennv-fo1-owned-cave-composition-recipe/v1"
    ):
        raise Fo1ProfileError("unexpected owned cave composition recipe")
    cave_kit = presentation_manifest["caveKit"]
    assets_by_role = {str(row["role"]): row for row in cave_kit["assets"]}
    required_roles = {
        "wall",
        "corner",
        "room",
        "large-rock",
        "small-rock",
        "stalagmite",
        "vault-transition",
        "vault-frame",
        "vault-airlock",
        "vault-hall",
        "vault-hall-cap",
        "entrance-corpse",
    }
    if not required_roles.issubset(assets_by_role):
        raise Fo1ProfileError("owned cave kit is missing a composition role")
    scales = recipe.get("roleScale")
    if not isinstance(scales, dict) or not required_roles.issubset(scales):
        raise Fo1ProfileError("owned cave composition scale contract is incomplete")

    floor = recipe.get("floor")
    if not isinstance(floor, dict) or floor.get("schema") != (
        "opennv-fo1-owned-continuous-floor/v1"
    ):
        raise Fo1ProfileError("owned cave continuous-floor contract is missing")
    texture_paths = {
        str(row["requestedPath"]).replace("/", "\\").lower()
        for row in cave_kit.get("textures", [])
    }
    for field in ("diffusePath", "normalPath"):
        requested = str(floor.get(field, "")).replace("/", "\\").lower()
        if requested not in texture_paths:
            raise Fo1ProfileError(f"owned cave floor texture is missing: {requested}")
    if (
        float(floor.get("heightMeters", 1.0)) > 0.0
        or float(floor.get("heightMeters", -1.0)) < -PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT10
        or float(floor.get("textureRepeatMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5
        or len(floor.get("albedoColor", [])) != 4
        or not 0.0 <= float(floor.get("roughness", -1.0)) <= 1.0
        or not 0.0 <= float(floor.get("normalScale", -1.0)) <= 2.0
    ):
        raise Fo1ProfileError("owned cave continuous-floor material contract is invalid")

    grounding = recipe.get("grounding")
    grounding_roles = {"large-rock", "small-rock", "stalagmite"}
    if (
        not isinstance(grounding, dict)
        or grounding.get("schema") != "opennv-fo1-owned-cave-grounding/v1"
        or not 0.0 < float(grounding.get("maximumRuntimeErrorMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT005
        or set(grounding.get("roles", {})) != grounding_roles
    ):
        raise Fo1ProfileError("owned cave grounding contract is missing or invalid")
    for role, values in grounding["roles"].items():
        if (
            not isinstance(values, dict)
            or not 0.0 < float(values.get("seatDepthHeightFraction", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT40
            or not 0.0 < float(values.get("minimumSeatDepthMeters", 0.0))
            or float(values.get("minimumSeatDepthMeters", 0.0))
            > float(values.get("maximumSeatDepthMeters", 0.0))
            or float(values.get("maximumSeatDepthMeters", 0.0)) > PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT50
        ):
            raise Fo1ProfileError(f"owned cave grounding role is invalid: {role}")

    portal_recipe = recipe.get("vaultPortal")
    if not isinstance(portal_recipe, dict) or portal_recipe.get("schema") != (
        "opennv-fo1-owned-vault-portal/v1"
    ):
        raise Fo1ProfileError("owned cave Vault portal contract is missing")
    for field in ("diffusePath", "normalPath"):
        requested = str(portal_recipe.get(field, "")).replace("/", "\\").lower()
        if requested not in texture_paths:
            raise Fo1ProfileError(f"owned cave Vault portal texture is missing: {requested}")
    if (
        not 0.0 <= float(portal_recipe.get("behindDoorMeters", -1.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT5
        or not 0.0 <= float(portal_recipe.get("frontReliefMeters", -1.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT8
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5 <= float(portal_recipe.get("depthMeters", 0.0)) <= 4.0
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT2 <= float(portal_recipe.get("innerRadiusMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_2POINT5
        or not 4.0 <= float(portal_recipe.get("outerHalfWidthMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_14POINT0
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_4POINT8 <= float(portal_recipe.get("outerTopHeightMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_10POINT0
        or not -PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT3 <= float(portal_recipe.get("outerBottomHeightMeters", 1.0)) <= -PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT05
        or float(portal_recipe.get("outerTopHeightMeters", 0.0))
        <= float(portal_recipe.get("innerRadiusMeters", 0.0)) * 2.0 + 1.0
        or not 0.0 <= float(portal_recipe.get("radialNoiseMeters", -1.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT6
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_16 <= int(portal_recipe.get("segments", 0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_64
        or float(portal_recipe.get("textureRepeatMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5
        or len(portal_recipe.get("albedoColor", [])) != 4
        or not 0.0 <= float(portal_recipe.get("roughness", -1.0)) <= 1.0
        or not 0.0 <= float(portal_recipe.get("normalScale", -1.0)) <= 2.0
    ):
        raise Fo1ProfileError("owned cave Vault portal geometry or material contract is invalid")

    envelope_recipe = recipe.get("envelope")
    if not isinstance(envelope_recipe, dict) or envelope_recipe.get("schema") != (
        "opennv-fo1-owned-cave-topology-envelope/v1"
    ):
        raise Fo1ProfileError("owned cave envelope contract is missing")
    for field in ("diffusePath", "normalPath"):
        requested = str(envelope_recipe.get(field, "")).replace("/", "\\").lower()
        if requested not in texture_paths:
            raise Fo1ProfileError(f"owned cave envelope texture is missing: {requested}")
    if (
        not -PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT20 <= float(envelope_recipe.get("floorHeightMeters", 1.0)) <= 0.0
        or not 4.0 <= float(envelope_recipe.get("ceilingHeightMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_12POINT0
        or not 0.0 <= float(envelope_recipe.get("ceilingReliefMeters", -1.0)) <= 2.0
        or float(envelope_recipe.get("textureRepeatMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5
        or len(envelope_recipe.get("albedoColor", [])) != 4
        or not 0.0 <= float(envelope_recipe.get("roughness", -1.0)) <= 1.0
        or not 0.0 <= float(envelope_recipe.get("normalScale", -1.0)) <= 2.0
    ):
        raise Fo1ProfileError("owned cave envelope material or geometry contract is invalid")

    relief_recipe = recipe.get("frmRelief")
    if (
        not isinstance(relief_recipe, dict)
        or relief_recipe.get("schema") != "opennv-fo1-frm-relief-wall-set/v1"
        or not 1 <= int(relief_recipe.get("columnSampleStridePixels", 0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_8
        or not 1 <= int(relief_recipe.get("minimumOpaqueRunWidthPixels", 0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_16
        or not 0.0 < float(relief_recipe.get("normalBlurRadiusPixels", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_8POINT0
        or not 0.0 <= float(relief_recipe.get("normalLumaWeight", -1.0)) <= 1.0
        or not 0.0 <= float(relief_recipe.get("normalSilhouetteWeight", -1.0)) <= 1.0
        or not math.isclose(
            float(relief_recipe.get("normalLumaWeight", 0.0))
            + float(relief_recipe.get("normalSilhouetteWeight", 0.0)),
            1.0,
        )
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT1 <= float(relief_recipe.get("normalStrength", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_8POINT0
        or not 0.0 <= float(relief_recipe.get("frontRoughness", -1.0)) <= 1.0
        or not 0.0 <= float(relief_recipe.get("frontEmissionEnergy", -1.0)) <= 1.0
        or not 0.0 <= float(relief_recipe.get("groundAnchorMeters", -1.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT1
        or set(relief_recipe.get("profiles", {})) != {"cave", "vault"}
        or pixels_per_meter <= 0.0
    ):
        raise Fo1ProfileError("owned FRM relief contract is missing or invalid")
    for profile_name, profile in relief_recipe["profiles"].items():
        if (
            not isinstance(profile, dict)
            or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT1 <= float(profile.get("depthMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT5
            or float(profile.get("sideTextureRepeatMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5
            or len(profile.get("sideAlbedoColor", [])) != 4
            or not 0.0 <= float(profile.get("sideRoughness", -1.0)) <= 1.0
            or not 0.0 <= float(profile.get("sideNormalScale", -1.0)) <= 2.0
        ):
            raise Fo1ProfileError(f"owned FRM relief profile is invalid: {profile_name}")
        for field in ("sideDiffusePath", "sideNormalPath"):
            requested = str(profile.get(field, "")).replace("/", "\\").lower()
            if requested not in texture_paths:
                raise Fo1ProfileError(
                    f"owned FRM relief side texture is missing: {requested}"
                )

    connected_volume_recipe = recipe.get("connectedWallVolume")
    if (
        not isinstance(connected_volume_recipe, dict)
        or connected_volume_recipe.get("schema")
        != "opennv-fo1-connected-wall-volume/v2"
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_6
        <= int(connected_volume_recipe.get("minimumContourSegments", 0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_64
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT05
        <= float(connected_volume_recipe.get("groundSinkMeters", 0.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT75
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT20
        <= float(connected_volume_recipe.get("minimumRadiusMeters", 0.0))
        <= float(connected_volume_recipe.get("maximumRadiusMeters", 0.0))
        <= 3.0
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT1
        <= float(connected_volume_recipe.get("radiusFromFrmWidthScale", 0.0))
        <= 2.0
        or not 2.0
        <= float(connected_volume_recipe.get("minimumHeightMeters", 0.0))
        <= float(connected_volume_recipe.get("maximumHeightMeters", 0.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_12POINT0
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT25
        <= float(connected_volume_recipe.get("heightFromFrmPixelsScale", 0.0))
        <= 4.0
        or not 0.0
        <= float(connected_volume_recipe.get("radialNoiseFraction", -1.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT45
        or not 0.0
        <= float(connected_volume_recipe.get("verticalNoiseMeters", -1.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT05
        <= float(connected_volume_recipe.get("surfaceSampleSpacingMeters", 0.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT08
        <= float(connected_volume_recipe.get("contourResampleSpacingMeters", 0.0))
        <= 1.0
        or not 1
        <= int(connected_volume_recipe.get("contourSmoothIterations", 0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_12
        or not 0.0
        < float(connected_volume_recipe.get("contourSmoothStrength", 0.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT45
        or not 0.0
        <= float(connected_volume_recipe.get("contourInflationMeters", -1.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT75
        or not 0.0
        <= float(connected_volume_recipe.get("boundaryBulgeMeters", -1.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT75
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5
        <= float(connected_volume_recipe.get("macroNoiseWavelengthMeters", 0.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_12POINT0
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT2
        <= float(connected_volume_recipe.get("microNoiseWavelengthMeters", 0.0))
        <= 4.0
        or not isinstance(connected_volume_recipe.get("noiseSeed"), int)
        or set(connected_volume_recipe.get("profiles", {})) != {"cave", "vault"}
    ):
        raise Fo1ProfileError("owned connected wall-volume contract is missing or invalid")
    noise_blend = connected_volume_recipe.get("noiseBlend")
    if (
        not isinstance(noise_blend, dict)
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT25 <= float(noise_blend.get("ringWavelengthBase", 0.0)) <= 2.0
        or not 0.0
        <= float(noise_blend.get("ringWavelengthHeightScale", -1.0))
        <= 1.0
        or not 0.0 <= float(noise_blend.get("macroWeight", -1.0)) <= 1.0
        or not 0.0 <= float(noise_blend.get("ringMacroWeight", -1.0)) <= 1.0
        or not 0.0 <= float(noise_blend.get("microRadialWeight", -1.0)) <= 2.0
        or not 0.0 <= float(noise_blend.get("microJitterWeight", -1.0)) <= 2.0
        or not 0.0 <= float(noise_blend.get("verticalMacroWeight", -1.0)) <= 1.0
        or not 0.0 <= float(noise_blend.get("verticalMicroWeight", -1.0)) <= 1.0
        or not 0.0 <= float(noise_blend.get("periodicPrimaryWeight", -1.0)) <= 1.0
        or not 0.0
        <= float(noise_blend.get("periodicSecondaryWeight", -1.0))
        <= 1.0
        or not 1
        <= int(noise_blend.get("periodicSecondaryFrequencyMultiplier", 0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_6
        or not 0
        <= int(noise_blend.get("periodicSecondaryFrequencyOffset", -1))
        <= 4
        or not 0.0
        <= float(noise_blend.get("periodicSecondaryPhaseScale", -1.0))
        <= 2.0
        or not math.isclose(
            float(noise_blend["periodicPrimaryWeight"])
            + float(noise_blend["periodicSecondaryWeight"]),
            1.0,
        )
        or not math.isclose(
            float(noise_blend["verticalMacroWeight"])
            + float(noise_blend["verticalMicroWeight"]),
            1.0,
        )
    ):
        raise Fo1ProfileError("owned connected wall-volume noise blend is invalid")
    surface_dressing = connected_volume_recipe.get("surfaceDressing")
    dressing_scale = (
        surface_dressing.get("scale", [])
        if isinstance(surface_dressing, dict)
        else []
    )
    dressing_profiles = (
        surface_dressing.get("profiles", [])
        if isinstance(surface_dressing, dict)
        else []
    )
    hidden_dressing_surfaces = (
        surface_dressing.get("hiddenSurfaceIdentities", [])
        if isinstance(surface_dressing, dict)
        else []
    )
    if (
        not isinstance(surface_dressing, dict)
        or surface_dressing.get("schema")
        != "opennv-fo1-owned-cave-wall-dressing/v1"
        or not isinstance(dressing_profiles, list)
        or not dressing_profiles
        or not set(map(str, dressing_profiles)).issubset({"cave", "vault"})
        or str(surface_dressing.get("assetRole", "")) != "wall"
        or not 1.0 <= float(surface_dressing.get("spacingMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_12POINT0
        or not 1
        <= int(surface_dressing.get("minimumInstancesPerContour", 0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_8
        or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5
        <= float(surface_dressing.get("minimumContourPerimeterMeters", 0.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_12POINT0
        or not 1 <= int(surface_dressing.get("maximumInstances", 0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_1024
        or not isinstance(dressing_scale, list)
        or len(dressing_scale) != 3
        or any(not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT1 <= float(value) <= 2.0 for value in dressing_scale)
        or not 0.0
        <= float(surface_dressing.get("embedBehindContourMeters", -1.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_5POINT0
        or not 0.0
        <= float(surface_dressing.get("groundSinkMeters", -1.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT75
        or not math.isfinite(float(surface_dressing.get("yawOffsetDegrees", math.nan)))
        or not 0.0
        <= float(surface_dressing.get("yawJitterDegrees", -1.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_25POINT0
        or not 0.0
        <= float(surface_dressing.get("uniformScaleJitterFraction", -1.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT25
        or not 0.0
        <= float(surface_dressing.get("verticalScaleJitterFraction", -1.0))
        <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT25
        or not isinstance(hidden_dressing_surfaces, list)
        or not hidden_dressing_surfaces
        or len(set(map(str, hidden_dressing_surfaces)))
        != len(hidden_dressing_surfaces)
    ):
        raise Fo1ProfileError("owned connected wall-volume surface dressing is invalid")
    connected_rings = connected_volume_recipe.get("rings")
    if not isinstance(connected_rings, list) or not 4 <= len(connected_rings) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_12:
        raise Fo1ProfileError("owned connected wall-volume rings are missing or invalid")
    ring_heights = []
    for ring in connected_rings:
        if (
            not isinstance(ring, dict)
            or not 0.0 <= float(ring.get("heightFraction", -1.0)) <= 1.0
            or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT1 <= float(ring.get("radiusMultiplier", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT6
            or not 0.0 <= float(ring.get("centerJitterFraction", -1.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT3
        ):
            raise Fo1ProfileError("owned connected wall-volume ring is invalid")
        ring_heights.append(float(ring["heightFraction"]))
    if (
        ring_heights != sorted(set(ring_heights))
        or not math.isclose(ring_heights[0], 0.0)
        or not math.isclose(ring_heights[-1], 1.0)
    ):
        raise Fo1ProfileError("owned connected wall-volume rings must span zero to one")
    for profile_name, profile in connected_volume_recipe["profiles"].items():
        if (
            not isinstance(profile, dict)
            or float(profile.get("textureRepeatMeters", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5
            or len(profile.get("albedoColor", [])) != 4
            or not 0.0 <= float(profile.get("roughness", -1.0)) <= 1.0
            or not 0.0 <= float(profile.get("normalScale", -1.0)) <= 2.0
            or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT5 <= float(profile.get("triplanarSharpness", 0.0)) <= PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_16POINT0
            or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT25 <= float(profile.get("radiusScale", 0.0)) <= 2.0
            or not PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_0POINT25 <= float(profile.get("heightScale", 0.0)) <= 2.0
        ):
            raise Fo1ProfileError(
                f"owned connected wall-volume profile is invalid: {profile_name}"
            )
        for field in ("diffusePath", "normalPath"):
            requested = str(profile.get(field, "")).replace("/", "\\").lower()
            if requested not in texture_paths:
                raise Fo1ProfileError(
                    f"owned connected wall-volume texture is missing: {requested}"
                )

    cave_wall_pattern = re.compile(
        str(relief_recipe["caveWallArtPattern"]), re.IGNORECASE
    )
    vault_wall_pattern = re.compile(
        str(relief_recipe["vaultWallArtPattern"]), re.IGNORECASE
    )
    rock_pattern = re.compile(str(recipe["rockArtPattern"]), re.IGNORECASE)
    stalagmite_pattern = re.compile(str(recipe["stalagmiteArtPattern"]), re.IGNORECASE)
    cave_walls = [
        row
        for row in obstacles
        if cave_wall_pattern.fullmatch(str(row["artFilename"]))
    ]
    vault_walls = [
        row
        for row in obstacles
        if vault_wall_pattern.fullmatch(str(row["artFilename"]))
    ]
    walls = cave_walls + vault_walls
    rocks = [row for row in obstacles if rock_pattern.fullmatch(str(row["artFilename"]))]
    stalagmites = [
        row for row in obstacles if stalagmite_pattern.fullmatch(str(row["artFilename"]))
    ]
    placements: list[dict[str, object]] = []
    sprite_by_serial = {int(row["serial"]): row for row in sprite_placements}
    relief_artifacts: dict[str, dict[str, object]] = {}
    relief_placements: list[dict[str, object]] = []
    for row in sorted(walls, key=lambda value: int(value["serial"])):
        serial = int(row["serial"])
        sprite = sprite_by_serial.get(serial)
        if sprite is None:
            raise Fo1ProfileError(f"FRM relief wall has no sprite placement: {serial}")
        artifact_id = str(sprite["artifactId"])
        artifact = sprite_artifacts.get(artifact_id)
        if artifact is None:
            raise Fo1ProfileError(f"FRM relief wall has no artifact: {artifact_id}")
        if artifact_id not in relief_artifacts:
            image_path = staging / "sprites" / f"{artifact_id}.png"
            image = Image.open(image_path).convert("RGBA")
            contours = _column_silhouette_contours(
                image,
                int(relief_recipe["columnSampleStridePixels"]),
                int(relief_recipe["minimumOpaqueRunWidthPixels"]),
            )
            if not contours:
                raise Fo1ProfileError(f"FRM relief alpha has no contour: {artifact_id}")
            normal = _relief_normal_map(
                image,
                float(relief_recipe["normalBlurRadiusPixels"]),
                float(relief_recipe["normalLumaWeight"]),
                float(relief_recipe["normalSilhouetteWeight"]),
                float(relief_recipe["normalStrength"]),
            )
            normal_relative = Path("wall-relief") / f"{artifact_id}-normal.png"
            normal_artifact = save_png(
                normal,
                staging / normal_relative,
                output_root / normal_relative,
            )
            alpha = image.getchannel("A")
            bounds = alpha.getbbox()
            relief_artifacts[artifact_id] = {
                "id": artifact_id,
                "sourcePng": artifact["png"],
                "sourcePngSha256": artifact["pngSha256"],
                "sourceSha256": artifact["sourceSha256"],
                "width": artifact["width"],
                "height": artifact["height"],
                "frameOffset": artifact["frameOffset"],
                "normalPng": normal_artifact["png"],
                "normalPngSha256": normal_artifact["pngSha256"],
                "opaqueBoundsPixels": list(bounds) if bounds is not None else [],
                "opaquePixels": sum(
                    1 for value in alpha.get_flattened_data() if value > 0
                ),
                "contours": contours,
            }
        profile = "cave" if row in cave_walls else "vault"
        relief_placements.append(
            {
                "id": f"source-frm-relief-{serial}",
                "serial": serial,
                "tile": row["tile"],
                "worldMeters": hex_center(int(row["tile"])),
                "rotation": sprite["rotation"],
                "pixelOffset": sprite["pixelOffset"],
                "artifactId": artifact_id,
                "profile": profile,
                "artFilename": row["artFilename"],
                "source": {
                    "mapping": (
                        "exact FRM alpha/color face at the exact MAP tile; image-derived "
                        "contour extrusion supplies only the otherwise unknowable rotated depth"
                    ),
                    "serial": serial,
                    "tile": row["tile"],
                    "artFilename": row["artFilename"],
                },
            }
        )

    for row in sorted(rocks, key=lambda value: int(value["serial"])):
        rock_number = int(re.search(r"([0-9]+)", str(row["artFilename"])).group(1))
        role = "large-rock" if rock_number <= 3 else "small-rock"
        asset = assets_by_role[role]
        placements.append(
            {
                "id": f"source-rock-{row['serial']}",
                "assetId": asset["id"],
                "assetRole": role,
                "positionMeters": hex_center(int(row["tile"])),
                "yawDegrees": (
                    -float(row["rotation"]) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_60POINT0
                    + (
                        int(row["serial"])
                        * int(generation["rockSerialYawMultiplierDegrees"])
                    )
                    % PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_360
                ),
                "scale": scales[role],
                "source": {
                    "mapping": "exact MAP scenery hex with deterministic presentation yaw",
                    "serials": [row["serial"]],
                    "tiles": [row["tile"]],
                    "artFilename": row["artFilename"],
                },
            }
        )

    for row in sorted(stalagmites, key=lambda value: int(value["serial"])):
        role = "stalagmite"
        asset = assets_by_role[role]
        placements.append(
            {
                "id": f"source-stalagmite-{row['serial']}",
                "assetId": asset["id"],
                "assetRole": role,
                "positionMeters": hex_center(int(row["tile"])),
                "yawDegrees": -float(row["rotation"]) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_60POINT0,
                "scale": scales[role],
                "source": {
                    "mapping": "exact MAP scenery hex and rotation",
                    "serials": [row["serial"]],
                    "tiles": [row["tile"]],
                    "artFilename": row["artFilename"],
                },
            }
        )

    door_center = hex_center(int(door["tile"]))
    entry_center = hex_center(int(entry["tile"]))
    behind = [door_center[0] - entry_center[0], 0.0, door_center[2] - entry_center[2]]
    distance = math.hypot(behind[0], behind[2])
    if distance <= 0.0:
        raise Fo1ProfileError("door and first-run spawn cannot share a cave transition axis")
    behind = [behind[0] / distance, 0.0, behind[2] / distance]
    frame_behind = float(recipe["doorFrameBehindMeters"])

    toward_cave = [-behind[0], 0.0, -behind[2]]
    lateral = [toward_cave[2], 0.0, -toward_cave[0]]
    frame_asset = assets_by_role["vault-frame"]
    envelope = {
        **envelope_recipe,
        "topology": "all non-default V13ENT floor-backed 200x200 movement hexes",
        "source": {
            "mapping": (
                "continuous textured ceiling and outer closure generated from the exact "
                "Fallout 1 floor-backed hex topology; no rectangular room grid or sky box"
            ),
            "doorSerial": door["serial"],
            "doorTile": door["tile"],
            "entryTile": entry["tile"],
        },
    }
    vault_portal = {
        **portal_recipe,
        "originMeters": [
            door_center[0] + behind[0] * float(portal_recipe["behindDoorMeters"]),
            0.0,
            door_center[2] + behind[2] * float(portal_recipe["behindDoorMeters"]),
        ],
        "cavewardVector": toward_cave,
        "lateralVector": lateral,
        "floorHeightMeters": float(floor["heightMeters"]),
        "source": {
            "mapping": (
                "rock-lined circular portal derived from the exact Fallout 1 door-to-spawn "
                "axis; masks the donor module silhouette without changing the door tile, "
                "corridor axis, floor topology, or walkability"
            ),
            "doorSerial": door["serial"],
            "doorTile": door["tile"],
            "entryTile": entry["tile"],
        },
    }
    threshold_yaw = math.degrees(math.atan2(behind[0], behind[2]))
    placements.append(
        {
            "id": "authored-vault-frame",
            "assetId": frame_asset["id"],
            "assetRole": "vault-frame",
            "positionMeters": [
                door_center[0] + behind[0] * frame_behind,
                0.0,
                door_center[2] + behind[2] * frame_behind,
            ],
            "yawDegrees": threshold_yaw,
            "scale": recipe["vaultFrameScale"],
            "source": {
                "mapping": (
                    "owned FNV cave-to-Vault threshold shell aligned behind the exact "
                    "Fallout 1 door tile and door-to-spawn axis; presentation only, not "
                    "Fallout 1 layout authority"
                ),
                "serials": [door["serial"]],
                "tiles": [door["tile"], entry["tile"]],
                "behindMeters": frame_behind,
            },
        }
    )
    corpse_recipe = recipe.get("entranceCorpse")
    if not isinstance(corpse_recipe, dict):
        raise Fo1ProfileError("owned cave composition is missing the entrance corpse contract")
    corpse = next(
        (
            row
            for row in sprite_placements
            if int(row["serial"]) == int(corpse_recipe["serial"])
        ),
        None,
    )
    if corpse is None or any(
        str(corpse[field]).lower() != str(corpse_recipe[field]).lower()
        for field in ("pid", "artFilename")
    ) or int(corpse["tile"]) != int(corpse_recipe["tile"]):
        raise Fo1ProfileError("V13ENT entrance corpse source identity drift")
    corpse_role = "entrance-corpse"
    corpse_asset = assets_by_role[corpse_role]
    placements.append(
        {
            "id": f"source-entrance-corpse-{corpse['serial']}",
            "assetId": corpse_asset["id"],
            "assetRole": corpse_role,
            "positionMeters": hex_center(int(corpse["tile"])),
            "yawDegrees": -float(corpse["rotation"]) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_60POINT0
            + float(generation["corpseYawOffsetDegrees"]),
            "rotationDegrees": [
                float(generation["corpsePitchDegrees"]),
                -float(corpse["rotation"]) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_60POINT0
                + float(generation["corpseYawOffsetDegrees"]),
                0.0,
            ],
            "scale": scales[corpse_role],
            "source": {
                "mapping": "owned 3D corpse presentation on exact V13ENT item serial/tile",
                "serials": [corpse["serial"]],
                "tiles": [corpse["tile"]],
                "pid": corpse["pid"],
                "artFilename": corpse["artFilename"],
            },
        }
    )

    role_counts: dict[str, int] = {}
    for placement in placements:
        role = str(placement["assetRole"])
        role_counts[role] = role_counts.get(role, 0) + 1
    connected_volume_profiles = sorted(
        {str(row["profile"]) for row in relief_placements}
    )
    connected_volume_components = wall_volume_components(relief_placements)
    role_counts["wall-ribbon"] = len(connected_volume_components)
    role_counts["terrain-envelope"] = 1
    role_counts["vault-portal"] = 1
    grounded_instances = sum(role_counts.get(role, 0) for role in grounding_roles)
    return {
        "schema": "opennv-fo1-owned-cave-composition/v1",
        "status": "source-bound-owned-3d-composition",
        "recipe": recipe,
        "placements": placements,
        "frmRelief": {
            **relief_recipe,
            "pixelsPerMeter": pixels_per_meter,
            "artifacts": [
                relief_artifacts[key] for key in sorted(relief_artifacts)
            ],
            "placements": relief_placements,
            "coverage": {
                "artifacts": len(relief_artifacts),
                "placements": len(relief_placements),
                "contours": sum(
                    len(row["contours"]) for row in relief_artifacts.values()
                ),
                "caveWallPlacements": len(cave_walls),
                "vaultWallPlacements": len(vault_walls),
            },
        },
        "connectedWallVolume": {
            **connected_volume_recipe,
            "pixelsPerMeter": pixels_per_meter,
            "sourcePlacementContract": "frmRelief.placements",
            "components": connected_volume_components,
            "coverage": {
                "profileMeshes": len(connected_volume_components),
                "profiles": connected_volume_profiles,
                "sourcePlacements": len(relief_placements),
                "sourceSerials": len(
                    {int(row["serial"]) for row in relief_placements}
                ),
                "sourceTiles": len({int(row["tile"]) for row in relief_placements}),
            },
        },
        "envelope": envelope,
        "vaultPortal": vault_portal,
        "coverage": {
            "instances": len(placements) + len(connected_volume_components) + 2,
            "roles": role_counts,
            "wallRibbonSegments": len(relief_placements),
            "wallVolumeMeshes": len(connected_volume_components),
            "sourceWallObjects": len(walls),
            "sourceCaveWallObjects": len(cave_walls),
            "sourceVaultWallObjects": len(vault_walls),
            "sourceRockObjects": len(rocks),
            "sourceStalagmiteObjects": len(stalagmites),
            "sourceWallSerialsCovered": len({int(row["serial"]) for row in walls}),
            "sourceRockSerialsCovered": len({int(row["serial"]) for row in rocks}),
            "sourceStalagmiteSerialsCovered": len(
                {int(row["serial"]) for row in stalagmites}
            ),
            "groundedInstances": grounded_instances,
            "groundingRoles": {
                role: role_counts.get(role, 0) for role in sorted(grounding_roles)
            },
        },
    }


def floor_index_for_hex(tile: int) -> int:
    x = tile % PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200
    y = tile // PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200
    return (y // 2) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_100 + (PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_99 - x // 2)


def parse_ai_section(payload: str, section: str) -> dict[str, str]:
    current = ""
    values: dict[str, str] = {}
    for raw_line in payload.splitlines():
        line = raw_line.strip()
        if not line or line.startswith((";", "#")):
            continue
        if line.startswith("[") and line.endswith("]"):
            current = line[1:-1].strip()
            continue
        if current != section or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip()
    if not values:
        raise Fo1ProfileError(f"Fallout AI section is absent: {section}")
    return values


def floor_patch_center(index: int) -> list[float]:
    if not 0 <= index < PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_10000:
        raise ValueError(f"Fallout floor tile is outside the 100x100 grid: {index}")
    floor_x = PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_99 - index % PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_100
    floor_y = index // PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_100
    centers = [
        hex_center((floor_y * 2 + offset_y) * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200 + floor_x * 2 + offset_x)
        for offset_y in range(2)
        for offset_x in range(2)
    ]
    return [sum(center[axis] for center in centers) / 4.0 for axis in range(3)]


def classic_floor_screen(index: int) -> list[int]:
    if not 0 <= index < PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_10000:
        raise ValueError(f"Fallout floor tile is outside the 100x100 grid: {index}")
    storage_x = index % PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_100
    y = index // PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_100
    x = PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_99 - storage_x
    return [PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_4752 + PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_32 * y - PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_48 * x, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_24 * y + PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_12 * x]


def classic_hex_screen(tile: int) -> list[int]:
    if not 0 <= tile < PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_40000:
        raise ValueError(f"Fallout hex tile is outside the 200x200 grid: {tile}")
    x = tile % PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200
    y = tile // PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200
    return [
        PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_4816 - ((((x + 1) >> 1) << PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_5) + ((x >> 1) << 4) - (y << 4)),
        PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_12 * (x >> 1) + y * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_12 + PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_11,
    ]


def unproject_floor(image: Image.Image, size: int = PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_128) -> Image.Image:
    if image.width < 4 or image.height < 4 or size < 4:
        raise ValueError("Fallout floor FRM or unprojected texture size is invalid")
    source = image.convert("RGBA")
    denominator = float(size - 1)
    half_x = (source.width - 1) / 2.0
    half_y = (source.height - 1) / 2.0
    result = source.transform(
        (size, size),
        Image.Transform.AFFINE,
        (
            half_x / denominator,
            -half_x / denominator,
            half_x,
            half_y / denominator,
            half_y / denominator,
            0.0,
        ),
        Image.Resampling.BILINEAR,
    )
    pixels = result.load()
    opaque = deque()
    nearest: list[list[tuple[int, int] | None]] = [
        [None for _ in range(size)] for _ in range(size)
    ]
    for y in range(size):
        for x in range(size):
            if pixels[x, y][3] == PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_255:
                nearest[y][x] = (x, y)
                opaque.append((x, y))
    if not opaque:
        return result
    while opaque:
        x, y = opaque.popleft()
        source_coordinate = nearest[y][x]
        for next_x, next_y in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if (
                0 <= next_x < size
                and 0 <= next_y < size
                and nearest[next_y][next_x] is None
            ):
                nearest[next_y][next_x] = source_coordinate
                opaque.append((next_x, next_y))
    for y in range(size):
        for x in range(size):
            if pixels[x, y][3] == PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_255:
                continue
            source_x, source_y = nearest[y][x]
            red, green, blue, _ = pixels[source_x, source_y]
            pixels[x, y] = (red, green, blue, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_255)
    return result


def gltf_width(path: Path) -> float:
    document = read_json(path)
    minimums = []
    maximums = []
    for mesh in document.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            accessor_index = primitive.get("attributes", {}).get("POSITION")
            if accessor_index is None:
                continue
            accessor = document["accessors"][accessor_index]
            minimums.append(float(accessor["min"][0]))
            maximums.append(float(accessor["max"][0]))
    if not minimums:
        raise Fo1ProfileError(f"Vault door glTF has no POSITION bounds: {path}")
    width = max(maximums) - min(minimums)
    if width <= 0.0:
        raise Fo1ProfileError("Vault door glTF has a non-positive width")
    return width


def parse_critter_pro(data: bytes) -> dict[str, object]:
    if len(data) not in {PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_19C, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_1A0}:
        raise Fo1ProfileError(f"unsupported critter PRO size: 0x{len(data):x}")
    base = struct.unpack_from(">35i", data, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_30)
    bonus = struct.unpack_from(">35i", data, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_BC)
    stats = [base[index] + bonus[index] for index in range(PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_35)]
    head_fid, ai_packet, team = struct.unpack_from(">3i", data, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_20)
    return {
        "headFid": head_fid,
        "aiPacket": ai_packet,
        "team": team,
        "strength": stats[0],
        "perception": stats[1],
        "endurance": stats[2],
        "charisma": stats[3],
        "intelligence": stats[4],
        "agility": stats[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_5],
        "luck": stats[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_6],
        "hitPoints": stats[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_7],
        "actionPoints": stats[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_8],
        "armorClass": stats[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_9],
        "unarmedDamage": stats[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_10],
        "meleeDamage": stats[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_11],
        "sequence": stats[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_13],
        "criticalChance": stats[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_15],
    }


def parse_item_pro(data: bytes) -> dict[str, object]:
    """Decode the bounded item fields needed by the V13ENT combat slice.

    Fallout item PRO integers are big-endian.  The common item header ends at
    byte 0x38 and the subtype payload begins at 0x39.  Weapon and ammunition
    payload sizes are fixed; rejecting any other size keeps the generated
    gameplay contract from silently accepting a shifted layout.
    """
    if len(data) < PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_39:
        raise Fo1ProfileError(f"item PRO is too short: 0x{len(data):x}")
    pid = struct.unpack_from(">I", data, 0)[0]
    if (pid >> PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_24) != 0:
        raise Fo1ProfileError(f"item PRO stores a non-item PID: {pid:08x}")
    subtype = struct.unpack_from(">i", data, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_20)[0]
    subtype_names = {
        0: "armor",
        1: "container",
        2: "drug",
        3: "weapon",
        4: "ammo",
        PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_5: "misc",
        PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_6: "key",
    }
    if subtype not in subtype_names:
        raise Fo1ProfileError(f"unsupported item PRO subtype {subtype}: {pid:08x}")
    result: dict[str, object] = {
        "pid": f"{pid:08x}",
        "subtype": subtype,
        "subtypeName": subtype_names[subtype],
    }
    if subtype == 3:
        if len(data) != PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_122:
            raise Fo1ProfileError(f"unsupported weapon PRO size: 0x{len(data):x}")
        values = struct.unpack_from(">16i", data, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_39)
        fields = (
            "animationCode",
            "minimumDamage",
            "maximumDamage",
            "damageType",
            "maximumRangePrimary",
            "maximumRangeSecondary",
            "projectilePid",
            "minimumStrength",
            "actionPointCostPrimary",
            "actionPointCostSecondary",
            "criticalFailureType",
            "perk",
            "roundsPerAttack",
            "caliber",
            "ammunitionPid",
            "ammunitionCapacity",
        )
        result.update(dict(zip(fields, values)))
        result["soundCode"] = data[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_79]
    elif subtype == 4:
        if len(data) != PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_81:
            raise Fo1ProfileError(f"unsupported ammunition PRO size: 0x{len(data):x}")
        values = struct.unpack_from(">6i", data, PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_39)
        fields = (
            "caliber",
            "roundsPerObject",
            "armorClassModifier",
            "damageResistanceModifier",
            "damageMultiplier",
            "damageDivisor",
        )
        result.update(dict(zip(fields, values)))
    return result


def parse_pid_header(payload: str) -> dict[str, int]:
    result = {
        match.group(1): int(match.group(2), 0)
        for match in re.finditer(
            r"^\s*#define\s+(PID_[A-Z0-9_]+)\s+\(\s*(0x[0-9a-fA-F]+|\d+)\s*\)",
            payload,
            re.MULTILINE,
        )
    }
    if not result:
        raise Fo1ProfileError("Fallout item PID header contains no literal PID definitions")
    return result


def parse_starting_inventory(
    payload: str,
    pid_values: dict[str, int],
    skill_names: dict[str, str],
) -> dict[str, object]:
    base_match = re.search(
        r"procedure\s+base_inventory\s+begin\s*"
        r"call\s+give_item\s*\(\s*dude_obj\s*,\s*\{(?P<items>.*?)\}\s*\)\s*;\s*end",
        payload,
        re.IGNORECASE | re.DOTALL,
    )
    if base_match is None:
        raise Fo1ProfileError("V13CAVE base_inventory procedure could not be decoded")
    base_pairs = re.findall(
        r"(PID_[A-Z0-9_]+)\s*:\s*(\d+)", base_match.group("items")
    )
    if not base_pairs:
        raise Fo1ProfileError("V13CAVE base_inventory contains no literal items")

    def item(symbol: str, count: int) -> dict[str, object]:
        if symbol not in pid_values:
            raise Fo1ProfileError(f"V13CAVE inventory PID is absent from itempid.h: {symbol}")
        if count <= 0:
            raise Fo1ProfileError(f"V13CAVE inventory count is not positive: {symbol}")
        return {"symbol": symbol, "pid": pid_values[symbol], "objects": count}

    base = [item(symbol, int(count)) for symbol, count in base_pairs]
    tag_blocks = re.findall(
        r"if\s+is_skill_tagged\s*\(\s*(SKILL_[A-Z0-9_]+)\s*\)\s*then\s*begin"
        r"(?P<body>.*?)\bend\b",
        payload,
        re.IGNORECASE | re.DOTALL,
    )
    bonuses = []
    for skill_symbol, body in tag_blocks:
        normalized_skill = skill_symbol.upper()
        if normalized_skill not in skill_names:
            raise Fo1ProfileError(
                f"V13CAVE tag inventory skill has no runtime-name mapping: {normalized_skill}"
            )
        creates = list(
            re.finditer(
                r"Item\s*:=\s*create_object\s*\(\s*(PID_[A-Z0-9_]+)\s*,\s*0\s*,\s*0\s*\)\s*;",
                body,
                re.IGNORECASE,
            )
        )
        rows = []
        for index, create in enumerate(creates):
            segment_end = creates[index + 1].start() if index + 1 < len(creates) else len(body)
            segment = body[create.end() : segment_end]
            add = re.search(
                r"add_(?P<multiple>mult_)?objs?_to_inven\s*\(\s*dude_obj\s*,\s*Item"
                r"(?:\s*,\s*(?P<count>\d+))?\s*\)\s*;",
                segment,
                re.IGNORECASE,
            )
            if add is None:
                raise Fo1ProfileError(
                    f"V13CAVE tag inventory create has no supported add call: {create.group(1)}"
                )
            count = int(add.group("count")) if add.group("multiple") else 1
            rows.append(item(create.group(1).upper(), count))
        if not rows:
            raise Fo1ProfileError(
                f"V13CAVE tag inventory block contains no literal items: {normalized_skill}"
            )
        bonuses.append(
            {
                "skillSymbol": normalized_skill,
                "skill": skill_names[normalized_skill],
                "items": rows,
            }
        )
    if not bonuses:
        raise Fo1ProfileError("V13CAVE TagInven procedure could not be decoded")
    return {"base": base, "tagBonuses": bonuses}


def save_png(image: Image.Image, staging_path: Path, final_path: Path) -> dict[str, object]:
    staging_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(staging_path, format="PNG", optimize=False)
    return {
        "png": str(final_path.resolve()),
        "pngSha256": sha256_path(staging_path),
        "width": image.width,
        "height": image.height,
    }


def prepare(
    recipe_path: Path,
    ettu_root: Path,
    ettu_source_root: Path,
    fallout2_master: Path,
    fallout2_critter: Path,
    object_contract_path: Path,
    door_proof_path: Path,
    output_root: Path,
    presentation_manifest_path: Path | None = None,
) -> dict[str, object]:
    if output_root.exists():
        raise Fo1ProfileError(f"refusing to overwrite Fallout hex cache: {output_root}")
    recipe = read_json(recipe_path)
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise Fo1ProfileError(f"unexpected Fallout hex recipe: {recipe_path}")
    runtime_profile = load_runtime_profile_recipe(recipe_path, recipe.get("runtimeProfile"))
    generation = runtime_profile["generationAdaptation"]
    source_recipe = recipe["source"]
    map_path = (ettu_root / Path(source_recipe["mapRelativePath"])).resolve()
    palette_path = (ettu_root / Path(source_recipe["paletteRelativePath"])).resolve()
    if sha256_path(map_path) != source_recipe["mapSha256"]:
        raise Fo1ProfileError("V13ENT MAP hash drift")
    if sha256_path(palette_path) != source_recipe["paletteSha256"]:
        raise Fo1ProfileError("Fallout palette hash drift")
    if sha256_path(fallout2_master) != source_recipe["fallout2MasterSha256"]:
        raise Fo1ProfileError("Fallout 2 master.dat hash drift")
    if sha256_path(fallout2_critter) != source_recipe["fallout2CritterSha256"]:
        raise Fo1ProfileError("Fallout 2 critter.dat hash drift")
    if sha256_path(object_contract_path) != source_recipe["objectContractSha256"]:
        raise Fo1ProfileError("V13ENT object-contract hash drift")
    if sha256_path(door_proof_path) != recipe["door"]["proofSha256"]:
        raise Fo1ProfileError("Vault door proof hash drift")
    presentation_manifest = None
    if presentation_manifest_path is not None:
        presentation_manifest = read_json(presentation_manifest_path)
        if (
            presentation_manifest.get("schema") != "opennv-fo1-3d-presentation/v1"
            or presentation_manifest.get("status") != "transported-owned-presentation"
        ):
            raise Fo1ProfileError("unexpected Fallout 3D presentation manifest")
        if presentation_manifest.get("composition", {}).get("schema") != (
            "opennv-fo1-owned-cave-composition-recipe/v1"
        ):
            raise Fo1ProfileError("unexpected Fallout owned cave composition contract")
        creature_presentation = presentation_manifest["creature"]
        parse_form_id(str(creature_presentation["formId"]), "creature presentation formId")
        if (
            not str(creature_presentation["editorId"]).strip()
            or creature_presentation["coverage"]["animations"] < 1
        ):
            raise Fo1ProfileError("Fallout giant-rat presentation identity drift")
        if sha256_path(Path(creature_presentation["model"])) != creature_presentation["modelSha256"]:
            raise Fo1ProfileError("Fallout giant-rat presentation model hash drift")
        player_presentation = presentation_manifest["player"]
        parse_form_id(
            str(player_presentation["sourceActor"]["baseFormId"]),
            "player presentation baseFormId",
        )
        parse_form_id(
            str(player_presentation["outfit"]["formId"]),
            "player presentation outfitFormId",
        )
        if (
            not str(player_presentation["role"]).strip()
            or not str(player_presentation["displayName"]).strip()
            or player_presentation["coverage"]["surfaces"] < 1
            or player_presentation["coverage"]["skins"] < 1
            or player_presentation["coverage"]["animations"] < 1
        ):
            raise Fo1ProfileError("Fallout Vault Dweller presentation identity drift")
        if (
            sha256_path(Path(player_presentation["model"]))
            != player_presentation["modelSha256"]
            or sha256_path(Path(player_presentation["sidecar"]))
            != player_presentation["sidecarSha256"]
        ):
            raise Fo1ProfileError("Fallout Vault Dweller presentation artifact hash drift")

    layout = parse_map_layout(map_path.read_bytes())
    if len(layout.elevations) != 1 or layout.elevations[0].elevation != 0:
        raise Fo1ProfileError("V13ENT hex slice requires elevation zero only")
    elevation = layout.elevations[0]
    if elevation.raw_sha256 != source_recipe["floorGridSha256"]:
        raise Fo1ProfileError("V13ENT floor-grid hash drift")
    if (
        layout.header.enteringTile != recipe["mapHeaderEntry"]["tile"]
        or layout.header.enteringElevation != recipe["mapHeaderEntry"]["elevation"]
        or layout.header.enteringRotation != recipe["mapHeaderEntry"]["rotation"]
    ):
        raise Fo1ProfileError("V13ENT MAP-header entry contract drift")
    entry_recipe = recipe["entry"]
    entry_source_path = (ettu_source_root / Path(entry_recipe["sourceRelativePath"])).resolve()
    if sha256_path(entry_source_path) != entry_recipe["sourceSha256"]:
        raise Fo1ProfileError("V13CAVE first-run spawn source hash drift")
    entry_source_text = entry_source_path.read_text(encoding="utf-8")
    expected_override = (
        f"override_map_start_hex({entry_recipe['tile']}, "
        f"{entry_recipe['elevation']}, {entry_recipe['rotation']});"
    )
    if expected_override not in entry_source_text:
        raise Fo1ProfileError("V13CAVE first-run spawn override drift")

    inventory_recipe = recipe["startingInventory"]
    inventory_source_path = (
        ettu_source_root / Path(inventory_recipe["sourceRelativePath"])
    ).resolve()
    if sha256_path(inventory_source_path) != inventory_recipe["sourceSha256"]:
        raise Fo1ProfileError("V13CAVE starting-inventory source hash drift")
    pid_header_path = (
        ettu_source_root / Path(inventory_recipe["pidHeaderRelativePath"])
    ).resolve()
    if sha256_path(pid_header_path) != inventory_recipe["pidHeaderSha256"]:
        raise Fo1ProfileError("Fallout item PID header hash drift")
    pid_values = parse_pid_header(pid_header_path.read_text(encoding="utf-8"))
    starting_inventory = parse_starting_inventory(
        inventory_source_path.read_text(encoding="utf-8"),
        pid_values,
        {
            str(symbol): str(name)
            for symbol, name in inventory_recipe["skillNamesBySymbol"].items()
        },
    )

    rat_ai_recipe = recipe["ratAi"]
    rat_ai_path = (
        ettu_source_root / Path(rat_ai_recipe["sourceRelativePath"])
    ).resolve()
    if sha256_path(rat_ai_path) != rat_ai_recipe["sourceSha256"]:
        raise Fo1ProfileError("Fo1in2 rat AI source hash drift")
    rat_ai = parse_ai_section(
        rat_ai_path.read_text(encoding="utf-8"), str(rat_ai_recipe["section"])
    )
    expected_rat_ai = {
        "packet_num": int(rat_ai_recipe["packetNumber"]),
        "aggression": int(rat_ai_recipe["aggression"]),
        "max_dist": int(rat_ai_recipe["maximumDistanceHexes"]),
    }
    if any(int(rat_ai.get(field, "-1")) != expected for field, expected in expected_rat_ai.items()):
        raise Fo1ProfileError("Fo1in2 [Rats] AI packet contract drift")

    objects = read_json(object_contract_path)
    door = next(
        (row for row in objects["map"]["doors"] if row["serial"] == recipe["door"]["serial"]),
        None,
    )
    frame = next(
        (
            row
            for level in objects["map"]["objects"]["elevations"]
            for row in level["objects"]
            if row["serial"] == recipe["door"]["frameSerial"]
        ),
        None,
    )
    if door is None or frame is None:
        raise Fo1ProfileError("V13ENT door/frame objects are absent")
    for row, expected_tile, expected_rotation, expected_art in (
        (door, recipe["door"]["tile"], recipe["door"]["rotation"], recipe["door"]["artFilename"]),
        (frame, recipe["door"]["tile"], recipe["door"]["rotation"], recipe["door"]["frameArtFilename"]),
    ):
        if row["tile"] != expected_tile or row["rotation"] != expected_rotation or row["artFilename"] != expected_art:
            raise Fo1ProfileError("V13ENT door/frame placement drift")

    proof = read_json(door_proof_path)
    if proof.get("schema") != "opennv-fo1-door-presentation-proof/v1":
        raise Fo1ProfileError("unexpected Vault door proof schema")
    if proof["sourceObjectContract"]["door"]["serial"] != door["serial"]:
        raise Fo1ProfileError("Vault door proof source identity drift")
    def proof_output_path(value: object) -> Path:
        path = Path(str(value))
        if not path.is_absolute():
            path = door_proof_path.parent / path
        return path.resolve()

    model_path = proof_output_path(proof["outputs"]["model"])
    sidecar_path = proof_output_path(proof["outputs"]["sidecar"])
    material_path = proof_output_path(proof["outputs"]["materialManifest"])
    if sha256_path(model_path) != proof["outputs"]["modelSha256"]:
        raise Fo1ProfileError("Vault door model hash drift")
    if sha256_path(material_path) != proof["outputs"]["materialManifestSha256"]:
        raise Fo1ProfileError("Vault door material hash drift")

    resolver = Fo1ResourceResolver(ettu_root, fallout2_master, [fallout2_critter])
    inventory_symbols = sorted(
        {
            str(row["symbol"])
            for row in starting_inventory["base"]
        }
        | {
            str(row["symbol"])
            for bonus in starting_inventory["tagBonuses"]
            for row in bonus["items"]
        }
    )
    display_names = {
        str(symbol): str(name)
        for symbol, name in inventory_recipe["displayNamesBySymbol"].items()
    }
    if set(inventory_symbols) - set(display_names):
        raise Fo1ProfileError(
            "Fallout starting inventory contains items without display-name mappings: "
            + ", ".join(sorted(set(inventory_symbols) - set(display_names)))
        )
    inventory_items = []
    for symbol in inventory_symbols:
        pid = int(pid_values[symbol])
        prototype = resolver.prototype(pid)
        if prototype.object_type != 0 or prototype.filename is None:
            raise Fo1ProfileError(f"Fallout starting inventory PID is not an item: {symbol}")
        resource = resolver.read(f"proto\\items\\{prototype.filename}")
        profile = parse_item_pro(resource.data)
        if profile["pid"] != f"{pid:08x}":
            raise Fo1ProfileError(f"Fallout starting inventory PRO PID drift: {symbol}")
        inventory_items.append(
            {
                "symbol": symbol,
                "pid": f"{pid:08x}",
                "displayName": display_names[symbol],
                "prototypeFilename": prototype.filename,
                "prototypeSource": resource.source,
                "prototypeSha256": resource.sha256,
                "profile": profile,
            }
        )
    inventory_items_by_symbol = {row["symbol"]: row for row in inventory_items}
    ranged_symbol = str(inventory_recipe["equippedRangedSymbol"])
    melee_symbol = str(inventory_recipe["equippedMeleeSymbol"])
    ammo_symbol = str(inventory_recipe["ammunitionSymbol"])
    try:
        ranged_item = inventory_items_by_symbol[ranged_symbol]
        melee_item = inventory_items_by_symbol[melee_symbol]
        ammunition_item = inventory_items_by_symbol[ammo_symbol]
    except KeyError as error:
        raise Fo1ProfileError(
            f"Fallout equipped starting item is absent from the decoded inventory: {error.args[0]}"
        ) from error
    ranged_profile = ranged_item["profile"]
    melee_profile = melee_item["profile"]
    ammunition_profile = ammunition_item["profile"]
    if (
        ranged_profile["subtypeName"] != "weapon"
        or melee_profile["subtypeName"] != "weapon"
        or ammunition_profile["subtypeName"] != "ammo"
        or int(ranged_profile["ammunitionPid"]) != int(pid_values[ammo_symbol])
        or int(ranged_profile["caliber"]) != int(ammunition_profile["caliber"])
        or int(ranged_profile["ammunitionCapacity"]) <= 0
        or int(ranged_profile["roundsPerAttack"]) <= 0
        or int(melee_profile["maximumRangePrimary"]) != 1
        or int(melee_profile["ammunitionPid"]) != -1
    ):
        raise Fo1ProfileError("Fallout starting ranged/melee/ammunition relationship drift")
    tile_names = resolver.list_lines("art\\tiles\\tiles.lst")
    colors = palette_rgba(palette_path)
    floor_ids = [entry & PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_0FFF for entry in elevation.entries]
    unique_floor_ids = sorted(set(floor_ids))

    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=output_root.name + ".", dir=output_root.parent))
    try:
        floor_art = []
        for floor_id in unique_floor_ids:
            if floor_id >= len(tile_names):
                raise Fo1ProfileError(f"floor art ID {floor_id} exceeds tiles.lst")
            filename = tile_names[floor_id].split(" ", 1)[0].strip()
            resource = resolver.read(f"art\\tiles\\{filename}")
            decoded = decode_frm(resource.data, colors)
            source_frame = decoded["directions"][0]["frames"][0]["image"]
            unprojected = unproject_floor(
                source_frame,
                int(generation["unprojectedFloorTextureSizePixels"]),
            )
            relative = Path("textures") / f"floor-{floor_id:04d}.png"
            artifact = save_png(unprojected, staging / relative, output_root / relative)
            floor_art.append(
                {
                    "id": floor_id,
                    "filename": filename,
                    "source": resource.source,
                    "sourceSha256": resource.sha256,
                    "sourceWidth": source_frame.width,
                    "sourceHeight": source_frame.height,
                    **artifact,
                }
            )

        source_door_artifacts = []
        for label, filename in (
            ("door", door["artFilename"]),
            ("frame", frame["artFilename"]),
        ):
            resource = resolver.read(f"art\\scenery\\{filename}")
            decoded = decode_frm(resource.data, colors)
            frame_data = decoded["directions"][0]["frames"][0]
            image = frame_data["image"]
            relative = Path("textures") / f"source-{label}.png"
            artifact = save_png(image, staging / relative, output_root / relative)
            source_door_artifacts.append(
                {
                    "role": label,
                    "filename": filename,
                    "source": resource.source,
                    "sourceSha256": resource.sha256,
                    "frames": decoded["framesPerDirection"],
                    "frameOffset": [frame_data["x"], frame_data["y"]],
                    **artifact,
                }
            )

        source_door_image = next(row for row in source_door_artifacts if row["role"] == "door")
        target_door_width_meters = gltf_width(model_path) * float(recipe["door"]["targetUnitsToMeters"])
        measured_pixels_per_meter = float(source_door_image["width"]) / target_door_width_meters
        pixels_per_meter = float(runtime_profile["scenePresentation"]["sourceSprites"]["pixelsPerMeter"])
        if not math.isclose(measured_pixels_per_meter, pixels_per_meter, rel_tol=PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_FLOAT_1POINT0ENEGATIVE9):
            raise Fo1ProfileError("Fallout source-sprite scale drifted from its door-fit measurement")
        sprite_artifacts: dict[str, dict[str, object]] = {}
        sprite_placements = []
        skipped_sprite_objects = []
        top_level_objects = objects["map"]["objects"]["elevations"][0]["objects"]
        blocker_rows = []
        for obj in top_level_objects:
            flags = int(obj["flags"], PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_16)
            if obj["tile"] >= 0 and not flags & PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_00000010:
                blocker_rows.append(
                    {
                        "serial": obj["serial"],
                        "tile": obj["tile"],
                        "flags": obj["flags"],
                        "multihex": bool(flags & PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_00000800),
                        "artFilename": obj["artFilename"],
                        "objectType": obj["prototype"]["object_type"],
                        "objectTypeName": OBJECT_TYPE_NAMES[int(obj["prototype"]["object_type"])],
                    }
                )
        excluded_serials = {recipe["door"]["serial"], recipe["door"]["frameSerial"]}
        for obj in top_level_objects:
            if obj["serial"] in excluded_serials:
                continue
            if obj["tile"] < 0 or obj["artFilename"] is None:
                skipped_sprite_objects.append(
                    {"serial": obj["serial"], "reason": "off-grid-or-no-art"}
                )
                continue
            flags = int(obj["flags"], PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_16)
            if flags & 0x00000001:
                skipped_sprite_objects.append(
                    {"serial": obj["serial"], "reason": "OBJECT_HIDDEN"}
                )
                continue
            object_type = int(obj["prototype"]["object_type"])
            directory = TYPE_DIRECTORIES.get(object_type)
            if directory is None:
                skipped_sprite_objects.append(
                    {"serial": obj["serial"], "reason": f"unsupported-object-type-{object_type}"}
                )
                continue
            if object_type == 1:
                fid = int(obj["fid"], PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_16)
                animation = (fid >> PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_16) & PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_FF
                weapon = (fid >> PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_12) & PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_0F
                packed_rotation = (fid >> PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_28) & PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_HEX_07
                if animation != 0 or weapon != 0 or packed_rotation != 0:
                    skipped_sprite_objects.append(
                        {
                            "serial": obj["serial"],
                            "reason": (
                                f"unsupported-critter-fid-animation-{animation}-"
                                f"weapon-{weapon}-rotation-{packed_rotation}"
                            ),
                        }
                    )
                    continue
                base_name = obj["artFilename"].split(",", 1)[0]
                logical_path = f"art\\critters\\{base_name}aa.frm"
            else:
                logical_path = f"art\\{directory}\\{obj['artFilename']}"
            resource = resolver.read(logical_path)
            decoded = decode_frm(resource.data, colors)
            rotation = int(obj["rotation"])
            frames = decoded["directions"][rotation]["frames"]
            frame_index = int(obj["frame"])
            if not 0 <= frame_index < len(frames):
                raise Fo1ProfileError(
                    f"MAP object {obj['serial']} frame {frame_index} exceeds {logical_path} ({len(frames)})"
                )
            frame_data = frames[frame_index]
            artifact_key = f"{resource.sha256}:{rotation}:{frame_index}"
            artifact_id = hashlib.sha256(artifact_key.encode("ascii")).hexdigest()[:PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_20]
            if artifact_id not in sprite_artifacts:
                relative = Path("sprites") / f"{artifact_id}.png"
                artifact = save_png(
                    frame_data["image"],
                    staging / relative,
                    output_root / relative,
                )
                sprite_artifacts[artifact_id] = {
                    "id": artifact_id,
                    "logicalPath": logical_path,
                    "source": resource.source,
                    "sourceSha256": resource.sha256,
                    "rotation": rotation,
                    "frame": frame_index,
                    "frameOffset": [frame_data["x"], frame_data["y"]],
                    **artifact,
                }
            sprite_placements.append(
                {
                    "serial": obj["serial"],
                    "objectId": obj["id"],
                    "tile": obj["tile"],
                    "hex": [obj["tileX"], obj["tileY"]],
                    "worldMeters": hex_center(obj["tile"]),
                    "rotation": rotation,
                    "pixelOffset": obj["pixelOffset"],
                    "fid": obj["fid"],
                    "pid": obj["pid"],
                    "flags": obj["flags"],
                    "objectType": obj["prototype"]["object_type"],
                    "objectTypeName": OBJECT_TYPE_NAMES[object_type],
                    "artFilename": obj["artFilename"],
                    "artifactId": artifact_id,
                }
            )

        sprite_by_serial = {row["serial"]: row for row in sprite_placements}
        critter_names = {
            str(pid).lower(): str(name)
            for pid, name in recipe["critterDisplayNamesByPid"].items()
        }
        critter_profiles: dict[int, dict[str, object]] = {}
        combat_mobs = []
        for obj in top_level_objects:
            if int(obj["prototype"]["object_type"]) != 1:
                continue
            pid = int(obj["pid"], PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_16)
            profile = critter_profiles.get(pid)
            if profile is None:
                prototype_filename = obj["prototype"]["filename"]
                resource = resolver.read(f"proto\\critters\\{prototype_filename}")
                profile = {
                    **parse_critter_pro(resource.data),
                    "pid": obj["pid"],
                    "prototypeFilename": prototype_filename,
                    "prototypeSha256": resource.sha256,
                }
                critter_profiles[pid] = profile
            instance = obj["instanceValues"]
            if len(instance) != PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_11:
                raise Fo1ProfileError(f"critter MAP instance {obj['serial']} has {len(instance)} values")
            presentation = sprite_by_serial[obj["serial"]]
            display_name = critter_names.get(str(obj["pid"]).lower())
            if not display_name:
                raise Fo1ProfileError(
                    f"Fallout critter display-name mapping is absent: {obj['pid']}"
                )
            combat_mobs.append(
                {
                    "serial": obj["serial"],
                    "name": display_name,
                    "pid": obj["pid"],
                    "tile": obj["tile"],
                    "rotation": obj["rotation"],
                    "artifactId": presentation["artifactId"],
                    "currentHitPoints": instance[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_8],
                    "currentActionPoints": instance[3],
                    "runtimeAiPacket": instance[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_5],
                    "runtimeTeam": instance[PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_6],
                    "profile": profile,
                }
            )

        player_resource = resolver.read("art\\critters\\hmjmpsaa.frm")
        player_decoded = decode_frm(player_resource.data, colors)
        player_frame = player_decoded["directions"][recipe["entry"]["rotation"]]["frames"][0]
        player_relative = Path("sprites") / "player-hmjmpsaa.png"
        player_artifact = {
            "id": "fo1-player-hmjmpsaa",
            "logicalPath": "art\\critters\\hmjmpsaa.frm",
            "source": player_resource.source,
            "sourceSha256": player_resource.sha256,
            "rotation": recipe["entry"]["rotation"],
            "frame": 0,
            "frameOffset": [player_frame["x"], player_frame["y"]],
            **save_png(
                player_frame["image"],
                staging / player_relative,
                output_root / player_relative,
            ),
        }
        placement_by_serial = {row["serial"]: row for row in sprite_placements}
        obstacle_rows = []
        for blocker in blocker_rows:
            if int(blocker["objectType"]) == 1 or blocker["serial"] not in placement_by_serial:
                continue
            placement = placement_by_serial[blocker["serial"]]
            artifact = sprite_artifacts[placement["artifactId"]]
            obstacle_rows.append(
                {
                    **blocker,
                    "heightMeters": max(
                        float(generation["obstacleMinimumHeightMeters"]),
                        min(
                            float(generation["obstacleMaximumHeightMeters"]),
                            float(artifact["height"]) / pixels_per_meter,
                        ),
                    ),
                    "radiusMeters": max(
                        float(generation["obstacleMinimumRadiusMeters"]),
                        min(
                            float(generation["obstacleMaximumRadiusMeters"]),
                            float(artifact["width"]) / pixels_per_meter / 2.0,
                        ),
                    ),
                    "rotation": placement["rotation"],
                    "scaleSourceArtifactId": placement["artifactId"],
                }
            )
        owned_cave_composition = (
            None
            if presentation_manifest is None
            else build_owned_cave_composition(
                obstacle_rows,
                sprite_placements,
                sprite_artifacts,
                door,
                recipe["entry"],
                presentation_manifest,
                generation,
                staging,
                output_root,
                pixels_per_meter,
            )
        )

        floor_by_id = {row["id"]: row for row in floor_art}
        default_floor_id = int(recipe["grid"]["defaultFloorId"])
        non_default_floor_count = sum(
            floor_id != default_floor_id for floor_id in floor_ids
        )
        if owned_cave_composition is not None:
            owned_cave_composition["coverage"]["continuousFloorHexes"] = (
                non_default_floor_count * 4
            )
            owned_cave_composition["coverage"]["continuousFloorTriangles"] = (
                non_default_floor_count * PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_24
            )
        blocked_set = {row["tile"] for row in blocker_rows}
        blocked_hexes = sorted(blocked_set)
        provisional_walkable_hexes = sum(
            floor_ids[floor_index_for_hex(tile)] != default_floor_id
            and tile not in blocked_set
            for tile in range(PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_40000)
        )
        scene = {
            "schema": SCENE_SCHEMA,
            "status": "interactive-hex-topology-proof",
            "recipe": {"id": recipe["id"], "sha256": sha256_path(recipe_path)},
            "runtimeProfile": runtime_profile,
            "source": {
                "map": {"file": map_path.name, "sha256": sha256_path(map_path)},
                "floorGridSha256": elevation.raw_sha256,
                "objectContractSha256": sha256_path(object_contract_path),
                "fallout2MasterSha256": sha256_path(fallout2_master),
                "fallout2CritterSha256": sha256_path(fallout2_critter),
                "paletteSha256": sha256_path(palette_path),
            },
            "grid": {
                **recipe["grid"],
                "floorIds": floor_ids,
                "floorPatchCenters": [floor_patch_center(index) for index in range(PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_10000)],
                "floorArt": floor_art,
                "defaultFloorId": default_floor_id,
                "blockedHexes": blocked_hexes,
                "blockers": blocker_rows,
                "threeDObstacles": obstacle_rows,
                "threeDPresentation": {
                    "status": (
                        "owned-fnv-cave-kit-v1"
                        if presentation_manifest is not None
                        else "procedural-topology-proof"
                    ),
                    "boundaryHeightMeters": generation["proceduralBoundaryHeightMeters"],
                    "sourceSpriteOverlayDefaultVisible": presentation_manifest is None,
                    "source": "exact floor presence and MAP blocker central hexes",
                    "ownedPresentation": (
                        None
                        if presentation_manifest is None
                        else {
                            "manifest": str(presentation_manifest_path.resolve()),
                            "manifestSha256": sha256_path(presentation_manifest_path),
                            "caveKit": presentation_manifest["caveKit"],
                            "composition": owned_cave_composition,
                        }
                    ),
                },
            },
            "entry": {
                **recipe["entry"],
                "hex": [recipe["entry"]["tile"] % PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200, recipe["entry"]["tile"] // PREPARE_FO1_HEX_SCENE_COMPILER_CONTRACT_INTEGER_200],
                "worldMeters": hex_center(recipe["entry"]["tile"]),
                "floorId": floor_ids[floor_index_for_hex(recipe["entry"]["tile"])],
            },
            "mapHeaderEntry": {
                **recipe["mapHeaderEntry"],
                "worldMeters": hex_center(recipe["mapHeaderEntry"]["tile"]),
            },
            "door": {
                "source": door,
                "frame": frame,
                "worldMeters": hex_center(door["tile"]),
                "sourceArt": source_door_artifacts,
                "target": {
                    "model": str(model_path.resolve()),
                    "sidecar": str(sidecar_path.resolve()),
                    "sourceSha256": proof["target"]["sourceNifSha256"],
                    "materialManifest": str(material_path.resolve()),
                    "materialManifestSha256": proof["outputs"]["materialManifestSha256"],
                    "unitsToMeters": recipe["door"]["targetUnitsToMeters"],
                },
            },
            "objectSprites": {
                "presentation": (
                    "exact source FRM frame at exact MAP hex; world-locked static 2.5D; "
                    "camera-facing actors"
                ),
                "staticWorldYawDegrees": generation["staticWorldSpriteYawDegrees"],
                "pixelsPerMeter": pixels_per_meter,
                "scaleSource": "source door FRM width matched to mapped 3D door-leaf width",
                "artifacts": [sprite_artifacts[key] for key in sorted(sprite_artifacts)],
                "placements": sprite_placements,
                "skipped": skipped_sprite_objects,
            },
            "combat": {
                "status": "interactive-source-bound-ranged-and-melee-slice",
                "objectPixelsPerMeter": pixels_per_meter,
                "player": {
                    "name": recipe["tacticalProof"]["player"]["name"],
                    "presentation": (
                        "owned animated 3D Vault 13-suit donor with exact source sprite retained "
                        "as optional parity reference"
                        if presentation_manifest is not None
                        else "owned male Vault-jumpsuit idle art; character selection not connected"
                    ),
                    "tile": recipe["entry"]["tile"],
                    "artifact": player_artifact,
                    "ownedPresentation": (
                        None if presentation_manifest is None else presentation_manifest["player"]
                    ),
                    "stats": {
                        **recipe["tacticalProof"]["player"]["stats"],
                        "actionPoints": recipe["tacticalProof"]["actionPointsPerTurn"],
                    },
                    "weapon": {
                        "name": ranged_item["displayName"],
                        "source": "owned Fallout item PRO transported from the effective Et Tu/Fallout 2 resource set",
                        "pid": ranged_item["pid"],
                        "prototypeFilename": ranged_item["prototypeFilename"],
                        "prototypeSha256": ranged_item["prototypeSha256"],
                        "minimumDamage": ranged_profile["minimumDamage"],
                        "maximumDamage": ranged_profile["maximumDamage"],
                        "rangeHexes": ranged_profile["maximumRangePrimary"],
                        "actionPointCost": ranged_profile["actionPointCostPrimary"],
                        "minimumStrength": ranged_profile["minimumStrength"],
                        "roundsPerAttack": ranged_profile["roundsPerAttack"],
                        "caliber": ranged_profile["caliber"],
                        "ammunitionPid": f"{int(ranged_profile['ammunitionPid']):08x}",
                        "ammunitionCapacity": ranged_profile["ammunitionCapacity"],
                        "initialLoadedRounds": ranged_profile["ammunitionCapacity"],
                        "soundCode": ranged_profile["soundCode"],
                        "skill": "Small Guns",
                    },
                    "meleeWeapon": {
                        "name": melee_item["displayName"],
                        "source": "owned Fallout item PRO transported from the effective Et Tu/Fallout 2 resource set",
                        "pid": melee_item["pid"],
                        "prototypeFilename": melee_item["prototypeFilename"],
                        "prototypeSha256": melee_item["prototypeSha256"],
                        "minimumDamage": melee_profile["minimumDamage"],
                        "maximumDamage": melee_profile["maximumDamage"],
                        "rangeHexes": melee_profile["maximumRangePrimary"],
                        "actionPointCost": melee_profile["actionPointCostPrimary"],
                        "minimumStrength": melee_profile["minimumStrength"],
                        "soundCode": melee_profile["soundCode"],
                        "skill": "Melee Weapons",
                        "characterMeleeDamageApplied": True,
                    },
                    "inventory": {
                        "schema": "opennv-fo1-starting-inventory/v1",
                        "source": {
                            "script": str(inventory_recipe["sourceRelativePath"]),
                            "scriptSha256": str(inventory_recipe["sourceSha256"]),
                            "pidHeader": str(inventory_recipe["pidHeaderRelativePath"]),
                            "pidHeaderSha256": str(inventory_recipe["pidHeaderSha256"]),
                            "baseProcedure": str(inventory_recipe["baseProcedure"]),
                            "tagProcedure": str(inventory_recipe["tagProcedure"]),
                        },
                        "newWeaponMagazinePolicy": str(
                            inventory_recipe["newWeaponMagazinePolicy"]
                        ),
                        "equippedRangedSymbol": ranged_symbol,
                        "equippedMeleeSymbol": melee_symbol,
                        "ammunitionSymbol": ammo_symbol,
                        "items": inventory_items,
                        "base": [
                            {**row, "pid": f"{int(row['pid']):08x}"}
                            for row in starting_inventory["base"]
                        ],
                        "tagBonuses": [
                            {
                                **bonus,
                                "items": [
                                    {**row, "pid": f"{int(row['pid']):08x}"}
                                    for row in bonus["items"]
                                ],
                            }
                            for bonus in starting_inventory["tagBonuses"]
                        ],
                    },
                },
                "mobs": combat_mobs,
                "ownedCreaturePresentation": (
                    None if presentation_manifest is None else presentation_manifest["creature"]
                ),
                "ownedCombatPresentation": (
                    None
                    if presentation_manifest is None
                    else presentation_manifest["combatPresentation"]
                ),
                "rules": {
                    "turnOrder": "player then source-team-1 rats by sequence/serial",
                    "ratMovementLimitHexes": runtime_profile["gameplayAdaptation"][
                        "ratMovementLimitHexes"
                    ],
                    "ratAttackRangeHexes": runtime_profile["gameplayAdaptation"][
                        "ratAttackRangeHexes"
                    ],
                    "ratActivation": {
                        "source": rat_ai_recipe["source"],
                        "sourceSha256": rat_ai_recipe["sourceSha256"],
                        "section": rat_ai_recipe["section"],
                        "packetNumber": expected_rat_ai["packet_num"],
                        "aggression": expected_rat_ai["aggression"],
                        "maximumDistanceHexes": expected_rat_ai["max_dist"],
                        "contract": (
                            "local source-informed activation; direct attack always alerts; "
                            "not a complete Fallout engine perception/LOS implementation"
                        ),
                    },
                    "damageRoll": (
                        "source weapon bounds plus character melee bonus with deterministic "
                        "scene-seeded proof rolls"
                    ),
                },
                "unsupported": [
                    "critical hit/failure tables, aimed shots, burst fire, and armor resistance",
                    "complete AI packet behavior and sequence queue",
                ],
            },
            "camera": {
                "homeFocusMeters": [
                    (hex_center(recipe["entry"]["tile"])[0] + hex_center(door["tile"])[0]) / 2.0,
                    0.0,
                    (hex_center(recipe["entry"]["tile"])[2] + hex_center(door["tile"])[2]) / 2.0,
                ],
                "homeSizeMeters": runtime_profile["camera"]["tactical"]["homeSizeMeters"],
                "yawDegrees": runtime_profile["camera"]["tactical"]["homeYawDegrees"],
                "pitchDegrees": runtime_profile["camera"]["tactical"]["homePitchDegrees"],
            },
            "tacticalProof": recipe["tacticalProof"],
            "coverage": {
                "floorEntries": len(floor_ids),
                "uniqueFloorIds": len(floor_by_id),
                "nonDefaultFloorEntries": non_default_floor_count,
                "floorBackedHexes": non_default_floor_count * 4,
                "provisionalWalkableHexesAfterObjectFlags": provisional_walkable_hexes,
                "blockedHexes": len(blocked_hexes),
                "multihexBlockersWithCentralHexOnly": sum(row["multihex"] for row in blocker_rows),
                "threeDObstacles": len(obstacle_rows),
                "topLevelObjects": objects["map"]["objects"]["totalTopLevelObjects"],
                "spritePlacements": len(sprite_placements),
                "spriteArtifacts": len(sprite_artifacts),
                "skippedSpriteObjects": len(skipped_sprite_objects),
                "combatMobs": len(combat_mobs),
                "ownedCreatureAnimations": (
                    0
                    if presentation_manifest is None
                    else presentation_manifest["creature"]["coverage"]["animations"]
                ),
                "ownedCaveAssets": (
                    0
                    if presentation_manifest is None
                    else len(presentation_manifest["caveKit"]["assets"])
                ),
                "ownedCaveInstances": (
                    0
                    if owned_cave_composition is None
                    else owned_cave_composition["coverage"]["instances"]
                ),
                "doors": len(objects["map"]["doors"]),
                "sourceDoorFrames": door["prototype"]["subtype_name"] == "door",
            },
            "supported": recipe["supported"],
            "unsupported": recipe["unsupported"],
        }
        scene_path = staging / "hex-scene.json"
        write_json(scene_path, scene)
        manifest = {
            "schema": CACHE_SCHEMA,
            "status": "prepared-owned-data",
            "scene": str((output_root / "hex-scene.json").resolve()),
            "sceneSha256": sha256_path(scene_path),
            "floorTextures": len(floor_art),
            "walkableHexes": provisional_walkable_hexes,
            "entryTile": recipe["entry"]["tile"],
            "doorTile": door["tile"],
            "retailOrDerivedAssetsPackaged": False,
        }
        write_json(staging / "hex-cache-manifest.json", manifest)
        os.replace(staging, output_root)
        return manifest
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--ettu-root", type=Path, required=True)
    parser.add_argument("--ettu-source-root", type=Path, required=True)
    parser.add_argument("--fallout2-master", type=Path, required=True)
    parser.add_argument("--fallout2-critter", type=Path, required=True)
    parser.add_argument("--object-contract", type=Path, required=True)
    parser.add_argument("--door-proof", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--presentation-manifest", type=Path)
    args = parser.parse_args()
    result = prepare(
        args.recipe.resolve(),
        args.ettu_root.resolve(),
        args.ettu_source_root.resolve(),
        args.fallout2_master.resolve(),
        args.fallout2_critter.resolve(),
        args.object_contract.resolve(),
        args.door_proof.resolve(),
        args.output_root.resolve(),
        None if args.presentation_manifest is None else args.presentation_manifest.resolve(),
    )
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
