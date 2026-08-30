"""Compile the source-authored TTW CG00-to-CG01 traversal boundary.

This is deliberately separate from the live retail stage-10 observation
contract.  It admits record-authored MoveTo markers, package/IDLE/KF playback
at the instant a stage result starts, and the exact CG01 NAVM corridor.  It
does not invent a Camera3D projection or a player collision controller.
"""

from __future__ import annotations

import argparse
import hashlib
import heapq
import json
import math
import struct
from functools import cache
from pathlib import Path

from actor_gltf import animation_sequence_manifest, sample_transform_animation
from bsa_archive import BsaArchive, canonical_member_path
from cell_catalog import parse_navmesh, parse_transform
from cell_scene import godot_position, godot_rotation_quaternion
from corpus_io import atomic_json
from plugin_records import iter_subrecords, zstring
from plugin_stack import file_sha256
from ttw_effective_source import load_ttw_effective_record_source
from ttw_fo3_stage10_resource_closure import _record_identity, _values


SCHEMA = "opennv-ttw-fo3-source-authored-cg00-to-cg01-traversal/v1"
STATUS = "source-authored-root-traversal-player-collision-controller-unresolved"
REPORT_FILE = "ttw-fo3-source-authored-cg00-cg01-traversal.json"
CAMPAIGN = "Fallout3"
EDITION = "TTW"
SOURCE_AUTHORITY = "owned-ttw-effective-records-members-no-standalone-substitution"
PROJECTION_SCHEMA = "opennv-ttw-fo3-cg00-profile-projection/v1"
ARTIFACT_SCHEMA = "opennv-ttw-fo3-cg00-stage10-godot-world-artifact/v1"
STATIC_PROOF_SCHEMA = "opennv-ttw-fo3-stage10-static-world-collision-readiness/v1"
TTW_RUNTIME_BASE_MASTER = "FalloutNV" + ".esm"
CG00_QUEST = f"{TTW_RUNTIME_BASE_MASTER}:01f388"
CG00_PLAYER_MARKER = "Fallout3.esm:039562"
CG01_QUEST = "Fallout3.esm:014e83"
CG01_PLAYER_MARKER = "Fallout3.esm:02ea4f"
CG01_DAD_TRIGGER = "Fallout3.esm:02ea54"
CG01_DAD_TRIGGER_BASE = "Fallout3.esm:081984"
CG01_DAD_TRIGGER_SCRIPT = "Fallout3.esm:081983"
CG01_NAVM = "Fallout3.esm:056a9a"
VAULT101D_CELL = "Fallout3.esm:028138"
CG00_STAGE = 10
CG01_MOVEMENT_STAGE = 10
CG01_TRIGGER_STAGE = 12
CG01_MOVIE_STAGE = 5
CG01_OBJECTIVE = 10
CG01_DAD_TIMER_SECONDS = 5
CG01_PLAYER_SCALE = 0.4
CG01_PLAYER_NAVM_TRIANGLE = 78
CG01_TRIGGER_NAVM_TRIANGLE = 10
CAMERA_TARGET_NODE = "Camera1st"
CAMERA_SAMPLES_PER_SECOND = 30.0
FORM_ID_BYTES = 4
TRIANGLE_VERTEX_COUNT = 3
SHARED_EDGE_VERTEX_COUNT = 2
NO_ADJACENT_TRIANGLE = -1
GODOT_VECTOR_COMPONENTS = 3

PARTICIPANT_MARKERS = {
    "father": ("Fallout3.esm:0290a7", "Fallout3.esm:03a17b"),
    "doctor": ("Fallout3.esm:0290a5", "Fallout3.esm:0290a4"),
    "mother": ("Fallout3.esm:05ede0", "Fallout3.esm:06a810"),
}

ADMITTED_SIGNATURES = frozenset(
    {"ACHR", "ACTI", "IDLE", "NAVM", "PACK", "QUST", "REFR", "SCPT"}
)


def _canonical_sha256(value: object) -> str:
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()


def _descriptor(path: Path, schema: str) -> dict[str, object]:
    resolved = path.resolve()
    return {
        "path": str(resolved),
        "bytes": resolved.stat().st_size,
        "sha256": file_sha256(resolved),
        "schema": schema,
    }


def _load_json(path: Path) -> dict[str, object]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"TTW traversal input is not an object: {path}")
    return value


def _single(values: dict[str, list[bytes]], signature: str, label: str) -> bytes:
    rows = values.get(signature, [])
    if len(rows) != 1:
        raise ValueError(f"TTW traversal {label} has {len(rows)} {signature} rows")
    return rows[0]


