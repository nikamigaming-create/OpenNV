"""Decode the shared source-owned classic Fallout door presentation contract."""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import tempfile
import wave
from pathlib import Path
from typing import Any

from fo1_frm import decode_frm, palette_rgba_bytes
from fo1_profile import Fo1ProfileError


DOOR_SOUND_CODE_OFFSET = 40
DOOR_MINIMUM_PRO_BYTES = 49
DOOR_PALETTE_ENTRIES = 256
FFMPEG_SUCCESS = 0


def decode_classic_door(resolver, pid: int, art_filename: str) -> dict[str, Any]:
    prototype = resolver.prototype(pid)
    if prototype.object_type != 2 or prototype.subtype_name != "door" or not prototype.filename:
        raise Fo1ProfileError(f"Fallout PID is not a scenery door: {pid:08x}")
    prototype_path = f"proto\\scenery\\{prototype.filename}"
    prototype_resource = resolver.read(prototype_path)
    if len(prototype_resource.data) < DOOR_MINIMUM_PRO_BYTES:
        raise Fo1ProfileError(f"Fallout door PRO is truncated: {prototype_path}")
    sound_byte = prototype_resource.data[DOOR_SOUND_CODE_OFFSET]
    if not chr(sound_byte).isascii() or not chr(sound_byte).isalpha():
        raise Fo1ProfileError(f"Fallout door PRO sound code is invalid: {pid:08x}")
    sound_code = chr(sound_byte).upper()
    sound_catalog = resolver.read("sound\\sfx\\sndlist.lst")
    catalog_lines = sound_catalog.data.decode("ascii").splitlines()
    catalog_names = set(catalog_lines[1::4])

    def sound(kind: str) -> dict[str, str]:
        filename = f"S{kind}DOORS{sound_code}.ACM"
        if filename not in catalog_names:
            raise Fo1ProfileError(
                f"Fallout door sound is absent from sndlist.lst: {filename}"
            )
        logical_path = f"sound\\sfx\\{filename}"
        resource = resolver.read(logical_path)
        return {
            "logicalPath": logical_path,
            "source": resource.source,
            "sha256": resource.sha256,
        }

    art_path = f"art\\scenery\\{art_filename}"
    art_resource = resolver.read(art_path)
    decoded = decode_frm(
        art_resource.data,
        [(0, 0, 0, 0)] * DOOR_PALETTE_ENTRIES,
    )
    frame_counts = {len(direction["frames"]) for direction in decoded["directions"]}
    if decoded["storedFps"] <= 0 or len(frame_counts) != 1:
        raise Fo1ProfileError(f"Fallout door FRM timing is invalid: {art_path}")
    frame_count = frame_counts.pop()
    if frame_count <= 1 or not 0 <= decoded["actionFrame"] < frame_count:
        raise Fo1ProfileError(f"Fallout door FRM frame contract is invalid: {art_path}")
    return {
        "prototype": {
            "logicalPath": prototype_path,
            "source": prototype_resource.source,
            "sha256": prototype_resource.sha256,
            "soundCode": sound_code,
        },
        "art": {
            "logicalPath": art_path,
            "source": art_resource.source,
            "sha256": art_resource.sha256,
        },
        "animation": {
            "storedFramesPerSecond": decoded["storedFps"],
            "actionFrame": decoded["actionFrame"],
            "frameCount": frame_count,
            "closedFrame": 0,
            "openFrame": frame_count - 1,
        },
        "sounds": {"open": sound("O"), "close": sound("C")},
    }


def materialize_classic_door_assets(
    resolver,
    source: dict[str, Any],
    rotation: int,
    final_root: Path,
    ffmpeg: str,
) -> dict[str, Any]:
    final_root = final_root.resolve()
    if final_root.exists():
        raise Fo1ProfileError(f"refusing to overwrite classic door assets: {final_root}")
    final_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{final_root.name}-", dir=final_root.parent))
    try:
        art = resolver.read(source["art"]["logicalPath"])
        palette = resolver.read("color.pal")
        decoded = decode_frm(art.data, palette_rgba_bytes(palette.data))
        if not 0 <= rotation < len(decoded["directions"]):
            raise Fo1ProfileError("classic door MAP rotation exceeds its FRM directions")
        frames = []
        for frame_index, frame in enumerate(decoded["directions"][rotation]["frames"]):
            relative = Path("frames") / f"{frame_index:04d}.png"
            output = staging / relative
            output.parent.mkdir(parents=True, exist_ok=True)
            frame["image"].save(output, format="PNG", optimize=False)
            frames.append(
                {
                    "frame": frame_index,
                    "path": str((final_root / relative).resolve()),
                    "sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
                    "width": frame["width"],
                    "height": frame["height"],
                    "offset": [frame["x"], frame["y"]],
                }
            )
        sounds: dict[str, Any] = {}
        for role in ("open", "close"):
            identity = source["sounds"][role]
            member = resolver.read(identity["logicalPath"])
            acm = staging / f"{role}.acm"
            wav = staging / f"{role}.wav"
            acm.write_bytes(member.data)
            command = [
                ffmpeg,
                "-hide_banner",
                "-loglevel",
                "error",
                "-err_detect",
                "explode",
                "-y",
                "-i",
                str(acm),
                "-map_metadata",
                "-1",
                "-vn",
                "-acodec",
                "pcm_s16le",
                str(wav),
            ]
            conversion = subprocess.run(
                command,
                check=False,
                capture_output=True,
                text=True,
            )
            if conversion.returncode != FFMPEG_SUCCESS:
                raise Fo1ProfileError(
                    "FFmpeg could not decode owned classic door sound: "
                    f"{identity['logicalPath']}: {conversion.stderr.strip()}"
                )
            acm.unlink()
            with wave.open(str(wav), "rb") as stream:
                audio = {
                    "channels": stream.getnchannels(),
                    "sampleWidthBytes": stream.getsampwidth(),
                    "sampleRate": stream.getframerate(),
                    "sampleFrames": stream.getnframes(),
                }
            sounds[role] = {
                **identity,
                "wav": str((final_root / f"{role}.wav").resolve()),
                "wavSha256": hashlib.sha256(wav.read_bytes()).hexdigest(),
                **audio,
            }
        os.replace(staging, final_root)
        return {
            "rotation": rotation,
            "paletteSha256": palette.sha256,
            "frames": frames,
            "sounds": sounds,
        }
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise
