"""Decode the owned Fallout exterior weather and image-space graph.

The module intentionally stops at authored record semantics.  It does not
invent substitute sky colors, light directions, or post-processing values.
"""

from __future__ import annotations

import hashlib
import math
import struct
from dataclasses import dataclass
from pathlib import Path

from plugin_records import Record, iter_plugin_records, iter_subrecords, zstring


ENVIRONMENT_SCHEMA = "opennv-fallout-environment/v2"
WORLDSPACE_RECORD = "WRLD"
CLIMATE_RECORD = "CLMT"
WEATHER_RECORD = "WTHR"
IMAGE_SPACE_RECORD = "IMGS"
IMAGE_SPACE_MODIFIER_RECORD = "IMAD"
ENVIRONMENT_RECORDS = frozenset(
    {
        WORLDSPACE_RECORD,
        CLIMATE_RECORD,
        WEATHER_RECORD,
        IMAGE_SPACE_RECORD,
        IMAGE_SPACE_MODIFIER_RECORD,
    }
)

FORM_ID_BYTES = 4
BITS_PER_BYTE = 8
BYTE_MAXIMUM = float((1 << BITS_PER_BYTE) - 1)
CLIMATE_TIMING_BYTES = 6
CLIMATE_WEATHER_ENTRY_BYTES = 12
CLIMATE_TIME_UNITS_PER_HOUR = 6.0
HIGH_NOON_HOUR = 12.0
DAYTIME_COLOR_EXTENSION_HOURS = 0.5
DAY_HOURS = 24.0
SYMMETRIC_INTERVAL_HALF = 0.5

WEATHER_TIME_NAMES = (
    "sunrise",
    "day",
    "sunset",
    "night",
    "highNoon",
    "midnight",
)
WEATHER_LEGACY_SAMPLE_COUNT = 4
WEATHER_SAMPLE_COUNT = len(WEATHER_TIME_NAMES)
WEATHER_COLOR_NAMES = (
    "skyUpper",
    "fog",
    "unused2",
    "ambient",
    "sunlight",
    "sun",
    "stars",
    "skyLower",
    "horizon",
    "unused9",
)
WEATHER_CLOUD_LAYER_COUNT = 4
WEATHER_COLOR_BYTES = 4
WEATHER_FOG_VALUE_COUNT = 6
WEATHER_FOG_BYTES = WEATHER_FOG_VALUE_COUNT * struct.calcsize("<f")
WEATHER_DATA_MINIMUM_BYTES = 15
WEATHER_WIND_SPEED_DATA_OFFSET = 0
WEATHER_IMAGE_SPACE_FLOAT_COUNT = 76
WEATHER_IMAGE_SPACE_BYTES = WEATHER_IMAGE_SPACE_FLOAT_COUNT * struct.calcsize("<f")
WEATHER_MAX_CLOUD_LAYERS_BYTES = 4
WEATHER_CLOUD_SPEED_BYTES = WEATHER_CLOUD_LAYER_COUNT

