# Current work

## Owner and objective

The November 3 user direction is two playable opening routes on one integrated
branch. Root owns integration, builds, ordinary game input and publication.

- **New Vegas:** normal New Game, Doc Mitchell and character creation, all
  Goodsprings tutorials, the first local quest resolution, then walk out of town.
- **Fallout 3:** normal New Game, complete Vault 101 childhood/tutorials and
  escape, then walk into Megaton. Its source residents must appear, run their
  behavior, and offer appropriate dialogue and interactions.

Both routes require working quests, inventory, combat and fresh-process save
continuation. Bots use the same controls as players. They may read objectives
and state; they may not force stages, spawn residents, teleport past the route
or edit saves. An empty destination or source actor count is not completion.

Only shared gameplay repairs needed by these routes are immediate work. Broad
weapon/mob sweeps, unrelated regions, benchmark campaigns, capture projects and
new harness infrastructure are deferred. Reuse existing bots and input tooling.
Recording stays off. Close bounded gameplay sessions before editing/building.

## Verified baseline and current blockers

The baseline is merged PR351 at adacd1a8f18f550e94083e58d4116b06d29d49aa;
local main and origin/main matched at inspection with no open PR. Existing
runtime-gate and selected owned resource/combat results are in status.md. They
do not establish either route. The current candidate has been exported to
`tmp/development-runtime/windows/OpenNV.exe`.

Fresh PR351 FNV New Game completed name, appearance, SPECIAL, questionnaire,
tag/trait choices and Doc's stage-200 handoff through ordinary bot input. A cold
Continue from its genuine stage-80 save resumed that progression. The bot then
activated the actual house exit into the Mojave. Ordinary F5 completed outside;
the active save is `local/opening-routes-active/fnv/courier-v1.json`. The second
session closed through Quit with exit zero, but actor teardown logged a retained
idle failure. The first bounded session required forced cleanup inside dialogue.
Recording stayed off. Sunny's tutorials and departure remain unverified here.

The candidate adds source recipe/quantity controls and unique objective-to-recipe
matching to the existing bot. Focused policy and owned native UI checks pass,
including quantity-one confirmation through the existing callbacks. Integrated
gameplay remains pending; a click is not tutorial completion.

Standalone FO3's current PR351 export exits before the opening menu: the owned
executable's hard system-color registrations are not admitted by the decoder.
The bounded November 3 run closed after five seconds with recording off. The
candidate admits the owned optimized hard-color factory through the shared
declaration reader. Synthetic corruption checks and the existing rendered owned
palette audit pass for both FO3 and FNV, with unchanged source bytes and no
retained frames. This does not prove New Game. Older birth CG00:22, Vault and TTW
observations do not certify this build. Vault exit, Megaton arrival and population
remain unaccepted. Use the normal owned launcher path; do not extend the old
birth-preview implementation.

Session replacement and quit now retire source actor bindings while the graph is
intact. Failures after retirement cannot resume or save that scene; Main Menu and
Quit remain available. The existing native menu component check passes. Actual
scene retirement and cold continuation still need the candidate executable.

The legacy combat checkpoint lacks inventory-script history. Do not backfill it.
Use normal New Game and keep current-build checkpoints reached through play.
Flat/XR share gameplay and saves; physical-headset acceptance remains separate.

## Next action and publication

One branch: `codex/playable-opening-routes`, based on the baseline above. Root
integrates recipe/quantity input, the FO3 palette reader and actor retirement
ordering. Owned component checks, Release export and the complete required
runtime gate pass, including Release/Debug, formatting/analyzers, contract probes,
launcher and native Godot checks. Candidate gameplay launches were rejected by the
execution environment, including a bounded attempt with cleanup; no new game
process was started. Restore permitted gameplay execution before claiming either
repair works in the opening route. Then resume FNV from its fresh exterior save
toward Sunny and rerun standalone FO3 New Game through the repaired menu to its
next actual blocker. Full finish criteria are in implementation-plan.md. Neither
complete opening route is accepted.

All five prior local feature branches and four remote feature branches were
already merged and are now removed. Only main and the active task branch remain.
Historical recovery stashes/tags are preserved. The apparent classic import changes
were confirmed byte-identical to HEAD; no content needs inclusion or discard.
Use one checked PR for the integrated work, merge after required checks, verify
local main equals origin/main and remove the merged task branch. No game was
running at inspection. Keep the PR in draft while candidate gameplay verification
is blocked. Private assets, saves and captures remain private.
