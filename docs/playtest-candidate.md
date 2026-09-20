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
ordinary combat is not accepted as complete. Environmental wind forces are not yet owned,
so physical props can settle and sleep. These limits must not be described as
completed functionality in promotional material.

The ordinary flat bot reached Primm and entered Nash Residence. Fresh flat and
simulator runs repaired ED-E with collected parts, recruited him, exited together,
let him kill an outdoor hostile, and looted that corpse. Follow resumes after
combat. Repaired flying actors can initially sit too high above a raised repair
target. Enhanced Sensors acquisition is saved, but its detection effect and
the NPC radio remain unbound. Muzzle-light flicker and some impact particles are
missing. Player melee and post-damage healing still need paired gameplay proof.

The user's flat and physical headset playtests are required before considering
a golden release. Record the mode, location, action, visible result, and build
commit from `release-manifest.json` when reporting a failure.
