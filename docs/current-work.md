# Current work

## Active owner: exterior frame pacing and the Doc Mitchell tutorial

The user requests one maintained `main`, reliable exterior play and all authored
Doc Mitchell tutorials. Whole-game completion remains unverified.

The pending exterior/session PR retains immutable CELL definitions per source
stack, indexes resident references and avoids repeatedly attempting missing
presentations. Unsupported SpeedTree references remain explicit divergences.
Terrain shadow casting is retained. Dynamic bodies capture their final live
physics before teardown; an old saved snapshot is not a teardown substitute.

Selected owned checks pass for reference enable/fade/material/warm residency,
negative resolution invalidation, and skull/grenade physical continuation,
including freeing the body before the presentation owner. Full runtime gate
and checked publication are in progress.

Recording-free exterior reproduction uses a private copy of the Doc-exit save,
49 resident cells and Forward+ at 1280x720. The pending exported build measures
27-30 FPS with rolling draw median 36.45 ms and p95 65.57 ms. A separate 10-second
CPU sample attributes 4.84 seconds to creature native route search. These are
diagnostic observations, not a retail comparison or performance acceptance.

Next repairs are shared capsule-search budget boundaries and the source
tutorial's player-state queries. `CGTutorial` currently halts on player.GetDead
because the player reference is incorrectly treated as an ESM placed record.
Fix the authoritative query owner, execute the real source script, then check
ordinary New Game through Doc exit and exterior play with recording disabled.
Keep all reached unsupported owners visible, prove save/cold continuation, run
the owned audits and required gate, and merge each checked block into `main`.
