#!/usr/bin/env python3
"""Derive exact TTW CG00 stage-10 per-surface camera-depth evidence.

The private observation owns the rendered NiAVObject transforms and vertex
payloads.  This producer does not choose a clipping allowance.  It recomputes
every observed surface vertex in the exact observed camera space and records
the complete sorted depth distribution, including the result of the observed
near-plane comparison.
"""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
import os
from pathlib import Path


PRESENTATION_SCHEMA = "opennv.fo3-ttw-oracle-cg00-stage10-presentation/v1"
RAW_SCHEMA = "nikami-retail-oracle/v4"
OUTPUT_SCHEMA = "opennv-ttw-fo3-cg00-stage10-retail-surface-depth/v1"
OUTPUT_STATUS = "exact-live-retail-surface-depth-distribution-derived"
ACTOR_GEOMETRY_EVENT = "actor-geometry"
ACTOR_FRAME_EVENT = "actor-frame"
CAMERA_EVENT = "review-camera-observation"
ACTOR_GEOMETRY_STATUS_EVENT = "actor-geometry-status"
APP_CULLED_FLAG = 0x1
ROTATION_COMPONENTS = 9
VECTOR_COMPONENTS = 3
NPC_ROLES = ("father", "doctor", "mother")
ACTOR_SET_SCHEMA = "opennv-ttw-fo3-cg00-stage10-actor-set/v1"
ACTOR_SET_STATUS = "effective-ttw-actors-materialized-for-exact-live-stage10"
ACTOR_SCENE_SCHEMA = "opennv-actor-scene/v5"
ACTOR_SIDECAR_SCHEMA = "opennv-actor-gltf/v4"
SKELETON_HELPER_NAME = "HeadAnims:0"
SKELETON_HELPER_VERTEX_COUNT = 3
SKELETON_HELPER_VERTEX_FNV1A32 = 965597692
SEMANTIC_ROLE_BY_RETAIL_NAME = {
    "FaceGenFace": "head",
    "FaceGenMouth": "mouth",
    "FaceGenTeethLower": "teeth-lower",
    "FaceGenTeethUpper": "teeth-upper",
    "FaceGenTongue": "tongue",
    "FaceGenHairNoHat": "hair",
    "FaceGenHairHat": "hair",
    "FaceGenEyeLeft": "eye-left",
    "FaceGenEyeRight": "eye-right",
    "FaceGenAccessory": "head-part",
}


def _sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _canonical_sha256(value: object) -> str:
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()


def _hex(value: object, label: str) -> str:
    result = str(value).casefold()
    if len(result) != 64 or any(character not in "0123456789abcdef" for character in result):
        raise ValueError(f"TTW stage-10 {label} is not a SHA-256")
    return result


def _finite_vector(value: object, count: int, label: str) -> list[float]:
    if not isinstance(value, list) or len(value) != count:
        raise ValueError(f"TTW stage-10 {label} cardinality differs")
    result = [float(component) for component in value]
    if not all(math.isfinite(component) for component in result):
        raise ValueError(f"TTW stage-10 {label} contains a non-finite component")
    return result


def _matrix_vector(rotation: list[float], vector: list[float]) -> list[float]:
    return [
        sum(rotation[row * VECTOR_COMPONENTS + column] * vector[column]
            for column in range(VECTOR_COMPONENTS))
        for row in range(VECTOR_COMPONENTS)
    ]


def _transpose_matrix_vector(rotation: list[float], vector: list[float]) -> list[float]:
    return [
        sum(rotation[row * VECTOR_COMPONENTS + column] * vector[row]
            for row in range(VECTOR_COMPONENTS))
        for column in range(VECTOR_COMPONENTS)
    ]


