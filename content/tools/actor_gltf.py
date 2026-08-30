"""Export a directly resolved Fallout humanoid assembly to a skinned glTF."""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import struct
import time
from dataclasses import dataclass
from pathlib import Path
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
ACTOR_GLTF_DIAGNOSTIC_CONTRACT_FLOAT_1POINT0ENEGATIVE12 = 1.0e-12
ACTOR_GLTF_DIAGNOSTIC_CONTRACT_FLOAT_1POINT0ENEGATIVE5 = 1.0e-5
ACTOR_GLTF_DIAGNOSTIC_CONTRACT_INTEGER_8 = 8
FURNITURE_MARKER_ORIENTATION_UNITS_PER_RADIAN = 1000.0
ACCUMULATION_ROOT_ZERO_TRANSLATION = (0.0, 0.0, 0.0)


if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from PIL import Image
from pyffi.formats.nif import NifFormat  # type: ignore  # noqa: E402

from bsa_archive import BsaArchive, canonical_member_path
from export_static_nif_gltf import generate_tangents
from gltf_io import (
    GL_ARRAY_BUFFER,
    GL_ELEMENT_ARRAY_BUFFER,
    GL_FLOAT,
    GL_LINEAR,
    GL_LINEAR_MIPMAP_LINEAR,
    GL_REPEAT,
    GL_UNSIGNED_INT,
    GL_UNSIGNED_SHORT,
    GL_UNSIGNED_SHORT_MAX,
    BufferBuilder,
    pack_floats,
)
from facegen import apply_geometry_morphs, facegen_geometry_control_deltas
from facegen_animation import FaceGenTri, decode_tri
from nif_decoder import decode_nif
from texture_pipeline import decode_dds
from actor_material import (
    actor_texture_paths,
    actor_vertex_colors_enabled,
    build_actor_material as _material,
)
from runtime_configuration import ContentCompilerConfiguration


ACTOR_GLTF_SCHEMA = "opennv-actor-gltf/v4"
RUNTIME_SURFACE_NODE_PREFIX = "ActorSurface_"
RIGID_ATTACHMENT_NIF_ROOT = "nif-root-skeleton-node"
RIGID_ATTACHMENT_NIF_PARENT = "nif-prn-skeleton-node"
RIGID_ATTACHMENT_CONFIGURED_NODE = "configured-unparented-skeleton-node"
PRN_ROOT_MARKER_DISPOSITION = "omit-authored-prn-root-marker"
RETAIL_HIDDEN_CREATURE_SURFACE_DISPOSITION = (
    "omit-retail-hidden-creature-surface-at-observed-frame"
)
FACEGEN_RIGID_COMPONENT_ROLES = frozenset(
    {
        "eye-left",
        "eye-right",
        "hair",
        "head-part",
        "mouth",
        "teeth-lower",
        "teeth-upper",
        "tongue",
    }
)
RETAIL_APPEARANCE_ROLE_BY_COMPONENT_ROLE = {
    "eye-left": "eyes",
    "eye-right": "eyes",
    "mouth": "headPart",
    "teeth-lower": "headPart",
    "teeth-upper": "headPart",
    "tongue": "headPart",
    "head-part": "headPart",
}
RUNTIME_GEOMETRY_NONIDENTITY_TOKENS = frozenset(
    {"face", "gen", "human", "female", "male"}
)
NIF_PARENT_EXTRA_DATA_NAME = "prn"
NIF_LINEAR_INTERPOLATION = 1
NIF_QUADRATIC_INTERPOLATION = 2
NIF_TBC_INTERPOLATION = 3
NIF_XYZ_ROTATION_INTERPOLATION = 4
NIF_INVALID_TRANSFORM_COMPONENT_MAGNITUDE = 3.0e38
SLERP_LINEAR_DOT_THRESHOLD = 0.9995
NORMALIZATION_EPSILON = 1.0e-12
GLTF_PRIMARY_SKIN_INFLUENCES = 4
SKIN_WEIGHT_SUM_TOLERANCE = 1.0e-5
SKIN_WEIGHT_DUPLICATE_TOLERANCE = 1.0e-7
UNIFORM_CUBIC_BASIS_DIVISOR = 6.0
QUATERNION_DIAGONAL_COEFFICIENT = 0.25
FACEGEN_RIGID_SOURCE_SHAPE_BASIS = (
    "owned-skinned-head-exact-biped-head-inverse-bind"
)
FACEGEN_RIGID_NORMAL_ACTOR_BASIS = (
    "owned-normal-actor-facegen-prn-via-exact-head-skin-inverse-bind"
)


@dataclass(frozen=True)
class ActorComponent:
    role: str
    model_path: str
    model_payload: bytes
    egm_path: str | None = None
    egm_payload: bytes | None = None
    tri_path: str | None = None
    tri_payload: bytes | None = None
    bake_shape_transform: bool = False
    selected_shape: str | None = None
    included_shape_names: tuple[str, ...] = ()
    excluded_shape_prefixes: tuple[str, ...] = ()
    diffuse_override: str | None = None
    normal_override: str | None = None
    generated_diffuse: Image.Image | None = None
    generated_diffuse_by_source: tuple[tuple[str, Image.Image], ...] = ()
    facegen_detail_path: str | None = None
    generated_facegen_detail: Image.Image | None = None
    tint_rgb: tuple[float, float, float] | None = None
    diffuse_aliases: tuple[tuple[str, str], ...] = ()
    egm_vertex_offset: int = 0
    egm_symmetric_control_names: tuple[str, ...] = ()
    egm_symmetric_control_axes: tuple[tuple[float, ...], ...] = ()
    source_form_id: str | None = None
    source_slot: int | None = None
    runtime_shape_name: str | None = None


@dataclass(frozen=True)
class ActorAnimation:
    logical_path: str
    payload: bytes
    retain_accumulation_root_translation: bool = False


@dataclass(frozen=True)
class SampledTransformAnimation:
    sequence_name: str
    target_node: str
    start_seconds: float
    stop_seconds: float
    cycle_type: int
    samples_per_second: float
    parent_chain: tuple[dict[str, object], ...]
    sample_times: tuple[float, ...]
    translations: tuple[tuple[float, float, float], ...]
    rotations: tuple[tuple[float, float, float, float], ...]
    animated_parent_tracks: tuple[dict[str, object], ...] = ()

    def manifest(self) -> dict[str, object]:
        result = {
            "sequenceName": self.sequence_name,
            "targetNode": self.target_node,
            "startSeconds": self.start_seconds,
            "stopSeconds": self.stop_seconds,
            "cycleType": self.cycle_type,
            "samplesPerSecond": self.samples_per_second,
            "parentChain": list(self.parent_chain),
            "samples": [
                {
                    "timeSeconds": time_value,
                    "translationGodotGameUnits": list(translation),
                    "rotationQuaternionXyzw": list(rotation),
                }
                for time_value, translation, rotation in zip(
                    self.sample_times,
                    self.translations,
                    self.rotations,
                    strict=True,
                )
            ],
        }
        if self.animated_parent_tracks:
            result["animatedParentTracks"] = list(self.animated_parent_tracks)
        return result


@dataclass(frozen=True)
class SampledRootMotion:
    sequence_name: str
    target_node: str
    start_seconds: float
    stop_seconds: float
    cycle_type: int
    displacement_godot_game_units: tuple[float, float, float]

    @property
    def speed_game_units_per_second(self) -> float:
        duration = self.stop_seconds - self.start_seconds
        if duration <= 0.0:
            raise ValueError("Root-motion duration must be positive")
        return math.sqrt(
            sum(value * value for value in self.displacement_godot_game_units)
        ) / duration

    def manifest(self) -> dict[str, object]:
        return {
            "sequenceName": self.sequence_name,
            "targetNode": self.target_node,
            "startSeconds": self.start_seconds,
            "stopSeconds": self.stop_seconds,
            "cycleType": self.cycle_type,
            "displacementGodotGameUnits": list(
                self.displacement_godot_game_units
            ),
            "speedGameUnitsPerSecond": self.speed_game_units_per_second,
        }


def animation_sequence_manifest(payload: bytes) -> dict[str, object]:
    """Return the authored playback identity of one owned KF sequence."""

    document = _read_nif(payload)
    if len(document.roots) != 1 or not isinstance(
        document.roots[0], NifFormat.NiControllerSequence
    ):
        raise ValueError("Actor animation is not one NiControllerSequence")
    sequence = document.roots[0]
    start = float(sequence.start_time)
    stop = float(sequence.stop_time)
    if start != 0.0 or stop <= start:
        raise ValueError(
            f"Actor animation has an unexpected time range: {start}..{stop}"
        )
    transform_priorities: dict[str, int] = {}
    for controlled in sequence.controlled_blocks:
        if _text(controlled.get_controller_type()) != "NiTransformController":
            continue
        node_name = _text(controlled.get_node_name())
        priority = int(controlled.priority)
        if node_name in transform_priorities and transform_priorities[node_name] != priority:
            raise ValueError(
                f"Actor animation has conflicting priorities for {node_name!r}"
            )
        transform_priorities[node_name] = priority
    if not transform_priorities:
        raise ValueError("Actor animation has no transform-controller priorities")
    return {
        "sequenceName": _text(sequence.name),
        "startSeconds": start,
        "stopSeconds": stop,
        "cycleType": int(sequence.cycle_type),
        "controlledBlocks": len(sequence.controlled_blocks),
        "transformPrioritiesByNode": dict(sorted(transform_priorities.items())),
    }


def furniture_marker_manifest(payload: bytes, marker_id: int) -> dict[str, object]:
    """Decode one exact owned BSFurnitureMarker entry without placement policy."""

    document = _read_nif(payload)
    markers = [
        value
        for value in document.get_global_iterator()
        if isinstance(value, NifFormat.BSFurnitureMarker)
    ]
    if len(markers) != 1:
        raise ValueError(
            f"Furniture NIF must contain one BSFurnitureMarker, found {len(markers)}"
        )
    marker = markers[0]
    matches = [
        (index, position)
        for index, position in enumerate(marker.positions)
        if int(position.position_ref_1) == marker_id
        and int(position.position_ref_2) == marker_id
    ]
    if len(matches) != 1:
        raise ValueError(
            f"Furniture NIF marker {marker_id} is absent or ambiguous: {len(matches)}"
        )
    index, position = matches[0]
    offset_nif = _nif_vector(position.offset)
    offset_godot = _convert_vector(offset_nif)
    if not all(math.isfinite(value) for value in (*offset_nif, *offset_godot)):
        raise ValueError(f"Furniture NIF marker {marker_id} has a non-finite offset")
    return {
        "extraDataName": _text(marker.name),
        "index": index,
        "positionRef1": int(position.position_ref_1),
        "positionRef2": int(position.position_ref_2),
        "offsetNifGameUnits": list(offset_nif),
        "offsetGodotGameUnits": list(offset_godot),
        "orientation": int(position.orientation),
        "orientationRadians": (
            int(position.orientation) /
            FURNITURE_MARKER_ORIENTATION_UNITS_PER_RADIAN
        ),
        "heading": float(position.heading),
        "animationType": int(position.animation_type),
    }


@dataclass(frozen=True)
class RetailRenderPart:
    role: str
    source_form_id: str
    source_slot: int
    required: bool
    attached: bool
    drawable: bool
    visible: bool
    skinned: bool
    geometry_name: str
    visual_node_path: str
    texture_paths: tuple[str, ...]


def retail_render_parts_from_snapshot(
    snapshot: dict[str, object],
) -> tuple[RetailRenderPart, ...]:
    raw_parts = snapshot.get("renderParts")
    if not isinstance(raw_parts, list) or not raw_parts:
        raise ValueError("Retail appearance snapshot has no render parts")
    parts: list[RetailRenderPart] = []
    for raw_part in raw_parts:
        if not isinstance(raw_part, dict):
            raise ValueError("Retail appearance render part is not an object")
        raw_bindings = raw_part.get("textureBindings")
        if not isinstance(raw_bindings, list):
            raise ValueError("Retail appearance render part has no texture bindings")
        parts.append(
            RetailRenderPart(
                role=str(raw_part["role"]),
                source_form_id=str(raw_part["sourceFormId"]),
                source_slot=int(raw_part["sourceSlot"]),
                required=bool(raw_part["required"]),
                attached=bool(raw_part["attached"]),
                drawable=bool(raw_part["drawable"]),
                visible=bool(raw_part["visible"]),
                skinned=bool(raw_part["skinned"]),
                geometry_name=str(raw_part["geometryName"]),
                visual_node_path=str(raw_part["visualNodePath"]),
                texture_paths=tuple(
                    sorted(
                        {
                            canonical_member_path(str(binding["path"]))
                            for binding in raw_bindings
                            if isinstance(binding, dict) and str(binding.get("path", ""))
                        }
                    )
                ),
            )
        )
    return tuple(parts)


def _visible_creature_geometry_names(
    component: ActorComponent,
    retail_render_parts: tuple[RetailRenderPart, ...],
) -> frozenset[str]:
    """Resolve visible shapes by exact owned source identity and runtime name.

    Creature add-on NIFs (screens, voice boxes, exposed components, and similar
    parts) can be reported under a semantic role other than ``actor``.  The
    CREA model slot plus geometry identity is the authoritative join.
    """

    return frozenset(
        part.geometry_name
        for part in retail_render_parts
        if part.source_form_id == component.source_form_id
        and part.source_slot == component.source_slot
        and part.required
        and part.attached
        and part.drawable
        and part.visible
    )


@dataclass(frozen=True)
class ActorGltfInput:
    actor_form_id: str
    actor_name: str
    skeleton_path: str
    skeleton_payload: bytes
    symmetric_geometry: tuple[float, ...]
    asymmetric_geometry: tuple[float, ...]
    components: tuple[ActorComponent, ...]
    idle_animation_path: str
    idle_animation_payload: bytes
    skeleton_root_node: str
    rigid_attachment_node: str
    biped_head_node: str
    include_dismember_cap_shapes: bool = False
    additional_animations: tuple[ActorAnimation, ...] = ()
    retail_render_parts: tuple[RetailRenderPart, ...] = ()
    head_only_facegen_preview: bool = False


