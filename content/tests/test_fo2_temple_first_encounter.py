from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class Fo2TempleFirstEncounterTest(unittest.TestCase):
    def test_sole_owned_critter_uses_source_path_ap_combat_and_cold_save(self) -> None:
        recipe = json.loads(
            (ROOT / "content/recipes/fo2-temple-of-trials-v1.json").read_text(
                encoding="utf-8"
            )
        )["boundedConfrontation"]
        profile = json.loads(
            (
                ROOT
                / "runtime/config/fo2-temple-confrontation-runtime-v1.json"
            ).read_text(encoding="utf-8")
        )
        self.assertEqual(
            (recipe["critter"]["serial"], recipe["critter"]["tile"]),
            (379, 21101),
        )
        self.assertEqual(recipe["critter"]["pid"], "01000003")
        self.assertEqual(recipe["critter"]["sid"], "04000001")
        self.assertEqual(recipe["loot"]["serial"], 378)
        self.assertEqual(recipe["loot"]["pid"], "00000007")
        guardian = recipe["guardianScript"]
        self.assertEqual(guardian["program"]["scriptsListIndex"], 750)
        self.assertEqual(guardian["program"]["logicalPath"], "scripts\\acklint.int")
        self.assertEqual(
            guardian["messageCatalog"]["logicalPath"],
            "text\\english\\dialog\\acklint.msg",
        )
        self.assertNotIn("nodes", guardian)
        self.assertNotIn("preTrialPlayerArtFids", guardian)
        self.assertNotIn("hostilityTrigger", guardian)
        self.assertEqual(profile["adapter"]["movementActionPointCost"], 1)
        self.assertEqual(
            profile["adapter"]["movementResolution"],
            "exact-adjacent-source-walk-mask-hex-v1",
        )
        self.assertFalse(profile["adapter"]["targetTurns"])
        self.assertFalse(profile["adapter"]["generalIntScripts"])
        self.assertTrue(profile["adapter"]["boundedGuardianDialogue"])
        self.assertIn("source-acklint-dialogue", profile["adapter"]["identity"])

        contract = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleConfrontationContract.cs"
        ).read_text(encoding="utf-8")
        movement = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleMovementConsumer.cs"
        ).read_text(encoding="utf-8")
        player = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArroyoCavesPlayerRuntime.cs"
        ).read_text(encoding="utf-8")
        combat = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleConfrontationRuntime.cs"
        ).read_text(encoding="utf-8")
        proof = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/CharacterStart/Fo2TempleConfrontationProof.cs"
        ).read_text(encoding="utf-8")
        save = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStartSave.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("topLevelCritters.Length != 1", contract)
        self.assertIn("BuildShortestPath(int startTile, int targetTile)", movement)
        self.assertIn("internal bool TryTacticalStep(int destinationTile)", player)
        self.assertIn("internal bool TryMove(int destinationTile)", combat)
        self.assertIn("_state.PlayerActionPoints - _profile.MovementActionPointCost", combat)
        self.assertIn("TargetPlacementExact", combat)
        self.assertIn("internal bool Talk()", combat)
        self.assertIn("internal bool LookAtGuardian()", combat)
        self.assertIn('"look_at_p_proc"', combat)
        self.assertIn("internal bool SelectDialogueOption(int messageId)", combat)
        self.assertIn("confrontation.SelectDialogueOption(106)", proof)
        self.assertIn("confrontation.SelectDialogueOption(116)", proof)
        self.assertIn("confrontation.SelectDialogueOption(120)", proof)
        self.assertIn('["Node001", "Node003", "Node005"]', proof)
        self.assertIn("dialogueBeforeFirstDamage = true", proof)
        self.assertIn("confrontation.TryPostGuardianStep(destination)", proof)
        self.assertIn("confrontation.TryApplyTempleExit()", proof)
        self.assertIn("TargetMapIndex == 4", proof)
        self.assertIn("saved.TempleExitTransition == appliedTempleExit", proof)
        self.assertIn("destinationPresentationLoaded = false", proof)
        self.assertIn("BuildShortestPath(", proof)
        self.assertIn("confrontation.TryMove(destination)", proof)
        self.assertIn("proofSetupRepositionedToSourceWalkableAdjacentHex = false", proof)
        self.assertNotIn("player.Restore(\n                adjacentTile", proof)
        self.assertIn("targetAiExecuted = false", proof)
        self.assertIn("targetHitPoints = TempleConfrontation.TargetHitPoints", save)
        self.assertIn("playerActionPoints = TempleConfrontation.PlayerActionPoints", save)
        self.assertIn('opennv-fo2-character-arroyo-save/v15', save)
        self.assertIn('confrontation.Loot() || !confrontation.State.CombatActive', proof)
        self.assertIn('attack-player-requested', proof)
        self.assertIn('scriptState = TempleConfrontation.ScriptState.Save()', save)
        self.assertIn("ReadTempleExitTransition(", save)
        self.assertIn("ValidateTempleExitTransition(", save)
        self.assertIn("applied.TargetMapIndex != 4", save)


if __name__ == "__main__":
    unittest.main()
