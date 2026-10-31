# Current work

## Active owner

Continue retail recording, Godot playback and shared-system repair. User target:
playable game at native 3840x2160, sustained 60 FPS, with every source scene and
applicable gameplay action accounted for. The user accepts roughly 57â€“60 FPS
with both games running as the comparison floor, with no further reduction.
Use scripted native channels, ordinary input and real saves. No computer-use or
new test-suite work. Root owns integration/builds/input/Git; two workers now
implement separate shared-system fixes while root continues gameplay.

## Current candidate

The ordinary campaign has traversed the Goodsprings road and entered the Saloon
in both games. Source NAVM routes drove ordinary keys/look input; no teleport or
scene substitution. The entrance positions were approximately 0.28 m apart;
aiming and destination labels differed. Eye/projection, clocks after dialogue,
cloud phase and campaign history are not fully matched. Do not claim pixel parity.

Capture-off short outdoor movement measured 60.02 draws/s OpenNV versus 60.06
retail. The longer Saloon approach measured 58.59 versus 60.05 over 37.72 seconds.
A 15.41-second interior sequence with movement/dialogue measured 59.13 versus
59.59. These are bounded observations with both games at native 4K, not long-run
or post-combat acceptance. A simultaneous two-encoder approach fell to 43.66
draws/s while blocked; the same spot later ran at 59.93 without recording.

Both games completed Sunny's first training dialogue and bottle lesson through
ordinary triggers, weapon input and three distinct source hit events; both
opened the same continuation choices. OpenNV followed Sunny to the wells and
now advances through stages 40 and 45 after restoring the stage-35 checkpoint,
matching retail's observed stage history. Travel arrival now publishes its
completion event independently of ContinueIfPCNear selection retention.
The committed well checkpoint is 86652b7c-6bdb-43e2-8082-85d1ed3face4.
Ordinary combat has exercised damage, a 25-body/24-joint death ragdoll and player
death with menu recovery. A subsequent ordinary fight killed both geckos with
the player surviving at 220 health. Both games opened and looted a corpse;
their different random loot rolls are not a parity failure by themselves.
OpenNV quicksave committed in 3.110 s. After movement, quickload restored the
exact position and two Gecko Meat plus one Gecko Egg, recovering 57+ FPS in
11.625 s. The post-loot checkpoint is a6340ebb-f5e8-4e08-9858-dcf29cc72074.

The compass strip is visible, the nonspatial window region retains its source
weather, F5 -> movement -> F9 restores the exact saved position, and a later menu
save does not retarget quicksave. Easy Pete physically exited the Saloon and sat
in his chair without initial-placement snapping. Restored package lifecycle
reconciliation fixed the first outdoor save failure. Immutable source/index and
decoded-resource reuse reduced observed warm recovery from 26.84 to 17.24 seconds;
full source reuse was confirmed. Compass NPC ticks, player markers, blink and
quest portal redirection remain unbound.

The current combined Release export adds
shared assembly work budgeting, player health base preservation, projectile
contact impulses, and matching XTEL portal approaches. Native AV observation
qualified health 220 = reference base 100 + derived 120, END 6, level 1, with zero
modifiers; restored OpenNV now has that health. Bullet impulses were absent from
the ordinary impact path despite successful script hits; actual prop impact and
physics persistence still need smoke. Warm well assembly took 2.841 s for 2,309
references; this is not a same-scene comparison against Saloon loading times.

