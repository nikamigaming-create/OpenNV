"""Direct DDS extraction and deterministic PNG cache preparation."""

from __future__ import annotations

import hashlib
import os
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageOps

from bsa_archive import BsaArchive, canonical_member_path


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

    def manifest(self) -> dict[str, object]:
        return {
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


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


class TexturePipeline:
    def __init__(
        self,
        archives: list[Path],
        cache_root: Path,
        aliases: dict[str, str],
    ):
        self.archives = [BsaArchive(path) for path in archives]
        self.cache_root = cache_root
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

        asset_id = hashlib.sha256(requested.encode()).hexdigest()[:20]
        png_path = self.cache_root / "generated" / "textures" / f"{asset_id}.png"
        normal_green_inverted = requested.endswith("_n.dds")
        image = decode_dds(member.data, normal_green_inverted)
        _atomic_png(png_path, image)
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


def _atomic_bytes(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def _atomic_png(path: Path, image: Image.Image) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    image.save(temporary, format="PNG", optimize=True, compress_level=9)
    os.replace(temporary, path)
