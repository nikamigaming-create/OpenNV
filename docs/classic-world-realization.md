# Fallout 1 — world realization plan

The owned installation contains **72 MAP files and 117 stored elevations**. This is an inventory, not a completion claim. The 43 principal-area maps contain 72 elevations; 23 encounter/special maps contain 39; six auxiliary maps contain six. Runtime map placement, exits, floors, roofs, characters and items remain owned by MAP/PRO/FRM and campaign state.

The three concept boards in `local/fo1-world-concepts` are the accepted visual direction: tactile dark caves, readable occupied rooms, aged materials and selective warm light. They are not exact source layouts. Their occasional generated signs and room arrangements are not source assets or instructions for level placement.

## Production sequence

1. **Every source level:** preserve exact source hex and elevation coordinates, asymmetric floor projection, exits, source inventory and object identity. Generate connected geometry around those coordinates.
2. **Reusable environment families:** finish cave, vault, adobe, scrap, urban ruin, sewer, industrial and cathedral surfaces. Apply the same capability to every matching source art binding across both classic games.
3. **Room contents:** choose a fitting model from the owned FNV and FO3 libraries per source FRM/prototype. Keep intact/broken variants, multi-part beds, doors, stairs and important signs distinct. Use the original art as source reference and suitable surface paint. A retained world sprite explicitly counts as unfinished 3D work; illustrated portraits and original interface art remain intentional 2D.
4. **Lighting and depth:** carry source placed-light radius/intensity into 3D, add shadow response to original floors, tune normal detail/contact occlusion and camera cutaways. Color/height/3D attenuation are explicit art adaptations until matched against reference.
5. **Missing geometry:** use original generated references through local ComfyUI/Trellis, clean and ground meshes in Blender, prepare material/LOD data, and bind only to the matching source object family. Generated candidates do not become campaign authority.
6. **Inhabitants and interaction:** bind source actor identity, outfit, animation, AI, dialogue and inventory; restore doors, stairs/ladders/elevators, inventory/Pip-Boy, combat and persistent transitions. Restore the six default character likenesses and custom portrait/body handoffs.
7. **Campaign completion:** quests, barter, companions, timed events, world travel, endings and complete save restoration must work through ordinary play. All map art and gameplay must be finished before claiming either campaign complete.

## Area worklist

| Area | MAP files / stored elevations | Rooms and scope | Art pass |
|---|---:|---|---|
| Vault 13 | 2 / 4 | Granite cave, gear door, vault corridors, living quarters and command center. | Cave geology and vault metal; source rats, terminals, lockers and bunks. Preserve the Vault 13 door's identity and clearance. |
| Vault 15 | 2 / 4 | Desert entrance, caverns and ruined vault levels. | Collapsed vault panels, rubble, exposed floors and rope access, driven by the actual source objects. |
| Shady Sands | 3 / 3 | Adobe homes, farming plots, pens and the radscorpion cave. | Adobe, timber doors, earthen floors, farm items and correct source inhabitants. |
| Junktown | 3 / 4 | Entrance/lab, casino and Crash House. | Scrap partitions, worn diner furniture, shop fittings, signage and source lights. |
| Raiders | 1 / 2 | Compound and underground rooms. | Occupied adobe, bedding, storage, firelight and raider equipment. |
| The Hub | 7 / 9 | Caravan entrance, downtown/hideout, Heights, Old Town/Thieves Circle, water merchants and lairs. | Street paving, trading counters, caravan clutter, wealthy interiors and distinct underground spaces. |
| Necropolis | 4 / 9 | Hall of the Dead, hotel, watershed, linked sewers and vault. | Decayed concrete, damaged motel furniture, pipes, pumps, sewage and source ghouls. |
| Brotherhood | 4 / 6 | Entrance, levels 1–4 and ruined state. | Maintained vault steel, armory and workshop equipment, bunks and power-armored personnel. |
| The Glow | 3 / 7 | Entrance and laboratory levels 1–6. | Scorched laboratory surfaces, damaged machinery and radiation-related presentation attached to real game state. |
| Boneyard | 5 / 8 | Adytum, Blades, library, Gun Runners, warehouse and underground rooms. | Urban brick/concrete, books, fabrication benches, industrial storage and source inhabitants. |
| Mariposa | 4 / 6 | Base entrance, stronghold, vats and ruined state. | Heavy industrial steel, blast doors, vats, catwalks and source mutants. |
| Cathedral / Master's Lair | 5 / 10 | Cathedral entrance/tower levels, destroyed state and lair levels 1–4. | Gothic stone, stained glass, pews and underground machinery. The art grouping does not override source route identity. |
| Encounters and special maps | 23 / 39 | Desert/coast/mountains/city, caravan encounter sets and special encounters. | Reuse the same material/prop families; retain special source objects and encounter logic. |
| Auxiliary maps | 6 / 6 | Files present in the owned installation without normal indexed campaign routes. | Keep them in the source browser; do not invent campaign links to them. |

