from __future__ import annotations

import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1DestinationInventoryInteractionTest(unittest.TestCase):
    def test_compiler_selects_only_reachable_unscripted_source_container_with_hash_joins(self) -> None:
        tool = (ROOT / "content" / "tools" / "prepare_fo1_destination_inventory_interaction.py").read_text(encoding="utf-8")
        self.assertIn("nearest-reachable-unscripted-container-with-positive-source-inventory-v1", tool)
        self.assertIn("source.get(\"scriptIndex\") != -1", tool)
        self.assertIn("prototype.get(\"subtype_name\") != \"container\"", tool)
        self.assertIn("shortest_contact_path", tool)
        self.assertIn("transport/exit-grid source join drifted", tool)
        self.assertIn("presentation map hash join drifted", tool)
        self.assertIn("refusing to overwrite destination interaction descriptor", tool)

    def test_runtime_requires_explicit_descriptor_and_restores_looted_destination_host(self) -> None:
        contract = (FO1 / "Fo1DestinationInventoryInteractionContract.cs").read_text(encoding="utf-8")
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        coordinator = read_csharp_source_module((ROOT / "runtime" / "src" / "RuntimeCoordinator.cs"))
        self.assertIn("presentation join drifted", contract)
        self.assertIn("MAP join drifted", contract)
        self.assertIn("route is not source-adjacent", contract)
        self.assertIn("_destinationInventoryInteractionPath", session)
        self.assertIn("Fo1DestinationInventoryInteractionContract.Load", session)
        self.assertIn("savedLootedHostSerials", session)
        self.assertIn("_inactiveMapInventoryHostSerials", session)
        self.assertIn("sourceHostSerials", session)
        self.assertIn("inventory interaction hash drifted", session)
        self.assertIn("RunDestinationInventoryInteractionProof", flow)
        self.assertIn("RunDestinationInventoryInteractionColdRestoreProof", flow)
        self.assertIn("fo1-destination-inventory-interaction-proof", coordinator)
        self.assertIn("fo1-destination-inventory-interaction-cold-restore-proof", coordinator)


if __name__ == "__main__":
    unittest.main()
