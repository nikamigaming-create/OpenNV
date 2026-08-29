from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_fo3_birth_presentation import _cache_relative_derivative  # noqa: E402


class Fo3BirthPresentationTest(unittest.TestCase):
    def test_actor_derivative_is_cache_relative(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            cache_root = Path(temporary)
            actor = cache_root / "generated" / "actors" / "dad" / "actor-scene.json"

            self.assertEqual(
                "generated/actors/dad/actor-scene.json",
                _cache_relative_derivative(cache_root, actor),
            )

    def test_actor_derivative_cannot_escape_cache(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            cache_root = Path(temporary) / "cache"
            outside = Path(temporary) / "actor-scene.json"

            with self.assertRaisesRegex(ValueError, "escapes its local cache"):
                _cache_relative_derivative(cache_root, outside)


if __name__ == "__main__":
    unittest.main()
