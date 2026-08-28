#!/usr/bin/env python3
"""Prepare source-authored 2.5D presentation contracts for every Fallout 1 MAP.

The resulting ignored cache contains locally derived PNGs and neutral JSON.
It is a source-reference rendering lane, not a generic 3D, gameplay, quest, or
parity promotion.
"""

from __future__ import annotations

import argparse
from collections import Counter, deque
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import tempfile
from typing import Any

from fo1_frm import decode_frm, palette_rgba
from fo1_map_objects import Fo1ResourceResolver, OBJECT_TYPE_NAMES, TYPE_DIRECTORIES
from fo1_profile import Fo1ProfileError, sha256_path
from prepare_fo1_hex_scene import (
    floor_index_for_hex,
    hex_center,
    load_runtime_profile_recipe,
    parse_critter_pro,
    unproject_floor,
)
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_HEX_07 = 0x07
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_HEX_0F = 0x0F
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_HEX_0FFF = 0x0FFF
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_HEX_FF = 0xFF
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_FLOAT_1POINT08 = 1.08
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_100 = 100
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_10000 = 10000
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_11 = 11
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_12 = 12
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_16 = 16
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_20 = 20
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200 = 200
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_FLOAT_255POINT0 = 255.0
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_28 = 28
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_40000 = 40000
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_5 = 5
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_6 = 6
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_7 = 7
PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_8 = 8