def _stage_sources(record: object) -> dict[int, str]:
    stages: dict[int, list[str]] = {}
    current: int | None = None
    for row in iter_subrecords(record):
        if row.signature == "INDX":
            if len(row.data) not in {2, 4}:
                raise ValueError("TTW traversal QUST INDX size differs")
            current = int.from_bytes(row.data, "little")
        elif row.signature == "SCTX" and current is not None:
            stages.setdefault(current, []).append(zstring(row.data))
    if any(len(rows) != 1 for rows in stages.values()):
        raise ValueError("TTW traversal QUST stage sources are ambiguous")
    return {stage: rows[0] for stage, rows in stages.items()}


def _commands(source: str) -> list[str]:
    return [
        command
        for line in source.splitlines()
        if (command := line.split(";", 1)[0].strip())
    ]


def _normalized_command(value: str) -> str:
    return " ".join(value.casefold().split())


def _require_commands(source: str, expected: tuple[str, ...], label: str) -> None:
    actual = [_normalized_command(value) for value in _commands(source)]
    normalized_expected = [_normalized_command(value) for value in expected]
    if actual != normalized_expected:
        raise ValueError(
            f"TTW traversal {label} commands differ: {actual!r}"
        )


def _source_contract(source: str, semantics: list[dict[str, object]]) -> dict[str, object]:
    return {
        "sourceSha256": hashlib.sha256(source.encode("utf-8")).hexdigest(),
        "commandCount": len(_commands(source)),
        "semantics": semantics,
    }


def _authored_transform(source: object, form_key: str) -> dict[str, object]:
    version = source.records.winner(form_key)
    transform = parse_transform(
        _single(_values(version), "DATA", form_key),
        version.record,
    )
    return {
        "sourceIdentity": _record_identity(source, form_key),
        "positionGameUnits": list(transform.position),
        "rotationRadians": list(transform.rotation_radians),
        "positionGodotGameUnits": godot_position(transform.position, (0.0, 0.0, 0.0)),
        "rotationGodotQuaternionXyzw": godot_rotation_quaternion(
            transform.rotation_radians
        ),
    }


def _identity_matches(left: object, right: object, label: str) -> None:
    if (
        not isinstance(left, dict)
        or not isinstance(right, dict)
        or any(right.get(key) != value for key, value in left.items())
    ):
        raise ValueError(f"TTW traversal {label} identity differs")


def _case_insensitive_descendant(root: Path, logical_path: str) -> Path:
    current = root
    for part in canonical_member_path(logical_path).split("\\"):
        matches = [row for row in current.iterdir() if row.name.casefold() == part.casefold()]
        if len(matches) != 1:
            raise FileNotFoundError(
                f"TTW traversal member does not resolve uniquely: {logical_path}"
            )
        current = matches[0]
    if not current.is_file():
        raise FileNotFoundError(f"TTW traversal member is not a file: {logical_path}")
    return current


@cache
def _archive(path: Path) -> BsaArchive:
    return BsaArchive(path)


@cache
def _source_file_sha256(path: Path) -> str:
    return file_sha256(path)


def _member_payload(profile: dict[str, object], member: dict[str, object]) -> bytes:
    logical_path = str(member["logicalPath"])
    winner = member.get("winner")
    roots = profile.get("sourceRoots")
    if not isinstance(winner, dict) or not isinstance(roots, list):
        raise ValueError("TTW traversal member/profile source shape differs")
    root_index = winner.get("sourceRootIndex")
    if not isinstance(root_index, int) or isinstance(root_index, bool) or not 0 <= root_index < len(roots):
        raise ValueError("TTW traversal member source root differs")
    root = Path(str(roots[root_index])).resolve()
    kind = winner.get("kind")
    if kind == "bsa":
        archive_name = str(winner.get("archive", ""))
        archive_path = _case_insensitive_descendant(root, archive_name)
        if _source_file_sha256(archive_path) != winner.get("archiveSha256"):
            raise ValueError("TTW traversal source archive hash differs")
        extracted = _archive(archive_path).extract(logical_path)
        payload = extracted.data
    elif kind == "loose":
        payload = _case_insensitive_descendant(root, logical_path).read_bytes()
    else:
        raise ValueError(f"TTW traversal member winner kind differs: {kind!r}")
    if (
        len(payload) != member.get("bytes")
        or hashlib.sha256(payload).hexdigest() != member.get("sha256")
    ):
        raise ValueError(f"TTW traversal member payload changed: {logical_path}")
    return payload


def _vector_subtract(left: tuple[float, float, float], right: tuple[float, float, float]) -> tuple[float, float, float]:
    return tuple(left[index] - right[index] for index in range(GODOT_VECTOR_COMPONENTS))


