# Current work

## Authority and next outcome

The immediate work is a visible complete VR body with only its head excluded
from the player's eyes, continuous reactive gameplay control and a usable single-eye
recording with game audio. Preserve physical hands, held-weapon contact,
one/two-handed handling and measured runtime performance. Continue in this task without
subagents. D:/code/MGS5VR supplied behavioral reference for source-bound support
grips; its own wall-contact and physical-melee work remains unfinished. Ordinary
prop interaction, controls, friendly-animal touch and deliberate melee require
separate responses on the shared contact/gameplay owners, not speed-only damage.
Preserve the requested trip to the Strip through the real world/door graph,
fixing roads, resident collision, neighboring cells and continuous LOD. Do not
replace travel with position writes or a capture scene. The broader matched
cell review and NV/FO3/TTW objectives remain open. The current AGENTS contract
requires checked publication on main without a new branch or PR.
[The cell review workflow](cell-parity-review.md)
owns source failures encountered on the route.
The separate classic task below remains paused with its work preserved.

## Current New Vegas showcase and next failures

The private requested showcase is
`local/recordings/vr-showcase-20260908/OpenNV-New-Vegas-VR-showcase.mp4`.
It contains 68 seconds of one actual submitted left eye with game-process audio:
Novac exterior, the Strip near Gomorrah, Dino Bite gift shop with live wrist
focus, then ordinary SIM movement and pistol fire at a source coyote. The user
explicitly requested diagnostic teleports for this video. Separate copied saves
changed only the active cell and source marker/reciprocal-door arrival pose;
these cuts do not establish normal fast travel or traversal to the Strip.

The source coyote died through ordinary trigger input and retained its death
inventory and ragdoll state in `native-showcase-combat-20260908-run1/save.json`
under `tmp/development-lab`. Its rendered body disappears after death in this
capture. Visible corpse, blood and loot presentation are not accepted by this
run. Novac also reports unsupported source package calendar/schedule evaluation;
the gift shop NPC fails `default-eye-selection-required` and its reference script
contains an unbound token. Strip source gate meshes
obstruct this arrival view, and exterior LOD/resource failures remain visible.
These are next shared runtime owners to diagnose, not accepted cells. Full NPC
schedules, source actor residency, corpse presentation and camera/body quality
remain unfinished. Recording is off; the four ordinary simulator processes are
stopped and their separate saves are retained. The wider cell-by-cell review and
normal fast travel remain pending.
## Full body and reactive gameplay

XR now draws the complete world outfit and hands. Source head parts use a camera
exclusion layer while retaining their original shadow policy and all 18 bone
query volumes. The first-person draw retains the authored weapon and live cuff;
its body geometry has zero eye layers. No geometry or contacts are deleted.
The actual source FaceGen eyes replace Camera1st as the full body's eye anchor.
The flat weapon camera was behind the anatomical eyes and exposed the neck hole.
Grounded pelvis, source-length spine/leg constraints and independent body heading
now supply posed shoulders to both skeletons. Excess head/foot reach is explicit
telemetry. Automatic room-scale stepping, slope-adaptive feet, torso balance,
physical limb response and headset acceptance remain open.

The free-hand grip frame now uses the palmar normal instead of the dorsal normal.
The reversed sign had rolled the hands 180 degrees while preserving wrist position.
Both source skeletons pass independent grip/index-trigger/thumb motion, finger
release and source closing-direction checks. Submitted-eye SIM inspection shows
the corrected left glove through wrist turning and live cuff focus/release.
The private 14-second single-left-eye recording with game audio is
`local/recordings/vr-hand-20260908/OpenNV-VR-hand-fix.mp4` (245 unique frames in
419 output frames). The ordinary save is
`tmp/development-lab/native-left-hand-sim-20260908-run2/save.json`.
Recording is off and the simulator is stopped. Headset fit and capture cadence remain
unaccepted; this check also reports about 5.9 cm of unresolved full-body head reach
at its calibrated standing pose. Wrist/contact errors remain zero in that pose.

The selected owned body audit passes anatomical eye placement, shared shoulder
frames, crouching foot targets, invariant spine lengths, visibility-independent
contacts and an unreachable-body negative fixture. Ordinary SIM full-body Run3
continued a real exterior save and moved about 3.6 metres through the capsule.
Both submitted-eye views show connected outfit sleeves, hands, held pistol and
the live cuff. Dark exterior lighting, body fit across other outfits, long-motion
quality and physical comfort are not accepted. Its save is
`tmp/development-lab/native-full-body-sim-20260908-run3/save.json`.

ReactiveReferenceBot and ReactiveSteering are reusable C# input policies without
Godot dependencies. The native adapter observes resident source identities,
collision, controls and actual interaction state each frame. Source NAVM supplies
routes; flat keys or explicitly selected SIM controller input execute them.
SIM input requires expiring device leases. A moving Doc was reached through
ordinary flat input, including a 17-waypoint/12-second approach. The fresh opening
reached stage 200 with Guns/Repair/Speech, no traits and Hardcore off; its actual
save is `tmp/development-lab/native-reactive-bot-20260908-run1/save.json`.
Vigor telemetry now includes active source controls and animation phase.

The bot remains bounded reference approach/follow/activation, not a campaign
decision-maker. A SIM route hit Doc's actual Gurney01 collider and stopped with
the blocker reported. Capsule-aware local clearance and dynamic avoidance are
the next navigation owner; never bypass that failure with position writes.
The operating-table Broken SMG script also retains its compiled-script divergence.

