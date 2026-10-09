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

Ordinary OpenNV Continue loaded the outdoor save. Skills, note text and the
resident Local Map were rendered and inspected. Recorded retail F1/F2/F3 inputs
replayed through C# and left the actual menu on Data. The owned Pip-Boy and
companion checks and byte-transport check pass. Successful ordinary fast-travel
arrival remains to be exercised; it is not established by the menu checks.

Fast-travel elapsed time, nearby-enemy policy, follower transfer, complete General
statistics and all inventory actions remain incomplete. Outdoor play still
exposes compiled-script, AI procedure, SpeedTree and LOD material omissions.
Complete FNV/FO3/TTW campaigns, every selected native DLL/mod, and headset
playability are not established. Do not equate these menu fixes with completion.

The separate codex/full-runtime-owner-integration checkout owns the larger
compiled gameplay/native startup candidate and is actively modified. Reuse its
working implementations without publishing its unfinished native startup as
game support. Publish the current runnable batch through a checked PR and advance
the FNV Test package.
