"""Decode owned Fallout FRM frames into a disposable PNG preview cache."""

from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

from PIL import Image, ImageDraw

from dat2_archive import Dat2Archive
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
FO1_FRM_FORMAT_CONTRACT_HEX_0A = 0x0A
FO1_FRM_FORMAT_CONTRACT_HEX_16 = 0x16
FO1_FRM_FORMAT_CONTRACT_HEX_22 = 0x22
FO1_FRM_FORMAT_CONTRACT_HEX_3A = 0x3A
FO1_FRM_FORMAT_CONTRACT_HEX_3E = 0x3E
FO1_FRM_FORMAT_CONTRACT_INTEGER_10 = 10
FO1_FRM_FORMAT_CONTRACT_INTEGER_12 = 12
FO1_FRM_FORMAT_CONTRACT_INTEGER_155 = 155
FO1_FRM_FORMAT_CONTRACT_INTEGER_20 = 20
FO1_FRM_FORMAT_CONTRACT_INTEGER_205 = 205
FO1_FRM_FORMAT_CONTRACT_INTEGER_22 = 22
FO1_FRM_FORMAT_CONTRACT_INTEGER_220 = 220
FO1_FRM_FORMAT_CONTRACT_INTEGER_24 = 24
FO1_FRM_FORMAT_CONTRACT_INTEGER_255 = 255
FO1_FRM_FORMAT_CONTRACT_INTEGER_256 = 256
FO1_FRM_FORMAT_CONTRACT_INTEGER_260 = 260
FO1_FRM_FORMAT_CONTRACT_INTEGER_3 = 3
FO1_FRM_FORMAT_CONTRACT_INTEGER_300 = 300
FO1_FRM_FORMAT_CONTRACT_INTEGER_320 = 320
FO1_FRM_FORMAT_CONTRACT_INTEGER_340 = 340
FO1_FRM_FORMAT_CONTRACT_INTEGER_4 = 4
FO1_FRM_FORMAT_CONTRACT_INTEGER_6 = 6
FO1_FRM_FORMAT_CONTRACT_INTEGER_63 = 63
FO1_FRM_FORMAT_CONTRACT_INTEGER_768 = 768
FO1_FRM_FORMAT_CONTRACT_INTEGER_8 = 8

SUPPORTED_FRM_VERSIONS = frozenset(
    (FO1_FRM_FORMAT_CONTRACT_INTEGER_3, FO1_FRM_FORMAT_CONTRACT_INTEGER_4)
)



def palette_rgba_bytes(data: bytes) -> list[tuple[int, int, int, int]]:
    if len(data) < FO1_FRM_FORMAT_CONTRACT_INTEGER_768:
        raise ValueError("Fallout palette requires at least 768 bytes")
    values = data[:FO1_FRM_FORMAT_CONTRACT_INTEGER_768]
    colors = []
    for index in range(FO1_FRM_FORMAT_CONTRACT_INTEGER_256):
        r, g, b = values[index * 3 : index * 3 + 3]
        # Fallout stores valid palette channels as six-bit values. COLOR.PAL
        # also contains invalid/sentinel entries above 63, so deciding whether
        # to scale from the global maximum incorrectly darkens every valid UI
        # and world color. The retail color loader rejects those entries.
        if r <= FO1_FRM_FORMAT_CONTRACT_INTEGER_63 and g <= FO1_FRM_FORMAT_CONTRACT_INTEGER_63 and b <= FO1_FRM_FORMAT_CONTRACT_INTEGER_63:
            red, green, blue = r * 4, g * 4, b * 4
        else:
            red, green, blue = 0, 0, 0
        colors.append((red, green, blue, 0 if index == 0 else FO1_FRM_FORMAT_CONTRACT_INTEGER_255))
    return colors


def palette_rgba(path: Path) -> list[tuple[int, int, int, int]]:
    return palette_rgba_bytes(path.read_bytes())