## Complete source file and elevation inventory

Elevation numbers below are one-based for readability; the runtime retains zero-based MAP elevations. Level names are read from owned MAP.MSG where present. Some legacy area labels differ from the art grouping above (notably the Master's Lair); source identifiers remain unchanged.

| Art family | MAP file | Source map index | Stored levels |
|---|---|---:|---|
| Vault 13 | V13ENT.MAP | 35 | 1: Cave Entrance |
| Vault 13 | VAULT13.MAP | 6 | 1: Entrance; 2: Living Quarters; 3: Command Center |
| Vault 15 | VAULTENT.MAP | 7 | 1: Cave Entrance |
| Vault 15 | VAULTBUR.MAP | 8 | 1: Caverns; 2: Living Quarters; 3: Command Center |
| Shady Sands | SHADYE.MAP | 25 | 1: Garden |
| Shady Sands | SHADYW.MAP | 26 | 1: Town Hall |
| Shady Sands | CAVES.MAP | 16 | 1: Caves |
| Junktown | JUNKENT.MAP | 10 | 1: Entrance; 2: Lab |
| Junktown | JUNKCSNO.MAP | 11 | 1: Casino |
| Junktown | JUNKKILL.MAP | 12 | 1: Crash House |
| Raiders | RAIDERS.MAP | 24 | 1: Base; 2:  |
| The Hub | HUBENT.MAP | 36 | 1: Entrance |
| The Hub | HUBDWNTN.MAP | 38 | 1: Downtown; 2: Decker's Hideout |
| The Hub | HUBHEIGT.MAP | 39 | 1: Heights |
| The Hub | HUBOLDTN.MAP | 40 | 1: Old Town; 2: Thieves Circle |
| The Hub | HUBWATER.MAP | 41 | 1: Merchants |
| The Hub | DETHCLAW.MAP | 37 | 1: Lair |
| The Hub | HUBMIS1.MAP | 65 | 1: unnamed source elevation |
| Necropolis | HALLDED.MAP | 3 | 1: Sewers; 2: Hall |
| Necropolis | HOTEL.MAP | 4 | 1: Sewers; 2: Hotel |
| Necropolis | WATRSHD.MAP | 5 | 1: Sewers; 2: Watershed |
| Necropolis | VAULTNEC.MAP | 9 | 1: Sewers; 2: Living Quarters; 3: Command Center |
| Brotherhood | BROHDENT.MAP | 13 | 1: Entrance |
| Brotherhood | BROHD12.MAP | 14 | 1: Level 1; 2: Level 2 |
| Brotherhood | BROHD34.MAP | 15 | 1: Level 3; 2: Level 4 |
| Brotherhood | BRODEAD.MAP | 55 | 1: Ruins |
| The Glow | GLOWENT.MAP | 27 | 1: Entrance |
| The Glow | GLOW1.MAP | 42 | 1: Level 1; 2: Level 2; 3: Level 3 |
| The Glow | GLOW2.MAP | 43 | 1: Level 4; 2: Level 5; 3: Level 6 |
| Boneyard | LAADYTUM.MAP | 28 | 1: Adytum; 2: Underground |
| Boneyard | LABLADES.MAP | 44 | 1: Downtown |
| Boneyard | LAFOLLWR.MAP | 29 | 1: Library; 2: Underground |
| Boneyard | LAGUNRUN.MAP | 46 | 1: Fortress |
| Boneyard | LARIPPER.MAP | 45 | 1: Warehouse; 2: Underground |
| Mariposa | MBENT.MAP | 30 | 1: Entrance |
| Mariposa | MBSTRG12.MAP | 31 | 1: Stronghold Level 1; 2: Stronghold Level 2 |
| Mariposa | MBVATS12.MAP | 32 | 1: Vats Level 1; 2: Vats Level 2 |
| Mariposa | MBDEAD.MAP | 48 | 1: Ruins |
| Cathedral / Master's Lair | CHILDRN1.MAP | 17 | 1: Entrance; 2: Tower Level 1 |
| Cathedral / Master's Lair | CHILDRN2.MAP | 18 | 1: Tower Level 2; 2: Tower Level 3; 3: Tower Level 4 |
| Cathedral / Master's Lair | CHILDEAD.MAP | 47 | 1: Crater |
| Cathedral / Master's Lair | MSTRLR12.MAP | 33 | 1: Lair Level 1; 2: Lair Level 2 |
| Cathedral / Master's Lair | MSTRLR34.MAP | 34 | 1: Lair Level 3; 2: Lair Level 4 |
| Encounters and special maps | DESERT1.MAP | 0 | 1: unnamed source elevation |
| Encounters and special maps | DESERT2.MAP | 1 | 1: unnamed source elevation |
| Encounters and special maps | DESERT3.MAP | 2 | 1: unnamed source elevation |
| Encounters and special maps | COAST1.MAP | 20 | 1: unnamed source elevation |
| Encounters and special maps | COAST2.MAP | 21 | 1: unnamed source elevation |
| Encounters and special maps | MOUNTN1.MAP | 49 | 1: unnamed source elevation |
| Encounters and special maps | MOUNTN2.MAP | 50 | 1: unnamed source elevation |
| Encounters and special maps | CITY1.MAP | 19 | 1: unnamed source elevation |
| Encounters and special maps | DESCRVN1.MAP | 56 | 1: ; 2: ; 3:  |
| Encounters and special maps | DESCRVN2.MAP | 57 | 1: ; 2: ; 3:  |
| Encounters and special maps | DESCRVN3.MAP | 61 | 1: ; 2: ; 3:  |
| Encounters and special maps | DESCRVN4.MAP | 63 | 1: ; 2: ; 3:  |
| Encounters and special maps | MNTCRVN1.MAP | 58 | 1: ; 2: ; 3:  |
| Encounters and special maps | MNTCRVN2.MAP | 59 | 1: ; 2: ; 3:  |
| Encounters and special maps | MNTCRVN3.MAP | 62 | 1: ; 2: ; 3:  |
| Encounters and special maps | MNTCRVN4.MAP | 64 | 1: ; 2: ; 3:  |
| Encounters and special maps | COLATRUK.MAP | 22 | 1: Special |
| Encounters and special maps | FSAUSER.MAP | 23 | 1: Special |
| Encounters and special maps | FOOT.MAP | 51 | 1: Special |
| Encounters and special maps | TARDIS.MAP | 52 | 1: Special |
| Encounters and special maps | TALKCOW.MAP | 53 | 1: Special |
| Encounters and special maps | USEDCAR.MAP | 54 | 1: Special |
| Encounters and special maps | VIPERS.MAP | 60 | 1: unnamed source elevation |
| Auxiliary maps | CARAVAN.MAP | -1 | 1: unnamed source elevation |
| Auxiliary maps | JUNKDEMO.MAP | -1 | 1: unnamed source elevation |
| Auxiliary maps | TEMPLAT1.MAP | -1 | 1: unnamed source elevation |
| Auxiliary maps | ZDESERT1.MAP | -1 | 1: unnamed source elevation |
| Auxiliary maps | ZDESERT2.MAP | -1 | 1: unnamed source elevation |
| Auxiliary maps | ZDESERT3.MAP | -1 | 1: unnamed source elevation |

## Current implementation boundary

The shared renderer loads the source catalog for both games, retains original floor/roof and fallback scenery art, builds connected cave/vault geometry, supports source movement/save/exit grids, and now reads both owned donor libraries with isolated model/texture resolution. Source light placement and lit floor response are implemented. Sixteen first-party furniture models cover twenty bed/table FRM bindings, and one locally generated cave formation has its own source-art binding. Source material colors and prop anchor offsets are retained. The modeled set remains an initial art pass. Remaining wall families, broad prop/outfit bindings, map initialization and the gameplay sequence above are unfinished. Compilation is not visual acceptance; no computer control or new game/audit run was performed for this pass.
