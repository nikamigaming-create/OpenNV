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
    _compile_cg01_stage0_transition,
    _compile_cg00_section4_transition,
    _compile_post_stage65_dialogue,
    _compile_post_stage80_dialogue,
    _compile_post_stage85_dialogue,
    _compile_stage65_appearance_contract,
    _compile_stage100_transition,
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

    def test_compiles_post_stage85_info_and_exact_stage90_result(self) -> None:
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
            0x0001F379,
            0,
            subrecord("QSTI", struct.pack("<I", QUEST_FORM))
            + subrecord("NAM1", b"Hang on, Catherine! Hang on....\0")
            + subrecord("CTDA", condition(427, VOICE_FORM))
            + subrecord(
                "CTDA",
                condition(58, QUEST_FORM, operator_flags=0x60, comparison=80.0),
            )
            + subrecord("SCTX", b"setstage CG00 90\0")
            + subrecord("NEXT"),
            (GroupContext(struct.pack("<I", TOPIC_FORM), 7),),
        )
        quest_script = Record(
            "SCPT",
            0x0003A17C,
            0,
            subrecord("EDID", b"CG00SCRIPT\0")
            + subrecord("SCTX", b"float timer\r\nshort runTimer\r\n\0"),
            (),
        )
        image_space = Record(
            "IMAD",
            0x0002D14C,
            0,
            subrecord("EDID", b"FadeToWhiteAndBackISFX\0")
            + subrecord("DNAM", struct.pack("<If", 1, 8.0))
            + subrecord(
                "NAM3",
                struct.pack(
                    "<10f",
                    0.0,
                    1.0,
                    1.0,
                    1.0,
                    0.0,
                    1.0,
                    1.0,
                    1.0,
                    1.0,
                    0.0,
                ),
            ),
            (),
        )
        sound = Record(
            "SOUN",
            0x000BC424,
            0,
            subrecord("EDID", b"QSTFadeToWhiteA\0")
            + subrecord("FNAM", b"fx\\qst\\qst_fadetowhite_a.wav\0")
            + subrecord("SNDD", bytes(36)),
            (),
        )
        selection = {
            "postStage85Dialogue": {
                "topicEditorId": "CG00DadSpeech",
                "topicFormId": f"{TOPIC_FORM:08x}",
                "resultInfoFormId": "0001f379",
                "minimumStage": 80,
                "targetStage": 90,
            }
        }
        stage90_source = "\n".join(
            (
                "set CG00.timer to 2.2",
                "set CG00.runTimer to 1",
                "imod FadeToWhiteAndBackISFX",
                "playSound QSTFadeToWhiteA",
            )
        )

        dialogue, transition = _compile_post_stage85_dialogue(
            (topic, voice, info, quest_script, image_space, sound),
            selection,
            QUEST_FORM,
            quest_script,
            {90: [stage90_source]},
        )

        self.assertEqual("0001f379", dialogue["branches"][0]["infoFormId"])
        self.assertEqual(1, dialogue["branches"][0]["continuationMarkerCount"])
        self.assertEqual(90, dialogue["targetStage"])
        self.assertEqual(
            [
                "setQuestVariable",
                "setQuestVariable",
                "applyImageSpaceModifier",
                "playSound",
            ],
            [command["kind"] for command in transition["commands"]],
        )
        self.assertEqual(
            "0002d14c",
            transition["commands"][2]["modifier"]["formId"],
        )
        self.assertEqual(
            "sound\\fx\\qst\\qst_fadetowhite_a.wav",
            transition["commands"][3]["sound"]["logicalPath"],
        )

    def test_compiles_stage100_boundary_and_nested_cg01_stage5_closure(self) -> None:
        quest_script_source = "\n".join(
            (
                "scn CG00SCRIPT",
                "float timer",
                "short runTimer",
                "begin gamemode",
                "if runTimer == 1",
                "if timer > 0",
                "set timer to timer - GetSecondsPassed",
                "else",
                "if getstage CG00 == 5",
                "setstage CG00 6",
                "elseif getstage CG00 == 6",
                "setstage CG00 8",
                "elseif getstage CG00 == 90",
                "setstage CG00 100",
                "endif",
                "endif",
                "endif",
                "if chooseSex == 1",
                "endif",
                "end",
            )
        )
        quest_script = Record(
            "SCPT",
            0x0003A17C,
            0,
            subrecord("EDID", b"CG00SCRIPT\0")
            + subrecord("SCTX", quest_script_source.encode("cp1252") + b"\0"),
            (),
        )
        actor_script = lambda form_id, editor_id: Record(
            "SCPT",
            form_id,
            0,
            subrecord("EDID", editor_id.encode("ascii") + b"\0")
            + subrecord("SCTX", b"short doTalk\0"),
            (),
        )
        mom_script = actor_script(0x0005EDDD, "CG00MomSCRIPT")
        dad_script = actor_script(0x0002C9F6, "CG00DadSCRIPT")
        mom_base = Record(
            "NPC_",
            0x0005EDDF,
            0,
            subrecord("EDID", b"CG00Mom\0")
            + subrecord("SCRI", struct.pack("<I", mom_script.form_id)),
            (),
        )
        dad_base = Record(
            "NPC_",
            0x000290A6,
            0,
            subrecord("EDID", b"CG00Dad\0")
            + subrecord("SCRI", struct.pack("<I", dad_script.form_id)),
            (),
        )
        mom_ref = Record(
            "ACHR",
            0x0005EDE0,
            0,
            subrecord("EDID", b"CG00MomREF\0")
            + subrecord("NAME", struct.pack("<I", mom_base.form_id)),
            (),
        )
        dad_ref = Record(
            "ACHR",
            0x000290A7,
            0,
            subrecord("EDID", b"CG00DadREF\0")
            + subrecord("NAME", struct.pack("<I", dad_base.form_id)),
            (),
        )
        cg00 = Record(
            "QUST",
            QUEST_FORM,
            0,
            subrecord("EDID", b"CG00\0"),
            (),
        )
        cell_groups = (GroupContext(struct.pack("<I", 0x00028138), 6),)

        def placed_reference(
            signature: str,
            form_id: int,
            editor_id: str,
            base_form_id: int,
            flags: int,
            transform: tuple[float, float, float, float, float, float],
        ) -> Record:
            return Record(
                signature,
                form_id,
                flags,
                subrecord("EDID", editor_id.encode("ascii") + b"\0")
                + subrecord("NAME", struct.pack("<I", base_form_id))
                + subrecord("DATA", struct.pack("<6f", *transform)),
                cell_groups,
            )

        cg01_dad_script = Record(
            "SCPT",
            0x0002EA3B,
            0,
            subrecord("EDID", b"CG01DadSCRIPT\0")
            + subrecord("SCTX", b"short doTalk\nshort talking\0"),
            (),
        )
        cg01_script = Record(
            "SCPT",
            0x00030769,
            0,
            subrecord("EDID", b"CG01SCRIPT\0") + subrecord("SCTX", b"short runTimer\0"),
            (),
        )
        cg01_dad_base = Record(
            "NPC_",
            0x0002EA46,
            0,
            subrecord("EDID", b"CG01Dad\0")
            + subrecord("SCRI", struct.pack("<I", cg01_dad_script.form_id)),
            (),
        )
        cg02_dad_base = Record(
            "NPC_",
            0x0002FDCF,
            0,
            subrecord("EDID", b"CG02Dad\0"),
            (),
        )
        marker_base = Record(
            "STAT",
            0x00000034,
            0,
            subrecord("EDID", b"XMarkerHeading\0"),
            (),
        )
        cg01_dad_ref = placed_reference(
            "ACHR",
            0x0002EA4D,
            "CG01DadREF",
            cg01_dad_base.form_id,
            0x00000C00,
            (-2620.4392, -5482.714, 7424.0, 0.0, 0.0, 3.1415935),
        )
        cg01_dad_marker = placed_reference(
            "REFR",
            0x0002EA4E,
            "CG01DadStartMarker",
            marker_base.form_id,
            0x00000400,
            (-2601.1814, -5426.611, 7424.0, 0.0, 0.0, 3.175226),
        )
        cg01_player_marker = placed_reference(
            "REFR",
            0x0002EA4F,
            "CG01PlayerStartMarker",
            marker_base.form_id,
            0x00000400,
            (-2582.588, -5798.251, 7424.0, 0.0, 0.0, 0.0460234),
        )
        cg02_dad_ref = placed_reference(
            "ACHR",
            0x000300EF,
            "CG02DadREF",
            cg02_dad_base.form_id,
            0x00000C00,
            (1815.2443, -10371.58, 7552.0, 0.0, 0.0, 0.2124003),
        )
        baby_babble = Record(
            "SOUN",
            0x00089B4C,
            0,
            subrecord("EDID", b"QSTBabyBabble\0")
            + subrecord("FNAM", b"fx\\qst\\baby\\babble\\\0")
            + subrecord("SNDD", bytes(36)),
            (),
        )
        cg01_source = "\n".join(
            (
                "CG01DadREF.moveto CG01DadStartMarker",
                "setstage CG01 5",
                "player.setscale .4",
                "player.moveto CG01PlayerStartMarker",
            )
        )
        cg01_stage5_source = "\n".join(
            (
                "SetLocationSpecificLoadScreensOnly 1",
                "SetInCharGen 1 ; no leveling during chargen",
                "CG01DadRef.enable",
                "CG02DadRef.enable",
                "set CG01DadREF.doTalk to 1",
                "set CG01DadREF.talking to 0",
                "EnablePlayerControls 0 0 0 0 1",
                "DisablePlayerControls 1 1 1 1 0 0 1",
                "AutoDisplayObjectives 1",
                "SetNoActivationSound QSTBabyBabble",
                "SetPCToddler 1",
                "SetPCYoung 1",
                'playBink "1 year later.bik" 0 0 1 0',
            )
        )
        cg01 = Record(
            "QUST",
            0x00014E83,
            0,
            subrecord("EDID", b"CG01\0")
            + subrecord("SCRI", struct.pack("<I", cg01_script.form_id))
            + subrecord("INDX", struct.pack("<H", 0))
            + subrecord("SCTX", cg01_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 5))
            + subrecord("SCTX", cg01_stage5_source.encode("cp1252") + b"\0"),
            (),
        )
        modifier = Record(
            "IMAD",
            0x00035A20,
            0,
            subrecord("EDID", b"CG00BirthBaseISFX\0"),
            (),
        )
        stage100_source = "\n".join(
            (
                "player.removescriptpackage",
                "set CG00MomREF.doTalk to 0",
                "set CG00DadREF.doTalk to 0",
                "rimod CG00BirthBaseISFX",
                "CG00DadREF.disable",
                "stopQuest CG00",
                "SetPCYoung 1",
                "setstage CG01 0",
            )
        )
        selection = {
            "stage100Transition": {
                "sourceStage": 90,
                "targetStage": 100,
                "removedImageSpaceModifierEditorId": "CG00BirthBaseISFX",
                "removedImageSpaceModifierFormId": "00035a20",
                "nextQuestEditorId": "CG01",
                "nextQuestFormId": "00014e83",
                "nextQuestStage": 0,
            },
            "cg01Stage0Transition": {
                "questEditorId": "CG01",
                "questFormId": "00014e83",
                "entryStage": 0,
                "nestedStage": 5,
                "cellFormId": "00028138",
                "dadReferenceFormId": "0002ea4d",
                "dadStartMarkerFormId": "0002ea4e",
                "playerStartMarkerFormId": "0002ea4f",
                "nextDadReferenceFormId": "000300ef",
                "noActivationSoundFormId": "00089b4c",
                "transitionVideo": "1 year later.bik",
            },
        }

        records = (
            quest_script,
            mom_script,
            dad_script,
            mom_base,
            dad_base,
            mom_ref,
            dad_ref,
            cg00,
            cg01,
            cg01_script,
            cg01_dad_script,
            cg01_dad_base,
            cg02_dad_base,
            marker_base,
            cg01_dad_ref,
            cg01_dad_marker,
            cg01_player_marker,
            cg02_dad_ref,
            baby_babble,
            modifier,
        )
        contract = _compile_stage100_transition(
            records,
            selection,
            QUEST_FORM,
            quest_script,
            quest_script_source,
            {100: [stage100_source]},
        )

        self.assertEqual(100, contract["stage"])
        self.assertEqual(8, contract["accountedCommandCount"])
        self.assertEqual(7, contract["appliedCommandCount"])
        self.assertEqual("000290a7", contract["commands"][4]["referenceFormId"])
        self.assertEqual("00014e83", contract["nextBoundary"]["questFormId"])
        self.assertFalse(contract["nextBoundary"]["applied"])

        cg01_contract = _compile_cg01_stage0_transition(records, selection, contract)
        self.assertEqual(
            "opennv-fo3-cg01-stage-0-to-5-transition/v1",
            cg01_contract["schema"],
        )
        self.assertEqual(17, cg01_contract["accountedCommandCount"])
        stage0 = cg01_contract["stage0Result"]
        self.assertEqual(4, stage0["accountedCommandCount"])
        self.assertEqual(
            ["moveToReference", "setStage", "setPlayerScale", "moveToReference"],
            [command["kind"] for command in stage0["commands"]],
        )
        self.assertEqual(0.4, stage0["commands"][2]["value"])
        self.assertEqual(
            "0002ea4e",
            stage0["commands"][0]["target"]["formId"],
        )
        self.assertEqual(
            "0002ea4f",
            stage0["commands"][3]["target"]["formId"],
        )
        self.assertAlmostEqual(
            -2601.1814,
            stage0["commands"][0]["target"]["sourceTransform"]["positionGameUnits"][0],
            places=3,
        )
        stage5 = stage0["commands"][1]["stageResult"]
        self.assertEqual(13, stage5["accountedCommandCount"])
        self.assertEqual(
            [
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
            ],
            [command["kind"] for command in stage5["commands"]],
        )
        self.assertEqual([0, 0, 0, 0, 1], stage5["commands"][6]["arguments"])
        self.assertEqual([1, 1, 1, 1, 0, 0, 1], stage5["commands"][7]["arguments"])
        self.assertEqual("00089b4c", stage5["commands"][9]["sound"]["formId"])
        self.assertEqual("1 year later.bik", stage5["commands"][12]["logicalPath"])
        self.assertEqual([0, 0, 1, 0], stage5["commands"][12]["arguments"])
        moved_dad = stage0["commands"][0]["subject"]["formId"]
        self.assertEqual(moved_dad, stage5["commands"][2]["reference"]["formId"])
        self.assertEqual(moved_dad, stage5["commands"][4]["reference"]["formId"])
        self.assertFalse(cg01_contract["nextBoundary"]["applied"])

        cg01_without_stage5 = Record(
            "QUST",
            cg01.form_id,
            0,
            subrecord("EDID", b"CG01\0")
            + subrecord("SCRI", struct.pack("<I", cg01_script.form_id))
            + subrecord("INDX", struct.pack("<H", 0))
            + subrecord("SCTX", cg01_source.encode("cp1252") + b"\0"),
            (),
        )
        records_without_stage5 = tuple(
            cg01_without_stage5 if record is cg01 else record for record in records
        )
        with self.assertRaisesRegex(ValueError, "CG01 stage 5 result is ambiguous"):
            _compile_cg01_stage0_transition(records_without_stage5, selection, contract)

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
