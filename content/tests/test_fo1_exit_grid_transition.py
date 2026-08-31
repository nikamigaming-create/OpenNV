from __future__ import annotations

import json
import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1ExitGridTransitionContractTest(unittest.TestCase):
    def test_recipe_is_explicit_map_world_data_with_reciprocal_source_join(self) -> None:
        recipe = json.loads((ROOT / "content" / "recipes" / "fo1-v13ent-exit-grid-transition-v1.json").read_text(encoding="utf-8"))
        tool = (ROOT / "content" / "tools" / "prepare_fo1_exit_grid_transition.py").read_text(encoding="utf-8")
        self.assertEqual(recipe["schema"], "opennv-fo1-exit-grid-transition-recipe/v1")
        self.assertEqual(recipe["destination"]["instanceValues"], [recipe["destination"]["mapIndex"], recipe["destination"]["tile"], recipe["destination"]["elevation"], recipe["destination"]["rotation"]])
        self.assertIn("reciprocalInstanceValues", recipe)
        self.assertIn("refusing to overwrite", tool)
        self.assertIn("source MAP has no exact exit grid", tool)
        self.assertIn("destination MAP has no reciprocal exit grid", tool)
        self.assertIn("destinationScenePolicy", tool)

    def test_runtime_requires_descriptor_to_match_scene_and_persists_only_a_real_trigger(self) -> None:
        contract = (FO1 / "Fo1ExitGridTransitionContract.cs").read_text(encoding="utf-8")
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        loader = (FO1 / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        wrapper = (ROOT / "scripts" / "Test-OpenNVFallout1NativeFirstBeat.ps1").read_text(encoding="utf-8")
        self.assertIn("ValidateAgainstScene", contract)
        self.assertIn("bool destinationSceneLoaded = false", contract)
        self.assertIn("transitionCommitted", contract)
        self.assertIn("exitGridTransition.ValidateAgainstScene", loader)
        self.assertIn("_exitGridTransition?.IsTrigger(_playerTile)", session)
        self.assertIn("TryActivateAdjacentSourceDoor", session)
        self.assertIn("_walkable[door.Tile] = true", session)
        self.assertIn("sourceDoor", session)
        self.assertIn("descriptorSha256", session)
        self.assertIn("Fallout save exit-grid activation is not a source trigger", session)
        self.assertIn("RunNativeFirstBeatCaveExitGridTransition", flow)
        self.assertIn("FindWalkablePathToAny", flow)
        self.assertIn("MoveTacticalAdjacentToSourceTile", flow)
        self.assertIn("session.TryActivateAdjacentSourceDoor()", flow)
        self.assertIn("sourceWalkMaskOnly = true", flow)
        self.assertIn("ExitGridTransition", wrapper)
        self.assertIn("--fo1-exit-grid-transition", wrapper)
        self.assertIn("doorActivation", wrapper)
        self.assertIn("DestinationPresentation", wrapper)
        self.assertIn("--fo1-destination-presentation", wrapper)

    def test_headless_preflight_reports_a_mask_blocker_without_inventing_a_route(self) -> None:
        proof = (ROOT / "content" / "tools" / "prove_fo1_exit_grid_transition.py").read_text(encoding="utf-8")
        self.assertIn("blocked-source-walk-mask-door-transition-unimplemented", proof)
        self.assertIn("sourceWalkMaskOnly", proof)
        self.assertIn("destinationSceneLoaded", proof)
        self.assertIn("refusing to overwrite", proof)

    def test_destination_is_loaded_only_after_transition_without_frm_player_fallback(self) -> None:
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        destination = (FO1 / "Fo1DestinationPresentationContract.cs").read_text(encoding="utf-8")
        viewer = (FO1 / "Fo1CampaignPresentationViewer.cs").read_text(encoding="utf-8")
        self.assertIn("LoadCommittedDestinationPresentation", flow)
        self.assertIn("includeSourcePlayer: false", flow)
        self.assertIn("MoveOneLegalDestinationHex", flow)
        self.assertIn("sourcePlayerFallback = false", destination)
        self.assertIn("transition.DestinationMapSha256", destination)
        self.assertIn("bool includeSourcePlayer = true", viewer)

    def test_destination_cold_restore_requires_explicit_saved_path_and_hash_join(self) -> None:
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        loader = (FO1 / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        wrapper = (ROOT / "scripts" / "Test-OpenNVFallout1NativeFirstBeat.ps1").read_text(encoding="utf-8")
        self.assertIn('ActiveMapSchema = "opennv-fo1-active-map/v1"', session)
        self.assertIn("activeMap = SaveActiveMap()", session)
        self.assertIn("LoadSavedDestination", session)
        self.assertIn("if (savedDestination is null)", session)
        self.assertIn("presentation path differs from launch input", session)
        self.assertIn("presentation hash drifted", session)
        self.assertIn("ApplyDestinationTacticalState(savedDestination, tile)", session)
        self.assertIn("session.LoadedDestinationPresentation is { } restoredDestination", loader)
        self.assertIn("includeSourcePlayer: false", loader)
        self.assertIn("RunDestinationColdRestoreProof", flow)
        self.assertIn("fo1-destination-cold-restore-proof", wrapper)
        self.assertIn("ColdRestoreReport", wrapper)


if __name__ == "__main__":
    unittest.main()
