"""Export one decoded LAND cell to verified glTF and a baked diffuse contract."""

from __future__ import annotations

import hashlib
import json
import os
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Protocol

from PIL import Image, ImageChops

from gltf_io import (
    GL_ARRAY_BUFFER,
    GL_ELEMENT_ARRAY_BUFFER,
    GL_FLOAT,
    GL_UNSIGNED_SHORT,
    BufferBuilder,
    atomic_write,
    compiler_sources_sha256,
    pack_floats,
    sha256_bytes,
)
from landscape_catalog import (
    CONFIGURED_MISSING_BASE_SOURCE,
    LAND_VERTEX_SIDE,
    Landscape,
    LandscapeIdentity,
    resolved_layer_texture_form_id,
)
from export_static_nif_gltf import generate_tangents
from runtime_configuration import ContentCompilerConfiguration
from texture_pipeline import TextureArtifact, TexturePipeline, file_sha256


SCHEMA = "opennv-landscape-gltf/v1"
GENERATOR = "OpenNV direct LAND exporter v1"
LANDSCAPE_MATERIAL_SCHEMA = "opennv-landscape-layer-material/v3"
LANDSCAPE_LIGHTING_MODEL = "retail-sls-land-weighted-ambient-directional-lambert"
LANDSCAPE_DIFFUSE_DOMAIN = "encoded"
LANDSCAPE_NORMAL_DECODE = "weighted-signed-rgb-normalize-once"
LANDSCAPE_LAYER_WEIGHT_OPERATION = (
    "float32-descending-atxt-sum-base-one-minus-sum-normalize-per-vertex"
)
LANDSCAPE_WEIGHT_INTERPOLATION = "per-vertex-linear"
LANDSCAPE_WEIGHT_STORAGE = "generated-17x17-rgba32f-vertex-lookup"
LANDSCAPE_RETAIL_WEIGHT_SEMANTICS = ("TEXCOORD1", "TEXCOORD2")
LANDSCAPE_RETAIL_WEIGHT_TYPE = "float4"
LANDSCAPE_CONTRACT_SOURCE = "matched-live-land-shader-package"
LANDSCAPE_COLLISION_FACE_SELECTION = "all-source-faces"
LAND_QUADRANT_VERTEX_SIDE = 17
LAND_QUADRANT_LAST_VERTEX = LAND_QUADRANT_VERTEX_SIDE - 1
BYTE_CHANNEL_MAXIMUM = 255.0
LAND_VERTEX_SPACING_GAME_UNITS = 128.0
LAND_QUADRANT_COUNT = 4
LAND_WEIGHT_MAP_CHANNELS = ("r", "g", "b", "a")
LAND_WEIGHTS_PER_MAP = len(LAND_WEIGHT_MAP_CHANNELS)
LAND_WEIGHT_MAP_ROLE = "vertex-weight-rgba32f"
LAND_SHADER_SAMPLER_BUDGET = 16
LAND_BASE_TEXTURE_SAMPLERS = 2
LAND_TEXTURE_SAMPLERS_PER_ALPHA_LAYER = 2


def landscape_weight_map_count(layer_count: int) -> int:
    if layer_count < 0:
        raise ValueError("LAND alpha layer count cannot be negative")
    return 0 if layer_count == 0 else (layer_count + 1 + LAND_WEIGHTS_PER_MAP - 1) // LAND_WEIGHTS_PER_MAP


LAND_MAX_ALPHA_LAYERS = max(
    layer_count
    for layer_count in range(LAND_SHADER_SAMPLER_BUDGET + 1)
    if (
        LAND_BASE_TEXTURE_SAMPLERS
        + layer_count * LAND_TEXTURE_SAMPLERS_PER_ALPHA_LAYER
        + landscape_weight_map_count(layer_count)
    )
    <= LAND_SHADER_SAMPLER_BUDGET
)
LAND_WEIGHT_VECTOR_COMPONENTS = (
    landscape_weight_map_count(LAND_MAX_ALPHA_LAYERS) * LAND_WEIGHTS_PER_MAP
)


@dataclass(frozen=True)
class LandscapeExport:
    asset: dict[str, object]
    runtime_textures: tuple[dict[str, object], ...]
    diagnostic_bake: dict[str, object]


class LandscapeTextureResolver(Protocol):
    def diffuse_path(self, texture_form_id: int) -> str: ...

    def texture_contract(self, texture_form_id: int) -> dict[str, object]: ...


def compiler_provenance() -> dict[str, str]:
    if getattr(sys, "frozen", False):
        executable = Path(sys.executable)
        return {
            "name": "OpenNV.Content packaged direct LAND exporter v1",
            "sha256": sha256_bytes(executable.read_bytes()),
        }
    root = Path(__file__).resolve().parent
    return {
        "name": GENERATOR,
        "sha256": compiler_sources_sha256(
            [
                Path(__file__),
                root / "gltf_io.py",
                root / "landscape_catalog.py",
                root / "export_static_nif_gltf.py",
            ]
        ),
    }


