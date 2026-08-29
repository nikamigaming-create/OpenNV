from __future__ import annotations

import struct
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from plugin_records import GroupContext, Record  # noqa: E402
from prepare_fo3_profile import (  # noqa: E402
    _compile_cg00_section4_transition,
    _compile_post_stage65_dialogue,
    _compile_post_stage80_dialogue,
    _compile_stage65_appearance_contract,
    _float_contract,
)


PACKAGE_FORM = 0x0006A818
LOCATION_FORM = 0x00039562
SECTION4_IDLE_FORM = 0x00069EFC
SECTION5_IDLE_FORM = 0x00069EFD
QUEST_FORM = 0x0001F388
TOPIC_FORM = 0x0001F378
VOICE_FORM = 0x00019FDF


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


def condition(
    function: int,
    parameter1: int,
    *,
    operator_flags: int = 0,
    comparison: float = 1.0,
    run_on: int = 0,
) -> bytes:
    return struct.pack(
        "<B3xfH2xIIII",
        operator_flags,
        comparison,
        function,
        parameter1,
        0,
        run_on,
        0,
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
    def test_compiles_sex_specific_info_results_and_exact_stage80_commands(self) -> None:
        topic = Record(
            "DIAL",
            TOPIC_FORM,
            0,
            subrecord("EDID", b"CG00DadSpeech\0")
            + subrecord("QSTI", struct.pack("<I", QUEST_FORM)),
            (),
        )
        voice = Record(
            "VTYP",
            VOICE_FORM,
            0,
            subrecord("EDID", b"MaleUniqueDad\0"),
            (),
        )

        def info(form_id: int, sex: int) -> Record:
            return Record(
                "INFO",
                form_id,
                0,
                subrecord("QSTI", struct.pack("<I", QUEST_FORM))
                + subrecord("SCTX", b"setstage CG00 80\0")
                + subrecord(
                    "NAM1",
                    (
                        b"Owned authored response for the focused compiler test.\0"
                    ),
                )
                + subrecord("CTDA", condition(70, sex, run_on=1))
                + subrecord("CTDA", condition(427, VOICE_FORM))
                + subrecord(
                    "CTDA",
                    condition(58, QUEST_FORM, operator_flags=0x80, comparison=80.0),
                ),
                (GroupContext(struct.pack("<I", TOPIC_FORM), 7),),
            )

        stage80_source = "\n".join(
            (
                "player.addScriptPackage CG00PlayerSection5",
                "set CG00DadREF.doTalk to 1",
                "set CG00MomREF.doTalk to 1",
                "CG00DadREF.evp",
                "CG00MomREF.evp",
                "CG00DoctorLiREF.evp",
                "CG01DadRef.enable",
            )
        )
        selection = {
            "postStage65Dialogue": {
                "topicEditorId": "CG00DadSpeech",
                "topicFormId": f"{TOPIC_FORM:08x}",
                "resultInfoFormIds": ["0001f37f", "0001f380"],
                "targetStage": 80,
            }
        }
        result = _compile_post_stage65_dialogue(
            (topic, voice, info(0x0001F37F, 1), info(0x0001F380, 0)),
            selection,
            QUEST_FORM,
            {80: [stage80_source]},
        )

        self.assertEqual(
            {"female": "0001f37f", "male": "0001f380"},
            {branch["engineSex"]: branch["infoFormId"] for branch in result["branches"]},
        )
        self.assertFalse(result["dialoguePlaybackImplemented"])
        self.assertEqual(
            {1},
            {branch["response"]["index"] for branch in result["branches"]},
        )
        self.assertTrue(
            all(branch["response"]["textSha256"] for branch in result["branches"])
        )
        self.assertEqual(
            [
                "addScriptPackage",
                "setScriptVariable",
                "setScriptVariable",
                "evaluatePackage",
                "evaluatePackage",
                "evaluatePackage",
                "enable",
            ],
            [command["kind"] for command in result["stageResult"]["commands"]],
        )

    def test_compiles_post_stage80_info_and_empty_stage85_result(self) -> None:
        topic = Record(
            "DIAL",
            TOPIC_FORM,
            0,
            subrecord("EDID", b"CG00DadSpeech\0")
            + subrecord("QSTI", struct.pack("<I", QUEST_FORM)),
            (),
        )
        voice = Record(
            "VTYP",
            VOICE_FORM,
            0,
            subrecord("EDID", b"MaleUniqueDad\0"),
            (),
        )
        info = Record(
            "INFO",
            0x0001F37B,
            0,
            subrecord("QSTI", struct.pack("<I", QUEST_FORM))
            + subrecord("SCTX", b"setstage CG00 85\0")
            + subrecord("CTDA", condition(427, VOICE_FORM))
            + subrecord(
                "CTDA",
                condition(58, QUEST_FORM, operator_flags=0x60, comparison=80.0),
            ),
            (GroupContext(struct.pack("<I", TOPIC_FORM), 7),),
        )
        selection = {
            "postStage80Dialogue": {
                "topicEditorId": "CG00DadSpeech",
                "topicFormId": f"{TOPIC_FORM:08x}",
                "resultInfoFormId": "0001f37b",
                "targetStage": 85,
            }
        }

        dialogue, transition = _compile_post_stage80_dialogue(
            (topic, voice, info),
            selection,
            QUEST_FORM,
            {85: ["; beginning of emergency\r\n\r\n"]},
        )

        self.assertEqual("0001f37b", dialogue["info"]["formId"])
        self.assertEqual(85, dialogue["targetStage"])
        self.assertFalse(dialogue["dialoguePlaybackImplemented"])
        self.assertEqual([], dialogue["stageResult"]["commands"])
        self.assertEqual(0, transition["accountedCommandCount"])
        self.assertEqual(
            "fo3-cg00-post-stage-85-dialogue-trigger-not-compiled",
            transition["nextBoundary"],
        )

    def test_compiles_owned_package_activation_and_stops_before_stage_65(self) -> None:
        stage_65 = "\n".join(
            (
                "CG01DadREF.MatchRace player",
                "CG01DadREF.MatchFaceGeometry player CGMatchFace",
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

    def test_stage65_applies_owned_race_and_half_face_geometry(self) -> None:
        parent_base_form = 0x100
        parent_reference_form = 0x101
        race_form = 0x200
        zero_symmetric = (0.0,) * 50
        zero_asymmetric = (0.0,) * 30
        race_symmetric = (0.2,) * 50
        race_asymmetric = (0.2,) * 30
        race_texture = (0.3,) * 50
        player_symmetric = (0.8,) * 50
        player_asymmetric = (0.8,) * 30
        player_texture = (0.4,) * 50
        parent = SimpleNamespace(
            form_id=parent_base_form,
            editor_id="SyntheticDad",
            female=False,
            race_form_id=race_form,
            face_symmetric_geometry=zero_symmetric,
            face_asymmetric_geometry=zero_asymmetric,
        )
        race = SimpleNamespace(
            male_face_symmetric_geometry=race_symmetric,
            male_face_asymmetric_geometry=race_asymmetric,
            male_face_symmetric_texture=race_texture,
        )
        catalog = SimpleNamespace(
            actors={parent_base_form: parent},
            races={race_form: race},
            record_data_sha256={"NPC_": {parent_base_form: "a" * 64}},
        )
        records = [
            Record(
                "ACHR",
                parent_reference_form,
                0,
                subrecord("EDID", b"SyntheticDadREF\0")
                + subrecord("NAME", struct.pack("<I", parent_base_form)),
                (),
            ),
            Record(
                "GLOB",
                0x300,
                0,
                subrecord("EDID", b"CGMatchFace\0")
                + subrecord("FNAM", b"s")
                + subrecord("FLTV", struct.pack("<f", 50.0)),
                (),
            ),
        ]
        facegen = {
            "symmetricGeometry": _float_contract(player_symmetric, 50),
            "asymmetricGeometry": _float_contract(player_asymmetric, 30),
            "symmetricTexture": _float_contract(player_texture, 50),
        }
        races = [
            {
                "formId": f"{race_form:08x}",
                "sex": {
                    "male": {"faceGenDefaults": facegen},
                    "female": {"faceGenDefaults": facegen},
                },
            }
        ]
        character_selection = {
            "section4Transition": {
                "sourceStage": 62,
                "nextStageResult": {
                    "stage": 65,
                    "stageSourceSha256": "b" * 64,
                    "runtimeReady": False,
                    "blocker": "synthetic-blocker",
                    "commands": [
                        {
                            "kind": "matchRace",
                            "subject": "SyntheticDadREF",
                            "target": "player",
                        },
                        {
                            "kind": "matchFaceGeometry",
                            "subject": "SyntheticDadREF",
                            "target": "player",
                            "template": "CGMatchFace",
                        },
                    ],
                },
            }
        }

        result = _compile_stage65_appearance_contract(
            catalog,
            records,
            character_selection,
            races,
        )

        self.assertEqual(2, result["accountedCommandCount"])
        self.assertEqual(2, len(result["selectionResults"]))
        parent_result = result["selectionResults"][0]["parents"][0]
        self.assertEqual(0.5, parent_result["faceGen"]["symmetricGeometry"]["values"][0])
        self.assertEqual(0.3, parent_result["faceGen"]["symmetricTexture"]["values"][0])
        self.assertTrue(
            character_selection["section4Transition"]["nextStageResult"]["runtimeReady"]
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
