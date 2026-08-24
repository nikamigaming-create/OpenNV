#!/usr/bin/env python3
"""Build a retail-versus-Godot actor appearance differential."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageStat

from runtime_configuration import RuntimeConfiguration, load_runtime_configuration


FORM_ID_HEX_CHARACTERS = 8
QUATERNION_DIAGONAL_COEFFICIENT = 0.25
BYTE_CHANNEL_MAXIMUM = 255.0
BYTE_VALUE_COUNT = 256


def normalize_form(value: object) -> str:
    text = str(value).lower().removeprefix("0x")
    return text.zfill(FORM_ID_HEX_CHARACTERS)


def vector_error(left: object, right: object) -> float:
    left_values = [float(value) for value in left]
    right_values = [float(value) for value in right]
    if len(left_values) != 3 or len(right_values) != 3:
        raise ValueError("State vectors must contain three values.")
    return math.sqrt(sum((a - b) ** 2 for a, b in zip(left_values, right_values)))


def angle_error(left: float, right: float) -> float:
    return abs((left - right + math.pi) % (2.0 * math.pi) - math.pi)


def matrix_multiply(left: list[list[float]], right: list[list[float]]) -> list[list[float]]:
    return [
        [sum(left[row][axis] * right[axis][column] for axis in range(3)) for column in range(3)]
        for row in range(3)
    ]


def quaternion_from_matrix(matrix: list[list[float]]) -> list[float]:
    trace = matrix[0][0] + matrix[1][1] + matrix[2][2]
    if trace > 0.0:
        scale = math.sqrt(trace + 1.0) * 2.0
        value = [
            (matrix[2][1] - matrix[1][2]) / scale,
            (matrix[0][2] - matrix[2][0]) / scale,
            (matrix[1][0] - matrix[0][1]) / scale,
            QUATERNION_DIAGONAL_COEFFICIENT * scale,
        ]
    else:
        axis = max(range(3), key=lambda index: matrix[index][index])
        next_axis = (axis + 1) % 3
        last_axis = (axis + 2) % 3
        scale = math.sqrt(
            1.0
            + matrix[axis][axis]
            - matrix[next_axis][next_axis]
            - matrix[last_axis][last_axis]
        ) * 2.0
        value = [0.0, 0.0, 0.0, 0.0]
        value[axis] = QUATERNION_DIAGONAL_COEFFICIENT * scale
        value[3] = (matrix[last_axis][next_axis] - matrix[next_axis][last_axis]) / scale
        value[next_axis] = (matrix[next_axis][axis] + matrix[axis][next_axis]) / scale
        value[last_axis] = (matrix[last_axis][axis] + matrix[axis][last_axis]) / scale
    length = math.sqrt(sum(component * component for component in value))
    return [component / length for component in value]


def quaternion_error(left: object, right: object) -> float:
    first = [float(value) for value in left]
    second = [float(value) for value in right]
    if len(first) != 4 or len(second) != 4:
        raise ValueError("Pose quaternions must contain four values.")
    first_length = math.sqrt(sum(value * value for value in first))
    second_length = math.sqrt(sum(value * value for value in second))
    dot = abs(sum(a * b for a, b in zip(first, second)) / (first_length * second_length))
    return 2.0 * math.acos(min(1.0, dot))


def retail_bone_in_godot(transform: dict[str, object]) -> tuple[list[float], list[float]]:
    values = [float(value) for value in transform["localRotation"]]
    if len(values) != 3 * 3:
        raise ValueError("Retail bone rotation must contain nine values.")
    game = [values[index : index + 3] for index in range(0, 3 * 3, 3)]
    conversion = [[1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, -1.0, 0.0]]
    conversion_inverse = [list(row) for row in zip(*conversion)]
    rotation = matrix_multiply(conversion, matrix_multiply(game, conversion_inverse))
    translation = [float(value) for value in transform["localTranslation"]]
    if len(translation) != 3:
        raise ValueError("Retail bone translation must contain three values.")
    return [translation[0], translation[2], -translation[1]], quaternion_from_matrix(rotation)


def game_position_to_godot(
    position: object,
    origin: object,
    units_to_meters: float,
) -> list[float]:
    values = [float(value) for value in position]
    anchor = [float(value) for value in origin]
    if len(values) != 3 or len(anchor) != 3:
        raise ValueError("Game/Godot position conversion requires three values.")
    return [
        (values[0] - anchor[0]) * units_to_meters,
        (values[2] - anchor[2]) * units_to_meters,
        -(values[1] - anchor[1]) * units_to_meters,
    ]


def bone_pose_metrics(
    retail_rows: object,
    godot_rows: object,
    origin: object,
    units_to_meters: float,
    configuration: RuntimeConfiguration,
) -> dict[str, object]:
    parity = configuration.actor_parity
    actor_state = configuration.document["retailActorState"]
    excluded_pose_nodes = set(actor_state["excludedPoseNodes"])
    retail_bones = {
        bone["name"]: bone
        for bone in retail_rows
        if bone["name"] not in excluded_pose_nodes
    }
    godot_bones = {bone["name"]: bone for bone in godot_rows}
    names_match = (
        retail_bones.keys() == godot_bones.keys()
        and len(retail_bones) >= int(actor_state["minimumPoseBones"])
    )
    translation_errors = []
    rotation_errors = []
    rows = []
    if names_match:
        for name, retail_bone in retail_bones.items():
            transform = retail_bone["transform"]
            translation = game_position_to_godot(
                transform["worldTranslation"], origin, units_to_meters
            )
            _, rotation = retail_bone_in_godot(
                {
                    "localRotation": transform["worldRotation"],
                    "localTranslation": [0.0, 0.0, 0.0],
                }
            )
            translation_error = vector_error(
                translation, godot_bones[name]["worldPosition"]
            )
            rotation_error = quaternion_error(
                rotation, godot_bones[name]["worldRotationQuaternion"]
            )
            translation_errors.append(translation_error)
            rotation_errors.append(rotation_error)
            rows.append(
                {
                    "name": name,
                    "translationErrorMeters": translation_error,
                    "rotationErrorRadians": rotation_error,
                }
            )
    maximum_translation = max(translation_errors) if translation_errors else math.inf
    maximum_rotation = max(rotation_errors) if rotation_errors else math.inf
    return {
        "status": "pass"
        if (
            maximum_translation <= parity.pose_translation_tolerance_meters
            and maximum_rotation <= parity.pose_rotation_tolerance_radians
        )
        else "fail",
        "bones": len(retail_bones),
        "maximumTranslationErrorMeters": maximum_translation,
        "maximumRotationErrorRadians": maximum_rotation,
        "worstBones": sorted(
            rows,
            key=lambda row: max(
                row["translationErrorMeters"] / parity.pose_translation_tolerance_meters,
                row["rotationErrorRadians"] / parity.pose_rotation_tolerance_radians,
            ),
            reverse=True,
        )[:parity.maximum_reported_worst_bones],
    }


def shot_state_metrics(
    retail_state: dict[str, object],
    godot_shot: dict[str, object],
    configuration: RuntimeConfiguration,
) -> dict[str, object]:
    parity = configuration.actor_parity
    actor_state = configuration.document["retailActorState"]
    retail_camera = retail_state["camera"]
    retail_pose = retail_state["pose"]
    idle = next(
        sequence
        for sequence in retail_pose["activeSequences"]
        if str(sequence["file"]).replace("/", "\\").lower().endswith(
            r"characters\_male\locomotion\mtidle.kf"
        )
    )
    placement_error = vector_error(
        retail_state["referenceTransform"]["position"],
        godot_shot["referencePositionGameUnits"],
    )
    yaw_error = angle_error(
        float(retail_state["referenceTransform"]["rotation"][2]),
        float(godot_shot["referenceYawRadians"]),
    )
    godot_yaw_error = angle_error(
        -float(retail_state["referenceTransform"]["rotation"][2]),
        float(godot_shot["referenceGodotYawRadians"]),
    )
    camera_position_error = vector_error(
        retail_camera["position"],
        godot_shot["cameraPositionGameUnits"],
    )
    camera_aim_error = vector_error(retail_camera["aim"], godot_shot["cameraAimGameUnits"])
    camera_distance_error = abs(
        float(retail_camera["distance"]) * configuration.world_units_to_meters
        - float(godot_shot["distanceMeters"])
    )
    fov_error = abs(
        float(retail_camera["projection"]["fovYDegrees"])
        - float(godot_shot["verticalFovDegrees"])
    )
    phase_error = abs(
        float(idle["lastScaled"]) - float(godot_shot["appliedAnimationPhaseSeconds"])
    )
    origin = godot_shot["cellOriginGameUnits"]
    units_to_meters = float(godot_shot["unitsToMeters"])
    if units_to_meters != configuration.world_units_to_meters:
        raise ValueError("Godot actor shot was rendered with another world-unit configuration")
    target_pose = bone_pose_metrics(
        retail_pose["bones"],
        godot_shot["poseBones"],
        origin,
        units_to_meters,
        configuration,
    )
    retail_contexts = {
        normalize_form(actor["referenceForm"]): actor for actor in retail_state["contextActors"]
    }
    godot_contexts = {
        normalize_form(actor["referenceFormId"]): actor for actor in godot_shot["contextActors"]
    }
    context_status = "pass"
    context_metrics = []
    if retail_contexts.keys() != godot_contexts.keys() or not retail_contexts:
        context_status = "fail"
    else:
        for reference, retail_context in retail_contexts.items():
            godot_context = godot_contexts[reference]
            sequence = next(
                row
                for row in retail_context["activeSequences"]
                if float(row["weight"]) >= float(actor_state["minimumContextSequenceWeight"])
            )
            pose = bone_pose_metrics(
                retail_context["bones"],
                godot_context["poseBones"],
                origin,
                units_to_meters,
                configuration,
            )
            placement = vector_error(
                retail_context["position"], godot_context["positionGameUnits"]
            )
            yaw = angle_error(
                -float(retail_context["rotation"][2]),
                float(godot_context["godotYawRadians"]),
            )
            phase = abs(
                float(sequence["lastScaled"])
                - float(godot_context["appliedAnimationPhaseSeconds"])
            )
            actor_passed = (
                normalize_form(retail_context["baseForm"])
                == normalize_form(godot_context["baseFormId"])
                and placement <= parity.placement_tolerance_game_units
                and yaw <= parity.yaw_tolerance_radians
                and phase <= parity.animation_phase_tolerance_seconds
                and pose["status"] == "pass"
            )
            if not actor_passed:
                context_status = "fail"
            context_metrics.append(
                {
                    "referenceForm": reference,
                    "status": "pass" if actor_passed else "fail",
                    "placementErrorGameUnits": placement,
                    "godotYawErrorRadians": yaw,
                    "animationPhaseErrorSeconds": phase,
                    "pose": pose,
                }
            )
    passed = (
        bool(godot_shot["retailStateApplied"])
        and placement_error <= parity.placement_tolerance_game_units
        and yaw_error <= parity.yaw_tolerance_radians
        and godot_yaw_error <= parity.yaw_tolerance_radians
        and camera_position_error <= parity.camera_position_tolerance_game_units
        and camera_aim_error <= parity.camera_aim_tolerance_game_units
        and camera_distance_error <= parity.camera_distance_tolerance_meters
        and fov_error <= parity.vertical_fov_tolerance_degrees
        and phase_error <= parity.animation_phase_tolerance_seconds
        and target_pose["status"] == "pass"
        and context_status == "pass"
    )
    return {
        "status": "pass" if passed else "fail",
        "placementErrorGameUnits": placement_error,
        "yawErrorRadians": yaw_error,
        "godotYawErrorRadians": godot_yaw_error,
        "cameraPositionErrorGameUnits": camera_position_error,
        "cameraAimErrorGameUnits": camera_aim_error,
        "cameraDistanceErrorMeters": camera_distance_error,
        "verticalFovErrorDegrees": fov_error,
        "animationPhaseErrorSeconds": phase_error,
        "targetPose": target_pose,
        "contextActorsStatus": context_status,
        "contextActors": context_metrics,
    }


def image_metrics(path: Path) -> dict[str, object]:
    with Image.open(path) as source:
        image = source.convert("RGB")
        gray = image.convert("L")
        stats = ImageStat.Stat(gray)
        return {
            "width": image.width,
            "height": image.height,
            "meanLuminance": stats.mean[0] / BYTE_CHANNEL_MAXIMUM,
            "luminanceDeviation": stats.stddev[0] / BYTE_CHANNEL_MAXIMUM,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        }


def difference_metrics(
    retail_path: Path,
    godot_path: Path,
    configuration: RuntimeConfiguration,
) -> dict[str, float]:
    tolerance = configuration.actor_parity.changed_pixel_channel_tolerance
    with Image.open(retail_path) as retail_source, Image.open(godot_path) as godot_source:
        retail = retail_source.convert("RGB")
        godot = godot_source.convert("RGB")
        if retail.size != godot.size:
            raise ValueError(f"Frame sizes differ: retail={retail.size} godot={godot.size}")
        difference = ImageChops.difference(retail, godot)
        histogram = difference.histogram()
        samples = retail.width * retail.height * 3
        absolute = sum((index % BYTE_VALUE_COUNT) * count for index, count in enumerate(histogram))
        squared = sum(
            ((index % BYTE_VALUE_COUNT) ** 2) * count
            for index, count in enumerate(histogram)
        )
        changed = sum(
            1
            for pixel in difference.get_flattened_data()
            if max(pixel) > tolerance
        )
        return {
            "meanAbsoluteError": absolute / samples / BYTE_CHANNEL_MAXIMUM,
            "rootMeanSquareError": math.sqrt(squared / samples) / BYTE_CHANNEL_MAXIMUM,
            "changedPixelChannelTolerance": tolerance,
            "changedPixelFraction": changed / (retail.width * retail.height),
        }


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
    configuration: RuntimeConfiguration,
) -> None:
    sheet = configuration.actor_parity.contact_sheet
    with Image.open(retail_path) as retail_source, Image.open(godot_path) as godot_source:
        retail = retail_source.convert("RGB")
        godot = godot_source.convert("RGB")
        if retail.size != godot.size:
            raise ValueError("Contact-sheet inputs must have identical dimensions.")
        header = sheet.header_pixels
        canvas = Image.new(
            "RGB",
            (retail.width * 2, retail.height + header),
            sheet.background_rgb,
        )
        canvas.paste(retail, (0, header))
        canvas.paste(godot, (retail.width, header))
        draw = ImageDraw.Draw(canvas)
        title_font = font(sheet.title_font_pixels)
        detail_font = font(sheet.detail_font_pixels)
        draw.text(
            (sheet.text_margin_x_pixels, sheet.title_y_pixels),
            "RETAIL FNV — PASS",
            fill=sheet.retail_title_rgb,
            font=title_font,
        )
        draw.text(
            (retail.width + sheet.text_margin_x_pixels, sheet.title_y_pixels),
            "OPENNV GODOT — CURRENT FAIL",
            fill=sheet.godot_title_rgb,
            font=title_font,
        )
        detail = (
            f"{shot_kind}  |  MAE {metrics['meanAbsoluteError']:.3f}  |  "
            f"changed pixels {metrics['changedPixelFraction']:.1%}"
        )
        draw.text(
            (sheet.text_margin_x_pixels, sheet.detail_y_pixels),
            detail,
            fill=sheet.detail_rgb,
            font=detail_font,
        )
        canvas.save(output_path)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--retail-summary", required=True, type=Path)
    parser.add_argument("--godot-report", required=True, type=Path)
    parser.add_argument("--output-root", required=True, type=Path)
    args = parser.parse_args()
    configuration = load_runtime_configuration()
    parity = configuration.actor_parity
    if args.output_root.exists():
        raise SystemExit(f"Refusing to overwrite actor differential: {args.output_root}")
    args.output_root.mkdir(parents=True)

    retail_summary = json.loads(args.retail_summary.read_text(encoding="utf-8"))
    godot_report = json.loads(args.godot_report.read_text(encoding="utf-8"))
    if (
        godot_report.get("configurationSchema") != configuration.document["schema"]
        or str(godot_report.get("configurationSha256", "")).lower()
        != configuration.sha256
    ):
        raise ValueError("Godot actor report was produced with another OpenNV configuration")
    retail = retail_summary["retailPortraits"]
    retail_state_path = Path(retail["stateContract"])
    retail_state = json.loads(retail_state_path.read_text(encoding="utf-8"))
    retail_states = {shot["shotKind"]: shot for shot in retail_state["shots"]}
    retail_state_sha256 = hashlib.sha256(retail_state_path.read_bytes()).hexdigest()
    godot_state = godot_report["retailActorStateContract"]
    godot_state_sha256 = None if godot_state is None else godot_state.get(
        "sha256", godot_state.get("Sha256")
    )
    state_contract_match = (
        godot_state is not None
        and str(godot_state_sha256).lower() == retail_state_sha256
        and int(godot_state["shots"])
        == len(configuration.document["retailActorState"]["requiredShotKinds"])
    )
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
        retail_shot_state = retail_states[shot_kind]
        retail_camera = retail_shot_state["camera"]
        godot_shot = godot_shots[shot_kind]
        godot_frame = Path(godot_shot["file"])
        retail_metrics = image_metrics(retail_frame)
        godot_metrics = image_metrics(godot_frame)
        difference = difference_metrics(retail_frame, godot_frame, configuration)
        state_metrics = shot_state_metrics(retail_shot_state, godot_shot, configuration)
        rendering_pass = (
            difference["meanAbsoluteError"] <= parity.maximum_mean_absolute_error
            and difference["changedPixelFraction"] <= parity.maximum_changed_pixel_fraction
            and abs(
                float(retail_metrics["meanLuminance"])
                - float(godot_metrics["meanLuminance"])
            ) <= parity.maximum_mean_luminance_delta
        )
        objective_pass = (
            rendering_pass and state_contract_match and state_metrics["status"] == "pass"
        )
        sheet = args.output_root / f"trudy-{shot_kind}-retail-vs-godot.png"
        contact_sheet(
            retail_frame,
            godot_frame,
            sheet,
            shot_kind,
            difference,
            configuration,
        )
        comparisons.append(
            {
                "shotKind": shot_kind,
                "status": "pass" if objective_pass else "fail",
                "renderingStatus": "pass" if rendering_pass else "fail",
                "retailFrame": str(retail_frame.resolve()),
                "godotFrame": str(godot_frame.resolve()),
                "contactSheet": str(sheet.resolve()),
                "retailCamera": retail_camera,
                "godotCamera": godot_shot,
                "stateContractStatus": state_metrics,
                "retailFrameMetrics": retail_metrics,
                "godotFrameMetrics": godot_metrics,
                "differenceMetrics": difference,
            }
        )

    identity_pass = all(row["status"] == "pass" for row in identities)
    rendering_pass = all(row["renderingStatus"] == "pass" for row in comparisons)
    state_pass = state_contract_match and all(
        row["stateContractStatus"]["status"] == "pass" for row in comparisons
    )
    report = {
        "schema": "opennv-retail-godot-actor-differential/v1",
        "configuration": configuration.manifest(),
        "status": "pass" if identity_pass and state_pass and rendering_pass else "fail",
        "target": "trudy",
        "identityStatus": "pass" if identity_pass else "fail",
        "renderingStatus": "pass" if rendering_pass else "fail",
        "stateContractStatus": "pass" if state_pass else "fail",
        "retailStateContract": str(retail_state_path.resolve()),
        "retailStateContractSha256": retail_state_sha256,
        "exactProjectionResolved": bool(retail_state["exactProjectionResolved"]),
        "humanVisualVerdictRequired": True,
        "retailCaptureRanBeforeGodot": True,
        "capturesRanConcurrently": False,
        "identities": identities,
        "godotOnlyIdentities": {
            "outfitForms": [
                normalize_form(value) for value in godot_actor["outfitFormIds"]
            ],
            "headPartForms": [normalize_form(value) for value in godot_actor["headPartFormIds"]],
        },
        "comparisons": comparisons,
    }
    report_path = args.output_root / "trudy-retail-vs-godot-report.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": report["status"], "report": str(report_path.resolve())}))


if __name__ == "__main__":
    main()
