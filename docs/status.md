# Product status

Updated September 27, 2026 from current code and fresh tests. OpenNV is
experimental. Code, component checks, ordinary input, cold continuation,
simulator presentation and physical acceptance are separate evidence levels.
[Current work](current-work.md) identifies the exact candidate and active work.

The immediate direction is a functioning flat world, including autonomous
NPC/creature interactions and source routines. The September 27 work repairs
player-only combat acquisition/assistance, weaponless NPC attacks, squat
creature motion, compiled script-local admission and delayed quest death events.
It also adds partial-stack quantity selection, recipe ownership conditions,
live script distance/time queries and saved-clock package schedule selection.
Resident NPC/creature movement no longer requires a player object.
Nonfatal hits now select source hit-reaction IDLEs from limb condition and hit
location, play their KF, interrupt the current attack and return to combat.
Reaction phase and source identity survive v20 saves. Source NPC and gecko checks
cover moving poses, cold resumption and continued attacks without a player.
Fresh ordinary flat Continue restores the genuine Primm checkpoint, where an
unprovoked hostile fires at the player, ED-E responds and two patrols advance.
A prior parser regression no longer rejects four saved quest-script owners;
all 446 original owners retain their clocks, failures and quest progress.
These are bounded runtime improvements; complete resident routines, quests,
mod gameplay and matched retail behavior remain open. Follow the
[flat gameplay work order](flat-gameplay-plan.md).

Fat Man impact no longer scans whole ancestor child lists for every overlapping
shape. Direct destruction ownership and collider deduplication reduce one
repeated flat shot's detonation processing from 1,730 to 128 ms. Flight, contact,
effect construction and blast phases now expose separate timings. Rendering and
capture stalls remain; this is a selected measurement, not retail timing parity.

Source laser BeamEnd geometry now follows the resolved ray endpoint and PROJ
visibility duration. Normal and long-fuse dynamite bind their external KF
emitter channels; native input checks pass hold/release, one-item consumption,
physical flight and one timed detonation. The new simulator take shows the
laser hostile encounter and reachable two-hand support. Dynamite's separate
lighter attachment in VR remains broken. Enabling Jolt ray face indices and
preserving reserved decal bits repairs the two thrown-impact failures in native
checks. Ordinary flat knife, hatchet and spear throws each discharge once and
finish after real contacts without those errors. The new 44-second paired reel
includes dynamite, knife, hatchet and two-hand support, with faults captioned.
All-weapon and all-mod gameplay remain incomplete.

## New Vegas

