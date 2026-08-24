"""Export one decoded LAND cell to verified glTF and a baked diffuse contract."""

from __future__ import annotations

import hashlib
import json
import os
import struct
import sys
from pathlib import Path
from typing import Callable

from PIL import Image

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
from landscape_catalog import LAND_VERTEX_SIDE, Landscape, LandscapeCatalog
from runtime_configuration import ContentCompilerConfiguration
from texture_pipeline import TextureArtifact, TexturePipeline, file_sha256


SCHEMA = "opennv-landscape-gltf/v1"
GENERATOR = "OpenNV direct LAND exporter v1"
LAND_QUADRANT_VERTEX_SIDE = 17
LAND_QUADRANT_LAST_VERTEX = LAND_QUADRANT_VERTEX_SIDE - 1
BYTE_CHANNEL_MAXIMUM = 255.0
EXTERIOR_CELL_SIZE_GAME_UNITS = 4096.0
LAND_VERTEX_SPACING_GAME_UNITS = 128.0


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
            [Path(__file__), root / "gltf_io.py", root / "landscape_catalog.py"]
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
        composite = _tiled_quadrant(image_for_texture(base.texture_form_id), compiler)
        for layer in sorted(
            (value for value in landscape.alpha_layers if value.quadrant == quadrant),
            key=lambda value: value.layer_index,
        ):
            overlay = _tiled_quadrant(image_for_texture(layer.texture_form_id), compiler)
            composite = Image.composite(
                overlay,
                composite,
                _opacity_mask(layer.opacities, compiler),
            )
        result.paste(composite, destinations[quadrant])
    return result


def landscape_geometry(
    landscape: Landscape,
    cell_coordinates: tuple[int, int],
    origin_game_units: tuple[float, float, float],
) -> tuple[
    list[tuple[float, float, float]],
    list[tuple[float, float, float]],
    list[tuple[float, float]],
    list[tuple[float, float, float, float]],
    list[tuple[int, int, int]],
]:
    cell_x = cell_coordinates[0] * EXTERIOR_CELL_SIZE_GAME_UNITS
    cell_y = cell_coordinates[1] * EXTERIOR_CELL_SIZE_GAME_UNITS
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
            uvs.append((x / (LAND_VERTEX_SIDE - 1), 1.0 - y / (LAND_VERTEX_SIDE - 1)))
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


def _atomic_png(path: Path, image: Image.Image, compression_level: int) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    image.save(temporary, format="PNG", optimize=True, compress_level=compression_level)
    os.replace(temporary, path)


