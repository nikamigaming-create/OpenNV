import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
OWNER = ROOT / "runtime/src/Campaigns/Classic/ClassicRetailRandomLifecycle.cs"
HOST = ROOT / "runtime/src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStartHost.cs"
SAVE = ROOT / "runtime/src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStartSave.cs"


class ClassicRetailRandomLifecycleTests(unittest.TestCase):
    def test_one_shared_state_owns_exact_resets_and_event_calls(self) -> None:
        owner = OWNER.read_text(encoding="utf-8")
        self.assertIn("ClassicRetailRandomLifecycleState", owner)
        self.assertIn("ClassicRetailSeedOwner.ResetForNewGame", owner)
        self.assertIn("ClassicRetailSeedOwner.ResetForLoad", owner)
        self.assertIn("ClassicRetailRandom.Next(state.RandomState", owner)
        self.assertIn("string eventId", owner)
        self.assertIn("string ownerId", owner)
        self.assertNotIn("skip", owner.lower())
        self.assertNotIn("Random.Shared", owner)

    def test_fo2_route_resets_then_stops_at_first_unowned_call_owner(self) -> None:
        host = HOST.read_text(encoding="utf-8")
        reset = host.index("ClassicRetailRandomLifecycle.ResetForNewGame")
        boundary = host.index("ClassicRetailRandomLifecycle.RequireSourceCall")
        scene = host.index("Scene = Fo2ArroyoCavesScene.Build", boundary)
        self.assertLess(reset, boundary)
        self.assertLess(boundary, scene)
        self.assertIn('"arroyo-map-load"', host)
        self.assertIn(
            '"exact-build-engine-script-interleaving-before-source-map-enter-random"',
            host,
        )
        self.assertIn("ClassicRetailRandomLifecycle.ResetForLoad", host)

    def test_retail_random_state_is_not_serialized(self) -> None:
        save = SAVE.read_text(encoding="utf-8")
        self.assertNotIn("ClassicRetailRandomState", save)
        self.assertNotIn("retailRandom", save)


if __name__ == "__main__":
    unittest.main()
