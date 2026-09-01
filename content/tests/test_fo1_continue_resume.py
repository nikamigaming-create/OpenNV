from __future__ import annotations

import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1ContinueResumeTest(unittest.TestCase):
    def test_continue_is_exposed_only_by_the_restored_session_gate(self) -> None:
        menu = (FO1 / "Fo1MainMenu.cs").read_text(encoding="utf-8")
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))

        self.assertIn("internal event Action? ContinueRequested;", menu)
        self.assertIn("if (_continueAvailable)", menu)
        self.assertIn('BuildMenuButton("CONTINUE")', menu)
        self.assertIn("menu.Configure(", flow)
        self.assertIn("loaded.Session.CanContinue,", flow)
        self.assertIn(
            "loaded.Session.CreateSaveSlotCatalog().ReadSlots().Count > 0", flow
        )
        self.assertIn(
            "var profile = loaded.Session.RequireRestoredCharacterForContinue();",
            flow,
        )
        self.assertIn("if (!_restoredCharacterFromSave", session)
        self.assertIn("_pendingSavedPlayerPresentation is not null", session)

    def test_continue_bypasses_new_game_and_preserves_saved_tile(self) -> None:
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        resume_start = flow.index("private static async Task ResumeInteractive(")
        resume_end = flow.index("private static void ShowCharacterSelection(", resume_start)
        resume = flow[resume_start:resume_end]
        self.assertNotIn("PlayOpening(", resume)
        self.assertNotIn("ShowCharacterSelection(", resume)
        self.assertIn("AttachPipBoy(contract, profile);", resume)
        self.assertIn("AttachClassicInterface(contract, loaded.Settings);", resume)
        self.assertIn("RevealRestoredWorld", resume)

        reveal_start = flow.index("private static async Task RevealRestoredWorld(")
        reveal_end = flow.index("private static async Task RunCombatShowcase(", reveal_start)
        reveal = flow[reveal_start:reveal_end]
        self.assertIn("var savedTile = loaded.Session.PlayerTile;", reveal)
        self.assertIn("loaded.Session.SnapPlayerToHexCenter();", reveal)
        self.assertNotIn("loaded.EntryTile", reveal)
        self.assertNotIn("PlayOpening(", reveal)
        self.assertLess(
            reveal.index("loaded.Camera.ProcessMode = Node.ProcessModeEnum.Inherit;"),
            reveal.index("loaded.Session.ProcessMode = Node.ProcessModeEnum.Inherit;"),
        )

    def test_identity_migrates_explicitly_and_weapon_policy_fails_closed(self) -> None:
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        self.assertIn(
            '"Legacy Fallout 1 character save has no presentation identity."',
            session,
        )
        self.assertIn("ParseSavedCharacterIdentity", session)
        self.assertIn("ParseLegacyCharacterIdentity", session)
        self.assertNotIn("FromLegacyProfile", session)
        self.assertIn("!binding.WeaponAttachmentsBound", session)
        self.assertIn("binding.WeaponVisualsSuppressed", session)
        self.assertIn("_ownedPlayer.Value.Root.IsAncestorOf", session)
        self.assertIn(
            'private const string SaveSchema = "opennv-fo1-hex-save/v1";', session
        )

    def test_camera_state_is_finite_required_and_applied_before_controls(self) -> None:
        camera = (FO1 / "Fo1TacticalCamera.cs").read_text(encoding="utf-8")
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))

        self.assertIn('Schema = "opennv-fo1-camera-state/v1"', camera)
        self.assertIn(
            'state.Mode is not ("hex-tactical" or "shoulder" or "first-person")',
            camera,
        )
        for field in (
            "state.YawRadians",
            "state.PitchRadians",
            "state.TacticalZoomMeters",
            "state.ShoulderDistanceMeters",
        ):
            self.assertIn(f"!float.IsFinite({field})", camera)
        self.assertIn("camera = cameraState?.SaveState()", session)
        self.assertIn("Fo1CameraSaveState.Load(camera)", session)
        self.assertIn("_ = RequireRestoredCameraForContinue();", session)

        reveal_start = flow.index("private static async Task RevealRestoredWorld(")
        reveal_end = flow.index("private static async Task RunCombatShowcase(", reveal_start)
        reveal = flow[reveal_start:reveal_end]
        apply_camera = reveal.index("loaded.Camera.ApplySaveState(cameraState);")
        release_camera = reveal.index(
            "loaded.Camera.ProcessMode = Node.ProcessModeEnum.Inherit;"
        )
        release_controls = reveal.index(
            "loaded.Session.ProcessMode = Node.ProcessModeEnum.Inherit;"
        )
        self.assertLess(apply_camera, release_camera)
        self.assertLess(release_camera, release_controls)

    def test_new_game_persists_camera_after_selected_mode_is_initialized(self) -> None:
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        reveal_start = flow.index("private static async Task<LandingPlayback> RevealWorld(")
        reveal_end = flow.index("private static async Task RevealRestoredWorld(", reveal_start)
        reveal = flow[reveal_start:reveal_end]
        mode = reveal.index('if (startPresentation == "hex-tactical")')
        persist = reveal.index("loaded.Session.PersistCameraState();")
        self.assertLess(mode, persist)

    def test_continue_routes_saved_destination_through_the_menu_event(self) -> None:
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        menu = (FO1 / "Fo1MainMenu.cs").read_text(encoding="utf-8")
        coordinator = read_csharp_source_module((ROOT / "runtime" / "src" / "RuntimeCoordinator.cs"))
        wrapper = (ROOT / "scripts" / "Test-OpenNVFallout1ContinueVault13.ps1").read_text(encoding="utf-8")
        self.assertIn("RequestContinueForHeadlessProof", menu)
        self.assertIn("ContinueRequested?.Invoke()", menu)
        self.assertIn("RunContinueMenuProof", flow)
        self.assertIn("RevealRestoredDestination", flow)
        self.assertIn("Fo1MainMenu.ContinueRequested", flow)
        self.assertIn("fo1-continue-menu-proof", coordinator)
        self.assertIn("fo1-new-game", wrapper)
        self.assertIn("fo1-continue-menu-proof", wrapper)
        self.assertIn("activeMap.presentation.path", wrapper)
        self.assertIn("sourceWalkMaskOnly", wrapper)


if __name__ == "__main__":
    unittest.main()