The recorder accepts a bounded duration or stop signal and captures only game
process audio. The private simulator can publish one actual submitted eye into
that recorder; unsupported compositor overlays fail explicitly. Repeated frames
and original timestamps remain visible through stalls. The current deliverable is
`local/recordings/vr-body-20260908/OpenNV-VR-body-check.mp4`: 17 seconds, one
1280x1280 submitted left eye and stereo game audio. It shows head/body movement,
crouching, live cuff enlargement/release and a road shot. It contains 332 unique
source frames in 512 output frames; capture cadence still needs improvement.
This is a body/weapon check, not the requested complete gameplay take.
Full-body SIM Run4 retained the head exclusion in both eyes and fired twice at
original road collision without hitting self volumes (2 -> 0 loaded). Sampled
late-motion head/foot errors stayed below 0.1 mm with no contact error. That sample
does not cover the entire motion or establish skin quality. Its ordinary save is
`tmp/development-lab/native-full-body-sim-20260908-run4/save.json`.
Recording is off and ordinary development processes are stopped.

The user requires complete Fallout 1 and Fallout 2 hex campaigns in the shared
launcher, retaining FNV/FO3 and eventual shared FPS/VR state. The visual goal is
a cinematic diorama preserving original art, silhouettes, palette, source
identities and exact MAP/PRO/FRM placement. Hex state owns navigation, blocking,
exits and eventual combat; appearance never changes those rules. Use the same
main publication workflow, without subagents. Preserve the New Vegas work below. All campaign and
parity acceptance remains open.

## Current classic runtime

Both games share the owned map/elevation catalog, character picker, source hex
navigation, original HUD and persistent player saves. FO2 starts at ARTEMPLE
and honors loose-over-DAT2 resources. All six premades decode from original
GCD/BIO/FRM data. Custom choices retain approved Reflectron appearance, SPECIAL,
tags, traits and source identity. FO1 Envision is an illustrated portrait; FO2
keeps its natural 3D appearance. Full likeness acceptance remains open.

Source floor columns, object hex columns, MAP offsets and FRM projection agree.
Uniform fitting preserves prop proportions; transparent source floor/roof art
contributes no surface. Cave walls are joined textured shells. Metal walls use
source baselines, joined capped volumes, original surface paint, corners and
doorway geometry, including dv/mmb families. MBSTRG12's mmb1037 contour remains
unresolved. The Vault 13 portal/backing airlock and cinematic handoff are still
missing from the native scene.

Twenty first-party models provide twenty-four source furniture bindings. Their
original paint is projected onto suitable visible mesh faces, with transient
depth rejection and physical wood/cloth detail. Source and donor assets are read
live; no transformed retail cache is a persistent launch input. Sectional joins,
remaining walls, doors, furniture facing and unseen materials still need work.

Native humanoid recipes now bind male/female vault, tribal, leather, metal and
combat-armor families and all six premade face candidates to owned NIF/DDS/KF
assemblies. Custom appearance is preserved. Tribal male hair is darker, with a
modest broader-shoulder/narrower-waist adjustment across the entire dressed rig.
Source equipment controls admission: the temple guard's actual spear PID 7 has
its owned model, Weapon socket, aim and grip clips. Held meshes no longer run a
second loose-item physics simulation. Constant-opaque donor materials can cast
real shadows. Held minigun poses, other weapon/action states, individual faces, accurate
tribal clothing and the Vault 13 jumpsuit number remain unfinished.

Rats, ants, dogs, brahmin, scorpions, deathclaws and unarmed super-mutants have
explicit donor candidates. Rats and the temple guard have native close footage;
other candidates are not accepted by inventory count. Source pose/state remains
authoritative. Single-frame standing sprites now permit an independent 3D idle
clock; this is visual breathing, not AI or combat. Actor close-up framing uses
creature height. FPS/shoulder locomotion remains unrestored. The industrial
mamtntka direction/frame failure is still reported and retains its last image.

F4 or View switches the world, inventory rows, equipment slots, equipped paper
doll, loot portraits, quantity dialogs and HUD weapon together, including while
inventory pauses gameplay. The inventory/quantity mode buttons change this same
world state. F3 compares two cameras; F2 hides presentation controls. Missing
analogs show their item name and an explicit unavailable label in 3D. Original
HUD frames and source floor imagery remain in both representations.
Slow camera passes, mark/restore view, orbit and 1080p controls are live.

The tree7/tree8/tree9 source families now use owned dead-tree meshes (88 original
ARTEMPLE placements). EDG ground art retains its source color and topology under
meter-scaled owned soil, fine grain, normals and dust. A transient joined source
color atlas removes isolated tile-edge minification seams. Other tile families,
including temple paving, still need detail. Fog and contact shadows are added;
this is not acceptance of lighting or cinematic parity.

## Inventory and UI

Inventory previews render live owned meshes in isolated, transient viewports.
The paper doll uses the world character's saved identity, source armor family,
held item and visual idle. Framing measures the posed skin and held geometry;
UI scaling controls render resolution. Static views render on demand, live
controllers retain independent instances, and views are released on close.
Ground inventory donors retain physical scale and center on their actual hex;
pickup-FRM rectangle fitting no longer displaces dropped weapons. Narg's spear
was dropped, cold-restored, picked up and re-equipped through native input.

