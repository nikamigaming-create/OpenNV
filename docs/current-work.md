# Current work

## Objective and boundaries

Complete ordinary New Vegas play through Doc, original character creation,
Vigor and psychology, the Pip-Boy handoff, leaving the house and Sunny's full
tutorial, then verify cold Continue. Owned files and shared C# gameplay own
state; Godot presents it. No replacement menus, named scene fixes or captured
reference values may become gameplay authority. Preserve architecture.md.

Work stays on main and in this task only. All three subagents are stopped at
the user's request. Do not resume delegation. Preserve unrelated work.
All 33 recovery-checklist.md acceptance requirements remain open.
scene-defects.md tracks component corrections without claiming scene parity.

## Current verified state

- The reported gurney wheel cutouts now survive GPU upload. The previous fix
  covered its separate falloff surface; native DXT1 upload still forced opaque
  alpha on the wheel texture. The shared DDS owner preserves encoded BC1 alpha
  and every authored mip, leaving opaque BC1 compressed. The owned GPU audit
  samples all 349,525 texels across ten mip levels: the old upload loses 63,464
  alpha values and the correction loses none. Ordinary room-79 skips the
  cinematic, accepts original creation and reaches free movement at stage 55;
  the close-up shows open spokes on both large wheels and the caster. Live
  telemetry confirms only the alpha-bearing texture expands; normal/mask
  formats and hashes stay unchanged. Matched retail lighting, shadows and
  camera/pixels remain open. See texture-alpha.md.
- Ordinary room-82 accepts original creation and Vigor allocation
  6/6/6/6/6/5/5, plays Doc's source reaction and psychology introduction, and
  reaches stage 80 without script, animation or package errors. The former
  Look end-event failure is resolved: the script target, physical head pose,
  authored animation override and subsequent StopLook execute. Doc's subsequent
  furniture package now follows NAVM to its authored entry point, plays the
  source sit-down sequence and completes once occupied. The selected owned
  audit verifies every phase and continuity; ordinary room-82 records the new
  package path and later occupation without initial placement. The seated
  endpoint is visually inspected; the complete live entry motion was not
  captured. Navigation now identifies its package and target, and actors
  publish actual world transforms. The previous couch-route report mistakenly
  used the prior package's eight-waypoint path. Player couch activation and the
  original questionnaire remain unbound. Retail is seated at its first
  questionnaire prompt; camera, menu and time states are unaligned. We are
  still inside Doc's house; no ordinary save exists at this checkpoint.
  See head-tracking.md and furniture-motion.md.
- Original name/Reflectron XML, bitmap controls, dynamic FaceGen, source hair
  and voice/LIP are connected. The user accepted hair/age for advancing.
  The original No/Yes creation confirmation now works: source scroll-unit
  extents prevent the false overflow that previously hid the entire dialog.
  The full owned creation audit and ordinary confirmation pass.
- Package idle playback evaluates candidate and parent IDLE conditions after
  replay cooldowns, then selects among eligible source entries. Source NPC
  faction inheritance and activity/package predicates bind the selected owner.
  Doc's source faction excludes the premature smoking idle at name/creation;
  a positive owned faction member passes. Synthetic, nine owned scenarios and
  ordinary telemetry pass. Dynamic factions, broader activity ownership and
  exact native animation phase/cigarette timing remain unbound.
- Single-INFO SayTo admits Goodbye with Say Once. The Vigor introduction now
  plays its original audio/LIP, executes SetObjectiveDisplayed and releases
  its speech wait without replaying the stage. Ordinary room-73 verifies the
  complete introduction and subsequent tester acceptance. Unsupported Random
  selection remains fail-closed.
- Shared quest state now binds winning QOBJ/NNAM declarations, displayed and
  completed flags, ordered change events, telemetry and persistence. Six owned
  opening commands and JSON round trips pass. Ordinary stage 55 displays
  objective 10, stage 60 completes it, the INFO displays objective 30, and
  tester acceptance completes it. HUD objective messages and target navigation
  remain unbound. The broader sweep admits 633 quest owners and 1465 objective
  declarations; seven quests retain duplicate-variable errors. These counts
  are source coverage only. Missing saved objective state fails closed.
- Production timed transitions now execute the source GameMode program through
  shared quest clocks. General function arguments, arithmetic and branches
  publish compiled quest-variable slots before source SetStage. Thirty owned
  SPECIAL/sex cases select eligible INFOs; all six balanced-case calculated
  values agree with retained native telemetry. Target-sex and quest-variable
  dialogue conditions read gameplay state. Ordinary room-74 plays the expected
  reaction without a fitted table or initialized-default shortcut.
