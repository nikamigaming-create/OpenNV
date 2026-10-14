# Adversarial code audit: functional recovery

This is a source-inspection report for the working tree based on
`f0608dec50ed870f09be0a3684c283158ffaa294`. The tree is being edited concurrently.
Findings describe the code inspected, including defects already assigned for
repair. A completed textual scan is not a completed semantic review, a build
result, or gameplay acceptance. No game, build, or retail observation was run by
this reviewer. No owned asset, save, executable, private address, or raw reverse
engineering output is included here.

The required functional result remains: ordinary New Game, returned gameplay
frames and input, complete save, normal Quit, and Continue in a fresh process.
The requested three routes and videos remain separate acceptance work.

## Verified checkpoint

The full pinned Godot/C# runtime gate passes, including Release/Debug builds,
formatting, analyzers, contract probes, launcher and native project checks.
The selected owned birth-CELL audits pass with 1,610 references, ten genuine
engine-created plane/portal bases and three cold component cycles. Debug and
Release idle-field audits pass all 4,560 effective packages and 159 collections.
Focused Current/Primary getter, changed-owner/record and JSON checks pass.

The ordinary FO3 repeat returns loading, same-controller context/post-actions
and idle update. Its next failure is the unfinished inhibited/life late Player
tail; current diagnostics retain the shared failure without publication loss.
The failed investigation was stopped explicitly. Detailed fault capture is
explicitly partial; that request still needs an actual failed-session check.
The latest ordinary repeat additionally returns late Flags, independent
inhibition fields, Actor-time and XY/Z stores using actual cached Main time,
then fails at mover-delta. Pause and observed Quit/Yes exit normally with code
zero. Complete gameplay, save, cold Continue and routes remain unaccepted.
The original New Vegas save is unchanged. A private read-only retail FO3 launch
and observation succeeded but establishes no matched gameplay or pixel parity.

## What is actually preventing progress

The subsequent movement batch compiles and its focused field/controller checks
pass, including 100,000 existing position/change calls with bounded receipts.
Owned source execution exposed and corrected a constructor-relative selector
offset. Ordinary construction then exposed a false INI provenance/stack identity
check; the repair now borrows the actual injected numeric settings owner.
Complete movement is still unaccepted. Review also found failed-model factory
cleanup and cold nonnull-model reconstruction gaps; cleanup is repaired and a
separate cold graph/binding repair passes complete authored NIF graph preservation,
fresh identities and historical-read joins through three JSON reconstructions.
The ordinary FO3 repeat returns both mover-delta and its nonnull-data Actor
movement child, then refuses the following Player tail. Observed Quit/Yes exits
zero with source-owner disposal. Complete Player/Main, save and native Continue
remain unaccepted. The full reference-contract suite also caught an FO3-only
field validator applied to NV; its source-family predicate is corrected and the
suite passes.

A16 now uses typed raw GLOB operands and the actual owning plugin/live globals.
Authored operator, changed-value, caller and cold checks pass. A selected FO3
corpus audit resolves 778 comparison-global operands without running condition
functions. A17 now runs silent player INFO Begin/End/link effects without voice
publication; authored and compiled-result three-cycle tests pass. Native quest,
dialogue, complete save/Continue and retail parity remain unaccepted. These
executed component repairs supersede the original restrictions in A16/A17 below.

The reached TTW Book pointer mutation now has independent direction operands,
actual native SDK/store/restore handlers and shared module/image leases. Managed
and native Debug builds and authored checks pass. The first owned source audit
exposed an artificial writable restriction on its read-only CString input. The
corrected readable, non-executable, fully file-backed data guard passes authored
and owned audits. Actual unchanged TTW Load returns four SDK/store/restore events
per direction, preserves the four model events, then refuses the next distinct
mutation. Native resources retire on child exit. No complete module Load or Book
audio claim is accepted. The next Player-tail component passes authored and owned
source checks but awaits genuine loaded animation owners in production.

The critical defects are a mixture of missing behavior and authored admission
rules which reject valid state. They require different repairs. Removing every
`NotSupportedException` would hide real missing behavior. Conversely, retaining
a categorical rejection after its real C# owner returns is an implementation bug.

