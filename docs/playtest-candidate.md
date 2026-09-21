# Windows playtest candidate

This is an experimental OpenNV build, not a golden release or a complete
Fallout implementation. Bring your own legally owned New Vegas installation.
No game data or saves are included. Read `NOTICE.md` and `THIRD_PARTY.md`.

Extract the entire archive, keeping the executable, PCK, and runtime directory
together. Run **OpenNV Flat.cmd**, select New Vegas, select your installation,
and choose first person. For VR, start your headset's OpenXR runtime, run
**OpenNV VR.cmd**, and select OpenXR in the desktop launcher. Both modes use
the same OpenNV save. The VR launcher does not change your system runtime.
Godot and .NET are included; development tools are not needed to play.

Use WASD/mouse and E to move/look/activate in flat mode; Tab opens the Pip-Boy.
F5 saves in flat; the left controller's primary button saves in VR.
In VR use the thumbsticks for movement/turning and the controller pointer/trigger
for menus and interaction. The wrist device shares inventory and gameplay state.
Close the game before switching modes. Preserve a copy of your OpenNV save
before an extended test; existing retail saves are not loaded or modified.
OpenNV saves live under `%APPDATA%/Godot/app_userdata/OpenNV/profiles/`.

## What to test

- Continue or complete the opening; talk to Doc and check that he turns toward
  you, speaks, shows choices, and releases dialogue on Goodbye.
- Buy and sell an item; check caps and inventory, save, then Continue in the
  other mode and verify the transaction persisted.
- Open the Pip-Boy, equip a weapon, use a workbench, and check recipe costs and
  results. Exercise combat and corpse loot with a disposable playtest save.
- Walk outside, inspect cloud movement, distant structures/terrain and mobs,
  push a tumbleweed, and watch animals for independent idle timing.

## Known limits

The full campaign, arbitrary plugins and all weapon families are incomplete.
Ordinary dialogue and barter were exercised in flat and Elliott Tate's OpenXR
Simulator. Simulator evidence does not certify a physical headset. Some NPC
packages and source scripts remain unsupported. Missing SpeedTree vegetation,
streaming stalls, distant-material failures, and broader encounter coverage
remain visible issues. Gecko ragdoll/sever component checks pass with Jolt;
ordinary combat is not accepted as complete. Source wind flags now drive
independent exterior gusts in both modes. A distant body still falls below terrain;
cold dynamic-prop persistence remains unverified. These limits must not be described as
completed functionality in promotional material.

The ordinary flat bot reached Primm and entered Nash Residence. Fresh flat and
simulator runs repaired ED-E with collected parts, recruited him, exited together,
let him kill an outdoor hostile, and looted that corpse. Follow resumes after
combat. Fresh checks also cross the curb during a second hostile encounter.
Mobile actor placement now uses the authored navigation floor; ordinary repair
in both modes keeps ED-E off the counter. Enhanced Sensors acquisition is saved, but its detection effect and
the NPC radio remain unbound. Muzzle-light flicker and some impact particles are
missing. Ordinary post-damage Stimpak use now passes in both modes. Player melee,
VR crafting and an uninterrupted paired route still need gameplay proof.

Background content preparation adapts to process CPU/memory availability and
uses at most four workers. The tested safe renderer reaches 60 FPS flat and
45 FPS in Elliott Tate's simulator at one exterior checkpoint; cell-crossing
stalls remain. Streamed NPCs now prepare source geometry on bounded workers and
assemble complete bodies over multiple frames. A selected walk's largest upload
fell from 183 to 60 ms, but rolling p95 and cell commit did not improve.
Subsequent resident-index changes reduce two measured cell commits from 49/60
to 30/39 ms. A completed shot, reload or holster no longer triggers a full
campaign save. Ordinary firing/reload and explicit saves pass in both modes;
cold Continue preserves the updated magazine and ammunition across modes.
Explicit save capture/writing still runs synchronously and can pause play.
Separate rendering reached about 83 FPS in an intermediate XR
build but exposed a Godot shutdown error, so the safe mode remains the default.
Native actor route searches now share a two-millisecond physics-frame budget;
one node expansion can overrun it. A selected route request fell from earlier
52–142 ms samples to 2.71 ms after source-projection and search changes.
These are selected Windows measurements, not headset or cross-platform acceptance.

The user's flat and physical headset playtests are required before considering
a golden release. Record the mode, location, action, visible result, and build
commit from `release-manifest.json` when reporting a failure.
