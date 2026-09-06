# Opening-to-Sunny recovery checklist

This is the acceptance queue for the active implementation. It contains
**36 requirements: 0 accepted, 36 open**. R01-R33 preserve the original scope;
R34-R36 add the user's September 6 instructions. Existing implementation is
replaceable under implementation-plan.md. An open row means its whole
acceptance condition has not passed. This is not a whole-game denominator.

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

Follow implementation-plan.md: general runtime capabilities, an aggressive
separate development lab, complete ordinary gameplay chains and proof across
unrelated source instances/cells. At matched checkpoints retain discrepancies,
fix the general owner and replay that section. Skip the cinematic. Existing
failures remain open when implementation order changes; broad component
corrections do not close a cell or a complete requirement.

| ID | Requirement and acceptance condition | Current evidence / missing owner |
| --- | --- | --- |
| R01 | Complete winning source graph for this route, with every reachable unsupported record/resource/runtime entity reported | Readers exist; complete route reconciliation remains open |
| R02 | New → owned intro with correct sound, timing, interruption and natural completion → opening | New/movie/Escape works; natural completion and matched timing unverified |
| R03 | Wake-up, sit-up, seated and exit camera transforms/projection/pause agree at matched times | Source KF hierarchy/FOV bind; complete matched timeline and exit open |
| R04 | Doc's source body, outfit, face, hair, materials and attachments remain correct in motion | Direct assembly works; face/material correspondence unverified |
| R05 | Correct chair occupancy, sitting layers, entry and exit, with source root transfer | Selected NPC entry/occupation audit passes; player furniture, complete motion and matched timing remain open |
| R06 | Source package idles, cigarette/object attachment, gesture layers and interruption/resumption | Seated base, collection playback, ANIO and source replay cooldown bind; condition admission, full timing and matched motion remain open |
| R07 | Source effects, including any smoking effects, start/stop/attach correctly | Old smoke uses custom sphere puffs; this is not source-driven smoke support |
| R08 | Complete opening voice, response ordering, subtitles and interaction/skip rules | Four initial INFOs and post-name mirror handoff exercised; full dialogue open |
| R09 | Voice-timed lips, expressions, listener reactions, eye/head aiming and blinking | Owned TRI morphs now follow LIP speech time on actual meshes; private paired run30 shows mouth motion. Idle/speech face blending, aiming, reactions and complete matched timing remain open |
| R10 | Original name menu fonts/art/layout, caret, selection, input, acceptance and world pause | Owned XML/font/art and name pause bind; complete visual/input acceptance open |
| R11 | Original Reflectron shell, screen materials, lights, buttons and camera from owned data | Live entry now uses direct NIF/XML/FNT presentation; all twenty pages render in the native diagnostic. Ordinary matched replay and material/effect acceptance remain open |
| R12 | All supported source race/sex/face/hair/eyes controls change authoritative player identity | Owned executable declarations bind 43 face controls to CTL; source presets, part and RGB editing owners added. Randomization, complete input and saved-state acceptance remain open |
| R13 | Reflectron face preview updates correctly for each edit, with source geometry, pose and material | Source player and complete MTIdle animate in the direct preview; paired run40 fixes teal hair and the user accepted current hair/age presentation. Bounds, drag/zoom, complete edits and exact material agreement remain open |
| R14 | Walk to/use the Vigor Tester; original device/UI and SPECIAL allocation/results | Original allocation and ordinary acceptance execute; framing, timing and complete matched acceptance remain open |
| R15 | Complete psychology dialogue, choices, response effects and source sequencing | Player furniture/trigger state and original conversation-choice execution remain unbound |
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
| R34 | Every Goodsprings interior and exterior cell in a source-derived scope manifest supports its objects, actors, effects, interactions, connected travel and quest progression, with cold persistence and matched acceptance | Added September 6 at the user's request. Complete scope inventory and cell-by-cell runtime/ordinary/parity evidence are absent; do not assume a name search defines the whole area |
| R35 | Shared data-driven runtime behavior replaces scene/actor/quest-specific success paths and replacement interfaces; unrelated source instances/cells and winning overrides demonstrate reuse | Added September 6 at the user's request. Existing code and tests are replaceable greenfield; source values inside bespoke orchestration do not prove generality |
| R36 | A separate development lab loads/tears down cells and actors, stresses animation/physics/interactions/scripts, tests cold state, exposes source-to-runtime-to-pixel failures and reproduces them without repetitive manual play | Added September 6 at the user's request. Some component tools exist; the integrated lab and complete batch scope are not verified. Lab manipulation never substitutes for ordinary-game acceptance |

## Evidence and updates

Current component evidence and limitations are in current-work.md,
scene-defects.md and the linked technical contracts. Previous reports are
starting points to revalidate, not proof that the current implementation works.
Keep build/source identities and machine-readable failure artifacts for each
accepted result. Private observations and owned captures stay outside the
repository. The implementation remains replaceable under implementation-plan.md.
