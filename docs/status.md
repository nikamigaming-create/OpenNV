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
| Aid | Health and limb restoration, Medicine/Survival scaling, item consumption, sound and saved timed healing share a C# owner. | Synthetic and actual Stimpak normal/hardcore/cold checks pass. Actual flat and simulator wrist Use consume one item; the native check began at full health. Post-combat healing footage, radiation, scripted/addictive effects and other actor values remain open. |
| Companions | Creature Follow/Dialogue, persistent recruitment effects, embedded weapons, teammate combat targets and follower door transfer have shared owners. | Source recruitment results/cold state and native ED-E laser kill/combat-end fixtures pass. Ordinary flat and Elliott Tate simulator runs repair/recruit ED-E, exit Nash, let him kill a hostile, resume Follow and loot the corpse. The paired reel exists; raised-target interior height and antenna appearance still need work. Enhanced Sensors detection, NPC radio and exact AI/recharge cadence remain open; see [companion gameplay](companion-gameplay.md). |
| Barter | Merchant-container resolution, staged item/cap exchange and source pricing inputs are connected to ShowBarterMenu. | Fresh ordinary flat and Elliott Tate simulator dialogue/barter takes exist. Complete price modifiers, restocking and physical controller acceptance remain open. |
| Crafting | RCCT/RCPE readers, recipe gates and rollback-safe ingredient/output transactions connect to ShowRecipeMenu. | A normal flat reloading-bench activation and 9mm breakdown transaction were recorded and inspected. HasPerk conditions, controller use and cold restoration need further proof. |
| Weapons | Hitscan, automatic/multi-pellet, missile/lobber, beam/flame, attack events, spread/ammo/condition paths exist within explicit admission rules. | Owned admission is not all-weapon gameplay. Mines, timed/rotated throws, several explosion effects, condition-wear calibration and complete damage rules remain open. |
| NPC combat | Aggression/assistance, ranged/melee attacks and player/actor limb damage have shared owners. | New paths mostly have component proof. Tactics, reactions, critical/sneak modifiers, hit/death script events and death XP remain unbound. |
| Death and loot | Source ragdolls, corpse inventories, saved severed limbs and lethal weapon limb selection exist. | Gecko combat/sever fixtures pass with Jolt. Damaged ED-E now initializes from authored XRGD and rests on the counter in flat and simulator views; cold-pose checks pass. XRGB root rotation and broad stability/combat remain open. |
| Travel | Source doors, a moving exterior grid, LAND/LOD and retained reference state are connected. Source-portal A* uses native player-capsule clearance and bounded replanning. | The flat bot completed the Primm approach and entered Nash Residence. Both modes repaired/recruited ED-E and completed the follower door exit and outdoor kill/loot sequence. Complete routes and streaming spikes remain open. |
| Actor admission | Per-reference leveled-template choices now persist and feed appearance, inventory, scripts, voice, factions and combat. | Synthetic selection/save tests pass. The source appearance audit admits 6,861/7,681 references; that is not eligible-spawn or render completeness. Encounter-zone level policy, missing resources/outfits and native acceptance remain open. |
| OpenXR | Tracked body/hands/weapons, contacts, support grip, wrist device and shared stereo menu surfaces exist. | Fresh headless player/contact checks pass. They are not simulator gameplay or physical acceptance. Room-scale fit, comfort and integrated controls remain open. |
| Audio/rendering | Source voices, animation, material/effect clocks, impacts and casings have runtime owners. | Moving clouds and water-tower cutouts were inspected in simulator output. Lighting, broader alpha/LOD issues, sound mix, timing and matched final pixels remain incomplete. |

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
