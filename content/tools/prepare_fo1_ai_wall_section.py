#!/usr/bin/env python3
"""Compose one complete Fallout wall section for modern image-to-3D inference.

The source MAP/FRM composition remains canonical.  This tool deliberately
groups adjacent wall slices before inference so a 3D model never receives the
individual "board" fragments that are only meaningful after retail draw-order
composition.
"""

from __future__ import annotations

import argparse
import json
import os
from collections import deque
from pathlib import Path

from PIL import Image

from fo1_profile import sha256_path
from prepare_fo1_hex_scene import classic_hex_screen
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_HEX_00000008 = 0x00000008
PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_16 = 16
PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_200 = 200
PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_256 = 256
PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_40000 = 40000



RECIPE_SCHEMA = "opennv-fo1-ai-wall-reconstruction-recipe/v1"
OUTPUT_SCHEMA = "opennv-fo1-ai-wall-section/v1"


def read_json(path: Path) -> dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def coordinate(tile: int) -> tuple[int, int]:
    if not 0 <= tile < PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_40000:
        raise ValueError(f"Fallout wall tile escapes the 200x200 grid: {tile}")
    return tile % PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_200, tile // PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_200


def neighbors(tile: int) -> tuple[int, ...]:
    x, y = coordinate(tile)
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
        if 0 <= target_x < PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_200 and 0 <= target_y < PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_200:
            values.append(target_y * PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_200 + target_x)
    return tuple(values)


def cube(tile: int) -> tuple[int, int, int]:
    x, y = coordinate(tile)
    axial_y = y - (x + (x & 1)) // 2
    return x, -x - axial_y, axial_y


def distance(first: int, second: int) -> int:
    first_cube = cube(first)
    second_cube = cube(second)
    return max(abs(first_cube[index] - second_cube[index]) for index in range(3))


def components(
    placements: list[dict[str, object]],
    adjacency_distance: int,
) -> list[list[dict[str, object]]]:
    if adjacency_distance != 1:
        raise ValueError("The v1 Fallout wall-section contract supports exact edge adjacency only")
    by_tile: dict[int, list[int]] = {}
    for index, placement in enumerate(placements):
        by_tile.setdefault(int(placement["tile"]), []).append(index)
    visited: set[int] = set()
    result: list[list[dict[str, object]]] = []
    for start in range(len(placements)):
        if start in visited:
            continue
        visited.add(start)
        queue = deque([start])
        component: list[dict[str, object]] = []
        while queue:
            index = queue.popleft()
            placement = placements[index]
            component.append(placement)
            linked_tiles = (int(placement["tile"]), *neighbors(int(placement["tile"])))
            for tile in linked_tiles:
                for linked in by_tile.get(tile, []):
                    if linked not in visited:
                        visited.add(linked)
                        queue.append(linked)
        result.append(component)
    return result


def paste(canvas: Image.Image, image: Image.Image, position: tuple[int, int]) -> None:
    canvas.alpha_composite(image, position)


