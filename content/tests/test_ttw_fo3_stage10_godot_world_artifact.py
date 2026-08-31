from __future__ import annotations

import copy
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from plugin_stack import file_sha256  # noqa: E402
from runtime_configuration import load_runtime_configuration  # noqa: E402
from ttw_effective_source import ResolvedTtwMember  # noqa: E402
from ttw_fo3_stage10_godot_world_artifact import (  # noqa: E402
    OUTPUT_MANIFEST_NAME,
    OUTPUT_REPORT_NAME,
    PHANTOM_FILTER_BLOCKER,
    SCHEMA,
    materialize_ttw_fo3_stage10_godot_world,
)
from ttw_fo3_stage10_collision import SCHEMA as STATIC_COLLISION_SCHEMA  # noqa: E402
from ttw_fo3_stage10_resource_closure import LIVE_ONLY_FIELDS  # noqa: E402
from ttw_fo3_stage10_runtime_world_input import (  # noqa: E402
    SCHEMA as WORLD_INPUT_SCHEMA,
    STATUS as WORLD_INPUT_STATUS,
)


DIGEST = "a" * 64
PLUGIN_STACK_ID = "b" * 64
SAVE_COMPATIBILITY_ID = f"ttw:{PLUGIN_STACK_ID}"


def member(path: str, payload: bytes) -> dict[str, object]:
    source_sha256 = hashlib.sha256(payload).hexdigest()
    return {
        "logicalPath": path,
        "bytes": len(payload),
        "sha256": source_sha256,
        "winner": {
            "kind": "bsa",
            "archive": "Synthetic.bsa",
            "archiveOrderIndex": 0,
            "sourceRootIndex": 1,
            "archiveSha256": DIGEST,
            "memberBytes": len(payload),
            "memberSha256": source_sha256,
        },
        "overriddenVersions": [],
    }


def record(form_key: str, record_type: str, flags: int = 0) -> dict[str, object]:
    return {
        "formKey": form_key,
        "runtimeFormId": "06000001",
        "winner": {
            "plugin": "TaleOfTwoWastelands.esm",
            "loadOrderIndex": 1,
            "sourceRootIndex": 1,
            "pluginSha256": DIGEST,
            "recordSha256": DIGEST,
            "flags": flags,
        },
        "overriddenVersions": [],
        "recordType": record_type,
        "editorId": None,
        "stableLocalFormId": "00000001",
    }


def node(
    form_key: str,
    resource_id: str | None,
    *,
    flags: int = 0,
    collision: bool = False,
) -> dict[str, object]:
    result: dict[str, object] = {
        "reference": record(form_key, "REFR", flags),
        "baseFormKey": "Fallout3.esm:000100",
        "closureJsonPointer": "/cell/references/0",
        "authoredTransform": {
            "authority": "effective-reference-DATA-and-XSCL-authored-not-live",
            "positionGameUnits": [1.0, 2.0, 3.0],
            "rotationRadians": [0.0, 0.0, 0.0],
            "scale": 1.0,
            "dataSha256": DIGEST,
            "xsclSha256": None,
        },
        "liveTransformAuthority": False,
    }
    if resource_id is not None:
        result.update(
            {
                "nodeKind": "owned-nif-cell-reference",
                "resourceId": resource_id,
                "presentationInput": True,
                "collisionInput": collision,
            }
        )
    return result


