"""Load the single versioned OpenNV policy boundary used by compiler and runtime."""

from __future__ import annotations

import hashlib
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path


RUNTIME_CONFIGURATION_SCHEMA = "opennv-runtime-configuration/v1"
RUNTIME_CONFIGURATION_FILE = "open-nv-runtime-v1.json"
BYTE_CHANNEL_MAXIMUM = 255
PILLOW_PNG_MAX_COMPRESSION_LEVEL = 9
SHA256_HEX_CHARACTERS = 64
FORM_ID_HEX_CHARACTERS = 8
FORM_ID_RADIX = 16
CONFIGURATION_SECTIONS = (
    "world",
    "simulation",
    "renderer",
    "player",
    "xr",
    "door",
    "hud",
    "capture",
    "proof",
    "diagnosticPreview",
    "exteriorEnvironment",
    "retailActorState",
    "actorParity",
    "setupView",
    "desktopLauncher",
    "legalAssets",
    "contentCompiler",
    "actorCompiler",
)


@dataclass(frozen=True)
class ContentCompilerConfiguration:
    asset_id_hex_characters: int
    stable_id_hex_characters: int
    png_compression_level: int
    animation_samples_per_second: float
    xyz_rotation_equality_tolerance: float
    zero_specular_epsilon: float
    minimum_material_roughness: float
    default_material_glossiness: float
    landscape_quadrant_pixels: int
    landscape_tiles_per_quadrant: int
    landscape_tile_repeats_per_cell: int


@dataclass(frozen=True)
class ActorParityContactSheetConfiguration:
    header_pixels: int
    background_rgb: tuple[int, int, int]
    title_font_pixels: int
    detail_font_pixels: int
    text_margin_x_pixels: int
    title_y_pixels: int
    detail_y_pixels: int
    retail_title_rgb: tuple[int, int, int]
    godot_title_rgb: tuple[int, int, int]
    detail_rgb: tuple[int, int, int]


@dataclass(frozen=True)
class ActorParityConfiguration:
    pose_translation_tolerance_meters: float
    pose_rotation_tolerance_radians: float
    maximum_reported_worst_bones: int
    placement_tolerance_game_units: float
    yaw_tolerance_radians: float
    camera_position_tolerance_game_units: float
    camera_aim_tolerance_game_units: float
    camera_distance_tolerance_meters: float
    vertical_fov_tolerance_degrees: float
    animation_phase_tolerance_seconds: float
    changed_pixel_channel_tolerance: int
    maximum_mean_absolute_error: float
    maximum_changed_pixel_fraction: float
    maximum_mean_luminance_delta: float
    contact_sheet: ActorParityContactSheetConfiguration


