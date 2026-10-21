# Current work

Updated 2026-10-21. Priority: fully playable vanilla Fallout: New Vegas flat mode.
Root owns implementation, builds, input and Git without delegated agents. This
is greenfield development: prioritize working gameplay over old-save compatibility.
Owned retail inputs remain read-only. Follow docs/implementation-plan.md.

## Active block

Branch codex/fnv-prefetch-npc-ownership, based on main ae2a2364 after PR #253.
Local main was verified equal to origin/main. No task PR remains open; unrelated
classic PR #242 is preserved.

PR #253 merged the elevated-marker projection fix with local/hosted gates passing.
Sunny reaches the second well, completes travel, saves, cold-continues at the exact
completed pose and saves again. Both first-well geckos were killed by ordinary
rifle input, stage 50 reached, and Gecko Meat looted into inventory.

The next save failure was caused by exterior prefetch calling normal NPC placement
before publication. Detached prepared NPCs acquired AI and save callbacks and
could remain unattached indefinitely. Geometry-only NPC bodies now stay in a
separate preparation collection. Actual area publication calls normal placement,
rechecks current equipment/state, binds gameplay and attaches the actor. Cancelled
preparation frees the unbound bodies. This is an NPC staging fix; creature staging
and general Sandbox execution have not been changed.

Debug builds and the selected owned NPC preparation/cold-binding check pass.
Ordinary Continue and following Sunny reproduce preparedAhead=true with two
prepared NPC bodies; F5 now succeeds there. Ordinary walking across the cell
boundary publishes both 10a72b and 1629a7; they retain the visible unsupported
Sandbox error, have captureReady=true and F5 succeeds again. Latest successful
checkpoint: local/fnv-wells-fixed/prefetch-resume.json. Detailed published state:
local/fnv-wells-fixed/prefetch-live/0000000012.state.json. Earlier preparation
state is 0000000004.state.json. The session exited cleanly; recording stayed off.

The full runtime gate passes; log: local/fnv-wells-fixed/prefetch-gate.log.
Separate-process Continue restores both published NPCs at their saved positions,
with their unsupported Sandbox errors still visible and captureReady=true. Sunny
retains completed travel at the second well. Another ordinary F5 succeeds. Latest
checkpoint: local/fnv-wells-fixed/prefetch-cold-resume.json. Cold state:
local/fnv-wells-fixed/prefetch-cold/0000000002.state.json. The game exited cleanly.
Review and publish through a checked PR, merge and synchronize main. No PR exists
for this block yet.

## Next gameplay owner

Continue the remaining wells, gathering and campfire cooking, then Primm/ED-E
and the Strip. General NPC/creature Sandbox selection, movement, activity use,
occupancy and interruption remain absent. The first-well fight works independently
of that ambient failure; it does not establish full combat or population support.

Keep failures visible in docs/status.md and docs/implementation-plan.md. Inventory
scrolling still needs ordinary review; bottles remain intact after shots. No full
tutorial, campaign or retail parity completion is claimed. Physical FNV VR package
g remains rejected. FO1/FO2 work and unrelated PR #242 must be preserved.
