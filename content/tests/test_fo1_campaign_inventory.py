from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo1_campaign_inventory import (  # noqa: E402
    build_inventory,
    parse_maps_txt,
    write_inventory,
)
from fo1_profile import MAP_HEADER_SIZE, Fo1ProfileError  # noqa: E402


def synthetic_map(name: str, map_index: int) -> bytes:
    data = bytearray(MAP_HEADER_SIZE + 10000 * 4)
    struct.pack_into(">i", data, 0x00, 20)
    encoded = f"{name}.MAP".encode("ascii")
    data[0x04 : 0x04 + len(encoded)] = encoded
    struct.pack_into(">10i", data, 0x14, 17690, 0, 2, 0, -1, 12, 1, 0, map_index, 0)
    for index in range(10000):
        struct.pack_into(">I", data, MAP_HEADER_SIZE + index * 4, (1 << 16) | 1)
    return bytes(data)


class Fo1CampaignInventoryTest(unittest.TestCase):
    def test_maps_txt_and_map_layout_inventory_are_deterministic(self) -> None:
        text = """
            ; comment
            [Map 035] # inline section comment
            lookup_name=Vault 13 entrance
            map_name=V13ENT
            music=13carvrn
        """
        self.assertEqual(parse_maps_txt(text)[35]["map_name"], "V13ENT")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            maps = root / "maps"
            maps.mkdir()
            (maps / "V13ENT.MAP").write_bytes(synthetic_map("V13ENT", 35))
            maps_txt = root / "Maps.txt"
            maps_txt.write_text(text, encoding="cp1252")
            result = build_inventory(
                maps,
                maps_txt,
                ["V13ENT"],
                ["V13ENT"],
                ["V13ENT"],
            )
            self.assertEqual(result["coverage"]["mapFiles"], 1)
            self.assertEqual(result["coverage"]["presentElevations"], 1)
            self.assertEqual(result["promotion"]["tacticalPlayableMaps"], 1)
            self.assertTrue(result["maps"][0]["identity"]["mapsTxtMatchesFilename"])
            self.assertFalse(result["maps"][0]["promotion"]["questScriptsExecutable"])

    def test_promotion_gates_must_be_monotonic(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            maps = root / "maps"
            maps.mkdir()
            (maps / "ONE.MAP").write_bytes(synthetic_map("ONE", 0))
            maps_txt = root / "Maps.txt"
            maps_txt.write_text("[Map 000]\nmap_name=ONE\n", encoding="cp1252")
            with self.assertRaises(Fo1ProfileError):
                build_inventory(maps, maps_txt, [], [], ["ONE"])

    def test_inventory_writer_refuses_existing_digest(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "coverage.json"
            output.with_suffix(".json.sha256").write_text("reserved\n", encoding="ascii")
            with self.assertRaises(Fo1ProfileError):
                write_inventory(output, {"schema": "synthetic"})
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
