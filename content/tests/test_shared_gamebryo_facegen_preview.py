from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST = (
    ROOT
    / "runtime"
    / "src"
    / "Presentation"
    / "CharacterCreation"
    / "OwnedGamebryoFaceGenPreviewHost.cs"
)
CONTRACTS = (
    ROOT
    / "runtime"
    / "src"
    / "Presentation"
    / "CharacterCreation"
    / "OwnedGamebryoFaceGenPreviewContracts.cs"
)
ACTOR_LOADER = (
    ROOT / "runtime" / "src" / "World" / "Actors" / "ActorModelSlice.cs"
)
FNV = ROOT / "runtime" / "src" / "Campaigns" / "NewVegas" / "Opening"
FO3 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout3"


class SharedGamebryoFaceGenPreviewTest(unittest.TestCase):
    def test_both_gamebryo_creators_use_the_shared_owned_actor_host(self) -> None:
        host = HOST.read_text(encoding="utf-8")
        actor_loader = ACTOR_LOADER.read_text(encoding="utf-8")
        fnv = (FNV / "OpeningQuestRuntime.CharacterCreation.cs").read_text(
            encoding="utf-8"
        )
        fo3 = (FO3 / "Fo3OpeningFlow.Cg00.cs").read_text(encoding="utf-8")

        self.assertIn("ActorModelSlice.Load(", host)
        self.assertNotIn("Campaigns.NewVegas", host)
        self.assertIn("VerifyHash(source.GltfPath, source.GltfSha256)", host)
        self.assertIn("VerifyHash(source.SidecarPath, source.SidecarSha256)", host)
        self.assertIn("ActorComplexionJoin.Apply(scene, surfaces)", actor_loader)
        self.assertIn("OwnedGamebryoFaceGenPreviewHost.Load(", fnv)
        self.assertIn("OwnedGamebryoFaceGenPreviewHost.Load(", fo3)

    def test_both_gamebryo_creators_use_shared_exact_selection_inventory(self) -> None:
        contracts = CONTRACTS.read_text(encoding="utf-8")
        fnv = (FNV / "OpeningQuestRuntime.CharacterCreation.cs").read_text(
            encoding="utf-8"
        )
        fo3 = (FO3 / "Fo3OpeningContracts.cs").read_text(encoding="utf-8")

        self.assertIn("class OwnedGamebryoFaceGenSelectionInventory", contracts)
        self.assertIn("internal static bool IsComplete(", contracts)
        self.assertIn("internal static OpeningPlayerFaceGenPreview Require(", contracts)
        self.assertIn("OwnedGamebryoFaceGenSelectionInventory.Require(", fnv)
        self.assertIn("OwnedGamebryoFaceGenSelectionInventory.Require(", fo3)

    def test_fo3_has_no_texture_tile_facegen_fallback(self) -> None:
        ui = (FO3 / "Fo3OpeningFlow.Ui.cs").read_text(encoding="utf-8")
        self.assertNotIn("RenderAppearancePreview", ui)
        self.assertNotIn("AppearancePreviewTile", ui)
        self.assertNotIn('Label("HEAD"', ui)
        self.assertNotIn('Label("HAIR"', ui)
        self.assertNotIn('Label("EYES"', ui)


if __name__ == "__main__":
    unittest.main()
