from __future__ import annotations

import hashlib
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module
from types import SimpleNamespace


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from plugin_records import GroupContext, Record  # noqa: E402
from prepare_fo3_profile import (  # noqa: E402
    FORM_ID_RADIX,
    _bind_cg01_toddler_world,
    _bind_cg01_transition_video,
    _cg00_package_playback_contract,
    _cg00_package_stage_condition,
    _compile_cg01_stage0_transition,
    _compile_cg00_section4_transition,
    _compile_post_stage65_dialogue,
    _compile_post_stage80_dialogue,
    _compile_post_stage85_dialogue,
    _compile_stage65_appearance_contract,
    _compile_stage100_transition,
    _fallout_default_fov_projection,
    _float_contract,
    _special_book_menu_tile_contract,
    load_recipe,
)


PACKAGE_FORM = 0x0006A818
LOCATION_FORM = 0x00039562
SECTION4_IDLE_FORM = 0x00069EFC
SECTION5_IDLE_FORM = 0x00069EFD
QUEST_FORM = 0x0001F388
TOPIC_FORM = 0x0001F378
VOICE_FORM = 0x00019FDF
CG01_DAD_TRIGGER_SCRIPT_FORM = 0x00081983
CG01_DAD_TRIGGER_BASE_FORM = 0x00081984
CG01_DAD_TRIGGER_REFERENCE_FORM = 0x0002EA54
CG01_DAD_TALK_IDLE_FORM = 0x0003D33B
CG01_DAD_BECKON_FEMALE_IDLE_FORM = 0x0005943A
CG01_DAD_BECKON_MALE_IDLE_FORM = 0x0005943B
CG01_DAD_WALKED_IDLE_FORM = 0x0005943D
CG01_WALK_OBJECTIVE_INDEX = 10
CG01_WALK_TARGET_STAGE = 12
CG01_TRIGGER_COLLISION_LAYERS = 12
CG01_TRIGGER_PRIMITIVE_TYPE = 2
CG01_TRIGGER_PRIMITIVE = (69.24656, 69.24656, 69.24656, 0.8, 0.298039, 0.15, 0.0)
CG01_TRIGGER_TRANSFORM = (-2600.9722, -5436.599, 7432.794, 0.0, 0.0, 0.0)
FO3_RECIPE = Path(__file__).resolve().parents[1] / "recipes" / (
    "fo3-goty-opening-profile-v1.json"
)
FO3_OPENING_FLOW = Path(__file__).resolve().parents[2] / "runtime" / "src" / (
    "Campaigns/Fallout3/Fo3OpeningFlow.cs"
)
FO3_CG00_EARLY_RUNTIME = Path(__file__).resolve().parents[2] / "runtime" / "src" / (
    "Campaigns/Fallout3/Fo3Cg00EarlyBirthRuntime.cs"
)


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
    def test_compiles_owned_special_book_menu_tile_hierarchy(self) -> None:
        bindings = "".join(
            f'<{action}><ref src="{tile}" trait="clicked"/></{action}>'
            for action, tile in (
                ("xbuttonrt", "next_page"), ("xbuttonlt", "previous_page"),
                ("xright", "increase_value"), ("xleft", "decrease_value"),
                ("xup", "index_up"), ("xdown", "index_down"),
                ("xbuttonx", "exit_menu"),
            )
        )
        controls = "".join(
            f'<{kind} name="{name}"><id>{index}</id><width>1</width><height>1</height>'
            f'<_x>100</_x><_y>200</_y><visible>&true;</visible><target>&true;</target>'
            f'</{kind}>'
            for index, (kind, name) in enumerate((
                ("hotrect", "index_up"), ("hotrect", "index_down"),
                ("hotrect", "clickable"), ("image", "next_page"),
                ("image", "previous_page"), ("image", "increase_value"),
                ("image", "decrease_value"), ("image", "exit_menu"),
            ))
        )
        payload = (
            '<menu name="SPECIALBookMenu"><class>&SPECIALBookMenu;</class>'
            '<stackingtype>&no_click_past;</stackingtype><alpha>0</alpha>'
            '<locus>&true;</locus><menufade>0.25</menufade>'
            '<systemcolor>&hudmain;</systemcolor>' + bindings + controls + '</menu>'
        ).encode("cp1252")
        contract = _special_book_menu_tile_contract(
            SimpleNamespace(data=payload, sha256="a" * 64))
        self.assertEqual("SPECIALBookMenu", contract["menuName"])
        self.assertEqual(8, len(contract["controls"]))
        self.assertEqual(7, len(contract["bindings"]))
        self.assertEqual("increase_value", contract["bindings"][2]["tile"])

    def test_capture_creates_fresh_output_directory_before_png_write(self) -> None:
        source = read_csharp_source_module(FO3_OPENING_FLOW)
        capture = source[source.index("private async Task<Fo3AppearanceProofCapture>") :]

        self.assertLess(
            capture.index("Directory.CreateDirectory(captureRoot);"),
            capture.index("image.SavePng(path);"),
        )

    def test_default_fov_uses_owned_four_by_three_horizontal_convention(self) -> None:
        projection = _fallout_default_fov_projection(75.0)

        self.assertEqual(75.0, projection["sourceHorizontalFovDegrees"])
        self.assertEqual(0.75, projection["referenceAspectHeightOverWidth"])
        self.assertAlmostEqual(59.840444, projection["verticalFovDegrees"], places=5)
        self.assertEqual("keep-height", projection["godotKeepAspect"])

    def test_cg00_package_playback_preserves_owned_change_idle_event(self) -> None:
        records = synthetic_records()
        package = records[0]
        contract = _cg00_package_playback_contract(
            package,
            {record.form_id: record for record in records},
        )

        self.assertEqual(
            {"flags": 1, "count": 1, "timerSeconds": 0.0},
            contract["idleSelection"],
        )
        self.assertEqual(
            {"begin": "00069efc", "end": None, "change": "00069efd"},
            contract["events"],
        )

    def test_cg00_actor_package_condition_binds_exact_quest_stage(self) -> None:
        package = Record(
            "PACK",
            0x0008F778,
            0,
            subrecord("EDID", b"CG00DadSection0\0")
            + subrecord(
                "CTDA",
                condition(
                    58,
                    QUEST_FORM,
                    operator_flags=0x60,
                    comparison=8.0,
                ),
            ),
            (),
        )

        self.assertEqual(
            {
                "function": "GetStage",
                "functionId": 58,
                "operator": "equal",
                "operatorFlags": 0x60,
                "questFormId": "0001f388",
                "stage": 8,
                "runOn": 0,
            },
            _cg00_package_stage_condition(package, QUEST_FORM),
        )

    def test_cg00_actor_packages_switch_by_stage_condition_not_idle_stop(self) -> None:
        source = FO3_CG00_EARLY_RUNTIME.read_text(encoding="utf-8")

        self.assertIn("GamebryoPackageSelector.SelectFirst", source)
        self.assertIn("GamebryoPackageComparison.Equal", source)
        self.assertIn("selected.Value.AnimationStartSeconds", source)
        self.assertNotIn("section == 0 && _cg00ActorPackages.Count == 0", source)
        self.assertNotIn(
            "while (playback.ElapsedSeconds >= playback.Contract.AnimationStopSeconds)",
            source,
        )

    def test_recipe_pins_ffmpeg2theora_video_import(self) -> None:
        video_import = dict(load_recipe(FO3_RECIPE)["videoImport"])

        self.assertEqual("ffmpeg2theora", video_import["transcoderKind"])
        self.assertEqual(
            "a1e0f97bde8b1b8874480a2f153651258e0f35b86d1d24a8a911bd4a841b8308",
            video_import["transcoderSha256"],
        )
        self.assertTrue(video_import["disableSkeleton"])
        self.assertTrue(video_import["stripMetadata"])

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
            + subrecord(
                "SCTX",
                "\n".join(
                    (
                        "scn CG01DadSCRIPT",
                        "short doTalk",
                        "short talking",
                        "float timer",
                        "begin gamemode",
                        "if doTalk == 1 && talking == 0",
                        "if timer > 0",
                        "set timer to timer - GetSecondsPassed",
                        "else",
                        "SayTo player CG01DadSpeech 1",
                        "set talking to 1",
                        "endif",
                        "endif",
                        "end",
                        "begin OnPackageDone CG01DadCloseDoor",
                        "setstage CG01 18",
                        "end",
                        "begin OnPackageDone CG01DadReturn",
                        "setstage CG01 72",
                        "end",
                        "begin SayToDone CG01DadSpeech",
                        "set talking to 0",
                        "look player",
                        "if getStage CG01 == 75",
                        "setstage CG01 80",
                        "endif",
                        "end",
                    )
                ).encode("cp1252")
                + b"\0",
            ),
            (),
        )
        cg01_script = Record(
            "SCPT",
            0x00030769,
            0,
            subrecord("EDID", b"CG01SCRIPT\0") + subrecord("SCTX", (
                b"float timer\nshort runTimer\nbegin gamemode\nif runTimer == 1\n"
                b"if timer > 0\nset timer to timer - GetSecondsPassed\nelse\n"
                b"if getstageDone CG01 50 == 1 && getstageDone CG01 70 == 0\n"
                b"setstage CG01 70\nendif\nendif\nendif\nend\0")),
            (),
        )
        cg01_dad_base = Record(
            "NPC_",
            0x0002EA46,
            0,
            subrecord("EDID", b"CG01Dad\0")
            + subrecord("VTCK", struct.pack("<I", VOICE_FORM))
            + subrecord("SCRI", struct.pack("<I", cg01_dad_script.form_id))
            + subrecord("PKID", struct.pack("<I", 0x0002EAAF))
            + subrecord("PKID", struct.pack("<I", 0x000457C4))
            + subrecord("PKID", struct.pack("<I", 0x000C6DE4))
            + subrecord("PKID", struct.pack("<I", 0x0002ECC3)),
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
        cg01_close_gate_target = placed_reference(
            "REFR", 0x000457C2, "CG01DadExitPlaypenMarker2", marker_base.form_id, 0,
            (-2644.5, -5710.0, 7424.0, 0.0, 0.0, 0.092),
        )
        cg01_close_door_target = placed_reference(
            "REFR", 0x000457C0, "CG01DadExitRoomMarker", marker_base.form_id, 0,
            (-2686.3, -5948.4, 7424.0, 0.0, 0.0, 3.275),
        )
        cg01_leave_room_target = placed_reference(
            "REFR", 0x0002EA4B, "CG01DadGoneMarker", marker_base.form_id, 0,
            (-3168.0, -5888.0, 7424.0, 0.0, 0.0, 3.188),
        )
        cg01_playpen_gate = placed_reference(
            "REFR", 0x00047803, "CG01PlaypenGateREF", marker_base.form_id, 0,
            (-2645.0, -5590.6, 7425.0, 0.0, 0.0, 1.571),
        )
        cg01_playroom_door = placed_reference(
            "REFR", 0x0002EA4A, "CG01PlayroomDoor", marker_base.form_id, 0,
            (-2688.0, -5888.0, 7424.0, 0.0, 0.0, 2.750),
        )
        cg01_main_door = placed_reference(
            "REFR", 0x0002EA49, "CG01MainDoor", marker_base.form_id, 0,
            (-2800.0, -6000.0, 7424.0, 0.0, 0.0, 0.0),
        )
        cg01_return_target = placed_reference(
            "REFR", 0x000401BA, "CG01DadReturnMarker", marker_base.form_id, 0,
            (-2700.0, -5800.0, 7424.0, 0.0, 0.0, 0.3),
        )

        def cg01_package(
            form_id: int,
            editor_id: str,
            stage: int,
            target_form_id: int,
            end_source: str | None = None,
        ) -> Record:
            events = subrecord("POBA") + subrecord("POEA")
            if end_source is not None:
                events += subrecord("SCTX", end_source.encode("cp1252") + b"\0")
            events += subrecord("POCA")
            return Record(
                "PACK",
                form_id,
                0,
                subrecord("EDID", editor_id.encode("ascii") + b"\0")
                + subrecord("PKDT", bytes(12))
                + subrecord("PLDT", struct.pack("<iIi", 0, target_form_id, 0))
                + subrecord(
                    "CTDA",
                    condition(
                        58,
                        0x00014E83,
                        operator_flags=0x60,
                        comparison=float(stage),
                    ),
                )
                + events,
                (),
            )

        cg01_close_gate_package = cg01_package(
            0x000C6DE4,
            "CG01DadCloseGate",
            14,
            cg01_close_gate_target.form_id,
            "setstage CG01 16",
        )
        cg01_close_door_package = cg01_package(
            0x000457C4,
            "CG01DadCloseDoor",
            16,
            cg01_close_door_target.form_id,
        )
        cg01_leave_room_package = cg01_package(
            0x0002EAAF,
            "CG01DadLeaveRoom",
            20,
            cg01_leave_room_target.form_id,
        )
        cg01_return_package = cg01_package(
            0x0002ECC3, "CG01DadReturn", 70, cg01_return_target.form_id)
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
        cg01_stage10_source = "\n".join(
            (
                "setObjectiveDisplayed CG01 10 1",
                "set CG01DadREF.timer to 5",
                "EnablePlayerControls 1 0 0 0 1 1 0",
                "autosave",
            )
        )
        cg01_stage12_source = "\n".join(
            (
                "setObjectiveCompleted CG01 10 1",
                "DisablePlayerControls 1 1 1 1 0 0 1",
                "set CG01DadREF.doTalk to 1",
                "set CG01DadREF.timer to 0",
            )
        )
        cg01_stage14_source = "CG01DadREF.evp"
        cg01_stage16_source = "\n".join(
            ("set CG01DadREF.doTalk to 1", "CG01PlaypenGateREF.setOpenState 0")
        )
        cg01_stage18_source = "\n".join(
            (
                "set CG01DadREF.doTalk to 0",
                "CG01PlayroomDoor.setOpenState 0",
                "CG01PlayroomDoor.Lock 100",
                "CG01PlaypenGateREF.setOpenState 0",
                "setstage CG01 20",
            )
        )
        cg01_stage20_source = "\n".join(
            (
                "CG01DadREF.evp",
                "EnablePlayerControls 1 0 0 0 1 1 0",
                "setObjectiveDisplayed CG01 20 1",
            )
        )
        cg01_stage30_source = "\n".join((
            "setObjectiveCompleted CG01 20 1", "setObjectiveDisplayed CG01 30 1",
            "CG01PlayroomDoor.setOpenState 0", "CG01PlayroomDoor.Lock 100"))
        cg01_stage40_source = "\n".join((
            "setObjectiveCompleted CG01 30 1", "setObjectiveDisplayed CG01 40 1",
            "CG01PlayroomDoor.setOpenState 0", "CG01PlayroomDoor.Lock 100"))
        cg01_stage50_source = "\n".join((
            "setObjectiveCompleted CG01 40 1", "set CG01.timer to 10", "set CG01.runTimer to 1"))
        cg01_stage70_source = "\n".join(("set CG01.runTimer to 0", "CG01DadREF.evp"))
        cg01_stage72_source = "\n".join((
            "set CG01DadREF.timer to 1", "set CG01DadREF.doTalk to 1",
            "CG01PlayroomDoor.Unlock", "CG01PlayroomDoor.setOpenState 1",
            "CG01MainDoor.Lock 100", "CG01MainDoor.setOpenState 0"))
        cg01 = Record(
            "QUST",
            0x00014E83,
            0,
            subrecord("EDID", b"CG01\0")
            + subrecord("SCRI", struct.pack("<I", cg01_script.form_id))
            + subrecord("INDX", struct.pack("<H", 0))
            + subrecord("SCTX", cg01_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 5))
            + subrecord("SCTX", cg01_stage5_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 10))
            + subrecord("SCTX", cg01_stage10_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", CG01_WALK_TARGET_STAGE))
            + subrecord("SCTX", cg01_stage12_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 14))
            + subrecord("SCTX", cg01_stage14_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 16))
            + subrecord("SCTX", cg01_stage16_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 18))
            + subrecord("SCTX", cg01_stage18_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 20))
            + subrecord("SCTX", cg01_stage20_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 30))
            + subrecord("SCTX", cg01_stage30_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 40))
            + subrecord("SCTX", cg01_stage40_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 50))
            + subrecord("SCTX", cg01_stage50_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 70))
            + subrecord("SCTX", cg01_stage70_source.encode("cp1252") + b"\0")
            + subrecord("INDX", struct.pack("<H", 72))
            + subrecord("SCTX", cg01_stage72_source.encode("cp1252") + b"\0")
            + subrecord("QOBJ", struct.pack("<I", CG01_WALK_OBJECTIVE_INDEX))
            + subrecord("NNAM", b"Walk to Dad.\0"),
            (),
        )
        cg01_dad_trigger_script = Record(
            "SCPT",
            CG01_DAD_TRIGGER_SCRIPT_FORM,
            0,
            subrecord("EDID", b"CG01DadTriggerSCRIPT\0")
            + subrecord(
                "SCTX",
                "\n".join(
                    (
                        "ScriptName CG01DadTriggerSCRIPT",
                        "short doOnce",
                        "begin onTriggerEnter player",
                        "if getStageDone CG01 12 == 0",
                        "if IsActionRef player == 1",
                        "setstage CG01 12",
                        "endif",
                        "endif",
                        "End",
                    )
                ).encode("cp1252")
                + b"\0",
            ),
            (),
        )
        cg01_dad_trigger_base = Record(
            "ACTI",
            CG01_DAD_TRIGGER_BASE_FORM,
            0,
            subrecord("EDID", b"CG01DadTrigger\0")
            + subrecord("SCRI", struct.pack("<I", cg01_dad_trigger_script.form_id)),
            (),
        )
        cg01_dad_trigger_reference = Record(
            "REFR",
            CG01_DAD_TRIGGER_REFERENCE_FORM,
            0,
            subrecord("NAME", struct.pack("<I", cg01_dad_trigger_base.form_id))
            + subrecord("XTRI", struct.pack("<I", CG01_TRIGGER_COLLISION_LAYERS))
            + subrecord(
                "XPRM",
                struct.pack(
                    "<7fI",
                    *CG01_TRIGGER_PRIMITIVE,
                    CG01_TRIGGER_PRIMITIVE_TYPE,
                ),
            )
            + subrecord(
                "DATA",
                struct.pack("<6f", *CG01_TRIGGER_TRANSFORM),
            ),
            cell_groups,
        )
        def scripted_interaction(script_form, script_editor, source, base_form, base_editor,
                                 ref_form, signature="ACTI", full=None, model=b"Triggers\\TrigPlayerWall01.NIF\0",
                                 primitive=None):
            script = Record("SCPT", script_form, 0,
                subrecord("EDID", script_editor.encode() + b"\0") +
                subrecord("SCTX", source.encode("cp1252") + b"\0"), ())
            base_data = subrecord("EDID", base_editor.encode() + b"\0")
            if full is not None:
                base_data += subrecord("FULL", full.encode("cp1252") + b"\0")
            base_data += subrecord("MODL", model) + subrecord("SCRI", struct.pack("<I", script_form))
            base = Record(signature, base_form, 0, base_data, ())
            reference_editor = {
                0x00047803: "CG01PlaypenGateREF",
                0x0002EA53: "CG01ExitCribTriggerREF",
                0x0002ECC0: "CG01SpecialBookREF",
            }[ref_form]
            ref_data = subrecord("EDID", reference_editor.encode() + b"\0") + subrecord("NAME", struct.pack("<I", base_form))
            if primitive is not None:
                ref_data += subrecord("XPRM", struct.pack("<7fI", *primitive, 1))
            ref_data += subrecord("DATA", struct.pack("<6f", *CG01_TRIGGER_TRANSFORM))
            return script, base, Record("REFR", ref_form, 0, ref_data, cell_groups)

        playpen_records = scripted_interaction(
            0x0002EA3C, "CG01PlaypenGateSCRIPT",
            "begin OnActivate\nif IsActionRef player == 1 && getStage CG01 == 20\nsetstage CG01 30\nendif\nActivate\nend",
            0x000479D3, "CG01PlayPenGateNew", 0x00047803, "DOOR", "Gate Door",
            b"Dungeons\\Vault\\Accessories\\VPlaypenDoor01.NIF\0")
        exit_records = scripted_interaction(
            0x0002ECBA, "CG01ExitCribTriggerSCRIPT",
            "begin onTriggerEnter\nif IsActionRef player == 1 && getStage CG01 == 30\nsetstage CG01 40\nendif\nEnd",
            0x0002EA40, "CG01ExitCribTrigger", 0x0002EA53,
            primitive=(241.0, 24.0, 111.0, 0.8, 0.3, 0.2, 0.15))
        book_records = scripted_interaction(
            0x0002ECB9, "CG01SpecialBookSCRIPT",
            "begin OnActivate\nif getStage CG01 >= 30 && getStageDone CG01 50 == 0\nsetstage CG01 50\nssbmp 40\nendif\nend",
            0x00061215, "BabyBookActivator", 0x0002ECC0, full="You're SPECIAL!",
            model=b"Clutter\\BabyBookSmall01.NIF\0")
        special_names = (
            (0x3E8, "AVStrength", "Strength"),
            (0x3E9, "AVPerception", "Perception"),
            (0x3EA, "AVEndurance", "Endurance"),
            (0x3EB, "AVCharisma", "Charisma"),
            (0x3EC, "AVIntelligence", "Intelligence"),
            (0x3ED, "AVAgility", "Agility"),
            (0x3EE, "AVLuck", "Luck"),
        )
        special_values = tuple(Record(
            "AVIF", form_id, 0,
            subrecord("EDID", editor.encode() + b"\0") +
            subrecord("FULL", label.encode() + b"\0") +
            subrecord("DESC", (label + " description").encode() + b"\0"), ())
            for form_id, editor, label in special_names)
        player_base = Record(
            "NPC_", 0x00000007, 0,
            subrecord("EDID", b"Player\0") + subrecord("DATA", b"\x64\0\0\0" + bytes([5] * 7)), ())
        tutorial = Record(
            "QUST",
            0x00059C85,
            0,
            subrecord("EDID", b"CGTutorial\0"),
            (),
        )
        voice = Record(
            "VTYP",
            VOICE_FORM,
            0,
            subrecord("EDID", b"MaleUniqueDad\0"),
            (),
        )
        cg01_dad_topic = Record(
            "DIAL",
            0x0001F3D8,
            0,
            subrecord("EDID", b"CG01DadSpeech\0")
            + subrecord("QSTI", struct.pack("<I", cg01.form_id)),
            (),
        )

        def cg01_dad_info(
            form_id: int,
            sex_value: int,
            text: str,
            sources: tuple[str, ...],
            speaker_idle_form_id: int,
        ) -> Record:
            return Record(
                "INFO",
                form_id,
                0,
                subrecord("QSTI", struct.pack("<I", cg01.form_id))
                + subrecord("NAM1", text.encode("cp1252") + b"\0")
                + subrecord("SNAM", struct.pack("<I", speaker_idle_form_id))
                + subrecord("CTDA", condition(131, sex_value))
                + subrecord("CTDA", condition(72, cg01_dad_base.form_id))
                + b"".join(
                    subrecord("SCTX", source.encode("cp1252") + b"\0")
                    for source in sources
                ),
                (GroupContext(struct.pack("<I", cg01_dad_topic.form_id), 7),),
            )

        cg01_dad_infos = (
            cg01_dad_info(
                0x0001F3E8,
                1,
                "Don't look straight into the light, honey.",
                ("set CG01DadREF.timer to 1",),
                CG01_DAD_TALK_IDLE_FORM,
            ),
            cg01_dad_info(
                0x0001F3E9,
                0,
                "Don't look straight into the light, pal.",
                ("set CG01DadREF.timer to 1",),
                CG01_DAD_TALK_IDLE_FORM,
            ),
            cg01_dad_info(
                0x0001F3E6,
                1,
                "Come on over here, sweetie. Come on! Walk to Daddy!",
                ("setstage CG01 10", "setstage CGTutorial 2"),
                CG01_DAD_BECKON_FEMALE_IDLE_FORM,
            ),
            cg01_dad_info(
                0x0001F3E7,
                0,
                "Come on over here, son. Come on! Walk to Daddy!",
                ("setstage CG01 10", "setstage CGTutorial 2"),
                CG01_DAD_BECKON_MALE_IDLE_FORM,
            ),
        )
        cg01_stage12_dad_infos = (
            Record(
                "INFO",
                0x0001F3E0,
                0,
                subrecord("DATA", struct.pack("<BBH", 1, 0, 4))
                + subrecord("QSTI", struct.pack("<I", cg01.form_id))
                + subrecord(
                    "NAM1",
                    b"There you go! My goodness! Just a year old, and already walking "
                    b"like a pro.\0",
                )
                + subrecord("SNAM", struct.pack("<I", CG01_DAD_WALKED_IDLE_FORM))
                + subrecord("CTDA", condition(72, cg01_dad_base.form_id))
                + subrecord(
                    "CTDA",
                    condition(58, cg01.form_id, comparison=12.0),
                ),
                (GroupContext(struct.pack("<I", cg01_dad_topic.form_id), 7),),
            ),
            Record(
                "INFO",
                0x0001F3DF,
                0,
                subrecord("DATA", struct.pack("<BBH", 1, 0, 4))
                + subrecord("QSTI", struct.pack("<I", cg01.form_id))
                + subrecord("NAM1", b"Your mother would have been so proud.\0")
                + subrecord("SNAM", struct.pack("<I", CG01_DAD_WALKED_IDLE_FORM))
                + subrecord("CTDA", condition(72, cg01_dad_base.form_id))
                + subrecord(
                    "CTDA",
                    condition(58, cg01.form_id, comparison=12.0),
                )
                + subrecord("SCTX", b"setstage CG01 14\0"),
                (GroupContext(struct.pack("<I", cg01_dad_topic.form_id), 7),),
            ),
        )
        cg01_stage16_dad_infos = (
            Record(
                "INFO",
                0x0001F3DE,
                0,
                subrecord("DATA", struct.pack("<BBH", 1, 0, 4))
                + subrecord("QSTI", struct.pack("<I", cg01.form_id))
                + subrecord("NAM1", b"Listen, kiddo, Daddy needs to leave for a minute.\0")
                + subrecord("CTDA", condition(72, cg01_dad_base.form_id)),
                (GroupContext(struct.pack("<I", cg01_dad_topic.form_id), 7),),
            ),
            Record(
                "INFO",
                0x0001F3DD,
                0,
                subrecord("DATA", struct.pack("<BBH", 1, 0, 4))
                + subrecord("QSTI", struct.pack("<I", cg01.form_id))
                + subrecord("NAM1", b"You'll be okay, pal. I'll be back in a bit.\0")
                + subrecord("CTDA", condition(131, 0))
                + subrecord("CTDA", condition(72, cg01_dad_base.form_id))
                + subrecord("SCTX", b"set CG01DadREF.doTalk to 0\0"),
                (GroupContext(struct.pack("<I", cg01_dad_topic.form_id), 7),),
            ),
            Record(
                "INFO",
                0x0001F3DC,
                0,
                subrecord("DATA", struct.pack("<BBH", 1, 0, 4))
                + subrecord("QSTI", struct.pack("<I", cg01.form_id))
                + subrecord("NAM1", b"You'll be okay, honey. I'll be back in a bit.\0")
                + subrecord("CTDA", condition(131, 1))
                + subrecord("CTDA", condition(72, cg01_dad_base.form_id))
                + subrecord("SCTX", b"set CG01DadREF.doTalk to 0\0"),
                (GroupContext(struct.pack("<I", cg01_dad_topic.form_id), 7),),
            ),
        )
        cg01_return_infos = (
            Record("INFO", 0x0002016A, 0,
                subrecord("NAM1", b"Quite the little explorer.\0") +
                subrecord("CTDA", condition(72, cg01_dad_base.form_id)) +
                subrecord("SCTX", b"setstage CG01 73\0"),
                (GroupContext(struct.pack("<I", cg01_dad_topic.form_id), 7),)),
            Record("INFO", 0x0001F3DA, 0,
                subrecord("NAM1", b"Let's go see Amata.\0") +
                subrecord("CTDA", condition(72, cg01_dad_base.form_id)) +
                subrecord("SCTX", b"setstage CG01 75\0"),
                (GroupContext(struct.pack("<I", cg01_dad_topic.form_id), 7),)),
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
                "dialogueTargetStage": 10,
                "cellFormId": "00028138",
                "dadReferenceFormId": "0002ea4d",
                "dadStartMarkerFormId": "0002ea4e",
                "playerStartMarkerFormId": "0002ea4f",
                "nextDadReferenceFormId": "000300ef",
                "noActivationSoundFormId": "00089b4c",
                "transitionVideo": "1 year later.bik",
                "dadSpeechTopicEditorId": "CG01DadSpeech",
                "dadSpeechTopicFormId": "0001f3d8",
                "dadSpeechPreludeInfoFormIds": ["0001f3e8", "0001f3e9"],
                "dadSpeechStageInfoFormIds": ["0001f3e6", "0001f3e7"],
                "stage12DadResponseInfoFormIds": ["0001f3e0", "0001f3df"],
                "stage12DadResponseTargetStage": 14,
                "postStage14Transition": {
                    "stage16": 16,
                    "stage18": 18,
                    "stage20": 20,
                    "closeGatePackageFormId": "000c6de4",
                    "closeDoorPackageFormId": "000457c4",
                    "leaveRoomPackageFormId": "0002eaaf",
                    "closeGateTargetFormId": "000457c2",
                    "closeDoorTargetFormId": "000457c0",
                    "leaveRoomTargetFormId": "0002ea4b",
                    "dadResponseInfoFormIds": ["0001f3de", "0001f3dd", "0001f3dc"],
                    "playpenGateReferenceFormId": "00047803",
                    "exitCribTriggerReferenceFormId": "0002ea53",
                    "specialBookReferenceFormId": "0002ecc0",
                    "stage30": 30,
                    "stage40": 40,
                    "stage50": 50,
                },
                "tutorialQuestEditorId": "CGTutorial",
                "tutorialQuestFormId": "00059c85",
                "tutorialQuestStage": 2,
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
            tutorial,
            cg01_script,
            cg01_dad_trigger_script,
            cg01_dad_trigger_base,
            cg01_dad_trigger_reference,
            *playpen_records,
            *exit_records,
            *book_records,
            *special_values,
            player_base,
            cg01_dad_script,
            cg01_dad_base,
            voice,
            cg01_dad_topic,
            *cg01_dad_infos,
            *cg01_stage12_dad_infos,
            *cg01_stage16_dad_infos,
            *cg01_return_infos,
            idle(
                CG01_DAD_TALK_IDLE_FORM,
                "ttnpcNTRLHandsDownTalkA",
                r"Characters\_Male\IdleAnims\ttnpcNTRLHandsDownTalkA.kf",
            ),
            idle(
                CG01_DAD_BECKON_FEMALE_IDLE_FORM,
                "LooseBeckoningToChildA",
                r"Characters\_Male\IdleAnims\BeckoningToChildA.kf",
            ),
            idle(
                CG01_DAD_BECKON_MALE_IDLE_FORM,
                "LooseBeckoningToChildB",
                r"Characters\_Male\IdleAnims\BeckoningToChildB.kf",
            ),
            idle(
                CG01_DAD_WALKED_IDLE_FORM,
                "LooseCrouchTalkToBaby",
                r"Characters\_Male\IdleAnims\BeckoningToChildTalk.kf",
            ),
            cg02_dad_base,
            marker_base,
            cg01_dad_ref,
            cg01_dad_marker,
            cg01_player_marker,
            cg01_close_gate_target,
            cg01_close_door_target,
            cg01_leave_room_target,
            cg01_playroom_door,
            cg01_main_door,
            cg01_return_target,
            cg01_close_gate_package,
            cg01_close_door_package,
            cg01_leave_room_package,
            cg01_return_package,
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
        post_stage5 = cg01_contract["postStage5Transition"]
        self.assertEqual(
            "opennv-fo3-cg01-stage-5-to-10-transition/v1",
            post_stage5["schema"],
        )
        self.assertEqual(10, post_stage5["targetStage"])
        self.assertEqual(
            [0, 0, 1, 1],
            [branch["sequence"] for branch in post_stage5["dialogue"]["branches"]],
        )
        self.assertEqual(
            ["female", "male", "female", "male"],
            [branch["engineSex"] for branch in post_stage5["dialogue"]["branches"]],
        )
        self.assertEqual(
            [
                "0003d33b",
                "0003d33b",
                "0005943a",
                "0005943b",
            ],
            [
                branch["speakerIdle"]["formId"]
                for branch in post_stage5["dialogue"]["branches"]
            ],
        )
        self.assertEqual(
            r"meshes\characters\_male\idleanims\beckoningtochildb.kf",
            post_stage5["dialogue"]["branches"][3]["speakerIdle"]["modelPath"],
        )
        stage12_response = post_stage5["postStage12DadResponse"]
        self.assertEqual(
            "opennv-fo3-cg01-stage-12-to-14-dad-response/v1",
            stage12_response["schema"],
        )
        self.assertEqual(
            ["0001f3e0", "0001f3df"],
            [row["infoFormId"] for row in stage12_response["dialogue"]["branches"]],
        )
        self.assertTrue(
            all(row["sayOnce"] for row in stage12_response["dialogue"]["branches"])
        )
        self.assertEqual(
            "0005943d",
            stage12_response["dialogue"]["branches"][0]["speakerIdle"]["formId"],
        )
        self.assertEqual(
            14,
            stage12_response["dialogue"]["branches"][1]["effects"][0]["stage"],
        )
        self.assertEqual(
            "evaluatePackage",
            stage12_response["stageResult"]["commands"][0]["kind"],
        )
        post_stage14 = post_stage5["postStage14Transition"]
        self.assertEqual(
            "opennv-fo3-cg01-stage-14-to-20-runtime/v1",
            post_stage14["schema"],
        )
        self.assertEqual(20, post_stage14["targetStage"])
        self.assertEqual(
            ["000c6de4", "000457c4", "0002eaaf"],
            [
                post_stage14["packages"][name]["formId"]
                for name in ("closeGate", "closeDoor", "leaveRoom")
            ],
        )
        self.assertEqual(
            ["0001f3de", "0001f3dd", "0001f3dc"],
            [row["infoFormId"] for row in post_stage14["dialogue"]["branches"]],
        )
        self.assertTrue(post_stage14["nextBoundary"]["applied"])
        interaction = post_stage14["stage20Interaction"]
        self.assertEqual([30, 40, 50], [row["stage"] for row in interaction["stageResults"]])
        self.assertEqual("00047803", interaction["gate"]["referenceFormId"])
        self.assertEqual("0002ea53", interaction["exitTrigger"]["referenceFormId"])
        self.assertEqual("0002ecc0", interaction["specialBook"]["referenceFormId"])
        self.assertEqual(40, interaction["specialBook"]["menuPoints"])
        self.assertEqual(
            [5] * 7,
            [row["initialValue"] for row in interaction["specialBook"]["actorValues"]],
        )
        self.assertEqual(
            [
                ["setObjectiveCompleted", "setObjectiveDisplayed", "setOpenState", "lock"],
                ["setObjectiveCompleted", "setObjectiveDisplayed", "setOpenState", "lock"],
                ["setObjectiveCompleted", "setQuestVariable", "setQuestVariable"],
            ],
            [[command["kind"] for command in row["commands"]]
             for row in interaction["stageResults"]],
        )
        self.assertTrue(interaction["nextBoundary"]["applied"])
        timer = interaction["timerTransition"]
        self.assertEqual((50, 70), (timer["sourceStage"], timer["targetStage"]))
        self.assertEqual(10.0, timer["timerVariable"]["initialSeconds"])
        self.assertEqual(
            ["setQuestVariable", "evaluatePackage"],
            [row["kind"] for row in timer["targetResult"]["commands"]],
        )
        self.assertTrue(timer["nextBoundary"]["applied"])
        dad_return = timer["dadReturn"]
        self.assertEqual("0002ecc3", dad_return["package"]["formId"])
        self.assertEqual("000401ba", dad_return["package"]["target"]["formId"])
        self.assertEqual(72, dad_return["package"]["completionStage"])
        self.assertEqual(75, dad_return["targetStage"])
        self.assertEqual(
            "fo3-cg01-stage-75-dad-lead-package-runtime-not-implemented",
            dad_return["nextBoundary"]["blocker"],
        )
        self.assertEqual(
            [
                "setObjectiveDisplayed",
                "setScriptVariable",
                "enablePlayerControls",
                "autosave",
            ],
            [command["kind"] for command in post_stage5["stageResult"]["commands"]],
        )
        self.assertEqual(
            [1, 0, 0, 0, 1, 1, 0],
            post_stage5["stageResult"]["commands"][2]["arguments"],
        )
        walk_to_dad = post_stage5["postStage10TriggerTransition"]
        self.assertEqual(CG01_WALK_TARGET_STAGE, walk_to_dad["targetStage"])
        self.assertEqual("Walk to Dad.", walk_to_dad["objective"]["text"])
        self.assertEqual(
            CG01_DAD_TRIGGER_REFERENCE_FORM,
            int(walk_to_dad["trigger"]["referenceFormId"], FORM_ID_RADIX),
        )
        self.assertEqual(
            [
                "setObjectiveCompleted",
                "disablePlayerControls",
                "setScriptVariable",
                "setScriptVariable",
            ],
            [command["kind"] for command in walk_to_dad["stageResult"]["commands"]],
        )

        transition_video = {
            "file": "1 year later.bik",
            "source": "C:/owned/Fallout 3/Data/Video/1 year later.bik",
            "bytes": 12345,
            "sha256": "a" * 64,
            "runtime": {
                "schema": "opennv-owned-opening-video/v1",
                "status": "deterministic-owned-video-transcode",
                "inputs": {
                    "source": "C:/owned/Fallout 3/Data/Video/1 year later.bik",
                    "sourceSha256": "a" * 64,
                    "policy": {"videoCodec": "libtheora"},
                },
                "output": "C:/cache/generated/opening/video/cg01.ogv",
                "outputBytes": 9876,
                "outputSha256": "b" * 64,
            },
        }
        character_selection = {
            "stage100Transition": contract,
            "cg01Stage0Transition": cg01_contract,
        }
        with tempfile.TemporaryDirectory() as temporary:
            default_ini = Path(temporary) / "Fallout_default.ini"
            default_ini.write_text(
                "[Display]\nfDefaultFOV=75.0000\nfNearDistance=5\n",
                encoding="cp1252",
            )
            _bind_cg01_toddler_world(
                character_selection,
                dict(
                    dict(load_recipe(FO3_RECIPE)["opening"])[
                        "characterSelection"
                    ]["cg01Stage0Transition"]
                ),
                default_ini,
                SimpleNamespace(
                    document={
                        "player": {
                            "spawnCenterHeightMeters": 0.9,
                            "capsuleRadiusMeters": 0.32,
                            "capsuleHeightMeters": 1.8,
                            "moveSpeedMetersPerSecond": 3.6,
                            "mouseSensitivityRadiansPerPixel": 0.0025,
                            "verticalLookLimitRadians": 1.45,
                            "desktopCameraOffsetMeters": [0.0, 0.72, 0.0],
                            "cameraFarMeters": 1000.0,
                            "collisionLayer": 2,
                            "collisionMask": 1,
                            "desktopInput": {
                                "moveLeft": {"action": "move_left"},
                                "moveRight": {"action": "move_right"},
                                "moveForward": {"action": "move_forward"},
                                "moveBackward": {"action": "move_backward"},
                            },
                        },
                        "simulation": {"gravityMetersPerSecondSquared": 9.8},
                    },
                    manifest=lambda: {
                        "schema": "opennv-runtime-configuration/v1",
                        "sha256": "c" * 64,
                    },
                ),
            )
        _bind_cg01_transition_video(character_selection, transition_video)
        bound_transition = character_selection["cg01Stage0Transition"]
        toddler_world = bound_transition["toddlerWorld"]
        self.assertEqual(
            "opennv-fo3-cg01-toddler-world/v1",
            toddler_world["schema"],
        )
        self.assertEqual(0.4, toddler_world["player"]["scale"])
        self.assertEqual("0002ea4f", toddler_world["player"]["startMarker"]["formId"])
        self.assertEqual(
            75.0,
            toddler_world["camera"]["sourceHorizontalFovDegrees"],
        )
        self.assertEqual(
            0.75,
            toddler_world["camera"]["referenceAspectHeightOverWidth"],
        )
        self.assertAlmostEqual(
            59.840444,
            toddler_world["camera"]["verticalFovDegrees"],
            places=5,
        )
        self.assertEqual("keep-height", toddler_world["camera"]["godotKeepAspect"])
        self.assertEqual(5.0, toddler_world["camera"]["nearGameUnits"])
        self.assertEqual(
            "0002ea54",
            toddler_world["triggerReferenceFormId"],
        )
        self.assertEqual(12, toddler_world["targetStage"])
        bound_stage0 = bound_transition["stage0Result"]
        self.assertEqual(
            ["moveToReference", "setStage", "setPlayerScale", "moveToReference"],
            [command["kind"] for command in bound_stage0["commands"]],
        )
        bound_stage5 = bound_stage0["commands"][1]["stageResult"]
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
            [command["kind"] for command in bound_stage5["commands"]],
        )
        bound_movie = bound_stage5["commands"][12]
        self.assertEqual("a" * 64, bound_movie["video"]["sha256"])
        self.assertEqual(
            "b" * 64,
            bound_movie["video"]["runtime"]["outputSha256"],
        )
        expected_contract_sha256 = hashlib.sha256(
            json.dumps(
                bound_transition,
                sort_keys=True,
                separators=(",", ":"),
            ).encode("utf-8")
        ).hexdigest()
        bound_stage100 = character_selection["stage100Transition"]
        self.assertEqual(
            expected_contract_sha256,
            bound_stage100["commands"][7]["stageResultContract"]["sha256"],
        )
        self.assertEqual(
            bound_stage100["commands"][7]["stageResultContract"],
            bound_stage100["nextBoundary"]["transitionContract"],
        )

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
