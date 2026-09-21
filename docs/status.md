# Product status

Updated September 20, 2026 from current code and fresh tests. OpenNV is
experimental. Code, component checks, ordinary input, cold continuation,
simulator presentation and physical acceptance are separate evidence levels.
[Current work](current-work.md) identifies the exact candidate and active work.

## New Vegas

| System | Current implementation | Evidence and remaining work |
| --- | --- | --- |
| Launcher | Godot install/profile picker enters the existing coordinator in process. | Fresh Windows export and flat/simulator entry pass. XR is enabled experimentally; physical acceptance remains open. |
| Opening/dialogue | Reference events, quest/INFO effects, conversations and topic paging use shared C# owners. | Fresh owned opening/farewell and native questionnaire checks pass (14 choices, 18 responses). Prior ordinary stage-200 and Victor runs exist; broader dialogue, original tag/trait UI and physical XR remain open. |
| Inventory/Pip-Boy | Items, conditions, equipment, magazines, containers and flat/wrist views share state. | Fresh flat Stats/Items pixels and original illustrations were inspected. Inventory UI and cold-state component checks pass. Full item actions, quantity/theft behavior and physical readability remain incomplete. |
| Aid | Health and limb restoration, Medicine/Survival scaling, item consumption, sound and saved timed healing share a C# owner. | Synthetic and actual Stimpak normal/hardcore/cold checks pass. Fresh ordinary post-combat Use consumes one: flat HP 26 to 65; simulator wrist saved HP 94 to 133. Both takes exist. Radiation, scripted/addictive effects and other actor values remain open. |
| Companions | Creature Follow/Dialogue, persistent recruitment effects, embedded weapons, teammate combat targets and follower door transfer have shared owners. Actor MoveTo projects mobile actors onto source NAVM; route refinement uses their native capsule. | Ordinary flat and Elliott Tate simulator runs repair/recruit ED-E, exit Nash, kill and loot a hostile. Fresh repair checks place him on the indoor floor; a second encounter crosses the curb, kills the additional hostile and resumes Follow. The paired reel exists. Antenna appearance, Enhanced Sensors detection, NPC radio and exact AI/recharge cadence remain open; see [companion gameplay](companion-gameplay.md). |
| Barter | Merchant-container resolution, staged item/cap exchange and source pricing inputs are connected to ShowBarterMenu. | Fresh ordinary flat and Elliott Tate simulator dialogue/barter takes exist. Complete price modifiers, restocking and physical controller acceptance remain open. |
| Crafting | RCCT/RCPE readers, recipe gates and rollback-safe ingredient/output transactions connect to ShowRecipeMenu. | A normal flat reloading-bench activation and 9mm breakdown transaction were recorded and inspected. HasPerk conditions, controller use and cold restoration need further proof. |
| Weapons | Hitscan, automatic/multi-pellet, missile/lobber, beam/flame, attack events, spread/ammo/condition paths exist within explicit admission rules. Weapon animation completion no longer triggers a full campaign save. | A selected gun fires/reloads without per-shot save writes in ordinary flat and Elliott Tate input. Explicit saving and cross-mode cold Continue retain magazines, ammunition and shot random state. All-weapon gameplay, mines, timed/rotated throws, several explosion effects, condition-wear calibration and complete damage rules remain open. |
| NPC combat | Aggression/assistance, ranged/melee attacks and player/actor limb damage have shared owners. Player sight contacts, stationary turning, occluded pursuit and scenery-saturated friendly queries are corrected. | The source Fiend passes native blocked-route/dense-world checks and fires, reloads and damages the player in ordinary flat and Elliott Tate simulator gameplay; ED-E kills him and resumes Follow. Tactics, reactions, critical/sneak modifiers, hit/death script events and death XP remain unbound. |
| Death and loot | Source ragdolls, corpse inventories, saved severed limbs and lethal weapon limb selection exist. | Gecko combat/sever fixtures pass with Jolt. Damaged ED-E now initializes from authored XRGD and rests on the counter in flat and simulator views; cold-pose checks pass. XRGB root rotation and broad stability/combat remain open. |
| Travel | Source doors, a moving exterior grid, LAND/LOD and retained reference state are connected. Source-portal A* uses native player-capsule clearance and bounded replanning. | The flat bot completed the Primm approach and entered Nash Residence. Both modes repaired/recruited ED-E and completed the follower door exit and outdoor kill/loot sequence. Complete routes and streaming spikes remain open. |
| Actor admission | Persistent encounter-zone levels and per-reference leveled-template choices feed appearance, inventory, scripts, voice, factions and combat. Missing female ARMO models use the corresponding male model and materials. | Zone selection, spatial exterior ownership and cold-state contracts pass. The selected Primm grid plus 12 connected interiors passes admission/resource checks for 54 enabled actors; 48 source-disabled actors remain disabled. All 14 enabled Bison Steve NPCs pass preparation, with six upstairs NPCs present in the exported continuation. These are bounded checks; missing resources, scripts/packages and broader visible population remain open. |
| OpenXR | Tracked body/hands/weapons, contacts, support grip, wrist device and shared stereo menu surfaces exist. | Fresh headless player/contact checks pass. They are not simulator gameplay or physical acceptance. Room-scale fit, comfort and integrated controls remain open. |
| Audio/rendering | Source voices, animation, material/effect clocks, impacts and casings have runtime owners. Cloud rates include weather wind and winning GMST speed. Source-flagged rigid bodies receive exterior gusts. | All 98 weather declarations and selected native tumbleweed wake/clone/disable/warm-reentry checks pass. Ordinary flat/XR wind forces and changing poses are observed; one distant body falls below terrain and cold prop persistence remains open. CPU/memory-bounded workers, cached settings and shared weather constants improve the selected flat checkpoint to 60 FPS. Safe XR is about 45 FPS; separate rendering is faster but has a shutdown defect. Streamed NPC source work and native body assembly are staged; selected peak uploads are lower, but frame spikes, cell commits, distant water, broader alpha, sound mix and matched final pixels remain incomplete; see [performance](runtime-performance.md). |

The September 18 faction reel is a silent diagnostic scene preview. Source
placements and advancing idles do not establish dialogue, combat AI or travel.

## Other routes

FO1/FO2 retain owned DAT/MAP/PRO/FRM hex previews, source UI, item/equipment state,
independent saves and bounded 3D presentation. Campaigns, combat, scripts and
likeness are incomplete. FO3 retains its bounded Vault 101 route. TTW recognition
and JAM registration do not supply complete runtime semantics; those routes
remain unavailable.

## Acceptance

The [recovery checklist](recovery-checklist.md) preserves all 36 broad
requirements. All remain open at their full scope; this is not a whole-game
percentage or a statement that every component is absent. Historical specialized
notes describe contracts/investigations, not a current build pass.

Private logs, saves and footage stay out of the repository/release. The package
reads user-owned files in place. No campaign, all-weapon, physical-headset or
matched retail parity completion is claimed.
