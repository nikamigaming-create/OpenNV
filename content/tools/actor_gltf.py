"""Export a directly resolved Fallout humanoid assembly to a skinned glTF."""

from __future__ import annotations

import hashlib
import io
import json
import math
import os
import struct
import time
from dataclasses import dataclass
from pathlib import Path

if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from PIL import Image
from pyffi.formats.nif import NifFormat  # type: ignore  # noqa: E402

from bsa_archive import BsaArchive, canonical_member_path
from export_static_nif_gltf import BufferBuilder, generate_tangents, pack_floats
from facegen import apply_geometry_morphs, repair_facegen_nif_uv_flag
from texture_pipeline import decode_dds
from actor_material import (
    actor_vertex_colors_enabled,
    build_actor_material as _material,
)


ACTOR_GLTF_SCHEMA = "opennv-actor-gltf/v1"


@dataclass(frozen=True)
class ActorComponent:
    role: str
    model_path: str
    model_payload: bytes
    egm_path: str | None = None
    egm_payload: bytes | None = None
    rigid_to_head: bool = False
    bake_shape_transform: bool = False
    selected_shape: str | None = None
    excluded_shape_prefixes: tuple[str, ...] = ()
    diffuse_override: str | None = None
    generated_diffuse: Image.Image | None = None
    generated_diffuse_by_source: tuple[tuple[str, Image.Image], ...] = ()
    tint_rgb: tuple[float, float, float] | None = None
    diffuse_aliases: tuple[tuple[str, str], ...] = ()
    repair_facegen: bool = False
    egm_vertex_offset: int = 0


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
    additional_animations: tuple["ActorAnimation", ...] = ()


@dataclass(frozen=True)
class ActorAnimation:
    logical_path: str
    payload: bytes