def export_landscape_gltf(
    landscape: Landscape,
    catalog: LandscapeCatalog,
    cell_coordinates: tuple[int, int],
    origin_game_units: tuple[float, float, float],
    texture_pipeline: TexturePipeline,
    output_root: Path,
    compiler_configuration: ContentCompilerConfiguration,
) -> tuple[dict[str, object], dict[str, object]]:
    source_hash = sha256_bytes(landscape.source_bytes)
    asset_id = hashlib.sha256(
        f"LAND:{landscape.form_id:08x}:{source_hash}".encode()
    ).hexdigest()[:compiler_configuration.asset_id_hex_characters]
    diffuse_paths = {
        layer.texture_form_id: canonical_texture_path(catalog.diffuse_path(layer.texture_form_id))
        for layer in (*landscape.base_layers, *landscape.alpha_layers)
    }
    texture_artifacts: dict[int, TextureArtifact] = {
        form_id: texture_pipeline.prepare(path) for form_id, path in diffuse_paths.items()
    }
    prepared_images = {
        form_id: Image.open(artifact.png_path).convert("RGB")
        for form_id, artifact in texture_artifacts.items()
    }
    baked = bake_landscape_diffuse(
        landscape,
        prepared_images.__getitem__,
        compiler_configuration,
    )
    baked_id = hashlib.sha256(
        f"LAND-BAKED:{landscape.form_id:08x}:{source_hash}".encode()
    ).hexdigest()[:compiler_configuration.asset_id_hex_characters]
    baked_path = output_root / f"{baked_id}.png"
    _atomic_png(baked_path, baked, compiler_configuration.png_compression_level)
    baked_sources = [
        {
            "ltexFormId": f"{form_id:08x}",
            "requestedPath": diffuse_paths[form_id],
            "sourceSha256": texture_artifacts[form_id].source_sha256,
        }
        for form_id in sorted(texture_artifacts)
    ]
    baked_source_hash = sha256_bytes(
        landscape.source_bytes
        + "".join(row["sourceSha256"] for row in baked_sources).encode()
    )
    baked_manifest = {
        "id": baked_id,
        "requestedPath": f"generated\\landscape\\{landscape.form_id:08x}-diffuse.png",
        "archivePath": None,
        "sourceSha256": baked_source_hash,
        "png": str(baked_path.resolve()),
        "pngSha256": file_sha256(baked_path),
        "width": baked.width,
        "height": baked.height,
        "normalGreenInverted": False,
        "sources": baked_sources,
        "bakeContract": {
            "quadrants": "0=lower-left,1=lower-right,2=upper-left,3=upper-right",
            "tileRepeatsPerCell": compiler_configuration.landscape_tile_repeats_per_cell,
            "alphaInterpolation": "17x17-bilinear",
            "alphaOrder": "ascending-ATXT-layer",
        },
    }

    positions, normals, uvs, colors, triangles = landscape_geometry(
        landscape, cell_coordinates, origin_game_units
    )
    builder = BufferBuilder()
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
    surface_name = f"LAND_{landscape.form_id:08x}"
    model_path = output_root / f"{asset_id}.gltf"
    sidecar_path = output_root / f"{asset_id}.opennv.json"
    buffer_name = model_path.with_suffix(".bin").name
    gltf = {
        "asset": {"version": "2.0", "generator": GENERATOR},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": surface_name, "mesh": 0}],
        "meshes": [{
            "name": surface_name,
            "primitives": [{
                "attributes": {
                    "POSITION": position_accessor,
                    "NORMAL": normal_accessor,
                    "TEXCOORD_0": uv_accessor,
                    "COLOR_0": color_accessor,
                },
                "indices": index_accessor,
                "material": 0,
            }],
        }],
        "materials": [{
            "name": f"{surface_name} material",
            "pbrMetallicRoughness": {
                "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
                "metallicFactor": 0.0,
                "roughnessFactor": 1.0,
            },
        }],
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
    sidecar = {
        "schema": SCHEMA,
        "status": "geometry-only",
        "source": {
            "logicalPath": (
                f"falloutnv.esm\\worldspace-{landscape.worldspace_form_id:08x}\\"
                f"cell-{landscape.cell_form_id:08x}\\land-{landscape.form_id:08x}"
            ),
            "bytes": len(landscape.source_bytes),
            "sha256": source_hash,
            "compressionChecksumValid": landscape.compression_checksum_valid,
        },
        "compiler": compiler,
        "outputs": {
            "gltf": {"file": model_path.name, "bytes": len(gltf_bytes), "sha256": sha256_bytes(gltf_bytes)},
            "buffer": {"file": buffer_name, "bytes": len(binary_bytes), "sha256": sha256_bytes(binary_bytes)},
        },
        "coverage": {
            "surfaces": 1,
            "vertices": len(positions),
            "triangles": len(triangles),
            "collisionExported": False,
            "baseLayers": len(landscape.base_layers),
            "alphaLayers": len(landscape.alpha_layers),
        },
        "surfaces": [{
            "stableId": hashlib.sha256(
                f"{source_hash}:{surface_name}".encode()
            ).hexdigest()[:compiler_configuration.stable_id_hex_characters],
            "name": surface_name,
            "vertices": len(positions),
            "triangles": len(triangles),
            "attributes": ["COLOR_0", "NORMAL", "POSITION", "TEXCOORD_0"],
            "textures": [baked_manifest["requestedPath"]],
            "transformBakedToRoot": True,
        }],
    }
    atomic_write(sidecar_path, (json.dumps(sidecar, indent=2, sort_keys=True) + "\n").encode())
    asset = {
        "id": asset_id,
        "logicalPath": sidecar["source"]["logicalPath"],
        "sourceSha256": source_hash,
        "model": str(model_path.resolve()),
        "sidecar": str(sidecar_path.resolve()),
        "surfaces": 1,
        "compiler": compiler,
        "collision": {
            "enabled": True,
            "source": "LAND-height-grid",
            "blockTypes": ["LAND"],
        },
        "materials": [{
            "surfaceIndex": 0,
            "name": surface_name,
            "diffuseTextureId": baked_id,
            "normalTextureId": None,
            "emissiveTextureId": None,
            "environmentTextureId": None,
            "environmentMaskTextureId": None,
            "environmentMapScale": 1.0,
            "emissiveColor": [0.0, 0.0, 0.0],
            "emissiveReplace": False,
            "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
            "roughness": 1.0,
            "alphaContract": {"mode": "OPAQUE", "cutoff": None},
            "vertexColorMode": "multiply",
            "doubleSided": False,
            "unshaded": False,
        }],
    }
    return asset, baked_manifest