| ID | Severity | Concrete finding | Correction and verification | Classification |
| --- | --- | --- | --- | --- |
| A01 | P0 | Player scene has no complete returned frame. The latest ordinary FO3 run returns loading, controller context/post-actions and the idle-package query, then reaches the unconditional inhibited/life late-frame-tail refusal. Its persistence contract rejects returned clocks and any retained clock/following continuation, and the prelude rejects a second invocation merely because the previous prelude exists. | Finish reached frame behavior and its thread exit/bracket decrement; admit returned history, reject in-flight/failed history, run at least three real Main frames, then save. | Missing behavior plus contradictory admission. Frame owner assigned. |
| A02 | P0 | Complete process save rejected every nonnull mover `Join`, although the production Actor assignment now provides the actual Current setter, linked-null and High reset predecessors. | Root replaced the ban with an indexed saved-parent join and structural package-store comparison; source review found no remaining list identity comparison in these joins. Returned product save/cold verification remains required. | Artificial admission; source correction inspected. |
| A03 | P0 | Actor assignment child membership used generated record equality through `Contains`. Linked and High DTOs contain `IReadOnlyList` stores; JSON reconstruction creates separate list objects for their duplicate graph occurrences. A valid saved graph was therefore rejected. | Root added indexed child identity and scalar/store/nested-child structural equality. JSON roundtrip the actual returned Common/Actor graph before cold construction and repeat three cold processes; static correction does not establish product acceptance. | Persistence implementation defect; source correction inspected. |
| A04 | P1 | Ordinary player translation and mouse look modify Godot transforms, while the retained C# reference XYZ/orientation owners lack the corresponding ordinary movement producers. Saves capture the Godot transform and package queries borrow the separate retained reference fields. Startup position/reset is deliberately limited to startup ingress. | Publish one actual C# movement/orientation result after the physics/input step, consume it from script/package queries, persistence and presentation, and validate consistency before/after cold Continue. Do not reuse startup setters or manufacture an original callback. | Missing shared gameplay authority. |
| A05 | P1 | Exterior CELL attach always calls `OptionalTreeChild`, whose production implementation was an unconditional throw. Loading a target outside source phases 3/6 likewise enters a failure-only loader. | Implement the original optional-owner null/non-null dispatcher and real shared CELL loading. Verify both returned CELL crossings and failures; preserve ParentCELL/source membership. Genuine directed doors also need the callback in A11. Optional tree owner assigned. | Missing behavior. |
| A06 | P1 | New Vegas/TTW Main still has failure-only held-byte, timed-context and clock consumers; the Player scene adapter likewise only has the qualified FO3 scene path. Successful mod construction would still not produce an ordinary NV/TTW frame. | Qualify each edition's inputs and bind the same behavioral owners. Exercise normal non-menu frames and a CELL transition. | Missing source-family behavior. |
| A07 | P1 | Native source continuation captured modules in a dictionary keyed only by `Domain.Generation`, while every original module now shares the same domain. Two initialized modules collided. | Root now keys capture and validation by generation, original logical path and source hash. Genuine native global/hook/callback/co-save cold continuation is still explicitly missing; RaceMenu model mutation also retains an unconditional cold boundary. A smaller module set is not a correction. | Shared-domain identity source correction inspected; genuine native continuation incomplete. |
| A08 | P1 | Default Release developer launch built audio and exported while excluding `generated/**`, but did not stage the x86 companion which NV/TTW plugin/platform startup requires. The separate package script did stage it. | Root added the native build and one shared five-file staging helper to developer export and packaging, including staged hash checks. An actual exported launch remains separate verification. | Launch/package integration source correction inspected. |
| A09 | P1 | Main failure originally left Player and quest callbacks active. Broader inspection found omitted direct XR input, independent NPC/creature/combat callbacks, activation, projectiles, non-frame scripts, dialogue flow and Pip-Boy equipment/travel actions. | Current source guards the reviewed entries with the same actual World failure. Dialogue transitions receive that admission through constructor injection before speaker, voice, AddedTopics or SayOnce writes; Pip-Boy equipment/use/travel uses its injected World. Speech completion/resume preserves the original stopped prefix. Root reports quest-write, dialogue admission and related authored checks passed. Physical close/Pause/retirement remain possible; actual desktop/XR gameplay acceptance remains separate. | Conflicting authority after failure; expanded source correction inspected, no complete mutation-freeze claim. |
| A10 | P2 | Detection scheduling copied the accumulated visits array after each visit: 1+2+...+N elements per frame. | Root replaced it with a private builder, immutable observation copy and one final copy on return/failure. Capture refuses reentry, and finally stores the complete prefix before idle capture; source inspection confirms removal of this growing-copy loop. Allocation scaling remains unmeasured. | Avoidable O(N²) bookkeeping; source correction inspected. |
| A11 | P1 | Every genuine directed XTEL door payload creates a nonnull post-transfer callback. Successful non-reference pending transfer enters that callback, but its production factory unconditionally throws. This affects FO3 doors as well as NV/TTW. NV final flag processing also calls an unconditional unowned manager-word reader. | Implement actual door handler/context lease and source operation, then the independent NV manager field constructor/writers. Exercise a directed door transition and its final request release/null/flag stores. Do not omit the authored callback. | Missing transition behavior. |
| A12 | P0 | Pause and the held-Player null-context child now use actual C# menu, input, image and thread owners. Fresh source root instances prevent old close traits leaking into a reopened menu. Retained child values, constructor intervals and connected cold histories are validated against their actual owners. Live summaries are bounded and held frames reuse the qualified source. | The full pinned gate, owned menu-source audit and three authored cold component cycles pass. A fresh ordinary headless FO3 session opens, resumes, reopens with a distinct root and quits normally with exit zero and no recorded publication losses. The session still retains the late Player-tail failure. Verify healthy gameplay saving, cold Continue and ordinary dialogue/Pip-Boy routes. | Menu admission and lifetime defects repaired; full Player and save/Continue remain open. |
| A13 | P1 | Manual-save preparation originally paused the driver and quest subtree while actual Main's Always delivery remained active. | Current source acquires a resumable Main admission pause and drains its actual entered Task before source ordering, audio/tree/input pause and capture. Release resumes admission without completing a call. Cancellation cleanup now restores tree/mouse only after that preparation acquired their mutations. Root reports authored lease checks passed; stable actual save wait, commit/cancel and cold Continue remain required. | Source producer pause ownership gap; source correction inspected. |
| A14 | P1 | Manager/Common Dispose categorically rejected an incomplete failed managed factory/transfer even after callbacks had unwound. The authored failed-factory fixture explicitly had no native handle, yet expected Dispose to throw. This prevented normal retirement after a visible functional failure. | Current source permits unwound failed semantic prefixes to retire, retains their failure history, and still rejects busy callbacks and releases genuine early-controller leases before clearing Common owners. Root owns focused verification and actual failed-session Quit; a real native release failure must remain visible. | Artificial retirement admission; source correction inspected. |
| A15 | P1 | The content provider had no retired read-admission state. After Dispose, loose TryRead could still read and repopulate the cache; a later BSA access could lazily reopen an archive after the closure pass. | Current monitor-based synchronous read leases close new admission, permit already-entered nested dependency reads, drain them and independent constructor/discovery owners before archive closure, and reject reentrant observer retirement. Root reports actual authored provider retirement, nested-read/concurrent-drain and fresh cold-provider checks passed. | Current/cold lifetime defect; source correction inspected and Root-reported authored execution. |
| A16 | P1 | CTDA parsing accepts comparison-global flag 4, but shared AllPass rejects it through the 0x1e flag mask before evaluating any condition. Its comparison field remains a float interpretation of the raw GLOB operand, although current winning GLOB state already exists. Dialogue, quest stages and package selection all call this evaluator. | Preserve a typed raw comparison-global FormID, normalize it through the condition's owning plugin, and read its current value from the actual shared global owner independently of the condition-function result. Add synthetic and reached owned-data proof. No observed route failure is attributed to this capability gap yet. | Missing general condition capability; code-proven restriction, no executed route claim. |
| A17 | P1 | A source-eligible INFO with no response is categorically rejected before its result scripts or authored links can execute. The parser retains zero-response INFO fields, and the component test deliberately expects the eligible case to fail. Conversation Begin always publishes a voice response, so removing the guard alone cannot implement it. | Implement the genuine result-only INFO flow, Begin/End effect and link ordering, and settled persistence without manufacturing an audio completion. Test eligible and filtered-out empty INFO separately, then qualify reached owned data. No observed route failure is attributed to this capability gap yet. | Missing general dialogue flow; code-proven restriction, no executed route claim. |
| A18 | P0 | New controller read-history validation originally required every historical invocation's constructor to equal the scene's current constructor. Cold Restore retains old controller calls while creating a fresh scene constructor, so this rejected valid retained history. | Current source matches the exact historical (PlayerProcess, Constructor) lifetime interval and source/thread/factory joins. Root reports three fresh Scene/JSON cycles and tamper substitutions passed. The fixture uses a retained failed scene and authored child lineage; actual full controller-child aggregate cold restoration and functional save remain unverified. | Current/cold history admission defect; source correction inspected. |
| A19 | P1 | An earlier actual Main idle-package failure latched the Common boundary, then package diagnostic State invoked the normal gameplay package getter. That getter correctly refuses failed Common state, so live JSON stopped publishing and became stale; the separate marker reported 3,583 publication failures. | Current source provides immutable retained Current/Primary fields, availability and original boundary separately from gameplay admission. The inspected fresh FO3 repeat returned the idle query, retained the later World/Scene failure and recorded zero publication failures. Detailed fault inspection explicitly marks a partial snapshot and its authoritative-reference capture failure while leaving save admission intact. | Observed diagnostic publication loss; source correction and one Root-run fresh failure summary inspected. Detailed fault request and complete telemetry coverage remain separate verification. |
| A20 | P1 | First/change climate entry and streamed grid commit selected a winning lighting WTHR before source reset erased it. Streamed projection restore also fabricated a full save snapshot lacking source ownership and was rejected by full-save validation. | Reset before applying the prepared lighting projection, preserve override clearing and validate before source mutation. Keep full-source restoration independent. Authored first/repeat/grid/rejection and owned climate checks pass; fresh day/night views render. Original weather-frame/Moon completion remains unaccepted. | Actual rendering and streamed-state integration defects; typed source correction and component execution. |
| A21 | P1 | The selected owned celestial package uses VS2.0/PS2.x while the reader only admitted Model 3. Version extension exposed malformed DCL/TEXLD/modifier forms silently aliased or ignored by emission. | Decode distinct fixed banks and bind semantic inputs/outputs; enforce stage, masks, modifiers, five-bit declarations and reserved token fields. Authored positive/negative and actual selected package decode checks pass. No package fallback or pixel-parity claim. | Owned-format restriction and unsafe admission; source correction and component execution. |

