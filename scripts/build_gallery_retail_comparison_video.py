#!/usr/bin/env python3
"""Build a manifest-bound retail/OpenNV wipe and side-by-side gallery video."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import shutil
import subprocess
import sys
from pathlib import Path


POLICY_SCHEMA = "opennv-gallery-retail-comparison-policy/v1"
GALLERY_SCHEMA = "opennv-owned-gallery-compiled/v5"
VIDEO_REPORT_SCHEMA = "nikami-opennv-gallery-video/v1"
SHOT_SCHEMA = "opennv-gallery-shot/v5"
RETAIL_EVIDENCE_SCHEMA = "opennv-gallery-retail-evidence/v4"
OUTPUT_SCHEMA = "opennv-gallery-retail-comparison-video/v1"


def _read_json(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"JSON root is not an object: {path}")
    return document


def _sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _evidence(path: Path) -> dict[str, object]:
    resolved = path.resolve()
    return {
        "path": str(resolved),
        "bytes": resolved.stat().st_size,
        "sha256": _sha256(resolved),
    }


def _verified_file(descriptor: object, description: str) -> Path:
    if not isinstance(descriptor, dict):
        raise ValueError(f"{description} descriptor is missing")
    path = Path(str(descriptor.get("path", ""))).resolve()
    if not path.is_file():
        raise FileNotFoundError(f"{description} is missing: {path}")
    if (
        int(descriptor.get("bytes", -1)) != path.stat().st_size
        or str(descriptor.get("sha256", "")).casefold() != _sha256(path)
    ):
        raise ValueError(f"{description} hash or size differs: {path}")
    return path


def _require_number(value: object, description: str, *, positive: bool = True) -> float:
    if not isinstance(value, (int, float)) or not math.isfinite(float(value)):
        raise ValueError(f"{description} must be finite")
    result = float(value)
    if positive and result <= 0.0:
        raise ValueError(f"{description} must be positive")
    return result


def _load_policy(path: Path) -> dict[str, object]:
    policy = _read_json(path)
    if (
        policy.get("schema") != POLICY_SCHEMA
        or policy.get("status") != "deterministic-derivative-non-parity"
        or policy.get("phaseOrder")
        != ["retail-to-opennv-wipe", "side-by-side", "opennv-motion"]
    ):
        raise ValueError("Gallery comparison policy identity or phase order differs")
    phases = policy.get("phases")
    style = policy.get("style")
    encoding = policy.get("encoding")
    labels = policy.get("labels")
    provenance = policy.get("provenance")
    if not all(isinstance(value, dict) for value in (phases, style, encoding, labels, provenance)):
        raise ValueError("Gallery comparison policy sections are incomplete")
    _require_number(phases["retail-to-opennv-wipe"].get("durationSeconds"), "wipe duration")
    _require_number(phases["side-by-side"].get("durationSeconds"), "side-by-side duration")
    if (
        phases["retail-to-opennv-wipe"].get("direction") != "left-to-right"
        or phases["side-by-side"].get("retailSide") != "left"
        or phases["side-by-side"].get("openNvSide") != "right"
        or phases["opennv-motion"].get("durationSource")
        != "gallery-video-report.gallery.secondsPerSubject"
        or provenance.get("retailRecaptureRequired") is not False
        or provenance.get("parityClaimed") is not False
    ):
        raise ValueError("Gallery comparison policy semantics differ")
    font_candidates = style.get("fontCandidates")
    if not isinstance(font_candidates, list) or not font_candidates:
        raise ValueError("Gallery comparison policy has no font candidates")
    font = next((Path(str(value)) for value in font_candidates if Path(str(value)).is_file()), None)
    if font is None:
        raise FileNotFoundError("No configured gallery comparison font exists")
    policy["resolvedFont"] = str(font.resolve())
    for field in (
        "labelFontPixels",
        "subjectFontPixels",
        "marginPixels",
        "boxBorderPixels",
        "dividerPixels",
        "wipeLinePixels",
    ):
        _require_number(style.get(field), f"style.{field}")
    for field in ("constantRateFactor", "durationToleranceFrames"):
        _require_number(encoding.get(field), f"encoding.{field}", positive=False)
    return policy


def _probe(path: Path) -> dict[str, object]:
    command = [
        "ffprobe",
        "-v",
        "error",
        "-show_entries",
        "format=duration:stream=codec_type,codec_name,r_frame_rate,width,height",
        "-of",
        "json",
        str(path),
    ]
    result = subprocess.run(command, check=True, capture_output=True, text=True)
    probe = json.loads(result.stdout)
    videos = [row for row in probe.get("streams", []) if row.get("codec_type") == "video"]
    if len(videos) != 1:
        raise ValueError(f"Expected one video stream: {path}")
    video = videos[0]
    return {
        "durationSeconds": float(probe["format"]["duration"]),
        "codec": str(video["codec_name"]),
        "rate": str(video["r_frame_rate"]),
        "width": int(video["width"]),
        "height": int(video["height"]),
    }


def _filter_text(value: str) -> str:
    return (
        value.replace("\\", "\\\\")
        .replace(":", "\\:")
        .replace("'", "’")
        .replace("%", "\\%")
    )


def _filter_path(path: Path) -> str:
    return _filter_text(path.as_posix())


def _drawtext(
    font: Path,
    text: str,
    x: str,
    y: str,
    size: int,
    style: dict[str, object],
) -> str:
    return (
        "drawtext="
        f"fontfile='{_filter_path(font)}':"
        f"text='{_filter_text(text)}':"
        f"x={x}:y={y}:fontsize={size}:"
        f"fontcolor={style['textColor']}:"
        f"box=1:boxcolor={style['boxColor']}:boxborderw={int(style['boxBorderPixels'])}"
    )


def _normalized(label: str, width: int, height: int, fps: int, color: str) -> str:
    return (
        f"[{label}]scale={width}:{height}:force_original_aspect_ratio=decrease,"
        f"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:{color},"
        f"fps={fps},setsar=1"
    )


def _comparison_filter(
    width: int,
    height: int,
    fps: int,
    open_nv_seconds: float,
    subject: str,
    location: str,
    policy: dict[str, object],
) -> str:
    phases = policy["phases"]
    labels = policy["labels"]
    style = policy["style"]
    font = Path(str(policy["resolvedFont"]))
    wipe_seconds = float(phases["retail-to-opennv-wipe"]["durationSeconds"])
    side_seconds = float(phases["side-by-side"]["durationSeconds"])
    margin = int(style["marginPixels"])
    divider = int(style["dividerPixels"])
    wipe_line = int(style["wipeLinePixels"])
    label_size = int(style["labelFontPixels"])
    subject_size = int(style["subjectFontPixels"])
    half_width = (width - divider) // 2
    subject_text = f"{subject}  /  {location}"
    retail_label = _drawtext(font, str(labels["retail"]), str(margin), str(margin), label_size, style)
    open_nv_label = _drawtext(
        font,
        str(labels["openNv"]),
        f"w-text_w-{margin}",
        str(margin),
        label_size,
        style,
    )
    subject_label = _drawtext(
        font,
        subject_text,
        "(w-text_w)/2",
        f"h-text_h-{margin}",
        subject_size,
        style,
    )
    wipe_phase = _drawtext(
        font,
        str(labels["wipe"]),
        "(w-text_w)/2",
        str(margin),
        subject_size,
        style,
    )
    side_phase = _drawtext(
        font,
        str(labels["sideBySide"]),
        "(w-text_w)/2",
        str(margin),
        subject_size,
        style,
    )
    open_nv_only = _drawtext(
        font,
        str(labels["openNvOnly"]),
        str(margin),
        str(margin),
        label_size,
        style,
    )
    filters = [
        _normalized("0:v", width, height, fps, str(style["canvasColor"]))
        + ",split=2[rw][rs]",
        _normalized("1:v", width, height, fps, str(style["canvasColor"]))
        + ",split=3[ow][os][oo]",
        (
            f"[rw][ow]blend=all_expr='if(lte(X,W*T/{wipe_seconds}),B,A)':shortest=1,"
            f"trim=duration={wipe_seconds},setpts=PTS-STARTPTS[wb]"
        ),
        (
            f"color=c={style['dividerColor']}:s={wipe_line}x{height}:d={wipe_seconds}:r={fps}[wl];"
            f"[wb][wl]overlay=x='min(main_w-overlay_w,max(0,main_w*t/{wipe_seconds}))':y=0,"
            f"{retail_label},{open_nv_label},{wipe_phase},{subject_label},setsar=1[wipe]"
        ),
        (
            f"[rs]scale={half_width}:{height}:force_original_aspect_ratio=decrease,"
            f"pad={half_width}:{height}:(ow-iw)/2:(oh-ih)/2:{style['canvasColor']}[rh]"
        ),
        (
            f"[os]scale={half_width}:{height}:force_original_aspect_ratio=decrease,"
            f"pad={half_width}:{height}:(ow-iw)/2:(oh-ih)/2:{style['canvasColor']}[oh]"
        ),
        (
            f"color=c={style['dividerColor']}:s={divider}x{height}:d={side_seconds}:r={fps}[sd];"
            f"[rh][sd][oh]hstack=inputs=3,trim=duration={side_seconds},setpts=PTS-STARTPTS,"
            f"{retail_label},{open_nv_label},{side_phase},{subject_label},setsar=1[side]"
        ),
        (
            f"[oo]trim=duration={open_nv_seconds},setpts=PTS-STARTPTS,"
            f"{open_nv_only},{subject_label},setsar=1[ours]"
        ),
        "[wipe][side][ours]concat=n=3:v=1:a=0,format=yuv420p[outv]",
    ]
    return ";".join(filters)


def _encode_shot(
    retail_frame: Path,
    open_nv_segment: Path,
    output: Path,
    subject: str,
    location: str,
    policy: dict[str, object],
    media: dict[str, object],
    open_nv_seconds: float,
) -> dict[str, object]:
    encoding = policy["encoding"]
    width = int(media["width"])
    height = int(media["height"])
    rate_text = str(media["rate"])
    numerator, denominator = (int(value) for value in rate_text.split("/"))
    if denominator <= 0 or numerator % denominator:
        raise ValueError(f"Gallery segment frame rate is not an integer: {rate_text}")
    fps = numerator // denominator
    filter_graph = _comparison_filter(
        width,
        height,
        fps,
        open_nv_seconds,
        subject,
        location,
        policy,
    )
    command = [
        "ffmpeg",
        "-hide_banner",
        "-loglevel",
        "error",
        "-y",
        "-loop",
        "1",
        "-framerate",
        str(fps),
        "-i",
        str(retail_frame),
        "-i",
        str(open_nv_segment),
        "-filter_complex",
        filter_graph,
        "-map",
        "[outv]",
        "-an",
        "-r",
        str(fps),
        "-c:v",
        str(encoding["videoCodec"]),
        "-pix_fmt",
        str(encoding["pixelFormat"]),
        "-crf",
        str(encoding["constantRateFactor"]),
        "-preset",
        str(encoding["encoderPreset"]),
        str(output),
    ]
    subprocess.run(command, check=True)
    result = _probe(output)
    expected = (
        float(policy["phases"]["retail-to-opennv-wipe"]["durationSeconds"])
        + float(policy["phases"]["side-by-side"]["durationSeconds"])
        + open_nv_seconds
    )
    tolerance = float(encoding["durationToleranceFrames"]) / fps
    if abs(float(result["durationSeconds"]) - expected) > tolerance:
        raise ValueError(f"Comparison segment duration differs: {output}")
    return result


def build(
    gallery_manifest_path: Path,
    gallery_video_report_path: Path,
    policy_path: Path,
    output_root: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite comparison output: {output_root}")
    for command in ("ffmpeg", "ffprobe"):
        if shutil.which(command) is None:
            raise FileNotFoundError(f"Required media tool is unavailable: {command}")
    policy = _load_policy(policy_path)
    gallery = _read_json(gallery_manifest_path)
    video_report = _read_json(gallery_video_report_path)
    if (
        gallery.get("schema") != GALLERY_SCHEMA
        or gallery.get("status") != "compiled-owned-authored-gallery-retail-bound"
        or gallery.get("parityClaimed") is not False
        or video_report.get("schema") != VIDEO_REPORT_SCHEMA
        or video_report.get("status")
        != "captured-gallery-video-retail-bound-pending-parity"
        or video_report.get("capture", {}).get("parityClaimed") is not False
    ):
        raise ValueError("Gallery compile or video report identity differs")
    jobs = sorted(gallery.get("jobs", []), key=lambda row: int(row["ordinal"]))
    video_segments = {
        str(row["id"]): row for row in video_report.get("segments", [])
    }
    if (
        len(jobs) != int(gallery.get("shotCount", -1))
        or len(video_segments) != len(jobs)
        or set(video_segments) != {str(row["id"]) for row in jobs}
    ):
        raise ValueError("Gallery comparison job and segment identities differ")
    open_nv_seconds = _require_number(
        video_report.get("gallery", {}).get("secondsPerSubject"),
        "gallery video seconds per subject",
    )
    output_root.mkdir(parents=True)
    segment_root = output_root / "segments"
    segment_root.mkdir()
    rows = []
    expected_media: dict[str, object] | None = None
    for index, job in enumerate(jobs, start=1):
        if int(job["ordinal"]) != index:
            raise ValueError("Gallery job ordinals are not contiguous")
        shot_path = Path(str(job["shotContract"])).resolve()
        if _sha256(shot_path) != str(job["shotContractSha256"]).casefold():
            raise ValueError(f"Gallery shot contract hash differs: {shot_path}")
        shot = _read_json(shot_path)
        if shot.get("schema") != SHOT_SCHEMA or shot.get("id") != job.get("id"):
            raise ValueError(f"Gallery shot identity differs: {shot_path}")
        retail_evidence_path = _verified_file(
            shot.get("retailEvidence"), f"retail evidence for {job['id']}"
        )
        retail_evidence = _read_json(retail_evidence_path)
        if (
            retail_evidence.get("schema") != RETAIL_EVIDENCE_SCHEMA
            or retail_evidence.get("shot", {}).get("id") != job.get("id")
        ):
            raise ValueError(f"Retail evidence identity differs: {retail_evidence_path}")
        retail_frame = _verified_file(
            retail_evidence.get("retail", {})
            .get("presentation", {})
            .get("sourceFrame"),
            f"retail source frame for {job['id']}",
        )
        video_segment = video_segments[str(job["id"])]
        open_nv_segment = _verified_file(
            video_segment.get("segment"), f"OpenNV segment for {job['id']}"
        )
        source_media = _probe(open_nv_segment)
        media_identity = {
            "width": source_media["width"],
            "height": source_media["height"],
            "rate": source_media["rate"],
            "codec": source_media["codec"],
        }
        if expected_media is None:
            expected_media = media_identity
        elif media_identity != expected_media:
            raise ValueError("OpenNV gallery source segments do not share one media contract")
        segment_path = segment_root / (
            f"{index:03d}" + str(policy["encoding"]["segmentExtension"])
        )
        comparison_media = _encode_shot(
            retail_frame,
            open_nv_segment,
            segment_path,
            str(job["label"]),
            str(job["location"]),
            policy,
            source_media,
            open_nv_seconds,
        )
        rows.append(
            {
                "ordinal": index,
                "id": str(job["id"]),
                "label": str(job["label"]),
                "location": str(job["location"]),
                "retailFrame": _evidence(retail_frame),
                "retailEvidence": _evidence(retail_evidence_path),
                "openNvSourceSegment": _evidence(open_nv_segment),
                "comparisonSegment": _evidence(segment_path),
                "media": comparison_media,
            }
        )
    concat_path = segment_root / "concat.txt"
    concat_path.write_text(
        "".join(f"file '{Path(row['comparisonSegment']['path']).name}'\n" for row in rows),
        encoding="utf-8",
        newline="\n",
    )
    delivery_path = output_root / str(policy["encoding"]["deliveryFileName"])
    subprocess.run(
        [
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            str(concat_path),
            "-c",
            "copy",
            str(delivery_path),
        ],
        check=True,
    )
    delivery_media = _probe(delivery_path)
    expected_segment_seconds = (
        float(policy["phases"]["retail-to-opennv-wipe"]["durationSeconds"])
        + float(policy["phases"]["side-by-side"]["durationSeconds"])
        + open_nv_seconds
    )
    expected_seconds = len(rows) * expected_segment_seconds
    rate_text = str(delivery_media["rate"])
    numerator, denominator = (int(value) for value in rate_text.split("/"))
    fps = numerator / denominator
    tolerance = (
        len(rows)
        * float(policy["encoding"]["durationToleranceFrames"])
        / fps
    )
    if (
        abs(float(delivery_media["durationSeconds"]) - expected_seconds) > tolerance
        or delivery_media["codec"] != "h264"
    ):
        raise ValueError("Gallery comparison delivery failed duration or codec validation")
    report = {
        "schema": OUTPUT_SCHEMA,
        "status": "complete-retail-reference-versus-current-opennv-non-parity",
        "policy": _evidence(policy_path),
        "galleryManifest": _evidence(gallery_manifest_path),
        "galleryVideoReport": _evidence(gallery_video_report_path),
        "capture": {
            "retailRecaptured": False,
            "windowsAppControlUsed": False,
            "foregroundActivationUsed": False,
            "foregroundInputInjected": False,
            "parityClaimed": False,
        },
        "phaseOrder": list(policy["phaseOrder"]),
        "shotCount": len(rows),
        "secondsPerShot": expected_segment_seconds,
        "delivery": {
            "file": _evidence(delivery_path),
            "media": delivery_media,
            "expectedDurationSeconds": expected_seconds,
        },
        "shots": rows,
        "artifacts": [
            _evidence(policy_path),
            _evidence(gallery_manifest_path),
            _evidence(gallery_video_report_path),
            _evidence(concat_path),
            _evidence(delivery_path),
        ],
    }
    report_path = output_root / str(policy["encoding"]["reportFileName"])
    report_path.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    report["report"] = str(report_path.resolve())
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gallery-manifest", type=Path, required=True)
    parser.add_argument("--gallery-video-report", type=Path, required=True)
    parser.add_argument("--policy", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = build(
            args.gallery_manifest.resolve(),
            args.gallery_video_report.resolve(),
            args.policy.resolve(),
            args.output_root.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_GALLERY_COMPARISON_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_GALLERY_COMPARISON "
        + json.dumps(
            {
                "report": report["report"],
                "delivery": report["delivery"]["file"]["path"],
                "shotCount": report["shotCount"],
                "status": report["status"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
