# Source Escort packages

`FalloutEscortPackage` reads winning type-2 PACK records and master-adjusts their
explicit reference target and destination. `PKE2` supplies the escort distance;
`PLDT` supplies the destination radius. Required field extents and counts,
reference identities, radius and supported behavior flags are validated before
binding. Zero authored distance stays zero. Unknown selectors, additional search
locations and behavior flags remain visible failures.

The shared procedure first approaches its target. After acquisition it leads
toward the destination, waiting when the target exceeds the authored distance
and is no closer to the destination. A target already closer to the destination
does not force that wait. These rules follow the documented
[Escort procedure](https://geckwiki.com/index.php?title=Escort_Package); record
fields follow the [PACK layout](https://tes5edit.github.io/fopdoc/Fallout3/Records/PACK.html).
Neither source documentation nor the checks below establish matched retail
timing or full package parity.

`RuntimeNativeNpc` currently binds the player as the target and an explicit
marker in the actor's resident cell as the destination. It reads the player's
current collision-resident position. The existing package-motion owner projects
the marker onto owned NAVM, refines the route against native actor clearance and
consumes source KF locomotion through the native capsule. Escort does not write
actor transforms, move the player or override collision. Arrival requires floor
contact and the existing native package arrival tolerance, including the minimum
stop radius; exact retail stopping distance remains unverified.

Acquisition and completion accompany the reference-owned package motion,
animation identity and clock. Cold restoration validates the winning package and
restores its lifecycle without repeating the consumed start event. A completed
restored procedure does not publish another completion. Unsupported legacy save
schemas reject nested Escort progress. Before the first native motion snapshot
exists, reference capture refuses saving; this also covers unavailable targets
or a conversation before the first move. Live diagnostics expose the pending
capture count and defer the full reference snapshot while retaining actor state.
Queued autosaves remain pending during this initialization window instead of
turning a temporary capture refusal into a permanent execution failure.

Completion publishes the motion/procedure state before running source results.
The package-event owner checks its active package and revision after synchronous
completion callbacks, so replacing the package cannot mark its replacement done.
Existing source failures retain their consumed prefix and prevent replay.
Cold restoration also retains package-result failures on actors without an
attached script; a completed motion snapshot cannot silently erase that fault.

Actor telemetry exposes package, target, destination, source distances, projected
endpoint, acquired/completed phase and residency/procedure status. Current AI
queries report Escort, its approach, wait and completed procedures through the
same owner.

## Verification

`ReferenceScriptContractProbe --escort-contracts` passes winning override and
master-adjustment checks; malformed source rejection; approach, lead, wait,
catch-up and target-ahead decisions; completion refusal while the target lags;
serialized cold motion; once-only lifecycle restoration; and synchronous
package replacement during completion. A failed completion on an unscripted
actor retains its applied actor-value prefix and error through serialized cold
restoration without replaying the consumed package event. The native reference
audit exercises queued autosave deferral through the production driver and
verifies that explicit capture still refuses incomplete procedure state.

The selected `NativeActorPerformanceAudit --escort-package` owned-data fixture
passes actual source NAVM, KF and native capsule motion, stationary waiting,
serialized cold waiting without start replay, target-ahead continuation, a
blocking-wall refusal, resumed arrival and one completion event. It also checks
that pre-motion capture refuses losing the consumed lifecycle. This fixture
uses an explicitly synthetic floor and target observations; it is not ordinary
campaign traversal, actual hall collision proof or matched retail evidence.
Frame recording remains off.

Non-player targets need their temporary Follow-assignment owner. Object targets
need search, pickup and drop procedures. Other-cell destinations, non-marker
destination interactions, additional target selectors and search flags remain
unbound. Ordinary unlocked route doors now use the shared
[NPC door navigation owner](npc-door-navigation.md). The original reached live
route resumed after player activation without any collision or grid changes;
that observation alone does not verify NPC automatic activation. Combat
assistance, exact retail scheduling/stopping behavior and
physical OpenXR acceptance require separate evidence. The ordinary campaign
result and next reached blocker belong in [current work](current-work.md).
