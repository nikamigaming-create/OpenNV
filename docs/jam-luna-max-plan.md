# JAM and MCM implementation plan for Luna Max

Intended executor: `gpt-5.6-luna`, reasoning effort `max`. This is an execution
plan for the existing OpenNV task. It does not create another task or change a
model setting. Implement and verify each block, publish it, then continue with
the next unresolved source behavior. Work without subagents.

The user's priority is complete JAM first, including MCM, followed by TTW and
the remaining [ten targets](mod-compatibility.md). Mods remain additive launcher
options with automatic ordering and optional Advanced overrides. The player
selects their owned folders; ordinary launch, settings, gameplay and saved
continuation must work in flat and OpenXR. Dependency behavior belongs to that
scope. NVAC and the retail 4GB patcher are not targets.

This plan is durable direction, not a claim that JAM works today. Extend the
worklist when a newly reached source operation exposes another missing owner.
Do not redefine completion to fit the currently executable scripts.

## Start and resume

1. Read `AGENTS.md`, [current work](current-work.md),
   [architecture](architecture.md), [status](status.md),
   [implementation plan](implementation-plan.md), this plan and
   [script runtime](nvse-script-runtime.md).
2. Inspect the branch, working tree, remote main and open task PRs. Preserve
   unrelated edits. Finish an existing checked block before starting another;
   otherwise create a fresh `codex/` branch from current `origin/main`.
3. Read the next open block below and its existing owners. Inspect the selected
   local source and latest private failure report before implementing it.
   Reproduce stale reports when code or the selected sources have changed.
4. Implement one general capability end to end: source interpretation, state
   owner, native binding, persistence where applicable and meaningful tests.
   A new command name with a constant return or an empty callback is not work
   completed. Keep failed operations visible and retain their executed prefix.
5. Run focused checks during edits. Before publication run the selected owned
   audit and required repository gate. Open a PR, wait for its checks, merge,
   synchronize local main and verify a clean tree with `HEAD == origin/main`.
6. Update current work with only verified state, remaining failure and next
   owner. Continue on a fresh branch. If interrupted, leave exact branch/commit,
   test results, source identity and the first executable next action; do not
   leave only a narrative or a percentage.

Routine implementation, audits and the repository's checked PR workflow are
already authorized. Do not repeatedly ask whether to continue. Ask only for
information or an external action that is actually required; continue other
independent work while waiting. Physical headset acceptance is a separate user
action and cannot be inferred from simulator results.

## Verified starting point

