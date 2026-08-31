import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
OWNER = ROOT / "runtime/src/Campaigns/Classic/ClassicMapInitialization.cs"
TEMPLE = ROOT / "runtime/src/Campaigns/Fallout2/Temple/Fo2TemplePresentationContract.cs"
CAVES = ROOT / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArroyoCavesPresentationContract.cs"


class ClassicMapInitializationTests(unittest.TestCase):
    def test_shared_owner_preserves_source_order_and_excludes_extent_padding(self) -> None:
        owner = OWNER.read_text(encoding="utf-8")
        self.assertIn('obj.GetProperty("sourceOffset")', owner)
        self.assertIn("Add(inventory.GetProperty(\"object\")", owner)
        self.assertIn("slotIndex < length", owner)
        self.assertIn("liveCount", owner)
        self.assertNotIn("OrderBy(row => row.Serial)", owner)

    def test_both_owned_fo2_maps_bind_the_shared_initialization_owner(self) -> None:
        for path in (TEMPLE, CAVES):
            source = path.read_text(encoding="utf-8")
            self.assertIn("ClassicMapInitializationOwner.Parse(map)", source)
            self.assertIn("ClassicMapInitialization Initialization", source)


if __name__ == "__main__":
    unittest.main()