def canonical_texture_path(value: str) -> str:
    path = value.replace("/", "\\").lstrip("\\").lower()
    return path if path.startswith("textures\\") else f"textures\\{path}"


def _tiled_quadrant(
    source: Image.Image,
    compiler: ContentCompilerConfiguration,
) -> Image.Image:
    quadrant_pixels = compiler.landscape_quadrant_pixels
    tiles_per_quadrant = compiler.landscape_tiles_per_quadrant
    tile_size = quadrant_pixels // tiles_per_quadrant
    tile = source.convert("RGB").resize((tile_size, tile_size), Image.Resampling.LANCZOS)
    result = Image.new("RGB", (quadrant_pixels, quadrant_pixels))
    for y in range(tiles_per_quadrant):
        for x in range(tiles_per_quadrant):
            result.paste(tile, (x * tile_size, y * tile_size))
    return result


def _opacity_mask(
    rows: tuple[object, ...],
    compiler: ContentCompilerConfiguration,
) -> Image.Image:
    vertices = Image.new(
        "L",
        (LAND_QUADRANT_VERTEX_SIDE, LAND_QUADRANT_VERTEX_SIDE),
        0,
    )
    for row in rows:
        x = row.vertex_index % LAND_QUADRANT_VERTEX_SIDE
        y = row.vertex_index // LAND_QUADRANT_VERTEX_SIDE
        vertices.putpixel(
            (x, LAND_QUADRANT_LAST_VERTEX - y),
            round(row.opacity * BYTE_CHANNEL_MAXIMUM),
        )
    return vertices.resize(
        (compiler.landscape_quadrant_pixels, compiler.landscape_quadrant_pixels),
        Image.Resampling.BILINEAR,
    )


def bake_landscape_diffuse(
    landscape: Landscape,
    image_for_texture: Callable[[int], Image.Image],
    compiler: ContentCompilerConfiguration,
) -> Image.Image:
    quadrant_pixels = compiler.landscape_quadrant_pixels
    result = Image.new("RGB", (quadrant_pixels * 2, quadrant_pixels * 2))
    destinations = {
        0: (0, quadrant_pixels),
        1: (quadrant_pixels, quadrant_pixels),
        2: (0, 0),
        3: (quadrant_pixels, 0),
    }
    for quadrant in range(4):
        base = next(layer for layer in landscape.base_layers if layer.quadrant == quadrant)
        base_image = _tiled_quadrant(
            image_for_texture(resolved_layer_texture_form_id(landscape, base)),
            compiler,
        )
        weighted_layers = []
        accumulated_weight = Image.new("L", base_image.size, 0)
        for layer in sorted(
            (value for value in landscape.alpha_layers if value.quadrant == quadrant),
            key=lambda value: value.layer_index,
        ):
            overlay = _tiled_quadrant(
                image_for_texture(resolved_layer_texture_form_id(landscape, layer)),
                compiler,
            )
            weight = _opacity_mask(layer.opacities, compiler)
            weighted_layers.append((overlay, weight))
            accumulated_weight = ImageChops.add(accumulated_weight, weight)
        base_weight = ImageChops.invert(accumulated_weight)
        composite = ImageChops.multiply(
            base_image,
            Image.merge("RGB", (base_weight, base_weight, base_weight)),
        )
        for overlay, weight in weighted_layers:
            weighted = ImageChops.multiply(
                overlay,
                Image.merge("RGB", (weight, weight, weight)),
            )
            composite = ImageChops.add(composite, weighted)
        result.paste(composite, destinations[quadrant])
    return result


def landscape_geometry(
    landscape: Landscape,
    cell_coordinates: tuple[int, int],
    origin_game_units: tuple[float, float, float],
    exterior_cell_size_game_units: float,
) -> tuple[
    list[tuple[float, float, float]],
    list[tuple[float, float, float]],
    list[tuple[float, float]],
    list[tuple[float, float, float, float]],
    list[tuple[int, int, int]],
]:
    cell_x = cell_coordinates[0] * exterior_cell_size_game_units
    cell_y = cell_coordinates[1] * exterior_cell_size_game_units
    positions = []
    normals = []
    uvs = []
    for y in range(LAND_VERTEX_SIDE):
        for x in range(LAND_VERTEX_SIDE):
            index = y * LAND_VERTEX_SIDE + x
            game_x = cell_x + x * LAND_VERTEX_SPACING_GAME_UNITS
            game_y = cell_y + y * LAND_VERTEX_SPACING_GAME_UNITS
            game_z = landscape.heights[index]
            positions.append(
                (
                    game_x - origin_game_units[0],
                    game_z - origin_game_units[2],
                    -(game_y - origin_game_units[1]),
                )
            )
            normal_x, normal_y, normal_z = landscape.normals[index]
            normals.append((normal_x, normal_z, -normal_y))
            uvs.append((x / (LAND_VERTEX_SIDE - 1), y / (LAND_VERTEX_SIDE - 1)))
    triangles = []
    for y in range(LAND_VERTEX_SIDE - 1):
        for x in range(LAND_VERTEX_SIDE - 1):
            lower_left = y * LAND_VERTEX_SIDE + x
            lower_right = lower_left + 1
            upper_left = lower_left + LAND_VERTEX_SIDE
            upper_right = upper_left + 1
            triangles.extend(
                ((lower_left, lower_right, upper_left), (lower_right, upper_right, upper_left))
            )
    return positions, normals, uvs, list(landscape.colors), triangles


