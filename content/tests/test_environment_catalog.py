from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from environment_catalog import (  # noqa: E402
    IMAGE_SPACE_CINEMATIC_BRIGHTNESS_INDEX,
    IMAGE_SPACE_CINEMATIC_CONTRAST_AVERAGE_INDEX,
    IMAGE_SPACE_CINEMATIC_CONTRAST_INDEX,
    IMAGE_SPACE_CINEMATIC_SATURATION_INDEX,
    IMAGE_SPACE_CINEMATIC_TINT_STRENGTH_INDEX,
    IMAGE_SPACE_RESERVED_BYTES,
    IMAGE_SPACE_TRAIT_COUNT,
    WEATHER_CLOUD_LAYER_COUNT,
    WEATHER_COLOR_NAMES,
    WEATHER_IMAGE_SPACE_FLOAT_COUNT,
    WEATHER_SAMPLE_COUNT,
    ClimateTiming,
    fallout_weather_time_blend,
    interpolate_weather_color,
    parse_image_space,
    parse_image_space_modifier,
    parse_weather,
    scan_environment_catalog,
)
from plugin_records import (  # noqa: E402
    PluginFormatError,
    Record,
    iter_subrecords,
)


CAPTURED_GOODSPRINGS_HOUR = 12.1527586
CAPTURED_GOODSPRINGS_AMBIENT = (
    0.387037218,
    0.469090641,
    0.602324128,
)
GOODSPRINGS_AMBIENT_SAMPLES = (
    (173, 173, 209, 0),
    (87, 105, 138, 0),
    (183, 183, 242, 0),
    (172, 170, 215, 0),
    (99, 120, 154, 0),
    (0, 0, 0, 0),
)
GOODSPRINGS_CLIMATE_TIMING = ClimateTiming(36, 48, 108, 120, 0, 131)
SYNTHETIC_WORLDSPACE = 0x10
SYNTHETIC_CLIMATE = 0x20
SYNTHETIC_WEATHER = 0x30
SYNTHETIC_IMAGE_SPACE = 0x40


def subrecord(signature: str | bytes, data: bytes) -> bytes:
    encoded = signature.encode("ascii") if isinstance(signature, str) else signature
    return encoded + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes) -> bytes:
    return struct.pack(
        "<4s4I2H",
        signature.encode("ascii"),
        len(data),
        0,
        form_id,
        0,
        0,
        0,
    ) + data


def color_payload(rows: int) -> bytes:
    return bytes(rows * WEATHER_SAMPLE_COUNT * 4)


def weather_payload() -> bytes:
    return (
        subrecord("EDID", b"SyntheticWeather\0")
        + subrecord(b"\x00IAD", struct.pack("<I", 0x50))
        + subrecord("DNAM", b"sky\\alpha.dds\0")
        + subrecord("CNAM", b"sky\\alpha.dds\0")
        + subrecord("ANAM", b"sky\\alpha.dds\0")
        + subrecord("BNAM", b"sky\\cloud.dds\0")
        + subrecord("LNAM", struct.pack("<I", WEATHER_CLOUD_LAYER_COUNT))
        + subrecord("ONAM", bytes(WEATHER_CLOUD_LAYER_COUNT))
        + subrecord("PNAM", color_payload(WEATHER_CLOUD_LAYER_COUNT))
        + subrecord("NAM0", color_payload(len(WEATHER_COLOR_NAMES)))
        + subrecord("FNAM", struct.pack("<6f", 10.0, 120000.0, 0.0, 150000.0, 0.5, 0.5))
        + subrecord("INAM", struct.pack(f"<{WEATHER_IMAGE_SPACE_FLOAT_COUNT}f", *([1.0] * WEATHER_IMAGE_SPACE_FLOAT_COUNT)))
        + subrecord("DATA", bytes(15))
    )


def image_space_payload(flags: int = 15) -> bytes:
    traits = [1.0] * IMAGE_SPACE_TRAIT_COUNT
    return (
        subrecord("EDID", b"SyntheticExterior\0")
        + subrecord(
            "DNAM",
            struct.pack(f"<{IMAGE_SPACE_TRAIT_COUNT}f", *traits)
            + bytes(IMAGE_SPACE_RESERVED_BYTES)
            + bytes([flags, 0, 0, 0]),
        )
    )


