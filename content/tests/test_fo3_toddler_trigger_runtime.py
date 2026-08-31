from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
FO3 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout3"


class Fo3ToddlerTriggerRuntimeTest(unittest.TestCase):
    def test_toddler_motion_uses_only_configured_input_actions(self) -> None:
        source = (FO3 / "Fo3Cg01ToddlerWorld.cs").read_text(encoding="utf-8")

        self.assertIn("Input.GetVector(", source)
        self.assertIn("_contract.MoveLeftAction", source)
        self.assertIn("_contract.MoveRightAction", source)
        self.assertIn("_contract.MoveForwardAction", source)
        self.assertIn("_contract.MoveBackwardAction", source)
        self.assertNotIn("SetAcceptanceTarget", source)
        self.assertNotIn("AcceptanceDirection", source)
        self.assertNotIn("_acceptanceTarget", source)
        self.assertNotIn("AcceptanceWallClearance", source)

    def test_owned_trigger_dispatches_stage_and_dialogue_through_shared_owners(self) -> None:
        world = (FO3 / "Fo3Cg01ToddlerWorld.cs").read_text(encoding="utf-8")
        stage12 = (FO3 / "Fo3Cg01Stage12Transition.cs").read_text(encoding="utf-8")
        response = (FO3 / "Fo3Cg01Stage12DadResponse.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("trigger.BodyEntered += body =>", world)
        self.assertIn("entered(player);", world)
        self.assertIn("GamebryoStageCommandExecutor.ExecuteAll(commands", stage12)
        self.assertIn("GamebryoDialoguePlayback.RequireStageResult(", response)
        self.assertIn("GamebryoStageCommandExecutor.ExecuteAll(commands", response)
        stage10 = (FO3 / "Fo3Cg01Stage10Transition.cs").read_text(encoding="utf-8")
        self.assertIn("awaiting-source-owned-player-trigger-entry", stage10)
        self.assertIn("awaiting-source-owned-dad-response-completion", stage12)
        self.assertIn(
            "awaiting-source-owned-post-stage-14-package-completion", response
        )

    def test_post_stage14_executes_owned_packages_and_persists_stage20(self) -> None:
        contract = (FO3 / "Fo3Cg01PostStage14Transition.cs").read_text(
            encoding="utf-8"
        )
        flow = (FO3 / "Fo3OpeningFlow.Cg01.cs").read_text(encoding="utf-8")
        persistence = (FO3 / "Fo3OpeningFlow.Persistence.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("GamebryoPackageTravel.ArriveAtSourceTarget", flow)
        self.assertIn("CloseGatePackage", flow)
        self.assertIn("CloseDoorPackage", flow)
        self.assertIn("LeaveRoomPackage", flow)
        self.assertIn("EnableMovementAtSourceStage", flow)
        self.assertIn("source-backed-package-dialogue-runtime-ready", contract)
        self.assertIn("cg01PostStage14Transition", persistence)
        self.assertIn("Fo3Cg01Stage20Interaction", contract)
        self.assertIn("NextBoundaryBlocker", contract)
        self.assertIn("InstallStage20Interactions", (FO3 / "Fo3Cg01ToddlerWorld.cs").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
