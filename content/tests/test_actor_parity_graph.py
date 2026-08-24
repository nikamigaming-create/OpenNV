from __future__ import annotations

import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_parity_graph import resolve_template_variants  # noqa: E402


class ActorParityGraphTest(unittest.TestCase):
    def test_npc_traits_require_retail_proven_use_template_actor_flag(self) -> None:
        root_key = "FalloutNV.esm:000100"
        template_key = "FalloutNV.esm:000101"
        bases = {
            root_key: {
                "formKey": root_key,
                "recordType": "NPC_",
                "actorFlags": 0,
                "templateFlags": 0x0041,
                "template": {"key": template_key},
            },
            template_key: {
                "formKey": template_key,
                "recordType": "NPC_",
                "actorFlags": 0,
                "templateFlags": 0,
                "template": None,
            },
        }

        gaps = resolve_template_variants(bases, {})

        self.assertEqual(gaps, [])
        sources = bases[root_key]["appearanceVariants"][0]["categorySources"]
        self.assertEqual(sources["traits"], root_key)
        self.assertEqual(sources["model"], template_key)

        bases[root_key]["actorFlags"] = 0x00000100
        gaps = resolve_template_variants(bases, {})
        self.assertEqual(gaps, [])
        sources = bases[root_key]["appearanceVariants"][0]["categorySources"]
        self.assertEqual(sources["traits"], template_key)


if __name__ == "__main__":
    unittest.main()
