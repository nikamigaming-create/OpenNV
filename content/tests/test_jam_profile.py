from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from jam_profile import inspect_jam_profile  # noqa: E402


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes) -> bytes:
    return struct.pack(
        "<4s4I2H",
        signature.encode("ascii"),
        len(data),
        0,
        form_id,
        0,
        0,
        0,
    ) + data


def global_record(form_id: int, editor_id: str, value: float) -> bytes:
    return record(
        "GLOB",
        form_id,
        subrecord("EDID", editor_id.encode("ascii") + b"\0")
        + subrecord("FNAM", b"f")
        + subrecord("FLTV", struct.pack("<f", value)),
    )


def script_record(form_id: int, editor_id: str, source: str) -> bytes:
    return record(
        "SCPT",
        form_id,
        subrecord("EDID", editor_id.encode("ascii") + b"\0")
        + subrecord("SCTX", source.encode("cp1252")),
    )


def plugin(*masters: str, with_jvs: bool = False) -> bytes:
    data = subrecord("HEDR", struct.pack("<fII", 1.34, 0, 0))
    for master in masters:
        data += subrecord("MAST", master.encode("ascii") + b"\0")
        data += subrecord("DATA", bytes(8))
    content = record("TES4", 0, data)
    if not with_jvs:
        return content
    source = """
        DispatchEvent "JVSStateChange"
        GetControl
        IsControlPressed
        IsKeyPressed
        GetController
        IsButtonPressed
        SetGameMainLoopCallback
        SetNthPerkEntryValue1
        SetOnKeyDownEventHandler
        SetSpeedMult
    """
    for index, editor_id in enumerate(
        ("JVSScript", "JVSOnKeyDownEventHandler", "JVSMainLoopEventHandler"),
        start=1,
    ):
        content += script_record(0x100 + index, editor_id, source)
    for index, (editor_id, value) in enumerate(
        (
            ("JVSEnabled", 1.0),
            ("JVSKey", 42.0),
            ("JVSButton", 64.0),
            ("JVSToggle", 0.0),
            ("JVSSpeedMult", 75.0),
        ),
        start=1,
    ):
        content += global_record(0x200 + index, editor_id, value)
    return content


class JamProfileTest(unittest.TestCase):
    def test_hash_binds_effective_local_jam_dependencies_without_copying(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            game = root / "game"
            base = game / "Data"
            mods = root / "mo2" / "mods"
            jam = mods / "JAM"
            dependencies = mods / "JAM Requirements"
            game.mkdir()
            base.mkdir()
            jam.mkdir(parents=True)
            (dependencies / "NVSE" / "Plugins").mkdir(parents=True)

            for name in ("nvse_1_4.dll", "nvse_loader.exe", "nvse_steam_loader.dll"):
                (game / name).write_bytes(("xnvse:" + name).encode("ascii"))
            (base / "FalloutNV.esm").write_bytes(plugin())
            dlc = ("DeadMoney.esm", "HonestHearts.esm", "OldWorldBlues.esm", "LonesomeRoad.esm")
            for name in dlc:
                (base / name).write_bytes(plugin())
            (jam / "JustAssortedMods.esp").write_bytes(
                plugin("FalloutNV.esm", *dlc, with_jvs=True)
            )
            for name in (
                "jip_nvse.dll",
                "johnnyguitar.dll",
                "kNVSE.dll",
                "nvse_stewie_tweaks.dll",
                "nvse_stewie_tweaks.ini",
                "ui_organizer.dll",
            ):
                (dependencies / "NVSE" / "Plugins" / name).write_bytes(
                    ("dependency:" + name).encode("ascii")
                )

            document = inspect_jam_profile(
                game,
                [base, dependencies, jam],
                "synthetic-4.6",
            )

            self.assertEqual(document["status"], "validated-local-dependency-profile")
            self.assertEqual(document["declaredJamVersion"], "synthetic-4.6")
            self.assertEqual(document["jamPlugin"]["masters"], ["FalloutNV.esm", *dlc])
            self.assertEqual(len(document["files"]["gameRoot"]), 3)
            self.assertEqual(len(document["files"]["effectiveData"]), 12)
            self.assertFalse(document["runtimeCompatibility"]["ready"])
            self.assertFalse(document["runtimeCompatibility"]["nativeDllLoading"])
            self.assertEqual(document["missingDependencies"], [])
            self.assertEqual(document["missingPluginMasters"], [])
            capability = document["portableCapabilities"][0]
            self.assertEqual(
                capability["status"],
                "transported-bounded-runtime-capability",
            )
            self.assertEqual(capability["runtime"]["desktopPhysicalKey"], "Shift")
            self.assertEqual(capability["runtime"]["speedMultiplier"], 1.75)
            self.assertEqual(
                document["runtimeCompatibility"]["transportedCapabilities"],
                ["jvs-forward-sprint-speed-v1"],
            )
            self.assertIn(
                "uio-xml-merge-hud-menu-injection-and-refresh",
                document["runtimeCompatibility"]["unsupportedSemantics"],
            )
            self.assertEqual(list(jam.iterdir()), [jam / "JustAssortedMods.esp"])

    def test_missing_native_packages_remain_explicit_and_runtime_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            game = root / "game"
            base = game / "Data"
            jam = root / "JAM"
            game.mkdir()
            base.mkdir()
            jam.mkdir()
            for name in ("nvse_1_4.dll", "nvse_loader.exe", "nvse_steam_loader.dll"):
                (game / name).write_bytes(name.encode("ascii"))
            dlc = ("DeadMoney.esm", "HonestHearts.esm", "OldWorldBlues.esm", "LonesomeRoad.esm")
            (base / "FalloutNV.esm").write_bytes(plugin())
            for name in dlc:
                (base / name).write_bytes(plugin())
            (jam / "JustAssortedMods.esp").write_bytes(
                plugin("FalloutNV.esm", *dlc, with_jvs=True)
            )

            document = inspect_jam_profile(game, [base, jam], "synthetic-4.6")

            self.assertEqual(document["status"], "incomplete-local-dependency-profile")
            self.assertEqual(len(document["missingDependencies"]), 6)
            self.assertFalse(document["runtimeCompatibility"]["ready"])
            self.assertIn(
                "jvs-forward-sprint-speed-v1",
                document["runtimeCompatibility"]["transportedCapabilities"],
            )
            self.assertEqual(
                document["portableCapabilities"][0]["status"],
                "transported-bounded-runtime-capability",
            )


if __name__ == "__main__":
    unittest.main()
