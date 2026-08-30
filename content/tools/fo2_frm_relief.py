"""Derive deterministic alpha-island molded contracts from verified owned FRM PNGs."""

from __future__ import annotations

from collections import deque
from pathlib import Path
from typing import Any

from PIL import Image

from fo1_profile import Fo1ProfileError
from plugin_stack import file_sha256
from prepare_fo1_hex_scene import _relief_normal_map


RELIEF_SCHEMA = "opennv-fo2-frm-alpha-relief/v3"
RELIEF_MODE = "exact-frm-alpha-island-molded-relief-v2"
RGB_VALUE_START = 2
RGB_VALUE_END = 5
OPAQUE_THRESHOLD = 0
MASK_OFF = 0
MASK_ON = 255
LUMA_RED_WEIGHT = 0.2126
LUMA_GREEN_WEIGHT = 0.7152
LUMA_BLUE_WEIGHT = 0.0722
NEIGHBORS = ((-1, 0), (1, 0), (0, -1), (0, 1))


def _solid_mask(
    image: Image.Image,
) -> Image.Image:
    alpha = image.getchannel("A")
    source = alpha.load()
    mask = Image.new("L", image.size, MASK_OFF)
    output = mask.load()
    for y in range(image.height):
        for x in range(image.width):
            if source[x, y] > OPAQUE_THRESHOLD:
                output[x, y] = MASK_ON
    return mask


def _islands(mask: Image.Image) -> list[dict[str, Any]]:
    pixels = mask.load()
    visited: set[tuple[int, int]] = set()
    result: list[dict[str, Any]] = []
    for start_y in range(mask.height):
        for start_x in range(mask.width):
            start = (start_x, start_y)
            if pixels[start_x, start_y] == MASK_OFF or start in visited:
                continue
            pending = [start]
            visited.add(start)
            points: list[tuple[int, int]] = []
            while pending:
                x, y = pending.pop()
                points.append((x, y))
                for offset_x, offset_y in NEIGHBORS:
                    neighbor = (x + offset_x, y + offset_y)
                    if (
                        0 <= neighbor[0] < mask.width
                        and 0 <= neighbor[1] < mask.height
                        and pixels[neighbor[0], neighbor[1]] != MASK_OFF
                        and neighbor not in visited
                    ):
                        visited.add(neighbor)
                        pending.append(neighbor)
            result.append(
                {
                    "opaquePixels": len(points),
                    "boundsPixels": [
                        min(point[0] for point in points),
                        min(point[1] for point in points),
                        max(point[0] for point in points) + 1,
                        max(point[1] for point in points) + 1,
                    ],
                }
            )
    return sorted(
        result,
        key=lambda row: (
            -int(row["opaquePixels"]),
            tuple(int(value) for value in row["boundsPixels"]),
        ),
    )


def _depth_field(
    image: Image.Image,
    mask: Image.Image,
    recipe: dict[str, Any],
) -> Image.Image:
    """Bake a source-derived normalized bulge without filling transparent pixels."""

    profile = recipe["depthField"]
    maximum_distance = int(profile["maximumInteriorDistancePixels"])
    silhouette_weight = float(profile["silhouetteWeight"])
    luma_weight = float(profile["lumaWeight"])
    mask_pixels = mask.load()
    distance = [[0 for _ in range(mask.width)] for _ in range(mask.height)]
    pending: deque[tuple[int, int]] = deque()
    for y in range(mask.height):
        for x in range(mask.width):
            if mask_pixels[x, y] == MASK_OFF:
                continue
            if any(
                x + offset_x < 0
                or x + offset_x >= mask.width
                or y + offset_y < 0
                or y + offset_y >= mask.height
                or mask_pixels[x + offset_x, y + offset_y] == MASK_OFF
                for offset_x, offset_y in NEIGHBORS
            ):
                distance[y][x] = 1
                pending.append((x, y))
    while pending:
        x, y = pending.popleft()
        next_distance = min(distance[y][x] + 1, maximum_distance)
        for offset_x, offset_y in NEIGHBORS:
            neighbor_x = x + offset_x
            neighbor_y = y + offset_y
            if (
                0 <= neighbor_x < mask.width
                and 0 <= neighbor_y < mask.height
                and mask_pixels[neighbor_x, neighbor_y] != MASK_OFF
                and distance[neighbor_y][neighbor_x] == 0
            ):
                distance[neighbor_y][neighbor_x] = next_distance
                pending.append((neighbor_x, neighbor_y))

    rgba = image.convert("RGBA")
    source = rgba.load()
    output = Image.new("L", image.size, MASK_OFF)
    depth_pixels = output.load()
    for y in range(image.height):
        for x in range(image.width):
            if mask_pixels[x, y] == MASK_OFF:
                continue
            red, green, blue, _ = source[x, y]
            luma = (
                red * LUMA_RED_WEIGHT
                + green * LUMA_GREEN_WEIGHT
                + blue * LUMA_BLUE_WEIGHT
            ) / MASK_ON
            silhouette = min(distance[y][x], maximum_distance) / maximum_distance
            depth_pixels[x, y] = round(
                MASK_ON * (silhouette * silhouette_weight + luma * luma_weight)
            )
    return output


