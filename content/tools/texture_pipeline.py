"""Direct DDS extraction and deterministic PNG cache preparation."""

from __future__ import annotations

import hashlib
import os
import struct
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageOps

from bsa_archive import BsaArchive, ExtractedMember, canonical_member_path
from owned_archive_stack import OwnedArchiveStack
from runtime_configuration import ContentCompilerConfiguration


DDS_HEADER_BYTES = 128
DDS_FLAGS_OFFSET = 8
DDS_HEIGHT_OFFSET = 12
DDS_WIDTH_OFFSET = 16
DDS_LINEAR_SIZE_OFFSET = 20
DDS_MIP_MAP_COUNT_OFFSET = 28
DDS_PIXEL_FORMAT_FOURCC_OFFSET = 84
DDS_CAPABILITIES_ONE_OFFSET = 108
DDS_CAPABILITIES_TWO_OFFSET = 112
DDS_MIP_MAP_COUNT_FLAG = 0x00020000
DDS_CAPABILITY_COMPLEX = 0x00000008
DDS_CAPABILITY_TEXTURE = 0x00001000
DDS_CAPABILITY_MIPMAP = 0x00400000
DDS_CUBEMAP_FLAG = 0x0200
DDS_ALL_CUBEMAP_FACES_MASK = 0xFC00
DDS_CUBEMAP_FACE_COUNT = 6
DDS_CUBEMAP_GODOT_FACE_ORDER = (0, 1, 4, 5, 3, 2)
DDS_BLOCK_BYTES_BY_FOURCC = {
    b"DXT1": 8,
    b"DXT3": 16,
    b"DXT5": 16,
}
BYTE_CHANNEL_MAXIMUM = 255

@dataclass(frozen=True)
class TextureArtifact:
    asset_id: str
    requested_path: str
    archive_path: str
    source_sha256: str
    source_bytes: int
    dds_path: Path
    authored_mip_count: int
    rgba8_mip_path: Path | None
    png_path: Path
    png_sha256: str
    width: int
    height: int
    normal_green_inverted: bool
    cube_face_paths: tuple[Path, ...] = ()
    cube_face_sha256: tuple[str, ...] = ()
    source_archive: str | None = None
    source_archive_sha256: str | None = None

    def manifest(self) -> dict[str, object]:
        result = {
            "id": self.asset_id,
            "requestedPath": self.requested_path,
            "archivePath": self.archive_path,
            "sourceSha256": self.source_sha256,
            "sourceBytes": self.source_bytes,
            "dds": str(self.dds_path.resolve()),
            "ddsBytes": self.dds_path.stat().st_size,
            "ddsSha256": file_sha256(self.dds_path),
            "authoredMipCount": self.authored_mip_count,
            "png": str(self.png_path.resolve()),
            "pngSha256": self.png_sha256,
            "width": self.width,
            "height": self.height,
            "normalGreenInverted": self.normal_green_inverted,
            "sourceArchive": self.source_archive,
            "sourceArchiveSha256": self.source_archive_sha256,
        }
        if self.cube_face_paths:
            result["cubeFaces"] = [
                {
                    "png": str(path.resolve()),
                    "pngSha256": sha256,
                }
                for path, sha256 in zip(self.cube_face_paths, self.cube_face_sha256)
            ]
        if self.rgba8_mip_path is not None:
            result.update(
                {
                    "rgba8MipChain": str(self.rgba8_mip_path.resolve()),
                    "rgba8MipChainBytes": self.rgba8_mip_path.stat().st_size,
                    "rgba8MipChainSha256": file_sha256(self.rgba8_mip_path),
                    "rgba8MipChainFormat": "RGBA8-authored-levels-base-to-1x1",
                    "rgba8MipChainReason": "BC1-one-bit-alpha-preservation",
                }
            )
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
        archive_index: dict[str, list[BsaArchive]] = {}
        for archive in self.archives:
            for member_path in archive.members:
                archive_index.setdefault(member_path, []).append(archive)
        self.archive_index = {
            member_path: tuple(member_archives)
            for member_path, member_archives in archive_index.items()
        }
        self.cache_root = cache_root
        self.compiler = compiler
        self.aliases = {
            canonical_member_path(source): canonical_member_path(target)
            for source, target in aliases.items()
        }

    def member_source_count(self, requested_path: str) -> int:
        requested = canonical_member_path(requested_path)
        archive_path = self.aliases.get(requested, requested)
        return len(self.archive_index.get(archive_path, ()))

    def prepare(self, requested_path: str) -> TextureArtifact:
        requested = canonical_member_path(requested_path)
        archive_path = self.aliases.get(requested, requested)
        matches = self.archive_index.get(archive_path, ())
        if len(matches) != 1:
            raise FileNotFoundError(
                f"Expected one texture member {archive_path!r}, found {len(matches)} archives"
            )
        member = matches[0].extract(archive_path)
        asset_id = hashlib.sha256(requested.encode()).hexdigest()[
            :self.compiler.asset_id_hex_characters
        ]
        return prepare_texture_artifact(
            requested,
            archive_path,
            member,
            self.cache_root,
            self.compiler,
            asset_id,
        )


