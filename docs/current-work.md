# Current work

Updated 2026-10-21. Priority: fully playable vanilla Fallout: New Vegas in flat
mode. Root owns this block without delegated agents. Development is greenfield:
prioritize working gameplay, not compatibility with old development saves.
Owned retail inputs remain read-only. Follow docs/implementation-plan.md.

## Active block

Branch: codex/fnv-remaining-wells, based on main c99aceb8 after merged PR #252.
Local main was verified equal to origin/main; no task PR remains open.
The linked-patrol eligibility and placed PGRE/PMIS physical restore fixes pass
selected owned checks plus the full local and hosted gates.

Ordinary gameplay completes the shooting lesson and first-well encounter.
Rifle fire kills both source geckos, their scripts advance VCG02 to stage 50,
and the encounter objective completes. Corpse activation and taking Gecko Meat
transfer one item to player inventory. F5 completes after the fight. Private
checkpoint: local/fnv-wells-fixed/resume.json (stage 50, before accepting more wells).
The navigation/combat adapter used ordinary look, movement, firing and reload
input; no health, inventory or quest state was forced.

Accepting Sunny's remaining-wells choice originally failed package 10a215 with
"Package destination has no nearby authored navigation floor." The owned marker
10a1fc sits 154 game units above NAVM; the old six-body-radius distance gate
incorrectly included that downward projection. Runtime projection now permits
an elevated source marker to resolve downward while retaining lateral and
upward proximity bounds. Physical route refinement and collision remain intact.
Synthetic projection/navigation checks and Debug build pass.

Ordinary replay now accepts the choice, moves Sunny from the first to second
well, and completes her actual travel package (position approximately
[-939.489, 106.688, 200.442] metres). F5 after accepting the choice completes.
Private successful checkpoint: local/fnv-wells-fixed/marker-resume.json.
Following the player into the second-well area then exposes a separate save
blocker: references 10a72b and 1629a7 have unsupported Sandbox packages with
native-transform-unattached capture predicates. That later F5 fails. Private
state: local/fnv-wells-fixed/marker-live/0000000013.state.json. Do not substitute
that failed request for the successful checkpoint. Sunny's completed route is
in state 0000000009.state.json in the same directory.

The live session exited cleanly. The full runtime gate and diff check pass.
Publish this block through a checked PR, merge and synchronize main, then
repair the separate prepared-ahead NPC ownership failure. No PR exists yet.

## Next gameplay owner

The new save blocker comes from prepared-ahead exterior NPCs: staging calls
PlaceNativeReference, which configures AI and installs shared capture callbacks
before the node enters the tree. The area can remain prepared indefinitely.
State 13 shows preparedAhead=true, no pending uploads and unattached callbacks
for both affected references. Defer gameplay binding until actual publication;
do not bypass the capture predicate or erase unsupported Sandbox diagnostics.

Continue from the successful accepted-lesson checkpoint, verify cold completed
travel and resolve the new streamed-NPC save blocker. Then finish the remaining
wells, gathering and campfire cooking. General Sandbox selection and execution
remain unimplemented for NPCs and creatures. The first-well fight works despite
that ambient failure; it does not establish full combat or population support.

Keep remaining failures visible in docs/status.md and docs/implementation-plan.md.
Container inventory transferred the meat correctly; only the first few player
rows were visible and scrolling still needs review. Bottles remain intact after
hits; impact response and retail firing are unverified. No full tutorial, campaign
or retail parity completion is claimed. Recording remained off; temporary
screenshots and raw frame companions were removed.

## Preserved separate work

FO1/FO2 work remains separate. PR #242 (codex/classic-script-audio) is unrelated
and must be preserved. Physical FNV VR package g remains rejected by the user.
