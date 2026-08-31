from __future__ import annotations

import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1DestinationFlareUseTest(unittest.TestCase):
    def test_compiler_requires_source_item_script_and_expiry_evidence(self) -> None:
        tool = (ROOT / "content" / "tools" / "prepare_fo1_destination_flare_use.py").read_text(encoding="utf-8")
        self.assertIn("PID_FLARE", tool)
        self.assertIn("SCRIPT_FLARE", tool)
        self.assertIn("use_proc", tool)
        self.assertIn("set_local_var", tool)
        self.assertIn("game_time", tool)
        self.assertIn("unimplemented-fail-closed", tool)
        self.assertIn("refusing to overwrite flare use descriptor", tool)

    def test_runtime_keeps_flare_out_of_weapon_attachment_and_persists_only_bound_state(self) -> None:
        contract = (FO1 / "Fo1DestinationFlareUseContract.cs").read_text(encoding="utf-8")
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        inventory = (FO1 / "Fo1ClassicInventoryScreen.cs").read_text(encoding="utf-8")
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        self.assertIn("interaction join drifted", contract)
        self.assertIn("UseInventoryScriptedItem", session)
        self.assertIn("_destinationFlareLit", session)
        self.assertIn("destinationFlare", session)
        self.assertIn("UseSelectedSourceInventoryForProof", inventory)
        self.assertIn("activeHand = \"not-proven-by-script\"", flow)
        self.assertIn("source-script flare state", flow)


if __name__ == "__main__":
    unittest.main()
