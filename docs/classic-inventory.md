# Classic inventory and equipment

Both campaigns share `ClassicItemDefinitions`, `ClassicInventory` and the native
inventory screens. Owned MAP/PRO records determine identity, quantities, weight,
size, equipment and ammunition. Donor NIF/DDS/KF resources provide appearance.
Neither campaign is complete.

## Implemented

The inventory ledger retains original map hashes and serials, stack IDs, nested
owners, ground map/elevation/hex, instance payloads, hand slots and worn armor.
Partial transfers, arbitrary deposits, Take All, dropping, pickup and portable
containers share that ledger. Size and weight limits reject transfers; failed
ownership changes roll back. Containers cannot own themselves or form cycles.

Ammo quantities are rounds: full source packs plus the final object's charges,
including authored MAP objects containing more than one box's rounds. Unload
returns the magazine's actual type and count with its source weapon provenance.
Reload checks caliber and capacity and forbids mixing ammo types in a partial
magazine. Saves conserve rounds across loose ammunition and loaded weapons.
These exploration actions do not yet have a combat AP owner.

Native save v4 preserves the complete ledger, equipment, campaign-start
identity and source door state. Native v1-v3 saves migrate; richer older-runtime saves stay adjacent rather than losing unsupported
state. Changed source hashes, duplicated stacks, invalid ownership and created
ammunition are rejected. The seven item PRO kinds decode directly; Fallout 2's
real item PRO 255 takes precedence over the older unallocated-marker rule.

Fallout 2's campaign start now reads live INT/GAM bytes. The native INT reader
retains module variables, procedure order, aliased bodies and source offsets,
then passes startup and the header's map-enter procedure to the existing C# VM.
The resulting inventory, arrival and light come from those instructions.
Created items use original PRO defaults and the same inventory ledger as MAP
items. Cold restoration recomputes the source baseline, verifies its identity
and applies saved changes; dropping the initial spear cannot grant another.
Older native exploration saves retain their position and MAP loot while their
missing initial source inventory is introduced once. FO1 engine starting grants,
object-script initialization and ongoing script events are still unbound.

## Native controls and graphics

`I` opens INVBOX. Select an item and click a hand or armor slot to equip it;
click the same slot again to unequip. `B` switches hands; `R` reloads. Inventory
also provides Reload, Unload, Drop and Open. Original MOVEMULT art hosts typed
quantities, increment/decrement, All, Done and Cancel inside the game viewport.
LOOT retains source names and icons; carried containers open a nested loot panel.

`F4`, View, or the mode button above an inventory/quantity panel switches the
same world representation. It covers every item row, both hand slots, armor,
the live equipped paper doll, loot portrait, quantity picker and HUD weapon.
The switch continues working while gameplay is paused. Original mode reads the
campaign's FRMs. 3D mode uses the same source-PID-bound owned meshes as ground
items; unavailable models show their name and an explicit `3D unavailable`
label. The original interface frames, names, counts and controls remain.

The paper doll assembles the current saved character, source armor art family
and actual held equipment, with independent visual idle playback. It does not
change inventory, clocks, position or combat state. Its camera measures posed
skin and attached equipment. Transient antialiased render targets follow UI
resolution and close with the screen. Static models render on demand; animated
models get separate live controller bindings when appearing in several slots.

Source weapon/armor art controls the player's FRM family and idle/walk selection.
Held candidates include the spear, knife/throwing/combat knife and 10mm pistol.
Inventory and ground bindings additionally include chems, ammunition, keys,
books, power armor, explosives, miniguns, launchers and energy weapons. Held and dropped
inventory meshes share physical units. Missing analogs remain reported, with
original art available through the shared toggle. Physical dropped items center
on the source hex without the pickup FRM's presentation offset. Vault-cloth dye is restricted
to blue fabric, preserving gold trim, skin, boots, alpha, normals and UVs.

Bones is a source item/container and now participates in 3D prop admission and
visible-mesh picking. Its skeleton donor lies on the floor with blue cloth.
This is a candidate, not an accepted source pose or likeness match.

## Verification and remaining work