def decode_frm_frame(
    data: bytes,
    colors: list[tuple[int, int, int, int]],
    rotation: int,
    frame_index: int,
) -> dict[str, object]:
    """Decode one explicitly admitted frame without materializing other frames."""
    if len(colors) != FO1_FRM_FORMAT_CONTRACT_INTEGER_256:
        raise ValueError("Fallout FRM decoding requires exactly 256 palette colors")
    if len(data) < FO1_FRM_FORMAT_CONTRACT_HEX_3E:
        raise ValueError("FRM header is truncated")
    version, fps, action_frame, frame_count = struct.unpack_from(">IHHH", data, 0)
    if version not in SUPPORTED_FRM_VERSIONS or frame_count <= 0:
        raise ValueError(f"unsupported FRM header: version={version} frames={frame_count}")
    if not 0 <= rotation < FO1_FRM_FORMAT_CONTRACT_INTEGER_6:
        raise ValueError(f"FRM rotation is outside 0..5: {rotation}")
    if not 0 <= frame_index < frame_count:
        raise ValueError(f"FRM frame is outside 0..{frame_count - 1}: {frame_index}")
    x_offsets = struct.unpack_from(">6h", data, FO1_FRM_FORMAT_CONTRACT_HEX_0A)
    y_offsets = struct.unpack_from(">6h", data, FO1_FRM_FORMAT_CONTRACT_HEX_16)
    data_offsets = struct.unpack_from(">6I", data, FO1_FRM_FORMAT_CONTRACT_HEX_22)
    frame_area_size = struct.unpack_from(">I", data, FO1_FRM_FORMAT_CONTRACT_HEX_3A)[0]
    frame_area_end = (
        len(data)
        if frame_area_size == 0
        else min(len(data), FO1_FRM_FORMAT_CONTRACT_HEX_3E + frame_area_size)
    )
    cursor = FO1_FRM_FORMAT_CONTRACT_HEX_3E + data_offsets[rotation]
    for current_index in range(frame_index + 1):
        if cursor + FO1_FRM_FORMAT_CONTRACT_INTEGER_12 > frame_area_end:
            raise ValueError("FRM frame header escapes frame area")
        width, height, size, x, y = struct.unpack_from(">HHIhh", data, cursor)
        cursor += FO1_FRM_FORMAT_CONTRACT_INTEGER_12
        if width <= 0 or height <= 0 or size != width * height or cursor + size > frame_area_end:
            raise ValueError("FRM frame dimensions or payload are invalid")
        if current_index == frame_index:
            indexes = data[cursor : cursor + size]
            rgba = bytearray(size * 4)
            for pixel_index, palette_index in enumerate(indexes):
                rgba[pixel_index * 4 : pixel_index * 4 + 4] = bytes(colors[palette_index])
            return {
                "version": version,
                "fps": fps or FO1_FRM_FORMAT_CONTRACT_INTEGER_10,
                "storedFps": fps,
                "actionFrame": action_frame,
                "framesPerDirection": frame_count,
                "frameAreaSize": frame_area_size,
                "rotation": rotation,
                "directionOffset": [x_offsets[rotation], y_offsets[rotation]],
                "dataOffset": data_offsets[rotation],
                "frame": {
                    "index": frame_index,
                    "width": width,
                    "height": height,
                    "x": x,
                    "y": y,
                    "image": Image.frombytes("RGBA", (width, height), bytes(rgba)),
                },
            }
        cursor += size
    raise AssertionError("admitted FRM frame loop did not return")


def decode_frm(data: bytes, colors: list[tuple[int, int, int, int]]) -> dict[str, object]:
    if len(data) < FO1_FRM_FORMAT_CONTRACT_HEX_3E:
        raise ValueError("FRM header is truncated")
    version, fps, action_frame, frame_count = struct.unpack_from(">IHHH", data, 0)
    if version not in SUPPORTED_FRM_VERSIONS or frame_count <= 0:
        raise ValueError(f"unsupported FRM header: version={version} frames={frame_count}")
    x_offsets = struct.unpack_from(">6h", data, FO1_FRM_FORMAT_CONTRACT_HEX_0A)
    y_offsets = struct.unpack_from(">6h", data, FO1_FRM_FORMAT_CONTRACT_HEX_16)
    data_offsets = struct.unpack_from(">6I", data, FO1_FRM_FORMAT_CONTRACT_HEX_22)
    frame_area_size = struct.unpack_from(">I", data, FO1_FRM_FORMAT_CONTRACT_HEX_3A)[0]
    frame_area_end = len(data) if frame_area_size == 0 else min(len(data), FO1_FRM_FORMAT_CONTRACT_HEX_3E + frame_area_size)
    decoded_by_offset: dict[int, list[dict[str, object]]] = {}
    directions = []
    for rotation, relative_offset in enumerate(data_offsets):
        if relative_offset in decoded_by_offset:
            frames = decoded_by_offset[relative_offset]
        else:
            cursor = FO1_FRM_FORMAT_CONTRACT_HEX_3E + relative_offset
            frames = []
            for frame_index in range(frame_count):
                if cursor + FO1_FRM_FORMAT_CONTRACT_INTEGER_12 > frame_area_end:
                    raise ValueError("FRM frame header escapes frame area")
                width, height, size, x, y = struct.unpack_from(">HHIhh", data, cursor)
                cursor += FO1_FRM_FORMAT_CONTRACT_INTEGER_12
                if width <= 0 or height <= 0 or size != width * height or cursor + size > frame_area_end:
                    raise ValueError("FRM frame dimensions or payload are invalid")
                indexes = data[cursor : cursor + size]
                cursor += size
                rgba = bytearray(size * 4)
                for pixel_index, palette_index in enumerate(indexes):
                    rgba[pixel_index * 4 : pixel_index * 4 + 4] = bytes(colors[palette_index])
                image = Image.frombytes("RGBA", (width, height), bytes(rgba))
                frames.append(
                    {
                        "index": frame_index,
                        "width": width,
                        "height": height,
                        "x": x,
                        "y": y,
                        "image": image,
                    }
                )
            decoded_by_offset[relative_offset] = frames
        directions.append(
            {
                "rotation": rotation,
                "xOffset": x_offsets[rotation],
                "yOffset": y_offsets[rotation],
                "dataOffset": relative_offset,
                "frames": frames,
            }
        )
    return {
        "version": version,
        "fps": fps or FO1_FRM_FORMAT_CONTRACT_INTEGER_10,
        "storedFps": fps,
        "actionFrame": action_frame,
        "framesPerDirection": frame_count,
        "frameAreaSize": frame_area_size,
        "directions": directions,
    }


