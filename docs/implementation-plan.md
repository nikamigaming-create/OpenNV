# Gameplay, playtest and release plan

Release priority: a workable playtest candidate and functional flat/OpenXR
side-by-side first. Fix broken interactions; leave visual polish for later.
The user's own flat and physical-headset playtests are required before calling
any candidate a golden release. The video uses a single eye from Elliott Tate's
OpenXR Simulator and is labelled accordingly.

## Immediate outcome

Implement the ten authorized targets in [mod compatibility](mod-compatibility.md),
starting with complete JAM support, then TTW, exposed as launcher options.
The user selects the owned base/mod/dependency folders; runtime source
resolution, settings and persistent state must work without a generated profile
or developer command. The requested acceptance is full out-of-box behavior,
including dependency semantics, ordinary flat/OpenXR input and cold save/load.
Use fresh `codex/` branches from current main for each published block. Do not
label registration, a parser pass
or the earlier sprint/time-scale adapters as mod support. The exact verified
state and next owners are in [mod compatibility](mod-compatibility.md).
Work in this task without subagents. Preserve the original campaign, release
and physical-headset requirements below.

Launcher UX must use original ImageGen artwork and readable native controls.
Mods are additive checkboxes beneath the active game. Load order is automatic
from authored dependencies and verified rules, with optional Advanced overrides.
Selecting mod settings must not replace the active game or disable other mods.

## Execution

Use copies of genuine saves and ordinary runtime input:
talk -> trade -> craft -> equip/reload -> fight -> loot -> save -> quit ->
cold Continue. Exercise the same state changes in flat and OpenXR.
Label diagnostic fixtures and selected checkpoints; they cannot substitute for
ordinary traversal or establish a complete campaign.

1. Establish one reproducible candidate. Preserve existing work, fix build/test
   blockers, run the required gate and selected owned-data audits, retain
   failures and code identity. Keep frame capture off during development.
2. Verify the shared interaction chain: dialogue choices/results/control release;
   merchant item/cap conservation, accept/cancel and persistence; station
   activation, recipe gates, rollback and ingredient/output counts.
3. Exercise combat across ballistic/automatic/multi-pellet, energy/beam/flame,
   launchers, melee/unarmed, thrown weapons and mines. Retain unsupported cases.
   Check animation events, ammo, health/limbs, NPC response, death, loot and saves.
   A selected working weapon does not establish every family.
4. Verify XR attachment/contact checks and real simulator input. Inspect both
   final eyes through head/hand motion, wrist use, dialogue, trade, crafting,
   firing and reload. Controls must remain readable and escapable.
   Resolve simulator death/dismemberment/loot failures before requesting another
   physical headset acceptance run. The user's hardware test separately
   establishes comfort, scale, tracking feel and controller alignment.
5. Test the exported executable: launcher/install selection, explicit flat/XR
   entry and copied-save cold Continue. Supply controls, logs and known blockers.
6. Record short functional takes with audio and measured cadence. Inspect the
   complete takes, then compose comparable actions side by side with mode labels
   and honest timing. Remove temporary frames in finally/cleanup paths.
7. Publish a checked PR, merge it, synchronize main, and publish the experimental
   package with hashes, notices and limitations. A test release is not campaign
   completion or retail/headset acceptance.

For each failure, inspect winning records and runtime ownership, reproduce the
earliest incorrect behavior, fix the general owner, check unrelated source
instances/overrides and negative cases, then verify ordinary presentation.
Builds, counts, identity and plausible stills do not establish playability.

## Preserved scope

Finish the original opening/menus, Easy Pete, Sunny/Cheyenne and the entire
tutorial, all connected Goodsprings interiors/exteriors, travel through Novac
to the Strip, and installed DLC/winning-plugin behavior. Preserve complete
NV/FO3/TTW objectives and classic work. The finite ledger is
[recovery-checklist.md](recovery-checklist.md), R01-R36; no requirement is dropped
to shorten a showcase. Discover actual scope from source references, world grids
and door links, including unnamed spaces.

## Implementation and lab

C# owns source precedence, formats, gameplay, events, timing and persistence;
Godot owns presentation/input/OpenXR. Both modes share state. Retail files are
read-only; transformed retail resources are never persistent launch inputs.
No OpenMW code/runtime or decompiler output enters the product.

Use the existing lab through real runtime APIs for arbitrary cell assembly and
teardown, actor/animation/physics stress, script/event replay, cold restoration,
provenance and concise failures. Disposable lab manipulation stays identifiable
and never counts as an ordinary playthrough. Retail observation is read-only;
an input bridge may mirror ordinary input but never write retail state.

Replace faulty implementation freely when shared behavior is demonstrated.
Remove artificial restrictions disproved by the corpus. Never hand-place named
actors, invent proxy scenery, hard-code quest outcomes, hide failures or create
location-specific success paths.

## Performance, quality and evidence

Keep cohesive owners and small files. Measure frame time, allocations, loading
and repeated travel. Bound in-process source/decoded reuse by winning identity
and lifetime; world/save owners retain state when presentation is evicted.
Avoid whole-corpus frame scans and duplicate uploads. Disabled diagnostics must
stop their collection overhead.

Fix geometry/skin/attachment ownership before tuning downstream appearance.
Source bytes, semantic state, event order, timing, audio, UI and final pixels are
independent lanes. Exact parity additionally needs matched source state and
moving retail evidence. Simulator proof cannot establish hardware acceptance.

Before pushing a runtime or claim change, run the selected owned-data audit and:

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

Use focused checks during edits and the full gate for publication. Do not repeat
unchanged broad tests or build a second proof framework. If progress stalls,
report the actual blocker and change approach.

[current-work.md](current-work.md) holds only current result, verification,
blocker, next executable outcome and necessary private continuation.
[status.md](status.md) summarizes capabilities. Technical contracts belong near
their owners; obsolete task narratives are not current priorities.