P0 means it prevents the immediate save/Continue milestone. P1 affects ordinary
gameplay, the requested transitions/editions, or the product launch path. P2 is
a concrete scaling defect to repair before performance acceptance. These are
priorities, not a completeness percentage.

## Code evidence

Line references are the inspected working-tree locations; concurrent repairs
can shift them. The inventory contains per-file SHA256 values.

- A01: `runtime/src/Gameplay/State/FalloutStandalonePlayerSceneState.Prelude.cs`
  has the initial-prefix guard and terminal failure; the corresponding
  `FalloutStandalonePlayerSceneState.Clock.cs` has no successful return/decrement
  producer. `FalloutStandalonePlayerSceneState.Persistence.cs` originally
  explicitly rejected `Clock.Step == Returned`, required a failed clock/prelude,
  and rejects `saved.Clock is not null` / `saved.FollowingContinuation is not
  null` in `RequirePlayable` (the latter at inspected lines 178/180).
- A02: `runtime/src/Gameplay/State/FalloutActorProcessRuntimeSaveContract.cs`
  inspected lines 67–71 rejected any `operation.Join`. Production
  `runtime/src/World/Cells/FalloutReferenceWorld.ActorPackageConsumers.cs:57`
  enters the real mover reset with the actual Actor assignment join. The root
  repair is now in `FalloutActorProcessRuntimeSaveContract.Mover.cs`.
