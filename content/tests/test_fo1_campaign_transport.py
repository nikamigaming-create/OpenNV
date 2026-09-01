from __future__ import annotations

from contextlib import contextmanager
import hashlib
import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo1_campaign_transport import (  # noqa: E402
    canonical_map_id,
    json_payload,
    map_summary,
    write_payload,
)
from classic_map_joins import exit_grid_records, reciprocal_map_joins  # noqa: E402
from fo1_map_objects import Prototype, build_contract, parse_map_objects  # noqa: E402
from fo1_profile import MAP_HEADER_SIZE, Fo1ProfileError  # noqa: E402


def synthetic_complete_map(name: str, map_index: int) -> bytes:
    data = bytearray(MAP_HEADER_SIZE + 10000 * 4)
    struct.pack_into(">i", data, 0x00, 20)
    encoded = f"{name}.MAP".encode("ascii")
    data[0x04 : 0x04 + len(encoded)] = encoded
    struct.pack_into(
        ">10i",
        data,
        0x14,
        17690,
        0,
        2,
        0,
        -1,
        12,
        1,
        0,
        map_index,
        0,
    )
    for index in range(10000):
        struct.pack_into(">I", data, MAP_HEADER_SIZE + index * 4, (1 << 16) | 1)
    data.extend(struct.pack(">9i", *([0] * 9)))
    return bytes(data)


class StubResolver:
    def __init__(self, override_root: Path, master_dat: Path):
        self.override_root = override_root.resolve()
        self.master_dat = master_dat.resolve()
        self.resources = {}
        self.scopes = 0

    @contextmanager
    def access_scope(self):
        self.scopes += 1
        yield set()

    def prototype(self, pid: int) -> Prototype:
        return Prototype(
            pid,
            5,
            pid & 0x00FFFFFF,
            "synthetic-exit.pro",
            1600,
            0x05000027,
            None,
            None,
            "synthetic",
            "a" * 64,
        )

    def art_filename(self, fid: int) -> str:
        return "synthetic-exit.frm"