IMAGE_SPACE_TRAIT_NAMES = (
    "eyeAdaptSpeed",
    "hdrBlurRadius",
    "hdrBlurPasses",
    "emissiveMultiplier",
    "targetLuminance",
    "upperLuminanceClamp",
    "brightScale",
    "brightClamp",
    "luminanceRampNoTexture",
    "luminanceRampMin",
    "luminanceRampMax",
    "sunlightDimmer",
    "grassDimmer",
    "treeDimmer",
    "skinDimmer",
    "bloomBlurRadius",
    "bloomAlphaInterior",
    "bloomAlphaExterior",
    "getHitBlurRadius",
    "getHitBlurDamping",
    "getHitDamping",
    "nightEyeTintRed",
    "nightEyeTintGreen",
    "nightEyeTintBlue",
    "nightEyeBrightness",
    "cinematicSaturation",
    "cinematicContrastAverageLuminance",
    "cinematicContrast",
    "cinematicBrightness",
    "cinematicTintRed",
    "cinematicTintGreen",
    "cinematicTintBlue",
    "cinematicTintStrength",
)
IMAGE_SPACE_TRAIT_COUNT = len(IMAGE_SPACE_TRAIT_NAMES)
IMAGE_SPACE_TRAIT_BYTES = IMAGE_SPACE_TRAIT_COUNT * struct.calcsize("<f")
IMAGE_SPACE_RESERVED_DWORD_COUNT = 4
IMAGE_SPACE_RESERVED_BYTES = IMAGE_SPACE_RESERVED_DWORD_COUNT * struct.calcsize("<I")
IMAGE_SPACE_CINEMATIC_FLAGS_OFFSET = IMAGE_SPACE_TRAIT_BYTES + IMAGE_SPACE_RESERVED_BYTES
IMAGE_SPACE_CINEMATIC_SATURATION = 1 << 0
IMAGE_SPACE_CINEMATIC_CONTRAST = 1 << 1
IMAGE_SPACE_CINEMATIC_TINT = 1 << 2
IMAGE_SPACE_CINEMATIC_BRIGHTNESS = 1 << 3
IMAGE_SPACE_CINEMATIC_SATURATION_INDEX = IMAGE_SPACE_TRAIT_NAMES.index(
    "cinematicSaturation"
)
IMAGE_SPACE_CINEMATIC_CONTRAST_AVERAGE_INDEX = IMAGE_SPACE_TRAIT_NAMES.index(
    "cinematicContrastAverageLuminance"
)
IMAGE_SPACE_CINEMATIC_CONTRAST_INDEX = IMAGE_SPACE_TRAIT_NAMES.index(
    "cinematicContrast"
)
IMAGE_SPACE_CINEMATIC_BRIGHTNESS_INDEX = IMAGE_SPACE_TRAIT_NAMES.index(
    "cinematicBrightness"
)
IMAGE_SPACE_CINEMATIC_TINT_STRENGTH_INDEX = IMAGE_SPACE_TRAIT_NAMES.index(
    "cinematicTintStrength"
)
IMAGE_SPACE_MODIFIER_CHANNEL_NAMES = (
    "eyeAdaptSpeed",
    "hdrBlurRadius",
    "skinDimmer",
    "emissiveMultiplier",
    "targetLuminance",
    "upperLuminanceClamp",
    "brightScale",
    "brightClamp",
    "luminanceRampNoTexture",
    "luminanceRampMin",
    "luminanceRampMax",
    "sunlightDimmer",
    "grassDimmer",
    "treeDimmer",
    "bloomBlurRadius",
    "bloomAlphaInterior",
    "bloomAlphaExterior",
    "cinematicSaturation",
    "cinematicContrastAverageLuminance",
    "cinematicContrast",
    "cinematicBrightness",
)
IMAGE_SPACE_MODIFIER_CHANNEL_COUNT = len(IMAGE_SPACE_MODIFIER_CHANNEL_NAMES)
IMAGE_SPACE_MODIFIER_ADD_CHANNEL_OFFSET = 0x40
IMAGE_SPACE_MODIFIER_FLOAT_KEY_BYTES = struct.calcsize("<2f")
IMAGE_SPACE_MODIFIER_COLOR_KEY_BYTES = struct.calcsize("<5f")
IMAGE_SPACE_MODIFIER_CURVE_SIGNATURES = {
    "BNAM": "blurRadius",
    "VNAM": "doubleVisionStrength",
    "RNAM": "radialBlurStrength",
    "SNAM": "radialBlurRampUp",
    "UNAM": "radialBlurStart",
    "NAM1": "radialBlurRampDown",
    "NAM2": "radialBlurDownStart",
    "WNAM": "depthOfFieldStrength",
    "XNAM": "depthOfFieldDistance",
    "YNAM": "depthOfFieldRange",
    "NAM4": "motionBlurStrength",
}


def _form_id(value: int | None) -> str | None:
    return None if value is None else f"0x{value:08X}"


def _subrecords(record: Record) -> dict[str, list[bytes]]:
    result: dict[str, list[bytes]] = {}
    for subrecord in iter_subrecords(record):
        result.setdefault(subrecord.signature, []).append(subrecord.data)
    return result


def _optional_one(
    values: dict[str, list[bytes]],
    signature: str,
    record: Record,
) -> bytes | None:
    matches = values.get(signature, [])
    if len(matches) > 1:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} has duplicate {signature} subrecords"
        )
    return matches[0] if matches else None


def _required_one(
    values: dict[str, list[bytes]],
    signature: str,
    record: Record,
) -> bytes:
    data = _optional_one(values, signature, record)
    if data is None:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} has no {signature} subrecord"
        )
    return data


