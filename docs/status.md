# Product status

OpenNV is in active gameplay repair, not whole-game acceptance. The target is
native 3840x2160 at sustained 60 FPS, with retail behavior, presentation and
persistent gameplay. Flat and XR share authoritative state; physical XR still
requires separate acceptance. The user's physical FNV VR playtest remains failed.

PR342 (`aa4450b1`) merged after the full local gate, selected owned audits and
all five hosted checks passed. Local main equals origin/main. The next branch,
`codex/npc-animation-and-placed-fire`, contains reviewed NPC, animation and
placed-fire repairs; its final local gate passes, and selected live Boulder save/cold and barrel-fire checks pass. Whole-scene acceptance remains incomplete.
See [current-work.md](current-work.md) for the current build and next owner,
[implementation-plan.md](implementation-plan.md) for delivery order and
[runtime-capability-map.md](runtime-capability-map.md) for capability gaps.

## Current gameplay evidence

| Area | Verified scope and remaining work |
| --- | --- |
| Save/load | Selected crafting and Follow state survived F5 and a fresh-process load. The new Boulder build now completes ordinary F5 after empty-Sandbox and Dialogue-package repairs; a fresh-process load restores the exact position and both repaired NPCs without their prior errors. Generic furniture, food search and other cross-cell procedures remain blockers elsewhere. |
| NPCs and creatures | Source actor placement, selected combat/routines and Follow/Accompany work in checked cases. Ten distinct NPC capture blockers were recorded across four regional snapshots. No complete missing-NPC census or all-routine acceptance exists. |
| Combat and VATS | Selected weapon changes, queued attacks, ammo/AP spending, damage and persistence work. Two torso-selected VATS shots physically hit the head; hit-part and broader weapon/creature behavior remain open. Earlier ordinary gecko pursuit, melee and player death have evidence. |
| Crafting and repair | Ordinary ammo breakdown, repair-kit use and a two-shell quantity batch have persistence evidence. All recipes, workbenches, skill/perk requirements and repair variants are not accepted. |
| Quests and dialogue | The source-driven opening and selected dialogue/tutorial paths execute. All quest branches, faction outcomes and campaign continuation remain unverified. Compiled SCDA execution is still missing; source-text execution does not establish compiled-script support. |
| Travel and rooms | Ordinary McCarran and Freeside transitions and selected outdoor walks pass. Freeside additionally crosses its next CELL boundary after the geometry-free LAND repair. Both games enter Vault 11 through the ordinary door; its interior exposes missing joints/effects and mantises reaching unowned furniture behavior; native selection/failure semantics are being qualified. The inner control pod also exposes a shared ACTI default-activation failure. Source arrivals are diagnostic placement, not ordinary travel proof. |
| Fire and lighting | Two Boulder barrel references now render source flames/smoke in a paired close view. Brightness differs and persistent fire sounds remain missing. Three other lamps still reject controller chains. Selected cinematic channel mapping and pulse/slow-pulse repairs are integrated. |
| Models and vegetation | Grass renders, and stationary grazing no longer translates the body in selected samples. SpeedTree shrubs remain missing: 582 reference failures in three regional captures. Those captures also contain 32 ragdoll-joint failures, seven shader-semantic failures and two door-controller failures. Counts describe captured instances, not unique implementation bugs. |
| UI and Pip-Boy | HUD/compass/map icons, CRT, light-state persistence and source crafting controls have repairs. Local-map rendering, exact placement/pixels and complete control/animation behavior remain open. |
| Audio | Selected source sounds and voices play. Complete radio scheduling, mix, timing, looping emitters and cell-retirement behavior are not accepted. |
| Interaction systems | Bounded terminal, lockpicking, hacking, inventory and skill owners exist. All source branches, player interactions and persistence variants still require ordinary gameplay verification. An ordinary complete Caravan match remains unaccepted. |
| Performance | Actual 4K rendering is verified. A six-minute moving run averaged 51.21 FPS; a later selected walk measured 55.31 versus retail 59.89 FPS. A short 59.70/60.13 result is not sustained 4K60 acceptance. |
| XR | Desktop/simulator checks do not establish headset playability. Hand/weapon presentation, input, stereo/lifecycle and physical 144 Hz acceptance remain open. |
| Other campaigns | FO3 previously rendered New Game through birth stage 22; no standalone FO3 release acceptance. Classic work is preserved but parked. TTW and mod behavior remain in the objective, with acceptance dependent on shared systems and actual execution. |

## Coverage boundary

The selected owned-stack scope inventory contains 320 map markers, 492 interiors,
1,378 directed door links and 30 source-named vault cells. Static decoding and
connectivity are not completed visits. The private playability ledger separates
18 work areas into known failures, working selected cases and unverified scope;
there is no defensible whole-game completion percentage.

No campaign completion, complete population/weapon support, retail pixel parity,
sustained 4K60 or physical-headset acceptance is claimed. Unsupported behavior
and telemetry loss stay visible. Owned inputs, saves and captures remain private;
source assets remain read-only and are never distributed with OpenNV.
