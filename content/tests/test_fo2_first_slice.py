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


def synthetic_critter_pro() -> bytes:
    result = bytearray(0x1A0)
    struct.pack_into(">III", result, 0, 0x01000003, 300, 0x01000040)
    struct.pack_into(">3i", result, 0x20, -1, 1, 1)
    stats = [8, 5, 5, 5, 5, 8, 5, 50, 9, 8, 0, 3, 0, 15, 0, 5] + [0] * 19
    struct.pack_into(">35i", result, 0x30, *stats)
    struct.pack_into(">35i", result, 0xBC, *([0] * 35))
    return bytes(result)


def synthetic_weapon_pro() -> bytes:
    result = bytearray(122)
    struct.pack_into(">III", result, 0, 0x00000007, 700, 0x0000002A)
    struct.pack_into(">i", result, 0x20, 3)
    struct.pack_into(
        ">16i",
        result,
        0x39,
        4,
        3,
        10,
        0,
        2,
        8,
        0x05000007,
        4,
        4,
        6,
        1,
        -1,
        0,
        0,
        -1,
        0,
    )
    result[0x79] = 56
    return bytes(result)


def synthetic_confrontation_map() -> bytes:
    data = synthetic_map()
    header_and_tiles_and_scripts = data[: 0xEC + 10000 * 4 + 20]
    critter_base = struct.pack(
        ">21i",
        1,
        21101,
        0,
        0,
        0,
        0,
        0,
        5,
        0x01004040,
        0x20000000,
        0,
        0x01000003,
        -1,
        0,
        0,
        0,
        0x04000001,
        750,
        1,
        10,
        0,
    )
    critter_instance = struct.pack(">11i", 0, 0, 0, 9, 0, 1, 1, -1, 50, 0, 0)
    weapon_base = struct.pack(
        ">21i",
        2,
        -1,
        0,
        0,
        0,
        0,
        0,
        0,
        0x0000002A,
        0x02000008,
        0,
        0x00000007,
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
    weapon_instance = struct.pack(">3i", 0, 0, -1)
    objects = (
        struct.pack(">2i", 1, 1)
        + critter_base
        + critter_instance
        + struct.pack(">i", 1)
        + weapon_base
        + weapon_instance
        + struct.pack(">2i", 0, 0)
    )
    return header_and_tiles_and_scripts + objects


class Fo2FirstSliceTest(unittest.TestCase):
    def test_compiles_exact_map_object_pro_and_frm_graph_without_assets(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            install = root / "Fallout 2"
            install.mkdir()
            map_data = synthetic_confrontation_map()
            critter_pro = synthetic_critter_pro()
            weapon_pro = synthetic_weapon_pro()
            critter_list = b"unused.pro\r\nunused.pro\r\n00000003.pro\r\n"
            item_list = b"unused.pro\r\n" * 6 + b"00000013.pro\r\n"
            critter_art_list = b"unused.frm\r\n" * 64 + b"nmwarr,11,1\r\n"
            item_art_list = b"unused.frm\r\n" * 42 + b"spear.frm\r\n"
            (install / "master.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("maps\\artemple.map", map_data, True),
                        ("proto\\critters\\critters.lst", critter_list, False),
                        ("proto\\critters\\00000003.pro", critter_pro, False),
                        ("proto\\items\\items.lst", item_list, False),
                        ("proto\\items\\00000013.pro", weapon_pro, False),
                        ("art\\critters\\critters.lst", critter_art_list, False),
                        ("art\\critters\\nmwarrga.frm", synthetic_frm(), True),
                        ("art\\items\\items.lst", item_art_list, False),
                        ("art\\items\\spear.frm", synthetic_frm(), True),
                        ("text\\english\\game\\pro_crit.msg", b"{300}{}{Villager}\r\n", False),
                        ("text\\english\\game\\pro_item.msg", b"{700}{}{Spear}\r\n", False),
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
                        "schema": "opennv-fo2-first-slice-recipe/v2",
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
                        "boundedConfrontation": {
                            "schema": "opennv-fo2-temple-confrontation-recipe/v1",
                            "critter": {
                                "serial": 2,
                                "tile": 21101,
                                "pid": "01000003",
                                "sid": "04000001",
                                "prototypeSha256": hashlib.sha256(critter_pro).hexdigest(),
                            },
                            "loot": {
                                "serial": 1,
                                "pid": "00000007",
                                "quantity": 1,
                                "prototypeSha256": hashlib.sha256(weapon_pro).hexdigest(),
                            },
                            "messageCatalogs": {
                                "critter": "text\\english\\game\\pro_crit.msg",
                                "item": "text\\english\\game\\pro_item.msg",
                            },
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
            self.assertEqual(document["map"]["allObjectCount"], 2)
            confrontation = document["boundedConfrontation"]
            self.assertEqual(confrontation["critter"]["serial"], 2)
            self.assertEqual(confrontation["critter"]["currentHitPoints"], 50)
            self.assertEqual(confrontation["critter"]["prototype"]["stats"]["actionPoints"], 9)
            self.assertEqual(confrontation["defeatLoot"]["serial"], 1)
            self.assertEqual(confrontation["defeatLoot"]["displayName"], "Spear")
            self.assertEqual(
                confrontation["defeatLoot"]["prototype"]["weapon"]["actionPointCostPrimary"],
                4,
            )
            self.assertTrue(document["promotion"]["transported"])
            self.assertFalse(document["runtimeCompatibility"]["ready"])
            self.assertFalse(document["retailOrDerivedAssetsPackaged"])
            self.assertEqual(document["generatedCaches"], [])


if __name__ == "__main__":
    unittest.main()
