"""Load the single versioned OpenNV policy boundary used by compiler and runtime."""

from __future__ import annotations

import hashlib
import json
import math
import re
import sys
from dataclasses import dataclass
from functools import cache
from pathlib import Path
from types import MappingProxyType
from typing import Mapping


RUNTIME_CONFIGURATION_SCHEMA = "opennv-runtime-configuration/v1"
RUNTIME_CONFIGURATION_FILE = "open-nv-runtime-v1.json"
ACTOR_ARTIFACT_CONFIGURATION_SCHEMA = (
    "opennv-actor-artifact-runtime-configuration/v1"
)
ACTOR_ARTIFACT_CONTENT_COMPILER_FIELDS = (
    "animationSamplesPerSecond",
    "assetIdHexCharacters",
    "defaultMaterialGlossiness",
    "minimumMaterialRoughness",
    "pngCompressionLevel",
    "zeroSpecularEpsilon",
)
FACEGEN_MATERIAL_SCHEMA = "opennv-retail-facegen-material/v2"
FACEGEN_ANIMATION_SCHEMA = "opennv-retail-facegen-animation/v1"
SRGB_TRANSFER_SCHEMA = "opennv-srgb-transfer/v1"
RETAIL_IMAGE_SPACE_SCHEMA = "opennv-retail-image-space-composition/v2"
RETAIL_GRASS_COMPILER_SCHEMA = "opennv-retail-grass-compiler-contract/v1"
RETAIL_GRASS_CAPTURE_SCHEMA = "opennv-retail-grass-capture-contract/v1"
RETAIL_GRASS_CAPTURE_EVENT = "texture-sampler-contract"
BYTE_CHANNEL_MAXIMUM = 255
RGBA_CHANNEL_COUNT = 4
PILLOW_PNG_MAX_COMPRESSION_LEVEL = 9
SHA256_HEX_CHARACTERS = 64
FORM_ID_HEX_CHARACTERS = 8
FORM_ID_RADIX = 16
D3D9_FLOAT_REGISTER_COMPONENTS = 4
RGB_CHANNEL_COUNT = 3
CONFIGURATION_SECTIONS = (
    "world",
    "simulation",
    "renderer",
    "player",
    "xr",
    "pool",
    "pickup",
    "door",
    "hud",
    "capture",
    "proof",
    "performance",
    "diagnosticPreview",
    "actorReview",
    "exteriorEnvironment",
    "falloutEnvironment",
    "retailActorState",
    "actorParity",
    "setupView",
    "desktopLauncher",
    "legalAssets",
    "tooling",
    "contentCompiler",
    "actorCompiler",
)


@dataclass(frozen=True)
class ContentCompilerConfiguration:
    asset_id_hex_characters: int
    stable_id_hex_characters: int
    png_compression_level: int
    animation_samples_per_second: float
    zero_specular_epsilon: float
    minimum_material_roughness: float
    default_material_glossiness: float
    exterior_cell_size_game_units: float
    landscape_quadrant_pixels: int
    landscape_tiles_per_quadrant: int
    landscape_tile_repeats_per_cell: int
    speed_tree: SpeedTreeCompilerConfiguration
    retail_grass: RetailGrassCompilerConfiguration
    facegen_animation: FaceGenAnimationConfiguration
    non_presentation_base_form_ids: frozenset[int]


@dataclass(frozen=True)
class RetailGrassTextureConfiguration:
    path: str
    fnv1a32: int
    top_level_fnv1a32: int
    width_pixels: int
    height_pixels: int
    level_count: int
    d3d9_format: int


@dataclass(frozen=True)
class RetailGrassMaterialConfiguration:
    alpha_mode: str
    diffuse_domain: str
    sampler: str
    vertex_lighting_bake: str
    wind_bake: str
    texture_clamp_mode: int
    double_sided: bool
    unshaded: bool


@dataclass(frozen=True)
class RetailGrassShaderConfiguration:
    vertex_fnv1a32: int
    pixel_fnv1a32: int
    instance_first_register: int
    instance_capacity: int
    vertex_constant_register_count: int
    pixel_constant_register_count: int
    instance_register_ceiling: float
    float_tolerance: float
    registers: dict[str, int]


@dataclass(frozen=True)
class RetailGrassDrawConfiguration:
    primitive_type: int
    vertex_stride_bytes: int
    declaration: tuple[tuple[int, ...], ...]
    sampler: dict[str, int]
    render_state: dict[str, int]
    render_frame_lead: int
    strip_bridge_indices: int
    primitive_count_bias: int
    full_batch_trailing_bridge_indices: int


@dataclass(frozen=True)
class RetailGrassReconstructionConfiguration:
    zero_length_epsilon: float
    scale_base: float
    scale_per_instance: float
    shade_base: float
    shade_fraction: float
    phase_spatial_scale: float
    phase_radians_scale: float
    phase_offset: float
    tau: float
    pi: float


@dataclass(frozen=True)
class RetailGrassCaptureConfiguration:
    schema: str
    event: str
    texture_stage_count: int
    maximum_candidates: int
    maximum_records: int
    maximum_shader_bytes: int
    maximum_vertex_buffer_bytes: int
    minimum_matching_records: int
    required_matched_resource_count: int
    require_every_observed_mesh: bool


@dataclass(frozen=True)
class RetailGrassMeshConfiguration:
    suffix: str
    path: str
    sha256: str
    source_vertices: int
    strip_length: int


@dataclass(frozen=True)
class SpeedTreeCompilerConfiguration:
    billboard_texture: str
    billboard_alpha_cutoff: float


@dataclass(frozen=True)
class FaceGenLipConfiguration:
    byte_order: str
    version: int
    file_header_fields: tuple[str, ...]
    decoded_header_fields: tuple[str, ...]
    integer_bytes: int
    value_bytes: int
    run_marker: int
    run_length_bytes: int
    stored_size_bias_bytes: int
    implicit_trailing_zero_bytes: int
    compressed_flag: int
    big_endian_flag: int
    uncompressed_marker: int
    sample_rate_hz: float
    interpolation: str
    zero_outside_authored_range: bool
    maximum_decoded_bytes: int
    maximum_frames: int
    maximum_absolute_weight: float
    target_names: tuple[str, ...]
    morph_target_names: tuple[str | None, ...]