@dataclass(frozen=True)
class RuntimeConfiguration:
    document: dict[str, object]
    sha256: str
    path: Path

    @property
    def world_units_to_meters(self) -> float:
        world = _object(self.document, "world")
        value = float(world["gameUnitsToMeters"])
        if value <= 0.0:
            raise ValueError("OpenNV gameUnitsToMeters must be positive")
        return value

    def manifest(self) -> dict[str, str]:
        return {"schema": RUNTIME_CONFIGURATION_SCHEMA, "sha256": self.sha256}

    @property
    def content_compiler(self) -> ContentCompilerConfiguration:
        source = _object(self.document, "contentCompiler")
        configuration = ContentCompilerConfiguration(
            asset_id_hex_characters=int(source["assetIdHexCharacters"]),
            stable_id_hex_characters=int(source["stableIdHexCharacters"]),
            png_compression_level=int(source["pngCompressionLevel"]),
            animation_samples_per_second=float(source["animationSamplesPerSecond"]),
            xyz_rotation_equality_tolerance=float(source["xyzRotationEqualityTolerance"]),
            zero_specular_epsilon=float(source["zeroSpecularEpsilon"]),
            minimum_material_roughness=float(source["minimumMaterialRoughness"]),
            default_material_glossiness=float(source["defaultMaterialGlossiness"]),
            landscape_quadrant_pixels=int(source["landscapeQuadrantPixels"]),
            landscape_tiles_per_quadrant=int(source["landscapeTilesPerQuadrant"]),
            landscape_tile_repeats_per_cell=int(source["landscapeTileRepeatsPerCell"]),
        )
        for name, value in configuration.__dict__.items():
            if value <= 0:
                raise ValueError(f"OpenNV contentCompiler.{name} must be positive")
        if configuration.minimum_material_roughness > 1:
            raise ValueError("OpenNV minimumMaterialRoughness must not exceed one")
        if configuration.png_compression_level > PILLOW_PNG_MAX_COMPRESSION_LEVEL:
            raise ValueError("OpenNV PNG compression level exceeds Pillow's contract")
        if (
            configuration.asset_id_hex_characters > SHA256_HEX_CHARACTERS
            or configuration.stable_id_hex_characters > SHA256_HEX_CHARACTERS
        ):
            raise ValueError("OpenNV content IDs exceed the SHA-256 hexadecimal digest")
        if (
            configuration.landscape_quadrant_pixels
            % configuration.landscape_tiles_per_quadrant
        ):
            raise ValueError("OpenNV landscape quadrant pixels must divide evenly into tiles")
        return configuration

    @property
    def actor_parity(self) -> ActorParityConfiguration:
        source = _object(self.document, "actorParity")
        sheet = _object(source, "contactSheet")
        configuration = ActorParityConfiguration(
            pose_translation_tolerance_meters=float(source["poseTranslationToleranceMeters"]),
            pose_rotation_tolerance_radians=float(source["poseRotationToleranceRadians"]),
            maximum_reported_worst_bones=int(source["maximumReportedWorstBones"]),
            placement_tolerance_game_units=float(source["placementToleranceGameUnits"]),
            yaw_tolerance_radians=float(source["yawToleranceRadians"]),
            camera_position_tolerance_game_units=float(source["cameraPositionToleranceGameUnits"]),
            camera_aim_tolerance_game_units=float(source["cameraAimToleranceGameUnits"]),
            camera_distance_tolerance_meters=float(source["cameraDistanceToleranceMeters"]),
            vertical_fov_tolerance_degrees=float(source["verticalFovToleranceDegrees"]),
            animation_phase_tolerance_seconds=float(source["animationPhaseToleranceSeconds"]),
            changed_pixel_channel_tolerance=int(source["changedPixelChannelTolerance"]),
            maximum_mean_absolute_error=float(source["maximumMeanAbsoluteError"]),
            maximum_changed_pixel_fraction=float(source["maximumChangedPixelFraction"]),
            maximum_mean_luminance_delta=float(source["maximumMeanLuminanceDelta"]),
            contact_sheet=ActorParityContactSheetConfiguration(
                header_pixels=int(sheet["headerPixels"]),
                background_rgb=_rgb(sheet, "backgroundRgb"),
                title_font_pixels=int(sheet["titleFontPixels"]),
                detail_font_pixels=int(sheet["detailFontPixels"]),
                text_margin_x_pixels=int(sheet["textMarginXPixels"]),
                title_y_pixels=int(sheet["titleYPixels"]),
                detail_y_pixels=int(sheet["detailYPixels"]),
                retail_title_rgb=_rgb(sheet, "retailTitleRgb"),
                godot_title_rgb=_rgb(sheet, "godotTitleRgb"),
                detail_rgb=_rgb(sheet, "detailRgb"),
            ),
        )
        scalar_values = [
            value
            for name, value in configuration.__dict__.items()
            if name != "contact_sheet"
        ]
        if any(value <= 0 for value in scalar_values):
            raise ValueError("OpenNV actorParity scalar policy values must be positive")
        if any(
            value > 1
            for value in (
                configuration.maximum_mean_absolute_error,
                configuration.maximum_changed_pixel_fraction,
                configuration.maximum_mean_luminance_delta,
            )
        ):
            raise ValueError("OpenNV actorParity normalized thresholds must not exceed one")
        if configuration.changed_pixel_channel_tolerance > BYTE_CHANNEL_MAXIMUM:
            raise ValueError("OpenNV actorParity pixel tolerance exceeds one byte")
        sheet_scalars = [
            value
            for value in configuration.contact_sheet.__dict__.values()
            if isinstance(value, int)
        ]
        if any(value <= 0 for value in sheet_scalars):
            raise ValueError("OpenNV actorParity contact-sheet dimensions must be positive")
        return configuration


