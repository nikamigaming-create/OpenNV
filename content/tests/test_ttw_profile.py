from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from ttw_profile import inspect_ttw_profile  # noqa: E402


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def plugin(*masters: str) -> bytes:
    data = subrecord("HEDR", struct.pack("<fII", 1.34, 0, 0))
    for master in masters:
        data += subrecord("MAST", master.encode("ascii") + b"\0")
        data += subrecord("DATA", bytes(8))
    return struct.pack("<4s4I2H", b"TES4", len(data), 0, 0, 0, 0, 0) + data


class TtwProfileTest(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