def landscape_quadrant_geometry(
    landscape: Landscape,
    cell_coordinates: tuple[int, int],
    origin_game_units: tuple[float, float, float],
    quadrant: int,
    exterior_cell_size_game_units: float,
) -> tuple[
    list[tuple[float, float, float]],
    list[tuple[float, float, float]],
    list[tuple[float, float]],
    list[tuple[float, float, float, float]],
    list[tuple[int, int, int]],
]:
    if quadrant < 0 or quadrant >= LAND_QUADRANT_COUNT:
        raise ValueError(f"LAND quadrant is outside the authored range: {quadrant}")
    quadrant_x = quadrant % 2
    quadrant_y = quadrant // 2
    vertex_offset_x = quadrant_x * LAND_QUADRANT_LAST_VERTEX
    vertex_offset_y = quadrant_y * LAND_QUADRANT_LAST_VERTEX
    cell_x = cell_coordinates[0] * exterior_cell_size_game_units
    cell_y = cell_coordinates[1] * exterior_cell_size_game_units
    positions = []
    normals = []
    uvs = []
    colors = []
    for local_y in range(LAND_QUADRANT_VERTEX_SIDE):
        source_y = vertex_offset_y + local_y
        for local_x in range(LAND_QUADRANT_VERTEX_SIDE):
            source_x = vertex_offset_x + local_x
            source_index = source_y * LAND_VERTEX_SIDE + source_x
            game_x = cell_x + source_x * LAND_VERTEX_SPACING_GAME_UNITS
            game_y = cell_y + source_y * LAND_VERTEX_SPACING_GAME_UNITS
            positions.append(
                (
                    game_x - origin_game_units[0],
                    landscape.heights[source_index] - origin_game_units[2],
                    -(game_y - origin_game_units[1]),
                )
            )
            normal_x, normal_y, normal_z = landscape.normals[source_index]
            normals.append((normal_x, normal_z, -normal_y))
            uvs.append(
                (
                    local_x / LAND_QUADRANT_LAST_VERTEX,
                    local_y / LAND_QUADRANT_LAST_VERTEX,
                )
            )
            colors.append(landscape.colors[source_index])
    triangles = []
    for y in range(LAND_QUADRANT_LAST_VERTEX):
        for x in range(LAND_QUADRANT_LAST_VERTEX):
            lower_left = y * LAND_QUADRANT_VERTEX_SIDE + x
            lower_right = lower_left + 1
            upper_left = lower_left + LAND_QUADRANT_VERTEX_SIDE
            upper_right = upper_left + 1
            triangles.extend(
                ((lower_left, lower_right, upper_left), (lower_right, upper_right, upper_left))
            )
    return positions, normals, uvs, colors, triangles


def _atomic_png(path: Path, image: Image.Image, compression_level: int) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    image.save(temporary, format="PNG", optimize=True, compress_level=compression_level)
    os.replace(temporary, path)


def _float32(value: float) -> float:
    return struct.unpack("<f", struct.pack("<f", value))[0]


def landscape_quadrant_vertex_weights(
    layers: list[object],
) -> list[tuple[float, float, float, float, float, float, float, float]]:
    """Reconstruct the two retail TEXCOORD float4 weight streams exactly."""
    if len(layers) > LAND_MAX_ALPHA_LAYERS:
        raise ValueError("LAND alpha layers exceed the retail shader contract")
    if [layer.layer_index for layer in layers] != sorted(
        layer.layer_index for layer in layers
    ):
        raise ValueError("LAND alpha layers must be ordered by layer index")

    alpha_rows = [
        [_float32(0.0) for _ in layers]
        for _ in range(LAND_QUADRANT_VERTEX_SIDE * LAND_QUADRANT_VERTEX_SIDE)
    ]
    for ordinal, layer in enumerate(layers):
        for row in layer.opacities:
            if row.vertex_index < 0 or row.vertex_index >= len(alpha_rows):
                raise ValueError(
                    f"LAND VTXT vertex index is outside its quadrant: {row.vertex_index}"
                )
            alpha_rows[row.vertex_index][ordinal] = _float32(row.opacity)

    result = []
    for alpha in alpha_rows:
        alpha_sum = _float32(0.0)
        for opacity in reversed(alpha):
            alpha_sum = _float32(alpha_sum + opacity)
        base = max(_float32(_float32(1.0) - alpha_sum), _float32(0.0))
        denominator = _float32(base + alpha_sum)
        if denominator <= 0.0:
            raise ValueError("LAND vertex weights have a zero normalization denominator")
        weights = [_float32(base / denominator)]
        weights.extend(_float32(opacity / denominator) for opacity in alpha)
        weights.extend(
            _float32(0.0)
            for _ in range(LAND_WEIGHT_VECTOR_COMPONENTS - len(weights))
        )
        result.append(tuple(weights))
    return result


