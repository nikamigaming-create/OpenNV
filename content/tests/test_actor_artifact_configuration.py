from __future__ import annotations

import copy
import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from runtime_configuration import (  # noqa: E402
    ACTOR_ARTIFACT_CONFIGURATION_SCHEMA,
    ACTOR_ARTIFACT_CONTENT_COMPILER_FIELDS,
    actor_artifact_configuration_manifest,
    load_runtime_configuration,
)


class ActorArtifactConfigurationTest(unittest.TestCase):
    def setUp(self):
        self.document = copy.deepcopy(load_runtime_configuration().document)

    def identity(self, document: dict[str, object]) -> dict[str, object]:
        return actor_artifact_configuration_manifest(document)

    def test_scope_is_explicit_and_unrelated_pickup_change_is_reusable(self):
        baseline = self.identity(self.document)
        changed = copy.deepcopy(self.document)
        changed["pickup"]["holdDistanceMeters"] += 1.0

        self.assertEqual(baseline, self.identity(changed))
        self.assertEqual(baseline["schema"], ACTOR_ARTIFACT_CONFIGURATION_SCHEMA)
        self.assertEqual(
            baseline["sections"],
            {
                "actorCompiler": "all",
                "contentCompiler": list(ACTOR_ARTIFACT_CONTENT_COMPILER_FIELDS),
            },
        )

    def test_every_compiled_actor_section_changes_identity(self):
        baseline = self.identity(self.document)["sha256"]
        actor_changed = copy.deepcopy(self.document)
        actor_changed["actorCompiler"]["faceGenMaterial"]["toneScale"] += 1.0
        self.assertNotEqual(baseline, self.identity(actor_changed)["sha256"])

        for field in ACTOR_ARTIFACT_CONTENT_COMPILER_FIELDS:
            with self.subTest(field=field):
                changed = copy.deepcopy(self.document)
                value = changed["contentCompiler"][field]
                changed["contentCompiler"][field] = value + 1
                self.assertNotEqual(baseline, self.identity(changed)["sha256"])

    def test_unconsumed_content_compiler_policy_does_not_invalidate_actor(self):
        baseline = self.identity(self.document)
        changed = copy.deepcopy(self.document)
        changed["contentCompiler"]["landscapeQuadrantPixels"] += 1
        self.assertEqual(baseline, self.identity(changed))


if __name__ == "__main__":
    unittest.main()
