import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class FnvLazyLinkedCellsTest(unittest.TestCase):
    def test_normal_campaign_uses_lazy_cells_but_proofs_remain_eager(self):
        coordinator = (ROOT / "runtime" / "src" / "RuntimeCoordinator.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("usesCampaignState &&", coordinator)
        for excluded in (
            'options.ContainsKey("opening-proof")',
            'options.ContainsKey("opening-character-video")',
            'options.ContainsKey("capture-root")',
        ):
            self.assertIn(f"!{excluded}", coordinator)

    def test_saved_active_cell_is_the_only_synchronous_materialization(self):
        route = (
            ROOT
            / "runtime"
            / "src"
            / "World"
            / "Cells"
            / "LazyLinkedCellRoute.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("value.FormId.Equals(session.ActiveCellFormId", route)
        synchronous = route.split("var route = new LazyLinkedCellRoute(", 1)[0]
        self.assertEqual(synchronous.count("CellContentLoader.Load("), 1)
        self.assertIn(
            "_ = route.PrefetchInitialAdjacentAfterFirstFrame();",
            route,
        )
        self.assertIn("SceneTree.SignalName.ProcessFrame", route)

    def test_portal_demand_reuses_source_alignment_and_dynamic_world_owners(self):
        route = (
            ROOT
            / "runtime"
            / "src"
            / "World"
            / "Cells"
            / "LazyLinkedCellRoute.cs"
        ).read_text(encoding="utf-8")
        portal = (
            ROOT
            / "runtime"
            / "src"
            / "World"
            / "Portals"
            / "CellPortalTravel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("_materializeAdjacent?.Invoke(_session.ActiveCellFormId);", portal)
        self.assertIn("PortalAlignmentToleranceMeters", route)
        self.assertIn("PortalNormalAgreementMinimum", route)
        self.assertIn("_activeSet.AddSpace", route)
        self.assertIn("_environmentSet?.AddContent", route)
        self.assertIn("_session.AddWorldContent", route)
        self.assertIn("_actorGrounding.AddSpace", route)
        self.assertIn("_portalTravel!.AddLink", route)


if __name__ == "__main__":
    unittest.main()
