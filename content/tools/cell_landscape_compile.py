"""Compile one corpus-bound LAND into the shared per-CELL artifact contract."""

from __future__ import annotations

import json
from pathlib import Path

from bsa_archive import canonical_member_path
from cell_landscape_contract import landscape_contract_for
from cell_static_contract import (
    LANDSCAPE_ASSET_KIND,
    LANDSCAPE_RUNTIME_TEXTURE_KIND,
    LANDSCAPE_TEXTURE_KIND,
)
from landscape_gltf import (
    canonical_texture_path,
    export_landscape_gltf,
    landscape_baked_source_hash,
)
from landscape_stack import resolve_owned_landscape
from owned_archive_stack import OwnedArchiveStack
from plugin_stack import file_sha256
from runtime_configuration import ContentCompilerConfiguration
from texture_pipeline import OwnedTexturePipeline, TextureArtifact


class TrackingTexturePipeline:
    def __init__(self, pipeline: OwnedTexturePipeline):
        self.pipeline = pipeline
        self.artifacts: list[TextureArtifact] = []

    def prepare(self, requested_path: str) -> TextureArtifact:
        artifact = self.pipeline.prepare(requested_path)
        self.artifacts.append(artifact)
        return artifact


def compile_landscape(
    data_root: Path,
    corpus_manifest: dict[str, object],
    cell: dict[str, object],
    child: dict[str, object],
    archives: OwnedArchiveStack,
    staging_root: Path,
    origin: tuple[float, float, float],
    compiler_configuration: ContentCompilerConfiguration,
    texture_aliases: dict[str, str],
) -> tuple[dict[str, object], list[dict[str, object]], dict[str, object]]:
    source = resolve_owned_landscape(
        data_root,
        corpus_manifest,
        cell,
        child,
    )
    coordinates = tuple(int(value) for value in cell["coordinates"])
    if len(coordinates) != 2:
        raise ValueError(f"LAND CELL coordinates differ: {cell['formKey']}")
    tracking = TrackingTexturePipeline(
        OwnedTexturePipeline(
            archives,
            staging_root,
            texture_aliases,
            compiler_configuration,
        )
    )
    provisional_root = staging_root / "generated" / "landscape"
    exported = export_landscape_gltf(
        source.landscape,
        source.textures,
        coordinates,
        origin,
        tracking,
        provisional_root,
        compiler_configuration,
        identity=source.identity,
        texture_output_root=staging_root / "generated" / "textures",
    )
    raw_asset = exported.asset
    baked = exported.diagnostic_bake
    asset_id = str(raw_asset["id"])
    asset_root = staging_root / "generated" / "assets" / asset_id
    asset_root.mkdir(parents=True, exist_ok=True)
    model_path = Path(str(raw_asset["model"]))
    sidecar_path = Path(str(raw_asset["sidecar"]))
    buffer_path = model_path.with_suffix(".bin")
    moved_paths = {
        source_path: asset_root / source_path.name
        for source_path in (model_path, buffer_path, sidecar_path)
    }
    for source_path, target_path in moved_paths.items():
        source_path.replace(target_path)
    sidecar_path = moved_paths[sidecar_path]
    sidecar = json.loads(sidecar_path.read_text(encoding="utf-8"))
    outputs = {
        name: {
            "file": _relative(asset_root / str(descriptor["file"]), staging_root),
            "bytes": int(descriptor["bytes"]),
            "sha256": str(descriptor["sha256"]),
        }
        for name, descriptor in sidecar["outputs"].items()
    }
    outputs["sidecar"] = {
        "file": _relative(sidecar_path, staging_root),
        "bytes": sidecar_path.stat().st_size,
        "sha256": file_sha256(sidecar_path),
    }
    baked_path = Path(str(baked["png"]))
    aliases = {
        canonical_member_path(source): canonical_member_path(target)
        for source, target in texture_aliases.items()
    }
    for source_contract in baked["sources"]:
        normal_path = source_contract.get("normalPath")
        normal_source = None
        if normal_path:
            requested = canonical_texture_path(str(normal_path))
            archive_path = aliases.get(requested, requested)
            member = archives.extract(archive_path)
            normal_source = {
                "requestedPath": requested,
                "archivePath": archive_path,
                "sourceSha256": member.sha256,
                "sourceBytes": len(member.data),
                "sourceArchive": member.source_archive,
                "sourceArchiveSha256": member.source_archive_sha256,
            }
        source_contract["normalSource"] = normal_source
    baked["sourceSha256"] = landscape_baked_source_hash(
        source.landscape,
        baked["sources"],
    )
    baked["sourceBytes"] = len(source.landscape.source_bytes) + sum(
        int(row["sourceBytes"])
        + (
            0
            if row["normalSource"] is None
            else int(row["normalSource"]["sourceBytes"])
        )
        for row in baked["sources"]
    )
    diagnostic_texture = {
        "textureKind": LANDSCAPE_TEXTURE_KIND,
        "textureId": baked["id"],
        "requestedPath": baked["requestedPath"],
        "archivePath": None,
        "sourceSha256": baked["sourceSha256"],
        "sourceBytes": baked["sourceBytes"],
        "sourceArchive": None,
        "sourceArchiveSha256": None,
        "png": _relative(baked_path, staging_root),
        "pngBytes": baked_path.stat().st_size,
        "pngSha256": baked["pngSha256"],
        "width": baked["width"],
        "height": baked["height"],
        "normalGreenInverted": False,
        "cubeFaces": [],
        "diagnosticOnly": True,
        "sources": baked["sources"],
        "bakeContract": baked["bakeContract"],
    }
    runtime_textures = [
        _runtime_texture_row(manifest, staging_root)
        for manifest in exported.runtime_textures
    ]
    landscape_contract = landscape_contract_for(source, cell, origin)
    asset = {
        "assetKind": LANDSCAPE_ASSET_KIND,
        "assetId": asset_id,
        "requestedModelPath": None,
        "logicalPath": raw_asset["logicalPath"],
        "sourcePlugin": source.identity.source_plugin,
        "sourceLocalFormId": source.identity.source_local_form_id,
        "sourceArchive": None,
        "sourceArchiveSha256": None,
        "sourceBytes": len(source.landscape.source_bytes),
        "sourceSha256": raw_asset["sourceSha256"],
        "outputs": outputs,
        "coverage": sidecar["coverage"],
        "surfaces": sidecar["surfaces"],
        "textureBindings": [
            {
                "requestedPath": texture["requestedPath"],
                "textureId": texture["id"],
            }
            for texture in runtime_textures
        ],
        "materials": raw_asset["materials"],
        "collision": raw_asset["collision"],
        "landscape": landscape_contract,
    }
    return asset, [diagnostic_texture, *runtime_textures], landscape_contract


