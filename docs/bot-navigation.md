# Bot navigation and interaction endpoints

`ReactiveReferenceBot` plans from current source-reference observations and
submits ordinary movement, looking and activation input. Winning NAVM corridors
are refined against the live player's native capsule, floor support, step rules
and resident collision. Neither planning nor execution writes gameplay state,
player transforms or collision results.

Each returned route distinguishes the requested standoff, the source-projected
NAVM endpoint and the locally verified segment endpoint. A partial corridor must
be replanned from its observed arrival before the next segment is consumed.
Reaching a projected endpoint alone does not establish target arrival or a
successful interaction.

The final waypoint tolerance scales with the segment's initial closing distance,
bounded from 2 to 20 centimetres. This prevents a short segment from being
consumed immediately and removes the contradictory fixed 10-centimetre movement
requirement. The obstruction guard tracks decreasing waypoint distance with a
small, scaled observation tolerance; sideways or backwards displacement cannot
continually reset it. Stationary partial segments and obstructed movement retain
bounded retries and release held input.

At a projected endpoint, interaction first aims at the target's live geometry.
Only an observed ordinary ray identifying that exact reference permits activation.
Activation is sent once, then waits for an observed gameplay response. An
outcome token changes only for actual target state or menu effects observed
inside that target's successful OnActivate blocks/default action. GameMode,
OnLoad, unrelated stages, pause and conversation clocks cannot supply that
proof. Multiple activation blocks preserve source order; a later source failure
discards tentative results. Delayed conversation or portal proof requires a new
target-specific request created inside the activation and its matching observed
presentation or destination; a request alone never completes the goal. Three
seconds without a response fails visibly. A standoff or projection that cannot
reach the interaction receives bounded closer-source queries; tightening keeps
the already observed floor's vertical offset admissible. Requested range uses
three-dimensional distance, so horizontal overlap alone cannot accept a different
floor. No source target or quest outcome is special-cased.

Bot state retains its goal after completion or failure, active status, source
navigation identity, requested/projected/local endpoints, projection radius,
endpoint distances, observed movement, retries and error. The surrounding live
session evidence supplies build, process and source-stack identities. A delivery
receipt remains separate from ordinary activation and the resulting state.

The focused `ReactiveSteeringContractProbe` covers the original moving-target,
modal-control, exact-reference and transport contracts. Its navigation cases
also cover an 8-centimetre approach to a short endpoint, sub-10-centimetre partial
arrival, already-inside range, slow consistent closing, projection refinement,
near-range aiming that must move closer, observed modal response, missing
response, zero-length segments, movement away from the waypoint, different-floor
refusal and bounded unreachable-endpoint aiming. Moving-target cases cover a
stale endpoint after target movement below the normal replan threshold, repeated
successful follow legs and a stationary player despite repeated moving-target
replans.

An ordinary flat bot run from the genuine CG01 stage-40 checkpoint reached and
activated the source SPECIAL book. The native activation was accepted, CG01
entered stage 50 and the actual menu 1060 opened. The run used one source route,
with no obstruction or closing retry, and observed about 16 centimetres of
movement. Its requested-to-projected endpoint difference was about 84 centimetres;
the actual interaction ray, rather than distance to the reference origin,
established activation eligibility. Subsequent ordinary source-geometry pointer
input completed the allocation. A separate live continuation opened the source
main door and followed Dad to his destination, reaching CG01 stage100 and the
next quest's retained AgeRace failure. A fresh process cold-loads the genuine
stage80 autosave and repeats the continuation with eight moving-target replans
and no bot error. The Vault exit, Megaton, train travel,
campaign completion, matched retail presentation and physical OpenXR acceptance
remain unverified.

Follow mode reacquires moving resident references and retains a bounded goal.
At a reached endpoint, observed target movement requests a fresh standoff route
without tightening source projection. Actual segment closing distinguishes
successful following from repeated stationary endpoint replans. A separate
no-motion observation survives waypoint invalidation, so a moving target cannot
continually reset a blocked player's stall limit. Observing the requested follow
distance retires the old route before the target moves again.
It does not infer door activation or a campaign transition when an actor becomes
nonresident. Such transitions require source-informed decisions and separate
ordinary interaction goals.
