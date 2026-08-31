from __future__ import annotations

import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1DestinationMedicLookTest(unittest.TestCase):
    def test_compiler_requires_nearest_medic_map_actor_script_message_and_open_door(self) -> None:
        tool = (ROOT / "content" / "tools" / "prepare_fo1_destination_medic_look.py").read_text(encoding="utf-8")
        self.assertIn("nearest-reachable-script-medic-actor-after-opened-generic-door-v1", tool)
        self.assertIn("SCRIPT_MEDIC", tool)
        self.assertIn("look_at_p_proc", tool)
        self.assertIn("decode_single_message_look", tool)
        self.assertIn("decode_single_reply_option_dialogue", tool)
        self.assertIn('"effectProgram": effect_program', tool)
        self.assertIn("display-message-only", tool)
        self.assertIn("MedicStartHealing", tool)
        self.assertIn("decoded-bounded-option-results", tool)
        self.assertIn("decoded-targets-only", tool)
        self.assertIn("Medic owned PRO bytes do not match the MAP prototype hash", tool)
        self.assertIn("refusing to overwrite destination Medic look descriptor", tool)

    def test_runtime_executes_decoded_dialogue_targets_and_persists_the_procedure(self) -> None:
        contract = (FO1 / "Fo1DestinationMedicLookContract.cs").read_text(encoding="utf-8")
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        coordinator = read_csharp_source_module((ROOT / "runtime" / "src" / "RuntimeCoordinator.cs"))
        self.assertIn("prerequisite join drifted", contract)
        self.assertIn("decoded-bounded-option-results", contract)
        self.assertIn("UnsupportedDialogueTargets", contract)
        self.assertIn("TryLookAtAdjacentDestinationMedic", session)
        self.assertIn("ExecuteWithActions", session)
        self.assertIn("TryTalkToAdjacentDestinationMedicSeriouslyWounded", session)
        self.assertIn("TrySelectDestinationMedicDialogueOption", session)
        self.assertIn("_destinationMedicDialogueProcedure", session)
        self.assertIn("dialogueProcedure", session)
        self.assertIn("medic.UnsupportedDialogueTargets.Contains", session)
        self.assertIn("_destinationMedicLookViewed", session)
        self.assertIn("destinationMedicLook", session)
        self.assertIn("RunDestinationMedicLookProof", flow)
        self.assertIn("RunDestinationMedicLookColdRestoreProof", flow)
        self.assertIn("not-proven-by-look-at-only", flow)
        self.assertIn("fo1-destination-medic-look-proof", coordinator)
        self.assertIn("fo1-destination-medic-look-cold-restore-proof", coordinator)


if __name__ == "__main__":
    unittest.main()