- Stage quest-variable assignments now bind compiled references alongside
  existing global assignments. Shared state exposes variable values and exact
  storage bits. Synthetic source overrides, expression short circuits, staged
  failure and cold recurrence pass. Mixed global/local expressions, conditional
  stage results, synchronous effects after SetStage, exact claimed-script
  lifetime/MenuMode scheduling and dynamic quest start/stop remain unbound.
  The existing creation handoff executes its authored MenuMode block at close;
  this does not establish native menu-time event order. Reference-instance
  state and general trigger/furniture scripts remain unbound.
- The next package failure comes from a real event script, not a corrupt name.
  SCTX is size-delimited with an optional terminal null. Its general decoder
  now handles both encodings and rejects embedded nulls. All nine package
  declarations now load; events dispatch at begin, completion and change, with
  unreached scripts retained in their own compiled-reference scopes. Empty/
  comment lifecycles and the reached POEA Look now execute through their owners.
  Ordinary room-82 completes that event and the later furniture approach/entry.
  Later furniture packages complete only after the finite source entry; initial
  process placement remains a separate, explicitly identified disposition.
  Event topics, deferred change idles and persistent event state remain unbound.
  See package-events.md.
- Head tracking now binds source settings/BPTD, compiled references, target-slot
  lifetime and post-animation head rotation. Native observation confirms the
  default humanoid binding, first-person target, override threshold and rest
  axes. Synthetic/Godot checks, seven owned opening commands plus the package
  command, and the full gate pass. The first replay exposed skipped introductory
  commands against dormant references; those now retain an explicit no-process
  disposition, while a missing loaded actor still fails. Ordinary room-81
  records active head publication and subsequent source release. Automatic
  targeting, eye aiming, whole-body mode, complete process lifetime, persistence
  and matched native pose/frame timing remain unbound. See head-tracking.md.
- The replacement psychology panel and its direct skip to skill review are
  removed. Source couch/furniture activation and dialogue choices must own
  progression. Tag/trait and farewell replacements remain separate defects.
- Vigor plays its authored opening sequence onto Strength. Keyboard Up/Down
  changes the current attribute and retains review-row routing. The nested
  CanvasLayer camera lifecycle and owned eight-page/allocation audit pass;
  ordinary activation and paired Up work. Framing, backdrop, illumination,
  exact timing and final pixels still differ. Original review acceptance is
  now exercised in ordinary gameplay; review keyboard adjustment still needs
  verification beyond the component's pointer checks.
- Vigor activation now evaluates the source OnActivate predicates and orders
  its effects through compiled bindings. An ordinary early E emits zero
  effects while Doc continues speaking; the later E opens the menu and enters
  stage 65 immediately, as retained native review telemetry shows. World-hour
  bits and actor time remain unchanged throughout the paused allocation/review.
  The original background blur is visible and releases on acceptance. Source
  reaction/psychology playback then resumes. Six owned cases and synthetic
  overrides/ordering pass. Camera size and the background cabinet still differ;
  no world geometry is hidden. See activation-lifecycle.md.
- The blink-induced whole-face whitening has a general morph representation
  correction. Absolute targets preserve additive source movement and the
  packed normal/tangent basis across concurrent and signed weights. The old
  implementation fails the new regression; the candidate, 18 Forward+ samples,
  ten owned Doc surfaces and full gate pass. A fresh 16-second ordinary replay
  retains seven blink cycles, with no whitening in inspected blink frames.
  Expression-normal recomputation, exact blink phase and matched retail skin/
  lighting pixels remain open. See morph-lighting.md.
- Post-creation retail HUD/crosshair and movement instructions remain missing.
  Paired movement distances and collisions differ; the route to Vigor used
  separate ordinary inputs and is not matched-motion evidence.
- Source placement removes the obstructing room module; 19 selected native
  transforms agree. Clothing preserves its texture. The separate falloff and
  BC1 upload corrections remove the gurney's observed black fills. Original
  gift icons/brackets/bitmap text render and
  startup scripts produce 19 grants. Remaining HUD/radio, queues, fading,
  loading transitions, complete geometry/alpha/collision and pixels remain.
- Shared source quest clocks retain Float32 recurrence, overshoot and script
  lifetime. All 252 selected native initial countdowns match exact bits.
  Stage-global SETs publish the authored night hour. Complete script admission,
  MenuMode, mutable delays, dynamic quest start/stop and ForceWeather remain.
  See quest-script-timing.md.
