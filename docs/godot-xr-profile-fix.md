# Experimental Godot interaction-profile cleanup

OpenNV's pinned official Godot 4.8-dev7 creates interaction-profile RIDs in
OpenXRInterface::_load_action_map but checks and appends the local array of
profile resources. The member RID collection remains empty, so the interface's
normal free_interaction_profiles cannot release those allocations at exit.
Distinct profile resources can resolve to the same runtime path and RID.

The patch in patches/godot/interaction-profile-retention.patch records each
created RID once in the actual member cleanup collection. It changes no
registered API, binding suggestions, gameplay state, tracked poses or pixels.
Godot's MIT license accompanies the patch. The source is pinned to
c971f93e7e76b0ef919bf6009e7b868bea04db7f; see the upstream
[loader](https://github.com/godotengine/godot/blob/c971f93e7e76b0ef919bf6009e7b868bea04db7f/modules/openxr/openxr_interface.cpp#L380-L393),
[cleanup](https://github.com/godotengine/godot/blob/c971f93e7e76b0ef919bf6009e7b868bea04db7f/modules/openxr/openxr_interface.cpp#L674-L682)
and [RID reuse](https://github.com/godotengine/godot/blob/c971f93e7e76b0ef919bf6009e7b868bea04db7f/modules/openxr/openxr_api.cpp#L3894-L3917).

This is an experimental source template, not a replacement for the product's
official SDK/editor/templates or the mandatory official-editor runtime gate.
Its build name is opennv_xr_profile_fix1, never official. Do not install it over
the global template or relabel an existing package. Distribution and physical
acceptance remain separate requirements.

## Build

Use a separate upstream checkout at the exact commit, the matching official Mono
editor and an isolated SCons executable with its generator dependencies. Follow
the official [Windows build prerequisites](https://docs.godotengine.org/en/latest/engine_details/development/compiling/compiling_for_windows.html)
and [managed glue contract](https://docs.godotengine.org/en/latest/engine_details/development/compiling/compiling_with_dotnet.html).
The internal patch does not require publishing a conflicting managed SDK package.
The exact official editor generates the native/managed glue, and real OpenNV
startup must still validate that glue against the existing managed assemblies.
The selected official-generated core glue hash is 889748612. The release template
does not report its native ClassDB hash; actual Continue checks establish bounded
managed compatibility, not an independently measured native API hash or complete
ABI certification. That verification and the distribution license inventory
remain required before promoting a source engine into a package.

```powershell
.\scripts\Build-GodotXrProfileFix.ps1 -SourceRoot $SeparateGodotCheckout `
  -SCons $IsolatedSCons -Godot $OfficialMonoEditor `
  -DependenciesRoot $SeparateBuildDependencies -Jobs 6
```

The builder rejects a different commit or unrelated source edits, checks the
exact reviewed diff, and downloads only pinned x64 Mesa, Agility and AccessKit
archives with SHA-256 verification. Explicit dependency paths preserve installed
SDKs. The AccessKit version matters: an older globally installed header can fail
this engine build. The build manifest starts as building and records failure or
the actual binary hash, identity, arguments, source/patch and dependency pins.
The source checkout retains the complete upstream licensing files. This template
has D3D12, Vulkan and native OpenGL; the optional ANGLE driver is not included.
It is not a performance-equivalent rebuild of the official distribution.

## Regression check

```powershell
.\scripts\Test-GodotXrProfileExit.ps1 -Godot $OfficialMonoEditor `
  -Template $ExplicitTemplate -SimulatorRuntime $ExplicitSimulatorManifest
```

The check stages only first-party fixture files and the production action map
under tmp, exports a synthetic project with the selected template and runs 45
process frames before ordinary tree quit. It never loads OpenNV's runtime
assembly or owned Fallout files, records frames or changes the registered XR
runtime. Per-child environment variables select the simulator. It retains the
actual process handle, exit code, full logs and template digest, and rejects
unexpected errors or missing completion. overallXrAccepted stays false.

Use -DuplicateProfile to create two distinct profile resources with the same
Touch path. The fixture verifies three resource entries and two Touch entries;
repeating the same resource would be deduplicated before exercising RID reuse.
The ordinary map must report two resources and one Touch entry. The official
template is a negative: -ExpectedProfileLeaks 1 checks its known leak explicitly.
That option does not mean leak-free acceptance. A repaired template must pass
both variants with the default zero expected leaks.

Follow synthetic checks with actual owned direct/launcher Continue and clean
shutdown, checkpoint integrity and the full runtime gate. A profile cleanup pass
does not resolve the remaining physical rendering, rig, gameplay or frame-pacing
failures.

The selected experimental template passes normal and shared-path synthetic
fixtures with exit zero, zero profile leaks and zero other errors. The exact
official template is the negative with one profile leak in both fixtures.
Actual optimized owned direct and launcher Continue/close checks also report
exit zero, zero startup/exit draw errors, zero profile leaks, drained readers
and unchanged original checkpoint. Flat Vulkan Continue/main-menu/Continue/quit
passes with the original checkpoint unchanged. Existing source-content and
presentation divergences remain visible in the game logs; these are specific
profile/draw ownership checks, not error-free gameplay acceptance.
