# Creature package movement

Creature package selection follows winning PKID priority and source conditions.
Follow and Dialogue declarations may omit PLDT; requiring a start location for
every package incorrectly rejected ordinary companion packages. The admitted
Follow procedure reads its target and distance from PTDT. Creature Dialogue can
approach its target and request the shared conversation owner. Reached start/end
locations, other procedures, unsupported conditions and event effects remain
explicit failures.

Native movement shares the actor's source BBX envelope, KF root displacement,
turn/speed configuration, source navigation and Godot collision solver with
combat. Package clocks remain separate from ambient idles and combat. Gameplay
pause, restraint, disabled references and combat suspend package movement.
Stopping package intent preserves falling velocity. Dialogue approach continues
the idle physics step and waits for floor support before starting its greeting.
Hover height remains in the source skeleton/animation; no actor-specific offset
or flying collision bypass is applied.
Mobile-actor MoveTo projects the requested destination onto owned NAVM before
publishing placement. Copying a raised prop origin had put the repaired actor
above the counter. Immobile model flags and non-actor moves preserve exact
coordinates; missing navigation refuses the move without partially changing
state. Synthetic checks cover projection, offsets, inherited Immobile flags,
missing floors and cold restoration. This follows the documented
[MoveTo actor-placement behavior](https://geckwiki.com/index.php/MoveTo).
ResetAI/EVP reach both creature and humanoid package owners. Conversation history
and package motion persist in save v15; v14 Aid saves remain readable. A moved
reference's dialogue cell comes from its live placement, not source ancestry.

The native owned-data fixture exercises movement, source stopping distance and
cold position/clock continuation on an explicitly synthetic floor. ED-E covers
the ordinary 330-unit package and source scaled health. Contract checks cover the
500-unit alternative, priority, missing/unsupported targets, invalid saves and
level clamps. These checks do not establish ordinary repair/recruitment, door
transfer, companion combat, retail AI timing or final-eye acceptance.

Actor pursuit now refines short source corridors with each actor's complete
capsule. Searches yield between node expansions under a shared two-millisecond
physics-thread budget and a 512-node search limit. An individual expansion can
exceed the time target. Unavailable routes use the source idle and bounded retry;
stalled waypoint progress requests a fresh route. Source NAVM nearest-point
queries reject distant mesh bounds before exact triangle projection, preserving
the existing distance and FormID tie ordering.

Step queries can find a walkable top beyond a steep bevel. They still require
nearby floor support and complete upward/forward capsule clearance. Native
fixtures cover both directions over a curb and around a wall, low ceilings,
unsupported airborne positions, unavailable-route recovery and cold pose/clock.
The selected owned Primm curb changes from 0.0023 m to 3.6033 m of forward travel
in the collision fixture. Ordinary flat and Elliott Tate simulator continuations
then clear that curb, let ED-E kill the second hostile and resume Follow. These
are finite route checks, not all-world traversal acceptance.

Mobility penalties use BPND actor values 29/30, not localized part names or
legacy bone names. A weapon bound to a thigh bone is not a leg. Synthetic checks
cover one/two mobility injuries, misleading names and ambiguous bindings; the
owned ED-E anatomy declares no mobility limbs. See the
[GECK actor-value definitions](https://geckwiki.com/index.php/Actor_Value_Codes).

Run the owned package check with the ReferenceScriptContractProbe's
`--companion-packages` option. NativeActorCombatAudit accepts owned Data followed
by `--follow <ACRE runtime hex> <PACK runtime hex> [MoveTo reference runtime hex]`
for the isolated motion check. The optional reference uses the real MoveTo owner
before admission, so a relocated actor uses its current cell's encounter policy.