- A03: `runtime/src/World/Cells/FalloutActorProcessCommonState.ActorAssignmentValidation.cs`
  inspected lines 137/147 used `fields.LinkedCalls.Contains(linked)` and
  `fields.HighCalls.Contains(high)`.
  `FalloutActorPackageAssignment.Types.cs:54–67` defines the list-bearing DTOs.
  `FalloutNativeCampaignSave.cs` uses ordinary JSON serialization, without
  reference-preservation semantics. Structural equality is required even when
  two empty arrays have the same contents.
- A04: `runtime/src/World/Cells/RuntimeNativePlayer.cs:361` changes yaw and pitch
  in native input; `:386–453` owns independent physical movement.
  `RuntimeNativeOpeningStageDriver.Commands.cs:246` captures that transform.
  `FalloutReferenceWorld.SourcePackageConsumers.cs:156` reads retained source
  XYZ. `FalloutReferenceWorld.ReferencePositionConsumers.cs:80–84` refuses
  non-startup model reset. `FalloutPlayerReferenceOrientationOwner.cs:135–151`
  retains yaw/pitch delta boundaries rather than applying them; inspected callers
  only publish transfer placement and startup yaw into this owner.
- A05: `runtime/src/World/Cells/FalloutActorProcessRuntimeState.PlayerMain.cs:204,214`
  calls target load and the post-attach tree child;
  `runtime/src/RuntimeCoordinator.MainPlayer.cs:130–131,147–148` provides the
  failure-only consumers. This is downstream of the startup/Player frame repair.
- A06: `runtime/src/World/Cells/FalloutCampaignMainScriptConsumers.cs:55–66,78–89`
  rejects the NV held/timed/clock branches.
  `FalloutActorProcessRuntimeState.ScriptCaller.cs` invokes MainHold for the
  normal non-menu arm and TimedContexts unconditionally afterward.
  `RuntimeCoordinator.MainPlayer.cs` rejects scene mode/presence/body without
  the qualified standalone scene. This is not repaired by a successful DLL Load.
