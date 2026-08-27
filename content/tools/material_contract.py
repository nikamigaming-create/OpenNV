"""Translate direct NIF surface evidence into the shared runtime material contract."""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from functools import cache

from actor_material import nif_material_roughness
from runtime_configuration import ContentCompilerConfiguration, configured_recipe_path


MATERIAL_BINDING_SCHEMA = "opennv-nif-material-binding-contract/v1"
MATERIAL_BINDING_ROLES = (
    "diffuse",
    "normal",
    "emissive",
    "environment",
    "environmentMask",
)
MISSING_MEMBER_POLICIES = frozenset({"error", "unbound-no-substitution"})
STENCIL_DRAW_BOTH = 3
ENCODED_DIFFUSE_TEXTURE_PREFIXES = ("textures\\landscape\\roads\\",)
SLS_LIGHTING_PROPERTY_TYPES = frozenset({"BSShaderPPLightingProperty"})
RETAIL_LIGHTING_CONTRACT_SCHEMA = "opennv-retail-material-lighting/v1"
RETAIL_AMBIENT_DIRECTIONAL_LAMBERT_MODEL = "ambient-plus-directional-lambert"


@dataclass(frozen=True)
class TextureSlotBinding:
    role: str
    index: int
    missing_owned_member: str


@dataclass(frozen=True)
class MaterialBindingContract:
    schema: str
    contract_id: str
    status: str
    canonical_sha256: str
    file_name: str
    slots: dict[str, TextureSlotBinding]
    environment_flag_1: str
    environment_disable_flag_2: str

    def manifest(self) -> dict[str, str]:
        return {
            "schema": self.schema,
            "id": self.contract_id,
            "status": self.status,
            "file": self.file_name,
            "canonicalSha256": self.canonical_sha256,
        }


def _object(parent: dict[str, object], name: str) -> dict[str, object]:
    value = parent.get(name)
    if not isinstance(value, dict):
        raise ValueError(f"Material binding contract object is missing: {name}")
    return value