Source-scoped item bindings now cover common chems, ammunition, keys, books,
holodisks, explosives, loose armor, larger weapons, hides, plants and the GECK.
The native component check admits all 98 selected FO1 item identities and 168
FO2 identities, using 72/97 distinct donor models. These are selected bindings,
not full asset coverage or likeness acceptance. Inactive source transform
channels retain their authored pose. Direct boolean visibility uses its source
clock, including particle visibility; completed event-free initialization can
retire before appearance reuse. Animated/particle instances retain independent
C# bindings. Embedded loose-armor skins publish their own source bones and
partition palettes; repeated instances have independent poses. Vertices used
only by degenerate strip stitches no longer reject otherwise complete skins.
Empty blast/range markers retain their fields; actual damage-stage descendants
remain unbound. Dormant refraction parameters do not enable refraction flags.
Many item analogs remain unmapped, and held animation coverage is smaller than
inventory/ground-model coverage. The selected item check exits without engine
errors or leaked collision shapes after transient resource cleanup.

The shared item owner supports partial transfers, arbitrary deposits, Take All,
nested portable containers, drop/pickup, hand/armor slots, unload/reload,
source caliber/type checks and v4 cold saves. Native v1-v3 saves migrate; richer
older saves stay adjacent. See [inventory and equipment](classic-inventory.md).
Synthetic and owned fixtures pass for both games, including Bones containers,
magazines, nested bags, cycle rejection and transfer conservation. All 242 FO1 /
531 FO2 item PROs decode; this is format coverage, not complete item behavior.
FO2 PRO 255 now resolves instead of being mistaken for an unallocated marker.

Ordinary FO1 input looted Bones, equipped its knife, selected a six-round drop
through MOVEMULT and cold-restored 18 rounds carried / 6 on hex 17489. Clicking
the restored 3D ammo box recovered the six rounds. A subsequent Bones deposit
and Take All round trip retained exactly 24 rounds. Max is saved in V13ENT on
hex 17489 with 24 rounds carried and the source knife equipped in the right hand.
Bones now has a 3D container and visible-mesh
picking. The clothed skeleton lies flat with blue cloth; source-pose/likeness
acceptance remains open. Both updated original inventory screens were inspected.
The native INT reader now feeds original module variables and the campaign
header's startup/map-enter procedures into the existing C# VM. FO2's source
entry supplies its spear, hex 17488/rotation 5 and daylight. All three premades
pass the actual initialization owner; a synthetic script variation also changes
the actual granted item/quantity. Cold saves verify the initialization identity
before restoring item changes, including a dropped spear without duplicate grants.
Narg's existing save is profiles/fallout2/chosen-v1.json, migrated to v4 with his
position 18496 preserved and the source spear equipped through native INVBOX.
FO1 starting grants, object/world script events, item use, full corpse
interactions, combat and quests remain required. The running clock and modded
calendar overrides remain unbound; this is campaign-header startup only.

Original LOOT/INVBOX/IFACE art is read separately from each campaign. Text now
uses measured bitmap-font widths, word wrapping, explicit alignment and clipped
panel bounds. Loot names, item titles, weight, armor class and HUD status are
centered within their actual panels; descriptions/logs keep consistent left
margins. Character headings/counters/status are centered. Native FO1 loot and
inventory views and both campaign character/inventory layouts were inspected
after the change. The final character counter sits inside the controls panel.

## Saved work and verification

The private catalog local/classic-asset-inventory/latest/index.html covers
227 stored maps / 338 elevations, art, prototypes, exact placements, nested
inventories and FNV/FO3 donor namespaces. Counts do not prove playable maps or
complete graphics. Thirty-five initial source-resolution failures remain listed.
Concept boards/prompts are in local/fo1-world-concepts; first-party geometry
sources are in local/classic-scenery-workshop and local/classic-authoring/build_classic_furniture.py.
See classic-actor-recovery.md and classic-scenery-authoring.md for ownership.

The launcher at desktop/release/win-unpacked/open-nevada-launcher.exe preserves
both campaigns, independent saves and both donor-library roots. Both use the
native main scene with Forward+. Debug/Release builds, the focused character/
text contracts, original hex/movement checks, and FO1 source-container transfer,
conservation and cold restore passed. Current Debug/Release builds and native
Godot project loading pass. Complete-source gate and publication status are in
the verification section below; the offline helpers now remain private. The requested
private 103-second, 1080p/30 native
video is local/classic-runtime-video/fallout-classic-analogs-and-inventory.mp4;
its adjacent JSON identifies chapters and inspection versus gameplay. It is
silent and includes both original HUDs, actor comparisons, actual FO1 walking
and item transfers, FO2 inventory, and labeled furniture/scorpion inspection.
That video predates the new item ledger, quantity UI and 3D Bones changes;
current selected native images are local/classic-runtime-video/item-systems-*.
The new 72-second, 1080p/30 recording is
local/classic-runtime-video/fallout-1-2-inventory-3d-toggle.mp4. It shows both
native campaigns switching world/HUD, item slots, paper dolls and quantity UI,
FO1 Bones in LOOT, and Narg unequipping/re-equipping his spear. It is silent;
the adjacent JSON records scope and chapters. Selected current images use
inventory-*.png. No campaign completion is implied by this UI recording.
Classic capture processes are stopped and recording is off. Temporary clips
and debug dumps are removed after export; selected UI diagnostics remain local.

## Door interactions and saved source state

The shared [door owner and archive recovery](classic-interactions.md) now connects
pointer interaction, source door initialization, FRM clocks, dynamic passage,
sprite/3D frames and v4 saves. Owned loading covered 72 FO1 maps / 392 doors and
155 FO2 maps / 1,171 doors without a door-owner map-load failure; 4 FO1 and 195
FO2 door initializers still reach unbound effects. Source fixtures verified
open/close, mid-animation/cold persistence and source-change rejection. The
panels are source-derived extrusions and still require finished 3D art.

