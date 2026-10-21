# Current work

Updated 2026-10-20. User priority is **fully playable flat Fallout: New Vegas**.
Root works alone and owns code, builds, input and Git. Work is active on
`codex/fnv-dialogue-camera`, based on `origin/main` at `1a803001`.

## Current owner

Repair the ordinary New Game route: Doc Mitchell's player/head positioning and
questionnaire, physical object handling including the tutorial skull, Sunny's
shooting and creature encounters, gathering and campfire cooking, then campaign
progression through Primm/ED-E and the Strip. These are gameplay requirements;
existing component passes do not close the user's reported failures.

PR #243 is merged with local and hosted runtime checks passing. Flat Grab uses
the actual owned NIF dynamic body, collision and winning weight limits. Normal
Continue from v52 acquired the skull with Z, lifted it from the shelf and dropped
it onto the floor, delivering two source grab/release cycles. F5 saved in v53.
A fresh Continue restored the player and consumed event marks, but reset the
skull to its original shelf. Physical prop persistence is a confirmed open defect.

Normal New Game and cold Continue complete Vigor, the seated questionnaire,
tag skills and traits, reach stage 110, exit the couch with E and save with F5.
The shared flat dialogue camera now follows the actual animated speaker while
retaining the source camera position; furniture entry clears the old activation
pitch. Owned camera ownership/target/release checks, a fresh-process
questionnaire visual check and the full runtime gate pass. Dialogue
zoom, turn timing and matched retail framing remain open.

The broken-SMG script has an extra source and compiled `endif`; the interpreter
rejects it. Physical prop persistence and tutorial/campaign progression remain
open. After publishing this camera repair, the next owner is persistent
source-bound dynamic-body state.
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