class TextureLibrary:
    def __init__(
        self,
        archives: list[BsaArchive],
        output_root: Path,
        gltf_path: Path,
        compiler: ContentCompilerConfiguration,
    ):
        self.archives = archives
        self.output_root = output_root
        self.gltf_path = gltf_path
        self.compiler = compiler
        self.images: list[dict[str, object]] = []
        self.textures: list[dict[str, object]] = []
        self.rows: list[dict[str, object]] = []
        self._indices: dict[tuple[str, bool], int] = {}

    def source(self, logical_path: str, *, normal: bool = False) -> int:
        requested = canonical_member_path(logical_path)
        key = (requested, normal)
        if key in self._indices:
            return self._indices[key]
        matches = [archive for archive in self.archives if requested in archive.members]
        if len(matches) != 1:
            raise FileNotFoundError(f"Expected one actor texture {requested!r}, found {len(matches)}")
        member = matches[0].extract(requested)
        image = decode_dds(member.data, normal)
        return self._store(
            requested,
            image,
            source_sha256=member.sha256,
            source_archive=member.source_archive,
            source_archive_sha256=member.source_archive_sha256,
            normal_green_inverted=normal,
            key=key,
        )

    def generated(self, identity: str, image: Image.Image, source_sha256: str) -> int:
        key = (identity, False)
        if key in self._indices:
            return self._indices[key]
        return self._store(
            identity,
            image,
            source_sha256=source_sha256,
            source_archive=None,
            source_archive_sha256=None,
            normal_green_inverted=False,
            key=key,
        )

    def _store(
        self,
        identity: str,
        image: Image.Image,
        *,
        source_sha256: str,
        source_archive: str | None,
        source_archive_sha256: str | None,
        normal_green_inverted: bool,
        key: tuple[str, bool],
    ) -> int:
        asset_id = hashlib.sha256(f"{identity}:{normal_green_inverted}".encode()).hexdigest()[
            :self.compiler.asset_id_hex_characters
        ]
        path = self.output_root / "textures" / f"{asset_id}.png"
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_name(path.name + ".tmp")
        image.convert("RGBA").save(
            temporary,
            format="PNG",
            optimize=True,
            compress_level=self.compiler.png_compression_level,
        )
        os.replace(temporary, path)
        payload_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        uri = os.path.relpath(path, self.gltf_path.parent).replace("\\", "/")
        image_index = len(self.images)
        texture_index = len(self.textures)
        self.images.append({"name": identity, "uri": uri})
        self.textures.append({"source": image_index, "sampler": 0})
        self.rows.append(
            {
                "identity": identity,
                "sourceSha256": source_sha256,
                "sourceArchive": source_archive,
                "sourceArchiveSha256": source_archive_sha256,
                "png": uri,
                "pngSha256": payload_hash,
                "width": image.width,
                "height": image.height,
                "normalGreenInverted": normal_green_inverted,
            }
        )
        self._indices[key] = texture_index
        return texture_index


