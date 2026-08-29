from __future__ import annotations

import json
import os
import struct
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from ttw_profile import (  # noqa: E402
    derive_flattened_installer_load_order,
    inspect_ttw_profile,
    main,
)


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def plugin(*masters: str) -> bytes:
    data = subrecord("HEDR", struct.pack("<fII", 1.34, 0, 0))
    for master in masters:
        data += subrecord("MAST", master.encode("ascii") + b"\0")
        data += subrecord("DATA", bytes(8))
    return struct.pack("<4s4I2H", b"TES4", len(data), 0, 0, 0, 0, 0) + data


class TtwProfileTest(unittest.TestCase):
    def _flattened_profile(self, root: Path) -> tuple[Path, ...]:
        paths = (
            root / "FalloutNV.esm",
            root / "Fallout3.esm",
            root / "TaleOfTwoWastelands.esm",
            root / "YUPTTW.esm",
        )
        paths[0].write_bytes(plugin())
        paths[1].write_bytes(plugin("FalloutNV.esm"))
        paths[2].write_bytes(plugin("FalloutNV.esm", "Fallout3.esm"))
        paths[3].write_bytes(plugin("TaleOfTwoWastelands.esm"))
        base_time = 1_600_000_000_000_000_000
        for index, path in enumerate(paths):
            timestamp = base_time + index * 1_000_000_000
            os.utime(path, ns=(timestamp, timestamp))
        return paths

    def test_layered_generated_profile_has_stable_plugin_identity(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            base = root / "base" / "Data"
            generated = root / "ttw-output"
            base.mkdir(parents=True)
            generated.mkdir()
            (base / "FalloutNV.esm").write_bytes(plugin())
            (generated / "Fallout3.esm").write_bytes(plugin("FalloutNV.esm"))
            (generated / "TaleOfTwoWastelands.esm").write_bytes(
                plugin("FalloutNV.esm", "Fallout3.esm")
            )
            (generated / "YUPTTW.esm").write_bytes(
                plugin("TaleOfTwoWastelands.esm")
            )
            (generated / "TaleOfTwoWastelands - Main.bsa").write_bytes(b"owned")
            load_order = root / "loadorder.txt"
            load_order.write_text(
                "FalloutNV.esm\n"
                "Fallout3.esm\n"
                "TaleOfTwoWastelands.esm\n"
                "YUPTTW.esm\n",
                encoding="utf-8",
            )

            document = inspect_ttw_profile(
                [root / "base", generated], load_order, "synthetic"
            )

            self.assertEqual(document["status"], "validated-generated-plugin-profile")
            self.assertEqual(len(document["plugins"]), 4)
            self.assertEqual(document["plugins"][-1]["sourceRootIndex"], 1)
            self.assertEqual(document["archives"][0]["admission"], "discovered-not-yet-compiled")
            self.assertFalse(document["runtimeCompatibility"]["ready"])

    def test_vanilla_profile_reports_missing_ttw_markers(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "FalloutNV.esm").write_bytes(plugin())
            load_order = root / "plugins.txt"
            load_order.write_text("*FalloutNV.esm\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "TaleOfTwoWastelands.esm"):
                inspect_ttw_profile([root], load_order)

    def test_flattened_installer_output_derives_strict_all_active_order(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self._flattened_profile(root)

            load_order, evidence = derive_flattened_installer_load_order(root)

            self.assertEqual(
                load_order,
                (
                    "FalloutNV.esm",
                    "Fallout3.esm",
                    "TaleOfTwoWastelands.esm",
                    "YUPTTW.esm",
                ),
            )
            self.assertEqual([row["file"] for row in evidence], list(load_order))

    def test_flattened_installer_output_rejects_duplicate_plugin_mtimes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            paths = self._flattened_profile(root)
            timestamp = paths[0].stat().st_mtime_ns
            os.utime(paths[1], ns=(timestamp, timestamp))

            with self.assertRaisesRegex(ValueError, "not strictly ordered"):
                derive_flattened_installer_load_order(root)

    def test_flattened_installer_output_rejects_master_after_dependent(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            paths = self._flattened_profile(root)
            first_time = paths[0].stat().st_mtime_ns
            fallout3_time = paths[1].stat().st_mtime_ns
            os.utime(paths[1], ns=(fallout3_time + 1_000_000_000, fallout3_time + 1_000_000_000))
            os.utime(paths[2], ns=(fallout3_time, fallout3_time))
            os.utime(paths[3], ns=(fallout3_time + 2_000_000_000, fallout3_time + 2_000_000_000))
            self.assertLess(first_time, fallout3_time)

            with self.assertRaisesRegex(ValueError, "master is not earlier"):
                derive_flattened_installer_load_order(root)

    def test_flattened_mode_writes_snapshot_with_lower_fallback_root(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            base = root / "base"
            flattened = root / "flattened"
            output = root / "profiles" / "ttw-profile.json"
            base.mkdir()
            flattened.mkdir()
            (base / "FalloutNV.esm").write_bytes(plugin())
            (base / "BaseOnly.bsa").write_bytes(b"owned")
            self._flattened_profile(flattened)

            with patch.object(
                sys,
                "argv",
                [
                    "ttw_profile.py",
                    "--data-root",
                    str(base),
                    "--flattened-installer-output",
                    str(flattened),
                    "--output",
                    str(output),
                    "--ttw-version",
                    "synthetic",
                ],
            ):
                self.assertEqual(main(), 0)

            document = json.loads(output.read_text(encoding="utf-8"))
            snapshot = output.with_name("ttw-profile.loadorder.txt")
            self.assertTrue(snapshot.is_file())
            self.assertEqual(
                document["sourceRoots"],
                [str(base.resolve()), str(flattened.resolve())],
            )
            self.assertEqual(document["loadOrderSource"]["file"], str(snapshot.resolve()))
            self.assertTrue(
                all(row["sourceRootIndex"] == 1 for row in document["plugins"])
            )
            self.assertEqual(document["archives"][0]["file"], "BaseOnly.bsa")


if __name__ == "__main__":
    unittest.main()
