from __future__ import annotations

import struct
import sys
from io import BytesIO
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from facegen import (  # noqa: E402
    apply_geometry_morphs,
    compose_body_albedo,
    compose_skin_albedo,
    synthesize_texture_detail,
)
from PIL import Image  # noqa: E402
from pyffi.formats.egm import EgmFormat  # type: ignore  # noqa: E402


class FaceGenTest(unittest.TestCase):
    def test_geometry_modes_apply_symmetric_and_asymmetric_coefficients(self) -> None:
        data = EgmFormat.Data(num_vertices=2)
        data.add_sym_morph().set_relative_vertices(((1.0, 0.0, 0.0), (0.0, 2.0, 0.0)))
        data.add_asym_morph().set_relative_vertices(((0.0, 0.0, 3.0), (-1.0, 0.0, 0.0)))
        stream = BytesIO()
        data.write(stream)
        result = apply_geometry_morphs(
            [(10.0, 20.0, 30.0), (40.0, 50.0, 60.0)],
            stream.getvalue(),
            (2.0,),
            (0.5,),
        )
        for actual, expected in zip(result[0], (12.0, 20.0, 31.5)):
            self.assertAlmostEqual(actual, expected, places=3)
        for actual, expected in zip(result[1], (39.5, 54.0, 60.0)):
            self.assertAlmostEqual(actual, expected, places=3)

    def test_texture_modes_use_float_scale_and_signed_rgb_deltas(self) -> None:
        header = (
            b"FREGT003"
            + struct.pack("<5I", 2, 1, 2, 0, 81)
            + bytes(36)
        )
        first = struct.pack("<f", 1.0) + struct.pack("<2b", 10, -10) * 3
        second = struct.pack("<f", 0.5) + struct.pack("<2b", -4, 8) * 3
        image = synthesize_texture_detail(header + first + second, (2.0, 1.0))
        self.assertEqual(image.size, (2, 1))
        self.assertEqual(image.getpixel((0, 0)), (146, 146, 146, 255))
        self.assertEqual(image.getpixel((1, 0)), (112, 112, 112, 255))

    def test_texture_contract_rejects_asymmetric_modes(self) -> None:
        payload = b"FREGT003" + struct.pack("<5I", 1, 1, 0, 1, 81) + bytes(36)
        with self.assertRaisesRegex(ValueError, "asymmetric=1"):
            synthesize_texture_detail(payload, ())

    def test_skin_shader_composition_uses_detail_and_tone(self) -> None:
        diffuse = Image.new("RGBA", (1, 1), (100, 120, 140, 200))
        detail = Image.new("RGB", (1, 1), (138, 118, 128))
        result = compose_skin_albedo(diffuse, detail, (64, 64, 64))
        self.assertEqual(result.getpixel((0, 0)), (120, 100, 141, 200))

    def test_body_shader_composition_multiplies_the_authored_body_mod(self) -> None:
        diffuse = Image.new("RGBA", (1, 1), (128, 100, 64, 222))
        modifier = Image.new("RGB", (1, 1), (120, 128, 140))
        result = compose_body_albedo(diffuse, modifier)
        self.assertEqual(result.getpixel((0, 0)), (120, 100, 70, 222))


if __name__ == "__main__":
    unittest.main()