def _optional_form_id(
    values: dict[str, list[bytes]],
    signature: str,
    record: Record,
) -> int | None:
    data = _optional_one(values, signature, record)
    if data is None:
        return None
    if len(data) != FORM_ID_BYTES:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} {signature} is not a FormID"
        )
    value = struct.unpack("<I", data)[0]
    return value if value != 0 else None


def _optional_text(
    values: dict[str, list[bytes]],
    signature: str,
    record: Record,
) -> str:
    data = _optional_one(values, signature, record)
    return "" if data is None else zstring(data).replace("/", "\\").lower()


def _colors(
    data: bytes,
    rows: int,
    label: str,
) -> tuple[tuple[tuple[int, int, int, int], ...], ...]:
    row_bytes = rows * WEATHER_COLOR_BYTES
    if len(data) % row_bytes:
        raise ValueError(f"{label} byte count is not divisible by its authored rows")
    sample_count = len(data) // row_bytes
    if sample_count not in {WEATHER_LEGACY_SAMPLE_COUNT, WEATHER_SAMPLE_COUNT}:
        raise ValueError(f"{label} has unsupported sample count {sample_count}")
    values = tuple(
        tuple(data[offset : offset + WEATHER_COLOR_BYTES])
        for offset in range(0, len(data), WEATHER_COLOR_BYTES)
    )
    return tuple(
        values[row * sample_count : (row + 1) * sample_count]
        for row in range(rows)
    )


def _float_keys(data: bytes | None, label: str) -> tuple[tuple[float, float], ...]:
    if data is None:
        return ()
    if len(data) % IMAGE_SPACE_MODIFIER_FLOAT_KEY_BYTES:
        raise ValueError(f"{label} byte count is not a sequence of float keys")
    result = tuple(
        struct.unpack_from("<2f", data, offset)
        for offset in range(0, len(data), IMAGE_SPACE_MODIFIER_FLOAT_KEY_BYTES)
    )
    if any(not math.isfinite(component) for key in result for component in key):
        raise ValueError(f"{label} contains a non-finite float key")
    if any(left[0] > right[0] for left, right in zip(result, result[1:])):
        raise ValueError(f"{label} key times are not ordered")
    return result


def _color_keys(
    data: bytes | None,
    label: str,
) -> tuple[tuple[float, float, float, float, float], ...]:
    if data is None:
        return ()
    if len(data) % IMAGE_SPACE_MODIFIER_COLOR_KEY_BYTES:
        raise ValueError(f"{label} byte count is not a sequence of color keys")
    result = tuple(
        struct.unpack_from("<5f", data, offset)
        for offset in range(0, len(data), IMAGE_SPACE_MODIFIER_COLOR_KEY_BYTES)
    )
    if any(not math.isfinite(component) for key in result for component in key):
        raise ValueError(f"{label} contains a non-finite color key")
    if any(left[0] > right[0] for left, right in zip(result, result[1:])):
        raise ValueError(f"{label} key times are not ordered")
    return result


@dataclass(frozen=True)
class ClimateTiming:
    sunrise_begin: int
    sunrise_end: int
    sunset_begin: int
    sunset_end: int
    volatility: int
    moon_info: int

    def hours(self) -> tuple[float, float, float, float]:
        return tuple(
            value / CLIMATE_TIME_UNITS_PER_HOUR
            for value in (
                self.sunrise_begin,
                self.sunrise_end,
                self.sunset_begin,
                self.sunset_end,
            )
        )

    def manifest(self) -> dict[str, object]:
        sunrise_begin, sunrise_end, sunset_begin, sunset_end = self.hours()
        return {
            "raw": [
                self.sunrise_begin,
                self.sunrise_end,
                self.sunset_begin,
                self.sunset_end,
                self.volatility,
                self.moon_info,
            ],
            "sunriseBeginHour": sunrise_begin,
            "sunriseEndHour": sunrise_end,
            "sunsetBeginHour": sunset_begin,
            "sunsetEndHour": sunset_end,
            "volatility": self.volatility,
            "moonInfo": self.moon_info,
        }


@dataclass(frozen=True)
class WeatherTimeBlend:
    primary: int
    secondary: int
    primary_strength: float

    def manifest(self) -> dict[str, object]:
        return {
            "primary": WEATHER_TIME_NAMES[self.primary],
            "secondary": WEATHER_TIME_NAMES[self.secondary],
            "primaryStrength": self.primary_strength,
        }


