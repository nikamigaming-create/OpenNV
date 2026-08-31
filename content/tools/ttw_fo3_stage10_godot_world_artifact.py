"""Materialize the TTW Vault 101 stage-10 static Godot world artifact.

The input contract contains exact effective TTW resource identities and authored
non-actor reference transforms.  This producer converts only those owned NIFs
to deterministic glTF/collision artifacts.  It deliberately emits no camera
and keeps every actor identity hidden and unplaced until the live TTW stage-10
observation contract supplies the seven required fields.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import tempfile
from pathlib import Path
from typing import Callable

from cell_catalog import INITIALLY_DISABLED_RECORD_FLAG
from cell_scene import godot_position, godot_rotation_quaternion
from export_static_nif_gltf import (
    NoStaticPresentationGeometryError,
    export_static_nif,
)
from havok_collision_gltf import HAVOK_TO_GAME_UNITS
from plugin_stack import file_sha256
from runtime_configuration import RuntimeConfiguration, load_runtime_configuration
from ttw_effective_source import ResolvedTtwMember, load_ttw_effective_source
from ttw_fo3_stage10_resource_closure import ADMITTED_RECORD_TYPES, LIVE_ONLY_FIELDS
from ttw_fo3_stage10_collision import (
    SCHEMA as STATIC_COLLISION_SCHEMA,
    compile_ttw_stage10_collision,
)
from ttw_fo3_stage10_runtime_world_input import (
    LIVE_OBSERVATION_BLOCKER,
    SCHEMA as WORLD_INPUT_SCHEMA,
    STATUS as WORLD_INPUT_STATUS,
)
from ttw_fo3_stage10_world_materialization import TARGET_STAGE


SCHEMA = "opennv-ttw-fo3-cg00-stage10-godot-world-artifact/v1"
STATUS = "source-owned-static-godot-artifacts-live-stage10-observation-required"
REPORT_SCHEMA = "opennv-ttw-fo3-cg00-stage10-godot-world-artifact-report/v1"
SOURCE_AUTHORITY = "owned-ttw-effective-members-no-standalone-substitution"
STATIC_ROOT_DISPOSITION = "identity-root-direct-cell-source-coordinates"
STATIC_TRANSFORM_DISPOSITION = (
    "effective-reference-authored-transform-gamebryo-to-godot-global-axis-conversion"
)
STATIC_EXPORT_POLICY = (
    "broad-owned-nif-geometry-transport-record-source-controllers-without-executing-them"
)
ACTOR_DISPOSITION = "identity-declared-hidden-unplaced-live-stage10-state-required"
CAMERA_DISPOSITION = "not-emitted-live-stage10-camera-contract-required"
PHANTOM_DISPOSITION = (
    "exact-owned-area3d-shape-inactive-until-runtime-filter-mapping"
)
INLINE_VOLUME_DISPOSITION = "source-metadata-only-not-physics-collision"
MATERIAL_BLOCKER = "source-material-texture-artifacts-not-materialized-in-this-slice"
PHANTOM_FILTER_BLOCKER = "gamebryo-trigger-filter-to-godot-layer-map-not-admitted"
ADAPTER_BLOCKER = "ttw-stage10-live-camera-and-participant-state-absent"
DYNAMICS_PARITY_BLOCKER = (
    "source-havok-dynamics-and-constraints-to-godot-parity-not-proven;"
    "static-collision-shapes-only"
)
OUTPUT_MANIFEST_NAME = "ttw-fo3-stage10-godot-world-artifact.json"
OUTPUT_REPORT_NAME = "ttw-fo3-stage10-godot-world-artifact-report.json"
SOURCE_ORIGIN_GAME_UNITS = (0.0, 0.0, 0.0)
VECTOR_COMPONENTS = 3
QUATERNION_COMPONENTS = 4
AFFINE_VALUE_COUNT = VECTOR_COMPONENTS * (VECTOR_COMPONENTS + 1)
AFFINE_BASIS_VALUE_COUNT = VECTOR_COMPONENTS * VECTOR_COMPONENTS


MemberResolver = Callable[[str], ResolvedTtwMember]
StaticExporter = Callable[..., dict[str, object]]
StaticCollisionCompiler = Callable[[bytes], dict[str, object]]


def _canonical_sha256(value: object) -> str:
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()


def _file_descriptor(path: Path, root: Path) -> dict[str, object]:
    resolved = path.resolve()
    relative = resolved.relative_to(root.resolve()).as_posix()
    return {
        "file": relative,
        "bytes": resolved.stat().st_size,
        "sha256": file_sha256(resolved),
    }


def _finite_vector(value: object, label: str, count: int) -> list[float]:
    if not isinstance(value, list) or len(value) != count:
        raise ValueError(f"TTW stage-10 Godot artifact {label} length differs")
    result = [float(component) for component in value]
    if not all(math.isfinite(component) for component in result):
        raise ValueError(f"TTW stage-10 Godot artifact {label} is not finite")
    return result


def _validate_world_input(
    world_input: dict[str, object],
    world_input_path: Path,
) -> None:
    if (
        world_input.get("schema") != WORLD_INPUT_SCHEMA
        or world_input.get("status") != WORLD_INPUT_STATUS
        or world_input.get("campaign") != "Fallout3"
        or world_input.get("edition") != "TTW"
        or world_input.get("stage")
        != {"questEditorId": "CG00", "stage": TARGET_STAGE}
        or world_input.get("runtimeWorldInputReady") is not True
        or world_input.get("runtimeNodeDescriptorsEmitted") is not True
        or world_input.get("runtimeArtifactsMaterialized") is not False
        or world_input.get("adapterSceneIdentityReady") is not False
        or world_input.get("ownedPayloadsEmitted") is not False
        or world_input.get("standaloneArtifactsAccepted") is not False
        or world_input.get("runtimeReady") is not False
    ):
        raise ValueError("TTW stage-10 runtime world-input gate differs")
    identity = world_input.get("identity")
    resources = world_input.get("resources")
    nodes = world_input.get("nodes")
    live_gate = world_input.get("liveObservationGate")
    if not all(isinstance(value, dict) for value in (identity, resources, nodes, live_gate)):
        raise ValueError("TTW stage-10 runtime world-input shape differs")
    if (
        nodes.get("cellRoot", {}).get("transformDisposition")
        != "identity-root-no-scene-specific-rebase"
        or live_gate.get("requiredFields") != list(LIVE_ONLY_FIELDS)
        or live_gate.get("resolvedFields") != []
        or live_gate.get("unresolvedFields") != list(LIVE_ONLY_FIELDS)
        or live_gate.get("allFieldsResolved") is not False
        or live_gate.get("standaloneFallout3ContractAccepted") is not False
        or live_gate.get("standaloneNewVegasContractAccepted") is not False
        or not world_input_path.is_file()
    ):
        raise ValueError("TTW stage-10 runtime world-input live/isolation gate differs")


def _validate_member(
    expected: dict[str, object],
    resolved: ResolvedTtwMember,
) -> None:
    actual = resolved.contract()
    if actual != expected:
        raise ValueError(
            "TTW stage-10 effective member changed: "
            f"{expected.get('logicalPath')}"
        )


def _asset_id(logical_path: str, source_sha256: str, length: int) -> str:
    return hashlib.sha256(
        f"{logical_path.casefold()}\0{source_sha256}".encode("utf-8")
    ).hexdigest()[:length]


def _matrix3_multiply(
    left: list[list[float]],
    right: list[list[float]],
) -> list[list[float]]:
    return [
        [
            sum(left[row][axis] * right[axis][column] for axis in range(3))
            for column in range(3)
        ]
        for row in range(3)
    ]


def _matrix3_vector(
    matrix: list[list[float]],
    value: list[float],
) -> list[float]:
    return [
        sum(matrix[row][axis] * value[axis] for axis in range(3))
        for row in range(3)
    ]


def _affine(value: object, label: str) -> tuple[list[list[float]], list[float]]:
    raw = _finite_vector(value, label, AFFINE_VALUE_COUNT)
    # The decoder publishes the NIF/Havok 4x4 as three column-major basis
    # columns followed by its translation column.
    basis = [
        [
            raw[column * VECTOR_COMPONENTS + row]
            for column in range(VECTOR_COMPONENTS)
        ]
        for row in range(VECTOR_COMPONENTS)
    ]
    return basis, raw[AFFINE_BASIS_VALUE_COUNT:]


def _compose_affine(
    parent: tuple[list[list[float]], list[float]],
    child: tuple[list[list[float]], list[float]],
) -> tuple[list[list[float]], list[float]]:
    basis = _matrix3_multiply(parent[0], child[0])
    rotated_translation = _matrix3_vector(parent[0], child[1])
    translation = [
        rotated_translation[index] + parent[1][index]
        for index in range(VECTOR_COMPONENTS)
    ]
    return basis, translation


def _gamebryo_basis_to_godot(source: list[list[float]]) -> list[list[float]]:
    conversion = [[1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, -1.0, 0.0]]
    inverse = [
        [conversion[column][row] for column in range(3)]
        for row in range(3)
    ]
    return _matrix3_multiply(conversion, _matrix3_multiply(source, inverse))


def _phantom_contract(node: dict[str, object]) -> dict[str, object]:
    collision = node.get("collision")
    if not isinstance(collision, dict) or collision.get("semantics") != (
        "retain-non-blocking-overlap-trigger"
    ):
        raise ValueError("TTW stage-10 phantom collision semantics differ")
    shape = collision.get("shape")
    source_filter = collision.get("filter")
    broad_phase = collision.get("broadPhase")
    if (
        collision.get("coordinateSpace")
        != "source-nif-havok-space-no-runtime-conversion"
        or not isinstance(shape, dict)
        or shape.get("type") != "box-half-extents"
        or not isinstance(source_filter, dict)
        or source_filter.get("layerName") != "FOL_TRIGGER"
        or not isinstance(broad_phase, dict)
        or broad_phase.get("typeName") != "BROAD_PHASE_PHANTOM"
    ):
        raise ValueError("TTW stage-10 phantom source contract differs")
    half_extents = _finite_vector(
        shape.get("halfExtents"),
        "phantom half extents",
        VECTOR_COMPONENTS,
    )
    if not all(component > 0.0 for component in half_extents):
        raise ValueError("TTW stage-10 phantom half extents are invalid")
    combined = _compose_affine(
        _affine(
            collision.get("phantomAffineMatrixColumnMajor"),
            "phantom affine matrix",
        ),
        _affine(shape.get("affineMatrixColumnMajor"), "phantom shape affine matrix"),
    )
    translation_game_units = [
        component * HAVOK_TO_GAME_UNITS for component in combined[1]
    ]
    size_game_units = [
        half_extents[0] * HAVOK_TO_GAME_UNITS * 2.0,
        half_extents[2] * HAVOK_TO_GAME_UNITS * 2.0,
        half_extents[1] * HAVOK_TO_GAME_UNITS * 2.0,
    ]
    return {
        "godotNodeType": "Area3D",
        "runtimeDisposition": PHANTOM_DISPOSITION,
        "monitoring": False,
        "monitorable": False,
        "godotCollisionLayer": 0,
        "godotCollisionMask": 0,
        "sourceFilter": copy.deepcopy(source_filter),
        "sourceBroadPhase": copy.deepcopy(broad_phase),
        "shape": {
            "godotShapeType": "BoxShape3D",
            "sizeGodotGameUnits": size_game_units,
            "localBasisRowsGodot": _gamebryo_basis_to_godot(combined[0]),
            "localPositionGodotGameUnits": godot_position(
                tuple(translation_game_units),
                SOURCE_ORIGIN_GAME_UNITS,
            ),
            "havokToGameUnits": HAVOK_TO_GAME_UNITS,
            "sourceHalfExtentsHavokUnits": half_extents,
            "sourcePhantomAffineMatrixColumnMajor": copy.deepcopy(
                collision["phantomAffineMatrixColumnMajor"]
            ),
            "sourceShapeAffineMatrixColumnMajor": copy.deepcopy(
                shape["affineMatrixColumnMajor"]
            ),
        },
    }


def _placement(
    node: dict[str, object],
    artifact_resource_id: str | None,
) -> dict[str, object]:
    transform = node.get("authoredTransform")
    reference = node.get("reference")
    if not isinstance(transform, dict) or not isinstance(reference, dict):
        raise ValueError("TTW stage-10 static node transform/identity is absent")
    source_position = _finite_vector(
        transform.get("positionGameUnits"),
        "reference position",
        VECTOR_COMPONENTS,
    )
    source_rotation = _finite_vector(
        transform.get("rotationRadians"),
        "reference rotation",
        VECTOR_COMPONENTS,
    )
    scale = float(transform.get("scale", math.nan))
    if not math.isfinite(scale) or scale <= 0.0:
        raise ValueError("TTW stage-10 static node scale is invalid")
    flags = int(reference.get("winner", {}).get("flags", -1))
    if flags < 0:
        raise ValueError("TTW stage-10 static node flags are absent")
    initially_disabled = bool(flags & INITIALLY_DISABLED_RECORD_FLAG)
    collision_input = bool(node.get("collisionInput", False))
    return {
        "reference": copy.deepcopy(reference),
        "baseFormKey": node.get("baseFormKey"),
        "artifactResourceId": artifact_resource_id,
        "transformAuthority": STATIC_TRANSFORM_DISPOSITION,
        "positionGodotGameUnits": godot_position(
            tuple(source_position),
            SOURCE_ORIGIN_GAME_UNITS,
        ),
        "rotationGodotQuaternion": godot_rotation_quaternion(
            tuple(source_rotation)
        ),
        "uniformScale": scale,
        "initiallyDisabled": initially_disabled,
        "visible": not initially_disabled,
        "authoredCollisionInput": collision_input,
        "collisionActive": collision_input and not initially_disabled,
        "sourceTransform": copy.deepcopy(transform),
    }


def _sidecar_outputs(
    sidecar: dict[str, object],
    sidecar_path: Path,
    output_root: Path,
) -> dict[str, dict[str, object]]:
    outputs = sidecar.get("outputs")
    if not isinstance(outputs, dict):
        raise ValueError("TTW stage-10 static glTF sidecar has no outputs")
    result: dict[str, dict[str, object]] = {}
    for name, descriptor in outputs.items():
        if not isinstance(descriptor, dict) or not isinstance(descriptor.get("file"), str):
            raise ValueError("TTW stage-10 static glTF output descriptor differs")
        path = sidecar_path.parent / str(descriptor["file"])
        actual = _file_descriptor(path, output_root)
        if (
            actual["bytes"] != descriptor.get("bytes")
            or actual["sha256"] != descriptor.get("sha256")
        ):
            raise ValueError("TTW stage-10 static glTF output identity differs")
        result[str(name)] = actual
    result["sidecar"] = _file_descriptor(sidecar_path, output_root)
    return result


def materialize_ttw_fo3_stage10_godot_world(
    world_input: dict[str, object],
    *,
    world_input_path: Path,
    output_root: Path,
    member_resolver: MemberResolver,
    configuration: RuntimeConfiguration,
    static_exporter: StaticExporter = export_static_nif,
    static_collision_compiler: StaticCollisionCompiler = compile_ttw_stage10_collision,
) -> tuple[dict[str, object], dict[str, object]]:
    """Convert the strict world input to a source-owned Godot artifact/report."""

    _validate_world_input(world_input, world_input_path)
    if output_root.exists():
        raise FileExistsError(
            f"Refusing to overwrite TTW stage-10 Godot artifact: {output_root}"
        )
    resources = world_input["resources"]
    models = resources.get("models")
    nodes = world_input["nodes"]
    shell_nodes = nodes.get("cellShell")
    phantom_nodes = nodes.get("phantoms")
    inline_nodes = nodes.get("inlineVolumes")
    actor_nodes = nodes.get("actors")
    if (
        not isinstance(models, dict)
        or not isinstance(shell_nodes, list)
        or not isinstance(phantom_nodes, list)
        or not isinstance(inline_nodes, list)
        or not isinstance(actor_nodes, dict)
    ):
        raise ValueError("TTW stage-10 world-input node/resource sets differ")

    shell_resource_ids = sorted(
        {
            str(row.get("resourceId"))
            for row in shell_nodes
            if isinstance(row, dict)
        }
    )
    phantom_resource_ids = sorted(
        {
            str(row.get("resourceId"))
            for row in phantom_nodes
            if isinstance(row, dict)
        }
    )
    used_resource_ids = sorted(set(shell_resource_ids) | set(phantom_resource_ids))
    if any(resource_id not in models for resource_id in used_resource_ids):
        raise ValueError("TTW stage-10 node references an unknown model resource")

    grouped: dict[tuple[str, str], list[str]] = {}
    for resource_id in shell_resource_ids:
        resource = models[resource_id]
        member = resource.get("member") if isinstance(resource, dict) else None
        if not isinstance(member, dict):
            raise ValueError("TTW stage-10 model member identity is absent")
        key = (str(member.get("logicalPath", "")).casefold(), str(member.get("sha256", "")))
        if not key[0] or len(key[1]) != hashlib.sha256().digest_size * 2:
            raise ValueError("TTW stage-10 model member identity is malformed")
        grouped.setdefault(key, []).append(resource_id)

    output_root.mkdir(parents=True)
    assets_root = output_root / "assets"
    assets_root.mkdir()
    compiled_assets: dict[str, dict[str, object]] = {}
    model_to_artifact: dict[str, str] = {}
    excluded_nonpresentation: list[dict[str, object]] = []
    blockers: list[dict[str, object]] = []
    with tempfile.TemporaryDirectory(prefix="opennv-ttw-stage10-") as temporary:
        source_root = Path(temporary)
        for (logical_path_folded, source_sha256), resource_ids in sorted(grouped.items()):
            resource = models[resource_ids[0]]
            expected_member = resource["member"]
            logical_path = str(expected_member["logicalPath"])
            resolved = member_resolver(logical_path)
            _validate_member(expected_member, resolved)
            asset_id = _asset_id(
                logical_path,
                source_sha256,
                configuration.content_compiler.asset_id_hex_characters,
            )
            asset_root = assets_root / asset_id
            asset_root.mkdir()
            source_path = source_root / asset_id / Path(logical_path.replace("\\", "/")).name
            source_path.parent.mkdir(parents=True)
            source_path.write_bytes(resolved.data)
            gltf_path = asset_root / "model.gltf"
            sidecar_path = asset_root / "model.opennv.json"
            try:
                sidecar = static_exporter(
                    source_path,
                    logical_path,
                    gltf_path,
                    sidecar_path,
                    configuration.content_compiler,
                    strict=False,
                )
            except NoStaticPresentationGeometryError as error:
                if any(bool(models[resource_id].get("collisionInputPresent")) for resource_id in resource_ids):
                    blockers.append(
                        {
                            "kind": "collision-only-model-not-materialized",
                            "logicalPath": logical_path,
                            "sourceSha256": source_sha256,
                            "evidence": error.evidence,
                        }
                    )
                else:
                    excluded_nonpresentation.append(
                        {
                            "logicalPath": logical_path,
                            "sourceSha256": source_sha256,
                            "resourceIds": sorted(resource_ids),
                            "evidence": error.evidence,
                        }
                    )
                asset_root.rmdir()
                continue
            except Exception as error:
                blockers.append(
                    {
                        "kind": "static-model-export-failed",
                        "logicalPath": logical_path,
                        "sourceSha256": source_sha256,
                        "errorType": type(error).__name__,
                        "detail": str(error),
                    }
                )
                if asset_root.exists() and not any(asset_root.iterdir()):
                    asset_root.rmdir()
                continue
            coverage = sidecar.get("coverage")
            if not isinstance(coverage, dict):
                raise ValueError("TTW stage-10 static glTF coverage is absent")
            collision_exported = coverage.get("collisionExported") is True
            controllers = coverage.get("controllers")
            if not isinstance(controllers, list):
                raise ValueError("TTW stage-10 static controller coverage is absent")
            dynamic_bodies = coverage.get("dynamicPhysicsBodies")
            if not isinstance(dynamic_bodies, list):
                raise ValueError("TTW stage-10 dynamic collision coverage is absent")
            dynamic_collision_exact = bool(
                coverage.get("dynamicPhysicsExported") is True
                and dynamic_bodies
                and coverage.get("dynamicPhysicsUnsupportedReasons") == []
            )
            collision_expected = any(
                bool(models[resource_id].get("collisionInputPresent"))
                for resource_id in resource_ids
            )
            collision_publication: dict[str, object]
            supplemental_collision: dict[str, object] | None = None
            supplemental_descriptor: dict[str, object] | None = None
            supplemental_error: Exception | None = None
            if collision_expected:
                try:
                    supplemental_collision = static_collision_compiler(resolved.data)
                except Exception as error:
                    supplemental_error = error
                else:
                    if (
                        supplemental_collision.get("schema") != STATIC_COLLISION_SCHEMA
                        or supplemental_collision.get("sourceSha256") != source_sha256
                        or supplemental_collision.get("collisionReady") is not True
                        or supplemental_collision.get("renderMeshSubstitutionUsed") is not False
                    ):
                        raise ValueError(
                            "TTW stage-10 supplemental collision identity differs"
                        )
                    supplemental_path = asset_root / "model.static-collision.opennv.json"
                    supplemental_path.write_text(
                        json.dumps(supplemental_collision, indent=2, sort_keys=True)
                        + "\n",
                        encoding="utf-8",
                    )
                    supplemental_descriptor = _file_descriptor(
                        supplemental_path,
                        output_root,
                    )
            if collision_exported:
                collision_publication = {
                    "ready": True,
                    "transport": "existing-authored-collision-gltf",
                    "runtimeNodeTypes": ["StaticBody3D"],
                    "bodyCount": len(coverage.get("collisionBodies", [])),
                    "shapeCount": len(coverage.get("collisionBodies", [])),
                    "sourceFiltersPreserved": True,
                    "renderMeshSubstitutionUsed": False,
                    "engineDynamicsParityReady": True,
                    "exactShapeContract": supplemental_descriptor,
                }
            elif dynamic_collision_exact:
                collision_publication = {
                    "ready": True,
                    "transport": "existing-source-dynamic-convex-contract",
                    "runtimeNodeTypes": ["RigidBody3D"],
                    "bodyCount": len(dynamic_bodies),
                    "shapeCount": sum(
                        len(body.get("hulls", []))
                        for body in dynamic_bodies
                        if isinstance(body, dict)
                    ),
                    "sourceFiltersPreserved": True,
                    "renderMeshSubstitutionUsed": False,
                    "engineDynamicsParityReady": False,
                    "exactShapeContract": supplemental_descriptor,
                }
            elif supplemental_collision is not None:
                supplemental_node_types = sorted(
                    {
                        str(body["godotBodyType"])
                        for body in supplemental_collision["bodies"]
                    }
                )
                collision_publication = {
                    "ready": True,
                    "transport": "supplemental-exact-havok-shape-contract",
                    "runtimeNodeTypes": supplemental_node_types,
                    "bodyCount": supplemental_collision["collisionBodyCount"],
                    "shapeCount": supplemental_collision["collisionShapeCount"],
                    "sourceFiltersPreserved": supplemental_collision[
                        "sourceFiltersPreserved"
                    ],
                    "renderMeshSubstitutionUsed": supplemental_collision[
                        "renderMeshSubstitutionUsed"
                    ],
                    "engineDynamicsParityReady": (
                        supplemental_collision["engineDynamicsParityReady"]
                    ),
                    "contract": supplemental_descriptor,
                    "exactShapeContract": supplemental_descriptor,
                }
            else:
                collision_publication = {
                    "ready": not collision_expected,
                    "transport": "none" if not collision_expected else "blocked",
                    "runtimeNodeTypes": [],
                    "bodyCount": 0,
                    "shapeCount": 0,
                    "sourceFiltersPreserved": False,
                    "renderMeshSubstitutionUsed": False,
                    "engineDynamicsParityReady": False,
                    "exactShapeContract": None,
                }
            if collision_expected and collision_publication["ready"] is not True:
                blockers.append(
                    {
                        "kind": "authored-collision-export-differs",
                        "logicalPath": logical_path,
                        "sourceSha256": source_sha256,
                        "collisionInputPresent": collision_expected,
                        "collisionExported": collision_exported,
                        "collisionUnsupportedReason": coverage.get(
                            "collisionUnsupportedReason"
                        ),
                        "supplementalErrorType": (
                            type(supplemental_error).__name__
                            if supplemental_error is not None
                            else None
                        ),
                        "supplementalDetail": (
                            str(supplemental_error)
                            if supplemental_error is not None
                            else None
                        ),
                    }
                )
            outputs = _sidecar_outputs(sidecar, sidecar_path, output_root)
            if supplemental_descriptor is not None:
                outputs["staticCollisionContract"] = supplemental_descriptor
            compiled_assets[asset_id] = {
                "artifactResourceId": asset_id,
                "sourceAuthority": SOURCE_AUTHORITY,
                "logicalPath": logical_path,
                "sourceBytes": len(resolved.data),
                "sourceSha256": resolved.sha256,
                "effectiveMember": copy.deepcopy(expected_member),
                "worldInputResourceIds": sorted(resource_ids),
                "outputs": outputs,
                "coverage": copy.deepcopy(coverage),
                "collisionPublication": collision_publication,
                "sourceControllers": copy.deepcopy(controllers),
                "controllerRuntimeReady": not controllers,
                "materialBindings": copy.deepcopy(sidecar.get("surfaces", [])),
            }
            for resource_id in resource_ids:
                model_to_artifact[resource_id] = asset_id

    excluded_resource_ids = {
        resource_id
        for row in excluded_nonpresentation
        for resource_id in row["resourceIds"]
    }
    failed_resource_ids = (
        set(shell_resource_ids) - set(model_to_artifact) - excluded_resource_ids
    )
    shell_placements = []
    for node in shell_nodes:
        resource_id = str(node["resourceId"])
        artifact_id = model_to_artifact.get(resource_id)
        placement = _placement(node, artifact_id)
        collision_artifact = bool(
            placement["authoredCollisionInput"]
            and artifact_id is not None
            and compiled_assets[artifact_id]["collisionPublication"].get("ready")
        )
        placement["collisionArtifactMaterialized"] = collision_artifact
        placement["collisionActive"] = bool(
            placement["authoredCollisionInput"]
            and collision_artifact
            and not placement["initiallyDisabled"]
        )
        if resource_id in excluded_resource_ids:
            placement.update(
                {
                    "godotNodeType": "Node3D",
                    "presentationDisposition": "source-classified-nonpresentation",
                    "visible": False,
                    "collisionActive": False,
                }
            )
        elif artifact_id is None:
            placement.update(
                {
                    "godotNodeType": None,
                    "presentationDisposition": "blocked-source-artifact",
                    "visible": False,
                    "collisionActive": False,
                }
            )
        else:
            placement.update(
                {
                    "godotNodeType": "Node3D-with-verified-gltf-instance",
                    "presentationDisposition": "source-owned-static-gltf",
                }
            )
        shell_placements.append(placement)

    phantom_placements = []
    for node in phantom_nodes:
        resource_id = str(node["resourceId"])
        resource = models.get(resource_id)
        if not isinstance(resource, dict):
            raise ValueError("TTW stage-10 phantom resource is absent")
        resolved = member_resolver(str(resource["member"]["logicalPath"]))
        _validate_member(resource["member"], resolved)
        if resolved.sha256 != resource["member"]["sha256"]:
            raise ValueError("TTW stage-10 phantom payload identity differs")
        phantom_placements.append(
            {
                **_placement(node, None),
                **_phantom_contract(node),
                "effectiveMember": copy.deepcopy(resource["member"]),
            }
        )

    inline_placements = [
        {
            **_placement(node, None),
            "godotNodeType": "Node3D-metadata-only",
            "visible": False,
            "collisionActive": False,
            "runtimeDisposition": INLINE_VOLUME_DISPOSITION,
            "primitive": copy.deepcopy(node["primitive"]),
        }
        for node in inline_nodes
    ]

    actors = {}
    for role, node in sorted(actor_nodes.items()):
        actors[role] = {
            "godotNodeEmitted": False,
            "resourceId": node.get("resourceId"),
            "reference": copy.deepcopy(node.get("reference")),
            "visible": False,
            "placed": False,
            "transform": None,
            "controllerPhase": None,
            "runtimeDisposition": ACTOR_DISPOSITION,
        }

    artifact_blockers = [
        *blockers,
        *[
            {
                "kind": "source-controller-publication-not-materialized",
                "logicalPath": row["logicalPath"],
                "sourceSha256": row["sourceSha256"],
                "controllers": copy.deepcopy(row["sourceControllers"]),
            }
            for row in compiled_assets.values()
            if row["sourceControllers"]
        ],
        {"kind": "runtime-boundary", "detail": MATERIAL_BLOCKER},
        {"kind": "runtime-boundary", "detail": PHANTOM_FILTER_BLOCKER},
        {"kind": "runtime-boundary", "detail": DYNAMICS_PARITY_BLOCKER},
        {"kind": "runtime-boundary", "detail": ADAPTER_BLOCKER},
        {"kind": "runtime-boundary", "detail": LIVE_OBSERVATION_BLOCKER},
    ]
    identity = world_input["identity"]
    runtime_configuration = {
        **configuration.manifest(),
        "path": str(configuration.path.resolve()),
    }
    artifact = {
        "schema": SCHEMA,
        "status": STATUS,
        "campaign": "Fallout3",
        "edition": "TTW",
        "stage": {"questEditorId": "CG00", "stage": TARGET_STAGE},
        "sourceAuthority": SOURCE_AUTHORITY,
        "identity": {
            "runtimeWorldInput": {
                "path": str(world_input_path.resolve()),
                "bytes": world_input_path.stat().st_size,
                "sha256": file_sha256(world_input_path),
                "schema": WORLD_INPUT_SCHEMA,
            },
            "resourceClosure": copy.deepcopy(identity["resourceClosure"]),
            "projection": copy.deepcopy(identity["projection"]),
            "sourceProfile": copy.deepcopy(identity["sourceProfile"]),
            "sourceNamespace": copy.deepcopy(identity["sourceNamespace"]),
            "pluginStackId": identity["pluginStackId"],
            "saveCompatibilityId": identity["saveCompatibilityId"],
            "expandedRecordClosureSha256": identity[
                "expandedRecordClosureSha256"
            ],
            "expandedMemberClosureSha256": identity[
                "expandedMemberClosureSha256"
            ],
            "runtimeConfiguration": runtime_configuration,
        },
        "coordinates": {
            "source": world_input["coordinates"]["source"],
            "godot": "X-right/Y-up/Z-back, game units below uniformly scaled root",
            "sourceOriginGameUnits": list(SOURCE_ORIGIN_GAME_UNITS),
            "worldUnitsToMeters": configuration.world_units_to_meters,
            "rootDisposition": STATIC_ROOT_DISPOSITION,
            "referenceDisposition": STATIC_TRANSFORM_DISPOSITION,
            "staticExportPolicy": STATIC_EXPORT_POLICY,
            "havokToGameUnits": HAVOK_TO_GAME_UNITS,
            "sceneSpecificOffsetsAccepted": False,
        },
        "assets": {
            "count": len(compiled_assets),
            "models": compiled_assets,
            "sourceClassifiedNonpresentation": excluded_nonpresentation,
            "failedResourceIds": sorted(failed_resource_ids),
        },
        "godotWorld": {
            "cellRoot": {
                "godotNodeType": "Node3D",
                "sourceIdentity": copy.deepcopy(nodes["cellRoot"]["sourceIdentity"]),
                "positionGodotGameUnits": [0.0, 0.0, 0.0],
                "rotationGodotQuaternion": [0.0, 0.0, 0.0, 1.0],
                "uniformScaleMetersPerGameUnit": configuration.world_units_to_meters,
                "disposition": STATIC_ROOT_DISPOSITION,
            },
            "cellShell": shell_placements,
            "phantoms": phantom_placements,
            "inlineVolumes": inline_placements,
            "actors": actors,
            "camera": {
                "godotNodeEmitted": False,
                "transform": None,
                "projection": None,
                "controllerPhase": None,
                "runtimeDisposition": CAMERA_DISPOSITION,
            },
        },
        "coverage": {
            "worldInputModelResourcesUsed": len(used_resource_ids),
            "uniqueEffectiveModelMembers": len(grouped),
            "compiledStaticAssets": len(compiled_assets),
            "sourceClassifiedNonpresentationAssets": len(excluded_nonpresentation),
            "blockedStaticAssets": len(blockers),
            "cellShellNodes": len(shell_placements),
            "cellShellNodesWithArtifacts": sum(
                row["artifactResourceId"] is not None for row in shell_placements
            ),
            "cellShellCollisionInputNodes": sum(
                row["authoredCollisionInput"] for row in shell_placements
            ),
            "cellShellCollisionArtifactNodes": sum(
                row["collisionArtifactMaterialized"] for row in shell_placements
            ),
            "cellShellCollisionBlockedNodes": sum(
                row["authoredCollisionInput"]
                and not row["collisionArtifactMaterialized"]
                for row in shell_placements
            ),
            "collisionPublicationAssets": sum(
                row["collisionPublication"]["ready"]
                and row["collisionPublication"]["transport"] != "none"
                for row in compiled_assets.values()
            ),
            "exactCollisionShapeContractAssets": sum(
                row["collisionPublication"].get("exactShapeContract") is not None
                or row["collisionPublication"].get("contract") is not None
                for row in compiled_assets.values()
            ),
            "collisionPublicationByTransport": {
                transport: sum(
                    row["collisionPublication"]["transport"] == transport
                    for row in compiled_assets.values()
                )
                for transport in (
                    "existing-authored-collision-gltf",
                    "existing-source-dynamic-convex-contract",
                    "supplemental-exact-havok-shape-contract",
                )
            },
            "initiallyDisabledCellShellNodes": sum(
                row["initiallyDisabled"] for row in shell_placements
            ),
            "phantomNodes": len(phantom_placements),
            "inlineVolumeNodes": len(inline_placements),
            "actorIdentitiesDeclaredHiddenUnplaced": len(actors),
            "cameraNodesEmitted": 0,
        },
        "liveObservationGate": copy.deepcopy(world_input["liveObservationGate"]),
        "runtimeArtifactsMaterialized": not blockers,
        "staticWorldTransportReady": not blockers,
        "materialsReady": False,
        "phantomRuntimeFilterMappingReady": False,
        "actorsPlacedOrVisible": False,
        "cameraEmitted": False,
        "adapterSceneIdentityReady": False,
        "runtimeBlockers": artifact_blockers,
        "ownedPayloadsEmitted": False,
        "standaloneArtifactsAccepted": False,
        "runtimeReady": False,
    }
    manifest_path = output_root / OUTPUT_MANIFEST_NAME
    manifest_path.write_text(
        json.dumps(artifact, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    report = {
        "schema": REPORT_SCHEMA,
        "status": STATUS,
        "artifact": _file_descriptor(manifest_path, output_root),
        "artifactCanonicalSha256": _canonical_sha256(artifact),
        "coverage": copy.deepcopy(artifact["coverage"]),
        "runtimeArtifactsMaterialized": artifact["runtimeArtifactsMaterialized"],
        "staticWorldTransportReady": artifact["staticWorldTransportReady"],
        "actorsPlacedOrVisible": False,
        "cameraEmitted": False,
        "runtimeReady": False,
        "blockers": copy.deepcopy(artifact_blockers),
    }
    report_path = output_root / OUTPUT_REPORT_NAME
    report_path.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return artifact, report


def compile_ttw_fo3_stage10_godot_world_artifact(
    world_input_path: Path,
    output_root: Path,
) -> tuple[dict[str, object], dict[str, object]]:
    resolved_input = world_input_path.resolve()
    world_input = json.loads(resolved_input.read_text(encoding="utf-8"))
    identity = world_input.get("identity")
    if not isinstance(identity, dict):
        raise ValueError("TTW stage-10 world-input identity is absent")
    profile_path = Path(str(identity.get("sourceProfile", {}).get("file", ""))).resolve()
    namespace_path = Path(
        str(identity.get("sourceNamespace", {}).get("file", ""))
    ).resolve()
    source = load_ttw_effective_source(
        profile_path,
        namespace_path,
        ADMITTED_RECORD_TYPES,
    )
    if source.members is None:
        raise ValueError("TTW stage-10 effective member resolver is absent")
    return materialize_ttw_fo3_stage10_godot_world(
        world_input,
        world_input_path=resolved_input,
        output_root=output_root.resolve(),
        member_resolver=source.members.resolve,
        configuration=load_runtime_configuration(),
    )


def _main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--world-input", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    arguments = parser.parse_args()
    compile_ttw_fo3_stage10_godot_world_artifact(
        arguments.world_input,
        arguments.output_root,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(_main())
