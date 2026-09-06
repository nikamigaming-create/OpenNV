# Current work

## Objective

Implement the user's [durable plan](implementation-plan.md): general,
source-driven gameplay and an aggressive development lab, then complete the
opening, Pip-Boy, house exit, Easy Pete, Sunny's tutorial and every Goodsprings
interior/exterior cell. Verify persistence and shared flat/OpenXR behavior.
All installed DLC and winning plugin behavior remain in scope.

There are 36 open requirements in recovery-checklist.md; none has complete
acceptance evidence. R01-R33 are preserved. R34-R36 record the September 6
Goodsprings, generalization and laboratory-tool instructions.

## Implementation authority

The user explicitly permits replacing or removing existing first-party code,
tests and tools. This is greenfield work, with no requirement to retain the
current design. Inspect source evidence and decide independently. Previous
conclusions are starting points to check, not constraints on the next design.
Preserve architecture.md's product boundaries and unrelated user work.

Use main directly. One implementation task at a time; no subagents. The new
implementation task takes over after this documentation handoff is published.
The previous task must not keep editing the same checkout concurrently.

## Last verified runtime state

- Runtime code at 80c4db0 reached stage 80 by ordinary New, original creation
  and Vigor. Doc's source Look/speech/StopLook and later furniture approach,
  entry and occupation execute. The seated endpoint was visually checked;
  complete matched entry motion was not captured.
- Player couch activation, the original questionnaire, Pip-Boy handoff,
  ordinary exterior progression and Sunny remain unfinished. There is no
  ordinary OpenNV save at the stage-80 checkpoint.
- Shared corrections exist for source texture alpha/mips, face morph lighting,
  NIF placement, selected lighting/fog, script calculations/objectives and
  NPC animation. These are component evidence, not cell/game completion.
  See texture-alpha.md, morph-lighting.md, head-tracking.md,
  furniture-motion.md and scene-defects.md for their limitations.
- Inspection finds skipped model-less references, missing reference-instance
  script state, selected activation handling and replacement tag/trait/
  farewell panels. Recheck these and the surrounding system independently;
  do not assume this is a complete defect inventory or the only root cause.
- Both game processes and the harness were absent at the September 6 handoff
  check. Old session files are stale. Revalidate actual handles before input;
  do not wait on or reuse a dead process. No game has been relaunched during
  preparation of this plan.

## Next executable outcome

Read implementation-plan.md, inspect the current tree, and choose the smallest
complete shared gameplay change that removes the ordinary progression block
and demonstrates reuse elsewhere. Use development tools to load arbitrary
source cells/actors, reproduce failures and stress the real runtime. Do not
resume manual screenshot-by-screenshot scene construction or repeat the
cinematic. Complete a playable interaction and cold restoration; a new parser,
audit count or plausible still alone is insufficient.

Derive a Goodsprings scope/capability manifest from owned records and links.
Keep every unsupported object/behavior visible. Exercise unrelated instances,
source overrides and another cell before claiming general behavior. The new
task may replace the current architecture substantially to achieve this.

## Verification and publication

Before this handoff, main matched origin/main and there were no open PRs.
Revalidate before publication. The last published runtime has its selected
owned furniture audit and full gate evidence; they do not prove the new scope.
Run relevant owned-data checks and the full AGENTS.md gate before pushing
runtime or claim changes. Matched independent evidence is required for parity.

Owned inputs and private captures/observations remain outside the repository.
Use private Win32 Ghidrust observe-mode reads for retail and ordinary diagnostic
keyboard/mouse input. Lab state manipulation is allowed only in disposable
OpenNV tests and cannot establish ordinary gameplay or retail equivalence.
