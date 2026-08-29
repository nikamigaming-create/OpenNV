#!/usr/bin/env python3
"""Prepare the owned Fallout 2 premade picker and female Arroyo presentation."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import struct
import sys
import tempfile
from pathlib import Path
from typing import Any

from corpus_io import atomic_json
from fo1_frm import decode_frm, decode_frm_frame, palette_rgba_bytes
from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import Fo1ProfileError
from fo2_first_slice import PROFILE_SCHEMA, _archive_paths
from plugin_stack import file_sha256
from prepare_fo2_temple_presentation import _save_admitted_frame


RECIPE_SCHEMA = "opennv-fo2-character-start-recipe/v1"
CACHE_SCHEMA = "opennv-fo2-character-start-cache/v1"
CACHE_MANIFEST_NAME = "fo2-character-start-cache.json"
SPECIAL_NAMES = [
    "Strength",
    "Perception",
    "Endurance",
    "Charisma",
    "Intelligence",
    "Agility",
    "Luck",
]
SKILL_NAMES = [
    "Small Guns",
    "Big Guns",
    "Energy Weapons",
    "Unarmed",
    "Melee Weapons",
    "Throwing",
    "First Aid",
    "Doctor",
    "Sneak",
    "Lockpick",
    "Steal",
    "Traps",
    "Science",
    "Repair",
    "Speech",
    "Barter",
    "Gambling",
    "Outdoorsman",
]
TRAIT_NAMES = [
    "Fast Metabolism",
    "Bruiser",
    "Small Frame",
    "One Hander",
    "Finesse",
    "Kamikaze",
    "Heavy Handed",
    "Fast Shot",
    "Bloody Mess",
    "Jinxed",
    "Good Natured",
    "Chem Reliant",
    "Chem Resistant",
    "Night Person",
    "Skilled",
    "Gifted",
]


def default_recipe_path() -> Path:
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    path = base / "recipes" / "fo2-character-start-v1.json"
    if not path.is_file():
        raise Fo1ProfileError(f"Fallout 2 character-start recipe is missing: {path}")
    return path


def _load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise Fo1ProfileError(f"JSON document is not an object: {path}")
    return value


def _load_recipe(path: Path) -> dict[str, Any]:
    recipe = _load_json(path)
    premades = recipe.get("premades")
    female = recipe.get("femalePresentation")
    presentation = recipe.get("presentation")
    inventory = recipe.get("inventory")
    if (
        recipe.get("schema") != RECIPE_SCHEMA
        or recipe.get("id") != path.stem
        or recipe.get("campaign") != "Fallout2"
        or recipe.get("sourceProfileSchema") != PROFILE_SCHEMA
        or recipe.get("overlayOrderHighToLow")
        != ["patch000.dat", "critter.dat", "master.dat"]
        or not isinstance(premades, list)
        or [(row.get("id"), row.get("name")) for row in premades]
        != [("combat", "Narg"), ("stealth", "Mingan"), ("diplomat", "Chitsa")]
        or not isinstance(inventory, dict)
        or inventory.get("logicalPath") != "art\\intrface\\invbox.frm"
        or not isinstance(inventory.get("sha256"), str)
        or len(inventory["sha256"]) != 64
        or not isinstance(inventory.get("width"), int)
        or inventory["width"] <= 0
        or not isinstance(inventory.get("height"), int)
        or inventory["height"] <= 0
        or inventory.get("frame") != 0
        or not isinstance(female, dict)
        or female.get("artIndex") != 61
        or female.get("artListEntry") != "hfprim,11,1"
        or female.get("fid") != "0100003d"
        or female.get("prototypeListLogicalPath") != "proto\\critters\\critters.lst"
        or female.get("prototypeListIndex") != 2
        or female.get("prototypeListEntry") != "00000002.pro"
        or female.get("prototypeLogicalPath") != "proto\\critters\\00000002.pro"
        or female.get("prototypePid") != "01000002"
        or female.get("logicalPath") != "art\\critters\\hfprimaa.frm"
        or female.get("frame") != 0
        or female.get("directions") != list(range(6))
        or female.get("walkAnimationCode") != "AB"
        or female.get("walkLogicalPath") != "art\\critters\\hfprimab.frm"
        or female.get("walkFrames") != list(range(8))
        or female.get("walkFps") != 10
        or not isinstance(presentation, dict)
        or presentation.get("viewport") != [640, 480]
        or presentation.get("panel") != [24, 20, 592, 260]
        or not isinstance(recipe.get("unsupported"), list)
        or not recipe["unsupported"]
    ):
        raise Fo1ProfileError("unexpected Fallout 2 character-start recipe")
    return recipe


def parse_fo2_premade_gcd(data: bytes) -> dict[str, object]:
    """Decode only the FO2 GCD fields admitted by the three-premade picker."""
    if len(data) != 432:
        raise Fo1ProfileError(f"Fallout 2 premade GCD must be 432 bytes, got {len(data)}")
    values = struct.unpack(">108i", data)
    name_field = data[372:404]
    terminator = name_field.find(b"\x00")
    if terminator <= 0:
        raise Fo1ProfileError("Fallout 2 premade GCD has no bounded name")
    try:
        name = name_field[:terminator].decode("cp1252", errors="strict")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("Fallout 2 premade name is not cp1252") from error
    special = list(values[1:8])
    age = values[34]
    sex_index = values[35]
    tags = list(values[101:104])
    raw_traits = list(values[105:107])
    traits = [value for value in raw_traits if value != -1]
    if (
        not name.strip()
        or len(name) > 11
        or any(ord(character) < 32 for character in name)
        or any(value < 1 or value > 10 for value in special)
        or sum(special) != 40
        or age < 16
        or age > 35
        or sex_index not in (0, 1)
        or values[104] != -1
        or len(set(tags)) != 3
        or any(value < 0 or value >= len(SKILL_NAMES) for value in tags)
        or values[107] != 0
        or len(set(traits)) != len(traits)
        or any(value < 0 or value >= len(TRAIT_NAMES) for value in traits)
    ):
        raise Fo1ProfileError("Fallout 2 premade GCD fields are invalid")
    return {
        "name": name,
        "age": age,
        "sex": "Female" if sex_index == 1 else "Male",
        "allocatedSpecial": special,
        "taggedSkillIndices": tags,
        "taggedSkills": [SKILL_NAMES[value] for value in tags],
        "traitIndices": traits,
        "traits": [TRAIT_NAMES[value] for value in traits],
    }


def _verified(resolver: Fo1ResourceResolver, descriptor: dict[str, Any]):
    resource = resolver.read(str(descriptor["logicalPath"]))
    if resource.sha256 != descriptor.get("sha256"):
        raise Fo1ProfileError(
            f"Fallout 2 source hash drift: {descriptor['logicalPath']}"
        )
    return resource


def _save_ui_frame(
    *,
    asset_id: str,
    resource: Any,
    descriptor: dict[str, Any],
    colors: list[tuple[int, int, int, int]],
    staging: Path,
    opaque: bool,
) -> dict[str, Any]:
    frame_index = int(descriptor["frame"])
    decoded = decode_frm_frame(resource.data, colors, 0, frame_index)
    frame = decoded["frame"]
    if (frame["width"], frame["height"]) != (
        int(descriptor["width"]),
        int(descriptor["height"]),
    ):
        raise Fo1ProfileError(f"Fallout 2 character-start dimensions drift: {asset_id}")
    image = frame["image"].copy()
    if opaque:
        image.putalpha(255)
    relative = Path("assets") / "ui" / f"{asset_id}.png"
    destination = staging / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    image.save(destination, format="PNG", optimize=False)
    return {
        "id": asset_id,
        "logicalPath": resource.logical_path,
        "source": resource.source,
        "sourceBytes": len(resource.data),
        "sourceSha256": resource.sha256,
        "frame": frame_index,
        "width": frame["width"],
        "height": frame["height"],
        "opaque": opaque,
        "png": relative.as_posix(),
        "pngBytes": destination.stat().st_size,
        "pngSha256": file_sha256(destination),
    }


def prepare_fo2_character_start(
    profile_path: Path,
    output_root: Path,
    recipe_path: Path | None = None,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    output_root = output_root.resolve()
    recipe_path = (recipe_path or default_recipe_path()).resolve()
    if output_root.exists():
        raise Fo1ProfileError(f"refusing to overwrite Fallout 2 character cache: {output_root}")
    profile = _load_json(profile_path)
    recipe = _load_recipe(recipe_path)
    if (
        profile.get("schema") != PROFILE_SCHEMA
        or profile.get("status") != "registered-owned-install"
        or profile.get("campaign") != "Fallout2"
        or profile.get("runtimeCompatibility", {}).get("ready") is not False
        or profile.get("retailOrDerivedAssetsPackaged") is not False
    ):
        raise Fo1ProfileError("Fallout 2 character source profile is not registered")
    install_root = Path(str(profile.get("install", {}).get("root", ""))).resolve()
    if output_root.is_relative_to(install_root):
        raise Fo1ProfileError("Fallout 2 character cache must be outside the owned install")

    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])
    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{output_root.name}-", dir=output_root.parent))
    try:
        with resolver.access_scope() as accessed:
            palette = _verified(resolver, recipe["palette"])
            colors = palette_rgba_bytes(palette.data)
            picker = _save_ui_frame(
                asset_id="picker",
                resource=_verified(resolver, recipe["picker"]),
                descriptor=recipe["picker"],
                colors=colors,
                staging=staging,
                opaque=True,
            )
            inventory = _save_ui_frame(
                asset_id="inventory",
                resource=_verified(resolver, recipe["inventory"]),
                descriptor=recipe["inventory"],
                colors=colors,
                staging=staging,
                opaque=True,
            )
            characters = []
            for descriptor in recipe["premades"]:
                gcd = _verified(resolver, descriptor["gcd"])
                bio = _verified(resolver, descriptor["bio"])
                panel = _save_ui_frame(
                    asset_id=f"panel-{descriptor['id']}",
                    resource=_verified(resolver, descriptor["panel"]),
                    descriptor=descriptor["panel"],
                    colors=colors,
                    staging=staging,
                    opaque=False,
                )
                profile_row = parse_fo2_premade_gcd(gcd.data)
                if profile_row["name"] != descriptor["name"]:
                    raise Fo1ProfileError(
                        f"Fallout 2 premade identity drift: {descriptor['id']}"
                    )
                try:
                    bio_text = bio.data.decode("cp1252", errors="strict").strip()
                except UnicodeDecodeError as error:
                    raise Fo1ProfileError(
                        f"Fallout 2 premade BIO is not cp1252: {descriptor['id']}"
                    ) from error
                if len(bio_text) < 80:
                    raise Fo1ProfileError(
                        f"Fallout 2 premade BIO is too short: {descriptor['id']}"
                    )
                characters.append(
                    {
                        "id": descriptor["id"],
                        "role": descriptor["role"],
                        "gcd": {
                            "logicalPath": gcd.logical_path,
                            "source": gcd.source,
                            "bytes": len(gcd.data),
                            "sha256": gcd.sha256,
                        },
                        "bio": {
                            "logicalPath": bio.logical_path,
                            "source": bio.source,
                            "bytes": len(bio.data),
                            "sha256": bio.sha256,
                            "text": bio_text,
                        },
                        "panel": panel,
                        "profile": profile_row,
                    }
                )

            female = recipe["femalePresentation"]
            fid = int(female["fid"], 16)
            if (fid & 0x0FFF) != female["artIndex"]:
                raise Fo1ProfileError("Fallout 2 female player FID/art index drifted")
            critter_list = resolver.read(female["critterListLogicalPath"])
            entries = resolver.list_lines(female["critterListLogicalPath"])
            if (
                female["artIndex"] >= len(entries)
                or entries[female["artIndex"]].strip() != female["artListEntry"]
                or resolver.art_filename(fid) != female["artListEntry"]
                or resolver.placed_idle_frm_path(fid) != female["logicalPath"]
            ):
                raise Fo1ProfileError("Fallout 2 female player source resolution drifted")
            prototype = resolver.prototype(int(female["prototypePid"], 16))
            prototype_list = resolver.list_lines(female["prototypeListLogicalPath"])
            prototype_list_resource = resolver.read(female["prototypeListLogicalPath"])
            prototype_entry = prototype_list[female["prototypeListIndex"] - 1].strip()
            if (
                prototype.list_index != female["prototypeListIndex"]
                or prototype_entry != female["prototypeListEntry"]
                or prototype.filename != female["prototypeListEntry"]
                or prototype.fid != fid
                or prototype.pid != int(female["prototypePid"], 16)
            ):
                raise Fo1ProfileError("Fallout 2 female player PRO/FID identity drifted")
            prototype_resource = resolver.read(female["prototypeLogicalPath"])
            if prototype.sha256 != prototype_resource.sha256:
                raise Fo1ProfileError("Fallout 2 female player PRO resource identity drifted")
            female_frm = resolver.read(female["logicalPath"])
            female_artifacts = [
                _save_admitted_frame(
                    kind="female-player",
                    logical_path=female["logicalPath"],
                    source=female_frm,
                    colors=colors,
                    rotation=direction,
                    frame_index=female["frame"],
                    staging=staging,
                )
                for direction in female["directions"]
            ]
            art_base = female["artListEntry"].split(",", 1)[0].casefold()
            walk_logical_path = (
                f"art\\critters\\{art_base}{female['walkAnimationCode'].casefold()}.frm"
            )
            if walk_logical_path != female["walkLogicalPath"]:
                raise Fo1ProfileError("Fallout 2 female AB walk path resolution drifted")
            female_walk_frm = resolver.read(walk_logical_path)
            female_walk_decoded = decode_frm(female_walk_frm.data, colors)
            if (
                female_walk_decoded["fps"] != female["walkFps"]
                or female_walk_decoded["framesPerDirection"] != len(female["walkFrames"])
                or female_walk_decoded["actionFrame"] != 0
                or len(female_walk_decoded["directions"]) != len(female["directions"])
            ):
                raise Fo1ProfileError("Fallout 2 female AB walk animation contract drifted")
            female_walk_artifacts = [
                _save_admitted_frame(
                    kind="female-player-walk",
                    logical_path=walk_logical_path,
                    source=female_walk_frm,
                    colors=colors,
                    rotation=direction,
                    frame_index=frame,
                    staging=staging,
                )
                for direction in female["directions"]
                for frame in female["walkFrames"]
            ]

        resources = [
            {
                "logicalPath": resolver.resources[path].logical_path,
                "source": resolver.resources[path].source,
                "bytes": len(resolver.resources[path].data),
                "sha256": resolver.resources[path].sha256,
            }
            for path in sorted(accessed)
        ]
        document = {
            "schema": CACHE_SCHEMA,
            "status": "decoded-disposable-local-cache",
            "campaign": "Fallout2",
            "slice": "CharacterStartToArroyo",
            "sourceProfile": {
                "file": str(profile_path),
                "schema": PROFILE_SCHEMA,
                "sourceProfileId": profile["sourceProfileId"],
                "sha256": file_sha256(profile_path),
            },
            "recipe": {
                "file": str(recipe_path),
                "schema": RECIPE_SCHEMA,
                "id": recipe["id"],
                "sha256": file_sha256(recipe_path),
            },
            "overlayOrderHighToLow": recipe["overlayOrderHighToLow"],
            "palette": {
                "logicalPath": palette.logical_path,
                "source": palette.source,
                "bytes": len(palette.data),
                "sha256": palette.sha256,
            },
            "presentation": recipe["presentation"],
            "picker": picker,
            "inventory": inventory,
            "characters": characters,
            "femalePresentation": {
                "fid": female["fid"],
                "critterListLogicalPath": critter_list.logical_path,
                "critterListSha256": critter_list.sha256,
                "artIndex": female["artIndex"],
                "artListEntry": female["artListEntry"],
                "prototype": {
                    "listLogicalPath": female["prototypeListLogicalPath"],
                    "listIndex": prototype.list_index,
                    "listEntry": prototype_entry,
                    "listSha256": prototype_list_resource.sha256,
                    "logicalPath": female["prototypeLogicalPath"],
                    "pid": f"{prototype.pid:08x}",
                    "fid": f"{prototype.fid:08x}",
                    "source": prototype_resource.source,
                    "bytes": len(prototype_resource.data),
                    "sha256": prototype_resource.sha256,
                },
                "logicalPath": female["logicalPath"],
                "source": female_frm.source,
                "sourceBytes": len(female_frm.data),
                "sourceSha256": female_frm.sha256,
                "frame": female["frame"],
                "directions": female_artifacts,
                "animationPlayback": False,
                "walkArt": {
                    "animationCode": female["walkAnimationCode"],
                    "logicalPath": walk_logical_path,
                    "source": female_walk_frm.source,
                    "sourceBytes": len(female_walk_frm.data),
                    "sourceSha256": female_walk_frm.sha256,
                    "fps": female_walk_decoded["fps"],
                    "actionFrame": female_walk_decoded["actionFrame"],
                    "framesPerDirection": female_walk_decoded["framesPerDirection"],
                    "directions": female_walk_artifacts,
                    "animationPlayback": True,
                },
            },
            "resources": resources,
            "counts": {
                "premades": len(characters),
                "uiPngs": 2 + len(characters),
                "femaleDirectionPngs": len(female_artifacts),
                "femaleWalkFramePngs": len(female_walk_artifacts),
                "sourceResources": len(resources),
            },
            "promotion": {
                "transported": True,
                "decodedPresentationAssets": True,
                "rendered": False,
                "interactive": False,
                "parityReviewed": False,
            },
            "runtimeCompatibility": {
                "ready": False,
                "firstSliceBlocker": (
                    "The three owned premades, inventory background, and female idle/AB walk "
                    "art are decoded, but selection, handoff, and persistence are runtime "
                    "responsibilities."
                ),
            },
            "cachePolicy": {
                "disposition": "disposable-local-only",
                "containsDerivedOwnedPixels": True,
                "distributionAllowed": False,
            },
            "retailOrDerivedAssetsPackaged": False,
        }
        atomic_json(staging / CACHE_MANIFEST_NAME, document)
        os.replace(staging, output_root)
        return document
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Build a disposable owned Fallout 2 character-start cache."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=None)
    args = parser.parse_args()
    try:
        document = prepare_fo2_character_start(
            args.profile,
            args.output_root,
            args.recipe,
        )
    except Exception as error:
        print(f"OPENNV_FO2_CHARACTER_START_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_CHARACTER_START "
        + json.dumps(
            {
                "cache": str(args.output_root.resolve()),
                "premades": document["counts"]["premades"],
                "femaleDirectionPngs": document["counts"]["femaleDirectionPngs"],
                "femaleWalkFramePngs": document["counts"]["femaleWalkFramePngs"],
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
