# Playable OpenNV execution plan

Updated 2026-10-28 under the user's direction: deliver working game code, with
the coordinator planning and reviewing and GPT-6.1 Sol at High reasoning doing
all implementation. This is the sole active implementation plan. Retained
[repair design notes](runtime-repair-design.md) provide technical context, not a
second work queue.

Latest user override: this is a greenfield project. Old saves, historical-state
restoration, backward compatibility and existing implementations are expendable.
Use the current product build and meaningful current-build checkpoints. Replace
or simplify broken systems when that delivers working gameplay; do not preserve
old machinery for its own sake. Current-build saving remains a game feature,
not a commitment to load historical checkpoints. This overrides preservation-oriented instructions
in the retained notes and the initial coordination objective.

Current explicit acceptance target: sustained native 3840x2160 at 60 FPS both
indoors and outdoors, with retail behavior and presentation throughout
Goodsprings to the Strip. Combat, crafting, looting, quests, factions, trading,
Caravan, VATS and the other retail systems are required. Arrival and a selected
successful route do not establish coverage. Measure movement, streaming,
interactions and combat without concurrent builds; keep misses visible and fix
them in the integrated product. Continue until the target works.

## Current implementation boundary

The merged baseline is `a8c6b154` (PR #325), with its required gate and hosted
checks passed. Branch `codex/sandbox-activity-execution` is unpublished and has
passed its fresh required gate and selected owned NPC furniture/checkpoint audit.
Hosted checks await publication. OpenNV has working portions of New
Vegas; complete campaign, retail presentation, sustained 4K60 and physical XR
acceptance remain unmet.

Candidate `06d9e517-0d91-4e53-b0ee-c3fa50bb1c7f` passes canonical stage-200
exterior Continue. Both reached NPCs, 104f03 and 104f0a, complete LooseRaking
through End and return to marker selection or Wander without AI/animation
errors. Ordinary F5 command 103, generation 1, succeeded; the 21:56:13 checkpoint
retains two marker continuations and their idle request graphs. Continue from
this progress for the next gameplay check; cold restoration of the new marker
checkpoint remains unverified. The held-phase combat interruption guard is fixed
in source after this candidate and passes the publication gate.

Ordinary play then reached Sunny in the saloon and completed F5 generation 2.
Her next conversation stops at Cheyenne script idle 0c1991: finite creature
completion/return is unimplemented. The saved saloon state precedes that failure;
this is the next runtime owner after the current integration is merged.

An earlier integrated candidate passed actual Stimpak Drop/Take, 9 -> 8 -> 9,
through a settled physical body and E activation. Doc's real bed approach
supports saving; bed entry/exit remains unverified. Caravan and VATS runtime,
input and menus compile together, but an ordinary match/engagement is untested.
Per-instance magazines and Take All changes compile; targeted regression cases
passed the central publication gate. Repair/Mod and broader inventory use also
need ordinary gameplay coverage.

The interruption correction, final required gate and selected owned audit pass.
Next publish this coherent integration through a checked PR. Continue
connected play through the available inventory, combat, crafting, trading,
Caravan and VATS interactions. A passing local feature check does not close
campaign, presentation or performance acceptance.

## One implementation and one build

- Use the shared checkout and one active `codex/` feature branch based on current
  `origin/main`. No parallel worktrees or alternative implementations.
- The coordinator owns priorities, review and integration decisions.
  User-authorized GPT-6.1 Sol High workers implement nonconflicting files in
  parallel. One executor alone owns integrated builds, game input and Git
  publication; workers must not create another build or branch.
- Review and replace existing code as needed. Publish coherent integrated
  changes; do not accumulate recovery snapshots or retain obsolete code merely
  because it predates this work. Owned retail inputs remain read-only.
- Finish a block, run the required gate, review and merge its checked PR, verify
  local main equals origin/main, then start the next branch. Keep one active PR.
- Builds and audits do not run during measured gameplay. Recording stays off
  except for a specific visual defect, with temporary frames cleaned afterward.

## Ordered delivery blocks

1. **Publish a checked integration.** Finish the reached lifecycle correction,
   resolve actual build/analyzer/contract failures and establish which assembly
   is launched. Preserve the meaningful current-build checkpoint. Exit: the
   required gate is green, current-build continuation works, and the reviewed
   PR is merged. The current unpublished branch still needs this exit. A green
   build alone is not playable acceptance.
2. **Finish a dependable Goodsprings session.** Play from New Game through Doc,
   the tutorial interactions and quest
   consequences, fight, loot, use inventory and crafting, enter/exit buildings,
   and save at meaningful progress. Include available trading, factions,
   Caravan and VATS; implement missing actions when reached rather than skipping
   them to make the route advance. Repair encountered blockers in coherent
   gameplay batches. Check a current-build Continue at the completed section;
   do not replay the opening or cold restoration after every edit.
   Exit: ordinary play can complete this section and resume current saved
   progress, without forced quest changes, manual state edits or missing actors
   being counted as success.
3. **Reach Primm and the Strip through connected play.** Support actual quest,
   dialogue, encounter and streaming transitions, including ED-E where reached.
   Fix broken background behavior and exposed optional interactions as part of
   their shared system, not named-route exceptions. Exit: connected travel and
   quest progress survive current-build continuation; each reached failure is resolved
   or remains explicitly open. Arrival alone does not close world support.
4. **Extend integrated systems and campaign coverage.** Missing actions exposed
   in the earlier blocks must already have real input, effects, interruption and
   persistence. Extend ordinary play to remaining main-quest branches,
   endings, side content, DLC and all skills. Maintain a concise coverage list;
   unknown areas stay unknown. FO3, TTW and plugin coverage follow the standalone
   FNV implementation and remain part of the product objective.
5. **Deliver the physical XR game and performance targets.** Repair the tracked
   hands/weapon rig and render/session failures before offering a headset build.
   Flat and XR must keep the same gameplay and saves. Preserve native 4K60 flat
   and physical 144 Hz XR targets, source content and visual fidelity. Measure
   real streaming/combat workloads; a simulator or desktop number cannot pass
   headset acceptance. Physical user acceptance is required for the XR handoff.

Stalls, input loss, rendering failures and save corruption that block the current
session are fixed immediately inside its block. Performance work otherwise
follows measured gameplay costs; no isolated optimization loop displaces a
reached gameplay failure. XR stays an integrated mode throughout these blocks.

## The daily burn-down loop

Retail is the reference throughout this loop. Compare each reached interaction
and repaired presentation against equivalent retail state, including content,
materials, lighting, camera, animation, UI, sound and behavior. Use existing
trustworthy private references where they match; acquire missing references
through the authorized retail observation/input bridge. Keep unmatched or
different details open. Successful gameplay and component checks cannot close
retail appearance or behavior. The Vigor Tester and Reflectron still require
matched retail acceptance; continue the current connected gameplay session
without replaying the opening after each repair.

Use one gameplay profile and one current build. Run ordinary input until a
new failure occurs or a meaningful section completes. Identify whether it is a
game defect, automation defect, source-integrity problem or unmet performance
target. Implement a complete repair at the appropriate owner, replacing it if
needed, replay the failing
interaction once, then continue forward. A broken bot is not automatically a
broken game; improving automation must not replace gameplay implementation.

The coordinator reviews changes for stale-state publication, duplicate effects,
input ownership, actor disappearance, save continuity and hidden fallbacks.
Use targeted synthetic/owned-data checks when they catch a relevant regression.
Do not rerun passed checks without a new change or unresolved risk. Run the
mandatory full gate before publication, not after every exploratory edit:

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.8-dev7_mono_win64\Godot_v4.8-dev7_mono_win64_console.exe'
git diff --check
```

Every progress report says what the player can now do, what still stops them,
and the next concrete code change. Update `current-work.md` with current state
and next owner, not a growing history of individual checks. Do not promise a
finish date from record counts or one successful route. Retail comparison is
required for a parity claim; it must not become a substitute for implementing
missing game behavior. Keep owned inputs, saves and private observations local.