def fixture(root: Path) -> tuple[dict[str, object], Path, dict[str, bytes]]:
    payloads = {
        "meshes\\vault\\wall.nif": b"wall-owned-source",
        "meshes\\vault\\decor.nif": b"decor-owned-source",
        "meshes\\triggers\\trigplayerwall01.nif": b"trigger-owned-source",
    }
    members = {path: member(path, payload) for path, payload in payloads.items()}
    wall_resource = {
        "id": "model:wall",
        "member": members["meshes\\vault\\wall.nif"],
        "collisionInputPresent": True,
        "collision": {"blockCount": 1},
    }
    decor_resource = {
        "id": "model:decor",
        "member": members["meshes\\vault\\decor.nif"],
        "collisionInputPresent": False,
        "collision": {"blockCount": 0},
    }
    trigger_collision = {
        "source": "embedded-in-model-member",
        "semantics": "retain-non-blocking-overlap-trigger",
        "coordinateSpace": "source-nif-havok-space-no-runtime-conversion",
        "filter": {
            "layer": 12,
            "layerName": "FOL_TRIGGER",
            "flags": 0,
            "group": 0,
        },
        "broadPhase": {"type": 2, "typeName": "BROAD_PHASE_PHANTOM"},
        "phantomAffineMatrixColumnMajor": [
            1.0,
            0.0,
            0.0,
            0.0,
            1.0,
            0.0,
            0.0,
            0.0,
            1.0,
            0.0,
            0.0,
            0.0,
        ],
        "shape": {
            "type": "box-half-extents",
            "halfExtents": [1.0, 2.0, 3.0],
            "affineMatrixColumnMajor": [
                1.0,
                0.0,
                0.0,
                0.0,
                1.0,
                0.0,
                0.0,
                0.0,
                1.0,
                0.0,
                0.0,
                4.0,
            ],
        },
    }
    trigger_resource = {
        "id": "model:trigger",
        "member": members["meshes\\triggers\\trigplayerwall01.nif"],
        "collisionInputPresent": True,
        "collision": trigger_collision,
    }
    wall_node = node("Fallout3.esm:000200", "model:wall", collision=True)
    decor_node = node(
        "Fallout3.esm:000201",
        "model:decor",
        flags=0x00000800,
    )
    phantom_node = node("Fallout3.esm:000202", "model:trigger")
    phantom_node.update(
        {
            "nodeKind": "owned-nif-phantom",
            "collision": trigger_collision,
        }
    )
    inline_node = node("Fallout3.esm:000203", None)
    inline_node.update(
        {
            "nodeKind": "source-inline-volume",
            "primitive": {
                "dimensionsGameUnits": [1.0, 2.0, 3.0],
                "physicsCollisionAuthority": False,
            },
        }
    )
    actors = {
        role: {
            "resourceId": f"actor:{role}",
            "reference": None if role == "player" else record(
                f"Fallout3.esm:10000{index}",
                "ACHR",
            ),
        }
        for index, role in enumerate(("player", "father", "doctor", "mother"))
    }
    world_input = {
        "schema": WORLD_INPUT_SCHEMA,
        "status": WORLD_INPUT_STATUS,
        "campaign": "Fallout3",
        "edition": "TTW",
        "stage": {"questEditorId": "CG00", "stage": 10},
        "identity": {
            "resourceClosure": {"path": "closure.json", "sha256": DIGEST},
            "projection": {"path": "projection.json", "sha256": DIGEST},
            "sourceProfile": {"file": "profile.json", "sha256": DIGEST},
            "sourceNamespace": {"file": "namespace.json", "sha256": DIGEST},
            "pluginStackId": PLUGIN_STACK_ID,
            "saveCompatibilityId": SAVE_COMPATIBILITY_ID,
            "expandedRecordClosureSha256": DIGEST,
            "expandedMemberClosureSha256": DIGEST,
        },
        "coordinates": {
            "source": "Gamebryo X-right/Y-forward/Z-up, radians, game units"
        },
        "resources": {
            "models": {
                "model:wall": wall_resource,
                "model:decor": decor_resource,
                "model:trigger": trigger_resource,
            }
        },
        "nodes": {
            "cellRoot": {
                "sourceIdentity": record("Fallout3.esm:028138", "CELL"),
                "transformDisposition": "identity-root-no-scene-specific-rebase",
            },
            "cellShell": [wall_node, decor_node],
            "phantoms": [phantom_node],
            "inlineVolumes": [inline_node],
            "actors": actors,
        },
        "liveObservationGate": {
            "requiredFields": list(LIVE_ONLY_FIELDS),
            "resolvedFields": [],
            "unresolvedFields": list(LIVE_ONLY_FIELDS),
            "allFieldsResolved": False,
            "standaloneFallout3ContractAccepted": False,
            "standaloneNewVegasContractAccepted": False,
        },
        "runtimeWorldInputReady": True,
        "runtimeNodeDescriptorsEmitted": True,
        "runtimeArtifactsMaterialized": False,
        "adapterSceneIdentityReady": False,
        "ownedPayloadsEmitted": False,
        "standaloneArtifactsAccepted": False,
        "runtimeReady": False,
    }
    path = root / "world-input.json"
    path.write_text(json.dumps(world_input), encoding="utf-8")
    return world_input, path, payloads


def resolved_member(path: str, payload: bytes) -> ResolvedTtwMember:
    expected = member(path, payload)
    return ResolvedTtwMember(
        path,
        payload,
        copy.deepcopy(expected["winner"]),
        (),
    )


