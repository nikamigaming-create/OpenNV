import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class MovingStaticRuntimeTests(unittest.TestCase):
    def test_only_single_body_mstt_enters_rigid_body_owner(self):
        source = (ROOT / "runtime" / "src" / "World" / "Cells" /
                  "CellContentLoader.cs").read_text(encoding="utf-8")
        start = source.index('else if (baseRecordType == "MSTT")')
        end = source.index("\n                else\n", start)
        block = source[start:end]
        self.assertIn("if (dynamicBodies.Count == 1)", block)
        self.assertIn("new MovingStaticInstance()", block)
        self.assertIn("MSTT_UNSUPPORTED_PHYSICS_", block)
        self.assertIn("disposition=visual-only-no-collision", block)
        self.assertNotIn("requires one authored dynamic body", block)
        self.assertNotIn("001059af", block)
        self.assertNotIn("0016b87a", block)


if __name__ == "__main__":
    unittest.main()
