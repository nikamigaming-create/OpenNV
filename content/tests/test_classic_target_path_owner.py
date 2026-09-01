from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ClassicTargetPathOwnerTest(unittest.TestCase):
    def test_shared_path_uses_classic_neighbors_and_fails_closed_on_missing_contracts(self) -> None:
        owner = (
            ROOT / "runtime/src/Campaigns/Classic/ClassicTargetPathOwner.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("Fo1HexMath.Neighbors(targetTile)", owner)
        self.assertIn("foreach (var neighbor in Fo1HexMath.Neighbors(tile))", owner)
        self.assertIn("Fo1HexMath.TileInDirection(state.CurrentTile, value)", owner)
        self.assertIn("DoorStateRequired", owner)
        self.assertIn("MultihexCoverageRequired", owner)
        self.assertIn("MoveAnimationRequired", owner)
        self.assertIn("StepActionPointCostRequired", owner)
        self.assertIn("state.ActionPoints - stepCost", owner)
        self.assertIn("ClassicAttackOwner.Prepare(", owner)
        self.assertIn("Fo1HexMath.Distance(state.CurrentTile, state.TargetTile)", owner)
        self.assertNotIn("MathF", owner)
        self.assertNotIn("Vector", owner)

    def test_fo2_plans_against_owned_zero_door_zero_multihex_topology_and_persists(self) -> None:
        topology = (
            ROOT / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleTopology.cs"
        ).read_text(encoding="utf-8")
        runtime = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleConfrontationRuntime.cs"
        ).read_text(encoding="utf-8")
        save = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStartSave.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("doors.Length == 0", topology)
        self.assertIn("multihexBlockers.Length == 0", topology)
        self.assertIn("row.Serial != catalog.Confrontation.Critter.Serial", topology)
        self.assertIn("ClassicTargetPathOwner.Plan(", runtime)
        self.assertIn("_topology.TargetWalkableTiles", runtime)
        self.assertIn("LastTargetPath = targetPath", runtime)
        self.assertNotIn(
            "Source target turn selected movement; AI-packet path execution is fail-closed.",
            runtime,
        )
        self.assertIn("lastTargetPath = TempleConfrontation.LastTargetPath", save)
        self.assertIn("ReadClassicTargetPath(value)", save)

    def test_fo1_rat_removes_authored_movement_limit_and_uses_same_owner(self) -> None:
        runtime = (
            ROOT / "runtime/src/Campaigns/Fallout1/Fo1TacticalSession.cs"
        ).read_text(encoding="utf-8")
        start = runtime.index("private void RunRatTurn()")
        end = runtime.index("private void RatAttack(Fo1Mob mob)", start)
        turn = runtime[start:end]
        self.assertIn("ClassicTargetPathOwner.Plan(", turn)
        self.assertIn("_sourceMultihexCoverageComplete", turn)
        self.assertNotIn("RatMovementLimitHexes", turn)
        self.assertNotIn("mob.SpendActionPoint()", turn)
        self.assertNotIn("mob.MoveTo(destination)", turn)


if __name__ == "__main__":
    unittest.main()
