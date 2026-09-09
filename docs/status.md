# Product status

The current user priority is the complete visible VR body with head-only eye
exclusion, reactive gameplay control and a single-eye recording. Preserve physical
hands, weapon handling, measured performance, the trip to the Strip and universal cell repairs;
see [current work](current-work.md) and [the cell workflow](cell-parity-review.md).
Source hand/weapon sweeps, dynamic prop pushing, two-hand support and live wrist
inventory have bounded native/SIM checks. Reach release and source twist-bone
handling are implemented. Low-crouch sleeve presentation, solid actor response,
physical melee, petting and full combat remain unfinished. Player animation
sampling/reduction reuses storage and sharply reduces measured allocations.
No headset or complete cell acceptance is claimed. Classic recovery is paused; see the
[delivery plan](classic-fallout-plan.md), [diorama direction](classic-world-realization.md)
and [current work](current-work.md). FO1/FO2 share native owned MAP/PRO/FRM loading,
source hex movement, character selection, original HUDs, independent saves and
source exit-grid transitions. The launcher preserves both classic games and both
donor libraries. Neither campaign is complete.

The diorama uses corrected floor/hex projection, joined cave and metal walls,
original wall/furniture surface paint, twenty authored furniture models, and
owned 3D props, humans and creatures. World, inventory items, equipment, paper
doll, loot/quantity panels and HUD weapons switch together, even while paused;
live side-by-side cameras share the same world. Source tribal hair/proportions,
the actual temple guard's spear/grip/shadow, 3D idle clocks, tree-family bindings
and layered EDG soil are implemented. Individual likeness, other equipment and
action states, remaining walls/props, temple paving and the Vault 13 cinematic
portal still need work. See [actor recovery](classic-actor-recovery.md) and
[scenery authoring](classic-scenery-authoring.md).

The shared [item owner](classic-inventory.md) supports partial transfers,
portable nested containers, Take All, drop/pickup, hand/armor slots, unload/reload
and cold restoration. The original MOVEMULT quantity panel, INVBOX, LOOT and
campaign HUDs are live. FO1 Bones now has a visible 3D skeleton and geometry
picking; native input equipped its knife, dropped six rounds, cold-restored and
picked up the 3D ammunition, then completed a deposit/Take All round trip.
Owned fixtures in both games cover source containers, magazines, equipment and
nested bags; they do not prove normal FO2 acquisition or campaign travel.
FO2 now executes its owned campaign header at startup through the shared INT VM:
source-created starting equipment, entry hex and light are connected. Narg's
source spear is present in INVBOX and equipped in the 3D world. Cold restore
retains the grant and later item changes without issuing another spear.
FO1 starting grants, object/world scripts, item use, full corpse looting,
Pip-Boy, combat, dialogue, quests and persistent world execution remain required.
Native saves migrate to v4 while preserving richer older saves. The shared
[door owner](classic-interactions.md) connects ordinary pointer interaction,
source initialization, lock state, FRM animation and dynamic hex blocking.
Door use/examination displays owned MSG responses and original descriptions;
the HUD has wrapped, scrollable message history. Saved INT message handles
retain their source identity on cold load. Trapped-door skill/stat checks and
floating messages remain unbound.
Its loading check covers all 227 source maps, while 199 door initializers and
general scripted door use still have unbound behavior. DAT1 dictionary reset
and stored-block length corrections restore corrupted FO1 source resources;
verified recovery preserves pre-fix player/item state and an original save backup.
The 3D paper doll uses current identity/outfit/held gear and visual idle. Item
previews share the world's donor bindings, with transient antialiased views.
Missing analogs remain explicit. The selected native item-model check admits
98 FO1 and 168 FO2 item identities, including embedded loose-armor skins, plastic
explosives, holodisks, plants/hides and larger weapons. Many other items remain
unmapped; model shape and palette still need refinement. Inventory models do not establish
held animation or combat coverage. A new private 72-second native recording at
local/classic-runtime-video/fallout-1-2-inventory-3d-toggle.mp4 shows both games'
switches, original HUDs, quantity UI, Bones portrait and live spear equipment.

The private catalog spans 227 stored maps / 338 elevations; candidate counts do
not accept geometry or campaigns. Requested native footage shows bounded real
movement, inventory, actors and labeled map-browser inspection. No matched
retail visual parity or campaign completion is claimed. Repository validation is
tracked separately in current-work.md.

