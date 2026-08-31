from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ACCEPTANCE = ROOT / "runtime" / "src" / "RuntimeCoordinator.Acceptance.cs"
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
        self.assertIn("ActiveSet.Activate(portal.ToCellFormId)", arrival)
        self.assertIn("portal.ToCollisionLayer", arrival)
        self.assertNotIn("loaded.Player.CollisionMask", arrival)
        self.assertIn("portal.ToRoot.IsAncestorOf", arrival)
        self.assertIn("ActiveSet.Activate(portal.FromCellFormId)", arrival)
        self.assertIn("portal is null", passage)
        self.assertIn("ray.To - ray.From", passage)

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