@dataclass(frozen=True)
class WorldspaceEnvironment:
    form_id: int
    editor_id: str
    climate_form_id: int | None
    image_space_form_id: int | None

    def manifest(self) -> dict[str, object]:
        return {
            "formId": _form_id(self.form_id),
            "editorId": self.editor_id,
            "climateFormId": _form_id(self.climate_form_id),
            "imageSpaceFormId": _form_id(self.image_space_form_id),
        }


@dataclass(frozen=True)
class Climate:
    form_id: int
    editor_id: str
    weather_entries: tuple[tuple[int, int, int | None], ...]
    sun_texture: str
    sun_glare_texture: str
    night_sky_model: str
    timing: ClimateTiming

    def manifest(self) -> dict[str, object]:
        return {
            "formId": _form_id(self.form_id),
            "editorId": self.editor_id,
            "weatherEntries": [
                {
                    "weatherFormId": _form_id(weather),
                    "chance": chance,
                    "globalFormId": _form_id(global_form),
                }
                for weather, chance, global_form in self.weather_entries
            ],
            "sunTexture": self.sun_texture,
            "sunGlareTexture": self.sun_glare_texture,
            "nightSkyModel": self.night_sky_model,
            "timing": self.timing.manifest(),
        }


@dataclass(frozen=True)
class ImageSpace:
    form_id: int
    editor_id: str
    raw_traits: tuple[float, ...]
    effective_traits: tuple[float, ...]
    cinematic_flags: int | None
    dnam_sha256: str

    def manifest(self) -> dict[str, object]:
        return {
            "formId": _form_id(self.form_id),
            "editorId": self.editor_id,
            "rawTraits": dict(zip(IMAGE_SPACE_TRAIT_NAMES, self.raw_traits)),
            "effectiveTraits": dict(zip(IMAGE_SPACE_TRAIT_NAMES, self.effective_traits)),
            "effectiveTraitArray": list(self.effective_traits),
            "cinematicFlags": self.cinematic_flags,
            "dnamSha256": self.dnam_sha256,
        }


@dataclass(frozen=True)
class ImageSpaceModifier:
    form_id: int
    editor_id: str
    adapter_flags: int
    duration: float
    multiply: tuple[tuple[tuple[float, float], ...], ...]
    add: tuple[tuple[tuple[float, float], ...], ...]
    tint: tuple[tuple[float, float, float, float, float], ...]
    fade: tuple[tuple[float, float, float, float, float], ...]
    curves: dict[str, tuple[tuple[float, float], ...]]
    intro_sound: int | None
    outro_sound: int | None
    record_sha256: str

    def manifest(self) -> dict[str, object]:
        return {
            "formId": _form_id(self.form_id),
            "editorId": self.editor_id,
            "adapterFlags": self.adapter_flags,
            "duration": self.duration,
            "multiply": {
                name: [list(key) for key in keys]
                for name, keys in zip(IMAGE_SPACE_MODIFIER_CHANNEL_NAMES, self.multiply)
            },
            "add": {
                name: [list(key) for key in keys]
                for name, keys in zip(IMAGE_SPACE_MODIFIER_CHANNEL_NAMES, self.add)
            },
            "tint": [list(key) for key in self.tint],
            "fade": [list(key) for key in self.fade],
            "curves": {
                name: [list(key) for key in keys]
                for name, keys in sorted(self.curves.items())
            },
            "introSoundFormId": _form_id(self.intro_sound),
            "outroSoundFormId": _form_id(self.outro_sound),
            "recordSha256": self.record_sha256,
        }


