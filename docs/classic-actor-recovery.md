# Classic actors and scenery in the native world

Classic MAP/PRO/FID identity, sex, tile, rotation, offsets, equipment and action
remain authoritative. Owned New Vegas/FO3 geometry supplies appearance only.
The current scene is an incomplete 3D diorama, not a complete campaign or a
claim of one-to-one likeness.

## Humanoids, equipment and likeness

`runtime/config/classic-humanoids-v1.json` binds original six-letter families to
owned skinned assemblies: male/female vault suits, tribal clothing, leather,
metal and combat armor. All six premades have explicit face candidates. A custom
approved FaceGen selection remains unchanged. The tribal donor is Dead Horses
Stalker clothing, using the shared native skeleton, body, hands and outfit.
The male tribal hair is now dark, with slightly broader shoulders and a narrower
waist. The adjustment applies to the dressed skeleton and its hand socket,
restoring the prior pose before every animation update so it cannot accumulate.

The guard in ARTEMPLE has an actual source-equipped spear: item PID 7 and weapon
animation 4. Its owned spear NIF attaches through the Weapon bone with its aim
and hand-grip KF layers. A donor dropped-item Havok body had detached it from the
hand. Classic presentation now removes donor collision owners before tree entry;
MAP/PRO state remains the sole simulation authority. Constant-opaque source
materials use an opaque draw only after texture, material, vertex-alpha and
controller checks, allowing the spear to cast a shadow without changing true
fractional transparency.

Armed source actors require their exact weapon/action recipe. Other equipment,
miniguns, attacks and death states do not receive an unarmed substitute. The
tribal garment, individual faces, complexion, hairstyles and Vault 13 suit
number remain unfinished. The guard's donor throwing stance also differs from
the classic standing-spear silhouette. Native close views establish visible
geometry, hair, grip and shadow; they do not accept final likeness or combat.

## Creatures and animation

`runtime/config/classic-creatures-v1.json` names source families, owned skeletons,
skins, KF clips and explicit source-animation roles. The giant-rat path has
native close footage; ants, dogs, brahmin, scorpions, deathclaws and unarmed
super-mutants have candidates requiring per-family visual/action acceptance.
The native ARCAVES inspection also shows admitted scorpion geometry. Merely
resolving a model is not acceptance of its facing, color, scale or animation.

`ClassicSourceActorSprite` retains the original actor image and publishes its
source state/phase to the analog. A one-frame standing source pose permits the
owned 3D idle to run on its own presentation clock. It does not create AI,
movement, attacks or damage. Source death/scripted poses are not given breathing
idles. Donor root translation is consumed without adding a second movement to
the source hex path. Original direction/frame failures remain visible; the
mamtntka failure in the industrial map retains its last valid source image.

## Controls and interfaces

F4 or View switches every world sprite and admitted 3D analog with one action,
including the player, NPCs, creatures and scenery. Unsupported 3D forms are
counted; their original art is available in sprite mode. F3 compares two cameras
rendering the same world and actor state. Switching View exits the comparison
so the change is visible. Original HUD and floor art intentionally remain in
both views. F2 hides presentation controls and the map-browser panel.

Mark view, Return to view and Slow camera pass provide a 22-second push, orbit
and pullback. Next actor moves only the camera to an actual source actor and
frames creatures by their model height. Q/E/right-drag orbit; the mouse wheel
zooms to the configured 1.25-meter minimum. These are desktop orbit controls;
first-person and third-person locomotion remain unrestored.

The original campaign LOOT/INVBOX/IFACE art and AAFF font render the live
inventory. Names/headings, carry weight, armor class and HUD status have measured,
centered panel bounds; descriptions and the log remain left aligned. Wrapping
keeps words together and clips to each display. Character headings and counters
are centered within their control regions.