A private retail well recording contains 280 acknowledged ordinary inputs and
kills both geckos. Uncorrected Godot replay delivered all 280 inputs but left
both geckos alive. This is an unjoined diagnostic, not matched-state acceptance:
the saves have different skills, weapon condition, ammunition and actor history.
Both natural gecko hits deal 5 damage. Retail's larger final position change
includes 3.645 m of post-stop hit movement absent in Godot; a run-speed multiplier
change is not justified. Shared melee knockback now has an implementation;
the current Release has been exercised with real gecko hits. Ten samples have
active knockback; the retained rate is 0.74295 m/sÂ² for the owned 5-damage,
Agility-6 inputs. Fourteen post-stop samples retain no movement keys and show
0.829 m net displacement. Both geckos subsequently died through ordinary shots,
the player survived at 170 health, and quicksave completed to
7fd8df60-ad04-4985-8809-4f73130fd9b9. Active airborne cold restoration is divergent:
native preserves instantaneous controller/proxy velocity but does not serialize
the remaining knockback timer. OpenNV currently retains rate/time and omits
independent motion. This needs a coherent motion owner and post-load observation,
not an isolated timer reset. The long-range approach bot spent 55 s planning
without movement; the successful approach used ordinary keys over the existing
source NAVM route. Approach now retains a checked segment like other moving-target
modes. Its live retry reached the moving gecko in 45.64 s with 39.49 m net
movement, observed arrival at 3.184 m and no error; the old cancellation reason
was not recorded. NPC step/combat resets also clear their displaced vertical bookkeeping;
stopped-pose readiness now agrees with its existing knockback diagnostics.
The canonical comparison is 32 s, 3840x1152, 30 FPS, with two aspect-preserved
1920x1080 panels and explicit diagnostic labels. One global camera-onset offset
aligns sequential GPU captures. Audio and exact video-clock joins are unverified.

The candidate changes preserve Travel flags during furniture approach,
bind emitter initial-radius controllers, and preserve authored IEEE UV data
under the qualified sized-Bethesda layout. The existing owned Godot build audit
passed for the Hoover Dam AA gun: 103 nodes, 57 surfaces and 31,611 vertices,
including its managed material sequence samples. The final combined gate and
Release export remain pending; this is not in-game animation/pixel acceptance.

The required locomotion check exposed premature floor support at a raised
slope endpoint. Shared tangent clearance now precedes the planner's existing
downward settling/root checks; live movement retains normal gravity/snap.
The full existing controller audit passes steps, walls, ceilings, both capsule
sizes, unsupported destinations and steep-slope refusal without relaxed bounds.

Paused menu saving exposed a separate finite-audio admission bug: four active
generations across three windmills were refused before the existing drain
could start. The corrected Release admitted two original windmill generations
to that existing drain, observed genuine completion and committed menu slot
2cbfbe47-196e-42c3-a610-2f8c00bba429 with gameplay still paused and health 170.
Unpaused F5 also drained windmill and bighorner voices and committed successfully.
The final required gate and Release export pass with the NPC bookkeeping and
bot corrections included. The final build killed the first gecko after ordinary
reloading, then navigated around the water-tower obstruction and killed the
second; player health was 210. The direct stationary combat skill correctly
refused the obstructed shot before the navigation-capable shoot flow was used.
Approach/combat FPS samples dipped below the requested target; this is not a
sustained 4K60 combat pass.

Two capture-off stationary windows measured 57.482 and 59.547 draws/s with
physics near 90 Hz. Retail was not presenting during those windows, so they
are not valid simultaneous game-versus-game measurements. Retail has since
been recovered at native 4K with fresh presentation/input/observation. It is
precombat while the current OpenNV save is postcombat. Eleven intermittent
live-state replacement failures remain visible; exact telemetry parity is not
established by the successful gameplay smoke.

A fresh uncaptured stationary well interval measured 59.994 draws/s over 43.74
seconds with both games running. A prior lower interval included a one-shot 4K
readback and a 2.965-second draw gap. This is not moving/post-combat acceptance.

The complete selected source inventory finished: 44,517 winning CELL reports.
It retained 877 source failure events and unverified runtime ownership: 484
LAND records without authored vertex geometry, 275 primitive extent refusals,
106 external NAVM triangle conflicts and 12 world persistent-cell refusals.
Plane primitives can legitimately have a zero dimension; they need their actual
owner, not an epsilon box. This is an inventory, not visited-scene acceptance.