| System | Current implementation | Evidence and remaining work |
| --- | --- | --- |
| Launcher | Searchable Godot game/mod library, additive mod checkboxes, folder/dependency setup, automatic ordering and optional overrides enter the existing coordinator in process. | Native checks pass at 1280x900 and 1060x700. All ten target sources and combined JAM/TTW/NMC open through the shared source owner. Mod gameplay remains gated; scripts and extension behavior are incomplete. See [mod compatibility](mod-compatibility.md). |
| Script extensions | Numeric NVSE expressions, scalar user functions, loops, per-script load/restart queries and frame/key callbacks share existing state owners. | Synthetic recursion/reload and native frame/mode/key-edge checks pass. Owned JAM initialization reaches specific UI, effect, perk and render-event gaps. Typed strings/arrays, complete MCM settings and mod gameplay remain incomplete; see [script runtime](nvse-script-runtime.md). |
| Session/save recovery | Shared pause, save browser, separate manual slots, preserved previous Continue and in-process main-menu return. Zero health enters a reload menu and blocks post-death saves. | Flat save/healing and Elliott Tate controller pause/load/save/title/healing operations pass. XR death UI was inspected in both eyes. Physical buttons/readability and retail death presentation remain open; see [recovery](patrol-session-recovery.md). |
| Opening/dialogue | Reference events, quest/INFO effects, conversations and topic paging use shared C# owners. | Fresh owned opening/farewell and native questionnaire checks pass (14 choices, 18 responses). Prior ordinary stage-200 and Victor runs exist; broader dialogue, original tag/trait UI and physical XR remain open. |
| Inventory/Pip-Boy | Items, conditions, equipment, magazines, containers and flat/wrist views share state. Container rows support the source quantity dialog and threshold. | Native checks pass partial transfer, cancel, stale choice, small stacks and cold counts; the dialog was rendered and inspected. Full item actions, theft, ammunition/currency transfer policy and physical readability remain incomplete. |
| Aid | Health and limb restoration, Medicine/Survival scaling, item consumption, sound and saved timed healing share a C# owner. | Synthetic and actual Stimpak normal/hardcore/cold checks pass. Fresh ordinary post-combat Use consumes one: flat HP 26 to 65; simulator wrist saved HP 94 to 133. Both takes exist. Radiation, scripted/addictive effects and other actor values remain open. |
| Companions | Creature Follow/Dialogue, persistent recruitment effects, embedded weapons, teammate combat targets and follower door transfer have shared owners. Actor MoveTo projects mobile actors onto source NAVM; route refinement uses their native capsule. | Ordinary flat and Elliott Tate simulator runs repair/recruit ED-E, exit Nash, kill and loot a hostile. Fresh repair checks place him on the indoor floor; a second encounter crosses the curb, kills the additional hostile and resumes Follow. The paired reel exists. Antenna appearance, Enhanced Sensors detection, NPC radio and exact AI/recharge cadence remain open; see [companion gameplay](companion-gameplay.md). |
| Barter | Merchant-container resolution, staged item/cap exchange and source pricing inputs are connected to ShowBarterMenu. | Fresh ordinary flat and Elliott Tate simulator dialogue/barter takes exist. Complete price modifiers, restocking and physical controller acceptance remain open. |
| Crafting | RCCT/RCPE readers, recipe conditions and atomic ingredient/output transactions connect to ShowRecipeMenu. Player note/perk/equipment conditions use live ownership, including explicit player targets. | A prior flat 9mm breakdown transaction exists. New checks reject missing/insufficient ingredients, aggregate duplicate requirements, roll back failed outputs, and evaluate all 127 selected source note/perk conditions. Complete recipe/quest chains, GetDeadCount, batch crafting, controller use and cold continuation remain open. |
| Weapons | Source Fire/Loop/Hold/Release and WEAP automatic cadence feed shared ammo/attack owners. Weapon and Aid wheels, rotating inventory throws, timed fuses, source grenade contact response, EXPL effects and DEST stages are connected. Destruction, knockdown and blast exposure persist in v21 saves. | Selected native checks pass wheels, held/released automatic fire, grenade fuse/particles and Fat Man/car destruction/cold state. Fresh flat/simulator footage shows carbine, laser, Fat Man, frag, Flamer, spear and cleaver actions. The car is a separate native fixture. Mines, bare fists, ammo variants, beam/flame/tracer completeness, full blast rules and all-weapon gameplay remain open. |
| NPC/creature combat | Aggression/assistance, ranged/melee/natural attacks, limb damage and source IDLE hit reactions have shared owners. Acquisition and assistance consider resident actors without a player dependency. | Native NPC/gecko reactions change the pose, resume cold and return to attacks. Source encounters pass initiation, retaliation and allied assistance without a player. Healthy-limb and wall negatives pass. Forced reactions, blending, target weighting, tactics, critical/sneak modifiers, hit events and death XP remain open. See [hit reactions](combat-hit-reactions.md). |
| Death and loot | Source ragdolls, corpse inventories, saved severed limbs, lethal weapon limb selection and delayed killer-filtered OnDeath script dispatch exist. Pending death timing survives v19 saves. | Native delayed death updates a quest once; synthetic cold continuation retains the delay and consumed event. Gecko combat/sever fixtures pass with Jolt. ED-E initializes from XRGD; cold-pose checks pass. Essential recovery, hit events, XP, XRGB root rotation and broad stability remain open. |
| Travel | Source doors, a moving exterior grid, LAND/LOD and retained reference state are connected. Source-portal A* uses native player-capsule clearance and bounded replanning. | The flat bot completed the Primm approach and entered Nash Residence. Both modes repaired/recruited ED-E and completed the follower door exit and outdoor kill/loot sequence. Complete routes and streaming spikes remain open. |
| Actor admission | Persistent encounter-zone levels and per-reference leveled-template choices feed appearance, inventory, scripts, voice, factions and combat. Missing female ARMO models use the corresponding male model and materials. Linked Patrol routes support the two elevated Primm riflemen. | The selected Primm grid plus 12 interiors passes admission/resource checks for 54 enabled actors; 48 disabled actors in that saved state remain disabled, including prior combat deaths. All 14 enabled hotel NPCs prepare. Both elevated riflemen reach their first markers, wait and proceed in native gameplay. Entire routes, missing resources, scripts/packages and broader population remain open. |
| OpenXR | Tracked body/hands/weapons, contacts, source support grip, wrist device and shared stereo surfaces exist. Held fingers no longer reopen when controls are untouched. The Flamer gun, shaderless helper and body pack now follow their source presentation roles. | Owned contact/grip/Flamer assembly checks pass, and corrected both-eye images plus selected simulator weapon input are recorded. The paired weapon captures remain choppy (about 19.7 distinct frames/sec flat and 12.9 simulator). Room-scale fit, comfort, physical controllers and integrated headset acceptance remain open. |
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
