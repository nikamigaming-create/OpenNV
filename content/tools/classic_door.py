"""Decode the shared source-owned classic Fallout door presentation contract."""

from __future__ import annotations

from typing import Any

from fo1_frm import decode_frm
from fo1_profile import Fo1ProfileError


DOOR_SOUND_CODE_OFFSET = 40
DOOR_MINIMUM_PRO_BYTES = 49
DOOR_PALETTE_ENTRIES = 256


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
