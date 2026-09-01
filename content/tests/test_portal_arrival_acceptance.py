from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ACCEPTANCE = ROOT / "runtime" / "src" / "RuntimeCoordinator.Acceptance.cs"
COORDINATOR = ROOT / "runtime" / "src" / "RuntimeCoordinator.cs"
PLAYER = ROOT / "runtime" / "src" / "World" / "Cells" / "CellPlayer.cs"


def method_body(source: str, signature: str) -> str:
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[brace : index + 1]
    raise AssertionError(f"Unterminated method: {signature}")


class PortalArrivalAcceptanceTest(unittest.TestCase):
    def test_linked_floor_probe_uses_active_destination_collision(self) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        passage = method_body(
            source, "private async Task<PortalTraversalProof> ProvePortalPassage("
        )
        arrival = method_body(
            source, "private async Task<PortalArrivalFloor> ProvePortalArrivalFloor("
        )

        self.assertIn("ProvePortalArrivalFloor", passage)
        self.assertIn("reverse ? portal.FromCellFormId : portal.ToCellFormId", arrival)
        self.assertIn("ActiveSet.Activate(targetCellFormId)", arrival)
        self.assertIn("reverse\n            ? portal.FromCollisionLayer", arrival)
        self.assertNotIn("loaded.Player.CollisionMask", arrival)
        self.assertIn("targetRoot.IsAncestorOf", arrival)
        self.assertIn("ActiveSet.Activate(portal.FromCellFormId)", arrival)
        self.assertIn("reverse: false", passage)
        self.assertIn("reverse: true", passage)
        self.assertIn('"xtel-activation"', passage)
        self.assertIn("portal is null", passage)
        self.assertIn("capsuleWalkForward", passage)
        self.assertIn(": null", passage)
        report = COORDINATOR.read_text(encoding="utf-8")
        self.assertIn("traversalMode = portal.TraversalMode", report)
        self.assertIn(
            "floorOwnedCellCollision = portal.FloorOwnedCellCollision", report
        )

    def test_acceptance_reuses_the_production_xtel_arrival_transform(self) -> None:
        acceptance = ACCEPTANCE.read_text(encoding="utf-8")
        player = PLAYER.read_text(encoding="utf-8")
        arrival = method_body(
            acceptance,
            "private async Task<PortalArrivalFloor> ProvePortalArrivalFloor(",
        )
        apply_arrival = method_body(player, "internal void ApplyPortalArrival(")

        transform_owner = "ResolvePortalArrivalFloorPosition"
        self.assertIn(transform_owner, arrival)
        self.assertIn(transform_owner, apply_arrival)


if __name__ == "__main__":
    unittest.main()
