import sys
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_gltf import (  # noqa: E402
    ActorAnimation,
    ActorGltfInput,
    NifFormat,
    _compensated_inverse_bind,
    _converted_matrix,
    _euler_xyz_quaternion,
    _multiply,
    actor_animation_translations,
    gltf_skeleton_inverse_binds,
)
from actor_material import (  # noqa: E402
    actor_alpha_contract,
    actor_base_color_factor,
    actor_roughness,
    actor_vertex_colors_enabled,
)


class ActorGltfTest(unittest.TestCase):
    def test_xyz_animation_quaternion_identity_and_single_axis(self):
        identity = _euler_xyz_quaternion((0.0, 0.0, 0.0))
        self.assertEqual(identity, (1.0, 0.0, 0.0, 0.0))
        quarter_turn = _euler_xyz_quaternion((0.0, 0.0, 3.141592653589793 / 2.0))
        self.assertAlmostEqual(quarter_turn[0], 2**-0.5)
        self.assertAlmostEqual(quarter_turn[3], 2**-0.5)

    def test_actor_input_keeps_idle_only_callers_source_compatible(self):
        source = ActorGltfInput(
            actor_form_id="00000001",
            actor_name="Synthetic",
            skeleton_path="skeleton.nif",
            skeleton_payload=b"skeleton",
            symmetric_geometry=(),
            asymmetric_geometry=(),
            components=(),
            idle_animation_path="mtidle.kf",
            idle_animation_payload=b"idle",
        )
        self.assertEqual(source.additional_animations, ())
        self.assertEqual(
            ActorAnimation("mtforward.kf", b"forward").logical_path,
            "mtforward.kf",
        )

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
        self.assertEqual(actor_roughness(material), (1.0, "ni-material-zero-specular"))

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

    def test_bip01_locomotion_translation_is_relative_to_skeleton_rest(self):
        values = [(0.0, 0.0, 0.0), (0.5, 0.0, -0.25)]
        rest = (0.0, 67.771, -0.657)
        self.assertEqual(
            actor_animation_translations("Bip01", values, rest),
            [(0.0, 67.771, -0.657), (0.5, 67.771, -0.907)],
        )

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

    def test_emitted_skeleton_inverse_binds_reconstruct_rest_identity(self):
        nodes = [
            {"name": "ACTOR", "children": [1]},
            {
                "name": "Bip01",
                "translation": [2.0, 3.0, 4.0],
                "rotation": [0.0, 0.0, 0.0, 1.0],
                "scale": [1.0, 1.0, 1.0],
                "children": [2],
            },
            {
                "name": "Bip01 Child",
                "translation": [5.0, 0.0, 0.0],
                "rotation": [0.0, 0.0, 2**-0.5, 2**-0.5],
                "scale": [2.0, 1.0, 1.0],
                "children": [],
            },
        ]
        inverse_binds = gltf_skeleton_inverse_binds(
            nodes,
            {"Bip01": 1, "Bip01 Child": 2},
        )
        root_global = [
            [1.0, 0.0, 0.0, 2.0],
            [0.0, 1.0, 0.0, 3.0],
            [0.0, 0.0, 1.0, 4.0],
            [0.0, 0.0, 0.0, 1.0],
        ]
        child_local = [
            [0.0, -1.0, 0.0, 5.0],
            [2.0, 0.0, 0.0, 0.0],
            [0.0, 0.0, 1.0, 0.0],
            [0.0, 0.0, 0.0, 1.0],
        ]
        for global_matrix, name in (
            (root_global, "Bip01"),
            (_multiply(root_global, child_local), "Bip01 Child"),
        ):
            product = _multiply(global_matrix, inverse_binds[name])
            for row in range(4):
                for column in range(4):
                    self.assertAlmostEqual(
                        product[row][column],
                        1.0 if row == column else 0.0,
                    )


if __name__ == "__main__":
    unittest.main()
