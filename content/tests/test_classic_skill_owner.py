import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "runtime" / "config" / "classic-skill-fo2-1.02-v1.json"
RUNTIME = ROOT / "runtime" / "src" / "Campaigns" / "Classic" / "ClassicSkillOwner.cs"


class ClassicSkillOwnerTests(unittest.TestCase):
    def test_fo2_exact_build_contract_carries_all_skill_rules(self) -> None:
        contract = json.loads(CONTRACT.read_text(encoding="utf-8"))
        self.assertEqual(contract["schema"], "opennv-classic-skill/v1")
        self.assertEqual(contract["exactBuild"], "fallout2-retail-1.02")
        self.assertEqual(len(contract["skills"]), 18)
        self.assertEqual(
            [row["id"] for row in contract["skills"]],
            [
                "Small Guns", "Big Guns", "Energy Weapons", "Unarmed",
                "Melee Weapons", "Throwing", "First Aid", "Doctor", "Sneak",
                "Lockpick", "Steal", "Traps", "Science", "Repair", "Speech",
                "Barter", "Gambling", "Outdoorsman",
            ],
        )
        self.assertTrue(all(
            not row["difficultyAdjusted"] for row in contract["skills"][:6]
        ))
        self.assertTrue(all(
            row["difficultyAdjusted"] for row in contract["skills"][6:]
        ))
        self.assertEqual(
            contract["difficultyAdjustments"],
            {"easy": 20, "normal": 0, "hard": -10},
        )

    def test_runtime_requires_explicit_source_and_modifier_inputs(self) -> None:
        runtime = RUNTIME.read_text(encoding="utf-8")
        self.assertIn("int SourceBonus", runtime)
        self.assertIn("int? TraitAdjustment", runtime)
        self.assertIn("int? PerkAdjustment", runtime)
        self.assertIn("ClassicSkillDifficulty Difficulty", runtime)
        self.assertIn("rule.DifficultyAdjusted", runtime)
        profile = (
            ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" /
            "CharacterStart" / "Fo2CharacterStartContract.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("SkillBonuses[skillIndex]", profile)
        self.assertIn("ClassicSkillDifficulty difficulty", profile)
        self.assertNotIn("Random.Shared", runtime)


if __name__ == "__main__":
    unittest.main()
