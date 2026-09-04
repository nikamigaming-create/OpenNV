from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
NATIVE_READ_PATH = (
    "runtime/src/Content/RuntimeOwnedContentSource.cs",
    "runtime/src/Content/NativeGameInstallation.cs",
    "runtime/src/Content/FalloutBsaArchive.cs",
    "runtime/src/Content/FalloutPluginReader.cs",
    "runtime/src/Content/FalloutPluginStack.cs",
    "runtime/src/Content/FalloutCellScene.cs",
    "runtime/src/Content/FalloutActorCreatureLedger.cs",
    "runtime/src/Content/FalloutHumanoidAppearance.cs",
    "runtime/src/Content/FalloutDat1Archive.cs",
    "runtime/src/Content/Fallout1NativeFormats.cs",
    "runtime/src/Content/Fallout1NativeObjectGraph.cs",
    "runtime/src/Content/Fallout1OwnedContentSource.cs",
    "runtime/src/Content/FalloutDoorTransition.cs",
    "runtime/src/Content/FalloutNewGamePlayerStart.cs",
    "runtime/src/Content/FalloutPlacedLight.cs",
    "runtime/src/Content/FalloutLandscapeTransport.cs",
    "runtime/src/Content/FalloutSoundRecord.cs",
    "runtime/src/Content/NativeOwnedMediaLoader.cs",
    "runtime/src/Content/NativeOwnedSoundPlayback.cs",
    "runtime/src/Content/RuntimeNativePlacedLightBuilder.cs",
    "runtime/src/Content/RuntimeNativeLandscapeTransport.cs",
    "runtime/src/Content/RuntimeNativeDoorPortal.cs",
    "runtime/src/Formats/Gamebryo/FalloutNifFile.cs",
    "runtime/src/Formats/Gamebryo/FalloutNifSkinContract.cs",
    "runtime/src/Formats/Gamebryo/FalloutNifBsxContract.cs",
    "runtime/src/Formats/Gamebryo/NativeNifMeshBuilder.cs",
    "runtime/src/Formats/Gamebryo/RuntimeNifControllerPlayer.cs",
    "runtime/src/Formats/Gamebryo/NativeNifCollisionBuilder.cs",
    "runtime/src/Presentation/Rendering/RuntimeMaterialLoader.cs",
    "runtime/src/RuntimeCoordinator.NativeOwned.cs",
    "runtime/src/RuntimeCoordinator.Fallout1Native.cs",
    "runtime/src/Campaigns/Classic/Native/FalloutClassicOwnedSource.cs",
    "runtime/src/Campaigns/Fallout1/Native/Fo1NativeOwnedSource.cs",
    "runtime/src/Campaigns/Fallout1/Native/Fallout1NativeV13Presentation.cs",
    "runtime/src/Campaigns/Fallout1/Native/Fallout1NativeV13InteractionRuntime.cs",
    "runtime/src/Campaigns/Fallout1/Native/Fallout1NativeV13SemanticTransport.cs",
    "runtime/src/Campaigns/Fallout2/Native/Fo2NativeOwnedData.cs",
    "runtime/src/Campaigns/Fallout2/Native/Fo2NativeMap3Objects.cs",
    "runtime/src/Campaigns/Fallout2/Native/Fo2NativeMap3Presentation.cs",
    "runtime/src/Campaigns/Fallout2/Native/Fo2NativePopulationLedger.cs",
    "runtime/tools/Fo2NativeOwnedAudit/Fo2NativeOwnedAudit.cs",
    "runtime/tools/NativeFo1OwnedAudit/NativeFo1OwnedAudit.cs",
    "runtime/tools/NativeGoodspringsActorAudit/NativeGoodspringsActorAudit.cs",
    "runtime/tools/NativeFallout3ActorLedgerAudit/NativeFallout3ActorLedgerAudit.cs",
)
FORBIDDEN_WRITE_APIS = (
    "File.Write",
    "File.Create",
    "File.OpenWrite",
    "File.Append",
    "File.Copy",
    "File.Move",
    "File.Replace",
    "File.Delete",
    "Directory.Create",
    "Directory.Delete",
    "Directory.Move",
    "FileMode.Create",
    "FileMode.CreateNew",
    "FileMode.Append",
    "FileMode.OpenOrCreate",
    "FileMode.Truncate",
    "ResourceSaver",
    "GetTempFileName",
)


def test_native_owned_content_path_has_no_write_or_extraction_api() -> None:
    findings: list[str] = []
    for relative in NATIVE_READ_PATH:
        source = (ROOT / relative).read_text(encoding="utf-8")
        for forbidden in FORBIDDEN_WRITE_APIS:
            if forbidden in source:
                findings.append(f"{relative}: {forbidden}")
    assert findings == []