class TextureLibrary:
    def __init__(self, archives: list[BsaArchive], output_root: Path, gltf_path: Path):
        self.archives = archives
        self.output_root = output_root
        self.gltf_path = gltf_path
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
            normal_green_inverted=False,
            key=key,
        )

    def _store(
        self,
        identity: str,
        image: Image.Image,
        *,
        source_sha256: str,
        normal_green_inverted: bool,
        key: tuple[str, bool],
    ) -> int:
        asset_id = hashlib.sha256(f"{identity}:{normal_green_inverted}".encode()).hexdigest()[:20]
        path = self.output_root / "textures" / f"{asset_id}.png"
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_name(path.name + ".tmp")
        image.convert("RGBA").save(temporary, format="PNG", optimize=True, compress_level=9)
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
) -> dict[str, object]:
    gltf_path.parent.mkdir(parents=True, exist_ok=True)
    skeleton = _read_nif(source.skeleton_payload)
    skeleton_root = _named_node(skeleton, "Bip01")
    nodes: list[dict[str, object]] = [{"name": f"ACTOR_{source.actor_form_id}_{source.actor_name}", "children": []}]
    node_by_name: dict[str, int] = {}
    _append_skeleton_nodes(skeleton_root, 0, nodes, node_by_name)
    inverse_bind_by_name = gltf_skeleton_inverse_binds(nodes, node_by_name)
    builder = BufferBuilder()
    meshes: list[dict[str, object]] = []
    skins: list[dict[str, object]] = []
    materials: list[dict[str, object]] = []
    surfaces: list[dict[str, object]] = []
    textures = TextureLibrary(texture_archives, gltf_path.parent, gltf_path)

    for component in source.components:
        payload = component.model_payload
        repairs: tuple[int, ...] = ()
        if component.repair_facegen:
            repaired = repair_facegen_nif_uv_flag(payload)
            payload = repaired.payload
            repairs = repaired.uv_flag_offsets
        document = _read_nif(payload)
        component_root = document.roots[0]
        shapes = [
            shape
            for shape in document.get_global_iterator()
            if isinstance(shape, (NifFormat.NiTriShape, NifFormat.NiTriStrips)) and shape.data is not None
        ]
        if component.selected_shape is not None:
            shapes = [shape for shape in shapes if _text(shape.name) == component.selected_shape]
        shapes = [
            shape
            for shape in shapes
            if not any(_text(shape.name).startswith(prefix) for prefix in component.excluded_shape_prefixes)
        ]
        if not shapes:
            raise ValueError(f"Actor component {component.role} selected no shapes from {component.model_path}")
        for shape in shapes:
            mesh_index, skin_index, surface = _append_shape(
                source,
                component,
                component_root,
                shape,
                node_by_name,
                inverse_bind_by_name,
                builder,
                meshes,
                skins,
                materials,
                textures,
            )
            node: dict[str, object] = {
                "name": f"{component.role}_{_text(shape.name)}",
                "mesh": mesh_index,
            }
            if skin_index is not None:
                node["skin"] = skin_index
                parent = 0
            else:
                if "HeadAnims" not in node_by_name:
                    raise ValueError(
                        "Actor rigid head component requires a HeadAnims skeleton node"
                    )
                parent = node_by_name["HeadAnims"]
            node_index = len(nodes)
            nodes.append(node)
            nodes[parent].setdefault("children", []).append(node_index)
            surface["node"] = node_index
            surface["faceGenUvFlagRepairs"] = list(repairs)
            surfaces.append(surface)

    animation_inputs = (
        ActorAnimation(source.idle_animation_path, source.idle_animation_payload),
        *source.additional_animations,
    )
    animation_rows = []
    animation_sidecars = []
    animation_names: set[str] = set()
    nonaccum_origin = None
    for animation_input in animation_inputs:
        animation, channels, animation_origin = _build_animation(
            animation_input.payload,
            node_by_name,
            nodes,
            builder,
        )
        if animation is None:
            raise ValueError(
                f"Actor animation selected no supported channels: {animation_input.logical_path}"
            )
        name = str(animation["name"])
        if name in animation_names:
            raise ValueError(f"Actor animation name is not unique: {name}")
        animation_names.add(name)
        animation_rows.append(animation)
        animation_sidecars.append(
            {
                "logicalPath": animation_input.logical_path,
                "sha256": hashlib.sha256(animation_input.payload).hexdigest(),
                "name": name,
                "channels": channels,
                "nonAccumOriginGodotUnits": (
                    list(animation_origin) if animation_origin else None
                ),
            }
        )
        if nonaccum_origin is None:
            nonaccum_origin = animation_origin

    binary_path = gltf_path.with_suffix(".bin")
    gltf: dict[str, object] = {
        "asset": {"version": "2.0", "generator": "OpenNV direct actor exporter v1"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": nodes,
        "meshes": meshes,
        "skins": skins,
        "materials": materials,
        "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}],
        "images": textures.images,
        "textures": textures.textures,
        "buffers": [{"uri": binary_path.name, "byteLength": len(builder.data)}],
        "bufferViews": builder.views,
        "accessors": builder.accessors,
        "extras": {"openNvSchema": ACTOR_GLTF_SCHEMA, "actorFormId": source.actor_form_id},
    }
    gltf["animations"] = animation_rows
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
        },
        "animation": {
            "logicalPath": source.idle_animation_path,
            "sha256": hashlib.sha256(source.idle_animation_payload).hexdigest(),
            "name": animation_sidecars[0]["name"],
            "channels": animation_sidecars[0]["channels"],
            "nonAccumOriginGodotUnits": list(nonaccum_origin) if nonaccum_origin else None,
        },
        "animations": animation_sidecars,
        "outputs": {
            "gltf": {"file": gltf_path.name, "sha256": hashlib.sha256(gltf_bytes).hexdigest()},
            "buffer": {"file": binary_path.name, "sha256": hashlib.sha256(binary_bytes).hexdigest()},
        },
        "coverage": {
            "components": len(source.components),
            "surfaces": len(surfaces),
            "skins": len(skins),
            "inverseBindContract": "inverse of exact emitted glTF skeleton rest-global matrix",
            "textures": len(textures.rows),
            "animations": len(animation_rows),
            "animated": bool(animation_rows),
        },
        "surfaces": surfaces,
        "textures": textures.rows,
    }
    sidecar_bytes = (json.dumps(sidecar, indent=2, sort_keys=True) + "\n").encode()
    _atomic_write(sidecar_path, sidecar_bytes)
    return sidecar


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


