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
        self.assertEqual(
            coordinator.count("await LoadPreparedGameplay("),
            2,
        )
        self.assertIn("return loaded.InitialAdjacentReady;", coordinator)

    def test_prewarm_is_read_only_and_bounded_to_the_prepared_cache_closure(self):
        prewarm = (
            ROOT / "runtime" / "src" / "Content" / "PreparedGameplayPrewarm.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("Task.Run(() => ReadDependencyClosure", prewarm)
        self.assertIn("IsWithinCache(candidate, cacheRoot)", prewarm)
        self.assertIn("File.OpenRead(resolved)", prewarm)
        self.assertIn('property.NameEquals("linkedCells")', prewarm)
        self.assertIn('property.NameEquals("uri")', prewarm)
        self.assertIn("pending.Push(required)", prewarm)
        self.assertIn('value.StartsWith("data:"', prewarm)
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
        self.assertEqual(opening.count("RestoreOwnedFailureCover();"), 2)
        self.assertIn("_transitionCover.Visible = true;", opening)
        self.assertIn(
            "_viewport.MoveChild(_transitionCover, _viewport.GetChildCount() - 1);",
            opening,
        )
        self.assertLess(
            opening.index("_transitionCover.Visible = true;"),
            intro_wait,
        )
        self.assertIn("_video.Visible = false;", opening)
        self.assertIn("_canvas.Visible = true;", opening)

    def test_intro_release_waits_for_configured_opening_camera_owner(self):
        coordinator = (ROOT / "runtime" / "src" / "RuntimeCoordinator.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("Task openingReady = loaded.InitialAdjacentReady;", coordinator)
        self.assertIn(
            "openingReady = ConfigureOpeningAfterInitialAdjacent(",
            coordinator,
        )
        self.assertIn(
            "return usesCampaignState ? openingReady : loaded.InitialAdjacentReady;",
            coordinator,
        )
        self.assertIn("openingFlow.Configure(", coordinator)
        self.assertIn("openingFlow.ProcessMode = ProcessModeEnum.Inherit;", coordinator)

        runtime = (
            ROOT
            / "runtime"
            / "src"
            / "Campaigns"
            / "NewVegas"
            / "Opening"
            / "OpeningQuestRuntime.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("OPENNV_NEW_GAME_CAMERA_OWNER", runtime)
        self.assertIn("cameraLocal={_loaded.Player.Camera.Transform.Origin}", runtime)
        self.assertIn("cameraGlobal={_loaded.Player.Camera.GlobalPosition}", runtime)


if __name__ == "__main__":
    unittest.main()
