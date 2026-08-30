from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
FO1_PREVIEW = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1" / "Fo1PremadePlayerPreview.cs"
FO1_SESSION = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1" / "Fo1TacticalSession.cs"
FO1_LOADER = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1" / "Fo1HexSceneLoader.cs"
FO2_DONOR = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "Temple" / "Fo2HumanoidPresentation.cs"
FO2_PLAYER = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "Temple" / "Fo2ArroyoPlayerPresentation.cs"
FO2_RUNTIME = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "Temple" / "Fo2ArroyoCavesPlayerRuntime.cs"
RETAIL_ACTOR_MATERIAL = ROOT / "runtime" / "src" / "RetailActorMaterial.cs"


class ClassicHumanoidRuntimeSourceTest(unittest.TestCase):
    def test_fo1_has_no_procedural_humanoid_route(self) -> None:
        preview = FO1_PREVIEW.read_text(encoding="utf-8")
        session = FO1_SESSION.read_text(encoding="utf-8")

        self.assertIn("owned-humanoid-donor-unavailable-fail-closed", preview)
        self.assertIn("no-substitute-humanoid-rendered-donor-selection-mismatch", preview)
        self.assertNotIn("Fo1FirstPartyHumanoid", preview)
        self.assertNotIn("Fo1FirstPartyHumanoid", session)
        self.assertIn("has no compatible hash-bound owned humanoid donor", session)
        loader = FO1_LOADER.read_text(encoding="utf-8")
        self.assertIn('new[] { "male", "female" }', loader)
        self.assertIn("PlayerDonors", loader)
        self.assertIn("SelectOwnedPlayerDonor(profile.Sex)", session)
        self.assertIn("source-bound weapon/socket contracts", session)

    def test_fo2_consumes_hash_bound_modular_full_body_donor(self) -> None:
        donor = FO2_DONOR.read_text(encoding="utf-8")
        player = FO2_PLAYER.read_text(encoding="utf-8")
        runtime = FO2_RUNTIME.read_text(encoding="utf-8")

        self.assertIn("opennv-owned-player-facegen-preview-set/v3", donor)
        self.assertIn('RequiredBodyRoles = ["body", "left-hand", "right-hand"]', donor)
        self.assertIn("presentationOutfitFormId", donor)
        self.assertIn("rigidAttachmentNode", donor)
        self.assertIn("classic-humanoid-donor-preview-set", donor)
        self.assertIn("RequireFromOptions", donor)
        self.assertIn("new Fo2HumanoidVisual(", runtime)
        self.assertIn("_presentation.Visible = false;", runtime)
        self.assertIn(
            "selected character and owned humanoid donor must be bound together",
            runtime,
        )
        self.assertIn("opennv-retail-actor-skin-material/v1", donor)
        self.assertIn("owned-nif-bs-shader-type-shaderskin", donor)
        self.assertIn("head-paired-cheek-uv-islands", donor)
        self.assertNotIn("upperbodymale.dds", donor)
        self.assertNotIn("upperbodyfemale.dds", donor)

        material = RETAIL_ACTOR_MATERIAL.read_text(encoding="utf-8")
        self.assertIn("opennv-retail-actor-skin-material/v1", material)
        self.assertIn("skin_complexion_target", material)
        self.assertIn("skin_encoded_to_linear", material)


if __name__ == "__main__":
    unittest.main()
