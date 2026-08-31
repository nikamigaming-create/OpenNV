from __future__ import annotations

import sys
from pathlib import Path
from types import SimpleNamespace
import unittest


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from player_facegen_preview import (  # noqa: E402
    PLAYER_FACEGEN_FULL_BODY_PREVIEW_SCHEMA,
    PLAYER_FACEGEN_PLAYABLE_RACE_PREVIEW_SCHEMA,
    PLAYER_FACEGEN_PLAYABLE_RACE_SELECTION_SCOPE,
    PLAYER_FACEGEN_PLAYABLE_RACE_UNSUPPORTED_SCOPE,
    PLAYER_FACEGEN_PREVIEW_SCHEMA,
    PLAYER_FULL_BODY_COMPONENT_ROLES,
    _head_only_facegen_assembly,
    _with_outfit_body,
    _player_body_component_sources,
    _player_preview_selections,
    _playable_race_default_preview_selections,
)


class PlayerFaceGenPreviewTests(unittest.TestCase):
    def test_full_body_preview_uses_actor_space_for_rigid_facegen_parts(self) -> None:
        self.assertFalse(_head_only_facegen_assembly(include_full_body=True))
        self.assertTrue(_head_only_facegen_assembly(include_full_body=False))

    def test_owned_outfit_replaces_nude_body_without_replacing_hands(self) -> None:
        rows = (
            SimpleNamespace(role="body"),
            SimpleNamespace(role="left-hand"),
            SimpleNamespace(role="right-hand"),
        )

        dressed = _with_outfit_body(
            rows,
            "armor/vaultsuit/m/outfit.nif",
            "characters/male/upperbodymale.dds",
        )

        self.assertEqual(tuple(row.role for row in dressed), PLAYER_FULL_BODY_COMPONENT_ROLES)
        self.assertEqual(dressed[0].model_path, "armor/vaultsuit/m/outfit.nif")
        self.assertTrue(dressed[0].use_source_materials)
        self.assertIs(dressed[1], rows[1])
        self.assertIs(dressed[2], rows[2])

    def test_full_body_contract_is_schema_distinct_from_head_only_profile(self) -> None:
        self.assertEqual(PLAYER_FACEGEN_PREVIEW_SCHEMA.rsplit("/", 1)[-1], "v1")
        self.assertEqual(
            PLAYER_FACEGEN_FULL_BODY_PREVIEW_SCHEMA.rsplit("/", 1)[-1],
            "v3",
        )
        self.assertEqual(
            PLAYER_FACEGEN_PLAYABLE_RACE_PREVIEW_SCHEMA.rsplit("/", 1)[-1],
            "v4",
        )
        self.assertEqual(
            PLAYER_FACEGEN_PLAYABLE_RACE_SELECTION_SCOPE,
            "all-playable-race-sex-source-order-default-hair-eyes",
        )
        self.assertEqual(
            PLAYER_FACEGEN_PLAYABLE_RACE_UNSUPPORTED_SCOPE,
            "nondefault-hair-or-eyes-cache-artifact-absent",
        )

    def test_default_selection_identities_cover_owned_male_and_female(self) -> None:
        appearance = {
            "player": {
                "formId": "00000007",
                "defaultRaceFormId": "00000019",
            },
            "races": [
                {
                    "formId": "00000019",
                    "sex": {
                        "male": {
                            "defaultHairFormId": "0002ddee",
                            "defaultEyesFormId": "00004253",
                        },
                        "female": {
                            "defaultHairFormId": "00022e4e",
                            "defaultEyesFormId": "00004253",
                        },
                    },
                }
            ],
        }

        rows = _player_preview_selections(appearance, 0x00000007)

        self.assertEqual(
            [
                (row.sex, row.race_form_id, row.hair_form_id, row.eyes_form_id)
                for row in rows
            ],
            [
                ("male", 0x00000019, 0x0002DDEE, 0x00004253),
                ("female", 0x00000019, 0x00022E4E, 0x00004253),
            ],
        )

    def test_missing_female_selection_identity_fails_closed(self) -> None:
        appearance = {
            "player": {
                "formId": "00000007",
                "defaultRaceFormId": "00000019",
            },
            "races": [
                {
                    "formId": "00000019",
                    "sex": {
                        "male": {
                            "defaultHairFormId": "0002ddee",
                            "defaultEyesFormId": "00004253",
                        }
                    },
                }
            ],
        }

        with self.assertRaisesRegex(ValueError, "sex selections are incomplete"):
            _player_preview_selections(appearance, 0x00000007)

    def test_playable_race_defaults_expand_both_sexes_deterministically(self) -> None:
        appearance = {
            "player": {"formId": "00000007"},
            "races": [
                {
                    "formId": "00000029",
                    "sex": {
                        "male": {
                            "defaultHairFormId": "00000291",
                            "defaultEyesFormId": "00000292",
                        },
                        "female": {
                            "defaultHairFormId": "00000293",
                            "defaultEyesFormId": "00000294",
                        },
                    },
                },
                {
                    "formId": "00000019",
                    "sex": {
                        "male": {
                            "defaultHairFormId": "00000191",
                            "defaultEyesFormId": "00000192",
                        },
                        "female": {
                            "defaultHairFormId": "00000193",
                            "defaultEyesFormId": "00000194",
                        },
                    },
                },
            ],
        }

        rows = _playable_race_default_preview_selections(appearance, 0x00000007)

        self.assertEqual(
            [
                (row.sex, row.race_form_id, row.hair_form_id, row.eyes_form_id)
                for row in rows
            ],
            [
                ("male", 0x00000019, 0x00000191, 0x00000192),
                ("female", 0x00000019, 0x00000193, 0x00000194),
                ("male", 0x00000029, 0x00000291, 0x00000292),
                ("female", 0x00000029, 0x00000293, 0x00000294),
            ],
        )

    def test_default_male_body_roles_follow_owned_race_table(self) -> None:
        race = SimpleNamespace(
            male_body_models=("body.nif", "left.nif", "right.nif", "body.egt"),
            male_body_textures=("body.dds", "left.dds", "right.dds", None),
            female_body_models=(
                "female-body.nif",
                "female-left.nif",
                "female-right.nif",
                "female-body.egt",
            ),
            female_body_textures=(
                "female-body.dds",
                "female-left.dds",
                "female-right.dds",
                None,
            ),
        )

        rows = _player_body_component_sources(race, "male")

        self.assertEqual(tuple(row.role for row in rows), PLAYER_FULL_BODY_COMPONENT_ROLES)
        self.assertEqual(
            tuple(row.model_path for row in rows),
            ("body.nif", "left.nif", "right.nif"),
        )
        self.assertEqual(
            tuple(row.texture_path for row in rows),
            ("body.dds", "left.dds", "right.dds"),
        )
        self.assertEqual(
            tuple(row.bake_shape_transform for row in rows),
            (False, True, True),
        )

    def test_default_female_body_roles_follow_owned_race_table(self) -> None:
        race = SimpleNamespace(
            male_body_models=("body.nif", "left.nif", "right.nif", "body.egt"),
            male_body_textures=("body.dds", "left.dds", "right.dds", None),
            female_body_models=(
                "female-body.nif",
                "female-left.nif",
                "female-right.nif",
                "female-body.egt",
            ),
            female_body_textures=(
                "female-body.dds",
                "female-hand.dds",
                "female-hand.dds",
                None,
            ),
        )

        rows = _player_body_component_sources(race, "female")

        self.assertEqual(tuple(row.role for row in rows), PLAYER_FULL_BODY_COMPONENT_ROLES)
        self.assertEqual(
            tuple(row.model_path for row in rows),
            ("female-body.nif", "female-left.nif", "female-right.nif"),
        )
        self.assertEqual(
            tuple(row.texture_path for row in rows),
            ("female-body.dds", "female-hand.dds", "female-hand.dds"),
        )
        self.assertEqual(
            tuple(row.bake_shape_transform for row in rows),
            (False, False, False),
        )

    def test_incomplete_body_table_fails_closed(self) -> None:
        race = SimpleNamespace(
            male_body_models=("body.nif", "left.nif"),
            male_body_textures=("body.dds", "left.dds"),
            female_body_models=("female-body.nif", "female-left.nif"),
            female_body_textures=("female-body.dds", "female-left.dds"),
        )

        with self.assertRaisesRegex(ValueError, "body table is incomplete"):
            _player_body_component_sources(race, "male")

    def test_missing_body_component_fails_closed(self) -> None:
        race = SimpleNamespace(
            male_body_models=("body.nif", None, "right.nif"),
            male_body_textures=("body.dds", "left.dds", "right.dds"),
            female_body_models=("female-body.nif", "female-left.nif", "female-right.nif"),
            female_body_textures=("female-body.dds", "female-left.dds", "female-right.dds"),
        )

        with self.assertRaisesRegex(ValueError, "body component is absent"):
            _player_body_component_sources(race, "male")

    def test_unknown_body_sex_fails_closed(self) -> None:
        race = SimpleNamespace(
            male_body_models=("body.nif", "left.nif", "right.nif"),
            male_body_textures=("body.dds", "left.dds", "right.dds"),
            female_body_models=("female-body.nif", "female-left.nif", "female-right.nif"),
            female_body_textures=("female-body.dds", "female-left.dds", "female-right.dds"),
        )

        with self.assertRaisesRegex(ValueError, "Unsupported owned player body sex"):
            _player_body_component_sources(race, "robot")


if __name__ == "__main__":
    unittest.main()