def _vector_add(left: tuple[float, float, float], right: tuple[float, float, float]) -> tuple[float, float, float]:
    return tuple(left[index] + right[index] for index in range(GODOT_VECTOR_COMPONENTS))


def _vector_scale(value: tuple[float, float, float], scale: float) -> tuple[float, float, float]:
    return tuple(component * scale for component in value)


def _dot(left: tuple[float, float, float], right: tuple[float, float, float]) -> float:
    return sum(left[index] * right[index] for index in range(GODOT_VECTOR_COMPONENTS))


def _distance_squared(left: tuple[float, float, float], right: tuple[float, float, float]) -> float:
    return sum((left[index] - right[index]) ** 2 for index in range(GODOT_VECTOR_COMPONENTS))


def _closest_point(navmesh: object, triangle_index: int, point: tuple[float, float, float]) -> tuple[float, float, float]:
    triangle = navmesh.triangles[triangle_index]
    first, second, third = [navmesh.vertices[index] for index in triangle.vertex_indices]
    first_to_second = _vector_subtract(second, first)
    first_to_third = _vector_subtract(third, first)
    first_to_point = _vector_subtract(point, first)
    first_second_projection = _dot(first_to_second, first_to_point)
    first_third_projection = _dot(first_to_third, first_to_point)
    if first_second_projection <= 0.0 and first_third_projection <= 0.0:
        return first
    second_to_point = _vector_subtract(point, second)
    second_first_projection = _dot(first_to_second, second_to_point)
    second_third_projection = _dot(first_to_third, second_to_point)
    if second_first_projection >= 0.0 and second_third_projection <= second_first_projection:
        return second
    first_second_region = (
        first_second_projection * second_third_projection
        - second_first_projection * first_third_projection
    )
    if first_second_region <= 0.0 and first_second_projection >= 0.0 and second_first_projection <= 0.0:
        weight = first_second_projection / (first_second_projection - second_first_projection)
        return _vector_add(first, _vector_scale(first_to_second, weight))
    third_to_point = _vector_subtract(point, third)
    third_second_projection = _dot(first_to_second, third_to_point)
    third_first_projection = _dot(first_to_third, third_to_point)
    if third_first_projection >= 0.0 and third_second_projection <= third_first_projection:
        return third
    first_third_region = (
        third_second_projection * first_third_projection
        - first_second_projection * third_first_projection
    )
    if first_third_region <= 0.0 and first_third_projection >= 0.0 and third_first_projection <= 0.0:
        weight = first_third_projection / (first_third_projection - third_first_projection)
        return _vector_add(first, _vector_scale(first_to_third, weight))
    second_third_region = (
        second_first_projection * third_first_projection
        - third_second_projection * second_third_projection
    )
    second_third_first = second_third_projection - second_first_projection
    second_third_second = third_second_projection - third_first_projection
    if second_third_region <= 0.0 and second_third_first >= 0.0 and second_third_second >= 0.0:
        weight = second_third_first / (second_third_first + second_third_second)
        return _vector_add(second, _vector_scale(_vector_subtract(third, second), weight))
    denominator = 1.0 / (
        second_third_region + first_third_region + first_second_region
    )
    second_weight = first_third_region * denominator
    third_weight = first_second_region * denominator
    return _vector_add(
        first,
        _vector_add(
            _vector_scale(first_to_second, second_weight),
            _vector_scale(first_to_third, third_weight),
        ),
    )


def _nearest_triangle(navmesh: object, point: tuple[float, float, float]) -> tuple[int, tuple[float, float, float], float]:
    rows = []
    for index in range(len(navmesh.triangles)):
        nearest = _closest_point(navmesh, index, point)
        rows.append((_distance_squared(point, nearest), index, nearest))
    distance_squared, index, nearest = min(rows)
    return index, nearest, distance_squared


def _internal_neighbors(navmesh: object, source: int) -> list[int]:
    source_vertices = set(navmesh.triangles[source].vertex_indices)
    result = []
    for adjacent in sorted(set(navmesh.triangles[source].adjacent_triangles)):
        if (
            adjacent != NO_ADJACENT_TRIANGLE
            and 0 <= adjacent < len(navmesh.triangles)
            and adjacent != source
            and source in navmesh.triangles[adjacent].adjacent_triangles
            and len(source_vertices.intersection(navmesh.triangles[adjacent].vertex_indices))
            == SHARED_EDGE_VERTEX_COUNT
        ):
            result.append(adjacent)
    return result