def _append_shape(
    source: ActorGltfInput,
    component: ActorComponent,
    component_root: object,
    shape: object,
    node_by_name: dict[str, int],
    inverse_bind_by_name: dict[str, list[list[float]]],
    builder: BufferBuilder,
    meshes: list[dict[str, object]],
    skins: list[dict[str, object]],
    materials: list[dict[str, object]],
    textures: TextureLibrary,
) -> tuple[int, int | None, dict[str, object]]:
    mesh = shape.data
    vertex_count = len(mesh.vertices)
    if vertex_count == 0 or not mesh.uv_sets:
        raise ValueError(f"Actor shape lacks vertices or UVs: {_text(shape.name)}")
    raw_positions = [(float(value.x), float(value.y), float(value.z)) for value in mesh.vertices]
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
    transform_shape = component.rigid_to_head or component.bake_shape_transform
    positions = [
        _transform_position(position, shape_transform)
        if transform_shape else _convert_vector(position)
        for position in raw_positions
    ]
    triangles = [tuple(int(index) for index in triangle) for triangle in mesh.get_triangles()]
    if not triangles:
        raise ValueError(f"Actor shape has no triangles: {_text(shape.name)}")
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
    uvs = [(float(value.u), float(value.v)) for value in mesh.uv_sets[0]]
    tangents = generate_tangents(positions, normals, uvs, triangles)
    attributes: dict[str, int] = {
        "POSITION": builder.add(
            pack_floats(positions),
            component_type=5126,
            count=vertex_count,
            value_type="VEC3",
            target=34962,
            minimum=[min(row[axis] for row in positions) for axis in range(3)],
            maximum=[max(row[axis] for row in positions) for axis in range(3)],
        ),
        "NORMAL": builder.add(
            pack_floats(normals), component_type=5126, count=vertex_count, value_type="VEC3", target=34962
        ),
        "TANGENT": builder.add(
            pack_floats(tangents), component_type=5126, count=vertex_count, value_type="VEC4", target=34962
        ),
        "TEXCOORD_0": builder.add(
            pack_floats(uvs), component_type=5126, count=vertex_count, value_type="VEC2", target=34962
        ),
    }
    properties = list(getattr(shape, "properties", []))
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
            component_type=5126,
            count=vertex_count,
            value_type="VEC4",
            target=34962,
        )
    skin_index: int | None = None
    if not component.rigid_to_head:
        skin_index = _append_skin(
            component.role,
            shape,
            node_by_name,
            inverse_bind_by_name,
            builder,
            attributes,
            skins,
            shape_transform if component.bake_shape_transform else None,
        )
    indices = [value for triangle in triangles for value in triangle]
    component_type = 5123 if vertex_count <= 65535 else 5125
    value_format = "H" if component_type == 5123 else "I"
    index_accessor = builder.add(
        struct.pack(f"<{len(indices)}{value_format}", *indices),
        component_type=component_type,
        count=len(indices),
        value_type="SCALAR",
        target=34963,
    )
    material_index = len(materials)
    material, material_row = _material(component, shape, textures)
    materials.append(material)
    mesh_index = len(meshes)
    meshes.append(
        {
            "name": f"{component.role}_{_text(shape.name)}",
            "primitives": [{"attributes": attributes, "indices": index_accessor, "material": material_index}],
        }
    )
    return mesh_index, skin_index, {
        "role": component.role,
        "modelPath": component.model_path,
        "modelSha256": hashlib.sha256(component.model_payload).hexdigest(),
        "egmPath": component.egm_path,
        "egmSha256": hashlib.sha256(component.egm_payload).hexdigest() if component.egm_payload else None,
        "shape": _text(shape.name),
        "vertices": vertex_count,
        "triangles": len(triangles),
        "morphed": morphed,
        "skinned": skin_index is not None,
        "skinShapeTransformCompensated": (
            skin_index is not None and component.bake_shape_transform
        ),
        "vertexColorsEnabled": vertex_colors_enabled,
        "material": material_row,
    }


