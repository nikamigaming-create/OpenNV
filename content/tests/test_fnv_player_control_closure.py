import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
OPENING = ROOT / "runtime" / "src" / "Campaigns" / "NewVegas" / "Opening"


class FnvPlayerControlClosureTest(unittest.TestCase):
    def test_runtime_uses_the_owned_fnv_control_argument_order(self):
        contracts = (OPENING / "OpeningFlowContracts.cs").read_text(encoding="utf-8")
        runtime = (OPENING / "OpeningQuestRuntime.cs").read_text(encoding="utf-8")
        state = (OPENING / "OpeningQuestRuntime.State.cs").read_text(encoding="utf-8")

        for argument in (
            '"movement"',
            '"pipBoy"',
            '"fighting"',
            '"pointOfView"',
            '"looking"',
            '"rolloverText"',
            '"sneaking"',
        ):
            self.assertIn(argument, contracts)
        self.assertIn("private const int LookingControlIndex = 4;", runtime)
        self.assertIn("private const int RolloverTextControlIndex = 5;", runtime)
        self.assertIn("Enabled(MovementControlIndex),\n            Enabled(LookingControlIndex)", state)
        self.assertNotIn("ActivationControlIndex", runtime)

    def test_closed_creator_releases_gui_input_before_deferred_capture(self):
        state = (OPENING / "OpeningQuestRuntime.State.cs").read_text(encoding="utf-8")
        player = (
            ROOT / "runtime" / "src" / "World" / "Cells" / "CellPlayer.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("GetViewport().GuiReleaseFocus();", state)
        self.assertIn(
            "_viewport.MouseFilter = Control.MouseFilterEnum.Ignore;",
            state,
        )
        self.assertIn(
            "Callable.From(CaptureDesktopPointerAfterUiDispatch).CallDeferred();",
            state,
        )
        self.assertIn(
            "if (_activeModal is null && _playerControls[LookingControlIndex])",
            state,
        )
        self.assertIn(
            "Input.WarpMouse(GetViewport().GetVisibleRect().GetCenter());",
            player,
        )


if __name__ == "__main__":
    unittest.main()
