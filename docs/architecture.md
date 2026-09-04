# OpenNV architecture

OpenNV has one runtime architecture: C# readers consume a legally owned game
installation in place and publish authoritative state to Godot.

## Boundaries

- Retail files are read-only. OpenNV never edits the selected installation.
- No Bethesda asset, executable, save, or converted derivative is committed,
  packaged, uploaded, or distributed.
- Runtime launch accepts a live installation root and a campaign identity.
- Loose files override archives through a case-insensitive source namespace.
- Active ESM and ESP records are resolved in load order with master-aware
  FormIDs. BSA members are resolved in memory.
- NIF, DDS, KF, audio, string tables, records, DAT, MAP, PRO, and FRM data are
  interpreted by C# owners inside the runtime.
- Gameplay and save state are authoritative and shared by flat and OpenXR
  presentation adapters.
- Unknown binary layouts and unsupported behaviors fail closed.

## Main owners

- `runtime/src/Content`: installation detection, plugin/archive readers,
  strings, media, records, and live source precedence.
- `runtime/src/Formats`: NIF and engine-family binary interpretation.
- `runtime/src/Gameplay`: authoritative inventory, stats, crafting, settings,
  and save state.
- `runtime/src/World`: cells, actors, collision, movement, interactions, and
  streaming.
- `runtime/src/Campaigns`: source-backed campaign progression.
- `runtime/src/Presentation`: Godot rendering, UI, character creation, and XR
  adapters.
- `contract-tests`: C# synthetic contracts and explicitly selected owned-data
  audits.
- `desktop`: launcher registration and invocation.

## Launch sequence

1. The launcher validates the selected installation and campaign.
2. `NativeGameInstallation` identifies the game and content root.
3. `RuntimeLiveContentSource` resolves plugins, loose files, and archives.
4. C# readers build record/resource relationships in memory.
5. Campaign and world owners create Godot entities from those relationships.
6. Save files contain gameplay state and source compatibility identity only.

## Promotion rule

A parser count is not gameplay. A rendered cell is not a campaign. Support is
claimed only for behavior exercised by ordinary input and persistent state.

## Live parity evidence

The parity subsystem uses one canonical little-endian telemetry contract for a
private, read-only retail observer and the public OpenNV runtime. Each producer
publishes hash-bound frames through a Windows shared-memory ring. The comparator
joins equivalent state keys, checks canonical state bytes exactly, expands the
first mismatch into typed field deltas, and retains a bounded in-memory frame
window around divergence. Godot can show retail, OpenNV, and absolute-difference
views; the C# evidence writer can encode selected retail-left/OpenNV-right MP4
clips with hash-bound reports. Retail observation is one-way and never supplies
gameplay state to OpenNV.