@dataclass(frozen=True)
class Weather:
    form_id: int
    editor_id: str
    image_space_modifiers: tuple[int | None, ...]
    cloud_textures: tuple[str, ...]
    max_cloud_layers: int
    cloud_speeds: tuple[int, ...]
    cloud_colors: tuple[tuple[tuple[int, int, int, int], ...], ...]
    colors: tuple[tuple[tuple[int, int, int, int], ...], ...]
    fog_distances: tuple[float, ...]
    weather_image_space_values: tuple[float, ...]
    data: tuple[int, ...]

    @property
    def sample_count(self) -> int:
        return len(self.colors[0])

    @property
    def wind_speed_byte(self) -> int:
        return self.data[WEATHER_WIND_SPEED_DATA_OFFSET]

    def record_manifest(self) -> dict[str, object]:
        return {
            "formId": _form_id(self.form_id),
            "editorId": self.editor_id,
            "sampleCount": self.sample_count,
            "imageSpaceModifiers": {
                name: _form_id(value)
                for name, value in zip(WEATHER_TIME_NAMES, self.image_space_modifiers)
            },
            "cloudTextures": list(self.cloud_textures),
            "maxCloudLayers": self.max_cloud_layers,
            "cloudSpeeds": list(self.cloud_speeds),
            "cloudColors": _color_table(self.cloud_colors),
            "colors": {
                name: samples
                for name, samples in zip(
                    WEATHER_COLOR_NAMES,
                    _color_table(self.colors),
                )
            },
            "fogDistances": list(self.fog_distances),
            "weatherImageSpaceValues": list(self.weather_image_space_values),
            "weatherImageSpaceStatus": "preserved-unresolved",
            "physicsWind": {
                "sourceSubrecord": "DATA",
                "sourceByteOffset": WEATHER_WIND_SPEED_DATA_OFFSET,
                "storage": "uint8",
                "magnitudeByte": self.wind_speed_byte,
                "worldForceStatus": "unsupported-missing-observed-listener-equation",
            },
            "data": list(self.data),
        }

    def manifest(self, game_hour: float, timing: ClimateTiming) -> dict[str, object]:
        blend = fallout_weather_time_blend(game_hour, timing)
        if self.sample_count != WEATHER_SAMPLE_COUNT:
            raise ValueError(
                f"WTHR {self.form_id:08x} lacks the six samples required for exact FNV blending"
            )
        result = self.record_manifest()
        result.update(
            {
            "gameHour": game_hour,
            "timeBlend": blend.manifest(),
                "resolvedColors": {
                    name: list(interpolate_weather_color(row, blend))
                    for name, row in zip(WEATHER_COLOR_NAMES, self.colors)
                },
                "resolvedCloudColors": [
                    list(interpolate_weather_color(row, blend))
                    for row in self.cloud_colors
                ],
            }
        )
        return result


@dataclass(frozen=True)
class EnvironmentCatalog:
    worldspaces: dict[int, WorldspaceEnvironment]
    climates: dict[int, Climate]
    weather: dict[int, Weather]
    image_spaces: dict[int, ImageSpace]
    image_space_modifiers: dict[int, ImageSpaceModifier]

    def exterior_manifest(self, worldspace_form_id: int) -> dict[str, object]:
        worldspace = self.worldspaces.get(worldspace_form_id)
        if worldspace is None or worldspace.climate_form_id is None:
            raise ValueError(
                f"WRLD {worldspace_form_id:08x} has no exact exterior climate relationship"
            )
        climate = self.climates.get(worldspace.climate_form_id)
        if climate is None:
            raise ValueError(
                f"WRLD {worldspace_form_id:08x} climate is absent from the owned master"
            )
        image_space = (
            None
            if worldspace.image_space_form_id is None
            else self.image_spaces.get(worldspace.image_space_form_id)
        )
        if worldspace.image_space_form_id is not None and image_space is None:
            raise ValueError(
                f"WRLD {worldspace_form_id:08x} image space is absent from the owned master"
            )
        linked_modifier_forms = sorted(
            {
                modifier
                for weather in self.weather.values()
                for modifier in weather.image_space_modifiers
                if modifier is not None
            }
        )
        missing_modifiers = [
            form
            for form in linked_modifier_forms
            if form not in self.image_space_modifiers
        ]
        if missing_modifiers:
            raise ValueError(
                "Owned WTHR graph references absent IMAD records: "
                + ", ".join(f"{form:08x}" for form in missing_modifiers)
            )
        return {
            "schema": ENVIRONMENT_SCHEMA,
            "worldspace": worldspace.manifest(),
            "climate": climate.manifest(),
            "weather": [
                value.record_manifest()
                for _form, value in sorted(self.weather.items())
            ],
            "baseImageSpace": None if image_space is None else image_space.manifest(),
            "imageSpaceModifiers": [
                self.image_space_modifiers[form].manifest()
                for form in linked_modifier_forms
            ],
        }


def _color_table(
    rows: tuple[tuple[tuple[int, int, int, int], ...], ...],
) -> list[list[list[int]]]:
    return [[list(color) for color in row] for row in rows]