def _surface_depths(
    geometry: dict[str, object],
    camera_rotation: list[float],
    camera_translation: list[float],
) -> list[float]:
    transform = geometry.get("transform")
    if not isinstance(transform, dict):
        raise ValueError("TTW stage-10 actor surface transform is absent")
    rotation = _finite_vector(
        transform.get("worldRotation"), ROTATION_COMPONENTS, "surface rotation"
    )
    translation = _finite_vector(
        transform.get("worldTranslation"), VECTOR_COMPONENTS, "surface translation"
    )
    scale = float(transform.get("worldScale"))
    vertices = geometry.get("vertices")
    if not math.isfinite(scale) or scale <= 0.0 or not isinstance(vertices, list):
        raise ValueError("TTW stage-10 actor surface geometry is invalid")
    depths: list[float] = []
    for index, source in enumerate(vertices):
        vertex = _finite_vector(source, VECTOR_COMPONENTS, f"surface vertex {index}")
        world_offset = _matrix_vector(rotation, [component * scale for component in vertex])
        world = [world_offset[axis] + translation[axis] for axis in range(VECTOR_COMPONENTS)]
        camera_local = _transpose_matrix_vector(
            camera_rotation,
            [world[axis] - camera_translation[axis] for axis in range(VECTOR_COMPONENTS)],
        )
        depth = camera_local[0]
        if not math.isfinite(depth):
            raise ValueError("TTW stage-10 actor surface depth is not finite")
        depths.append(depth)
    if len(depths) != int(geometry.get("vertexCount", -1)) or not depths:
        raise ValueError("TTW stage-10 actor surface vertex count differs")
    return sorted(depths)


def _geometry_node_rows(
    actor_frame: dict[str, object], geometries: list[dict[str, object]]
) -> list[dict[str, object]]:
    nodes = actor_frame.get("bones")
    if not isinstance(nodes, list):
        raise ValueError("TTW stage-10 actor scene-node observation is absent")
    node_groups: dict[tuple[object, object, object], list[dict[str, object]]] = {}
    for node in nodes:
        if not isinstance(node, dict):
            raise ValueError("TTW stage-10 actor scene-node observation is malformed")
        key = (node.get("name"), node.get("parentName"), node.get("depth"))
        node_groups.setdefault(key, []).append(node)
    geometry_groups: dict[tuple[object, object, object], list[int]] = {}
    for index, geometry in enumerate(geometries):
        key = (geometry.get("name"), geometry.get("parentName"), geometry.get("depth"))
        geometry_groups.setdefault(key, []).append(index)
    result: list[dict[str, object] | None] = [None] * len(geometries)
    for key, indices in geometry_groups.items():
        matches = node_groups.get(key, [])
        if len(matches) != len(indices):
            raise ValueError(
                "TTW stage-10 actor surface/node identity cardinality differs: "
                f"{key!r} geometry={len(indices)} nodes={len(matches)}"
            )
        for index, node in zip(indices, matches, strict=True):
            result[index] = node
    if any(row is None for row in result):
        raise ValueError("TTW stage-10 actor surface/node join is incomplete")
    return [row for row in result if row is not None]


