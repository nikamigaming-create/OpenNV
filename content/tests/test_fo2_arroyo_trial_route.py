from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "content/tools"))

from fo2_arroyo_trial_route import _parse_dialogue_catalog  # noqa: E402


class Fo2ArroyoTrialRouteTest(unittest.TestCase):
    def test_multiline_dialogue_catalog_is_complete(self) -> None:
        messages = _parse_dialogue_catalog(
            b"{103}{}{One line}\n{104}{}{First half\nsecond half.}\n"
        )
        self.assertEqual(messages, {103: "One line", 104: "First half second half."})

    def test_contract_binds_cameron_before_klint_gate(self) -> None:
        recipe = json.loads(
            (ROOT / "content/recipes/fo2-arroyo-trial-route-v1.json").read_text(
                encoding="utf-8"
            )
        )
        trial = recipe["trialState"]
        self.assertEqual(trial["globalCatalog"]["name"], "GVAR_START_ARROYO_TRIAL")
        self.assertEqual(trial["globalCatalog"]["index"], 10)
        self.assertEqual(
            (trial["cameron"]["serial"], trial["cameron"]["elevation"]),
            (3394, 2),
        )
        self.assertEqual(
            trial["cameron"]["taggedSpeechBranch"]["selectedMessageIds"],
            [108, 117, 165, 168, 171],
        )
        self.assertEqual(
            trial["cameron"]["taggedSpeechBranch"]["result"]["globalVariable10"],
            2,
        )
        self.assertEqual(trial["klintGate"]["requiredGlobalVariable10"], 2)
        self.assertEqual(
            (trial["klintGate"]["sourceTile"], trial["klintGate"]["destinationTile"]),
            (21303, 19698),
        )
        self.assertFalse(recipe["movement"]["wallEdgeCollisionImplemented"])

        compiler = (
            ROOT / "content/tools/fo2_arroyo_trial_route.py"
        ).read_text(encoding="utf-8")
        self.assertIn("_intra_map_exits(source)", compiler)
        self.assertIn("_walkable_by_elevation(source)", compiler)
        self.assertIn("released_walkable", compiler)
        self.assertIn("post-trial obelisk move", compiler)
        self.assertIn("parse_map_objects", compiler)
        self.assertIn("firstLegalAction", compiler)
        self.assertIn("decode_classic_door", compiler)
        self.assertIn("materialize_classic_door_assets", compiler)
        self.assertNotIn("guardian death", compiler.casefold())

        contract = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArroyoTrialRouteContract.cs"
        ).read_text(encoding="utf-8")
        runtime = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArroyoTrialRuntime.cs"
        ).read_text(encoding="utf-8")
        save = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStartSave.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("LoadPath(movement.GetProperty(\"approachCameron\"))", contract)
        self.assertIn("ApplyKlintMapEnter", runtime)
        self.assertIn("TryApplyPostTrial(_contract.Village)", runtime)
        self.assertIn("ApplyVillageFirstLegalAction", runtime)
        self.assertIn("VillageFirstActionStage", runtime)
        self.assertIn("ClassicDoorSession", runtime)
        self.assertIn("ClassicDoorPlayback", runtime)
        self.assertIn("CameronDoorPlaybackState", runtime)
        self.assertIn('SetMeta("source_door_frame"', runtime)
        self.assertIn('"source_door_sound"', runtime)
        self.assertIn('GetProperty("doorPresentation")', contract)
        self.assertIn("CameronDialogueSelections", save)
        self.assertIn("VillageFirstActionApplied", save)
        self.assertIn("rejected guardian shortcut", save)
        confrontation = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleConfrontationRuntime.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("Klint is not the final trial guardian", confrontation)
        self.assertIn("ACKlint moves the gate", confrontation)


if __name__ == "__main__":
    unittest.main()