class EnvironmentCatalogTest(unittest.TestCase):
    def test_binary_weather_image_space_signature_is_exactly_bounded(self) -> None:
        source = Record("WTHR", SYNTHETIC_WEATHER, 0, weather_payload(), ())
        signatures = [row.signature for row in iter_subrecords(source)]

        self.assertIn("0IAD", signatures)
        invalid = Record(
            "WTHR",
            SYNTHETIC_WEATHER,
            0,
            subrecord(b"\x06IAD", bytes(4)),
            (),
        )
        with self.assertRaises(PluginFormatError):
            list(iter_subrecords(invalid))

    def test_binary_image_space_modifier_channels_are_record_scoped(self) -> None:
        source = Record(
            "IMAD",
            0x50,
            0,
            subrecord(b"\x14IAD", struct.pack("<2f", 0.0, 1.3))
            + subrecord(b"\x54IAD", struct.pack("<2f", 0.0, 0.2)),
            (),
        )
        self.assertEqual(
            [row.signature for row in iter_subrecords(source)],
            ["20IAD", "84IAD"],
        )
        for record_signature, channel in (("IMAD", 0x15), ("IMAD", 0x55), ("WTHR", 0x14)):
            invalid = Record(
                record_signature,
                0x50,
                0,
                subrecord(bytes([channel]) + b"IAD", bytes(8)),
                (),
            )
            with self.assertRaises(PluginFormatError):
                list(iter_subrecords(invalid))

    def test_image_space_modifier_retains_authored_curves(self) -> None:
        payload = (
            subrecord("EDID", b"SyntheticModifier\0")
            + subrecord("DNAM", struct.pack("<If", 3, 1.0))
            + subrecord(
                b"\x14IAD",
                struct.pack("<4f", 0.0, 1.3, 1.0, 1.1),
            )
            + subrecord(
                b"\x54IAD",
                struct.pack("<4f", 0.0, 0.2, 1.0, 0.0),
            )
            + subrecord(
                "TNAM",
                struct.pack("<5f", 0.0, 1.0, 0.5, 0.0, 0.4),
            )
        )
        parsed = parse_image_space_modifier(Record("IMAD", 0x50, 0, payload, ()))

        self.assertEqual(parsed.adapter_flags, 3)
        self.assertEqual(parsed.duration, 1.0)
        self.assertAlmostEqual(parsed.multiply[20][0][1], 1.3)
        self.assertAlmostEqual(parsed.multiply[20][1][1], 1.1)
        self.assertAlmostEqual(parsed.add[20][0][1], 0.2)
        self.assertAlmostEqual(parsed.add[20][1][1], 0.0)
        self.assertAlmostEqual(parsed.tint[0][4], 0.4)

    def test_image_space_modifier_cinematic_channels_match_retail_register_order(self) -> None:
        payload = (
            subrecord("EDID", b"CinematicModifier\0")
            + subrecord("DNAM", struct.pack("<If", 0, 1.0))
            + subrecord(b"\x12IAD", struct.pack("<2f", 0.0, 4.0))
            + subrecord(b"\x13IAD", struct.pack("<2f", 0.0, 0.8))
        )
        manifest = parse_image_space_modifier(
            Record("IMAD", 0x50, 0, payload, ())
        ).manifest()

        self.assertAlmostEqual(
            manifest["multiply"]["cinematicContrastAverageLuminance"][0][1],
            4.0,
        )
        self.assertAlmostEqual(
            manifest["multiply"]["cinematicContrast"][0][1],
            0.8,
        )

    def test_goodsprings_time_blend_reproduces_retail_ambient(self) -> None:
        blend = fallout_weather_time_blend(
            CAPTURED_GOODSPRINGS_HOUR,
            GOODSPRINGS_CLIMATE_TIMING,
        )
        resolved = interpolate_weather_color(GOODSPRINGS_AMBIENT_SAMPLES, blend)

        self.assertEqual(blend.manifest()["primary"], "day")
        self.assertEqual(blend.manifest()["secondary"], "highNoon")
        for actual, expected in zip(resolved, CAPTURED_GOODSPRINGS_AMBIENT):
            self.assertAlmostEqual(actual, expected, places=7)

    def test_disabled_cinematic_controls_become_shader_identities(self) -> None:
        traits = [1.0] * IMAGE_SPACE_TRAIT_COUNT
        traits[IMAGE_SPACE_CINEMATIC_SATURATION_INDEX] = 0.25
        traits[IMAGE_SPACE_CINEMATIC_CONTRAST_AVERAGE_INDEX] = 0.75
        traits[IMAGE_SPACE_CINEMATIC_CONTRAST_INDEX] = 0.5
        traits[IMAGE_SPACE_CINEMATIC_BRIGHTNESS_INDEX] = 0.25
        traits[IMAGE_SPACE_CINEMATIC_TINT_STRENGTH_INDEX] = 0.5
        payload = (
            subrecord("EDID", b"DisabledControls\0")
            + subrecord(
                "DNAM",
                struct.pack(f"<{IMAGE_SPACE_TRAIT_COUNT}f", *traits)
                + bytes(IMAGE_SPACE_RESERVED_BYTES)
                + bytes(4),
            )
        )
        parsed = parse_image_space(Record("IMGS", SYNTHETIC_IMAGE_SPACE, 0, payload, ()))

        self.assertEqual(parsed.effective_traits[IMAGE_SPACE_CINEMATIC_SATURATION_INDEX], 1.0)
        self.assertEqual(
            parsed.effective_traits[IMAGE_SPACE_CINEMATIC_CONTRAST_AVERAGE_INDEX],
            0.0,
        )
        self.assertEqual(parsed.effective_traits[IMAGE_SPACE_CINEMATIC_CONTRAST_INDEX], 1.0)
        self.assertEqual(parsed.effective_traits[IMAGE_SPACE_CINEMATIC_BRIGHTNESS_INDEX], 1.0)
        self.assertEqual(
            parsed.effective_traits[IMAGE_SPACE_CINEMATIC_TINT_STRENGTH_INDEX],
            0.0,
        )

    def test_owned_graph_resolves_without_hardcoded_environment_values(self) -> None:
        climate_timing = bytes((36, 48, 108, 120, 0, 131))
        plugin = (
            record("TES4", 0, subrecord("HEDR", struct.pack("<fII", 1.34, 0, 0)))
            + record(
                "WRLD",
                SYNTHETIC_WORLDSPACE,
                subrecord("EDID", b"SyntheticWorld\0")
                + subrecord("CNAM", struct.pack("<I", SYNTHETIC_CLIMATE))
                + subrecord("INAM", struct.pack("<I", SYNTHETIC_IMAGE_SPACE)),
            )
            + record(
                "CLMT",
                SYNTHETIC_CLIMATE,
                subrecord("EDID", b"SyntheticClimate\0")
                + subrecord(
                    "WLST",
                    struct.pack("<IiI", SYNTHETIC_WEATHER, 100, 0),
                )
                + subrecord("TNAM", climate_timing),
            )
            + record("WTHR", SYNTHETIC_WEATHER, weather_payload())
            + record("IMGS", SYNTHETIC_IMAGE_SPACE, image_space_payload())
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "Synthetic.esm"
            path.write_bytes(plugin)
            catalog = scan_environment_catalog(path)

        worldspace = catalog.worldspaces[SYNTHETIC_WORLDSPACE]
        climate = catalog.climates[worldspace.climate_form_id]
        weather = catalog.weather[climate.weather_entries[0][0]]
        image_space = catalog.image_spaces[worldspace.image_space_form_id]
        self.assertEqual(weather.sample_count, WEATHER_SAMPLE_COUNT)
        self.assertEqual(weather.image_space_modifiers[0], 0x50)
        self.assertEqual(image_space.cinematic_flags, 15)
        self.assertEqual(climate.timing, GOODSPRINGS_CLIMATE_TIMING)

    def test_weather_parser_preserves_unresolved_weather_image_space_bytes(self) -> None:
        parsed = parse_weather(
            Record("WTHR", SYNTHETIC_WEATHER, 0, weather_payload(), ())
        )

        self.assertEqual(
            len(parsed.weather_image_space_values),
            WEATHER_IMAGE_SPACE_FLOAT_COUNT,
        )
        self.assertTrue(all(value == 1.0 for value in parsed.weather_image_space_values))


if __name__ == "__main__":
    unittest.main()