def save_preview(decoded: dict[str, object], output_dir: Path) -> dict[str, object]:
    output_dir.mkdir(parents=True, exist_ok=False)
    unique_directions = []
    seen_offsets = set()
    frame_rows = []
    for direction in decoded["directions"]:
        if direction["dataOffset"] in seen_offsets:
            continue
        seen_offsets.add(direction["dataOffset"])
        unique_directions.append(direction["rotation"])
        for frame in direction["frames"]:
            name = f"r{direction['rotation']}-f{frame['index']:02d}.png"
            frame["image"].save(output_dir / name)
            frame_rows.append((name, direction["rotation"], frame))

    thumbs = []
    for name, rotation, frame in frame_rows:
        image = frame["image"].copy()
        image.thumbnail((FO1_FRM_FORMAT_CONTRACT_INTEGER_320, FO1_FRM_FORMAT_CONTRACT_INTEGER_260), Image.Resampling.NEAREST)
        thumbs.append((name, rotation, frame, image))
    columns = min(4, max(1, len(thumbs)))
    rows = (len(thumbs) + columns - 1) // columns
    cell_width, cell_height = FO1_FRM_FORMAT_CONTRACT_INTEGER_340, FO1_FRM_FORMAT_CONTRACT_INTEGER_300
    sheet = Image.new("RGBA", (columns * cell_width, rows * cell_height), (FO1_FRM_FORMAT_CONTRACT_INTEGER_20, FO1_FRM_FORMAT_CONTRACT_INTEGER_22, FO1_FRM_FORMAT_CONTRACT_INTEGER_20, FO1_FRM_FORMAT_CONTRACT_INTEGER_255))
    draw = ImageDraw.Draw(sheet)
    for index, (name, rotation, frame, image) in enumerate(thumbs):
        x = (index % columns) * cell_width
        y = (index // columns) * cell_height
        sheet.alpha_composite(image, (x + (cell_width - image.width) // 2, y + FO1_FRM_FORMAT_CONTRACT_INTEGER_24 + (FO1_FRM_FORMAT_CONTRACT_INTEGER_260 - image.height) // 2))
        draw.text((x + FO1_FRM_FORMAT_CONTRACT_INTEGER_8, y + FO1_FRM_FORMAT_CONTRACT_INTEGER_6), f"R{rotation} F{frame['index']} {frame['width']}x{frame['height']}", fill=(FO1_FRM_FORMAT_CONTRACT_INTEGER_220, FO1_FRM_FORMAT_CONTRACT_INTEGER_205, FO1_FRM_FORMAT_CONTRACT_INTEGER_155, FO1_FRM_FORMAT_CONTRACT_INTEGER_255))
    sheet.save(output_dir / "contact-sheet.png")
    manifest = {
        key: value
        for key, value in decoded.items()
        if key != "directions"
    }
    manifest["uniqueDirections"] = unique_directions
    manifest["frames"] = [
        {
            "file": name,
            "rotation": rotation,
            "index": frame["index"],
            "width": frame["width"],
            "height": frame["height"],
            "x": frame["x"],
            "y": frame["y"],
        }
        for name, rotation, frame in frame_rows
    ]
    (output_dir / "frm-preview.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--master-dat", type=Path, required=True)
    parser.add_argument("--logical-path", required=True)
    parser.add_argument("--palette", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    member = Dat2Archive(args.master_dat.resolve()).extract(args.logical_path)
    decoded = decode_frm(member.data, palette_rgba(args.palette.resolve()))
    manifest = save_preview(decoded, args.output_dir.resolve())
    print(json.dumps({"logicalPath": member.logical_path, "sourceSha256": member.sha256, **manifest}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
