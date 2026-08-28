from __future__ import annotations

import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from dat2_archive import Dat2Archive, canonical_dat2_path  # noqa: E402


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


class Dat2ArchiveTest(unittest.TestCase):
    def test_stored_and_compressed_members_are_case_insensitive(self) -> None:
        payload = synthetic_dat2(
            [
                ("PROTO\\ITEMS\\ONE.PRO", b"stored", False),
                ("proto\\scenery\\two.pro", b"compressed-data" * 8, True),
            ]
        )
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "synthetic.dat"
            path.write_bytes(payload)
            archive = Dat2Archive(path)
            self.assertEqual(archive.extract("proto/items/one.pro").data, b"stored")
            compressed = archive.extract("PROTO\\SCENERY\\TWO.PRO")
            self.assertTrue(compressed.compressed)
            self.assertEqual(compressed.data, b"compressed-data" * 8)

    def test_paths_and_directory_bounds_fail_closed(self) -> None:
        with self.assertRaises(ValueError):
            canonical_dat2_path("../escape.pro")
        with self.assertRaises(ValueError):
            canonical_dat2_path("C:/escape.pro")
        payload = bytearray(synthetic_dat2([("safe.pro", b"safe", False)]))
        struct.pack_into("<I", payload, len(payload) - 8, len(payload))
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "bad.dat"
            path.write_bytes(payload)
            with self.assertRaises(ValueError):
                Dat2Archive(path)


if __name__ == "__main__":
    unittest.main()
