# Actor package continuation

Campaign save v32 adds two explicit reference-owned continuations. Older v31
files remain readable without rewriting them or inventing these new lanes.

A stopped NPC initialization retains the winning selected PACK/hash, actual
world position and exact basis, locomotion variant and existing animation clock, AI random state,
remaining poll delay and schedule time. Its retired predecessor retains the
consumed lifecycle revision, last change event and winning predecessor hash.
The blink queue retains its source settings, delay, targets, partial blend and
elapsed time, so rebuilding the face consumes no replacement random draw.
Cold assembly retries the selected pre-begin binding without re-evaluating random
selection predicates or replaying the predecessor's results. The procedure
failure stays visible; this continuation does not implement that procedure.

Capture requires a healthy retired lifecycle, no active furniture, travel,
escort, patrol, dialogue, response/event idle or combat pose, and an empty
selected idle collection. A pending selection, active continuation, result
failure or missing clock still refuses this capture. Native retirement transfers
the stopped state to the world, and unloaded current-package queries retain its
selected identity without beginning it. Starting a new selection clears the old
stopped state. Telemetry counts stopped bindings separately from procedures that
still lack capture owners.

Guard approach reads the winning procedure14, location, distinct intrusion
target/radius and admitted flags. Reference-marker and editor locations use the
existing shared placement reader. NPCs and creatures share the native NAVM,
source KF, capsule and floor owner for the initial approach. The location radius
belongs to later wandering, as described by the
[Guard package contract](https://geckwiki.com/index.php/Guard_Package); it does
not truncate the initial marker approach. Reference package motion retains the
anchor, approach progress, winning package/KF identities, clock and actual pose.
Cold validation checks the anchor after all saved reference placements restore.

Approach arrival keeps the Guard package unfinished and dispatches no end result.
Nonzero-radius wandering, intrusion warnings and combat confinement remain
explicitly unowned phases. Unsupported flags, targets and other-cell routes fail
closed. The bounded stationary no-warning variant has no wandering phase.
Complete Guard behavior, actor cold state and retail timing remain open.

Synthetic checks cover consumed retirement, random/poll/base-clock continuation,
unloaded identity, atomic source/pose rejection, distinct Guard radii and anchor
validation. The original TTW radroach Guard passes an isolated native floor/stage
fixture with source NAVM/KF/capsule movement, cold restoration during approach,
bounded marker arrival and no quest/end-result effects. This fixture does not
establish ordinary campaign collision or matched retail presentation.
