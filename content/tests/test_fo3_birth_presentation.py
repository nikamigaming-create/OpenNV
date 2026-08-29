from __future__ import annotations

import hashlib
import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_fo3_birth_presentation import (  # noqa: E402
    _cache_relative_derivative,
    _facegen_values,
)


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

    def test_stage65_facegen_values_are_hash_bound(self) -> None:
        values = [float(index) / 10.0 for index in range(50)]
        sha256 = hashlib.sha256(struct.pack("<50f", *values)).hexdigest()

        actual, actual_sha256 = _facegen_values(
            {"count": len(values), "values": values, "sha256": sha256},
            len(values),
        )

        self.assertEqual(tuple(values), actual)
        self.assertEqual(sha256, actual_sha256)

    def test_stage65_facegen_values_reject_hash_drift(self) -> None:
        values = [0.0] * 30
        with self.assertRaisesRegex(ValueError, "value hash differs"):
            _facegen_values(
                {"count": len(values), "values": values, "sha256": "0" * 64},
                len(values),
            )


if __name__ == "__main__":
    unittest.main()