def landscape_weight_map_payload(layers: list[object], map_index: int) -> bytes:
    map_count = landscape_weight_map_count(len(layers))
    if map_index < 0 or map_index >= map_count:
        raise ValueError("LAND weight-map index is outside its authored range")
    weights = landscape_quadrant_vertex_weights(layers)
    first_channel = map_index * LAND_WEIGHTS_PER_MAP
    payload = bytearray()
    for image_y in range(LAND_QUADRANT_VERTEX_SIDE):
        source_y = image_y
        for x in range(LAND_QUADRANT_VERTEX_SIDE):
            vertex_index = source_y * LAND_QUADRANT_VERTEX_SIDE + x
            payload.extend(
                struct.pack(
                    "<4f",
                    *weights[vertex_index][
                        first_channel : first_channel + LAND_WEIGHTS_PER_MAP
                    ],
                )
            )
    return bytes(payload)


def _generated_weight_map_manifest(
    texture_id: str,
    requested_path: str,
    path: Path,
    source_sha256: str,
    source_bytes: int,
) -> dict[str, object]:
    return {
        "id": texture_id,
        "requestedPath": requested_path,
        "archivePath": None,
        "sourceSha256": source_sha256,
        "sourceBytes": source_bytes,
        "png": str(path.resolve()),
        "pngBytes": path.stat().st_size,
        "pngSha256": file_sha256(path),
        "width": LAND_QUADRANT_VERTEX_SIDE,
        "height": LAND_QUADRANT_VERTEX_SIDE,
        "normalGreenInverted": False,
        "cubeFaces": [],
        "sourceArchive": None,
        "sourceArchiveSha256": None,
        "landscapeRole": LAND_WEIGHT_MAP_ROLE,
    }


def _texture_id(artifact: TextureArtifact | None) -> str | None:
    return None if artifact is None else artifact.asset_id


