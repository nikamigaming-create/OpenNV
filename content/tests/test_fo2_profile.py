from __future__ import annotations

import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo2_profile import inspect_fo2_profile  # noqa: E402


def synthetic_dat2(members: list[tuple[str, bytes, bool]]) -> bytes:
    data = bytearray()
    rows = []
    for logical_path, decoded, compressed in sorted(
        members,
        key=lambda row: row[0].casefold(),
    ):
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


class Fo2ProfileTest(unittest.TestCase):
    def test_registers_three_owned_dat2_archives_without_copying(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            install = Path(temporary) / "Fallout 2"
            install.mkdir()
            fixtures = {
                "master.dat": [("art\\items\\item.frm", b"master" * 8, True)],
                "critter.dat": [("art\\critters\\hero.frm", b"critter", False)],
                "patch000.dat": [("text\\english\\game\\patch.msg", b"patch", True)],
            }
            for name, members in fixtures.items():
                (install / name).write_bytes(synthetic_dat2(members))

            document = inspect_fo2_profile(install, "synthetic")

            self.assertEqual(document["status"], "registered-owned-install")
            self.assertEqual(document["campaign"], "Fallout2")
            self.assertEqual(document["declaredVersion"], "synthetic")
            self.assertEqual(len(document["install"]["archives"]), 3)
            self.assertFalse(document["runtimeCompatibility"]["ready"])
            self.assertFalse(document["retailOrDerivedAssetsPackaged"])
            self.assertEqual(document["generatedCaches"], [])
            self.assertTrue(
                all(
                    row["formatIdentity"]["format"] == "fallout-dat2"
                    for row in document["install"]["archives"]
                )
            )
            self.assertTrue(
                all(
                    not mode["ready"]
                    for mode in document["runtimeCompatibility"]["presentations"].values()
                )
            )
            self.assertEqual(
                sorted(path.name for path in install.iterdir()),
                sorted(fixtures),
            )

    def test_rejects_a_required_archive_that_is_not_dat2(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            install = Path(temporary)
            for name in ("master.dat", "critter.dat", "patch000.dat"):
                (install / name).write_bytes(
                    synthetic_dat2([("safe.txt", name.encode("ascii"), False)])
                )
            (install / "patch000.dat").write_bytes(b"not-a-dat2")

            with self.assertRaises(ValueError):
                inspect_fo2_profile(install)


if __name__ == "__main__":
    unittest.main()
