# Current work

## Active objective

Make Fallout: New Vegas, Fallout 3, TTW and the selected mod stack playable from
owned files in flat/OpenXR. JAM, JIP LN, JohnnyGuitar and their dependencies remain
in scope. Prioritize substantial working code, main publication and usable
builds. Existing saves do not constrain implementation. Use practical smoke
checks after code batches and reuse the existing retail observation/input tools.

## Current implementation

The launcher accepts the installed game's folder, selects the matching game,
remembers the selection and presents launch failures. Selected mods enter after
actual file/dependency resolution; catalog readiness labels do not disable Play.
The retail native input recorder and C# replay are integrated. Menu feedback
finishes during pause without blocking a save.

The Pip-Boy batch initializes the original Stats list extents, binds note/quest
bodies, supports indexed XML text expressions and scrolling, lists acquired
perks and quest items, and exposes CND/RAD/EFF and General summaries. F1/F2/F3
open/switch pages, retaining each page's tab. Local Map renders resident geometry.
World Map selects discovered destinations and enters the shared cell-transfer
owner through the source-linked arrival point. Nearby map discovery persists
and awards the source XP setting. Notes
use their original text/image/sound/voice records. Radio selection can be stopped.

Teammate perk commands retain a separate player-owned list. Current teammates
consume it through existing perk effects; dismissal removes that inheritance.
Saves restore both lists independently.

The parity transport now spans a large frame across shared-memory slots instead
of rejecting an outdoor scene. Canonical bytes remain unchanged. Live comparison
can attach to the retained ring, and publication continues while menus pause the
world. The input client tolerates brief Windows state-publication sharing races.

## Actual run and next work

The real retail executable runs through the established native device bridge.
Steam's stale active-process registration caused the earlier startup failure;
restarting Steam repaired it. Ordinary retail menu input was recorded and replayed
through C#. Independent saves are not a matched gameplay checkpoint, and adapter
acknowledgements do not establish exact native consumption timing.

Ordinary source and packaged Release Continue loaded the outdoor save. Skills,
note text and the resident Local Map were rendered and inspected. Recorded retail
F1/F2/F3 inputs replayed through C# and left the actual menu on Data. The owned
Pip-Boy and companion checks and byte-transport check pass. The packaged World
Map control accepts the actual click and displays the source undiscovered-location
message. Map canvases sit behind the authored art and interactive controls.
Ordinary walking and jumping reach the next prepared exterior region. Quicksave
finishes after the outstanding finite ambient voices complete.

The same outdoor run found a malformed/unsupported model aborting the whole
exterior prefetch. Invalid model data now reaches the ordinary per-reference
failure owner, which retains the missing reference and its error, while the
remaining cell preparation can continue.

A packaged cold load exposed a native crash in Mesh.CreateTrimeshShape while
constructing packed NIF collision. The builder now supplies validated source
triangles directly to the concave physics shape, preserving vertex transforms,
winding, backface collision and face-material order without a temporary render
mesh. A fresh packaged Continue loads all 49 resident cells and remains alive.

Walking into Goodsprings discovery range exposed the shared XP owner's refusal
of Skilled's XP entry. The existing integration branch's numeric perk dispatcher
now serves RewardXP and discovery here, with live player conditions and upward
rounding. A fresh packaged Continue discovers Goodsprings, awards 9 XP with
Skilled, and the ordinary Pip-Boy Travel click reaches its source-linked arrival
in another cell. Loading closes, floor contact settles and movement resumes.
Multiple simultaneous XP entries and full level-up remain separately unbound.

The reached post-travel save refusal came from old dust-devil PCM loops being
cancelled when the previous cell root retired. Committed cell/reference unloads
now explicitly end attached loops and retain that terminal state for saving.
Finite audio tails keep their independent native host; unexpected destruction
still reports cancellation. The destination NPC controller also adopts a matching
retained package failure only through its current registered binding; the old
controller cannot leave the new one unable to save. Post-transfer save/Continue
both succeed in the packaged runtime: a fresh process restores the destination
cell, discovered marker and 9 XP, with no pending actor procedure captures.

The reached exterior windows now bind their complete source shader controller
chains, including fire period followed by refraction strength, and their managed
specular, emissive and opacity channels. Dynamic alpha selects the material
opacity pass without requiring a separate alpha property. Direct controllers
update only their owned field, preserving other managed channels on the same
material. Inactive refraction properties cannot select a different shader.
Ordinary Continue runs the source window scripts and reaches the daytime Right
endpoint on both observed references without a window-script failure. The eight
daytime material float fields match the live retail bytes on both references.
The owned Left sequence reaches the night opacity endpoint; its transition and
final pixels are not a matched retail comparison. Active refraction distortion
still retains its existing unmatched-kernel declaration.

Fast-travel elapsed time, nearby-enemy policy, follower transfer, complete General
statistics and all inventory actions remain incomplete. Outdoor play still
exposes compiled-script, AI procedure, SpeedTree and LOD material omissions.
Complete FNV/FO3/TTW campaigns, every selected native DLL/mod, and headset
playability are not established. Do not equate these menu fixes with completion.

The separate codex/full-runtime-owner-integration checkout owns the larger
compiled gameplay/native startup candidate and is actively modified. Reuse its
working implementations without publishing its unfinished native startup as
game support. The FNV Test shortcut selects the packaged Release. The next
gameplay owners are the reached outdoor script/creature-package failures,
SpeedTree and LOD omissions, and complete fast-travel consequences.
