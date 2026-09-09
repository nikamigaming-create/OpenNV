# Current work

## User priority

The immediate deliverable is a flat New Vegas ragdoll/dismemberment showcase
using geckos and raiders. The user explicitly corrected the creature selection
to gecko, not coyote, and set VR aside for this test. Continue in this task
without subagents. Work and publication stay on main under AGENTS.md.

The wider objectives remain complete shared combat/looting, universal cell
repairs, continuous travel through Novac to the Strip, and physical VR with
source hands, body, weapons and live Pip-Boy. The separate classic Fallout work
is paused and preserved; see status.md and classic-fallout-plan.md. No campaign,
cell, headset or retail parity completion is claimed.

## Current executable result

The requested private video is
`local/recordings/flat-ragdoll-20260908/OpenNV-gecko-and-raider-ragdolls.mp4`:
74 seconds, 1280x720 flat gameplay, H.264 and stereo game-process AAC audio.
It shows a source gecko dying after three ordinary pistol inputs and an armored
Jackal raider after four. Their original physics bodies fall against actual
world collision. A labeled development command then detaches both legs, both
arms and the head through the shared source limb owner. The commands and
source-based travel cuts are explicitly identified in the video. Automatic
weapon dismemberment and combat AI are not demonstrated or complete.

The first gecko check exposed skin weights stretching across separated limbs.
Crossing bindings now follow an anchor on the correct side of the authored cut,
with vertex position preserved at detachment. Source wound caps and connecting
sections follow dismember partition state. Cut poses, order, injury, rigid-body
state and source hash survive saves. The ordinary raider cold load retains all
five separated parts without the stretched connections. The physics envelope
is still an approximation of the source Havok solver.

NPC health now distinguishes manual and autocalculated source stats. Equipped
armor condition and constant relevant effects contribute defense; decoded
sources and modifiers are reused. Missing inferred facial normal-map companions
retain their explicit NIF normal instead of rejecting the actor. This restores
the selected source raider mouth. See actor-damage.md for the bounded contracts.

The recording has repeated frames: gecko 1,039 unique samples in 1,197 output
frames; raider 699 in 1,198. Capture/streaming cadence is not accepted. Post-shot
lighting also differs from cold restoration even though muzzle telemetry has
expired; its cause remains unresolved. Do not call this complete gore, smooth
combat, matched lighting or a finished gameplay video.

## Verification and private continuation

The required full gate passes in `tmp/development-lab/ragdoll-full-gate.log`:
Release/Debug, formatting/analyzers, contract probes, launcher tests and native
Godot project checks. The selected owned audit `ragdoll-skin-owned.log` covers
all five severable parts of both actors, source collision bodies, cut-side skin
bindings, floor retention and fresh-instance restoration. Synthetic damage
contracts cover fractional NPC stats, zero-health corpses, armor layouts,
severed injury persistence, invalid cuts and once-only death inventory.

The actual ordinary saves are under `tmp/development-lab`:

- `native-gecko-ragdoll-20260908-run2/save.json`: killed and fully separated gecko.
- `native-raider-ragdoll-20260908-run1/save.json`: killed and fully separated raider.
- `native-raider-ragdoll-20260908-cold/save.json`: fresh-process continuation of that corpse.
- `native-cell-shop-flat-20260908-run3/save.json`: restored Cliff, blocked conversation below.

All ordinary game processes are stopped. Frame recording and trace are off.
Requested video and selected stills are private; temporary frames and rejected
capture intermediates are removed after inspection/export. Revalidate process
identity before resuming any saved run.

## Next shared owners

Weapon-driven limb selection, critical/sneak rules, exploded limbs, source gore
replacement/debris and sever-triggered blood/decal effects remain unbound.
Combat AI, damage reactions, hit/death scripts, XP, armor wear and encounter-level
selection remain open. Continue from the visible failure through the source
owner rather than adding location-specific actors or success paths.

The cell review is paused behind this requested showcase. Optional NPC ENAM
now retains authored race eye materials. Source expression commas are accepted,
and parser-versioned quest restoration admits newly parsed legacy comma-bearing
programs without resetting existing clocks/progress. Reference parse failures
retry on cold restoration; reached execution errors remain visible. Cliff now
renders and can be reached through ordinary flat input, but conversation fails
at INFO 08d09f condition 67 (GetInCell). Its prefix-EDID semantics and subject/run-on
context still need the shared condition owner. Do not claim dialogue completion.

Other open cell failures include package travel/calendar ownership, persistent
leveled actor choices, TREE/SpeedTree, property-free LOD materials, exterior
residency/streaming and incomplete populations. Follow cell-parity-review.md.
The earlier 68-second single-left-eye simulator video is retained at
`local/recordings/vr-showcase-20260908/OpenNV-New-Vegas-VR-showcase.mp4`.
It uses diagnostic travel cuts and does not establish walking/fast travel to the
Strip or physical headset acceptance.

## Preserved VR and classic work

XR draws the world outfit and hands with source head geometry excluded only
from eye cameras, retaining shadows and bone contacts. Anatomical eye anchoring,
source-length body constraints and the corrected palmar grip frame are present.
Ordinary SIM input, live wrist focus and single-eye recording have bounded checks.
Room-scale stepping, slope-aware feet, outfit fitting, physical comfort,
complete hand/weapon collision response and campaign controls remain open.
The reactive bot uses observed resident references, NAVM and ordinary expiring
input for approach/follow/activation; campaign decisions and combat tactics are
not implemented. Preserve the actual stage-200 opening save at
`tmp/development-lab/native-reactive-bot-20260908-run1/save.json`.

FO1/FO2 retain their shared owned map/hex runtime, item/equipment state, original
HUDs, source-bound 3D presentation and independent saves. Their campaigns,
world/script coverage, likeness and remaining scenery are incomplete. The
classic delivery plan and product status retain the broader implementation scope.
