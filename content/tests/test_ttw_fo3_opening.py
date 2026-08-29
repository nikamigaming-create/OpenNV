from __future__ import annotations

import hashlib
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from plugin_stack import PluginContext, file_sha256  # noqa: E402
from ttw_fo3_opening import (  # noqa: E402
    EffectiveRecords,
    _movie_source,
    _validated_stack,
    parse_stage,
)
from ttw_profile import plugin_stack_id  # noqa: E402


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes, flags: int = 0) -> bytes:
    return (
        signature.encode("ascii")
        + struct.pack("<I", len(data))
        + struct.pack("<I", flags)
        + struct.pack("<I", form_id)
        + bytes(8)
        + data
    )


def plugin_payload(masters: list[str], *records: bytes) -> bytes:
    header = b"".join(subrecord("MAST", name.encode("ascii") + b"\0") for name in masters)
    return record("TES4", 0, header) + b"".join(records)


class TtwFo3OpeningTests(unittest.TestCase):
    def test_ttw_command_dialects_are_admitted_exactly(self) -> None:
        cg00 = """
            PlayBink "Fallout INTRO Vsk.bik" 1 1 0 1
            SetLocationSpecificLoadScreensOnly 1
            SetInCharGen 1
            CG00DadREF.moveto CG00DadStartMarker
            CG00DoctorLiREF.moveto CG00DoctorLiStartMarker
            CG00MomREF.moveto CG00MomStartMarker
            setstage CG00 5
            player.moveto CG00PlayerStartMarker
            SetNumericGameSetting fKarmaModMurderingNonEvilNPC -100
            SetNumericGameSetting fKarmaModMurderingNonEvilCreature -25
        """
        commands = parse_stage(
            cg00,
            [
                "playBink",
                "setLocationSpecificLoadScreensOnly",
                "setInCharGen",
                "moveToReference",
                "moveToReference",
                "moveToReference",
                "setStage",
                "moveToReference",
                "setNumericGameSetting",
                "setNumericGameSetting",
            ],
            "CG00 stage 0",
        )
        self.assertEqual(commands[0]["arguments"], [1, 1, 0, 1])
        self.assertEqual(commands[8]["value"], -100)

        cg01_stage0 = """
            SetSoundSourceFile PHYBabyRattle "fx\\phy\\babyrattle\\"
            CG01DadREF.moveto CG01DadStartMarker
            setstage CG01 5
            player.setscale .4
            player.moveto CG01PlayerStartMarker
        """
        parsed = parse_stage(
            cg01_stage0,
            [
                "setSoundSourceFile",
                "moveToReference",
                "setStage",
                "setPlayerScale",
                "moveToReference",
            ],
            "CG01 stage 0",
        )
        self.assertEqual(parsed[0]["soundEditorId"], "PHYBabyRattle")
        self.assertEqual(parsed[3]["value"], 0.4)

    def test_cg01_stage5_control_and_movie_commands_are_typed(self) -> None:
        source = """
            SetLocationSpecificLoadScreensOnly 1
            SetInCharGen 1
            CG01DadRef.enable
            CG02DadRef.enable
            set CG01DadREF.doTalk to 1
            set CG01DadREF.talking to 0
            EnablePlayerControls 0 0 0 0 1
            DisablePlayerControls 1 1 1 1 0 0 1
            AutoDisplayObjectives 1
            SetNoActivationSound QSTBabyBabble
            SetPCToddler 1
            SetPCYoung 1
            playBink "1 year later.bik" 0 0 1 0
        """
        expected = [
            "setLocationSpecificLoadScreensOnly",
            "setInCharGen",
            "enable",
            "enable",
            "setScriptVariable",
            "setScriptVariable",
            "enablePlayerControls",
            "disablePlayerControls",
            "autoDisplayObjectives",
            "setNoActivationSound",
            "setPlayerToddler",
            "setPlayerYoung",
            "playBink",
        ]
        commands = parse_stage(source, expected, "CG01 stage 5")
        self.assertEqual(commands[6]["arguments"], [0, 0, 0, 0, 1])
        self.assertEqual(commands[7]["arguments"], [1, 1, 1, 1, 0, 0, 1])
        self.assertEqual(commands[12]["arguments"], [0, 0, 1, 0])

    def test_missing_ttw_command_fails_closed(self) -> None:
        with self.assertRaisesRegex(ValueError, "command dialect differs"):
            parse_stage(
                "CG01DadREF.moveto CG01DadStartMarker\nsetstage CG01 5",
                ["setSoundSourceFile", "moveToReference", "setStage"],
                "CG01 stage 0",
            )

    def test_effective_record_contract_records_override_provenance(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            base = root / "Fallout3.esm"
            ttw = root / "TaleOfTwoWastelands.esm"
            base.write_bytes(
                plugin_payload([], record("CELL", 0x00028138, subrecord("EDID", b"Vault101d\0")))
            )
            ttw.write_bytes(
                plugin_payload(
                    ["Fallout3.esm"],
                    record("CELL", 0x00028138, subrecord("EDID", b"Vault101d\0")),
                )
            )
            contexts = (
                PluginContext("Fallout3.esm", base, 0, (), ("Fallout3.esm",), file_sha256(base), base.stat().st_size),
                PluginContext(
                    "TaleOfTwoWastelands.esm",
                    ttw,
                    1,
                    ("Fallout3.esm",),
                    ("Fallout3.esm", "TaleOfTwoWastelands.esm"),
                    file_sha256(ttw),
                    ttw.stat().st_size,
                ),
            )
            effective = EffectiveRecords(
                contexts,
                {"fallout3.esm": 0, "taleoftwowastelands.esm": 0},
                {"fallout3.esm": 0, "taleoftwowastelands.esm": 1},
            )
            contract = effective.contract(
                {
                    "formKey": "Fallout3.esm:028138",
                    "recordType": "CELL",
                    "editorId": "Vault101d",
                    "winnerPlugin": "TaleOfTwoWastelands.esm",
                }
            )
            self.assertEqual(contract["winner"]["plugin"], "TaleOfTwoWastelands.esm")
            self.assertEqual(len(contract["overriddenVersions"]), 1)
            self.assertEqual(contract["runtimeFormId"], "00028138")

    def test_nested_movie_resolution_uses_highest_data_layer(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            lower = root / "lower"
            upper = root / "upper"
            (lower / "Video").mkdir(parents=True)
            (upper / "video").mkdir(parents=True)
            (lower / "Video" / "Age.bik").write_bytes(b"lower")
            (upper / "video" / "age.BIK").write_bytes(b"upper")
            source = _movie_source((lower, upper), "Video/Age.bik")
            self.assertEqual(source["winner"]["sourceRootIndex"], 1)
            self.assertEqual(source["winner"]["sha256"], hashlib.sha256(b"upper").hexdigest())
            self.assertEqual(len(source["overriddenVersions"]), 1)

    def test_validated_stack_rejects_plugin_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            plugins = [
                ("FalloutNV.esm", []),
                ("Fallout3.esm", ["FalloutNV.esm"]),
                ("TaleOfTwoWastelands.esm", ["FalloutNV.esm", "Fallout3.esm"]),
                (
                    "YUPTTW.esm",
                    ["FalloutNV.esm", "Fallout3.esm", "TaleOfTwoWastelands.esm"],
                ),
            ]
            rows = []
            for index, (name, masters) in enumerate(plugins):
                path = root / name
                path.write_bytes(plugin_payload(masters))
                rows.append(
                    {
                        "file": name,
                        "loadOrderIndex": index,
                        "sourceRootIndex": 0,
                        "bytes": path.stat().st_size,
                        "sha256": file_sha256(path),
                        "masters": masters,
                    }
                )
            load_order = root / "loadorder.txt"
            load_order.write_text("\n".join(name for name, _ in plugins) + "\n", encoding="utf-8")
            stack_id = plugin_stack_id(rows)
            profile = root / "profile.json"
            profile.write_text(
                json.dumps(
                    {
                        "schema": "opennv-ttw-profile/v1",
                        "status": "validated-generated-plugin-profile",
                        "kind": "ttw",
                        "sourceRoots": [str(root)],
                        "loadOrderSource": {
                            "file": str(load_order),
                            "sha256": file_sha256(load_order),
                        },
                        "plugins": rows,
                        "pluginStackId": stack_id,
                        "saveCompatibilityId": f"ttw:{stack_id}",
                    }
                ),
                encoding="utf-8",
            )
            _, _, contexts, _ = _validated_stack(profile)
            self.assertEqual(len(contexts), 4)
            (root / "YUPTTW.esm").write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "bytes or hash changed"):
                _validated_stack(profile)


if __name__ == "__main__":
    unittest.main()
