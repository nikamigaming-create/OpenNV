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
FACEGEN_MATERIAL_SCHEMA = "opennv-retail-facegen-material/v2"
SKIN_MATERIAL_SCHEMA = "opennv-retail-actor-skin-material/v1"
GLTF_UNLIT_EXTENSION = "KHR_materials_unlit"


def build_actor_material(
    component: ActorComponent,
    shape: object,
    textures: TextureLibrary,
    compiler: ContentCompilerConfiguration,
) -> tuple[dict[str, object], dict[str, object]]:
    properties = list(getattr(shape, "properties", []))
    paths = list(actor_texture_paths(properties))
    source_diffuse_path = paths[0] if paths else None
    source_normal_path = paths[1] if len(paths) > 1 and paths[1] else None
    diffuse_path = (
        canonical_member_path(component.diffuse_override)
        if component.diffuse_override
        else source_diffuse_path
    )
    normal_path = (
        canonical_member_path(component.normal_override)
        if component.normal_override
        else source_normal_path
    )
    aliases = {canonical_member_path(source): canonical_member_path(target) for source, target in component.diffuse_aliases}
    if diffuse_path in aliases:
        diffuse_path = aliases[diffuse_path]
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
    unshaded = any(
        isinstance(
            prop,
            (
                NifFormat.BSShaderNoLightingProperty,
                NifFormat.BSEffectShaderProperty,
            ),
        )
        for prop in properties
    )
    if unshaded:
        material["extensions"] = {GLTF_UNLIT_EXTENSION: {}}
    pbr = material["pbrMetallicRoughness"]
    base_texture_index = None
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
        base_texture_index = texture_index
    elif diffuse_path:
        base_texture_index = textures.source(diffuse_path)
        pbr["baseColorTexture"] = {"index": base_texture_index}
    tint = component.tint_rgb or (1.0, 1.0, 1.0)
    base_color_factor, factor_source = actor_base_color_factor(properties, tint)
    pbr["baseColorFactor"] = base_color_factor
    normal_texture_index = None
    if normal_path:
        normal_texture_index = textures.source(normal_path, normal=True)
        material["normalTexture"] = {"index": normal_texture_index}
    facegen_sources = sum(
        value is not None
        for value in (component.facegen_detail_path, component.generated_facegen_detail)
    )
    if facegen_sources > 1:
        raise ValueError(f"Actor component {component.role} declares multiple FaceGen detail maps")
    facegen_contract = None
    if facegen_sources == 1:
        if base_texture_index is None or normal_texture_index is None:
            raise ValueError(
                f"Actor component {component.role} FaceGen material requires base and normal maps"
            )
        if component.facegen_detail_path is not None:
            detail_texture_index = textures.source(component.facegen_detail_path)
            detail_source = canonical_member_path(component.facegen_detail_path)
            detail_sha256 = None
        else:
            assert component.generated_facegen_detail is not None
            detail_sha256 = hashlib.sha256(
                component.generated_facegen_detail.tobytes()
            ).hexdigest()
            detail_source = f"generated:facegen:{component.role}:{detail_sha256}"
            detail_texture_index = textures.generated(
                detail_source,
                component.generated_facegen_detail,
                detail_sha256,
            )
        facegen_contract = {
            "schema": FACEGEN_MATERIAL_SCHEMA,
            "baseTextureIndex": base_texture_index,
            "normalTextureIndex": normal_texture_index,
            "detailTextureIndex": detail_texture_index,
            "detailSource": detail_source,
            "detailGeneratedSha256": detail_sha256,
        }
        material["extras"] = {"openNvFaceGenMaterial": facegen_contract}
    skin_shader = next(
        (
            prop
            for prop in properties
            if isinstance(prop, NifFormat.BSShaderPPLightingProperty)
            and prop.shader_type == NifFormat.BSShaderType.SHADERSKIN
        ),
        None,
    )
    skin_contract = (
        {
            "schema": SKIN_MATERIAL_SCHEMA,
            "source": "owned-nif-bs-shader-type-shaderskin",
            "shaderType": int(skin_shader.shader_type),
            "diffuseDomain": "encoded",
        }
        if skin_shader is not None
        else None
    )
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
        "sourceNormal": source_normal_path,
        "resolvedNormal": normal_path,
        "generatedDiffuseSha256": generated_hash,
        "alphaMode": material.get("alphaMode", "OPAQUE"),
        "alphaContract": alpha_contract,
        "tintRgb": component.tint_rgb,
        "baseColorFactorSource": factor_source,
        "roughness": roughness,
        "roughnessSource": roughness_source,
        "metallic": 0.0,
        "unshaded": unshaded,
        "unshadedSource": (
            "owned-nif-no-lighting-or-effect-shader" if unshaded else None
        ),
        "faceGen": facegen_contract,
        "skin": skin_contract,
    }


def actor_texture_paths(properties: list[object]) -> tuple[str, ...]:
    for prop in properties:
        texture_set = getattr(prop, "texture_set", None)
        if texture_set is not None:
            return tuple(
                canonical_member_path(value) if value else ""
                for value in (_text(texture_set.textures[index]) for index in range(len(texture_set.textures)))
            )
        # BSEffectShaderProperty stores its authored diffuse directly in Source
        # Texture rather than a BSShaderTextureSet.
        if isinstance(prop, NifFormat.BSEffectShaderProperty):
            source_texture = _text(prop.source_texture)
            if source_texture:
                return (canonical_member_path(source_texture),)
        # Fallout's unlit effect path predates BSEffectShaderProperty and
        # stores the image directly in BSShaderNoLightingProperty.File Name.
        # Preserve that external NIF contract without model-specific policy.
        if isinstance(prop, NifFormat.BSShaderNoLightingProperty):
            file_name = _text(prop.file_name)
            if file_name:
                return (canonical_member_path(file_name),)
    return ()


def actor_base_color_factor(
    properties: list[object],
    tint: tuple[float, float, float],
) -> tuple[list[float], str]:
    material = next(
        (prop for prop in properties if isinstance(prop, NifFormat.NiMaterialProperty)),
        None,
    )
    bethesda_shader = any(
        isinstance(
            prop,
            (
                NifFormat.BSShaderProperty,
                NifFormat.BSLightingShaderProperty,
                NifFormat.BSEffectShaderProperty,
            ),
        )
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
