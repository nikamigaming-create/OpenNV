#!/usr/bin/env python3
"""Bind gallery shots to hash-verified authored-reference retail observations."""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import sys
from pathlib import Path

from actor_review_contract import (
    _descriptor,
    _environment_contract,
    _event_hash,
    load_actor_observation_evidence,
)
from plugin_stack import FORM_ID_HEX_CHARACTERS, FORM_ID_RADIX, file_sha256
from prepare_wasteland_gallery import _gallery_shot_identity, _load_gallery
from runtime_configuration import configuration_path, load_runtime_configuration


EVIDENCE_SCHEMA = "opennv-gallery-retail-evidence/v2"
EVIDENCE_STATUS = "retail-authored-reference-observed"
MANIFEST_SCHEMA = "opennv-gallery-retail-evidence-manifest/v1"
MANIFEST_STATUS = "complete-retail-authored-reference-observations"
AUTHORED_PLACEMENT_MODE = "owned-authored-reference-preserved"
EXIT_DATA_ERROR = 2
SOURCE_FRAME_PATTERN = re.compile(r"^frame-(?P<frame>[0-9]+)\.bmp$", re.IGNORECASE)
SPATIAL_DIMENSIONS = 3
ROTATION_COMPONENT_COUNT = SPATIAL_DIMENSIONS * SPATIAL_DIMENSIONS
FRUSTUM_COMPONENT_COUNT = 7
VIEWPORT_COMPONENT_COUNT = 4
MATRIX_COMPONENT_COUNT = 16
FRUSTUM_LEFT_INDEX = 0
FRUSTUM_RIGHT_INDEX = 1
FRUSTUM_TOP_INDEX = 2
FRUSTUM_BOTTOM_INDEX = 3
FRUSTUM_NEAR_INDEX = 4
FRUSTUM_FAR_INDEX = 5
FRUSTUM_ORTHOGRAPHIC_INDEX = 6


def _load_json(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"Expected one JSON object: {path}")
    return document


