#!/usr/bin/env python3
"""Build a retail-versus-Godot actor appearance differential."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageStat


UNITS_TO_METERS = 0.0142875


def normalize_form(value: object) -> str:
    text = str(value).lower().removeprefix("0x")
    return text.zfill(8)


def image_metrics(path: Path) -> dict[str, object]:
    with Image.open(path) as source:
        image = source.convert("RGB")
        gray = image.convert("L")
        stats = ImageStat.Stat(gray)
        return {
            "width": image.width,
            "height": image.height,
            "meanLuminance": stats.mean[0] / 255.0,
            "luminanceDeviation": stats.stddev[0] / 255.0,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        }


def difference_metrics(retail_path: Path, godot_path: Path) -> dict[str, float]:
    with Image.open(retail_path) as retail_source, Image.open(godot_path) as godot_source:
        retail = retail_source.convert("RGB")
        godot = godot_source.convert("RGB")
        if retail.size != godot.size:
            raise ValueError(f"Frame sizes differ: retail={retail.size} godot={godot.size}")
        difference = ImageChops.difference(retail, godot)
        histogram = difference.histogram()
        samples = retail.width * retail.height * 3
        absolute = sum((index % 256) * count for index, count in enumerate(histogram))
        squared = sum(((index % 256) ** 2) * count for index, count in enumerate(histogram))
        changed = sum(
            1
            for pixel in difference.get_flattened_data()
            if max(pixel) > 8
        )
        return {
            "meanAbsoluteError": absolute / samples / 255.0,
            "rootMeanSquareError": math.sqrt(squared / samples) / 255.0,
            "changedPixelFractionAtTolerance8": changed / (retail.width * retail.height),
        }


def json_lines(path: Path) -> list[dict[str, object]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line]


def font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for candidate in ("C:/Windows/Fonts/segoeui.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            pass
    return ImageFont.load_default()


def contact_sheet(
    retail_path: Path,
    godot_path: Path,
    output_path: Path,
    shot_kind: str,
    metrics: dict[str, float],
) -> None:
    with Image.open(retail_path) as retail_source, Image.open(godot_path) as godot_source:
        retail = retail_source.convert("RGB")
        godot = godot_source.convert("RGB")
        if retail.size != godot.size:
            raise ValueError("Contact-sheet inputs must have identical dimensions.")
        header = 104
        canvas = Image.new("RGB", (retail.width * 2, retail.height + header), (20, 22, 26))
        canvas.paste(retail, (0, header))
        canvas.paste(godot, (retail.width, header))
        draw = ImageDraw.Draw(canvas)
        title_font = font(32)
        detail_font = font(22)
        draw.text((24, 13), "RETAIL FNV — PASS", fill=(245, 245, 245), font=title_font)
        draw.text((retail.width + 24, 13), "OPENNV GODOT — CURRENT FAIL", fill=(255, 118, 98), font=title_font)
        detail = (
            f"{shot_kind}  |  MAE {metrics['meanAbsoluteError']:.3f}  |  "
            f"changed pixels {metrics['changedPixelFractionAtTolerance8']:.1%}"
        )
        draw.text((24, 61), detail, fill=(190, 198, 208), font=detail_font)
        canvas.save(output_path)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--retail-summary", required=True, type=Path)
    parser.add_argument("--godot-report", required=True, type=Path)
    parser.add_argument("--output-root", required=True, type=Path)
    args = parser.parse_args()
    if args.output_root.exists():
        raise SystemExit(f"Refusing to overwrite actor differential: {args.output_root}")
    args.output_root.mkdir(parents=True)

    retail_summary = json.loads(args.retail_summary.read_text(encoding="utf-8"))
    godot_report = json.loads(args.godot_report.read_text(encoding="utf-8"))
    retail = retail_summary["retailPortraits"]
    retail_target = retail["target"]
    godot_actor = godot_report["actorReferences"][0]
    identity_pairs = {
        "referenceForm": (retail_target["referenceForm"], godot_actor["formId"]),
        "baseForm": (retail_target["baseForm"], godot_actor["baseFormId"]),
        "raceForm": (retail_target["raceForm"], godot_actor["raceFormId"]),
        "hairForm": (retail_target["hairForm"], godot_actor["hairFormId"]),
        "eyesForm": (retail_target["eyesForm"], godot_actor["eyesFormId"]),
    }
    identities = [
        {
            "field": field,
            "retail": normalize_form(values[0]),
            "godot": normalize_form(values[1]),
            "status": "pass" if normalize_form(values[0]) == normalize_form(values[1]) else "fail",
        }
        for field, values in identity_pairs.items()
    ]

    godot_shots = {shot["shotKind"]: shot for shot in godot_report["actorShots"]}
    comparisons = []
    for retail_shot in retail["shots"]:
        shot_kind = retail_shot["cameraShotKind"]
        group = retail_shot["groups"][0]
        retail_frame = Path(group["screenshots"][0])
        retail_events = json_lines(Path(group["output"]))
        retail_camera = next(event for event in retail_events if event["event"] == "portrait-camera-set")
        godot_shot = godot_shots[shot_kind]
        godot_frame = Path(godot_shot["file"])
        retail_metrics = image_metrics(retail_frame)
        godot_metrics = image_metrics(godot_frame)
        difference = difference_metrics(retail_frame, godot_frame)
        camera_error = abs(
            float(retail_camera["cameraDistance"]) * UNITS_TO_METERS
            - float(godot_shot["distanceMeters"])
        )
        objective_pass = (
            difference["meanAbsoluteError"] <= 0.05
            and difference["changedPixelFractionAtTolerance8"] <= 0.25
            and abs(
                float(retail_metrics["meanLuminance"])
                - float(godot_metrics["meanLuminance"])
            ) <= 0.03
            and camera_error <= 0.01
        )
        sheet = args.output_root / f"trudy-{shot_kind}-retail-vs-godot.png"
        contact_sheet(retail_frame, godot_frame, sheet, shot_kind, difference)
        comparisons.append(
            {
                "shotKind": shot_kind,
                "status": "pass" if objective_pass else "fail",
                "retailFrame": str(retail_frame.resolve()),
                "godotFrame": str(godot_frame.resolve()),
                "contactSheet": str(sheet.resolve()),
                "retailCamera": retail_camera,
                "godotCamera": godot_shot,
                "cameraDistanceErrorMeters": camera_error,
                "retailFrameMetrics": retail_metrics,
                "godotFrameMetrics": godot_metrics,
                "differenceMetrics": difference,
            }
        )

    identity_pass = all(row["status"] == "pass" for row in identities)
    pixel_pass = all(row["status"] == "pass" for row in comparisons)
    report = {
        "schema": "opennv-retail-godot-actor-differential/v1",
        "status": "pass" if identity_pass and pixel_pass else "fail",
        "target": "trudy",
        "identityStatus": "pass" if identity_pass else "fail",
        "renderingStatus": "pass" if pixel_pass else "fail",
        "humanVisualVerdictRequired": True,
        "retailCaptureRanBeforeGodot": True,
        "capturesRanConcurrently": False,
        "identities": identities,
        "godotOnlyIdentities": {
            "outfitForm": normalize_form(godot_actor["outfitFormId"]),
            "headPartForms": [normalize_form(value) for value in godot_actor["headPartFormIds"]],
        },
        "comparisons": comparisons,
    }
    report_path = args.output_root / "trudy-retail-vs-godot-report.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": report["status"], "report": str(report_path.resolve())}))


if __name__ == "__main__":
    main()
