#!/usr/bin/env python3
"""Normalize and render a TRELLIS wall asset inside Blender.

Run with Blender's Python after ``--``.  The proof recipe contains every
presentation value so that the generated asset and video remain reproducible.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import subprocess
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


SCHEMA = "opennv-fo1-ai-wall-blender-proof/v1"


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_json(path: Path, value: object) -> None:
    payload = (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-glb", type=Path, required=True)
    parser.add_argument("--generation-report", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--mode", choices=("candidates", "video"), required=True)
    values = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    return parser.parse_args(values)


def reset_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.curves, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def import_primary_mesh(path: Path) -> bpy.types.Object:
    bpy.ops.import_scene.gltf(filepath=str(path))
    meshes = [item for item in bpy.context.scene.objects if item.type == "MESH"]
    if not meshes:
        raise ValueError("TRELLIS GLB contains no mesh")
    primary = max(meshes, key=lambda item: len(item.data.polygons))
    for item in list(bpy.context.scene.objects):
        if item.type == "MESH" and item != primary:
            bpy.data.objects.remove(item, do_unlink=True)
    world_matrix = primary.matrix_world.copy()
    primary.parent = None
    primary.data.transform(world_matrix)
    primary.matrix_world = Matrix.Identity(4)
    primary.name = "FO1_V13ENT_ENTRY_WALL_AI_VOLUME"
    return primary


def bounds(item: bpy.types.Object) -> tuple[Vector, Vector]:
    points = [item.matrix_world @ Vector(corner) for corner in item.bound_box]
    return (
        Vector(tuple(min(point[index] for point in points) for index in range(3))),
        Vector(tuple(max(point[index] for point in points) for index in range(3))),
    )


def normalize(item: bpy.types.Object, config: dict[str, object]) -> dict[str, object]:
    if config["upAxis"] != "Z":
        raise ValueError("this proof currently requires Z-up normalized GLB input")
    lower, upper = bounds(item)
    width = upper.x - lower.x
    if width <= 0.0:
        raise ValueError("TRELLIS wall has zero width")
    scale = float(config["targetWidthMeters"]) / width
    item.scale = Vector((scale, scale, scale))
    bpy.context.view_layer.update()
    lower, upper = bounds(item)
    item.location.x -= (lower.x + upper.x) * 0.5
    item.location.y -= (lower.y + upper.y) * 0.5
    item.location.z += float(config["groundClearanceMeters"]) - lower.z
    bpy.context.view_layer.update()
    lower, upper = bounds(item)
    dimensions = upper - lower
    return {
        "scale": scale,
        "boundsMinimum": list(lower),
        "boundsMaximum": list(upper),
        "dimensionsMeters": list(dimensions),
        "depthToWidthRatio": dimensions.y / dimensions.x,
        "groundOffsetMeters": lower.z,
    }


def principled_material(
    name: str, color: list[float], roughness: float
) -> bpy.types.Material:
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = color
    principled.inputs["Roughness"].default_value = roughness
    return material


def add_floor(config: dict[str, object]) -> None:
    if not config["enabled"]:
        return
    bpy.ops.mesh.primitive_plane_add(
        size=float(config["sizeMeters"]),
        location=(0.0, 0.0, float(config["verticalOffsetMeters"])),
    )
    floor = bpy.context.active_object
    floor.name = "FO1_PROOF_GROUND"
    floor.data.materials.append(
        principled_material(
            "FO1_PROOF_GROUND_MATERIAL",
            list(config["baseColorLinear"]),
            float(config["roughness"]),
        )
    )


def orbit_position(
    azimuth_degrees: float,
    elevation_degrees: float,
    distance: float,
    target: Vector,
) -> Vector:
    azimuth = math.radians(azimuth_degrees)
    elevation = math.radians(elevation_degrees)
    horizontal = math.cos(elevation) * distance
    return target + Vector(
        (
            math.sin(azimuth) * horizontal,
            -math.cos(azimuth) * horizontal,
            math.sin(elevation) * distance,
        )
    )


def look_at(item: bpy.types.Object, target: Vector) -> None:
    rotation = (target - item.location).to_track_quat("-Z", "Y")
    if item.rotation_mode == "QUATERNION":
        item.rotation_quaternion = rotation
    else:
        item.rotation_euler = rotation.to_euler()


def add_area_light(
    name: str,
    config: dict[str, object],
    target: Vector,
    scene_radius: float,
) -> None:
    data = bpy.data.lights.new(name, type="AREA")
    data.energy = float(config["energyWatts"])
    data.shape = "DISK"
    data.size = float(config["sizeMeters"])
    data.color = tuple(config["colorLinear"])
    item = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(item)
    item.location = orbit_position(
        float(config["azimuthDegrees"]),
        float(config["elevationDegrees"]),
        scene_radius * float(config["distanceScale"]),
        target,
    )
    look_at(item, target)


def configure_scene(
    item: bpy.types.Object, render: dict[str, object]
) -> tuple[bpy.types.Object, Vector, float]:
    scene = bpy.context.scene
    scene.render.engine = str(render["engine"])
    scene.render.resolution_x = int(render["resolutionPixels"][0])
    scene.render.resolution_y = int(render["resolutionPixels"][1])
    scene.render.resolution_percentage = int(render["resolutionPercentage"])
    scene.render.image_settings.file_format = str(render["fileFormat"])
    scene.render.image_settings.color_mode = str(render["colorMode"])
    scene.render.film_transparent = bool(render["filmTransparent"])
    scene.render.image_settings.color_depth = "8"
    scene.render.image_settings.compression = 35
    scene.world.color = tuple(render["worldColorLinear"][:3])
    scene.view_settings.look = "AgX - Medium High Contrast"
    if hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = int(render["samples"])

    lower, upper = bounds(item)
    dimensions = upper - lower
    target = Vector((0.0, 0.0, lower.z + dimensions.z * float(render["camera"]["targetHeightFraction"])))
    radius = max(dimensions) * float(render["camera"]["distanceScale"])

    camera_data = bpy.data.cameras.new("FO1_PROOF_CAMERA")
    camera_data.type = "PERSP"
    camera_data.lens = float(render["camera"]["lensMillimeters"])
    camera_data.sensor_width = float(render["camera"]["sensorWidthMillimeters"])
    camera = bpy.data.objects.new("FO1_PROOF_CAMERA", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera

    for name, light in render["lighting"].items():
        add_area_light(f"FO1_PROOF_{name.upper()}", light, target, max(dimensions))
    add_floor(render["floor"])
    return camera, target, radius


def set_camera(
    camera: bpy.types.Object,
    target: Vector,
    radius: float,
    azimuth: float,
    elevation: float,
) -> None:
    camera.location = orbit_position(azimuth, elevation, radius, target)
    look_at(camera, target)


def render_candidates(
    output_root: Path,
    camera: bpy.types.Object,
    target: Vector,
    radius: float,
    camera_config: dict[str, object],
) -> list[dict[str, object]]:
    rows = []
    for index, azimuth in enumerate(camera_config["candidateAzimuthDegrees"]):
        set_camera(
            camera,
            target,
            radius,
            float(azimuth),
            float(camera_config["elevationDegrees"]),
        )
        path = output_root / f"candidate-{index:02d}-azimuth-{int(azimuth):03d}.png"
        bpy.context.scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        rows.append({"azimuthDegrees": azimuth, "path": str(path), "sha256": sha256_path(path)})
    return rows


def render_video(
    output_root: Path,
    camera: bpy.types.Object,
    target: Vector,
    radius: float,
    camera_config: dict[str, object],
    config: dict[str, object],
) -> Path:
    scene = bpy.context.scene
    fps = int(config["framesPerSecond"])
    hold = int(config["holdFrames"])
    orbit = int(config["orbitFrames"])
    final_hold = int(config["finalHoldFrames"])
    interval = int(config["keyframeIntervalFrames"])
    start = float(camera_config["canonicalAzimuthDegrees"])
    elevation = float(camera_config["elevationDegrees"])
    end = hold + orbit + final_hold
    scene.frame_start = 1
    scene.frame_end = end
    scene.render.fps = fps

    camera.rotation_mode = "QUATERNION"
    for frame in range(1, hold + 1, max(hold - 1, 1)):
        set_camera(camera, target, radius, start, elevation)
        camera.keyframe_insert(data_path="location", frame=frame)
        camera.keyframe_insert(data_path="rotation_quaternion", frame=frame)
    if config["motion"] != "sine-sweep":
        raise ValueError("unsupported configured turntable motion")
    for frame in range(hold, hold + orbit + 1, interval):
        progress = (frame - hold) / orbit
        azimuth = start + float(config["orbitDegrees"]) * 0.5 * math.sin(
            progress * math.tau
        )
        set_camera(camera, target, radius, azimuth, elevation)
        camera.keyframe_insert(data_path="location", frame=frame)
        camera.keyframe_insert(data_path="rotation_quaternion", frame=frame)
    for frame in (hold + orbit, end):
        set_camera(camera, target, radius, start, elevation)
        camera.keyframe_insert(data_path="location", frame=frame)
        camera.keyframe_insert(data_path="rotation_quaternion", frame=frame)
    frames_root = output_root / "frames"
    frames_root.mkdir()
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.filepath = str(frames_root / "frame-")
    bpy.ops.render.render(animation=True)
    path = output_root / "fo1-v13ent-ai-wall-turntable-mobile.mp4"
    command = [
        str(config["externalEncoder"]),
        "-y",
        "-framerate",
        str(fps),
        "-start_number",
        "1",
        "-i",
        str(frames_root / "frame-%04d.png"),
        "-c:v",
        str(config["videoCodec"]),
        "-preset",
        str(config["encodingPreset"]),
        "-crf",
        str(int(config["constantRateFactor"])),
        "-pix_fmt",
        str(config["pixelFormat"]),
    ]
    if config["fastStart"]:
        command.extend(["-movflags", "+faststart"])
    command.append(str(path))
    subprocess.run(command, check=True)
    return path


def main() -> int:
    args = arguments()
    input_glb = args.input_glb.resolve()
    generation_report_path = args.generation_report.resolve()
    recipe_path = args.recipe.resolve()
    output_root = args.output_root.resolve()
    if output_root.exists():
        raise ValueError(f"refusing to overwrite Blender proof: {output_root}")
    for path in (input_glb, generation_report_path, recipe_path):
        if not path.is_file():
            raise ValueError(f"missing Blender proof input: {path}")
    recipe = json.loads(recipe_path.read_text(encoding="utf-8"))
    generation = json.loads(generation_report_path.read_text(encoding="utf-8"))
    if recipe.get("schema") != SCHEMA:
        raise ValueError("unexpected Blender proof recipe")
    if generation["artifact"]["sha256"] != sha256_path(input_glb):
        raise ValueError("TRELLIS GLB no longer matches its generation report")

    output_root.mkdir(parents=True)
    reset_scene()
    wall = import_primary_mesh(input_glb)
    normalization = normalize(wall, recipe["normalization"])
    camera, target, radius = configure_scene(wall, recipe["render"])
    if args.mode == "candidates":
        artifacts = render_candidates(
            output_root,
            camera,
            target,
            radius,
            recipe["render"]["camera"],
        )
    else:
        video = render_video(
            output_root,
            camera,
            target,
            radius,
            recipe["render"]["camera"],
            recipe["render"]["turntable"],
        )
        artifacts = [{"path": str(video), "sha256": sha256_path(video), "bytes": video.stat().st_size}]
    blend_path = output_root / "fo1-v13ent-ai-wall-proof.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    report = {
        "schema": "opennv-fo1-ai-wall-blender-proof-run/v1",
        "mode": args.mode,
        "source": {
            "glb": str(input_glb),
            "glbSha256": sha256_path(input_glb),
            "generationReport": str(generation_report_path),
            "generationReportSha256": sha256_path(generation_report_path),
            "recipe": str(recipe_path),
            "recipeSha256": sha256_path(recipe_path),
        },
        "normalization": normalization,
        "artifacts": artifacts,
        "blend": {"path": str(blend_path), "sha256": sha256_path(blend_path)},
        "status": "rendered-unaccepted-awaiting-visual-review",
    }
    write_json(output_root / "blender-proof-run.json", report)
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
