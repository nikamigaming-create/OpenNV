"""Prepare the Fallout 1 creator and Overseer opening from owned local data."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import struct
import subprocess
import tempfile
from pathlib import Path

from PIL import Image

from dat1_archive import Dat1Archive
from fo1_frm import decode_frm
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_04C11DB7 = 0x04C11DB7
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_41414646 = 0x41414646
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_4F4E5631 = 0x4F4E5631
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_80000000 = 0x80000000
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_FFFFFFFF = 0xFFFFFFFF
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_10 = 10
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_100 = 100
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_FLOAT_100POINT0 = 100.0
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_100000 = 100_000
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_1024 = 1024
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_103 = 103
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_104 = 104
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_106 = 106
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_11 = 11
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_12 = 12
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_128 = 128
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_FLOAT_130POINT0 = 130.0
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_14 = 14
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_15 = 15
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_FLOAT_15POINT0 = 15.0
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_16 = 16
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_1000000 = 1_000_000
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_1500 = 1_500
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_2060 = 2060
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_22 = 22
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_24 = 24
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_255 = 255
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_256 = 256
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_26 = 26
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_27 = 27
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_32 = 32
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_320 = 320
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_32768 = 32768
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_34 = 34
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_35 = 35
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_368 = 368
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_40 = 40
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_400 = 400
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_428 = 428
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_432 = 432
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_63 = 63
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_7 = 7
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_768 = 768
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_8 = 8
PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_80 = 80



RECIPE_SCHEMA = "opennv-fo1-character-start-recipe/v1"
MANIFEST_SCHEMA = "opennv-fo1-character-start/v1"
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


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_1024 * PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def colors_from_palette(data: bytes) -> list[tuple[int, int, int, int]]:
    if len(data) < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_768:
        raise ValueError("Fallout palette requires at least 768 bytes")
    values = data[:PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_768]
    colors = []
    for index in range(PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_256):
        red, green, blue = values[index * 3 : index * 3 + 3]
        if red <= PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_63 and green <= PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_63 and blue <= PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_63:
            red, green, blue = red * 4, green * 4, blue * 4
        else:
            red, green, blue = 0, 0, 0
        colors.append(
            (
                red,
                green,
                blue,
                0 if index == 0 else PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_255,
            )
        )
    return colors


def color_table_rgb(data: bytes, color_table_index: int) -> tuple[int, int, int]:
    table_offset = PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_768
    table_size = PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_32768
    if len(data) < table_offset + table_size:
        raise ValueError("Fallout COLOR.PAL is missing its 15-bit color table")
    if color_table_index < 0 or color_table_index >= table_size:
        raise ValueError(f"Fallout color-table index is invalid: {color_table_index}")
    palette_index = data[table_offset + color_table_index]
    red, green, blue = data[palette_index * 3 : palette_index * 3 + 3]
    if red > PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_63 or green > PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_63 or blue > PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_63:
        raise ValueError(
            f"Fallout color-table entry {color_table_index} resolves to an invalid palette color"
        )
    return red * 4, green * 4, blue * 4


def decode_aaf_font(
    data: bytes,
    destination: Path,
    tint_rgb: tuple[int, int, int],
) -> dict[str, object]:
    """Decode one Fallout AAFF bitmap font into a deterministic 16x16 atlas."""
    glyph_data_offset = PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_2060
    if len(data) < glyph_data_offset or struct.unpack_from(">I", data, 0)[0] != PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_41414646:
        raise ValueError("Fallout bitmap font has an invalid or truncated AAFF header")
    maximum_height, letter_spacing, word_spacing, line_spacing = struct.unpack_from(
        ">4h", data, 4
    )
    if maximum_height <= 0 or maximum_height > PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_128:
        raise ValueError(f"Fallout bitmap font maximum height is invalid: {maximum_height}")

    records: list[tuple[int, int, int]] = []
    for code_point in range(PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_256):
        width, height, offset = struct.unpack_from(">hhI", data, PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_12 + code_point * PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_8)
        if width < 0 or height < 0 or width > PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_128 or height > maximum_height:
            raise ValueError(f"Fallout bitmap glyph {code_point} has invalid dimensions")
        size = width * height
        if offset > len(data) - glyph_data_offset or size > len(data) - glyph_data_offset - offset:
            raise ValueError(f"Fallout bitmap glyph {code_point} escapes the AAFF payload")
        records.append((width, height, offset))

    cell_width = max(width for width, _, _ in records)
    if cell_width <= 0:
        raise ValueError("Fallout bitmap font has no visible glyphs")
    atlas = Image.new("RGBA", (cell_width * PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_16, maximum_height * PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_16), (0, 0, 0, 0))
    for code_point, (width, height, offset) in enumerate(records):
        source = data[glyph_data_offset + offset : glyph_data_offset + offset + width * height]
        if any(value > PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_7 for value in source):
            raise ValueError(f"Fallout bitmap glyph {code_point} has an invalid intensity")
        left = (code_point % PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_16) * cell_width
        top = (code_point // PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_16) * maximum_height + maximum_height - height
        for y in range(height):
            for x in range(width):
                intensity = source[y * width + x]
                if intensity:
                    atlas.putpixel(
                        (left + x, top + y),
                        (*tint_rgb, round(intensity * PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_255 / PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_7)),
                    )
    atlas.save(destination)
    return {
        "atlasPng": destination,
        "atlasPngSha256": sha256_path(destination),
        "atlasWidth": atlas.width,
        "atlasHeight": atlas.height,
        "cellWidth": cell_width,
        "maximumHeight": maximum_height,
        "letterSpacing": letter_spacing,
        "wordSpacing": word_spacing,
        "lineSpacing": line_spacing,
        "glyphWidths": [width for width, _, _ in records],
        "glyphHeights": [height for _, height, _ in records],
        "tintRgb": list(tint_rgb),
    }


def parse_timing(data: bytes, units_per_second: int) -> list[dict[str, object]]:
    try:
        text = data.decode("cp1252", errors="strict")
    except UnicodeDecodeError as error:
        raise ValueError("Fallout Overseer timing is not valid Windows text") from error
    rows = []
    previous_tick = -1
    for line_number, line in enumerate(text.splitlines(), start=1):
        if not line.strip():
            continue
        tick_text, separator, subtitle = line.partition(":")
        if not separator or not tick_text.isdigit():
            raise ValueError(f"invalid Overseer timing row {line_number}: {line!r}")
        tick = int(tick_text)
        if tick <= previous_tick:
            raise ValueError("Overseer timing rows are not strictly increasing")
        previous_tick = tick
        rows.append(
            {
                "tick": tick,
                "seconds": tick / units_per_second,
                "text": subtitle,
            }
        )
    if len(rows) < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_10:
        raise ValueError("Overseer timing contract is unexpectedly short")
    return rows


def normalize_ogg_serial(path: Path, serial: int) -> None:
    data = bytearray(path.read_bytes())
    cursor = 0
    while cursor < len(data):
        if cursor + PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_27 > len(data) or data[cursor : cursor + 4] != b"OggS":
            raise ValueError("Fallout Overseer Ogg page is truncated or invalid")
        segment_count = data[cursor + PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_26]
        table_end = cursor + PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_27 + segment_count
        if table_end > len(data):
            raise ValueError("Fallout Overseer Ogg segment table is truncated")
        page_end = table_end + sum(data[cursor + PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_27 : table_end])
        if page_end > len(data):
            raise ValueError("Fallout Overseer Ogg page escapes the file")
        struct.pack_into("<I", data, cursor + PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_14, serial)
        struct.pack_into("<I", data, cursor + PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_22, 0)
        checksum = 0
        for value in data[cursor:page_end]:
            checksum ^= value << PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_24
            for _ in range(PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_8):
                checksum = (
                    ((checksum << 1) ^ PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_04C11DB7)
                    if checksum & PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_80000000
                    else checksum << 1
                ) & PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_FFFFFFFF
        struct.pack_into("<I", data, cursor + PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_22, checksum)
        cursor = page_end
    path.write_bytes(data)


def verified_member(archive: Dat1Archive, source: dict[str, object]):
    member = archive.extract(str(source["logicalPath"]))
    if member.sha256 != source["sha256"]:
        raise ValueError(
            f"owned Fallout member hash mismatch for {source['logicalPath']}: {member.sha256}"
        )
    return member


def parse_premade_gcd(data: bytes) -> dict[str, object]:
    """Decode the bounded Fallout 1 premade fields used by the picker.

    Fallout's three shipped GCD files are 107 big-endian signed 32-bit slots.
    The parser intentionally admits only the identity, allocated SPECIAL, age,
    sex, tag-skill, and trait fields needed by this slice.
    """
    if len(data) != PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_428:
        raise ValueError(f"Fallout premade GCD must be exactly 428 bytes, got {len(data)}")
    values = struct.unpack(">107i", data)
    name_bytes = data[PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_368:PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_400]
    terminator = name_bytes.find(b"\x00")
    if terminator <= 0:
        raise ValueError("Fallout premade GCD has no bounded character name")
    try:
        name = name_bytes[:terminator].decode("cp1252", errors="strict")
    except UnicodeDecodeError as error:
        raise ValueError("Fallout premade GCD name is not valid Windows text") from error
    if not name.strip() or len(name) > PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_11 or any(ord(value) < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_32 for value in name):
        raise ValueError(f"Fallout premade GCD name is invalid: {name!r}")

    special_values = list(values[1:PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_8])
    if any(value < 1 or value > PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_10 for value in special_values) or sum(special_values) != PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_40:
        raise ValueError("Fallout premade GCD has invalid allocated SPECIAL values")
    age = values[PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_34]
    sex_index = values[PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_35]
    if age < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_16 or age > PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_35 or sex_index not in (0, 1):
        raise ValueError("Fallout premade GCD has an invalid age or sex")

    tag_indices = list(values[PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_100:PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_103])
    if (
        values[PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_103] != -1
        or len(set(tag_indices)) != 3
        or any(index < 0 or index >= len(SKILL_NAMES) for index in tag_indices)
    ):
        raise ValueError("Fallout premade GCD has invalid tagged-skill slots")
    raw_traits = list(values[PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_104:PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_106])
    trait_indices = [index for index in raw_traits if index != -1]
    if (
        values[PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_106] != 0
        or len(set(trait_indices)) != len(trait_indices)
        or any(index < 0 or index >= len(TRAIT_NAMES) for index in trait_indices)
    ):
        raise ValueError("Fallout premade GCD has invalid trait slots")

    return {
        "name": name,
        "age": age,
        "sex": "Female" if sex_index == 1 else "Male",
        "allocatedSpecial": special_values,
        "taggedSkillIndices": tag_indices,
        "taggedSkills": [SKILL_NAMES[index] for index in tag_indices],
        "traitIndices": trait_indices,
        "traits": [TRAIT_NAMES[index] for index in trait_indices],
    }


def decode_interface_frame(
    member,
    source: dict[str, object],
    colors: list[tuple[int, int, int, int]],
    destination: Path,
    label: str,
    *,
    frame_index: int = 0,
    opaque: bool = False,
) -> dict[str, object]:
    decoded = decode_frm(member.data, colors)
    frames = decoded["directions"][0]["frames"]
    if frame_index < 0 or frame_index >= len(frames):
        raise ValueError(f"Fallout {label} frame {frame_index} is unavailable")
    frame = frames[frame_index]
    width = int(frame["width"])
    height = int(frame["height"])
    expected_width = int(source["width"])
    expected_height = int(source["height"])
    if (width, height) != (expected_width, expected_height):
        raise ValueError(
            f"Fallout {label} is {width}x{height}, expected "
            f"{expected_width}x{expected_height}"
        )
    image = frame["image"].copy()
    if opaque:
        image.putalpha(PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_255)
    image.save(destination)
    return {
        "png": destination,
        "pngSha256": sha256_path(destination),
        "width": width,
        "height": height,
        "sourceFrmSha256": member.sha256,
        "frameIndex": frame_index,
        "opaque": opaque,
    }


def prepare(
    recipe_path: Path,
    fallout_master: Path,
    manual_path: Path,
    output_root: Path,
    ffmpeg: str,
    ffprobe: str,
) -> dict[str, object]:
    recipe = json.loads(recipe_path.read_text(encoding="utf-8"))
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise ValueError(f"unexpected Fallout character-start recipe: {recipe_path}")
    source = recipe["source"]
    if sha256_path(fallout_master) != source["falloutMasterSha256"]:
        raise ValueError("owned Fallout MASTER.DAT hash does not match the recipe")
    if sha256_path(manual_path) != source["manualSha256"]:
        raise ValueError("owned Fallout manual hash does not match the recipe")
    if output_root.exists():
        raise FileExistsError(f"refusing to replace existing character-start cache: {output_root}")

    archive = Dat1Archive(fallout_master)
    palette = verified_member(archive, source["palette"])
    creator = verified_member(archive, source["creatorChrome"])
    creator_numbers = verified_member(archive, source["creatorNumbers"])
    picker = verified_member(archive, source["characterPicker"])
    movie = verified_member(archive, source["overseerMovie"])
    transcript = verified_member(archive, source["overseerText"])
    timing = verified_member(archive, source["overseerTiming"])
    timing_rows = parse_timing(timing.data, int(source["overseerTiming"]["unitsPerSecond"]))
    if (
        recipe["creation"]["special"] != SPECIAL_NAMES
        or recipe["creation"]["skills"] != SKILL_NAMES
        or recipe["creation"]["traits"] != TRAIT_NAMES
    ):
        raise ValueError("Fallout character recipe vocabulary drifted from the GCD parser")

    output_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{output_root.name}-", dir=output_root.parent))
    try:
        colors = colors_from_palette(palette.data)
        chrome_path = staging / "EDTRCRTE.png"
        creator_asset = decode_interface_frame(
            creator,
            source["creatorChrome"],
            colors,
            chrome_path,
            "creator chrome",
            opaque=True,
        )
        width = int(creator_asset["width"])
        height = int(creator_asset["height"])
        creator_numbers_path = staging / "BIGNUM.png"
        creator_numbers_asset = decode_interface_frame(
            creator_numbers,
            source["creatorNumbers"],
            colors,
            creator_numbers_path,
            "creator number atlas",
            opaque=True,
        )
        picker_path = staging / "PICKCHAR.png"
        picker_asset = decode_interface_frame(
            picker,
            source["characterPicker"],
            colors,
            picker_path,
            "character picker",
            opaque=True,
        )

        premade_rows = []
        expected_premade_ids = ["max-stone", "natalia", "albert"]
        configured_premades = source["premadeCharacters"]
        if [row["id"] for row in configured_premades] != expected_premade_ids:
            raise ValueError("Fallout premade recipe must contain Max Stone, Natalia, and Albert")
        for premade_source in configured_premades:
            premade_id = str(premade_source["id"])
            gcd = verified_member(archive, premade_source["gcd"])
            bio = verified_member(archive, premade_source["bio"])
            portrait = verified_member(archive, premade_source["portrait"])
            profile = parse_premade_gcd(gcd.data)
            try:
                bio_text = bio.data.decode("cp1252", errors="strict").replace("\r\n", "\n").strip()
            except UnicodeDecodeError as error:
                raise ValueError(f"Fallout premade bio is invalid: {premade_id}") from error
            if len(bio_text) < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_80:
                raise ValueError(f"Fallout premade bio is unexpectedly short: {premade_id}")
            gcd_path = staging / f"{premade_id}.GCD"
            gcd_path.write_bytes(gcd.data)
            bio_path = staging / f"{premade_id}.BIO"
            bio_path.write_bytes(bio.data)
            portrait_path = staging / f"{premade_id}.png"
            portrait_asset = decode_interface_frame(
                portrait,
                premade_source["portrait"],
                colors,
                portrait_path,
                f"{premade_id} portrait",
            )
            premade_rows.append(
                {
                    "id": premade_id,
                    "role": premade_source["role"],
                    "gcd": str((output_root / gcd_path.name).resolve()),
                    "gcdSha256": gcd.sha256,
                    "bio": str((output_root / bio_path.name).resolve()),
                    "bioSha256": bio.sha256,
                    "bioText": bio_text,
                    "portraitPng": str((output_root / portrait_path.name).resolve()),
                    "portraitPngSha256": portrait_asset["pngSha256"],
                    "portraitWidth": portrait_asset["width"],
                    "portraitHeight": portrait_asset["height"],
                    "sourcePortraitFrmSha256": portrait.sha256,
                    "profile": profile,
                }
            )

        pip_boy_assets = {}
        for asset_id, (filename, opaque) in {
            "main": ("PIP.png", True),
            "sidePanel": ("PIP2.png", False),
            "upButton": ("PIPUP.png", True),
            "downButton": ("PIPDN.png", True),
            "screensaver": ("PIPX.png", False),
        }.items():
            asset_source = source["pipBoy"][asset_id]
            member = verified_member(archive, asset_source)
            asset_path = staging / filename
            asset = decode_interface_frame(
                member,
                asset_source,
                colors,
                asset_path,
                f"Pip-Boy {asset_id}",
                opaque=opaque,
            )
            pip_boy_assets[asset_id] = {
                "png": str((output_root / filename).resolve()),
                "pngSha256": asset["pngSha256"],
                "width": asset["width"],
                "height": asset["height"],
                "sourceFrmSha256": member.sha256,
            }
        pip_messages = verified_member(archive, source["pipBoy"]["messages"])
        pip_messages_path = staging / "PIPBOY.MSG"
        pip_messages_path.write_bytes(pip_messages.data)

        interface_assets = {}
        for asset_id, (filename, opaque) in {
            "main": ("IFACE.png", True),
            "numbers": ("NUMBERS.png", True),
            "actionPointGreen": ("HLGRN.png", True),
            "actionPointYellow": ("HLYEL.png", True),
            "actionPointRed": ("HLRED.png", True),
            "endWindow": ("ENDANIM-open.png", True),
            "endTurn": ("ENDTURNU.png", True),
            "endCombat": ("ENDCMBTU.png", True),
            "endLightGreen": ("ENDLTGRN.png", False),
            "endLightRed": ("ENDLTRED.png", False),
            "itemPanel": ("SATTKBUP.png", True),
            "singleAttack": ("SINGLE.png", False),
            "movePoints": ("MVEPNT.png", False),
            "moveNumbers": ("MVENUM.png", False),
            "inventoryButton": ("INVBUTUP.png", True),
            "optionsButton": ("OPTIUP.png", True),
            "redButton": ("BIGREDUP.png", False),
            "automapButton": ("MAPUP.png", False),
            "characterButton": ("CHAUP.png", True),
            "pipBoyButton": ("PIPUP-HUD.png", True),
        }.items():
            asset_source = source["interfaceHud"][asset_id]
            member = verified_member(archive, asset_source)
            asset_path = staging / filename
            asset = decode_interface_frame(
                member,
                asset_source,
                colors,
                asset_path,
                f"gameplay interface {asset_id}",
                frame_index=int(asset_source.get("frameIndex", 0)),
                opaque=opaque,
            )
            interface_assets[asset_id] = {
                "png": str((output_root / filename).resolve()),
                "pngSha256": asset["pngSha256"],
                "width": asset["width"],
                "height": asset["height"],
                "sourceFrmSha256": member.sha256,
            }

        weapon_inventory_assets = {}
        for symbol, asset_source in sorted(
            source["interfaceHud"]["weaponInventoryBySymbol"].items()
        ):
            member = verified_member(archive, asset_source)
            filename = f"INVEN-{symbol}.png"
            asset_path = staging / filename
            asset = decode_interface_frame(
                member,
                asset_source,
                colors,
                asset_path,
                f"gameplay interface weapon {symbol}",
            )
            weapon_inventory_assets[symbol] = {
                "png": str((output_root / filename).resolve()),
                "pngSha256": asset["pngSha256"],
                "width": asset["width"],
                "height": asset["height"],
                "sourceFrmSha256": member.sha256,
            }

        classic_inventory_source = source["classicInventory"]
        classic_inventory_assets = {}
        for asset_id, filename in {
            "background": "INVBOX.png",
            "scrollUp": "INVUPOUT.png",
            "scrollDown": "INVDNOUT.png",
        }.items():
            asset_source = classic_inventory_source[asset_id]
            member = verified_member(archive, asset_source)
            asset_path = staging / filename
            asset = decode_interface_frame(
                member,
                asset_source,
                colors,
                asset_path,
                f"classic inventory {asset_id}",
                opaque=asset_id == "background",
            )
            classic_inventory_assets[asset_id] = {
                "png": str((output_root / filename).resolve()),
                "pngSha256": asset["pngSha256"],
                "width": asset["width"],
                "height": asset["height"],
                "sourceFrmSha256": member.sha256,
            }

        classic_inventory_item_assets = {}
        for symbol, asset_source in sorted(
            classic_inventory_source["itemInventoryBySymbol"].items()
        ):
            member = verified_member(archive, asset_source)
            filename = f"INVBOX-{symbol}.png"
            asset_path = staging / filename
            asset = decode_interface_frame(
                member,
                asset_source,
                colors,
                asset_path,
                f"classic inventory item {symbol}",
            )
            classic_inventory_item_assets[symbol] = {
                "png": str((output_root / filename).resolve()),
                "pngSha256": asset["pngSha256"],
                "width": asset["width"],
                "height": asset["height"],
                "sourceFrmSha256": member.sha256,
            }

        font_source = source["interfaceHud"]["messageFont"]
        font_member = verified_member(archive, font_source)
        message_color = color_table_rgb(
            palette.data,
            int(font_source["colorTableIndex"]),
        )
        font_path = staging / "FONT1-green.png"
        font_asset = decode_aaf_font(font_member.data, font_path, message_color)
        interface_font = {
            "atlasPng": str((output_root / font_path.name).resolve()),
            "atlasPngSha256": font_asset["atlasPngSha256"],
            "atlasWidth": font_asset["atlasWidth"],
            "atlasHeight": font_asset["atlasHeight"],
            "cellWidth": font_asset["cellWidth"],
            "maximumHeight": font_asset["maximumHeight"],
            "letterSpacing": font_asset["letterSpacing"],
            "wordSpacing": font_asset["wordSpacing"],
            "lineSpacing": font_asset["lineSpacing"],
            "glyphWidths": font_asset["glyphWidths"],
            "glyphHeights": font_asset["glyphHeights"],
            "tintRgb": font_asset["tintRgb"],
            "sourceAafSha256": font_member.sha256,
            "colorTableIndex": font_source["colorTableIndex"],
        }

        mve_path = staging / "OVRINTRO.MVE"
        mve_path.write_bytes(movie.data)
        frames_directory = staging / "overseer-frames"
        frames_directory.mkdir()
        frame_command = [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(mve_path),
            "-map",
            "0:v:0",
            "-an",
            "-vf",
            "fps=15",
            "-q:v",
            "3",
            "-start_number",
            "0",
            str(frames_directory / "frame-%05d.jpg"),
        ]
        subprocess.run(frame_command, check=True)
        frame_paths = sorted(frames_directory.glob("frame-*.jpg"))
        if len(frame_paths) < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_1500:
            raise ValueError(
                f"Fallout Overseer conversion produced only {len(frame_paths)} frames"
            )
        with Image.open(frame_paths[0]) as first_frame:
            movie_width, movie_height = first_frame.size
        if (movie_width, movie_height) != (PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_432, PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_320):
            raise ValueError(
                f"Fallout Overseer frames are {movie_width}x{movie_height}, expected 432x320"
            )
        repair = source["playbackRepair"]
        if int(repair["framesPerSecond"]) != PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_15:
            raise ValueError("Fallout Overseer repair contract has an unexpected frame rate")
        repaired_frames = []
        for first, last in repair["repeatPreviousFrameRanges"]:
            if first < 1 or last < first or last >= len(frame_paths):
                raise ValueError("Fallout Overseer repair range escapes the decoded frame set")
            replacement = frame_paths[first - 1].read_bytes()
            for frame_index in range(first, last + 1):
                frame_paths[frame_index].write_bytes(replacement)
                repaired_frames.append(frame_index)
        frame_pack_path = staging / "OVRINTRO.frames"
        with frame_pack_path.open("wb") as stream:
            stream.write(b"ONVFO1M1")
            stream.write(struct.pack("<IIIII", movie_width, movie_height, PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_15, 1, len(frame_paths)))
            for frame_path in frame_paths:
                frame_data = frame_path.read_bytes()
                if len(frame_data) < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_256:
                    raise ValueError(f"Fallout Overseer JPEG frame is invalid: {frame_path.name}")
                stream.write(struct.pack("<I", len(frame_data)))
                stream.write(frame_data)
        shutil.rmtree(frames_directory)

        audio_path = staging / "OVRINTRO.ogg"
        audio_command = [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(mve_path),
            "-map",
            "0:a:0",
            "-vn",
            "-c:a",
            "libvorbis",
            "-q:a",
            "5",
            str(audio_path),
        ]
        subprocess.run(audio_command, check=True)
        normalize_ogg_serial(audio_path, PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_HEX_4F4E5631)
        duration_result = subprocess.run(
            [
                ffprobe,
                "-v",
                "error",
                "-show_entries",
                "format=duration",
                "-of",
                "default=noprint_wrappers=1:nokey=1",
                str(audio_path),
            ],
            check=True,
            capture_output=True,
            text=True,
        )
        audio_duration_seconds = float(duration_result.stdout.strip())
        if not PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_FLOAT_100POINT0 < audio_duration_seconds < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_FLOAT_130POINT0:
            raise ValueError(
                f"Fallout Overseer audio duration is invalid: {audio_duration_seconds}"
            )
        if frame_pack_path.stat().st_size < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_1000000 or audio_path.stat().st_size < PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_100000:
            raise ValueError("Fallout Overseer deterministic playback assets are incomplete")

        transcript_path = staging / "OVRINTRO.TXT"
        transcript_path.write_bytes(transcript.data)
        timing_path = staging / "OVRINTRO.SVE"
        timing_path.write_bytes(timing.data)
        manifest = {
            "schema": MANIFEST_SCHEMA,
            "status": "prepared-owned-data",
            "recipe": {"id": recipe["id"], "sha256": sha256_path(recipe_path)},
            "source": {
                "falloutMaster": str(fallout_master.resolve()),
                "falloutMasterSha256": source["falloutMasterSha256"],
                "manual": str(manual_path.resolve()),
                "manualSha256": source["manualSha256"],
                "creatorFrmSha256": creator.sha256,
                "creatorNumbersFrmSha256": creator_numbers.sha256,
                "characterPickerFrmSha256": picker.sha256,
                "overseerMveSha256": movie.sha256,
                "overseerTextSha256": transcript.sha256,
                "overseerTimingSha256": timing.sha256,
            },
            "creator": {
                "chromePng": str((output_root / chrome_path.name).resolve()),
                "chromePngSha256": sha256_path(chrome_path),
                "width": width,
                "height": height,
                "dynamicNumbers": {
                    "atlasPng": str((output_root / creator_numbers_path.name).resolve()),
                    "atlasPngSha256": creator_numbers_asset["pngSha256"],
                    "width": creator_numbers_asset["width"],
                    "height": creator_numbers_asset["height"],
                    "sourceFrmSha256": creator_numbers.sha256,
                    "digitWidth": source["creatorNumbers"]["digitWidth"],
                    "specialDigitStride": source["creatorNumbers"]["specialDigitStride"],
                    "whiteOffsetX": source["creatorNumbers"]["whiteOffsetX"],
                    "layout": source["creatorNumbers"]["layout"],
                },
                "rules": recipe["creation"],
            },
            "characterPicker": {
                "chromePng": str((output_root / picker_path.name).resolve()),
                "chromePngSha256": picker_asset["pngSha256"],
                "width": picker_asset["width"],
                "height": picker_asset["height"],
                "premadeCharacters": premade_rows,
                "customCharacter": {
                    "id": "custom",
                    "creatorChromeSha256": creator.sha256,
                },
            },
            "pipBoy": {
                "model": "Pip-Boy 2000",
                "assets": pip_boy_assets,
                "messages": str((output_root / pip_messages_path.name).resolve()),
                "messagesSha256": pip_messages.sha256,
                "pages": ["STATUS", "AUTOMAPS", "ARCHIVES"],
            },
            "interfaceHud": {
                "source": "owned Fallout 1 ART/INTRFACE FRMs",
                "assets": interface_assets,
                "weaponInventoryBySymbol": weapon_inventory_assets,
                "messageFont": interface_font,
                "layout": source["interfaceHud"]["layout"],
                "pipBoyAccess": "P key or PIP control",
            },
            "classicInventory": {
                "source": "owned Fallout 1 ART/INTRFACE/INVBOX.FRM and ART/INVEN FRMs",
                "assets": classic_inventory_assets,
                "itemInventoryBySymbol": classic_inventory_item_assets,
                "messageFont": interface_font,
                "input": classic_inventory_source["input"],
                "layout": classic_inventory_source["layout"],
                "stateAuthority": "Fo1TacticalSession inventoryObjects",
            },
            "opening": {
                "sourceMve": str((output_root / mve_path.name).resolve()),
                "sourceMveSha256": movie.sha256,
                "playbackFrames": str((output_root / frame_pack_path.name).resolve()),
                "playbackFramesSha256": sha256_path(frame_pack_path),
                "playbackAudio": str((output_root / audio_path.name).resolve()),
                "playbackAudioSha256": sha256_path(audio_path),
                "width": movie_width,
                "height": movie_height,
                "framesPerSecond": PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_INTEGER_15,
                "frameCount": len(frame_paths),
                "durationSeconds": len(frame_paths) / PREPARE_FO1_CHARACTER_START_COMPILER_CONTRACT_FLOAT_15POINT0,
                "playbackDurationSeconds": audio_duration_seconds,
                "decodedFrameRepair": {
                    "reason": repair["reason"],
                    "repairedFrames": repaired_frames,
                    "sourceMveUnchanged": True,
                },
                "transcript": str((output_root / transcript_path.name).resolve()),
                "transcriptSha256": transcript.sha256,
                "timing": str((output_root / timing_path.name).resolve()),
                "timingSha256": timing.sha256,
                "timingRows": timing_rows,
            },
            "handoff": recipe["handoff"],
            "supported": recipe["supported"],
            "unsupported": recipe["unsupported"],
            "retailOrDerivedAssetsPackaged": False,
        }
        manifest_path = staging / "character-start.json"
        manifest_path.write_text(
            json.dumps(manifest, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        os.replace(staging, output_root)
        return {
            "schema": MANIFEST_SCHEMA,
            "status": "prepared-owned-data",
            "manifest": str((output_root / "character-start.json").resolve()),
            "manifestSha256": sha256_path(output_root / "character-start.json"),
            "creatorChromeSha256": manifest["creator"]["chromePngSha256"],
            "creatorNumbersSha256": manifest["creator"]["dynamicNumbers"]["atlasPngSha256"],
            "characterPickerSha256": manifest["characterPicker"]["chromePngSha256"],
            "premadeCharacters": [row["profile"]["name"] for row in premade_rows],
            "pipBoyMainSha256": manifest["pipBoy"]["assets"]["main"]["pngSha256"],
            "interfaceHudSha256": manifest["interfaceHud"]["assets"]["main"]["pngSha256"],
            "interfaceWeaponSymbols": sorted(manifest["interfaceHud"]["weaponInventoryBySymbol"]),
            "classicInventorySha256": manifest["classicInventory"]["assets"]["background"]["pngSha256"],
            "classicInventorySymbols": sorted(manifest["classicInventory"]["itemInventoryBySymbol"]),
            "interfaceFontSha256": manifest["interfaceHud"]["messageFont"]["atlasPngSha256"],
            "openingFramesSha256": manifest["opening"]["playbackFramesSha256"],
            "openingAudioSha256": manifest["opening"]["playbackAudioSha256"],
            "entryTile": recipe["handoff"]["tile"],
        }
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--fallout-master", type=Path, required=True)
    parser.add_argument("--manual", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--ffmpeg", default=shutil.which("ffmpeg") or "ffmpeg")
    parser.add_argument("--ffprobe", default=shutil.which("ffprobe") or "ffprobe")
    args = parser.parse_args()
    result = prepare(
        args.recipe.resolve(),
        args.fallout_master.resolve(),
        args.manual.resolve(),
        args.output_root.resolve(),
        args.ffmpeg,
        args.ffprobe,
    )
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
