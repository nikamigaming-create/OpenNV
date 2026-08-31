import struct
import unittest
from pathlib import Path
import sys


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from classic_int_effects import inventory_int_program  # noqa: E402
from fo1_map_objects import parse_script_section  # noqa: E402


def synthetic_random_int() -> bytes:
    push = lambda value: struct.pack(">Hi", 0xC001, value)
    opcode = lambda value: struct.pack(">H", value)
    name = b"start\0"
    body_offset = 42 + 4 + 24 + 4 + len(name)
    body = b"".join(
        [opcode(0x802B), push(1), push(4), opcode(0x80B4), opcode(0x801C)]
    )
    return b"\0" * 42 + struct.pack(">I6I", 1, 4, 0, 0, 0, body_offset, 0) + (
        struct.pack(">I", len(name)) + name + body
    )


class ClassicIntInitializationTests(unittest.TestCase):
    def test_int_inventory_decodes_literal_random_and_branch_free_start(self) -> None:
        inventory = inventory_int_program(synthetic_random_int())
        self.assertEqual(inventory["randomOpcode"], "80b4")
        self.assertEqual(
            inventory["randomSites"],
            [
                {
                    "procedure": "start",
                    "offset": inventory["randomSites"][0]["offset"],
                    "operandKind": "literal-inclusive-range",
                    "minimum": 1,
                    "maximum": 4,
                }
            ],
        )
        self.assertEqual(inventory["procedures"][0]["eventKind"], "program-start")
        self.assertEqual(inventory["procedures"][0]["branches"], [])

    def test_map_script_records_decode_type_specific_program_indices(self) -> None:
        data = bytearray()
        data.extend(struct.pack(">i", 0))
        data.extend(struct.pack(">i", 1))
        for slot in range(16):
            row = [0] * 18
            row[0] = 0x01000000 + slot
            row[3] = 99
            row[5] = 30 + slot
            data.extend(struct.pack(">18i", *row))
        data.extend(struct.pack(">ii", 1, 0))
        data.extend(struct.pack(">i", 0))
        data.extend(struct.pack(">i", 1))
        for slot in range(16):
            row = [0] * 16
            row[0] = 0x03000000 + slot
            row[3] = 511 + slot
            row[5] = 14 + slot
            data.extend(struct.pack(">16i", *row))
        data.extend(struct.pack(">ii", 1, 0))
        data.extend(struct.pack(">i", 0))

        lists, end = parse_script_section(bytes(data), 0)
        self.assertEqual(end, len(data))
        spatial = lists[1]["extents"][0]["slots"][0]
        scenery = lists[3]["extents"][0]["slots"][0]
        self.assertEqual(spatial["scriptIndex"], 30)
        self.assertIsNone(spatial["objectId"])
        self.assertEqual(scenery["scriptIndex"], 511)
        self.assertEqual(scenery["objectId"], 14)


if __name__ == "__main__":
    unittest.main()
