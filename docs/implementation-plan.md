# Implementation plan

Updated 2026-10-17 after the user's rejected physical FNV VR playtest of package g.
Priority remains vanilla FNV, standalone FO3, mods, then TTW. Root works alone and
owns code, builds, live input and Git.

## Acceptance reset

Package g is rejected as a playable VR candidate. Its earlier component and
simulator passes remain bounded evidence only. Rendering the world, hands or a
menu does not establish functioning people, combat, progression or persistence.
The requested 90-second private edit is selected development footage, not an
acceptance video. No runtime repair is implemented by this planning change.

The private 46:25 recording was surveyed across its full timeline at 20-second
intervals, with denser samples around selected interactions and visible defects.
This is a sampled review, not uninterrupted normal-speed or every-frame review.
Timecodes below refer to that original recording, not the highlight edit. Session
logs provide independent implementation failures; they are not synchronized to
video frames, and repeated error lines are not distinct affected actors.

A fresh selected owned construction/idle/cold audit passes for references
169b7a and 0e745f. It does not exercise their attack or hit-reaction/save path;
those failures remain open despite that passing component check.

## Evidence and repair inventory

| Priority / item | Evidence and boundary | Runtime owner and required repair | Acceptance before closing |
| --- | --- | --- | --- |
| P0: damaged combat state blocks saving | Session reports hit-reaction NIF user version 2 = 27 unsupported and an actual failed manual save. | NIF layout reader, actor hit reaction, player package capture and campaign snapshot: establish corpus-backed version layout and retained reaction continuation. Do not discard the affected actor or reaction to save. | Hit the source creature, save during and after reaction, cold-load and compare actor pose, health, combat and quest state. Original user save stays untouched. |
| P0: creature attacks fail to bind | About 16:08-16:27 a deathclaw approaches and overlaps the view. Logs reject deathclaw h2hattackleft_a animation-group binding on multiple references. Exact impact/damage timing remains unproven. | FalloutActorAnimationResources, NativeActorCombatAnimation and RuntimeNativeActorCombat.Attack: source variant/group selection, animation clocks, attack windows, reach and contact. | Ordinary terrain encounter visibly winds up, hits at the source event, applies damage and recovers; moving out of range genuinely avoids the hit. Test another creature family and cold continuation. |
| P0: armed and embedded attacks | User rejects combat. Logs also reject embedded weapons with multiple ProjectileNode emitters and report unsupported impact materials. | RuntimeNativeActorCombat.Weapons, attack-node selection, ranged combat and shot effects: bind authored emitter selection, ammo/reload/projectile events and impact material. | Source-armed NPC detects, aims, fires, reloads and damages through real collision; blocked shots stay blocked. Exercise multi-emitter creature separately. |
| P0: NPCs do not perform their behavior | User report; NPC response area around 20:56-21:22. Logs require travel/procedure and dialogue-location owners, linked patrol starts, patrol flags and AI condition ownership. Sunny walking around 34:44 is only a bounded positive example. | RuntimeNativeNpc.PackageSelection, PendingPackageSelection, DialoguePackage, shared package schedule/target/procedure owners: resolve full winning conditions and targets before execution, including interruption and resumption. | Observe multiple ordinary NPCs execute eligible travel, patrol, dialogue and idle/schedule changes; provoke, disengage and resume. No named-actor exceptions. |
| P0: combat relationships/assistance | Logs report conflicting enemy/friendly faction disposition priority. User reports poor response. | FalloutReferenceWorld relationships and RuntimeNativeActorCombat acquisition/engagement/assistance: implement source disposition arbitration and assistance eligibility. | Mixed-faction positive and negative fixtures plus actual combat show the correct actors retaliate, assist, remain neutral or flee. No universal-hostility fallback. |
| P0: first-person rig is visibly malformed | About 32:10-32:48 the rifle pose produces large deformed/penetrating hand and forearm geometry. Other weapon transitions need dense review. | RuntimeNativePlayerActor.OpenXR/HandContacts, NativeXrArm, NativeXrHandContact and NativeXrWeaponSupport: inspect bind spaces, bone weights, grip targets and pose publication. | Both final eyes retain coherent arms and grip contact during independent head/hand motion, support-hand release, rifle/pistol switching, aim, fire and reload. Physical comfort and alignment need a real headset retest. |
| P0: reliable launch/render/session lifecycle | Earlier D3D12 run was white; visible local launcher plus Vulkan Continue displayed the world. Logs contain shader compilation, view-count and 2D-versus-2DArray errors near session end. Vulkan recovery is not proof D3D12 is fixed. | Launcher process visibility, renderer resource lifetimes, multiview shaders and OpenXR session transitions. Reproduce the errors before assigning a common cause. | Repeated launch, Continue, New Game, menu return, tracking/focus loss and quit show actual pixels without white/black hangs, bad view counts or stale resources; test each advertised graphics path. |
| P1: creature procedures and cross-cell routes | Logs reject creature procedures 0/9/12/13 and additional travel inputs; marker travel lacks nearby authored floor. Prior saloon creature portal and Sunny return failures remain open. | Shared creature package binding, CellNavigationGraph, portal/capsule planning, streaming residency and deferred continuation. | Actor reaches source target over real slopes, narrow doors and cell boundaries, survives unload/reload and resumes without teleport, proxy or fabricated target. |
| P1: dialogue, quest scripts and activation | Around 29:20-30:22 and 43:20-44:10 conversations/UI are exercised. Logs contain AddReputation without owner, unbound conversation conditions, Invalid script else and generic Sequence contains no matching element failures on activation/GameMode/triggers. | Shared script interpreter/control flow, semantic command/condition owners, reference events and conversation results. Replace opaque lookup errors with exact failing source operation context. | Reach the affected conversation/trigger normally; observe real reputation, quest, inventory and package consequences once, then save/cold-load. Synthetic control-flow and absent-target fixtures must fail meaningfully. |
| P1: survival and actor values | Logs reject LeftMobilityCondition base/formula and an ability effect. | Actor-value formulas and source effect application/persistence. | Damage, cripple/heal and ability changes produce the correct authoritative values and locomotion/combat consequences before and after save. |
| P1: wrist UI, radio and input release | About 08:00-08:09 visible wrist pages change. Logs reject tuning without station broadcast scheduler. This does not establish every page or action. | NativeOwnedPipBoy, shared menu transactions, native radio/audio scheduling and NativeXrRig input ownership. | Stats/items/quests/map/radio execute genuine actions; tuning plays the selected station on its source schedule. Close, tracking loss and menu changes release held trigger/stick state. |
| P1: body/creature/model omissions | Logs reject absent creature resources, a protectron part with no presented surfaces, beard texture index -1, NIF skin/head binding restrictions and nonfinite UVs. | Winning resource selection, general NIF/model/skin/material readers and actor construction. Inspect actual source bytes; do not hide missing actors. | Each affected winning source is either correctly presented and animated or has an explicit source-integrity failure. Genuine unsupported layouts get corpus-backed tests, not guessed coercion. |
| P1: content identity | Official ESM selection is logged, but a missing creature path contains MMM_ghoulvariant_v1. This alone does not establish why the path won or prove the installation pristine. | RuntimeLiveContentSource, loose/BSA precedence and plugin record provenance: trace winning record and every resource layer, privately. | Record exact source winners; distinguish altered owned inputs, missing files and engine defects. Preserve installation and saves; test verified vanilla separately from intended mod support. |
| P1: collision and physical objects | Logs reject shaped phantoms and model constraints. | Source overlap/contact, Havok joint/constraint and Godot physics owners. | Triggers fire on genuine enter/leave; objects retain constraints through contact and save. Phantom data never becomes fake solid floor. |
| P2: vegetation, lighting, LOD and effects | Logs repeatedly reject present SpeedTree geometry, missing cactus/agave resources, light flags, shaders, world material for LOD, morph inputs, BSValueNode behavior, impacts and sever effects. Scenery in the edit is incomplete. | Resource readers and rendering/LOD/material/light/effect owners; fix resolution failures before decoding failures. | Traverse the same cells across lighting/LOD transitions and inspect both eyes, including distant draws, vegetation, shadows and impacts. Retail pixel parity requires matched retail evidence. |
| P2: audio and shutdown | Radio scheduler is unbound; prior NPC fixtures had playback shutdown warnings; this session ends with an OpenXR RID leak. Source recording contains audio, but this review does not certify mix/sync. | Audio voice/scheduler ownership, session retirement and XR resource disposal. | Correct event-bound voices/music/radio, complete finite voice termination and repeated clean shutdown with no dangling runtime resources. |
| P2: frame pacing and loading | Startup includes black/white intervals around 03:20-06:20. Desktop recording cadence is not headset performance. | Source loading, actor/animation ticks, renderer and telemetry owners. | Measure settled headset CPU/GPU times and missed frames with recording/bots/builds off; inspect hitches separately during streaming. Set a measured refresh-rate budget before claiming performance. |

