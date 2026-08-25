"""Translate direct NIF surface evidence into the shared runtime material contract."""

from __future__ import annotations

from actor_material import nif_material_roughness
from runtime_configuration import ContentCompilerConfiguration


DIFFUSE_TEXTURE_SLOT = 0
NORMAL_TEXTURE_SLOT = 1
EMISSIVE_TEXTURE_SLOT = 2
ENVIRONMENT_TEXTURE_SLOT = 4
ENVIRONMENT_MASK_TEXTURE_SLOT = 5
STENCIL_DRAW_BOTH = 3


def environment_texture_paths(surface: dict[str, object]) -> tuple[str | None, str | None]:
    material = surface["material"]
    if "sf_environment_mapping" not in set(material.get("shaderFlags1Enabled", [])):
        return None, None
    if "sf_2_envmap_light_fade" in set(material.get("shaderFlags2Enabled", [])):
        return None, None
    textures = surface["textures"]
    environment = (
        textures[ENVIRONMENT_TEXTURE_SLOT]
        if len(textures) > ENVIRONMENT_TEXTURE_SLOT and textures[ENVIRONMENT_TEXTURE_SLOT]
        else None
    )
    mask = (
        textures[ENVIRONMENT_MASK_TEXTURE_SLOT]
        if len(textures) > ENVIRONMENT_MASK_TEXTURE_SLOT and textures[ENVIRONMENT_MASK_TEXTURE_SLOT]
        else None
    )
    return environment, mask


def material_bindings(
    sidecar: dict[str, object],
    texture_ids: dict[str, str],
    compiler_configuration: ContentCompilerConfiguration,
) -> list[dict[str, object]]:
    bindings = []
    for surface_index, surface in enumerate(sidecar["surfaces"]):
        textures = surface["textures"]
        diffuse = _texture_slot(textures, DIFFUSE_TEXTURE_SLOT)
        normal = _texture_slot(textures, NORMAL_TEXTURE_SLOT)
        emissive = _texture_slot(textures, EMISSIVE_TEXTURE_SLOT)
        material = surface["material"]
        environment, environment_mask = environment_texture_paths(surface)
        glossiness = float(
            material.get(
                "glossiness",
                compiler_configuration.default_material_glossiness,
            )
        )
        roughness, roughness_source = nif_material_roughness(
            [float(value) for value in material.get("specular", [0.0, 0.0, 0.0])],
            glossiness,
            compiler_configuration,
        )
        unshaded = "BSShaderNoLightingProperty" in surface["propertyTypes"]
        emissive_color = [float(value) for value in material.get("emissive", [0.0, 0.0, 0.0])]
        emissive_controlled = bool(material.get("emissiveControlled", False))
        emissive_active = not unshaded and (emissive is not None or emissive_controlled)
        emission_texture = emissive if emissive_active else None
        if not emissive_active:
            emissive_color = [0.0, 0.0, 0.0]
        bindings.append(
            {
                "surfaceIndex": surface_index,
                "name": surface["name"],
                "diffuseTextureId": _texture_id(texture_ids, diffuse),
                "normalTextureId": _texture_id(texture_ids, normal),
                "emissiveTextureId": _texture_id(texture_ids, emission_texture),
                "environmentTextureId": _texture_id(texture_ids, environment),
                "environmentMaskTextureId": _texture_id(texture_ids, environment_mask),
                "environmentMapScale": float(material.get("environmentMapScale", 1.0)),
                "emissiveColor": emissive_color,
                "emissiveReplace": emissive_controlled and emissive is None,
                "baseColorFactor": [
                    *[float(value) for value in material.get("baseColor", [1.0, 1.0, 1.0])],
                    float(material.get("alpha", 1.0)),
                ],
                "roughness": roughness,
                "roughnessSource": roughness_source,
                "alphaContract": material["alphaContract"],
                "vertexColorMode": material["vertexColorMode"],
                "doubleSided": int(material.get("stencilDrawMode", 1)) == STENCIL_DRAW_BOTH,
                "unshaded": unshaded,
            }
        )
    return bindings


def _texture_slot(textures: list[str], index: int) -> str | None:
    return textures[index] if len(textures) > index and textures[index] else None


def _texture_id(texture_ids: dict[str, str], path: str | None) -> str | None:
    return texture_ids.get(path) if path else None
