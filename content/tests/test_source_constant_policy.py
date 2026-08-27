from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


REPOSITORY = Path(__file__).resolve().parents[2]
MODULE_PATH = REPOSITORY / "scripts" / "audit_source_constants.py"
SPEC = importlib.util.spec_from_file_location("audit_source_constants", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
AUDIT = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = AUDIT
SPEC.loader.exec_module(AUDIT)


class SourceConstantPolicyTest(unittest.TestCase):
    def write(self, directory: str, name: str, source: str) -> Path:
        path = Path(directory) / name
        path.write_text(source, encoding="utf-8")
        return path

    def test_python_allows_named_contract_and_rejects_function_literal(self):
        with tempfile.TemporaryDirectory() as directory:
            accepted = self.write(directory, "accepted.py", "FORMAT_BYTES = 12\n")
            rejected = self.write(directory, "rejected.py", "def value():\n    return 12\n")
            self.assertEqual(AUDIT.python_violations(accepted), [])
            self.assertEqual(len(AUDIT.python_violations(rejected)), 1)

    def test_csharp_ignores_strings_and_requires_const(self):
        with tempfile.TemporaryDirectory() as directory:
            accepted = self.write(
                directory,
                "Accepted.cs",
                'internal class A { private const int Width = 12; string Hash = "abc256"; }\n',
            )
            rejected = self.write(
                directory,
                "Rejected.cs",
                "internal class A { int Width = 12; }\n",
            )
            self.assertEqual(AUDIT.csharp_violations(accepted), [])
            self.assertEqual(len(AUDIT.csharp_violations(rejected)), 1)

    def test_javascript_allows_named_contract_and_ignores_templates(self):
        with tempfile.TemporaryDirectory() as directory:
            accepted = self.write(
                directory,
                "accepted.mjs",
                "const WINDOW_WIDTH = 1280;\nconst text = `width 640 ${index + 1}`;\n",
            )
            rejected = self.write(directory, "rejected.mjs", "window.setTimeout(done, 1280);\n")
            self.assertEqual(AUDIT.javascript_violations(accepted), [])
            self.assertEqual(len(AUDIT.javascript_violations(rejected)), 1)

    def test_powershell_allows_named_contract_and_ignores_strings(self):
        with tempfile.TemporaryDirectory() as directory:
            accepted = self.write(
                directory,
                "accepted.ps1",
                '$ArchiveJsonDepth = 8\n$message = "version 90"\n',
            )
            rejected = self.write(directory, "rejected.ps1", "if ($count -lt 90) { throw 'low' }\n")
            self.assertEqual(AUDIT.powershell_violations(accepted), [])
            self.assertEqual(len(AUDIT.powershell_violations(rejected)), 1)

    def test_godot_bootstrap_cannot_duplicate_injected_runtime_policy(self):
        with tempfile.TemporaryDirectory() as directory:
            accepted = self.write(
                directory,
                "accepted.godot",
                'renderer/rendering_method="forward_plus"\n',
            )
            rejected = self.write(
                directory,
                "rejected.godot",
                "window/size/viewport_width=1280\n",
            )
            self.assertEqual(AUDIT.godot_project_violations(accepted), [])
            self.assertEqual(len(AUDIT.godot_project_violations(rejected)), 1)

    def test_data_gate_rejects_owned_identities_paths_and_substitutions(self):
        with tempfile.TemporaryDirectory() as directory:
            accepted = self.write(
                directory,
                "accepted.py",
                'sentinel = "00000000"\npath = f"textures/{owner}/{form_id}_0.dds"\n',
            )
            rejected = self.write(
                directory,
                "rejected.py",
                'actor = "00104e84"\n'
                'asset = "meshes\\\\characters\\\\owned.nif"\n'
                'digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"\n'
                'name = "Configured Subject"\n'
                'policy = "runtime fallback"\n',
            )
            self.assertEqual(AUDIT.source_data_violations(accepted), [])
            violations = AUDIT.source_data_violations(
                rejected,
                frozenset({"Configured Subject"}),
            )
            self.assertEqual(
                {violation.language for violation in violations},
                {
                    "content-form-id",
                    "content-sha256",
                    "owned-asset-path",
                    "content-identity",
                    "guessed-substitution",
                },
            )


if __name__ == "__main__":
    unittest.main()