Door use and Shift-click examination resolve the owned script message lists
and literal INT strings into the native HUD. The Vault 13 door's original
control-terminal response and FO2 temple door descriptions execute. Source
message IDs retain stable handles across saved script variables. Unsupported
events publish neither partial state nor messages. The HUD keeps wrapped text
history and supports wheel scrolling over its message panel. Skill/stat checks
still prevent trapped-door procedures from completing; floating text and dialogue
remain unbound.

DAT1 compressed blocks now reset their dictionary and use the correct stored
length mask. All 20,456 compressed FO1 members decode, and script-list indices
are restored. Verified v1-v3 save recovery compares the old decoder's exact
source hash and unchanged item records before rebasing. Max's native save was
recovered and saved as v4 with its original backup, retaining tile 17489,
43 HP, 34 steps, knife and 24 rounds. No owned files were changed.
Narg's existing save was also saved and cold-loaded as v4, retaining tile 18496,
44 HP, 13 steps and its equipped source spear. The current Debug and Release
builds, native Godot project loading, INT contracts, both item-system fixtures,
FO2 startup and focused door checks pass. Offline authoring/gallery helpers are
preserved byte-for-byte under local/classic-authoring, outside product inputs.
Complete-source validation is recorded in the verification section below.
No campaign completion is claimed. The selected doors-source-fo1.png image is a diagnostic of source
panel extrusion; its art needs refinement.

## Next required implementation

The classic task is paused at the user's requested stopping point. The native
runtime remains at the verified door/message/inventory checkpoint above; no
skill-check gameplay changes were introduced during the follow-up inspection.
Both v4 saves are intact, and no classic test process is running. The preserved
classic source is included in the complete first-party publication checkpoint.

Resume with the source skill/stat-check instructions reached by door use and
examination (0x80AC and 0x80AE), plus their result predicates. Reuse
ClassicSkillOwner, ClassicRetailAttackRollOwner and the existing FO2 skill,
critical-selection and random contracts. Connect them to authoritative player
stats, a shared campaign random stream/clock and atomic script effects. FO1
still needs its separate verified rules; do not apply the FO2 contract to it.
Private source inspection is checkpointed under the Ghidrust evidence workspace
at fallout-classic/script-checks. The current disassembler output has decode
gaps/register-width inconsistencies; settle those before treating the missing
instruction semantics as established. No roll behavior was inferred into code.

Neither classic campaign is complete. FO1 startup and general object-script initialization,
scripted skills/traps/timers/audio, stairs/ladders/elevators, complete equipment effects and item use, Pip-Boy,
combat/AP/range/targeting, dialogue, quests, world travel and persistent world changes
remain required. Older first-party owners survive before 7140879; connect them
through native source readers without restoring prepared retail-asset launch
caches. Source exit grids and dynamic door blocking have owners; full scripts,
actor movement/collision and combat do not. Keep improving the source-bound graphics alongside those gameplay
owners. Do not present map-browser inspection as campaign travel.
The free inspection camera can enter walls during an orbit; camera obstruction
handling remains required. A rest-skeleton-only creature yaw experiment failed
the live animated comparison and was reverted; per-family facing acceptance
still requires the animated source/donor join.

## Physical hands and runtime cost

NativeXrHandContact now sweeps the original hand Havok shapes and held weapon
shapes against resident world bodies. The visible first/world skeletons and
muzzle use the resulting wrist pose. A first-person model without pickup Havok
uses the winning world weapon's shapes in model space. Shapes and motion-query
objects are reused until equipment changes; missing shapes fail explicitly.
Translation sweeps, bounded rotational subdivision, sliding, tracking loss and
unloaded collision have native tests. A capped contact spring pushes actual
dynamic props using their existing mass/inertia and the contact point. It emits
no combat damage. Source actor hit areas, per-finger/forearm contact, room-scale
body constraints, physical melee and friendly-animal responses remain required.

Two-handed source aim poses supply the actual off-hand socket. Proximity dwell
engages support, the right palm remains primary, and source muzzle orientation
follows the two-hand solve. Tracking loss, wrist interaction, reload, excessive
reach and equipment changes release support; firing retains it. Anatomical reach
constrains targets before contact sweeps. A retained contact that later exceeds
body reach reports an error and disables firing from that wrist. Eight saved
weapons bind contact shapes, including two support-capable guns. These bindings
are not headset or all-weapon acceptance.

VR retains each equipped weapon's original holding transform and finger grip
through action clips. Flat reload hand motion cannot displace a gun inside the
tracked hand; source internal model channels and gameplay/sound timing continue.
The owned grenade-rifle reload previously displaced its grip by 4.5 cm. Selected
source firing/reload pose checks now cover both pistols, the caravan shotgun and
grenade rifle. This is grip continuity, not support for every shot/reload mechanic.

Tracked arm solving now retains the source elbow bend plane and drives the
authored forearm/upper-arm twist helpers. Wrist pronation no longer rolls the
entire elbow skin. The head-relative torso and clavicles share the tracked
shoulder frame. Source reach and pronation checks pass. The low crouching view
still exposes incomplete sleeve presentation near the shoulders: first-person
inverse binds agree, and torso-section culling truncates actual arms. Keep the
complete sleeve geometry; its partial-body appearance is not accepted.

