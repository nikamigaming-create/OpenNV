# Playable OpenNV execution plan

Updated 2026-10-29 under the user's direction: deliver working game code, with
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

The merged baseline is `c99d7f21` (PR #329), with the required local gate,
selected owned/native audits, diff check and all five hosted checks passed.
Local main equals origin/main. Current branch `codex/sandbox-sleep-continuation`
contains the reviewed source Sleep/marker/query-retirement repairs and selected
unchosen-meal failure capture; full publication validation remains pending.

Current ordinary gameplay is beyond the opening and well lesson. Receipt export
`440e7ae0` Continued the ingredient checkpoint, reached the campfire through
ordinary movement, crafted Healing Powder and completed F5 command 263 at
10:19:05.997 UTC without save/cleanup error. VCG03 15d912 objectives 10/20/30 are
complete and 40 is displayed/incomplete. Canonical SHA256 is
`5D7BDDD425DBB0259B55DAB97A0E675B0267A41349D5229CDD0A779A17A45BA8`.
The next needed candidate launch must Continue this cooking save toward objective
40, Sunny/Trudy, nearby trading and Caravan. Never replay opening, ingredients or
cooking unless actual authoritative state requires it. Newest Continue remains
unverified until that ordinary launch.

Marker actor 10a72b now publishes without its prior duplicate-history failure.
Sunny's Sleep 176ad3 selected, began native travel and handed off on schedule to
0200d5 without a surviving error. Physical bed arrival/wake remains open. The
unchosen-meal failure receipt passes synthetic contracts; its live fault capture
remains unverified because the original two-food error did not recur before the
successful save. Do not call retained failure an Eat behavior implementation.

During objective 40 travel, actor 157c8f selected IdleMarker activity 4/target
157cc0 and reached an unqualified script-visible procedure query. Detailed state
284 is the bounded diagnosis fixture. The game closed normally without
superseding the cooking save. Those bounded repairs are now reviewed/frozen;
selected source/contract checks pass. Integrated export 92183296 Continued the
saved Healing Powder with healthy owners. Final inherited-IDLE phase observation
is contract-tested after that candidate. The mandatory publication gate and diff
check pass; checked publication is the current boundary. Next play must keep the
bot stopped until continuous manual traversal clears the elevated rock corridor,
then continue Sunny/Trudy and ordinary trade/Caravan.

Sustained native 4K60, physical XR144, complete campaign execution and matched
retail pixels/audio remain unmet. Active non-radio speech saving, raven source
locomotion, background creature Find/Food, complete Caravan and full system
coverage remain explicit gaps. Neither a successful arrival nor selected feature
checks establishes complete movement or parity.

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
   PR is merged. PR #327 completed this boundary. Every new patch repeats it. A green
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