def _centroid(navmesh: object, triangle_index: int) -> tuple[float, float, float]:
    vertices = [navmesh.vertices[index] for index in navmesh.triangles[triangle_index].vertex_indices]
    return tuple(
        sum(vertex[axis] for vertex in vertices) / TRIANGLE_VERTEX_COUNT
        for axis in range(GODOT_VECTOR_COMPONENTS)
    )


def _navmesh_route(navmesh: object, start: tuple[float, float, float], destination: tuple[float, float, float]) -> dict[str, object]:
    start_index, start_projected, start_distance_squared = _nearest_triangle(navmesh, start)
    destination_index, destination_projected, destination_distance_squared = _nearest_triangle(navmesh, destination)
    frontier: list[tuple[float, int]] = [(0.0, start_index)]
    previous: dict[int, int] = {}
    costs = {start_index: 0.0}
    while frontier:
        _, current = heapq.heappop(frontier)
        if current == destination_index:
            break
        for adjacent in _internal_neighbors(navmesh, current):
            cost = costs[current] + math.sqrt(
                _distance_squared(_centroid(navmesh, current), _centroid(navmesh, adjacent))
            )
            if cost >= costs.get(adjacent, math.inf):
                continue
            costs[adjacent] = cost
            previous[adjacent] = current
            priority = cost + math.sqrt(
                _distance_squared(_centroid(navmesh, adjacent), _centroid(navmesh, destination_index))
            )
            heapq.heappush(frontier, (priority, adjacent))
    if destination_index != start_index and destination_index not in previous:
        raise ValueError("TTW traversal NAVM has no internal route")
    triangle_path = [destination_index]
    while triangle_path[-1] != start_index:
        triangle_path.append(previous[triangle_path[-1]])
    triangle_path.reverse()
    waypoints = []
    for first, second in zip(triangle_path, triangle_path[1:], strict=False):
        shared = sorted(
            set(navmesh.triangles[first].vertex_indices).intersection(
                navmesh.triangles[second].vertex_indices
            )
        )
        if len(shared) != SHARED_EDGE_VERTEX_COUNT:
            raise ValueError("TTW traversal NAVM corridor has no shared edge")
        point = tuple(
            (navmesh.vertices[shared[0]][axis] + navmesh.vertices[shared[1]][axis])
            / SHARED_EDGE_VERTEX_COUNT
            for axis in range(GODOT_VECTOR_COMPONENTS)
        )
        waypoints.append(
            {
                "kind": "shared-edge-midpoint",
                "fromTriangle": first,
                "toTriangle": second,
                "sharedVertexIndices": shared,
                "positionGameUnits": list(point),
                "positionGodotGameUnits": godot_position(point, (0.0, 0.0, 0.0)),
            }
        )
    waypoints.append(
        {
            "kind": "projected-trigger-center",
            "triangle": destination_index,
            "positionGameUnits": list(destination_projected),
            "positionGodotGameUnits": godot_position(destination_projected, (0.0, 0.0, 0.0)),
        }
    )
    return {
        "algorithm": "cell-navigation-graph-centroid-a-star-shared-edge-midpoints",
        "startTriangle": start_index,
        "destinationTriangle": destination_index,
        "startProjectedGameUnits": list(start_projected),
        "startProjectionDistanceGameUnits": math.sqrt(start_distance_squared),
        "destinationProjectedGameUnits": list(destination_projected),
        "destinationProjectionDistanceGameUnits": math.sqrt(destination_distance_squared),
        "trianglePath": triangle_path,
        "waypoints": waypoints,
    }


def _validate_inputs(
    profile_path: Path,
    namespace_path: Path,
    projection_path: Path,
    resource_closure_path: Path,
    artifact_path: Path,
    static_proof_path: Path,
) -> tuple[
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
]:
    profile = _load_json(profile_path)
    namespace = _load_json(namespace_path)
    projection = _load_json(projection_path)
    resource_closure = _load_json(resource_closure_path)
    artifact = _load_json(artifact_path)
    static_proof = _load_json(static_proof_path)
    if (
        projection.get("schema") != PROJECTION_SCHEMA
        or projection.get("campaign") != CAMPAIGN
        or projection.get("edition") != EDITION
        or projection.get("runtimeReady") is not False
        or artifact.get("schema") != ARTIFACT_SCHEMA
        or artifact.get("campaign") != CAMPAIGN
        or artifact.get("edition") != EDITION
        or artifact.get("staticWorldTransportReady") is not True
        or artifact.get("runtimeReady") is not False
        or static_proof.get("schema") != STATIC_PROOF_SCHEMA
        or static_proof.get("headlessStaticWorldCollisionReadinessPassed") is not True
        or static_proof.get("playerTraversalExecuted") is not False
    ):
        raise ValueError("TTW traversal projection/artifact/static-proof gate differs")
    static_artifact = static_proof.get("artifact")
    artifact_identity = artifact.get("identity")
    projection_profile = projection.get("identityEnvelope", {}).get("sourceProfile", {})
    if (
        not isinstance(static_artifact, dict)
        or static_artifact.get("sha256") != file_sha256(artifact_path)
        or not isinstance(artifact_identity, dict)
        or artifact_identity.get("pluginStackId") != profile.get("pluginStackId")
        or artifact_identity.get("saveCompatibilityId") != profile.get("saveCompatibilityId")
        or projection_profile.get("pluginStackId") != profile.get("pluginStackId")
        or projection_profile.get("saveCompatibilityId") != profile.get("saveCompatibilityId")
    ):
        raise ValueError("TTW traversal identity chain differs")
    closure_identity = artifact_identity.get("resourceClosure")
    if not isinstance(closure_identity, dict) or closure_identity.get("sha256") != file_sha256(resource_closure_path):
        raise ValueError("TTW traversal resource-closure join differs")
    return profile, namespace, projection, resource_closure, artifact, static_proof


