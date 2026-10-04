# Actor collision and package head tracking

Native NPC and creature movement retains its intended collision filters through
reference registration, Disable/Enable and fade changes. A capsule attached after
registration updates the shared retained filter owner. A disabled reference keeps
zero live filters until its owner enables it again.

Bounded turning normalizes its quaternion and preserves the captured world scale.
Float roundoff in a published basis is not a source resize. Unchanged scales retain
the same capsule and route; actual appearance/scale changes still rebuild the
movement envelope. Local route segments retain the source arrival region when
that region is reachable within the segment bound, instead of demanding an
obstructed intermediate NAVM portal centre.

PACK Head-Tracking Off, bit 0x100000, is retained by the shared script package
reader and admitted by marker/editor Travel. The active package suppresses the
physical head target while retaining the script Look reference and its clocks.
Release uses the existing authored-pose easing. Body conversation facing remains
independent. Other unsupported package flags still fail visibly.

Synthetic declarations verify enabled/disabled flags and existing negative cases.
The selected owned marker package exercises late filter registration, disable/
enable, unchanged capsule identity during turning, retained Look with suppressed
physical publication, real NAVM/KF motion, cold route continuation and one-time
arrival. The static room fixture uses enabled source placements within twenty
metres and their declared collision. Controller-owned objects are reported as
excluded; this fixture does not own their animation or establish whole-cell
collision, ordinary campaign progress or matched retail pixels. Recording stays
off. The isolated floor regression previously reproduced capsule recreation during
turning; the static owned-room regression previously stopped at an obstructed
intermediate portal. Both require separate ordinary-play verification.