Whole source-stack Take/Return, quantity, payload, source item icons, weight,
capacity and cold save restoration are implemented. The FO1 source Bones loot
was reached by actual pointer movement; its knife and ammunition were transferred
and restored. FO2 inventory opens, but its entry contains no accessible container
for the focused probe. Equip/use/drop, scripted containers, NPC/corpse looting,
combat and quests still need their source owners.

## Where the modeled furniture appears

These are actual source placements, not rooms populated for a capture.
The browser uses the selected installation's map and elevation names.

| Map / elevation | Authored models used there |
| --- | --- |
| Vault 13 / Living Quarters | Vault beds, tables and chairs |
| Military Base, MBSTRG12 / Stronghold Level 1 | Full bunk, bunk head and bunk foot |
| Military Base, MBSTRG12 / Stronghold Level 2 | Wall sleeping pods, panels, full and sectional bunks |
| Necropolis, HALLDED / Hall | Metal and salvage beds, mats, bedrolls, round/square/long and broken tables |
| Cathedral, CHILDRN1 | Mats, bedrolls, metal beds, toppled/broken and dining tables |

The live September 7 inspection showed a metal bed in the Necropolis hall and a
full bunk in Military Base Stronghold Level 1. Orientation, sectional joins and
close material quality remain unfinished; these observations do not accept all
furniture. The source-level inventory is available through the development lab's
`classic-scenery` command using the existing runtime readers.

The subsequent material pass attached original paint to visible mesh surfaces,
kept physical texture scale and corrected the full bunk's authored facing.
The same room now uses connected industrial wall geometry for its `mmb` family;
one contour variant remains unresolved. See [scenery authoring](classic-scenery-authoring.md) for
the material recipe, current private native diagnostics and full asset inventory.

A valid all-transparent roof FRM (`fom1000.frm`) previously prevented the hall
from opening. Transparent floor/roof art now contributes no drawn surface without
rejecting its source level. Source tile IDs and navigation are unchanged.

Owned scenery now uses uniform scale fitted to its full classic projection,
including MAP rotation, rather than treating a rock's entire image height as
vertical height. Authored furniture also includes source rotation in that fit.
MAP and FRM offsets still determine placement. Meshes do not alter the source
walk mask. Remaining donor silhouette and furniture-facing mismatches need work.

## Ground and atmosphere

The EDG source ground family keeps original tile color/layout under owned soil,
normal relief, fine grain and dust in world coordinates measured in meters.
Joining the source color tiles in a transient atlas before minification avoids
per-tile clamp seams. Other floor families, including temple paving, still need
work. Source tree7/tree8/tree9 placements now admit owned dead-tree geometry;
other grasses, trees and props are still missing. Volumetric fog and tighter
contact shadows add depth without changing hex placement or walkability.

## Recovery references and remaining owners

Before 7140879 the first-party code included Fo1CreatureModel, Fo1Mob,
Fo1TacticalCamera, Fo1TacticalSession, Fo1OwnedCaveKit, the movie player and FO2
humanoid consumers. Recover their useful capabilities through native source
owners without restoring prepared retail-asset launch caches. The old cave kit
contains the embedded Vault 13 portal/backing airlock. Its numbered door and
cinematic handoff are not yet restored in the native world.

Neither campaign is complete. Source script initialization, general doors and
elevation interactions, equipment, combat, dialogue, quests, world travel and
persistent world changes remain required. Scene inspection does not demonstrate
campaign travel. The private videos under local/classic-runtime-video contain
actual native viewport output and remain local. Temporary capture segments and
debug dumps are removed after the requested export.

The current private export is
`local/classic-runtime-video/fallout-classic-analogs-and-inventory.mp4`
(103 seconds, 1920×1080, 30 fps, silent). The adjacent JSON lists its chapters.
It shows the source temple guard and spear, both original inventory/HUD layouts,
the cave rat, actual FO1 pointer movement and Bones item transfers, and labeled
Vault 13 furniture / temple scorpion map inspection. Original and 3D views share
the same live scene. These are current candidates with visible missing-model
counts. The inspection orbit can enter walls; obstruction handling is unfinished.
