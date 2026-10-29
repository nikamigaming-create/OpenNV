# Playable OpenNV execution plan

Updated 2026-10-28 under the user's direction: deliver working game code, with
the coordinator planning and reviewing and GPT-6.1 Sol at High reasoning doing
all implementation. This is the sole active implementation plan. Retained
[repair design notes](runtime-repair-design.md) provide technical context, not a
second work queue.

Latest user override: this is a greenfield project. Old saves, historical-state
restoration, backward compatibility and existing implementations are expendable.
Start a fresh game on the current product build. Replace or simplify broken
systems when that delivers working gameplay; do not preserve old machinery for
its own sake. Current-build saving remains a game feature, not a commitment to
load historical checkpoints. This overrides preservation-oriented instructions
in the retained notes and the initial coordination objective.

Current explicit acceptance target: sustained native 3840x2160 at 60 FPS both
indoors and outdoors, with retail behavior and presentation throughout
Goodsprings to the Strip. Combat, crafting, looting, quests, factions, trading,
Caravan, VATS and the other retail systems are required. Arrival and a selected
successful route do not establish coverage. Measure movement, streaming,
interactions and combat without concurrent builds; keep misses visible and fix
them in the integrated product. Continue until the target works.

## Honest starting point

OpenNV has working portions of New Vegas, not an accepted complete game. The
latest retained session crafted Healing Powder, closed the recipe menu, continued
Sunny's conversation and saved. Campaign completion, dependable population and
combat, complete presentation and physical XR acceptance remain open. Caravan
match input/execution and VATS still lack complete runtime integration. The
current integrated changes have not passed the full repository gate.

The inspected baseline is b94d8e8d. Existing uncommitted work spans gameplay,
save schemas, navigation, rendering, streaming, tests and diagnostics. Its
breadth and inconsistent build validation make regressions difficult to isolate.
Numerous successful component checks have not established sustained gameplay.
Performance optimization has also continued without a controlled integrated
gameplay result. These are delivery problems as well as unfinished engine work.

## One implementation and one build

- Use the shared checkout and one active `codex/` feature branch based on current
  `origin/main`. No parallel worktrees or alternative implementations.
- The coordinator owns priorities, adversarial review and integration decisions.
  One Sol High executor owns code changes, builds and gameplay runs. Additional
  robots, if needed, must use the same model/effort and get nonconflicting work;
  they must not create another build or branch.
- Review and replace existing code as needed. Publish coherent integrated
  changes; do not accumulate recovery snapshots or retain obsolete code merely
  because it predates this work. Owned retail inputs remain read-only.
- Finish a block, run the required gate, review and merge its checked PR, verify
  local main equals origin/main, then start the next branch. Keep one active PR.
- Builds and audits do not run during measured gameplay. Recording stays off
  except for a specific visual defect, with temporary frames cleaned afterward.

## Ordered delivery blocks

1. **Recover the integrated build.** Resolve actual
   build/analyzer/contract failures and establish which assembly is launched.
   Launch the actual Release executable and start a fresh game. Exit: rendered
   New Game works and the required gate is green. A gate
   failure keeps this block open. A green build alone is not playable acceptance.
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
retail appearance or behavior. Start with the reported Vigor Tester and
Reflectron textures/colors, then continue through the connected game.

Use one fresh gameplay profile and one current build. Run ordinary input until a
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