- A07: `runtime/src/Content/FalloutNativePluginCampaign.Continuation.cs:34,62,69`
  originally keyed/validated capture by generation. Current code defines
  `FalloutNativeSourceModuleCaptureKey(Generation, LogicalPath, Sha256)` for both
  capture and revalidation.
  `FalloutNativePluginCampaign.cs:151,173` assigns the same shared domain to
  each module. `RequireOriginalModuleColdContinuation` separately refuses
  initialized modules until their real native state owner exists.
- A08: `scripts/Start-OpenNV.ps1:5` defaults Release; the inspected export path
  initially built only audio. `runtime/export_presets.cfg:11` excludes generated
  content. `RuntimeCoordinator.NativePlugins.cs:26–27` and
  `RuntimeCoordinator.Steam.cs:25–27` select the adjacent companion path.
  `scripts/Package-OpenNV.ps1` already stages native services. Root is sharing
  that staging now; the original issue must not be listed as still unchanged.
- A09: `runtime/src/World/Cells/RuntimeNativeSourceMainScriptCaller.Async.cs:49–54`
  disables itself and reports into `RuntimeNativeOpeningStageDriver.StagePersistence.cs:42–49`.
  Root added `FalloutReferenceWorld.FunctionalFailure.cs` and `World.Capture`
  refusal. Current reviewed guards include `RuntimeNativePlayer.OpenXR.cs:150`,
  `Presentation/OpenXR/NativeXrRig.cs:142–143`,
  `RuntimeNativeReferenceEvents.cs:245,281,328,401`,
  `FalloutReferenceScripts.cs:83,274`, `FalloutReferenceScripts.ResultAuthority.cs:40`,
  NPC and creature frame/package entries, independent combat physics,
  `FalloutReferenceWorld.Health.cs:90,118`, and `HitCallbacks.cs:33`.
  Actual player/NPC flights bind their World before publication
  (`RuntimeNativePlayer.Projectiles.cs:176`, `RuntimeNativeActorCombat.Ranged.cs:467`).
  `RuntimeNativeProjectileFlight.cs:132–136` stops before simulation while
  retaining its in-flight state; `RuntimeNativePlayer.ExplosionExposure.cs:37,70`
  guards direct effect admission and subsequent exposure advancement.
  A further discovered bypass was `FalloutReferenceScripts.SpeechCompletions.cs`
  calling unguarded `DispatchFrameCore` directly. Production connects that
  source callback at `RuntimeCoordinator.NativeOwned.cs:1433` and
  `RuntimeNativeOpeningStageDriver.ReferenceEvents.cs:25–28`.
  `RuntimeNativeSpeech.cs:257,550,654–655` has explicit Pausable delivery and
  forwards real native completion. Current `SpeechCompletions.cs:30` now checks
  the shared World before source dispatch. Shared source/compiled execution now
  checks before instruction advancement. `Continuation.cs:14,40` prevents
  resume on a faulted World before changing its retained stopped prefix; without
  that early check the new execution guard was caught as a replacement source
  failure and lost the original prepared-detection membership.
  Further inspected UI bypasses were `RuntimeNativeConversation.Lifetime.cs:28–34`
  and `RuntimeNativeConversation.cs:148`: a response's callback can enter pure
  conversation flow without World-fault admission. With INFO flag 8, End results
  have already run, so `FalloutConversation.cs:43–46` skips script execution and
  commits authored AddedTopics directly. Begin likewise marks SayOnce before a
  guarded result. `NativeOwnedPipBoyMenu.cs:146,431–435,466–467` directly invokes
  item callbacks and shared Equip/Unequip without consulting its actual injected
  World. `RuntimeCoordinator.PipBoy.cs:46` also admits pending fast travel without
  that check. These are source-proven admissions, not executed post-fault game
  results. A genuine audio finish must remain visible without being mislabeled
  as a successful logical dialogue transition. Current pure conversation
  `Mutate` receives actual gameplay admission before its first semantic write,
  and production native Configure requires and forwards the same admission.
  Pip-Boy equipment/use and fast-travel entries now guard their injected World.
  The authored immediate-results fixture exercises the specific AddedTopics
  bypass, and separately verifies blocked voice entry, SayOnce, choice and cold
  restoration against an actual retained World failure.
- A10: `runtime/src/World/Cells/FalloutActorProcessManager.Schedule.cs` now uses
  `_activeScheduleVisits`, `ObserveSchedule()` and a final immutable visit copy.
  `FalloutActorProcessManager.Persistence.cs:10` refuses capture while busy;
  the final copy happens before the transaction becomes idle.
