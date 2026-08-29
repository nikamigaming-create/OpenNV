"""Decode FaceGen CTL control axes from an owned archive member.

The binary layout implemented here follows FaceGen's published CTL file format.
Only the four linear control tables are decoded.  The demographic tail remains
opaque and hash-bound because the Doc Mitchell slider surface does not consume it.
"""

from __future__ import annotations

import hashlib
import math
import struct
from dataclasses import dataclass


FACEGEN_CTL_SIGNATURE = b"FRCTL001"
FACEGEN_CTL_SIGNATURE_BYTES = len(FACEGEN_CTL_SIGNATURE)
FACEGEN_CTL_HEADER_INTEGER_COUNT = 6
FACEGEN_CTL_HEADER = struct.Struct(
    f"<{FACEGEN_CTL_SIGNATURE_BYTES}s{FACEGEN_CTL_HEADER_INTEGER_COUNT}I"
)
FACEGEN_CTL_COUNT = struct.Struct("<I")
FACEGEN_CTL_FLOAT_BYTES = struct.calcsize("<f")
FACEGEN_CTL_TEXT_ENCODING = "utf-8"


@dataclass(frozen=True)
class FaceGenLinearControl:
    """One source label and its normalized basis-axis coefficients."""

    index: int
    label: str
    axis: tuple[float, ...]
    axis_sha256: str

    def manifest(self) -> dict[str, object]:
        return {
            "index": self.index,
            "sourceLabel": self.label,
            "axis": list(self.axis),
            "axisSha256": self.axis_sha256,
        }


@dataclass(frozen=True)
class FaceGenControlSpace:
    """The exact linear control portion of one FaceGen CTL payload."""

    geometry_basis_version: int
    texture_basis_version: int
    symmetric_geometry_basis_count: int
    asymmetric_geometry_basis_count: int
    symmetric_texture_basis_count: int
    asymmetric_texture_basis_count: int
    symmetric_geometry: tuple[FaceGenLinearControl, ...]
    asymmetric_geometry: tuple[FaceGenLinearControl, ...]
    symmetric_texture: tuple[FaceGenLinearControl, ...]
    asymmetric_texture: tuple[FaceGenLinearControl, ...]
    linear_bytes: int
    opaque_tail_bytes: int
    opaque_tail_sha256: str

    def manifest(self) -> dict[str, object]:
        return {
            "formatSignature": FACEGEN_CTL_SIGNATURE.decode("ascii"),
            "geometryBasisVersion": self.geometry_basis_version,
            "textureBasisVersion": self.texture_basis_version,
            "basisCounts": {
                "symmetricGeometry": self.symmetric_geometry_basis_count,
                "asymmetricGeometry": self.asymmetric_geometry_basis_count,
                "symmetricTexture": self.symmetric_texture_basis_count,
                "asymmetricTexture": self.asymmetric_texture_basis_count,
            },
            "linearControlCounts": {
                "symmetricGeometry": len(self.symmetric_geometry),
                "asymmetricGeometry": len(self.asymmetric_geometry),
                "symmetricTexture": len(self.symmetric_texture),
                "asymmetricTexture": len(self.asymmetric_texture),
            },
            "linearBytes": self.linear_bytes,
            "opaqueDemographicTail": {
                "bytes": self.opaque_tail_bytes,
                "sha256": self.opaque_tail_sha256,
                "disposition": "hash-bound-not-consumed-by-doc-creator-sliders",
            },
            "controls": {
                "symmetricGeometry": [
                    control.manifest() for control in self.symmetric_geometry
                ],
                "asymmetricGeometry": [
                    control.manifest() for control in self.asymmetric_geometry
                ],
                "symmetricTexture": [
                    control.manifest() for control in self.symmetric_texture
                ],
                "asymmetricTexture": [
                    control.manifest() for control in self.asymmetric_texture
                ],
            },
        }


