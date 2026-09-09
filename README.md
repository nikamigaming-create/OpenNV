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

## Classic Fallout dioramas

Fallout 1 and Fallout 2 share one source-driven hex world. MAP/PRO/FRM identity,
positions, elevations and gameplay rules remain the authority. The visual goal
is a continuous, cinematic 3D diorama that retains the original games' palette,
silhouettes, portraits, furniture, signs and surface detail. Owned FNV/FO3 models
and materials, Blender geometry and atmosphere add depth around that layout.
Turning on the hex grid must expose the same positions used for movement and
targeting; appearance changes cannot move or replace those positions.

The saved direction and all-map worklist are in
[world realization](docs/classic-world-realization.md), with model bindings and
the Blender workflow in [scenery authoring](docs/classic-scenery-authoring.md).
The three local concept boards and their prompts are preserved in
`local/fo1-world-concepts/`. They guide style, not placement or gameplay rules.
Runtime recipes live in `runtime/config/classic-*-v1.json`, and first-party
model geometry lives in `runtime/assets/classic/`.

The private searchable asset worklist is generated at
`local/classic-asset-inventory/latest/index.html`: both classic games' art,
source maps/elevations, placed objects and nested inventories, with FNV/FO3
donor catalogs. Missing models and unresolved source data remain explicit.

The current [actor and scenery recovery](docs/classic-actor-recovery.md) identifies
which models are visible and which 3D forms remain missing. Source-bound humanoid
and creature candidates use owned NIF/DDS/KF assemblies. Tribal hair/proportions,
the source-equipped temple guard spear, real weapon shadows and 3D idle clocks
are live. F4 switches the world, inventory items, equipped paper doll, loot,
quantity dialogs and HUD weapon together, including while inventory is paused.
The inventory mode button controls the same state. F3 compares the live scene.
Source doors now share pointer interaction, FRM animation, dynamic hex blocking
and v4 save state in both games. Door use and Shift-click examination display
original source messages in the scrollable HUD. Scripted restrictions remain enforced; see
[interaction coverage and archive recovery](docs/classic-interactions.md).
Original inventory/loot/HUD art uses measured, aligned bitmap text. Both games
share partial transfers, nested bags, dropped items, hand/armor slots, magazine
unload/reload and saved ownership. The source MOVEMULT quantity panel is live;
see [inventory and equipment](docs/classic-inventory.md) for controls, evidence
and remaining systems. Full item use, likeness, combat and questing are unfinished.

The current private inventory recording is
`local/classic-runtime-video/fallout-1-2-inventory-3d-toggle.mp4`
(1:12, 1080p/30, silent). It shows both campaigns' world/inventory switches,
equipped paper dolls, quantity UI, Bones loot portrait and spear equip/unequip.
Many item analogs and complete gameplay still need implementation.

The earlier private native video is
`local/classic-runtime-video/fallout-classic-analogs-and-inventory.mp4`
(1:43, 1080p/30, silent), with chapter/scope details in the adjacent JSON.
It includes both games, sprite/3D comparisons, FO1 movement and item transfers,
the corrected text layout, and explicitly labeled map inspection.

The shared browser exposes each owned map and elevation. G toggles the grid,
R toggles roofs, Q/E orbit, and the mouse wheel zooms. Continue restores the
campaign's saved player; the map browser itself is a scene viewer. Combat,
scripts, general door/elevation interactions and complete campaigns remain
unfinished. The locally rebuilt Windows launcher is
`desktop/release/win-unpacked/open-nevada-launcher.exe`.

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

For normal Windows play, `scripts/Start-OpenNV.ps1` defaults to a native release
export and registers it with the same launcher. Install matching Godot 4.7.2
Mono export templates first. `-ValidateOnly` builds and checks without starting
or registering the launcher; `-Configuration Debug` retains the project runner
for debugging. A plain `dotnet build -c Release` does not make Godot's `--path`
runner load release assemblies. The local export contains OpenNV code and
first-party resources; owned game data and saves remain external inputs.

See [current work](docs/current-work.md), [architecture](docs/architecture.md),
[installation](docs/installation.md), and [current status](docs/status.md).