- A11: `runtime/src/World/Cells/FalloutPlayerRawTransferFactory.cs:20–22`
  always creates the actual directed-door callback. `FalloutActorProcessRuntimeState.PlayerMain.cs:138–140`
  calls it after a successful non-reference transfer; `FalloutMainPlayerPendingState.cs:136–141`
  resolves and invokes the nonnull payload handler.
  `RuntimeCoordinator.MainPlayer.PendingConsumers.cs:25–28` provides only
  unconditional callback/manager-word refusals. `RuntimeCoordinator.MainPlayer.cs:82–84`
  routes the final NV query to that reader.
- A12: `runtime/src/World/Cells/RuntimeNativeStandaloneInterface.cs:66–76`
  throws on any nonempty native menu census or open Pip-Boy.
  `Gameplay/State/FalloutStandaloneInterfaceState.cs:69–87,174` calls that actual
  manager lifetime reader for source interface queries.
  `RuntimeCoordinator.Menus.cs:13` adds session menu code 1013;
  `RuntimeCoordinator.SessionMenu.cs:123` pauses the scene tree but
  `RuntimeNativeSourceMainScriptCaller.cs:73` uses explicit Always mode.
  `FalloutActorProcessRuntimeState.ScriptCaller.cs:130` immediately reads the
  source interface, before Player. `PlayerMain.cs:162–165` subsequently enters
  the held child when the genuine cached-menu predicate requires it;
  `RuntimeCoordinator.MainPlayer.cs:91–92` still provides only a throw for it.
- A13: `runtime/src/RuntimeCoordinator.ManualSaveRequests.cs:83–87` supplies
  `sourceProducers: [scripts]` and `driverProducer: driver`.
  `Gameplay/State/RuntimeNativeSaveDriverPause.cs:20` disables only that parent.
  `RuntimeNativeSourceMainScriptCaller.cs:73–86` owns separate Always delivery.
  Current preparation additionally acquires the actual Main pause.
  `RuntimeNativeSourceMainScriptCaller.SavePause.cs:10–15,33–45` retains the
  active Task and validates the same living delivery owner.
  `RuntimeSourceDeliveryPause.cs:11–15,39–52` pauses admission, observes the
  actual call result and releases only admission. `RuntimeNativeManualSavePreparation.cs:68–73,85–103`
  waits for that return before acquiring its other pause owners.
  At `:94–101,197–199,226–233`, prior tree/mouse state is sampled at actual
  acquisition and restored only when acquired. An earlier source revision
  restored Begin-time samples on pre-acquisition cancellation and could change
  an external menu's state; that introduced cleanup defect is now corrected.
- A14: Current `FalloutActorProcessManager.cs:197–203` keeps the busy-callback
  check and permits a failed unwound managed factory to dispose.
  `FalloutActorProcessCommonState.cs:253–259` similarly releases actual
  early-controller fields first; `EarlyController.cs:176–184` clears each lease
  only after its release returns. `ActorProcessContracts.cs:186` now disposes
  its failed owner and cold copy idempotently. The original fixture's no-native-
  handle failure demonstrates why factory completeness was not a resource lease.
- A15: `RuntimeLiveContentSource.cs:221,261,276` and its resource/audio enumeration
  boundaries now acquire read leases. `RuntimeLiveContentSource.Lifetime.cs:14–42`
  controls admission and drain; its per-thread membership permits entered
  synchronous nesting and prevents retirement within an entered read.
  `RuntimeLiveContentSource.cs:356–367` joins producer owners, drains public
  reads and warmup, then clears payloads and closes actual created archives.
  `ModContentContracts.Lifetime.cs` exercises actual loose read admission,
  concurrent drain, nested resource dependency, observer retirement refusal,
  retired resolve/extent/enumeration/audio and a fresh reopened provider.
- A16: `Content/FalloutConditions.cs:29–35` accepts flag 4 while retaining the
  raw comparison bytes as Float32; `:45–46` rejects that flag before the supplied
  evaluator runs. `Gameplay/State/FalloutGlobalState.cs:22–29` supplies current
  winning global reads/writes, but AllPass has no comparison-global resolver.
  `FalloutDialogueTopic.cs:272`, `FalloutQuestStages.cs:71`, and
  `FalloutDialogueQuestSelection.cs:11` are concrete shared callers. This
  inspection does not establish which owned quest branch first reaches it.
- A17: `Content/FalloutDialogueTopic.cs:132–135,286–287` checks actual eligibility
  and then refuses selected empty responses. `FalloutConversation.cs:86–93`
  unconditionally publishes a response after entered results;
  `FalloutConversation.Continuation.cs:22–23` requires an actual response index.
  `ConversationContracts.cs:66–69` retains an authored empty INFO's conditions,
  results and links, whereas `:98–102` explicitly expects the eligible INFO to
  fail before effects. This fixture establishes the present support limitation,
  not successful result-only dialogue behavior.