def parse_worldspace_environment(record: Record) -> WorldspaceEnvironment:
    values = _subrecords(record)
    return WorldspaceEnvironment(
        record.form_id,
        zstring(_required_one(values, "EDID", record)),
        _optional_form_id(values, "CNAM", record),
        _optional_form_id(values, "INAM", record),
    )


def parse_climate(record: Record) -> Climate:
    values = _subrecords(record)
    timing_data = _required_one(values, "TNAM", record)
    if len(timing_data) != CLIMATE_TIMING_BYTES:
        raise ValueError(f"CLMT {record.form_id:08x} TNAM has an invalid byte count")
    weather_data = _optional_one(values, "WLST", record) or b""
    if len(weather_data) % CLIMATE_WEATHER_ENTRY_BYTES:
        raise ValueError(f"CLMT {record.form_id:08x} WLST has an invalid byte count")
    weather_entries = []
    for offset in range(0, len(weather_data), CLIMATE_WEATHER_ENTRY_BYTES):
        weather, chance, global_form = struct.unpack_from("<IiI", weather_data, offset)
        weather_entries.append((weather, chance, global_form if global_form != 0 else None))
    return Climate(
        record.form_id,
        zstring(_required_one(values, "EDID", record)),
        tuple(weather_entries),
        _optional_text(values, "FNAM", record),
        _optional_text(values, "GNAM", record),
        _optional_text(values, "MODL", record),
        ClimateTiming(*timing_data),
    )


def parse_image_space(record: Record) -> ImageSpace:
    values = _subrecords(record)
    dnam = _required_one(values, "DNAM", record)
    if len(dnam) < IMAGE_SPACE_TRAIT_BYTES:
        raise ValueError(f"IMGS {record.form_id:08x} has no complete Fallout trait array")
    raw_traits = struct.unpack_from(f"<{IMAGE_SPACE_TRAIT_COUNT}f", dnam)
    effective = list(raw_traits)
    flags = (
        dnam[IMAGE_SPACE_CINEMATIC_FLAGS_OFFSET]
        if len(dnam) > IMAGE_SPACE_CINEMATIC_FLAGS_OFFSET
        else None
    )
    if flags is not None:
        if not flags & IMAGE_SPACE_CINEMATIC_SATURATION:
            effective[IMAGE_SPACE_CINEMATIC_SATURATION_INDEX] = 1.0
        if not flags & IMAGE_SPACE_CINEMATIC_CONTRAST:
            effective[IMAGE_SPACE_CINEMATIC_CONTRAST_AVERAGE_INDEX] = 0.0
            effective[IMAGE_SPACE_CINEMATIC_CONTRAST_INDEX] = 1.0
        if not flags & IMAGE_SPACE_CINEMATIC_TINT:
            effective[IMAGE_SPACE_CINEMATIC_TINT_STRENGTH_INDEX] = 0.0
        if not flags & IMAGE_SPACE_CINEMATIC_BRIGHTNESS:
            effective[IMAGE_SPACE_CINEMATIC_BRIGHTNESS_INDEX] = 1.0
    return ImageSpace(
        record.form_id,
        zstring(_required_one(values, "EDID", record)),
        tuple(raw_traits),
        tuple(effective),
        flags,
        hashlib.sha256(dnam).hexdigest(),
    )


