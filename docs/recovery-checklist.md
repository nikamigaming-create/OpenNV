# Opening-to-Sunny recovery checklist

This is the acceptance queue for the current recovery, frozen on 2026-09-04.
It contains **33 requirements: 0 accepted, 33 open**. Existing working components
are retained; an open row means its whole acceptance condition has not passed.
This is a bounded recovery scope, not a denominator for whole-game parity.

Every placed reference, inventory item, dialogue response, script transition,
animation object and UI tile within a row comes from the winning owned graph.
Do not replace those sets with selected examples. Newly discovered requirements
must be added explicitly with the reason; never silently remove or redefine an
open requirement to make the count decrease.

## Closure rule

A row closes only after its source contract is exercised through ordinary input,
its runtime state is checked, and its visible/audio result is inspected where
applicable. Record the build and private evidence identity, plus relevant tests.
Matched retail evidence is additionally required for an exact/parity claim.
The full repository gate is required before publication. Asset counts, successful
decoding, a build pass and a plausible still frame cannot close broader behavior.

## Work order

At each matched opening checkpoint, stop on a discrepancy, retain state and
frames, fix its owner, replay that section, then advance. Skip the cinematic for
these iterations. The current priority is missing notifications (R24/R33),
focus/distortion/menu blur (R22), and transparent surfaces (R11/R18/R21), then
remaining creation, actor performance and room gameplay. Continue through the
outdoor and Sunny route (R25–R29), persistence and integrated acceptance (R30–R32).
Existing failures stay in this queue when user input changes the immediate order.

| ID | Requirement and acceptance condition | Current evidence / missing owner |
| --- | --- | --- |
| R01 | Complete winning source graph for this route, with every reachable unsupported record/resource/runtime entity reported | Readers exist; complete route reconciliation remains open |
| R02 | New → owned intro with correct sound, timing, interruption and natural completion → opening | New/movie/Escape works; natural completion and matched timing unverified |
| R03 | Wake-up, sit-up, seated and exit camera transforms/projection/pause agree at matched times | Source KF hierarchy/FOV bind; complete matched timeline and exit open |
| R04 | Doc's source body, outfit, face, hair, materials and attachments remain correct in motion | Direct assembly works; face/material correspondence unverified |
| R05 | Correct chair occupancy, sitting layers, entry and exit, with source root transfer | Run room-17 visibly fixes floating root; complete entry/exit still open |
| R06 | Source package idles, cigarette/object attachment, gesture layers and interruption/resumption | Seated base, collection playback, ANIO and source replay cooldown bind; condition admission, full timing and matched motion remain open |
| R07 | Source effects, including any smoking effects, start/stop/attach correctly | Old smoke uses custom sphere puffs; this is not source-driven smoke support |
| R08 | Complete opening voice, response ordering, subtitles and interaction/skip rules | Four initial INFOs and post-name mirror handoff exercised; full dialogue open |
| R09 | Voice-timed lips, expressions, listener reactions, eye/head aiming and blinking | Owned TRI morphs now follow LIP speech time on actual meshes; private paired run30 shows mouth motion. Idle/speech face blending, aiming, reactions and complete matched timing remain open |
| R10 | Original name menu fonts/art/layout, caret, selection, input, acceptance and world pause | Owned XML/font/art and name pause bind; complete visual/input acceptance open |
| R11 | Original Reflectron shell, screen materials, lights, buttons and camera from owned data | Live entry now uses direct NIF/XML/FNT presentation; all twenty pages render in the native diagnostic. Ordinary matched replay and material/effect acceptance remain open |
| R12 | All supported source race/sex/face/hair/eyes controls change authoritative player identity | Owned executable declarations bind 43 face controls to CTL; source presets, part and RGB editing owners added. Randomization, complete input and saved-state acceptance remain open |
| R13 | Reflectron face preview updates correctly for each edit, with source geometry, pose and material | Source player and complete MTIdle animate in the direct preview; paired run40 fixes teal hair and the user accepted current hair/age presentation. Bounds, drag/zoom, complete edits and exact material agreement remain open |
| R14 | Walk to/use the Vigor Tester; original device/UI and SPECIAL allocation/results | Native functional entry exists; original presentation and ordinary route open |
| R15 | Complete psychology dialogue, choices, response effects and source sequencing | Existing owners available; direct ordinary sequence unverified |
| R16 | Original tag skill and trait menus, all source choices, validation and results | Functional substitute entries remain; source UI and route open |
| R17 | Farewell, inventory grants, control release and quest transitions all execute correctly | Partial native owners exist; complete ordinary farewell unverified |
| R18 | Every room reference/setpiece loads at the winning source transform with correct geometry/material/collision | Room draws; full reference-to-runtime-to-pixel reconciliation open |
| R19 | Tutorial skull and every lootable room item/container support correct interaction, inventory and persistence | Source references exist; complete interaction sweep pending |
| R20 | Working pool table, balls and cue interactions with authoritative physics and persistence | Ordinary interaction/physics acceptance pending |
| R21 | Room lighting, window surfaces, fog, hue and atmosphere agree in matched views | Source CELL/NIF inputs bind partly; attenuation/shadows/fog output unverified |
| R22 | HDR/image-space effects, fade, blur, double vision and depth of field bind to their actual source lifetimes/parameters | Cinematic/fade owner binds; several effect channels explicitly unbound |
| R23 | Clock, fan and all animated/effect objects remain stable/correct across a continuous frame sequence | Fan now visibly moves on instance-owned clock; reported clock flicker unresolved |
| R24 | All HUD, prompts, dialogue/menu surfaces and fonts use their owned definitions with correct timing | Several default Godot menus still present; full UI sweep open |
| R25 | Ordinary doors, exterior/interior streaming, actors and return travel retain world state | Current route/recovery incomplete; one-pair restrictions remain |
| R26 | Easy Pete encounter: source actor pose, dialogue choices, voices and resulting state | Direct ordinary encounter pending |
| R27 | Sunny/Cheyenne meet, dialogue, packages, follow/travel and setpieces execute from owned data | Earlier owners exist; direct route binding open |
| R28 | Sunny's weapon/target/creature tutorial: aiming, ammo, hits, damage, AI, quest updates and rewards | Direct combat/creature owners not fully connected |
| R29 | Remaining Sunny tutorial branches, ingredients, crafting, dialogue and completion | Entire source-defined quest traversal pending |
| R30 | Save, exit, cold Continue during each major phase; quest/actor/item/UI-relevant state restores correctly | Native save does not yet persist the complete recovered graph |
| R31 | Continuous matched retail/OpenNV run with aligned input/events/state/audio/UI/final frames and visible telemetry loss | SBS harness exists; full aligned run and evidence lanes incomplete |
| R32 | Flat and physical OpenXR share this gameplay/save path and pass their interaction/presentation checks | Physical headset recovery acceptance not run |
| R33 | Every installed DLC initializes its source quests, messages, radios, item grants, form/leveled-list changes and affected world/vendor state, with correct timing and persistence | Added explicitly at the user's request on 2026-09-04. Four pack scripts execute; expansion startup scripts still reach unbound expression, form-list, faction or world-state operations. No DLC completion claim |

