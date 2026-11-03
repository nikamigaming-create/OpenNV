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
selected existing owned weapon audit completed all 496 records; 67 still report
findings (previously 83). These are assembly checks, not accepted encounters.
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
The active branch addresses synchronous beam/impact work with shared immutable
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

Next owner is root: publish the verified effects/throughput batch, then repair
the shared animated muzzle-light lifetime and investigate actual input/discharge
timing and synchronous damage scheduling before the next bounded encounter batch.
The branch preserves finite signed projectile coefficients, with unqualified
negative flight behavior refused at admission. It adds source EFSH metadata and
a preflighted, persistent Start/Finish lifecycle, but no pulse/fire admission:
posed actor emission, shader evaluation, add-ons, recast and gore state remain
unqualified. Unreachable floor queries can still
exhaust the remaining component synchronously; retained reachability does not
certify a frame budget. Use existing in-world performance publication with cell,
pose and loading state. Close sessions before coding, keep recording off, and
exclude paused/menu timing from gameplay results.

## Publication and local candidate

PR348 and the navigation/script-cache follow-up PR349 are merged with all hosted
checks passing. Main was verified against origin/main at
`26a2d9fe4bfd8d34ead88b9a0ff66ab11bf797f5`. The active branch is
`codex/combat-effects-and-throughput`, based on that main. Publish completed
changes through a checked PR and verify main against origin/main afterward.

The current Release executable is `tmp/development-runtime/windows/OpenNV.exe`.
Unrelated classic import changes remain outside these commits. Private assets,
saves, captures and raw reverse-engineering outputs stay outside public history.
The November 2 experimental ZIP predates the repairs and is not current.
