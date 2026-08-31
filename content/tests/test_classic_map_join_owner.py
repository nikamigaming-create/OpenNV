from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ClassicMapJoinOwnerTest(unittest.TestCase):
    def test_shared_join_validates_exact_active_state_and_reciprocal_maps(self) -> None:
        owner = (
            ROOT / "runtime/src/Campaigns/Classic/ClassicMapJoinOwner.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("activeMapIndex != join.Source.MapIndex", owner)
        self.assertIn("activeMapSha256.Equals(join.Source.MapSha256", owner)
        self.assertIn("activeTile != join.Source.Tile", owner)
        self.assertIn("ValidateReciprocal", owner)
        self.assertIn("SameMap(forward.Source, reverse.Destination)", owner)

    def test_fo1_forward_and_reverse_owned_descriptors_use_shared_join(self) -> None:
        recipe = json.loads(
            (ROOT / "content/recipes/fo1-v13ent-exit-grid-transition-v1.json").read_text(
                encoding="utf-8"
            )
        )
        self.assertEqual(recipe["sourceMap"]["mapIndex"], 35)
        self.assertEqual(recipe["destination"]["mapIndex"], 6)
        self.assertEqual(recipe["reciprocalInstanceValues"][0], 35)
        contract = (
            ROOT / "runtime/src/Campaigns/Fallout1/Fo1ExitGridTransitionContract.cs"
        ).read_text(encoding="utf-8")
        runtime = (
            ROOT / "runtime/src/Campaigns/Fallout1/Fo1TacticalSession.cs"
        ).read_text(encoding="utf-8")
        save = (
            ROOT / "runtime/src/Campaigns/Fallout1/Fo1TacticalSession.Persistence.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("ClassicMapJoinOwner.Commit(", contract)
        self.assertIn("JoinForTrigger", contract)
        self.assertIn("ClassicMapJoinOwner.ValidateReciprocal(", runtime)
        self.assertIn("destinationReturnExitGrid", save)
        self.assertIn("classicPlayerStatus", save)
        self.assertIn("gameTime = _classicScriptGameTime", save)

    def test_fo2_map3_temple_map4_chain_uses_shared_join_without_replacing_player(self) -> None:
        player = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArroyoCavesPlayerRuntime.cs"
        ).read_text(encoding="utf-8")
        transition = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleTransitionRuntime.cs"
        ).read_text(encoding="utf-8")
        save = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStartSave.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("ClassicMapJoinOwner.Commit(", player)
        self.assertIn("Reparent(destination.Root, keepGlobalTransform: false)", player)
        self.assertIn("ClassicMapJoinOwner.Commit(", transition)
        self.assertIn("TryApplyPostTrial", transition)
        self.assertIn("templeExitTransition", save)
        self.assertIn("spearLooted = TempleConfrontation.SpearLooted", save)
        self.assertIn("scriptState", save)


if __name__ == "__main__":
    unittest.main()
