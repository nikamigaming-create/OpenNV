"""Compile owned Fallout first-person hands into independently tracked skinned rigs."""

from __future__ import annotations

import hashlib
from pathlib import Path

from actor_gltf import ActorComponent, ActorGltfInput, export_actor_gltf
from bsa_archive import BsaArchive, canonical_member_path
from owned_archive_stack import OwnedArchiveStack
from runtime_configuration import ContentCompilerConfiguration


FIRST_PERSON_RIG_SCHEMA = "opennv-first-person-rig/v1"
FIRST_PERSON_RIG_STATUS = "owned-data-skinned-hands"
HAND_ROLES = ("left", "right")


def _member(archive: BsaArchive | OwnedArchiveStack, logical_path: str):
    canonical = canonical_member_path(logical_path)
    path = canonical if canonical.startswith("meshes\\") else f"meshes\\{canonical}"
    return archive.extract(path)


def _file_sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def prepare_first_person_rig(
    meshes_path: Path,
    texture_archive_paths: list[Path],
    cache_root: Path,
    recipe: dict[str, object],
    compiler: ContentCompilerConfiguration,
    owned_archives: OwnedArchiveStack | None = None,
) -> dict[str, object]:
    configured = recipe.get("firstPersonRig")
    if not isinstance(configured, dict):
        raise ValueError("Cell recipe requires one firstPersonRig object")

    meshes = owned_archives if owned_archives is not None else BsaArchive(meshes_path)
    texture_archives = (
        [owned_archives]
        if owned_archives is not None
        else [BsaArchive(path) for path in texture_archive_paths]
    )
    skeleton = _member(meshes, str(configured["skeletonPath"]))
    animation = _member(meshes, str(configured["poseAnimationPath"]))
    skeleton_root_node = str(configured["skeletonRootBone"])
    rigid_attachment_node = str(configured["rigidAttachmentBone"])
    biped_head_node = str(configured["bipedHeadBone"])
    if not skeleton_root_node or not rigid_attachment_node or not biped_head_node:
        raise ValueError("First-person rig node identities must be explicit")
    output_root = cache_root / "generated" / "cells" / str(recipe["id"]) / "first-person"
    hands: dict[str, object] = {}
    for role in HAND_ROLES:
        role_configuration = configured[role]
        if not isinstance(role_configuration, dict):
            raise ValueError(f"First-person {role} hand configuration must be an object")
        model = _member(meshes, str(role_configuration["modelPath"]))
        gltf_path = output_root / f"{role}-hand.gltf"
        sidecar_path = output_root / f"{role}-hand.opennv.json"
        sidecar = export_actor_gltf(
            ActorGltfInput(
                actor_form_id=f"first-person-{role}",
                actor_name=f"Retail {role} first-person hand",
                skeleton_path=skeleton.logical_path,
                skeleton_payload=skeleton.data,
                symmetric_geometry=(),
                asymmetric_geometry=(),
                components=(
                    ActorComponent(
                        role=f"{role}-hand",
                        model_path=model.logical_path,
                        model_payload=model.data,
                    ),
                ),
                idle_animation_path=animation.logical_path,
                idle_animation_payload=animation.data,
                skeleton_root_node=skeleton_root_node,
                rigid_attachment_node=rigid_attachment_node,
                biped_head_node=biped_head_node,
            ),
            texture_archives,
            gltf_path,
            sidecar_path,
            compiler,
        )
        hands[role] = {
            "model": str(gltf_path.resolve()),
            "sidecar": str(sidecar_path.resolve()),
            "modelSha256": sidecar["outputs"]["gltf"]["sha256"],
            "sidecarSha256": _file_sha256(sidecar_path),
            "sourceModelPath": model.logical_path,
            "sourceModelSha256": model.sha256,
            "sourceArchive": getattr(model, "source_archive", None),
            "sourceArchiveSha256": getattr(model, "source_archive_sha256", None),
            "gripBone": str(role_configuration["gripBone"]),
        }

    return {
        "schema": FIRST_PERSON_RIG_SCHEMA,
        "status": FIRST_PERSON_RIG_STATUS,
        "provider": "retail-first-person-skinned-hands",
        "skeletonPath": skeleton.logical_path,
        "skeletonSha256": skeleton.sha256,
        "skeletonSourceArchive": getattr(skeleton, "source_archive", None),
        "skeletonSourceArchiveSha256": getattr(
            skeleton, "source_archive_sha256", None
        ),
        "poseAnimationPath": animation.logical_path,
        "poseAnimationSha256": animation.sha256,
        "poseAnimationSourceArchive": getattr(animation, "source_archive", None),
        "poseAnimationSourceArchiveSha256": getattr(
            animation, "source_archive_sha256", None
        ),
        "cameraBone": str(configured["cameraBone"]),
        "weaponBone": str(configured["weaponBone"]),
        "hands": hands,
    }
