#!/usr/bin/env python3
"""Render a deterministic Blender preview for an OpenNV actor glTF."""

from __future__ import annotations

import sys
from pathlib import Path

import bpy
from mathutils import Vector


def main() -> None:
    arguments = sys.argv[sys.argv.index("--") + 1 :]
    if len(arguments) != 2:
        raise ValueError("expected actor.gltf and output.png")
    model = Path(arguments[0]).resolve()
    output = Path(arguments[1]).resolve()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=str(model))
    bpy.context.view_layer.update()

    meshes = [value for value in bpy.context.scene.objects if value.type == "MESH"]
    if not meshes:
        raise ValueError("actor preview has no meshes")
    points = [
        value.matrix_world @ Vector(corner)
        for value in meshes
        for corner in value.bound_box
    ]
    minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    extent = max(size)

    camera_data = bpy.data.cameras.new("ActorPreviewCamera")
    camera = bpy.data.objects.new("ActorPreviewCamera", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    camera.location = center + Vector((extent * 1.9, -extent * 2.8, extent * 1.15))
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = extent * 1.30
    bpy.context.scene.camera = camera

    for name, location, energy, size_meters in (
        ("Key", center + Vector((extent * 2.0, -extent * 1.5, extent * 2.5)), 1700.0, extent),
        ("Fill", center + Vector((-extent * 1.5, -extent, extent)), 900.0, extent * 0.8),
    ):
        data = bpy.data.lights.new(name, "AREA")
        data.energy = energy
        data.shape = "DISK"
        data.size = size_meters
        light = bpy.data.objects.new(name, data)
        light.location = location
        light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
        bpy.context.scene.collection.objects.link(light)

    world = bpy.context.scene.world or bpy.data.worlds.new("ActorPreviewWorld")
    bpy.context.scene.world = world
    world.color = (0.025, 0.030, 0.036)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(output)
    scene.render.film_transparent = False
    scene.view_settings.look = "AgX - Medium High Contrast"
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.render.render(write_still=True)
    print(
        "OPENNV_ACTOR_BLENDER_PREVIEW "
        f"output={output} meshes={len(meshes)} bounds={tuple(round(value, 4) for value in size)}"
    )


if __name__ == "__main__":
    main()