@dataclass(frozen=True)
class FaceGenTriConfiguration:
    signature: str
    byte_order: str
    header_fields: tuple[str, ...]
    integer_bytes: int
    scalar_bytes: int
    delta_component_bytes: int
    reserved_bytes: int
    labelled_vertex_prefix_bytes: int
    labelled_surface_prefix_bytes: int
    uv_extension_flag: int
    position_components: int
    uv_components: int
    triangle_indices: int
    quad_indices: int
    export_morph_kinds: tuple[str, ...]
    target_name_collision_policy: str
    normal_target_policy: str


@dataclass(frozen=True)
class FaceGenAnimationConfiguration:
    schema: str
    lip: FaceGenLipConfiguration
    tri: FaceGenTriConfiguration


@dataclass(frozen=True)
class RetailGrassCompilerConfiguration:
    schema: str
    material_schema: str
    material_model: str
    material: RetailGrassMaterialConfiguration
    texture: RetailGrassTextureConfiguration
    shader: RetailGrassShaderConfiguration
    draw: RetailGrassDrawConfiguration
    capture: RetailGrassCaptureConfiguration
    reconstruction: RetailGrassReconstructionConfiguration
    meshes: tuple[RetailGrassMeshConfiguration, ...]
    meshes_by_batch_vertices: Mapping[int, RetailGrassMeshConfiguration]


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
    ground_contact_maximum_ulp: int
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
class ActorRigProfileConfiguration:
    skeleton_root_node: str
    unparented_rigid_node: str


@dataclass(frozen=True)
class ActorRigConfiguration:
    biped_head_node: str
    profiles: Mapping[str, ActorRigProfileConfiguration]


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

    def actor_artifact_manifest(self) -> dict[str, object]:
        return actor_artifact_configuration_manifest(self.document)

    @property
    def actor_rig(self) -> ActorRigConfiguration:
        source = _object(_object(self.document, "actorCompiler"), "rigidAttachment")
        profiles_source = _object(source, "profiles")
        profiles = {
            str(record_type): ActorRigProfileConfiguration(
                skeleton_root_node=str(profile["skeletonRootNode"]),
                unparented_rigid_node=str(profile["unparentedRigidNode"]),
            )
            for record_type, profile in profiles_source.items()
            if isinstance(profile, dict)
        }
        configuration = ActorRigConfiguration(
            biped_head_node=str(source["bipedHeadNode"]),
            profiles=MappingProxyType(profiles),
        )
        if (
            not configuration.biped_head_node.strip()
            or len(profiles) != len(profiles_source)
            or not profiles
            or any(
                not record_type.strip()
                or not profile.skeleton_root_node.strip()
                or not profile.unparented_rigid_node.strip()
                for record_type, profile in profiles.items()
            )
        ):
            raise ValueError("OpenNV actor rigid-attachment contract is invalid")
        return configuration

    @property
    def content_compiler(self) -> ContentCompilerConfiguration:
        source = _object(self.document, "contentCompiler")
        non_presentation_form_ids = source["nonPresentationBaseFormIds"]
        if (
            not isinstance(non_presentation_form_ids, list)
            or not non_presentation_form_ids
            or any(
                not isinstance(value, str)
                or re.fullmatch(r"[0-9a-fA-F]{8}", value) is None
                for value in non_presentation_form_ids
            )
        ):
            raise ValueError("OpenNV non-presentation base FormIDs are invalid")
        configuration = ContentCompilerConfiguration(
            asset_id_hex_characters=int(source["assetIdHexCharacters"]),
            stable_id_hex_characters=int(source["stableIdHexCharacters"]),
            png_compression_level=int(source["pngCompressionLevel"]),
            animation_samples_per_second=float(source["animationSamplesPerSecond"]),
            zero_specular_epsilon=float(source["zeroSpecularEpsilon"]),
            minimum_material_roughness=float(source["minimumMaterialRoughness"]),
            default_material_glossiness=float(source["defaultMaterialGlossiness"]),
            exterior_cell_size_game_units=float(source["exteriorCellSizeGameUnits"]),
            landscape_quadrant_pixels=int(source["landscapeQuadrantPixels"]),
            landscape_tiles_per_quadrant=int(source["landscapeTilesPerQuadrant"]),
            landscape_tile_repeats_per_cell=int(source["landscapeTileRepeatsPerCell"]),
            speed_tree=_speed_tree_configuration(_object(source, "speedTree")),
            retail_grass=_retail_grass_configuration(
                _object(source, "retailGrass")
            ),
            facegen_animation=_facegen_animation_configuration(
                _object(_object(self.document, "actorCompiler"), "faceGenAnimation")
            ),
            non_presentation_base_form_ids=frozenset(
                int(str(value), FORM_ID_RADIX)
                for value in non_presentation_form_ids
            ),
        )
        for name, value in configuration.__dict__.items():
            if name in {
                "non_presentation_base_form_ids",
                "retail_grass",
                "speed_tree",
                "facegen_animation",
            }:
                continue
            if value <= 0:
                raise ValueError(f"OpenNV contentCompiler.{name} must be positive")
        if not configuration.non_presentation_base_form_ids:
            raise ValueError("OpenNV non-presentation base FormIDs must be nonempty")
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
            ground_contact_maximum_ulp=int(source["groundContactMaximumUlp"]),
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

def actor_artifact_configuration_payload(
    document: dict[str, object],
) -> dict[str, object]:
    actor_compiler = _object(document, "actorCompiler")
    content_compiler = _object(document, "contentCompiler")
    missing = set(ACTOR_ARTIFACT_CONTENT_COMPILER_FIELDS) - set(content_compiler)
    if missing:
        raise ValueError(
            "OpenNV actor artifact configuration fields are missing: "
            + ", ".join(sorted(missing))
        )
    return {
        "actorCompiler": actor_compiler,
        "contentCompiler": {
            field: content_compiler[field]
            for field in ACTOR_ARTIFACT_CONTENT_COMPILER_FIELDS
        },
    }


def actor_artifact_configuration_manifest(
    document: dict[str, object],
) -> dict[str, object]:
    payload = _configuration_identity_value(
        actor_artifact_configuration_payload(document)
    )
    digest = hashlib.sha256(
        json.dumps(
            payload,
            ensure_ascii=False,
            separators=(",", ":"),
            sort_keys=True,
        ).encode("utf-8")
    ).hexdigest()
    return {
        "schema": ACTOR_ARTIFACT_CONFIGURATION_SCHEMA,
        "sha256": digest,
        "sections": {
            "actorCompiler": "all",
            "contentCompiler": list(ACTOR_ARTIFACT_CONTENT_COMPILER_FIELDS),
        },
    }