- A18: `FalloutStandalonePlayerSceneState.PostProcessContext.Validation.cs:59`
  compares each read's constructor to `saved.Constructor`. Both controller
  source scopes use this helper. `FalloutStandalonePlayerSceneState.cs:13`
  creates a fresh constructor, while `Persistence.cs:52,55,68–69` retains old
  calls and appends the new parent lifetime; Capture `:14` emits the fresh
  constructor. The existing validated POV lifetime chain can establish exact
  historical process/constructor membership. Current helper checks that exact
  pair within the call's lifetime interval, the reviewed controller source, and
  retained thread factory/Main/thread. `ControllerReadCold.cs` exercises this
  pure validation helper across three fresh Scene/JSON cycles. Its child lineage
  is authored against a retained failed scene, not a full returned aggregate.
  These new read/query DTOs contain scalar records, so this is distinct from
  list-identity equality.
- A19: `RuntimeNativeOpeningStageDriver` exposes its package State through the
  native drive snapshot. The previous `RuntimeNativePlayerPackage.State` read
  `CurrentPackage`, which calls `FalloutReferenceWorld.PackagePresentation`
  and `FalloutActorProcessCommonState.PackageSlots.cs:40` into `ReadSceneQueries`.
  `SceneQueries.cs:38–40` rejects the Common boundary legitimately retained by
  `IdleAnimation.cs:27` and `HoldMutation`. Current `PackageObservation.cs`
  copies retained immutable fields/availability/boundary without that virtual
  gameplay read, checks actual actor/epoch/field-publication ownership, and the
  native package State uses this dedicated observation. The reviewed regression
  commits an actual package, latches a Common failure, then requires identical
  observation fields plus failure while the gameplay getter still refuses.
  Separately, the detailed native drive previously called `World.Capture`
  after a functional failure, whose guard correctly refuses a save. Current
  `RuntimeCoordinator.NativeOwned.cs:58–59,68–69,118–121` explicitly reports a
  partial diagnostic snapshot and authoritative-reference capture failure,
  without calling `World.Capture` on that branch. The fresh Root-run summary
  retains the actual World and Scene late-frame failure, Current/Primary fields,
  and returned controller children, with zero recorded publication failures and
  no state-error marker. This is one inspected failure summary; an actual detailed
  fault request and complete telemetry coverage are not inferred from it.

## Additional checked boundaries

The source early-controller audit and live locomotion use different physics
resources. `RuntimeNativePlayer.cs:224–234` constructs a configured capsule.
`FalloutReferenceWorld.EarlyController.cs` constructs/publishes the source
detached controller independently. A detached rounded-convex constructor audit
does not prove that the actual moving player's capsule has the source shape,
contact or slope behavior. The correct repair binds source physical state to the
actual locomotion owner; a visual walk alone cannot qualify one-to-one physics.

DataHandler current records binding and production read lifetime are now checked
separately. `FalloutReferenceWorld.ActorPackageAssignment.cs:63` requires the
actual live records receipt before package loading reads. It avoids calling the
idle construction guard during an entered command. The reader's loading thread
remains historical, distinct from the current source gameplay thread. The
binding DTO is not a live reader lease in a deserialized save; cold construction
must retain the association to the actual new reader, as the source repair does.

High NPC detection's real producer still ends at an explicit missing behavior
boundary after its cleared-list prefix (`FalloutActorProcessManager.Schedule.cs:169`).
It needs its controlled-actor/hearing/reactive/perk/timer behavior when that arm
is reached. The refusal cannot be removed because an independent detection ray
or scalar returned.

The old Electron package describes itself as migration compatibility; its
`desktop/src/main.mjs:1126–1131` always rejects launch, while its renderer can
display an enabled launch button. The ordinary product entry is now the Godot
launcher. The migration UI must not be presented as an accepted playable launcher.

Direct DDS upload constructs resident `ImageTexture` resources
(`Presentation/Rendering/NativeDdsTexture.cs:28`). No streaming acceptance follows
from the project setting alone. Texture residency budgets, frame allocations,
CPU scaling and complete route measurements remain separate checks.

## Architecture correction

C# is the correct place for winning records, current gameplay, scripts, clocks,
quests, inventory and saves. Godot is an input/presentation/physics adapter. The
current defect is not solved by another renderer: several authoritative values
are still split between the native character and retained reference owners, and
failure is not shared across producers.

Source-byte qualification, canonical fields, receipt lineage and final pixels
remain evidence lanes. They must not substitute for a behavioral producer, nor
become categorical denials after that producer has valid current state. A
complete semantic save persists actual current gameplay and reconstructs fresh
resources. It does not compare managed array identity, replay startup writers,
or require an identical old native pointer. Exact canonical/pixel parity is a
separate claim requiring its own matched evidence.

