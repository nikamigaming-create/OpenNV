# Current work

## Owner and objective

The November 3 user direction is to repair every weapon family and mob family
through shared capabilities, exercise them against source-placed enemies in their
actual areas, and reach sustained native 3840x2160 at 60 FPS. Teleporting or fast
travel to a source area is authorized for setup. Do not substitute source counts,
assembly checks or firing counters for working encounters or retail parity.

Use short, bounded gameplay sessions: launch, execute the selected encounters,
read compact outcomes/timing, and close the game before coding. Recording stays
off. Do not create proof captures or new harness infrastructure. Use the existing
command center/scripted bridge and headless tools; desktop control is prohibited.
Root owns integration, builds, game input and publication. Workers use Sol with
medium reasoning and nonoverlapping ownership. Required publication checks remain.

## Source scope

The active graph is the installed FNV official/DLC graph. The private comparison
plugin adds placement/containers without overriding weapon or actor combat data;
FNVR.esp is excluded. FO3, TTW and other graphs require separate execution.

The private matrix remains `local/outputs/combat-parity-20261102/combat-parity-matrix.xlsx`:
496 winning WEAP records (302 playable), 6,455 actor definitions, 233 source type
groups and 7,681 placed actor references. These are scope inventories. Every full
retail-parity row remains open; physical XR and complete campaign support remain
unaccepted.

## Integrated repair batch

The merged combat batch retains source unarmed/BIPL equipment, player/source mines,
fatigue knockout/get-up and save continuation, actor hit reactions and pursuit,
weapon recharge, firing/reload ownership, inventory script events and source
sound/UI repairs. Existing live observations covered selected gecko retaliation,
boxing-glove knockout/get-up, unarmed/brass contact, deployed Powder Charge
save/load/disarm/pickup, and source creature Eat continuation only.

November 3 shared corrections add:

- Per-projectile weapon damage instead of applying the whole discharge to every
  pellet; inventory totals and explosion damage remain separate.
- Exact firing-item script identity retained across delayed contacts and weapon
  switches, captured before ammunition/condition publication.
- Source non-discharge OnFire actions and creator-owned remote mine commands,
  using the existing physical/persistent mine owner.
- Conditional WEAP enchantment delivery and instant explosion health effects;
  tooltip-only effects do not mutate gameplay. Health effects use authoritative
  death/loot/save owners without weapon anatomy or armor multiplication.
- Pursuit invalidation before knockdown/hit-reaction displacement, supported
  attack-region arrival before an occupied target centre, and live NPC Unarmed
  values at the strike.
- An immutable triangle bounds index for repeated source-floor projection,
  preserving exact triangle/height admission and source-order ties.
- Exact material-controller channel selection for shared weapon animations.
- Enabled-only source connectivity and reuse of the exact source-route floor
  predicate for combat arrival. Unfiltered off-path queries retain their directed
  frontier and stop after discovering a reachable target; filtered callback
  scopes keep their existing snapshot semantics.
- Stack-scoped immutable weapon script metadata, including unscripted weapons,
  without caching or weakening live exact firing-item admission.

Active unowned DOT, scripts, resistance and effect presentation still fail
visibly. Remote explosions outside residency remain delayed. Critical/sneak,
melee/thrown/multi-projectile VATS, split-beam damage bonus and additive-perk
ordering remain unfinished or unqualified. Do not widen admission to hide them.

## Verification and next work

The complete required runtime gate passes: Release/Debug, formatting/analyzers,
contract probes, launcher checks and native Godot loading/behavior audits. The
selected existing owned weapon audit completed all 496 records; 61 still report
findings (previously 67). These are assembly checks, not accepted encounters.
The current Release executable is `tmp/development-runtime/windows/OpenNV.exe`.

Bounded source-gecko encounters used the existing guarded player MoveTo followed
by ordinary input. Laser RCW changed target health from 40 to 31.075 and the
gecko pursued/struck the player. On the final follow-up build, Hunting Shotgun
changed target health from 40 to 19.3 and the gecko retaliated. These are selected
contact/health observations, not all-pellet receipt or complete weapon acceptance.
The two successful sessions closed normally after 121 and 119 seconds; no game
or harness remains running and recording stayed off.

The final build's unpaused 20-second moving window confirmed native 3840x2160 at
scale 1, with ten FPS samples between 58 and 60, p95 frame interval 18.73 ms and
maximum 22.50 ms across the 1,188 observed host-draw intervals. This is not
sustained 4K60 or display-presentation acceptance. The earlier RCW window retained
a 166.62 ms spike; different encounter states do not establish causal speedup.
Merged PR350 addresses synchronous beam/impact work with shared immutable
geometry, independent materials/controllers, preparation on equip, and bounded
reuse of completed effects. Fresh overlaps still create independent owners. Its
complete required runtime gate passes. Selected owned controller/geometry and
finite audio-lifetime audits pass, as does decoding the four source pulse/fire
effect shaders. No measured speedup is claimed yet.

