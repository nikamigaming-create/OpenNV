from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ClassicAdjacentMapCompilerTest(unittest.TestCase):
    def test_fo1_campaign_emits_source_derived_reciprocal_joins(self) -> None:
        compiler = (ROOT / "content/tools/fo1_campaign_transport.py").read_text(
            encoding="utf-8"
        )
        self.assertIn("exit_grid_records(", compiler)
        self.assertIn("reciprocal_map_joins(join_maps)", compiler)
        self.assertIn('"mapJoins": map_joins', compiler)
        self.assertIn('"reciprocalMapJoins": len(map_joins)', compiler)

    def test_fo2_compiler_admits_every_direct_registry_destination(self) -> None:
        compiler = (ROOT / "content/tools/fo2_adjacent_map_compiler.py").read_text(
            encoding="utf-8"
        )
        self.assertIn('resolver.read("data\\\\maps.txt")', compiler)
        self.assertIn('for row in source["exitGrids"]', compiler)
        self.assertIn("_compile_map(", compiler)
        self.assertIn("reciprocal_map_joins([source, *adjacent])", compiler)
        self.assertIn('"missingReciprocalJoin": "fail-closed"', compiler)
        self.assertNotIn("ARBRIDGE", compiler)
        self.assertNotIn("ARGARDEN", compiler)

    def test_runtime_catalog_routes_compiled_pairs_through_shared_owner(self) -> None:
        runtime = (
            ROOT / "runtime/src/Campaigns/Classic/ClassicAdjacentMapCatalog.cs"
        ).read_text(encoding="utf-8")
        self.assertIn('GetProperty("forwardExitGrids")', runtime)
        self.assertIn('GetProperty("reverseExitGrids")', runtime)
        self.assertIn("ClassicMapJoinOwner.ValidateReciprocal(join, reverse[0])", runtime)
        self.assertIn("ClassicMapJoinOwner.Commit(join, mapIndex", runtime)
        self.assertIn("CommitAt(", runtime)
        self.assertNotIn("Fallout1", runtime)
        self.assertNotIn("Fallout2", runtime)


if __name__ == "__main__":
    unittest.main()