def compile_ttw_fo3_source_traversal(
    profile_path: Path,
    namespace_path: Path,
    projection_path: Path,
    resource_closure_path: Path,
    artifact_path: Path,
    static_proof_path: Path,
) -> dict[str, object]:
    (
        profile,
        _namespace,
        projection,
        resource_closure,
        artifact,
        static_proof,
    ) = _validate_inputs(
        profile_path,
        namespace_path,
        projection_path,
        resource_closure_path,
        artifact_path,
        static_proof_path,
    )
    source = load_ttw_effective_record_source(
        profile_path,
        namespace_path,
        ADMITTED_SIGNATURES,
    )
    compiler_identity = source.compiler_contract()
    if (
        compiler_identity["pluginStackId"] != profile["pluginStackId"]
        or compiler_identity["saveCompatibilityId"] != profile["saveCompatibilityId"]
        or compiler_identity["standaloneFallout3ProfileAccepted"] is not False
        or compiler_identity["standaloneNewVegasProfileAccepted"] is not False
    ):
        raise ValueError("TTW traversal source namespace isolation differs")

    early = projection.get("earlyBirthSequence")
    if not isinstance(early, dict):
        raise ValueError("TTW traversal early-birth projection is absent")
    cg00_stages = {int(row["stage"]): row for row in early.get("stages", [])}
    cg00_source = _stage_sources(source.records.winner(CG00_QUEST).record)
    _require_commands(
        cg00_source[CG00_STAGE],
        (
            "set CG00DadREF.doTalk to 1",
            "set CG00DadREF.timer to 0",
            "player.addScriptPackage CG00PlayerSection1",
        ),
        "CG00 stage 10",
    )
    if cg00_stages[CG00_STAGE].get("sourceSha256") != hashlib.sha256(
        cg00_source[CG00_STAGE].encode("utf-8")
    ).hexdigest():
        raise ValueError("TTW traversal CG00 stage-10 source hash differs")

    player_marker = _authored_transform(source, CG00_PLAYER_MARKER)
    projected_marker = early.get("playerStartMarker")
    if not isinstance(projected_marker, dict):
        raise ValueError("TTW traversal projected player marker is absent")
    _identity_matches(
        player_marker["sourceIdentity"],
        projected_marker.get("sourceIdentity"),
        "CG00 player marker",
    )
    participants = []
    projected_participants = {
        str(row["role"]): row for row in early.get("sceneParticipants", [])
    }
    actor_sections = early.get("actorPackageSections")
    if not isinstance(actor_sections, dict):
        raise ValueError("TTW traversal actor package sections are absent")
    for role, (reference_key, marker_key) in PARTICIPANT_MARKERS.items():
        reference = _record_identity(source, reference_key)
        marker = _authored_transform(source, marker_key)
        projected = projected_participants.get(role)
        if not isinstance(projected, dict):
            raise ValueError(f"TTW traversal projected participant is absent: {role}")
        _identity_matches(reference, projected.get("reference", {}).get("sourceIdentity"), f"{role} reference")
        _identity_matches(marker["sourceIdentity"], projected.get("startMarker", {}).get("sourceIdentity"), f"{role} start marker")
        sections = [row for row in actor_sections.get(role, []) if row.get("section") == 1]
        if len(sections) != 1:
            raise ValueError(f"TTW traversal {role} section-1 package is ambiguous")
        section = sections[0]
        payload = _member_payload(profile, dict(section["animationMemberIdentity"]))
        playback = animation_sequence_manifest(payload)
        participants.append(
            {
                "role": role,
                "reference": reference,
                "stage0MoveToMarker": marker,
                "stage10Controller": {
                    "phaseDisposition": "stage-entry-package-start-elapsed-zero-seconds",
                    "elapsedSeconds": 0.0,
                    "package": section["packageSourceIdentity"],
                    "idle": section["idleSourceIdentity"],
                    "animation": section["animationMemberIdentity"],
                    "playback": playback,
                },
            }
        )

    camera = early.get("playerCamera")
    if not isinstance(camera, dict) or camera.get("targetNode") != CAMERA_TARGET_NODE:
        raise ValueError("TTW traversal Camera1st projection differs")
    camera_animation_payload = _member_payload(profile, dict(camera["animationMemberIdentity"]))
    camera_skeleton_payload = _member_payload(profile, dict(camera["skeletonMemberIdentity"]))
    camera_track = sample_transform_animation(
        camera_animation_payload,
        camera_skeleton_payload,
        CAMERA_TARGET_NODE,
        CAMERA_SAMPLES_PER_SECOND,
        include_animated_parent_tracks=True,
    ).manifest()
    camera_sample_hash = _canonical_sha256(camera_track)

    cg01_quest = source.records.winner(CG01_QUEST)
    cg01_sources = _stage_sources(cg01_quest.record)
    _require_commands(
        cg01_sources[0],
        (
            'SetSoundSourceFile PHYBabyRattle "fx\\phy\\babyrattle\\"',
            "CG01DadREF.moveto CG01DadStartMarker",
            "setstage CG01 5",
            "player.setscale .4",
            "player.moveto CG01PlayerStartMarker",
        ),
        "CG01 stage 0",
    )
    _require_commands(
        cg01_sources[CG01_MOVEMENT_STAGE],
        (
            "setObjectiveDisplayed CG01 10 1",
            "set CG01DadREF.timer to 5",
            "EnablePlayerControls 1 0 0 0 1 1 0",
            "autosave",
        ),
        "CG01 stage 10",
    )
    _require_commands(
        cg01_sources[CG01_TRIGGER_STAGE],
        (
            "setObjectiveCompleted CG01 10 1",
            "DisablePlayerControls 1 1 1 1 0 0 1",
            "set CG01DadREF.doTalk to 1",
            "set CG01DadREF.timer to 0",
        ),
        "CG01 stage 12",
    )
    cg01_player_marker = _authored_transform(source, CG01_PLAYER_MARKER)
    trigger = _authored_transform(source, CG01_DAD_TRIGGER)
    trigger_version = source.records.winner(CG01_DAD_TRIGGER)
    trigger_base_key = trigger_version.context.form_key(
        struct.unpack("<I", _single(_values(trigger_version), "NAME", "CG01 Dad trigger"))[0]
    ).text
    if trigger_base_key.casefold() != CG01_DAD_TRIGGER_BASE.casefold():
        raise ValueError("TTW traversal CG01 Dad trigger base differs")
    trigger_base = source.records.winner(trigger_base_key)
    trigger_script_key = trigger_base.context.form_key(
        struct.unpack("<I", _single(_values(trigger_base), "SCRI", "CG01 Dad trigger base"))[0]
    ).text
    if trigger_script_key.casefold() != CG01_DAD_TRIGGER_SCRIPT.casefold():
        raise ValueError("TTW traversal CG01 Dad trigger script link differs")
    trigger_script = zstring(
        _single(_values(source.records.winner(trigger_script_key)), "SCTX", "CG01 Dad trigger script")
    )
    collapsed_trigger = " ".join(
        _normalized_command(command) for command in _commands(trigger_script)
    )
    required_trigger_tokens = (
        "begin ontriggerenter player",
        "getstagedone cg01 12 == 0",
        "isactionref player == 1",
        "setstage cg01 12",
    )
    if any(token not in collapsed_trigger for token in required_trigger_tokens):
        raise ValueError("TTW traversal CG01 Dad trigger semantics differ")

    navmesh_version = source.records.winner(CG01_NAVM)
    navmesh = parse_navmesh(navmesh_version.record)
    navmesh_cell = navmesh_version.context.form_key(navmesh.cell_form_id).text
    if navmesh_cell.casefold() != VAULT101D_CELL.casefold():
        raise ValueError("TTW traversal CG01 NAVM parent CELL differs")
    route = _navmesh_route(
        navmesh,
        tuple(cg01_player_marker["positionGameUnits"]),
        tuple(trigger["positionGameUnits"]),
    )
    if (
        route["startTriangle"] != CG01_PLAYER_NAVM_TRIANGLE
        or route["destinationTriangle"] != CG01_TRIGGER_NAVM_TRIANGLE
    ):
        raise ValueError("TTW traversal CG01 NAVM endpoint triangles changed")

    coordinates = artifact.get("coordinates")
    if (
        not isinstance(coordinates, dict)
        or coordinates.get("sceneSpecificOffsetsAccepted") is not False
        or coordinates.get("sourceOriginGameUnits") != [0.0, 0.0, 0.0]
    ):
        raise ValueError("TTW traversal artifact coordinate contract differs")
    world_scale = float(coordinates["worldUnitsToMeters"])
    if not math.isfinite(world_scale) or world_scale <= 0.0:
        raise ValueError("TTW traversal world scale differs")

    cg01_identity = _record_identity(source, CG01_QUEST)
    navmesh_identity = _record_identity(source, CG01_NAVM)
    document: dict[str, object] = {
        "schema": SCHEMA,
        "status": STATUS,
        "campaign": CAMPAIGN,
        "edition": EDITION,
        "sourceAuthority": SOURCE_AUTHORITY,
        "identity": {
            "pluginStackId": profile["pluginStackId"],
            "saveCompatibilityId": profile["saveCompatibilityId"],
            "sourceProfile": _descriptor(profile_path, str(profile["schema"])),
            "sourceNamespace": _descriptor(namespace_path, str(_namespace["schema"])),
            "projection": _descriptor(projection_path, PROJECTION_SCHEMA),
            "resourceClosure": _descriptor(resource_closure_path, str(resource_closure["schema"])),
            "staticWorldArtifact": _descriptor(artifact_path, ARTIFACT_SCHEMA),
            "staticCollisionProof": _descriptor(static_proof_path, STATIC_PROOF_SCHEMA),
            "compilerSource": compiler_identity,
            "standaloneFallout3Accepted": False,
            "standaloneNewVegasAccepted": False,
        },
        "coordinates": {
            "source": coordinates["source"],
            "godot": coordinates["godot"],
            "sourceOriginGameUnits": coordinates["sourceOriginGameUnits"],
            "worldUnitsToMeters": world_scale,
            "sceneSpecificOffsetsAccepted": False,
        },
        "cg00Stage10": {
            "quest": _record_identity(source, CG00_QUEST),
            "stage": CG00_STAGE,
            "stageResult": _source_contract(
                cg00_source[CG00_STAGE],
                [
                    {"kind": "setDadTalk", "value": 1},
                    {"kind": "setDadTimer", "seconds": 0},
                    {"kind": "addPlayerScriptPackage", "packageEditorId": "CG00PlayerSection1"},
                ],
            ),
            "controls": {
                "movementEnabled": False,
                "source": "CG00-stage5-disableplayercontrols-remains-authoritative",
            },
            "playerStartMarker": player_marker,
            "participants": participants,
            "camera1st": {
                "authority": "owned-first-person-skeleton-plus-section1-kf-at-package-start",
                "phaseDisposition": "stage-entry-package-start-elapsed-zero-seconds",
                "elapsedSeconds": 0.0,
                "targetNode": CAMERA_TARGET_NODE,
                "package": camera["packageSourceIdentity"],
                "idle": camera["idleSourceIdentity"],
                "animation": camera["animationMemberIdentity"],
                "skeleton": camera["skeletonMemberIdentity"],
                "sampleContractSha256": camera_sample_hash,
                "track": camera_track,
                "camera3dProjectionEmitted": False,
                "projectionBlocker": "owned-record-and-kf-closure-has-no-runtime-camera-frustum-or-fov",
            },
            "rootPlacementReady": True,
            "controllerPhaseReadyAtStageEntry": True,
            "renderCameraReady": False,
            "movementBeatAllowed": False,
        },
        "cg01Stage10Traversal": {
            "quest": cg01_identity,
            "stage0": _source_contract(
                cg01_sources[0],
                [
                    {"kind": "movePlayerTo", "target": CG01_PLAYER_MARKER},
                    {"kind": "setPlayerScale", "value": CG01_PLAYER_SCALE},
                    {"kind": "setStage", "stage": CG01_MOVIE_STAGE},
                ],
            ),
            "stage5": {
                "sourceSha256": hashlib.sha256(
                    cg01_sources[CG01_MOVIE_STAGE].encode("utf-8")
                ).hexdigest(),
                "commandCount": len(_commands(cg01_sources[CG01_MOVIE_STAGE])),
                "movementEnabled": False,
                "movieLogicalPath": "1 year later.bik",
            },
            "stage10": _source_contract(
                cg01_sources[CG01_MOVEMENT_STAGE],
                [
                    {"kind": "displayObjective", "objective": CG01_OBJECTIVE},
                    {"kind": "setDadTimer", "seconds": CG01_DAD_TIMER_SECONDS},
                    {"kind": "enablePlayerMovementOnly", "arguments": [1, 0, 0, 0, 1, 1, 0]},
                    {"kind": "autosave"},
                ],
            ),
            "stage12": _source_contract(
                cg01_sources[CG01_TRIGGER_STAGE],
                [
                    {"kind": "completeObjective", "objective": CG01_OBJECTIVE},
                    {"kind": "disablePlayerControls", "arguments": [1, 1, 1, 1, 0, 0, 1]},
                    {"kind": "beginDadTalk"},
                ],
            ),
            "playerStartMarker": cg01_player_marker,
            "dadTrigger": {
                **trigger,
                "base": _record_identity(source, trigger_base_key),
                "script": _record_identity(source, trigger_script_key),
                "scriptSourceSha256": hashlib.sha256(trigger_script.encode("utf-8")).hexdigest(),
                "semantics": "on-player-action-reference-enter-set-cg01-stage12-once",
            },
            "navigation": {
                "navmesh": navmesh_identity,
                "parentCellFormKey": navmesh_cell,
                "version": navmesh.version,
                "vertices": len(navmesh.vertices),
                "triangles": len(navmesh.triangles),
                "route": route,
            },
            "controls": {
                "movementEnabled": True,
                "lookEnabled": True,
                "attackEnabled": False,
                "activateEnabled": False,
                "menuEnabled": True,
                "consoleEnabled": True,
            },
            "autosaveCommandCount": 1,
            "rootTraversalReady": True,
            "physicalPlayerCollisionReady": False,
            "physicalPlayerCollisionBlocker": (
                "owned TTW records/scripts/NAVM/KFs do not encode the runtime player "
                "character-controller shape; OpenNV capsule policy is excluded"
            ),
        },
        "saveContract": {
            "schema": "opennv-ttw-fo3-source-traversal-save/v1",
            "namespace": profile["saveCompatibilityId"],
            "identityFields": [
                "pluginStackId",
                "saveCompatibilityId",
                "sourceTraversalContractSha256",
                "staticWorldArtifactSha256",
                "staticCollisionProofSha256",
            ],
            "stateFields": [
                "questEditorId",
                "stage",
                "routeWaypointIndex",
                "playerRootPositionGodotGameUnits",
                "objective10Displayed",
                "stage10ApplicationCount",
                "autosaveCount",
            ],
            "coldRestoreNoReplayRequired": True,
        },
        "headlessProofPlan": {
            "applyCheckpointWaypointIndex": len(route["waypoints"]) // 2,
            "restoreFinalWaypointIndex": len(route["waypoints"]),
            "movementAuthority": "exact-navm-root-waypoints-no-player-body-proxy",
            "staticCollisionJoin": "hash-bound-passed-godot-shape-publication-proof",
        },
        "readiness": {
            "sourceAuthoredCg00Stage10StateReady": True,
            "sourceAuthoredCg01Stage10RootTraversalReady": True,
            "staticCollisionShellReady": True,
            "saveColdRestoreProofReady": False,
            "physicalPlayerCollisionReady": False,
            "cameraProjectionReady": False,
            "actorsMaterialized": False,
            "runtimeReady": False,
        },
        "remainingBlockers": [
            "exact owned TTW player character-controller collision shape/step policy is absent",
            "CG00 Camera1st node pose has no owned runtime projection/frustum/FOV contract",
            "participant actor artifacts and package controllers are not materialized in this shell",
            "source trigger filter-to-Godot player-body overlap is not executed without that player body",
        ],
        "ownedPayloadsEmitted": False,
        "runtimeReady": False,
    }
    document["contractSha256"] = _canonical_sha256(document)
    return document


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Compile the strict source-authored TTW CG00-to-CG01 traversal contract."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--source-namespace", type=Path, required=True)
    parser.add_argument("--projection", type=Path, required=True)
    parser.add_argument("--resource-closure", type=Path, required=True)
    parser.add_argument("--artifact", type=Path, required=True)
    parser.add_argument("--static-collision-proof", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    output = arguments.output.resolve()
    if output.exists():
        raise FileExistsError(f"Refusing to overwrite TTW traversal contract: {output}")
    document = compile_ttw_fo3_source_traversal(
        arguments.profile,
        arguments.source_namespace,
        arguments.projection,
        arguments.resource_closure,
        arguments.artifact,
        arguments.static_collision_proof,
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    atomic_json(output, document)
    print(
        "TTW_FO3_SOURCE_TRAVERSAL_PASS "
        f"output={output} contractSha256={document['contractSha256']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
