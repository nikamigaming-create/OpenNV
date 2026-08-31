#!/usr/bin/env python3
"""Decode the exact owned Fallout 2 Arroyo player idle directions to local cache."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any

from corpus_io import atomic_json
from fo1_frm import decode_frm, palette_rgba_bytes
from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import Fo1ProfileError
from fo2_first_slice import PROFILE_SCHEMA, _archive_paths
from fo2_frm_relief import RELIEF_MODE, RELIEF_SCHEMA, derive_relief
from plugin_stack import file_sha256
from prepare_fo2_temple_presentation import _save_admitted_frame


RECIPE_SCHEMA = "opennv-fo2-player-presentation-recipe/v1"
CACHE_SCHEMA = "opennv-fo2-player-presentation-cache/v1"
CACHE_MANIFEST_NAME = "fo2-arroyo-player-presentation-cache.json"
PALETTE_LOGICAL_PATH = "color.pal"
CRITTER_WEAPON_ART_SUFFIXES = "adefghij"
EQUIPPED_GEOMETRY_DISPOSITION = (
    "owned-critter-frm-composites-player-and-spear-no-separable-3d-weapon-transform"
)
LIVE_3D_PRESENTATION_SCHEMA = "opennv-classic-humanoid-role-donor/v1"
LIVE_3D_PRESENTATION_AUTHORITY = (
    "fo2-source-role-to-owned-fnv-presentation-donor"
)
LIVE_3D_PRESENTATION_OUTFIT_FORM_ID = "0003307c"


def default_recipe_path() -> Path:
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    path = base / "recipes" / "fo2-arroyo-player-presentation-v1.json"
    if not path.is_file():
        raise Fo1ProfileError(f"Fallout 2 player presentation recipe is missing: {path}")
    return path


def _load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise Fo1ProfileError(f"JSON document is not an object: {path}")
    return value


def _load_recipe(path: Path) -> dict[str, Any]:
    recipe = _load_json(path)
    player = recipe.get("player")
    equipped = player.get("equippedWeapon") if isinstance(player, dict) else None
    live_3d = player.get("live3dPresentation") if isinstance(player, dict) else None
    if (
        recipe.get("schema") != RECIPE_SCHEMA
        or recipe.get("id") != path.stem
        or recipe.get("campaign") != "Fallout2"
        or recipe.get("sourceProfileSchema") != PROFILE_SCHEMA
        or recipe.get("overlayOrderHighToLow")
        != ["patch000.dat", "critter.dat", "master.dat"]
        or not isinstance(player, dict)
        or player.get("role") != "Chosen One male tribal source presentation"
        or player.get("critterListLogicalPath") != "art\\critters\\critters.lst"
        or player.get("artIndex") != 62
        or player.get("artListEntry") != "hmwarr,11,1"
        or player.get("objectType") != 1
        or player.get("fid") != "0100003e"
        or player.get("prototypeListLogicalPath") != "proto\\critters\\critters.lst"
        or player.get("prototypeListIndex") != 1
        or player.get("prototypeListEntry") != "00000001.pro"
        or player.get("prototypeLogicalPath") != "proto\\critters\\00000001.pro"
        or player.get("prototypePid") != "01000001"
        or player.get("idleFrmLogicalPath") != "art\\critters\\hmwarraa.frm"
        or player.get("frame") != 0
        or player.get("directions") != list(range(6))
        or player.get("walkAnimationCode") != "AB"
        or player.get("walkFrmLogicalPath") != "art\\critters\\hmwarrab.frm"
        or player.get("walkFrames") != list(range(8))
        or player.get("walkFps") != 10
        or not isinstance(live_3d, dict)
        or live_3d.get("schema") != LIVE_3D_PRESENTATION_SCHEMA
        or live_3d.get("authority") != LIVE_3D_PRESENTATION_AUTHORITY
        or live_3d.get("donorGame") != "FalloutNV"
        or live_3d.get("outfitFormId") != LIVE_3D_PRESENTATION_OUTFIT_FORM_ID
        or live_3d.get("role") != "Chosen One tribal silhouette donor"
        or live_3d.get("fullBody") is not True
        or live_3d.get("requiredBodyRoles")
        != ["body", "left-hand", "right-hand"]
        or live_3d.get("retailParity") is not False
        or not isinstance(equipped, dict)
        or equipped.get("role") != "Spear-equipped Chosen One source animation"
        or equipped.get("itemFid") != "0000002a"
        or equipped.get("itemPid") != "00000007"
        or equipped.get("weaponAnimationCode") != 4
        or equipped.get("weaponArtSuffix") != "g"
        or equipped.get("idleAnimationCode") != "GA"
        or equipped.get("idleFrmLogicalPath") != "art\\critters\\hmwarrga.frm"
        or equipped.get("idleFrame") != 0
        or equipped.get("walkAnimationCode") != "GB"
        or equipped.get("walkFrmLogicalPath") != "art\\critters\\hmwarrgb.frm"
        or equipped.get("walkFrames") != list(range(8))
        or equipped.get("walkFps") != 10
        or equipped.get("geometryDisposition") != EQUIPPED_GEOMETRY_DISPOSITION
        or not isinstance(player.get("relief3d"), dict)
        or player["relief3d"].get("schema") != RELIEF_SCHEMA
        or player["relief3d"].get("mode") != RELIEF_MODE
        or not isinstance(recipe.get("unsupported"), list)
        or not recipe["unsupported"]
    ):
        raise Fo1ProfileError("unexpected Fallout 2 player presentation recipe")
    return recipe


def prepare_fo2_player_presentation(
    profile_path: Path,
    output_root: Path,
    recipe_path: Path | None = None,
) -> dict[str, Any]:
    profile_path = profile_path.resolve()
    output_root = output_root.resolve()
    recipe_path = (recipe_path or default_recipe_path()).resolve()
    if output_root.exists():
        raise Fo1ProfileError(
            f"refusing to overwrite Fallout 2 player presentation cache: {output_root}"
        )
    profile = _load_json(profile_path)
    recipe = _load_recipe(recipe_path)
    if (
        profile.get("schema") != PROFILE_SCHEMA
        or profile.get("status") != "registered-owned-install"
        or profile.get("campaign") != "Fallout2"
        or profile.get("runtimeCompatibility", {}).get("ready") is not False
        or profile.get("retailOrDerivedAssetsPackaged") is not False
    ):
        raise Fo1ProfileError("Fallout 2 player source profile is not registered and asset-free")
    install_root = Path(str(profile.get("install", {}).get("root", ""))).resolve()
    if output_root.is_relative_to(install_root):
        raise Fo1ProfileError("Fallout 2 player cache must be outside the owned install")

    player = recipe["player"]
    fid = int(player["fid"], 16)
    if ((fid >> 24) & 0x0F) != player["objectType"] or (fid & 0x0FFF) != player["artIndex"]:
        raise Fo1ProfileError("Fallout 2 player FID no longer binds the declared art-list index")
    archive_paths = _archive_paths(profile, recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])
    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{output_root.name}-", dir=output_root.parent))
    try:
        with resolver.access_scope() as accessed:
            critter_list = resolver.read(player["critterListLogicalPath"])
            entries = resolver.list_lines(player["critterListLogicalPath"])
            if player["artIndex"] >= len(entries):
                raise Fo1ProfileError("Fallout 2 player art index exceeds critters.lst")
            list_entry = entries[player["artIndex"]].strip()
            if list_entry != player["artListEntry"]:
                raise Fo1ProfileError(
                    f"Fallout 2 player critters.lst entry drifted: {list_entry!r}"
                )
            if resolver.art_filename(fid) != player["artListEntry"]:
                raise Fo1ProfileError("Fallout 2 player FID/list resolution drifted")
            prototype = resolver.prototype(int(player["prototypePid"], 16))
            prototype_list = resolver.list_lines(player["prototypeListLogicalPath"])
            prototype_list_resource = resolver.read(player["prototypeListLogicalPath"])
            prototype_entry = prototype_list[player["prototypeListIndex"] - 1].strip()
            if (
                prototype.list_index != player["prototypeListIndex"]
                or prototype_entry != player["prototypeListEntry"]
                or prototype.filename != player["prototypeListEntry"]
                or prototype.fid != fid
                or prototype.pid != int(player["prototypePid"], 16)
            ):
                raise Fo1ProfileError("Fallout 2 player PRO/FID identity drifted")
            prototype_resource = resolver.read(player["prototypeLogicalPath"])
            if prototype.sha256 != prototype_resource.sha256:
                raise Fo1ProfileError("Fallout 2 player PRO resource identity drifted")
            logical_path = resolver.placed_idle_frm_path(fid)
            if logical_path != player["idleFrmLogicalPath"]:
                raise Fo1ProfileError(
                    f"Fallout 2 player idle FRM resolution drifted: {logical_path}"
                )
            frm = resolver.read(logical_path)
            palette = resolver.read(PALETTE_LOGICAL_PATH)
            colors = palette_rgba_bytes(palette.data)
            decoded = decode_frm(frm.data, colors)
            if (
                decoded["framesPerDirection"] <= player["frame"]
                or len(decoded["directions"]) != len(player["directions"])
            ):
                raise Fo1ProfileError("Fallout 2 player idle FRM direction/frame contract drifted")
            idle_artifacts = [
                _save_admitted_frame(
                    kind="player",
                    logical_path=logical_path,
                    source=frm,
                    colors=colors,
                    rotation=direction,
                    frame_index=player["frame"],
                    staging=staging,
                )
                for direction in player["directions"]
            ]
            art_base = list_entry.split(",", 1)[0].casefold()
            walk_logical_path = (
                f"art\\critters\\{art_base}{player['walkAnimationCode'].casefold()}.frm"
            )
            if walk_logical_path != player["walkFrmLogicalPath"]:
                raise Fo1ProfileError("Fallout 2 player AB walk path resolution drifted")
            walk_frm = resolver.read(walk_logical_path)
            walk_decoded = decode_frm(walk_frm.data, colors)
            if (
                walk_decoded["fps"] != player["walkFps"]
                or walk_decoded["framesPerDirection"] != len(player["walkFrames"])
                or walk_decoded["actionFrame"] != 0
                or len(walk_decoded["directions"]) != len(player["directions"])
            ):
                raise Fo1ProfileError("Fallout 2 player AB walk animation contract drifted")
            walk_artifacts = [
                _save_admitted_frame(
                    kind="player-walk",
                    logical_path=walk_logical_path,
                    source=walk_frm,
                    colors=colors,
                    rotation=direction,
                    frame_index=frame,
                    staging=staging,
                )
                for direction in player["directions"]
                for frame in player["walkFrames"]
            ]
            equipped = player["equippedWeapon"]
            weapon_code = int(equipped["weaponAnimationCode"])
            if (
                weapon_code >= len(CRITTER_WEAPON_ART_SUFFIXES)
                or CRITTER_WEAPON_ART_SUFFIXES[weapon_code]
                != equipped["weaponArtSuffix"]
            ):
                raise Fo1ProfileError(
                    "Fallout 2 Spear PRO animation code no longer resolves weapon art suffix g"
                )
            equipped_idle_path = (
                f"art\\critters\\{art_base}{equipped['idleAnimationCode'].casefold()}.frm"
            )
            if equipped_idle_path != equipped["idleFrmLogicalPath"]:
                raise Fo1ProfileError("Fallout 2 player GA equipped-idle path drifted")
            equipped_idle_frm = resolver.read(equipped_idle_path)
            equipped_idle_decoded = decode_frm(equipped_idle_frm.data, colors)
            if (
                equipped_idle_decoded["framesPerDirection"] <= equipped["idleFrame"]
                or len(equipped_idle_decoded["directions"]) != len(player["directions"])
            ):
                raise Fo1ProfileError(
                    "Fallout 2 player GA equipped-idle direction/frame contract drifted"
                )
            equipped_idle_artifacts = [
                _save_admitted_frame(
                    kind="player-equipped",
                    logical_path=equipped_idle_path,
                    source=equipped_idle_frm,
                    colors=colors,
                    rotation=direction,
                    frame_index=equipped["idleFrame"],
                    staging=staging,
                )
                for direction in player["directions"]
            ]
            equipped_walk_path = (
                f"art\\critters\\{art_base}{equipped['walkAnimationCode'].casefold()}.frm"
            )
            if equipped_walk_path != equipped["walkFrmLogicalPath"]:
                raise Fo1ProfileError("Fallout 2 player GB equipped-walk path drifted")
            equipped_walk_frm = resolver.read(equipped_walk_path)
            equipped_walk_decoded = decode_frm(equipped_walk_frm.data, colors)
            if (
                equipped_walk_decoded["fps"] != equipped["walkFps"]
                or equipped_walk_decoded["framesPerDirection"]
                != len(equipped["walkFrames"])
                or equipped_walk_decoded["actionFrame"] != 0
                or len(equipped_walk_decoded["directions"])
                != len(player["directions"])
            ):
                raise Fo1ProfileError(
                    "Fallout 2 player GB equipped-walk animation contract drifted"
                )
            equipped_walk_artifacts = [
                _save_admitted_frame(
                    kind="player-equipped-walk",
                    logical_path=equipped_walk_path,
                    source=equipped_walk_frm,
                    colors=colors,
                    rotation=direction,
                    frame_index=frame,
                    staging=staging,
                )
                for direction in player["directions"]
                for frame in equipped["walkFrames"]
            ]
            artifacts = (
                idle_artifacts
                + walk_artifacts
                + equipped_idle_artifacts
                + equipped_walk_artifacts
            )
            for artifact in artifacts:
                artifact["relief3d"] = derive_relief(
                    staging,
                    artifact,
                    player["relief3d"],
                    output_folder="player-relief3d",
                )

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
            "slice": "ArroyoCavesPlayer",
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
            "critterList": {
                "logicalPath": critter_list.logical_path,
                "source": critter_list.source,
                "bytes": len(critter_list.data),
                "sha256": critter_list.sha256,
                "entries": len(entries),
                "artIndex": player["artIndex"],
                "entry": list_entry,
            },
            "prototype": {
                "listLogicalPath": player["prototypeListLogicalPath"],
                "listIndex": prototype.list_index,
                "listEntry": prototype_entry,
                "listSha256": prototype_list_resource.sha256,
                "logicalPath": player["prototypeLogicalPath"],
                "pid": f"{prototype.pid:08x}",
                "fid": f"{prototype.fid:08x}",
                "source": prototype_resource.source,
                "bytes": len(prototype_resource.data),
                "sha256": prototype_resource.sha256,
            },
            "idleArt": {
                "role": player["role"],
                "fid": player["fid"],
                "logicalPath": logical_path,
                "source": frm.source,
                "bytes": len(frm.data),
                "sha256": frm.sha256,
                "fps": decoded["fps"],
                "actionFrame": decoded["actionFrame"],
                "framesPerDirection": decoded["framesPerDirection"],
                "decodedDirections": len(decoded["directions"]),
                "admittedFrame": player["frame"],
                "admittedDirections": player["directions"],
                "animationPlayback": False,
            },
            "walkArt": {
                "role": player["role"],
                "fid": player["fid"],
                "animationCode": player["walkAnimationCode"],
                "logicalPath": walk_logical_path,
                "source": walk_frm.source,
                "bytes": len(walk_frm.data),
                "sha256": walk_frm.sha256,
                "fps": walk_decoded["fps"],
                "actionFrame": walk_decoded["actionFrame"],
                "framesPerDirection": walk_decoded["framesPerDirection"],
                "decodedDirections": len(walk_decoded["directions"]),
                "admittedFrames": player["walkFrames"],
                "admittedDirections": player["directions"],
                "animationPlayback": True,
            },
            "live3dPresentation": player["live3dPresentation"],
            "equippedWeaponArt": {
                "role": equipped["role"],
                "itemFid": equipped["itemFid"],
                "itemPid": equipped["itemPid"],
                "weaponAnimationCode": equipped["weaponAnimationCode"],
                "weaponArtSuffix": equipped["weaponArtSuffix"],
                "geometryDisposition": equipped["geometryDisposition"],
                "idle": {
                    "animationCode": equipped["idleAnimationCode"],
                    "logicalPath": equipped_idle_path,
                    "source": equipped_idle_frm.source,
                    "bytes": len(equipped_idle_frm.data),
                    "sha256": equipped_idle_frm.sha256,
                    "fps": equipped_idle_decoded["fps"],
                    "actionFrame": equipped_idle_decoded["actionFrame"],
                    "framesPerDirection": equipped_idle_decoded["framesPerDirection"],
                    "decodedDirections": len(equipped_idle_decoded["directions"]),
                    "admittedFrame": equipped["idleFrame"],
                    "admittedDirections": player["directions"],
                    "animationPlayback": False,
                },
                "walk": {
                    "animationCode": equipped["walkAnimationCode"],
                    "logicalPath": equipped_walk_path,
                    "source": equipped_walk_frm.source,
                    "bytes": len(equipped_walk_frm.data),
                    "sha256": equipped_walk_frm.sha256,
                    "fps": equipped_walk_decoded["fps"],
                    "actionFrame": equipped_walk_decoded["actionFrame"],
                    "framesPerDirection": equipped_walk_decoded["framesPerDirection"],
                    "decodedDirections": len(equipped_walk_decoded["directions"]),
                    "admittedFrames": equipped["walkFrames"],
                    "admittedDirections": player["directions"],
                    "animationPlayback": True,
                },
            },
            "palette": {
                "logicalPath": palette.logical_path,
                "source": palette.source,
                "bytes": len(palette.data),
                "sha256": palette.sha256,
                "decodedColors": len(colors),
            },
            "artifacts": artifacts,
            "resources": resources,
            "counts": {
                "sourceResources": len(resources),
                "idleDirectionArtifacts": len(idle_artifacts),
                "walkFrameArtifacts": len(walk_artifacts),
                "equippedIdleDirectionArtifacts": len(equipped_idle_artifacts),
                "equippedWalkFrameArtifacts": len(equipped_walk_artifacts),
                "closedReliefArtifacts": len(artifacts),
            },
            "promotion": {
                "transported": True,
                "decodedPresentationAssets": True,
                "rendered": False,
                "interactive": False,
                "parityReviewed": False,
                "headsetAccepted": False,
            },
            "runtimeCompatibility": {
                "ready": False,
                "firstSliceBlocker": (
                    "Exact owned HMWARR unarmed AA/AB and Spear-equipped GA/GB states are "
                    "decoded, but FO2's composited FRMs provide no separable 3D weapon "
                    "transform; this cache alone does not provide gameplay or save state."
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
        description="Build a disposable local Fallout 2 Arroyo player PNG cache."
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=None)
    args = parser.parse_args()
    try:
        document = prepare_fo2_player_presentation(
            args.profile,
            args.output_root,
            args.recipe,
        )
    except Exception as error:
        print(f"OPENNV_FO2_PLAYER_PRESENTATION_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_PLAYER_PRESENTATION "
        + json.dumps(
            {
                "cache": str(args.output_root.resolve()),
                "sourceProfileId": document["sourceProfile"]["sourceProfileId"],
                "idleDirectionArtifacts": document["counts"]["idleDirectionArtifacts"],
                "walkFrameArtifacts": document["counts"]["walkFrameArtifacts"],
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