def derive_surface_contract(
    presentation: dict[str, object],
    events: list[dict[str, object]],
    *,
    presentation_path: Path,
    presentation_sha256: str,
    raw_path: Path,
    raw_sha256: str,
) -> dict[str, object]:
    if (
        presentation.get("schema") != PRESENTATION_SCHEMA
        or presentation.get("campaign") != "Fallout3"
        or presentation.get("edition") != "TTW"
        or presentation.get("stage") != 10
    ):
        raise ValueError("TTW stage-10 presentation contract identity differs")
    evidence = presentation.get("evidence")
    camera = presentation.get("camera")
    participants = presentation.get("participants")
    if not all(isinstance(value, dict) for value in (evidence, camera, participants)):
        raise ValueError("TTW stage-10 presentation authority is incomplete")
    if (
        Path(str(evidence.get("rawPath", ""))).resolve() != raw_path.resolve()
        or _hex(evidence.get("rawSha256"), "raw observation") != raw_sha256
    ):
        raise ValueError("TTW stage-10 raw observation binding differs")
    camera_rows = [row for row in events if row.get("event") == CAMERA_EVENT]
    if len(camera_rows) != 1:
        raise ValueError("TTW stage-10 live camera observation is absent or repeated")
    observed_camera = camera_rows[0]
    frame = int(camera.get("frame", -1))
    frustum = _finite_vector(camera.get("frustum"), 7, "presentation frustum")
    observed_frustum = _finite_vector(
        observed_camera.get("frustum"), 7, "observed frustum"
    )
    camera_world = camera.get("worldTransform")
    observed_world = observed_camera.get("cameraWorld")
    if (
        observed_camera.get("schema") != RAW_SCHEMA
        or observed_camera.get("frame") != frame
        or observed_camera.get("projectionExact") is not True
        or frustum != observed_frustum
        or not isinstance(camera_world, dict)
        or not isinstance(observed_world, dict)
    ):
        raise ValueError("TTW stage-10 observed camera differs from the presentation contract")
    camera_rotation = _finite_vector(
        observed_world.get("rotation"), ROTATION_COMPONENTS, "camera rotation"
    )
    camera_translation = _finite_vector(
        observed_world.get("translation"), VECTOR_COMPONENTS, "camera translation"
    )
    if (
        camera_rotation != _finite_vector(
            camera_world.get("rotationRowMajor"), ROTATION_COMPONENTS, "contract camera rotation"
        )
        or camera_translation != _finite_vector(
            camera_world.get("translationGameUnits"), VECTOR_COMPONENTS,
            "contract camera translation",
        )
        or float(observed_world.get("scale")) != float(camera_world.get("scale"))
    ):
        raise ValueError("TTW stage-10 live camera transform differs from the contract")
    near = frustum[4]

    role_rows: dict[str, object] = {}
    for role in NPC_ROLES:
        participant = participants.get(role)
        if not isinstance(participant, dict):
            raise ValueError(f"TTW stage-10 {role} participant is absent")
        runtime_form_id = str(participant.get("runtimeFormId", ""))
        try:
            ref_form = int(runtime_form_id, 16)
        except ValueError as error:
            raise ValueError(f"TTW stage-10 {role} runtime FormID is invalid") from error
        geometries = [
            row for row in events
            if row.get("event") == ACTOR_GEOMETRY_EVENT
            and row.get("frame") == frame
            and row.get("refForm") == ref_form
        ]
        frames = [
            row for row in events
            if row.get("event") == ACTOR_FRAME_EVENT
            and row.get("frame") == frame
            and row.get("refForm") == ref_form
        ]
        statuses = [
            row for row in events
            if row.get("event") == ACTOR_GEOMETRY_STATUS_EVENT
            and row.get("frame") == frame
            and row.get("refForm") == ref_form
        ]
        if len(frames) != 1 or len(statuses) != 1 or not geometries:
            raise ValueError(f"TTW stage-10 {role} actor geometry observation is incomplete")
        status = statuses[0]
        if (
            status.get("emittedShapes") != len(geometries)
            or status.get("geometryCandidates") != len(geometries)
            or status.get("pointerReadFailures") != 0
            or status.get("dataReadFailures") != 0
            or status.get("invalidDataLayouts") != 0
            or status.get("vertexReadFailures") != 0
            or status.get("traversalFault") is not False
        ):
            raise ValueError(f"TTW stage-10 {role} geometry traversal did not complete exactly")
        nodes = _geometry_node_rows(frames[0], geometries)
        surface_rows = []
        for geometry, node in zip(geometries, nodes, strict=True):
            if (
                geometry.get("schema") != RAW_SCHEMA
                or geometry.get("complete") is not True
                or geometry.get("name") != node.get("name")
                or geometry.get("parentName") != node.get("parentName")
                or geometry.get("depth") != node.get("depth")
            ):
                raise ValueError(f"TTW stage-10 {role} surface identity differs")
            depths = _surface_depths(geometry, camera_rotation, camera_translation)
            flags = int(node.get("runtimeFlags", -1))
            if flags < 0:
                raise ValueError(f"TTW stage-10 {role} surface runtime flags are invalid")
            clipped = sum(depth <= near for depth in depths)
            surface_rows.append({
                "name": str(geometry.get("name")),
                "parentName": str(geometry.get("parentName")),
                "runtimeType": str(geometry.get("runtimeType")),
                "shaderPropertyType": geometry.get("shaderPropertyType"),
                "skinInstanceType": geometry.get("skinInstanceType"),
                "sceneDepth": int(geometry.get("depth", -1)),
                "runtimeFlags": flags,
                "appCulled": bool(flags & APP_CULLED_FLAG),
                "vertexCount": len(depths),
                "sourceVertexFnv1a32": int(geometry.get("fnv1a32", -1)),
                "minimumDepthGameUnits": depths[0],
                "maximumDepthGameUnits": depths[-1],
                "verticesAtOrBehindNearPlane": clipped,
                "verticesInFrontOfNearPlane": len(depths) - clipped,
                "sortedDepthsGameUnits": depths,
                "sortedDepthDistributionSha256": _canonical_sha256(depths),
            })
        role_rows[role] = {
            "referenceFormKey": participant.get("referenceFormKey"),
            "runtimeFormId": runtime_form_id.casefold(),
            "visible": participant.get("visible"),
            "appCulled": participant.get("appCulled"),
            "observedSurfaceCount": len(surface_rows),
            "observedVertexCount": sum(row["vertexCount"] for row in surface_rows),
            "nonAppCulledSurfaceCount": sum(not row["appCulled"] for row in surface_rows),
            "nonAppCulledVertexCount": sum(
                row["vertexCount"] for row in surface_rows if not row["appCulled"]
            ),
            "surfaces": surface_rows,
        }
    return {
        "schema": OUTPUT_SCHEMA,
        "status": OUTPUT_STATUS,
        "campaign": "Fallout3",
        "edition": "TTW",
        "stage": 10,
        "sourceAuthority": (
            "hash-bound-private-live-actor-geometry-and-camera-observation;"
            "no-authored-transform-or-clipping-allowance"
        ),
        "presentationContract": {
            "path": str(presentation_path.resolve()),
            "sha256": presentation_sha256,
        },
        "rawObservation": {"path": str(raw_path.resolve()), "sha256": raw_sha256},
        "camera": {
            "frame": frame,
            "rotationRowMajor": camera_rotation,
            "translationGameUnits": camera_translation,
            "nearGameUnits": near,
            "depthContract": (
                "transpose(cameraWorldRotation)*(surfaceWorldRotation*"
                "(vertex*surfaceWorldScale)+surfaceWorldTranslation-"
                "cameraWorldTranslation);local-positive-X"
            ),
        },
        "participants": role_rows,
    }