class OwnedTexturePipeline:
    """Prepare effective textures through the official owned-archive stack."""

    def __init__(
        self,
        archives: OwnedArchiveStack,
        cache_root: Path,
        aliases: dict[str, str],
        compiler: ContentCompilerConfiguration,
    ):
        self.archives = archives
        self.cache_root = cache_root
        self.compiler = compiler
        self.aliases = {
            canonical_member_path(source): canonical_member_path(target)
            for source, target in aliases.items()
        }

    def member_source_count(self, requested_path: str) -> int:
        requested = canonical_member_path(requested_path)
        archive_path = self.aliases.get(requested, requested)
        return int(archive_path in self.archives.members)

    def prepare(self, requested_path: str) -> TextureArtifact:
        requested = canonical_member_path(requested_path)
        archive_path = self.aliases.get(requested, requested)
        member = self.archives.extract(archive_path)
        asset_id = hashlib.sha256(
            f"{requested}:{member.sha256}".encode("utf-8")
        ).hexdigest()[:self.compiler.asset_id_hex_characters]
        return prepare_texture_artifact(
            requested,
            archive_path,
            member,
            self.cache_root,
            self.compiler,
            asset_id,
        )


def prepare_texture_artifact(
    requested: str,
    archive_path: str,
    member: ExtractedMember,
    cache_root: Path,
    compiler: ContentCompilerConfiguration,
    asset_id: str,
) -> TextureArtifact:
    source_path = cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
    _atomic_bytes(source_path, member.data)
    png_path = cache_root / "generated" / "textures" / f"{asset_id}.png"
    normal_green_inverted = requested.endswith("_n.dds")
    cube_images = decode_dds_cubemap(member.data)
    authored_mips = (
        decode_dds_mip_chain(member.data, normal_green_inverted)
        if not cube_images and member.data[DDS_PIXEL_FORMAT_FOURCC_OFFSET :
            DDS_PIXEL_FORMAT_FOURCC_OFFSET + 4] == b"DXT1"
        else []
    )
    image = (
        cube_images[0]
        if cube_images
        else authored_mips[0]
        if authored_mips
        else decode_dds(member.data, normal_green_inverted)
    )
    rgba8_mip_path = None
    if authored_mips and any(
        mip.getchannel("A").getextrema()[0] < BYTE_CHANNEL_MAXIMUM
        for mip in authored_mips
    ):
        rgba8_mip_path = (
            cache_root
            / "generated"
            / "textures"
            / f"{asset_id}-rgba8-authored-mips.bin"
        )
        _atomic_bytes(
            rgba8_mip_path,
            b"".join(mip.tobytes() for mip in authored_mips),
        )
    _atomic_png(png_path, image, compiler.png_compression_level)
    cube_paths = tuple(
        png_path.with_name(f"{asset_id}-cube-{index}.png")
        for index in range(len(cube_images))
    )
    for path, cube_image in zip(cube_paths, cube_images):
        _atomic_png(path, cube_image, compiler.png_compression_level)
    width, height = image.size
    return TextureArtifact(
        asset_id,
        requested,
        archive_path,
        member.sha256,
        len(member.data),
        source_path,
        dds_mip_count(member.data),
        rgba8_mip_path,
        png_path,
        file_sha256(png_path),
        width,
        height,
        normal_green_inverted,
        cube_paths,
        tuple(file_sha256(path) for path in cube_paths),
        member.source_archive,
        member.source_archive_sha256,
    )


