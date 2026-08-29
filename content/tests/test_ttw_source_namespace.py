from __future__ import annotations

import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from ttw_profile import inspect_ttw_profile  # noqa: E402
from ttw_source_namespace import inspect_ttw_source_namespace  # noqa: E402


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def plugin(*masters: str) -> bytes:
    data = subrecord("HEDR", struct.pack("<fII", 1.34, 0, 0))
    for master in masters:
        data += subrecord("MAST", master.encode("ascii") + b"\0")
        data += subrecord("DATA", bytes(8))
    return struct.pack("<4s4I2H", b"TES4", len(data), 0, 0, 0, 0, 0) + data


def bsa_v104(*, file_count: int = 0) -> bytes:
    return struct.pack(
        "<4s8I",
        b"BSA\0",
        104,
        36,
        3,
        0,
        file_count,
        0,
        0,
        0,
    )


class TtwSourceNamespaceTest(unittest.TestCase):
    def _profile(self, root: Path) -> tuple[Path, Path]:
        base = root / "base"
        generated = root / "generated"
        base.mkdir()
        generated.mkdir()
        (base / "FalloutNV.esm").write_bytes(plugin())
        (generated / "Fallout3.esm").write_bytes(plugin("FalloutNV.esm"))
        (generated / "TaleOfTwoWastelands.esm").write_bytes(
            plugin("FalloutNV.esm", "Fallout3.esm")
        )
        (generated / "YUPTTW.esm").write_bytes(
            plugin("TaleOfTwoWastelands.esm")
        )
        (base / "BaseOnly.bsa").write_bytes(bsa_v104())
        (base / "Shared.bsa").write_bytes(bsa_v104())
        (generated / "Shared.bsa").write_bytes(bsa_v104(file_count=1))
        (base / "Readme.txt").write_text("base", encoding="utf-8")
        (generated / "Readme.txt").write_text("winner", encoding="utf-8")
        (generated / "TaleOfTwoWastelands - Main.override").write_bytes(b"")
        load_order = root / "loadorder.txt"
        load_order.write_text(
            "FalloutNV.esm\n"
            "Fallout3.esm\n"
            "TaleOfTwoWastelands.esm\n"
            "YUPTTW.esm\n",
            encoding="utf-8",
        )
        profile_document = inspect_ttw_profile(
            [base, generated], load_order, "synthetic"
        )
        profile = root / "ttw-profile.json"
        profile.write_text(
            json.dumps(profile_document, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        return profile, generated

    def test_effective_namespace_revalidates_and_inventories_winners(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            profile, _generated = self._profile(Path(temporary))

            document = inspect_ttw_source_namespace(profile)

            self.assertEqual(
                document["status"], "validated-neutral-effective-source-namespace"
            )
            self.assertEqual(len(document["plugins"]), 4)
            self.assertEqual(
                [row["file"] for row in document["archives"]],
                ["BaseOnly.bsa", "Shared.bsa"],
            )
            self.assertEqual(document["archives"][0]["sourceRootIndex"], 0)
            self.assertEqual(document["archives"][1]["sourceRootIndex"], 1)
            self.assertEqual(document["archives"][1]["header"]["version"], 104)
            self.assertEqual(document["looseFiles"][0]["file"], "Readme.txt")
            self.assertEqual(document["looseFiles"][0]["sourceRootIndex"], 1)
            self.assertEqual(len(document["overrideMarkers"]), 1)
            self.assertFalse(document["runtimeCompatibility"]["ready"])

    def test_effective_namespace_rejects_plugin_hash_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            profile, generated = self._profile(Path(temporary))
            plugin_path = generated / "YUPTTW.esm"
            payload = bytearray(plugin_path.read_bytes())
            payload[-1] ^= 1
            plugin_path.write_bytes(payload)

            with self.assertRaisesRegex(ValueError, "plugin hash changed"):
                inspect_ttw_source_namespace(profile)

    def test_effective_namespace_rejects_nonempty_override_marker(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            profile, generated = self._profile(Path(temporary))
            (generated / "TaleOfTwoWastelands - Main.override").write_bytes(b"opaque")

            with self.assertRaisesRegex(ValueError, "nonempty"):
                inspect_ttw_source_namespace(profile)


if __name__ == "__main__":
    unittest.main()