def configuration_path() -> Path:
    packaged_root = getattr(sys, "_MEIPASS", None)
    if packaged_root is not None:
        return Path(packaged_root) / "config" / RUNTIME_CONFIGURATION_FILE
    return Path(__file__).resolve().parents[2] / "runtime" / "config" / RUNTIME_CONFIGURATION_FILE


def load_runtime_configuration() -> RuntimeConfiguration:
    path = configuration_path()
    payload = path.read_bytes()
    document = json.loads(payload)
    if document.get("schema") != RUNTIME_CONFIGURATION_SCHEMA:
        raise ValueError(f"Unexpected OpenNV runtime configuration: {path}")
    expected_top_level = {"schema", *CONFIGURATION_SECTIONS}
    if set(document) != expected_top_level:
        raise ValueError(
            "OpenNV runtime configuration top-level fields differ: "
            f"expected={sorted(expected_top_level)} actual={sorted(document)}"
        )
    for section_name in CONFIGURATION_SECTIONS:
        section = _object(document, section_name)
        provenance = _object(section, "provenance")
        for field in ("classification", "status", "source", "evidence"):
            if not str(provenance.get(field, "")).strip():
                raise ValueError(
                    f"OpenNV runtime configuration {section_name}.provenance.{field} is empty"
                )
    actor_compiler = _object(document, "actorCompiler")
    states = actor_compiler.get("states")
    if not isinstance(states, list) or not states:
        raise ValueError("OpenNV actorCompiler.states must be a nonempty array")
    references = set()
    for state in states:
        if not isinstance(state, dict):
            raise ValueError("OpenNV actor compiler state must be an object")
        reference = str(state.get("referenceFormId", "")).lower()
        if (
            len(reference) != FORM_ID_HEX_CHARACTERS
            or re.fullmatch(r"[0-9a-f]+", reference) is None
        ):
            raise ValueError(f"OpenNV actor compiler FormID is invalid: {reference}")
        int(reference, FORM_ID_RADIX)
        if reference in references:
            raise ValueError(f"OpenNV actor compiler FormID is duplicated: {reference}")
        references.add(reference)
        tone = state.get("skinToneRgba")
        if (
            not isinstance(tone, list)
            or len(tone) != 4
            or any(not isinstance(channel, int) or channel < 0 or channel > BYTE_CHANNEL_MAXIMUM for channel in tone)
        ):
            raise ValueError(f"OpenNV actor compiler skin tone is invalid: {reference}")
        for field in ("idleAnimation", "skinToneSource"):
            if not str(state.get(field, "")).strip():
                raise ValueError(f"OpenNV actor compiler field is empty: {reference}.{field}")
        aliases = state.get("bodyTextureSourceAliases")
        if not isinstance(aliases, list) or any(not str(alias).strip() for alias in aliases):
            raise ValueError(f"OpenNV actor compiler aliases are invalid: {reference}")
    configuration = RuntimeConfiguration(
        document=document,
        sha256=hashlib.sha256(payload).hexdigest(),
        path=path,
    )
    configuration.content_compiler
    configuration.actor_parity
    return configuration


def _object(parent: dict[str, object], name: str) -> dict[str, object]:
    value = parent.get(name)
    if not isinstance(value, dict):
        raise ValueError(f"OpenNV runtime configuration section is missing: {name}")
    return value


def _rgb(parent: dict[str, object], name: str) -> tuple[int, int, int]:
    value = parent.get(name)
    if not isinstance(value, list) or len(value) != 3:
        raise ValueError(f"OpenNV runtime configuration RGB value is invalid: {name}")
    result = tuple(int(channel) for channel in value)
    if any(channel < 0 or channel > BYTE_CHANNEL_MAXIMUM for channel in result):
        raise ValueError(f"OpenNV runtime configuration RGB value is out of range: {name}")
    return result
