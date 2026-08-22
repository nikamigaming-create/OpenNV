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


def build_actor_material(
    component: ActorComponent,
    shape: object,
    textures: TextureLibrary,
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
    roughness, roughness_source = actor_roughness(material_property)
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


def actor_roughness(material: object | None) -> tuple[float, str]:
    if material is None:
        return 1.0, "no-ni-material"
    specular = material.specular_color
    if max(float(specular.r), float(specular.g), float(specular.b)) <= 1.0e-6:
        return 1.0, "ni-material-zero-specular"
    glossiness = float(material.glossiness)
    return max(0.08, min(1.0, math.sqrt(2.0 / (glossiness + 2.0)))), "ni-material-glossiness"


def actor_alpha_contract(alpha_property: object) -> dict[str, object]:
    flags = int(alpha_property.flags)
    blend_enabled = bool(flags & 0x0001)
    test_enabled = bool(flags & 0x0200)
    mode = "BLEND" if blend_enabled else "MASK" if test_enabled else "OPAQUE"
    return {
        "mode": mode,
        "cutoff": float(alpha_property.threshold) / 255.0 if test_enabled else None,
        "flags": flags,
        "blendEnabled": blend_enabled,
        "testEnabled": test_enabled,
        "sourceBlendMode": (flags >> 1) & 0xF,
        "destinationBlendMode": (flags >> 5) & 0xF,
        "testFunction": (flags >> 10) & 0x7,
        "noSorter": bool(flags & 0x2000),
    }


def _text(value: object) -> str:
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    return str(value)
