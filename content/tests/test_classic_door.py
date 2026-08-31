from __future__ import annotations

import hashlib
import struct
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "content/tools"))

from classic_door import decode_classic_door  # noqa: E402
from fo1_profile import Fo1ProfileError  # noqa: E402


def frm(frames: int, fps: int, action_frame: int) -> bytes:
    header = bytearray(0x3E)
    struct.pack_into(">IHHH", header, 0, 4, fps, action_frame, frames)
    struct.pack_into(">6h", header, 0x0A, *([0] * 6))
    struct.pack_into(">6h", header, 0x16, *([0] * 6))
    struct.pack_into(">6I", header, 0x22, *([0] * 6))
    payload = b"".join(
        struct.pack(">HHIhh", 1, 1, 1, 0, 0) + bytes((index,))
        for index in range(frames)
    )
    struct.pack_into(">I", header, 0x3A, len(payload))
    return bytes(header) + payload


class Resolver:
    def __init__(self) -> None:
        prototype = bytearray(49)
        prototype[40] = ord("S")
        self.resources = {
            "proto\\scenery\\door.pro": bytes(prototype),
            "art\\scenery\\door.frm": frm(3, 12, 1),
            "sound\\sfx\\sndlist.lst": (
                b"2\r\nSODOORSS.ACM\r\n1\r\n1\r\n1\r\n"
                b"SCDOORSS.ACM\r\n1\r\n1\r\n2\r\n"
            ),
            "sound\\sfx\\SODOORSS.ACM": b"open",
            "sound\\sfx\\SCDOORSS.ACM": b"close",
        }

    def prototype(self, _pid: int):
        return SimpleNamespace(
            object_type=2,
            subtype_name="door",
            filename="door.pro",
        )

    def read(self, logical_path: str):
        data = self.resources[logical_path]
        return SimpleNamespace(
            data=data,
            source=f"owned:{logical_path}",
            sha256=hashlib.sha256(data).hexdigest(),
        )


class ClassicDoorTest(unittest.TestCase):
    def test_pro_sound_and_frm_timing_are_source_decoded(self) -> None:
        door = decode_classic_door(Resolver(), 0x02000001, "door.frm")
        self.assertEqual(door["prototype"]["soundCode"], "S")
        self.assertEqual(
            door["animation"],
            {
                "storedFramesPerSecond": 12,
                "actionFrame": 1,
                "frameCount": 3,
                "closedFrame": 0,
                "openFrame": 2,
            },
        )
        self.assertEqual(
            door["sounds"]["open"]["logicalPath"],
            "sound\\sfx\\SODOORSS.ACM",
        )
        self.assertEqual(
            door["sounds"]["close"]["logicalPath"],
            "sound\\sfx\\SCDOORSS.ACM",
        )

    def test_missing_owned_sound_fails_closed(self) -> None:
        resolver = Resolver()
        del resolver.resources["sound\\sfx\\SCDOORSS.ACM"]
        with self.assertRaises(KeyError):
            decode_classic_door(resolver, 0x02000001, "door.frm")


if __name__ == "__main__":
    unittest.main()
