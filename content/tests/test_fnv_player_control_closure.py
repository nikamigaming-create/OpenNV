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

    def test_open_world_handoff_releases_the_authored_camera_and_keeps_portal_owners(self):
        state = (OPENING / "OpeningQuestRuntime.State.cs").read_text(
            encoding="utf-8"
        )
        player = (
            ROOT / "runtime" / "src" / "World" / "Cells" / "CellPlayer.cs"
        ).read_text(encoding="utf-8")
        portal = (
            ROOT / "runtime" / "src" / "World" / "Portals" / "CellPortalTravel.cs"
        ).read_text(encoding="utf-8")

        complete = state[state.index("private void CompleteOpening()") :]
        release = player[player.index("internal void ReleaseAuthoredCameraPresentation()") :]
        self.assertIn("_loaded.Player.ReleaseAuthoredCameraPresentation();", complete)
        self.assertLess(
            complete.index("ReleaseAuthoredCameraPresentation"),
            complete.index("ApplyStageControlPolicy"),
        )
        self.assertIn("_configuration.Player.DesktopCameraOffsetMeters.Vector3()", release)
        self.assertIn("Basis.Identity", release)
        self.assertIn("_session.CrossPortal(", portal)
        self.assertIn("_activeSet.Activate(target.CellFormId);", portal)
        self.assertIn("_environmentSet?.Activate(target.CellFormId);", portal)

    def test_owned_pipboy_reset_publishes_inventory_and_control_state(self):
        state = (OPENING / "OpeningQuestRuntime.State.cs").read_text(encoding="utf-8")
        session = (
            ROOT / "runtime" / "src" / "Gameplay" / "State" / "GameplaySession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn('case "resetPipBoyManager":', state)
        self.assertIn(
            "_loaded.Session.PublishOpeningState(CaptureState(false));",
            state,
        )
        self.assertIn(
            "_loaded.Session.SetPipBoyControlEnabled(_playerControls[PipBoyControlIndex]);",
            state,
        )
        self.assertIn("_loaded.Session.SetPipBoyControlEnabled(false);", state)
        self.assertIn("internal void PublishOpeningState(OpeningCampaignState state)", session)
        self.assertIn("if (_pipBoyControlEnabled)", session)


if __name__ == "__main__":
    unittest.main()
