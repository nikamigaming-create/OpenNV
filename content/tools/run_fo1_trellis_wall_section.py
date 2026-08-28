#!/usr/bin/env python3
"""Run the configured local TRELLIS.2 wall-section job through ComfyUI."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

from fo1_profile import sha256_path


RECIPE_SCHEMA = "opennv-fo1-ai-wall-reconstruction-recipe/v1"
SECTION_SCHEMA = "opennv-fo1-ai-wall-section/v1"
RUN_SCHEMA = "opennv-fo1-trellis-wall-run/v1"


def read_json(path: Path) -> dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def request_json(
    url: str,
    method: str = "GET",
    document: object | None = None,
    timeout: float = 30.0,
) -> dict[str, object]:
    data = None
    headers = {}
    if document is not None:
        data = json.dumps(document).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        payload = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"ComfyUI HTTP {error.code}: {payload}") from error


def build_prompt(
    generation: dict[str, object],
    input_name: str,
    output_prefix: str,
) -> dict[str, object]:
    postprocess = generation["meshPostprocess"]
    texture_bake = generation["textureBake"]
    return {
        "1": {
            "class_type": "Trellis2LoadImageWithTransparency",
            "inputs": {"image": input_name},
        },
        "2": {
            "class_type": "Trellis2PreProcessImage",
            "inputs": {
                "image": ["1", 2],
                "padding": int(generation["inputPaddingPixels"]),
                "remove_background": bool(generation["removeBackground"]),
                "max_size": int(generation["inputMaximumPixels"]),
            },
        },
        "3": {
            "class_type": "Trellis2LoadModel",
            "inputs": {
                "modelname": generation["model"],
                "backend": generation["attentionBackend"],
                "device": generation["device"],
                "low_vram": bool(generation["lowVram"]),
                "keep_models_loaded": bool(generation["keepModelsLoaded"]),
                "conv_backend": generation["convolutionBackend"],
                "sparse_backend": generation["sparseAttentionBackend"],
                "use_reconviagen": bool(generation["useReconViaGen"]),
            },
        },
        "4": {
            "class_type": "Trellis2MeshWithVoxelGenerator",
            "inputs": {
                "pipeline": ["3", 0],
                "image": ["2", 0],
                "seed": int(generation["seed"]),
                "pipeline_type": generation["pipelineType"],
                "sparse_structure_steps": int(generation["sparseStructureSteps"]),
                "shape_steps": int(generation["shapeSteps"]),
                "texture_steps": int(generation["textureSteps"]),
                "max_num_tokens": int(generation["maximumTokens"]),
                "max_views": int(generation["maximumViews"]),
                "sparse_structure_resolution": int(
                    generation["sparseStructureResolution"]
                ),
                "generate_texture_slat": bool(generation["generateTextureLatent"]),
                "use_tiled_decoder": bool(generation["useTiledDecoder"]),
                "sampler": generation["sampler"],
                "fill_holes": bool(generation["fillHoles"]),
                "hole_iterations": int(generation["holeIterations"]),
                "hole_fill_algorithm": generation["holeFillAlgorithm"],
                "keep_only_shell": bool(generation["keepOnlyShell"]),
            },
        },
        "5": {
            "class_type": "Trellis2PostProcessMesh",
            "inputs": {
                "mesh": ["4", 0],
                "remove_duplicate_faces": bool(postprocess["removeDuplicateFaces"]),
                "repair_non_manifold_edges": bool(postprocess["repairNonManifoldEdges"]),
                "remove_non_manifold_faces": bool(postprocess["removeNonManifoldFaces"]),
                "remove_small_connected_components": bool(
                    postprocess["removeSmallConnectedComponents"]
                ),
                "remove_small_connected_components_size": float(
                    postprocess["minimumConnectedComponentSize"]
                ),
                "unify_faces_orientation": bool(postprocess["unifyFacesOrientation"]),
                "remove_floaters": bool(postprocess["removeFloaters"]),
                "remove_infinite_vertices": bool(postprocess["removeInfiniteVertices"]),
                "merge_vertices": bool(postprocess["mergeVertices"]),
                "merge_distance": float(postprocess["mergeDistance"]),
                "remove_nan_vertices": bool(postprocess["removeNanVertices"]),
            },
        },
        "6": {
            "class_type": "Trellis2SimplifyMesh",
            "inputs": {
                "mesh": ["5", 0],
                "target_face_num": int(generation["targetFaces"]),
                "method": generation["simplificationMethod"],
            },
        },
        "7": {
            "class_type": "Trellis2UnWrapAndRasterizer",
            "inputs": {
                "mesh": ["6", 0],
                "mesh_cluster_threshold_cone_half_angle_rad": float(
                    texture_bake["meshClusterConeHalfAngleDegrees"]
                ),
                "mesh_cluster_refine_iterations": int(
                    texture_bake["meshClusterRefineIterations"]
                ),
                "mesh_cluster_global_iterations": int(
                    texture_bake["meshClusterGlobalIterations"]
                ),
                "mesh_cluster_smooth_strength": int(
                    texture_bake["meshClusterSmoothStrength"]
                ),
                "texture_size": int(texture_bake["textureSizePixels"]),
                "texture_alpha_mode": texture_bake["alphaMode"],
                "double_side_material": bool(texture_bake["doubleSided"]),
                "bake_on_vertices": bool(texture_bake["bakeOnVertices"]),
                "use_custom_normals": bool(texture_bake["useCustomNormals"]),
                "bvh": ["4", 1],
                "inpainting": texture_bake["inpainting"],
                "reorient_vertices": texture_bake["reorientVertices"],
            },
        },
        "8": {
            "class_type": "Trellis2ExportMesh",
            "inputs": {
                "trimesh": ["7", 0],
                "filename_prefix": output_prefix,
                "file_format": generation["exportFormat"],
            },
        },
    }


def run(
    server: str,
    comfy_root: Path,
    recipe_path: Path,
    section_manifest_path: Path,
    conditioned_image_path: Path,
    output_root: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise ValueError(f"refusing to overwrite Fallout TRELLIS wall run: {output_root}")
    recipe = read_json(recipe_path)
    section = read_json(section_manifest_path)
    if recipe.get("schema") != RECIPE_SCHEMA or section.get("schema") != SECTION_SCHEMA:
        raise ValueError("unexpected Fallout TRELLIS input contract")
    source_recipe_hash = section["source"]["recipeSha256"]
    if source_recipe_hash != sha256_path(recipe_path):
        raise ValueError("Fallout AI wall recipe changed after source composition")
    if not conditioned_image_path.is_file():
        raise ValueError("conditioned Fallout wall image is missing")

    generation = recipe["geometryGeneration"]
    poll_interval = float(generation["pollIntervalSeconds"])
    maximum_wait = float(generation["maximumWaitSeconds"])
    if poll_interval <= 0.0 or maximum_wait <= poll_interval:
        raise ValueError("Fallout TRELLIS polling contract is invalid")

    object_info = request_json(f"{server.rstrip('/')}/object_info")
    node_names = {
        "Trellis2LoadImageWithTransparency",
        "Trellis2PreProcessImage",
        "Trellis2LoadModel",
        "Trellis2MeshWithVoxelGenerator",
        "Trellis2PostProcessMesh",
        "Trellis2SimplifyMesh",
        "Trellis2UnWrapAndRasterizer",
        "Trellis2ExportMesh",
    }
    missing = sorted(node_names - set(object_info))
    if missing:
        raise ValueError(f"ComfyUI is missing Fallout TRELLIS nodes: {missing}")

    input_hash = sha256_path(conditioned_image_path)
    input_name = f"fo1-v13ent-entry-wall-{input_hash[:16]}.png"
    comfy_input = comfy_root / "input" / input_name
    comfy_input.parent.mkdir(parents=True, exist_ok=True)
    if comfy_input.exists() and sha256_path(comfy_input) != input_hash:
        raise ValueError(f"ComfyUI input-name collision: {comfy_input}")
    if not comfy_input.exists():
        shutil.copyfile(conditioned_image_path, comfy_input)

    recipe_hash = sha256_path(recipe_path)
    output_prefix = f"fo1-v13ent/entry-wall-{input_hash[:16]}-{recipe_hash[:8]}"
    prompt = build_prompt(generation, input_name, output_prefix)
    client_id = str(uuid.uuid4())
    submitted = request_json(
        f"{server.rstrip('/')}/prompt",
        method="POST",
        document={"prompt": prompt, "client_id": client_id},
    )
    prompt_id = str(submitted["prompt_id"])
    started = time.monotonic()
    history_row = None
    while time.monotonic() - started <= maximum_wait:
        history = request_json(f"{server.rstrip('/')}/history/{prompt_id}")
        if prompt_id in history:
            history_row = history[prompt_id]
            break
        time.sleep(poll_interval)
    if history_row is None:
        raise TimeoutError(f"TRELLIS wall generation exceeded {maximum_wait:.1f} seconds")
    status = history_row.get("status", {})
    if status.get("status_str") != "success" or not status.get("completed"):
        raise RuntimeError(f"TRELLIS wall generation failed: {status}")

    generated_root = comfy_root / "output" / "fo1-v13ent"
    candidates = sorted(
        generated_root.glob(
            f"entry-wall-{input_hash[:16]}-{recipe_hash[:8]}*.{generation['exportFormat']}"
        )
    )
    if len(candidates) != 1:
        raise RuntimeError(f"expected one TRELLIS wall GLB, found {len(candidates)}")
    output_root.mkdir(parents=True)
    final_glb = output_root / "entry-wall-trellis-v1.glb"
    shutil.copyfile(candidates[0], final_glb)
    report = {
        "schema": RUN_SCHEMA,
        "status": "generated-unaccepted-awaiting-blender-and-canonical-visual-gates",
        "source": {
            "recipe": str(recipe_path.resolve()),
            "recipeSha256": sha256_path(recipe_path),
            "sectionManifest": str(section_manifest_path.resolve()),
            "sectionManifestSha256": sha256_path(section_manifest_path),
            "conditionedImage": str(conditioned_image_path.resolve()),
            "conditionedImageSha256": input_hash,
        },
        "generator": {
            **generation,
            "server": server,
            "promptId": prompt_id,
            "clientId": client_id,
            "elapsedSeconds": time.monotonic() - started,
            "apiPrompt": prompt,
        },
        "artifact": {
            "path": str(final_glb.resolve()),
            "sha256": sha256_path(final_glb),
            "bytes": final_glb.stat().st_size,
        },
        "acceptance": recipe["acceptance"],
    }
    write_json(output_root / "trellis-wall-run.json", report)
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", default="http://127.0.0.1:8190")
    parser.add_argument("--comfy-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--section-manifest", type=Path, required=True)
    parser.add_argument("--conditioned-image", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    report = run(
        args.server,
        args.comfy_root.resolve(),
        args.recipe.resolve(),
        args.section_manifest.resolve(),
        args.conditioned_image.resolve(),
        args.output_root.resolve(),
    )
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
