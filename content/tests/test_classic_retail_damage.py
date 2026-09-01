from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ClassicRetailDamageTest(unittest.TestCase):
    def test_fo2_exact_build_damage_order_is_data_bound(self) -> None:
        contract = json.loads(
            (ROOT / "runtime/config/classic-retail-damage-fo2-1.02-v1.json")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(contract["schema"], "opennv-classic-retail-damage/v1")
        self.assertEqual(contract["exactBuild"], "fallout2-retail-1.02")
        self.assertEqual(contract["damageMultiplierDivisor"], 2)
        self.assertEqual(contract["percentScale"], 100)

    def test_owner_preserves_retail_integer_operation_order(self) -> None:
        runtime = (
            ROOT / "runtime/src/Campaigns/Classic/ClassicRetailDamageOwner.cs"
        ).read_text(encoding="utf-8")
        ammunition = runtime.index("inputs.AmmunitionDivisor")
        outcome_divisor = runtime.index("contract.DamageMultiplierDivisor", ammunition)
        difficulty = runtime.index("inputs.DifficultyPercent", outcome_divisor)
        threshold = runtime.index("scaled - inputs.DamageThreshold", difficulty)
        resistance = runtime.index("inputs.DamageResistancePercent", threshold)
        self.assertLess(ammunition, outcome_divisor)
        self.assertLess(outcome_divisor, difficulty)
        self.assertLess(difficulty, threshold)
        self.assertLess(threshold, resistance)
        self.assertNotIn("Random.Shared", runtime)


if __name__ == "__main__":
    unittest.main()