Animation samples are value types. Spline outputs use stack storage, key searches
reuse static delegates, and each skeleton retains its layer-reduction storage.
The player reuses its layer array and avoids polling/copying unchanged equipment
every frame. Source keyed/compact/spline checks preserve their values with zero
allocations over 5,000 samples. The selected owned player-animation check fell
from 3,024–7,528 bytes per weapon update to 64 (unarmed: 7,240 to 24), after the
initial layer-reduction change. This is a bounded allocation result, not a whole
game frame-rate claim. Native contact, prop response, source animation and wrist
checks pass; the source addon-audio failure remains explicit.

The route checkpoint at CELL 0daea0, position
[-942.11597,132.13156,-241.57341], is source-route waypoint 118/778. Ordinary input
moved another 51 metres across adjoining cells. F5 is ignored by the paused
Pip-Boy, so the private route helper now saves before opening it and verifies the
file position. The latest flat run cold-loaded, walked another metre, opened and
closed the source Pip-Boy, and saved [-942.14966,131.95059,-242.53456] in
`tmp/development-lab/native-contact-flat-20260908-run2/save.json`.
The Strip has not been reached.

SIM `native-contact-sim-20260908-run2` cold-loaded the route checkpoint. Actual
controller rays changed the wrist inventory between shotgun and 9mm pistol.
Shouldered/turned shotgun support engaged with zero measured wrist gap; excessive
reach and the live Pip-Boy released it. The left hand stopped on sloped LAND with
its visible wrist on the physical target. Both-eye views were inspected, including
the remaining low-crouch sleeve defect. Tracking loss and reacquisition with the
trigger held emitted no shot; release/repress fired one source 9mm round, casing
and dirt impact. Its F5 save retains that route position, equipped 9mm and one
loaded round at `tmp/development-lab/native-contact-sim-20260908-run2/save.json`.
Sustained-motion and physical-headset acceptance remain open.

The final SIM `native-contact-sim-20260908-run4` used actual wrist input to equip
the grenade rifle and B to complete its source reload with one grenade loaded.
Support released during the clip and reacquired afterward; the tracked right
wrist stayed matched. Physical left-hand cartridge handling is still unbound.
The current F5 checkpoint is that run's `save.json`, at the same route position
with the 9mm equipped (1 loaded), 10mm (12 loaded) and grenade rifle (1 loaded).
The two retained private images identify the active shoulder and reload-handling
defects; other temporary SIM frames were removed.

Exterior models/textures now prepare off the render thread and cells stage ahead
of entry. LOD publishes arriving complete coverage while retaining coarse cover;
LAND quadrants reuse identical shader programs. Uploads still stall: the latest
route peaked at 181 ms on source reference 1767bf, with a 23 ms residency commit.
Terrain contact also stopped the earlier route; contact normals now identify the
actual LAND/body. These remain real traversal/performance work, not accepted LOD.
All contact-development flat and SIM processes are stopped; physical Meta XR
was not launched. Recording is off. Debug/Release builds, Release export,
native source contact/rig checks and the recorded flat/SIM actions provide bounded
verification. Complete-source gate and publication status are below.

## New Vegas headset feedback and simulator combat work

The physical headset test failed: upright hands/gun, near-head clouds, missing
actors, small wrist UI and incorrectly rotated world pieces. That process is
stopped; its ordinary player save remains in
`tmp/development-lab/native-quest-link-20260907-run1`. Do not launch physical XR
again yet. The user explicitly requires actual simulator shooting, death,
dismemberment, blood and looting before returning to the headset. Preserve the
unrelated classic work above. Full campaign, retail and physical acceptance
remain open.

The existing Pip-Boy now enlarges uniformly to 2.25x and twists only around the
anatomical forearm centerline while left grip is held. It restores scale and roll
on release. The previous reader-directed displacement and free-axis facing are
removed. A shortest-angle roll prevents a full spin after repeated wrist turns.
The original device, screen, buttons and skeleton remain the single owners.
The owned audit checks the fixed cuff center/axis, 144 motion steps, release,
unchanged hand bones and source button ray hits. Ordinary SIM input operated
ITEMS, DATA and STAT at different wrist poses with the world unpaused. Both-eye
views show the enlarged cuff centered around the arm and the original menu
readable; physical comfort and anatomical appearance still require acceptance.

Other bounded corrections remain: grip axes use tracked knuckle/palm frames;
weapons use separate controller aim and the source ProjectileNode. Sky vertices
are infinite per-eye directions. Clockwise source XYZ reference transforms agree
with 911/920 private tilted-reference samples; nine non-atomic samples remain
open. Inactive hair texture slots no longer reject actors. Source rigid headgear
and two affected NCR actor assemblies pass. Three invariant-template coyotes pass
source animation and cold continuation; random leveled template ownership is
still missing. These checks do not establish complete populations or scene parity.

## Current firing and effects

The user additionally requires hidden VR body parts to retain full shadows and
collision. The original third-person body now remains in both flat and XR. Eye
visibility uses per-surface shadow-only rendering, independently of its 18 source
Havok contact volumes. Player shot/activation rays exclude these self volumes.
The full world rig uses its own compatible source animations and tracked wrist
solvers; its anatomical source eye frame anchors the tracked head.
The live enlarged Pip-Boy casts its own shadow and the world outfit's duplicate
device is hidden. No additional gameplay actor or inventory is created.

