# Current work

Updated 2026-10-21. Priority: fully playable vanilla Fallout: New Vegas flat mode.
Root owns implementation, builds, input and Git without delegated agents. This
is greenfield development: prioritize working gameplay over old-save compatibility.
Owned retail inputs remain read-only. Follow docs/implementation-plan.md.

## Active block

Branch codex/fnv-terrain-impact-material, based on synchronized main 6e2f21cf.
PR #255 merged effective enable-parent state into shared combat. Ordinary second-
well play now produces gecko pursuit, damaging melee and settler/gecko combat.
The next reached firing failure was a blended LAND face without a unique material.

FalloutLandscapeImpactMaterial now selects a source vertex from the horizontal
impact point, then its strongest runtime texture weight. Shared player/NPC hit
resolution uses this owner. Terrain preparation retains the decoded weights in
process. The old per-face table remains diagnostic only. Private static analysis
supports point selection, corner tie order, snapped quadrant ownership, residual
base/normalized alpha weights and the six-runtime-slot selector bound. Global
cell-edge lookup and matched retail impact observations remain unverified.

Debug build and the plugin contract probe pass. The selected owned terrain audit
passes 44 uniform-sample and 240 blended-sample ray hits over 25 cells through
Godot's actual material dispatch. This is integration coverage, not retail parity.
The sixth-alpha-only source vertex exposed the old face-material audit oracle as
incorrect; the final synthetic slot-bound regression also passes in the full runtime gate.
The required runtime gate passes; exec session 21140 exited zero.
Log: local/fnv-wells-fixed/terrain-impact-gate.log. No terrain PR exists yet.

Ordinary approach produces two damaging rifle contacts and a reload before player
death. The save browser restores the checkpoint. Backward walking, reload and an
aimed shot hit LAND 0e26c0 with impact-resolved and no material/effect errors.
Receipt: local/fnv-wells-fixed/terrain-impact-live/0000000021.state.json.
Original LAND 0e25c7 triangle566 was not reproduced. Live session 42821 exited
cleanly. Recording stayed off; no capture files were created. No game is running.

## Next runtime owner and gameplay checkpoint

User explicitly requires idle townspeople, monsters, combat and quests to work
together. Both NPC and creature dispatch lack Sandbox (12). Live failures include
shared PACK 074fe6 (editor-location radius512), plus creature PACK 026ad5 (radius16)
with different authored flags. Private source record inspection is in
local/fnv-wells-fixed/sandbox-records.private.json. Private binary research is in
the Ghidrust workspace evidence/fnv-1.4.0.525/sandbox-20261021 directory. Only RTTI
and initial lifecycle functions are identified; no Sandbox behavioral contract or
implementation is established. Implement shared ambient execution with combat
interruption, quest-driven package changes and persistent continuation, rather
than special cases for individual actors.

Latest verified checkpoint: local/fnv-wells-fixed/prefetch-cold-resume.json, stage50
at the second well with Sunny's marker travel completed. First-well geckos are dead
and Gecko Meat was looted. NPC geometry-only prefetch is merged in PR #254; F5
before/after publication, separate-process Continue and another F5 pass. Unsupported
Sandbox remains visible. Creature staging was not changed by that PR.

After publishing the terrain repair, continue the general population owner and
ordinary remaining wells, gathering/campfire cooking, then Primm/ED-E and the Strip.
Inventory scrolling still needs ordinary review; bottles remain intact after shots.
No full tutorial, campaign, population or retail parity completion is claimed.
Physical FNV VR package g remains rejected. Preserve FO1/FO2 work and unrelated
PR #242. No other task PR is outstanding.
