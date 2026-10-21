# Current work

Updated 2026-10-20. User priority is **fully playable flat Fallout: New Vegas**.
Root works alone and owns code, builds, input and Git. Work is active on
`codex/fnv-flat-gameplay`, based on `origin/main` at `e9ce032e`.

## Current owner

Repair the ordinary New Game route: Doc Mitchell's player/head positioning and
questionnaire, physical object handling including the tutorial skull, Sunny's
shooting and creature encounters, gathering and campfire cooking, then campaign
progression through Primm/ED-E and the Strip. These are gameplay requirements;
existing component passes do not close the user's reported failures.

The current change restores the missing flat Grab input to the playable player.
It applies bounded forces to the actual owned NIF dynamic body, retains collision
and uses the winning walking/running weight limits. An owned-skull fixture passes
movement, release/gravity, solid-obstacle, modal/teleport release, weight/frozen
refusal and source-body retirement checks. The full runtime gate and diff check
pass. Ordinary room placement and physics save/cold continuation remain unverified.

A fresh ordinary-input New Game reaches Doc's free-movement/Vigor stage 55 and
successfully saves. The initial stage 0-to-90 jump belongs to the source intro,
not an engine defect. The broken-SMG script has an extra source and compiled
`endif`; the interpreter rejects it. Doc's seated questionnaire/head positioning,
physical interactions and tutorial/campaign progression remain open.
Recording is off during development. Use specific temporary captures only when
needed to inspect a visual defect, and remove them after inspection.

## Remaining work

Fix the shared source-driven owners reached during ordinary play. Creature
attacks must acquire, approach, animate, hit or miss, damage and recover correctly.
Tutorials must perform their real inventory, quest and world effects, including
crafting and physical interactions, with save and cold continuation. Continue
through the full requested route and expand to remaining campaign behavior;
the entire flat game remains incomplete until it actually works.

Preserved FNV defects are in [status.md](status.md) and
[implementation-plan.md](implementation-plan.md). Rendering, population, combat,
scripts, collision, audio and persistence still have unresolved failures. No
campaign or 1:1 retail parity completion is claimed.

## Preserved work

FO1/FO2 work is preserved. PR #242 (`codex/classic-script-audio`) remains separate
from this FNV branch; its hosted checks pass. Classic script audio details and
remaining campaign owners are in [classic-audio.md](classic-audio.md),
[classic-fallout-plan.md](classic-fallout-plan.md),
[classic-script-events.md](classic-script-events.md) and
[classic-interactions.md](classic-interactions.md). No classic full playthrough
qualifies. Package g remains rejected for physical VR use.
