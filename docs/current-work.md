# Current work

Updated 2026-10-21. Priority: fully playable vanilla Fallout: New Vegas flat mode.
Root owns implementation, builds, input and Git without delegated agents. This
is greenfield development: prioritize working gameplay over old-save compatibility.
Owned retail inputs remain read-only. Follow docs/implementation-plan.md.

## Active block

Branch codex/fnv-effective-combat-enablement, based on synchronized main e6485ad9.
PR #254 merged NPC geometry-only prefetch with local/hosted gates passing.
Ordinary F5 before and after NPC publication, separate-process Continue and another
F5 pass. No task PR remains open; unrelated classic PR #242 is preserved.

User emphasizes a general population/combat urgency problem. At the second well,
ordinary rifle hits damaged gecko 10a210 and selected the player as its target, but
its pursuit and hit-reaction clocks never advanced. Source ACRE 10a210 has an
initially-disabled local flag and XESP parent 11d9a5. The saved quest-enabled parent
is true while the child's local flag remains false. Rendering correctly follows
FalloutReferenceWorld.IsEnabled; five shared combat checks incorrectly used the
local flag. This affects NPCs and creatures, not just this encounter.

The runtime correction uses effective enablement for combat ticking, acquisition,
assistance, companion targeting, explosion eligibility and combat observation.
Diagnostics report effective and local enablement separately. Debug build passes.
Ordinary Continue and approach now produce gecko pursuit and repeated melee hits,
ending in player death. The settler and geckos also exchange source attacks.
Live state: local/fnv-wells-fixed/combat-enable-live/0000000004.state.json.
The game exited cleanly; its temporary death-screen capture was removed.
The existing owned retaliation check now enables the parent chain without changing
a child's local flag and checks enabled/disabled explosion eligibility. That check
passes for 10a210 against 10a20f, including a real source melee hit from 20 to 5 HP.
Log: local/fnv-wells-fixed/combat-enable-owned.log.

The full required gate passes (combat-enable-gate.log). Its initial attempt caught
four CRLF formatting violations; corrected line endings pass the complete rerun.
Review, publish through a checked PR and synchronize main. No PR exists for this
block yet. All changes are uncommitted.

## Live checkpoints and next gameplay owner

Latest verified checkpoint: local/fnv-wells-fixed/prefetch-cold-resume.json, stage50
at the second well with Sunny's marker travel completed. First-well geckos are
dead and Gecko Meat was looted. Resume this checkpoint for the combat correction.
The subsequent wells-next session exposed the combat bug. Its F5 failed with speech;
do not use it as a completed checkpoint. It exited cleanly and temporary screenshot
files were removed. Detailed combat state is wells-next/0000000016.state.json
under local/fnv-wells-fixed. No game is running. Recording stays off.

Next reached blocker: shot-impact material resolution reports "Hit triangle 566
has no unique source Havok material" and stops the input bot during combat. Trace
the actual collision mesh/material before changing it. The shot receipt identifies
LAND 0e25c7 LandscapeCollision, triangle 566. CollisionMaterials deliberately marks
mixed painted LAND material as unknown. Private research is in Ghidrust workspace
evidence/fnv-1.4.0.525/terrain-impact-20261021/notes.md. An existing decoded-code
capture matches this installation's executable hash, but no material selection
contract or dynamic retail verification has been established yet.
Continue remaining wells, gathering and campfire cooking, then Primm/ED-E and the
Strip. General NPC/creature Sandbox behavior remains missing. Creature staging was
not changed by PR #254. Inventory scrolling still needs ordinary review; bottles
remain intact after shots. No full tutorial, campaign or retail parity completion
is claimed. Physical FNV VR package g remains rejected. Preserve FO1/FO2 work and
unrelated PR #242.