`NativePlayerBodyAudit` passes all 18 actual query volumes through all four
visibility/shadow combinations, tracked wrist poses and head yaw/pitch/roll,
plus an intentionally disabled collision fixture. Hiding both eye geometry and
shadows masks drawing while retaining contacts. Ordinary flat body Run2 shows
the full shadow, switches first/third person with 18 contacts retained, and fires
at original road geometry (3 to 2 rounds, no self-hit). SIM body Run2 continued
that save, walked about 2.7 metres, used the original wrist STAT/Skills buttons
without pausing, and fired at LAND (2 to 1 round, no self-hit). Daylight Run3
reloaded the flat save and shows the body shadow in both eyes with head tilt and
roll. Its saved game hour is 16.143; the earlier darker view was after sunset,
not evidence for a rendering correction. These are hit/query volumes, not
completed player damage, hand-against-wall response or ragdoll dynamics.
SIM sampled 64 FPS / 16.35 ms median / 20.38 ms p95 with recording off and the
live harness active; physical/performance acceptance remains open.

`FalloutPlayerSkills` now supplies conditional SPECIAL and skill values to
script queries and the original Pip-Boy rows. Owned GMSTs supply base, primary,
luck and tag terms; source SPEL abilities, PERK traits and worn ENCH effects
supply constant modifiers. Ingestible DATA/ENIT economics now participate in
live inventory weight; hardcore ammunition weight is conditional. Weight reuse
is invalidated by inventory revision or hardcore mode. A missing framed x87
unit/zero initializer form is decoded rather than substituting a setting value.
Synthetic tests cover source tag overrides, conditional traits, equipment,
weight-mode changes, malformed records and unsupported timed effects. Owned
saved values at hour 15 are Guns 12, Medicine 27 and Melee 29; morning/evening
trait conditions change the same owner. Ordinary flat Skills scrolling retains
the right row values; SIM pointing displays them on the live device. Timed
effects, complete actor-value pools, progression and retail matching remain open.

Required gameplay continuation: retain the matched cell review and universal
fixes below. The user will announce when ready for a headset test; keep physical
XR on hold and continue implementation in this task. The required order remains
ordinary flat input, OpenXR simulator, then physical acceptance. The current
combat checkpoint and remaining owners are below.

The shared weapon owner publishes semi-automatic hitscan at the original attack
Hit event, consumes ammunition and saves magazine/recovery random state. Source
stereo fire audio, reload, holster, tracking-loss and wrist-focus interlocks have
bounded checks. One effect clock advances original NIF controllers and particles
across short emission windows and long frames, then preserves the particle tail.
Cylinder/sphere emitters, drag and replace blending have selected source contracts.
Original WEAP MOD2 casings use the posed ShellCasingNode, source shell settings,
independent Havok bodies and an in-process reusable prototype. PROJ/LIGH supplies
the muzzle light. Collision shape/triangle material selects original IPDS/IPCT
models and spatial sounds. Mixed NIF subparts resolve per face; LAND admits only
unambiguous LTEX material triangles. Blended-material selection stays unbound.
Transient casing meshes and particle draws now inherit exterior lighting/fog and
unregister on expiration, while separate UI viewports retain their own environment.

Actual SIM trigger shots outside Doc produced visible source muzzle flash and
brass ejection in both eyes. A night capture shows the original muzzle light
briefly illuminating hands, sleeves and road. Wood and concrete hits select their
source effects without a material-resolution error; concrete particles emit and
expire. Their exact visibility, decal appearance, sound mix and retail timing are
not accepted. The 9mm's source tracer chance is zero, so no tracer is invented.
ADDN audio/pooling, exact drag and smoke motion, impact decals, casing contact
sounds, muzzle shadows and flat-world muzzle lighting remain unbound.

The actual weapon Hit event now damages the rendered actor's source bone region.
Winning weapon skill settings, NV condition, limb multipliers and unconditional
ability defense feed the shared health owner. Source CREA health and BPTD pools,
exactly-once death inventory and injury snapshots have synthetic contracts.
Death replaces living animation with source skeleton Havok bodies and validated
joint endpoints. The Godot 6DOF angular envelopes remain an approximation of
Havok cone/friction/inertia/malleable behavior; this is not physics parity.

Ordinary flat Run1 walked approximately 195 metres through source NAVM and real
resident collision to coyote reference 154139, using jumps and a sidestep around
obstacles. Three Weathered 10mm shots consumed 12 to 9 rounds and changed its
health 30 -> 16.56 -> 3.12 -> -10.32. The source 21-body/20-joint corpse rolled
downhill and settled. Ordinary E opened Search Coyote; Take All transferred its
one meat. The saved corpse is empty and the player retains the meat. The private
checkpoint is `tmp/development-lab/native-combat-flat-20260908-run1/save.json`.
Cold startup exposed rejected sibling attachment during Godot Ready; death
construction now defers until the cell hierarchy is attached. The selected
coyote and both radscorpion audits reproduce that off-tree construction and
verify live body queries, persisted poses and inventory without engine errors.
Ordinary cold Run3 restores the visible/searchable corpse and empty inventory.
SIM Run1 then used right-grip pointing and trigger to search it; right-stick
scrolling, deposit, Take All, Exit and controller save operated the same menu.
The world remained live. A subsequent trigger shot hit source body 43, consumed
9 to 8 rounds, retained dead health and produced visible muzzle flash and brass
in both eyes. Its private checkpoint is
`tmp/development-lab/native-combat-sim-20260908-run1/save.json`.
That SIM is stopped. Run3 subsequently walked approximately 213 metres with real
SIM stick/sprint/jump controls and a sidestep around an obstructing tree. The
live wrist ITEMS page equipped the Weathered 10mm. Three trigger shots changed
the original coyote's health 30 -> 16.56 -> 3.12 -> -10.32 and its magazine
12 -> 9, with a 21-body/20-joint ragdoll, visible blood and brass in both eyes.
The player followed the body downhill, pointed at Search Coyote and used Take
All. The saved player retains one meat and one hide; the corpse is empty.
`tmp/development-lab/native-combat-sim-20260908-run3/save.json` is the latest
checkpoint; `before-combat-save.json` preserves the living target and equipped
pistol. Run3 is stopped. Limb destruction remains required.
Captured menu text was obscured by nearby rocks; the stereo modal surface now
disables world depth rejection and both-eye reinspection passes. Hidden generic menu
viewports stop rendering and resume when an actual menu opens.

