import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CONTRACT = (
    ROOT / "runtime" / "config" / "classic-critical-selection-fo2-1.02-v1.json"
)
RUNTIME = (
    ROOT / "runtime" / "src" / "Campaigns" / "Classic" /
    "ClassicCriticalSelector.cs"
)


class ClassicCriticalSelectorTests(unittest.TestCase):
    def test_exact_build_selection_dimensions_are_data(self) -> None:
        contract = json.loads(CONTRACT.read_text(encoding="utf-8"))
        self.assertEqual(contract["exactBuild"], "fallout2-retail-1.02")
        self.assertEqual(contract["criticalScoreThresholds"], [20, 45, 70, 90, 100])
        self.assertEqual(contract["fumbleScoreThresholds"], [20, 50, 75, 95])
        self.assertEqual(contract["hitLocationCount"], 9)
        self.assertEqual(contract["fumbleTypeCount"], 7)
        self.assertEqual(contract["playerFumbleImmunityDays"], 6)

    def test_selector_keeps_effect_application_outside_selection(self) -> None:
        runtime = RUNTIME.read_text(encoding="utf-8")
        self.assertIn("SelectCritical", runtime)
        self.assertIn("SelectFumble", runtime)
        self.assertIn("criticalUpgradeBonus", runtime)
        self.assertIn("gameTime / contract.TicksPerDay", runtime)
        self.assertNotIn("DamageApplied", runtime)
        self.assertNotIn("Random.Shared", runtime)


if __name__ == "__main__":
    unittest.main()
