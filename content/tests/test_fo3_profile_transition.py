from __future__ import annotations

import struct
import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from plugin_records import Record  # noqa: E402
from prepare_fo3_profile import _compile_cg00_section4_transition  # noqa: E402


PACKAGE_FORM = 0x0006A818
LOCATION_FORM = 0x00039562
SECTION4_IDLE_FORM = 0x00069EFC
SECTION5_IDLE_FORM = 0x00069EFD


def subrecord(signature: str, data: bytes = b"") -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def idle(form_id: int, editor_id: str, model: str) -> Record:
    return Record(
        "IDLE",
        form_id,
        0,
        subrecord("EDID", editor_id.encode("ascii") + b"\0")
        + subrecord("MODL", model.encode("ascii") + b"\0"),
        (),
    )


def synthetic_records() -> tuple[Record, ...]:
    package = Record(
        "PACK",
        PACKAGE_FORM,
        0,
        b"".join(
            (
                subrecord("EDID", b"CG00PlayerSection4\0"),
                subrecord("PKDT", struct.pack("<IBBHHH", 0x1004, 6, 0, 0, 0, 0)),
                subrecord("PLDT", struct.pack("<III", 0, LOCATION_FORM, 0)),
                subrecord("IDLF", b"\x01"),
                subrecord("IDLC", struct.pack("<I", 1)),
                subrecord("IDLT", struct.pack("<f", 0.0)),
                subrecord("IDLA", struct.pack("<I", SECTION4_IDLE_FORM)),
                subrecord("POBA"),
                subrecord("INAM", struct.pack("<I", SECTION4_IDLE_FORM)),
                subrecord("POEA"),
                subrecord("INAM", struct.pack("<I", 0)),
                subrecord("POCA"),
                subrecord("INAM", struct.pack("<I", SECTION5_IDLE_FORM)),
            )
        ),
        (),
    )
    dad_source = (
        "scn CG00DadSCRIPT\r\nbegin gamemode\r\n"
        "if getStage CG00 >= 60 && GetStageDone CG00 65 == 0\r\n"
        "setstage CG00 65\r\nendif\r\nend\r\n"
    )
    dad_script = Record(
        "SCPT",
        0x0002C9F6,
        0,
        subrecord("EDID", b"CG00DadSCRIPT\0")
        + subrecord("SCTX", dad_source.encode("cp1252") + b"\0"),
        (),
    )
    return (
        package,
        idle(
            SECTION4_IDLE_FORM,
            "LooseCG00PlayerSection04",
            r"Characters\_Male\IdleAnims\CG00PlayerSection04.kf",
        ),
        idle(
            SECTION5_IDLE_FORM,
            "LooseCG00PlayerSection05",
            r"Characters\_Male\IdleAnims\CG00PlayerSection05.kf",
        ),
        dad_script,
    )


def selection() -> dict[str, object]:
    return {
        "appearanceStage": 60,
        "section4Package": {
            "editorId": "CG00PlayerSection4",
            "formId": "0006a818",
            "locationReferenceEditorId": "CG00PlayerStartMarker",
            "locationReferenceFormId": "00039562",
        },
    }


class Fo3ProfileTransitionTest(unittest.TestCase):
    def test_compiles_owned_package_activation_and_stops_before_stage_65(self) -> None:
        stage_65 = "\n".join(
            (
                "CG01DadREF.MatchRace player",
                "CG02DadREF.MatchFaceGeometry player CGMatchFace",
            )
        )
        result = _compile_cg00_section4_transition(
            synthetic_records(),
            selection(),
            62,
            "player.addScriptPackage CG00PlayerSection4",
            {65: [stage_65]},
        )

        self.assertEqual("0006a818", result["package"]["formId"])
        self.assertEqual("00039562", result["package"]["location"]["referenceFormId"])
        self.assertEqual(65, result["nextStageTrigger"]["targetStage"])
        self.assertFalse(result["nextStageResult"]["runtimeReady"])
        self.assertEqual(
            ["matchRace", "matchFaceGeometry"],
            [command["kind"] for command in result["nextStageResult"]["commands"]],
        )

    def test_rejects_unknown_next_stage_command(self) -> None:
        with self.assertRaisesRegex(ValueError, "unsupported command"):
            _compile_cg00_section4_transition(
                synthetic_records(),
                selection(),
                62,
                "player.addScriptPackage CG00PlayerSection4",
                {65: ["player.moveto CG01DadREF"]},
            )


if __name__ == "__main__":
    unittest.main()