class _Cursor:
    def __init__(self, payload: bytes):
        self.payload = payload
        self.offset = 0

    def read(self, count: int, role: str) -> bytes:
        if count < 0 or self.offset + count > len(self.payload):
            raise ValueError(f"FaceGen CTL {role} exceeds the owned payload")
        value = self.payload[self.offset : self.offset + count]
        self.offset += count
        return value

    def uint32(self, role: str) -> int:
        return FACEGEN_CTL_COUNT.unpack(self.read(FACEGEN_CTL_COUNT.size, role))[0]


def _decode_linear_controls(
    cursor: _Cursor,
    basis_count: int,
    role: str,
) -> tuple[FaceGenLinearControl, ...]:
    control_count = cursor.uint32(f"{role} control count")
    controls = []
    axis_bytes = basis_count * FACEGEN_CTL_FLOAT_BYTES
    axis_struct = struct.Struct(f"<{basis_count}f")
    for index in range(control_count):
        raw_axis = cursor.read(axis_bytes, f"{role} control {index} axis")
        axis = axis_struct.unpack(raw_axis)
        if not all(math.isfinite(value) for value in axis):
            raise ValueError(f"FaceGen CTL {role} control {index} is non-finite")
        label_bytes = cursor.read(
            cursor.uint32(f"{role} control {index} label length"),
            f"{role} control {index} label",
        )
        try:
            label = label_bytes.decode(FACEGEN_CTL_TEXT_ENCODING)
        except UnicodeDecodeError as error:
            raise ValueError(
                f"FaceGen CTL {role} control {index} label is not UTF-8"
            ) from error
        if not label or "\0" in label:
            raise ValueError(f"FaceGen CTL {role} control {index} label is invalid")
        controls.append(
            FaceGenLinearControl(
                index,
                label,
                tuple(axis),
                hashlib.sha256(raw_axis).hexdigest(),
            )
        )
    return tuple(controls)


def decode_facegen_control_space(payload: bytes) -> FaceGenControlSpace:
    """Decode and validate the linear control tables in one owned CTL payload."""
    if len(payload) < FACEGEN_CTL_HEADER.size:
        raise ValueError("FaceGen CTL header is incomplete")
    cursor = _Cursor(payload)
    (
        signature,
        geometry_basis_version,
        texture_basis_version,
        symmetric_geometry_basis_count,
        asymmetric_geometry_basis_count,
        symmetric_texture_basis_count,
        asymmetric_texture_basis_count,
    ) = FACEGEN_CTL_HEADER.unpack(
        cursor.read(FACEGEN_CTL_HEADER.size, "header")
    )
    if signature != FACEGEN_CTL_SIGNATURE:
        raise ValueError("FaceGen CTL signature differs")
    basis_counts = (
        symmetric_geometry_basis_count,
        asymmetric_geometry_basis_count,
        symmetric_texture_basis_count,
        asymmetric_texture_basis_count,
    )
    if not geometry_basis_version or not texture_basis_version:
        raise ValueError("FaceGen CTL basis versions are invalid")
    if not any(basis_counts) or any(value < 0 for value in basis_counts):
        raise ValueError("FaceGen CTL basis counts are invalid")
    symmetric_geometry = _decode_linear_controls(
        cursor,
        symmetric_geometry_basis_count,
        "symmetric geometry",
    )
    asymmetric_geometry = _decode_linear_controls(
        cursor,
        asymmetric_geometry_basis_count,
        "asymmetric geometry",
    )
    symmetric_texture = _decode_linear_controls(
        cursor,
        symmetric_texture_basis_count,
        "symmetric texture",
    )
    asymmetric_texture = _decode_linear_controls(
        cursor,
        asymmetric_texture_basis_count,
        "asymmetric texture",
    )
    tail = payload[cursor.offset :]
    return FaceGenControlSpace(
        geometry_basis_version,
        texture_basis_version,
        symmetric_geometry_basis_count,
        asymmetric_geometry_basis_count,
        symmetric_texture_basis_count,
        asymmetric_texture_basis_count,
        symmetric_geometry,
        asymmetric_geometry,
        symmetric_texture,
        asymmetric_texture,
        cursor.offset,
        len(tail),
        hashlib.sha256(tail).hexdigest(),
    )
