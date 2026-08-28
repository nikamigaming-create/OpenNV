"""Prepare a hash-pinned static-pose Vault 13 door presentation proof."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import shutil
import tempfile
from pathlib import Path

from bsa_archive import BsaArchive
from cell_catalog import scan_cell_catalog
from cell_scene import environment_texture_paths
from export_static_nif_gltf import export_static_nif
from fo1_profile import Fo1ProfileError, parse_form_id, sha256_path
from texture_pipeline import TexturePipeline


RECIPE_SCHEMA = "opennv-fo1-object-presentation-map/v1"
MANIFEST_SCHEMA = "opennv-fo1-door-presentation-proof/v1"


def _atomic_json(path: Path, document: object) -> None:
    path.write_text(json.dumps(document, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def build(
    recipe_path: Path,
    object_contract_path: Path,
    fnv_data_root: Path,
    cache_root: Path,
) -> dict[str, object]:
    if cache_root.exists():
        raise Fo1ProfileError(f"refusing to overwrite door proof cache: {cache_root}")
    recipe = json.loads(recipe_path.read_text(encoding="utf-8"))
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise Fo1ProfileError(f"unsupported door mapping schema: {recipe.get('schema')!r}")
    object_contract_hash = sha256_path(object_contract_path)
    if object_contract_hash != recipe["sourceObjectContractSha256"]:
        raise Fo1ProfileError("Vault 13 object contract hash drift")
    object_contract = json.loads(object_contract_path.read_text(encoding="utf-8"))
    doors = object_contract["map"]["doors"]
    source = next((door for door in doors if door["serial"] == recipe["sourceDoor"]["serial"]), None)
    if source is None:
        raise Fo1ProfileError("Vault 13 source door serial is absent")
    expected_source = recipe["sourceDoor"]
    checks = {
        "id": source["id"],
        "tile": source["tile"],
        "tileX": source["tileX"],
        "tileY": source["tileY"],
        "rotation": source["rotation"],
        "pid": source["pid"],
        "fid": source["fid"],
        "artFilename": source["artFilename"],
        "prototypeFilename": source["prototype"]["filename"],
        "scriptIndex": source["scriptIndex"],
    }
    for key, actual in checks.items():
        if actual != expected_source[key]:
            raise Fo1ProfileError(f"Vault 13 source door {key} drift: expected {expected_source[key]!r}, got {actual!r}")

    target = recipe["target"]
    master_path = fnv_data_root / target["master"]["file"]
    meshes_path = fnv_data_root / target["meshesArchive"]["file"]
    if sha256_path(master_path) != target["master"]["sha256"]:
        raise Fo1ProfileError("FalloutNV.esm hash drift for Vault door mapping")
    if sha256_path(meshes_path) != target["meshesArchive"]["sha256"]:
        raise Fo1ProfileError("FNV meshes archive hash drift for Vault door mapping")
    texture_paths = []
    texture_rows = []
    for archive_recipe in target["textureArchives"]:
        archive_path = fnv_data_root / archive_recipe["file"]
        actual_hash = sha256_path(archive_path)
        if actual_hash != archive_recipe["sha256"]:
            raise Fo1ProfileError(f"FNV texture archive hash drift: {archive_recipe['file']}")
        texture_paths.append(archive_path)
        texture_rows.append(
            {
                "file": archive_path.name,
                "bytes": archive_path.stat().st_size,
                "sha256": actual_hash,
            }
        )
    catalog = scan_cell_catalog(master_path)
    base_form_id = parse_form_id(target["baseFormId"], "Vault door target baseFormId")
    base = catalog.base_objects.get(base_form_id)
    if base is None or base.record_type != target["recordType"] or base.editor_id != target["editorId"] or base.model_path != target["modelPath"]:
        raise Fo1ProfileError("FNV Vault gear-door base identity drift")
    member = BsaArchive(meshes_path).extract(target["logicalPath"])
    if member.sha256 != target["sourceNifSha256"]:
        raise Fo1ProfileError("FNV Vault gear-door NIF hash drift")

    cache_root.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=cache_root.name + ".", dir=cache_root.parent))
    try:
        source_path = staging / "source" / Path(member.logical_path.replace("\\", "/"))
        source_path.parent.mkdir(parents=True, exist_ok=True)
        source_path.write_bytes(member.data)
        generated = staging / "generated"
        generated.mkdir(parents=True)
        model = generated / "vault13-gear-door.gltf"
        sidecar = generated / "vault13-gear-door.opennv.json"
        export = export_static_nif(
            source_path,
            member.logical_path,
            model,
            sidecar,
            strict=False,
            include_shape_prefixes=tuple(recipe["staticPoseShapePrefixes"]),
        )
        controllers = export["coverage"]["controllers"]
        if controllers != recipe["staticPoseControllerInventory"]:
            raise Fo1ProfileError(
                f"FNV Vault door controller inventory drift: expected {recipe['staticPoseControllerInventory']}, got {controllers}"
            )
        requested_textures = sorted(
            {
                texture
                for surface in export["surfaces"]
                for texture in surface["textures"]
                if texture
            }
        )
        texture_pipeline = TexturePipeline(texture_paths, staging, {})
        texture_artifacts = {
            requested: texture_pipeline.prepare(requested) for requested in requested_textures
        }
        bindings = []
        for surface_index, surface in enumerate(export["surfaces"]):
            textures = surface["textures"]
            diffuse = textures[0] if len(textures) > 0 and textures[0] else None
            normal = textures[1] if len(textures) > 1 and textures[1] else None
            emissive = textures[2] if len(textures) > 2 and textures[2] else None
            material = surface["material"]
            environment, environment_mask = environment_texture_paths(surface)
            glossiness = float(material.get("glossiness", 10.0))
            specular = [float(value) for value in material.get("specular", [0.0, 0.0, 0.0])]
            roughness = (
                1.0
                if max(specular) <= 1.0e-6
                else max(0.08, min(1.0, math.sqrt(2.0 / (glossiness + 2.0))))
            )
            unshaded = "BSShaderNoLightingProperty" in surface["propertyTypes"]
            emissive_color = [float(value) for value in material.get("emissive", [0.0, 0.0, 0.0])]
            emissive_controlled = bool(material.get("emissiveControlled", False))
            emissive_active = not unshaded and (emissive is not None or emissive_controlled)
            emission_texture = emissive if emissive_active else None
            if not emissive_active:
                emissive_color = [0.0, 0.0, 0.0]
            alpha = float(material.get("alpha", 1.0))
            bindings.append(
                {
                    "surfaceIndex": surface_index,
                    "name": surface["name"],
                    "diffuseTextureId": texture_artifacts[diffuse].asset_id if diffuse else None,
                    "normalTextureId": texture_artifacts[normal].asset_id if normal else None,
                    "emissiveTextureId": (
                        texture_artifacts[emission_texture].asset_id if emission_texture else None
                    ),
                    "environmentTextureId": (
                        texture_artifacts[environment].asset_id if environment else None
                    ),
                    "environmentMaskTextureId": (
                        texture_artifacts[environment_mask].asset_id if environment_mask else None
                    ),
                    "environmentMapScale": float(material.get("environmentMapScale", 1.0)),
                    "emissiveColor": emissive_color,
                    "emissiveReplace": emissive_controlled and emissive is None,
                    "baseColorFactor": [
                        *[float(value) for value in material.get("baseColor", [1.0, 1.0, 1.0])],
                        alpha,
                    ],
                    "roughness": roughness,
                    "alphaContract": material["alphaContract"],
                    "vertexColorMode": material["vertexColorMode"],
                    "doubleSided": int(material.get("stencilDrawMode", 1)) == 3,
                    "unshaded": unshaded,
                }
            )

        def relocated_texture(artifact: object) -> dict[str, object]:
            row = artifact.manifest()
            row["png"] = str((cache_root / artifact.png_path.relative_to(staging)).resolve())
            if "cubeFaces" in row:
                row["cubeFaces"] = [
                    {
                        **face,
                        "png": str((cache_root / Path(face["png"]).relative_to(staging)).resolve()),
                    }
                    for face in row["cubeFaces"]
                ]
            return row

        material_manifest_path = generated / "vault13-gear-door.materials.json"
        material_manifest = {
            "schema": "opennv-static-material-manifest/v1",
            "textures": [
                relocated_texture(texture_artifacts[path]) for path in sorted(texture_artifacts)
            ],
            "asset": {
                "id": "fo1-vault13-gear-door",
                "materials": bindings,
            },
        }
        _atomic_json(material_manifest_path, material_manifest)
        material_manifest_hash = hashlib.sha256(material_manifest_path.read_bytes()).hexdigest()
        manifest = {
            "schema": MANIFEST_SCHEMA,
            "status": "transported-static-pose",
            "recipe": {"id": recipe["id"], "sha256": sha256_path(recipe_path)},
            "sourceObjectContract": {
                "file": object_contract_path.name,
                "sha256": object_contract_hash,
                "door": expected_source,
                "frame": recipe["sourceFrame"],
            },
            "target": {
                **target,
                "textureArchiveFiles": texture_rows,
                "archiveOffset": member.archive_offset,
                "storedBytes": member.stored_bytes,
                "compressed": member.compressed,
            },
            "outputs": {
                "model": str((cache_root / "generated" / model.name).resolve()),
                "sidecar": str((cache_root / "generated" / sidecar.name).resolve()),
                "modelSha256": export["outputs"]["gltf"]["sha256"],
                "bufferSha256": export["outputs"]["buffer"]["sha256"],
                "materialManifest": str(
                    (cache_root / "generated" / material_manifest_path.name).resolve()
                ),
                "materialManifestSha256": material_manifest_hash,
            },
            "controllerInventory": controllers,
            "unsupported": recipe["unsupported"],
        }
        _atomic_json(staging / "door-proof-manifest.json", manifest)
        os.replace(staging, cache_root)
        return manifest
    except Exception:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--object-contract", type=Path, required=True)
    parser.add_argument("--fnv-data-root", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    args = parser.parse_args()
    manifest = build(
        args.recipe.resolve(),
        args.object_contract.resolve(),
        args.fnv_data_root.resolve(),
        args.cache_root.resolve(),
    )
    print(json.dumps(manifest, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