def _save_derived(
    staging: Path,
    relative: Path,
    image: Image.Image,
) -> dict[str, Any]:
    path = staging / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=False)
    return {
        "file": relative.as_posix(),
        "bytes": path.stat().st_size,
        "sha256": file_sha256(path),
    }


def derive_relief(
    staging: Path,
    artifact: dict[str, Any],
    recipe: dict[str, Any],
    *,
    output_folder: str,
) -> dict[str, Any]:
    source_path = staging / artifact["png"]
    image = Image.open(source_path).convert("RGBA")
    normal = _relief_normal_map(
        image,
        float(recipe["normalBlurRadiusPixels"]),
        float(recipe["normalLumaWeight"]),
        float(recipe["normalSilhouetteWeight"]),
        float(recipe["normalStrength"]),
    )

    opaque = [
        (x, y, red, green, blue)
        for y in range(image.height)
        for x in range(image.width)
        for red, green, blue, alpha in [image.getpixel((x, y))]
        if alpha > OPAQUE_THRESHOLD
    ]
    if not opaque:
        raise Fo1ProfileError(
            f"Fallout 2 FRM alpha has no opaque pixels: {artifact['logicalPath']}"
        )
    mask = _solid_mask(image)
    islands = _islands(mask)
    if not islands:
        raise Fo1ProfileError(
            f"Fallout 2 FRM alpha has no solid relief island: {artifact['logicalPath']}"
        )
    depth = _depth_field(image, mask, recipe)
    folder = Path("assets") / output_folder
    normal_output = _save_derived(
        staging,
        folder / f"{artifact['id']}-normal.png",
        normal,
    )
    mask_output = _save_derived(
        staging,
        folder / f"{artifact['id']}-solid-mask.png",
        mask,
    )
    depth_output = _save_derived(
        staging,
        folder / f"{artifact['id']}-depth.png",
        depth,
    )

    bounds = image.getchannel("A").getbbox()
    average = [
        round(sum(pixel[channel] for pixel in opaque) / len(opaque))
        for channel in range(RGB_VALUE_START, RGB_VALUE_END)
    ]
    result = {
        "schema": RELIEF_SCHEMA,
        "mode": RELIEF_MODE,
        "sourcePngSha256": artifact["pngSha256"],
        "normalPng": normal_output["file"],
        "normalPngBytes": normal_output["bytes"],
        "normalPngSha256": normal_output["sha256"],
        "solidMaskPng": mask_output["file"],
        "solidMaskPngBytes": mask_output["bytes"],
        "solidMaskPngSha256": mask_output["sha256"],
        "depthPng": depth_output["file"],
        "depthPngBytes": depth_output["bytes"],
        "depthPngSha256": depth_output["sha256"],
        "opaqueBoundsPixels": list(bounds) if bounds is not None else [],
        "sourceOpaquePixels": len(opaque),
        "solidOpaquePixels": sum(int(row["opaquePixels"]) for row in islands),
        "averageOpaqueRgb": average,
        "islands": islands,
        "islandCount": len(islands),
        "depthField": {
            "mode": "owned-alpha-distance-and-luma-normalized-v1",
            "maximumInteriorDistancePixels": int(
                recipe["depthField"]["maximumInteriorDistancePixels"]
            ),
            "silhouetteWeight": float(recipe["depthField"]["silhouetteWeight"]),
            "lumaWeight": float(recipe["depthField"]["lumaWeight"]),
            "backDepthFraction": float(recipe["depthField"]["backDepthFraction"]),
        },
        "depthAuthority": (
            "owned FRM alpha/luma define exact molded cells and normalized depth; "
            "versioned role cap supplies only otherwise unknowable thickness"
        ),
    }
    return result
