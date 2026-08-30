from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_actor import ActorAppearanceOverride  # noqa: E402
from prepare_fo3_cg01_dad_variant import (  # noqa: E402
    Cg01DadVariant,
    _select_exact_variant,
    _variant_output_root,
    prepare_all_required_stale_variants,
    prepare_exact_variant,
)


def _variant(identity: str, variant_id: str = "variant") -> Cg01DadVariant:
    return Cg01DadVariant(
        output_identity=identity,
        appearance_override=ActorAppearanceOverride(
            variant_id=variant_id,
            authority="owned-test",
            source_sha256="a" * 64,
            reference_form_id=1,
            base_form_id=2,
            race_form_id=3,
            symmetric_geometry=(),
            asymmetric_geometry=(),
            symmetric_texture=(),
        ),
        runtime_animation_paths=("meshes\\idle.kf",),
    )


class Fo3Cg01DadVariantTest(unittest.TestCase):
    def test_exact_known_variant_is_selected(self) -> None:
        selected = _select_exact_variant(
            [_variant("actor-first"), _variant("actor-second")],
            "actor-second",
        )
        self.assertEqual("actor-second", selected.output_identity)

    def test_unknown_variant_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "unknown"):
            _select_exact_variant([_variant("actor-first")], "actor-other")

    def test_ambiguous_variant_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            _select_exact_variant(
                [_variant("actor-first"), _variant("actor-first")],
                "actor-first",
            )

    def test_output_identity_is_confined_to_one_actor_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.assertEqual(
                root.resolve() / "generated" / "actors" / "actor-first",
                _variant_output_root(root, "actor-first"),
            )
            for invalid in ("../actor", "actor/child", "actor\\child", ""):
                with self.subTest(invalid=invalid):
                    with self.assertRaisesRegex(ValueError, "identity is invalid"):
                        _variant_output_root(root, invalid)

    def test_exact_preparation_invokes_only_selected_output_identity(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            identity = "actor-variant"
            target = _variant_output_root(root, identity) / "actor-scene.json"
            variant = _variant(identity)
            manifest = {
                "manifest": str(target),
                "schema": "opennv-actor-scene/v5",
                "status": "skinned-animated",
                "appearanceOverride": {"variantId": "variant"},
            }
            with (
                mock.patch(
                    "prepare_fo3_cg01_dad_variant._resolve_variants",
                    return_value=(Path("D:/owned/Data"), {"id": "actor"}, [variant]),
                ),
                mock.patch(
                    "prepare_fo3_cg01_dad_variant.prepare_actor",
                    return_value=manifest,
                ) as prepare,
            ):
                actual = prepare_exact_variant(
                    root / "profile.json",
                    root,
                    root / "recipe.json",
                    identity,
                )

            self.assertEqual(target, actual)
            self.assertEqual(1, prepare.call_count)
            self.assertEqual("actor", prepare.call_args.args[2])
            self.assertEqual(
                variant.appearance_override,
                prepare.call_args.kwargs["appearance_override"],
            )

    def test_exact_preparation_rejects_another_output_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            identity = "actor-variant"
            variant = _variant(identity)
            manifest = {
                "manifest": str(root / "generated" / "actors" / "other" / "actor-scene.json"),
                "schema": "opennv-actor-scene/v5",
                "status": "skinned-animated",
                "appearanceOverride": {"variantId": "variant"},
            }
            with (
                mock.patch(
                    "prepare_fo3_cg01_dad_variant._resolve_variants",
                    return_value=(Path("D:/owned/Data"), {"id": "actor"}, [variant]),
                ),
                mock.patch(
                    "prepare_fo3_cg01_dad_variant.prepare_actor",
                    return_value=manifest,
                ),
                self.assertRaisesRegex(ValueError, "escaped its exact output identity"),
            ):
                prepare_exact_variant(
                    root / "profile.json",
                    root,
                    root / "recipe.json",
                    identity,
                )

    def test_batch_rebuilds_only_stale_variants(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first = _variant("actor-first", "first")
            second = _variant("actor-second", "second")
            third = _variant("actor-third", "third")
            stale = {second.output_identity}

            def load_actor(
                _cache_root: Path,
                _recipe: dict[str, object],
                appearance: ActorAppearanceOverride,
                _animations: tuple[str, ...],
            ) -> dict[str, object]:
                identity = f"actor-{appearance.variant_id}"
                if identity in stale:
                    raise ValueError("stale")
                return {
                    "manifest": str(
                        root
                        / "generated"
                        / "actors"
                        / identity
                        / "actor-scene.json"
                    )
                }

            def rebuild(
                _data_root: Path,
                _cache_root: Path,
                _recipe: dict[str, object],
                selected: Cg01DadVariant,
            ) -> Path:
                stale.remove(selected.output_identity)
                return (
                    root
                    / "generated"
                    / "actors"
                    / selected.output_identity
                    / "actor-scene.json"
                )

            with (
                mock.patch(
                    "prepare_fo3_cg01_dad_variant._resolve_variants",
                    return_value=(
                        Path("D:/owned/Data"),
                        {"id": "actor"},
                        [third, first, second],
                    ),
                ),
                mock.patch(
                    "prepare_fo3_cg01_dad_variant._load_prepared_actor",
                    side_effect=load_actor,
                ),
                mock.patch(
                    "prepare_fo3_cg01_dad_variant._prepare_selected_variant",
                    side_effect=rebuild,
                ) as prepare,
            ):
                batch = prepare_all_required_stale_variants(
                    root / "profile.json",
                    root,
                    root / "recipe.json",
                )

            self.assertEqual(
                ("actor-first", "actor-second", "actor-third"),
                batch.required,
            )
            self.assertEqual(("actor-first", "actor-third"), batch.reused)
            self.assertEqual(("actor-second",), batch.rebuilt)
            self.assertEqual(1, prepare.call_count)


if __name__ == "__main__":
    unittest.main()
