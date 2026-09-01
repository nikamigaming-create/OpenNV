#!/usr/bin/env python3
"""Prepare the owned Fallout 2 premade picker and female Arroyo presentation."""

from __future__ import annotations

import argparse
import configparser
import hashlib
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import wave
from fractions import Fraction
from pathlib import Path
from typing import Any

from PIL import Image

from corpus_io import atomic_json
from fo1_frm import decode_frm, decode_frm_frame, palette_rgba_bytes
from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import Fo1ProfileError
from fo2_first_slice import PROFILE_SCHEMA, _archive_paths
from fo2_frm_relief import RELIEF_MODE, RELIEF_SCHEMA, derive_relief
from plugin_stack import file_sha256
from prepare_fo2_temple_presentation import _save_admitted_frame


RECIPE_SCHEMA = "opennv-fo2-character-start-recipe/v1"
CACHE_SCHEMA = "opennv-fo2-character-start-cache/v1"
CACHE_MANIFEST_NAME = "fo2-character-start-cache.json"
RELIEF_DEPTH_METERS = 0.12
RELIEF_SIDE_ROUGHNESS = 0.86
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
GCD_SKILL_BONUS_START = 71
GCD_SKILL_BONUS_COUNT = len(SKILL_NAMES)
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
MVE_VIDEO_STREAM_COUNT = 1
MVE_AUDIO_STREAM_COUNT = 1
MVE_SOURCE_FRAME_NUMBER_ORIGIN = 1
MVE_FADE_COLOR_COMPONENTS = 3
MVE_WAVE_SAMPLE_WIDTH_BYTES = 2
MVE_WAVE_CHANNELS = 2
MVE_FRAME_FILENAME_DIGITS = 4
MVE_FFMPEG_SUCCESS = 0
MVE_TIMESTAMP_DECIMAL_DIGITS = 9
SHA256_HEX_LENGTH = 64
FO2_OPENING_HANDOFF_ARRIVAL_TILE = 28707
OPENING_RECIPE_MATCH_COUNT = 1
CRITTER_WEAPON_ART_SUFFIXES = "adefghij"
EQUIPPED_GEOMETRY_DISPOSITION = (
    "owned-critter-frm-composites-player-and-spear-no-separable-3d-weapon-transform"
)


def default_recipe_path() -> Path:
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    recipes = base / "recipes"
    candidates = [
        path
        for path in sorted(recipes.glob("fo2-character-start-*.json"))
        if isinstance(_load_json(path).get("openingTail"), dict)
    ]
    if len(candidates) != OPENING_RECIPE_MATCH_COUNT:
        raise Fo1ProfileError(
            "Fallout 2 opening character-start recipe selection is ambiguous"
        )
    return candidates[0]


def _load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise Fo1ProfileError(f"JSON document is not an object: {path}")
    return value


