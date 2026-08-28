from __future__ import annotations

import hashlib
import json
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo2_first_slice import compile_fo2_first_slice  # noqa: E402
from fo2_profile import inspect_fo2_profile  # noqa: E402


def synthetic_dat2(members: list[tuple[str, bytes, bool]]) -> bytes:
    data = bytearray()
    rows = []
    for logical_path, decoded, compressed in sorted(members, key=lambda row: row[0].casefold()):
        stored = zlib.compress(decoded, level=9) if compressed else decoded
        rows.append((logical_path, compressed, len(decoded), len(stored), len(data)))
        data.extend(stored)
    tree = bytearray(struct.pack("<I", len(rows)))
    for logical_path, compressed, decoded_size, stored_size, offset in rows:
        encoded = logical_path.encode("utf-8")
        tree.extend(struct.pack("<I", len(encoded)))
        tree.extend(encoded)
        tree.extend(bytes((1 if compressed else 0,)))
        tree.extend(struct.pack("<III", decoded_size, stored_size, offset))
    final_size = len(data) + len(tree) + 8
    return bytes(data + tree + struct.pack("<II", len(tree), final_size))


def synthetic_frm() -> bytes:
    header = bytearray(0x3E)
    struct.pack_into(">IHHH", header, 0, 4, 10, 0, 1)
    struct.pack_into(">6h", header, 0x0A, 0, 0, 0, 0, 0, 0)
    struct.pack_into(">6h", header, 0x16, 0, 0, 0, 0, 0, 0)
    struct.pack_into(">6I", header, 0x22, 0, 0, 0, 0, 0, 0)
    frame = struct.pack(">HHIhh", 1, 1, 1, 0, 0) + b"\x00"
    struct.pack_into(">I", header, 0x3A, len(frame))
    return bytes(header) + frame


def synthetic_map() -> bytes:
    header = bytearray(0xEC)
    struct.pack_into(">i", header, 0, 20)
    header[4:20] = b"ARTEMPLE.MAP\0\0\0\0"
    struct.pack_into(">10i", header, 0x14, 18492, 0, 0, 0, 745, 12, 1, 0, 126, 0)
    tiles = struct.pack(">10000I", *([0x00010001] * 10000))
    scripts = struct.pack(">5i", 0, 0, 0, 0, 0)
    object_base = struct.pack(
        ">21i",
        1,
        18493,
        0,
        0,
        0,
        0,
        0,
        0,
        0x02000000,
        0,
        0,
        0x02000001,
        -1,
        0,
        0,
        0,
        -1,
        -1,
        0,
        0,
        0,
    )
    objects = struct.pack(">2i", 1, 1) + object_base + struct.pack(">3i", 0, 0, 0)
    return bytes(header) + tiles + scripts + objects


class Fo2FirstSliceTest(unittest.TestCase):
    def test_compiles_exact_map_object_pro_and_frm_graph_without_assets(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            install = root / "Fallout 2"
            install.mkdir()
            map_data = synthetic_map()
            prototype = bytearray(0x24)
            struct.pack_into(">III", prototype, 0, 0x02000001, 100, 0x02000000)
            struct.pack_into(">i", prototype, 0x20, 5)
            (install / "master.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("maps\\artemple.map", map_data, True),
                        ("proto\\scenery\\scenery.lst", b"test.pro\r\n", False),
                        ("proto\\scenery\\test.pro", bytes(prototype), False),
                        ("art\\scenery\\scenery.lst", b"test.frm\r\n", False),
                        ("art\\scenery\\test.frm", synthetic_frm(), True),
                    ]
                )
            )
            (install / "critter.dat").write_bytes(
                synthetic_dat2([("art\\critters\\unused.frm", b"unused", False)])
            )
            (install / "patch000.dat").write_bytes(
                synthetic_dat2(
                    [
                        (
                            "data\\maps.txt",
                            b"[Map 126]\r\nlookup_name=Arroyo Temple\r\nmap_name=artemple\r\n",
                            True,
                        )
                    ]
                )
            )

            profile_path = root / "fallout2-profile.json"
            profile = inspect_fo2_profile(install, "synthetic")
            profile_path.write_text(json.dumps(profile), encoding="utf-8")
            recipe_path = root / "synthetic-temple.json"
            recipe_path.write_text(
                json.dumps(
                    {
                        "schema": "opennv-fo2-first-slice-recipe/v1",
                        "id": recipe_path.stem,
                        "campaign": "Fallout2",
                        "sourceProfileSchema": "opennv-fo2-owned-profile/v1",
                        "overlayOrderHighToLow": [
                            "patch000.dat",
                            "critter.dat",
                            "master.dat",
                        ],
                        "mapRegistry": {
                            "logicalPath": "data\\maps.txt",
                            "section": "Map 126",
                            "lookupName": "Arroyo Temple",
                            "mapName": "artemple",
                        },
                        "map": {
                            "logicalPath": "maps\\artemple.map",
                            "sha256": hashlib.sha256(map_data).hexdigest(),
                            "header": {
                                "version": 20,
                                "name": "ARTEMPLE.MAP",
                                "enteringTile": 18492,
                                "enteringElevation": 0,
                                "enteringRotation": 0,
                                "localVariables": 0,
                                "scriptIndex": 745,
                                "flags": 12,
                                "darkness": 1,
                                "globalVariables": 0,
                                "mapIndex": 126,
                                "lastVisitTime": 0,
                            },
                            "presentElevations": [0],
                        },
                        "declaredRole": "synthetic Temple source slice",
                        "unsupported": ["runtime"],
                    }
                ),
                encoding="utf-8",
            )

            document = compile_fo2_first_slice(profile_path, recipe_path)

            self.assertEqual(document["status"], "transported-source-manifest")
            self.assertEqual(document["newGameStart"]["playerEntry"]["tile"], 18492)
            self.assertFalse(document["newGameStart"]["playerEntry"]["placedPlayerObject"])
            self.assertEqual(document["map"]["objects"]["totalTopLevelObjects"], 1)
            self.assertEqual(document["map"]["allObjectCount"], 1)
            self.assertEqual(document["prototypes"][0]["logicalPath"], "proto\\scenery\\test.pro")
            self.assertEqual(document["frms"][0]["logicalPath"], "art\\scenery\\test.frm")
            self.assertTrue(document["promotion"]["transported"])
            self.assertFalse(document["runtimeCompatibility"]["ready"])
            self.assertFalse(document["retailOrDerivedAssetsPackaged"])
            self.assertEqual(document["generatedCaches"], [])


if __name__ == "__main__":
    unittest.main()