## Work order and reviewable blocks

1. Preserve the failing save/profile privately and bind evidence to source commit,
   content winners, graphics driver and session. Inventory all observed failure
   classes; keep unobserved campaign coverage explicitly unknown.
2. First implementation block: reproduce the hit-reaction-to-save failure on the
   affected owned creature. Repair the general NIF/reaction continuation owner,
   then prove ordinary hit -> manual save -> cold Continue. This prevents combat
   investigation from leaving the player unable to preserve progress.
3. Next blocks: deathclaw attack variants and event timing; embedded/ranged attack
   ownership; faction assistance. Require real damage and negative collision/range
   cases. A single attacking gecko or Viper does not close combat.
4. Repair NPC and creature package execution, schedule/conditions and cross-cell
   route ownership. Include interruption by combat/dialogue and authoritative
   resumption. Background actors must continue through the shared world owner.
5. Repair the malformed tracked rig and render/session lifecycle before another
   physical VR candidate. Audit both eyes through the complete motion sequence.
6. Close script/dialogue/reputation/actor-value, radio, content construction,
   physics and rendering defects from the inventory, ordered by actual route
   blockers. Retest persistence after every new state owner.
7. Run a fresh ordinary-input New Game -> Doc exit -> Goodsprings tutorial/wells
   -> Primm/ED-E route, with real combat, dialogue, doors, loot, crafting where
   required, fast travel, save and cold continuation. Survey the encountered
   population, not selected successful actors. Expand beyond this route before
   making broader game/campaign claims. FO3, mods and TTW remain later work.

Each block starts on a fresh codex/ branch from current origin/main, retains
unrelated work, adds meaningful synthetic and owned-data proof, runs the required
runtime gate, and publishes a checked PR merged into main. Confirm local main
matches origin/main and no task PR remains before the next block. No asset,
save, private capture, address or decompilation output enters the repository.

## Release acceptance

The next handoff needs all of: ordinary gameplay outcomes, full source-state
continuation, final-eye temporal review and a successful physical user retest.
Keep an explicit pass/fail/unproven entry for population, combat, quest changes,
input release, rendering, audio, loading and saves. Log every unsupported owner
and every missing entity; do not turn an exception into nominal success.

Before pushing a runtime or claim change, run the selected owned-data audit and:

~~~powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.8-dev7_mono_win64\Godot_v4.8-dev7_mono_win64_console.exe'
git diff --check
~~~

Capture only a specific visual check, then stop recording and remove its new
temporary frames after inspection, including failure paths. Preserve the user's
original recording and requested highlight. Do not retry earlier cleanup that
was blocked by automatic approval review. A passing build or selected highlight
cannot override a rejected playtest.