RECIPE_SCHEMA = "opennv-fo1-campaign-presentation-recipe/v1"
CAMPAIGN_TRANSPORT_SCHEMA = "opennv-fo1-campaign-transport/v1"
MAP_TRANSPORT_SCHEMA = "opennv-fo1-campaign-map-transport/v1"
PRESENTATION_SCHEMA = "opennv-fo1-campaign-presentation/v1"
MAP_PRESENTATION_SCHEMA = "opennv-fo1-campaign-map-presentation/v1"
CACHE_SCHEMA = "opennv-fo1-campaign-presentation-cache/v1"
WALL_TOPOLOGY_SCHEMA = "opennv-fo1-connected-wall-topology/v1"
MESSAGE_ROW = re.compile(r"^\{(-?[0-9]+)\}\{[^}]*\}\{(.*)\}$")


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def payload(document: object) -> bytes:
    return (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")


def write_json(path: Path, document: object) -> str:
    encoded = payload(document)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(encoded)
    return hashlib.sha256(encoded).hexdigest()


def parse_message_catalog(text: str) -> dict[int, str]:
    result: dict[int, str] = {}
    for source_line in text.replace("\r\n", "\n").replace("\r", "\n").split("\n"):
        line = source_line.strip()
        if not line or line.startswith("#"):
            continue
        match = MESSAGE_ROW.match(line)
        if match is None:
            raise Fo1ProfileError(f"unsupported Fallout MSG row: {line!r}")
        number = int(match.group(1))
        if number in result:
            raise Fo1ProfileError(f"duplicate Fallout MSG number: {number}")
        result[number] = match.group(2)
    if not result:
        raise Fo1ProfileError("Fallout message catalog is empty")
    return result


def source_sprite_logical_path(obj: dict[str, Any], map_format: dict[str, Any]) -> str | None:
    filename = obj.get("artFilename")
    if not filename:
        return None
    object_type = int(obj["prototype"]["object_type"])
    directory = TYPE_DIRECTORIES.get(object_type)
    if directory is None:
        return None
    if object_type != 1:
        return f"art\\{directory}\\{filename}"
    fid = int(obj["fid"], PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_16)
    animation = (fid >> PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_16) & PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_HEX_FF
    weapon = (fid >> PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_12) & PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_HEX_0F
    packed_rotation = (fid >> PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_28) & PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_HEX_07
    if (
        animation != int(map_format["supportedCritterIdleAnimation"])
        or weapon != int(map_format["supportedCritterIdleWeapon"])
        or packed_rotation != int(map_format["supportedCritterPackedRotation"])
    ):
        return None
    base_name = str(filename).split(",", 1)[0]
    return f"art\\critters\\{base_name}aa.frm"


def resolve_child(root: Path, relative: str) -> Path:
    if Path(relative).is_absolute():
        raise Fo1ProfileError(f"campaign artifact path must be relative: {relative}")
    path = (root / Path(relative)).resolve()
    if not path.is_relative_to(root.resolve()):
        raise Fo1ProfileError(f"campaign artifact path escapes its root: {relative}")
    return path


def save_image(image, staging_path: Path, final_relative: str) -> dict[str, Any]:
    staging_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(staging_path, format="PNG", optimize=False)
    return {
        "path": final_relative,
        "sha256": sha256_path(staging_path),
        "width": image.width,
        "height": image.height,
    }


def average_opaque_rgba(image, alpha_threshold: float) -> list[float] | None:
    """Return an alpha-weighted source color without treating transparency as black."""
    if not 0.0 <= alpha_threshold <= 1.0:
        raise Fo1ProfileError("Fallout sprite alpha threshold is invalid")
    threshold = round(alpha_threshold * PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_FLOAT_255POINT0)
    totals = [0.0, 0.0, 0.0]
    weight = 0.0
    for red, green, blue, alpha in image.convert("RGBA").getdata():
        if alpha <= threshold:
            continue
        sample_weight = alpha / PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_FLOAT_255POINT0
        totals[0] += red * sample_weight
        totals[1] += green * sample_weight
        totals[2] += blue * sample_weight
        weight += sample_weight
    if weight == 0.0:
        return None
    return [round(value / weight / PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_FLOAT_255POINT0, PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_7) for value in totals] + [1.0]


def hex_neighbor_across_edge(tile: int, edge: int) -> int:
    """Match retail column-parity directions and flat-top corner-edge order."""
    if not 0 <= tile < PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_40000 or not 0 <= edge < PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_6:
        return -1
    column = tile % PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200
    row = tile // PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200
    odd = bool(column & 1)
    offsets = (
        (1, (0 if odd else 1)),
        (0, 1),
        (-1, (0 if odd else 1)),
        (-1, (-1 if odd else 0)),
        (0, -1),
        (1, (-1 if odd else 0)),
    )
    target_column = column + offsets[edge][0]
    target_row = row + offsets[edge][1]
    if not 0 <= target_column < PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200 or not 0 <= target_row < PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200:
        return -1
    return target_row * PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200 + target_column


def build_connected_wall_topology(
    source_objects: list[dict[str, Any]],
    floor_ids: list[int],
    default_tile_id: int,
    no_block_flag: int,
) -> dict[str, Any]:
    """Build an exact, O(n) union of source wall-object hex occupancy.

    FRM images remain provenance and color evidence.  Geometry is derived from
    the union boundary, so adjacent wall records cannot become disconnected
    upright cards or one box per source object.
    """
    if len(floor_ids) != PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_10000 or default_tile_id < 0 or no_block_flag <= 0:
        raise Fo1ProfileError("Fallout connected-wall topology inputs are invalid")
    source_walls = [
        row for row in source_objects
        if int(row["prototype"]["object_type"]) == 3
    ]
    cells_by_tile: dict[int, list[dict[str, Any]]] = {}
    off_grid = 0
    source_serials: set[int] = set()
    for row in source_walls:
        serial = int(row["serial"])
        if serial in source_serials:
            raise Fo1ProfileError(f"duplicate Fallout wall serial: {serial}")
        source_serials.add(serial)
        tile = int(row["tile"])
        if not 0 <= tile < PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_40000:
            off_grid += 1
            continue
        rotation = int(row["rotation"])
        if not 0 <= rotation < PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_6:
            raise Fo1ProfileError(f"Fallout wall rotation is invalid: {serial}")
        flags = int(str(row["flags"]), PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_16)
        cells_by_tile.setdefault(tile, []).append(
            {
                "serial": serial,
                "rotation": rotation,
                "artFilename": row.get("artFilename"),
                "blocking": not bool(flags & no_block_flag),
            }
        )

    occupied = set(cells_by_tile)
    visited: set[int] = set()
    component_count = 0
    largest_component = 0
    isolated_hexes = 0
    for start in occupied:
        if start in visited:
            continue
        component_count += 1
        visited.add(start)
        queue = deque([start])
        component_size = 0
        while queue:
            tile = queue.popleft()
            component_size += 1
            for edge in range(PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_6):
                neighbor = hex_neighbor_across_edge(tile, edge)
                if neighbor in occupied and neighbor not in visited:
                    visited.add(neighbor)
                    queue.append(neighbor)
        largest_component = max(largest_component, component_size)
        if component_size == 1:
            isolated_hexes += 1

    boundary_edges = 0
    floor_facing_edges = 0
    void_facing_edges = 0
    for tile in occupied:
        for edge in range(PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_6):
            neighbor = hex_neighbor_across_edge(tile, edge)
            if neighbor in occupied:
                continue
            boundary_edges += 1
            if (
                neighbor >= 0
                and floor_ids[floor_index_for_hex(neighbor)] != default_tile_id
            ):
                floor_facing_edges += 1
            else:
                void_facing_edges += 1

    ordered_tiles = sorted(occupied)
    tile_payload = b"".join(struct.pack(">I", tile) for tile in ordered_tiles)
    cells = [
        {
            "tile": tile,
            "sourceObjects": sorted(
                cells_by_tile[tile], key=lambda row: int(row["serial"])
            ),
        }
        for tile in ordered_tiles
    ]
    on_grid = sum(len(row["sourceObjects"]) for row in cells)
    blocking_hexes = sum(
        any(bool(source["blocking"]) for source in row["sourceObjects"])
        for row in cells
    )
    return {
        "schema": WALL_TOPOLOGY_SCHEMA,
        "mode": "source-wall-hex-union-v1",
        "derivation": "exact MAP object_type=wall hex union; FRMs are provenance/color evidence only",
        "complexity": (
            "topology O(source-wall-objects + occupied-wall-hexes + exposed-edges); "
            "canonical ordering O(occupied-wall-hexes log occupied-wall-hexes)"
        ),
        "sourceWallObjects": len(source_walls),
        "onGridSourceWallObjects": on_grid,
        "offGridSourceWallObjects": off_grid,
        "occupiedHexesSha256": hashlib.sha256(tile_payload).hexdigest(),
        "cells": cells,
        "coverage": {
            "occupiedHexes": len(cells),
            "blockingHexes": blocking_hexes,
            "nonBlockingHexes": len(cells) - blocking_hexes,
            "connectedComponents": component_count,
            "largestComponentHexes": largest_component,
            "isolatedHexes": isolated_hexes,
            "boundaryEdges": boundary_edges,
            "floorFacingBoundaryEdges": floor_facing_edges,
            "voidFacingBoundaryEdges": void_facing_edges,
        },
    }


def validate_viewer_config(viewer: object) -> dict[str, Any]:
    if not isinstance(viewer, dict):
        raise Fo1ProfileError("Fallout campaign viewer config is absent")
    default_map = viewer.get("defaultMapId")
    if not isinstance(default_map, str) or re.fullmatch(r"[a-z0-9_]+", default_map) is None:
        raise Fo1ProfileError("Fallout campaign viewer default map ID is invalid")
    panel = viewer.get("statusPanel")
    capture = viewer.get("capture")
    scene = viewer.get("scene")
    walls = viewer.get("wallGeometry")
    if (
        not isinstance(panel, dict)
        or not isinstance(capture, dict)
        or not isinstance(scene, dict)
        or not isinstance(walls, dict)
    ):
        raise Fo1ProfileError(
            "Fallout campaign viewer scene, walls, panel, or capture config is absent"
        )
    if scene.get("sourceSpriteOrientation") != "camera-facing-source-reference":
        raise Fo1ProfileError("Fallout campaign viewer sprite orientation is unsupported")
    if (
        not isinstance(scene.get("sourceReferenceOrbitEnabled"), bool)
        or not isinstance(scene.get("sourceReferenceVisibleByDefault"), bool)
    ):
        raise Fo1ProfileError("Fallout campaign viewer orbit or source-overlay policy is invalid")
    source_color = scene.get("sourceColorMultiplier")
    if (
        not isinstance(source_color, list)
        or len(source_color) != 4
        or any(not isinstance(value, (int, float)) or value <= 0.0 for value in source_color)
    ):
        raise Fo1ProfileError("Fallout campaign viewer source-color multiplier is invalid")
    for field in ("tonemapExposure", "fogDensity", "fogAerialPerspective"):
        if not isinstance(scene.get(field), (int, float)) or float(scene[field]) < 0.0:
            raise Fo1ProfileError(f"Fallout campaign viewer scene value is invalid: {field}")
    if float(scene["tonemapExposure"]) <= 0.0 or float(scene["fogAerialPerspective"]) > 1.0:
        raise Fo1ProfileError("Fallout campaign viewer exposure or aerial perspective is invalid")
    if walls.get("mode") != "source-wall-hex-union-v1":
        raise Fo1ProfileError("Fallout campaign wall-geometry mode is unsupported")
    if (
        walls.get("sourceObjectType") != 3
        or walls.get("collisionMode") != "blocking-wall-hex-union-v1"
    ):
        raise Fo1ProfileError("Fallout campaign wall source or collision policy is invalid")
    for field in (
        "cellRadiusScale", "heightMeters", "groundSinkMeters", "roughness",
        "metallic", "sourceAlphaThreshold",
    ):
        if not isinstance(walls.get(field), (int, float)) or float(walls[field]) < 0.0:
            raise Fo1ProfileError(f"Fallout campaign wall-geometry value is invalid: {field}")
    if (
        not 1.0 <= float(walls["cellRadiusScale"]) <= PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_FLOAT_1POINT08
        or float(walls["heightMeters"]) <= 1.0
        or float(walls["groundSinkMeters"]) >= float(walls["heightMeters"])
        or float(walls["roughness"]) > 1.0
        or float(walls["metallic"]) > 1.0
        or float(walls["sourceAlphaThreshold"]) > 1.0
    ):
        raise Fo1ProfileError("Fallout campaign wall-geometry range is invalid")
    for field in ("unresolvedSourceAlbedo", "sideColorMultiplier", "topColorMultiplier"):
        color = walls.get(field)
        if (
            not isinstance(color, list)
            or len(color) != 4
            or any(
                not isinstance(value, (int, float)) or not 0.0 <= value <= 1.0
                for value in color
            )
        ):
            raise Fo1ProfileError(f"Fallout campaign wall color is invalid: {field}")
    for field in (
        "leftPixels", "topPixels", "rightPixels", "bottomPixels",
        "textLeftPixels", "textTopPixels", "textRightPixels", "textBottomPixels",
    ):
        if not isinstance(panel.get(field), (int, float)):
            raise Fo1ProfileError(f"Fallout campaign viewer panel value is invalid: {field}")
    for field in ("panelColor", "fontColor"):
        color = panel.get(field)
        if (
            not isinstance(color, list)
            or len(color) != 4
            or any(not isinstance(value, (int, float)) or not 0.0 <= value <= 1.0 for value in color)
        ):
            raise Fo1ProfileError(f"Fallout campaign viewer color is invalid: {field}")
    if not isinstance(panel.get("fontSizePixels"), int) or panel["fontSizePixels"] <= 0:
        raise Fo1ProfileError("Fallout campaign viewer font size is invalid")
    for field in ("warmupFrames", "settleFrames", "expectedWidthPixels", "expectedHeightPixels"):
        if not isinstance(capture.get(field), int) or capture[field] <= 0:
            raise Fo1ProfileError(f"Fallout campaign capture integer is invalid: {field}")
    for field in (
        "darkPixelLuminance", "minimumMeanLuminance",
        "minimumLuminanceDeviation", "maximumDarkFraction",
    ):
        if (
            not isinstance(capture.get(field), (int, float))
            or not 0.0 <= float(capture[field]) <= 1.0
        ):
            raise Fo1ProfileError(f"Fallout campaign capture threshold is invalid: {field}")
    return viewer


def prepare(
    recipe_path: Path,
    campaign_path: Path,
    campaign_sha256: str,
    ettu_root: Path,
    fallout2_master: Path,
    fallout2_critter: Path,
    output_root: Path,
) -> dict[str, Any]:
    recipe_path = recipe_path.resolve()
    campaign_path = campaign_path.resolve()
    ettu_root = ettu_root.resolve()
    fallout2_master = fallout2_master.resolve()
    fallout2_critter = fallout2_critter.resolve()
    output_root = output_root.resolve()
    if output_root.exists():
        raise Fo1ProfileError(
            f"refusing to overwrite Fallout campaign presentation cache: {output_root}"
        )
    recipe = read_json(recipe_path)
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise Fo1ProfileError(f"unexpected Fallout campaign presentation recipe: {recipe_path}")
    runtime_profile = load_runtime_profile_recipe(recipe_path, recipe.get("runtimeProfile"))
    source = recipe["source"]
    if sha256_path(campaign_path) != campaign_sha256.lower():
        raise Fo1ProfileError("Fallout campaign transport hash drift")
    if sha256_path(fallout2_master) != source["fallout2MasterSha256"]:
        raise Fo1ProfileError("Fallout 2 master.dat hash drift")
    if sha256_path(fallout2_critter) != source["fallout2CritterSha256"]:
        raise Fo1ProfileError("Fallout 2 critter.dat hash drift")
    palette_path = (ettu_root / Path(source["paletteRelativePath"])).resolve()
    if sha256_path(palette_path) != source["paletteSha256"]:
        raise Fo1ProfileError("Fallout palette hash drift")
    campaign = read_json(campaign_path)
    if (
        campaign.get("schema") != CAMPAIGN_TRANSPORT_SCHEMA
        or campaign.get("status") != "transported-not-rendered"
        or campaign.get("retailOrDerivedAssetsPackaged") is not False
    ):
        raise Fo1ProfileError("unexpected Fallout campaign transport contract")

    resolver = Fo1ResourceResolver(ettu_root, fallout2_master, [fallout2_critter])
    colors = palette_rgba(palette_path)
    tile_names = resolver.list_lines("art\\tiles\\tiles.lst")
    critter_message_resource = resolver.read(source["critterMessagesLogicalPath"])
    critter_messages = parse_message_catalog(
        critter_message_resource.data.decode("cp1252")
    )
    map_format = recipe["mapFormat"]
    viewer = validate_viewer_config(recipe.get("viewer"))
    default_tile_id = int(map_format["defaultTileId"])
    hidden_flag = int(map_format["objectHiddenFlag"])
    no_block_flag = int(map_format["objectNoBlockFlag"])
    multihex_flag = int(map_format["objectMultihexFlag"])

    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(
        tempfile.mkdtemp(prefix=f".{output_root.name}-", dir=output_root.parent)
    )
    tile_artifacts: dict[int, dict[str, Any]] = {}
    sprite_artifacts: dict[str, dict[str, Any]] = {}
    critter_profiles: dict[str, dict[str, Any]] = {}

    def ensure_tile_artifact(tile_id: int) -> dict[str, Any]:
        artifact = tile_artifacts.get(tile_id)
        if artifact is not None:
            return artifact
        if not 0 <= tile_id < len(tile_names):
            raise Fo1ProfileError(f"Fallout tile-art ID exceeds tiles.lst: {tile_id}")
        filename = tile_names[tile_id].split(" ", 1)[0].strip()
        if not filename:
            raise Fo1ProfileError(f"Fallout tile-art ID has no filename: {tile_id}")
        resource = resolver.read(f"art\\tiles\\{filename}")
        decoded = decode_frm(resource.data, colors)
        source_frame = decoded["directions"][0]["frames"][0]["image"]
        image = unproject_floor(
            source_frame,
            int(runtime_profile["generationAdaptation"]["unprojectedFloorTextureSizePixels"]),
        )
        relative = f"assets/tiles/{tile_id:04d}-{resource.sha256[:PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_16]}.png"
        artifact = {
            "id": tile_id,
            "filename": filename,
            "source": resource.source,
            "sourceSha256": resource.sha256,
            "sourceWidth": source_frame.width,
            "sourceHeight": source_frame.height,
            **save_image(image, staging / relative, relative),
        }
        tile_artifacts[tile_id] = artifact
        return artifact

    def ensure_sprite_artifact(
        logical_path: str,
        rotation: int,
        frame_index: int,
    ) -> dict[str, Any]:
        resource = resolver.read(logical_path)
        key = f"{resource.sha256}:{rotation}:{frame_index}"
        artifact_id = hashlib.sha256(key.encode("ascii")).hexdigest()[:PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_20]
        artifact = sprite_artifacts.get(artifact_id)
        if artifact is not None:
            return artifact
        decoded = decode_frm(resource.data, colors)
        if not 0 <= rotation < len(decoded["directions"]):
            raise Fo1ProfileError(
                f"Fallout sprite rotation exceeds FRM: {logical_path}/{rotation}"
            )
        frames = decoded["directions"][rotation]["frames"]
        if not 0 <= frame_index < len(frames):
            raise Fo1ProfileError(
                f"Fallout sprite frame exceeds FRM: {logical_path}/{frame_index}"
            )
        frame = frames[frame_index]
        relative = f"assets/sprites/{artifact_id}.png"
        artifact = {
            "id": artifact_id,
            "logicalPath": logical_path,
            "source": resource.source,
            "sourceSha256": resource.sha256,
            "rotation": rotation,
            "frame": frame_index,
            "frameOffset": [frame["x"], frame["y"]],
            "averageOpaqueColor": average_opaque_rgba(
                frame["image"],
                float(viewer["wallGeometry"]["sourceAlphaThreshold"]),
            ),
            **save_image(frame["image"], staging / relative, relative),
        }
        sprite_artifacts[artifact_id] = artifact
        return artifact

    player_artifacts: dict[int, str] = {}
    player_logical_path = source["playerArtLogicalPath"].replace("/", "\\")
    for rotation in sorted(
        {
            int(row["entry"]["rotation"])
            for row in campaign["maps"]
        }
    ):
        player_artifacts[rotation] = ensure_sprite_artifact(
            player_logical_path,
            rotation,
            0,
        )["id"]

    map_rows: list[dict[str, Any]] = []
    try:
        campaign_root = campaign_path.parent
        for catalog_row in campaign["maps"]:
            map_id = str(catalog_row["id"])
            map_transport_path = resolve_child(campaign_root, str(catalog_row["path"]))
            if sha256_path(map_transport_path) != catalog_row["sha256"]:
                raise Fo1ProfileError(f"Fallout map transport hash drift: {map_id}")
            transport = read_json(map_transport_path)
            if (
                transport.get("schema") != MAP_TRANSPORT_SCHEMA
                or transport.get("status") != "transported"
                or transport.get("id") != map_id
            ):
                raise Fo1ProfileError(f"unexpected Fallout map transport: {map_id}")
            objects_by_elevation = {
                int(row["elevation"]): row["objects"]
                for row in transport["objectGraph"]["objects"]["elevations"]
            }
            elevations: list[dict[str, Any]] = []
            map_sprite_placements = 0
            map_skipped_sprites = 0
            map_mobs = 0
            map_blockers = 0
            map_doors = 0
            map_wall_objects = 0
            map_wall_hexes = 0
            map_wall_components = 0
            map_wall_boundary_edges = 0
            for layout in transport["layout"]["elevations"]:
                elevation = int(layout["elevation"])
                raw_entries = [int(value) for value in layout["rawEntries"]]
                floor_ids = [value & PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_HEX_0FFF for value in raw_entries]
                roof_ids = [(value >> PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_16) & PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_HEX_0FFF for value in raw_entries]
                for tile_id in sorted(set(floor_ids) | set(roof_ids)):
                    ensure_tile_artifact(tile_id)
                placements: list[dict[str, Any]] = []
                skipped: list[dict[str, Any]] = []
                blockers: list[dict[str, Any]] = []
                mobs: list[dict[str, Any]] = []
                doors: list[dict[str, Any]] = []
                source_objects = objects_by_elevation[elevation]
                wall_topology = build_connected_wall_topology(
                    source_objects,
                    floor_ids,
                    default_tile_id,
                    no_block_flag,
                )
                for obj in source_objects:
                    flags = int(obj["flags"], PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_16)
                    tile = int(obj["tile"])
                    object_type = int(obj["prototype"]["object_type"])
                    if tile >= 0 and not flags & no_block_flag:
                        blockers.append(
                            {
                                "serial": obj["serial"],
                                "tile": tile,
                                "flags": obj["flags"],
                                "multihex": bool(flags & multihex_flag),
                            }
                        )
                    if tile < 0 or not obj.get("artFilename"):
                        skipped.append(
                            {"serial": obj["serial"], "reason": "off-grid-or-no-art"}
                        )
                        continue
                    if flags & hidden_flag:
                        skipped.append(
                            {"serial": obj["serial"], "reason": "OBJECT_HIDDEN"}
                        )
                        continue
                    logical_path = source_sprite_logical_path(obj, map_format)
                    if logical_path is None:
                        skipped.append(
                            {
                                "serial": obj["serial"],
                                "reason": "unsupported-or-unresolved-source-art-state",
                            }
                        )
                        continue
                    artifact = ensure_sprite_artifact(
                        logical_path,
                        int(obj["rotation"]),
                        int(obj["frame"]),
                    )
                    placement = {
                        "serial": obj["serial"],
                        "objectId": obj["id"],
                        "tile": tile,
                        "hex": [tile % PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200, tile // PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200],
                        "worldMeters": hex_center(tile),
                        "rotation": obj["rotation"],
                        "rotationSource": obj["rotationSource"],
                        "pixelOffset": obj["pixelOffset"],
                        "fid": obj["fid"],
                        "pid": obj["pid"],
                        "flags": obj["flags"],
                        "objectType": object_type,
                        "objectTypeName": OBJECT_TYPE_NAMES[object_type],
                        "artFilename": obj["artFilename"],
                        "artifactId": artifact["id"],
                    }
                    placements.append(placement)
                    if obj["prototype"]["subtype_name"] == "door":
                        doors.append(
                            {
                                "serial": obj["serial"],
                                "instanceFlags": obj["instanceFlags"],
                                "instanceValues": obj["instanceValues"],
                            }
                        )
                    if object_type == 1:
                        pid = str(obj["pid"]).lower()
                        profile = critter_profiles.get(pid)
                        if profile is None:
                            prototype_filename = obj["prototype"]["filename"]
                            resource = resolver.read(
                                f"proto\\critters\\{prototype_filename}"
                            )
                            message_number = obj["prototype"]["message_number"]
                            name = critter_messages.get(int(message_number), "")
                            profile = {
                                **parse_critter_pro(resource.data),
                                "pid": pid,
                                "prototypeFilename": prototype_filename,
                                "prototypeSha256": resource.sha256,
                                "messageNumber": message_number,
                                "displayName": name or None,
                                "displayNameSource": (
                                    source["critterMessagesLogicalPath"]
                                    if name
                                    else "source-message-empty-or-absent"
                                ),
                            }
                            critter_profiles[pid] = profile
                        instance = obj["instanceValues"]
                        if len(instance) != PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_11:
                            raise Fo1ProfileError(
                                f"Fallout critter instance field count drifted: {map_id}/{obj['serial']}"
                            )
                        mobs.append(
                            {
                                "serial": obj["serial"],
                                "profileId": pid,
                                "currentHitPoints": instance[PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_8],
                                "currentActionPoints": instance[3],
                                "runtimeAiPacket": instance[PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_5],
                                "runtimeTeam": instance[PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_6],
                            }
                        )
                blocked = {int(row["tile"]) for row in blockers}
                walkable = sum(
                    floor_ids[floor_index_for_hex(tile)] != default_tile_id
                    and tile not in blocked
                    for tile in range(PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_40000)
                )
                elevations.append(
                    {
                        "elevation": elevation,
                        "rawGridSha256": layout["rawSha256"],
                        "floorIds": floor_ids,
                        "roofIds": roof_ids,
                        "placements": placements,
                        "skippedPlacements": skipped,
                        "blockers": blockers,
                        "provisionalWalkableHexes": walkable,
                        "mobs": mobs,
                        "doors": doors,
                        "wallTopology": wall_topology,
                        "coverage": {
                            "topLevelObjects": len(source_objects),
                            "spritePlacements": len(placements),
                            "skippedSpriteObjects": len(skipped),
                            "blockers": len(blockers),
                            "multihexCentralOnly": sum(row["multihex"] for row in blockers),
                            "mobs": len(mobs),
                            "doors": len(doors),
                            "wallObjects": wall_topology["sourceWallObjects"],
                            "wallHexes": wall_topology["coverage"]["occupiedHexes"],
                            "wallComponents": wall_topology["coverage"]["connectedComponents"],
                            "wallBoundaryEdges": wall_topology["coverage"]["boundaryEdges"],
                        },
                    }
                )
                map_sprite_placements += len(placements)
                map_skipped_sprites += len(skipped)
                map_mobs += len(mobs)
                map_blockers += len(blockers)
                map_doors += len(doors)
                map_wall_objects += int(wall_topology["sourceWallObjects"])
                map_wall_hexes += int(wall_topology["coverage"]["occupiedHexes"])
                map_wall_components += int(
                    wall_topology["coverage"]["connectedComponents"]
                )
                map_wall_boundary_edges += int(
                    wall_topology["coverage"]["boundaryEdges"]
                )
            scene = {
                "schema": MAP_PRESENTATION_SCHEMA,
                "status": "prepared-source-reference",
                "id": map_id,
                "source": {
                    "transport": str(catalog_row["path"]),
                    "transportSha256": catalog_row["sha256"],
                    "mapFile": catalog_row["file"],
                    "mapSha256": catalog_row["sourceMapSha256"],
                },
                "header": transport["header"],
                "mapsTxt": transport["mapsTxt"],
                "entry": {
                    **transport["entry"],
                    "worldMeters": hex_center(int(transport["entry"]["tile"])),
                    "playerArtifactId": player_artifacts[
                        int(transport["entry"]["rotation"])
                    ],
                },
                "grid": {
                    "hexWidth": PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200,
                    "hexHeight": PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_200,
                    "floorWidth": PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_100,
                    "floorHeight": PREPARE_FO1_CAMPAIGN_PRESENTATION_COMPILER_CONTRACT_INTEGER_100,
                    "hexFlatToFlatMeters": 1.0,
                    "layout": "fallout-even-column-offset-flat-v1",
                    "defaultTileId": default_tile_id,
                },
                "elevations": elevations,
                "coverage": {
                    "elevations": len(elevations),
                    "spritePlacements": map_sprite_placements,
                    "skippedSpriteObjects": map_skipped_sprites,
                    "mobs": map_mobs,
                    "blockers": map_blockers,
                    "doors": map_doors,
                    "wallObjects": map_wall_objects,
                    "wallHexes": map_wall_hexes,
                    "wallComponents": map_wall_components,
                    "wallBoundaryEdges": map_wall_boundary_edges,
                },
                "promotion": {
                    "transported": True,
                    "sourceReferencePrepared": True,
                    "rendered": False,
                    "interactive": False,
                    "questExecutable": False,
                    "firstPersonReady": False,
                    "openXrAccepted": False,
                },
                "unsupported": recipe["unsupported"],
                "retailOrDerivedAssetsPackaged": False,
            }
            relative = f"maps/{map_id}.json"
            scene_sha256 = write_json(staging / relative, scene)
            map_rows.append(
                {
                    "id": map_id,
                    "file": catalog_row["file"],
                    "path": relative,
                    "sha256": scene_sha256,
                    "elevations": len(elevations),
                    "spritePlacements": map_sprite_placements,
                    "skippedSpriteObjects": map_skipped_sprites,
                    "mobs": map_mobs,
                    "blockers": map_blockers,
                    "doors": map_doors,
                    "wallObjects": map_wall_objects,
                    "wallHexes": map_wall_hexes,
                    "wallComponents": map_wall_components,
                    "wallBoundaryEdges": map_wall_boundary_edges,
                    "entry": scene["entry"],
                }
            )

        presentation = {
            "schema": PRESENTATION_SCHEMA,
            "status": "prepared-source-reference-not-rendered",
            "recipe": {
                "id": recipe["id"],
                "sha256": sha256_path(recipe_path),
            },
            "runtimeProfile": runtime_profile,
            "source": {
                "campaignTransport": str(campaign_path),
                "campaignTransportSha256": campaign_sha256.lower(),
                "fallout2MasterSha256": sha256_path(fallout2_master),
                "fallout2CritterSha256": sha256_path(fallout2_critter),
                "paletteSha256": sha256_path(palette_path),
                "critterMessagesSha256": critter_message_resource.sha256,
            },
            "presentation": {
                "source": "owned Fallout FRM frames and MAP placements",
                "pixelsPerMeter": runtime_profile["scenePresentation"]["sourceSprites"]["pixelsPerMeter"],
                "groundAnchorMeters": runtime_profile["scenePresentation"]["sourceSprites"]["groundAnchorMeters"],
                "staticWorldYawDegrees": runtime_profile["generationAdaptation"]["staticWorldSpriteYawDegrees"],
                "floorPatchCenters": {
                    "storage": "derived-not-repeated",
                    "algorithm": "fallout-100x100-isometric-floor-grid-v1"
                },
                "tileArtifacts": [tile_artifacts[key] for key in sorted(tile_artifacts)],
                "spriteArtifacts": [
                    sprite_artifacts[key] for key in sorted(sprite_artifacts)
                ],
                "playerArtifactsByRotation": {
                    str(key): value for key, value in sorted(player_artifacts.items())
                },
                "critterProfiles": [
                    critter_profiles[key] for key in sorted(critter_profiles)
                ],
            },
            "viewer": viewer,
            "coverage": {
                "maps": len(map_rows),
                "elevations": sum(row["elevations"] for row in map_rows),
                "tileArtifacts": len(tile_artifacts),
                "spriteArtifacts": len(sprite_artifacts),
                "spritePlacements": sum(row["spritePlacements"] for row in map_rows),
                "skippedSpriteObjects": sum(row["skippedSpriteObjects"] for row in map_rows),
                "mobs": sum(row["mobs"] for row in map_rows),
                "blockers": sum(row["blockers"] for row in map_rows),
                "doors": sum(row["doors"] for row in map_rows),
                "wallObjects": sum(row["wallObjects"] for row in map_rows),
                "wallHexes": sum(row["wallHexes"] for row in map_rows),
                "wallComponents": sum(row["wallComponents"] for row in map_rows),
                "wallBoundaryEdges": sum(row["wallBoundaryEdges"] for row in map_rows),
                "critterProfiles": len(critter_profiles),
                "objectTypes": dict(
                    sorted(
                        Counter(
                            artifact["logicalPath"].split("\\")[1]
                            for artifact in sprite_artifacts.values()
                        ).items()
                    )
                ),
            },
            "maps": map_rows,
            "supported": recipe["supported"],
            "unsupported": recipe["unsupported"],
            "promotion": {
                "transportedMaps": len(map_rows),
                "sourceReferencePreparedMaps": len(map_rows),
                "renderedMaps": 0,
                "interactiveMaps": 0,
                "questExecutableMaps": 0,
                "firstPersonReadyMaps": 0,
                "openXrAcceptedMaps": 0,
            },
            "retailOrDerivedAssetsPackaged": False,
        }
        presentation_sha256 = write_json(
            staging / "campaign-presentation.json",
            presentation,
        )
        manifest = {
            "schema": CACHE_SCHEMA,
            "status": "prepared-owned-data",
            "presentation": str((output_root / "campaign-presentation.json").resolve()),
            "presentationSha256": presentation_sha256,
            **presentation["coverage"],
            "retailOrDerivedAssetsPackaged": False,
        }
        write_json(staging / "campaign-presentation-cache.json", manifest)
        os.replace(staging, output_root)
        return manifest
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--campaign", type=Path, required=True)
    parser.add_argument("--campaign-sha256", required=True)
    parser.add_argument("--ettu-root", type=Path, required=True)
    parser.add_argument("--fallout2-master", type=Path, required=True)
    parser.add_argument("--fallout2-critter", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = prepare(
            args.recipe,
            args.campaign,
            args.campaign_sha256,
            args.ettu_root,
            args.fallout2_master,
            args.fallout2_critter,
            args.output_root,
        )
    except Exception as error:
        print(f"OPENNV_FO1_CAMPAIGN_PRESENTATION_ERROR {error}")
        return 2
    print("OPENNV_FO1_CAMPAIGN_PRESENTATION " + json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