## Current verified changes, not closed requirements

- Private `observe-seat-17-a`: Doc is visibly on the seat after removing the
  vertical approach delta from occupied-root placement. Native observation also
  shows an identity accumulation root and the marker-owned occupied height.
- Private `observe-chair-14-4` / `observe-chair-14-6`: the fan changes orientation
  through its own live controller. The instance fix passed the repository gate.
- Naming pauses the world; its prior continuing camera loop was visibly wrong.
- Paired run25 verifies publication of the white IMAD fade while the first
  startup message pauses simulation. Paired run29 verifies the original message
  panel's dark fill, source glyph baseline and centered button. Indexed traits,
  atlas members and installed background opacity are shared reader bindings.
  Message order, casing, glow and exact timing remain open.
- Paired run30 checks each of the four pack confirmations on both actual menus
  before advancing and reaches naming and character creation. Initial pack
  order and speech timing differ. The recording reports transport overflow and
  has no audio lane; it cannot establish continuous synchronized parity.
- Direct TRI reading passes synthetic contracts and all 49 owned TRI files.
  Run30 binds 49 expression targets on nine Doc surfaces; recorded frames show
  the mouth changing during owned speech. A parser pass and this component
  observation do not close the complete actor-performance requirement.
- Source PACK idle collections and KF visibility/morph data now decode. Package
  idle scheduling and complete face-channel application are still unbound;
  these decoder changes do not restore cigarette performance by themselves.
- Source grant rollback, once-only execution, Float64 quest values and cold
  restoration pass synthetic tests. The installed-data script audit restores
  grants/messages without duplicates. Full opening Continue remains open.
- Paired run40 verifies the source hair layer/tint correction and two switchable
  render traces; OFF detaches source observers. Trace coverage still lacks exact
  GPU pixel contributors, native event/frame correspondence and complete audio.
- These corrections do not close acceptance rows while their remaining
  conditions are outstanding.

## Source recovery notes

The original `OpeningCigaretteSmokePresentation` is a custom four-sphere effect
with fixed puff sizes/lifetimes; it must not be restored as owned particle data.
The cigarette itself is a genuine IDLE → ANIO → NIF attachment. Keep the two
facts separate when reporting recovery. The retained Reflectron renderer depends
on converted resources and fitted framing; recover its general capabilities via
direct owned NIF/XML/font input, without restoring those launch dependencies.

Private recordings, captures and observations remain outside the repository.
See [runtime-recovery.md](runtime-recovery.md) for retained first-party owners and
[current-work.md](current-work.md) for the latest tested build and next work.
