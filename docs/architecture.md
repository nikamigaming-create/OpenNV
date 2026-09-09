# OpenNV architecture

OpenNV has one runtime architecture: C# readers consume a legally owned game
installation in place and publish authoritative state to Godot.

## Boundaries

- Retail files are read-only. OpenNV never edits the selected installation.
- No Bethesda asset, executable, save, or converted derivative is committed,
  packaged, uploaded, or distributed.
- Runtime launch accepts a live installation root and a campaign identity.
- Loose files override archives through a case-insensitive source namespace.
- Active ESM and ESP records are resolved in load order with master-aware
  FormIDs. BSA members are resolved in memory.
- NIF, DDS, KF, audio, string tables, records, DAT, MAP, PRO, and FRM data are
  interpreted by C# owners inside the runtime.
- Gameplay and save state are authoritative and shared by flat and OpenXR
  presentation adapters.
- Unknown binary layouts and unsupported behaviors fail closed.

## Main owners

- `runtime/src/Content`: installation detection, plugin/archive readers,
  strings, media, records, and live source precedence.
- `runtime/src/Formats`: NIF and engine-family binary interpretation.
- `runtime/src/Gameplay`: authoritative inventory, stats, crafting, settings,
  and save state.
- `runtime/src/World`: cells, actors, collision, movement, interactions, and
  streaming.
- `runtime/src/Campaigns`: source-backed campaign progression.
- `runtime/src/Presentation`: Godot rendering, UI, character creation, and XR
  adapters.
- `contract-tests`: C# synthetic contracts and explicitly selected owned-data
  audits.
- `desktop`: launcher registration and invocation.
- `tools/OpenNV.DevelopmentLab`: separate headless corpus, reference-lifecycle
  and event-replay operations that call these runtime owners directly.

Placed-reference script locals belong to `FalloutReferenceWorld`, including
references with no model and initially disabled objects. Resident cell membership
is separate from retained world state. Bindings resolve winning base/script
attachments and declared slots; instances share immutable declarations without
sharing mutable locals. Campaign reference snapshots carry script identity and
hashes; restoration rejects changed declarations. The ordinary native player ray
and source primitive contacts now dispatch through FalloutReferenceScripts.
Reference enable state and source enable-parent relationships share that world
lifetime. Native models may be built on demand without creating a second source
path. Per-instance texture changes remain transient with presentation lifetime.
FalloutConversation owns INFO selection, results and choices; native source menu
and voice adapters report input/completion. Actual player furniture and posed
actor query contacts now connect to reference events. Complete event/physics,
dialogue presentation and cold interaction restoration remain incomplete; see
reference-events.md and dialogue-and-furniture.md for the bounded contracts.

The player keeps one complete world skeleton in flat and OpenXR. Camera
visibility, shadow casting and source bone query volumes are independent;
first-person visibility never removes anatomical contacts. XR shows the complete
world outfit and excludes only head geometry from its eye cameras. Source FaceGen
eyes define the head anchor, independent of the authored flat weapon camera.
Grounded pelvis/spine/leg constraints preserve source lengths and report excess
reach. The posed shoulders also anchor first-person weapon/device attachments;
the duplicate first-person body draw is masked. The actual wrist device casts its enlarged shadow; its duplicate outfit
device stays hidden. Self rays exclude the player's capsule and bone volumes.
These contacts provide hit queries, not completed limb rigid-body response.

