from __future__ import annotations

import hashlib
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_fo3_birth_presentation import (  # noqa: E402
    _cache_relative_derivative,
    _facegen_values,
    _load_prepared_actor,
)


class Fo3BirthPresentationTest(unittest.TestCase):
    @staticmethod
    def _prepared_actor_fixture(cache_root: Path) -> tuple[Path, ...]:
        output_root = cache_root / "generated" / "actors" / "doctor"
        output_root.mkdir(parents=True)
        gltf_path = output_root / "actor.gltf"
        buffer_path = output_root / "actor.bin"
        texture_path = output_root / "textures" / "face.png"
        texture_path.parent.mkdir()
        gltf_path.write_bytes(b"gltf")
        buffer_path.write_bytes(b"buffer")
        texture_path.write_bytes(b"texture")
        sidecar_path = output_root / "actor.opennv.json"
        sidecar = {
            "animations": [{"logicalPath": "meshes\\idle.kf"}],
            "outputs": {
                "gltf": {
                    "file": gltf_path.name,
                    "sha256": hashlib.sha256(gltf_path.read_bytes()).hexdigest(),
                },
                "buffer": {
                    "file": buffer_path.name,
                    "sha256": hashlib.sha256(buffer_path.read_bytes()).hexdigest(),
                },
            },
            "textures": [
                {
                    "png": texture_path.relative_to(output_root).as_posix(),
                    "pngSha256": hashlib.sha256(texture_path.read_bytes()).hexdigest(),
                }
            ],
        }
        sidecar_path.write_text(json.dumps(sidecar), encoding="utf-8")
        manifest_path = output_root / "actor-scene.json"
        manifest = {
            "schema": "opennv-actor-scene/v5",
            "status": "skinned-animated",
            "recipe": "doctor",
            "compiler": {"family": "actor", "sha256": "a" * 64},
            "configuration": {"schema": "actor-config", "sha256": "b" * 64},
            "idleAnimation": "meshes\\idle.kf",
            "outputs": {
                "gltf": gltf_path.name,
                "sidecar": sidecar_path.name,
                "gltfSha256": hashlib.sha256(gltf_path.read_bytes()).hexdigest(),
                "sidecarSha256": hashlib.sha256(sidecar_path.read_bytes()).hexdigest(),
                "bufferSha256": hashlib.sha256(buffer_path.read_bytes()).hexdigest(),
            },
        }
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        return manifest_path, gltf_path, buffer_path, sidecar_path, texture_path

    def test_actor_derivative_is_cache_relative(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            cache_root = Path(temporary)
            actor = cache_root / "generated" / "actors" / "dad" / "actor-scene.json"

            self.assertEqual(
                "generated/actors/dad/actor-scene.json",
                _cache_relative_derivative(cache_root, actor),
            )

    def test_actor_derivative_cannot_escape_cache(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            cache_root = Path(temporary) / "cache"
            outside = Path(temporary) / "actor-scene.json"

            with self.assertRaisesRegex(ValueError, "escapes its local cache"):
                _cache_relative_derivative(cache_root, outside)

    def test_stage65_facegen_values_are_hash_bound(self) -> None:
        values = [float(index) / 10.0 for index in range(50)]
        sha256 = hashlib.sha256(struct.pack("<50f", *values)).hexdigest()

        actual, actual_sha256 = _facegen_values(
            {"count": len(values), "values": values, "sha256": sha256},
            len(values),
        )

        self.assertEqual(tuple(values), actual)
        self.assertEqual(sha256, actual_sha256)

    def test_stage65_facegen_values_reject_hash_drift(self) -> None:
        values = [0.0] * 30
        with self.assertRaisesRegex(ValueError, "value hash differs"):
            _facegen_values(
                {"count": len(values), "values": values, "sha256": "0" * 64},
                len(values),
            )

    def test_birth_preparation_reuses_actor_artifact_without_writes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            cache_root = Path(temporary)
            paths = self._prepared_actor_fixture(cache_root)
            before = {
                path: (path.stat().st_mtime_ns, path.read_bytes()) for path in paths
            }
            configuration = mock.Mock()
            configuration.actor_artifact_manifest.return_value = {
                "schema": "actor-config",
                "sha256": "b" * 64,
            }
            with (
                mock.patch(
                    "prepare_fo3_birth_presentation.compiler_provenance",
                    return_value={"family": "actor", "sha256": "a" * 64},
                ),
                mock.patch(
                    "prepare_fo3_birth_presentation.load_runtime_configuration",
                    return_value=configuration,
                ),
            ):
                first = _load_prepared_actor(cache_root, {"id": "doctor"})
                second = _load_prepared_actor(cache_root, {"id": "doctor"})

            self.assertEqual(first, second)
            self.assertEqual(
                before,
                {
                    path: (path.stat().st_mtime_ns, path.read_bytes())
                    for path in paths
                },
            )

    def test_birth_preparation_rejects_actor_identity_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            cache_root = Path(temporary)
            self._prepared_actor_fixture(cache_root)
            configuration = mock.Mock()
            configuration.actor_artifact_manifest.return_value = {
                "schema": "actor-config",
                "sha256": "b" * 64,
            }
            with (
                mock.patch(
                    "prepare_fo3_birth_presentation.compiler_provenance",
                    return_value={"family": "actor", "sha256": "c" * 64},
                ),
                mock.patch(
                    "prepare_fo3_birth_presentation.load_runtime_configuration",
                    return_value=configuration,
                ),
                self.assertRaisesRegex(ValueError, "actor artifact identity differs"),
            ):
                _load_prepared_actor(cache_root, {"id": "doctor"})

    def test_birth_preparation_rejects_missing_actor_artifact(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaisesRegex(
                ValueError,
                "actor artifact is absent",
            ):
                _load_prepared_actor(Path(temporary), {"id": "doctor"})


if __name__ == "__main__":
    unittest.main()
