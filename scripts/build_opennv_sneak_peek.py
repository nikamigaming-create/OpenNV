#!/usr/bin/env python3
"""Build an auditable, asset-free OpenNV development sneak-peek reel."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import subprocess
from pathlib import Path
from typing import Any


SHOT_SCHEMA = "opennv-sneak-peek-shots/v1"
POLICY_SCHEMA = "opennv-sneak-peek-video-policy/v1"
REPORT_SCHEMA = "opennv-sneak-peek-video-report/v1"


def read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return value


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run(arguments: list[str]) -> None:
    subprocess.run(arguments, check=True)


def probe(path: Path) -> dict[str, Any]:
    result = subprocess.run(
        [
            "ffprobe",
            "-v",
            "error",
            "-show_entries",
            "format=duration,size:stream=index,codec_type,codec_name,width,height,r_frame_rate,sample_rate,channels",
            "-of",
            "json",
            str(path),
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    value = json.loads(result.stdout)
    if not isinstance(value, dict):
        raise ValueError(f"ffprobe returned malformed JSON: {path}")
    return value


def required_string(source: dict[str, Any], name: str) -> str:
    value = source.get(name)
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"Required string is empty: {name}")
    return value


def required_number(source: dict[str, Any], name: str) -> float:
    value = source.get(name)
    if not isinstance(value, (int, float)) or not math.isfinite(float(value)):
        raise ValueError(f"Required number is invalid: {name}")
    return float(value)


def filter_path(path: Path) -> str:
    return str(path.resolve()).replace("\\", "/").replace(":", "\\:").replace("'", "\\'")


def filter_text(value: str) -> str:
    return (
        value.replace("\\", "\\\\")
        .replace("'", "\\'")
        .replace(":", "\\:")
        .replace("%", "\\%")
    )


def video_streams(metadata: dict[str, Any], kind: str) -> list[dict[str, Any]]:
    streams = metadata.get("streams")
    if not isinstance(streams, list):
        return []
    return [row for row in streams if isinstance(row, dict) and row.get("codec_type") == kind]


def build_segment(
    shot: dict[str, Any],
    ordinal: int,
    policy: dict[str, Any],
    font: Path,
    output_root: Path,
) -> dict[str, Any]:
    kind = required_string(shot, "kind")
    if kind not in {"title", "image", "video"}:
        raise ValueError(f"Unsupported sneak-peek shot kind: {kind}")
    shot_id = required_string(shot, "id")
    label = required_string(shot, "label")
    sublabel = required_string(shot, "sublabel")
    duration = required_number(shot, "durationSeconds")
    if duration <= 0.0 or duration > required_number(policy, "maximumShotSeconds"):
        raise ValueError(f"Shot duration is outside policy: {shot_id}={duration}")

    landscape = policy["landscape"]
    width = int(required_number(landscape, "width"))
    height = int(required_number(landscape, "height"))
    fps = int(required_number(landscape, "fps"))
    fade = required_number(policy, "fadeSeconds")
    if fade <= 0.0 or fade * 2.0 >= duration:
        raise ValueError(f"Fade policy does not fit shot: {shot_id}")
    labels_root = output_root / "labels"
    labels_root.mkdir(parents=True, exist_ok=True)
    title_path = labels_root / f"{ordinal:02d}-{shot_id}-title.txt"
    subtitle_path = labels_root / f"{ordinal:02d}-{shot_id}-subtitle.txt"
    title_path.write_text(label + "\n", encoding="utf-8")
    subtitle_path.write_text(sublabel + "\n", encoding="utf-8")

    segment_root = output_root / "segments"
    segment_root.mkdir(parents=True, exist_ok=True)
    segment = segment_root / f"{ordinal:02d}-{shot_id}.mp4"
    source_report: dict[str, Any] | None = None
    arguments = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y"]
    if kind == "title":
        color = required_string(shot, "backgroundColor")
        arguments += ["-f", "lavfi", "-i", f"color=c={color}:s={width}x{height}:r={fps}:d={duration}"]
    else:
        source_path = Path(required_string(shot, "path")).resolve()
        if not source_path.is_file():
            raise FileNotFoundError(f"Sneak-peek source is missing: {source_path}")
        expected_sha256 = required_string(shot, "sha256").lower()
        actual_sha256 = sha256(source_path)
        if actual_sha256 != expected_sha256:
            raise ValueError(
                f"Sneak-peek source hash changed: {shot_id} expected={expected_sha256} actual={actual_sha256}"
            )
        metadata = probe(source_path)
        if len(video_streams(metadata, "video")) != 1:
            raise ValueError(f"Sneak-peek source needs exactly one video stream: {source_path}")
        source_report = {
            "path": str(source_path),
            "bytes": source_path.stat().st_size,
            "sha256": actual_sha256,
            "probe": metadata,
        }
        if kind == "image":
            arguments += ["-loop", "1", "-framerate", str(fps), "-t", str(duration), "-i", str(source_path)]
        else:
            start = required_number(shot, "startSeconds")
            if start < 0.0:
                raise ValueError(f"Video start is negative: {shot_id}")
            arguments += ["-ss", str(start), "-t", str(duration), "-i", str(source_path)]

    include_source_audio = bool(shot.get("includeSourceAudio", False))
    source_has_audio = source_report is not None and bool(video_streams(source_report["probe"], "audio"))
    use_source_audio = kind == "video" and include_source_audio and source_has_audio
    if include_source_audio and not use_source_audio:
        raise ValueError(f"Requested source audio is unavailable: {shot_id}")
    if not use_source_audio:
        arguments += [
            "-f",
            "lavfi",
            "-t",
            str(duration),
            "-i",
            "anullsrc=r=48000:cl=stereo",
        ]

    font_value = filter_path(font)
    title_value = filter_text(label)
    subtitle_value = filter_text(sublabel)
    if kind == "title":
        label_filters = (
            f"drawtext=fontfile='{font_value}':text='{title_value}':"
            f"fontsize={int(required_number(policy, 'titleCardFontSize'))}:fontcolor=white:"
            "x=(w-text_w)/2:y=(h-text_h)/2-42,"
            f"drawtext=fontfile='{font_value}':text='{subtitle_value}':"
            f"fontsize={int(required_number(policy, 'subtitleFontSize'))}:fontcolor=0x66d9ef:"
            "x=(w-text_w)/2:y=(h-text_h)/2+46"
        )
    else:
        margin = int(required_number(policy, "labelMarginPixels"))
        label_filters = (
            f"drawtext=fontfile='{font_value}':text='{title_value}':"
            f"fontsize={int(required_number(policy, 'labelFontSize'))}:fontcolor=white:"
            f"box=1:boxcolor=black@0.68:boxborderw=14:x={margin}:y={margin},"
            f"drawtext=fontfile='{font_value}':text='{subtitle_value}':"
            f"fontsize={int(required_number(policy, 'subtitleFontSize'))}:fontcolor=0x66d9ef:"
            f"box=1:boxcolor=black@0.68:boxborderw=12:x={margin}:y={margin + 66}"
        )
    fade_out = duration - fade
    video_filter = (
        f"[0:v]scale={width}:{height}:force_original_aspect_ratio=decrease,"
        f"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,fps={fps},"
        f"{label_filters},fade=t=in:st=0:d={fade},fade=t=out:st={fade_out}:d={fade},"
        "format=yuv420p[v]"
    )
    audio_input = "[0:a]" if use_source_audio else "[1:a]"
    audio_filter = (
        f"{audio_input}atrim=duration={duration},asetpts=PTS-STARTPTS,"
        f"aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,"
        f"afade=t=in:st=0:d={fade},afade=t=out:st={fade_out}:d={fade}[a]"
    )
    arguments += [
        "-filter_complex",
        video_filter + ";" + audio_filter,
        "-map",
        "[v]",
        "-map",
        "[a]",
        "-r",
        str(fps),
        "-c:v",
        required_string(policy, "videoCodec"),
        "-preset",
        required_string(policy, "preset"),
        "-crf",
        str(int(required_number(policy, "crf"))),
        "-pix_fmt",
        required_string(policy, "pixelFormat"),
        "-c:a",
        required_string(policy, "audioCodec"),
        "-b:a",
        required_string(policy, "audioBitrate"),
        "-ar",
        "48000",
        "-ac",
        "2",
        "-movflags",
        "+faststart",
        "-t",
        str(duration),
        str(segment),
    ]
    run(arguments)
    segment_probe = probe(segment)
    return {
        "ordinal": ordinal,
        "id": shot_id,
        "kind": kind,
        "label": label,
        "sublabel": sublabel,
        "durationSeconds": duration,
        "source": source_report,
        "segment": {
            "path": str(segment.resolve()),
            "bytes": segment.stat().st_size,
            "sha256": sha256(segment),
            "probe": segment_probe,
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--shots", required=True, type=Path)
    parser.add_argument("--policy", required=True, type=Path)
    parser.add_argument("--font", required=True, type=Path)
    parser.add_argument("--output-root", required=True, type=Path)
    args = parser.parse_args()

    if shutil.which("ffmpeg") is None or shutil.which("ffprobe") is None:
        raise FileNotFoundError("ffmpeg and ffprobe are required")
    shots_path = args.shots.resolve()
    policy_path = args.policy.resolve()
    font = args.font.resolve()
    if not shots_path.is_file() or not policy_path.is_file() or not font.is_file():
        raise FileNotFoundError("Shots, policy, or font input is missing")
    output_root = args.output_root.resolve()
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite output root: {output_root}")
    output_root.mkdir(parents=True)

    shots_document = read_json(shots_path)
    policy = read_json(policy_path)
    if shots_document.get("schema") != SHOT_SCHEMA:
        raise ValueError("Sneak-peek shot manifest schema is unsupported")
    if policy.get("schema") != POLICY_SCHEMA:
        raise ValueError("Sneak-peek policy schema is unsupported")
    rows = shots_document.get("shots")
    if not isinstance(rows, list) or not rows:
        raise ValueError("Sneak-peek shot manifest is empty")
    ids = [required_string(row, "id") for row in rows if isinstance(row, dict)]
    if len(ids) != len(rows) or len(ids) != len(set(ids)):
        raise ValueError("Sneak-peek shot IDs are malformed or duplicated")

    segment_reports = [
        build_segment(row, ordinal, policy, font, output_root)
        for ordinal, row in enumerate(rows, start=1)
    ]
    concat_path = output_root / "segments.txt"
    concat_path.write_text(
        "".join(
            "file '" + report["segment"]["path"].replace("'", "'\\''") + "'\n"
            for report in segment_reports
        ),
        encoding="utf-8",
    )
    landscape_name = required_string(policy, "landscapeFileName")
    landscape_output = output_root / landscape_name
    run(
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
            "-r",
            str(int(required_number(policy["landscape"], "fps"))),
            "-c:v",
            required_string(policy, "videoCodec"),
            "-preset",
            required_string(policy, "preset"),
            "-crf",
            str(int(required_number(policy, "crf"))),
            "-pix_fmt",
            required_string(policy, "pixelFormat"),
            "-c:a",
            required_string(policy, "audioCodec"),
            "-b:a",
            required_string(policy, "audioBitrate"),
            "-ar",
            "48000",
            "-ac",
            "2",
            "-movflags",
            "+faststart",
            str(landscape_output),
        ]
    )

    mobile = policy["mobile"]
    mobile_width = int(required_number(mobile, "width"))
    mobile_height = int(required_number(mobile, "height"))
    mobile_output = output_root / required_string(policy, "mobileFileName")
    mobile_filter = (
        "[0:v]split=2[background-source][foreground-source];"
        f"[background-source]scale={mobile_width}:{mobile_height}:force_original_aspect_ratio=increase,"
        f"crop={mobile_width}:{mobile_height},gblur=sigma=36[bg];"
        f"[foreground-source]scale={mobile_width}:{mobile_height}:force_original_aspect_ratio=decrease[fg];"
        "[bg][fg]overlay=(W-w)/2:(H-h)/2,format=yuv420p[v]"
    )
    run(
        [
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-i",
            str(landscape_output),
            "-filter_complex",
            mobile_filter,
            "-map",
            "[v]",
            "-map",
            "0:a:0",
            "-c:v",
            required_string(policy, "videoCodec"),
            "-preset",
            required_string(policy, "preset"),
            "-crf",
            str(int(required_number(policy, "crf"))),
            "-c:a",
            "copy",
            "-movflags",
            "+faststart",
            str(mobile_output),
        ]
    )

    report = {
        "schema": REPORT_SCHEMA,
        "status": "complete-current-development-sneak-peek-non-parity",
        "sourceCommit": required_string(shots_document, "sourceCommit"),
        "shotsManifest": {
            "path": str(shots_path),
            "sha256": sha256(shots_path),
        },
        "policy": {"path": str(policy_path), "sha256": sha256(policy_path)},
        "font": {"path": str(font), "sha256": sha256(font)},
        "shotCount": len(segment_reports),
        "durationSeconds": sum(report["durationSeconds"] for report in segment_reports),
        "shots": segment_reports,
        "outputs": {
            "landscape": {
                "path": str(landscape_output),
                "bytes": landscape_output.stat().st_size,
                "sha256": sha256(landscape_output),
                "probe": probe(landscape_output),
            },
            "mobile": {
                "path": str(mobile_output),
                "bytes": mobile_output.stat().st_size,
                "sha256": sha256(mobile_output),
                "probe": probe(mobile_output),
            },
        },
        "claims": {
            "retailParity": False,
            "fullCampaign": False,
            "windowsAppControlUsed": False,
            "foregroundInputInjected": False,
            "ownedMediaCommitted": False,
        },
    }
    report_path = output_root / required_string(policy, "reportFileName")
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(
        "OPENNV_SNEAK_PEEK_COMPLETE "
        f"shots={len(segment_reports)} landscape={landscape_output} mobile={mobile_output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