def parse_image_space_modifier(record: Record) -> ImageSpaceModifier:
    values = _subrecords(record)
    data = _required_one(values, "DNAM", record)
    if len(data) < struct.calcsize("<If"):
        raise ValueError(f"IMAD {record.form_id:08x} DNAM has no adapter flags/duration")
    adapter_flags, duration = struct.unpack_from("<If", data)
    if not math.isfinite(duration) or duration < 0.0:
        raise ValueError(f"IMAD {record.form_id:08x} has an invalid duration")
    multiply = tuple(
        _float_keys(
            _optional_one(values, f"{channel}IAD", record),
            f"IMAD {record.form_id:08x} multiply channel {channel}",
        )
        for channel in range(IMAGE_SPACE_MODIFIER_CHANNEL_COUNT)
    )
    add = tuple(
        _float_keys(
            _optional_one(
                values,
                f"{IMAGE_SPACE_MODIFIER_ADD_CHANNEL_OFFSET + channel}IAD",
                record,
            ),
            f"IMAD {record.form_id:08x} add channel {channel}",
        )
        for channel in range(IMAGE_SPACE_MODIFIER_CHANNEL_COUNT)
    )
    curves = {
        curve_name: _float_keys(
            _optional_one(values, signature, record),
            f"IMAD {record.form_id:08x} {signature}",
        )
        for signature, curve_name in IMAGE_SPACE_MODIFIER_CURVE_SIGNATURES.items()
    }
    known_signatures = {
        "EDID",
        "DNAM",
        "TNAM",
        "NAM3",
        "RDSD",
        "RDSI",
        *IMAGE_SPACE_MODIFIER_CURVE_SIGNATURES,
        *(f"{channel}IAD" for channel in range(IMAGE_SPACE_MODIFIER_CHANNEL_COUNT)),
        *(
            f"{IMAGE_SPACE_MODIFIER_ADD_CHANNEL_OFFSET + channel}IAD"
            for channel in range(IMAGE_SPACE_MODIFIER_CHANNEL_COUNT)
        ),
    }
    unknown = sorted(set(values) - known_signatures)
    if unknown:
        raise ValueError(
            f"IMAD {record.form_id:08x} has unsupported subrecords: {unknown}"
        )
    return ImageSpaceModifier(
        record.form_id,
        zstring(_required_one(values, "EDID", record)),
        adapter_flags,
        duration,
        multiply,
        add,
        _color_keys(
            _optional_one(values, "TNAM", record),
            f"IMAD {record.form_id:08x} TNAM",
        ),
        _color_keys(
            _optional_one(values, "NAM3", record),
            f"IMAD {record.form_id:08x} NAM3",
        ),
        curves,
        _optional_form_id(values, "RDSD", record),
        _optional_form_id(values, "RDSI", record),
        hashlib.sha256(record.data).hexdigest(),
    )
def parse_weather(record: Record) -> Weather:
    values = _subrecords(record)
    max_layers = _required_one(values, "LNAM", record)
    cloud_speeds = _required_one(values, "ONAM", record)
    fog = _required_one(values, "FNAM", record)
    weather_image_space = _required_one(values, "INAM", record)
    data = _required_one(values, "DATA", record)
    if len(max_layers) != WEATHER_MAX_CLOUD_LAYERS_BYTES:
        raise ValueError(f"WTHR {record.form_id:08x} LNAM has an invalid byte count")
    if len(cloud_speeds) != WEATHER_CLOUD_SPEED_BYTES:
        raise ValueError(f"WTHR {record.form_id:08x} ONAM has an invalid byte count")
    if len(fog) != WEATHER_FOG_BYTES:
        raise ValueError(f"WTHR {record.form_id:08x} FNAM has an invalid byte count")
    if len(weather_image_space) != WEATHER_IMAGE_SPACE_BYTES:
        raise ValueError(f"WTHR {record.form_id:08x} INAM has an invalid byte count")
    if len(data) < WEATHER_DATA_MINIMUM_BYTES:
        raise ValueError(f"WTHR {record.form_id:08x} DATA has an invalid byte count")
    image_space_modifiers = tuple(
        _optional_form_id(values, f"{sample}IAD", record)
        for sample in range(WEATHER_SAMPLE_COUNT)
    )
    return Weather(
        record.form_id,
        zstring(_required_one(values, "EDID", record)),
        image_space_modifiers,
        tuple(
            _optional_text(values, signature, record)
            for signature in ("DNAM", "CNAM", "ANAM", "BNAM")
        ),
        struct.unpack("<I", max_layers)[0],
        tuple(cloud_speeds),
        _colors(
            _required_one(values, "PNAM", record),
            WEATHER_CLOUD_LAYER_COUNT,
            f"WTHR {record.form_id:08x} PNAM",
        ),
        _colors(
            _required_one(values, "NAM0", record),
            len(WEATHER_COLOR_NAMES),
            f"WTHR {record.form_id:08x} NAM0",
        ),
        struct.unpack(f"<{WEATHER_FOG_VALUE_COUNT}f", fog),
        struct.unpack(f"<{WEATHER_IMAGE_SPACE_FLOAT_COUNT}f", weather_image_space),
        tuple(data),
    )


