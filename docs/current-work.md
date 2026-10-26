# Current work

## Active batch: Menu overlay parity and exterior streaming performance

Work addresses user request ("bad overlay with reflectron and vigor tester anmd outside was very low fps").
Active branch is `codex/fix-menu-overlays-and-exterior-performance` based on `main`.

### Verified capabilities and fixes

1. **Reflectron & Vigor Tester Presentation**:
   - Added solid dark backdrops behind both the Reflectron terminal ([RuntimeNativeRaceSexEntry.cs](file:///d:/code/OpenNV/runtime/src/Campaigns/NewVegas/Opening/RuntimeNativeRaceSexEntry.cs)) and the Vit-o-matic Vigor Tester ([RuntimeNativeVigorEntry.cs](file:///d:/code/OpenNV/runtime/src/Campaigns/NewVegas/Opening/RuntimeNativeVigorEntry.cs)) to prevent 1st-person interior geometry bleed-through.
   - Enforced player modal input throughout opening character generation in [RuntimeNativeOpeningStageDriver.cs](file:///d:/code/OpenNV/runtime/src/Campaigns/NewVegas/Opening/RuntimeNativeOpeningStageDriver.cs), suppressing reticle, action prompts, and gameplay HUD draw.
   - Fixed 16:9 vertical squashing defect in [NativeOwnedRenderedScreen.cs](file:///d:/code/OpenNV/runtime/src/Presentation/Ui/NativeOwnedRenderedScreen.cs) so the 4:3 CRT mesh receives a non-squashed 1280x960 texture with 1:1 mouse tracking.
   - Preserved vertical camera framing on widescreen displays in [NativeOwnedRenderedDevice.cs](file:///d:/code/OpenNV/runtime/src/Presentation/Ui/NativeOwnedRenderedDevice.cs) and [NativeOwnedLoveTesterMenu.cs](file:///d:/code/OpenNV/runtime/src/Presentation/Ui/NativeOwnedLoveTesterMenu.cs) so machines fit on-screen without clipping.

2. **Exterior Streaming Performance**:
   - Fast-rejected unbound SpeedTree `.spt` files in [RuntimeCoordinator.NativeOwned.cs](file:///d:/code/OpenNV/runtime/src/RuntimeCoordinator.NativeOwned.cs) before file queries occur.
   - Filtered `.spt` shrub references in [RuntimeCoordinator.Exterior.cs](file:///d:/code/OpenNV/runtime/src/RuntimeCoordinator.Exterior.cs), recording telemetry divergences directly and eliminating main-thread zlib decompression hitches during cell streaming.

3. **Runtime Gate and Builds**:
   - Clean Release and Debug C# builds pass.
   - Test suites, contract probes, and launcher checks pass (`OPENNV_CSHARP_GODOT_GATE_PASS`).
   - Zero git diff check errors (`git diff --check`).

### Next steps

1. Launch interactive fullscreen playtest to verify crisp menus and smooth exterior FPS.
2. Publish PR, merge into `main`, and continue autonomous campaign progression.
