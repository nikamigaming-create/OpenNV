from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ClassicScriptEffectsTest(unittest.TestCase):
    def test_shared_executor_is_campaign_neutral_and_fail_closed(self) -> None:
        source = (
            ROOT
            / "runtime/src/Campaigns/Classic/ClassicScriptEffects.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("Unsupported classic script operation", source)
        self.assertIn('"source-is-player"', source)
        self.assertIn('"can-see-player"', source)
        self.assertIn('"local-equals"', source)
        self.assertIn('"set-local"', source)
        self.assertIn('"set-flag"', source)
        self.assertIn('"dialogue-end"', source)
        self.assertIn("DialogueEnded", source)
        self.assertIn('"heal-player-to-maximum"', source)
        self.assertIn("PlayerHealing", source)
        self.assertIn('"clear-player-poison"', source)
        self.assertIn('"advance-game-time-by-player-poison"', source)
        self.assertIn('"clear-player-injuries"', source)
        self.assertIn("PlayerPoisonRemoved", source)
        self.assertIn("GameTimeAdvanceMinutes", source)
        self.assertIn("ClearedPlayerInjuries", source)
        self.assertIn("ClassicPlayerStatusState", source)
        self.assertIn('"elapsed-game-time-greater-than"', source)
        self.assertIn('"destroy-self"', source)
        self.assertIn("DestroySelf", source)
        self.assertIn("internal void Apply(ClassicScriptExecution execution)", source)
        self.assertNotIn("ACKlint", source)
        self.assertNotIn("flare", source.casefold())
        self.assertNotIn("Fallout1", source)
        self.assertNotIn("Fallout2", source)

    def test_both_classic_campaigns_consume_the_shared_executor(self) -> None:
        fo1 = (
            ROOT / "runtime/src/Campaigns/Fallout1/Fo1TacticalSession.Presentation.cs"
        ).read_text(encoding="utf-8")
        fo2 = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleConfrontationRuntime.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("ClassicScriptContext", fo1)
        self.assertIn("flare.Program.Execute", fo1)
        self.assertIn('EffectProgram.Execute(\n                "pickup_proc"', fo2)
        self.assertIn('EffectProgram.Execute(\n                "critter_proc"', fo2)
        self.assertNotIn("TakeDamage", fo2)


if __name__ == "__main__":
    unittest.main()