def fake_exporter(
    source_path: Path,
    logical_path: str,
    gltf_path: Path,
    sidecar_path: Path,
    _compiler: object,
    *,
    strict: bool,
) -> dict[str, object]:
    if strict:
        raise AssertionError("TTW broad static transport must record, not execute, controllers")
    gltf_path.write_text('{"asset":{"version":"2.0"}}\n', encoding="utf-8")
    binary_path = gltf_path.with_suffix(".bin")
    binary_path.write_bytes(source_path.read_bytes()[:4])
    outputs: dict[str, object] = {
        "gltf": {
            "file": gltf_path.name,
            "bytes": gltf_path.stat().st_size,
            "sha256": file_sha256(gltf_path),
        },
        "buffer": {
            "file": binary_path.name,
            "bytes": binary_path.stat().st_size,
            "sha256": file_sha256(binary_path),
        },
    }
    collision_exported = logical_path.endswith("wall.nif")
    if collision_exported:
        collision_path = gltf_path.with_name("model.collision.gltf")
        collision_path.write_text('{"asset":{"version":"2.0"}}\n', encoding="utf-8")
        collision_binary = collision_path.with_suffix(".bin")
        collision_binary.write_bytes(b"collision")
        outputs.update(
            {
                "collisionGltf": {
                    "file": collision_path.name,
                    "bytes": collision_path.stat().st_size,
                    "sha256": file_sha256(collision_path),
                },
                "collisionBuffer": {
                    "file": collision_binary.name,
                    "bytes": collision_binary.stat().st_size,
                    "sha256": file_sha256(collision_binary),
                },
            }
        )
    sidecar = {
        "outputs": outputs,
        "coverage": {
            "surfaces": 1,
            "collisionExported": collision_exported,
            "collisionUnsupportedReason": None,
            "collisionBodies": ([{"bodyBlock": 1}] if collision_exported else []),
            "dynamicPhysicsBodies": [],
            "dynamicPhysicsExported": False,
            "dynamicPhysicsUnsupportedReasons": [],
            "controllers": [],
        },
        "surfaces": [],
    }
    sidecar_path.write_text(json.dumps(sidecar), encoding="utf-8")
    return sidecar


