from __future__ import annotations

import struct
import sys
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo1_map_objects import parse_script_section  # noqa: E402


class Fo1MapObjectTest(unittest.TestCase):
    def test_script_slots_follow_sid_type_and_extent_lengths(self) -> None:
        payload = bytearray()
        payload.extend(struct.pack(">i", 0))
        payload.extend(struct.pack(">i", 2))
        for index in range(16):
            sid = 0x01000000 + index if index < 2 else -1
            record_size = 72 if sid >= 0 else 64
            payload.extend(struct.pack(">i", sid))
            payload.extend(bytes(record_size - 4))
        payload.extend(struct.pack(">2i", 2, 0))
        payload.extend(struct.pack(">i", 0))
        payload.extend(struct.pack(">i", 0))
        payload.extend(struct.pack(">i", 0))
        lists, offset = parse_script_section(bytes(payload), 0)
        self.assertEqual(offset, len(payload))
        self.assertEqual(lists[1]["liveCount"], 2)
        self.assertEqual(lists[1]["extents"][0]["length"], 2)
        self.assertEqual(lists[1]["extents"][0]["slots"][0]["bytes"], 72)
        self.assertEqual(lists[1]["extents"][0]["slots"][2]["bytes"], 64)


if __name__ == "__main__":
    unittest.main()
