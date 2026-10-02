# NPC route doors

The native actor navigation owner retains the actual rejected capsule contact,
collider identity, shape, normal and reachable approach. It considers a door only
when that same collider was hit along the intended source NAVM corridor and a
complete capsule route failed. Other doors touched during lateral exploration
cannot become activation targets. Source corridor probing never moves the actor.
Multiple native collision bodies can identify one door only through the existing
authoritative placed-reference index; both body identities remain in diagnostics.

A failed diameter-spaced actor search receives one radius-spaced retry. Both
passes retain the same capsule, step height, supported-floor checks, 512-node
limit and shared physics-thread time budget. This admits short support
transitions missed by the coarser lattice without changing physical clearance.
Telemetry records the coarse failure, refinement count and active spacing.

The actor walks the collision-validated approach using its existing source KF
motion. At arrival it repeats the physical contact at its actual position before
requesting activation. The request retains the NPC as the action reference
through `OnActivate` and any explicit default `Activate`. A source activation
block suppresses the default unless it requests it. Unrelated player inventory,
conversation and portal callbacks are not used for NPC door interaction.

The procedure submits one activation, then waits for the source door animation
to settle open. A fresh native capsule search still establishes clearance; the
open target flag or queued event never permits passing collision. An unreachable
approach, source fault or eight-second missing response remains visible. A moving
target or changed actor envelope invalidates the obsolete approach. Actor
telemetry includes the source-bound door, request count and contact details.

The supported branch is a living resident NPC using an unlocked ordinary door
with a native source Open/Close animation owner. Inaccessible and parent-only
doors are refused. Locked-door NPC key/ownership semantics, creature door
capabilities and actor portal transfer remain unbound. Exact retail reach,
waiting duration and closing-after-passage policy remain unverified.

The [PACK format](https://tes5edit.github.io/fopdoc/Fallout3/Records/PACK.html)
does not require a door-use opt-in; its lock/unlock flags change cell locks at
package boundaries. The [editor Door documentation](https://geckwiki.com/index.php/Door)
distinguishes sliding-door route intent from the AutomaticDoor player teleport
flag. [OnActivate](https://geckwiki.com/index.php/OnActivate) and
[Activate](https://geckwiki.com/index.php?title=Activate) document suppression and
action-reference propagation. Access declarations follow the
[reference documentation](https://geckwiki.com/index.php/Reference) and
[xEdit reference field definitions](https://github.com/TES5Edit/TES5Edit/blob/dev-4.1.6/Core/wbDefinitionsFNV.pas).

`NativeLocomotionAudit` verifies exact collider selection, exclusion of an
unrelated door, unchanged query-body pose, executable approach and renewed
clearance after synthetic door movement. `NativeReferenceEventsAudit` verifies
NPC `GetActionRef`, source suppression, explicit default activation, source
failure and locked/inaccessible/parent-only refusal. Both use synthetic fixtures;
neither establishes campaign traversal or matched retail parity. The genuine
closed-door campaign replay and current blocker belong in
[current work](current-work.md).

The additional native descent fixture reproduces a coarse-grid refusal and
executes the finer route onto the lower floor using ordinary capsule movement.
A private isolated fixture also reproduces the reached table pose against owned
local geometry and the source actor's BBX dimensions: the diameter grid refuses,
the radius grid finds a route, and the unchanged native controller executes its
waypoints to a supported door approach. This establishes the bounded sampling
repair, not a live NPC automatic-door or campaign completion claim.