def _configuration_identity_value(value: object) -> object:
    if isinstance(value, dict):
        return {
            str(key): _configuration_identity_value(child)
            for key, child in value.items()
        }
    if isinstance(value, list):
        return [_configuration_identity_value(child) for child in value]
    if isinstance(value, bool) or value is None or isinstance(value, str):
        return value
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float) and math.isfinite(value):
        return format(value, ".17g").casefold()
    raise ValueError("OpenNV actor artifact configuration contains an invalid value")


def configuration_path() -> Path:
    packaged_root = getattr(sys, "_MEIPASS", None)
    if packaged_root is not None:
        return Path(packaged_root) / "config" / RUNTIME_CONFIGURATION_FILE
    return Path(__file__).resolve().parents[2] / "runtime" / "config" / RUNTIME_CONFIGURATION_FILE


@cache
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
    performance = _object(document, "performance")
    sample_interval = performance.get("sampleIntervalSeconds")
    if (
        not isinstance(sample_interval, (int, float))
        or isinstance(sample_interval, bool)
        or not math.isfinite(float(sample_interval))
        or float(sample_interval) <= 0.0
    ):
        raise ValueError("OpenNV performance sample interval must be positive and finite")
    tooling_recipes = _object(_object(document, "tooling"), "recipeFiles")
    if (
        not tooling_recipes
        or any(
            not str(key).strip()
            or not isinstance(value, str)
            or not value.strip()
            or Path(value).name != value
            for key, value in tooling_recipes.items()
        )
    ):
        raise ValueError("OpenNV tooling recipe registry is invalid")
    legal_assets = _object(document, "legalAssets")
    owned_data = _object(legal_assets, "ownedData")
    for field in (
        "defaultOpeningRecipe",
        "defaultCellRecipe",
        "linkedWorldProofCellRecipe",
        "defaultCacheRoot",
        "packagedCompilerName",
        "smokeModelLogicalPath",
    ):
        if not str(legal_assets.get(field, "")).strip():
            raise ValueError(f"OpenNV legalAssets.{field} is empty")
    source_content_tool = _object(legal_assets, "sourceContentTool")
    for field in ("executable", "script", "compilerName"):
        if not str(source_content_tool.get(field, "")).strip():
            raise ValueError(f"OpenNV legalAssets.sourceContentTool.{field} is empty")
    for field in (
        "masterFile",
        "defaultIniFile",
        "meshesArchiveFile",
        "uiArchiveFile",
        "dataDirectoryName",
        "videoDirectoryName",
    ):
        if not str(owned_data.get(field, "")).strip():
            raise ValueError(f"OpenNV legalAssets.ownedData.{field} is empty")
    texture_archives = owned_data.get("textureArchiveFiles")
    if (
        not isinstance(texture_archives, list)
        or not texture_archives
        or any(not isinstance(value, str) or not value.strip() for value in texture_archives)
    ):
        raise ValueError("OpenNV legalAssets owned texture archives are invalid")
    video_import = _object(legal_assets, "videoImport")
    for field in (
        "transcoderExecutable",
        "outputExtension",
        "containerFormat",
        "videoCodec",
        "audioCodec",
        "pixelFormat",
        "logLevel",
    ):
        if not str(video_import.get(field, "")).strip():
            raise ValueError(f"OpenNV legalAssets.videoImport.{field} is empty")
    for field in ("videoQuality", "audioQuality", "threads"):
        if int(video_import.get(field, 0)) <= 0:
            raise ValueError(f"OpenNV legalAssets.videoImport.{field} must be positive")
    _validate_fallout_image_space(_object(document, "falloutEnvironment"))
    gallery_capture = _object(_object(document, "capture"), "gallery")
    gallery_provenance = _object(gallery_capture, "provenance")
    for field in ("classification", "status", "source", "evidence"):
        if not str(gallery_provenance.get(field, "")).strip():
            raise ValueError(
                f"OpenNV capture.gallery.provenance.{field} is empty"
            )
    for field in (
        "verticalFovDegrees",
        "maximumFrameOccupancy",
        "framesPerSubject",
        "framesPerSecond",
        "minimumMotionProgressFraction",
    ):
        value = float(gallery_capture.get(field, float("nan")))
        if not math.isfinite(value) or value <= 0.0:
            raise ValueError(f"OpenNV capture.gallery.{field} must be positive")
    for field in (
        "maximumFrameOccupancy",
        "minimumMotionProgressFraction",
    ):
        value = float(gallery_capture.get(field, float("nan")))
        if not math.isfinite(value) or not 0.0 <= value <= 1.0:
            raise ValueError(f"OpenNV capture.gallery.{field} must be normalized")
    if gallery_capture.get("modelFrontAxis") not in ("positive-z", "negative-z"):
        raise ValueError("OpenNV capture.gallery.modelFrontAxis is unsupported")
    actor_shot_kinds = _object(document, "capture").get("actorShotKinds")
    if (
        not isinstance(actor_shot_kinds, list)
        or not actor_shot_kinds
        or len(set(str(value) for value in actor_shot_kinds))
        != len(actor_shot_kinds)
    ):
        raise ValueError(
            "OpenNV capture.actorShotKinds must be nonempty and unique"
        )
    presentation = _object(gallery_capture, "retailPresentationSelection")
    candidate_shot_kinds = presentation.get("candidateShotKinds")
    facing_rules = presentation.get("semanticFocusFacingRules")
    if (
        presentation.get("schema")
        != "opennv-gallery-presentation-selection/v1"
        or not isinstance(candidate_shot_kinds, list)
        or not candidate_shot_kinds
        or len(set(str(value) for value in candidate_shot_kinds))
        != len(candidate_shot_kinds)
        or any(value not in actor_shot_kinds for value in candidate_shot_kinds)
        or presentation.get("requiredSurfaceStatus")
        != "visible-final-eye-semantic-focus-draw"
        or presentation.get("requireSemanticFocusSurface") is not True
        or presentation.get("requireCameraOutsideActorWorldBound") is not True
        or presentation.get("requireClearCameraCorridor") is not True
        or not math.isfinite(
            float(presentation.get("cameraTranslationToleranceGameUnits", float("nan")))
        )
        or float(presentation["cameraTranslationToleranceGameUnits"]) <= 0.0
        or presentation.get("tieBreak")
        != "candidate-order-then-lowest-source-frame"
        or not isinstance(facing_rules, list)
        or not facing_rules
    ):
        raise ValueError(
            "OpenNV capture.gallery retail presentation selection is invalid"
        )
    focus_kinds: set[str] = set()
    candidate_set = set(str(value) for value in candidate_shot_kinds)
    for rule in facing_rules:
        if not isinstance(rule, dict):
            raise ValueError("OpenNV gallery semantic-focus facing rule is invalid")
        focus_kind = str(rule.get("focusKind", ""))
        allowed = rule.get("allowedShotKinds")
        minimum_dot = float(
            rule.get("minimumCameraDirectionDotFocusForward", float("nan"))
        )
        maximum_dot = float(
            rule.get("maximumCameraDirectionDotFocusForward", float("nan"))
        )
        if (
            not focus_kind
            or focus_kind in focus_kinds
            or not isinstance(allowed, list)
            or not allowed
            or len(set(str(value) for value in allowed)) != len(allowed)
            or any(str(value) not in candidate_set for value in allowed)
            or not math.isfinite(minimum_dot)
            or not math.isfinite(maximum_dot)
            or minimum_dot < -1.0
            or maximum_dot > 1.0
            or minimum_dot > maximum_dot
        ):
            raise ValueError("OpenNV gallery semantic-focus facing rule is invalid")
        focus_kinds.add(focus_kind)
    if gallery_capture.get("targetNodeRole") != "sidecar-biped-head":
        raise ValueError(
            "OpenNV capture.gallery.targetNodeRole must use the owned sidecar biped head"
        )
    if gallery_capture.get("facingPoseSource") != "full-body-owned-animation-root":
        raise ValueError(
            "OpenNV capture.gallery.facingPoseSource must use the owned full-body animation root"
        )
    if gallery_capture.get("occlusionClearanceSource") != "camera-near-plane":
        raise ValueError(
            "OpenNV capture.gallery.occlusionClearanceSource must use the configured camera near plane"
        )
    if not str(gallery_capture.get("stillImageExtension", "")).startswith("."):
        raise ValueError(
            "OpenNV capture.gallery.stillImageExtension must be an extension"
        )
    for field in ("framesPerSubject", "framesPerSecond"):
        if not isinstance(gallery_capture.get(field), int):
            raise ValueError(f"OpenNV capture.gallery.{field} must be an integer")
    gallery_video = _object(gallery_capture, "video")
    gallery_video_provenance = _object(gallery_video, "provenance")
    for field in ("classification", "status", "source", "evidence"):
        if not str(gallery_video_provenance.get(field, "")).strip():
            raise ValueError(
                f"OpenNV capture.gallery.video.provenance.{field} is empty"
            )
    for field in (
        "sourceContainerExtension",
        "deliveryContainerExtension",
        "deliveryFileName",
        "reportFileName",
        "videoCodec",
        "pixelFormat",
        "encoderPreset",
    ):
        if not str(gallery_video.get(field, "")).strip():
            raise ValueError(f"OpenNV capture.gallery.video.{field} is empty")
    for field in ("sourceContainerExtension", "deliveryContainerExtension"):
        if not str(gallery_video[field]).startswith("."):
            raise ValueError(
                f"OpenNV capture.gallery.video.{field} must be an extension"
            )
    if Path(str(gallery_video["deliveryFileName"])).name != str(
        gallery_video["deliveryFileName"]
    ) or not str(gallery_video["deliveryFileName"]).endswith(
        str(gallery_video["deliveryContainerExtension"])
    ):
        raise ValueError("OpenNV gallery delivery file name is invalid")
    if Path(str(gallery_video["reportFileName"])).name != str(
        gallery_video["reportFileName"]
    ) or not str(gallery_video["reportFileName"]).endswith(".json"):
        raise ValueError("OpenNV gallery report file name is invalid")
    for field in ("constantRateFactor", "durationToleranceFrames"):
        value = gallery_video.get(field)
        if not isinstance(value, int) or value < 0:
            raise ValueError(
                f"OpenNV capture.gallery.video.{field} must be a nonnegative integer"
            )
    actor_compiler = _object(document, "actorCompiler")
    facegen_material = _object(actor_compiler, "faceGenMaterial")
    if facegen_material.get("schema") != FACEGEN_MATERIAL_SCHEMA:
        raise ValueError("OpenNV actorCompiler.faceGenMaterial schema is invalid")
    if facegen_material.get("sourceSamplerSrgbTexture") is not False:
        raise ValueError(
            "OpenNV actorCompiler.faceGenMaterial source sampler state is unsupported"
        )
    if facegen_material.get("sourceRenderTargetSrgbWrite") is not False:
        raise ValueError(
            "OpenNV actorCompiler.faceGenMaterial source render-target state is unsupported"
        )
    signed_detail_neutral = float(facegen_material.get("signedDetailNeutral", float("nan")))
    if (
        not math.isfinite(signed_detail_neutral)
        or not 0.0 <= signed_detail_neutral <= 1.0
    ):
        raise ValueError(
            "OpenNV actorCompiler.faceGenMaterial signed detail neutral is invalid"
        )
    for field in ("signedDetailScale", "toneScale"):
        value = float(facegen_material.get(field, float("nan")))
        if not math.isfinite(value) or value <= 0.0:
            raise ValueError(f"OpenNV actorCompiler.faceGenMaterial {field} is invalid")
    tone = facegen_material.get("toneMapRgba")
    if (
        not isinstance(tone, list)
        or len(tone) != RGBA_CHANNEL_COUNT
        or any(
            not isinstance(channel, int)
            or channel < 0
            or channel > BYTE_CHANNEL_MAXIMUM
            for channel in tone
        )
    ):
        raise ValueError("OpenNV actorCompiler.faceGenMaterial tone map is invalid")
    if not str(facegen_material.get("source", "")).strip():
        raise ValueError("OpenNV actorCompiler.faceGenMaterial source is empty")
    transfer = _object(facegen_material, "runtimeAlbedoTransfer")
    if transfer.get("schema") != SRGB_TRANSFER_SCHEMA:
        raise ValueError(
            "OpenNV actorCompiler.faceGenMaterial runtime albedo transfer schema is invalid"
        )
    for field in (
        "encodedCutoff",
        "linearScale",
        "offset",
        "normalization",
        "exponent",
    ):
        value = float(transfer.get(field, float("nan")))
        if not math.isfinite(value) or value <= 0.0:
            raise ValueError(
                "OpenNV actorCompiler.faceGenMaterial runtime albedo transfer "
                f"{field} is invalid"
            )
    if not str(transfer.get("source", "")).strip():
        raise ValueError(
            "OpenNV actorCompiler.faceGenMaterial runtime albedo transfer source is empty"
        )
    profiles = _object(actor_compiler, "animationProfiles")
    humanoid_profile = _object(profiles, "NPC_")
    creature_profile = _object(profiles, "CREA")
    if set(humanoid_profile) != {"mode", "path"} or (
        humanoid_profile.get("mode") != "exact-owned-member"
        or not str(humanoid_profile.get("path", "")).strip()
    ):
        raise ValueError("OpenNV NPC_ animation profile is invalid")
    if set(creature_profile) != {"mode", "fileName"} or (
        creature_profile.get("mode") != "skeleton-directory"
        or not str(creature_profile.get("fileName", "")).strip()
    ):
        raise ValueError("OpenNV CREA animation profile is invalid")
    rigid_attachment = _object(actor_compiler, "rigidAttachment")
    rigid_provenance = _object(rigid_attachment, "provenance")
    for field in ("classification", "status", "source", "evidence"):
        if not str(rigid_provenance.get(field, "")).strip():
            raise ValueError(
                f"OpenNV actor rigid-attachment provenance {field} is empty"
            )
    configuration = RuntimeConfiguration(
        document=document,
        sha256=hashlib.sha256(payload).hexdigest(),
        path=path,
    )
    configuration.content_compiler
    configuration.actor_parity
    configuration.actor_rig
    return configuration