def prepare(
    scene_path: Path,
    recipe_path: Path,
    output_root: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise ValueError(f"refusing to overwrite Fallout AI wall section: {output_root}")
    scene = read_json(scene_path)
    recipe = read_json(recipe_path)
    if scene.get("schema") != "opennv-fo1-hex-scene/v1":
        raise ValueError("unexpected Fallout hex-scene input")
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise ValueError("unexpected Fallout AI wall-section recipe")

    selection = recipe["sectionSelection"]
    composite_recipe = recipe["sourceComposite"]
    if selection["focus"] not in {"entry", "door"}:
        raise ValueError("Fallout AI wall-section focus is invalid")
    minimum = int(selection["minimumPlacements"])
    maximum = int(selection["maximumPlacements"])
    padding = int(composite_recipe["paddingPixels"])
    target_long_edge = int(composite_recipe["targetLongEdgePixels"])
    if (
        minimum < 2
        or maximum < minimum
        or padding < 0
        or target_long_edge < PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_256
        or composite_recipe["resampling"] != "nearest"
        or composite_recipe["background"] != "transparent"
        or composite_recipe["preserveOriginalAlpha"] is not True
    ):
        raise ValueError("Fallout AI wall-section composition contract is invalid")

    presentation = scene["grid"]["threeDPresentation"]["ownedPresentation"]["composition"]
    relief = presentation["frmRelief"]
    requested_profile = str(selection["profile"])
    candidates = [
        row for row in relief["placements"] if row["profile"] == requested_profile
    ]
    grouped = [
        rows
        for rows in components(candidates, int(selection["adjacencyDistanceHexes"]))
        if minimum <= len(rows) <= maximum
    ]
    if not grouped:
        raise ValueError("no Fallout wall component satisfies the recipe")
    focus_tile = int(scene[selection["focus"]]["tile"] if selection["focus"] == "entry" else scene["door"]["source"]["tile"])
    if selection["ranking"] != "nearest-focus-then-largest-then-lowest-source-serial":
        raise ValueError("Fallout AI wall-section ranking contract is invalid")
    grouped.sort(
        key=lambda rows: (
            min(distance(int(row["tile"]), focus_tile) for row in rows),
            -len(rows),
            min(int(row["serial"]) for row in rows),
        )
    )
    selected = grouped[0]
    selected_serials = {int(row["serial"]) for row in selected}

    sprite_source = scene["objectSprites"]
    sprite_artifacts = {row["id"]: row for row in sprite_source["artifacts"]}
    sprite_placements = {
        int(row["serial"]): row for row in sprite_source["placements"]
    }
    draw_rows: list[tuple[int, int, int, int, Image.Image, tuple[int, int], dict[str, object]]] = []
    source_artifacts: dict[str, dict[str, object]] = {}
    for relief_row in selected:
        serial = int(relief_row["serial"])
        sprite = sprite_placements.get(serial)
        if sprite is None:
            raise ValueError(f"Fallout wall serial has no exact sprite placement: {serial}")
        artifact = sprite_artifacts[sprite["artifactId"]]
        image_path = Path(artifact["png"])
        if sha256_path(image_path) != artifact["pngSha256"]:
            raise ValueError(f"Fallout wall source PNG hash drift: {image_path}")
        image = Image.open(image_path).convert("RGBA")
        screen = classic_hex_screen(int(sprite["tile"]))
        pixel = sprite["pixelOffset"]
        frame = artifact["frameOffset"]
        top_left = (
            screen[0] - image.width // 2 + int(pixel[0]) + int(frame[0]),
            screen[1] - image.height + int(pixel[1]) + int(frame[1]),
        )
        flags = int(sprite["flags"], PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_INTEGER_16)
        draw_rows.append(
            (
                0 if flags & PREPARE_FO1_AI_WALL_SECTION_COMPILER_CONTRACT_HEX_00000008 else 1,
                screen[1],
                screen[0],
                serial,
                image,
                top_left,
                artifact,
            )
        )
        source_artifacts[str(artifact["id"])] = {
            "id": artifact["id"],
            "png": str(image_path.resolve()),
            "pngSha256": artifact["pngSha256"],
            "sourceSha256": artifact["sourceSha256"],
            "width": artifact["width"],
            "height": artifact["height"],
            "frameOffset": artifact["frameOffset"],
        }

    alpha_bounds = []
    for *_, image, top_left, _artifact in draw_rows:
        bounds = image.getchannel("A").getbbox()
        if bounds is not None:
            alpha_bounds.append(
                (
                    top_left[0] + bounds[0],
                    top_left[1] + bounds[1],
                    top_left[0] + bounds[2],
                    top_left[1] + bounds[3],
                )
            )
    if not alpha_bounds:
        raise ValueError("Fallout wall section contains no opaque pixels")
    bounds = (
        min(row[0] for row in alpha_bounds) - padding,
        min(row[1] for row in alpha_bounds) - padding,
        max(row[2] for row in alpha_bounds) + padding,
        max(row[3] for row in alpha_bounds) + padding,
    )
    native = Image.new("RGBA", (bounds[2] - bounds[0], bounds[3] - bounds[1]), (0, 0, 0, 0))
    for *_, image, top_left, _artifact in sorted(draw_rows, key=lambda row: row[:4]):
        paste(native, image, (top_left[0] - bounds[0], top_left[1] - bounds[1]))
    opaque_bounds = native.getchannel("A").getbbox()
    if opaque_bounds is None:
        raise ValueError("Fallout wall composite became transparent")

    scale = target_long_edge / max(native.size)
    upscaled_size = (
        max(1, round(native.width * scale)),
        max(1, round(native.height * scale)),
    )
    upscaled = native.resize(upscaled_size, Image.Resampling.NEAREST)
    output_root.mkdir(parents=True)
    native_path = output_root / "entry-wall-source-native.png"
    input_path = output_root / "entry-wall-trellis-input.png"
    native.save(native_path)
    upscaled.save(input_path)

    report = {
        "schema": OUTPUT_SCHEMA,
        "status": "exact-connected-frm-composite-ready-for-local-image-to-3d",
        "source": {
            "scene": str(scene_path.resolve()),
            "sceneSha256": sha256_path(scene_path),
            "recipe": str(recipe_path.resolve()),
            "recipeSha256": sha256_path(recipe_path),
            "map": scene["source"]["map"],
        },
        "selection": {
            "profile": requested_profile,
            "focusTile": focus_tile,
            "minimumDistanceHexes": min(
                distance(int(row["tile"]), focus_tile) for row in selected
            ),
            "placements": len(selected),
            "serials": sorted(selected_serials),
            "tiles": sorted(int(row["tile"]) for row in selected),
            "artFilenames": sorted({str(row["artFilename"]) for row in selected}),
            "candidateComponents": len(grouped),
        },
        "composition": {
            "drawOrder": "Fallout flat-before-normal, then screen-Y, screen-X, source-serial",
            "globalOpaqueBoundsPixels": list(bounds),
            "nativeSizePixels": list(native.size),
            "upscaledSizePixels": list(upscaled.size),
            "artifacts": [source_artifacts[key] for key in sorted(source_artifacts)],
        },
        "generation": recipe["geometryGeneration"],
        "acceptance": recipe["acceptance"],
        "artifacts": {
            "native": {
                "path": str(native_path.resolve()),
                "sha256": sha256_path(native_path),
                "width": native.width,
                "height": native.height,
            },
            "trellisInput": {
                "path": str(input_path.resolve()),
                "sha256": sha256_path(input_path),
                "width": upscaled.width,
                "height": upscaled.height,
            },
        },
    }
    write_json(output_root / "wall-section-manifest.json", report)
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--scene", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    report = prepare(
        args.scene.resolve(),
        args.recipe.resolve(),
        args.output_root.resolve(),
    )
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
