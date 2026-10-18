# Current work

Updated 2026-10-18. The user has redirected implementation to complete Fallout 1
and make its 3D art faithful to the original game. FO1 is the immediate priority.
Root alone owns code, builds, input and Git. Existing FNV work is preserved;
package g remains rejected for physical VR use.

## Current owner

Current owner: codex/fo1-source-art-fidelity, based on origin/main 448f71b0.
The first measured divergence is authored scenery fitting: projecting mesh
bounding boxes includes empty corners, shrinking and shifting irregular models
relative to their source FRMs. Static scenery now projects referenced mesh
vertices for fitting, art anchoring and paint alignment. Skin bounds retain
their existing behavior; donor stack fitting is a separate remaining owner.

The native synthetic check covers transformed indexed geometry, unused vertices
and hidden meshes. The owned check exercises 23 furniture bindings in all six
directions. At 32 pixels/metre the old fitting lost up to 31.64% of the intended
limiting extent; repaired extent error is below 0.000001 and center error below
0.001 source pixels. The audit now reads the production world scale from the
recipe. The production-scale audit passes all 138 fits, with maximum relative
extent error 0.000000358 and center error 0.000240 pixels. The full runtime gate
passes, including Release/Debug builds, formatting/analyzers, contract probes,
launcher checks and native Godot checks. `git diff --check` passes.
These are geometry-fit checks, not silhouette, material or final-pixel acceptance.

A separate source-only Vault 13 world audit stops at its obsolete
`SourceJoinedWallBlockout` lookup after constructing elevation 0. That audit does
not supply donor libraries and is not a full current-world visual check. Repair
its scene-owner assumptions before using it for world acceptance; do not treat
its missing-shell failure as a passing whole-map check.

## Next owners and acceptance

- Compare original FO1 art with the actual native 3D presentation, starting with
  Vault 13 furniture and cave props. Fix source-facing geometry, dimensions,
  projection, materials and animation; donor identity alone is not fidelity.
- Recover reproducible first-party furniture authoring. The GLBs exist, but the
  documented local Blender helper is absent from this checkout.
- Extend source-bound art coverage across all locations, keeping missing models
  visible. Original UI/portraits remain intentional 2D.
- Complete starting grants, combat, script events, dialogue, quests, world travel,
  timers, companions, endings and persistent world changes through ordinary play.
- Finish shared first-person/OpenXR views and physical acceptance. No campaign,
  1:1 art or headset completion is established.

See [classic-fallout-plan.md](classic-fallout-plan.md),
[classic-world-realization.md](classic-world-realization.md),
[classic-inventory.md](classic-inventory.md) and
[classic-interactions.md](classic-interactions.md). Older classic overview rows
are superseded by the more specific inventory/interaction evidence.

FNV's preserved failures and requirements remain in [status.md](status.md),
[implementation-plan.md](implementation-plan.md), [runtime-performance.md](runtime-performance.md)
and [vr-rig-diagnostics.md](vr-rig-diagnostics.md). Original owned files, saves
and recordings remain private and unchanged. Recording stays off except for
specific visual checks; remove new temporary frames after inspection. Do not
retry cleanup actions previously rejected by automatic approval review.