OpenNV remains unfinished. All 36 requirements in recovery-checklist.md are open.
The objective and implementation authority are in implementation-plan.md; current
commands, measured results and next owners are in current-work.md.

The development lab now scans the full loaded plugin corpus and BSA directories,
replays reference events, and exercises reference-state teardown/restoration for
arbitrary or all winning cells. Shared reference locals, filtered event parsing,
winning-source save checks and cell-child indexes have synthetic and selected
owned-data proof. These results establish bounded headless behavior, not complete
cell, asset, campaign or parity support. The corrected all-cell reference sweep
passes all 44,517 winning CELL records for loading, repeated teardown and cold
reference state. A shared duplicate-local reader fix removes false padding
conflicts in seven owned scripts; source-body and gameplay failures remain
visible. Source-bound per-cell reports group resident reference/actor failures
and preserve original ancestry. The starting Goodsprings scope now has 12
failure groups after a dormant texture-mask fix restores the gas-station model.
The building/sign were inspected after ordinary walking. A new adjoining-cell
save cold-restores position, look pitch and unchanged inventory. Both games'
ten-plugin order is verified against retail's live array; NAM sidecars explain
DLC activation absent from plugins.txt. Full cell parity remains unverified.

The ordinary New Vegas opening now reaches stage 200 through the uninterrupted
intro, character creation, occupied couch, questionnaire and farewell. E on Doc's
front door reaches grounded Goodsprings; its return door and cold Continue work,
including cold restoration directly outside with the collected loot.
Original HUD tiles show the crosshair, E interaction prompts, HP and AP. Ordinary
loose-item pickup, container withdrawal/deposit, Take All and cold restoration
of the changed inventory and container contents pass. The bathroom door opens
and closes using its source animation, and the player walked through it with
collision enabled. These are bounded ordinary results, not clean campaign parity.

The current working tree replaces dedicated Vigor/farewell callbacks with shared
reference activation/contact dispatch. Synthetic contracts, owned scripts in two
cells and a native Godot contact audit pass. This connects ordinary entry points;
matched retail comparison remains outstanding.
Owned-data execution now covers the 14-choice questionnaire, source result
effects, follow-ups and a native source-menu input audit. Couch furniture and
posed actor contacts execute the source trigger chain in a disposable fixture.
Per-instance texture and reference enable owners have native component proof.
Native fade components and the complete source farewell logic now pass selected
owned audits. Five farewell INFOs grant real items, reset Pip-Boy state, accept
the hardcore answer and clear the door flag; the original timer completes VCG01
and starts the subsequent quest stages. These are separate headless results.
Complete Pip-Boy actions, original tag/trait screens, post-farewell sandbox AI and smoking,
active interaction saves and matched presentation remain open. Dialogue, barter,
combat, crafting and broader quest support are still incomplete. Containers lack
quantity selection, theft reactions and complete item scripts/sounds/filters.

Native CREA assembly now displays bighorners, ravens and Victor in the ordinary
exterior. Four creature families pass independent source idle, material-channel
and cold-continuation checks. Their AI, travel, attacks and speech faces remain
unfinished. Voice resolution now preserves the actor's inherited voice type,
original INFO identity and response number across plugin overrides. Speaking
creatures and saved unconscious state have source bindings. Victor's ordinary
conversation completed five topics, proper returns and Goodbye with his own
response voice paths. Its source map/objective results survived cold restoration.
This does not establish other actors or all dialogue condition scopes.

The native Pip-Boy now renders the owned device on the animated arm with original
menu art and font. The inspected Stats figure's head/face/body align, and source
zoom/crop/clip rules preserve art and text. Physical page buttons work; selecting
9mm/10mm rows changes equipment, saves and restores the held model. The original
world map shows the player and source marker state; physical wheel zoom works.
F switches first/third person, and the wheel adjusts camera distance. The ordinary
third-person body is upright and grounded with source directional movement.
Eight saved weapons plus unarmed state pass both-view assembly, movement/jump
binding and attachment checks. These are bounded results, not complete gameplay
or matched visual parity. Source draw/reload and magazine-state owners are bound.
Semi-automatic hitscan firing now uses the source attack Hit event and posed
weapon node, consumes ammunition and preserves magazine/recovery state across
cold saves. Actual simulator trigger checks cover empty/held input and wrist or
tracking interlocks. The 9mm's own stereo fire sound reaches the playback API.
Source muzzle effects now share a controller/particle clock, including short
emission windows across long host frames. Original shell casings eject with source
physics; actual collision materials select original impact models and sounds.
Transient meshes and particles inherit the exterior environment, and PROJ/LIGH
supplies the muzzle light. Simulator both-eye checks show flash and brass ejection;
the night flash illuminates the player and road. Exact impact pixels, decals,
addon audio, casing contact sounds and matched retail timing remain unfinished.
Ordinary flat input now shoots and kills a source-placed coyote, searches its
physical corpse and transfers its meat. Injury, corpse pose and inventory survive
save restoration; cold scene attachment has an additional regression check.
Ordinary SIM input also walked to the living coyote, equipped the pistol through
the live wrist menu, killed it with three trigger shots, searched the ragdoll and
saved its meat/hide in player inventory. Blood spray and brass are visible in
both eyes; exact retail timing and sound mix remain unaccepted. Limb destruction, living NPC damage,
combat AI, death events/XP, full inventory actions, radiation and radio/local
maps/fast travel remain unfinished.

