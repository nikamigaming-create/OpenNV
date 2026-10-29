# Current work

## Active owner: creature and furniture continuation integration

The coordinator plans/reviews; user-authorized GPT-6.1 Sol High implementers share
one checkout and feature branch. The central executor alone owns builds, game
input and Git. Follow [implementation-plan.md](implementation-plan.md).

Acceptance requires retail systems/presentation through Goodsprings to the Strip,
sustained native 3840x2160 at 60 FPS indoors/outdoors, and separate physical XR
acceptance including 144 Hz. Historical save compatibility is expendable;
current-build saving remains a gameplay feature. Recording and trace are off.

Merged PR #326 at `874c56d8b53e6a08d9123b4502060b39611defb7` passed its local
mandatory gate, selected owned NPC checkpoint audit and hosted checks. Local main
equals origin/main. Current branch `codex/creature-idle-completion` contains the
next unpublished patch; its fresh final gate and selected owned checkpoint audit pass. Hosted checks
await publication. Eighteen unrelated
generated classic import EOL changes remain preserved and excluded.

## Current verified result

ExportRelease MVID `d6367556-4a09-427b-ba2c-9dd38a633259` built with zero Debug
warnings/errors. Ordinary Continue restored tutorial 20, then ordinary rifle
practice, Sunny's route and hostile combat reached quest 10a214 stage 50. Both
first-well geckos died through source projectile/health events. Corpse activation
and Take All added NVGeckoMeat 1; player HP remained100. F5 command141 generation 1
completed with error/cleanup null, cell 0e1ab6, written 06:31:32.972 UTC.

The canonical profile at `local/integrated-baseline-20261028/gameplay` now holds
that successful tutorial 50 fight/loot checkpoint. Do not replay opening or
character creation, and do not return to tutorial 20 without an actual need.
The game closed normally and disposed source owners. No new creature-retirement,
exception-logger, missing marker-reservation or furniture-mask error was observed.
No game or build is running before the publication checks begin.

## Patch and evidence boundary

- Finite creature script idles now pass authored End into actual ease-out,
  retain completed KF state only while the return fade owns it, and restore or
  retire through the shared clock/pose owner. Actual saloon Continue completed
  Cheyenne idle 0c1991 at 2.6666667 seconds, resumed package 152afa and allowed
  Sunny's formerly failing stage 10 to complete all eight steps. Shutdown capture
  no longer reported its former creature/logger exception. The compact existing
  continuation regression passes the reference-script probe and full gate.
- World.Restore now adopts validated idle-marker claims alongside furniture
  seats. Actual Continue published both formerly missing NPCs 104f03/104f0a with
  no AI/animation error; source raking resumed and retired normally. No named
  reset, save edit or new selection was used. The existing cold-owner/conflict
  regression passes the full gate.
- Actual NPC 1629a8 selected Furniture 107721 and blocked an earlier post-fight
  save because FURN 153553 enables ordinal bits 0/1 while its owned lawn-chair NIF
  has one physical marker 0 of type 14. ReadSeats now iterates physical positions,
  then filters their ordinal permissions. Source bytes, marker type and GMST
  offsets remain intact. The exact owned pair and synthetic edge cases ground
  this decoder correction. Final replay had no blocker and saved, but NPC 1629a8
  did not retain an active chair continuation at that save; physical chair entry
  and exact retail surplus-bit behavior remain unverified.

Earlier integrated candidate c7888fb2 exercised real VATS selection/cancel with
AP 80 unchanged, then two queued 35-AP torso attacks on hostile Gecko 10a1fd. Both
rolled misses and completed actual rifle shots/impacts; loaded 4 -> 2, reserve 20,
AP decreased/regenerated, queue completed without error. Hit/damage, cinematic
and matched VATS behavior remain unverified. Never manipulate rolls or targets.

## Remaining owners and next action

The fresh mandatory gate and selected owned checkpoint audit pass. Publish
this coherent patch through a checked PR. Continue from tutorial 50 afterward.
No new feature implementation before that publication boundary.

Active SayTo saving has no serialized speech-channel continuation and refuses
capture; waiting for a line to finish is not that capability. Background gecko
16518c uses Find/Food package 1651b1 procedure 0, currently unbound; it is separate
from tutorial foes and the fixed furniture decoder. Repair/Mod and broader
magazine use need ordinary coverage. Caravan runtime/UI/consequences compile;
a complete match remains untested. Doc's bed approach saves, but bed/chair
entry-loop-exit gameplay remains unverified.

Native 4K60 travel/combat is unmet; observed streaming commits exceeded budget.
Full retail pixels, FaceGen, audio, crime/factions, merchant/crafting coverage,
other creature procedures and physical XR hands/render/session acceptance remain
open. No campaign, parity or performance completion is claimed. Keep detailed
design in [runtime-repair-design.md](runtime-repair-design.md).