The finite recovery sequence is therefore:

1. Correct the valid-returned-owner admission defects (A02/A03 and null-animation
   package presentation), then repeat the same ordinary startup.
2. Finish and return the real Player/Main frame, with current C# movement and
   orientation and a shared functional fault policy (A01/A04/A09).
3. Bind/verify ordinary Pause/Save menu and actual Main quiescence (A12/A13),
   then save through the normal control, Quit, and cold Continue that exact slot in
   three fresh processes. Compare reached quest, inventory, position/orientation,
   package state and current resource membership. Run an actual CELL transition.
4. Finish source-family Main/Player and unchanged native stacks (A06/A07), then
   repeat the requested NV/FO3/TTW quest routes. Fix CELL loading/tree consumers
   at the first real crossing (A05), and verify the exported product (A08).
5. Record actual accepted rendered gameplay and measure route performance. Repair
   avoidable bookkeeping costs (A10) without changing source semantic order.

## Coverage and practical limits

At `2026-10-14T16:15:59Z`, the complete non-ignored first-party code/config
inventory contains 3,722 files and 505,785 lines. Of these, code comprises
3,572 files / 494,160 lines. Production managed/native code is
2,598 files / 351,895 lines. Additional code comprises contract tests, runtime
audits, desktop migration tooling and other first-party tools.

Every inventoried text file was read by the scanner and checked with ten
syntactic rules: all throw sites, refusals, failure-only expressions, collection membership,
collection copies, history mutation, current/cold identity, native/Godot
boundaries, threads/I/O, and fixed bounds. Its 106,301 matches are review candidates,
not 106,301 defects. The all-throw category has 27,783 matches, including comments
and repeated throw tokens on a line, so review does not depend only on message
terminology. All 423 production expression-throw matches have been inspected in
their expression context. Their complete callers and source contracts have only
been checked for the focused paths recorded in the manifest. Switch defaults,
restore-only anti-replay delegates and invalid input guards are included; 423
is not a count of unconditional blockers.
A missing match is not proof of correctness.

Focused semantic inspection covered the production launch/New Game → input →
Main/Player → movement → save → Quit → cold reference/controller path, selected
native-module ownership and persistence, relevant Godot adapters, default/export
launch scripts, package scripts and current save/scene contract tests. The
coverage manifest names each of 224 focused files and its inspected region.
255 additional files have only throw-expression context inspection; 3,243
remaining files have textual scan only. Exact lists distinguish all three
categories. The 3,498 files outside focused semantic inspection are explicitly
listed as not semantically reviewed beyond their recorded scope. No file is
claimed exhaustively reviewed merely because it was inventoried or scanned.

The expanded focused regions include content-provider/cache/async retirement,
shared condition flags and current globals, script function/recurrence/active-
effect/speech execution entries, winning loose-folder lookup, and supported-
collection load ordering. This expansion does not imply all parser, script VM,
quest, audio, texture or renderer code received semantic inspection. Files with
only a constructor, admission, callback or cleanup region checked are marked
accordingly.

Source correction inspection and execution are recorded separately. Root
reports authored provider retirement/concurrent drain/nested reads/cold reopen,
Main pause lease contracts, and healthy/faulted quest-write checks passed. These
are Root-reported component executions; this reviewer ran no build or test.
The latest Root-run owned FO3 startup returned loading in 233,290 ms, the
actual nonnull controller context/post-action children, idle query, late Flags,
independent inhibition reads, Actor-time and XY/Z stores. It refused the reached
mover-delta virtual. The argument agrees with the actual cached Main timer. Its
inspected summary retains the same World/Scene failure and package fields,
records zero publication failures and no last publication failure, and has no
state-error marker. Recording was off. Escape and the observed Quit/Yes buttons
returned normal process exit zero and source-owner disposal. This is a
faulted-session cleanup check. The previous run's diagnostic loss
remains documented as A19; one corrected summary does not establish complete
telemetry, whole gameplay or detailed fault-request acceptance.
No complete normal Player frame,
actual menu save/Quit/cold Continue, requested quest route, or requested video
is claimed by this report.

The pinned gate builds/tests components, launcher entry and authored native
fixtures. `Test-GodotRuntime.ps1` explicitly reports controller gameplay and
headless HDR gameplay as unexecuted and recorded-input checks as `campaign=false`.
The existing Player scene tests pass an initial no-clock component snapshot and
assert that failed clock/prefix states are refused. They do not prove a complete
returned Player frame can save. Only the actual ordinary milestone closes that
gap. Checked CI/publication is necessary but does not establish game support.

This report is suitable for publication after the root owner verifies final
references and incorporates the current repairs. The large scanner outputs and
private working-tree artifacts are not intended for publication.