The final combined runtime gate passes, including both navigation additions and
immutable script caching. The existing owned Mojave route audit also passes:
4,037 NAVMs, 589,719 triangles, the same 41 source waypoints, no unavailable meshes.
This is one source route, not world traversal acceptance.

Retail startup is restored. Steam's ActiveProcess registration pointed to a
dead process while a different client was running. With no active Steam games,
a graceful client restart refreshed that registration. The unchanged isolated
game-launch path then produced fresh bridge/state in three seconds. That bounded
check was closed. The existing private launcher now refuses stale Steam process
registration before starting a game. Temporary startup files were removed.
The existing observer launcher also selects the current controlled-sampling
build and starts with sampling off. The user is away; no manual launch is needed.

A fresh paired Laser RCW encounter reached the same source-placed gecko in its
area and completed ordinary fire/reload, then closed both games and the harness
after 128 seconds. Recording stayed off. OpenNV spent two rounds, recorded one
torso hit for 8.925 damage, and reloaded from 58 to 60. Retail spent one round,
recorded hit location 1 with a reported x2 multiplier for 17.55 damage, and
reloaded from 59 to 60. These different hit locations, starting player states,
and observed shot counts do not qualify a damage-formula change or parity claim.
The compact run does expose OpenNV's unowned animated muzzle light and 28.99 ms
last-shot work, including 22.54 ms in damage scheduling. Aggregate timing includes
loading/menu states and cannot certify combat 4K60.

A bounded 9mm/Bloatfly comparison reached the source area in OpenNV and equipped
the weapon in both games, but retail's scoped pose sample reported seven read
failures. Firing was not attempted; both games closed after 109 seconds. This is
an observation failure, not an accepted encounter or a weapon damage verdict.

The active model/impact-preparation branch repairs source-empty billboard
handling and nonnegative texture-index declarations that the loaded geometry
never visits. Populated unsupported billboards and invalid signed indices still
fail. Existing synthetic checks pass. The owned audit completes all 496 records
with findings on 61, down from 67; 19 assembly failures across 15 records are gone.
This remains assembly/admission coverage, not encounter acceptance.

It also caches immutable actor perk declarations while merging live reference
and teammate grants per query, prepares dormant impact graphs on equip within
the existing eight-entry pool, and separates beam time from casing time. Eager
preparation errors remain visible separately from actual contact errors. First
activation, concurrent effects and source audio retain independent owners.
The owned impact-lifecycle check passes, including actual finite audio retirement;
unqualified reverb and listener-submersion lanes remain explicit.

A paired Q-35/source-gecko session completed one discharge in each game and
reload from 11 to 12 rounds, then closed both games after 132 seconds. OpenNV
reported no weapon errors; neither engine registered target damage. Its first
shot took 49.50 ms in the combined preparation/collision path. The branch now
prepares one detached projectile model on equip and shares immutable geometry
across later independent flight graphs. The owned Q-35 preparation check passes
for dormancy, overlapping flights, same-model re-equip and retirement. The final
runtime gate passes and the Release was exported with these changes. A 97-second
follow-up fired three ordinary Q-35 shots with no weapon errors, then closed
normally. Their shot work measured 2.21, 20.32 and 26.84 ms. All missed the moving
gecko; aim precision was unconfirmed. The run stopped before its performance
window. This does not establish hit parity, causal speedup or sustained 4K60.

The selected legacy checkpoint lacks PlayerInventoryChangeEntries. Its carried
script traversal cannot recover the historical source order. A bounded scan of
existing nearby checkpoints found no usable journal. Fresh New Game initializes
it; use ordinary acquisition and F5 to establish a current checkpoint. Never
backfill or edit saved history.

Next owner is root: publish this verified batch, establish the fresh ordinary
checkpoint, then continue shared contact and frame-budget repairs. Animated muzzle phase/refire semantics,
actual input/discharge timing and remaining contact work are still open.
Merged PR350 preserves finite signed projectile coefficients, with unqualified
negative flight behavior refused at admission. It adds source EFSH metadata and
a preflighted, persistent Start/Finish lifecycle, but no pulse/fire admission:
posed actor emission, shader evaluation, add-ons, recast and gore state remain
unqualified. Unreachable floor queries can still
exhaust the remaining component synchronously; retained reachability does not
certify a frame budget. Use existing in-world performance publication with cell,
pose and loading state. Close sessions before coding, keep recording off, and
exclude paused/menu timing from gameplay results.

## Publication and local candidate

PR348, PR349 and the effect-lifetime/resource batch PR350 are merged with all
five hosted checks passing. Main was verified against origin/main at
`33243bc6a8b936c244d27e737ecc904a6775718a`, with no open PR for that batch.
The active branch is `codex/weapon-models-and-impact-preparation`, based on that main. Publish completed
changes through a checked PR and verify main against origin/main afterward.

The current Release executable is `tmp/development-runtime/windows/OpenNV.exe`;
it contains the verified model, perk, impact and projectile preparation changes.
The latest gameplay session is closed; recording stayed off.
Unrelated classic import changes remain outside these commits. Private assets,
saves, captures and raw reverse-engineering outputs stay outside public history.
The November 2 experimental ZIP predates the repairs and is not current.
