"""Translate Fallout actor material flags into explicit glTF contracts."""

from __future__ import annotations

import hashlib
import math
import time
from typing import TYPE_CHECKING

if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from pyffi.formats.nif import NifFormat  # type: ignore  # noqa: E402

from bsa_archive import canonical_member_path

if TYPE_CHECKING:
    from actor_gltf import ActorComponent, TextureLibrary
    from runtime_configuration import ContentCompilerConfiguration


NIF_ALPHA_BLEND_ENABLED_FLAG = 0x0001
NIF_ALPHA_TEST_ENABLED_FLAG = 0x0200
NIF_ALPHA_BLEND_MODE_MASK = 0xF
NIF_ALPHA_TEST_FUNCTION_MASK = 0x7
NIF_ALPHA_SOURCE_BLEND_SHIFT = 1
NIF_ALPHA_DESTINATION_BLEND_SHIFT = 5
NIF_ALPHA_TEST_FUNCTION_SHIFT = 10
NIF_ALPHA_NO_SORTER_FLAG = 0x2000
BYTE_CHANNEL_MAXIMUM = 255.0


def build_actor_material(
    component: ActorComponent,
    shape: object,
    textures: TextureLibrary,
    compiler: ContentCompilerConfiguration,
) -> tuple[dict[str, object], dict[str, object]]:
    paths = []
    for prop in getattr(shape, "properties", []):
        texture_set = getattr(prop, "texture_set", None)
        if texture_set is not None:
            paths = [
                canonical_member_path(value) if value else ""
                for value in (_text(texture_set.textures[index]) for index in range(len(texture_set.textures)))
            ]
            break
    source_diffuse_path = paths[0] if paths else None
    diffuse_path = component.diffuse_override or source_diffuse_path
    aliases = {canonical_member_path(source): canonical_member_path(target) for source, target in component.diffuse_aliases}
    if diffuse_path in aliases:
        diffuse_path = aliases[diffuse_path]
    normal_path = paths[1] if len(paths) > 1 and paths[1] else None
    properties = list(getattr(shape, "properties", []))
    material_property = next(
        (prop for prop in properties if isinstance(prop, NifFormat.NiMaterialProperty)),
        None,
    )
    roughness, roughness_source = actor_roughness(material_property, compiler)
    material: dict[str, object] = {
        "name": f"{component.role}_{_text(shape.name)} material",
        "doubleSided": False,
        "pbrMetallicRoughness": {"metallicFactor": 0.0, "roughnessFactor": roughness},
    }
    pbr = material["pbrMetallicRoughness"]
    generated_image = component.generated_diffuse
    if source_diffuse_path is not None:
        generated_by_source = {
            canonical_member_path(path): image for path, image in component.generated_diffuse_by_source
        }
        generated_image = generated_by_source.get(source_diffuse_path, generated_image)
    generated_hash = None
    if generated_image is not None:
        generated_hash = hashlib.sha256(generated_image.tobytes()).hexdigest()
        texture_index = textures.generated(
            f"generated:{component.role}:{generated_hash}", generated_image, generated_hash
        )
        pbr["baseColorTexture"] = {"index": texture_index}
    elif diffuse_path:
        pbr["baseColorTexture"] = {"index": textures.source(diffuse_path)}
    tint = component.tint_rgb or (1.0, 1.0, 1.0)
    base_color_factor, factor_source = actor_base_color_factor(properties, tint)
    pbr["baseColorFactor"] = base_color_factor
    if normal_path:
        material["normalTexture"] = {"index": textures.source(normal_path, normal=True)}
    alpha_properties = [prop for prop in properties if isinstance(prop, NifFormat.NiAlphaProperty)]
    if alpha_properties:
        alpha_contract = actor_alpha_contract(alpha_properties[0])
        material["alphaMode"] = alpha_contract["mode"]
        if alpha_contract["mode"] == "MASK":
            material["alphaCutoff"] = alpha_contract["cutoff"]
        material["doubleSided"] = True
    else:
        alpha_contract = {
            "mode": "OPAQUE",
            "cutoff": None,
            "flags": None,
            "blendEnabled": False,
            "testEnabled": False,
        }
    return material, {
        "sourceDiffuse": source_diffuse_path,
        "resolvedDiffuse": diffuse_path,
        "sourceNormal": normal_path,
        "generatedDiffuseSha256": generated_hash,
        "alphaMode": material.get("alphaMode", "OPAQUE"),
        "alphaContract": alpha_contract,
        "tintRgb": component.tint_rgb,
        "baseColorFactorSource": factor_source,
        "roughness": roughness,
        "roughnessSource": roughness_source,
    }


