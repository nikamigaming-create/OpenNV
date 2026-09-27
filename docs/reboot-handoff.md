# OpenNV restart checklist

Saved September 27, 2026 for the user's reboot. This is the resumption point;
the game remains experimental. Keep this list and `current-work.md` current as
work is completed. Work in this task without subagents.

## Resume here

Open `D:\code\OpenNV` and read `current-work.md`, `architecture.md`, `status.md`
and `implementation-plan.md`. Continue the existing
`codex/source-object-animation` branch, preserving its local WIP checkpoint.
Do not restart the investigation or discard the branch because its work is
unfinished. Inspect `git status --short --branch` and `git log -3 --oneline`.

The ordered broad priorities remain in [flat gameplay](flat-gameplay-plan.md),
the [implementation plan](implementation-plan.md) and all 36 open items in the
[recovery checklist](recovery-checklist.md). This handoff does not narrow them.

## Saved state and verification boundary

- [x] PR [#58](https://github.com/nikamigaming-create/OpenNV/pull/58),
  "Resume safe saved script reads and bind live interior queries", is merged.
  Local `main` and `origin/main` match
  `ab2a6031bea412374970e340a52e17bfec2c0980`. No open PR remains at handoff.
- [x] That merged block passed the complete runtime check, selected synthetic
  and owned-data checks, and PR checks. It safely resumes 37 old reference
  clock/random faults in the protected checkpoint; 23 resident recoveries
  were observed in an ordinary copied flat Continue. Shared script randomness
  persists, and interior queries/package condition 300 read current CELL state.
  Subsequent animation and package failures remain visible.
- [x] The next block's five code files and this handoff are preserved in a local
  WIP commit on `codex/source-object-animation`, based on that merge.
- [x] WIP Debug build passes, zero warnings/errors. Local log:
  `tmp/object-animation-reboot-build.log`.
- [ ] WIP world/save integration, script dispatch, behavioral checks and full
  runtime checks are unfinished. The WIP has not been pushed or released.
- [x] No OpenNV, Godot or ffmpeg process is running at handoff. Recording is off.
  No further game/capture session needs to finish before reboot.

## First work block: source object animation and harvesting

The protected save contains 51 failed PlayGroup calls across six plant scripts.
An OnLoad failure stops the reference script before normal harvesting can work.
The recovered clock/random reads also reach missing PlayGroup/IsAnimPlaying
owners. Scripted window NIFs expose alternative looping sequences that the
merged controller currently rejects. Implement one general source-backed owner.

WIP files already saved:

- `runtime/src/Formats/Gamebryo/FalloutNifFile.cs`: lazy source hash.
- `runtime/src/Formats/Gamebryo/NativeNifMeshBuilder.cs`: controller/hash identity.
- `runtime/src/Formats/Gamebryo/RuntimeNifControllerPlayer.cs`: selectable loops,
  pending transition, cycle boundary processing and telemetry.
- `runtime/src/Formats/Gamebryo/RuntimeNifControllerPlayer.Script.cs`: preliminary
  PlayGroup initialization modes and source-validated capture/restore methods.
- `runtime/src/Content/FalloutObjectAnimationSnapshot.cs`: snapshot declaration.

Next actions, in order:

1. [ ] Review the WIP controller semantics before extending them. Check queued
   full-cycle transitions, immediate selection, authored loop starts, callback
   generation changes, direct-clock behavior and end/start text-key ordering.
   A successful build does not establish any of these behaviors.
2. [ ] Bind managed object controllers to `FalloutReferenceInstance` through
   `RuntimeNativeReferencePresentation`. Use source controller/hash identity;
   reject ambiguity and source drift. Keep actor skeleton/weapon pose ownership
   separate. Unknown groups/owners must remain explicit failures.
3. [ ] Connect animation snapshots to reference capture/restore, eviction and
   cold saves without replaying consumed events. Current save schema is v21;
   add compatible migration if advancing it. Preserve existing destruction
   state and warm residency. The new snapshot is currently unused.
4. [ ] Bind PlayGroup and IsAnimPlaying through the shared reference/result/quest
   script host and both native host construction paths. Avoid per-frame whole
   scene scans. Verify optional group/type semantics; do not assume an actor
   clip name is the same as an object sequence.
5. [ ] Extend conservative recovery to exact legacy missing-PlayGroup faults
   only when inspection proves no earlier mutation can repeat. The selected
   plant OnLoad blocks have pure state guards and PlayGroup effects. Never
   blanket-clear script errors or repeat an OnActivate inventory award.
6. [ ] Extend existing `NativeNifInstanceAudit`, `NativeReferenceEventsAudit`
   and `ReferenceScriptContractProbe`: sequence modes, boundary events, instance
   isolation, cold pending state, source mismatch, all six owned plant models
   and the affected window model. Exercise actual source scripts, item grants,
   destroyed state and repeated activation without duplicate rewards.
7. [ ] Use a copy of the genuine save for ordinary Continue -> harvest -> inspect
   inventory -> save -> cold Continue. Keep recording off unless a specific
   visual check needs it. Do not modify either protected original checkpoint.
8. [ ] Run the selected owned-data audit and complete required runtime checks;
   publish a checked PR, merge, synchronize main, then start the next block on
   a fresh `codex/` branch. Update the experimental export after stable changes.

Relevant integration files:

- `runtime/src/World/Cells/FalloutReferenceWorld.cs`
- `runtime/src/World/Cells/RuntimeNativeReferencePresentation.cs`
- `runtime/src/World/Cells/FalloutReferenceScripts.cs` and `.Recovery.cs`
- `runtime/src/Content/FalloutGameModeProgram.Recovery.cs`
- `runtime/src/Gameplay/State/FalloutNativeCampaignSave.cs`
- `runtime/src/RuntimeCoordinator.ReferenceEvents.cs`
- `runtime/src/Campaigns/NewVegas/Opening/RuntimeNativeOpeningStageDriver.ReferenceEvents.cs`

Private source inspection is already in `tmp/world-animation-scripts.private.json`,
`tmp/world-animation-models.private.json`, `tmp/world-window-nif.private.json`
and `tmp/world-xander-nif.private.json`. Keep these owned-data extracts private.
The six affected source scripts are CoyoteTobaccoScript, HoneyMesquiteScript,
BananaYuccaScript, BarrelCactusScript, NevadaAgaveScript and XanderRootScript.
Read their authored choices; do not hardcode a plant/location success path.

## Following work blocks

1. [ ] NPC/creature routines and script events: continue the package procedures
   reached after interior condition 300. Cover patrol, sandbox/eat/sleep and
   package event scripts/topics through existing movement/script owners. A
   creature Patrol procedure is not permission to invent generic wandering.
   Saved quest-script failures also remain; PR #58's recovery is for reference
   failures, not a complete repair of quest state.
2. [ ] Combat: autonomous hostility/assistance, source hit events and reactions,
   essential recovery, death XP, blast attenuation, player knockdown and full
   explosion effects. Existing selected actor-vs-actor checks and reactions do
   not establish every encounter or retail-equivalent behavior.
3. [ ] Weapons: decode the Flamer's BSStripParticleSystem, BSStripPSysData and
   BSPSysStripUpdateModifier; bind source lifetime. Finish VR dynamite lighter
   attachment, tracked-velocity throws, recoverable thrown objects, remaining
   tracer/projectile effects, mines/remote triggers, bare fists, ammo variants
   and weapon mods. Re-run the complete winning weapon inventory and retain
   each failure; the old 496-record sweep is stale and was not a gameplay pass.
4. [ ] Ordinary interaction/campaign loop: ownership/theft and loot, batch
   crafting, restocking, radiation/addiction, dialogue/quests, leveling/perks,
   VATS and persistence. Resume JAM/MCM then TTW and all ten requested mods
   through their runtime owners; launcher selection is not mod support.
5. [ ] Stability/presentation: exported Quit Game heap corruption (also seen
   with DummyAudio), streaming/capture stalls, vegetation/lighting/LOD, routes,
   prop floor persistence and physical XR attachment/interaction issues.
6. [ ] Updated showcase after stable gameplay: ordinary flat and labelled XR
   simulator actions, two-hand use, every requested weapon family, reactions,
   autonomous fights and a genuine destructible car encounter. Inspect footage
   and report uncovered cases. Keep retail comparison and physical headset
   acceptance distinct from simulator footage.

## Local inputs, checks and deliverables

Owned data: `D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data`.
Required checks before publishing runtime work:

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

Merged recovery audit and full-check results remain in
`tmp/world-script-recovery-owned.json` and `tmp/world-script-runtime-gate.log`.
The ordinary copied Continue run is under
`tmp/development-lab/world-script-recovery-one/`.

Protected originals, unchanged at handoff:

- `local/playtest-20260927-world/save.json`
- `local/playtest-20260920-companion/save.json`

Both SHA256:
`800A37CC6C97C543393A544229C4E71ACB213B45C12FC4584EF3E70E94E2FBB7`.

Latest exported candidate remains
`local/releases/OpenNV-0.1.0-experimental.20260927.5-windows-x64`, built from
`06ffd2cadd1f0e0a180882a666489503bea4aa30`. It does not include PR #58 or this WIP.
`local/playtest-20260927-world/Play Flat.cmd` still launches that candidate.

Retain requested footage in `local/recordings/weapon-showcase-20260927/`:
`OpenNV-weapons-flat-VR-updated.mp4` and
`flat-companion-response-candidate-five.mp4`. These are selected gameplay
clips with remaining failures, not all-weapon/mod or physical-headset acceptance.
Do not accumulate new frame archives while implementing. Previously rejected
temporary-image deletions must not be retried as part of this handoff.