The single tracked player/device owner now enlarges the whole Pip-Boy 2.25x and
rolls it around the anatomical forearm centerline during left grip. It stays
centered, restores on release and retains the original screen/button ray targets.
Repeated-turn and source-button audits pass; ordinary SIM input operates all three
pages with the world live. Physical feedback failed for hand/gun orientation,
cloud depth, populations, scale and reference rotation. Bounded corrections have
simulator/source checks, but physical appearance and comfort are not accepted.
XR now shows the full world outfit and hands while excluding source head parts
only from its eye cameras. Anatomical source eyes, a constrained spine, grounded
leg targets and independent torso heading replace the old head-following rest
body. First-person weapon/device attachments share the posed shoulder frame.
All 18 current source volumes pass
real physics queries through visibility/shadow changes and tracked head/wrist
poses. Ordinary flat and SIM checks show body shadows, retain contacts and shoot
past self volumes. New full-body SIM views show connected sleeves and hands.
Room-scale stepping, slope adaptation, broad outfit fitting and physical headset
acceptance remain incomplete. This does not establish player damage or full limb dynamics.
The Pip-Boy now displays computed SPECIAL and skill totals, including source tags,
conditional traits and worn apparel effects; ordinary scrolling and SIM pointing
pass. Other actor-value pools, effects and advancement remain incomplete.
The physical and simulator sessions are stopped with saves preserved.
The user requires real simulator death/dismemberment/looting before another headset
run; those gameplay owners remain required during the cell review. See
current-work.md for the checkpoint.
Optimized Release builds, reusable particle/casing resources and asynchronous
telemetry remain in place; cold loading and frame outliers still need work.
Debug/Release and selected owned/native checks pass. The required full repository
gate and publication status are tracked in current-work.md; the offline classic
helpers are preserved outside product inputs.

Dynamic item physics now moves the visible source mesh with its collision body;
owned cap/book audits verify independent cloned instances. Saved pickup state
prevents resurrection, but moved clutter poses are not saved. Exterior loading
uses source LAND layers and collision over a moving in-memory 25-cell grid.
Ordinary walking crossed between 0e1aa7 and 0e1aaf, retained overlapping objects,
saved, cold-continued in the new cell and walked back. Missing BTXT quadrant bases
now use the owned default landscape textures, fixing the GSHouseInterior02 exit.
Space jumps, Shift-W sprints, F5 saves, and low curbs have capsule-tested climbing.
Source atmosphere/cloud/star meshes and terrain/object LOD now render. The inspected
exterior has sky, roads and distant terrain; all 116 selected LOD blocks load in
the current run, with ten extra surfaces still missing their world material owner.
Those results do not accept weather, water, shadows, texture blending or pixels.
The legacy IMGS layout follows payload size, fixing incorrect color controls.
Raw source bytes and inactive LOD resources have bounded reuse. Repeated native
reference and compositor work is reduced, but loading still has upload spikes.
Full HUD compass/ammo, seamless door views/traversal and physical OpenXR remain
unverified or unimplemented. The complete runtime gate now passes with its
product-tree checks intact; offline authoring/gallery helpers remain private.

All Goodsprings cells and their connected progression, complete installed DLC
behavior, broader FO3/TTW support, cold gameplay continuation and physical OpenXR
acceptance remain unproven. Existing rendering, animation and audio corrections
retain their documented limitations. Exact independent evidence requirements
have not been reduced, and no acceptance row closes from counts or parser passes.
