"""Decode owned Fallout FRM frames into a disposable PNG preview cache."""

from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

from PIL import Image, ImageDraw

from dat2_archive import Dat2Archive


def palette_rgba(path: Path) -> list[tuple[int, int, int, int]]:
    data = path.read_bytes()
    if len(data) < 768:
        raise ValueError("Fallout palette requires at least 768 bytes")
    values = data[:768]
    scale = 4 if max(values) <= 63 else 1
    colors = []
    for index in range(256):
        r, g, b = values[index * 3 : index * 3 + 3]
        colors.append((min(255, r * scale), min(255, g * scale), min(255, b * scale), 0 if index == 0 else 255))
    return colors


def decode_frm(data: bytes, colors: list[tuple[int, int, int, int]]) -> dict[str, object]:
    if len(data) < 0x3E:
        raise ValueError("FRM header is truncated")
    version, fps, action_frame, frame_count = struct.unpack_from(">IHHH", data, 0)
    if version != 4 or frame_count <= 0:
        raise ValueError(f"unsupported FRM header: version={version} frames={frame_count}")
    x_offsets = struct.unpack_from(">6h", data, 0x0A)
    y_offsets = struct.unpack_from(">6h", data, 0x16)
    data_offsets = struct.unpack_from(">6I", data, 0x22)
    frame_area_size = struct.unpack_from(">I", data, 0x3A)[0]
    frame_area_end = len(data) if frame_area_size == 0 else min(len(data), 0x3E + frame_area_size)
    decoded_by_offset: dict[int, list[dict[str, object]]] = {}
    directions = []
    for rotation, relative_offset in enumerate(data_offsets):
        if relative_offset in decoded_by_offset:
            frames = decoded_by_offset[relative_offset]
        else:
            cursor = 0x3E + relative_offset
            frames = []
            for frame_index in range(frame_count):
                if cursor + 12 > frame_area_end:
                    raise ValueError("FRM frame header escapes frame area")
                width, height, size, x, y = struct.unpack_from(">HHIhh", data, cursor)
                cursor += 12
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
        "fps": fps or 10,
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
        image.thumbnail((320, 260), Image.Resampling.NEAREST)
        thumbs.append((name, rotation, frame, image))
    columns = min(4, max(1, len(thumbs)))
    rows = (len(thumbs) + columns - 1) // columns
    cell_width, cell_height = 340, 300
    sheet = Image.new("RGBA", (columns * cell_width, rows * cell_height), (20, 22, 20, 255))
    draw = ImageDraw.Draw(sheet)
    for index, (name, rotation, frame, image) in enumerate(thumbs):
        x = (index % columns) * cell_width
        y = (index // columns) * cell_height
        sheet.alpha_composite(image, (x + (cell_width - image.width) // 2, y + 24 + (260 - image.height) // 2))
        draw.text((x + 8, y + 6), f"R{rotation} F{frame['index']} {frame['width']}x{frame['height']}", fill=(220, 205, 155, 255))
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
