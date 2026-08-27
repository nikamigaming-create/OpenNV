#!/usr/bin/env python3
"""Build one fail-closed retail-versus-Godot actor review artifact."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

from actor_parity import contact_sheet, difference_metrics, image_metrics
from plugin_stack import file_sha256
from runtime_configuration import RuntimeConfiguration, load_runtime_configuration


ACTOR_REVIEW_SCENE_SCHEMA = "opennv-actor-review-scene/v1"
ACTOR_REVIEW_CONTRACT_SCHEMA = "opennv-actor-review-contract/v6"
GODOT_REVIEW_REPORT_SCHEMA = "nikami-opennv-actor-review-capture/v1"
GODOT_ENGINE_REPORT_SCHEMA = "opennv-godot-actor-review-capture/v1"
DIFFERENTIAL_SCHEMA = "opennv-actor-review-differential/v1"
COMPILED_SCENE_STATUS = "compiled-retail-observed-pending-godot-capture"
CAPTURED_GODOT_STATUS = "captured-pending-parity"
CAPTURED_ENGINE_STATUS = "captured-provisional-light-direction"
RETAIL_CONTRACT_STATUS = "retail-observed-godot-pending"
PASS_STATUS = "pass"
FAIL_STATUS = "fail"
PENDING_STATUS = "pending"
HUMAN_REVIEW_PENDING_STATUS = "human-review-pending"
IDLE_MOTION_SHOT = "idle-motion"
H264_CODEC = "h264"
H264_ENCODER = "libx264"
H264_PIXEL_FORMAT = "yuv420p"
SHA256_HEX_CHARACTERS = 64
FRAME_INDEX_DIGITS = 6
CONTACT_SHEET_INDEX_DIGITS = 3
FFCONCAT_DURATION_DECIMALS = 9
EXIT_DATA_ERROR = 2


def load_json(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return document


def require_object(value: object, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be an object")
    return value


def require_text(value: object, label: str) -> str:
    if not isinstance(value, str) or not value:
        raise ValueError(f"{label} must be a non-empty string")
    return value


def require_positive_number(value: object, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)) or value <= 0:
        raise ValueError(f"{label} must be positive")
    return float(value)


def path_key(path: Path) -> str:
    return str(path.resolve()).casefold()


def validate_hash(path: Path, expected: object, label: str) -> str:
    digest = require_text(expected, f"{label} SHA-256").lower()
    if len(digest) != SHA256_HEX_CHARACTERS or not re.fullmatch(r"[0-9a-f]+", digest):
        raise ValueError(f"{label} has an invalid SHA-256")
    if not path.is_file() or file_sha256(path).lower() != digest:
        raise ValueError(f"{label} is missing or changed: {path}")
    return digest


def validate_descriptor(value: object, label: str) -> Path:
    descriptor = require_object(value, label)
    path = Path(require_text(descriptor.get("path"), f"{label} path")).resolve()
    validate_hash(path, descriptor.get("sha256"), label)
    expected_bytes = descriptor.get("bytes")
    if expected_bytes is not None and path.stat().st_size != expected_bytes:
        raise ValueError(f"{label} byte count changed: {path}")
    return path


def artifact(path: Path) -> dict[str, object]:
    resolved = path.resolve()
    return {
        "path": str(resolved),
        "bytes": resolved.stat().st_size,
        "sha256": file_sha256(resolved),
    }


def sample_key(shot_kind: object, frame: object, label: str) -> tuple[str, int]:
    kind = require_text(shot_kind, f"{label} shot kind")
    if isinstance(frame, bool) or not isinstance(frame, int) or frame < 0:
        raise ValueError(f"{label} frame must be a non-negative integer")
    return kind, frame


def contract_samples(contract: dict[str, object]) -> dict[tuple[str, int], dict[str, object]]:
    retail = require_object(contract.get("retail"), "retail contract")
    shots = retail.get("shots")
    if not isinstance(shots, list) or not shots:
        raise ValueError("Retail contract must contain shots")
    rows: dict[tuple[str, int], dict[str, object]] = {}
    for shot in shots:
        shot_object = require_object(shot, "retail shot")
        kind = shot_object.get("kind")
        samples = shot_object.get("samples")
        if not isinstance(samples, list) or not samples:
            raise ValueError(f"Retail shot has no samples: {kind}")
        for sample in samples:
            sample_object = require_object(sample, f"retail {kind} sample")
            key = sample_key(kind, sample_object.get("frame"), "retail sample")
            if key in rows:
                raise ValueError(f"Duplicate retail sample: {key}")
            rows[key] = sample_object
    return rows


def godot_samples(engine: dict[str, object]) -> dict[tuple[str, int], dict[str, object]]:
    samples = engine.get("samples")
    if not isinstance(samples, list) or not samples:
        raise ValueError("Godot actor review must contain samples")
    rows: dict[tuple[str, int], dict[str, object]] = {}
    for sample in samples:
        sample_object = require_object(sample, "Godot sample")
        key = sample_key(
            sample_object.get("shotKind"), sample_object.get("Frame"), "Godot sample"
        )
        if key in rows:
            raise ValueError(f"Duplicate Godot sample: {key}")
        rows[key] = sample_object
    return rows


def rendering_passes(
    retail_metrics: dict[str, object],
    godot_metrics: dict[str, object],
    difference: dict[str, float],
    configuration: RuntimeConfiguration,
) -> bool:
    parity = configuration.actor_parity
    luminance_delta = abs(
        float(retail_metrics["meanLuminance"])
        - float(godot_metrics["meanLuminance"])
    )
    return (
        difference["meanAbsoluteError"] <= parity.maximum_mean_absolute_error
        and difference["changedPixelFraction"]
        <= parity.maximum_changed_pixel_fraction
        and luminance_delta <= parity.maximum_mean_luminance_delta
    )


def structural_passes(sample: dict[str, object]) -> bool:
    skin = require_object(sample.get("skinPalette"), "Godot skin-palette result")
    return (
        sample.get("projectionExact") is True
        and sample.get("posePassed") is True
        and skin.get("passed") is True
        and isinstance(sample.get("finalSceneColorSurface"), dict)
        and isinstance(sample.get("cullingObservation"), dict)
    )


def motion_durations(frames: list[int], timeline_frame_rate: float) -> list[float]:
    if len(frames) < 2:
        raise ValueError("Idle-motion evidence requires at least two samples")
    if frames != sorted(frames) or len(set(frames)) != len(frames):
        raise ValueError("Idle-motion source frames must be unique and ordered")
    durations = [
        (right - left) / timeline_frame_rate
        for left, right in zip(frames, frames[1:])
    ]
    if any(duration <= 0 for duration in durations):
        raise ValueError("Idle-motion sample duration must be positive")
    return [*durations, durations[-1]]


def ffconcat_path(path: Path) -> str:
    value = path.resolve().as_posix()
    if "'" in value:
        raise ValueError(f"FFconcat evidence path contains an unsupported quote: {path}")
    return value


def build_motion_clip(
    ffmpeg: Path,
    comparison_rows: list[dict[str, object]],
    retail_report: dict[str, object],
    output_root: Path,
) -> dict[str, object]:
    motion_rows = [
        row for row in comparison_rows if row["shotKind"] == IDLE_MOTION_SHOT
    ]
    motion_rows.sort(key=lambda row: int(row["frame"]))
    capture = require_object(retail_report.get("capture"), "retail capture")
    video = require_object(capture.get("motionVideo"), "retail motion-video contract")
    timeline_rate = require_positive_number(
        video.get("timelineFrameRate"), "retail motion timeline frame rate"
    )
    output_rate = require_positive_number(
        video.get("outputFrameRate"), "retail motion output frame rate"
    )
    if video.get("codec") != H264_CODEC:
        raise ValueError(f"Unsupported retail motion codec: {video.get('codec')}")
    frames = [int(row["frame"]) for row in motion_rows]
    retained_sources = video.get("sourceFrames")
    if not isinstance(retained_sources, list):
        raise ValueError("Retail motion-video contract has no source-frame ledger")
    retained_frames = [
        int(require_object(row, "retail motion source").get("frame"))
        for row in retained_sources
    ]
    if retained_frames != frames:
        raise ValueError("Retail motion-video frames differ from matched idle samples")
    for source, comparison in zip(retained_sources, motion_rows):
        source_path = Path(
            require_text(
                require_object(source, "retail motion source").get("path"),
                "retail motion source path",
            )
        )
        retail_frame = require_object(comparison.get("retailFrame"), "retail frame")
        if path_key(source_path) != path_key(
            Path(require_text(retail_frame.get("path"), "retail frame path"))
        ):
            raise ValueError("Retail motion-video source path differs from matched evidence")
    validate_descriptor(video.get("file"), "retail motion video")
    validate_descriptor(video.get("sourceManifest"), "retail motion source manifest")
    durations = motion_durations(frames, timeline_rate)
    manifest_path = output_root / "idle-motion-retail-vs-godot.ffconcat"
    lines = ["ffconcat version 1.0"]
    for row, duration in zip(motion_rows, durations):
        lines.append(f"file '{ffconcat_path(Path(str(row['contactSheet'])))}'")
        lines.append(f"duration {duration:.{FFCONCAT_DURATION_DECIMALS}f}")
    lines.append(f"file '{ffconcat_path(Path(str(motion_rows[-1]['contactSheet'])))}'")
    manifest_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    output_path = output_root / "idle-motion-retail-vs-godot.mp4"
    command = [
        str(ffmpeg.resolve()),
        "-hide_banner",
        "-loglevel",
        "error",
        "-f",
        "concat",
        "-safe",
        "0",
        "-i",
        str(manifest_path),
        "-r",
        str(output_rate),
        "-c:v",
        H264_ENCODER,
        "-pix_fmt",
        H264_PIXEL_FORMAT,
        "-movflags",
        "+faststart",
        str(output_path),
    ]
    completed = subprocess.run(command, capture_output=True, text=True, check=False)
    stdout_path = output_root / "ffmpeg.stdout.log"
    stderr_path = output_root / "ffmpeg.stderr.log"
    stdout_path.write_text(completed.stdout, encoding="utf-8")
    stderr_path.write_text(completed.stderr, encoding="utf-8")
    if completed.returncode != 0 or not output_path.is_file():
        raise ValueError(
            f"FFmpeg failed to build actor comparison clip; see {stderr_path}"
        )
    return {
        "timelineFrameRate": timeline_rate,
        "outputFrameRate": output_rate,
        "codec": H264_CODEC,
        "sourceFrames": frames,
        "manifest": artifact(manifest_path),
        "video": artifact(output_path),
        "stdout": artifact(stdout_path),
        "stderr": artifact(stderr_path),
    }


def build_actor_review_differential(
    scene_path: Path,
    godot_report_path: Path,
    output_root: Path,
    ffmpeg: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite actor differential: {output_root}")
    if not ffmpeg.is_file():
        raise FileNotFoundError(f"FFmpeg executable is missing: {ffmpeg}")
    configuration = load_runtime_configuration()
    scene = load_json(scene_path)
    if (
        scene.get("schema") != ACTOR_REVIEW_SCENE_SCHEMA
        or scene.get("status") != COMPILED_SCENE_STATUS
    ):
        raise ValueError(f"Unexpected actor review scene: {scene_path}")
    scene_configuration = require_object(scene.get("configuration"), "scene configuration")
    if (
        scene_configuration.get("schema") != configuration.document["schema"]
        or str(scene_configuration.get("sha256", "")).lower() != configuration.sha256
    ):
        raise ValueError("Actor review scene uses another runtime configuration")

    scene_contract = require_object(scene.get("retailContract"), "scene retail contract")
    contract_path = Path(require_text(scene_contract.get("path"), "contract path")).resolve()
    contract_hash = validate_hash(
        contract_path, scene_contract.get("sha256"), "scene retail contract"
    )
    contract = load_json(contract_path)
    if (
        contract.get("schema") != ACTOR_REVIEW_CONTRACT_SCHEMA
        or contract.get("status") != RETAIL_CONTRACT_STATUS
    ):
        raise ValueError(f"Unexpected retail actor contract: {contract_path}")
    review = require_object(contract.get("review"), "contract review")
    review_key = require_text(review.get("reviewKey"), "contract review key")
    record_type = require_text(review.get("recordType"), "contract record type")
    if review_key != scene.get("reviewKey") or record_type != scene.get("recordType"):
        raise ValueError("Scene and retail contract actor identity differ")
    retail = require_object(contract.get("retail"), "retail contract")
    retail_report_path = validate_descriptor(retail.get("report"), "retail report")
    retail_report = load_json(retail_report_path)

    godot_report = load_json(godot_report_path)
    if (
        godot_report.get("schema") != GODOT_REVIEW_REPORT_SCHEMA
        or godot_report.get("status") != CAPTURED_GODOT_STATUS
        or godot_report.get("parityPassed") is True
        or godot_report.get("reviewKey") != review_key
        or godot_report.get("recordType") != record_type
    ):
        raise ValueError(f"Unexpected Godot actor review report: {godot_report_path}")
    engine = require_object(godot_report.get("engine"), "Godot engine report")
    if (
        engine.get("schema") != GODOT_ENGINE_REPORT_SCHEMA
        or engine.get("status") != CAPTURED_ENGINE_STATUS
        or engine.get("parityPassed") is True
    ):
        raise ValueError("Godot engine report is not pending parity")
    engine_configuration = require_object(
        engine.get("configuration"), "Godot engine configuration"
    )
    if str(engine_configuration.get("sha256", "")).lower() != configuration.sha256:
        raise ValueError("Godot capture uses another runtime configuration")
    capture = require_object(godot_report.get("capture"), "Godot capture policy")
    if any(
        capture.get(key) is not False
        for key in (
            "windowsAppControlUsed",
            "foregroundActivationUsed",
            "foregroundInputInjected",
            "outputOverwritten",
        )
    ):
        raise ValueError("Godot actor capture violated the immutable no-control policy")
    capture_artifacts = godot_report.get("artifacts")
    if not isinstance(capture_artifacts, list) or not capture_artifacts:
        raise ValueError("Godot actor capture has no artifact ledger")
    capture_artifacts_by_path = {
        path_key(Path(require_text(row.get("path"), "capture artifact path"))): row
        for row in capture_artifacts
        if isinstance(row, dict)
    }
    if len(capture_artifacts_by_path) != len(capture_artifacts):
        raise ValueError("Godot capture artifact paths are missing or duplicated")
    for bound_path, label in (
        (scene_path, "actor review scene"),
        (contract_path, "retail actor contract"),
    ):
        bound_artifact = capture_artifacts_by_path.get(path_key(bound_path))
        if bound_artifact is None:
            raise ValueError(f"Godot capture did not retain its {label}")
        validate_hash(bound_path, bound_artifact.get("sha256"), label)
    engine_contract = require_object(engine.get("retailContract"), "engine retail contract")
    if (
        path_key(Path(require_text(engine_contract.get("Path"), "engine contract path")))
        != path_key(contract_path)
        or str(engine_contract.get("Sha256", "")).lower() != contract_hash
    ):
        raise ValueError("Godot capture is bound to another retail contract")

    retail_rows = contract_samples(contract)
    godot_rows = godot_samples(engine)
    if retail_rows.keys() != godot_rows.keys():
        raise ValueError("Retail and Godot sample identities do not match exactly")
    required_shots = review.get("requiredShots")
    if (
        not isinstance(required_shots, list)
        or set(required_shots) != {kind for kind, _ in retail_rows}
        or len(required_shots) != len(set(required_shots))
        or capture.get("sourceFrames") != len(retail_rows)
    ):
        raise ValueError("Required shots or captured sample count are incomplete")
    engine_files = {
        path_key(Path(require_text(row.get("path"), "Godot file evidence path"))): row
        for row in engine.get("files", [])
        if isinstance(row, dict)
    }
    if len(engine_files) != len(engine.get("files", [])):
        raise ValueError("Godot file evidence paths are missing or duplicated")

    output_root.mkdir(parents=True)
    comparisons: list[dict[str, object]] = []
    for ordinal, key in enumerate(retail_rows):
        shot_kind, frame = key
        retail_sample = retail_rows[key]
        godot_sample = godot_rows[key]
        retail_frame = validate_descriptor(
            retail_sample.get("sourceFrame"), f"retail {shot_kind} frame {frame}"
        )
        godot_frame = Path(
            require_text(godot_sample.get("godotFrame"), "Godot frame path")
        ).resolve()
        godot_evidence = engine_files.get(path_key(godot_frame))
        if godot_evidence is None:
            raise ValueError(f"Godot frame is absent from engine evidence: {godot_frame}")
        validate_hash(
            godot_frame,
            godot_evidence.get("sha256"),
            f"Godot {shot_kind} frame {frame}",
        )
        applied_retail = require_object(
            godot_sample.get("retailSourceFrame"), "Godot-applied retail source frame"
        )
        if (
            path_key(Path(require_text(applied_retail.get("Path"), "applied retail path")))
            != path_key(retail_frame)
            or str(applied_retail.get("Sha256", "")).lower()
            != file_sha256(retail_frame).lower()
        ):
            raise ValueError(f"Godot sample applied another retail frame: {key}")

        retail_metrics = image_metrics(retail_frame)
        godot_metrics = image_metrics(godot_frame)
        difference = difference_metrics(retail_frame, godot_frame, configuration)
        rendering_pass = rendering_passes(
            retail_metrics, godot_metrics, difference, configuration
        )
        structural_pass = structural_passes(godot_sample)
        sample_pass = rendering_pass and structural_pass
        safe_kind = re.sub(r"[^a-z0-9]+", "-", shot_kind.lower()).strip("-")
        if not safe_kind:
            raise ValueError(f"Shot kind has no safe file-name characters: {shot_kind}")
        sheet_path = output_root / (
            f"{ordinal:0{CONTACT_SHEET_INDEX_DIGITS}d}-{safe_kind}-"
            f"frame-{frame:0{FRAME_INDEX_DIGITS}d}-retail-vs-godot.png"
        )
        contact_sheet(
            retail_frame,
            godot_frame,
            sheet_path,
            shot_kind,
            difference,
            configuration,
            godot_status="PASS" if sample_pass else "CURRENT FAIL",
        )
        comparisons.append(
            {
                "shotKind": shot_kind,
                "frame": frame,
                "status": PASS_STATUS if sample_pass else FAIL_STATUS,
                "renderingStatus": PASS_STATUS if rendering_pass else FAIL_STATUS,
                "structuralStatus": PASS_STATUS if structural_pass else FAIL_STATUS,
                "retailFrame": artifact(retail_frame),
                "godotFrame": artifact(godot_frame),
                "contactSheet": str(sheet_path.resolve()),
                "contactSheetSha256": file_sha256(sheet_path),
                "retailFrameMetrics": retail_metrics,
                "godotFrameMetrics": godot_metrics,
                "differenceMetrics": difference,
                "posePassed": godot_sample.get("posePassed") is True,
                "skinPalettePassed": require_object(
                    godot_sample.get("skinPalette"), "Godot skin-palette result"
                ).get("passed")
                is True,
                "projectionExact": godot_sample.get("projectionExact") is True,
            }
        )

    presentation = require_object(engine.get("presentation"), "Godot presentation")
    directional_resolved = presentation.get("retailDirectionalVectorResolved") is True
    objective_pass = (
        all(row["status"] == PASS_STATUS for row in comparisons)
        and directional_resolved
    )
    motion = build_motion_clip(ffmpeg, comparisons, retail_report, output_root)
    fail_reasons = []
    if not directional_resolved:
        fail_reasons.append("retail-light-direction-unresolved")
    if any(row["renderingStatus"] != PASS_STATUS for row in comparisons):
        fail_reasons.append("pixel-rendering-threshold-failed")
    if any(row["structuralStatus"] != PASS_STATUS for row in comparisons):
        fail_reasons.append("pose-or-surface-structure-failed")

    report = {
        "schema": DIFFERENTIAL_SCHEMA,
        "status": HUMAN_REVIEW_PENDING_STATUS if objective_pass else FAIL_STATUS,
        "parityPassed": False,
        "reviewKey": review_key,
        "baseFormKey": review.get("baseFormKey"),
        "recordType": record_type,
        "editorId": review.get("editorId", ""),
        "configuration": configuration.manifest(),
        "scene": artifact(scene_path),
        "retailContract": artifact(contract_path),
        "retailReport": artifact(retail_report_path),
        "godotReport": artifact(godot_report_path),
        "objectiveStatus": PASS_STATUS if objective_pass else FAIL_STATUS,
        "humanVisualVerdict": PENDING_STATUS,
        "failReasons": fail_reasons,
        "comparisonCount": len(comparisons),
        "motion": motion,
        "comparisons": comparisons,
        "coverageLedgerRow": {
            "reviewKey": review_key,
            "recordType": record_type,
            "retailEvidenceStatus": PASS_STATUS,
            "godotCaptureStatus": PASS_STATUS,
            "matchedComparisonStatus": PASS_STATUS if objective_pass else FAIL_STATUS,
            "humanReviewStatus": PENDING_STATUS,
            "lookedAt": False,
            "parityStatus": FAIL_STATUS,
        },
        "evidencePolicy": {
            "everyRequiredSampleCompared": True,
            "motionClipRequired": True,
            "humanVisualVerdictRequired": True,
            "captureSuccessIsNotParityPass": True,
            "unresolvedLightDirectionCannotPass": True,
        },
    }
    report_path = output_root / "actor-review-differential-report.json"
    report_path.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    report["report"] = str(report_path.resolve())
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--actor-review-scene", required=True, type=Path)
    parser.add_argument("--godot-report", required=True, type=Path)
    parser.add_argument("--output-root", required=True, type=Path)
    parser.add_argument("--ffmpeg", required=True, type=Path)
    args = parser.parse_args()
    try:
        report = build_actor_review_differential(
            args.actor_review_scene.resolve(),
            args.godot_report.resolve(),
            args.output_root.resolve(),
            args.ffmpeg.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_ACTOR_REVIEW_DIFFERENTIAL_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_ACTOR_REVIEW_DIFFERENTIAL "
        + json.dumps(
            {
                "report": report["report"],
                "reviewKey": report["reviewKey"],
                "status": report["status"],
                "comparisons": report["comparisonCount"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
