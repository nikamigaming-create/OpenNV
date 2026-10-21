# Current work

Updated 2026-10-20. User priority is **fully playable flat Fallout: New Vegas**.
Root works alone and owns code, builds, input and Git. Work is active on
`codex/fnv-actor-lifecycle`, based on `origin/main` at `3c2a0405`.

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

PR #244 is merged with local and hosted runtime checks passing. Normal New Game
and cold Continue complete Vigor, the seated questionnaire, tag skills and traits,
exit the couch with E and save with F5. Dialogue follows the actual animated
speaker and furniture entry clears the activation pitch. Normal standing dialogue
also works: Doc's equipment handoff and Hardcore prompt reach stage 200, and the
real exit door loads Goodsprings and saves character creation complete. Dialogue
zoom, turn timing and matched retail framing remain open.

The shared font lifetime repair removes a loading tip's disposal of its borrowed
source-session atlas. A fresh ordinary Continue and exterior/interior/exterior
door cycle now retain HP/AP, reticle, activation text and message drawing, with
no disposed-font faults. F5 completes outside. Debug build, selected owned
gameplay checks and the full runtime gate pass.

PR #246 merged the checked HUD target recovery. Ordinary play reproduced the
linked-door error, then recovered Open on the saloon front door, entered with E,
recovered Sunny's Talk prompt and played her genuine greeting. Selecting her
training topic changed the source quest and sent Sunny through the actual exit
portal. This starts the tutorial; shooting, creatures and cooking remain open.
Linked-door effective lock behavior itself remains unimplemented.

Current repair: actor initialization, dialogue wait and cell retirement. Sunny's
source editor wait now uses native approach and activation-distance waiting.
Ordinary play opens the saloon, saves while she waits, cold-loads that wait and
walks into her voiced automatic greeting without pressing Talk. Normal Goodbye
then F5 commits again, with no pending procedure or corpse capture blockers.
Moving dialogue retains its native package pose and clock. Source-dead NPCs skip
living AI selection and EVP. Synthetic ownership and cold-state contracts pass;
the required full runtime gate passes. Publication is in progress.

The user also reports missing sweeping/farming idles, dialogue and broken Victor.
Live Goodsprings settlers select Sandbox PACK procedure 12, which the NPC runtime
rejects before any work behavior starts. Creature procedures 12/13 are also
unimplemented. The save failures reached through the saloon were traced to a
source corpse incorrectly selecting a living patrol, and detached preparation
diagnostics poisoning a different native owner's corpse capture status. Only the
bound owner can now change that status. Capture precedes cell teardown, deferred
corpse construction completes before retirement, and snapshots defer while real
corpse ownership is incomplete. Opaque or genuinely unready state still refuses
capture. Ordinary F5 and cold continuation now pass through the affected route.
Victor's live source selection is VGSVictorCircuitPatrol, refused by
the creature runtime's absent Patrol procedure. These source package owners are
the next implementation work.
The broken-SMG script has an extra source and compiled `endif`; the interpreter
rejects it. Physical prop persistence and tutorial/campaign progression remain
open.
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