def dds_mip_count(payload: bytes) -> int:
    if len(payload) < DDS_HEADER_BYTES or payload[:4] != b"DDS ":
        raise ValueError("Texture payload is not DDS")
    return max(1, struct.unpack_from("<I", payload, DDS_MIP_MAP_COUNT_OFFSET)[0])


def decode_dds_mip_chain(
    payload: bytes,
    invert_normal_green: bool,
) -> list[Image.Image]:
    if len(payload) < DDS_HEADER_BYTES or payload[:4] != b"DDS ":
        raise ValueError("Texture payload is not DDS")
    fourcc = payload[
        DDS_PIXEL_FORMAT_FOURCC_OFFSET : DDS_PIXEL_FORMAT_FOURCC_OFFSET + 4
    ]
    block_bytes = DDS_BLOCK_BYTES_BY_FOURCC.get(fourcc)
    if block_bytes is None:
        raise ValueError(f"DDS authored mip decoding does not support {fourcc!r}")
    if struct.unpack_from("<I", payload, DDS_CAPABILITIES_TWO_OFFSET)[0] & DDS_CUBEMAP_FLAG:
        raise ValueError("DDS cubemap mip chains require per-face decoding")

    width = struct.unpack_from("<I", payload, DDS_WIDTH_OFFSET)[0]
    height = struct.unpack_from("<I", payload, DDS_HEIGHT_OFFSET)[0]
    offset = DDS_HEADER_BYTES
    images: list[Image.Image] = []
    for _level in range(dds_mip_count(payload)):
        level_bytes = (
            max(1, (width + 3) // 4)
            * max(1, (height + 3) // 4)
            * block_bytes
        )
        end = offset + level_bytes
        if end > len(payload):
            raise ValueError("DDS authored mip chain is truncated")
        header = bytearray(payload[:DDS_HEADER_BYTES])
        struct.pack_into("<I", header, DDS_HEIGHT_OFFSET, height)
        struct.pack_into("<I", header, DDS_WIDTH_OFFSET, width)
        struct.pack_into("<I", header, DDS_LINEAR_SIZE_OFFSET, level_bytes)
        struct.pack_into("<I", header, DDS_MIP_MAP_COUNT_OFFSET, 1)
        flags = struct.unpack_from("<I", header, DDS_FLAGS_OFFSET)[0]
        struct.pack_into(
            "<I",
            header,
            DDS_FLAGS_OFFSET,
            flags & ~DDS_MIP_MAP_COUNT_FLAG,
        )
        capabilities = struct.unpack_from(
            "<I", header, DDS_CAPABILITIES_ONE_OFFSET
        )[0]
        capabilities &= ~(DDS_CAPABILITY_COMPLEX | DDS_CAPABILITY_MIPMAP)
        capabilities |= DDS_CAPABILITY_TEXTURE
        struct.pack_into(
            "<I", header, DDS_CAPABILITIES_ONE_OFFSET, capabilities
        )
        images.append(
            decode_dds(
                bytes(header) + payload[offset:end],
                invert_normal_green,
            )
        )
        offset = end
        width = max(1, width // 2)
        height = max(1, height // 2)
    if offset != len(payload):
        raise ValueError("DDS authored mip chain has trailing payload")
    return images


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