def _append_skin(
    role: str,
    shape: object,
    node_by_name: dict[str, int],
    inverse_bind_by_name: dict[str, list[list[float]]],
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
    weights = shape.get_vertex_weights()
    joint_rows = []
    weight_rows = []
    for vertex_weights in weights:
        selected = sorted(vertex_weights, key=lambda item: float(item[1]), reverse=True)[:4]
        total = sum(float(item[1]) for item in selected)
        if total <= 0.0:
            raise ValueError(f"Actor skin has an unweighted vertex: {_text(shape.name)}")
        joints = [int(item[0]) for item in selected]
        values = [float(item[1]) / total for item in selected]
        joint_rows.append(tuple(joints + [0] * (4 - len(joints))))
        weight_rows.append(tuple(values + [0.0] * (4 - len(values))))
    joint_payload = struct.pack(
        f"<{len(joint_rows) * 4}H", *(value for row in joint_rows for value in row)
    )
    attributes["JOINTS_0"] = builder.add(
        joint_payload, component_type=5123, count=len(joint_rows), value_type="VEC4", target=34962
    )
    attributes["WEIGHTS_0"] = builder.add(
        pack_floats(weight_rows), component_type=5126, count=len(weight_rows), value_type="VEC4", target=34962
    )
    inverse_bind_rows = []
    if len(instance.data.bone_list) != len(bone_names):
        raise ValueError(f"Actor skin bone data count drift: {_text(shape.name)}")
    for name in bone_names:
        inverse_bind_rows.append(_gltf_matrix(inverse_bind_by_name[name]))
    inverse_bind = builder.add(
        pack_floats(inverse_bind_rows),
        component_type=5126,
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


def _build_animation(
    payload: bytes,
    node_by_name: dict[str, int],
    nodes: list[dict[str, object]],
    builder: BufferBuilder,
) -> tuple[dict[str, object] | None, int, tuple[float, float, float] | None]:
    document = _read_nif(payload)
    sequence = document.roots[0]
    if not isinstance(sequence, NifFormat.NiControllerSequence):
        raise ValueError(f"Actor idle root is not NiControllerSequence: {type(sequence).__name__}")
    start = float(sequence.start_time)
    stop = float(sequence.stop_time)
    if start != 0.0 or stop <= start:
        raise ValueError(f"Actor idle has an unexpected time range: {start}..{stop}")
    frame_count = round((stop - start) * 30.0) + 1
    times = [start + frame / 30.0 for frame in range(frame_count)]
    times[-1] = stop
    time_accessor = builder.add(
        struct.pack(f"<{len(times)}f", *times),
        component_type=5126,
        count=len(times),
        value_type="SCALAR",
        target=None,
        minimum=[start],
        maximum=[stop],
    )
    samplers = []
    channels = []
    nonaccum_origin = None
    for controlled in sequence.controlled_blocks:
        node_name = _text(controlled.get_node_name())
        if node_name not in node_by_name or _text(controlled.get_controller_type()) != "NiTransformController":
            continue
        interpolator = controlled.interpolator
        translations: list[tuple[float, float, float]] = []
        rotations: list[tuple[float, float, float, float]] = []
        if isinstance(interpolator, NifFormat.NiBSplineCompTransformInterpolator):
            translation_control = list(interpolator.get_translations())
            rotation_control = list(interpolator.get_rotations())
            if translation_control:
                translations = [
                    _convert_vector(_uniform_cubic(translation_control, time, start, stop)) for time in times
                ]
            if rotation_control:
                rotations = [
                    _converted_nif_quaternion(_normalize_quaternion(_uniform_cubic(rotation_control, time, start, stop)))
                    for time in times
                ]
        elif isinstance(interpolator, NifFormat.NiTransformInterpolator):
            data = interpolator.data
            if data is not None:
                translation_keys = list(data.translations.keys)
                if translation_keys:
                    if int(data.translations.interpolation) != 1:
                        raise ValueError(f"Actor idle uses unsupported translation interpolation on {node_name}")
                    translations = [
                        _convert_vector(_linear_vector_keys(translation_keys, time)) for time in times
                    ]
                if int(data.num_rotation_keys) > 0:
                    if int(data.rotation_type) in (1, 2, 3):
                        quaternion_keys = list(data.quaternion_keys)
                        rotations = [
                            _converted_nif_quaternion(_slerp_keys(quaternion_keys, time)) for time in times
                        ]
                    elif int(data.rotation_type) == 4:
                        groups = list(data.xyz_rotations)
                        if len(groups) != 3 or any(len(group.keys) == 0 for group in groups):
                            raise ValueError(
                                f"Actor animation has incomplete XYZ rotations on {node_name}"
                            )
                        rotations = [
                            _converted_nif_quaternion(
                                _euler_xyz_quaternion(
                                    tuple(_scalar_keys(group, time) for group in groups)
                                )
                            )
                            for time in times
                        ]
                    else:
                        raise ValueError(
                            f"Actor animation uses unsupported rotation interpolation "
                            f"{int(data.rotation_type)} on {node_name}"
                        )
        else:
            raise ValueError(f"Actor idle uses unsupported transform interpolator: {type(interpolator).__name__}")

        if translations:
            if node_name == "Bip01 NonAccum":
                nonaccum_origin = translations[0]
            translations = actor_animation_translations(
                node_name,
                translations,
                tuple(float(value) for value in nodes[node_by_name[node_name]].get(
                    "translation",
                    [0.0, 0.0, 0.0],
                )),
            )
            output = builder.add(
                pack_floats(translations),
                component_type=5126,
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
                component_type=5126,
                count=len(rotations),
                value_type="VEC4",
                target=None,
            )
            sampler = len(samplers)
            samplers.append({"input": time_accessor, "output": output, "interpolation": "LINEAR"})
            channels.append({"sampler": sampler, "target": {"node": node_by_name[node_name], "path": "rotation"}})
    if not channels:
        return None, 0, nonaccum_origin
    return (
        {"name": _text(sequence.name), "samplers": samplers, "channels": channels},
        len(channels),
        nonaccum_origin,
    )


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
        inverse**3 / 6.0,
        (3.0 * value**3 - 6.0 * value**2 + 4.0) / 6.0,
        (-3.0 * value**3 + 3.0 * value**2 + 3.0 * value + 1.0) / 6.0,
        value**3 / 6.0,
    )
    return tuple(
        sum(basis[index] * float(control_points[segment + index][axis]) for index in range(4))
        for axis in range(len(control_points[0]))
    )


def actor_animation_translations(
    node_name: str,
    values: list[tuple[float, float, float]],
    rest_translation: tuple[float, float, float] | None = None,
) -> list[tuple[float, float, float]]:
    if not values:
        return values
    if node_name == "Bip01 NonAccum":
        origin = values[0]
        return [tuple(value[axis] - origin[axis] for axis in range(3)) for value in values]
    if node_name == "Bip01" and rest_translation is not None:
        return [
            tuple(value[axis] + rest_translation[axis] for axis in range(3))
            for value in values
        ]
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
    if dot > 0.9995:
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
    if length <= 1.0e-12:
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
    document = NifFormat.Data()
    document.read(io.BytesIO(payload))
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
    if length <= 1.0e-12:
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
        if length <= 1.0e-12:
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
    if length <= 1.0e-12 or min(scale) <= 0.0:
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
        if abs(augmented[pivot][column]) <= 1.0e-12:
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
                for index in range(8)
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
    if worst > 1.0e-5:
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
            0.25 * scale,
        ]
    else:
        axis = max(range(3), key=lambda index: matrix[index][index])
        following = (axis + 1) % 3
        remaining = (axis + 2) % 3
        scale = math.sqrt(1.0 + matrix[axis][axis] - matrix[following][following] - matrix[remaining][remaining]) * 2.0
        result = [0.0, 0.0, 0.0, 0.0]
        result[axis] = 0.25 * scale
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
