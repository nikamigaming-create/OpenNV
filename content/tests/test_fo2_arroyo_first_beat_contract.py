from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TEMPLE = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "Temple"
FIRST_BEAT = TEMPLE / "Fo2ArroyoArrivalFirstBeatProof.cs"
RUNTIME = TEMPLE / "Fo2ArroyoCavesPlayerRuntime.cs"
HOST = TEMPLE / "Fo2ArroyoArrivalFirstBeatProofHost.cs"
SCENE = TEMPLE / "Fo2ArroyoArrivalFirstBeatProof.tscn"
SCRIPT = ROOT / "scripts" / "Test-OpenNVFallout2ArroyoFirstBeat.ps1"


class Fo2ArroyoFirstBeatContractTest(unittest.TestCase):
    def test_first_beat_is_derived_from_the_verified_map_component(self) -> None:
        source = FIRST_BEAT.read_text(encoding="utf-8")

        self.assertIn("catalog.ArrivalTile", source)
        self.assertIn("catalog.Walkable", source)
        self.assertIn("catalog.WalkMaskSha256", source)
        self.assertIn("Fo2TempleMovementConsumer.MaskSha256", source)
        self.assertIn("var predecessors = new Dictionary<int, int>", source)
        self.assertIn("var pending = new Queue<int>();", source)
        self.assertIn("Fo1HexMath.Neighbors(tile)", source)
        self.assertIn("PathFromArrival(", source)
        self.assertIn("legalPathTiles = firstBeat.LegalPathTiles", source)
        self.assertIn("!component.Contains(neighbor)", source)
        self.assertIn("no source-bound reachable boundary tile", source)
        self.assertNotIn("28707", source)
        self.assertNotIn("31907", source)
        self.assertNotIn("32107", source)

    def test_runtime_and_headless_proof_share_the_fail_closed_contract(self) -> None:
        runtime = RUNTIME.read_text(encoding="utf-8")
        host = HOST.read_text(encoding="utf-8")
        script = SCRIPT.read_text(encoding="utf-8")

        self.assertIn("Fo2ArroyoArrivalFirstBeat.RequireArrivalComponent(catalog)", runtime)
        self.assertIn(
            "selected character and owned humanoid donor must be bound together",
            runtime,
        )
        self.assertIn("Fo2HumanoidDonorContract.RequireFromOptions(options)", host)
        self.assertIn("Fo2ArroyoArrivalFirstBeatProof.Run", host)
        self.assertIn("LoadFromPresentationOutput(temple)", host)
        self.assertNotIn('Require(options, "fo2-temple-transitions")', host)
        self.assertIn("--headless", script)
        self.assertIn("ClassicHumanoidInstallManifest", script)
        self.assertIn("Resolve-ClassicHumanoidDonorPreviewSet.ps1", script)
        self.assertIn("Assert-ClassicHumanoidDonorPreviewSet.ps1", script)
        self.assertIn("Resolve-Fo2TempleTransitionOutput.ps1", script)
        self.assertNotIn("TempleTransitions", script)
        self.assertNotIn("--fo2-temple-transitions", script)
        self.assertIn("$resolverOutput = @(& $resolver", script)
        self.assertIn("did not emit exactly one preview-set path", script)
        self.assertNotIn("Classic humanoid install-manifest resolution failed.", script)
        self.assertIn("legalPathAccepted", script)
        self.assertIn("expectedLegalMoves", script)
        self.assertIn("invalidMoveRejected", script)
        self.assertNotIn("Capture", script)
        self.assertNotIn("--windowed", script)
        self.assertNotIn("28707", script)
        self.assertTrue(SCENE.is_file())


if __name__ == "__main__":
    unittest.main()
