from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class FnvActorGroundingSupportTest(unittest.TestCase):
    def test_grounding_skips_dynamic_and_non_walkable_ray_hits(self) -> None:
        source = (
            ROOT / "runtime/src/Diagnostics/Capture/GalleryGroundContact.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("collider is StaticBody3D", source)
        self.assertIn("normal.Y >= walkableSurfaceNormalYMinimum", source)
        self.assertIn("excluded.Add(rejected.GetRid())", source)
        self.assertNotIn("collider is PhysicsBody3D", source)


if __name__ == "__main__":
    unittest.main()