def _atomic_json(path: Path, value: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def audit_materialized_actor_set(
    contract: dict[str, object],
    actor_set: dict[str, object],
    *,
    actor_set_path: Path,
    actor_set_sha256: str,
) -> dict[str, object]:
    if (
        actor_set.get("schema") != ACTOR_SET_SCHEMA
        or actor_set.get("status") != ACTOR_SET_STATUS
        or actor_set.get("campaign") != "Fallout3"
        or actor_set.get("edition") != "TTW"
        or actor_set.get("stage") != 10
        or actor_set.get("standaloneActorArtifactsAccepted") is not False
        or actor_set.get("ownedPayloadsEmbedded") is not False
    ):
        raise ValueError("TTW stage-10 materialized actor-set identity differs")
    actors = actor_set.get("actors")
    participants = contract.get("participants")
    if not isinstance(actors, dict) or not isinstance(participants, dict):
        raise ValueError("TTW stage-10 actor-set or retail surface contract is incomplete")
    rows: dict[str, object] = {}
    blockers: list[str] = []
    for role in NPC_ROLES:
        actor = actors.get(role)
        retail = participants.get(role)
        if not isinstance(actor, dict) or not isinstance(retail, dict):
            raise ValueError(f"TTW stage-10 {role} actor-set join is absent")
        scene_path = Path(str(actor.get("actorScene", ""))).resolve()
        scene_sha256 = _hex(actor.get("actorSceneSha256"), f"{role} actor scene")
        if not scene_path.is_file() or _sha256(scene_path) != scene_sha256:
            raise ValueError(f"TTW stage-10 {role} actor scene identity differs")
        scene = json.loads(scene_path.read_text(encoding="utf-8"))
        outputs = scene.get("outputs")
        if (
            scene.get("schema") != ACTOR_SCENE_SCHEMA
            or scene.get("status") != "skinned-animated"
            or not isinstance(outputs, dict)
        ):
            raise ValueError(f"TTW stage-10 {role} actor scene contract differs")
        sidecar_path = scene_path.parent / str(outputs.get("sidecar", ""))
        sidecar_sha256 = _hex(outputs.get("sidecarSha256"), f"{role} actor sidecar")
        if not sidecar_path.is_file() or _sha256(sidecar_path) != sidecar_sha256:
            raise ValueError(f"TTW stage-10 {role} actor sidecar identity differs")
        sidecar = json.loads(sidecar_path.read_text(encoding="utf-8"))
        surfaces = sidecar.get("surfaces")
        omitted_surfaces = sidecar.get("omittedSurfaces")
        if (
            sidecar.get("schema") != ACTOR_SIDECAR_SCHEMA
            or sidecar.get("status") != "skinned-animated"
            or not isinstance(surfaces, list)
            or not isinstance(omitted_surfaces, list)
        ):
            raise ValueError(f"TTW stage-10 {role} actor sidecar contract differs")
        materialized = []
        for surface in surfaces:
            if not isinstance(surface, dict):
                raise ValueError(f"TTW stage-10 {role} actor surface is malformed")
            vertex_count = int(surface.get("vertices", -1))
            if vertex_count <= 0:
                raise ValueError(f"TTW stage-10 {role} actor surface count is invalid")
            materialized.append({
                "role": str(surface.get("role")),
                "shape": str(surface.get("shape")),
                "sourceShape": str(surface.get("sourceShape")),
                "vertexCount": vertex_count,
                "modelPath": str(surface.get("modelPath")),
                "modelSha256": _hex(surface.get("modelSha256"), f"{role} surface model"),
                "sourceVertexFnv1a32": int(surface.get("sourceVertexFnv1a32", -1)),
            })
        retail_surfaces = retail.get("surfaces")
        if not isinstance(retail_surfaces, list):
            raise ValueError(f"TTW stage-10 {role} retail surface rows are absent")
        presented = [row for row in retail_surfaces if not bool(row.get("appCulled"))]
        skeleton_helpers = [
            row for row in presented if row.get("name") == SKELETON_HELPER_NAME
        ]
        if (
            len(skeleton_helpers) != 1
            or int(skeleton_helpers[0].get("vertexCount", -1))
            != SKELETON_HELPER_VERTEX_COUNT
            or int(skeleton_helpers[0].get("sourceVertexFnv1a32", -1))
            != SKELETON_HELPER_VERTEX_FNV1A32
        ):
            raise ValueError(f"TTW stage-10 {role} skeleton helper identity differs")
        actor_presentation_surfaces = [
            row for row in presented if row.get("name") != SKELETON_HELPER_NAME
        ]
        unmatched = set(range(len(materialized)))
        semantic_join = []
        for retail_surface in actor_presentation_surfaces:
            retail_name = str(retail_surface["name"])
            vertex_count = int(retail_surface["vertexCount"])
            vertex_hash = int(retail_surface["sourceVertexFnv1a32"])
            exact = [
                index for index in unmatched
                if materialized[index]["vertexCount"] == vertex_count
                and materialized[index]["sourceVertexFnv1a32"] == vertex_hash
            ]
            join_basis = "exact-source-vertex-hash"
            candidates = exact
            if not candidates:
                expected_role = SEMANTIC_ROLE_BY_RETAIL_NAME.get(retail_name)
                candidates = [
                    index for index in unmatched
                    if materialized[index]["vertexCount"] == vertex_count
                    and (
                        materialized[index]["role"] == expected_role
                        or expected_role == "head-part"
                        and materialized[index]["role"].startswith("head-part-")
                    )
                ]
                join_basis = "facegen-runtime-semantic-role-and-vertex-count"
            if len(candidates) != 1:
                raise ValueError(
                    f"TTW stage-10 {role} semantic surface join differs for "
                    f"{retail_name}: candidates={candidates}"
                )
            index = candidates[0]
            unmatched.remove(index)
            semantic_join.append({
                "retailName": retail_name,
                "retailVertexCount": vertex_count,
                "retailSourceVertexFnv1a32": vertex_hash,
                "materializedRole": materialized[index]["role"],
                "materializedSourceShape": materialized[index]["sourceShape"],
                "materializedModelPath": materialized[index]["modelPath"],
                "materializedModelSha256": materialized[index]["modelSha256"],
                "materializedSourceVertexFnv1a32": materialized[index][
                    "sourceVertexFnv1a32"
                ],
                "joinBasis": join_basis,
                "retailSortedDepthDistributionSha256": retail_surface[
                    "sortedDepthDistributionSha256"
                ],
            })
        app_culled = [row for row in retail_surfaces if bool(row.get("appCulled"))]
        omitted_hair = [
            row for row in omitted_surfaces
            if isinstance(row, dict)
            and row.get("role") == "hair"
            and row.get("disposition") == "omit-nonselected-authored-shape"
        ]
        app_culled_accounting = []
        for retail_surface in app_culled:
            candidates = [
                row for row in omitted_hair
                if int(row.get("vertices", -1)) == int(retail_surface["vertexCount"])
            ]
            if len(candidates) != 1:
                raise ValueError(
                    f"TTW stage-10 {role} AppCulled surface accounting differs: "
                    f"{retail_surface['name']} candidates={len(candidates)}"
                )
            app_culled_accounting.append({
                "retailName": retail_surface["name"],
                "retailRuntimeFlags": retail_surface["runtimeFlags"],
                "retailVertexCount": retail_surface["vertexCount"],
                "sourceModelPath": candidates[0]["modelPath"],
                "sourceModelSha256": candidates[0]["modelSha256"],
                "sourceShape": candidates[0]["shape"],
                "sourceVertexFnv1a32": candidates[0]["sourceVertexFnv1a32"],
                "disposition": "not-materialized-because-exact-runtime-AppCulled",
            })
        retail_counts = Counter(
            int(row["vertexCount"]) for row in actor_presentation_surfaces
        )
        materialized_counts = Counter(int(row["vertexCount"]) for row in materialized)
        count_multiset_match = retail_counts == materialized_counts
        total_match = sum(retail_counts.elements()) == sum(materialized_counts.elements())
        role_blockers = []
        if unmatched:
            role_blockers.append("materialized-surface-has-no-retail-semantic-match")
        if not count_multiset_match:
            role_blockers.append("non-app-culled-retail-surface-vertex-count-multiset-differs")
        if not total_match:
            role_blockers.append("non-app-culled-retail-total-vertex-count-differs")
        if role_blockers:
            blockers.extend(f"{role}:{blocker}" for blocker in role_blockers)
        rows[role] = {
            "actorScene": {"path": str(scene_path), "sha256": scene_sha256},
            "actorSidecar": {"path": str(sidecar_path), "sha256": sidecar_sha256},
            "skeletonSha256": _hex(actor.get("skeletonSha256"), f"{role} skeleton"),
            "retailNonAppCulledSurfaceCount": len(presented),
            "retailNonAppCulledVertexCount": sum(retail_counts.elements()),
            "retailActorPresentationSurfaceCount": len(actor_presentation_surfaces),
            "skeletonHelperAccounted": True,
            "materializedSurfaceCount": len(materialized),
            "materializedVertexCount": sum(materialized_counts.elements()),
            "surfaceVertexCountMultisetMatches": count_multiset_match,
            "totalVertexCountMatches": total_match,
            "retailNonAppCulledSurfaces": [
                {
                    "name": row["name"],
                    "parentName": row["parentName"],
                    "vertexCount": row["vertexCount"],
                    "sourceVertexFnv1a32": row["sourceVertexFnv1a32"],
                    "sortedDepthDistributionSha256": row[
                        "sortedDepthDistributionSha256"
                    ],
                }
                for row in presented
            ],
            "materializedSurfaces": materialized,
            "semanticSurfaceJoin": semantic_join,
            "appCulledSurfaceAccounting": app_culled_accounting,
            "exactDepthDistributionCompared": False,
            "exactDepthDistributionBlocker": (
                "native-posed-materialized-surface-depth-evidence-absent"
            ),
            "blockers": role_blockers,
        }
    blockers.append("all-roles:native-posed-materialized-surface-depth-evidence-absent")
    return {
        "actorSet": {"path": str(actor_set_path.resolve()), "sha256": actor_set_sha256},
        "standaloneActorArtifactsAccepted": False,
        "participants": rows,
        "surfaceIdentityAndCountReady": not any(
            row["blockers"] for row in rows.values()
        ),
        "exactDepthDistributionReady": False,
        "acceptedNativeSnapshotReady": False,
        "blockers": blockers,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--presentation-contract", type=Path, required=True)
    parser.add_argument("--actor-set", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    presentation_path = arguments.presentation_contract.resolve()
    presentation_sha256 = _sha256(presentation_path)
    presentation = json.loads(presentation_path.read_text(encoding="utf-8"))
    raw_path = Path(str(presentation["evidence"]["rawPath"])).resolve()
    raw_sha256 = _sha256(raw_path)
    events = [json.loads(line) for line in raw_path.read_text(encoding="utf-8").splitlines()]
    result = derive_surface_contract(
        presentation,
        events,
        presentation_path=presentation_path,
        presentation_sha256=presentation_sha256,
        raw_path=raw_path,
        raw_sha256=raw_sha256,
    )
    if arguments.actor_set is not None:
        actor_set_path = arguments.actor_set.resolve()
        actor_set_sha256 = _sha256(actor_set_path)
        actor_set = json.loads(actor_set_path.read_text(encoding="utf-8"))
        result["materializedActorAudit"] = audit_materialized_actor_set(
            result,
            actor_set,
            actor_set_path=actor_set_path,
            actor_set_sha256=actor_set_sha256,
        )
    _atomic_json(arguments.output.resolve(), result)
    print(json.dumps({
        "schema": OUTPUT_SCHEMA,
        "output": str(arguments.output.resolve()),
        "sha256": _sha256(arguments.output.resolve()),
    }, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
