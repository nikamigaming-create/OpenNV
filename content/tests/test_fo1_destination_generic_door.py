from __future__ import annotations

import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1DestinationGenericDoorTest(unittest.TestCase):
    def test_compiler_selects_only_reachable_unscripted_owned_door_blocker(self) -> None:
        tool = (ROOT / "content" / "tools" / "prepare_fo1_destination_generic_door.py").read_text(encoding="utf-8")
        self.assertIn("nearest-reachable-unscripted-scenery-door-blocker-v1", tool)
        self.assertIn("NO_SCRIPT_INDEX", tool)
        self.assertIn("NO_SCRIPT_ID", tool)
        self.assertIn("generic-door supplied master.dat does not match the transport input hash", tool)
        self.assertIn("Fo1ResourceResolver", tool)
        self.assertIn("frm_summary", tool)
        self.assertIn("decode_classic_door", tool)
        self.assertIn("materialize_classic_door_assets", tool)
        self.assertIn("refusing to overwrite destination generic-door descriptor", tool)

    def test_runtime_requires_explicit_hash_bound_door_and_persists_only_passability(self) -> None:
        contract = (FO1 / "Fo1DestinationGenericDoorContract.cs").read_text(encoding="utf-8")
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        coordinator = read_csharp_source_module((ROOT / "runtime" / "src" / "RuntimeCoordinator.cs"))
        wrapper = (ROOT / "scripts" / "Test-OpenNVFallout1ContinueVault13.ps1").read_text(encoding="utf-8")
        self.assertIn("presentation join drifted", contract)
        self.assertIn("MAP join drifted", contract)
        self.assertIn("no-script-boundary-generic-door-open-passability-only", contract)
        self.assertIn("unsupported behavior", contract)
        self.assertIn("TryActivateAdjacentDestinationGenericDoor", session)
        self.assertIn("_destinationGenericDoorOpen", session)
        self.assertIn("destinationGenericDoor", session)
        self.assertIn("genericDoor", session)
        self.assertIn("movedThroughOpenedBlocker", flow)
        self.assertIn("fo1-continue-generic-door-proof", coordinator)
        self.assertIn("DestinationGenericDoor", wrapper)
        self.assertIn("framesPerSecond", wrapper)
        self.assertIn("sourceFrame", wrapper)
        self.assertIn("ClassicDoorSession", session)
        self.assertIn("ClassicDoorPlayback", session)
        self.assertIn("BeginOpening", session)
        playback = (
            ROOT / "runtime/src/Campaigns/Classic/ClassicDoorPlayback.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("_session.Advance(delta)", playback)
        self.assertIn("AudioStreamWav.LoadFromFile", playback)
        self.assertIn("_sprite.Texture = _textures[state.Frame]", playback)
        self.assertIn("sourcePresentationState", (
            FO1 / "Fo1TacticalSession.Persistence.cs"
        ).read_text(encoding="utf-8"))
        self.assertIn("ClassicDoorState.Restore", (
            FO1 / "Fo1TacticalSession.Persistence.cs"
        ).read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