The first-person XR owner separately binds those source hand shapes and the
equipped weapon's Havok into persistent motion-query bodies. Collision-resolved
wrist targets publish to both existing skeletons before muzzle evaluation.
Resident world collision constrains translation/rotation; a bounded virtual
spring transfers impulses to real dynamic props at their contact points. Actor
hit areas still require a response/damage owner. Source two-hand aim poses define
the support socket; proximity dwell and release rules do not create another
weapon or inventory. Tracking loss and unavailable collision disable firing.
The free-hand grip frame uses the palmar normal: OpenXR +X points out of the
left palm and into the right palm; -Z runs from little to index finger
([OpenXR pose conventions](https://registry.khronos.org/OpenXR/specs/1.1-khr/pdf/xrspec.pdf#page=110)). The
source closing animations independently check that sign on both skeletons.
Wrist-distance agreement alone cannot detect an inverted hand. Grip, index
trigger and thumb touch retain independent source finger channels.
The tracked holding frame and source grip palette stay fixed during source flat
action clips; internal model animation and authoritative timing continue.
Anatomical reach limits targets before contact publication; a retained contact
that the body can no longer reach reports divergence and blocks that gun's shot.
Source elbow bend planes and authored twist helpers distribute wrist roll while
the prepared full-body torso supplies consistent shoulder anchors.
Animation layer reduction belongs to each skeleton, with reusable arrays and
value/stack-based sampling rather than per-frame channel-object graphs.

ReactiveReferenceBot and ReactiveSteering own an extractable, engine-independent
C# observation-to-input policy. RuntimeCoordinator supplies resident references,
source navigation and authoritative interaction observations; RuntimeLiveHarness
feeds ordinary flat inputs or a simulator adapter with bounded controller leases.
No bot path writes player transforms, inventory, quests or collision results.
Obstruction stops input and reports its actual collider; campaign decisions and
capsule-aware dynamic avoidance remain unbound.

Player SPECIAL and skill queries evaluate owned GMST formulas, tags, constant
abilities, conditional trait effects and equipped apparel effects against live
inventory/global state. They are also used by the Pip-Boy rows. Inventory weight
is reused until inventory revision or hardcore mode changes. Ingestible DATA
supplies weight and ENIT supplies value; ammunition weight applies in hardcore
mode. Timed effects, unsupported effect archetypes and conditions remain explicit
failures. Full damage, actor-value modifier pools and advancement are incomplete.

Quest stages, INFO results, reference events and quest GameMode share the same
statement interpreter. Compiled bindings are cached per owner/script. A stage
retains each QSDT entry's condition and reference scope; blocking presentation
suspends its statement iterator. Nested stages publish in source order and a
reached failure retains its executed prefix. Running quest admission, stopped
clocks, source inventory grants, dialogue history and active quest selection
remain C# state. Inventory expands LVLI into real item variants and retains its
random state in saves. Godot supplies playback, input and completion callbacks.
The original source program now advances the ordinary opening; a predicted
stage edge cannot replace its execution.

Destroyed references reject ordinary activation. MarkForDelete retains a
tombstone and commits deletion across residency teardown/reload. Neither a new
model nor an enable request can resurrect that state. Pip-Boy selection and
inventory invalidation have a shared owner. NativeOwnedPipBoy presents the source
menu on the owned arm-mounted NIF, with one camera/screen input projection;
equipment changes and quest/map state remain C# authority. Unspent character
creation state can be saved, while active interaction
continuations still fail visibly instead of being discarded.

RuntimeNativePlayerActor owns the player's equipped source skeleton, body, hand
variants, weapon model and KF palette. First/third-person views share inventory
and physics state. Animated equipment cannot also run dropped-object physics.
Source accumulation is separate from local bone poses; third-person collision
movement owns world translation. The first-person render target has an explicit
output color conversion and source FOV. Pip-Boy presentation uses the source Hit
hold point and winning arm IDLE. Source draw/reload clips, magazine state and
animation sound events share the gameplay owner. FalloutWeaponShot resolves
winning weapon/ammunition/projectile declarations. Source attack Hit events
publish semi-automatic hitscan rays from the posed weapon node, while the shared
weapon/inventory owner atomically consumes rounds and retains recovery random
state. XR and flat input enter this same path. Actual source bone contacts now
resolve BPTD regions and damage constant-health creatures through the shared
reference owner. Winning skill settings, NV weapon condition and unconditional
abilities supply the bounded damage calculation. Living NPC health, full armor,
critical/sneak modifiers, reactions, combat AI, hit/death scripts and XP remain
unbound. Death switches that actor from its idle clock to its source skeleton's
rigid bodies and joints; corpse activation uses the existing container owner.
Saved injury, death inventory and physical bone poses share reference persistence.
Cold death construction waits until cell attachment completes. The current
Godot angular envelopes do not reproduce every Havok solver parameter, and limb
severing/explosion presentation remains incomplete.
Muzzle presentation resolves source addon indices through winning ADDN records,
indexed from the runtime's existing plugin stack before presentation begins.
One effect clock advances source controllers and emitters through short emission
windows, including long host frames, and retains surviving particles. The
growth/shrink modifier blends from its base-size endpoint through the smaller
of its generation-specific envelopes; a zero endpoint does not erase particles.
Completed nonphysics impacts retain at most one reusable graph. Reuse resets
controllers, particle state and emission remainders only after all particle and
audio tails finish; overlapping effects keep independent state.
The posed ShellCasingNode ejects the original WEAP MOD2 with source shell settings and its
own collision body. Original PROJ/LIGH supplies muzzle illumination. Actual
collision shapes and packed triangle materials select WEAP IPDS/IPCT models and
sounds. LAND retains LTEX Havok declarations; ambiguous material blends remain
unbound. These are presentation owners and cannot establish gameplay damage.
Exterior lighting registers transient meshes and particle draws within the active
world, excludes separate viewports and unregisters expired objects.

Native OpenXR has one visible source world body and an authored first-person
attachment rig for weapon and Pip-Boy action channels. Both use the same prepared
shoulders and collision-resolved wrists. The wrist UI borrows the existing device; it never
creates another player. Flat and XR share source triangle/UV picking and native
menu/gameplay state. Grip focus keeps the world live. The whole original wrist
device scales around the anatomical forearm centerline and rolls only about
that axis toward the reader. Release restores its source pose by the shortest
angular path. Owner replacement tears
down UI subscriptions before binding the new equipment. Physical acceptance,
room-scale collision following and complete menu actions remain required.

Particle draws pack the same source transforms, RGBA and atlas coordinates into
one reusable MultiMesh buffer per effect. Active quad bounds prevent render-side
bounds reconstruction from the uploaded data. Conservative bounds expand when
quads escape and refit once per simulated second; empty effects skip draw work.
Native action identifiers and particle distance comparers are reused.
Opt-in live diagnostics expose viewport CPU/GPU timing, a bounded 512-frame host
cadence window and the actual loaded C# optimization flag. Snapshots and JSON
freeze on the engine thread; one background writer replaces the complete file
atomically, preserving open readers. Backpressure and write failures remain
visible. Lightweight coverage queries do not replace canonical capture.

The winning cell-child index is built once for each requested record signature.
Subsequent cell queries visit only that cell's indexed children. The lab's
all-cell sweep exercises the same index and lifetime owner as ordinary loading.

Exterior residency follows the player's source grid coordinates. Background C#
reads prepare LAND; Godot stages new terrain and reference uploads, retains the
overlap, then commits cell membership and lighting. Source ancestry remains
independent of the active grid. Overlap retains event contacts, OnLoad state and
actor animation owners. Collision availability guards the edge of the resident
grid. Source LOD uses the owned quadtree, terrain morph data and a mask of actual
detailed LAND. Available parent/finer coverage is retained during replacement.

Source byte reuse has a 512 MiB LRU; decoded NIF declarations are immutable and
reused within each file. Eviction removes the cache's ownership without invalidating
live readers. Decoded CELL metadata has a separate 128-entry LRU. Scene prototypes
and detailed texture entries expire with residency;
inactive LOD resources have a separate estimated budget. These caches are disposable
in-memory implementation details, never transformed launch inputs. Flat sprint and
step climbing are explicit OpenNV controller options; source jump height and shared
player control gates still govern ordinary movement.

## Launch sequence

The Windows development launcher defaults to a native ExportRelease build in
tmp/development-runtime/windows. Its manifest retains all campaign declarations
and selects the existing packaged-executable route. Explicit Debug uses the
Godot project runner, which always loads Debug OpenNV/GodotSharp assemblies.

1. The launcher validates the selected installation and campaign.
2. `NativeGameInstallation` identifies the game and content root.
3. `RuntimeLiveContentSource` resolves plugins, loose files, and archives.
4. C# readers build record/resource relationships in memory.
5. Campaign and world owners create Godot entities from those relationships.
6. Save files contain gameplay state and source compatibility identity only.

For NV/FO3, plugins.txt selects explicit activation; TES4 master flags and file
timestamps determine order. FNV NAM sidecars additionally activate matching
ESM/ESP files. Installed inactive masters are excluded. Archive admission uses
the Archive INI list, admitted plugin archives and FNV's base update archive;
unconfigured inactive archives do not become resource winners. Live retail's
admitted plugin array, rather than plugins.txt alone, verifies a comparison stack.

## Promotion rule

A parser count is not gameplay. A rendered cell is not a campaign. Support is
claimed only for behavior exercised by ordinary input and persistent state.

## Live parity evidence

The parity subsystem uses one canonical little-endian telemetry contract for a
private, read-only retail observer and the public OpenNV runtime. Each producer
publishes hash-bound frames through a Windows shared-memory ring. The comparator
joins equivalent state keys, checks canonical state bytes exactly, expands the
first mismatch into typed field deltas, and retains a bounded in-memory frame
window around divergence. Godot can show retail, OpenNV, and absolute-difference
views; the C# evidence writer can encode selected retail-left/OpenNV-right MP4
clips with hash-bound reports. Retail observation is one-way and never supplies
gameplay state to OpenNV. A diagnostic bridge may duplicate timestamped user
input into both games, but neither producer may use the other game's state to
advance simulation.

The comparison denominator is discovered from live source and runtime owners,
not a maintained checklist. A source identity without a runtime entity, an
unpublished event, a lost packet, or an unmatched final frame is divergence.

Exact source Float32 fields retain their original bits. Optional OpenNV frame
capture records native viewport bytes and state at both draw boundaries, with
PNG previews and file hashes. These outputs do not establish retail frame
correspondence. The live join currently produces candidate sample pairs;
unknown event identity stays unaligned, and full event/present observation is
still required. No sampled-packet or startup-capture result certifies gameplay.