Each coyote shot selected original ballistic blood IPCT/model and flesh impact
audio without an effect error. A retail calculation check corrected particle
growth/shrink: the base scale is the envelope endpoint, and overlapping ramps
take their minimum. Previously a zero base erased the blood spray and mist/splat
radii were much too small. Synthetic zero/nonzero endpoint, overlap, generation
and minimum-size cases plus owned emission/expiry checks pass. Ordinary flat and
SIM shots visibly emit the original blood spray. Blood timing and sound mix
remain unaccepted. First-shot timing isolated up to 1.51 seconds in muzzle
preparation; the ADDN catalog was reopening the entire plugin stack. Native
startup now binds that catalog from the already loaded winning records. Impact
construction measured about 13 ms per warm hit; one completed graph can now be
reused with reset clocks, modifier state and emission remainders. Active effects
remain independent and retain their complete particle/audio tails. Owned reset,
concurrency and bounded-retention checks pass. Exported flat Run6 and SIM Run4
cold-continued the preserved living-coyote checkpoint and each completed three
ordinary shots, source death and a save. First-shot preparation measured
58.93 ms flat and 73.08 ms SIM, compared with 803.25 ms and 1526.67 ms in the
earlier respective runs. Initial hit publication still costs 61.03/67.46 ms.
The subsequent living-target hit reused the completed impact: its total
publication cost was 6.24 ms flat and 7.61 ms SIM, with 0.82/1.55 ms in the
impact owner. Blood remains visible in flat and both SIM eyes, with stereo
brass. These are sampled owner timings, not an overall frame-rate guarantee.
Both processes are stopped with their saves intact; temporary captures were
deleted. Casing instantiation remains roughly one millisecond in these samples.
Living NPC health, full armor/resistance ordering, combat AI and
retaliation, hit/death script dispatch, XP, limb severing/explosion and damage
reactions remain required. Automatic, multi-projectile, melee/thrown and
ballistic/explosive attacks, skill/condition spread, critical hits and weapon wear
remain required. No manufactured target or state write replaced the ordinary run.

## Current cell review

The retained cell-review outcome is a matched retail/OpenNV review through the
source cell/door graph, collecting failures and repairing their common owners.
The SLSD reader no longer compares its 19 unused compiler bytes as local-variable
identity. Seven owned scripts now admit their repeated slot/name/flag declaration;
conflicting names, slots or flags still fail. All 44,517 winning CELL records
pass reference lifecycle loading, thirty unload/reload cycles and cold state
restoration, including 5,870 cells with references. This does not accept their
rendering, scripts' reached behavior or retail gameplay.

Lifecycle v2 includes stable CELL/worldspace/grid identities and plugin hashes.
The ordinary native detailed `state` command now includes the exact resident
reference scope, source compatibility identity, loaded assembly ID and capture
time. The new development-lab `cell-review` command checks that source binding,
groups declared failures, and resolves every affected reference's original
parent, base and model. Nonresident diagnostics are excluded and counted.
Synthetic source-skew, duplicate-scope, neighboring-parent and invalid-input
contracts pass. The report always leaves full parity unverified.

The starting Goodsprings exterior review covers 2,337 resident source references.
Before the material correction below it identified 13 failure groups:
335 missing runtime observations; 257 unavailable shrub SPT paths; 19 unsupported
controller-manager sequence chains; eight dynamic-alpha plant surfaces; actor,
damage-state, texture-set, light and material/morph failures. Counts overlap
between presence and failure lanes and cannot be summed as missing objects.
Retail may not render every source declaration; that requires observation.

The user authorized moving the retail player and making comparison saves.
The existing private native input bridge now loads and controls retail without
manual user setup. `OpenNV_Compare_20260908_Goodsprings_01` is a new retail save
outside Doc's, made after a short ordinary movement input. Its reload restores
CELL 0daebb and the same rotation; position settles by less than 0.12 mm.
All 132 pre-existing save files retained their hashes. Temporary autosave
settings were restored, held input is zero, and the private bridge now skips
GPU readback when neither a requested capture nor a live subscriber needs it.

Live observation of retail's admitted plugin array disproved the earlier
base-only assumption from plugins.txt: nine NAM sidecars activate the installed
DLC. Both games admit the same ten plugins in the same order. The shared loader
now uses activation, NAM sidecars, TES4 master flags and file timestamps instead
of appending every installed master. Archives follow the configured list, active
plugins and FNV's Update.bsa instead of admitting every BSA in the folder.
Inactive-file, case, encoding, timestamp-tie and FO3 isolation contracts pass.
The ordinary default retains the existing save compatibility identity; no
special launch profile or retail settings change was needed. Precise camera,
time/weather and inventory/quest branches still need matching before full parity.

The gas-station SCOL was rejected because its sign retained an environment mask
without selecting environment mapping. Dormant slot 5 metadata now stays dormant
instead of rejecting the whole model. Synthetic changed/missing inactive masks
preserve the draw; active environment inputs remain required. The owned model
builds eight surfaces / 8,911 vertices, and ordinary exterior movement showed
the building and sign. The same 2,337-reference starting scope now has 12 failure
groups, with the gas-station presence and material failures absent. Its complete
appearance is not accepted as retail parity.

