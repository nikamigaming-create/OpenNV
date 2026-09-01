from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "content" / "tools"
sys.path.insert(0, str(TOOLS))

from opening_catalog import (
    _compile_combat_encounters,
    _compile_ordinary_quests,
    _compile_hit_target_sets,
    _compile_package_dialogue_closure,
    _compile_topic_closure,
    _resolve_command_record_identities,
    _script_commands,
)


def record(
    record_type: str,
    form_id: str,
    editor_id: str,
    *,
    links: list[dict[str, str]] | None = None,
    source: str | None = None,
    stages: list[dict[str, object]] | None = None,
    objectives: list[dict[str, object]] | None = None,
) -> dict[str, object]:
    text = [{"signature": "EDID", "value": editor_id}]
    if source is not None:
        text.append({"signature": "SCTX", "value": source})
    return {
        "recordType": record_type,
        "formId": form_id,
        "text": text,
        "links": links or [],
        "questStageScripts": stages or [],
        "questObjectives": objectives or [],
    }


class FnvOrdinaryQuestHandoffTest(unittest.TestCase):
    def test_compiles_package_selected_greeting_and_linked_choice(self) -> None:
        greeting = record("DIAL", "000000c8", "GREETING")
        greeting["text"].append({"signature": "FULL", "value": "GREETING"})
        choice = record("DIAL", "0010a1de", "VCG02GSSunnySmilesTopic000")
        choice["text"].append({"signature": "FULL", "value": "Okay, I'm in."})
        greeting_info = record(
            "INFO",
            "0010a1ec",
            "",
            links=[
                {"signature": "QSTI", "formId": "0010a214"},
                {"signature": "TCLT", "formId": "0010a1de"},
            ],
            source="set VCG02.bShootingTutorialActive to 0",
        )
        greeting_info.update({
            "sourceOrder": 10,
            "groups": [{"type": 7, "label": "000000c8"}],
            "conditions": [
                {"function": 420, "parameter1": "0010a214", "parameter2": 10,
                 "comparisonValue": 1.0, "operatorFlags": 0},
                {"function": 72, "parameter1": "00104e84", "parameter2": 0,
                 "comparisonValue": 1.0, "operatorFlags": 0},
            ],
            "dialogueData": {"flags": 0, "responseType": 0},
        })
        choice_info = record(
            "INFO", "0010a1e5", "", source="SetStage VCG02 25"
        )
        choice_info.update({
            "sourceOrder": 11,
            "groups": [{"type": 7, "label": "0010a1de"}],
            "conditions": [],
            "dialogueData": {"flags": 1, "responseType": 0},
        })

        topics, root = _compile_package_dialogue_closure(
            [greeting, choice, greeting_info, choice_info],
            {
                "editorId": "VCG02SunnySmilesDialogueStart",
                "conditions": [{
                    "functionName": "getStageDone",
                    "parameter1": "0010a214",
                    "parameter2": 10,
                    "comparisonValue": 1.0,
                    "operatorFlags": 0,
                }],
            },
            "00104e84",
            {"formId": "0010a214", "editorId": "VCG02"},
            {},
            {"58": "getStage", "72": "getIsId", "420": "getStageDone",
             "421": "getObjectiveCompleted"},
        )

        self.assertEqual("000000c8", root)
        self.assertEqual(["000000c8", "0010a1de"], [topic["formId"] for topic in topics])
        self.assertEqual("setQuestVariable", topics[0]["infos"][0]["commands"][0]["kind"])
        self.assertEqual("setStage", topics[1]["infos"][0]["commands"][0]["kind"])

        sneak_info = record(
            "INFO", "0010a1ed", "",
            links=[{"signature": "QSTI", "formId": "0010a214"}],
            source="SetStage VCG02 35"
        )
        sneak_info.update({
            "sourceOrder": 12,
            "groups": [{"type": 7, "label": "000000c8"}],
            "conditions": [
                {"function": 58, "parameter1": "0010a214", "parameter2": 0,
                 "comparisonValue": 30.0, "operatorFlags": 0},
                {"function": 72, "parameter1": "00104e84", "parameter2": 0,
                 "comparisonValue": 1.0, "operatorFlags": 0},
            ],
            "dialogueData": {"flags": 1, "responseType": 0},
        })
        sneak_topics, _ = _compile_package_dialogue_closure(
            [greeting, sneak_info],
            {
                "editorId": "VCG02SunnySmilesDialogueSneakStart",
                "conditions": [{
                    "functionName": "getStage",
                    "parameter1": "0010a214",
                    "parameter2": 0,
                    "comparisonValue": 30.0,
                    "operatorFlags": 0,
                }],
            },
            "00104e84",
            {"formId": "0010a214", "editorId": "VCG02"},
            {},
            {"58": "getStage", "72": "getIsId", "420": "getStageDone",
             "421": "getObjectiveCompleted"},
        )
        self.assertEqual("0010a1ed", sneak_topics[0]["infos"][0]["formId"])
        self.assertEqual("setStage", sneak_topics[0]["infos"][0]["commands"][0]["kind"])

    def test_compiles_source_hit_target_set_without_target_specific_runtime_ids(self) -> None:
        records = [
            record(
                "SCPT",
                "0010a1ef",
                "VCG02TargetSCRIPT",
                source=(
                    "begin OnHitWith\n"
                    "if (GetQuestRunning VCG02 == 1 && Player.IsWeaponOut == 1 && "
                    "Player.GetWeaponAnimType > 3 && Player.GetWeaponAnimType < 9 && "
                    "Player.GetEquipped NVWeapMS22Camera == 0)\n"
                    "SunnyREF.SayTo player VCG02SunnyShotReaction\n"
                    "set VCG02.nTargetCount to VCG02.nTargetCount + 1\n"
                    "setstage CGTutorial 62\n"
                    "if VCG02.nTargetCount >= 3\n"
                    "SetObjectiveCompleted VCG02 10 1\nSunnyREF.evp\nendif\nendif\nend"
                ),
            ),
            record("REFR", "00168a92", "VCG02BottleMarkerREF"),
            record(
                "REFR",
                "0010a202",
                "TargetRef",
                links=[
                    {"signature": "NAME", "formId": "0010a1f6"},
                    {"signature": "XESP", "formId": "00168a92"},
                ],
            ),
            record(
                "MISC",
                "0010a1f6",
                "TargetBase",
                links=[{"signature": "SCRI", "formId": "0010a1ef"}],
            ),
            record("WEAP", "0011a208", "NVWeapMS22Camera"),
            record("QUST", "00059c85", "CGTutorial"),
            record("ACHR", "00104e85", "SunnyREF"),
        ]
        quests = [
            {
                "formId": "0010a214",
                "editorId": "VCG02",
                "variables": [{"index": 1, "name": "nTargetCount"}],
            }
        ]
        actors = [
            {"topics": [{"formId": "0010a1df", "editorId": "VCG02SunnyShotReaction"}]}
        ]

        result = _compile_hit_target_sets(
            records,
            [
                {
                    "scriptEditorId": "VCG02TargetSCRIPT",
                    "enableParentEditorId": "VCG02BottleMarkerREF",
                    "reactionTopicEditorId": "VCG02SunnyShotReaction",
                }
            ],
            quests,
            actors,
        )[0]

        self.assertEqual(["0010a202"], [value["referenceFormId"] for value in result["targets"]])
        self.assertEqual(1, result["questVariableIndex"])
        self.assertEqual(3, result["threshold"])
        self.assertEqual(62, result["tutorialStage"])

    def test_compiles_creature_health_ai_and_on_death_counter(self) -> None:
        death_source = """
        BEGIN OnDeath
          set VCG02.nGeckosKilled to VCG02.nGeckosKilled + 1
          if (GetObjectiveDisplayed VCG02 30 == 0 && GetStage VCG02 < 45)
            SetStage VCG02 45
          endif
          if (VCG02.nGeckosKilled == 2 && GetObjectiveCompleted VCG02 30 == 0)
            SetStage VCG02 50
            SunnyREF.ResetAI
          endif
        END
        """
        creature = record(
            "CREA",
            "0010a1f7",
            "VCG02CrGecko",
            links=[
                {"signature": "SCRI", "formId": "0010a1f2"},
                {"signature": "PKID", "formId": "00025482"},
            ],
        )
        creature["creature"] = {"maximumHealth": 20, "attackDamage": 5}
        records = [
            record("SCPT", "0010a1f2", "VCG02GeckoDeathSCRIPT", source=death_source),
            creature,
            record("PACK", "00025482", "DefaultPatrolCasual"),
            record(
                "ACRE",
                "0010a1fe",
                "VCG02Gecko1REF",
                links=[{"signature": "NAME", "formId": "0010a1f7"}],
            ),
            record(
                "ACRE",
                "0010a1fd",
                "VCG02Gecko2REF",
                links=[{"signature": "NAME", "formId": "0010a1f7"}],
            ),
            record("ACHR", "00104e85", "SunnyREF"),
        ]
        quests = [{
            "formId": "0010a214",
            "editorId": "VCG02",
            "variables": [{"index": 3, "name": "nGeckosKilled"}],
        }]

        encounter = _compile_combat_encounters(
            records,
            [{
                "deathScriptEditorId": "VCG02GeckoDeathSCRIPT",
                "referenceEditorIds": ["VCG02Gecko1REF", "VCG02Gecko2REF"],
            }],
            quests,
        )[0]

        self.assertEqual(45, encounter["minimumCombatStage"])
        self.assertEqual(50, encounter["completionStage"])
        self.assertEqual(2, encounter["threshold"])
        self.assertEqual(20, encounter["targets"][0]["maximumHealth"])
        self.assertEqual(5, encounter["targets"][0]["attackDamage"])

    def test_preserves_source_result_guards_and_resolves_leveled_grant(self) -> None:
        commands = _script_commands(
            """
            if (GetStage VCG02 < 20)
              SetStage VCG02 20
            endif
            if (Player.GetItemCount WeapNVVarmintRifle == 0)
              player.additem CondNVVarmintRifleLoot 1
            endif
            Player.AddItem Ammo556mm 30
            player.equipitem WeapNVVarmintRifle
            """
        )
        records = [
            record("QUST", "0010a214", "VCG02"),
            record("WEAP", "0007ea24", "WeapNVVarmintRifle"),
            record("AMMO", "00004240", "Ammo556mm"),
            record("LVLI", "00096c06", "CondNVVarmintRifleLoot"),
        ]
        records[-1]["leveledEntries"] = [
            {"level": level, "itemFormId": "0007ea24", "count": 1}
            for level in range(1, 8)
        ]
        records[1]["weapon"] = {
            "damage": 18,
            "clipSize": 5,
            "ammoFormId": "00004240",
            "animationType": 5,
        }

        contract = _resolve_command_record_identities(commands, records)

        self.assertEqual("questStageLessThan", commands[0]["guard"]["kind"])
        self.assertEqual("0010a214", commands[0]["guard"]["questFormId"])
        self.assertEqual("playerItemCountZero", commands[1]["guard"]["kind"])
        self.assertEqual("0007ea24", commands[1]["guard"]["itemFormId"])
        self.assertEqual("0007ea24", commands[1]["resolvedItemFormId"])
        self.assertEqual("WEAP", commands[1]["resolvedItemRecordType"])
        self.assertEqual(4, contract["commandCount"])

    def test_compiles_authored_entry_objective_from_quest_and_timer_identity(self) -> None:
        records = [
            record(
                "QUST",
                "0010a214",
                "VCG02",
                links=[{"signature": "SCRI", "formId": "0010a1f0"}],
                stages=[
                    {
                        "stage": 5,
                        "source": "SetObjectiveDisplayed VCG02 3 1\nForceActiveQuest VCG02",
                    }
                ],
                objectives=[{"index": 3, "text": "Talk to Sunny Smiles"}],
            ),
            record(
                "SCPT",
                "0010a1f0",
                "VCG02SCRIPT",
                source=(
                    "scn VCG02SCRIPT\n"
                    "short nTargetCount\n"
                    "short bShootingTutorialActive"
                ),
            ),
            record(
                "SCPT",
                "00168a80",
                "VGenericTimerSCRIPT",
                source="SetStage VCG01 200\nSetStage VCG02 5",
            ),
        ]

        quests = _compile_ordinary_quests(records, ["VCG02"], "VCG01", 200)

        self.assertEqual(1, len(quests))
        quest = quests[0]
        self.assertEqual("0010a214", quest["formId"])
        self.assertEqual("0010a1f0", quest["scriptFormId"])
        self.assertEqual("VCG02SCRIPT", quest["scriptEditorId"])
        self.assertEqual(5, quest["entryStage"])
        self.assertEqual(
            [
                {"index": 1, "name": "nTargetCount"},
                {"index": 2, "name": "bShootingTutorialActive"},
            ],
            quest["variables"],
        )
        command = quest["stages"][0]["commands"][0]
        self.assertEqual("objective", command["kind"])
        self.assertEqual("0010a214", command["questFormId"])
        self.assertEqual(3, command["index"])
        self.assertTrue(command["enabled"])
        self.assertTrue(quest["commandContract"]["allDeclaredRecordReferencesResolved"])

    def test_recipe_selects_vcg02_as_owned_route_root(self) -> None:
        recipe = json.loads(
            (ROOT / "content" / "recipes" / "fnv-new-game-opening-v1.json")
            .read_text(encoding="utf-8")
        )
        self.assertIn("VCG02", recipe["rootEditorIds"])
        self.assertEqual(
            ["VCG02"], recipe["newGameFlow"]["ordinaryQuestEditorIds"]
        )
        self.assertEqual(
            "VCG02", recipe["newGameFlow"]["ordinaryActors"][0]["questEditorId"]
        )
        self.assertIn(
            "VCG02SunnySneakCloserToWell",
            recipe["newGameFlow"]["ordinaryActors"][0]["packageEditorIds"],
        )
        self.assertIn(
            "VCG02SunnySmilesDialogueSneakEnd",
            recipe["newGameFlow"]["ordinaryActors"][0]["packageEditorIds"],
        )
        self.assertIn(
            {"packageEditorId": "VCG02SunnySmilesDialogueSneakEnd"},
            recipe["newGameFlow"]["ordinaryActors"][0][
                "automaticPackageDialogues"
            ],
        )

    def test_compiles_ordered_activation_topic_info_and_source_results(self) -> None:
        topic = record("DIAL", "0010a1e1", "VFreeformGoodspringsGSSunnySmilesTopic019")
        topic["text"].append({"signature": "FULL", "value": "Ask about survival"})
        info = record(
            "INFO",
            "0010a1e4",
            "",
            source="StartQuest VCG02\nSetStage VCG02 10",
        )
        info.update(
            {
                "sourceOrder": 41,
                "groups": [{"type": 7, "label": "0010a1e1"}],
                "conditions": [],
                "dialogueData": {"flags": 1, "responseType": 0},
            }
        )
        info["text"].extend(
            [
                {"signature": "NAM1", "value": "First response"},
                {"signature": "NAM1", "value": "Second response"},
            ]
        )

        topics, root_form_id = _compile_topic_closure(
            [topic, info],
            "VFreeformGoodspringsGSSunnySmilesTopic019",
            {},
            "VCG01",
        )

        self.assertEqual("0010a1e1", root_form_id)
        self.assertEqual("Ask about survival", topics[0]["prompt"])
        self.assertEqual(41, topics[0]["infos"][0]["sourceOrder"])
        self.assertEqual(
            ["First response", "Second response"], topics[0]["infos"][0]["lines"]
        )
        self.assertEqual(
            ["startQuest", "setStage"],
            [command["kind"] for command in topics[0]["infos"][0]["commands"]],
        )


if __name__ == "__main__":
    unittest.main()
