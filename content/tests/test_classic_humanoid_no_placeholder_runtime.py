from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"
FO2 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "CharacterStart"
RUNTIME_COORDINATOR = ROOT / "runtime" / "src" / "RuntimeCoordinator.cs"


class ClassicHumanoidNoPlaceholderRuntimeTest(unittest.TestCase):
    def test_creator_and_player_routes_admit_only_hash_bound_full_body_donors(self) -> None:
        fo1_loader = (FO1 / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")
        fo1_creator = (FO1 / "Fo1CharacterCreator.cs").read_text(encoding="utf-8")
        fo1_custom = (FO1 / "Fo1CustomAppearanceEditor.cs").read_text(encoding="utf-8")
        coordinator = RUNTIME_COORDINATOR.read_text(encoding="utf-8")
        fo2_picker = (FO2 / "Fo2CharacterPicker.cs").read_text(encoding="utf-8")
        fo2_preview = (FO2 / "Fo2PremadeHumanoidPreview.cs").read_text(encoding="utf-8")
        fo2_custom = (FO2 / "Fo2CustomCharacterEditor.cs").read_text(encoding="utf-8")

        self.assertIn("Fo2HumanoidDonorContract classicHumanoidDonor", fo1_loader)
        self.assertNotIn("classicHumanoidDonor is not null", fo1_loader)
        self.assertIn("RequireFromOptions(options)", coordinator)
        self.assertIn("male and female verified player donor contracts", fo1_creator)
        self.assertNotIn("Fo1ProceduralHeadPreview", fo1_creator + fo1_custom)
        self.assertIn("no substitute live 3D head", fo1_custom)

        self.assertIn("Fo2HumanoidDonorContract humanoidDonor", fo2_picker)
        self.assertIn("new Fo2HumanoidVisual(", fo2_preview)
        self.assertIn("Fo2HumanoidIdentity.FromPremade(character)", fo2_preview)
        self.assertIn("owned-fnv-body-is-presentation-only-not-fallout2-character-geometry", fo2_preview)
        self.assertNotIn("Fo2FrmReliefMesh", fo2_preview)
        self.assertNotIn("Fo2ProceduralHeadPreview", fo2_custom)
        self.assertIn("no substitute live 3D head", fo2_custom)

        canonical_creator_paths = fo1_creator + fo1_custom + fo2_picker + fo2_preview + fo2_custom
        for mesh in ("CapsuleMesh", "CylinderMesh", "BoxMesh", "SphereMesh"):
            self.assertNotIn(mesh, canonical_creator_paths)


if __name__ == "__main__":
    unittest.main()