def actor_base_color_factor(
    properties: list[object],
    tint: tuple[float, float, float],
) -> tuple[list[float], str]:
    material = next(
        (prop for prop in properties if isinstance(prop, NifFormat.NiMaterialProperty)),
        None,
    )
    bethesda_shader = any(
        isinstance(prop, (NifFormat.BSShaderProperty, NifFormat.BSLightingShaderProperty))
        for prop in properties
    )
    source = (1.0, 1.0, 1.0) if bethesda_shader or material is None else (
        float(material.diffuse_color.r),
        float(material.diffuse_color.g),
        float(material.diffuse_color.b),
    )
    alpha = float(material.alpha) if material is not None else 1.0
    return [source[axis] * tint[axis] for axis in range(3)] + [alpha], (
        "bethesda-shader-texture-neutral" if bethesda_shader else "ni-material-diffuse"
    )


def actor_vertex_colors_enabled(properties: list[object]) -> bool:
    return any(
        bool(getattr(getattr(prop, "shader_flags_2", None), "sf_2_vertex_colors", False))
        for prop in properties
    )


def actor_roughness(
    material: object | None,
    compiler: ContentCompilerConfiguration,
) -> tuple[float, str]:
    if material is None:
        return 1.0, "no-ni-material"
    specular = material.specular_color
    return nif_material_roughness(
        (float(specular.r), float(specular.g), float(specular.b)),
        float(material.glossiness),
        compiler,
    )


def nif_material_roughness(
    specular_rgb: tuple[float, float, float] | list[float],
    glossiness: float,
    compiler: ContentCompilerConfiguration,
) -> tuple[float, str]:
    if max(specular_rgb) <= compiler.zero_specular_epsilon:
        return 1.0, "ni-material-zero-specular"
    return (
        max(
            compiler.minimum_material_roughness,
            min(1.0, math.sqrt(2.0 / (glossiness + 2.0))),
        ),
        "ni-material-glossiness",
    )


def actor_alpha_contract(alpha_property: object) -> dict[str, object]:
    flags = int(alpha_property.flags)
    blend_enabled = bool(flags & NIF_ALPHA_BLEND_ENABLED_FLAG)
    test_enabled = bool(flags & NIF_ALPHA_TEST_ENABLED_FLAG)
    mode = "BLEND" if blend_enabled else "MASK" if test_enabled else "OPAQUE"
    return {
        "mode": mode,
        "cutoff": (
            float(alpha_property.threshold) / BYTE_CHANNEL_MAXIMUM
            if test_enabled
            else None
        ),
        "flags": flags,
        "blendEnabled": blend_enabled,
        "testEnabled": test_enabled,
        "sourceBlendMode": (
            flags >> NIF_ALPHA_SOURCE_BLEND_SHIFT
        ) & NIF_ALPHA_BLEND_MODE_MASK,
        "destinationBlendMode": (
            flags >> NIF_ALPHA_DESTINATION_BLEND_SHIFT
        ) & NIF_ALPHA_BLEND_MODE_MASK,
        "testFunction": (
            flags >> NIF_ALPHA_TEST_FUNCTION_SHIFT
        ) & NIF_ALPHA_TEST_FUNCTION_MASK,
        "noSorter": bool(flags & NIF_ALPHA_NO_SORTER_FLAG),
    }


def _text(value: object) -> str:
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    return str(value)
