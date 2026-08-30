from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from ttw_fo3_source_traversal import (  # noqa: E402
    ARTIFACT_SCHEMA,
    CG00_STAGE,
    CG01_MOVEMENT_STAGE,
    SCHEMA,
    _canonical_sha256,
    _navmesh_route,
    compile_ttw_fo3_source_traversal,
)


PROFILE = Path(
    r"C:\Users\nbrys\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0"
    r"\LocalCache\Local\OpenNV\profiles\ttw-profile.json"
)
NAMESPACE = PROFILE.with_name("ttw-effective-source.json")
PROJECTION = Path(
    r"D:\Builds\OpenNV-fo3-ttw-native-first-beat-20260829-r2"
    r"\ttw-fo3-profile-projection.json"
)
RESOURCE_CLOSURE = Path(
    r"D:\Builds\OpenNV-ttw-fo3-stage10-expanded-resource-closure-20260830-r3-trigger-contract"
    r"\ttw-fo3-stage10-expanded-resource-closure.json"
)
ARTIFACT_ROOT = Path(
    r"D:\Builds\OpenNV-ttw-fo3-stage10-godot-world-artifact-20260830-r7-static-readiness"
)
ARTIFACT = ARTIFACT_ROOT / "ttw-fo3-stage10-godot-world-artifact.json"
STATIC_PROOF = ARTIFACT_ROOT / "ttw-fo3-stage10-static-collision-readiness.json"


class TtwFo3SourceTraversalTests(unittest.TestCase):
    def test_navmesh_route_uses_shared_edge_midpoints(self) -> None:
        navmesh = SimpleNamespace(
            vertices=(
                (0.0, 0.0, 0.0),
                (1.0, 0.0, 0.0),
                (0.0, 1.0, 0.0),
                (1.0, 1.0, 0.0),
            ),
            triangles=(
                SimpleNamespace(vertex_indices=(0, 1, 2), adjacent_triangles=(1, -1, -1)),
                SimpleNamespace(vertex_indices=(1, 3, 2), adjacent_triangles=(0, -1, -1)),
            ),
        )
        route = _navmesh_route(navmesh, (0.1, 0.1, 0.0), (0.9, 0.9, 0.0))
        self.assertEqual(route["trianglePath"], [0, 1])
        self.assertEqual(route["waypoints"][0]["sharedVertexIndices"], [1, 2])
        self.assertEqual(
            route["waypoints"][0]["positionGameUnits"],
            [0.5, 0.5, 0.0],
        )

    @unittest.skipUnless(
        all(path.is_file() for path in (PROFILE, NAMESPACE, PROJECTION, RESOURCE_CLOSURE, ARTIFACT, STATIC_PROOF)),
        "owned TTW corpus/evidence is not installed",
    )
    def test_owned_ttw_cg00_to_cg01_contract_is_isolated_and_fail_closed(self) -> None:
        document = compile_ttw_fo3_source_traversal(
            PROFILE,
            NAMESPACE,
            PROJECTION,
            RESOURCE_CLOSURE,
            ARTIFACT,
            STATIC_PROOF,
        )
        identity = document["identity"]
        cg00 = document["cg00Stage10"]
        cg01 = document["cg01Stage10Traversal"]
        readiness = document["readiness"]
        self.assertEqual(document["schema"], SCHEMA)
        self.assertEqual(identity["staticWorldArtifact"]["schema"], ARTIFACT_SCHEMA)
        self.assertFalse(identity["standaloneFallout3Accepted"])
        self.assertFalse(identity["standaloneNewVegasAccepted"])
        self.assertEqual(cg00["stage"], CG00_STAGE)
        self.assertFalse(cg00["controls"]["movementEnabled"])
        self.assertFalse(cg00["camera1st"]["camera3dProjectionEmitted"])
        self.assertEqual(len(cg00["participants"]), 3)
        self.assertEqual(cg01["stage10"]["commandCount"], 4)
        self.assertEqual(CG01_MOVEMENT_STAGE, 10)
        self.assertEqual(
            cg01["navigation"]["route"]["trianglePath"],
            [78, 13, 12, 11, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
        )
        self.assertEqual(len(cg01["navigation"]["route"]["waypoints"]), 14)
        self.assertTrue(readiness["staticCollisionShellReady"])
        self.assertTrue(readiness["sourceAuthoredCg01Stage10RootTraversalReady"])
        self.assertFalse(readiness["physicalPlayerCollisionReady"])
        self.assertFalse(readiness["runtimeReady"])
        body = dict(document)
        digest = body.pop("contractSha256")
        self.assertEqual(digest, _canonical_sha256(body))
        self.assertEqual(
            json.loads(STATIC_PROOF.read_text(encoding="utf-8"))[
                "headlessStaticWorldCollisionReadinessPassed"
            ],
            True,
        )


if __name__ == "__main__":
    unittest.main()
