import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class FnvPreparedTextureCacheTest(unittest.TestCase):
    def test_one_session_cache_is_shared_by_main_and_linked_cell_loads(self):
        loader = (
            ROOT / "runtime" / "src" / "World" / "Cells" / "CellSceneLoader.cs"
        ).read_text(encoding="utf-8")

        self.assertEqual(1, loader.count("new RuntimeMaterialLoader.TextureCache()"))
        self.assertEqual(2, loader.count("textureCache);"))
        self.assertIn("OPENNV_CELL_TEXTURE_CACHE", loader)

    def test_cache_reuse_requires_the_exact_compiled_texture_contract(self):
        materials = (
            ROOT
            / "runtime"
            / "src"
            / "Presentation"
            / "Rendering"
            / "RuntimeMaterialLoader.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("var contract = texture.GetRawText();", materials)
        self.assertIn("entry.Contract.Equals(contract, StringComparison.Ordinal)", materials)
        self.assertIn("has conflicting contracts", materials)
        self.assertLess(
            materials.index("cache?.TryGet(id, contract"),
            materials.index("VerifiedGltfLoader.VerifyHash(path"),
        )


if __name__ == "__main__":
    unittest.main()
