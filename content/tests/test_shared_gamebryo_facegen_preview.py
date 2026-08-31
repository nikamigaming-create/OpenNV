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
MORPH_RUNTIME = (
    ROOT
    / "runtime"
    / "src"
    / "Presentation"
    / "CharacterCreation"
    / "OwnedGamebryoFaceGenMorphRuntime.cs"
)
TEXTURE_RUNTIME = (
    ROOT
    / "runtime"
    / "src"
    / "Presentation"
    / "CharacterCreation"
    / "OwnedGamebryoFaceGenTextureRuntime.cs"
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

    def test_both_creators_apply_source_ui_values_through_shared_morph_owner(self) -> None:
        morph_runtime = MORPH_RUNTIME.read_text(encoding="utf-8")
        fnv_state = (FNV / "OpeningQuestRuntime.State.cs").read_text(encoding="utf-8")
        fnv_ui = (FNV / "OpeningQuestRuntime.CharacterCreation.cs").read_text(
            encoding="utf-8"
        )
        fo3_contract = (FO3 / "Fo3OpeningContracts.cs").read_text(encoding="utf-8")
        fo3_ui = (FO3 / "Fo3OpeningFlow.Cg00.cs").read_text(encoding="utf-8")

        self.assertIn("class OwnedGamebryoFaceGenMorphRuntime", morph_runtime)
        self.assertIn("OwnedGamebryoFaceGenMorphRuntime.Evaluate(", fnv_state)
        self.assertIn("OwnedGamebryoFaceGenMorphRuntime.Advance(", fo3_contract)
        self.assertIn("OwnedGamebryoFaceGenMorphRuntime.Publish(", fnv_ui)
        self.assertIn("OwnedGamebryoFaceGenMorphRuntime.Publish(", fo3_ui)
        self.assertNotIn(
            "(float)value * activeControl.MorphWeightScale",
            fo3_ui,
        )

    def test_shared_preview_owns_exact_egt_texture_recomposition(self) -> None:
        host = HOST.read_text(encoding="utf-8")
        texture_runtime = TEXTURE_RUNTIME.read_text(encoding="utf-8")
        contracts = CONTRACTS.read_text(encoding="utf-8")

        self.assertIn("OwnedGamebryoFaceGenTextureRuntime", host)
        self.assertIn("internal void ApplyTexture(", host)
        self.assertIn("HasSupportedSignature", texture_runtime)
        self.assertIn("SHA256.HashData(_egt)", texture_runtime)
        self.assertIn("source.TextureControls", host)
        self.assertIn("OpeningNativeFaceGenTextureControl", contracts)
        fnv = (FNV / "OpeningQuestRuntime.CharacterCreation.cs").read_text(
            encoding="utf-8"
        )
        fo3 = (FO3 / "Fo3OpeningFlow.Cg00.cs").read_text(encoding="utf-8")
        fo3_save = (FO3 / "Fo3OpeningFlow.Persistence.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("previewHost.ApplyTexture(", fnv)
        self.assertIn("_activeFacePreview.ApplyTexture(", fo3)
        self.assertIn("textureControls = SavedTextureControls(selection)", fo3_save)


if __name__ == "__main__":
    unittest.main()
