import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class FnvGameplayPrewarmTest(unittest.TestCase):
    def test_menu_and_intro_share_one_prepared_dependency_prewarm(self):
        coordinator = (ROOT / "runtime" / "src" / "RuntimeCoordinator.cs").read_text(
            encoding="utf-8"
        )

        self.assertEqual(
            coordinator.count("PreparedGameplayPrewarm.Start(prepared)"),
            1,
        )
        self.assertEqual(coordinator.count("await CompleteGameplayPrewarm("), 1)
        self.assertIn(
            "() => gameplayPrewarm ??= StartGameplayPrewarm(prepared)",
            coordinator,
        )
        self.assertIn(
            'newGameOptions["new-game"] = "";',
            coordinator,
        )
        self.assertIn(
            "LoadPreparedGameplay(prepared, options, useOpeningCampaign: true);",
            coordinator,
        )

    def test_prewarm_is_read_only_and_bounded_to_the_prepared_cache_closure(self):
        prewarm = (
            ROOT / "runtime" / "src" / "Content" / "PreparedGameplayPrewarm.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("Task.Run(() => ReadDependencyClosure", prewarm)
        self.assertIn("IsWithinCache(candidate, cacheRoot)", prewarm)
        self.assertIn("File.OpenRead(resolved)", prewarm)
        self.assertIn('property.NameEquals("linkedCells")', prewarm)
        self.assertNotIn("File.Write", prewarm)
        self.assertNotIn("Directory.Create", prewarm)

    def test_opening_waits_for_prewarm_before_releasing_its_surface(self):
        opening = (
            ROOT
            / "runtime"
            / "src"
            / "Campaigns"
            / "NewVegas"
            / "Opening"
            / "RetailOpening.cs"
        ).read_text(encoding="utf-8")

        continue_wait = opening.index("await _menuActionRequested(action);")
        intro_wait = opening.index("await _introFinished();")
        self.assertGreater(opening.index("QueueFree();", continue_wait), continue_wait)
        self.assertGreater(opening.index("QueueFree();", intro_wait), intro_wait)
        self.assertIn("if (_introCompleted || _transitionStarted)", opening)


if __name__ == "__main__":
    unittest.main()