def _load_recipe(path: Path) -> dict[str, Any]:
    recipe = _load_json(path)
    premades = recipe.get("premades")
    female = recipe.get("femalePresentation")
    equipped = female.get("equippedWeapon") if isinstance(female, dict) else None
    presentation = recipe.get("presentation")
    inventory = recipe.get("inventory")
    opening = recipe.get("openingTail")
    opening_required = opening is not None
    opening_valid = (
        isinstance(opening, dict)
        and isinstance(opening.get("movie"), dict)
        and opening["movie"].get("logicalPath") == "art\\cuts\\elder.mve"
        and isinstance(opening["movie"].get("sha256"), str)
        and len(opening["movie"]["sha256"]) == SHA256_HEX_LENGTH
        and isinstance(opening.get("fadeConfig"), dict)
        and opening["fadeConfig"].get("logicalPath") == "art\\cuts\\elder.cfg"
        and isinstance(opening["fadeConfig"].get("sha256"), str)
        and len(opening["fadeConfig"]["sha256"]) == SHA256_HEX_LENGTH
        and isinstance(opening.get("video"), dict)
        and opening["video"].get("sourceFrameNumbersOneBased") is True
        and opening["video"].get("playbackStartFrame")
        == MVE_SOURCE_FRAME_NUMBER_ORIGIN
        and opening["video"].get("tailStartFrame")
        == opening.get("fade", {}).get("startFrame")
        and isinstance(opening.get("audio"), dict)
        and opening["audio"].get("channels") == MVE_WAVE_CHANNELS
        and opening["audio"].get("sampleBytes") == MVE_WAVE_SAMPLE_WIDTH_BYTES
        and isinstance(opening.get("fade"), dict)
        and opening["fade"].get("type") == "out"
        and opening["fade"].get("color") == [0, 0, 0]
        and opening["fade"].get("movieEndForcesBlack") is True
        and isinstance(opening.get("handoff"), dict)
        and opening["handoff"].get("mapIndex") == 3
        and opening["handoff"].get("elevation") == 0
        and opening["handoff"].get("arrivalTile")
        == FO2_OPENING_HANDOFF_ARRIVAL_TILE
        and opening["handoff"].get("arrivalRotation") == 0
    )
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
        or len(inventory["sha256"]) != SHA256_HEX_LENGTH
        or not isinstance(inventory.get("width"), int)
        or inventory["width"] <= 0
        or not isinstance(inventory.get("height"), int)
        or inventory["height"] <= 0
        or inventory.get("frame") != 0
        or opening_required != opening_valid
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
        or not isinstance(equipped, dict)
        or equipped.get("itemFid") != "0000002a"
        or equipped.get("itemPid") != "00000007"
        or equipped.get("weaponAnimationCode") != 4
        or equipped.get("weaponArtSuffix") != "g"
        or equipped.get("idleAnimationCode") != "GA"
        or equipped.get("idleLogicalPath") != "art\\critters\\hfprimga.frm"
        or equipped.get("idleFrame") != 0
        or equipped.get("walkAnimationCode") != "GB"
        or equipped.get("walkLogicalPath") != "art\\critters\\hfprimgb.frm"
        or equipped.get("walkFrames") != list(range(8))
        or equipped.get("walkFps") != 10
        or equipped.get("geometryDisposition") != EQUIPPED_GEOMETRY_DISPOSITION
        or not isinstance(female.get("relief3d"), dict)
        or female["relief3d"].get("schema") != RELIEF_SCHEMA
        or female["relief3d"].get("mode") != RELIEF_MODE
        or female["relief3d"].get("depthMeters") != RELIEF_DEPTH_METERS
        or female["relief3d"].get("sideRoughness") != RELIEF_SIDE_ROUGHNESS
        or not isinstance(presentation, dict)
        or presentation.get("viewport") != [640, 480]
        or presentation.get("panel") != [24, 20, 592, 260]
        or not isinstance(recipe.get("unsupported"), list)
        or not recipe["unsupported"]
    ):
        raise Fo1ProfileError("unexpected Fallout 2 character-start recipe")
    return recipe


def _parse_opening_fade_config(
    data: bytes,
    expected: dict[str, Any],
) -> dict[str, Any]:
    try:
        text = data.decode("ascii", errors="strict")
    except UnicodeDecodeError as error:
        raise Fo1ProfileError("Fallout 2 Elder fade config is not ASCII") from error
    config = configparser.ConfigParser(
        interpolation=None,
        inline_comment_prefixes=(";",),
    )
    config.read_string(text)
    try:
        total_effects = config.getint("info", "total_effects")
        effect_frames = [
            int(value.strip())
            for value in config.get("info", "effect_frames").split(",")
        ]
        start_frame = int(expected["startFrame"])
        section = config[str(start_frame)]
        fade_type = section.get("fade_type", "").strip().casefold()
        fade_color = [
            int(value.strip()) for value in section.get("fade_color", "").split(",")
        ]
        fade_steps = section.getint("fade_steps")
    except (KeyError, ValueError, configparser.Error) as error:
        raise Fo1ProfileError("Fallout 2 Elder fade config is malformed") from error
    if (
        total_effects != len(effect_frames)
        or len(set(effect_frames)) != total_effects
        or start_frame not in effect_frames
        or fade_type != expected["type"]
        or fade_color != expected["color"]
        or len(fade_color) != MVE_FADE_COLOR_COMPONENTS
        or fade_steps != expected["steps"]
        or fade_steps <= 0
    ):
        raise Fo1ProfileError("Fallout 2 Elder source fade contract drifted")
    return {
        "startFrame": start_frame,
        "endFrame": start_frame + fade_steps - MVE_SOURCE_FRAME_NUMBER_ORIGIN,
        "type": fade_type,
        "color": fade_color,
        "steps": fade_steps,
    }