def test_native_binary_streams_are_explicitly_read_only() -> None:
    for relative in (
        "runtime/src/Content/FalloutBsaArchive.cs",
        "runtime/src/Content/FalloutPluginReader.cs",
        "runtime/src/Content/FalloutPluginStack.cs",
        "runtime/src/Content/RuntimeOwnedContentSource.cs",
    ):
        source = (ROOT / relative).read_text(encoding="utf-8")
        assert "FileAccess.Write" not in source
        assert "FileAccess.ReadWrite" not in source
    bsa = (ROOT / "runtime/src/Content/FalloutBsaArchive.cs").read_text(encoding="utf-8")
    assert bsa.count("FileMode.Open, FileAccess.Read, FileShare.Read") == 2
    plugin = (ROOT / "runtime/src/Content/FalloutPluginReader.cs").read_text(encoding="utf-8")
    assert "File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read)" in plugin


class NativeOnlyLaunchPolicyTest(unittest.TestCase):
    def test_runtime_launches_owned_data_without_prepared_cache_routes(self) -> None:
        request = (ROOT / "runtime/src/RuntimeLaunchRequest.cs").read_text(encoding="utf-8")
        launch = (ROOT / "runtime/src/RuntimeCoordinator.Launch.cs").read_text(encoding="utf-8")
        coordinator = (ROOT / "runtime/src/RuntimeCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn('(\"source-stack\", RuntimeLaunchRoute.NativeOwnedData)', request)
        self.assertNotIn("RuntimeLaunchRoute.OwnedData", request)
        self.assertNotIn("RuntimeLaunchRoute.PreparedCache", request)
        self.assertNotIn("LegalAssetPreparer.Prepare", launch)
        self.assertNotIn("LegalAssetPreparer.TryRestore", launch)
        self.assertNotIn("LegalAssetPreparer.TryRestore", coordinator)
        self.assertNotIn("LegalAssetPreparer.Prepare(", coordinator)

    def test_native_installation_detector_covers_all_supported_games(self) -> None:
        detector = (ROOT / "runtime/src/Content/NativeGameInstallation.cs").read_text(encoding="utf-8")
        coordinator = (ROOT / "runtime/src/RuntimeCoordinator.cs").read_text(encoding="utf-8")
        fallout1_source = (ROOT / "runtime/src/Content/Fallout1OwnedContentSource.cs").read_text(
            encoding="utf-8"
        )
        launch = (ROOT / "runtime/src/RuntimeCoordinator.Launch.cs").read_text(encoding="utf-8")
        for game in ("Fallout1", "Fallout2", "Fallout3", "FalloutNewVegas"):
            self.assertIn(f"NativeGame.{game}", detector)
        self.assertIn('"patch000.dat"', detector)
        self.assertIn('"Fallout3.esm"', detector)
        self.assertIn(
            'FalloutNewVegasMasterName = "FalloutNV" + ".esm"', detector
        )
        self.assertIn(
            "ContainsFile(contentRoot, FalloutNewVegasMasterName)", detector
        )
        self.assertIn("RuntimeOwnedContentSource.Current!.ContentRoot", coordinator)
        self.assertIn("Fallout1OwnedContentSource LoadInstall(string installDirectory)", fallout1_source)
        self.assertIn("if (loose.Sha256.Length != 0)", fallout1_source)
        self.assertIn("LoadFallout1NativeInstall(_nativeInstallation.InstallRoot", launch)
        fallout2_source = (ROOT / "runtime/src/Campaigns/Fallout2/Native/Fo2NativeOwnedData.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("Fo2NativeOwnedSource LoadInstall(string installDirectory)", fallout2_source)
        self.assertIn("LoadFallout2NativeInstall(_nativeInstallation.InstallRoot)", launch)


def test_fo1_object_frame_cache_only_contains_validated_static_frames() -> None:
    source = (ROOT / "runtime/src/Campaigns/Fallout1/Native/Fallout1NativeV13Presentation.cs").read_text(
        encoding="utf-8"
    )
    static_gate = source.index("if (frame.FramesPerDirection != 1)")
    cache_insert = source.index("decodedFrames.Add(frameKey, decoded)")
    assert static_gate < cache_insert


def test_fo2_map3_classifies_every_top_level_object_before_presentation() -> None:
    source = (ROOT / "runtime/src/Campaigns/Fallout2/Native/Fo2NativeMap3Presentation.cs").read_text(
        encoding="utf-8"
    )
    assert "presentedObjects + semanticObjects != graph.TotalTopLevelObjects" in source
    assert 'root.SetMeta("unclassified_objects", 0)' in source
    assert '"critter-animation-ai"' in source
    assert '"scripted-state"' in source
    static_gate = source.index("if (frame.FramesPerDirection != 1)")
    cache_insert = source.index("decodedObjects.Add(frameKey, decoded)")
    assert static_gate < cache_insert
