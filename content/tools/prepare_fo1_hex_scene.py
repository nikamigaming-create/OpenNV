#!/usr/bin/env python3
"""Prepare an exact V13ENT hex/floor contract and local owned-art cache."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import shutil
import tempfile
from pathlib import Path

from PIL import Image

from fo1_frm import decode_frm, palette_rgba
from fo1_map_objects import Fo1ResourceResolver, OBJECT_TYPE_NAMES, TYPE_DIRECTORIES
from fo1_profile import Fo1ProfileError, parse_map_layout, sha256_path


RECIPE_SCHEMA = "opennv-fo1-hex-recipe/v1"
SCENE_SCHEMA = "opennv-fo1-hex-scene/v1"
CACHE_SCHEMA = "opennv-fo1-hex-cache/v1"


def read_json(path: Path) -> dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def hex_center(tile: int) -> list[float]:
    if not 0 <= tile < 40000:
        raise ValueError(f"Fallout hex tile is outside the 200x200 grid: {tile}")
    x = tile % 200
    y = tile // 200
    return [x + 0.5 * (y & 1), 0.0, y * (math.sqrt(3.0) / 2.0)]


def floor_index_for_hex(tile: int) -> int:
    x = tile % 200
    y = tile // 200
    return (y // 2) * 100 + (x // 2)


def floor_patch_center(index: int) -> list[float]:
    if not 0 <= index < 10000:
        raise ValueError(f"Fallout floor tile is outside the 100x100 grid: {index}")
    floor_x = index % 100
    floor_y = index // 100
    centers = [
        hex_center((floor_y * 2 + offset_y) * 200 + floor_x * 2 + offset_x)
        for offset_y in range(2)
        for offset_x in range(2)
    ]
    return [sum(center[axis] for center in centers) / 4.0 for axis in range(3)]


def unproject_floor(image: Image.Image, size: int = 128) -> Image.Image:
    if image.width < 4 or image.height < 4 or size < 4:
        raise ValueError("Fallout floor FRM or unprojected texture size is invalid")
    source = image.convert("RGBA")
    denominator = float(size - 1)
    half_x = (source.width - 1) / 2.0
    half_y = (source.height - 1) / 2.0
    return source.transform(
        (size, size),
        Image.Transform.AFFINE,
        (
            half_x / denominator,
            -half_x / denominator,
            half_x,
            half_y / denominator,
            half_y / denominator,
            0.0,
        ),
        Image.Resampling.BILINEAR,
    )


def gltf_width(path: Path) -> float:
    document = read_json(path)
    minimums = []
    maximums = []
    for mesh in document.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            accessor_index = primitive.get("attributes", {}).get("POSITION")
            if accessor_index is None:
                continue
            accessor = document["accessors"][accessor_index]
            minimums.append(float(accessor["min"][0]))
            maximums.append(float(accessor["max"][0]))
    if not minimums:
        raise Fo1ProfileError(f"Vault door glTF has no POSITION bounds: {path}")
    width = max(maximums) - min(minimums)
    if width <= 0.0:
        raise Fo1ProfileError("Vault door glTF has a non-positive width")
    return width


def save_png(image: Image.Image, staging_path: Path, final_path: Path) -> dict[str, object]:
    staging_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(staging_path, format="PNG", optimize=False)
    return {
        "png": str(final_path.resolve()),
        "pngSha256": sha256_path(staging_path),
        "width": image.width,
        "height": image.height,
    }


def prepare(
    recipe_path: Path,
    ettu_root: Path,
    fallout2_master: Path,
    fallout2_critter: Path,
    object_contract_path: Path,
    door_proof_path: Path,
    output_root: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise Fo1ProfileError(f"refusing to overwrite Fallout hex cache: {output_root}")
    recipe = read_json(recipe_path)
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise Fo1ProfileError(f"unexpected Fallout hex recipe: {recipe_path}")
    source_recipe = recipe["source"]
    map_path = (ettu_root / Path(source_recipe["mapRelativePath"])).resolve()
    palette_path = (ettu_root / Path(source_recipe["paletteRelativePath"])).resolve()
    if sha256_path(map_path) != source_recipe["mapSha256"]:
        raise Fo1ProfileError("V13ENT MAP hash drift")
    if sha256_path(palette_path) != source_recipe["paletteSha256"]:
        raise Fo1ProfileError("Fallout palette hash drift")
    if sha256_path(fallout2_master) != source_recipe["fallout2MasterSha256"]:
        raise Fo1ProfileError("Fallout 2 master.dat hash drift")
    if sha256_path(fallout2_critter) != source_recipe["fallout2CritterSha256"]:
        raise Fo1ProfileError("Fallout 2 critter.dat hash drift")
    if sha256_path(object_contract_path) != source_recipe["objectContractSha256"]:
        raise Fo1ProfileError("V13ENT object-contract hash drift")
    if sha256_path(door_proof_path) != recipe["door"]["proofSha256"]:
        raise Fo1ProfileError("Vault door proof hash drift")

    layout = parse_map_layout(map_path.read_bytes())
    if len(layout.elevations) != 1 or layout.elevations[0].elevation != 0:
        raise Fo1ProfileError("V13ENT hex slice requires elevation zero only")
    elevation = layout.elevations[0]
    if elevation.raw_sha256 != source_recipe["floorGridSha256"]:
        raise Fo1ProfileError("V13ENT floor-grid hash drift")
    if (
        layout.header.enteringTile != recipe["entry"]["tile"]
        or layout.header.enteringElevation != recipe["entry"]["elevation"]
        or layout.header.enteringRotation != recipe["entry"]["rotation"]
    ):
        raise Fo1ProfileError("V13ENT entry contract drift")

    objects = read_json(object_contract_path)
    door = next(
        (row for row in objects["map"]["doors"] if row["serial"] == recipe["door"]["serial"]),
        None,
    )
    frame = next(
        (
            row
            for level in objects["map"]["objects"]["elevations"]
            for row in level["objects"]
            if row["serial"] == recipe["door"]["frameSerial"]
        ),
        None,
    )
    if door is None or frame is None:
        raise Fo1ProfileError("V13ENT door/frame objects are absent")
    for row, expected_tile, expected_rotation, expected_art in (
        (door, recipe["door"]["tile"], recipe["door"]["rotation"], recipe["door"]["artFilename"]),
        (frame, recipe["door"]["tile"], recipe["door"]["rotation"], recipe["door"]["frameArtFilename"]),
    ):
        if row["tile"] != expected_tile or row["rotation"] != expected_rotation or row["artFilename"] != expected_art:
            raise Fo1ProfileError("V13ENT door/frame placement drift")

    proof = read_json(door_proof_path)
    if proof.get("schema") != "opennv-fo1-door-presentation-proof/v1":
        raise Fo1ProfileError("unexpected Vault door proof schema")
    if proof["sourceObjectContract"]["door"]["serial"] != door["serial"]:
        raise Fo1ProfileError("Vault door proof source identity drift")
    model_path = Path(proof["outputs"]["model"])
    sidecar_path = Path(proof["outputs"]["sidecar"])
    material_path = Path(proof["outputs"]["materialManifest"])
    if sha256_path(model_path) != proof["outputs"]["modelSha256"]:
        raise Fo1ProfileError("Vault door model hash drift")
    if sha256_path(material_path) != proof["outputs"]["materialManifestSha256"]:
        raise Fo1ProfileError("Vault door material hash drift")

    resolver = Fo1ResourceResolver(ettu_root, fallout2_master, [fallout2_critter])
    tile_names = resolver.list_lines("art\\tiles\\tiles.lst")
    colors = palette_rgba(palette_path)
    floor_ids = [entry & 0x0FFF for entry in elevation.entries]
    unique_floor_ids = sorted(set(floor_ids))

    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=output_root.name + ".", dir=output_root.parent))
    try:
        floor_art = []
        for floor_id in unique_floor_ids:
            if floor_id >= len(tile_names):
                raise Fo1ProfileError(f"floor art ID {floor_id} exceeds tiles.lst")
            filename = tile_names[floor_id].split(" ", 1)[0].strip()
            resource = resolver.read(f"art\\tiles\\{filename}")
            decoded = decode_frm(resource.data, colors)
            source_frame = decoded["directions"][0]["frames"][0]["image"]
            unprojected = unproject_floor(source_frame)
            relative = Path("textures") / f"floor-{floor_id:04d}.png"
            artifact = save_png(unprojected, staging / relative, output_root / relative)
            floor_art.append(
                {
                    "id": floor_id,
                    "filename": filename,
                    "source": resource.source,
                    "sourceSha256": resource.sha256,
                    "sourceWidth": source_frame.width,
                    "sourceHeight": source_frame.height,
                    **artifact,
                }
            )

        source_door_artifacts = []
        for label, filename in (
            ("door", door["artFilename"]),
            ("frame", frame["artFilename"]),
        ):
            resource = resolver.read(f"art\\scenery\\{filename}")
            decoded = decode_frm(resource.data, colors)
            image = decoded["directions"][0]["frames"][0]["image"]
            relative = Path("textures") / f"source-{label}.png"
            artifact = save_png(image, staging / relative, output_root / relative)
            source_door_artifacts.append(
                {
                    "role": label,
                    "filename": filename,
                    "source": resource.source,
                    "sourceSha256": resource.sha256,
                    "frames": decoded["framesPerDirection"],
                    **artifact,
                }
            )

        source_door_image = next(row for row in source_door_artifacts if row["role"] == "door")
        target_door_width_meters = gltf_width(model_path) * float(recipe["door"]["targetUnitsToMeters"])
        pixels_per_meter = float(source_door_image["width"]) / target_door_width_meters
        sprite_artifacts: dict[str, dict[str, object]] = {}
        sprite_placements = []
        skipped_sprite_objects = []
        top_level_objects = objects["map"]["objects"]["elevations"][0]["objects"]
        blocker_rows = []
        for obj in top_level_objects:
            flags = int(obj["flags"], 16)
            if obj["tile"] >= 0 and not flags & 0x00000010:
                blocker_rows.append(
                    {
                        "serial": obj["serial"],
                        "tile": obj["tile"],
                        "flags": obj["flags"],
                        "multihex": bool(flags & 0x00000800),
                        "artFilename": obj["artFilename"],
                    }
                )
        excluded_serials = {recipe["door"]["serial"], recipe["door"]["frameSerial"]}
        for obj in top_level_objects:
            if obj["serial"] in excluded_serials:
                continue
            if obj["tile"] < 0 or obj["artFilename"] is None:
                skipped_sprite_objects.append(
                    {"serial": obj["serial"], "reason": "off-grid-or-no-art"}
                )
                continue
            flags = int(obj["flags"], 16)
            if flags & 0x00000001:
                skipped_sprite_objects.append(
                    {"serial": obj["serial"], "reason": "OBJECT_HIDDEN"}
                )
                continue
            object_type = int(obj["prototype"]["object_type"])
            directory = TYPE_DIRECTORIES.get(object_type)
            if directory is None:
                skipped_sprite_objects.append(
                    {"serial": obj["serial"], "reason": f"unsupported-object-type-{object_type}"}
                )
                continue
            if object_type == 1:
                fid = int(obj["fid"], 16)
                animation = (fid >> 16) & 0xFF
                weapon = (fid >> 12) & 0x0F
                packed_rotation = (fid >> 28) & 0x07
                if animation != 0 or weapon != 0 or packed_rotation != 0:
                    skipped_sprite_objects.append(
                        {
                            "serial": obj["serial"],
                            "reason": (
                                f"unsupported-critter-fid-animation-{animation}-"
                                f"weapon-{weapon}-rotation-{packed_rotation}"
                            ),
                        }
                    )
                    continue
                base_name = obj["artFilename"].split(",", 1)[0]
                logical_path = f"art\\critters\\{base_name}aa.frm"
            else:
                logical_path = f"art\\{directory}\\{obj['artFilename']}"
            resource = resolver.read(logical_path)
            decoded = decode_frm(resource.data, colors)
            rotation = int(obj["rotation"])
            frames = decoded["directions"][rotation]["frames"]
            frame_index = int(obj["frame"])
            if not 0 <= frame_index < len(frames):
                raise Fo1ProfileError(
                    f"MAP object {obj['serial']} frame {frame_index} exceeds {logical_path} ({len(frames)})"
                )
            frame_data = frames[frame_index]
            artifact_key = f"{resource.sha256}:{rotation}:{frame_index}"
            artifact_id = hashlib.sha256(artifact_key.encode("ascii")).hexdigest()[:20]
            if artifact_id not in sprite_artifacts:
                relative = Path("sprites") / f"{artifact_id}.png"
                artifact = save_png(
                    frame_data["image"],
                    staging / relative,
                    output_root / relative,
                )
                sprite_artifacts[artifact_id] = {
                    "id": artifact_id,
                    "logicalPath": logical_path,
                    "source": resource.source,
                    "sourceSha256": resource.sha256,
                    "rotation": rotation,
                    "frame": frame_index,
                    "frameOffset": [frame_data["x"], frame_data["y"]],
                    **artifact,
                }
            sprite_placements.append(
                {
                    "serial": obj["serial"],
                    "objectId": obj["id"],
                    "tile": obj["tile"],
                    "hex": [obj["tileX"], obj["tileY"]],
                    "worldMeters": hex_center(obj["tile"]),
                    "rotation": rotation,
                    "pixelOffset": obj["pixelOffset"],
                    "fid": obj["fid"],
                    "pid": obj["pid"],
                    "flags": obj["flags"],
                    "objectType": obj["prototype"]["object_type"],
                    "objectTypeName": OBJECT_TYPE_NAMES[object_type],
                    "artFilename": obj["artFilename"],
                    "artifactId": artifact_id,
                }
            )

        floor_by_id = {row["id"]: row for row in floor_art}
        non_default_floor_count = sum(floor_id != 1 for floor_id in floor_ids)
        blocked_set = {row["tile"] for row in blocker_rows}
        blocked_hexes = sorted(blocked_set)
        provisional_walkable_hexes = sum(
            floor_ids[floor_index_for_hex(tile)] != 1 and tile not in blocked_set
            for tile in range(40000)
        )
        scene = {
            "schema": SCENE_SCHEMA,
            "status": "interactive-hex-topology-proof",
            "recipe": {"id": recipe["id"], "sha256": sha256_path(recipe_path)},
            "source": {
                "map": {"file": map_path.name, "sha256": sha256_path(map_path)},
                "floorGridSha256": elevation.raw_sha256,
                "objectContractSha256": sha256_path(object_contract_path),
                "fallout2MasterSha256": sha256_path(fallout2_master),
                "fallout2CritterSha256": sha256_path(fallout2_critter),
                "paletteSha256": sha256_path(palette_path),
            },
            "grid": {
                **recipe["grid"],
                "floorIds": floor_ids,
                "floorPatchCenters": [floor_patch_center(index) for index in range(10000)],
                "floorArt": floor_art,
                "defaultFloorId": 1,
                "blockedHexes": blocked_hexes,
                "blockers": blocker_rows,
            },
            "entry": {
                **recipe["entry"],
                "hex": [recipe["entry"]["tile"] % 200, recipe["entry"]["tile"] // 200],
                "worldMeters": hex_center(recipe["entry"]["tile"]),
                "floorId": floor_ids[floor_index_for_hex(recipe["entry"]["tile"])],
            },
            "door": {
                "source": door,
                "frame": frame,
                "worldMeters": hex_center(door["tile"]),
                "sourceArt": source_door_artifacts,
                "target": {
                    "model": str(model_path.resolve()),
                    "sidecar": str(sidecar_path.resolve()),
                    "sourceSha256": proof["target"]["sourceNifSha256"],
                    "materialManifest": str(material_path.resolve()),
                    "materialManifestSha256": proof["outputs"]["materialManifestSha256"],
                    "unitsToMeters": recipe["door"]["targetUnitsToMeters"],
                },
            },
            "objectSprites": {
                "presentation": "exact source FRM frame at exact MAP hex; camera-facing 2.5D",
                "pixelsPerMeter": pixels_per_meter,
                "scaleSource": "source door FRM width matched to mapped 3D door-leaf width",
                "artifacts": [sprite_artifacts[key] for key in sorted(sprite_artifacts)],
                "placements": sprite_placements,
                "skipped": skipped_sprite_objects,
            },
            "camera": {
                "homeFocusMeters": [
                    (hex_center(recipe["entry"]["tile"])[0] + hex_center(door["tile"])[0]) / 2.0,
                    0.0,
                    (hex_center(recipe["entry"]["tile"])[2] + hex_center(door["tile"])[2]) / 2.0,
                ],
                "homeSizeMeters": 30.0,
                "yawDegrees": -45.0,
                "pitchDegrees": -52.0,
            },
            "tacticalProof": recipe["tacticalProof"],
            "coverage": {
                "floorEntries": len(floor_ids),
                "uniqueFloorIds": len(floor_by_id),
                "nonDefaultFloorEntries": non_default_floor_count,
                "floorBackedHexes": non_default_floor_count * 4,
                "provisionalWalkableHexesAfterObjectFlags": provisional_walkable_hexes,
                "blockedHexes": len(blocked_hexes),
                "multihexBlockersWithCentralHexOnly": sum(row["multihex"] for row in blocker_rows),
                "topLevelObjects": objects["map"]["objects"]["totalTopLevelObjects"],
                "spritePlacements": len(sprite_placements),
                "spriteArtifacts": len(sprite_artifacts),
                "skippedSpriteObjects": len(skipped_sprite_objects),
                "doors": len(objects["map"]["doors"]),
                "sourceDoorFrames": door["prototype"]["subtype_name"] == "door",
            },
            "supported": recipe["supported"],
            "unsupported": recipe["unsupported"],
        }
        scene_path = staging / "hex-scene.json"
        write_json(scene_path, scene)
        manifest = {
            "schema": CACHE_SCHEMA,
            "status": "prepared-owned-data",
            "scene": str((output_root / "hex-scene.json").resolve()),
            "sceneSha256": sha256_path(scene_path),
            "floorTextures": len(floor_art),
            "walkableHexes": provisional_walkable_hexes,
            "entryTile": recipe["entry"]["tile"],
            "doorTile": door["tile"],
            "retailOrDerivedAssetsPackaged": False,
        }
        write_json(staging / "hex-cache-manifest.json", manifest)
        os.replace(staging, output_root)
        return manifest
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--ettu-root", type=Path, required=True)
    parser.add_argument("--fallout2-master", type=Path, required=True)
    parser.add_argument("--fallout2-critter", type=Path, required=True)
    parser.add_argument("--object-contract", type=Path, required=True)
    parser.add_argument("--door-proof", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    result = prepare(
        args.recipe.resolve(),
        args.ettu_root.resolve(),
        args.fallout2_master.resolve(),
        args.fallout2_critter.resolve(),
        args.object_contract.resolve(),
        args.door_proof.resolve(),
        args.output_root.resolve(),
    )
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