def export_landscape_gltf(
    landscape: Landscape,
    catalog: LandscapeTextureResolver,
    cell_coordinates: tuple[int, int],
    origin_game_units: tuple[float, float, float],
    texture_pipeline: TexturePipeline,
    output_root: Path,
    compiler_configuration: ContentCompilerConfiguration,
    identity: LandscapeIdentity,
    texture_output_root: Path | None = None,
) -> LandscapeExport:
    source_hash = sha256_bytes(landscape.source_bytes)
    asset_id = landscape_asset_id(
        landscape,
        identity,
        compiler_configuration,
    )
    generated_texture_root = texture_output_root or output_root
    diffuse_paths = {
        resolved_layer_texture_form_id(landscape, layer): canonical_texture_path(
            catalog.diffuse_path(resolved_layer_texture_form_id(landscape, layer))
        )
        for layer in (*landscape.base_layers, *landscape.alpha_layers)
    }
    texture_artifacts: dict[int, TextureArtifact] = {
        form_id: texture_pipeline.prepare(path) for form_id, path in diffuse_paths.items()
    }
    texture_contracts = {
        form_id: catalog.texture_contract(form_id) for form_id in diffuse_paths
    }
    normal_paths = {
        form_id: canonical_texture_path(str(contract["normalPath"]))
        for form_id, contract in texture_contracts.items()
        if contract.get("normalPath")
    }
    normal_artifacts: dict[int, TextureArtifact] = {
        form_id: texture_pipeline.prepare(path) for form_id, path in normal_paths.items()
    }

    runtime_textures: dict[str, dict[str, object]] = {}
    for artifact in (*texture_artifacts.values(), *normal_artifacts.values()):
        manifest = artifact.manifest()
        manifest["landscapeRole"] = (
            "normal"
            if artifact.requested_path.endswith("_n.dds")
            else "diffuse"
        )
        runtime_textures[artifact.asset_id] = manifest

    quadrant_weight_maps: dict[tuple[int, int], dict[str, object]] = {}
    for quadrant in range(LAND_QUADRANT_COUNT):
        layers = sorted(
            (row for row in landscape.alpha_layers if row.quadrant == quadrant),
            key=lambda row: row.layer_index,
        )
        if len(layers) > LAND_MAX_ALPHA_LAYERS:
            raise ValueError(
                f"LAND {landscape.form_id:08x} quadrant {quadrant} requires "
                f"{len(layers)} alpha layers but the renderer contract supports "
                f"{LAND_MAX_ALPHA_LAYERS} within {LAND_SHADER_SAMPLER_BUDGET} samplers"
            )
        if len({layer.layer_index for layer in layers}) != len(layers):
            raise ValueError(
                f"LAND {landscape.form_id:08x} quadrant {quadrant} repeats an ATXT layer"
            )
        for map_index in range(landscape_weight_map_count(len(layers))):
            payload = landscape_weight_map_payload(layers, map_index)
            weight_source = (
                landscape.source_bytes
                + f":quadrant={quadrant}:weight-map={map_index}:operation=".encode()
                + LANDSCAPE_LAYER_WEIGHT_OPERATION.encode()
                + b":layers="
                + ",".join(str(layer.layer_index) for layer in layers).encode()
                + payload
            )
            weight_source_sha256 = sha256_bytes(weight_source)
            weight_id = hashlib.sha256(
                f"LAND-WEIGHTS:{asset_id}:{quadrant}:{map_index}:"
                f"{weight_source_sha256}".encode()
            ).hexdigest()[:compiler_configuration.asset_id_hex_characters]
            weight_path = generated_texture_root / f"{weight_id}.rgba32f"
            atomic_write(weight_path, payload)
            weight_manifest = _generated_weight_map_manifest(
                weight_id,
                f"generated\\landscape\\{asset_id}-q{quadrant}-weights{map_index}.rgba32f",
                weight_path,
                weight_source_sha256,
                len(weight_source),
            )
            runtime_textures[weight_id] = weight_manifest
            quadrant_weight_maps[(quadrant, map_index)] = weight_manifest

    prepared_images = {}
    for form_id, artifact in texture_artifacts.items():
        with Image.open(artifact.png_path) as image:
            prepared_images[form_id] = image.convert("RGB")
    baked = bake_landscape_diffuse(
        landscape,
        prepared_images.__getitem__,
        compiler_configuration,
    )
    baked_id = landscape_baked_texture_id(
        landscape,
        identity,
        compiler_configuration,
    )
    baked_path = generated_texture_root / f"{baked_id}.png"
    _atomic_png(baked_path, baked, compiler_configuration.png_compression_level)
    baked_sources = [
        {
            **texture_contracts[form_id],
            "requestedPath": diffuse_paths[form_id],
            "archivePath": texture_artifacts[form_id].archive_path,
            "sourceSha256": texture_artifacts[form_id].source_sha256,
            "sourceBytes": texture_artifacts[form_id].source_bytes,
            "sourceArchive": texture_artifacts[form_id].source_archive,
            "sourceArchiveSha256": texture_artifacts[form_id].source_archive_sha256,
        }
        for form_id in sorted(texture_artifacts)
    ]
    baked_source_hash = landscape_baked_source_hash(landscape, baked_sources)
    baked_manifest = {
        "id": baked_id,
        "requestedPath": landscape_baked_requested_path(asset_id),
        "archivePath": None,
        "sourceSha256": baked_source_hash,
        "sourceBytes": len(landscape.source_bytes)
        + sum(int(row["sourceBytes"]) for row in baked_sources),
        "png": str(baked_path.resolve()),
        "pngBytes": baked_path.stat().st_size,
        "pngSha256": file_sha256(baked_path),
        "width": baked.width,
        "height": baked.height,
        "normalGreenInverted": False,
        "cubeFaces": [],
        "diagnosticOnly": True,
        "landscapeRole": "diagnostic-bake",
        "sources": baked_sources,
        "bakeContract": landscape_bake_contract(compiler_configuration),
        "zeroLayerBaseResolutions": [
            {
                "quadrant": layer.quadrant,
                "layerIndex": layer.layer_index,
                "resolvedLtexFormId": (
                    f"{resolved_layer_texture_form_id(landscape, layer):08x}"
                ),
            }
            for layer in landscape.alpha_layers
            if layer.texture_form_id == 0
        ],
        "configuredMissingBaseResolutions": [
            {
                "quadrant": layer.quadrant,
                "resolvedLtexFormId": f"{layer.texture_form_id:08x}",
                "source": layer.source,
            }
            for layer in landscape.base_layers
            if layer.source == CONFIGURED_MISSING_BASE_SOURCE
        ],
    }

    builder = BufferBuilder()
    primitives = []
    material_rows = []
    sidecar_surfaces = []
    total_vertices = 0
    total_triangles = 0
    for quadrant in range(LAND_QUADRANT_COUNT):
        positions, normals, uvs, colors, triangles = landscape_quadrant_geometry(
            landscape,
            cell_coordinates,
            origin_game_units,
            quadrant,
            compiler_configuration.exterior_cell_size_game_units,
        )
        tangents = generate_tangents(positions, normals, uvs, triangles)
        position_accessor = builder.add(
            pack_floats(positions),
            component_type=GL_FLOAT,
            count=len(positions),
            value_type="VEC3",
            target=GL_ARRAY_BUFFER,
            minimum=[min(value[axis] for value in positions) for axis in range(3)],
            maximum=[max(value[axis] for value in positions) for axis in range(3)],
        )
        normal_accessor = builder.add(
            pack_floats(normals),
            component_type=GL_FLOAT,
            count=len(normals),
            value_type="VEC3",
            target=GL_ARRAY_BUFFER,
        )
        tangent_accessor = builder.add(
            pack_floats(tangents),
            component_type=GL_FLOAT,
            count=len(tangents),
            value_type="VEC4",
            target=GL_ARRAY_BUFFER,
        )
        uv_accessor = builder.add(
            pack_floats(uvs),
            component_type=GL_FLOAT,
            count=len(uvs),
            value_type="VEC2",
            target=GL_ARRAY_BUFFER,
        )
        color_accessor = builder.add(
            pack_floats(colors),
            component_type=GL_FLOAT,
            count=len(colors),
            value_type="VEC4",
            target=GL_ARRAY_BUFFER,
        )
        indices = [value for triangle in triangles for value in triangle]
        index_accessor = builder.add(
            struct.pack(f"<{len(indices)}H", *indices),
            component_type=GL_UNSIGNED_SHORT,
            count=len(indices),
            value_type="SCALAR",
            target=GL_ELEMENT_ARRAY_BUFFER,
        )
        surface_name = f"LAND_{asset_id}_Q{quadrant}"
        primitives.append(
            {
                "attributes": {
                    "POSITION": position_accessor,
                    "NORMAL": normal_accessor,
                    "TANGENT": tangent_accessor,
                    "TEXCOORD_0": uv_accessor,
                    "COLOR_0": color_accessor,
                },
                "indices": index_accessor,
                "material": quadrant,
            }
        )
        material_rows.append(
            {
                "name": f"{surface_name} material",
                "pbrMetallicRoughness": {
                    "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
                    "metallicFactor": 0.0,
                    "roughnessFactor": 1.0,
                },
            }
        )
        texture_paths = [
            runtime_textures[texture_id]["requestedPath"]
            for texture_id in landscape_material_texture_ids(
                landscape,
                quadrant,
                texture_artifacts,
                normal_artifacts,
                quadrant_weight_maps,
            )
        ]
        sidecar_surfaces.append(
            {
                "stableId": hashlib.sha256(
                    f"{source_hash}:{surface_name}".encode()
                ).hexdigest()[:compiler_configuration.stable_id_hex_characters],
                "name": surface_name,
                "vertices": len(positions),
                "triangles": len(triangles),
                "attributes": [
                    "COLOR_0",
                    "NORMAL",
                    "POSITION",
                    "TANGENT",
                    "TEXCOORD_0",
                ],
                "textures": texture_paths,
                "transformBakedToRoot": True,
            }
        )
        total_vertices += len(positions)
        total_triangles += len(triangles)

    model_path = output_root / f"{asset_id}.gltf"
    sidecar_path = output_root / f"{asset_id}.opennv.json"
    buffer_name = model_path.with_suffix(".bin").name
    gltf = {
        "asset": {"version": "2.0", "generator": GENERATOR},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": f"LAND_{asset_id}", "mesh": 0}],
        "meshes": [{
            "name": f"LAND_{asset_id}",
            "primitives": primitives,
        }],
        "materials": material_rows,
        "buffers": [{"uri": buffer_name, "byteLength": len(builder.data)}],
        "bufferViews": builder.views,
        "accessors": builder.accessors,
        "extras": {"openNvSchema": SCHEMA, "sourceSha256": source_hash},
    }
    gltf_bytes = (json.dumps(gltf, indent=2, sort_keys=True) + "\n").encode()
    binary_bytes = bytes(builder.data)
    atomic_write(model_path.with_suffix(".bin"), binary_bytes)
    atomic_write(model_path, gltf_bytes)
    compiler = compiler_provenance()
    logical_path = landscape_logical_path(landscape, identity)
    sidecar = {
        "schema": SCHEMA,
        "status": "layered-material",
        "source": {
            "logicalPath": logical_path,
            "formKey": identity.form_key,
            "cellFormKey": identity.cell_form_key,
            "worldspaceFormKey": identity.worldspace_form_key,
            "bytes": len(landscape.source_bytes),
            "sha256": source_hash,
            "compressionChecksumValid": landscape.compression_checksum_valid,
            "layerContractSha256": landscape_layer_contract_sha256(landscape),
        },
        "compiler": compiler,
        "outputs": {
            "gltf": {"file": model_path.name, "bytes": len(gltf_bytes), "sha256": sha256_bytes(gltf_bytes)},
            "buffer": {"file": buffer_name, "bytes": len(binary_bytes), "sha256": sha256_bytes(binary_bytes)},
        },
        "coverage": {
            "surfaces": LAND_QUADRANT_COUNT,
            "vertices": total_vertices,
            "uniqueVertices": LAND_VERTEX_SIDE * LAND_VERTEX_SIDE,
            "sharedBorderVertices": total_vertices - LAND_VERTEX_SIDE * LAND_VERTEX_SIDE,
            "triangles": total_triangles,
            "collisionExported": False,
            "baseLayers": len(landscape.base_layers),
            "authoredBaseLayers": sum(
                layer.source != CONFIGURED_MISSING_BASE_SOURCE
                for layer in landscape.base_layers
            ),
            "configuredMissingBaseLayers": sum(
                layer.source == CONFIGURED_MISSING_BASE_SOURCE
                for layer in landscape.base_layers
            ),
            "alphaLayers": len(landscape.alpha_layers),
            "runtimeLayeredMaterials": True,
            "runtimeTextures": len(runtime_textures),
            "runtimeTextureIds": sorted(runtime_textures),
            "diagnosticBakeTextureId": baked_id,
        },
        "surfaces": sidecar_surfaces,
    }
    atomic_write(sidecar_path, (json.dumps(sidecar, indent=2, sort_keys=True) + "\n").encode())
    asset = {
        "id": asset_id,
        "logicalPath": sidecar["source"]["logicalPath"],
        "sourceSha256": source_hash,
        "model": str(model_path.resolve()),
        "sidecar": str(sidecar_path.resolve()),
        "surfaces": LAND_QUADRANT_COUNT,
        "compiler": compiler,
        "collision": {
            "enabled": True,
            "source": "LAND-height-grid",
            "faceSelection": LANDSCAPE_COLLISION_FACE_SELECTION,
            "blockTypes": ["LAND"],
        },
        "textureBindings": [
            {
                "requestedPath": requested_path,
                "textureId": texture_id,
            }
            for requested_path, texture_id in sorted(
                {
                    str(texture["requestedPath"]): str(texture["id"])
                    for texture in runtime_textures.values()
                }.items()
            )
        ],
        "materials": landscape_materials(
            landscape,
            asset_id,
            texture_artifacts,
            normal_artifacts,
            quadrant_weight_maps,
            compiler_configuration,
        ),
    }
    return LandscapeExport(
        asset,
        tuple(runtime_textures[texture_id] for texture_id in sorted(runtime_textures)),
        baked_manifest,
    )


