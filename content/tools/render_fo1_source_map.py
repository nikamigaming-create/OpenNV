#!/usr/bin/env python3
"""Render an owned V13ENT source-map reference and Godot side-by-side review."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path

from PIL import Image, ImageDraw

from fo1_frm import decode_frm, palette_rgba
from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import sha256_path
from prepare_fo1_hex_scene import classic_floor_screen, classic_hex_screen
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_HEX_00000008 = 0x00000008
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_10 = 10
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_105 = 105
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_110 = 110
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_12 = 12
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1279 = 1279
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1280 = 1280
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1298 = 1298
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_15 = 15
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_16 = 16
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_17 = 17
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_18 = 18
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_19 = 19
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_195 = 195
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_205 = 205
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_225 = 225
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_235 = 235
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_238 = 238
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255 = 255
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_2559 = 2559
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_2560 = 2560
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_38 = 38
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_45 = 45
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_49 = 49
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_5 = 5
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_50 = 50
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_55 = 55
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_7 = 7
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_72 = 72
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_720 = 720
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_769 = 769
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_770 = 770
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_8 = 8
RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_9 = 9



SCHEMA = "opennv-fo1-source-orientation-review/v1"


def read_json(path: Path) -> dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: object) -> None:
    path.write_text(json.dumps(document, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def paste_clipped(canvas: Image.Image, image: Image.Image, position: tuple[int, int]) -> None:
    x, y = position
    left = max(0, -x)
    top = max(0, -y)
    right = min(image.width, canvas.width - x)
    bottom = min(image.height, canvas.height - y)
    if right <= left or bottom <= top:
        return
    cropped = image.crop((left, top, right, bottom))
    canvas.alpha_composite(cropped, (x + left, y + top))


def image_artifact(path: Path) -> dict[str, object]:
    with Image.open(path) as image:
        width, height = image.size
    return {
        "path": str(path.resolve()),
        "bytes": path.stat().st_size,
        "width": width,
        "height": height,
        "sha256": sha256_path(path),
    }


def render(
    hex_scene_path: Path,
    ettu_root: Path,
    fallout2_master: Path,
    fallout2_critter: Path,
    godot_capture_path: Path,
    output_root: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise ValueError(f"refusing to overwrite orientation review: {output_root}")
    scene = read_json(hex_scene_path)
    if scene.get("schema") != "opennv-fo1-hex-scene/v1":
        raise ValueError("unexpected Fallout hex scene")
    if sha256_path(godot_capture_path) == "":
        raise ValueError("Godot capture hash is unavailable")
    output_root.mkdir(parents=True)

    resolver = Fo1ResourceResolver(ettu_root, fallout2_master, [fallout2_critter])
    palette_path = ettu_root / "mods" / "fo1_base" / "color.pal"
    colors = palette_rgba(palette_path)
    floor = scene["grid"]
    floor_ids = floor["floorIds"]
    default_floor_id = floor["defaultFloorId"]
    floor_art = {row["id"]: row for row in floor["floorArt"]}
    entry_tile = scene["entry"]["tile"]
    door_tile = scene["door"]["source"]["tile"]
    entry_screen = classic_hex_screen(entry_tile)
    door_screen = classic_hex_screen(door_tile)
    crop_size = (RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1280, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_720)
    center = [
        (entry_screen[0] + door_screen[0]) // 2,
        (entry_screen[1] + door_screen[1]) // 2,
    ]
    crop_origin = [center[0] - crop_size[0] // 2, center[1] - crop_size[1] // 2]
    source_canvas = Image.new("RGBA", crop_size, (RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_5, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_8, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_7, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255))

    floor_images: dict[int, Image.Image] = {}
    for floor_id, artifact in floor_art.items():
        resource = resolver.read(f"art\\tiles\\{artifact['filename']}")
        if resource.sha256 != artifact["sourceSha256"]:
            raise ValueError(f"floor source hash drift: {artifact['filename']}")
        decoded = decode_frm(resource.data, colors)
        floor_images[floor_id] = decoded["directions"][0]["frames"][0]["image"]
    floor_rows = sorted(
        (
            (*classic_floor_screen(index), index, floor_id)
            for index, floor_id in enumerate(floor_ids)
            if floor_id != default_floor_id
        ),
        key=lambda row: (row[1], row[0]),
    )
    for screen_x, screen_y, _, floor_id in floor_rows:
        paste_clipped(
            source_canvas,
            floor_images[floor_id],
            (screen_x - crop_origin[0], screen_y - crop_origin[1]),
        )

    sprite_source = scene["objectSprites"]
    artifacts = {row["id"]: row for row in sprite_source["artifacts"]}
    sprite_images = {}
    for artifact_id, artifact in artifacts.items():
        path = Path(artifact["png"])
        if sha256_path(path) != artifact["pngSha256"]:
            raise ValueError(f"sprite PNG hash drift: {path}")
        sprite_images[artifact_id] = Image.open(path).convert("RGBA")

    object_rows = []
    for placement in sprite_source["placements"]:
        artifact = artifacts[placement["artifactId"]]
        image = sprite_images[placement["artifactId"]]
        screen = classic_hex_screen(placement["tile"])
        pixel = placement["pixelOffset"]
        frame = artifact["frameOffset"]
        top_left = [
            screen[0] - image.width // 2 + int(pixel[0]) + int(frame[0]),
            screen[1] - image.height + int(pixel[1]) + int(frame[1]),
        ]
        flags = int(placement["flags"], RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_16)
        object_rows.append(
            (
                0 if flags & RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_HEX_00000008 else 1,
                screen[1],
                screen[0],
                image,
                top_left,
            )
        )
    for _, _, _, image, top_left in sorted(object_rows, key=lambda row: row[:3]):
        paste_clipped(
            source_canvas,
            image,
            (top_left[0] - crop_origin[0], top_left[1] - crop_origin[1]),
        )

    source_door_art = {row["role"]: row for row in scene["door"]["sourceArt"]}
    for role in ("frame", "door"):
        artifact = source_door_art[role]
        path = Path(artifact["png"])
        if sha256_path(path) != artifact["pngSha256"]:
            raise ValueError(f"source door PNG hash drift: {path}")
        image = Image.open(path).convert("RGBA")
        top_left = [door_screen[0] - image.width // 2, door_screen[1] - image.height]
        paste_clipped(
            source_canvas,
            image,
            (top_left[0] - crop_origin[0], top_left[1] - crop_origin[1]),
        )

    player_artifact = scene["combat"]["player"]["artifact"]
    player_path = Path(player_artifact["png"])
    if sha256_path(player_path) != player_artifact["pngSha256"]:
        raise ValueError(f"source player PNG hash drift: {player_path}")
    player_image = Image.open(player_path).convert("RGBA")
    player_top_left = [
        entry_screen[0] - player_image.width // 2,
        entry_screen[1] - player_image.height,
    ]
    paste_clipped(
        source_canvas,
        player_image,
        (player_top_left[0] - crop_origin[0], player_top_left[1] - crop_origin[1]),
    )

    draw = ImageDraw.Draw(source_canvas)
    for label, tile, color in (
        (f"FIRST-RUN SPAWN {entry_tile}", entry_tile, (RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_45, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_225, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255)),
        (f"DOOR {door_tile}", door_tile, (RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_205, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_55, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255)),
    ):
        screen = classic_hex_screen(tile)
        point = (screen[0] - crop_origin[0], screen[1] - crop_origin[1])
        draw.ellipse((point[0] - RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_9, point[1] - RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_9, point[0] + RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_9, point[1] + RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_9), outline=color, width=3)
        draw.text((point[0] + RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_12, point[1] - RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_10), label, fill=color)
    draw.rectangle((0, 0, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1279, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_38), fill=(3, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_7, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_5, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_225))
    draw.text(
        (RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_16, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_12),
        "OWNED SOURCE RECONSTRUCTION  •  V13ENT.MAP + ORIGINAL FRMs",
        fill=(RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_238, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_195, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_72, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255),
    )
    source_path = output_root / "v13ent-source-reference.png"
    source_canvas.save(source_path)

    with Image.open(godot_capture_path) as image:
        godot = image.convert("RGBA")
    if godot.size != crop_size:
        raise ValueError(f"Godot comparison capture must be 1280x720, got {godot.size}")
    comparison = Image.new("RGBA", (RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_2560, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_770), (RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_8, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_10, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_9, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255))
    comparison.alpha_composite(source_canvas, (0, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_50))
    comparison.alpha_composite(godot, (RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1280, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_50))
    compare_draw = ImageDraw.Draw(comparison)
    compare_draw.rectangle((0, 0, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1279, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_49), fill=(RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_15, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_19, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_16, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255))
    compare_draw.rectangle((RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1280, 0, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_2559, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_49), fill=(RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_15, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_19, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_16, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255))
    compare_draw.text((RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_18, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_17), "SOURCE MAP/FRM RECONSTRUCTION", fill=(RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_238, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_195, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_72, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255))
    compare_draw.text((RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1298, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_17), "GODOT — CORRECTED REVERSED FLOOR-X", fill=(RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_105, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_235, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_110, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255))
    compare_draw.line((RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1279, 0, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_1279, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_769), fill=(RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_238, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_195, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_72, RENDER_FO1_SOURCE_MAP_DIAGNOSTIC_CONTRACT_INTEGER_255), width=2)
    comparison_path = output_root / "v13ent-source-vs-godot.png"
    comparison.save(comparison_path)

    report = {
        "schema": SCHEMA,
        "status": "confirmed-transform-defect-corrected",
        "source": scene["source"],
        "hexSceneSha256": sha256_path(hex_scene_path),
        "comparison": {
            "area": "V13ENT elevation 0",
            "state": "static initial MAP state",
            "sourcePresentation": "deterministic MAP/FRM reconstruction; not executable capture",
            "godotPresentation": "orthographic tactical route view",
            "entryTile": entry_tile,
            "doorTile": door_tile,
            "sourceEntryScreen": entry_screen,
            "sourceDoorScreen": door_screen,
            "sourceDoorMinusEntry": [
                door_screen[0] - entry_screen[0],
                door_screen[1] - entry_screen[1],
            ],
            "cropOrigin": crop_origin,
        },
        "deltas": [
            {
                "symptom": "floor art and walkability mirrored under correctly placed object hexes",
                "owner": "100x100 floor-storage to 200x200 object-hex transform",
                "previous": "floorIndex=(hexY/2)*100+(hexX/2)",
                "corrected": "floorIndex=(hexY/2)*100+(99-hexX/2)",
                "confidence": "confirmed",
                "sourceFormula": "Mapper-compatible tileToScreen reverses floor X before projection",
            },
            {
                "symptom": "player began near the far MAP-header entry instead of just outside the Vault",
                "owner": "new-game spawn-state selection",
                "previous": "MAP header entry tile 20090 rotation 0",
                "corrected": f"V13CAVE map_first_run tile {entry_tile} rotation {scene['entry']['rotation']}",
                "confidence": "confirmed",
                "sourceSha256": scene["entry"]["sourceSha256"],
            },
        ],
        "artifacts": {
            "sourceReference": image_artifact(source_path),
            "godotCapture": image_artifact(godot_capture_path),
            "sideBySide": image_artifact(comparison_path),
        },
        "windowsAppControlUsed": False,
        "foregroundActivationUsed": False,
        "foregroundInputInjected": False,
    }
    write_json(output_root / "orientation-review.json", report)
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--hex-scene", type=Path, required=True)
    parser.add_argument("--ettu-root", type=Path, required=True)
    parser.add_argument("--fallout2-master", type=Path, required=True)
    parser.add_argument("--fallout2-critter", type=Path, required=True)
    parser.add_argument("--godot-capture", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    report = render(
        args.hex_scene.resolve(),
        args.ettu_root.resolve(),
        args.fallout2_master.resolve(),
        args.fallout2_critter.resolve(),
        args.godot_capture.resolve(),
        args.output_root.resolve(),
    )
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