class TtwFo3Stage10GodotWorldArtifactTest(unittest.TestCase):
    def test_materializes_static_artifacts_and_keeps_live_nodes_absent(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            world_input, world_input_path, payloads = fixture(root)

            def resolver(path: str) -> ResolvedTtwMember:
                return resolved_member(path, payloads[path])

            output_root = root / "artifact"
            artifact, report = materialize_ttw_fo3_stage10_godot_world(
                world_input,
                world_input_path=world_input_path,
                output_root=output_root,
                member_resolver=resolver,
                configuration=load_runtime_configuration(),
                static_exporter=fake_exporter,
            )

            self.assertEqual(artifact["schema"], SCHEMA)
            self.assertTrue(artifact["runtimeArtifactsMaterialized"])
            self.assertTrue(artifact["staticWorldTransportReady"])
            self.assertFalse(artifact["runtimeReady"])
            self.assertFalse(artifact["cameraEmitted"])
            self.assertFalse(artifact["actorsPlacedOrVisible"])
            self.assertEqual(artifact["coverage"]["compiledStaticAssets"], 2)
            self.assertEqual(
                artifact["coverage"]["cellShellCollisionArtifactNodes"],
                1,
            )
            self.assertEqual(
                artifact["coverage"]["cellShellCollisionBlockedNodes"],
                0,
            )
            self.assertEqual(
                artifact["godotWorld"]["cellShell"][0]["positionGodotGameUnits"],
                [1.0, 3.0, -2.0],
            )
            self.assertFalse(artifact["godotWorld"]["cellShell"][1]["visible"])
            self.assertTrue(
                artifact["godotWorld"]["cellShell"][1]["initiallyDisabled"]
            )
            self.assertEqual(
                artifact["godotWorld"]["phantoms"][0]["shape"][
                    "sizeGodotGameUnits"
                ],
                [14.0, 42.0, 28.0],
            )
            self.assertEqual(
                artifact["godotWorld"]["phantoms"][0]["shape"][
                    "localPositionGodotGameUnits"
                ],
                [0.0, 28.0, -0.0],
            )
            self.assertEqual(
                artifact["godotWorld"]["phantoms"][0]["godotCollisionLayer"],
                0,
            )
            self.assertTrue(
                all(
                    not row["visible"] and not row["placed"]
                    for row in artifact["godotWorld"]["actors"].values()
                )
            )
            self.assertTrue(
                any(
                    row["detail"] == PHANTOM_FILTER_BLOCKER
                    for row in artifact["runtimeBlockers"]
                    if row["kind"] == "runtime-boundary"
                )
            )
            self.assertTrue((output_root / OUTPUT_MANIFEST_NAME).is_file())
            self.assertTrue((output_root / OUTPUT_REPORT_NAME).is_file())
            self.assertEqual(
                report["artifact"]["sha256"],
                file_sha256(output_root / OUTPUT_MANIFEST_NAME),
            )

    def test_collision_mismatch_fails_static_transport_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            world_input, world_input_path, payloads = fixture(root)

            def no_collision_exporter(*args: object, **kwargs: object) -> dict[str, object]:
                result = fake_exporter(*args, **kwargs)
                logical_path = str(args[1])
                if logical_path.endswith("wall.nif"):
                    sidecar_path = Path(args[3])
                    for name in ("model.collision.gltf", "model.collision.bin"):
                        path = sidecar_path.parent / name
                        if path.exists():
                            path.unlink()
                    result["outputs"].pop("collisionGltf")
                    result["outputs"].pop("collisionBuffer")
                    result["coverage"]["collisionExported"] = False
                    result["coverage"]["collisionUnsupportedReason"] = "synthetic-gap"
                    sidecar_path.write_text(json.dumps(result), encoding="utf-8")
                return result

            artifact, _report = materialize_ttw_fo3_stage10_godot_world(
                world_input,
                world_input_path=world_input_path,
                output_root=root / "artifact",
                member_resolver=lambda path: resolved_member(path, payloads[path]),
                configuration=load_runtime_configuration(),
                static_exporter=no_collision_exporter,
            )
            self.assertFalse(artifact["runtimeArtifactsMaterialized"])
            self.assertFalse(artifact["staticWorldTransportReady"])
            self.assertFalse(
                artifact["godotWorld"]["cellShell"][0]["collisionActive"]
            )
            self.assertTrue(
                any(
                    row["kind"] == "authored-collision-export-differs"
                    for row in artifact["runtimeBlockers"]
                )
            )

    def test_exact_supplemental_collision_closes_exporter_shape_gap(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            world_input, world_input_path, payloads = fixture(root)

            def no_collision_exporter(*args: object, **kwargs: object) -> dict[str, object]:
                result = fake_exporter(*args, **kwargs)
                if str(args[1]).endswith("wall.nif"):
                    sidecar_path = Path(args[3])
                    for name in ("model.collision.gltf", "model.collision.bin"):
                        path = sidecar_path.parent / name
                        if path.exists():
                            path.unlink()
                    result["outputs"].pop("collisionGltf")
                    result["outputs"].pop("collisionBuffer")
                    result["coverage"]["collisionExported"] = False
                    result["coverage"]["collisionBodies"] = []
                    result["coverage"]["collisionUnsupportedReason"] = (
                        "unsupported-root-shape:bhkBoxShape"
                    )
                    sidecar_path.write_text(json.dumps(result), encoding="utf-8")
                return result

            def collision_compiler(payload: bytes) -> dict[str, object]:
                return {
                    "schema": STATIC_COLLISION_SCHEMA,
                    "sourceSha256": hashlib.sha256(payload).hexdigest(),
                    "collisionReady": True,
                    "renderMeshSubstitutionUsed": False,
                    "sourceFiltersPreserved": True,
                    "collisionBodyCount": 1,
                    "collisionShapeCount": 1,
                    "dynamicBodyCount": 0,
                    "engineDynamicsParityReady": True,
                    "bodies": [{"godotBodyType": "StaticBody3D"}],
                }

            artifact, _report = materialize_ttw_fo3_stage10_godot_world(
                world_input,
                world_input_path=world_input_path,
                output_root=root / "artifact",
                member_resolver=lambda path: resolved_member(path, payloads[path]),
                configuration=load_runtime_configuration(),
                static_exporter=no_collision_exporter,
                static_collision_compiler=collision_compiler,
            )

            placement = artifact["godotWorld"]["cellShell"][0]
            self.assertTrue(placement["collisionArtifactMaterialized"])
            self.assertTrue(placement["collisionActive"])
            asset = artifact["assets"]["models"][placement["artifactResourceId"]]
            self.assertEqual(
                asset["collisionPublication"]["transport"],
                "supplemental-exact-havok-shape-contract",
            )
            self.assertTrue(
                (
                    root
                    / "artifact"
                    / asset["collisionPublication"]["exactShapeContract"]["file"]
                ).is_file()
            )

    def test_refuses_to_overwrite_output_root(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            world_input, world_input_path, payloads = fixture(root)
            output_root = root / "artifact"
            output_root.mkdir()
            with self.assertRaises(FileExistsError):
                materialize_ttw_fo3_stage10_godot_world(
                    world_input,
                    world_input_path=world_input_path,
                    output_root=output_root,
                    member_resolver=lambda path: resolved_member(path, payloads[path]),
                    configuration=load_runtime_configuration(),
                    static_exporter=fake_exporter,
                )


if __name__ == "__main__":
    unittest.main()
