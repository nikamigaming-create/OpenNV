# Patrol, menus and save recovery

The session menu shares authoritative state between flat and OpenXR. Escape,
the left controller Menu button, or right-stick click opens pause. The same
buttons go back through the save browser and confirmations. VR keeps tracking
and the controller pointer active while gameplay is paused.

Manual saves use separate GUID files beside the configured Continue file in
`.slots-v1`. Metadata comes from each saved character, cell, health and date.
Selecting a slot validates the source-bound save before promotion; a different
previous Continue file is retained as another slot. Bad slots are reported
without hiding the valid ones. Source workers finish before an in-process
reload retires the world, plugin stack and detached prototypes. A diagnostic
harness reload resumes after acknowledged inputs rather than replaying them.

Zero health now enters a reload menu. There is no Resume or Create Save action
in that state, and the world-state writer rejects post-death saves. This repairs
the contradictory playable-looking state in which Aid reported a dead player.
It does not add retail death animations, camera behavior or automatic reload.
Stimpaks still use source effects and the shared inventory/vitals transaction;
dead-player use cannot consume an item or silently revive the character.

Patrol reads PACK type 13, its linked or explicit starting reference, PKPT,
XLKR chains, XPRD waits, IDLM selections and arrival declarations. Repeatable
routes start at the nearest point, loop when circular and reverse at linear
ends. Nonrepeatable routes finish at the authored end or after returning around
a loop. Unsupported flags, marker interactions and arrival scripts remain
explicit failures. See the [package declaration](https://github.com/TES5Edit/fopdoc/blob/master/FalloutNV/Records/PACK.md),
[Patrol behavior](https://geckwiki.com/index.php/Patrol_Package) and
[movement condition](https://geckwiki.com/index.php/IsMoving).

The native actor follows source NAVM with its own capsule and source locomotion
animation. An elevated editor marker is projected onto nearby source NAVM once
per destination; projection farther than six actor radii is rejected. Arrival
requires supported feet and three-dimensional proximity. Authored weapon-drawn
state selects the actual inventory weapon and source pose. Combat suspends
patrol. Save v18 carries route identity, marker index, direction and remaining
wait along with the existing package motion. Restoration rejects changed routes.

Synthetic tests cover route order, waits, reverse/wrap/completion, cold state,
source drift, save-slot promotion, backup retention and malformed entries.
The selected owned audit covers the two elevated Primm riflemen's ten-point
routes and idle resources. Their marker idles bind the actual equipped weapon
and animation objects; alternative weapon-model channels retain explicit absent
targets. A native component check applies all twelve marker clips to both
riflemen with their source inventory weapon, with no unbound channels.
Native flat observation reaches both first markers,
waits and proceeds; one rifleman also reaches the second marker. Full-loop and
all-package acceptance remain open. A source-enabled actor without a linked
patrol start still reports that unsupported procedure explicitly.

Ordinary checks cover flat pause/manual save and Stimpak health 64.94 to 104.54;
Elliott Tate controller input covers death recovery, selected load, pause using
both buttons, manual save, main-menu return and Stimpak health 161.19 to 200.
Both final eyes of the death menu were inspected.
The exported flat executable also rejects movement, save and Escape after death,
then loads a healthy selected Primm slot in process and restores movement.
The local physical launcher
selects Oculus OpenXR per process; physical tracking, controls and comfort still
require the user's headset test. These checks are not whole-game or retail parity.