Built and live-smoked changes include climate/GMST sun motion and retained night
light energy, complete-ancestor LOD replacement, four authored cloud layers
instead of sixteen, weather IMAD grading/defaults, exact SetEssential failure
continuation, shared GetPos binding, and smaller routine quest telemetry.
The cloud layer seam is removed. Current weather selects its owned day IMAD;
remaining HDR channels and weather transitions are visibly unbound.

The Nevada flag now loads and renders beside Doc's house. Its source skinned
morph partitions use direct scalar channels with the shared unpaused application
clock, separately from the looping Idle bone animation. Both morphs reach the
authored 22.833334-second clamp, matching the qualified native controller state.
Flag phase, sound timing and loading/menu clock accounting are not parity claims.
The live owned mouse setting now resolves to .002 with inversion and FOV scaling;
run/always-run use remappable controls and the existing profile overlay.

Vegetation has two separate missing owners: LTEX grass links are discarded and
no native GRAS/LAND placement generator exists; placed SPT models are explicitly
refused by the unimplemented procedural decoder. Density adjustments cannot
restore these missing capabilities.

The movement fallback is built and live-smoked: the raven resolves its actual
Idle clip without declaring its wander route arrived. Its AI failure clears,
quicksave commits, and the ordinary save browser reloads the same road checkpoint.
F9 now loads the dedicated quicksave through the normal save loader; both exact
position restoration and isolation from later menu saves were observed.
GRA now advances through GetPos and stops at SetPos. Numerous other script, actor, vegetation,
lighting, UI and scene-completeness failures remain. The full defect inventory
and individual proof limits are in [repair-batch.md](repair-batch.md).

## Immediate next work

Finish the current post-combat performance observation and publish the checked
batch, then continue ordinary interior/exterior routing. Paused-menu saving,
actual melee hit displacement, moving-target approach, two-creature combat, corpse
looting and post-combat save/reload are exercised. Prop impacts, broader actors,
weapons and active-knockback cold persistence remain open.
Check actual asynchronous engine receipts: harness submission alone is not
command execution. Save preparation must reach a committed receipt before any
Escape input; Escape cancels pending sound-voice preparation.

Cheyenne remains in the Saloon after the player leaves because unloaded package
selection has no movement executor. Native uses a virtual path handler for low
process actors, including source activation at doors; keeping every room's
physics alive is not required. The worker has a private draft of the qualified
tier, time and path owner, including door-driven MiddleLow admission; integration
follows review and current-batch publication. Do not place the dog beside
Sunny or claim metadata selection as movement.

The reloading bench completed Breakdown 20 Ga.: one shell consumed, with 18
pistol powder, one hull, one primer and 26 lead produced. This is one OpenNV
craft, not retail crafting parity or persistent-save acceptance. Continue NPC
combat, loot, other crafting stations, conversations, activity transitions,
long-distance streaming and recovery.
Use the existing source-navigation/input/bot owners to progress between cases;
record blockers and continue independent cases instead of repeating blind paths.

The source sensitivity at reference FOV75 is .002 in both games. Retail FOV55
reduces it to .00146667. Verify headings before forwarding movement. Recorded
barriers include actual fences, yucca and rock contacts; differences in paths
and capsule contacts remain explicit, not reasons to weaken collision.

Then continue matched recording and coherent fixes for remaining gameplay
owners, including SetPos, source-clock/weather queries, actor behavior and
combat. GetDayOfWeek and committed-weather IsRaining contracts are qualified;
SetPos must preserve motion/combat state and is not equivalent to SetPlacement.
Keep frame recording bounded to visual checks; clean temporary frames afterward.

Work remains on codex/essential-actor-lifecycle. Runtime changes are unpublished.
Earlier gate results predate the latest changes; current validation consists of
Release builds, actual gameplay smoke and diff checks. Required repository
checks still apply before publication. Do not call this candidate tester-ready,
complete, faster than retail, or a parity pass.
