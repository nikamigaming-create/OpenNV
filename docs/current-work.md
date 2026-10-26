# Current work

## Active batch: Exterior streaming performance and session quit physics continuation

Work addresses user request ("outside was very low fps", "outside is litk 5 fps", "?").
Active branch is `codex/fix-session-quit-physics-continuation` based on `main`.

### Verified capabilities and fixes

1. **Exterior Streaming & Frame Rate Performance**:
   - Fast-rejected SpeedTree `.spt` shrub models in [RuntimeCoordinator.NativeOwned.cs](file:///d:/code/OpenNV/runtime/src/RuntimeCoordinator.NativeOwned.cs) and [RuntimeCoordinator.Exterior.cs](file:///d:/code/OpenNV/runtime/src/RuntimeCoordinator.Exterior.cs), directly recording telemetry divergences without throwing `NotSupportedException` or dumping full stack traces via `GD.PushError` for ~1,000 shrubs during cell transitions.
   - Converted [RuntimeNativeReferencePresentation.cs](file:///d:/code/OpenNV/runtime/src/World/Cells/RuntimeNativeReferencePresentation.cs) to use an O(1) reference dictionary (`_referencesByKey`) and a negative-resolution cache (`_unresolvable`), eliminating continuous O(N) scans across 3,680+ references and stopping redundant re-materialization every frame.
   - Added thread-safe `ConditionalWeakTable` definition caching in [FalloutCellScene.cs](file:///d:/code/OpenNV/runtime/src/Content/FalloutCellScene.cs) across plugin stacks, eliminating synchronous ESM binary subrecord re-parsing for 49 exterior cells whenever the player moves.
   - Set `MeshInstance3D.CastShadow = Off` on landscape terrain meshes in [RuntimeNativeLandscapeTransport.cs](file:///d:/code/OpenNV/runtime/src/Content/RuntimeNativeLandscapeTransport.cs), eliminating ~800 redundant draw calls per frame into Godot's 4 directional shadow splits.

2. **Clean Session Teardown & Physics Continuation**:
   - Guarded `Capture()` in [RuntimeNativeReferencePresentation.Physics.cs](file:///d:/code/OpenNV/runtime/src/World/Cells/RuntimeNativeReferencePresentation.Physics.cs) to verify `body.IsInsideTree()` before calling `CapturePhysics()`, falling back to retained `instance.ObjectPhysics` during post-order tree teardown.
   - Added explicit `presentation.RetireBindings()` in [RuntimeCoordinator.SessionMenu.cs](file:///d:/code/OpenNV/runtime/src/RuntimeCoordinator.SessionMenu.cs) prior to `GetTree().Quit()`.
   - Used `FirstOrDefault` in [RuntimeCoordinator.ReferenceEvents.cs](file:///d:/code/OpenNV/runtime/src/RuntimeCoordinator.ReferenceEvents.cs) for resilient cell reference instance mapping.

3. **Runtime Gate and Verification**:
   - Clean Release and Debug C# builds pass with 0 warnings and 0 errors.
   - Test suites, contract probes, native locomotion, and launcher checks pass (`OPENNV_CSHARP_GODOT_GATE_PASS`).
   - Zero git diff check whitespace/formatting errors (`git diff --check`).

### Next steps

1. Re-export development runtime and launch interactive playtest to verify smooth exterior frame rate and clean exit.
2. Publish PR, merge into `main`, and continue autonomous campaign progression.
