# OpenNV agent contract

OpenNV is a clean, first-party Godot/OpenXR reimplementation that reads a
user-owned Fallout: New Vegas installation. Read `docs/architecture.md` before
changing subsystem boundaries or promotion claims.

## Non-negotiable boundaries

- Retail ESM/BSA/NIF/DDS files are read-only inputs. Never commit, package,
  upload, or distribute Bethesda assets, saves, executables, or converted derivatives.
- OpenNV does not depend on OpenMW source or runtime. Reverse-engineering output
  must be reduced to implementation-neutral contracts; never paste decompiler
  output into OpenNV.
- Flat and OpenXR share authoritative gameplay and save state. VR is a
  first-class product mode, not a later camera patch.
- Keep slices bounded and reviewable. Do not add placeholder managers, stubs,
  proxy actors, or speculative abstractions.

## Current actor truth

- Direct actor records, FaceGen primitives, native Godot presentation, and a
  fail-closed retail differential are the supported direction.
- Trudy identity and application of the provisional retail shot-state contract
  currently pass; rendering still fails. Do not describe the actor or saloon as
  retail-parity.
- An initially disabled ACHR may appear only through an explicit proof override
  until quest, enable-parent, and package state are implemented.
- The active gate is exact per-shot retail state: live reference transform,
  camera projection/FOV, idle phase, arm-bone transforms, final head/hair
  geometry, followed by matched Godot frames.
- The canonical compact contract is emitted as `retail/retail-state-contract.json`.
  Godot actor captures must receive it through `--retail-state-contract`; do not
  restore authored placement, arbitrary animation time, or 75-degree vertical
  FOV fallbacks.
- Latest private proof: Godot capture `trudy-saloon-retail-state-20260822-r20`
  and differential `trudy-retail-godot-differential-20260822-r16`. Both shots
  have zero placement/yaw/FOV/phase error and sub-0.00002 arm transform error;
  pixel MAE remains about 0.080/0.085 and exact projection is unresolved.

## Canonical evidence and tools

- Retail portrait proof is owned by the sibling `nikami-worlds` repository:
  `scripts/Invoke-FNVJamBackgroundCapture.ps1 -Target Retail -Scenario RetailPortraits`.
- Private retail camera evidence lives at
  `D:\Dev\Tools\Ghidrust\workspace\evidence\falloutnv_1_4_0_525\camera`.
  That directory and its private binary are never distribution inputs.
- Retail FNV is WOW64/x86. Live analysis uses the private Win32 Ghidrust MCP at
  `D:\Dev\Tools\Ghidrust\builds\wow64-i686-codex-nogpu\i686-pc-windows-msvc\release\ghidrust.exe`.
  Use one long-lived MCP process and `process_attach` in observe mode; the
  default x64 Ghidrust correctly rejects WOW64.

## Promotion gates

Before merging actor or renderer claims, run all content tests, Release/Debug
C# builds, `dotnet format --verify-no-changes`, a native Godot capture, and the
retail/Godot differential. Identity success is independent from rendering
success. Enhancements such as HD textures, new shaders, or upscaling remain
optional layers and may not hide a failing vanilla baseline.