The runtime baseline is main commit
[`252ba59`](https://github.com/nikamigaming-create/OpenNV/commit/252ba59f2f7d76e6084787e070d5585489b0bec6),
after [PR 45](https://github.com/nikamigaming-create/OpenNV/pull/45).
Its full local gate and all remote checks passed. Earlier numeric expressions
were published in [PR 44](https://github.com/nikamigaming-create/OpenNV/pull/44);
the launcher/source work is already merged. Rebase subsequent work on the latest
main, not on one of those historical feature branches.

Implemented at this baseline:

- Native launcher library, independent mod selection, folder dependencies,
  automatic load order, constrained manual overrides and separate stack saves.
- Direct winning ESM/ESP plus loose/BSA loading, including combined JAM/TTW/NMC
  source loading. All ten target source stacks open. This is not gameplay proof.
- Numeric NVSE assignments and expressions, scalar user functions, nested
  loops, source-bound quest/reference/global state and visible script failures.
- Per-script load/restart queries, main-loop callbacks and physical key edges.
  Process callback identities survive scene/save reload and bind to fresh world
  executors. Synthetic and native Godot event checks pass.

Still incomplete:

- JAM has 52 source scripts, with 34 parser rejections at this baseline.
  Passing the parser does not establish execution, event delivery or gameplay.
- All nine JAM MCM quest scripts first fail tokenization on constructed UI paths
  using text concatenation and `$` conversion. Their GameMode initialization
  also reads INI settings and JIP auxiliary variables.
- MCM's actual menu, script registration, controls, settings and persistence
  have not passed native or ordinary-input acceptance.
- Native DLLs are owned input packages. OpenNV does not execute their retail
  hooks. Required semantics must be implemented against OpenNV's real owners.
- Strings, arrays, chained reference expressions, several event families,
  effect/perk operations and mod UI/animation integration remain open.
- Full JAM gameplay and complete campaigns remain unavailable. Keep their
  existing support gates until acceptance justifies changing them.

The latest owned initialization audit stops at these concrete operations:

| Caller | First reached missing behavior |
| --- | --- |
| JBT initialization | `SetJohnnyOnRenderUpdateEventHandler` |
| JVS initialization | `PlayerRef.Dispel` |
| JHB initialization | `SetNthPerkEntryValue1` |
| JHI and JHM initialization | `UnloadUIComponent` |
| JDC callback admission | Chained dot call on the result of `GetEquippedItemRef` |

These are first failures, not an exhaustive feature list. Do not skip them to
make a later frame appear successful.

## Local inputs and evidence

The packages are already downloaded and extracted. Do not restart Nexus
downloads or ask for JAM's folder again. Revalidate file identity if the user
changes a selection. Local paths belong to this machine, not portable defaults.

| Input | Existing local path |
| --- | --- |
| New Vegas | `D:/SteamLibrary/steamapps/common/Fallout New Vegas` |
| JAM 4.6 | `D:/OpenNV-Mods/jam-4.6` |
| MCM 1.5.1 | `D:/OpenNV-Mods/mcm-1.5.1` |
| TTW 3.4 | `D:/TTW/Installed` |
| Other packages | `D:/OpenNV-Mods`, detailed in `tmp/mod-packages.private.json` |
| Godot console | `D:/code/gd/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe` |

JAM's selected dependency folders are `xnvse-6.4.9`, `jip-ln-57.30`,
`johnnyguitar-5.28`, `knvse-37`, `stewie-10.00`, `stewie-ini-2.1`,
`uio-2.30` and `mcm-1.5.1` beneath `D:/OpenNV-Mods`. Other downloaded presets
and TTW-specific dependencies are listed in the private inventory; do not add
every preset to a stack blindly.

Private evidence, never public repository inputs:

- `tmp/jam-events-source.private.json`: selected source graph/parser report.
- `tmp/jam-events-owned.private.json`: reached initialization state/failures.
- `tmp/jam-events-contracts.log`, `tmp/jam-events-native.log` and
  `tmp/jam-events-gate.log`: baseline synthetic, native and full-gate results.
- `tmp/jam-sources.private.json`: owned script text for local inspection only.
- `tmp/mod-packages.private.json` and `tmp/mod-stack-results.private.json`:
  package provenance and stack source checks.
- `tmp/jam-next-runtime.private.md`: optional local continuation details.

An unverified, incomplete four-file string-value experiment is preserved at
`tmp/jam-mcm-string-values-unverified.private.patch`. It is not in main and has
not passed compilation or tests. It changes the expression result type and
starts a string pool, but lacks host, local-variable, function-frame, save and
legacy argument integration. Use it only as scratch work after reviewing it;
do not apply it and call the capability implemented. The durable plan does not
depend on that patch or any temporary file surviving.

## Runtime ownership map

Paths below are relative to the repository. Read the current implementation
before editing; this map identifies ownership, not an instruction to grow large
files or create a parallel execution path.

| Concern | Existing owners |
| --- | --- |
| Statements, expressions, syntax migration | `runtime/src/Content/FalloutGameModeProgram.cs`, `FalloutNvseNumericExpression.cs` |
| Compiled forms/locals and user functions | `runtime/src/Content/FalloutScriptBindings.cs`, `FalloutScriptLocals.cs`, `FalloutUserFunction.cs` |
| Shared command/function execution | `runtime/src/World/Cells/FalloutReferenceScripts.cs`, `FalloutReferenceScripts.Functions.cs` |
| Quest scheduling, script session and snapshots | `runtime/src/Content/FalloutQuestScripts.cs`, `FalloutQuestState.cs`, `FalloutQuestScriptClock.cs` |
| Process events and native input | `runtime/src/Content/FalloutScriptEvents.cs`, `runtime/src/InputSystem/NativeScriptKeys.cs`, `RuntimeNativeScriptEvents.cs` |
| Ordinary native host binding | `runtime/src/RuntimeCoordinator.ReferenceEvents.cs`, `RuntimeCoordinator.NativeOwned.cs`, `RuntimeCoordinator.SessionMenu.cs`, `runtime/src/Campaigns/NewVegas/Opening/RuntimeNativeOpeningStageDriver.ReferenceEvents.cs` |
| Owned XML and live tile values | `runtime/src/Content/FalloutMenuXml.cs`, `runtime/src/Presentation/Ui/NativeOwnedMessageMenu.cs` (`NativeOwnedMenuTree`), `NativeOwnedHudMessages.cs` |
| Pause/menu and script presentation | `runtime/src/Presentation/Ui/NativeGameSessionMenu.cs`, `RuntimeNativeQuestScripts.cs`, `ProjectedSurfaceInputRouter.cs` |
| Inventory, AP/health and weapon behavior | `runtime/src/Gameplay/State/FalloutPlayerInventory*.cs`, `FalloutPlayerVitals.cs`, `FalloutWeaponHandling.cs`, `FalloutWeaponSpread.cs`; `runtime/src/World/Cells/RuntimeNativePlayer*.cs` |
| Reference effects, overrides and state | `runtime/src/World/Cells/FalloutReferenceWorld*.cs` |
| Persistent campaign state | `runtime/src/Gameplay/State/FalloutNativeCampaignSave.cs` plus the owning state snapshots |
| Package selection and ordering | `runtime/src/Content/FalloutModInstallation.cs`, `FalloutModStack.cs`, `FalloutModLoadOrder.cs`, `FalloutModSelection.cs`, `RuntimeLiveContentSource.cs` |
| Existing verification | `contract-tests/FalloutPluginRuntimeProbe/{NvseNumericProbe,NvseEventProbe,ScriptExpressionProbe,ModInstallationContracts}.cs`, `runtime/tools/NativeReferenceEventsAudit`, `runtime/tools/NativeModFolderAudit` |

Do not base mod UI on the legacy `OwnedGamebryoTileRuntime` projection, or JAM
gameplay on the old CellPlayer sprint/time-scale adapters. Extend the ordinary
native path and its shared C# state. OpenMW is not a source-code dependency;
local implementation-neutral observations may inform a contract, never supply
copied implementation or gameplay authority.

## Block 1: typed expressions and real string state

Start here. MCM's constructed UI paths are the reproduced source trigger.

1. Extend the shared expression value model to distinguish numbers, strings
   and forms. A form's string representation must use its owned name or the
   documented unnamed-form representation; do not stringify its decimal ID.
   Preserve vanilla `set`/`if` behavior and NVSE precedence/short circuiting.
2. Implement quoted strings, concatenation, `$`/`ToString`, comparison and
   string arguments/results in both expression and statement call paths.
   Parenthesized/dynamic string arguments must reach the command owner as one
   evaluated value. Do not split constructed paths into raw token arguments.
3. Retain source variable kinds alongside compiled SLSD/SCVR slots. Support
   `reference` as the published alias of `ref`. Resolve external quest locals
   and explicit forms through compiled bindings, not a global EDID search.
4. Own string storage, assignment/copy behavior, uninitialized state, lifetime
   and destruction. Preserve the distinction between legacy numeric string
   handles/aliases and NVSE text assignment. Support typed UDF parameters and
   return values with isolated recursive frames and correct cleanup.
5. Integrate string state into the existing save/session owner, ordinary native
   executor, result/stage scripts and headless execution path. Restore validated
   state before scripts resume; reject malformed IDs/types and truncated saves.
   Do not retain an old scene or executor through a string/function object.
6. Increment parser migration only for syntax actually newly admitted. Verify
   unchanged legacy saves retain their locals, progress, clocks and errors.
   Do not clear old runtime failures just because tokenization improved.

Proof: synthetic source scripts must construct a dynamic UI path, copy and
mutate independent strings, use string UDF arguments/returns, short-circuit
side effects and round-trip through serialized cold state. Check limits,
invalid types, alias behavior, destruction, recursive cleanup and migration.
Run the actual JAM source audit and report its new first failures. Reduced
parser failures alone do not close this block's state/execution tests.

## Block 2: MCM configuration and auxiliary values

1. Implement reached `GetINIFloat`/`SetINIFloat` and string counterparts with
   actual argument semantics, optional filename, section/key parsing and
   case handling. Read selected owned `Data/Config` defaults through the same
   source precedence as the rest of the stack.
2. Keep writes in an OpenNV user/profile configuration overlay. Never modify
   the selected game or extracted mod folders. Overlay values must survive
   process restart and participate in future reads. Use atomic writes, validate
   relative paths and retain unrelated keys. Establish missing/malformed-key
   semantics from evidence before assigning a convenient default.
3. Implement JIP auxiliary variable ownership and the reached get/set forms:
   owning form/reference, caller plugin, typed indexed values, public/private
   names and temporary/permanent lifetimes. Respect optional indices and the
   documented last-element index. Private ownership must follow the winning
   calling script plugin, including overrides.
4. Keep temporary auxiliary state in the process/session owner where required;
   permanent auxiliary state belongs to saves. Test save reload separately
   from cold process restart and new game. Do not give all storage one lifetime.
5. Bind these functions to the shared executor and the real player/reference
   identity. Re-run all nine JAM MCM initializers and trace each next failure.

Proof: change a value through a source script, read it through another legitimate
consumer, save, load, restart and read again with the expected lifetime. Verify
private-plugin isolation, public sharing, indices, missing values, selected-folder
precedence and unchanged hashes of owned files. Native UI testing comes next;
direct API calls alone cannot certify a working settings menu.

## Block 3: source-owned UI and working MCM

1. Resolve MCM's actual `The Mod Configuration Menu.esp`, configuration,
   `menus/options/start_menu.xml`, includes and `menus/prefabs/MCM` resources
   through the selected stack. Follow JAM/UIO's declared XML include/injection
   behavior; do not construct a hard-coded JAM settings page from global names.
2. Extend the existing menu tree with scoped tile lookup, live float/string
   traits and source-driven component creation/removal. Implement the reached
   `GetUIFloat`, `SetUIFloat`, `SetUIFloatAlt`, `SetUIStringEx`,
   `UnloadUIComponent` and related calls from their real contracts. Preserve
   dependency/trait evaluation and release detached tiles and resources.
3. Implement MCM's reached registration and option APIs, including
   `SetMCMFloat`/`SetMCMString`, and execute its own script behavior. Inspect
   actual signatures instead of assuming these commands take a generic path
   plus a value. Connect menu ID 1013 and menu open/close events to ordinary
   script scheduling and the paused native menu lifecycle.
4. Let source scripts/XML determine tabs, categories, options, defaults,
   conditional availability, labels and formatting. Route option interaction
   back through those scripts into the actual gameplay/config owners.
5. Connect ordinary flat pointer/keyboard and XR projected menu input, focus,
   scrolling, sliders, key binding, cancel and return to gameplay. Configuration
   must immediately affect its module when the source requests it.
6. Add MCM's required package artifacts to JAM setup validation for this full
   product scope. Keep package presence distinct from runtime support. Update
   synthetic dependency fixtures with valid synthetic plugin headers when
   adding an ESP requirement; existing dummy DLL bytes are not valid plugins.

Proof: ordinary launcher -> game -> pause -> MCM -> change each option type ->
return to gameplay -> observe the actual effect -> save -> quit -> cold Continue
-> reopen MCM. Check reset/defaults, reopening, reload, repeated registration,
module disable/re-enable and both input modes. Inspect UI and input behavior;
setting a GLOB from a test harness is insufficient.

## Block 4: remaining general language and dependency behavior

Build from the complete reached JAM/dependency graph, prioritizing the first
failure that prevents an ordinary JAM interaction. Split into cohesive PRs.

- Implement arrays/maps, indexing, iteration, assignment/reference lifetime,
  typed function values and cold serialization where the owned scripts use
  them. Cover cyclic/nested data only with explicit semantics and bounds.
- Implement chained reference calls and the reached inventory-reference
  behavior. An inventory reference must resolve the actual stack instance,
  count, equipment and lifetime; a base item identity is not an equivalent.
- Implement `Dispel`, active effect removal, perk entry mutation and other
  reached actor/weapon operations in their authoritative state owners.
  Changes must affect actual AP, movement, weapon spread and damage where used.
- Implement JohnnyGuitar render-update and reached hit/fire/menu/inventory
  events from their correct owners and order. Do not substitute quest polling
  for render callbacks or dispatch a gameplay mutation twice for stereo eyes.
- Bind required animation selection, kNVSE behavior, sound, time controls and
  UIO behavior to actual playback/render/input owners. Do not infer extension
  support or return a fictitious version from a DLL filename alone.
- Finish new-game/load lifecycle distinctions, focus-loss key release, callbacks
  without an active world and XR adaptation of configured mod actions. Preserve
  source key IDs while allowing real controller binding and cancellation.
- If a reached plugin has no usable source, add a proper compiled-script path
  for its actual instructions. Do not drop that plugin or treat bytecode as a
  successful empty script. Full arbitrary FNV mod compatibility additionally
  needs broader runtime/extension contracts; it is not implied by JAM passing.

The current `--audit-mod-scripts` command executes attached quests from the
selected entry plugin. It excludes dependency quests and does not reproduce an
ordinary world. Extend the existing audit/trace to expose reached dependency
scripts, callbacks, result scripts and relevant events. Do not interpret its
successful exit as complete stack execution or introduce fake player/UI hosts
that turn unsupported operations into passes.

Proof for each capability: a synthetic behavioral/negative case, actual owned
source execution reaching the owner, and native/ordinary evidence for its
visible effect. Retain source hash, script/event identity and earliest failure.
Continue until the complete selected stack's required behavior has owners.

## Block 5: all nine JAM modules through ordinary play

Use source-defined settings and limits, including their interactions. This table
is a minimum acceptance route, not a cap on what the authored scripts may do.

| Module | Required ordinary evidence |
| --- | --- |
| Dynamic Crosshair | Actual weapon/equipment, spread, stance, aiming and camera mode drive the authored reticle; settings and visibility update correctly. |
| Hit Marker | Real impacts and damage outcomes produce the appropriate marker, sound and lifetime; misses do not manufacture hits. |
| Hit Indicator | Actual incoming damage/attacker direction drives the source indicator through view changes, repeated hits and actor removal. |
| Visual Objectives | Active objective/quest changes, target position, travel and completion drive the correct labels/markers and cleanup. |
| Hold Breath | Valid aiming state, configured input, AP/effects and spread interact correctly; release, depletion and invalid states cancel. |
| Sprint | Real locomotion, AP, speed, animation and weapon state follow the source; input release, menu/death/furniture and load transitions clean up. |
| Bullet Time | Actual time and AP behavior, input, camera/weapon/effect interactions and cancellation follow the source without leaving global timing changed. |
| Weapon Hweel | The actual inventory/equipped item populates the source wheel; select, equip, drop, cancel and input focus preserve item/weapon state. |
| Loot Menu | Actual targeted container/corpse inventory, count, ownership and item actions drive the source menu; inventory totals and persistent transfers agree. |

For every row: enabled and disabled states, configuration changes, conflicting
input, repeated use, a cell/door transition, pause/resume, death/reload where
relevant, in-process load and cold restart. Verify shared state with flat-to-XR
and XR-to-flat continuation. Then exercise modules together: sprint/aim/breath,
bullet time/combat/markers, wheel/equipment/crosshair and loot/menu focus.

The first accepted milestone is an uninterrupted ordinary route with MCM and
all nine modules working on the selected dependency stack. It does not alone
certify every weapon, all campaigns, every combination or physical headset
readiness. Expand source/interaction coverage and resolve remaining divergence
before claiming the full requested JAM support.

## Validation and publication commands

Run from the repository in PowerShell. Keep recording off for builds and
headless checks. During a specific visual check, inspect both final eyes when
applicable and remove temporary frames in cleanup/finally paths.

```powershell
dotnet run --project contract-tests/FalloutPluginRuntimeProbe -c Release -- --script-contracts
if ($LASTEXITCODE -ne 0) { throw 'Script contracts failed.' }

$jamProbe = @(
    '--project', 'contract-tests/FalloutPluginRuntimeProbe', '-c', 'Release', '--',
    '--audit-mod-install', 'jam', 'D:/OpenNV-Mods/jam-4.6',
    'D:/SteamLibrary/steamapps/common/Fallout New Vegas',
    'D:/OpenNV-Mods/xnvse-6.4.9', 'D:/OpenNV-Mods/jip-ln-57.30',
    'D:/OpenNV-Mods/johnnyguitar-5.28', 'D:/OpenNV-Mods/knvse-37',
    'D:/OpenNV-Mods/stewie-10.00', 'D:/OpenNV-Mods/stewie-ini-2.1',
    'D:/OpenNV-Mods/uio-2.30', 'D:/OpenNV-Mods/mcm-1.5.1'
)
dotnet run @jamProbe
if ($LASTEXITCODE -ne 0) { throw 'JAM source audit failed to complete.' }
$jamProbe[5] = '--audit-mod-scripts'
dotnet run @jamProbe
if ($LASTEXITCODE -ne 0) { throw 'JAM execution audit failed to complete.' }

.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
if ($LASTEXITCODE -ne 0) { throw 'Diff whitespace check failed.' }
```

Stop on command failure; capture exit status as well as output. Save large owned
reports only under ignored local paths. The full gate covers Release/Debug,
formatting/analyzers, contracts, launcher checks and native project loading.
Use the existing `NativeReferenceEventsAudit` for relevant native event checks
and extend it when necessary; do not create another general proof framework.

Before changing support claims, additionally test the exported executable's
ordinary launcher and gameplay path, selected owned stack, settings and cold
continuation. Match retail and OpenNV source/state/input for a parity claim.
Byte/state, events, timing, audio, UI and final pixels remain independent lanes.
Commit only first-party code, synthetic fixtures and neutral documentation.

## Completion gates and later work

JAM is complete only when its selected, versioned source/dependency graph works
through ordinary launcher/input in flat and OpenXR, all nine modules and MCM
behave correctly together, settings and game state survive their required
lifetimes, remaining reached divergence is resolved, and the required evidence
supports the claim. Zero parse errors, a clean build, registration counts, a
screenshot or a callback test cannot individually satisfy that gate.

After that gate, implement TTW's combined FO3/FNV campaigns, DLC content,
start selection, travel and persistence with TTW NVSE and other required
dependencies. Preserve JAM as an additive selection. Distinguish YUPTTW from
standalone YUP, and require actual compatible variants/patches for conflicting
combinations. Then proceed through the other eight targets in the existing
priority list. Automatic load order cannot repair incompatible authored records.

All original campaign and physical-headset requirements in
[the implementation plan](implementation-plan.md) and
[recovery checklist](recovery-checklist.md) remain in scope. Finish first-party
runtime behavior as the selected source reaches it. Do not promise universal
binary-mod compatibility or a complete playthrough from exposed API names.

## Published contracts to consult

Use published behavior plus owned-source evidence. Resolve ambiguities through
controlled private observation under the repository's retail boundary; do not
guess a success value or copy another engine's implementation.

- [NVSE expressions](https://geckwiki.com/index.php/NVSE_Expressions),
  [string variables](https://geckwiki.com/index.php?title=String_Variable),
  [legacy set versus let](https://geckwiki.com/index.php?title=Tutorial%3A_String_Variables_3),
  [string cleanup](https://geckwiki.com/index.php?title=Tutorial%3A_String_Variables_4),
  [Sv_Destruct](https://geckwiki.com/index.php?title=Sv_Destruct).
- [GetINIFloat](https://geckwiki.com/index.php/GetINIFloat),
  [SetINIFloat](https://geckwiki.com/index.php/SetINIFloat),
  [GetINIString](https://geckwiki.com/index.php/GetINIString),
  [auxiliary variable ownership](https://geckwiki.com/index.php?title=Auxiliary_Variable),
  [AuxiliaryVariableGetFloat](https://geckwiki.com/index.php?title=AuxiliaryVariableGetFloat).
- [User functions](https://geckwiki.com/index.php/User_Defined_Function),
  [SetFunctionValue](https://geckwiki.com/index.php/SetFunctionValue),
  [main-loop callbacks](https://geckwiki.com/index.php/SetGameMainLoopCallback),
  [load](https://geckwiki.com/index.php/GetGameLoaded) and
  [restart](https://geckwiki.com/index.php/GetGameRestarted) queries.

## Executor handoff prompt

> Implement the JAM-first work in `docs/jam-luna-max-plan.md`. Read the required
> repository documents, inspect current main and existing edits, and continue
> from the first unfinished block. MCM is required. Use the existing owned
> packages and ordinary native runtime, preserve additive selection and automatic
> load order, and implement real state/behavior without no-op extensions or
> hard-coded mod outcomes. Work without subagents. Publish each tested block
> through the required checked PR/merge workflow, keep current work accurate and
> continue. Report precise remaining divergence; do not call parsing, package
> detection or a selected demo complete JAM support. TTW follows JAM acceptance.