def _runtime_texture_row(
    manifest: dict[str, object],
    staging_root: Path,
) -> dict[str, object]:
    png = Path(str(manifest["png"]))
    cube_faces = [
        {
            "png": _relative(Path(str(face["png"])), staging_root),
            "bytes": Path(str(face["png"])).stat().st_size,
            "pngSha256": str(face["pngSha256"]),
        }
        for face in manifest.get("cubeFaces", [])
    ]
    return {
        "textureKind": LANDSCAPE_RUNTIME_TEXTURE_KIND,
        "textureId": str(manifest["id"]),
        "requestedPath": str(manifest["requestedPath"]),
        "archivePath": manifest.get("archivePath"),
        "sourceSha256": str(manifest["sourceSha256"]),
        "sourceBytes": int(manifest["sourceBytes"]),
        "sourceArchive": manifest.get("sourceArchive"),
        "sourceArchiveSha256": manifest.get("sourceArchiveSha256"),
        "png": _relative(png, staging_root),
        "pngBytes": png.stat().st_size,
        "pngSha256": str(manifest["pngSha256"]),
        "width": int(manifest["width"]),
        "height": int(manifest["height"]),
        "normalGreenInverted": bool(manifest["normalGreenInverted"]),
        "cubeFaces": cube_faces,
        "landscapeRole": str(manifest.get("landscapeRole", "diffuse")),
    }
def _relative(path: Path, root: Path) -> str:
    return path.resolve().relative_to(root.resolve()).as_posix()
