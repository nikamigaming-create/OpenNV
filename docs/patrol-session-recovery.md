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
reload or ordinary Quit retires the world, plugin stack and detached prototypes.
Title indexing is also drained, and repeated transition requests cannot start
another retirement. The window close request uses the same native owner. A diagnostic
harness reload resumes after acknowledged inputs rather than replaying them.

The first settled Fallout 3 state now saves without a pre-existing New Vegas
snapshot. The same writer rejects active movies, furniture, speech, character
creation, trade/crafting menus, unsettled player transfers and death, retaining
the unsupported continuation instead of discarding it.

With an absolute private `--live-harness` directory, code can submit these JSON
commands using the existing numbered `.command`/receipt transport:

```json
{"op":"checkpoint.save","id":"0123456789abcdef0123456789abcdef"}
{"op":"checkpoint.load","id":"0123456789abcdef0123456789abcdef","pauseAfterLoad":true}
```

Use a new GUID for each saved state; duplicate identities reject before invoking
the writer or changing Continue. Loading works from the indexed title screen
or an active session and validates the complete source-bound
snapshot before promotion. A delivered load receipt acknowledges the request;
it does not confirm a finished scene. Wait for a fresh `live-state.json` whose
`checkpointRestored.id` matches the selected slot and whose player/CELL are
ready. The new scene clears the previous `checkpoint` request. When requested,
the ordinary pause menu opens before world gameplay advances, and normal
Escape/Resume or controller input releases it. Recording remains off.

These commands select actually reached saved states and are labelled diagnostic
preparation. Subsequent movement, activation and menu actions still use ordinary
flat/OpenXR adapters. They do not jump quest stages or establish traversal.
An owned ordinary birth run saves after the accepted character choice and reloads
through the native scene path. A paused resave retains the character, quest locals
and progression, inventory, controls, transform, globals, tracked reference
snapshots and player camera-package clock exactly. MenuMode script clocks continue
normally while paused. The owned C# cold fixture separately retains all 641 saved
quest-script owners, clocks and failure states before execution. Duplicate and
missing checkpoint requests preserve Continue; ordinary Resume/Pause advances
then holds the restored camera. Complete actor cold clocks, active interaction continuations,
source save eligibility and physical VR checkpoint acceptance remain open.

Save v25 additionally retains shared NPC-base face geometry with winning
NPC/model/RACE and CTL hashes; v24 and earlier supported saves still load.
The ordinary post-face TTW birth state reaches CG00 stage 80 and saves after
trait confirmation. Code loading and a paused resave preserve face overrides,
quest/reference state and the player camera-package clock. Cold presentation
also reports an unbound authored ragdoll accumulation-root rotation, so completed
checkpoint metadata alone does not make this new slot fully reusable. Dad/Dr. Li
dialogue selection remains failed in the reached state. No later campaign stage
is represented by this checkpoint.

The September 30 exported flat check continues a copied Primm checkpoint,
returns to the main menu, continues again and quits with code 0. Fresh-title
Quit also releases its unplaced prewarmed initial cell; native window close
exits with code 0 while title readers retire. Detached prototypes
previously survived ordinary Quit into native scene cleanup, where the same
checkpoint reported heap corruption. Cell destruction now retires compositor
GPU resources on the rendering thread; retained managed effects cannot keep a
pipeline, shader or sampler alive until engine shutdown. A native synthetic
fixture renders four replacement cells while retaining their old effects and
checks repeated retirement and retirement before the first draw. These checks
do not establish broad session stability or physical headset acceptance.
One repeated exported reload also crashed in native triangle-mesh construction
before gameplay resumed; a subsequent complete audit passed. That intermittent
construction failure remains open independently of shutdown retirement.
The selected owned runs still report two ObjectDB instances at exit; this
resource-owner gap remains visible.

Run `scripts/Test-NativeSessionRetirement.ps1` with an owned `-DataRoot` and
`-Checkpoint` after the Release export. It uses ordinary menu/key input and a
native window close request, keeps recording off, checks exit codes and GPU RID
leaks, and removes its copied profiles in a guarded cleanup path. The rendering
fixture is `runtime/tools/NativeSessionRetirementAudit/NativeSessionRetirementAudit.tscn`;
it requires Forward+ and a native graphics driver.

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
