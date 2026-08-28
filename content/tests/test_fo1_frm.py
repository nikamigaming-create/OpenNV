from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo1_frm import decode_frm, palette_rgba, save_preview  # noqa: E402


def synthetic_frm() -> bytes:
    header = bytearray(0x3E)
    struct.pack_into(">IHHH", header, 0, 4, 0, 0, 1)
    struct.pack_into(">6h", header, 0x0A, 1, 2, 3, 4, 5, 6)
    struct.pack_into(">6h", header, 0x16, -1, -2, -3, -4, -5, -6)
    struct.pack_into(">6I", header, 0x22, 0, 0, 0, 0, 0, 0)
    frame = struct.pack(">HHIhh", 2, 1, 2, 7, -8) + bytes((0, 1))
    struct.pack_into(">I", header, 0x3A, len(frame))
    return bytes(header) + frame


class Fo1FrmTest(unittest.TestCase):
    def test_palette_and_shared_direction_frames_decode_deterministically(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            root = Path(raw_directory)
            palette = root / "color.pal"
            values = bytearray(768)
            values[3:6] = bytes((1, 2, 3))
            values[255 * 3 : 255 * 3 + 3] = bytes((255, 255, 255))
            palette.write_bytes(values)
            colors = palette_rgba(palette)
            self.assertEqual(colors[0], (0, 0, 0, 0))
            self.assertEqual(colors[1], (4, 8, 12, 255))
            self.assertEqual(colors[255], (0, 0, 0, 255))

            decoded = decode_frm(synthetic_frm(), colors)
            self.assertEqual(decoded["version"], 4)
            self.assertEqual(decoded["fps"], 10)
            self.assertEqual(decoded["framesPerDirection"], 1)
            self.assertEqual(len(decoded["directions"]), 6)
            self.assertEqual(decoded["directions"][0]["frames"][0]["image"].getpixel((0, 0)), colors[0])
            self.assertEqual(decoded["directions"][0]["frames"][0]["image"].getpixel((1, 0)), colors[1])

            preview = save_preview(decoded, root / "preview")
            self.assertEqual(preview["uniqueDirections"], [0])
            self.assertEqual(preview["frames"][0]["x"], 7)
            self.assertEqual(preview["frames"][0]["y"], -8)
            self.assertTrue((root / "preview" / "contact-sheet.png").is_file())

    def test_truncated_frame_fails_closed(self) -> None:
        with self.assertRaises(ValueError):
            decode_frm(synthetic_frm()[:-1], [(0, 0, 0, 0)] * 256)


if __name__ == "__main__":
    unittest.main()