def _validate_fallout_image_space(fallout_environment: dict[str, object]) -> None:
    source = _object(fallout_environment, "imageSpace")
    provenance = _object(source, "provenance")
    for field in ("classification", "status", "source", "evidence"):
        if not str(provenance.get(field, "")).strip():
            raise ValueError(
                f"OpenNV falloutEnvironment.imageSpace.provenance.{field} is empty"
            )
    if source.get("schema") != RETAIL_IMAGE_SPACE_SCHEMA:
        raise ValueError("OpenNV Fallout image-space schema is invalid")
    channels = source.get("modifierChannels")
    if not isinstance(channels, list) or not channels or any(
        not isinstance(channel, dict)
        or not str(channel.get("name", "")).strip()
        or not isinstance(channel.get("traitIndex"), int)
        or int(channel["traitIndex"]) < 0
        for channel in channels
    ):
        raise ValueError("OpenNV Fallout image-space modifier channels are invalid")
    channel_names = [str(channel["name"]) for channel in channels]
    channel_indices = [int(channel["traitIndex"]) for channel in channels]
    if len(set(channel_names)) != len(channel_names) or len(set(channel_indices)) != len(
        channel_indices
    ):
        raise ValueError("OpenNV Fallout image-space modifier channels are duplicated")
    trait_indices = _object(source, "traitIndices")
    if not trait_indices or any(
        not isinstance(value, int) or value < 0 for value in trait_indices.values()
    ) or len(set(trait_indices.values())) != len(trait_indices):
        raise ValueError("OpenNV Fallout image-space trait indices are invalid")
    tolerance = float(source.get("shaderConstantTolerance", float("nan")))
    weights = source.get("luminanceWeightsRgb")
    if (
        not math.isfinite(tolerance)
        or tolerance <= 0.0
        or not isinstance(weights, list)
        or len(weights) != RGB_CHANNEL_COUNT
        or any(not math.isfinite(float(value)) or float(value) < 0.0 for value in weights)
        or not math.isclose(sum(float(value) for value in weights), 1.0, abs_tol=tolerance)
    ):
        raise ValueError("OpenNV Fallout cinematic luminance contract is invalid")
    register_names = (
        "hdrParametersRegister",
        "cinematicRegister",
        "tintRegister",
        "fadeRegister",
    )
    registers = [source.get(name) for name in register_names]
    if (
        source.get("shaderRegisterComponents") != D3D9_FLOAT_REGISTER_COMPONENTS
        or any(not isinstance(value, int) or value < 0 for value in registers)
        or len(set(registers)) != len(registers)
        or not isinstance(source.get("shaderByteCount"), int)
        or int(source["shaderByteCount"]) <= 0
        or re.fullmatch(r"0x[0-9a-fA-F]{8}", str(source.get("shaderFnv1a32", "")))
        is None
        or not str(source.get("shaderPath", "")).strip()
        or not isinstance(source.get("canvasLayer"), int)
    ):
        raise ValueError("OpenNV Fallout retail shader evidence contract is invalid")
    hdr_blend = _object(source, "hdrBlend")
    stages = (
        hdr_blend.get("blurredAdaptationStage"),
        hdr_blend.get("hdrSceneStage"),
    )
    if (
        any(not isinstance(value, int) or value < 0 for value in stages)
        or len(set(stages)) != len(stages)
        or not isinstance(hdr_blend.get("d3d9ResourceType"), int)
        or int(hdr_blend["d3d9ResourceType"]) <= 0
        or not isinstance(hdr_blend.get("d3d9SurfaceType"), int)
        or int(hdr_blend["d3d9SurfaceType"]) <= 0
        or not isinstance(hdr_blend.get("d3d9Usage"), int)
        or int(hdr_blend["d3d9Usage"]) < 0
        or not isinstance(hdr_blend.get("d3d9Pool"), int)
        or int(hdr_blend["d3d9Pool"]) < 0
        or not isinstance(hdr_blend.get("d3d9MultiSampleType"), int)
        or int(hdr_blend["d3d9MultiSampleType"]) < 0
        or not isinstance(hdr_blend.get("d3d9MultiSampleQuality"), int)
        or int(hdr_blend["d3d9MultiSampleQuality"]) < 0
        or not isinstance(hdr_blend.get("levelCount"), int)
        or int(hdr_blend["levelCount"]) <= 0
        or not isinstance(hdr_blend.get("d3d9TextureFormat"), int)
        or int(hdr_blend["d3d9TextureFormat"]) <= 0
        or not str(hdr_blend.get("d3d9TextureFormatName", "")).strip()
        or not isinstance(hdr_blend.get("componentCount"), int)
        or int(hdr_blend["componentCount"]) <= 0
        or not isinstance(hdr_blend.get("componentBytes"), int)
        or int(hdr_blend["componentBytes"]) <= 0
        or not math.isfinite(float(hdr_blend.get("bloomNormalizationScale", float("nan"))))
        or float(hdr_blend["bloomNormalizationScale"]) <= 0.0
        or not isinstance(hdr_blend.get("samplerSrgbEnabled"), bool)
        or not isinstance(hdr_blend.get("renderTargetSrgbWriteEnabled"), bool)
        or hdr_blend.get("outputTransfer") != "linear"
        or hdr_blend.get("samplerFilter") != "linear"
    ):
        raise ValueError("OpenNV Fallout HDR blend contract is invalid")
    for field in (
        "workGroupSidePixels",
        "readbackTimeoutSeconds",
        "adaptationDeltaSeconds",
        "adaptationRetentionBase",
        "minimumAdaptationMagnitude",
        "brightThreshold",
        "brightScale",
    ):
        value = float(hdr_blend.get(field, float("nan")))
        if not math.isfinite(value) or value <= 0.0:
            raise ValueError(f"OpenNV Fallout HDR {field} must be positive")
    if float(hdr_blend["adaptationRetentionBase"]) > 1.0:
        raise ValueError("OpenNV Fallout HDR adaptation retention exceeds one")
    blur_weights = hdr_blend.get("blurWeights")
    if (
        not isinstance(blur_weights, list)
        or not blur_weights
        or len(blur_weights) % 2 == 0
        or any(
            not math.isfinite(float(value)) or float(value) <= 0.0
            for value in blur_weights
        )
    ):
        raise ValueError("OpenNV Fallout HDR blur kernel is invalid")
    targets = _object(hdr_blend, "targets")
    expected_target_names = {
        "halfPixels",
        "sourcePixels",
        "downsamplePixels",
        "adaptationPixels",
        "brightPixels",
        "bloomPixels",
    }
    if set(targets) != expected_target_names:
        raise ValueError("OpenNV Fallout HDR target fields differ")
    target_pairs = [
        targets["halfPixels"],
        targets["sourcePixels"],
        targets["adaptationPixels"],
        targets["brightPixels"],
        targets["bloomPixels"],
    ]
    downsample_pairs = targets["downsamplePixels"]
    if not isinstance(downsample_pairs, list) or not downsample_pairs:
        raise ValueError("OpenNV Fallout HDR downsample targets are empty")
    target_pairs.extend(downsample_pairs)
    if any(
        not isinstance(pair, list)
        or len(pair) != 2
        or any(not isinstance(value, int) or value <= 0 for value in pair)
        for pair in target_pairs
    ):
        raise ValueError("OpenNV Fallout HDR target dimensions are invalid")


