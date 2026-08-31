from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "content" / "tools"
sys.path.insert(0, str(TOOLS))

from opening_catalog import _compile_ordinary_quests, _compile_topic_closure


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
                source="scn VCG02SCRIPT",
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
