from __future__ import annotations

import hashlib
import struct
import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo2_arroyo_caves_slice import _path_sha256, _shortest_path  # noqa: E402


class Fo2ArroyoExitTransitionTest(unittest.TestCase):
    def test_shortest_path_and_identity_follow_source_hex_neighbors(self) -> None:
        walkable = {28707, 28907, 29107, 29307, 29507, 29707, 29907, 30107, 30307,
                    30507, 30707, 30907, 31107, 31307, 31306, 31308}
        path = _shortest_path(28707, 31307, walkable)

        self.assertEqual(
            path,
            [28707, 28907, 29107, 29307, 29507, 29707, 29907, 30107, 30307,
             30507, 30707, 30907, 31107, 31307],
        )
        expected = hashlib.sha256(
            b"".join(struct.pack(">i", tile) for tile in path)
        ).hexdigest()
        self.assertEqual(_path_sha256(path), expected)


if __name__ == "__main__":
    unittest.main()