The development lab command `classic-item-systems <owned install> <fallout-1|fallout-2>`
runs seven synthetic PRO layout/identity/truncation contracts and decodes all
242 FO1 / 531 FO2 item prototypes. Separate owned fixtures exercise source
containers, partial transfers, cold saves, dropped items/magazines, equipment/AC,
hand switching, unload/reload, one-round stacks, incompatible ammo, nested bags,
self/cycle rejection and container volume limits. They do not represent campaign
travel or normal acquisition of their fixture equipment.

`classic-campaign-start <FO2 install> runtime/config/classic-retail-random-fo2-1.02-v1.json`
exercises all three original FO2 premades through the actual native startup
owner. It verifies the source spear, arrival/light, drop and cold restoration,
pickup/equipment, absence of duplicate grants, changed-source rejection and
exploration-save migration. A synthetic INT override changes the granted PID
and quantity and reaches the same real inventory. Nonzero module initializers
and truncated INT input are checked independently of the presentation.

Ordinary FO1 input reached and looted Bones, equipped its knife, selected a
six-round drop through MOVEMULT and cold-restored 18 carried / 6 ground rounds.
Clicking the restored 3D ammo box recovered all six; a further Bones deposit and
Take All round trip retained exactly 24 rounds, saved with the knife equipped. Both
campaigns' current original inventory screens were inspected in the native main
scene. Selected diagnostics are private under `local/classic-runtime-video/item-systems-*`.
Debug/Release builds and native Godot project loading pass. The full repository
gate stops at the existing furniture/gallery authoring-script ban; that gate has
not been weakened. No push or parity claim.

Both native main scenes were checked with F4 and the visible mode button while
inventory/quantity dialogs were open. The 3D paper doll updates on equip and
unequip. FO1 retains its knife and 24 rounds; FO2 retains the source-granted
spear after drop, cold restore, pickup and equipment. The current private
72-second recording is `local/classic-runtime-video/fallout-1-2-inventory-3d-toggle.mp4`;
its JSON identifies both chapters and the bounded interaction scope.

`res://tools/ClassicInventoryModelAudit/ClassicInventoryModelAudit.tscn` takes
the classic installation, campaign and owned FNV Data directory. It exercises
actual PRO/FRM-to-mesh admission and repeated live instances. All selected 98/168
FO1/FO2 item identities load, using 72/97 distinct donor models. This is not a
complete asset or likeness gate. Embedded loose-armor skins use source bone
palettes and independently published node poses. Empty blast markers retain
their range bytes; staged descendants still fail closed. The direct-visibility
owner handles nodes and particles, including the lit dynamite fuse. Inactive
transform channels retain authored poses. Constant/stepped boolean contracts,
skin palette/stitch contracts and native repeated-instance checks pass.
Dormant refraction parameters cannot enable a shader variant; actual refraction
flags remain unsupported. Shared scene duplication cannot replace C# animation
binding. Prototype collision shapes are released with their unused physics bodies.

An optional fourth argument writes one private native component PNG; a fifth
selects up to eight comma-separated source PIDs. It uses the same inventory
renderer without creating campaign items. Current selected comparisons are
`local/classic-runtime-video/inventory-item-component.png` and
`local/classic-runtime-video/inventory-items-expanded.png`. Loose armor, minigun,
laser rifle, consumables, sniper rifle, lit dynamite, GECK, plants, hide and Bozar
were inspected there. Their source/donor silhouettes differ; those images are
renderer checks, not final art acceptance.

FO1 starting grants, object/world script interactions, drug use/timers,
keys/locks, theft/barter, full corpse interactions, armor perks/damage effects,
combat, quests and dialogue remain required. Most item/actor analogs remain
missing. Narg's existing native save now carries the actual source starting
spear, equipped through INVBOX and visible in his 3D hand. Its new-game arrival
is source hex 17488; migrating an existing exploration save keeps its position.
The world clock, modded calendar overrides and complete map-event interleaving
are not implemented by this startup connection.

Field-order notes were checked against owned bytes and the
[sfall SDK declarations](https://github.com/sfall-team/sfall/blob/master/sfall/FalloutEngine/Structs.h).
These are format contracts, not imported implementation or gameplay authority.
The stock FO2 clock query convention was checked against
[the CE script-time reference](https://github.com/alexbatalov/fallout2-ce/blob/main/src/scripts.cc);
only the initialization values are bound here, not a running clock or matched timing.