@cache
def load_material_binding_contract() -> MaterialBindingContract:
    path = configured_recipe_path("materialBinding")
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict) or set(document) != {
        "schema",
        "id",
        "status",
        "provenance",
        "slots",
        "activation",
    }:
        raise ValueError("Material binding contract fields are invalid")
    if document.get("schema") != MATERIAL_BINDING_SCHEMA:
        raise ValueError(f"Unexpected material binding schema: {document.get('schema')}")
    for field in ("id", "status"):
        if not isinstance(document.get(field), str) or not str(document[field]).strip():
            raise ValueError(f"Material binding contract {field} is invalid")
    provenance = _object(document, "provenance")
    if set(provenance) != {"classification", "status", "source", "evidence"} or any(
        not isinstance(provenance.get(field), str) or not str(provenance[field]).strip()
        for field in provenance
    ):
        raise ValueError("Material binding contract provenance is invalid")
    source_slots = _object(document, "slots")
    if set(source_slots) != set(MATERIAL_BINDING_ROLES):
        raise ValueError("Material binding contract roles are invalid")
    slots: dict[str, TextureSlotBinding] = {}
    seen_indices: set[int] = set()
    for role in MATERIAL_BINDING_ROLES:
        source = _object(source_slots, role)
        if set(source) != {"index", "missingOwnedMember"}:
            raise ValueError(f"Material binding slot fields are invalid: {role}")
        index = source.get("index")
        policy = source.get("missingOwnedMember")
        if (
            not isinstance(index, int)
            or isinstance(index, bool)
            or index < 0
            or index in seen_indices
            or policy not in MISSING_MEMBER_POLICIES
        ):
            raise ValueError(f"Material binding slot is invalid: {role}")
        seen_indices.add(index)
        slots[role] = TextureSlotBinding(role, index, str(policy))
    activation = _object(document, "activation")
    if set(activation) != {"environmentFlag1", "environmentDisableFlag2"} or any(
        not isinstance(value, str) or not value
        for value in activation.values()
    ):
        raise ValueError("Material binding activation flags are invalid")
    canonical = json.dumps(
        document,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return MaterialBindingContract(
        schema=str(document["schema"]),
        contract_id=str(document["id"]),
        status=str(document["status"]),
        canonical_sha256=hashlib.sha256(canonical).hexdigest(),
        file_name=path.name,
        slots=slots,
        environment_flag_1=str(activation["environmentFlag1"]),
        environment_disable_flag_2=str(activation["environmentDisableFlag2"]),
    )


def diffuse_sample_srgb(
    path: str | None,
    property_types: list[str] | None = None,
) -> bool:
    """Return the runtime sampler domain proven for an owned diffuse family."""
    if not path:
        return True
    canonical = path.replace("/", "\\").casefold()
    # Native D3D9 draw telemetry joins the owned roadwasteland01 top mip to
    # stage zero and reports D3DSAMP_SRGBTEXTURE disabled. Keep this as a
    # reusable texture-family contract; model/editor names are not evidence.
    return not (
        bool(SLS_LIGHTING_PROPERTY_TYPES.intersection(property_types or []))
        or any(
        canonical.startswith(prefix)
        for prefix in ENCODED_DIFFUSE_TEXTURE_PREFIXES
        )
    )


def retail_lighting_contract(
    path: str | None,
    property_types: list[str] | None = None,
) -> dict[str, object] | None:
    """Return the matched retail shader contract for a proven diffuse family."""
    if not path:
        return None
    canonical = path.replace("/", "\\").casefold()
    sls_lighting = bool(
        SLS_LIGHTING_PROPERTY_TYPES.intersection(property_types or [])
    )
    road_family = any(
        canonical.startswith(prefix) for prefix in ENCODED_DIFFUSE_TEXTURE_PREFIXES
    )
    if not sls_lighting and not road_family:
        return None
    return {
        "schema": RETAIL_LIGHTING_CONTRACT_SCHEMA,
        "model": RETAIL_AMBIENT_DIRECTIONAL_LAMBERT_MODEL,
        "diffuseDomain": "encoded",
        "normalDecode": "signed-rgb",
        "vertexColorOperation": "multiply",
        "source": (
            "recovered-sls-ordinary-lighting-family"
            if sls_lighting
            else "matched-live-road-shader-package"
        ),
    }


def environment_texture_paths(surface: dict[str, object]) -> tuple[str | None, str | None]:
    contract = load_material_binding_contract()
    material = surface["material"]
    if contract.environment_flag_1 not in set(material.get("shaderFlags1Enabled", [])):
        return None, None
    if contract.environment_disable_flag_2 in set(material.get("shaderFlags2Enabled", [])):
        return None, None
    textures = surface["textures"]
    environment = _texture_slot(textures, contract.slots["environment"].index)
    mask = _texture_slot(textures, contract.slots["environmentMask"].index)
    return environment, mask


def texture_binding_requests(surface: dict[str, object]) -> list[dict[str, str]]:
    """Return only shader-active authored texture bindings for one surface."""

    contract = load_material_binding_contract()
    textures = surface["textures"]
    material = surface["material"]
    unshaded = "BSShaderNoLightingProperty" in surface["propertyTypes"]
    environment, environment_mask = environment_texture_paths(surface)
    paths = {
        "diffuse": _texture_slot(textures, contract.slots["diffuse"].index),
        "normal": _texture_slot(textures, contract.slots["normal"].index),
        "emissive": (
            None
            if unshaded
            else _texture_slot(textures, contract.slots["emissive"].index)
        ),
        "environment": environment,
        "environmentMask": environment_mask,
    }
    return [
        {
            "role": role,
            "path": path,
            "missingOwnedMember": contract.slots[role].missing_owned_member,
        }
        for role, path in paths.items()
        if path is not None
    ]


def material_bindings(
    sidecar: dict[str, object],
    texture_ids: dict[str, str],
    compiler_configuration: ContentCompilerConfiguration,
) -> list[dict[str, object]]:
    bindings = []
    binding_contract = load_material_binding_contract()
    for surface_index, surface in enumerate(sidecar["surfaces"]):
        requests = {
            request["role"]: request["path"]
            for request in texture_binding_requests(surface)
        }
        diffuse = requests.get("diffuse")
        normal = requests.get("normal")
        emissive = requests.get("emissive")
        material = surface["material"]
        property_types = [str(value) for value in surface.get("propertyTypes", [])]
        shader_flags_one = sorted(set(material.get("shaderFlags1Enabled", [])))
        shader_flags_two = sorted(set(material.get("shaderFlags2Enabled", [])))
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
                "bindingContract": binding_contract.manifest(),
                "authoredTextureBindings": requests,
                "unresolvedTextureRoles": sorted(
                    role
                    for role, path in requests.items()
                    if path not in texture_ids
                ),
                "diffuseTextureId": _texture_id(texture_ids, diffuse),
                "diffuseSampleSrgb": diffuse_sample_srgb(diffuse, property_types),
                "retailLightingContract": retail_lighting_contract(
                    diffuse,
                    property_types,
                ),
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
                "shaderFlags1Enabled": shader_flags_one,
                "shaderFlags2Enabled": shader_flags_two,
                # Godot's StandardMaterial3D exposes one repeat switch for
                # both axes. Preserve the authored NIF mode so the runtime
                # can at least apply the exact common modes (wrap-both and
                # clamp-both); road chunks use clamp-both and contain UVs
                # outside 0..1 in their authored atlas layout.
                "textureClampMode": int(material.get("textureClampMode", 0)),
                "decal": "sf_decal_single_pass" in shader_flags_one
                    or "sf_dynamic_decal_single_pass" in shader_flags_one,
                "dynamicDecal": "sf_dynamic_decal_single_pass" in shader_flags_one,
                # Bethesda's LOD-object shader uses the alpha channel of the
                # shared building atlas for silhouette cutout even when the
                # NIF has no separate NiAlphaProperty.
                "lodObjectAtlas": "sf_2_lod_building" in shader_flags_two,
            }
        )
    return bindings


def _texture_slot(textures: list[str], index: int) -> str | None:
    return textures[index] if len(textures) > index and textures[index] else None


def _texture_id(texture_ids: dict[str, str], path: str | None) -> str | None:
    return texture_ids.get(path) if path else None
