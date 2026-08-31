from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ClassicAttackOwnerTest(unittest.TestCase):
    def test_shared_owner_resolves_only_explicit_retail_rolls(self) -> None:
        owner = (
            ROOT / "runtime/src/Campaigns/Classic/ClassicAttackOwner.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("ActionPointCostRequired", owner)
        self.assertIn("RangeRequired", owner)
        self.assertIn("HitRollRequired", owner)
        self.assertIn("source.HitResolution == EngineRollRequired", owner)
        self.assertIn("ResolveObservedRolls", owner)
        self.assertIn("ClassicAttackResolutionContract", owner)
        self.assertIn("ClassicAttackOffense", owner)
        self.assertIn("rolls.HitPercent > hitChance", owner)
        self.assertIn("rolls.Damage + offense.DamageBonus", owner)
        self.assertIn("defense.DamageResistancePercent", owner)
        self.assertIn("CriticalResolutionRequired", owner)
        self.assertIn("FumbleResolutionRequired", owner)
        self.assertIn("intent.Source.HitResolution != EngineResolved", owner)
        self.assertIn("engineResolvedDamage < 0", owner)
        self.assertNotIn("Random(", owner)
        self.assertNotIn("SHA256", owner)

    def test_fo2_compiler_transports_combat_fields_from_owned_pros(self) -> None:
        compiler = (ROOT / "content/tools/fo2_first_slice.py").read_text(
            encoding="utf-8"
        )
        contract = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleConfrontationContract.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("CRITTER_PRO_SKILLS_OFFSET", compiler)
        self.assertIn('"damageThresholds"', compiler)
        self.assertIn('"damageResistances"', compiler)
        self.assertIn('"criticalFailureType"', compiler)
        self.assertIn('"ammunitionCapacity"', compiler)
        self.assertIn("IReadOnlyList<int> DamageThresholds", contract)
        self.assertIn("IReadOnlyList<int> Skills", contract)

    def test_fo2_target_attack_consumes_only_compiler_emitted_weapon_contract(self) -> None:
        runtime = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleConfrontationRuntime.cs"
        ).read_text(encoding="utf-8")
        contract = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleConfrontationContract.cs"
        ).read_text(encoding="utf-8")
        save = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStartSave.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("Fo2TempleEquippedAttack", contract)
        self.assertIn("Critter.EquippedAttack.ActionPointCost", contract)
        self.assertIn("ClassicAttackOwner.Prepare(", runtime)
        self.assertIn("EquippedAttackSource(_contract)", runtime)
        self.assertIn("contract.Critter.EquippedAttack.MinimumDamage", runtime)
        self.assertIn("LastTargetAttack = targetAttack", runtime)
        self.assertNotIn("hit/damage execution is fail-closed", runtime)
        self.assertIn("lastTargetAttack = TempleConfrontation.LastTargetAttack", save)
        self.assertIn("ReadClassicAttackIntent(value)", save)

    def test_fo1_rat_uses_same_owner_and_does_not_apply_placeholder_damage(self) -> None:
        runtime = (
            ROOT / "runtime/src/Campaigns/Fallout1/Fo1TacticalSession.cs"
        ).read_text(encoding="utf-8")
        start = runtime.index("private void RatAttack(Fo1Mob mob)")
        end = runtime.index("private void BuildWorldMarkers()", start)
        attack = runtime[start:end]
        self.assertIn("ClassicAttackOwner.Prepare(", attack)
        self.assertIn("ClassicAttackBoundary.ActionPointCostRequired", attack)
        self.assertNotIn("_playerHitPoints =", attack)
        self.assertNotIn("SpendActionPoint", attack)


if __name__ == "__main__":
    unittest.main()
