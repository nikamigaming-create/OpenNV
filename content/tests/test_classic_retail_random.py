from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ClassicRetailRandomTest(unittest.TestCase):
    def test_fo2_exact_build_contract_matches_owned_binary_constants(self) -> None:
        contract = json.loads(
            (
                ROOT
                / "runtime/config/classic-retail-random-fo2-1.02-v1.json"
            ).read_text(encoding="utf-8")
        )
        self.assertEqual(contract["schema"], "opennv-classic-retail-random/v1")
        self.assertEqual(contract["exactBuild"], "fallout2-retail-1.02")
        self.assertEqual(contract["modulus"], 2147483647)
        self.assertEqual(contract["multiplier"], 16807)
        self.assertEqual(contract["quotient"], 127773)
        self.assertEqual(contract["remainder"], 2836)
        self.assertEqual(contract["shuffleSlots"], 32)
        self.assertEqual(contract["warmupSteps"], 40)
        self.assertEqual(contract["shuffleIndexMask"], 31)
        self.assertEqual(contract["savePolicy"], "reset-from-new-seed-on-load")
        seed = contract["externalSeed"]
        self.assertEqual(seed["source"], "winmm-timeGetTime-u32")
        self.assertEqual(seed["mixerMultiplier"], 214013)
        self.assertEqual(seed["mixerIncrement"], 2531011)
        self.assertEqual(seed["outputShift"], 16)
        self.assertEqual(seed["outputMask"], 32767)
        self.assertEqual(seed["wordsPerSeed"], 2)
        self.assertEqual(seed["highWordShift"], 16)
        self.assertEqual(seed["newGameSeedUnsigned"], 3203399405)
        self.assertEqual(seed["newGamePolicy"], "explicit-seed-minimum-clamp")
        self.assertEqual(seed["loadPolicy"], "next-two-mixer-words")

    def test_external_seed_mixer_matches_recovered_call_sequence(self) -> None:
        contract = json.loads(
            (ROOT / "runtime/config/classic-retail-random-fo2-1.02-v1.json")
            .read_text(encoding="utf-8")
        )["externalSeed"]
        state = 123456
        words: list[int] = []
        for _ in range(4):
            state = (
                state * contract["mixerMultiplier"] + contract["mixerIncrement"]
            ) & 0xFFFFFFFF
            words.append(
                (state >> contract["outputShift"]) & contract["outputMask"]
            )
        self.assertEqual(words, [9977, 22818, 10150, 16017])
        self.assertEqual((words[0] << contract["highWordShift"]) + words[1], 653875490)
        self.assertEqual((words[2] << contract["highWordShift"]) + words[3], 665206417)

    def test_runtime_uses_contract_state_and_has_no_host_prng(self) -> None:
        runtime = (
            ROOT / "runtime/src/Campaigns/Classic/ClassicRetailRandom.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("ClassicRetailRandomContract", runtime)
        self.assertIn("ClassicRetailRandomState", runtime)
        self.assertIn("contract.Multiplier", runtime)
        self.assertIn("state.Selector & contract.ShuffleIndexMask", runtime)
        self.assertIn("shuffle[slot] = seed", runtime)
        self.assertIn("InitializeFromExactBuildClock", runtime)
        self.assertIn("ResetForLoad", runtime)
        self.assertIn("ResetForNewGame", runtime)
        self.assertIn("TimeGetTime", runtime)
        self.assertIn("NextMixerWord", runtime)
        self.assertIn("unchecked(state * contract.MixerMultiplier", runtime)
        self.assertNotIn("System.Random", runtime)
        self.assertNotIn("Random.Shared", runtime)
        self.assertNotIn("SHA256", runtime)


if __name__ == "__main__":
    unittest.main()