def _retail_grass_configuration(
    source: dict[str, object],
) -> RetailGrassCompilerConfiguration:
    provenance = _object(source, "provenance")
    for field in ("classification", "status", "source", "evidence"):
        if not str(provenance.get(field, "")).strip():
            raise ValueError(f"OpenNV retail grass provenance {field} is empty")
    if source.get("schema") != RETAIL_GRASS_COMPILER_SCHEMA:
        raise ValueError("OpenNV retail grass compiler schema is invalid")
    material = _object(source, "material")
    texture = _object(source, "texture")
    shader = _object(source, "shader")
    draw = _object(source, "draw")
    capture = _object(source, "capture")
    reconstruction = _object(source, "reconstruction")
    registers = _integer_object(shader, "registers")
    sampler = _integer_object(draw, "sampler")
    render_state = _integer_object(draw, "renderState")
    declaration_source = draw.get("declaration")
    if not isinstance(declaration_source, list) or not declaration_source:
        raise ValueError("OpenNV retail grass declaration is empty")
    declaration = tuple(
        tuple(int(value) for value in row)
        for row in declaration_source
        if isinstance(row, list) and row
    )
    if len(declaration) != len(declaration_source) or any(
        value < 0 for row in declaration for value in row
    ):
        raise ValueError("OpenNV retail grass declaration is invalid")
    meshes_source = source.get("meshes")
    if not isinstance(meshes_source, list) or not meshes_source:
        raise ValueError("OpenNV retail grass mesh contract is empty")
    meshes = tuple(
        RetailGrassMeshConfiguration(
            suffix=str(row["suffix"]),
            path=str(row["path"]),
            sha256=str(row["sha256"]),
            source_vertices=int(row["sourceVertices"]),
            strip_length=int(row["stripLength"]),
        )
        for row in meshes_source
        if isinstance(row, dict)
    )
    if len(meshes) != len(meshes_source) or len({mesh.suffix for mesh in meshes}) != len(
        meshes
    ):
        raise ValueError("OpenNV retail grass mesh identities are invalid")
    shader_configuration = RetailGrassShaderConfiguration(
        vertex_fnv1a32=_canonical_u32(
            shader["vertexFnv1a32"], "grass vertex shader"
        ),
        pixel_fnv1a32=_canonical_u32(
            shader["pixelFnv1a32"], "grass pixel shader"
        ),
        instance_first_register=int(shader["instanceFirstRegister"]),
        instance_capacity=int(shader["instanceCapacity"]),
        vertex_constant_register_count=int(
            shader["vertexConstantRegisterCount"]
        ),
        pixel_constant_register_count=int(shader["pixelConstantRegisterCount"]),
        instance_register_ceiling=float(shader["instanceRegisterCeiling"]),
        float_tolerance=float(shader["floatTolerance"]),
        registers=registers,
    )
    mesh_index = {
        mesh.source_vertices * shader_configuration.instance_capacity: mesh
        for mesh in meshes
    }
    if len(mesh_index) != len(meshes):
        raise ValueError("OpenNV retail grass batch-vertex identities are not unique")
    configuration = RetailGrassCompilerConfiguration(
        schema=str(source["schema"]),
        material_schema=str(source["materialSchema"]),
        material_model=str(source["materialModel"]),
        material=RetailGrassMaterialConfiguration(
            alpha_mode=str(material["alphaMode"]),
            diffuse_domain=str(material["diffuseDomain"]),
            sampler=str(material["sampler"]),
            vertex_lighting_bake=str(material["vertexLightingBake"]),
            wind_bake=str(material["windBake"]),
            texture_clamp_mode=int(material["textureClampMode"]),
            double_sided=bool(material["doubleSided"]),
            unshaded=bool(material["unshaded"]),
        ),
        texture=RetailGrassTextureConfiguration(
            path=str(texture["path"]),
            fnv1a32=_canonical_u32(texture["fnv1a32"], "grass texture"),
            top_level_fnv1a32=_canonical_u32(
                texture["topLevelFnv1a32"], "grass texture top level"
            ),
            width_pixels=int(texture["widthPixels"]),
            height_pixels=int(texture["heightPixels"]),
            level_count=int(texture["levelCount"]),
            d3d9_format=int(texture["d3d9Format"]),
        ),
        shader=shader_configuration,
        draw=RetailGrassDrawConfiguration(
            primitive_type=int(draw["primitiveType"]),
            vertex_stride_bytes=int(draw["vertexStrideBytes"]),
            declaration=declaration,
            sampler=sampler,
            render_state=render_state,
            render_frame_lead=int(draw["renderFrameLead"]),
            strip_bridge_indices=int(draw["stripBridgeIndices"]),
            primitive_count_bias=int(draw["primitiveCountBias"]),
            full_batch_trailing_bridge_indices=int(
                draw["fullBatchTrailingBridgeIndices"]
            ),
        ),
        capture=RetailGrassCaptureConfiguration(
            schema=str(capture["schema"]),
            event=str(capture["event"]),
            texture_stage_count=int(capture["textureStageCount"]),
            maximum_candidates=int(capture["maximumCandidates"]),
            maximum_records=int(capture["maximumRecords"]),
            maximum_shader_bytes=int(capture["maximumShaderBytes"]),
            maximum_vertex_buffer_bytes=int(capture["maximumVertexBufferBytes"]),
            minimum_matching_records=int(capture["minimumMatchingRecords"]),
            required_matched_resource_count=int(
                capture["requiredMatchedResourceCount"]
            ),
            require_every_observed_mesh=bool(
                capture["requireEveryObservedMesh"]
            ),
        ),
        reconstruction=RetailGrassReconstructionConfiguration(
            zero_length_epsilon=float(reconstruction["zeroLengthEpsilon"]),
            scale_base=float(reconstruction["scaleBase"]),
            scale_per_instance=float(reconstruction["scalePerInstance"]),
            shade_base=float(reconstruction["shadeBase"]),
            shade_fraction=float(reconstruction["shadeFraction"]),
            phase_spatial_scale=float(reconstruction["phaseSpatialScale"]),
            phase_radians_scale=float(reconstruction["phaseRadiansScale"]),
            phase_offset=float(reconstruction["phaseOffset"]),
            tau=float(reconstruction["tau"]),
            pi=float(reconstruction["pi"]),
        ),
        meshes=meshes,
        meshes_by_batch_vertices=MappingProxyType(mesh_index),
    )
    scalar_values = (
        configuration.texture.width_pixels,
        configuration.texture.height_pixels,
        configuration.texture.level_count,
        configuration.texture.d3d9_format,
        configuration.shader.instance_capacity,
        configuration.shader.vertex_constant_register_count,
        configuration.shader.pixel_constant_register_count,
        configuration.shader.instance_register_ceiling,
        configuration.shader.float_tolerance,
        configuration.draw.primitive_type,
        configuration.draw.vertex_stride_bytes,
        configuration.capture.texture_stage_count,
        configuration.capture.maximum_candidates,
        configuration.capture.maximum_records,
        configuration.capture.maximum_shader_bytes,
        configuration.capture.maximum_vertex_buffer_bytes,
        configuration.capture.minimum_matching_records,
        configuration.capture.required_matched_resource_count,
        *(value for value in configuration.reconstruction.__dict__.values()),
        *(mesh.source_vertices for mesh in configuration.meshes),
        *(mesh.strip_length for mesh in configuration.meshes),
    )
    if any(not math.isfinite(float(value)) or float(value) <= 0.0 for value in scalar_values):
        raise ValueError("OpenNV retail grass positive values are invalid")
    if any(
        not mesh.path.strip()
        or re.fullmatch(r"[0-9a-f]{64}", mesh.sha256.casefold()) is None
        for mesh in configuration.meshes
    ):
        raise ValueError("OpenNV retail grass owned mesh identity is invalid")
    if (
        not configuration.material_schema
        or not configuration.material_model
        or any(
            not value
            for value in (
                configuration.material.diffuse_domain,
                configuration.material.alpha_mode,
                configuration.material.sampler,
                configuration.material.vertex_lighting_bake,
                configuration.material.wind_bake,
            )
        )
        or configuration.material.texture_clamp_mode < 0
        or configuration.capture.schema != RETAIL_GRASS_CAPTURE_SCHEMA
        or configuration.capture.event != RETAIL_GRASS_CAPTURE_EVENT
        or not configuration.capture.require_every_observed_mesh
    ):
        raise ValueError("OpenNV retail grass material identity is empty")
    return configuration


