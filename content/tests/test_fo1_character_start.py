from __future__ import annotations

import json
import sys
import struct
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(TOOLS))

from prepare_fo1_character_start import (  # noqa: E402
    color_table_rgb,
    colors_from_palette,
    decode_aaf_font,
    normalize_ogg_serial,
    parse_premade_gcd,
    parse_timing,
)


class Fo1CharacterStartTest(unittest.TestCase):
    def test_recipe_uses_owned_dynamic_numbers_and_symbol_keyed_weapon_art(self) -> None:
        recipe = json.loads(
            (ROOT / "content" / "recipes" / "fo1-character-start-v1.json").read_text(
                encoding="utf-8"
            )
        )
        numbers = recipe["source"]["creatorNumbers"]
        self.assertEqual(numbers["logicalPath"], "ART/INTRFACE/BIGNUM.FRM")
        self.assertEqual((numbers["width"], numbers["height"]), (336, 24))
        self.assertEqual(numbers["digitWidth"], 14)
        self.assertEqual(numbers["specialDigitStride"], 18)
        self.assertEqual(len(numbers["layout"]["special"]), 7)
        self.assertEqual(len(numbers["layout"]["specialIncrease"]), 7)
        self.assertEqual(len(numbers["layout"]["specialDecrease"]), 7)
        self.assertEqual(len(numbers["layout"]["characterPoints"]), 2)

        hud = recipe["source"]["interfaceHud"]
        self.assertNotIn("weaponInventory", hud)
        self.assertEqual(
            set(hud["weaponInventoryBySymbol"]),
            {"PID_10MM_PISTOL", "PID_KNIFE"},
        )
        self.assertEqual(
            hud["weaponInventoryBySymbol"]["PID_KNIFE"]["logicalPath"],
            "ART/INVEN/KNIFE.FRM",
        )

        inventory = recipe["source"]["classicInventory"]
        self.assertEqual(
            inventory["background"]["logicalPath"],
            "ART/INTRFACE/INVBOX.FRM",
        )
        self.assertEqual((inventory["background"]["width"], inventory["background"]["height"]), (499, 377))
        self.assertEqual(inventory["input"]["physicalKey"], "I")
        self.assertEqual(
            set(inventory["itemInventoryBySymbol"]),
            {
                "PID_KNIFE",
                "PID_10MM_PISTOL",
                "PID_10MM_JHP",
                "PID_STIMPAK",
                "PID_FLARE",
                "PID_LOCKPICKS",
                "PID_BRASS_KNUCKLES",
                "PID_FIRST_AID_KIT",
                "PID_THROWING_KNIFE",
                "PID_MULTI_TOOL",
                "PID_WATER_FLASK",
                "PID_MENTATS",
                "PID_BUFFOUT",
            },
        )
        self.assertEqual(
            inventory["itemInventoryBySymbol"]["PID_KNIFE"]["logicalPath"],
            "ART/INVEN/OKNIFE.FRM",
        )

    @staticmethod
    def _premade_gcd() -> bytes:
        values = [0] * 107
        values[1:8] = [8, 4, 9, 4, 4, 7, 4]
        values[34] = 23
        values[35] = 0
        values[100:103] = [0, 1, 3]
        values[103] = -1
        values[104:106] = [6, 1]
        values[106] = 0
        data = bytearray(struct.pack(">107i", *values))
        data[368:400] = b"Max Stone\x00" + b"\x00" * 22
        return bytes(data)

    def test_premade_gcd_decodes_exact_picker_profile(self) -> None:
        profile = parse_premade_gcd(self._premade_gcd())
        self.assertEqual(profile["name"], "Max Stone")
        self.assertEqual(profile["age"], 23)
        self.assertEqual(profile["sex"], "Male")
        self.assertEqual(profile["allocatedSpecial"], [8, 4, 9, 4, 4, 7, 4])
        self.assertEqual(profile["taggedSkills"], ["Small Guns", "Big Guns", "Unarmed"])
        self.assertEqual(profile["traits"], ["Heavy Handed", "Bruiser"])

    def test_premade_gcd_fails_closed_on_layout_or_index_drift(self) -> None:
        with self.assertRaises(ValueError):
            parse_premade_gcd(self._premade_gcd()[:-1])
        damaged = bytearray(self._premade_gcd())
        struct.pack_into(">i", damaged, 100 * 4, 99)
        with self.assertRaises(ValueError):
            parse_premade_gcd(bytes(damaged))

    def test_timing_rows_are_strict_and_convert_tenths(self) -> None:
        rows = parse_timing(
            b"30:First\r\n80:Second\r\n" +
            b"90:\r\n100:\r\n110:\r\n120:\r\n130:\r\n140:\r\n150:\r\n160:\r\n",
            10,
        )
        self.assertEqual(rows[0], {"tick": 30, "seconds": 3.0, "text": "First"})
        self.assertEqual(rows[1]["seconds"], 8.0)

    def test_timing_rows_reject_out_of_order_ticks(self) -> None:
        with self.assertRaises(ValueError):
            parse_timing(
                b"30:First\n20:Second\n40:x\n50:x\n60:x\n70:x\n80:x\n90:x\n100:x\n110:x\n",
                10,
            )

    def test_palette_scales_valid_entries_despite_retail_sentinels(self) -> None:
        palette = bytearray(768 + 32768)
        palette[3:6] = bytes((1, 2, 3))
        palette[215 * 3 : 215 * 3 + 3] = bytes((15, 62, 0))
        palette[255 * 3 : 255 * 3 + 3] = bytes((255, 255, 255))
        palette[768 + 992] = 215
        colors = colors_from_palette(bytes(palette))
        self.assertEqual(colors[1], (4, 8, 12, 255))
        self.assertEqual(colors[255], (0, 0, 0, 255))
        self.assertEqual(color_table_rgb(bytes(palette), 992), (60, 248, 0))

    def test_aaff_font_decodes_bottom_aligned_tinted_atlas(self) -> None:
        font = bytearray(2064)
        struct.pack_into(">I4h", font, 0, 0x41414646, 9, 1, 4, 1)
        struct.pack_into(">hhI", font, 12 + 65 * 8, 2, 2, 0)
        font[2060:2064] = bytes((0, 7, 3, 0))
        with tempfile.TemporaryDirectory() as raw_directory:
            atlas_path = Path(raw_directory) / "font.png"
            decoded = decode_aaf_font(bytes(font), atlas_path, (60, 248, 0))
            self.assertEqual(decoded["maximumHeight"], 9)
            self.assertEqual(decoded["cellWidth"], 2)
            self.assertEqual(decoded["atlasWidth"], 32)
            self.assertEqual(decoded["atlasHeight"], 144)
            from PIL import Image

            with Image.open(atlas_path) as atlas:
                self.assertEqual(atlas.getpixel((3, 43)), (60, 248, 0, 255))
                self.assertEqual(atlas.getpixel((2, 44)), (60, 248, 0, 109))

    def test_ogg_serial_normalization_rewrites_crc_deterministically(self) -> None:
        def page(serial: int) -> bytes:
            value = bytearray(b"OggS\x00\x02" + b"\x00" * 8)
            value.extend(struct.pack("<III", serial, 0, 0))
            value.extend(b"\x01\x03abc")
            return bytes(value)

        with tempfile.TemporaryDirectory() as raw_directory:
            first = Path(raw_directory) / "first.ogg"
            second = Path(raw_directory) / "second.ogg"
            first.write_bytes(page(1))
            second.write_bytes(page(2))
            normalize_ogg_serial(first, 42)
            normalize_ogg_serial(second, 42)
            self.assertEqual(first.read_bytes(), second.read_bytes())


if __name__ == "__main__":
    unittest.main()
