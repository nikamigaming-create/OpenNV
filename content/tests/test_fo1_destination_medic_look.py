from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1DestinationMedicLookTest(unittest.TestCase):
    def test_compiler_requires_nearest_medic_map_actor_script_message_and_open_door(self) -> None:
        tool = (ROOT / "content" / "tools" / "prepare_fo1_destination_medic_look.py").read_text(encoding="utf-8")
        self.assertIn("nearest-reachable-script-medic-actor-after-opened-generic-door-v1", tool)
        self.assertIn("SCRIPT_MEDIC", tool)
        self.assertIn("look_at_p_proc", tool)
        self.assertIn("display-message-only", tool)
        self.assertIn("dialogue\": \"unimplemented-fail-closed", tool)
        self.assertIn("Medic owned PRO bytes do not match the MAP prototype hash", tool)
        self.assertIn("refusing to overwrite destination Medic look descriptor", tool)

    def test_runtime_keeps_dialogue_combat_and_ap_fail_closed_while_persisting_look_ledger(self) -> None:
        contract = (FO1 / "Fo1DestinationMedicLookContract.cs").read_text(encoding="utf-8")
        session = (FO1 / "Fo1TacticalSession.cs").read_text(encoding="utf-8")
        flow = (FO1 / "Fo1NewGameFlow.cs").read_text(encoding="utf-8")
        coordinator = (ROOT / "runtime" / "src" / "RuntimeCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("prerequisite join drifted", contract)
        self.assertIn("dialogue\") != \"unimplemented-fail-closed", contract)
        self.assertIn("TryLookAtAdjacentDestinationMedic", session)
        self.assertIn("_destinationMedicLookViewed", session)
        self.assertIn("destinationMedicLook", session)
        self.assertIn("RunDestinationMedicLookProof", flow)
        self.assertIn("RunDestinationMedicLookColdRestoreProof", flow)
        self.assertIn("not-proven-by-look-at-only", flow)
        self.assertIn("fo1-destination-medic-look-proof", coordinator)
        self.assertIn("fo1-destination-medic-look-cold-restore-proof", coordinator)


if __name__ == "__main__":
    unittest.main()