def _facegen_animation_configuration(
    source: dict[str, object],
) -> FaceGenAnimationConfiguration:
    provenance = _object(source, "provenance")
    for field in ("classification", "status", "source", "evidence"):
        if not str(provenance.get(field, "")).strip():
            raise ValueError(f"OpenNV FaceGen animation provenance {field} is empty")
    if source.get("schema") != FACEGEN_ANIMATION_SCHEMA:
        raise ValueError("OpenNV FaceGen animation schema is invalid")

    lip_source = _object(source, "lip")
    tri_source = _object(source, "tri")

    def names(parent: dict[str, object], field: str) -> tuple[str, ...]:
        value = parent.get(field)
        if (
            not isinstance(value, list)
            or not value
            or any(not isinstance(row, str) or not row.strip() for row in value)
        ):
            raise ValueError(f"OpenNV FaceGen {field} is empty or invalid")
        result = tuple(value)
        if len(set(result)) != len(result):
            raise ValueError(f"OpenNV FaceGen {field} contains duplicate names")
        return result

    def optional_names(parent: dict[str, object], field: str) -> tuple[str | None, ...]:
        value = parent.get(field)
        if not isinstance(value, list) or not value or any(
            row is not None and (not isinstance(row, str) or not row.strip())
            for row in value
        ):
            raise ValueError(f"OpenNV FaceGen {field} is empty or invalid")
        result = tuple(value)
        authored = tuple(row for row in result if row is not None)
        if not authored or len(set(authored)) != len(authored):
            raise ValueError(
                f"OpenNV FaceGen {field} has no authored names or contains duplicates"
            )
        return result

    zero_outside_authored_range = lip_source.get("zeroOutsideAuthoredRange")
    if not isinstance(zero_outside_authored_range, bool):
        raise ValueError("OpenNV FaceGen LIP range policy is invalid")

    lip = FaceGenLipConfiguration(
        byte_order=str(lip_source["byteOrder"]),
        version=int(lip_source["version"]),
        file_header_fields=names(lip_source, "fileHeaderFields"),
        decoded_header_fields=names(lip_source, "decodedHeaderFields"),
        integer_bytes=int(lip_source["integerBytes"]),
        value_bytes=int(lip_source["valueBytes"]),
        run_marker=int(lip_source["runMarker"]),
        run_length_bytes=int(lip_source["runLengthBytes"]),
        stored_size_bias_bytes=int(lip_source["storedSizeBiasBytes"]),
        implicit_trailing_zero_bytes=int(lip_source["implicitTrailingZeroBytes"]),
        compressed_flag=int(lip_source["compressedFlag"]),
        big_endian_flag=int(lip_source["bigEndianFlag"]),
        uncompressed_marker=int(lip_source["uncompressedMarker"]),
        sample_rate_hz=float(lip_source["sampleRateHz"]),
        interpolation=str(lip_source["interpolation"]),
        zero_outside_authored_range=zero_outside_authored_range,
        maximum_decoded_bytes=int(lip_source["maximumDecodedBytes"]),
        maximum_frames=int(lip_source["maximumFrames"]),
        maximum_absolute_weight=float(lip_source["maximumAbsoluteWeight"]),
        target_names=names(lip_source, "targetNames"),
        morph_target_names=optional_names(lip_source, "morphTargetNames"),
    )
    positive_lip_values = (
        lip.version,
        lip.integer_bytes,
        lip.value_bytes,
        lip.run_length_bytes,
        lip.stored_size_bias_bytes,
        lip.compressed_flag,
        lip.big_endian_flag,
        lip.uncompressed_marker,
        lip.sample_rate_hz,
        lip.maximum_decoded_bytes,
        lip.maximum_frames,
        lip.maximum_absolute_weight,
    )
    if any(not math.isfinite(float(value)) or float(value) <= 0.0 for value in positive_lip_values):
        raise ValueError("OpenNV FaceGen LIP positive values are invalid")
    if (
        lip.byte_order != "little"
        or lip.interpolation != "linear"
        or not lip.zero_outside_authored_range
        or lip.implicit_trailing_zero_bytes < 0
        or not 0 <= lip.run_marker <= BYTE_CHANNEL_MAXIMUM
        or not 0 <= lip.uncompressed_marker <= BYTE_CHANNEL_MAXIMUM
        or lip.compressed_flag & lip.big_endian_flag
        or len(lip.morph_target_names) != len(lip.target_names)
    ):
        raise ValueError("OpenNV FaceGen LIP contract is unsupported")

    tri = FaceGenTriConfiguration(
        signature=str(tri_source["signature"]),
        byte_order=str(tri_source["byteOrder"]),
        header_fields=names(tri_source, "headerFields"),
        integer_bytes=int(tri_source["integerBytes"]),
        scalar_bytes=int(tri_source["scalarBytes"]),
        delta_component_bytes=int(tri_source["deltaComponentBytes"]),
        reserved_bytes=int(tri_source["reservedBytes"]),
        labelled_vertex_prefix_bytes=int(tri_source["labelledVertexPrefixBytes"]),
        labelled_surface_prefix_bytes=int(tri_source["labelledSurfacePrefixBytes"]),
        uv_extension_flag=int(tri_source["uvExtensionFlag"]),
        position_components=int(tri_source["positionComponents"]),
        uv_components=int(tri_source["uvComponents"]),
        triangle_indices=int(tri_source["triangleIndices"]),
        quad_indices=int(tri_source["quadIndices"]),
        export_morph_kinds=names(tri_source, "exportMorphKinds"),
        target_name_collision_policy=str(tri_source["targetNameCollisionPolicy"]),
        normal_target_policy=str(tri_source["normalTargetPolicy"]),
    )
    positive_tri_values = (
        tri.integer_bytes,
        tri.scalar_bytes,
        tri.delta_component_bytes,
        tri.uv_extension_flag,
        tri.position_components,
        tri.uv_components,
        tri.triangle_indices,
        tri.quad_indices,
    )
    if any(value <= 0 for value in positive_tri_values) or any(
        value < 0
        for value in (
            tri.reserved_bytes,
            tri.labelled_vertex_prefix_bytes,
            tri.labelled_surface_prefix_bytes,
        )
    ):
        raise ValueError("OpenNV FaceGen TRI sizes are invalid")
    if (
        not tri.signature
        or tri.byte_order != "little"
        or set(tri.export_morph_kinds) != {"differential", "static"}
        or tri.target_name_collision_policy != "reject"
        or tri.normal_target_policy != "recompute-from-authored-topology"
    ):
        raise ValueError("OpenNV FaceGen TRI contract is unsupported")
    return FaceGenAnimationConfiguration(str(source["schema"]), lip, tri)