def landscape_asset_id(
    landscape: Landscape,
    identity: LandscapeIdentity,
    compiler_configuration: ContentCompilerConfiguration,
) -> str:
    source_hash = sha256_bytes(landscape.source_bytes)
    layer_hash = landscape_layer_contract_sha256(landscape)
    return hashlib.sha256(
        f"LAND:{identity.form_key}:{source_hash}:{layer_hash}".encode()
    ).hexdigest()[:compiler_configuration.asset_id_hex_characters]


def landscape_logical_path(
    landscape: Landscape,
    identity: LandscapeIdentity,
) -> str:
    return (
        f"{identity.source_plugin}\\worldspace-{identity.worldspace_form_key}\\"
        f"cell-{identity.cell_form_key}\\land-{identity.form_key}"
    )


def landscape_baked_texture_id(
    landscape: Landscape,
    identity: LandscapeIdentity,
    compiler_configuration: ContentCompilerConfiguration,
) -> str:
    source_hash = sha256_bytes(landscape.source_bytes)
    layer_hash = landscape_layer_contract_sha256(landscape)
    return hashlib.sha256(
        f"LAND-BAKED:{identity.form_key}:{source_hash}:{layer_hash}".encode()
    ).hexdigest()[:compiler_configuration.asset_id_hex_characters]