- Preserve source vertex colours, CELL light direction, radius, projected fog,
  angular opacity, material emittance and blend-dependent no-lighting fog.
  Ordinary room-67 binds all 743 declared fog surfaces; all 68 no-lighting
  selectors match owned properties, 20 have native corroboration, and the 20
  material-emittance matches remain. Selected owned/GPU audits pass. Regional
  image improvements do not prove exact camera/frame alignment or pixels.
  See material-fog-and-falloff.md for contracts and retained diagnostic scope.
- Four source response gestures, finite release/resumption, chair exit/NAVM
  travel, IDLE repeats, KF sounds and preview blink pass component audits.
  Complete motion, overlap, cigarette timing/smoke, audio and lighting remain.
- Source image programs, blur kernels, shared-clock double vision and original
  menu-background effects are connected. Opening haze/focus/DOF and final GPU
  output remain unverified. Save v11 adds objective state to existing global/
  calendar/script/inventory/sky identities; full cold progression remains open.

## Live comparison and evidence

Retain retail room-54 and the current OpenNV run/session configuration.
Revalidate process IDs/state before input; keep one instance of each game.
Room-82 is the current ordinary run, at stage 80 in the living room with Doc
seated and the reached package events complete. Room-79 retains the wheel
close-up, and room-77 retains the former end-event failure. Retail remains at
stage 80 in the original dialogue menu at the first questionnaire choice.
Current cameras, menus and times are not a
matched comparison.

Use the private diagnostic bridge for ordinary keyboard/relative mouse input.
The public harness rejects native.click callbacks; retail buttons require an
observed keyboard selection and Enter. Skip the cinematic with short Escape
when rebuilding requires a fresh New. Never force stages, poses, clocks, menus
or teleports. No OS/Computer Use input. Win32 Ghidrust observe helpers are
limited to attach/modules/read/detach; observations never supply gameplay state.

Keep the harness hidden while coding. Use bounded observe captures of both
native buffers and states. Parse selected fields; never dump whole traces.
Trace inventory links source ranges, decoded resources, geometry, materials,
image-space passes and pixels; frame association and complete audio/event/draw
lanes remain unbound. Tracing stays off outside evidence capture. Intermittent
atomic live-state replacement failures are a separate open telemetry defect;
zero trace-loss counts do not cover them. All private addresses, owned files,
derivatives and captures remain outside the repository.

## Next owners

1. Bind ordinary player couch activation, its temporary third-person body and
   camera, and the original questionnaire. Doc's Look/StopLook, intervening
   speech and subsequent furniture approach/entry now complete.
   Source furniture/trigger scripts and dialogue choices must own the next
   transition. Do not restore a replacement psychology confirmation. Automatic
   head/eye targeting and exact pose timing remain independent open owners.
2. Continue the shared script owner through reference-instance state,
   trigger/furniture events, conditional result programs and exact scheduling. Replace tag/trait and
   farewell panels with original interfaces. Never use placeholder
   confirmations or selected stage targets to claim progression support.
3. Complete Pip-Boy, loot/skull/pool/physics, doors/exterior, Sunny's dialogue/
   combat/tutorial and cold Continue. Six actor-audit shutdown resources remain.
   Broader NV/FO3/TTW/plugin support and integrated OpenXR remain unfinished.
4. Preserve corrected source rendering. Remaining camera far-plane, native GPU
   draw/frame association, per-pass fog admission, premultiplied selection,
   lighting/shadows, ceiling response and Vigor framing need source owners.
   Do not fit HDR, authored colours or hide world geometry to match a frame.

## Required publication gate

The head-target/pose contracts, owned script/skeleton audit and full repository
gate pass. Ordinary room-81 verifies Look, its animation override, StopLook,
later source speech and the prior approach without an event error. Furniture
placement/continuity contracts, the selected owned entry audit and the full
gate pass. Ordinary room-82 verifies later navigation and occupation using
that owner; only the seated endpoint was visually inspected. Player furniture
interaction, native pose/frame parity and complete live motion review remain
open. Private evidence remains outside the repository.

The BC1 source-alpha contracts, selected owned rendering audit, all-mip GPU
comparison and full repository gate pass. Ordinary room-79 verifies open wheel
spokes and the new upload-format telemetry. The image-format validators moved
unchanged into their own file so the format-only probe does not need renderer
stubs. No matched-retail scene completion is claimed.

The full gate, nine-package lifecycle, six owned activation cases, packed
owned face basis and Forward+ morph checks pass. The original morph code fails
the new regression. Ordinary room-77 verifies source activation, paused menu
lifetime, acceptance, later speech/travel and the remaining POEA failure.
Earlier selected idle/voice/creation and Vigor GPU/input evidence remains
scoped to those components. Re-run relevant checks before publication:

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

GPU audits require the normal Forward+ renderer. Read architecture.md,
status.md, clean-room.md and parity-telemetry.md alongside this file.
