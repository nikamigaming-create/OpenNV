import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ATTACK_MODES = ROOT / "runtime/config/classic-attack-modes-fo2-1.02-v1.json"
FUMBLE = ROOT / "runtime/config/classic-fumble-resolution-fo2-1.02-v1.json"
MODE_OWNER = ROOT / "runtime/src/Campaigns/Classic/ClassicAttackModeOwner.cs"
FUMBLE_OWNER = ROOT / "runtime/src/Campaigns/Classic/ClassicFumbleOwner.cs"


class ClassicFumbleOwnerTests(unittest.TestCase):
    def test_basic_punch_identity_is_exact_build_data(self) -> None:
        catalog = json.loads(ATTACK_MODES.read_text(encoding="utf-8"))
        self.assertEqual(catalog["exactBuild"], "fallout2-retail-1.02")
        self.assertEqual(catalog["modes"], [{
            "id": "basic-punch",
            "hitMode": 4,
            "skillIndex": 3,
            "minimumDamage": 1,
            "maximumDamageDerivedStat": "melee-damage",
            "maximumDamageBonus": 2,
            "maximumRangeHexes": 1,
            "actionPointCost": 3,
            "animationCode": 16,
            "damageType": 0,
            "criticalFailureType": 0,
            "ammunitionPerAttack": 0,
        }])
        owner = MODE_OWNER.read_text(encoding="utf-8")
        self.assertIn("derivedMaximumDamage + mode.MaximumDamageBonus", owner)
        self.assertNotIn("basic-punch", owner)

    def test_fumble_effect_order_and_secondary_rolls_are_data(self) -> None:
        contract = json.loads(FUMBLE.read_text(encoding="utf-8"))
        self.assertEqual(contract["effectOrder"], [
            "drop", "hit-self", "hurt-self", "lose-turn",
            "random-cripple", "random-hit",
        ])
        self.assertEqual(contract["hurtSelfDamageRange"], [1, 5])
        self.assertEqual(contract["randomCrippleRollRange"], [0, 3])
        self.assertEqual(contract["randomCrippleFlags"], [
            "crippled-left-leg", "crippled-right-leg",
            "crippled-left-arm", "crippled-right-arm",
        ])

    def test_transaction_is_shared_and_random_hit_fails_closed(self) -> None:
        owner = FUMBLE_OWNER.read_text(encoding="utf-8")
        hurt = owner.index('knownFlags.Contains("hurt-self")')
        lose = owner.index('knownFlags.Contains("lose-turn")')
        cripple = owner.index('knownFlags.Contains("random-cripple")')
        random_hit = owner.index('knownFlags.Contains("random-hit")')
        self.assertLess(hurt, lose)
        self.assertLess(lose, cripple)
        self.assertLess(cripple, random_hit)
        self.assertEqual(owner.count("ClassicRetailRandom.Next("), 2)
        self.assertIn("exact source-selected alternate target", owner)
        self.assertIn("ClassicFumbleFollowUp.HitSelf", owner)
        self.assertIn("ClassicFumbleFollowUp.RandomHit", owner)
        self.assertNotIn("Random.Shared", owner)


if __name__ == "__main__":
    unittest.main()
