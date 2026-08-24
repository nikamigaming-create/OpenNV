"""Direct DDS extraction and deterministic PNG cache preparation."""

from __future__ import annotations

import hashlib
import os
import struct
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageOps

from bsa_archive import BsaArchive, canonical_member_path
from runtime_configuration import ContentCompilerConfiguration


DDS_HEADER_BYTES = 128
DDS_CAPABILITIES_TWO_OFFSET = 112
DDS_CUBEMAP_FLAG = 0x0200
DDS_ALL_CUBEMAP_FACES_MASK = 0xFC00
DDS_CUBEMAP_FACE_COUNT = 6
DDS_CUBEMAP_GODOT_FACE_ORDER = (0, 1, 4, 5, 3, 2)

@dataclass(frozen=True)
class TextureArtifact:
    asset_id: str
    requested_path: str
    archive_path: str
    source_sha256: str
    png_path: Path
    png_sha256: str
    width: int
    height: int
    normal_green_inverted: bool
    cube_face_paths: tuple[Path, ...] = ()
    cube_face_sha256: tuple[str, ...] = ()

    def manifest(self) -> dict[str, object]:
        result = {
            "id": self.asset_id,
            "requestedPath": self.requested_path,
            "archivePath": self.archive_path,
            "sourceSha256": self.source_sha256,
            "png": str(self.png_path.resolve()),
            "pngSha256": self.png_sha256,
            "width": self.width,
            "height": self.height,
            "normalGreenInverted": self.normal_green_inverted,
        }
        if self.cube_face_paths:
            result["cubeFaces"] = [
                {
                    "png": str(path.resolve()),
                    "pngSha256": sha256,
                }
                for path, sha256 in zip(self.cube_face_paths, self.cube_face_sha256)
            ]
        return result


def file_sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


class TexturePipeline:
    def __init__(
        self,
        archives: list[Path],
        cache_root: Path,
        aliases: dict[str, str],
        compiler: ContentCompilerConfiguration,
    ):
        self.archives = [BsaArchive(path) for path in archives]
        self.cache_root = cache_root
        self.compiler = compiler
        self.aliases = {
            canonical_member_path(source): canonical_member_path(target)
            for source, target in aliases.items()
        }

    def prepare(self, requested_path: str) -> TextureArtifact:
        requested = canonical_member_path(requested_path)
        archive_path = self.aliases.get(requested, requested)
        matches = [archive for archive in self.archives if archive_path in archive.members]
        if len(matches) != 1:
            raise FileNotFoundError(
                f"Expected one texture member {archive_path!r}, found {len(matches)} archives"
            )
        member = matches[0].extract(archive_path)
        source_path = self.cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
        _atomic_bytes(source_path, member.data)

        asset_id = hashlib.sha256(requested.encode()).hexdigest()[
            :self.compiler.asset_id_hex_characters
        ]
        png_path = self.cache_root / "generated" / "textures" / f"{asset_id}.png"
        normal_green_inverted = requested.endswith("_n.dds")
        cube_images = decode_dds_cubemap(member.data)
        image = (
            cube_images[0]
            if cube_images
            else decode_dds(member.data, normal_green_inverted)
        )
        _atomic_png(png_path, image, self.compiler.png_compression_level)
        cube_paths = tuple(
            png_path.with_name(f"{asset_id}-cube-{index}.png")
            for index in range(len(cube_images))
        )
        for path, cube_image in zip(cube_paths, cube_images):
            _atomic_png(path, cube_image, self.compiler.png_compression_level)
        width, height = image.size
        return TextureArtifact(
            asset_id,
            requested,
            archive_path,
            member.sha256,
            png_path,
            file_sha256(png_path),
            width,
            height,
            normal_green_inverted,
            cube_paths,
            tuple(file_sha256(path) for path in cube_paths),
        )


def decode_dds(payload: bytes, invert_normal_green: bool) -> Image.Image:
    with Image.open(BytesIO(payload)) as source:
        if source.format != "DDS":
            raise ValueError("Texture payload is not DDS")
        image = source.convert("RGBA")
    if invert_normal_green:
        red, green, blue, alpha = image.split()
        image = Image.merge("RGBA", (red, ImageOps.invert(green), blue, alpha))
    return image


def decode_dds_cubemap(payload: bytes) -> list[Image.Image]:
    if len(payload) < DDS_HEADER_BYTES or payload[:4] != b"DDS ":
        raise ValueError("Texture payload is not DDS")
    caps_two = struct.unpack_from("<I", payload, DDS_CAPABILITIES_TWO_OFFSET)[0]
    if not caps_two & DDS_CUBEMAP_FLAG:
        return []
    if caps_two & DDS_ALL_CUBEMAP_FACES_MASK != DDS_ALL_CUBEMAP_FACES_MASK:
        raise ValueError("DDS cubemap does not contain all six faces")
    face_payload = payload[DDS_HEADER_BYTES:]
    if len(face_payload) % DDS_CUBEMAP_FACE_COUNT:
        raise ValueError("DDS cubemap face payloads are not equal in size")
    face_size = len(face_payload) // DDS_CUBEMAP_FACE_COUNT
    header = bytearray(payload[:DDS_HEADER_BYTES])
    struct.pack_into("<I", header, DDS_CAPABILITIES_TWO_OFFSET, 0)
    source_faces = [
        decode_dds(bytes(header) + face_payload[index * face_size : (index + 1) * face_size], False)
        for index in range(DDS_CUBEMAP_FACE_COUNT)
    ]
    return [source_faces[index] for index in DDS_CUBEMAP_GODOT_FACE_ORDER]


def _atomic_bytes(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def _atomic_png(path: Path, image: Image.Image, compression_level: int) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    image.save(
        temporary,
        format="PNG",
        optimize=True,
        compress_level=compression_level,
    )
    os.replace(temporary, path)