def _atomic_json(path: Path, document: object) -> None:
    if path.exists():
        raise FileExistsError(f"Refusing to overwrite retail evidence: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    os.replace(temporary, path)


def _normalize_form_id(value: object) -> str:
    text = str(value).strip().lower()
    if text.startswith("0x"):
        text = text[2:]
    return f"{int(text, FORM_ID_RADIX):0{FORM_ID_HEX_CHARACTERS}x}"


def _indexed_gallery(
    gallery: dict[str, object],
) -> tuple[dict[str, dict[str, object]], dict[str, dict[str, object]]]:
    subjects = {str(row["id"]): row for row in gallery["subjects"]}
    locations = {str(row["id"]): row for row in gallery["locations"]}
    if len(subjects) != len(gallery["subjects"]) or len(locations) != len(
        gallery["locations"]
    ):
        raise ValueError("Gallery subject/location identities are not unique")
    return subjects, locations


def _artifact_descriptor(
    path: Path,
    artifacts: dict[str, dict[str, object]],
) -> dict[str, object]:
    resolved = path.resolve()
    row = artifacts.get(str(resolved).casefold())
    if row is None:
        raise ValueError(f"Retail artifact is absent from its report ledger: {resolved}")
    descriptor = _descriptor(resolved)
    if (
        descriptor["bytes"] != int(row["bytes"])
        or str(descriptor["sha256"]).lower() != str(row["sha256"]).lower()
    ):
        raise ValueError(f"Retail artifact content changed: {resolved}")
    return descriptor


def _expected_shot_identity(
    subject: dict[str, object],
    subject_profile: dict[str, object],
    location: dict[str, object],
) -> dict[str, object]:
    return _gallery_shot_identity(subject, subject_profile, location)


def _finite_numbers(value: object, count: int, label: str) -> list[float]:
    if not isinstance(value, list) or len(value) != count:
        raise ValueError(f"Retail {label} must contain {count} values")
    result = [float(component) for component in value]
    if not all(math.isfinite(component) for component in result):
        raise ValueError(f"Retail {label} contains a non-finite value")
    return result


def _presentation_reference(
    events: list[dict[str, object]],
    source_frames: list[dict[str, object]],
    shot_kind: str,
    source_frame: int,
    source_frame_camera_contract: dict[str, object],
    selection_proof: dict[str, object],
    reference_form_id: str,
    base_form_id: str,
) -> dict[str, object]:
    cameras = [
        event
        for event in events
        if event.get("event") == "review-camera-observation"
        and event.get("shotKind") == shot_kind
        and int(event.get("frame", -1)) == source_frame
    ]
    if len(cameras) != 1:
        raise ValueError(
            f"Retail presentation shot {shot_kind!r} requires exactly one camera event"
        )
    camera = cameras[0]
    frame = int(camera["frame"])
    snapshots = [
        event
        for event in events
        if event.get("event") == "actor-visual-snapshot"
        and int(event.get("frame", -1)) == frame
    ]
    poses = [
        event
        for event in events
        if event.get("event") == "actor-pose-sample"
        and int(event.get("frame", -1)) == frame
    ]
    if len(snapshots) != 1 or len(poses) != 1:
        raise ValueError(
            "Retail presentation camera has no unique same-frame actor snapshot and pose"
        )
    snapshot = snapshots[0]
    pose = poses[0]
    expected_reference = int(reference_form_id, FORM_ID_RADIX)
    expected_base = int(base_form_id, FORM_ID_RADIX)
    for label, event in (("snapshot", snapshot), ("pose", pose)):
        if (
            int(event.get("refForm", -1)) != expected_reference
            or int(event.get("baseForm", -1)) != expected_base
        ):
            raise ValueError(f"Retail presentation {label} identifies another actor")

    matching_frames = []
    for descriptor in source_frames:
        match = SOURCE_FRAME_PATTERN.fullmatch(Path(str(descriptor["path"])).name)
        if match is not None and int(match.group("frame")) == frame:
            matching_frames.append(descriptor)
    if len(matching_frames) != 1:
        raise ValueError(
            "Retail presentation camera has no unique same-frame native source frame"
        )

    if not bool(camera.get("readable")) or not bool(camera.get("projectionExact")):
        raise ValueError("Retail presentation camera is not exact readable perspective data")
    camera_world = camera.get("cameraWorld")
    actor_root = snapshot.get("rootWorld")
    if not isinstance(camera_world, dict) or not isinstance(actor_root, dict):
        raise ValueError("Retail presentation transforms are missing")
    camera_rotation = _finite_numbers(
        camera_world.get("rotation"),
        ROTATION_COMPONENT_COUNT,
        "presentation camera rotation",
    )
    camera_translation = _finite_numbers(
        camera_world.get("translation"),
        SPATIAL_DIMENSIONS,
        "presentation camera translation",
    )
    actor_rotation = _finite_numbers(
        actor_root.get("rotation"),
        ROTATION_COMPONENT_COUNT,
        "presentation actor rotation",
    )
    actor_translation = _finite_numbers(
        actor_root.get("translation"),
        SPATIAL_DIMENSIONS,
        "presentation actor translation",
    )
    camera_scale = float(camera_world.get("scale", float("nan")))
    actor_scale = float(actor_root.get("scale", float("nan")))
    fov = float(camera.get("fovYRadians", float("nan")))
    frustum = _finite_numbers(
        camera.get("frustum"), FRUSTUM_COMPONENT_COUNT, "presentation frustum"
    )
    viewport = _finite_numbers(
        camera.get("viewport"), VIEWPORT_COMPONENT_COUNT, "presentation viewport"
    )
    view_matrix = _finite_numbers(
        camera.get("viewMatrix"), MATRIX_COMPONENT_COUNT, "presentation view matrix"
    )
    projection_matrix = _finite_numbers(
        camera.get("projectionMatrix"),
        MATRIX_COMPONENT_COUNT,
        "presentation projection matrix",
    )
    if (
        not math.isfinite(camera_scale)
        or camera_scale <= 0.0
        or not math.isfinite(actor_scale)
        or actor_scale <= 0.0
        or not math.isfinite(fov)
        or not 0.0 < fov < math.pi
        or frustum[FRUSTUM_LEFT_INDEX] >= frustum[FRUSTUM_RIGHT_INDEX]
        or frustum[FRUSTUM_BOTTOM_INDEX] >= frustum[FRUSTUM_TOP_INDEX]
        or frustum[FRUSTUM_NEAR_INDEX] <= 0.0
        or frustum[FRUSTUM_FAR_INDEX] <= frustum[FRUSTUM_NEAR_INDEX]
        or frustum[FRUSTUM_ORTHOGRAPHIC_INDEX] != 0.0
    ):
        raise ValueError("Retail presentation camera or actor transform is invalid")
    sequences = [
        {
            "file": str(sequence["file"]),
            "state": int(sequence["state"]),
            "cycle": int(sequence["cycle"]),
            "weight": float(sequence["weight"]),
            "frequency": float(sequence["frequency"]),
            "lastScaledSeconds": float(sequence["lastScaled"]),
            "group": int(sequence["group"]),
        }
        for sequence in pose.get("animationDataSequences", [])
        if isinstance(sequence, dict) and str(sequence.get("file", "")).strip()
    ]
    if not sequences or any(
        not all(
            math.isfinite(float(sequence[field]))
            for field in ("weight", "frequency", "lastScaledSeconds")
        )
        for sequence in sequences
    ):
        raise ValueError("Retail presentation pose has no finite active animation sequence")
    return {
        "shotKind": shot_kind,
        "frame": frame,
        "sourceFrame": matching_frames[0],
        "cameraEventSha256": _event_hash(camera),
        "sourceFrameCameraContractEventSha256": _event_hash(
            source_frame_camera_contract
        ),
        "actorSnapshotEventSha256": _event_hash(snapshot),
        "actorPoseEventSha256": _event_hash(pose),
        "camera": {
            "world": {
                "rotation": camera_rotation,
                "translation": camera_translation,
                "scale": camera_scale,
            },
            "offsetFromActorRootGameUnits": [
                camera_translation[index] - actor_translation[index]
                for index in range(3)
            ],
            "fovYRadians": fov,
            "frustum": frustum,
            "viewport": viewport,
            "viewMatrix": view_matrix,
            "projectionMatrix": projection_matrix,
        },
        "actor": {
            "rootWorld": {
                "rotation": actor_rotation,
                "translation": actor_translation,
                "scale": actor_scale,
            },
            "weaponOut": bool(pose.get("weaponOut")),
            "weaponForm": int(pose.get("weaponForm", 0)),
            "animationDataSequences": sequences,
        },
        "selection": selection_proof,
        "derivation": "same-frame-retail-camera-actor-root-pose-and-native-backbuffer",
    }


def _select_presentation_reference(
    events: list[dict[str, object]],
    report: dict[str, object],
    source_frames: list[dict[str, object]],
    selection: dict[str, object],
    reference_form_id: str,
    base_form_id: str,
) -> dict[str, object]:
    if selection.get("schema") != "opennv-gallery-presentation-selection/v1":
        raise ValueError("Gallery presentation selection schema is invalid")
    candidate_shot_kinds = [str(value) for value in selection["candidateShotKinds"]]
    rules = {str(row["focusKind"]): row for row in selection["semanticFocusFacingRules"]}
    if len(rules) != len(selection["semanticFocusFacingRules"]):
        raise ValueError("Gallery presentation focus kinds are not unique")
    surface_contract = report.get("runtime", {}).get("surfaceContract", {})
    surface_frames = surface_contract.get("sourceFrames")
    if not isinstance(surface_frames, list):
        raise ValueError("Retail report has no source-frame actor surface contract")
    camera_contracts = [
        event
        for event in events
        if event.get("event") == "portrait-camera-source-frame"
    ]
    translation_tolerance = float(selection["cameraTranslationToleranceGameUnits"])
    required_status = str(selection["requiredSurfaceStatus"])

    for shot_kind in candidate_shot_kinds:
        candidates = sorted(
            (
                row
                for row in surface_frames
                if str(row.get("shotKind", "")) == shot_kind
                and str(row.get("status", "")) == required_status
                and (
                    not bool(selection["requireSemanticFocusSurface"])
                    or bool(row.get("semanticFocusSurface"))
                )
            ),
            key=lambda row: int(row.get("sourceFrame", -1)),
        )
        for surface_frame in candidates:
            frame = int(surface_frame["sourceFrame"])
            matching_contracts = [
                event
                for event in camera_contracts
                if int(event.get("frame", -1)) == frame
                and str(event.get("shotKind", "")) == shot_kind
            ]
            if len(matching_contracts) != 1:
                continue
            camera_contract = matching_contracts[0]
            focus_kind = str(camera_contract.get("focusKind", ""))
            rule = rules.get(focus_kind)
            if rule is None or shot_kind not in [
                str(value) for value in rule["allowedShotKinds"]
            ]:
                continue
            focus_forward = _finite_numbers(
                camera_contract.get("headForwardXY"),
                2,
                "presentation focus forward",
            )
            camera_direction = _finite_numbers(
                camera_contract.get("cameraDirectionXY"),
                2,
                "presentation camera direction",
            )
            raw_facing_dot = sum(
                focus_forward[index] * camera_direction[index] for index in range(2)
            )
            # Both vectors are normalized by the retail oracle. Float32 rounding can
            # place their dot product a few ULPs outside the mathematical unit range.
            facing_dot = max(-1.0, min(1.0, raw_facing_dot))
            if not (
                float(rule["minimumCameraDirectionDotFocusForward"])
                <= facing_dot
                <= float(rule["maximumCameraDirectionDotFocusForward"])
            ):
                continue
            corridor = camera_contract.get("cameraCorridor")
            if not isinstance(corridor, dict):
                continue
            if (
                bool(selection["requireCameraOutsideActorWorldBound"])
                and not bool(corridor.get("outsideWorldBound"))
            ) or (
                bool(selection["requireClearCameraCorridor"])
                and not bool(corridor.get("passed"))
            ):
                continue
            cameras = [
                event
                for event in events
                if event.get("event") == "review-camera-observation"
                and int(event.get("frame", -1)) == frame
                and str(event.get("shotKind", "")) == shot_kind
            ]
            if len(cameras) != 1:
                continue
            camera_translation = _finite_numbers(
                cameras[0].get("cameraWorld", {}).get("translation"),
                3,
                "presentation observed camera translation",
            )
            contract_translation = _finite_numbers(
                camera_contract.get("camera"),
                3,
                "presentation source-frame camera translation",
            )
            if any(
                abs(camera_translation[index] - contract_translation[index])
                > translation_tolerance
                for index in range(3)
            ):
                continue
            selection_proof = {
                "policySchema": str(selection["schema"]),
                "tieBreak": str(selection["tieBreak"]),
                "focusKind": focus_kind,
                "focusRuleOrdinal": camera_contract.get("focusRuleOrdinal"),
                "cameraDirectionDotFocusForward": facing_dot,
                "rawCameraDirectionDotFocusForward": raw_facing_dot,
                "surfaceStatus": str(surface_frame["status"]),
                "semanticFocusSurface": bool(
                    surface_frame.get("semanticFocusSurface")
                ),
                "cameraOutsideActorWorldBound": bool(
                    corridor["outsideWorldBound"]
                ),
                "cameraCorridorPassed": bool(corridor["passed"]),
                "cameraTranslationToleranceGameUnits": translation_tolerance,
                "candidateShotKinds": candidate_shot_kinds,
            }
            return _presentation_reference(
                events,
                source_frames,
                shot_kind,
                frame,
                camera_contract,
                selection_proof,
                reference_form_id,
                base_form_id,
            )
    raise ValueError(
        "Retail capture has no presentation frame satisfying semantic surface, "
        "facing, actor-bound, and camera-corridor policy"
    )


def _require_authored_identity(
    subject: dict[str, object],
    subject_profile: dict[str, object],
    location: dict[str, object],
    report: dict[str, object],
) -> dict[str, object]:
    shot = _expected_shot_identity(subject, subject_profile, location)
    expected = {
        "galleryShotId": shot["id"],
        "ordinal": shot["ordinal"],
        "locationId": shot["locationId"],
        "referenceFormId": shot["referenceFormId"],
        "baseFormId": shot["baseFormId"],
        "actor": shot["actor"],
        "scene": shot["scene"],
        "locationClass": shot["locationClass"],
    }
    authored = report["capture"].get("authoredReference")
    runtime = report["runtime"]
    if not isinstance(authored, dict):
        raise ValueError("Retail report has no authored-reference capture contract")
    actual = {
        "galleryShotId": str(authored.get("galleryShotId", "")),
        "ordinal": int(authored.get("ordinal", -1)),
        "locationId": str(authored.get("locationId", "")),
        "referenceFormId": _normalize_form_id(authored.get("referenceFormId", "0")),
        "baseFormId": _normalize_form_id(authored.get("baseFormId", "0")),
        "actor": {
            "cellFormId": _normalize_form_id(
                authored.get("actor", {}).get("cellFormId", "0")
            )
        },
        "scene": {
            "cellFormId": _normalize_form_id(
                authored.get("scene", {}).get("cellFormId", "0")
            ),
            "worldspaceFormId": (
                None
                if authored.get("scene", {}).get("worldspaceFormId") is None
                else _normalize_form_id(
                    authored["scene"]["worldspaceFormId"]
                )
            ),
            "interior": bool(authored.get("scene", {}).get("interior")),
        },
        "locationClass": str(authored.get("locationClass", "")),
    }
    if actual != expected:
        raise ValueError(
            f"Retail authored-reference identity differs from gallery shot {subject['id']}"
        )
    if (
        bool(authored.get("actorTransformMutated", True))
        or str(runtime.get("placementMode", "")) != AUTHORED_PLACEMENT_MODE
        or runtime.get("spawnedReferenceFormId") is not None
        or _normalize_form_id(runtime.get("targetReferenceFormId", "0"))
        != expected["referenceFormId"]
        or _normalize_form_id(runtime.get("requestedTargetRuntimeFormId", "0"))
        != expected["referenceFormId"]
        or _normalize_form_id(runtime.get("requestedBaseRuntimeFormId", "0"))
        != expected["baseFormId"]
        or int(runtime["templateObservation"]["runtimeBaseForm"])
        != int(expected["baseFormId"], FORM_ID_RADIX)
    ):
        raise ValueError(
            f"Retail report mutated or substituted authored actor {subject['id']}"
        )
    live_location = runtime.get("liveLocation")
    if not isinstance(live_location, dict) or not bool(live_location.get("stable")):
        raise ValueError("Retail report has no stable authored actor live location")
    if (
        int(live_location.get("expectedAuthoredCellForm", -1))
        != int(shot["actor"]["cellFormId"], FORM_ID_RADIX)
        or bool(live_location.get("expectedInterior"))
        != bool(shot["scene"]["interior"])
        or (
            shot["scene"]["worldspaceFormId"] is None
            and live_location.get("expectedWorldSpaceForm") is not None
        )
        or (
            shot["scene"]["worldspaceFormId"] is not None
            and int(live_location.get("expectedWorldSpaceForm", -1))
            != int(shot["scene"]["worldspaceFormId"], FORM_ID_RADIX)
        )
    ):
        raise ValueError("Retail actor live location differs from gallery identity")
    return shot


def _require_scene_observer_identity(
    events: list[dict[str, object]],
    shot: dict[str, object],
) -> dict[str, object]:
    observations = [
        event for event in events if event.get("event") == "render-environment"
    ]
    if len(observations) != 1:
        raise ValueError("Retail capture requires one render-environment observation")
    observation = observations[0]
    location = observation.get("observerLocation")
    if not isinstance(location, dict):
        raise ValueError("Retail render environment has no observer CELL/WRLD identity")
    scene = shot["scene"]
    parent_cell = int(location.get("parentCellForm", 0))
    worldspace = int(location.get("worldSpaceForm", 0))
    interior = bool(location.get("interior"))
    expected_cell = int(scene["cellFormId"], FORM_ID_RADIX)
    expected_worldspace = (
        0
        if scene["worldspaceFormId"] is None
        else int(scene["worldspaceFormId"], FORM_ID_RADIX)
    )
    if (
        parent_cell != expected_cell
        or worldspace != expected_worldspace
        or interior != bool(scene["interior"])
        or int(location.get("parentWorldSpaceForm", 0)) != expected_worldspace
    ):
        raise ValueError(
            "Retail observer CELL/WRLD identity differs from the rendered gallery scene"
        )
    coordinates = location.get("parentCellCoordinates")
    if coordinates is not None:
        coordinates = [int(value) for value in _finite_numbers(
            coordinates, 2, "observer parent CELL coordinates"
        )]
    return {
        "eventSha256": _event_hash(observation),
        "cellFormId": f"{parent_cell:0{FORM_ID_HEX_CHARACTERS}x}",
        "worldspaceFormId": (
            None
            if worldspace == 0
            else f"{worldspace:0{FORM_ID_HEX_CHARACTERS}x}"
        ),
        "interior": interior,
        "cellCoordinates": coordinates,
    }


def _validated_evidence_index(
    gallery_path: Path,
    gallery: dict[str, object],
    evidence_paths: list[Path],
) -> dict[str, tuple[Path, dict[str, object]]]:
    subjects, locations = _indexed_gallery(gallery)
    gallery_sha256 = file_sha256(gallery_path).lower()
    runtime_configuration = _descriptor(configuration_path())
    evidence_by_id: dict[str, tuple[Path, dict[str, object]]] = {}
    for path in evidence_paths:
        resolved = path.resolve()
        document = _load_json(resolved)
        if (
            document.get("schema") != EVIDENCE_SCHEMA
            or document.get("status") != EVIDENCE_STATUS
            or str(document["gallery"]["sha256"]).lower() != gallery_sha256
            or not isinstance(document.get("runtimeConfiguration"), dict)
            or int(document["runtimeConfiguration"].get("bytes", -1))
            != int(runtime_configuration["bytes"])
            or str(document["runtimeConfiguration"].get("sha256", "")).lower()
            != str(runtime_configuration["sha256"]).lower()
            or not isinstance(document.get("retail"), dict)
            or not isinstance(document["retail"].get("presentation"), dict)
        ):
            raise ValueError(f"Unexpected gallery retail evidence: {resolved}")
        shot_id = str(document["shot"]["id"])
        if shot_id in evidence_by_id:
            raise ValueError(f"Duplicate gallery retail evidence: {shot_id}")
        subject = subjects.get(shot_id)
        if subject is None:
            raise ValueError(
                f"Retail evidence targets an unknown gallery shot: {shot_id}"
            )
        location = locations[str(subject["locationId"])]
        subject_profile = gallery["subjectProfiles"][str(subject["profile"])]
        if document["shot"] != _expected_shot_identity(
            subject, subject_profile, location
        ):
            raise ValueError(f"Retail evidence identity changed: {resolved}")
        evidence_by_id[shot_id] = (resolved, document)
    if set(evidence_by_id) != set(subjects):
        missing = sorted(set(subjects) - set(evidence_by_id))
        extra = sorted(set(evidence_by_id) - set(subjects))
        raise ValueError(
            f"Gallery retail evidence must be one-to-one; missing={missing} extra={extra}"
        )
    return evidence_by_id


def load_evidence_manifest(
    gallery_path: Path,
    manifest_path: Path,
) -> tuple[dict[str, object], dict[str, dict[str, object]]]:
    gallery = _load_gallery(gallery_path)
    manifest = _load_json(manifest_path)
    if (
        manifest.get("schema") != MANIFEST_SCHEMA
        or manifest.get("status") != MANIFEST_STATUS
        or str(manifest["gallery"]["sha256"]).lower()
        != file_sha256(gallery_path).lower()
        or int(manifest.get("shotCount", 0)) != int(gallery["expectedSubjectCount"])
        or not isinstance(manifest.get("shots"), list)
    ):
        raise ValueError(f"Unexpected gallery retail evidence manifest: {manifest_path}")
    descriptors: dict[str, dict[str, object]] = {}
    paths: list[Path] = []
    for row in manifest["shots"]:
        shot_id = str(row["id"])
        path = Path(str(row["path"])).resolve()
        if shot_id in descriptors or not path.is_file():
            raise ValueError(f"Invalid gallery retail evidence row: {shot_id}")
        descriptor = _descriptor(path)
        if (
            descriptor["bytes"] != int(row["bytes"])
            or str(descriptor["sha256"]).lower() != str(row["sha256"]).lower()
        ):
            raise ValueError(f"Gallery retail evidence content changed: {path}")
        descriptors[shot_id] = {
            "path": descriptor["path"],
            "bytes": descriptor["bytes"],
            "sha256": descriptor["sha256"],
        }
        paths.append(path)
    indexed = _validated_evidence_index(gallery_path, gallery, paths)
    if set(descriptors) != set(indexed):
        raise ValueError("Gallery retail evidence manifest IDs differ from its contracts")
    return manifest, descriptors


def build_shot_evidence(
    data_root: Path,
    corpus_root: Path,
    gallery_path: Path,
    shot_id: str,
    retail_report_path: Path,
    output_path: Path,
) -> dict[str, object]:
    gallery = _load_gallery(gallery_path)
    subjects, locations = _indexed_gallery(gallery)
    subject = subjects.get(shot_id)
    if subject is None:
        raise ValueError(f"Gallery has no subject: {shot_id}")
    location = locations.get(str(subject["locationId"]))
    if location is None:
        raise ValueError(f"Gallery subject has no location: {shot_id}")
    subject_profile = gallery["subjectProfiles"][str(subject["profile"])]
    report_header = _load_json(retail_report_path)
    review_key = str(report_header.get("classifiedReviewKey", ""))
    if not review_key:
        raise ValueError("Retail report has no classified review key")
    source = load_actor_observation_evidence(
        data_root,
        corpus_root,
        review_key,
        retail_report_path,
    )
    identity = _require_authored_identity(
        subject, subject_profile, location, source.report
    )
    capture_policy = source.report["evidencePolicy"]
    if any(
        bool(capture_policy.get(name, True))
        for name in (
            "windowsAppControlUsed",
            "foregroundActivationUsed",
            "foregroundInputInjected",
        )
    ):
        raise ValueError("Retail authored-reference evidence used forbidden app control")
    source_frames = [
        _artifact_descriptor(Path(str(path)), source.artifact_by_path)
        for path in source.report["capture"]["sourceFrames"]
    ]
    environment = _environment_contract(source.events, source.artifact_by_path)
    scene_observer = _require_scene_observer_identity(source.events, identity)
    effective_weather = int(environment["currentWeatherForm"])
    if effective_weather == 0:
        effective_weather = int(environment["defaultWeatherForm"])
    environment["effectiveWeatherForm"] = effective_weather
    configuration = load_runtime_configuration()
    gallery_capture = configuration.document["capture"]["gallery"]
    presentation = _select_presentation_reference(
        source.events,
        source.report,
        source_frames,
        gallery_capture["retailPresentationSelection"],
        identity["referenceFormId"],
        identity["baseFormId"],
    )
    contract = {
        "schema": EVIDENCE_SCHEMA,
        "status": EVIDENCE_STATUS,
        "gallery": _descriptor(gallery_path),
        "runtimeConfiguration": _descriptor(configuration_path()),
        "shot": identity,
        "retail": {
            "report": _descriptor(retail_report_path),
            "oracleJsonl": _descriptor(source.jsonl_path),
            "runtimePluginStackEventSha256": _event_hash(source.runtime_stack),
            "placementMode": AUTHORED_PLACEMENT_MODE,
            "actorTransformMutated": False,
            "sourceFrames": source_frames,
            "environment": environment,
            "sceneObserver": scene_observer,
            "presentation": presentation,
        },
        "provenance": {
            "corpusManifest": _descriptor(source.manifest_path),
            "officialPlugins": [
                {
                    "file": row["file"],
                    "bytes": row["bytes"],
                    "sha256": row["sha256"],
                }
                for row in source.manifest["inputs"]
            ],
        },
        "evidencePolicy": {
            "retailIsReferenceOnly": True,
            "ownedActorTransformPreserved": True,
            "windowsAppControlUsed": False,
            "foregroundActivationUsed": False,
            "foregroundInputInjected": False,
            "godotParityStatus": "pending",
        },
    }
    _atomic_json(output_path, contract)
    contract["contract"] = str(output_path.resolve())
    return contract


def build_evidence_manifest(
    gallery_path: Path,
    evidence_paths: list[Path],
    output_path: Path,
) -> dict[str, object]:
    gallery = _load_gallery(gallery_path)
    evidence_by_id = _validated_evidence_index(
        gallery_path,
        gallery,
        evidence_paths,
    )
    rows = []
    for subject in gallery["subjects"]:
        path, _ = evidence_by_id[str(subject["id"])]
        rows.append(
            {
                "id": str(subject["id"]),
                **_descriptor(path),
            }
        )
    manifest = {
        "schema": MANIFEST_SCHEMA,
        "status": MANIFEST_STATUS,
        "gallery": _descriptor(gallery_path),
        "shotCount": len(rows),
        "shots": rows,
        "complexity": {
            "galleryLookup": "single-pass-hash-index",
            "evidenceLookup": "single-pass-hash-index",
            "processingOrder": "gallery-plus-evidence",
        },
    }
    _atomic_json(output_path, manifest)
    manifest["manifest"] = str(output_path.resolve())
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    shot = subparsers.add_parser("shot")
    shot.add_argument("--data-root", type=Path, required=True)
    shot.add_argument("--corpus-root", type=Path, required=True)
    shot.add_argument("--gallery", type=Path, required=True)
    shot.add_argument("--shot-id", required=True)
    shot.add_argument("--retail-report", type=Path, required=True)
    shot.add_argument("--output", type=Path, required=True)
    manifest = subparsers.add_parser("manifest")
    manifest.add_argument("--gallery", type=Path, required=True)
    manifest.add_argument("--evidence", type=Path, action="append", required=True)
    manifest.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.command == "shot":
            result = build_shot_evidence(
                args.data_root.resolve(),
                args.corpus_root.resolve(),
                args.gallery.resolve(),
                args.shot_id,
                args.retail_report.resolve(),
                args.output.resolve(),
            )
            summary = {
                "contract": result["contract"],
                "shotId": result["shot"]["id"],
                "status": result["status"],
            }
        else:
            result = build_evidence_manifest(
                args.gallery.resolve(),
                [path.resolve() for path in args.evidence],
                args.output.resolve(),
            )
            summary = {
                "manifest": result["manifest"],
                "shotCount": result["shotCount"],
                "status": result["status"],
            }
    except Exception as error:
        print(f"OPENNV_GALLERY_RETAIL_EVIDENCE_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print("OPENNV_GALLERY_RETAIL_EVIDENCE " + json.dumps(summary, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
