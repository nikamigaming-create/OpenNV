import sys
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_gltf import (  # noqa: E402
    NifFormat,
    _compensated_inverse_bind,
    _converted_matrix,
    _multiply,
    actor_animation_translations,
)
from actor_material import (  # noqa: E402
    actor_alpha_contract,
    actor_base_color_factor,
    actor_roughness,
    actor_vertex_colors_enabled,
)
from runtime_configuration import load_runtime_configuration  # noqa: E402


class ActorGltfTest(unittest.TestCase):
    def test_bethesda_shader_does_not_multiply_textures_by_legacy_black_diffuse(self):
        material = NifFormat.NiMaterialProperty()
        material.diffuse_color.r = 0.0
        material.diffuse_color.g = 0.0
        material.diffuse_color.b = 0.0
        material.alpha = 1.0
        shader = NifFormat.BSShaderPPLightingProperty()
        factor, source = actor_base_color_factor([material, shader], (1.0, 1.0, 1.0))
        self.assertEqual(factor, [1.0, 1.0, 1.0, 1.0])
        self.assertEqual(source, "bethesda-shader-texture-neutral")

    def test_shader_flags_and_zero_specular_survive_translation(self):
        shader = NifFormat.BSShaderPPLightingProperty()
        shader.shader_flags_2.sf_2_vertex_colors = 0
        self.assertFalse(actor_vertex_colors_enabled([shader]))
        shader.shader_flags_2.sf_2_vertex_colors = 1
        self.assertTrue(actor_vertex_colors_enabled([shader]))
        material = NifFormat.NiMaterialProperty()
        material.specular_color.r = 0.0
        material.specular_color.g = 0.0
        material.specular_color.b = 0.0
        self.assertEqual(
            actor_roughness(material, load_runtime_configuration().content_compiler),
            (1.0, "ni-material-zero-specular"),
        )

    def test_retail_hair_blends_while_outfit_alpha_tests(self):
        hair = NifFormat.NiAlphaProperty()
        hair.flags = 0x10ED
        hair.threshold = 0
        hair_contract = actor_alpha_contract(hair)
        self.assertEqual(hair_contract["mode"], "BLEND")
        self.assertFalse(hair_contract["testEnabled"])

        outfit = NifFormat.NiAlphaProperty()
        outfit.flags = 0x12EC
        outfit.threshold = 20
        outfit_contract = actor_alpha_contract(outfit)
        self.assertEqual(outfit_contract["mode"], "MASK")
        self.assertAlmostEqual(outfit_contract["cutoff"], 20 / 255)

    def test_nonaccum_idle_translation_is_relative_to_its_first_sample(self):
        values = [(2.0, 66.0, -1.0), (2.5, 65.75, -0.5)]
        self.assertEqual(
            actor_animation_translations("Bip01 NonAccum", values),
            [(0.0, 0.0, 0.0), (0.5, -0.25, 0.5)],
        )
        self.assertIs(actor_animation_translations("Bip01 Head", values), values)

    def test_baked_shape_transform_is_removed_from_skin_bind(self):
        inverse_bind = NifFormat.Matrix44()
        inverse_bind.set_identity()
        shape = NifFormat.Matrix44()
        shape.set_identity()
        shape.m_11 = -1.0
        shape.m_22 = -1.0
        corrected = _compensated_inverse_bind(inverse_bind, shape)
        product = _multiply(corrected, _converted_matrix(shape))
        for row in range(4):
            for column in range(4):
                self.assertAlmostEqual(product[row][column], 1.0 if row == column else 0.0)


if __name__ == "__main__":
    unittest.main()