def export_actor_gltf(
    source: ActorGltfInput,
    texture_archives: list[BsaArchive],
    gltf_path: Path,
    sidecar_path: Path,
    compiler: ContentCompilerConfiguration,
) -> dict[str, object]:
    gltf_path.parent.mkdir(parents=True, exist_ok=True)
    skeleton_decode = decode_nif(source.skeleton_payload)
    skeleton = skeleton_decode.document
    skeleton_root = _named_node(skeleton, source.skeleton_root_node)
    nodes: list[dict[str, object]] = [{"name": f"ACTOR_{source.actor_form_id}_{source.actor_name}", "children": []}]
    node_by_name: dict[str, int] = {}
    _append_skeleton_nodes(skeleton_root, 0, nodes, node_by_name)
    accumulation_root_source_translation = _accumulation_root_source_translation(
        nodes,
        node_by_name,
        source.skeleton_root_node,
    )
    nonaccumulation_root_nodes = [
        name
        for name in node_by_name
        if name.casefold().endswith(" nonaccum")
    ]
    if len(nonaccumulation_root_nodes) > 1:
        raise ValueError(
            "Actor skeleton has multiple non-accumulation roots: "
            f"{sorted(nonaccumulation_root_nodes)}"
        )
    nonaccumulation_root_node = (
        nonaccumulation_root_nodes[0]
        if nonaccumulation_root_nodes
        else None
    )
    if source.rigid_attachment_node not in node_by_name:
        raise ValueError(
            "Actor skeleton has no configured rigid-attachment node: "
            f"{source.rigid_attachment_node}"
        )

    builder = BufferBuilder()
    meshes: list[dict[str, object]] = []
    skins: list[dict[str, object]] = []
    materials: list[dict[str, object]] = []
    surfaces: list[dict[str, object]] = []
    facegen_head_skin_inverse_bind: list[list[float]] | None = None
    omitted_surfaces: list[dict[str, object]] = []
    nif_decodes: list[dict[str, object]] = [
        {
            "role": "skeleton",
            "logicalPath": source.skeleton_path,
            "sha256": hashlib.sha256(source.skeleton_payload).hexdigest(),
            "decoder": skeleton_decode.evidence(),
        }
    ]
    textures = TextureLibrary(texture_archives, gltf_path.parent, gltf_path, compiler)

    for component in source.components:
        payload = component.model_payload
        component_decode = decode_nif(payload)
        document = component_decode.document
        nif_decodes.append(
            {
                "role": component.role,
                "logicalPath": component.model_path,
                "sha256": hashlib.sha256(component.model_payload).hexdigest(),
                "decoder": component_decode.evidence(),
            }
        )
        component_root = document.roots[0]
        unsupported_geometry = _unsupported_actor_geometry(document)
        if unsupported_geometry:
            rendered = ", ".join(
                f"{geometry_type}:{name!r}"
                for geometry_type, name in unsupported_geometry
            )
            raise ValueError(
                f"Actor component {component.role} contains unsupported render geometry "
                f"[{rendered}] in {component.model_path}; exact translation is required"
            )
        rigid_attachment_node, rigid_attachment_source = _rigid_attachment(
            document,
            component_root,
            node_by_name,
            source.rigid_attachment_node,
        )
        authored_shapes = [
            shape
            for shape in document.get_global_iterator()
            if isinstance(shape, (NifFormat.NiTriShape, NifFormat.NiTriStrips)) and shape.data is not None
        ]
        if component.selected_shape is not None:
            nonselected_shapes = [
                shape
                for shape in authored_shapes
                if _text(shape.name) != component.selected_shape
            ]
            omitted_surfaces.extend(
                _omitted_shape_row(
                    component,
                    shape,
                    "omit-nonselected-authored-shape",
                    "source-owned actor appearance selected shape",
                )
                for shape in nonselected_shapes
            )
            authored_shapes = [
                shape
                for shape in authored_shapes
                if _text(shape.name) == component.selected_shape
            ]
        if component.included_shape_names:
            included = frozenset(component.included_shape_names)
            absent = included.difference(_text(shape.name) for shape in authored_shapes)
            if absent:
                raise ValueError(
                    f"Actor component {component.role} exact live surface selection "
                    f"names absent shapes {sorted(absent)} from {component.model_path}"
                )
            excluded_by_live_selection = [
                shape for shape in authored_shapes if _text(shape.name) not in included
            ]
            for shape in excluded_by_live_selection:
                omitted_surfaces.append(
                    _omitted_shape_row(
                        component,
                        shape,
                        "omit-exact-live-retail-surface-absent",
                        "hash-bound exact live retail actor geometry observation",
                    )
                )
            authored_shapes = [
                shape for shape in authored_shapes if _text(shape.name) in included
            ]
        if not source.include_dismember_cap_shapes:
            dismember_cap_shapes = [
                shape for shape in authored_shapes if _is_dismember_cap_shape(shape)
            ]
            omitted_surfaces.extend(
                _omitted_shape_row(
                    component,
                    shape,
                    "omit-bsdismember-cap-shape",
                    "owned BSDismemberBodyPartType semantics",
                )
                for shape in dismember_cap_shapes
            )
            dismember_ids = {id(shape) for shape in dismember_cap_shapes}
            authored_shapes = [
                shape for shape in authored_shapes if id(shape) not in dismember_ids
            ]
        marker_shapes = [
            shape
            for shape in authored_shapes
            if _is_authored_prn_root_marker(
                component_root,
                shape,
                rigid_attachment_source,
                authored_shapes,
            )
        ]
        for shape in marker_shapes:
            omitted_surfaces.append(
                {
                    "role": component.role,
                    "modelPath": component.model_path,
                    "modelSha256": hashlib.sha256(component.model_payload).hexdigest(),
                    "shape": _text(shape.name),
                    "attachmentNode": rigid_attachment_node,
                    "attachmentSource": rigid_attachment_source,
                    "disposition": PRN_ROOT_MARKER_DISPOSITION,
                    "authority": (
                        "owned NIF Prn attachment, root-child topology, zero UV sets, "
                        "and material-only geometry"
                    ),
                }
            )
        marker_ids = {id(shape) for shape in marker_shapes}
        shapes = [shape for shape in authored_shapes if id(shape) not in marker_ids]
        excluded_by_prefix = [
            shape
            for shape in shapes
            if any(
                _text(shape.name).startswith(prefix)
                for prefix in component.excluded_shape_prefixes
            )
        ]
        for shape in excluded_by_prefix:
            omitted_surfaces.append(
                {
                    "role": component.role,
                    "modelPath": component.model_path,
                    "modelSha256": hashlib.sha256(component.model_payload).hexdigest(),
                    "shape": _text(shape.name),
                    "disposition": "omit-configured-shape-prefix",
                    "authority": "actor recipe excludedShapePrefixes",
                }
            )
        excluded_by_prefix_ids = {id(shape) for shape in excluded_by_prefix}
        shapes = [shape for shape in shapes if id(shape) not in excluded_by_prefix_ids]
        if source.retail_render_parts and component.role.startswith("creature-model-"):
            visible_runtime_names = _visible_creature_geometry_names(
                component,
                source.retail_render_parts,
            )
            hidden_shapes = [
                shape
                for shape in shapes
                if _text(shape.name) not in visible_runtime_names
            ]
            for shape in hidden_shapes:
                omitted_surfaces.append(
                    {
                        "role": component.role,
                        "sourceFormId": component.source_form_id,
                        "sourceSlot": component.source_slot,
                        "modelPath": component.model_path,
                        "modelSha256": hashlib.sha256(component.model_payload).hexdigest(),
                        "shape": _text(shape.name),
                        "disposition": RETAIL_HIDDEN_CREATURE_SURFACE_DISPOSITION,
                        "authority": (
                            "hash-bound retail actor appearance render-part visibility"
                        ),
                    }
                )
            hidden_ids = {id(shape) for shape in hidden_shapes}
            shapes = [shape for shape in shapes if id(shape) not in hidden_ids]
        if not shapes:
            if source.retail_render_parts and component.role.startswith("creature-model-"):
                continue
            raise ValueError(f"Actor component {component.role} selected no shapes from {component.model_path}")
        if component.runtime_shape_name is not None and len(shapes) != 1:
            raise ValueError(
                f"Actor component {component.role} cannot apply one retail runtime shape name "
                f"to {len(shapes)} source shapes"
            )
        for shape in shapes:
            mesh_index, skin_index, surface = _append_shape(
                source,
                component,
                component_root,
                shape,
                node_by_name,
                builder,
                meshes,
                skins,
                materials,
                textures,
                compiler,
            )
            if skin_index is not None:
                parent = 0
                if component.role == "head":
                    if facegen_head_skin_inverse_bind is not None:
                        raise ValueError(
                            "Actor FaceGen assembly has multiple skinned head surfaces"
                        )
                    facegen_head_skin_inverse_bind = _source_skin_bone_inverse_bind(
                        shape,
                        source.biped_head_node,
                        shape.get_transform(component_root)
                        if component.bake_shape_transform
                        else None,
                    )
            else:
                parent = node_by_name[rigid_attachment_node]
                surface["attachmentNode"] = rigid_attachment_node
                surface["attachmentSource"] = rigid_attachment_source
                if _uses_retail_biped_head_basis(
                    component.role,
                    rigid_attachment_node,
                    rigid_attachment_source,
                    source.biped_head_node,
                ):
                    if _is_facegen_rigid_component(component.role):
                        if facegen_head_skin_inverse_bind is None:
                            raise ValueError(
                                "Actor FaceGen rigid part precedes its unique skinned head surface"
                            )
                        attachment_matrix = _facegen_rigid_head_attachment_matrix(
                            facegen_head_skin_inverse_bind,
                            surface["sourceShapeTranslationGameUnits"],
                        )
                        surface["attachmentBasisSource"] = (
                            FACEGEN_RIGID_SOURCE_SHAPE_BASIS
                            if source.head_only_facegen_preview
                            else FACEGEN_RIGID_NORMAL_ACTOR_BASIS
                        )
                        surface["headSkinInverseBindMatrixGodot"] = list(
                            _gltf_matrix(facegen_head_skin_inverse_bind)
                        )
                        surface["sourceShapeTranslationDisposition"] = (
                            "owned-source-translation-composed-with-exact-head-"
                            "skin-inverse-bind"
                        )
                    else:
                        attachment_matrix = _facegen_rigid_attachment_matrix(
                            surface["sourceShapeTranslationGameUnits"]
                        )
                        surface["attachmentBasisSource"] = (
                            "retail-biped-prn-local-quarter-turn"
                            if rigid_attachment_source == RIGID_ATTACHMENT_NIF_PARENT
                            else "retail-facegen-biped-local-quarter-turn"
                        )
                    surface["attachmentLocalMatrixGodot"] = attachment_matrix
                else:
                    attachment_matrix = None
            _append_runtime_surface_node(
                nodes,
                parent,
                mesh_index,
                skin_index,
                surface,
                len(surfaces),
                attachment_matrix if skin_index is None else None,
            )
            surfaces.append(surface)

    animation_sources = (
        ActorAnimation(source.idle_animation_path, source.idle_animation_payload),
        *source.additional_animations,
    )
    logical_paths = [row.logical_path.casefold() for row in animation_sources]
    if len(set(logical_paths)) != len(logical_paths):
        raise ValueError("Actor animation paths must be unique")
    animations = []
    animation_rows = []
    animation_channels = 0
    nonaccum_origin = None
    use_path_names = len(animation_sources) > 1
    for animation_source in animation_sources:
        sequence_manifest = animation_sequence_manifest(animation_source.payload)
        try:
            animation, channels, animation_origin = _build_animation(
                animation_source.payload,
                node_by_name,
                nodes,
                builder,
                compiler,
                source.skeleton_root_node,
                nonaccumulation_root_node,
                animation_source.logical_path if use_path_names else None,
                animation_source.retain_accumulation_root_translation,
            )
        except Exception as error:
            raise ValueError(
                f"Actor animation {animation_source.logical_path} failed: {error}"
            ) from error
        if animation is not None:
            animations.append(animation)
        animation_channels += channels
        if animation_source is animation_sources[0]:
            nonaccum_origin = animation_origin
        animation_rows.append(
            {
                "logicalPath": animation_source.logical_path,
                "sha256": hashlib.sha256(animation_source.payload).hexdigest(),
                "channels": channels,
                "sequenceName": sequence_manifest["sequenceName"],
                "startSeconds": sequence_manifest["startSeconds"],
                "stopSeconds": sequence_manifest["stopSeconds"],
                "cycleType": sequence_manifest["cycleType"],
                "transformPrioritiesByNode": sequence_manifest[
                    "transformPrioritiesByNode"
                ],
                "accumulationRootTranslationDisposition": (
                    "preserve-hash-bound-owned-clip-root-curve"
                    if animation_source.retain_accumulation_root_translation
                    else "owned-world-root-authoritative-zero-local-translation"
                ),
                "nonAccumOriginGodotUnits": (
                    list(animation_origin) if animation_origin else None
                ),
            }
        )

    binary_path = gltf_path.with_suffix(".bin")
    gltf: dict[str, object] = {
        "asset": {"version": "2.0", "generator": "OpenNV direct actor exporter v2"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": nodes,
        "meshes": meshes,
        "skins": skins,
        "materials": materials,
        "samplers": [{
            "magFilter": GL_LINEAR,
            "minFilter": GL_LINEAR_MIPMAP_LINEAR,
            "wrapS": GL_REPEAT,
            "wrapT": GL_REPEAT,
        }],
        "images": textures.images,
        "textures": textures.textures,
        "buffers": [{"uri": binary_path.name, "byteLength": len(builder.data)}],
        "bufferViews": builder.views,
        "accessors": builder.accessors,
        "extras": {"openNvSchema": ACTOR_GLTF_SCHEMA, "actorFormId": source.actor_form_id},
    }
    extensions_used = sorted(
        {
            extension
            for material in materials
            for extension in material.get("extensions", {})
        }
    )
    if extensions_used:
        gltf["extensionsUsed"] = extensions_used
    if animations:
        gltf["animations"] = animations
    binary_bytes = bytes(builder.data)
    gltf_bytes = (json.dumps(gltf, indent=2, sort_keys=True) + "\n").encode()
    _atomic_write(binary_path, binary_bytes)
    _atomic_write(gltf_path, gltf_bytes)
    sidecar = {
        "schema": ACTOR_GLTF_SCHEMA,
        "status": "skinned-animated",
        "actorFormId": source.actor_form_id,
        "actorName": source.actor_name,
        "skeleton": {
            "logicalPath": source.skeleton_path,
            "sha256": hashlib.sha256(source.skeleton_payload).hexdigest(),
            "nodes": len(node_by_name),
            "rootNode": source.skeleton_root_node,
            "rigidAttachmentNode": source.rigid_attachment_node,
            "bipedHeadNode": source.biped_head_node,
            "rigidAttachmentPolicy": (
                "attach a rigid component to its authored NIF Prn skeleton node; "
                "without Prn, require either a matching NIF root or the configured "
                "engine-contract node"
            ),
            "animationTranslationPolicy": {
                "accumulationRootNode": source.skeleton_root_node,
                "accumulationRootSourceTranslationGodotGameUnits": list(
                    accumulation_root_source_translation
                ),
                "accumulationRootTranslation": (
                    "per-animation-hash-bound-disposition"
                ),
                "nonAccumulationRootNode": nonaccumulation_root_node,
                "nonAccumulationRootTranslation": (
                    "preserve-authored-absolute-local-translation"
                    if nonaccumulation_root_node
                    else "not-present"
                ),
            },
        },
        "animation": {
            "logicalPath": source.idle_animation_path,
            "sha256": hashlib.sha256(source.idle_animation_payload).hexdigest(),
            "channels": animation_rows[0]["channels"],
            "nonAccumOriginGodotUnits": list(nonaccum_origin) if nonaccum_origin else None,
        },
        "animations": animation_rows,
        "faceGenAnimation": {
            "schema": compiler.facegen_animation.schema,
            "lipTargetNames": list(compiler.facegen_animation.lip.target_names),
            "morphTargetNames": list(
                compiler.facegen_animation.lip.morph_target_names
            ),
            "unboundLipTargets": [
                name
                for name, morph_name in zip(
                    compiler.facegen_animation.lip.target_names,
                    compiler.facegen_animation.lip.morph_target_names,
                    strict=True,
                )
                if morph_name is None
            ],
        },
        "outputs": {
            "gltf": {"file": gltf_path.name, "sha256": hashlib.sha256(gltf_bytes).hexdigest()},
            "buffer": {"file": binary_path.name, "sha256": hashlib.sha256(binary_bytes).hexdigest()},
        },
        "coverage": {
            "components": len(source.components),
            "surfaces": len(surfaces),
            "omittedSurfaces": len(omitted_surfaces),
            "dismemberCapShapesIncluded": source.include_dismember_cap_shapes,
            "skins": len(skins),
            "inverseBindContract": "source NIF skin bind with baked-shape compensation",
            "textures": len(textures.rows),
            "animations": len(animations),
            "animationChannels": animation_channels,
            "animated": bool(animations) and animation_channels > 0,
            "faceGenTriSurfaces": sum(
                surface["faceGenMorphs"]["source"] == "exact-owned-tri"
                for surface in surfaces
            ),
            "faceGenMorphTargets": sum(
                len(surface["faceGenMorphs"]["targetNames"])
                for surface in surfaces
            ),
        },
        "surfaces": surfaces,
        "omittedSurfaces": omitted_surfaces,
        "textures": textures.rows,
        "nifDecodes": nif_decodes,
    }
    sidecar_bytes = (json.dumps(sidecar, indent=2, sort_keys=True) + "\n").encode()
    _atomic_write(sidecar_path, sidecar_bytes)
    return sidecar


def _append_runtime_surface_node(
    nodes: list[dict[str, object]],
    parent_index: int,
    mesh_index: int,
    skin_index: int | None,
    surface: dict[str, object],
    surface_index: int,
    local_matrix: list[list[float]] | None = None,
) -> None:
    runtime_node_name = _runtime_surface_node_name(surface_index)
    node: dict[str, object] = {
        "name": runtime_node_name,
        "mesh": mesh_index,
    }
    if skin_index is not None:
        node["skin"] = skin_index
    if local_matrix is not None:
        node["matrix"] = list(_gltf_matrix(local_matrix))
    node_index = len(nodes)
    nodes.append(node)
    nodes[parent_index].setdefault("children", []).append(node_index)
    surface["node"] = node_index
    surface["runtimeNodeName"] = runtime_node_name


def _facegen_rigid_attachment_matrix(
    source_shape_translation_game_units: object,
) -> list[list[float]]:
    """Return retail's BSFaceGenNiNodeBiped-to-part local transform.

    Runtime FaceGen rigid parts share a quarter-turn beneath Bip01 Head. The
    source shape translation remains authored per NIF and is converted without
    folding the NIF shape basis into the vertices.
    """

    if (
        not isinstance(source_shape_translation_game_units, list)
        or len(source_shape_translation_game_units) != 3
        or any(not isinstance(value, (int, float)) for value in source_shape_translation_game_units)
    ):
        raise ValueError("FaceGen rigid attachment translation must contain three numbers")
    translation = _convert_vector(tuple(float(value) for value in source_shape_translation_game_units))
    return [
        [0.0, 1.0, 0.0, translation[0]],
        [-1.0, 0.0, 0.0, translation[1]],
        [0.0, 0.0, 1.0, translation[2]],
        [0.0, 0.0, 0.0, 1.0],
    ]


def _source_skin_bone_inverse_bind(
    shape: object,
    bone_name: str,
    baked_shape_transform: object | None,
) -> list[list[float]]:
    """Return one exact converted inverse bind from the source skinned mesh.

    FaceGen eyes, hair, teeth, and head parts are authored in the same model
    space as the skinned head. Parenting those rigid vertices under the head
    bone therefore requires the head surface's own Bip01 Head inverse bind.
    Deriving another quarter-turn from the skeleton rest pose rotates the
    already-converted left/right axis into the actor's vertical axis.
    """

    instance = getattr(shape, "skin_instance", None)
    data = getattr(instance, "data", None)
    if instance is None or data is None:
        raise ValueError("Actor FaceGen head has no source skin instance")
    bone_names = [_text(bone.name) for bone in instance.bones]
    matches = [index for index, name in enumerate(bone_names) if name == bone_name]
    if len(matches) != 1 or len(data.bone_list) != len(bone_names):
        raise ValueError(
            f"Actor FaceGen head has no unique source inverse bind for {bone_name}"
        )
    return _compensated_inverse_bind(
        data.bone_list[matches[0]].get_transform(),
        baked_shape_transform,
    )


def _facegen_rigid_head_attachment_matrix(
    head_skin_inverse_bind: list[list[float]],
    source_shape_translation_game_units: object,
) -> list[list[float]]:
    """Compose one owned rigid-part translation with the exact head bind.

    Rigid FaceGen vertices are already authored in the head model basis, so the
    NiTriShape rotation must not be baked a second time.  Some effective actor
    winners retain a small but material per-part translation, however.  Apply
    that owned translation before the skinned head's exact inverse bind instead
    of discarding it behind a tolerance.
    """

    if (
        len(head_skin_inverse_bind) != 4
        or any(len(row) != 4 for row in head_skin_inverse_bind)
        or any(not math.isfinite(float(entry)) for row in head_skin_inverse_bind for entry in row)
    ):
        raise ValueError("FaceGen rigid head inverse bind must be a finite 4x4 matrix")
    if (
        not isinstance(source_shape_translation_game_units, list)
        or len(source_shape_translation_game_units) != 3
        or any(
            not isinstance(entry, (int, float)) or not math.isfinite(float(entry))
            for entry in source_shape_translation_game_units
        )
    ):
        raise ValueError("FaceGen rigid source translation must contain three finite numbers")
    translation = _convert_vector(
        tuple(float(entry) for entry in source_shape_translation_game_units)
    )
    source_translation = [
        [1.0, 0.0, 0.0, translation[0]],
        [0.0, 1.0, 0.0, translation[1]],
        [0.0, 0.0, 1.0, translation[2]],
        [0.0, 0.0, 0.0, 1.0],
    ]
    return _multiply(head_skin_inverse_bind, source_translation)


def _uses_retail_biped_head_basis(
    component_role: str,
    attachment_node: str,
    attachment_source: str,
    biped_head_node: str,
) -> bool:
    """Identify rigid parts authored in the Gamebryo biped-head basis.

    FaceGen parts use that basis, but they are not its only consumers.  Head
    apparel can declare the configured biped-head node through owned ``Prn``
    data and therefore requires the same local quarter-turn.
    """

    return (
        not component_role.startswith("creature-model-")
        and attachment_node == biped_head_node
        and (
            component_role in FACEGEN_RIGID_COMPONENT_ROLES
            or attachment_source == RIGID_ATTACHMENT_NIF_PARENT
        )
    )


def _is_facegen_rigid_component(component_role: str) -> bool:
    return (
        component_role in FACEGEN_RIGID_COMPONENT_ROLES
        or component_role.startswith("head-part-")
    )


def _runtime_surface_node_name(surface_index: int) -> str:
    if surface_index < 0:
        raise ValueError("Actor surface index cannot be negative")
    return f"{RUNTIME_SURFACE_NODE_PREFIX}{surface_index}"


def _unsupported_actor_geometry(document: object) -> tuple[tuple[str, str], ...]:
    supported = (NifFormat.NiTriShape, NifFormat.NiTriStrips)
    return tuple(
        (type(block).__name__, _text(block.name))
        for block in document.get_global_iterator()
        if isinstance(block, NifFormat.NiGeometry) and not isinstance(block, supported)
    )


def _is_authored_prn_root_marker(
    component_root: object,
    shape: object,
    attachment_source: str,
    authored_shapes: list[object],
) -> bool:
    """Classify an owned NIF's non-rendering rigid attachment marker.

    Fallout component NIFs attached through ``Prn`` can retain a Max-authored
    root marker as direct child geometry.  The owned files distinguish that
    marker structurally: it is unskinned, has no UV set, carries only a legacy
    material property, and sits beside actual render geometry.  This avoids
    actor, model, or shape-name exceptions while retaining the omitted source
    identity in the sidecar.
    """

    if attachment_source != RIGID_ATTACHMENT_NIF_PARENT:
        return False
    if getattr(shape, "skin_instance", None) is not None:
        return False
    if not any(child is shape for child in getattr(component_root, "children", [])):
        return False
    data = getattr(shape, "data", None)
    if data is None or len(getattr(data, "uv_sets", [])) != 0:
        return False
    properties = tuple(
        prop for prop in getattr(shape, "properties", []) if prop is not None
    )
    if len(properties) != 1 or not isinstance(properties[0], NifFormat.NiMaterialProperty):
        return False
    return any(other is not shape for other in authored_shapes)


def actor_component_geometry_inventory(
    payload: bytes,
) -> dict[str, tuple[tuple[str, str], ...]]:
    """Classify render geometry before a gallery decides component scope."""

    document = _read_nif(payload)
    supported = tuple(
        (type(block).__name__, _text(block.name))
        for block in document.get_global_iterator()
        if isinstance(block, (NifFormat.NiTriShape, NifFormat.NiTriStrips))
        and block.data is not None
    )
    return {
        "supported": supported,
        "unsupported": _unsupported_actor_geometry(document),
    }


def actor_skin_diffuse_paths(payload: bytes) -> tuple[str, ...]:
    """Return diffuse members selected by authored NIF skin-tint shaders."""

    document = _read_nif(payload)
    paths = set()
    for shape in document.get_global_iterator():
        if not isinstance(shape, (NifFormat.NiTriShape, NifFormat.NiTriStrips)):
            continue
        if shape.data is None or getattr(shape, "skin_instance", None) is None:
            continue
        properties = tuple(
            prop for prop in getattr(shape, "properties", ()) if prop is not None
        )
        shader = next(
            (
                prop
                for prop in properties
                if isinstance(prop, NifFormat.BSShaderPPLightingProperty)
            ),
            None,
        )
        if shader is None or shader.shader_type != NifFormat.BSShaderType.SHADERSKIN:
            continue
        texture_paths = actor_texture_paths(list(properties))
        if not texture_paths or not texture_paths[0]:
            raise ValueError(
                f"Actor skin-tint shape has no authored diffuse: {_text(shape.name)}"
            )
        paths.add(texture_paths[0])
    return tuple(sorted(paths))


def _append_skeleton_nodes(
    node: object,
    parent_index: int,
    nodes: list[dict[str, object]],
    node_by_name: dict[str, int],
) -> None:
    name = _text(node.name)
    if name in node_by_name:
        raise ValueError(f"Actor skeleton contains duplicate node name: {name}")
    node_index = len(nodes)
    node_by_name[name] = node_index
    translation, rotation, scale = _node_trs(node)
    row: dict[str, object] = {
        "name": name,
        "translation": translation,
        "rotation": rotation,
        "scale": scale,
        "children": [],
    }
    nodes.append(row)
    nodes[parent_index].setdefault("children", []).append(node_index)
    for child in getattr(node, "children", []):
        if isinstance(child, NifFormat.NiNode):
            _append_skeleton_nodes(child, node_index, nodes, node_by_name)


def _accumulation_root_source_translation(
    nodes: list[dict[str, object]],
    node_by_name: dict[str, int],
    accumulation_root_node: str,
) -> tuple[float, float, float]:
    if accumulation_root_node not in node_by_name:
        raise ValueError(
            "Actor skeleton has no configured accumulation-root node: "
            f"{accumulation_root_node}"
        )
    row = nodes[node_by_name[accumulation_root_node]]
    source = row.get("translation")
    if (
        not isinstance(source, list)
        or len(source) != len(ACCUMULATION_ROOT_ZERO_TRANSLATION)
        or not all(isinstance(value, float) and math.isfinite(value) for value in source)
    ):
        raise ValueError(
            "Actor accumulation-root source translation is invalid: "
            f"{accumulation_root_node}"
        )
    return tuple(source)


def _world_authoritative_accumulation_root_translations(
    authored: bool,
    retain_owned_curve: bool,
    sample_count: int,
) -> list[tuple[float, float, float]]:
    if authored:
        return []
    if retain_owned_curve:
        raise ValueError(
            "Hash-bound retained accumulation-root clip has no authored root curve"
        )
    return [ACCUMULATION_ROOT_ZERO_TRANSLATION for _ in range(sample_count)]


def _rigid_attachment(
    document: object,
    component_root: object,
    node_by_name: dict[str, int],
    configured_node: str,
) -> tuple[str, str]:
    """Resolve one rigid model from authored NIF identity or engine contract."""

    authored_parents = {
        _text(extra.string_data)
        for extra in document.get_global_iterator()
        if isinstance(extra, NifFormat.NiStringExtraData)
        and _text(extra.name).casefold() == NIF_PARENT_EXTRA_DATA_NAME
        and _text(extra.string_data)
    }
    if len(authored_parents) > 1:
        raise ValueError(
            "Actor rigid component declares multiple NIF Prn attachment nodes: "
            f"{sorted(authored_parents)}"
        )
    if authored_parents:
        authored_parent = next(iter(authored_parents))
        if authored_parent not in node_by_name:
            raise ValueError(
                "Actor skeleton has no NIF Prn attachment node: "
                f"{authored_parent}"
            )
        return authored_parent, RIGID_ATTACHMENT_NIF_PARENT

    authored_root = _text(component_root.name)
    if authored_root in node_by_name:
        return authored_root, RIGID_ATTACHMENT_NIF_ROOT
    if configured_node not in node_by_name:
        raise ValueError(
            "Actor skeleton has no configured unparented rigid-attachment node: "
            f"{configured_node}"
        )
    return configured_node, RIGID_ATTACHMENT_CONFIGURED_NODE


def authored_rigid_attachment_node(payload: bytes) -> str:
    """Return the single owned NIF ``Prn`` attachment node."""

    document = decode_nif(payload).document
    authored_parents = _authored_rigid_attachment_nodes(document)
    if len(authored_parents) != 1:
        raise ValueError(
            "Actor animation object must declare exactly one owned NIF Prn "
            f"attachment node, found {list(authored_parents)}"
        )
    return authored_parents[0]


def _authored_rigid_attachment_nodes(document: object) -> tuple[str, ...]:
    return tuple(sorted({
        _text(extra.string_data)
        for extra in document.get_global_iterator()
        if isinstance(extra, NifFormat.NiStringExtraData)
        and _text(extra.name).casefold() == NIF_PARENT_EXTRA_DATA_NAME
        and _text(extra.string_data)
    }))


def _geometry_identity_tokens(value: str) -> frozenset[str]:
    words = re.findall(
        r"[A-Z]+(?=[A-Z][a-z]|$)|[A-Z]?[a-z]+|[0-9]+",
        value.replace("_", " ").replace("-", " "),
    )
    return frozenset(
        word.casefold()
        for word in words
        if word.casefold() not in RUNTIME_GEOMETRY_NONIDENTITY_TOKENS
        and not word.isdecimal()
    )


def _component_retail_role(component_role: str) -> str:
    if component_role.startswith("creature-model-"):
        return "actor"
    if component_role.startswith("head-part-"):
        return "headPart"
    return RETAIL_APPEARANCE_ROLE_BY_COMPONENT_ROLE.get(
        component_role,
        component_role,
    )


def _resolved_component_texture_paths(
    component: ActorComponent,
    source_texture_paths: tuple[str, ...],
) -> frozenset[str]:
    paths = {
        canonical_member_path(path)
        for path in source_texture_paths
        if path
    }
    if component.diffuse_override:
        paths.add(canonical_member_path(component.diffuse_override))
    aliases = {
        canonical_member_path(source): canonical_member_path(target)
        for source, target in component.diffuse_aliases
    }
    paths.update(aliases[path] for path in tuple(paths) if path in aliases)
    return frozenset(paths)


def _resolve_retail_rigid_part(
    component: ActorComponent,
    source_shape_name: str,
    source_texture_paths: tuple[str, ...],
    render_parts: tuple[RetailRenderPart, ...],
) -> tuple[RetailRenderPart, str]:
    if component.source_form_id is None or component.source_slot is None:
        raise ValueError(
            f"Rigid actor component {component.role}/{source_shape_name} has no retail source identity"
        )
    retail_role = _component_retail_role(component.role)
    creature_component = component.role.startswith("creature-model-")
    candidates = [
        part
        for part in render_parts
        if (creature_component or part.role == retail_role)
        and part.source_form_id == component.source_form_id
        and part.source_slot == component.source_slot
        and part.required
        and part.attached
        and part.drawable
        and part.visible
        and not part.skinned
    ]
    if not candidates:
        raise ValueError(
            "Rigid actor surface has no required retail render part: "
            f"{component.role}/{source_shape_name} "
            f"source={component.source_form_id}/{component.source_slot}"
        )

    exact_name = [
        part for part in candidates if part.geometry_name == source_shape_name
    ]
    if len(exact_name) == 1:
        return exact_name[0], "exact-runtime-geometry-name"

    component_textures = _resolved_component_texture_paths(
        component,
        source_texture_paths,
    )
    texture_scores = {
        part: len(
            component_textures.intersection(
                canonical_member_path(path) for path in part.texture_paths
            )
        )
        for part in candidates
    }
    maximum_texture_score = max(texture_scores.values())
    texture_matches = [
        part
        for part, score in texture_scores.items()
        if score == maximum_texture_score and score > 0
    ]
    if len(texture_matches) == 1:
        return texture_matches[0], "exact-owned-texture-binding"
    if len(texture_matches) > 1:
        visual_identities = {
            (
                part.geometry_name,
                tuple(sorted(canonical_member_path(path) for path in part.texture_paths)),
            )
            for part in texture_matches
        }
        if len(visual_identities) == 1:
            return (
                min(texture_matches, key=lambda part: part.visual_node_path),
                "retail-equivalent-duplicate-runtime-part",
            )

    source_tokens = _geometry_identity_tokens(source_shape_name)
    token_scores = {
        part: len(source_tokens.intersection(_geometry_identity_tokens(part.geometry_name)))
        for part in candidates
    }
    maximum_token_score = max(token_scores.values())
    token_matches = [
        part
        for part, score in token_scores.items()
        if score == maximum_token_score and score > 0
    ]
    if len(token_matches) == 1:
        return token_matches[0], "exact-geometry-token-lineage"

    if len(candidates) == 1:
        return candidates[0], "unique-source-bound-runtime-part"
    identities = sorted(
        f"{part.geometry_name}@{part.visual_node_path}" for part in candidates
    )
    raise ValueError(
        "Rigid actor surface has ambiguous retail render parts: "
        f"{component.role}/{source_shape_name} candidates={identities}"
    )


def _append_shape(
    source: ActorGltfInput,
    component: ActorComponent,
    component_root: object,
    shape: object,
    node_by_name: dict[str, int],
    builder: BufferBuilder,
    meshes: list[dict[str, object]],
    skins: list[dict[str, object]],
    materials: list[dict[str, object]],
    textures: TextureLibrary,
    compiler: ContentCompilerConfiguration,
) -> tuple[int, int | None, dict[str, object]]:
    mesh = shape.data
    source_shape_name = _text(shape.name)
    vertex_count = len(mesh.vertices)
    if vertex_count == 0:
        raise ValueError(f"Actor shape has no vertices: {_text(shape.name)}")
    properties = list(getattr(shape, "properties", []))
    texture_paths = actor_texture_paths(properties)
    generated_texture = component.generated_diffuse is not None or bool(
        component.generated_diffuse_by_source
    )
    requires_uv = any(texture_paths) or component.diffuse_override is not None or generated_texture
    if not mesh.uv_sets and requires_uv:
        raise ValueError(f"Textured actor shape has no UV0: {_text(shape.name)}")
    raw_positions = [(float(value.x), float(value.y), float(value.z)) for value in mesh.vertices]
    if (component.tri_path is None) != (component.tri_payload is None):
        raise ValueError(
            f"Actor component {component.role} has incomplete FaceGen TRI provenance"
        )
    facegen_tri = (
        decode_tri(component.tri_payload, compiler.facegen_animation)
        if component.tri_payload is not None
        else None
    )
    if facegen_tri is not None and facegen_tri.vertex_count != vertex_count:
        raise ValueError(
            "Actor FaceGen TRI vertex count differs from its exact sibling NIF: "
            f"{component.role} nif={vertex_count} tri={facegen_tri.vertex_count}"
        )
    morphed = False
    if component.egm_payload is not None:
        raw_positions = apply_geometry_morphs(
            raw_positions,
            component.egm_payload,
            source.symmetric_geometry,
            source.asymmetric_geometry,
            vertex_offset=component.egm_vertex_offset,
        )
        morphed = True
    shape_transform = shape.get_transform(component_root)
    rigid_to_head = getattr(shape, "skin_instance", None) is None
    retail_part = None
    retail_binding_authority = None
    if rigid_to_head and source.retail_render_parts:
        retail_part, retail_binding_authority = _resolve_retail_rigid_part(
            component,
            source_shape_name,
            texture_paths,
            source.retail_render_parts,
        )
    runtime_shape_name = (
        retail_part.geometry_name
        if retail_part is not None
        else component.runtime_shape_name or source_shape_name
    )
    if not runtime_shape_name:
        raise ValueError(f"Actor component {component.role} has no runtime shape identity")
    # Rigid face and hair meshes already store their vertices in the local
    # space of the authored PRN/root attachment. Baking the NiTriShape-to-NIF
    # root transform here and then parenting the result under that skeleton
    # node applies the attachment basis twice (most visibly rotating hair in
    # front of the face). Creature add-on NIFs use ordinary authored shape
    # transforms instead of the FaceGen biped basis, so their rigid geometry
    # is baked once before it is parented to its declared PRN/root bone.
    creature_rigid_shape = (
        rigid_to_head and component.role.startswith("creature-model-")
    )
    transform_shape = _bake_actor_shape_transform(
        component,
        rigid=rigid_to_head,
        retail_bound=retail_part is not None,
    )
    positions = [
        _transform_position(position, shape_transform)
        if transform_shape else _convert_vector(position)
        for position in raw_positions
    ]
    triangles = [tuple(int(index) for index in triangle) for triangle in mesh.get_triangles()]
    if not triangles:
        raise ValueError(f"Actor shape has no triangles: {_text(shape.name)}")
    if not morphed and len(mesh.normals) != vertex_count:
        raise ValueError(f"Actor shape has incomplete normals: {_text(shape.name)}")
    normals = _recompute_normals(positions, triangles) if morphed else [
        (
            _transform_direction(
                (float(value.x), float(value.y), float(value.z)),
                shape_transform,
            )
            if transform_shape
            else _convert_direction((float(value.x), float(value.y), float(value.z)))
        )
        for value in mesh.normals
    ]
    attributes: dict[str, int] = {
        "POSITION": builder.add(
            pack_floats(positions),
            component_type=GL_FLOAT,
            count=vertex_count,
            value_type="VEC3",
            target=GL_ARRAY_BUFFER,
            minimum=[min(row[axis] for row in positions) for axis in range(3)],
            maximum=[max(row[axis] for row in positions) for axis in range(3)],
        ),
        "NORMAL": builder.add(
            pack_floats(normals), component_type=GL_FLOAT, count=vertex_count, value_type="VEC3", target=GL_ARRAY_BUFFER
        ),
    }
    morph_targets, morph_manifest = _append_facegen_morph_targets(
        facegen_tri,
        positions,
        normals,
        triangles,
        shape_transform,
        transform_shape,
        builder,
        compiler,
    )
    geometry_targets, geometry_manifest = _append_facegen_geometry_control_targets(
        component,
        positions,
        normals,
        triangles,
        shape_transform,
        transform_shape,
        builder,
    )
    duplicate_targets = set(morph_manifest["targetNames"]).intersection(
        geometry_manifest["targetNames"]
    )
    if duplicate_targets:
        raise ValueError(
            "Actor FaceGen TRI and EGM control target names overlap: "
            f"{sorted(duplicate_targets)}"
        )
    morph_targets = [*geometry_targets, *morph_targets]
    morph_manifest = {
        **morph_manifest,
        "targetNames": [
            *geometry_manifest["targetNames"],
            *morph_manifest["targetNames"],
        ],
        "geometryControls": geometry_manifest,
    }
    tangent_source = "absent"
    if mesh.uv_sets:
        uvs = [(float(value.u), float(value.v)) for value in mesh.uv_sets[0]]
        if len(uvs) != vertex_count:
            raise ValueError(f"Actor shape has incomplete UV0: {_text(shape.name)}")
        tangents = generate_tangents(positions, normals, uvs, triangles)
        attributes["TANGENT"] = builder.add(
            pack_floats(tangents), component_type=GL_FLOAT, count=vertex_count,
            value_type="VEC4", target=GL_ARRAY_BUFFER,
        )
        attributes["TEXCOORD_0"] = builder.add(
            pack_floats(uvs), component_type=GL_FLOAT, count=vertex_count,
            value_type="VEC2", target=GL_ARRAY_BUFFER,
        )
        tangent_source = "generated-uv-triangle"
    vertex_colors_enabled = actor_vertex_colors_enabled(properties)
    if vertex_colors_enabled:
        if len(mesh.vertex_colors) != vertex_count:
            raise ValueError(f"Actor shader enables incomplete vertex colors: {_text(shape.name)}")
        colors = [
            (float(value.r), float(value.g), float(value.b), float(value.a))
            for value in mesh.vertex_colors
        ]
        attributes["COLOR_0"] = builder.add(
            pack_floats(colors),
            component_type=GL_FLOAT,
            count=vertex_count,
            value_type="VEC4",
            target=GL_ARRAY_BUFFER,
        )
    skin_index: int | None = None
    if not rigid_to_head:
        skin_index = _append_skin(
            component.role,
            shape,
            node_by_name,
            builder,
            attributes,
            skins,
            shape_transform if component.bake_shape_transform else None,
        )
    indices = [value for triangle in triangles for value in triangle]
    component_type = GL_UNSIGNED_SHORT if vertex_count <= GL_UNSIGNED_SHORT_MAX else GL_UNSIGNED_INT
    value_format = "H" if component_type == GL_UNSIGNED_SHORT else "I"
    index_accessor = builder.add(
        struct.pack(f"<{len(indices)}{value_format}", *indices),
        component_type=component_type,
        count=len(indices),
        value_type="SCALAR",
        target=GL_ELEMENT_ARRAY_BUFFER,
    )
    material_index = len(materials)
    material, material_row = _material(component, shape, textures, compiler)
    materials.append(material)
    mesh_index = len(meshes)
    primitive: dict[str, object] = {
        "attributes": attributes,
        "indices": index_accessor,
        "material": material_index,
    }
    mesh_document: dict[str, object] = {
        "name": f"{component.role}_{runtime_shape_name}",
        "primitives": [primitive],
    }
    if morph_targets:
        primitive["targets"] = morph_targets
        mesh_document["weights"] = [0.0 for _target in morph_targets]
        mesh_document["extras"] = {"targetNames": morph_manifest["targetNames"]}
    meshes.append(mesh_document)
    return mesh_index, skin_index, {
        "role": component.role,
        "sourceFormId": component.source_form_id,
        "sourceSlot": component.source_slot,
        "modelPath": component.model_path,
        "modelSha256": hashlib.sha256(component.model_payload).hexdigest(),
        "egmPath": component.egm_path,
        "egmSha256": hashlib.sha256(component.egm_payload).hexdigest() if component.egm_payload else None,
        "triPath": component.tri_path,
        "triSha256": hashlib.sha256(component.tri_payload).hexdigest() if component.tri_payload else None,
        "shape": runtime_shape_name,
        "sourceShape": source_shape_name,
        "sourceVertexFnv1a32": _shape_vertex_fnv1a32(shape),
        "sourceShapeTranslationGameUnits": [
            float(shape_transform.m_41),
            float(shape_transform.m_42),
            float(shape_transform.m_43),
        ],
        "sourceShapeTransformGodotMatrix": list(
            _gltf_matrix(_converted_matrix(shape_transform))
        ),
        "vertices": vertex_count,
        "triangles": len(triangles),
        "uvSets": len(mesh.uv_sets),
        "tangentSource": tangent_source,
        "morphed": morphed,
        "faceGenMorphs": morph_manifest,
        "skinned": skin_index is not None,
        "attachmentSource": "nif-skin" if skin_index is not None else None,
        "retailGeometryName": (
            retail_part.geometry_name if retail_part is not None else None
        ),
        "retailVisualNodePath": (
            retail_part.visual_node_path if retail_part is not None else None
        ),
        "retailBindingAuthority": retail_binding_authority,
        "skinWeightSource": "nif-hardware-skin-partition" if skin_index is not None else None,
        "skinShapeTransformCompensated": (
            skin_index is not None and component.bake_shape_transform
        ),
        "rigidShapeTransformBaked": rigid_to_head and transform_shape,
        "vertexColorsEnabled": vertex_colors_enabled,
        "material": material_row,
    }


def _append_facegen_morph_targets(
    facegen_tri: FaceGenTri | None,
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    triangles: list[tuple[int, int, int]],
    shape_transform: object,
    transform_shape: bool,
    builder: BufferBuilder,
    compiler: ContentCompilerConfiguration,
) -> tuple[list[dict[str, int]], dict[str, object]]:
    if facegen_tri is None:
        return [], {
            "source": "absent",
            "targetNames": [],
            "differentialTargets": [],
            "staticTargets": [],
        }
    contract = compiler.facegen_animation.tri
    source_targets: list[
        tuple[str, str, tuple[tuple[float, float, float], ...]]
    ] = []
    if "differential" in contract.export_morph_kinds:
        source_targets.extend(
            (morph.name, "differential", morph.deltas)
            for morph in facegen_tri.differential_morphs
        )
    if "static" in contract.export_morph_kinds:
        for morph in facegen_tri.static_morphs:
            deltas = [
                tuple(0.0 for _axis in range(contract.position_components))
                for _vertex in facegen_tri.base_vertices
            ]
            for vertex_index, replacement in morph.replacements:
                deltas[vertex_index] = tuple(
                    replacement[axis] - facegen_tri.base_vertices[vertex_index][axis]
                    for axis in range(contract.position_components)
                )
            source_targets.append((morph.name, "static", tuple(deltas)))

    target_documents = []
    target_names = []
    differential_names = []
    static_names = []
    for name, kind, source_deltas in source_targets:
        if len(source_deltas) != len(positions):
            raise ValueError(
                f"Actor FaceGen morph {name!r} differs from its NIF vertex count"
            )
        deltas = [
            _transform_delta(delta, shape_transform)
            if transform_shape
            else _convert_vector(delta)
            for delta in source_deltas
        ]
        morphed_positions = [
            tuple(position[axis] + delta[axis] for axis in range(contract.position_components))
            for position, delta in zip(positions, deltas, strict=True)
        ]
        morphed_normals = _recompute_normals(morphed_positions, triangles)
        normal_deltas = [
            tuple(
                morphed[axis] - base[axis]
                for axis in range(contract.position_components)
            )
            for base, morphed in zip(normals, morphed_normals, strict=True)
        ]
        target_documents.append(
            {
                "POSITION": builder.add(
                    pack_floats(deltas),
                    component_type=GL_FLOAT,
                    count=len(deltas),
                    value_type="VEC3",
                    target=GL_ARRAY_BUFFER,
                ),
                "NORMAL": builder.add(
                    pack_floats(normal_deltas),
                    component_type=GL_FLOAT,
                    count=len(normal_deltas),
                    value_type="VEC3",
                    target=GL_ARRAY_BUFFER,
                ),
            }
        )
        target_names.append(name)
        (differential_names if kind == "differential" else static_names).append(name)
    return target_documents, {
        "source": "exact-owned-tri",
        "targetNames": target_names,
        "differentialTargets": differential_names,
        "staticTargets": static_names,
    }


def _append_facegen_geometry_control_targets(
    component: ActorComponent,
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    triangles: list[tuple[int, int, int]],
    shape_transform: object,
    transform_shape: bool,
    builder: BufferBuilder,
) -> tuple[list[dict[str, int]], dict[str, object]]:
    names = component.egm_symmetric_control_names
    axes = component.egm_symmetric_control_axes
    if not names and not axes:
        return [], {
            "source": "absent",
            "targetNames": [],
            "axisSha256": [],
        }
    if (
        component.egm_payload is None
        or component.egm_path is None
        or len(names) != len(axes)
        or len(set(names)) != len(names)
    ):
        raise ValueError(
            f"Actor component {component.role} has an incomplete EGM control contract"
        )
    source_controls = facegen_geometry_control_deltas(
        component.egm_payload,
        axes,
        vertex_offset=component.egm_vertex_offset,
        vertex_count=len(positions),
    )
    targets = []
    for name, source_deltas in zip(names, source_controls, strict=True):
        deltas = [
            _transform_delta(delta, shape_transform)
            if transform_shape
            else _convert_vector(delta)
            for delta in source_deltas
        ]
        morphed_positions = [
            tuple(position[axis] + delta[axis] for axis in range(3))
            for position, delta in zip(positions, deltas, strict=True)
        ]
        morphed_normals = _recompute_normals(morphed_positions, triangles)
        normal_deltas = [
            tuple(morphed[axis] - base[axis] for axis in range(3))
            for base, morphed in zip(normals, morphed_normals, strict=True)
        ]
        targets.append(
            {
                "POSITION": builder.add(
                    pack_floats(deltas),
                    component_type=GL_FLOAT,
                    count=len(deltas),
                    value_type="VEC3",
                    target=GL_ARRAY_BUFFER,
                ),
                "NORMAL": builder.add(
                    pack_floats(normal_deltas),
                    component_type=GL_FLOAT,
                    count=len(normal_deltas),
                    value_type="VEC3",
                    target=GL_ARRAY_BUFFER,
                ),
            }
        )
    return targets, {
        "source": "exact-owned-egm-composed-through-ctl-axis",
        "egmPath": component.egm_path,
        "egmSha256": hashlib.sha256(component.egm_payload).hexdigest(),
        "targetNames": list(names),
        "axisSha256": [
            hashlib.sha256(struct.pack(f"<{len(axis)}f", *axis)).hexdigest()
            for axis in axes
        ],
    }


def _bake_actor_shape_transform(
    component: ActorComponent,
    *,
    rigid: bool,
    retail_bound: bool,
) -> bool:
    """Apply the owned NIF component basis exactly once when required.

    Retail render-part binding selects the visible owned geometry; it does not
    replace that geometry's authored transform. Creature add-on NIFs are rigid
    PRN attachments whose vertices must be converted into attachment-local
    space even when a retail observation selected the surface.
    """

    if rigid and component.role.startswith("creature-model-"):
        return True
    return not retail_bound and component.bake_shape_transform


_DISMEMBER_CAP_BODY_PARTS = frozenset(
    value
    for name, value in zip(
        NifFormat.BSDismemberBodyPartType._enumkeys,
        NifFormat.BSDismemberBodyPartType._enumvalues,
    )
    if name.startswith("BP_SECTIONCAP_")
    or name.startswith("BP_TORSOCAP_")
    or name in {
        "SBP_130_HEAD",
        "SBP_131_HAIR",
        "SBP_141_LONGHAIR",
        "SBP_142_CIRCLET",
        "SBP_143_EARS",
        "SBP_150_DECAPITATEDHEAD",
        "SBP_230_HEAD",
    }
)


def _is_dismember_cap_shape(shape: object) -> bool:
    instance = getattr(shape, "skin_instance", None)
    if not isinstance(instance, NifFormat.BSDismemberSkinInstance):
        return False
    partitions = list(getattr(instance, "partitions", []))
    return bool(partitions) and all(
        int(partition.body_part) in _DISMEMBER_CAP_BODY_PARTS for partition in partitions
    )


def _shape_vertex_fnv1a32(shape: object) -> int:
    value = 2166136261
    for vertex in shape.data.vertices:
        for component in (float(vertex.x), float(vertex.y), float(vertex.z)):
            for byte in struct.pack("<f", component):
                value = ((value ^ byte) * 16777619) & 0xFFFFFFFF
    return value


def _omitted_shape_row(
    component: ActorComponent,
    shape: object,
    disposition: str,
    authority: str,
) -> dict[str, object]:
    return {
        "role": component.role,
        "modelPath": component.model_path,
        "modelSha256": hashlib.sha256(component.model_payload).hexdigest(),
        "shape": _text(shape.name),
        "vertices": len(shape.data.vertices),
        "sourceVertexFnv1a32": _shape_vertex_fnv1a32(shape),
        "disposition": disposition,
        "authority": authority,
    }


def _append_skin(
    role: str,
    shape: object,
    node_by_name: dict[str, int],
    builder: BufferBuilder,
    attributes: dict[str, int],
    skins: list[dict[str, object]],
    baked_shape_transform: object | None,
) -> int:
    instance = getattr(shape, "skin_instance", None)
    if instance is None or instance.data is None:
        raise ValueError(f"Actor non-rigid shape is not skinned: {_text(shape.name)}")
    bone_names = [_text(bone.name) for bone in instance.bones]
    missing = [name for name in bone_names if name not in node_by_name]
    if missing:
        raise ValueError(
            f"Actor {role} shape {_text(shape.name)!r} skin references missing skeleton nodes: {missing}"
        )
    weights = _hardware_vertex_weights(shape)
    joint_rows = []
    weight_rows = []
    for vertex_weights in weights:
        joints = [int(item[0]) for item in vertex_weights]
        values = [float(item[1]) for item in vertex_weights]
        joint_rows.append(tuple(
            joints + [0] * (GLTF_PRIMARY_SKIN_INFLUENCES - len(joints))))
        weight_rows.append(tuple(
            values + [0.0] * (GLTF_PRIMARY_SKIN_INFLUENCES - len(values))))
    joint_payload = struct.pack(
        f"<{len(joint_rows) * GLTF_PRIMARY_SKIN_INFLUENCES}H",
        *(value for row in joint_rows for value in row),
    )
    attributes["JOINTS_0"] = builder.add(
        joint_payload, component_type=GL_UNSIGNED_SHORT, count=len(joint_rows), value_type="VEC4", target=GL_ARRAY_BUFFER
    )
    attributes["WEIGHTS_0"] = builder.add(
        pack_floats(weight_rows), component_type=GL_FLOAT, count=len(weight_rows), value_type="VEC4", target=GL_ARRAY_BUFFER
    )
    inverse_bind_rows = []
    if len(instance.data.bone_list) != len(bone_names):
        raise ValueError(f"Actor skin bone data count drift: {_text(shape.name)}")
    for data in instance.data.bone_list:
        inverse_bind_rows.append(
            _gltf_matrix(
                _compensated_inverse_bind(
                    data.get_transform(),
                    baked_shape_transform,
                )
            )
        )
    inverse_bind = builder.add(
        pack_floats(inverse_bind_rows),
        component_type=GL_FLOAT,
        count=len(inverse_bind_rows),
        value_type="MAT4",
        target=None,
    )
    skin_index = len(skins)
    skins.append(
        {
            "name": f"{_text(shape.name)} skin",
            "joints": [node_by_name[name] for name in bone_names],
            "skeleton": node_by_name["Bip01"],
            "inverseBindMatrices": inverse_bind,
        }
    )
    return skin_index


def _hardware_vertex_weights(shape: object) -> list[list[tuple[int, float]]]:
    """Resolve the exact bone indices and weights consumed by retail's D3D skin path."""

    instance = getattr(shape, "skin_instance", None)
    partition = getattr(instance, "skin_partition", None)
    blocks = list(getattr(partition, "skin_partition_blocks", []))
    vertex_count = len(shape.data.vertices)
    if instance is None or partition is None or not blocks or vertex_count <= 0:
        raise ValueError(f"Actor skin has no hardware partitions: {_text(shape.name)}")
    resolved: list[list[tuple[int, float]] | None] = [None] * vertex_count
    for block_index, block in enumerate(blocks):
        vertex_map = list(block.vertex_map)
        vertex_weights = list(block.vertex_weights)
        bone_indices = list(block.bone_indices)
        bone_palette = [int(value) for value in block.bones]
        if (
            not vertex_map
            or len(vertex_map) != len(vertex_weights)
            or len(vertex_map) != len(bone_indices)
            or not bone_palette
        ):
            raise ValueError(
                f"Actor skin partition {block_index} is incomplete: {_text(shape.name)}"
            )
        for local_index, vertex_value in enumerate(vertex_map):
            vertex_index = int(vertex_value)
            if vertex_index < 0 or vertex_index >= vertex_count:
                raise ValueError(
                    f"Actor skin partition {block_index} has an invalid vertex index: "
                    f"{_text(shape.name)}"
                )
            weights = [float(value) for value in vertex_weights[local_index]]
            indices = [int(value) for value in bone_indices[local_index]]
            if len(weights) != len(indices) or len(weights) != GLTF_PRIMARY_SKIN_INFLUENCES:
                raise ValueError(
                    f"Actor skin partition {block_index} changes its influence width: "
                    f"{_text(shape.name)}"
                )
            row = []
            for palette_index, weight in zip(indices, weights):
                if not math.isfinite(weight) or weight < 0.0:
                    raise ValueError(
                        f"Actor skin partition {block_index} has an invalid weight: "
                        f"{_text(shape.name)}"
                    )
                if weight == 0.0:
                    continue
                if palette_index < 0 or palette_index >= len(bone_palette):
                    raise ValueError(
                        f"Actor skin partition {block_index} has an invalid palette index: "
                        f"{_text(shape.name)}"
                    )
                row.append((bone_palette[palette_index], weight))
            if not row or abs(sum(weight for _, weight in row) - 1.0) > SKIN_WEIGHT_SUM_TOLERANCE:
                raise ValueError(
                    f"Actor skin partition {block_index} has an unnormalized vertex: "
                    f"{_text(shape.name)}"
                )
            previous = resolved[vertex_index]
            if previous is not None:
                previous_by_bone = dict(previous)
                row_by_bone = dict(row)
                if set(previous_by_bone) != set(row_by_bone) or any(
                    abs(previous_by_bone[bone] - row_by_bone[bone]) >
                        SKIN_WEIGHT_DUPLICATE_TOLERANCE
                    for bone in previous_by_bone
                ):
                    raise ValueError(
                        f"Actor hardware partitions disagree for vertex {vertex_index}: "
                        f"{_text(shape.name)}"
                    )
            else:
                resolved[vertex_index] = row
    missing = [index for index, row in enumerate(resolved) if row is None]
    if missing:
        raise ValueError(
            f"Actor hardware skin omits {len(missing)} vertices: {_text(shape.name)}"
        )
    return [row for row in resolved if row is not None]


def _build_animation(
    payload: bytes,
    node_by_name: dict[str, int],
    nodes: list[dict[str, object]],
    builder: BufferBuilder,
    compiler: ContentCompilerConfiguration,
    accumulation_root_node: str,
    nonaccumulation_root_node: str | None,
    animation_name: str | None = None,
    retain_accumulation_root_translation: bool = False,
) -> tuple[dict[str, object] | None, int, tuple[float, float, float] | None]:
    document = _read_nif(payload)
    sequence = document.roots[0]
    if not isinstance(sequence, NifFormat.NiControllerSequence):
        raise ValueError(f"Actor idle root is not NiControllerSequence: {type(sequence).__name__}")
    start = float(sequence.start_time)
    stop = float(sequence.stop_time)
    if start != 0.0 or stop <= start:
        raise ValueError(f"Actor idle has an unexpected time range: {start}..{stop}")
    times = _animation_sample_times(
        start,
        stop,
        compiler.animation_samples_per_second,
    )
    time_accessor = builder.add(
        struct.pack(f"<{len(times)}f", *times),
        component_type=GL_FLOAT,
        count=len(times),
        value_type="SCALAR",
        target=None,
        minimum=[start],
        maximum=[stop],
    )
    samplers = []
    channels = []
    nonaccum_origin = None
    accumulation_root_translation_authored = False
    for controlled in sequence.controlled_blocks:
        node_name = _text(controlled.get_node_name())
        if node_name not in node_by_name or _text(controlled.get_controller_type()) != "NiTransformController":
            continue
        translations, rotations = _sample_transform_interpolator(
            controlled.interpolator,
            times,
            start,
            stop,
            node_name,
        )

        if translations:
            if node_name == accumulation_root_node:
                accumulation_root_translation_authored = True
            if node_name == nonaccumulation_root_node:
                nonaccum_origin = translations[0]
            translations = actor_animation_translations(
                node_name,
                translations,
                accumulation_root_node,
                nonaccumulation_root_node,
                retain_accumulation_root_translation,
            )
            output = builder.add(
                pack_floats(translations),
                component_type=GL_FLOAT,
                count=len(translations),
                value_type="VEC3",
                target=None,
            )
            sampler = len(samplers)
            samplers.append({"input": time_accessor, "output": output, "interpolation": "LINEAR"})
            channels.append({"sampler": sampler, "target": {"node": node_by_name[node_name], "path": "translation"}})
        if rotations:
            rotations = _continuous_quaternions(rotations)
            output = builder.add(
                pack_floats(rotations),
                component_type=GL_FLOAT,
                count=len(rotations),
                value_type="VEC4",
                target=None,
            )
            sampler = len(samplers)
            samplers.append({"input": time_accessor, "output": output, "interpolation": "LINEAR"})
            channels.append({"sampler": sampler, "target": {"node": node_by_name[node_name], "path": "rotation"}})
    root_world_translation = _world_authoritative_accumulation_root_translations(
        accumulation_root_translation_authored,
        retain_accumulation_root_translation,
        len(times),
    )
    if root_world_translation:
        output = builder.add(
            pack_floats(root_world_translation),
            component_type=GL_FLOAT,
            count=len(root_world_translation),
            value_type="VEC3",
            target=None,
        )
        sampler = len(samplers)
        samplers.append(
            {
                "input": time_accessor,
                "output": output,
                "interpolation": "LINEAR",
            }
        )
        channels.append(
            {
                "sampler": sampler,
                "target": {
                    "node": node_by_name[accumulation_root_node],
                    "path": "translation",
                },
            }
        )
    if not channels:
        return None, 0, nonaccum_origin
    return (
        {
            "name": animation_name or _text(sequence.name),
            "samplers": samplers,
            "channels": channels,
        },
        len(channels),
        nonaccum_origin,
    )


def sample_transform_animation(
    animation_payload: bytes,
    skeleton_payload: bytes,
    target_node: str,
    samples_per_second: float,
    *,
    include_animated_parent_tracks: bool = False,
) -> SampledTransformAnimation:
    """Sample one owned KF transform against its owned skeleton parent chain."""

    document = _read_nif(animation_payload)
    sequence = document.roots[0]
    if not isinstance(sequence, NifFormat.NiControllerSequence):
        raise ValueError(
            "Transform animation root is not NiControllerSequence: "
            f"{type(sequence).__name__}"
        )
    start = float(sequence.start_time)
    stop = float(sequence.stop_time)
    if start != 0.0 or stop <= start:
        raise ValueError(
            f"Transform animation has an unexpected time range: {start}..{stop}"
        )
    controlled = [
        value
        for value in sequence.controlled_blocks
        if _text(value.get_node_name()) == target_node
        and _text(value.get_controller_type()) == "NiTransformController"
    ]
    if len(controlled) != 1:
        raise ValueError(
            f"Transform animation must control one {target_node!r} node, "
            f"found {len(controlled)}"
        )
    times = _animation_sample_times(start, stop, samples_per_second)
    translations, rotations = _sample_transform_interpolator(
        controlled[0].interpolator,
        times,
        start,
        stop,
        target_node,
    )
    if len(translations) != len(times) or len(rotations) != len(times):
        raise ValueError(
            f"Transform animation {target_node!r} must author translation and rotation"
        )

    skeleton = _read_nif(skeleton_payload)
    nodes = [
        node
        for node in skeleton.get_global_iterator()
        if isinstance(node, NifFormat.NiNode) and _text(node.name) == target_node
    ]
    if len(nodes) != 1:
        raise ValueError(
            f"Actor skeleton must contain one {target_node!r} node, found {len(nodes)}"
        )
    parents = {
        id(child): node
        for node in skeleton.get_global_iterator()
        if isinstance(node, NifFormat.NiNode)
        for child in node.children
        if child is not None
    }
    parent_nodes = []
    parent = parents.get(id(nodes[0]))
    while parent is not None:
        parent_nodes.append(parent)
        parent = parents.get(id(parent))
    parent_nodes.reverse()
    parent_chain = tuple(
        {
            "nodeName": _text(node.name),
            "translationGodotGameUnits": translation,
            "rotationQuaternionXyzw": rotation,
            "scale": scale,
        }
        for node in parent_nodes
        for translation, rotation, scale in [_node_trs(node)]
    )
    animated_parent_tracks = (
        _sample_animated_parent_tracks(
            sequence,
            parent_nodes,
            times,
            start,
            stop,
        )
        if include_animated_parent_tracks
        else ()
    )
    return SampledTransformAnimation(
        _text(sequence.name),
        target_node,
        start,
        stop,
        int(sequence.cycle_type),
        samples_per_second,
        parent_chain,
        tuple(times),
        tuple(translations),
        tuple(rotations),
        animated_parent_tracks,
    )


def _sample_animated_parent_tracks(
    sequence: object,
    parent_nodes: list[object],
    times: list[float],
    start: float,
    stop: float,
) -> tuple[dict[str, object], ...]:
    """Sample every KF-authored node in the target's skeleton parent chain."""

    tracks = []
    for parent_chain_index, node in enumerate(parent_nodes):
        node_name = _text(node.name)
        controlled = [
            value
            for value in sequence.controlled_blocks
            if _text(value.get_node_name()) == node_name
            and _text(value.get_controller_type()) == "NiTransformController"
        ]
        if len(controlled) > 1:
            raise ValueError(
                f"Transform animation controls parent {node_name!r} more than once"
            )
        if not controlled:
            continue
        translations, rotations = _sample_transform_interpolator(
            controlled[0].interpolator,
            times,
            start,
            stop,
            node_name,
        )
        if len(translations) != len(times) or len(rotations) != len(times):
            raise ValueError(
                f"Animated parent {node_name!r} must author translation and rotation"
            )
        tracks.append(
            {
                "parentChainIndex": parent_chain_index,
                "nodeName": node_name,
                "samples": [
                    {
                        "timeSeconds": time_value,
                        "translationGodotGameUnits": list(translation),
                        "rotationQuaternionXyzw": list(rotation),
                    }
                    for time_value, translation, rotation in zip(
                        times,
                        translations,
                        rotations,
                        strict=True,
                    )
                ],
            }
        )
    return tuple(tracks)


def sample_root_motion(
    animation_payload: bytes,
    target_node: str,
    samples_per_second: float,
) -> SampledRootMotion:
    """Resolve owned locomotion displacement without baking it into the pose."""

    document = _read_nif(animation_payload)
    sequence = document.roots[0]
    if not isinstance(sequence, NifFormat.NiControllerSequence):
        raise ValueError(
            "Root-motion animation root is not NiControllerSequence: "
            f"{type(sequence).__name__}"
        )
    start = float(sequence.start_time)
    stop = float(sequence.stop_time)
    if start != 0.0 or stop <= start:
        raise ValueError(
            f"Root-motion animation has an unexpected time range: {start}..{stop}"
        )
    controlled = [
        value
        for value in sequence.controlled_blocks
        if _text(value.get_node_name()) == target_node
        and _text(value.get_controller_type()) == "NiTransformController"
    ]
    if len(controlled) != 1:
        raise ValueError(
            f"Root-motion animation must control one {target_node!r} node, "
            f"found {len(controlled)}"
        )
    times = _animation_sample_times(start, stop, samples_per_second)
    translations, _rotations = _sample_transform_interpolator(
        controlled[0].interpolator,
        times,
        start,
        stop,
        target_node,
    )
    if len(translations) != len(times):
        raise ValueError(
            f"Root-motion animation {target_node!r} must author translation"
        )
    first = translations[0]
    last = translations[-1]
    displacement = tuple(last[axis] - first[axis] for axis in range(3))
    if not all(math.isfinite(value) for value in displacement) or not any(
        abs(value) > NORMALIZATION_EPSILON for value in displacement
    ):
        raise ValueError(
            f"Root-motion animation {target_node!r} has no finite displacement"
        )
    return SampledRootMotion(
        _text(sequence.name),
        target_node,
        start,
        stop,
        int(sequence.cycle_type),
        displacement,
    )


def _animation_sample_times(
    start: float,
    stop: float,
    samples_per_second: float,
) -> list[float]:
    if not math.isfinite(samples_per_second) or samples_per_second <= 0.0:
        raise ValueError("Animation sampling frequency must be positive and finite")
    frame_count = round((stop - start) * samples_per_second) + 1
    times = [start + frame / samples_per_second for frame in range(frame_count)]
    times[-1] = stop
    return times


def _sample_transform_interpolator(
    interpolator: object,
    times: list[float],
    start: float,
    stop: float,
    node_name: str,
) -> tuple[
    list[tuple[float, float, float]],
    list[tuple[float, float, float, float]],
]:
    translations: list[tuple[float, float, float]] = []
    rotations: list[tuple[float, float, float, float]] = []
    if isinstance(interpolator, NifFormat.NiBSplineCompTransformInterpolator):
        translation_control = list(interpolator.get_translations())
        rotation_control = list(interpolator.get_rotations())
        if translation_control:
            translations = [
                _convert_vector(
                    _uniform_cubic(translation_control, time_value, start, stop)
                )
                for time_value in times
            ]
        if rotation_control:
            rotations = [
                _converted_nif_quaternion(
                    _normalize_quaternion(
                        _uniform_cubic(rotation_control, time_value, start, stop)
                    )
                )
                for time_value in times
            ]
    elif isinstance(interpolator, NifFormat.NiTransformInterpolator):
        data = interpolator.data
        if data is not None:
            translation_keys = list(data.translations.keys)
            if translation_keys:
                interpolation = int(data.translations.interpolation)
                if interpolation == NIF_LINEAR_INTERPOLATION:
                    translations = [
                        _convert_vector(_linear_vector_keys(translation_keys, time_value))
                        for time_value in times
                    ]
                elif interpolation == NIF_QUADRATIC_INTERPOLATION:
                    translations = [
                        _convert_vector(
                            _quadratic_vector_keys(translation_keys, time_value)
                        )
                        for time_value in times
                    ]
                else:
                    raise ValueError(
                        "Actor animation uses unsupported translation interpolation "
                        f"on {node_name}"
                    )
            if int(data.num_rotation_keys) > 0:
                if int(data.rotation_type) in {
                    NIF_LINEAR_INTERPOLATION,
                    NIF_QUADRATIC_INTERPOLATION,
                    NIF_TBC_INTERPOLATION,
                }:
                    quaternion_keys = list(data.quaternion_keys)
                    rotations = [
                        _converted_nif_quaternion(
                            _slerp_keys(quaternion_keys, time_value)
                        )
                        for time_value in times
                    ]
                elif int(data.rotation_type) == NIF_XYZ_ROTATION_INTERPOLATION:
                    rotations = [
                        _converted_xyz_rotation(data.xyz_rotations, time_value)
                        for time_value in times
                    ]
                else:
                    raise ValueError(
                        "Actor animation uses unsupported rotation interpolation "
                        f"on {node_name}"
                    )
        if not translations:
            source_translation = _valid_constant_transform_components(
                _nif_vector(interpolator.translation),
                node_name,
                "translation",
            )
            if source_translation is not None:
                translation = _convert_vector(source_translation)
                translations = [translation for _time in times]
        if not rotations:
            rotation = _valid_constant_transform_components(
                _nif_quaternion(interpolator.rotation),
                node_name,
                "rotation",
            )
            if rotation is not None:
                converted = _converted_nif_quaternion(
                    _normalize_quaternion(rotation)
                )
                rotations = [converted for _time in times]
    else:
        raise ValueError(
            "Actor animation uses unsupported transform interpolator: "
            f"{type(interpolator).__name__}"
        )
    return translations, rotations


def _valid_constant_transform_components(
    values: tuple[float, ...],
    node_name: str,
    role: str,
) -> tuple[float, ...] | None:
    if not all(math.isfinite(value) for value in values):
        raise ValueError(
            f"Actor animation has a non-finite constant {role} on {node_name}"
        )
    invalid = tuple(
        abs(value) >= NIF_INVALID_TRANSFORM_COMPONENT_MAGNITUDE
        for value in values
    )
    if all(invalid):
        return None
    if any(invalid):
        raise ValueError(
            f"Actor animation has a partial invalid constant {role} on {node_name}"
        )
    return values


def _uniform_cubic(
    control_points: list[tuple[float, ...]],
    time_value: float,
    start: float,
    stop: float,
) -> tuple[float, ...]:
    if len(control_points) < 4:
        raise ValueError("Actor B-spline has fewer than four control points")
    position = max(0.0, min(1.0, (time_value - start) / (stop - start))) * (len(control_points) - 3)
    segment = min(len(control_points) - 4, int(math.floor(position)))
    value = position - segment
    if time_value >= stop:
        value = 1.0
    inverse = 1.0 - value
    basis = (
        inverse**3 / UNIFORM_CUBIC_BASIS_DIVISOR,
        (
            3.0 * value**3
            - UNIFORM_CUBIC_BASIS_DIVISOR * value**2
            + 4.0
        ) / UNIFORM_CUBIC_BASIS_DIVISOR,
        (
            -3.0 * value**3
            + 3.0 * value**2
            + 3.0 * value
            + 1.0
        ) / UNIFORM_CUBIC_BASIS_DIVISOR,
        value**3 / UNIFORM_CUBIC_BASIS_DIVISOR,
    )
    return tuple(
        sum(basis[index] * float(control_points[segment + index][axis]) for index in range(4))
        for axis in range(len(control_points[0]))
    )


def actor_animation_translations(
    node_name: str,
    values: list[tuple[float, float, float]],
    accumulation_root_node: str,
    nonaccumulation_root_node: str | None,
    retain_accumulation_root_translation: bool = False,
) -> list[tuple[float, float, float]]:
    if not values:
        return values
    if node_name == accumulation_root_node:
        if not retain_accumulation_root_translation:
            return [(0.0, 0.0, 0.0) for _ in values]
        origin = values[0]
        return [
            tuple(value[axis] - origin[axis] for axis in range(3))
            for value in values
        ]
    if node_name != nonaccumulation_root_node:
        return values
    return values


def _linear_vector_keys(keys: list[object], time_value: float) -> tuple[float, float, float]:
    if time_value <= float(keys[0].time):
        return _nif_vector(keys[0].value)
    if time_value >= float(keys[-1].time):
        return _nif_vector(keys[-1].value)
    for first, second in zip(keys, keys[1:]):
        first_time, second_time = float(first.time), float(second.time)
        if first_time <= time_value <= second_time:
            amount = (time_value - first_time) / (second_time - first_time)
            one, two = _nif_vector(first.value), _nif_vector(second.value)
            return tuple(one[axis] + amount * (two[axis] - one[axis]) for axis in range(3))
    raise ValueError("Actor translation key interval was not found")


def _quadratic_vector_keys(
    keys: list[object],
    time_value: float,
) -> tuple[float, float, float]:
    if time_value <= float(keys[0].time):
        return _nif_vector(keys[0].value)
    if time_value >= float(keys[-1].time):
        return _nif_vector(keys[-1].value)
    for first, second in zip(keys, keys[1:]):
        first_time, second_time = float(first.time), float(second.time)
        if first_time <= time_value <= second_time:
            amount = (time_value - first_time) / (second_time - first_time)
            squared = amount * amount
            cubed = squared * amount
            first_value = _nif_vector(first.value)
            second_value = _nif_vector(second.value)
            first_tangent = _nif_vector(first.backward)
            second_tangent = _nif_vector(second.forward)
            first_basis = 2.0 * cubed - 3.0 * squared + 1.0
            second_basis = -2.0 * cubed + 3.0 * squared
            first_tangent_basis = cubed - 2.0 * squared + amount
            second_tangent_basis = cubed - squared
            return tuple(
                first_value[axis] * first_basis
                + second_value[axis] * second_basis
                + first_tangent[axis] * first_tangent_basis
                + second_tangent[axis] * second_tangent_basis
                for axis in range(len(first_value))
            )
    raise ValueError("Actor quadratic translation key interval was not found")


def _scalar_keys(group: object, time_value: float) -> float:
    keys = list(group.keys)
    if not keys:
        raise ValueError("Actor XYZ rotation channel has no keys")
    if time_value <= float(keys[0].time):
        return float(keys[0].value)
    if time_value >= float(keys[-1].time):
        return float(keys[-1].value)
    interpolation = int(group.interpolation)
    for first, second in zip(keys, keys[1:]):
        first_time, second_time = float(first.time), float(second.time)
        if first_time <= time_value <= second_time:
            amount = (time_value - first_time) / (second_time - first_time)
            first_value = float(first.value)
            second_value = float(second.value)
            if interpolation == NIF_LINEAR_INTERPOLATION:
                return first_value + amount * (second_value - first_value)
            if interpolation == NIF_QUADRATIC_INTERPOLATION:
                squared = amount * amount
                cubed = squared * amount
                return (
                    first_value * (2.0 * cubed - 3.0 * squared + 1.0)
                    + second_value * (-2.0 * cubed + 3.0 * squared)
                    + float(first.backward) * (cubed - 2.0 * squared + amount)
                    + float(second.forward) * (cubed - squared)
                )
            raise ValueError(
                f"Actor XYZ rotation uses unsupported scalar interpolation: {interpolation}"
            )
    raise ValueError("Actor XYZ rotation key interval was not found")


def _converted_xyz_rotation(
    groups: object,
    time_value: float,
) -> tuple[float, float, float, float]:
    channels = list(groups)
    if len(channels) != 3:
        raise ValueError(f"Actor XYZ rotation must contain three channels, found {len(channels)}")
    x, y, z = (_scalar_keys(channel, time_value) for channel in channels)
    sine_x, cosine_x = math.sin(x), math.cos(x)
    sine_y, cosine_y = math.sin(y), math.cos(y)
    sine_z, cosine_z = math.sin(z), math.cos(z)
    game_rotation = [
        [cosine_y * cosine_z, -cosine_y * sine_z, sine_y],
        [
            sine_x * sine_y * cosine_z + sine_z * cosine_x,
            cosine_x * cosine_z - sine_x * sine_y * sine_z,
            -sine_x * cosine_y,
        ],
        [
            sine_x * sine_z - cosine_x * sine_y * cosine_z,
            cosine_x * sine_y * sine_z + sine_x * cosine_z,
            cosine_x * cosine_y,
        ],
    ]
    return tuple(_quaternion(_converted_rotation(game_rotation)))


def _slerp_keys(keys: list[object], time_value: float) -> tuple[float, float, float, float]:
    if time_value <= float(keys[0].time):
        return _nif_quaternion(keys[0].value)
    if time_value >= float(keys[-1].time):
        return _nif_quaternion(keys[-1].value)
    for first, second in zip(keys, keys[1:]):
        first_time, second_time = float(first.time), float(second.time)
        if first_time <= time_value <= second_time:
            amount = (time_value - first_time) / (second_time - first_time)
            return _slerp(_nif_quaternion(first.value), _nif_quaternion(second.value), amount)
    raise ValueError("Actor rotation key interval was not found")


def _scalar_keys(group: object, time_value: float) -> float:
    keys = list(group.keys)
    if not keys:
        raise ValueError("Actor scalar animation key group is empty")
    if time_value <= float(keys[0].time):
        return float(keys[0].value)
    if time_value >= float(keys[-1].time):
        return float(keys[-1].value)
    for first, second in zip(keys, keys[1:]):
        first_time, second_time = float(first.time), float(second.time)
        if first_time <= time_value <= second_time:
            amount = (time_value - first_time) / (second_time - first_time)
            first_value, second_value = float(first.value), float(second.value)
            if int(group.interpolation) == 2:
                duration = second_time - first_time
                first_tangent = float(first.forward)
                second_tangent = float(second.backward)
                squared = amount * amount
                cubed = squared * amount
                return (
                    (2.0 * cubed - 3.0 * squared + 1.0) * first_value
                    + (cubed - 2.0 * squared + amount) * duration * first_tangent
                    + (-2.0 * cubed + 3.0 * squared) * second_value
                    + (cubed - squared) * duration * second_tangent
                )
            return first_value + amount * (second_value - first_value)
    raise ValueError("Actor scalar key interval was not found")


def _euler_xyz_quaternion(
    angles: tuple[float, float, float],
) -> tuple[float, float, float, float]:
    half_x, half_y, half_z = (angle / 2.0 for angle in angles)
    cx, cy, cz = math.cos(half_x), math.cos(half_y), math.cos(half_z)
    sx, sy, sz = math.sin(half_x), math.sin(half_y), math.sin(half_z)
    return _normalize_quaternion(
        (
            cx * cy * cz - sx * sy * sz,
            sx * cy * cz + cx * sy * sz,
            cx * sy * cz - sx * cy * sz,
            cx * cy * sz + sx * sy * cz,
        )
    )


def _slerp(
    first: tuple[float, float, float, float],
    second: tuple[float, float, float, float],
    amount: float,
) -> tuple[float, float, float, float]:
    dot = sum(one * two for one, two in zip(first, second))
    if dot < 0.0:
        second = tuple(-value for value in second)
        dot = -dot
    if dot > SLERP_LINEAR_DOT_THRESHOLD:
        return _normalize_quaternion(tuple(first[index] + amount * (second[index] - first[index]) for index in range(4)))
    angle = math.acos(max(-1.0, min(1.0, dot)))
    sine = math.sin(angle)
    one_weight = math.sin((1.0 - amount) * angle) / sine
    two_weight = math.sin(amount * angle) / sine
    return tuple(one_weight * first[index] + two_weight * second[index] for index in range(4))


def _converted_nif_quaternion(value: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
    w, x, y, z = value
    row = [
        [1.0 - 2.0 * (y * y + z * z), 2.0 * (x * y + z * w), 2.0 * (x * z - y * w)],
        [2.0 * (x * y - z * w), 1.0 - 2.0 * (x * x + z * z), 2.0 * (y * z + x * w)],
        [2.0 * (x * z + y * w), 2.0 * (y * z - x * w), 1.0 - 2.0 * (x * x + y * y)],
    ]
    return tuple(_quaternion(_converted_rotation(row)))


def _nif_vector(value: object) -> tuple[float, float, float]:
    return float(value.x), float(value.y), float(value.z)


def _nif_quaternion(value: object) -> tuple[float, float, float, float]:
    return float(value.w), float(value.x), float(value.y), float(value.z)


def _normalize_quaternion(value: tuple[float, ...]) -> tuple[float, float, float, float]:
    length = math.sqrt(sum(component * component for component in value))
    if length <= NORMALIZATION_EPSILON:
        raise ValueError("Actor animation contains a zero quaternion")
    return tuple(component / length for component in value)


def _continuous_quaternions(
    values: list[tuple[float, float, float, float]],
) -> list[tuple[float, float, float, float]]:
    result = [values[0]]
    for value in values[1:]:
        if sum(one * two for one, two in zip(result[-1], value)) < 0.0:
            value = tuple(-component for component in value)
        result.append(value)
    return result


def _read_nif(payload: bytes) -> object:
    document = decode_nif(payload).document
    if len(document.roots) != 1:
        raise ValueError(f"Actor NIF must have one root, found {len(document.roots)}")
    return document


def _named_node(document: object, name: str) -> object:
    matches = [
        node for node in document.get_global_iterator() if isinstance(node, NifFormat.NiNode) and _text(node.name) == name
    ]
    if len(matches) != 1:
        raise ValueError(f"Expected one actor skeleton node {name!r}, found {len(matches)}")
    return matches[0]


def _node_trs(node: object) -> tuple[list[float], list[float], list[float]]:
    translation = _convert_vector((float(node.translation.x), float(node.translation.y), float(node.translation.z)))
    rotation = _converted_rotation(
        [
            [float(node.rotation.m_11), float(node.rotation.m_12), float(node.rotation.m_13)],
            [float(node.rotation.m_21), float(node.rotation.m_22), float(node.rotation.m_23)],
            [float(node.rotation.m_31), float(node.rotation.m_32), float(node.rotation.m_33)],
        ]
    )
    scale = float(node.scale)
    return list(translation), _quaternion(rotation), [scale, scale, scale]


def _convert_vector(value: tuple[float, float, float]) -> tuple[float, float, float]:
    return value[0], value[2], -value[1]


def _convert_direction(value: tuple[float, float, float]) -> tuple[float, float, float]:
    converted = _convert_vector(value)
    length = math.sqrt(sum(axis * axis for axis in converted))
    if length <= NORMALIZATION_EPSILON:
        raise ValueError("Actor NIF contains a zero-length normal")
    return tuple(axis / length for axis in converted)


def _transform_position(value: tuple[float, float, float], matrix: object) -> tuple[float, float, float]:
    x, y, z = value
    return _convert_vector(
        (
            x * matrix.m_11 + y * matrix.m_21 + z * matrix.m_31 + matrix.m_41,
            x * matrix.m_12 + y * matrix.m_22 + z * matrix.m_32 + matrix.m_42,
            x * matrix.m_13 + y * matrix.m_23 + z * matrix.m_33 + matrix.m_43,
        )
    )


def _transform_direction(value: tuple[float, float, float], matrix: object) -> tuple[float, float, float]:
    x, y, z = value
    return _convert_direction(
        (
            x * matrix.m_11 + y * matrix.m_21 + z * matrix.m_31,
            x * matrix.m_12 + y * matrix.m_22 + z * matrix.m_32,
            x * matrix.m_13 + y * matrix.m_23 + z * matrix.m_33,
        )
    )


def _transform_delta(value: tuple[float, float, float], matrix: object) -> tuple[float, float, float]:
    x, y, z = value
    return _convert_vector(
        (
            x * matrix.m_11 + y * matrix.m_21 + z * matrix.m_31,
            x * matrix.m_12 + y * matrix.m_22 + z * matrix.m_32,
            x * matrix.m_13 + y * matrix.m_23 + z * matrix.m_33,
        )
    )


def _recompute_normals(
    positions: list[tuple[float, float, float]],
    triangles: list[tuple[int, int, int]],
) -> list[tuple[float, float, float]]:
    rows = [[0.0, 0.0, 0.0] for _ in positions]
    for first, second, third in triangles:
        one = tuple(positions[second][axis] - positions[first][axis] for axis in range(3))
        two = tuple(positions[third][axis] - positions[first][axis] for axis in range(3))
        normal = (
            one[1] * two[2] - one[2] * two[1],
            one[2] * two[0] - one[0] * two[2],
            one[0] * two[1] - one[1] * two[0],
        )
        for index in (first, second, third):
            for axis in range(3):
                rows[index][axis] += normal[axis]
    result = []
    for row in rows:
        length = math.sqrt(sum(value * value for value in row))
        if length <= NORMALIZATION_EPSILON:
            raise ValueError("Actor morph produced an isolated or degenerate vertex normal")
        result.append(tuple(value / length for value in row))
    return result


def _converted_matrix(value: object) -> list[list[float]]:
    row = [
        [float(value.m_11), float(value.m_12), float(value.m_13), float(value.m_14)],
        [float(value.m_21), float(value.m_22), float(value.m_23), float(value.m_24)],
        [float(value.m_31), float(value.m_32), float(value.m_33), float(value.m_34)],
        [float(value.m_41), float(value.m_42), float(value.m_43), float(value.m_44)],
    ]
    column = [[row[column_index][row_index] for column_index in range(4)] for row_index in range(4)]
    conversion = [
        [1.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 1.0, 0.0],
        [0.0, -1.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 1.0],
    ]
    inverse = [[conversion[column_index][row_index] for column_index in range(4)] for row_index in range(4)]
    return _multiply(conversion, _multiply(column, inverse))


def _trs_matrix(node: dict[str, object]) -> list[list[float]]:
    translation = [float(value) for value in node.get("translation", [0.0, 0.0, 0.0])]
    rotation = [float(value) for value in node.get("rotation", [0.0, 0.0, 0.0, 1.0])]
    scale = [float(value) for value in node.get("scale", [1.0, 1.0, 1.0])]
    if len(translation) != 3 or len(rotation) != 4 or len(scale) != 3:
        raise ValueError(f"Actor glTF node has invalid TRS: {node.get('name')}")
    x, y, z, w = rotation
    length = math.sqrt(x * x + y * y + z * z + w * w)
    if length <= ACTOR_GLTF_DIAGNOSTIC_CONTRACT_FLOAT_1POINT0ENEGATIVE12 or min(scale) <= 0.0:
        raise ValueError(f"Actor glTF node has non-invertible TRS: {node.get('name')}")
    x, y, z, w = (value / length for value in (x, y, z, w))
    matrix = [
        [1.0 - 2.0 * (y * y + z * z), 2.0 * (x * y - z * w), 2.0 * (x * z + y * w), translation[0]],
        [2.0 * (x * y + z * w), 1.0 - 2.0 * (x * x + z * z), 2.0 * (y * z - x * w), translation[1]],
        [2.0 * (x * z - y * w), 2.0 * (y * z + x * w), 1.0 - 2.0 * (x * x + y * y), translation[2]],
        [0.0, 0.0, 0.0, 1.0],
    ]
    for row in range(3):
        for column in range(3):
            matrix[row][column] *= scale[column]
    return matrix


def _inverse_matrix(matrix: list[list[float]]) -> list[list[float]]:
    if len(matrix) != 4 or any(len(row) != 4 for row in matrix):
        raise ValueError("Actor inverse-bind matrix must be 4x4")
    augmented = [
        [float(value) for value in matrix[row]]
        + [1.0 if row == column else 0.0 for column in range(4)]
        for row in range(4)
    ]
    for column in range(4):
        pivot = max(range(column, 4), key=lambda row: abs(augmented[row][column]))
        if abs(augmented[pivot][column]) <= ACTOR_GLTF_DIAGNOSTIC_CONTRACT_FLOAT_1POINT0ENEGATIVE12:
            raise ValueError("Actor skeleton rest matrix is singular")
        augmented[column], augmented[pivot] = augmented[pivot], augmented[column]
        divisor = augmented[column][column]
        augmented[column] = [value / divisor for value in augmented[column]]
        for row in range(4):
            if row == column:
                continue
            factor = augmented[row][column]
            augmented[row] = [
                augmented[row][index] - factor * augmented[column][index]
                for index in range(ACTOR_GLTF_DIAGNOSTIC_CONTRACT_INTEGER_8)
            ]
    return [row[4:] for row in augmented]


def gltf_skeleton_inverse_binds(
    nodes: list[dict[str, object]],
    node_by_name: dict[str, int],
) -> dict[str, list[list[float]]]:
    parents = {
        int(child): parent
        for parent, node in enumerate(nodes)
        for child in node.get("children", [])
    }
    globals_by_index: dict[int, list[list[float]]] = {}

    def global_matrix(index: int) -> list[list[float]]:
        if index in globals_by_index:
            return globals_by_index[index]
        local = _trs_matrix(nodes[index])
        result = (
            _multiply(global_matrix(parents[index]), local)
            if index in parents
            else local
        )
        globals_by_index[index] = result
        return result

    result = {
        name: _inverse_matrix(global_matrix(index))
        for name, index in node_by_name.items()
    }
    worst = max(
        max(
            abs(value - (1.0 if row == column else 0.0))
            for row, values in enumerate(_multiply(global_matrix(node_by_name[name]), inverse))
            for column, value in enumerate(values)
        )
        for name, inverse in result.items()
    )
    if worst > ACTOR_GLTF_DIAGNOSTIC_CONTRACT_FLOAT_1POINT0ENEGATIVE5:
        raise ValueError(f"Actor inverse-bind rest residual is too large: {worst}")
    return result


def _compensated_inverse_bind(
    inverse_bind: object,
    baked_shape_transform: object | None,
) -> list[list[float]]:
    converted = _converted_matrix(inverse_bind)
    if baked_shape_transform is None:
        return converted
    shape_inverse = _converted_matrix(baked_shape_transform.get_inverse(fast=False))
    return _multiply(converted, shape_inverse)


def _converted_rotation(value: list[list[float]]) -> list[list[float]]:
    column = [[value[column_index][row_index] for column_index in range(3)] for row_index in range(3)]
    conversion = [[1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, -1.0, 0.0]]
    inverse = [[conversion[column][row] for column in range(3)] for row in range(3)]
    return _multiply(conversion, _multiply(column, inverse))


def _multiply(left: list[list[float]], right: list[list[float]]) -> list[list[float]]:
    return [
        [sum(left[row][axis] * right[axis][column] for axis in range(len(right))) for column in range(len(right[0]))]
        for row in range(len(left))
    ]


def _quaternion(matrix: list[list[float]]) -> list[float]:
    trace = matrix[0][0] + matrix[1][1] + matrix[2][2]
    if trace > 0.0:
        scale = math.sqrt(trace + 1.0) * 2.0
        result = [
            (matrix[2][1] - matrix[1][2]) / scale,
            (matrix[0][2] - matrix[2][0]) / scale,
            (matrix[1][0] - matrix[0][1]) / scale,
            QUATERNION_DIAGONAL_COEFFICIENT * scale,
        ]
    else:
        axis = max(range(3), key=lambda index: matrix[index][index])
        following = (axis + 1) % 3
        remaining = (axis + 2) % 3
        scale = math.sqrt(1.0 + matrix[axis][axis] - matrix[following][following] - matrix[remaining][remaining]) * 2.0
        result = [0.0, 0.0, 0.0, 0.0]
        result[axis] = QUATERNION_DIAGONAL_COEFFICIENT * scale
        result[3] = (matrix[remaining][following] - matrix[following][remaining]) / scale
        result[following] = (matrix[following][axis] + matrix[axis][following]) / scale
        result[remaining] = (matrix[remaining][axis] + matrix[axis][remaining]) / scale
    length = math.sqrt(sum(value * value for value in result))
    return [value / length for value in result]


def _gltf_matrix(matrix: list[list[float]]) -> tuple[float, ...]:
    return tuple(matrix[row][column] for column in range(4) for row in range(4))


def _text(value: object) -> str:
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    try:
        return bytes(value).decode("utf-8", errors="replace")
    except (TypeError, ValueError):
        return str(value)


def _atomic_write(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)