def _probe_opening_movie(
    path: Path,
    ffprobe: str,
    expected_video: dict[str, Any],
    expected_audio: dict[str, Any],
) -> dict[str, Any]:
    result = subprocess.run(
        [
            ffprobe,
            "-v",
            "error",
            "-count_frames",
            "-show_entries",
            (
                "stream=index,codec_name,codec_type,width,height,r_frame_rate,"
                "nb_read_frames,sample_rate,channels"
            ),
            "-of",
            "json",
            str(path),
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    document = json.loads(result.stdout)
    streams = document.get("streams")
    if not isinstance(streams, list):
        raise Fo1ProfileError("Fallout 2 Elder movie probe has no streams")
    video_rows = [row for row in streams if row.get("codec_type") == "video"]
    audio_rows = [row for row in streams if row.get("codec_type") == "audio"]
    if len(video_rows) != MVE_VIDEO_STREAM_COUNT or len(audio_rows) != MVE_AUDIO_STREAM_COUNT:
        raise Fo1ProfileError("Fallout 2 Elder movie stream coverage drifted")
    video = video_rows[0]
    audio = audio_rows[0]
    expected_rate = (
        f"{expected_video['frameRateNumerator']}/"
        f"{expected_video['frameRateDenominator']}"
    )
    if (
        video.get("codec_name") != expected_video["codec"]
        or video.get("width") != expected_video["width"]
        or video.get("height") != expected_video["height"]
        or video.get("r_frame_rate") != expected_rate
        or int(video.get("nb_read_frames", -1)) != expected_video["sourceFrameCount"]
        or audio.get("codec_name") != expected_audio["sourceCodec"]
        or int(audio.get("sample_rate", -1)) != expected_audio["sampleRate"]
        or audio.get("channels") != expected_audio["channels"]
    ):
        raise Fo1ProfileError("Fallout 2 Elder movie stream identity drifted")
    return {
        "videoCodec": video["codec_name"],
        "audioCodec": audio["codec_name"],
        "width": video["width"],
        "height": video["height"],
        "frameRate": expected_rate,
        "sourceFrameCount": int(video["nb_read_frames"]),
        "sampleRate": int(audio["sample_rate"]),
        "channels": audio["channels"],
    }


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
    skill_bonuses = list(
        values[
            GCD_SKILL_BONUS_START :
            GCD_SKILL_BONUS_START + GCD_SKILL_BONUS_COUNT
        ]
    )
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
        or any(value < 0 for value in skill_bonuses)
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
        "skillBonuses": skill_bonuses,
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
    ffmpeg: str = "ffmpeg",
    ffprobe: str = "ffprobe",
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
            opening_tail = None
            opening_frames = []
            if "openingTail" in recipe:
                opening_recipe = recipe["openingTail"]
                opening_movie = _verified(resolver, opening_recipe["movie"])
                opening_fade_config = _verified(
                    resolver,
                    opening_recipe["fadeConfig"],
                )
                opening_fade = _parse_opening_fade_config(
                    opening_fade_config.data,
                    opening_recipe["fade"],
                )
                source_mve_path = staging / "ELDER.MVE"
                source_mve_path.write_bytes(opening_movie.data)
                opening_probe = _probe_opening_movie(
                    source_mve_path,
                    ffprobe,
                    opening_recipe["video"],
                    opening_recipe["audio"],
                )
                source_frame_count = int(opening_recipe["video"]["sourceFrameCount"])
                playback_start_frame = int(
                    opening_recipe["video"]["playbackStartFrame"]
                )
                tail_start_frame = int(opening_recipe["video"]["tailStartFrame"])
                playback_frame_count = (
                    source_frame_count
                    - playback_start_frame
                    + MVE_SOURCE_FRAME_NUMBER_ORIGIN
                )
                decoded_start_index = (
                    playback_start_frame - MVE_SOURCE_FRAME_NUMBER_ORIGIN
                )
                if (
                    playback_frame_count != source_frame_count
                    or opening_fade["startFrame"] != tail_start_frame
                ):
                    raise Fo1ProfileError("Fallout 2 Elder playback range is invalid")
                opening_directory = staging / "assets" / "opening"
                opening_directory.mkdir(parents=True)
                frame_pattern = opening_directory / (
                    f"frame-%0{MVE_FRAME_FILENAME_DIGITS}d.png"
                )
                frame_command = [
                    ffmpeg,
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-i",
                    str(source_mve_path),
                    "-map",
                    "0:v:0",
                    "-an",
                    "-vf",
                    f"select=gte(n\\,{decoded_start_index})",
                    "-fps_mode",
                    "passthrough",
                    "-start_number",
                    str(playback_start_frame),
                    str(frame_pattern),
                ]
                if subprocess.run(frame_command, check=False).returncode != MVE_FFMPEG_SUCCESS:
                    raise Fo1ProfileError("Fallout 2 Elder frame decode failed")
                frame_paths = sorted(opening_directory.glob("frame-*.png"))
                if len(frame_paths) != playback_frame_count:
                    raise Fo1ProfileError("Fallout 2 Elder frame count drifted")
                opening_frames = []
                for offset, frame_path in enumerate(frame_paths):
                    source_frame = playback_start_frame + offset
                    expected_name = (
                        f"frame-{source_frame:0{MVE_FRAME_FILENAME_DIGITS}d}.png"
                    )
                    with Image.open(frame_path) as frame_image:
                        dimensions = frame_image.size
                    if frame_path.name != expected_name or dimensions != (
                        opening_probe["width"],
                        opening_probe["height"],
                    ):
                        raise Fo1ProfileError(
                            "Fallout 2 Elder frame identity or dimensions drifted"
                        )
                    relative = frame_path.relative_to(staging).as_posix()
                    opening_frames.append(
                        {
                            "sourceFrame": source_frame,
                            "png": relative,
                            "pngBytes": frame_path.stat().st_size,
                            "pngSha256": file_sha256(frame_path),
                        }
                    )
                terminal_hash = opening_frames[-MVE_SOURCE_FRAME_NUMBER_ORIGIN][
                    "pngSha256"
                ]
                terminal_repeated_from = source_frame_count
                for frame in reversed(opening_frames):
                    if frame["pngSha256"] != terminal_hash:
                        break
                    terminal_repeated_from = frame["sourceFrame"]

                frame_rate_numerator = int(
                    opening_recipe["video"]["frameRateNumerator"]
                )
                frame_rate_denominator = int(
                    opening_recipe["video"]["frameRateDenominator"]
                )
                frame_period = Fraction(frame_rate_denominator, frame_rate_numerator)
                audio_start = frame_period * decoded_start_index
                audio_duration = frame_period * playback_frame_count
                audio_path = staging / "assets" / "opening" / "elder.wav"
                audio_command = [
                    ffmpeg,
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-i",
                    str(source_mve_path),
                    "-ss",
                    f"{float(audio_start):.{MVE_TIMESTAMP_DECIMAL_DIGITS}f}",
                    "-t",
                    f"{float(audio_duration):.{MVE_TIMESTAMP_DECIMAL_DIGITS}f}",
                    "-map",
                    "0:a:0",
                    "-vn",
                    "-c:a",
                    "pcm_s16le",
                    "-ar",
                    str(opening_recipe["audio"]["sampleRate"]),
                    "-ac",
                    str(opening_recipe["audio"]["channels"]),
                    str(audio_path),
                ]
                if subprocess.run(audio_command, check=False).returncode != MVE_FFMPEG_SUCCESS:
                    raise Fo1ProfileError("Fallout 2 Elder tail audio decode failed")
                with wave.open(str(audio_path), "rb") as audio_stream:
                    audio_channels = audio_stream.getnchannels()
                    audio_sample_bytes = audio_stream.getsampwidth()
                    audio_sample_rate = audio_stream.getframerate()
                    audio_sample_frames = audio_stream.getnframes()
                expected_audio_frames = round(float(audio_duration) * audio_sample_rate)
                if (
                    audio_channels != opening_recipe["audio"]["channels"]
                    or audio_sample_bytes != opening_recipe["audio"]["sampleBytes"]
                    or audio_sample_rate != opening_recipe["audio"]["sampleRate"]
                    or audio_sample_frames != expected_audio_frames
                ):
                    raise Fo1ProfileError("Fallout 2 Elder tail PCM identity drifted")
                source_mve_path.unlink()
                opening_tail = {
                    "source": {
                        "movie": {
                            "logicalPath": opening_movie.logical_path,
                            "source": opening_movie.source,
                            "bytes": len(opening_movie.data),
                            "sha256": opening_movie.sha256,
                        },
                        "fadeConfig": {
                            "logicalPath": opening_fade_config.logical_path,
                            "source": opening_fade_config.source,
                            "bytes": len(opening_fade_config.data),
                            "sha256": opening_fade_config.sha256,
                        },
                    },
                    "video": {
                        **opening_probe,
                        "frameRateNumerator": frame_rate_numerator,
                        "frameRateDenominator": frame_rate_denominator,
                        "sourceFrameNumbersOneBased": True,
                        "playbackStartFrame": playback_start_frame,
                        "playbackFrameCount": playback_frame_count,
                        "tailStartFrame": tail_start_frame,
                        "terminalFrame": source_frame_count,
                        "terminalFramePngSha256": terminal_hash,
                        "terminalFrameRepeatedFrom": terminal_repeated_from,
                        "frames": opening_frames,
                    },
                    "audio": {
                        "wav": audio_path.relative_to(staging).as_posix(),
                        "wavBytes": audio_path.stat().st_size,
                        "wavSha256": file_sha256(audio_path),
                        "channels": audio_channels,
                        "sampleBytes": audio_sample_bytes,
                        "sampleRate": audio_sample_rate,
                        "sampleFrames": audio_sample_frames,
                        "sourceStartNumerator": audio_start.numerator,
                        "sourceStartDenominator": audio_start.denominator,
                        "sourceDurationNumerator": audio_duration.numerator,
                        "sourceDurationDenominator": audio_duration.denominator,
                    },
                    "fade": {
                        **opening_fade,
                        "movieEndForcesBlack": opening_recipe["fade"][
                            "movieEndForcesBlack"
                        ],
                    },
                    "handoff": opening_recipe["handoff"],
                }
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
            equipped = female["equippedWeapon"]
            weapon_code = int(equipped["weaponAnimationCode"])
            if (
                weapon_code >= len(CRITTER_WEAPON_ART_SUFFIXES)
                or CRITTER_WEAPON_ART_SUFFIXES[weapon_code]
                != equipped["weaponArtSuffix"]
            ):
                raise Fo1ProfileError(
                    "Fallout 2 female Spear animation code no longer resolves suffix g"
                )
            equipped_idle_path = (
                f"art\\critters\\{art_base}{equipped['idleAnimationCode'].casefold()}.frm"
            )
            if equipped_idle_path != equipped["idleLogicalPath"]:
                raise Fo1ProfileError("Fallout 2 female GA equipped-idle path drifted")
            female_equipped_idle_frm = resolver.read(equipped_idle_path)
            female_equipped_idle_decoded = decode_frm(
                female_equipped_idle_frm.data,
                colors,
            )
            if (
                female_equipped_idle_decoded["framesPerDirection"]
                <= equipped["idleFrame"]
                or len(female_equipped_idle_decoded["directions"])
                != len(female["directions"])
            ):
                raise Fo1ProfileError(
                    "Fallout 2 female GA equipped-idle contract drifted"
                )
            female_equipped_idle_artifacts = [
                _save_admitted_frame(
                    kind="female-player-equipped",
                    logical_path=equipped_idle_path,
                    source=female_equipped_idle_frm,
                    colors=colors,
                    rotation=direction,
                    frame_index=equipped["idleFrame"],
                    staging=staging,
                )
                for direction in female["directions"]
            ]
            equipped_walk_path = (
                f"art\\critters\\{art_base}{equipped['walkAnimationCode'].casefold()}.frm"
            )
            if equipped_walk_path != equipped["walkLogicalPath"]:
                raise Fo1ProfileError("Fallout 2 female GB equipped-walk path drifted")
            female_equipped_walk_frm = resolver.read(equipped_walk_path)
            female_equipped_walk_decoded = decode_frm(
                female_equipped_walk_frm.data,
                colors,
            )
            if (
                female_equipped_walk_decoded["fps"] != equipped["walkFps"]
                or female_equipped_walk_decoded["framesPerDirection"]
                != len(equipped["walkFrames"])
                or female_equipped_walk_decoded["actionFrame"] != 0
                or len(female_equipped_walk_decoded["directions"])
                != len(female["directions"])
            ):
                raise Fo1ProfileError(
                    "Fallout 2 female GB equipped-walk contract drifted"
                )
            female_equipped_walk_artifacts = [
                _save_admitted_frame(
                    kind="female-player-equipped-walk",
                    logical_path=equipped_walk_path,
                    source=female_equipped_walk_frm,
                    colors=colors,
                    rotation=direction,
                    frame_index=frame,
                    staging=staging,
                )
                for direction in female["directions"]
                for frame in equipped["walkFrames"]
            ]
            for artifact in (
                female_artifacts
                + female_walk_artifacts
                + female_equipped_idle_artifacts
                + female_equipped_walk_artifacts
            ):
                artifact["relief3d"] = derive_relief(
                    staging,
                    artifact,
                    female["relief3d"],
                    output_folder="female-player-relief3d",
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
                "equippedWeaponArt": {
                    "itemFid": equipped["itemFid"],
                    "itemPid": equipped["itemPid"],
                    "weaponAnimationCode": equipped["weaponAnimationCode"],
                    "weaponArtSuffix": equipped["weaponArtSuffix"],
                    "geometryDisposition": equipped["geometryDisposition"],
                    "idle": {
                        "animationCode": equipped["idleAnimationCode"],
                        "logicalPath": equipped_idle_path,
                        "source": female_equipped_idle_frm.source,
                        "sourceBytes": len(female_equipped_idle_frm.data),
                        "sourceSha256": female_equipped_idle_frm.sha256,
                        "fps": female_equipped_idle_decoded["fps"],
                        "actionFrame": female_equipped_idle_decoded["actionFrame"],
                        "framesPerDirection": female_equipped_idle_decoded[
                            "framesPerDirection"
                        ],
                        "directions": female_equipped_idle_artifacts,
                        "animationPlayback": False,
                    },
                    "walk": {
                        "animationCode": equipped["walkAnimationCode"],
                        "logicalPath": equipped_walk_path,
                        "source": female_equipped_walk_frm.source,
                        "sourceBytes": len(female_equipped_walk_frm.data),
                        "sourceSha256": female_equipped_walk_frm.sha256,
                        "fps": female_equipped_walk_decoded["fps"],
                        "actionFrame": female_equipped_walk_decoded["actionFrame"],
                        "framesPerDirection": female_equipped_walk_decoded[
                            "framesPerDirection"
                        ],
                        "directions": female_equipped_walk_artifacts,
                        "animationPlayback": True,
                    },
                },
            },
            "resources": resources,
            "counts": {
                "premades": len(characters),
                "uiPngs": 2 + len(characters),
                "femaleDirectionPngs": len(female_artifacts),
                "femaleWalkFramePngs": len(female_walk_artifacts),
                "femaleEquippedIdleDirectionPngs": len(
                    female_equipped_idle_artifacts
                ),
                "femaleEquippedWalkFramePngs": len(
                    female_equipped_walk_artifacts
                ),
                "femaleClosedReliefArtifacts": len(female_artifacts)
                + len(female_walk_artifacts)
                + len(female_equipped_idle_artifacts)
                + len(female_equipped_walk_artifacts),
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
                    "The three owned premades, inventory background, and female unarmed "
                    "AA/AB plus Spear-equipped GA/GB states are decoded; the composited "
                    "FRM has no separable 3D weapon transform."
                ),
            },
            "cachePolicy": {
                "disposition": "disposable-local-only",
                "containsDerivedOwnedPixels": True,
                "distributionAllowed": False,
            },
            "retailOrDerivedAssetsPackaged": False,
        }
        if opening_tail is not None:
            document["openingTail"] = opening_tail
            document["counts"]["openingTailPngs"] = len(opening_frames)
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
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--ffprobe", default="ffprobe")
    args = parser.parse_args()
    try:
        document = prepare_fo2_character_start(
            args.profile,
            args.output_root,
            args.recipe,
            args.ffmpeg,
            args.ffprobe,
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
                "openingTailPngs": document["counts"].get("openingTailPngs", 0),
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