def landscape_baked_requested_path(asset_id: str) -> str:
    return f"generated\\landscape\\{asset_id}-diffuse.png"


def landscape_baked_source_hash(
    landscape: Landscape,
    sources: list[dict[str, object]],
) -> str:
    return sha256_bytes(
        landscape.source_bytes
        + landscape_layer_contract_sha256(landscape).encode("ascii")
        + json.dumps(
            sources,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
    )


def landscape_layer_contract_sha256(landscape: Landscape) -> str:
    document = {
        "base": [
            {
                "quadrant": layer.quadrant,
                "layerIndex": layer.layer_index,
                "ltexFormId": f"{layer.texture_form_id:08x}",
                "source": layer.source,
            }
            for layer in landscape.base_layers
        ],
        "alpha": [
            {
                "quadrant": layer.quadrant,
                "layerIndex": layer.layer_index,
                "ltexFormId": f"{layer.texture_form_id:08x}",
                "source": layer.source,
            }
            for layer in landscape.alpha_layers
        ],
    }
    return sha256_bytes(
        json.dumps(document, sort_keys=True, separators=(",", ":")).encode("utf-8")
    )


def landscape_bake_contract(
    compiler_configuration: ContentCompilerConfiguration,
) -> dict[str, object]:
    return {
        "quadrants": "0=lower-left,1=lower-right,2=upper-left,3=upper-right",
        "tileRepeatsPerCell": compiler_configuration.landscape_tile_repeats_per_cell,
        "alphaInterpolation": LANDSCAPE_WEIGHT_INTERPOLATION,
        "alphaOperation": LANDSCAPE_LAYER_WEIGHT_OPERATION,
        "weightStorage": LANDSCAPE_WEIGHT_STORAGE,
    }


def landscape_material_texture_ids(
    landscape: Landscape,
    quadrant: int,
    diffuse_artifacts: dict[int, TextureArtifact],
    normal_artifacts: dict[int, TextureArtifact],
    weight_maps: dict[tuple[int, int], dict[str, object]],
) -> list[str]:
    base = next(layer for layer in landscape.base_layers if layer.quadrant == quadrant)
    base_form_id = resolved_layer_texture_form_id(landscape, base)
    result = [diffuse_artifacts[base_form_id].asset_id]
    base_normal = normal_artifacts.get(base_form_id)
    if base_normal is not None:
        result.append(base_normal.asset_id)
    layers = sorted(
        (layer for layer in landscape.alpha_layers if layer.quadrant == quadrant),
        key=lambda layer: layer.layer_index,
    )
    for layer in layers:
        form_id = resolved_layer_texture_form_id(landscape, layer)
        result.append(diffuse_artifacts[form_id].asset_id)
        normal = normal_artifacts.get(form_id)
        if normal is not None:
            result.append(normal.asset_id)
    result.extend(
        str(weight_maps[(quadrant, map_index)]["id"])
        for map_index in range(
            landscape_weight_map_count(len(layers))
        )
    )
    return result


def landscape_materials(
    landscape: Landscape,
    asset_id: str,
    diffuse_artifacts: dict[int, TextureArtifact],
    normal_artifacts: dict[int, TextureArtifact],
    weight_maps: dict[tuple[int, int], dict[str, object]],
    compiler_configuration: ContentCompilerConfiguration,
) -> list[dict[str, object]]:
    result = []
    for quadrant in range(LAND_QUADRANT_COUNT):
        base = next(
            layer for layer in landscape.base_layers if layer.quadrant == quadrant
        )
        base_form_id = resolved_layer_texture_form_id(landscape, base)
        layers = sorted(
            (layer for layer in landscape.alpha_layers if layer.quadrant == quadrant),
            key=lambda layer: layer.layer_index,
        )
        weight_map_ids = [
            str(weight_maps[(quadrant, map_index)]["id"])
            for map_index in range(landscape_weight_map_count(len(layers)))
        ]
        layer_contracts = []
        for ordinal, layer in enumerate(layers):
            form_id = resolved_layer_texture_form_id(landscape, layer)
            weight_ordinal = ordinal + 1
            map_index = weight_ordinal // LAND_WEIGHTS_PER_MAP
            layer_contracts.append(
                {
                    "layerIndex": layer.layer_index,
                    "ltexFormId": f"{form_id:08x}",
                    "diffuseTextureId": diffuse_artifacts[form_id].asset_id,
                    "normalTextureId": _texture_id(normal_artifacts.get(form_id)),
                    "weightMapTextureId": weight_map_ids[map_index],
                    "weightMapIndex": map_index,
                    "weightChannel": weight_ordinal % LAND_WEIGHTS_PER_MAP,
                }
            )
        surface_name = f"LAND_{asset_id}_Q{quadrant}"
        result.append(
            {
                "surfaceIndex": quadrant,
                "name": surface_name,
                "diffuseTextureId": diffuse_artifacts[base_form_id].asset_id,
                "normalTextureId": _texture_id(normal_artifacts.get(base_form_id)),
                "emissiveTextureId": None,
                "environmentTextureId": None,
                "environmentMaskTextureId": None,
                "environmentMapScale": 1.0,
                "emissiveColor": [0.0, 0.0, 0.0],
                "emissiveReplace": False,
                "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
                "diffuseSampleSrgb": False,
                "roughness": 1.0,
                "alphaContract": {"mode": "OPAQUE", "cutoff": None},
                "vertexColorMode": "multiply",
                "doubleSided": False,
                "unshaded": False,
                "landscapeContract": {
                    "schema": LANDSCAPE_MATERIAL_SCHEMA,
                    "model": LANDSCAPE_LIGHTING_MODEL,
                    "diffuseDomain": LANDSCAPE_DIFFUSE_DOMAIN,
                    "normalDecode": LANDSCAPE_NORMAL_DECODE,
                    "layerWeightOperation": LANDSCAPE_LAYER_WEIGHT_OPERATION,
                    "weightInterpolation": LANDSCAPE_WEIGHT_INTERPOLATION,
                    "weightStorage": LANDSCAPE_WEIGHT_STORAGE,
                    "retailWeightSemantics": list(LANDSCAPE_RETAIL_WEIGHT_SEMANTICS),
                    "retailWeightType": LANDSCAPE_RETAIL_WEIGHT_TYPE,
                    "source": LANDSCAPE_CONTRACT_SOURCE,
                    "quadrant": quadrant,
                    "ltexFormId": f"{base_form_id:08x}",
                    "baseTextureSource": base.source,
                    "tileRepeats": compiler_configuration.landscape_tiles_per_quadrant,
                    "weightVertexSide": LAND_QUADRANT_VERTEX_SIDE,
                    "weightLastVertex": LAND_QUADRANT_LAST_VERTEX,
                    "weightMapTextureIds": weight_map_ids,
                    "baseWeightMapIndex": 0 if weight_map_ids else None,
                    "baseWeightChannel": 0 if weight_map_ids else None,
                    "shaderSamplerBudget": LAND_SHADER_SAMPLER_BUDGET,
                    "samplersUsed": (
                        LAND_BASE_TEXTURE_SAMPLERS
                        + len(layers) * LAND_TEXTURE_SAMPLERS_PER_ALPHA_LAYER
                        + landscape_weight_map_count(len(layers))
                    ),
                    "baseDiffuseTextureId": diffuse_artifacts[base_form_id].asset_id,
                    "baseNormalTextureId": _texture_id(normal_artifacts.get(base_form_id)),
                    "layers": layer_contracts,
                },
            }
        )
    return result
