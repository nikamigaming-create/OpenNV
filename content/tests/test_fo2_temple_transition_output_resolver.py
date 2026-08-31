from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RESOLVER = ROOT / "scripts" / "Resolve-Fo2TempleTransitionOutput.ps1"
FIRST_BEAT_HOST = (
    ROOT
    / "runtime"
    / "src"
    / "Campaigns"
    / "Fallout2"
    / "Temple"
    / "Fo2ArroyoArrivalFirstBeatProofHost.cs"
)
TRANSITION_CONTRACT = FIRST_BEAT_HOST.with_name("Fo2TempleTransitionContract.cs")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Fo2TempleTransitionOutputResolverTest(unittest.TestCase):
    def _write_cache(self, root: Path) -> tuple[Path, Path]:
        source = root / "temple-source.json"
        source.write_text("{}", encoding="utf-8")
        profile = root / "fallout2-profile.json"
        profile.write_text("{}", encoding="utf-8")
        transition = root / "fo2-temple-transitions.json"
        transition.write_text(
            json.dumps(
                {
                    "schema": "opennv-fo2-temple-transitions/v1",
                    "status": "compiled-owned-transition-records",
                    "sourceManifest": {"file": str(source), "sha256": sha256(source)},
                    "sourceProfile": {
                        "file": str(profile),
                        "sha256": sha256(profile),
                        "sourceProfileId": "owned-profile-id",
                    },
                }
            ),
            encoding="utf-8",
        )
        cache = root / "fo2-temple-presentation-cache.json"
        cache.write_text(
            json.dumps(
                {
                    "sourceManifest": {"file": str(source), "sha256": sha256(source)},
                    "sourceProfile": {
                        "file": str(profile),
                        "sha256": sha256(profile),
                        "sourceProfileId": "owned-profile-id",
                    },
                    "outputs": {
                        "templeTransitions": {
                            "file": transition.name,
                            "sha256": sha256(transition),
                            "sourceManifestSha256": sha256(source),
                            "sourceProfileSha256": sha256(profile),
                            "sourceProfileId": "owned-profile-id",
                        }
                    },
                }
            ),
            encoding="utf-8",
        )
        return cache, transition

    def _resolve(self, cache: Path) -> subprocess.CompletedProcess[str]:
        shell = shutil.which("pwsh") or shutil.which("powershell")
        if shell is None:
            self.skipTest("PowerShell is unavailable")
        return subprocess.run(
            [shell, "-NoProfile", "-File", str(RESOLVER), "-TempleCache", str(cache)],
            check=False,
            capture_output=True,
            text=True,
        )

    def test_resolves_only_the_hash_bound_co_located_transition(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            cache, transition = self._write_cache(Path(temporary))
            result = self._resolve(cache)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue(
                os.path.samefile(result.stdout.strip(), transition),
                result.stdout,
            )

    def test_rejects_stale_or_missing_transition_descriptor_hash(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            cache, _ = self._write_cache(Path(temporary))
            document = json.loads(cache.read_text(encoding="utf-8"))
            document["outputs"]["templeTransitions"]["sha256"] = "0" * 64
            cache.write_text(json.dumps(document), encoding="utf-8")
            stale = self._resolve(cache)
            self.assertNotEqual(stale.returncode, 0)
            self.assertIn("hash mismatch", stale.stderr)

            del document["outputs"]["templeTransitions"]["sha256"]
            cache.write_text(json.dumps(document), encoding="utf-8")
            missing = self._resolve(cache)
            self.assertNotEqual(missing.returncode, 0)
            self.assertIn("no descriptor sha256", missing.stderr)

    def test_first_beat_has_no_filename_based_transition_parameter(self) -> None:
        host = FIRST_BEAT_HOST.read_text(encoding="utf-8")
        contract = TRANSITION_CONTRACT.read_text(encoding="utf-8")

        self.assertIn("LoadFromPresentationOutput(temple)", host)
        self.assertNotIn('"fo2-temple-transitions"', host)
        self.assertIn("outputs", contract)
        self.assertIn("templeTransitions", contract)
        self.assertIn("transition output escapes its cache root", contract)
        self.assertIn("descriptor does not join the cache source/profile", contract)


if __name__ == "__main__":
    unittest.main()