def scan_environment_catalog(path: Path) -> EnvironmentCatalog:
    worldspaces: dict[int, WorldspaceEnvironment] = {}
    climates: dict[int, Climate] = {}
    weather: dict[int, Weather] = {}
    image_spaces: dict[int, ImageSpace] = {}
    image_space_modifiers: dict[int, ImageSpaceModifier] = {}
    destinations = {
        WORLDSPACE_RECORD: (worldspaces, parse_worldspace_environment),
        CLIMATE_RECORD: (climates, parse_climate),
        WEATHER_RECORD: (weather, parse_weather),
        IMAGE_SPACE_RECORD: (image_spaces, parse_image_space),
        IMAGE_SPACE_MODIFIER_RECORD: (
            image_space_modifiers,
            parse_image_space_modifier,
        ),
    }
    for record in iter_plugin_records(path, ENVIRONMENT_RECORDS):
        destination, parser = destinations[record.signature]
        if record.form_id in destination:
            raise ValueError(
                f"Duplicate {record.signature} FormID in owned master: {record.form_id:08x}"
            )
        destination[record.form_id] = parser(record)
    return EnvironmentCatalog(
        worldspaces,
        climates,
        weather,
        image_spaces,
        image_space_modifiers,
    )


def fallout_weather_time_blend(
    game_hour: float,
    timing: ClimateTiming,
) -> WeatherTimeBlend:
    if not math.isfinite(game_hour) or game_hour < 0.0 or game_hour >= DAY_HOURS:
        raise ValueError("FNV game hour must be finite and in [0, 24)")
    night_end, day_start, day_end, night_start = timing.hours()
    night_end -= DAYTIME_COLOR_EXTENSION_HOURS
    night_start += DAYTIME_COLOR_EXTENSION_HOURS
    sunrise, day, sunset, night, high_noon, _midnight = range(WEATHER_SAMPLE_COUNT)
    if game_hour <= night_end or game_hour >= night_start:
        return WeatherTimeBlend(night, night, 1.0)
    if night_end < game_hour < day_start:
        midpoint = (night_end + day_start) * SYMMETRIC_INTERVAL_HALF
        half_duration = (day_start - night_end) * SYMMETRIC_INTERVAL_HALF
        if half_duration <= 0.0:
            raise ValueError("FNV climate has no sunrise blend duration")
        if game_hour < midpoint:
            return WeatherTimeBlend(
                sunrise,
                night,
                min(max((game_hour - night_end) / half_duration, 0.0), 1.0),
            )
        return WeatherTimeBlend(
            sunrise,
            day,
            min(max((day_start - game_hour) / half_duration, 0.0), 1.0),
        )
    if day_start < game_hour < HIGH_NOON_HOUR:
        duration = HIGH_NOON_HOUR - day_start
        return WeatherTimeBlend(
            high_noon,
            day,
            min(max((game_hour - day_start) / duration, 0.0), 1.0),
        )
    if HIGH_NOON_HOUR < game_hour < day_end:
        duration = day_end - HIGH_NOON_HOUR
        return WeatherTimeBlend(
            day,
            high_noon,
            min(max((game_hour - HIGH_NOON_HOUR) / duration, 0.0), 1.0),
        )
    if day_end < game_hour < night_start:
        midpoint = (day_end + night_start) * SYMMETRIC_INTERVAL_HALF
        half_duration = (night_start - day_end) * SYMMETRIC_INTERVAL_HALF
        if half_duration <= 0.0:
            raise ValueError("FNV climate has no sunset blend duration")
        if game_hour < midpoint:
            return WeatherTimeBlend(
                sunset,
                day,
                min(max((game_hour - day_end) / half_duration, 0.0), 1.0),
            )
        return WeatherTimeBlend(
            sunset,
            night,
            min(max((night_start - game_hour) / half_duration, 0.0), 1.0),
        )
    return WeatherTimeBlend(day, day, 1.0)


def interpolate_weather_color(
    samples: tuple[tuple[int, int, int, int], ...],
    blend: WeatherTimeBlend,
) -> tuple[float, float, float]:
    if len(samples) != WEATHER_SAMPLE_COUNT:
        raise ValueError("Exact FNV color interpolation requires six authored samples")
    primary = samples[blend.primary]
    secondary = samples[blend.secondary]
    return tuple(
        (
            secondary[channel] * (1.0 - blend.primary_strength)
            + primary[channel] * blend.primary_strength
        )
        / BYTE_MAXIMUM
        for channel in range(WEATHER_COLOR_BYTES - 1)
    )
