from __future__ import annotations

import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"
FO2 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "CharacterStart"
FNV_OPENING = ROOT / "runtime" / "src" / "Campaigns" / "NewVegas" / "Opening"
FO3 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout3"
RUNTIME_COORDINATOR = ROOT / "runtime" / "src" / "RuntimeCoordinator.cs"


class ClassicHumanoidNoPlaceholderRuntimeTest(unittest.TestCase):
    def test_creator_and_player_routes_admit_only_hash_bound_full_body_donors(self) -> None:
        fo1_loader = (FO1 / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")
        fo1_creator = (FO1 / "Fo1CharacterCreator.cs").read_text(encoding="utf-8")
        fo1_custom = (FO1 / "Fo1CustomAppearanceEditor.cs").read_text(encoding="utf-8")
        fo1_portrait = (FO1 / "Fo1CustomPortraitPreview.cs").read_text(encoding="utf-8")
        coordinator = read_csharp_source_module(RUNTIME_COORDINATOR)
        fo2_picker = (FO2 / "Fo2CharacterPicker.cs").read_text(encoding="utf-8")
        fo2_preview = (FO2 / "Fo2PremadeHumanoidPreview.cs").read_text(encoding="utf-8")
        fo2_custom = (FO2 / "Fo2CustomCharacterEditor.cs").read_text(encoding="utf-8")

        self.assertIn("Fo2HumanoidDonorContract classicHumanoidDonor", fo1_loader)
        self.assertNotIn("classicHumanoidDonor is not null", fo1_loader)
        self.assertIn("RequireFromOptions(options)", coordinator)
        self.assertIn("male and female verified player donor contracts", fo1_creator)
        self.assertNotIn("Fo1ProceduralHeadPreview", fo1_creator + fo1_custom)
        self.assertIn("new OpeningRaceSexRenderedDeviceHost(", fo1_custom)
        self.assertIn("_portrait.SetPreviewState(_faceFraming, _greenProjection)", fo1_custom)
        self.assertIn('ActivateCreatorModeControl("FACE")', fo1_custom)
        self.assertIn('ActivateCreatorModeControl("BODY")', fo1_custom)
        self.assertIn('ActivateCreatorModeControl("PROJECTION")', fo1_custom)
        self.assertIn("owned-data custom donor", fo1_portrait)
        self.assertIn("never replaces authored premade art", fo1_portrait)
        self.assertIn('"VIEW: PORTRAIT"', fo1_creator)
        self.assertIn('"VIEW: 3D"', fo1_creator)
        self.assertIn("_pickerPortraitToggle.ToggleMode = true", fo1_creator)

        self.assertIn("Fo2HumanoidDonorContract humanoidDonor", fo2_picker)
        self.assertIn("new Fo2HumanoidVisual(", fo2_preview)
        self.assertIn("Fo2HumanoidIdentity.FromPremade(character)", fo2_preview)
        self.assertIn("owned-fnv-body-is-presentation-only-not-fallout2-character-geometry", fo2_preview)
        self.assertNotIn("Fo2FrmReliefMesh", fo2_preview)
        self.assertNotIn("Fo2ProceduralHeadPreview", fo2_custom)
        self.assertIn("verified owned full-body donor drives both face and body views", fo2_custom)
        self.assertIn("green-wireframe-body-projection", fo2_custom)
        self.assertIn("green-face-wireframe-closeup-projection", fo2_custom)
        self.assertIn('"VIEW: PORTRAIT"', fo2_picker)
        self.assertIn('"VIEW: 3D"', fo2_picker)
        self.assertIn("_panel.Visible = true", fo2_picker)
        self.assertIn("portraitFraming: true", fo2_picker)
        self.assertIn("projectionEnabled: true", fo2_picker)

        canonical_creator_paths = fo1_creator + fo1_custom + fo1_portrait + fo2_picker + fo2_preview + fo2_custom
        for mesh in ("CapsuleMesh", "CylinderMesh", "BoxMesh", "SphereMesh"):
            self.assertNotIn(mesh, canonical_creator_paths)

    def test_all_four_campaigns_use_the_same_reflectron_device_host(self) -> None:
        creator_routes = (
            FO1 / "Fo1CustomAppearanceEditor.cs",
            FO2 / "Fo2CustomCharacterEditor.cs",
            FNV_OPENING / "OpeningQuestRuntime.cs",
            FO3 / "Fo3OpeningFlow.cs",
        )

        for route in creator_routes:
            source = read_csharp_source_module(route)
            self.assertIn("new OpeningRaceSexRenderedDeviceHost(", source, route.name)
            self.assertIn("ConfigureCharacterControls(", source, route.name)
            self.assertIn("SetCreatorModeState(", source, route.name)

    def test_new_vegas_movie_stays_on_the_authored_creator_boundary(self) -> None:
        coordinator = read_csharp_source_module(RUNTIME_COORDINATOR)
        character_video = coordinator.split(
            "private async Task RunOpeningCharacterVideo", 1
        )[1].split("private async Task RunPoolProof", 1)[0]

        self.assertIn('"creator"', character_video)
        self.assertIn("appearancePresentationHoldFrames: 90", character_video)
        self.assertNotIn('"checkpoint"', character_video)


if __name__ == "__main__":
    unittest.main()
