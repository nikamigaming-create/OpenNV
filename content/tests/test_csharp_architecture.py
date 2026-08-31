from __future__ import annotations

import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCE_ROOT = ROOT / "runtime" / "src"
PROJECT = ROOT / "runtime" / "OpenNV.csproj"


class CSharpArchitectureTest(unittest.TestCase):
    def test_namespaces_follow_the_source_hierarchy(self) -> None:
        exceptions = {"AssemblyInfo.cs"}
        for path in SOURCE_ROOT.rglob("*.cs"):
            if path.name in exceptions or path.name.endswith("NamespaceBridge.cs"):
                continue
            relative_parent = path.relative_to(SOURCE_ROOT).parent
            suffix = ".".join(relative_parent.parts)
            expected = "OpenNV.Runtime" + (f".{suffix}" if suffix else "")
            source = path.read_text(encoding="utf-8")
            match = re.search(r"^namespace\s+([^;{]+)", source, re.MULTILINE)
            self.assertIsNotNone(match, f"{path} has no namespace")
            self.assertEqual(expected, match.group(1).strip(), str(path))

    def test_current_default_analyzers_are_pinned(self) -> None:
        project = PROJECT.read_text(encoding="utf-8")
        self.assertIn("<AnalysisLevel>latest-default</AnalysisLevel>", project)
        self.assertIn("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", project)

    def test_scene_traversal_is_shared_and_non_recursive(self) -> None:
        traversal = (SOURCE_ROOT / "SceneGraph" / "NodeTraversal.cs").read_text(
            encoding="utf-8"
        )
        actor_loader = (SOURCE_ROOT / "ActorModelSlice.cs").read_text(encoding="utf-8")
        body_rig = (
            SOURCE_ROOT
            / "Presentation"
            / "CharacterCreation"
            / "CharacterBodyProportions.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("Stack<Node>", traversal)
        self.assertNotIn("Descendants<T>(child)", traversal)
        self.assertIn("NodeTraversal.Descendants", actor_loader)
        self.assertIn("NodeTraversal.Descendants", body_rig)

    def test_character_shader_has_no_speculative_vats_contract(self) -> None:
        shader = (
            SOURCE_ROOT
            / "Presentation"
            / "CharacterCreation"
            / "ClassicGreenWireframeShader.cs"
        ).read_text(encoding="utf-8")
        self.assertNotIn("FutureVatsRole", shader)
        self.assertNotIn("opennv_future_use", shader)
        self.assertIn("classic-character-green-wireframe", shader)


if __name__ == "__main__":
    unittest.main()
