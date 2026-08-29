from __future__ import annotations

import struct
import sys
from io import BytesIO
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from facegen import (  # noqa: E402
    _combine_geometry_basis_deltas,
    apply_geometry_morphs,
    compose_body_albedo,
    compose_facegen_coordinates,
    synthesize_texture_detail,
)
from PIL import Image  # noqa: E402
from pyffi.formats.egm import EgmFormat  # type: ignore  # noqa: E402


class FaceGenTest(unittest.TestCase):
    def test_identity_coordinates_add_actor_values_to_race_baseline(self) -> None:
        self.assertEqual(
            compose_facegen_coordinates((1.25, -2.0, 0.5), (-0.25, 3.0, 0.75)),
            (1.0, 1.0, 1.25),
        )

    def test_identity_coordinate_composition_requires_matching_channels(self) -> None:
        with self.assertRaisesRegex(ValueError, "actor=2 race=1"):
            compose_facegen_coordinates((1.0, 2.0), (3.0,))

    def test_identity_coordinate_composition_rejects_non_finite_results(self) -> None:
        with self.assertRaisesRegex(ValueError, "non-finite"):
            compose_facegen_coordinates((float("inf"),), (0.0,))

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

    def test_control_axes_compose_exact_symmetric_basis_deltas(self) -> None:
        basis = (
            ((1.0, 0.0, 0.0), (0.0, 2.0, 0.0)),
            ((0.0, 0.0, 3.0), (-1.0, 0.0, 0.0)),
        )
        result = _combine_geometry_basis_deltas(
            basis,
            ((2.0, 0.5),),
            vertex_offset=0,
            vertex_count=2,
        )

        self.assertEqual(result, (((2.0, 0.0, 1.5), (-0.5, 4.0, 0.0)),))

    def test_control_axes_reject_wrong_basis_width(self) -> None:
        with self.assertRaisesRegex(ValueError, "differs from the EGM basis"):
            _combine_geometry_basis_deltas(
                (((1.0, 0.0, 0.0),),),
                ((1.0, 2.0),),
                vertex_offset=0,
                vertex_count=1,
            )

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

    def test_body_shader_composition_multiplies_the_authored_body_mod(self) -> None:
        diffuse = Image.new("RGBA", (1, 1), (128, 100, 64, 222))
        modifier = Image.new("RGB", (1, 1), (120, 128, 140))
        result = compose_body_albedo(diffuse, modifier)
        self.assertEqual(result.getpixel((0, 0)), (120, 100, 70, 222))


if __name__ == "__main__":
    unittest.main()