class Fo1CampaignTransportTest(unittest.TestCase):
    def test_compact_object_layout_is_explicit_and_defaults_only_absent_fields(self) -> None:
        compact = (
            1585,
            193,
            0,
            0,
            0x05000027,
            -1610579944,
            0,
            0x05000016,
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
        payload = (
            struct.pack(">2i", 1, 1)
            + struct.pack(">17i", *compact)
            + struct.pack(">5i", 0, 58, 19085, 1, 0)
            + struct.pack(">2i", 0, 0)
        )
        resolver = StubResolver(Path("synthetic/mods/fo1_base"), Path("master.dat"))
        objects, end_offset = parse_map_objects(payload, 0, 20, resolver)
        row = objects["elevations"][0]["objects"][0]
        self.assertEqual(end_offset, len(payload))
        self.assertEqual(row["baseLayout"], "compact-17")
        self.assertIsNone(row["cachedScreen"])
        self.assertEqual(row["frame"], 0)
        self.assertEqual(row["rotation"], 0)
        self.assertEqual(row["instanceValues"], [58, 19085, 1, 0])

    def test_shared_resolver_transports_a_complete_zero_object_map(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            ettu = root / "Fallout1in2"
            override = ettu / "mods" / "fo1_base"
            override.mkdir(parents=True)
            master = root / "master.dat"
            master.write_bytes(b"synthetic-master")
            map_path = root / "TESTMAP.MAP"
            map_path.write_bytes(synthetic_complete_map("TESTMAP", 7))
            resolver = StubResolver(override, master)

            contract = build_contract(map_path, ettu, master, resolver=resolver)

            self.assertEqual(contract["status"], "transported-object-graph")
            self.assertEqual(contract["map"]["objects"]["totalTopLevelObjects"], 0)
            self.assertEqual(contract["resources"], [])
            self.assertEqual(resolver.scopes, 1)

    def test_shared_resolver_identity_mismatch_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first = root / "first"
            second = root / "second"
            (first / "mods" / "fo1_base").mkdir(parents=True)
            (second / "mods" / "fo1_base").mkdir(parents=True)
            master = root / "master.dat"
            master.write_bytes(b"synthetic-master")
            map_path = root / "TESTMAP.MAP"
            map_path.write_bytes(synthetic_complete_map("TESTMAP", 7))
            resolver = StubResolver(first / "mods" / "fo1_base", master)
            with self.assertRaises(Fo1ProfileError):
                build_contract(map_path, second, master, resolver=resolver)

    def test_map_summary_and_payload_hash_are_deterministic(self) -> None:
        document = {
            "source": {"map": {"file": "TESTMAP.MAP", "sha256": "a" * 64}},
            "header": {"mapIndex": 7, "version": 20},
            "entry": {"tile": 17690, "elevation": 0, "rotation": 2},
            "mapsTxt": {"index": 7, "mapName": "TESTMAP"},
            "layout": {"presentElevations": [0]},
            "objectGraph": {
                "scriptLists": [{"liveCount": 2}, {"liveCount": 3}],
                "objects": {
                    "totalTopLevelObjects": 11,
                    "elevations": [
                        {"objects": [{"inventory": []} for _ in range(11)]}
                    ],
                },
                "doors": [{}, {}],
            },
            "initializationScripts": {
                "mapHeader": {"program": {"program": "TESTMAP.int"}},
                "randomSites": [],
            },
            "exitGrids": [],
            "resources": [{}, {}, {}],
            "promotion": {"state": "transported"},
        }
        payload = json_payload(document)
        digest = hashlib.sha256(payload).hexdigest()
        summary = map_summary("testmap", "maps/testmap.json", digest, document)
        self.assertEqual(summary["liveScripts"], 5)
        self.assertEqual(summary["topLevelObjects"], 11)
        self.assertEqual(summary["allObjects"], 11)
        self.assertEqual(summary["doors"], 2)
        self.assertEqual(summary["resources"], 3)
        self.assertEqual(summary["exitGrids"], 0)
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "testmap.json"
            self.assertEqual(write_payload(path, document), digest)
            self.assertEqual(path.read_bytes(), payload)

    def test_map_id_rejects_noncanonical_filename(self) -> None:
        self.assertEqual(canonical_map_id(Path("V13ENT.MAP")), "v13ent")
        with self.assertRaises(Fo1ProfileError):
            canonical_map_id(Path("bad map.MAP"))

    def test_reciprocal_map_join_is_derived_from_owned_exit_values(self) -> None:
        def objects(serial: int, tile: int, target: list[int]) -> dict:
            return {
                "elevations": [
                    {
                        "objects": [
                            {
                                "serial": serial,
                                "tile": tile,
                                "elevation": 0,
                                "pid": "05000016",
                                "instanceValues": target,
                                "prototype": {
                                    "object_type": 5,
                                    "sha256": "c" * 64,
                                },
                            }
                        ]
                    }
                ]
            }

        east = exit_grid_records(25, "SHADYE.MAP", "a" * 64, objects(7, 100, [26, 200, 0, 3]))
        west = exit_grid_records(26, "SHADYW.MAP", "b" * 64, objects(8, 200, [25, 100, 0, 1]))
        joins = reciprocal_map_joins(
            [
                {"mapIndex": 25, "mapName": "SHADYE.MAP", "mapSha256": "a" * 64, "exitGrids": east},
                {"mapIndex": 26, "mapName": "SHADYW.MAP", "mapSha256": "b" * 64, "exitGrids": west},
            ]
        )
        self.assertEqual(len(joins), 1)
        self.assertEqual(joins[0]["sourceMap"]["mapIndex"], 25)
        self.assertEqual(joins[0]["destinationMap"]["mapIndex"], 26)
        self.assertTrue(joins[0]["reciprocal"])


if __name__ == "__main__":
    unittest.main()
