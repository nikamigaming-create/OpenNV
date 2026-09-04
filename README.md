# OpenNV

OpenNV is a clean-room C# and Godot reimplementation for legally owned Fallout
installations. The runtime reads the selected installation directly. Retail
plugins and archives remain read-only, and OpenNV never distributes Bethesda
assets.

## Current state

OpenNV is under active development and is not a complete replacement for any
retail campaign.

- Fallout: New Vegas has the strongest live route: direct ESM/ESP/BSA loading,
  a bounded Doc Mitchell opening state machine, stage-200 campaign state, live
  player inventory, movement, activation, and validated cold Continue state.
- Fallout 3 has direct source transport and bounded Vault 101 opening work, but
  it is not yet a complete playable campaign.
- Fallout 1 and Fallout 2 have direct DAT/MAP/PRO/FRM readers and bounded native
  presentations. General campaign execution remains incomplete.
- TTW and JAM identities are recognized only in bounded compatibility work.
  Complete TTW, xNVSE, JIP, JohnnyGuitar, kNVSE, Stewie, UIO, and JAM behavior
  is not implemented.
- Flat play is ahead of OpenXR. Physical-headset acceptance is pending.

## Architecture rule

There is one product path:

```text
selected legal installation -> C# format readers -> authoritative gameplay state -> Godot
```

There is no offline asset preparation step and no generated retail-content
input to launch. NIF, DDS, KF, audio, strings, records, and classic formats are
decoded by the runtime from the selected installation.

## Build and verify

Requirements are .NET 9, Godot 4.7.2 Mono, Node.js 22 for the desktop launcher,
and PowerShell 7.

```powershell
dotnet build .\runtime\OpenNV.sln -c Release
npm test --prefix .\desktop
.\scripts\Test-GodotRuntime.ps1 -Godot 'C:\Path\To\Godot_console.exe'
```

Launch through the desktop app or pass a selected installation to Godot through
the launcher-owned `--data-root`, `--campaign`, and `--save-path` arguments.

See [current work](docs/current-work.md), [architecture](docs/architecture.md),
[installation](docs/installation.md), and [current status](docs/status.md).