Cold native Continue exposed a separate save bug: the player root stored yaw,
but its independent look pitch reset to zero. Save v13 now requires that pitch,
captures it through the shared campaign owner and restores the desktop camera.
Pre-v13 saves remain readable with their former level-view behavior. Missing
v13 pitch, nonfinite values and out-of-range angles reject before replacing a
save. Synthetic legacy/invalid-state checks and the owned campaign save probe
pass. Ordinary movement then crossed from CELL 0daebb into adjoining 0daebd
toward the gas station, followed by F5 and fresh-process Continue. The current
checkpoint is `tmp/development-lab/native-active-source-20260908-run3/save.json`.
It restores stage 200, position, pitch 0.07000002 radians and unchanged inventory.
Its source-bound report covers 1,995 resident references and 12 failure groups;
the changed reference count reflects the new cell scope, not fixes to that many
objects. The selected diagnostic view is
`tmp/development-lab/goodsprings-gas-station-restored.png`.

A real capture request exposed a separate input-transport loss: the queue
advanced before a temporarily locked command file could be read. Reads now
permit atomic replacement and keep failed requests pending, with their failure
and request number visible in telemetry. Synthetic file locks and the actual
paused native runtime verify that physics continues, no false receipt appears,
and the same command completes once after release. That earlier native process
is stopped; the current checkpoint and SIM are above. Retail was left paused
and untouched during the contact work. Revalidate identities before input.
Original retail saves and settings remain intact.
Recording and trace remain off; temporary readbacks are removed after inspection.

Current checks include `active-source-final-owned-contracts.log`,
`active-source-observed-check.json`, `gas-station-mask-owned-audit.log`,
`command-read-retry-contracts.log`, Debug/Release builds, the final export and
selected formatter checks. Runtime cold-load, command retry and cell reports
are beside the current checkpoint. Complete-source gate and publication status
are below. See [the workflow](cell-parity-review.md) for the next cell loop.

## Preserved simulator checkpoint and verification

The latest Release export is `tmp/development-runtime/windows/OpenNV.exe`.
The current daylight checkpoint is
`tmp/development-lab/native-xr-body-20260908-run3/save.json`, retaining two 9mm
rounds and the secondary 12-round magazine. It resumes the ordinary flat body
Run2 save, with simulator head/wrist and both-eye shadow checks. The later night
combat-query check remains at `native-xr-body-20260908-run2/save.json` with one
9mm round. Flat PID 7812 and SIM PIDs 6724/30820 were stopped after verifying
process identity and their saves. Current flat/retail work is tracked above;
these simulator processes remain stopped. Meta's physical runtime registry is
unchanged. Recording and render trace are off;
all temporary frames from these runs were deleted after inspection. Revalidate
processes before resuming.

Debug/Release builds and selected formatting pass. Logs in tmp/development-lab:
`player-body-build.log`, `player-body-export.log`, `player-body-audit.log`,
`player-body-reference-contracts.log`, `player-body-float-contracts.log`,
`cuff-impact-build.log`, `cuff-impact-export.log`,
`headset-feedback-xr-audit.log`, `shot-effects-audit.log`,
`land-impact-audit.log`, `land-material-contracts.log`,
`shot-nif-contracts.log` and `shot-source-contracts.log` cover the selected changes.
The native material audit hit both metal/wood windmill subparts; the LAND audit
verified 44 source-material rays. Late casing/particle environment binding,
viewport isolation and teardown pass. These are bounded contracts and runtime
checks; final retail/audio/physical acceptance remains separate.

The offline classic authoring/gallery helpers are preserved byte-for-byte in
`local/classic-authoring`. They are not product launch inputs. The required full
repository gate passes: Release/Debug builds, formatting/analyzers, contract
probes, launcher tests and native Godot loading/instance/reference/trace checks.
Selected owned shot-effect checks also pass, including completed-effect reset,
independent overlapping instances and bounded retention. The SDK selection is
shared by local builds and CI; PRs now run the same validation as main.
One selected owned audit reported 14 ObjectDB instances at shutdown. Four
subsequent verbose runs passed without that warning; its cause remains unresolved.
The published base includes PR #29 at d06bbe8. Current full-body and reactive-input
work passes the required full gate (`full-body-full-gate.log`), the selected owned
body audit, the source XR contact/weapon audit and tracked player/wrist UI audit.
The contact fixture includes source holding/reload checks across all eight saved
weapons plus unarmed assembly. Those checks do not establish every weapon's combat.
The palm-axis correction passes the full gate (`left-hand-full-gate.log`), the
extended owned body/finger audit, and the tracked player/wrist and contact/weapon
audits. Its playable export and local recording use that checked source.
Private retail files
and diagnostics remain outside publication inputs.
The simulator timing above includes the live harness and is not a physical
benchmark or streaming acceptance. Cold Continue and cell upload stalls remain.
Casing prototypes and completed impact graphs have bounded owner-managed reuse.

The original opening reaches stage 200 with bounded door exit/return, grounded
cell walking, loose loot, container transfers and cold saves. Victor's five
exercised topics retain voice identity and map/objective results. This does not
establish universal dialogue, scripts or quests. General item use/drop/repair,
crafting, limb/radiation state, radio/local maps/fast travel and complete quest
routing remain required. The moving XR map indicator needs refresh. TREE/SpeedTree,
property-free LOD materials, complete weather/shadows/water, seamless door views,
Novac/Strip traversal and full populations remain incomplete or unverified.