def _speed_tree_configuration(
    source: dict[str, object],
) -> SpeedTreeCompilerConfiguration:
    provenance = _object(source, "provenance")
    for field in ("classification", "status", "source", "evidence"):
        if not str(provenance.get(field, "")).strip():
            raise ValueError(f"OpenNV SpeedTree provenance {field} is empty")
    configuration = SpeedTreeCompilerConfiguration(
        billboard_texture=str(source["billboardTexture"]),
        billboard_alpha_cutoff=float(source["billboardAlphaCutoff"]),
    )
    if not configuration.billboard_texture.strip():
        raise ValueError("OpenNV SpeedTree billboard texture is empty")
    if (
        not math.isfinite(configuration.billboard_alpha_cutoff)
        or not 0.0 < configuration.billboard_alpha_cutoff <= 1.0
    ):
        raise ValueError("OpenNV SpeedTree billboard alpha cutoff is invalid")
    return configuration


def _integer_object(parent: dict[str, object], name: str) -> dict[str, int]:
    source = _object(parent, name)
    if not source or any(not isinstance(value, int) for value in source.values()):
        raise ValueError(f"OpenNV integer object is invalid: {name}")
    return {str(key): int(value) for key, value in source.items()}


def _canonical_u32(value: object, label: str) -> int:
    text = str(value)
    if re.fullmatch(r"0x[0-9a-fA-F]{8}", text) is None:
        raise ValueError(f"OpenNV {label} hash is not canonical")
    return int(text[2:], FORM_ID_RADIX)


def configured_recipe_path(key: str) -> Path:
    configuration = load_runtime_configuration()
    recipes = _object(_object(configuration.document, "tooling"), "recipeFiles")
    file_name = recipes.get(key)
    if not isinstance(file_name, str) or Path(file_name).name != file_name:
        raise ValueError(f"OpenNV tooling recipe key is missing or invalid: {key}")
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes" / file_name


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
