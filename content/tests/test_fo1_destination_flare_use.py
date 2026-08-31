from __future__ import annotations

import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1DestinationFlareUseTest(unittest.TestCase):
    def test_compiler_requires_source_item_script_and_expiry_evidence(self) -> None:
        tool = (ROOT / "content" / "tools" / "prepare_fo1_destination_flare_use.py").read_text(encoding="utf-8")
        parser = (ROOT / "content" / "tools" / "classic_ssl_effects.py").read_text(encoding="utf-8")
        self.assertIn("PID_FLARE", tool)
        self.assertIn("SCRIPT_FLARE", tool)
        self.assertIn("decode_flare_effects", tool)
        self.assertIn("use_proc", parser)
        self.assertIn("set_local_var", parser)
        self.assertIn("game_time", parser)
        self.assertIn("opennv-classic-script-effects/v1", parser)
        self.assertIn('"valueFrom": "game-time"', parser)
        self.assertIn("decoded-destroy-self", parser)
        self.assertIn('"start_proc"', parser)
        self.assertIn('"destroy-self"', parser)
        self.assertIn("refusing to overwrite flare use descriptor", tool)

    def test_runtime_executes_strict_expiry_and_persists_destroyed_inventory_state(self) -> None:
        contract = (FO1 / "Fo1DestinationFlareUseContract.cs").read_text(encoding="utf-8")
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        inventory = (FO1 / "Fo1ClassicInventoryScreen.cs").read_text(encoding="utf-8")
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        self.assertIn("interaction join drifted", contract)
        self.assertIn("UseInventoryScriptedItem", session)
        self.assertIn("_destinationFlareScriptState", session)
        self.assertIn('Program.Execute(\n                "use_proc"', session)
        self.assertIn("destinationFlare", session)
        self.assertIn("ProcessClassicTimedWorldActions", session)
        self.assertIn("execution.DestroySelf", session)
        self.assertIn("_destinationFlareExpired", session)
        self.assertIn("DestinationFlareExpired", session)
        self.assertIn("InventoryObjects(flare.Symbol) - 1", session)
        self.assertIn("decoded-destroy-self", contract)
        self.assertIn("UseSelectedSourceInventoryForProof", inventory)
        self.assertIn("activeHand = \"not-proven-by-script\"", flow)
        self.assertIn("source-script flare state", flow)


if __name__ == "__main__":
    unittest.main()
