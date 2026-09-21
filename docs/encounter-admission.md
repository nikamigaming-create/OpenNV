# Encounter admission and blocked walkways

The reported empty Bison Steve population was rejected before native construction:
actor admission had no persistent encounter-zone level owner. This is distinct
from save corruption. A preserved user save restores its actual hotel position,
inventory and reference state; the original save was not reset or replaced.

## Shared state contract

`FalloutEncounterZone` reads winning ECZN DATA and adjusted XEZN references.
`FalloutReferenceWorld` retains each zone's first player level, scaling input,
resolved level and source hash. A reference assignment takes precedence over
CELL, then WRLD. Persistent exterior references resolve their actual spatial CELL;
file ancestry and spatial residency remain distinct. Moved references use their
current placement when first admitted.

Minimum level, Match Below Minimum Level and `fLevelScalingMult` determine the
initial level. XLCM Easy/Medium/Hard/Boss use the winning leveled-actor multipliers;
None retains the list flags. Easy admits all eligible levels; the other explicit
difficulties choose from the highest eligible level. Actor choices remain
reference-owned and feed appearance, inventory, scripts, factions and combat.

Save v17 carries encounter zones. Earlier saves remain readable; already retained
actor choices are preserved, and zones without saved history initialize on their
next admission. Restoration validates the source hash and level calculation
before publishing any zone state. Respawn/reset scheduling, including the effect
of Never Resets, remains unimplemented; parsing that flag is not reset support.

The record contract follows the [ECZN format documentation](https://github.com/TES5Edit/fopdoc/blob/master/FalloutNV/Records/ECZN.md).
Level policy is described in [Encounter Zone](https://geckwiki.com/index.php/Encounter_Zone),
[Reference](https://geckwiki.com/index.php/Reference) and
[Leveled Character](https://geckwiki.com/index.php/LeveledCharacter).
The difficulty enumeration is documented by
[PlaceLeveledActorAtMe](https://geckwiki.com/index.php?title=PlaceLeveledActorAtMe).
This implementation has not passed a matched retail timing/selection comparison.

An absent female ARMO model now falls back to its male geometry and associated
material fields, as documented by [Armor](https://geckwiki.com/index.php/Armor).
Sex-specific ARMA add-ons retain their existing rules. No replacement outfit,
proxy actor or named-location exception is introduced.

## Traversal and menu behavior

An authored hotel walkway is about 45.84 degrees. Godot's implicit 45-degree
controller limit classified it as a wall. The configurable OpenNV maximum is
now explicitly 50 degrees for the player and actor controllers. Capsule route
and placement queries read the same body limit. Source triangle collision is
unchanged; step height, floor support and whole-capsule clearance still apply.
At a join into an ascending surface, stepping tries the remaining supported
heights if the first ray is too low to clear the capsule. Every candidate remains
within the step limit and must pass both upward and forward sweeps.
The value is an OpenNV movement policy, not a recovered retail controller limit.

Continue and Load resume the configured single save slot, show loading feedback
before construction and expose a failure message if construction throws. This
does not implement a multi-slot Load browser. Deferred door callbacks explicitly
use the void callable adapter so an asynchronous Task is not converted to Variant.

## Verification and limits

- Synthetic reader contracts cover shared zones, source difficulty, first-entry
  scaling, overrides, persistent/moved exterior ownership, WRLD inheritance,
  malformed records and atomic cold restoration.
- The selected owned audit reads 28 ECZN records and follows the Primm exterior
  grid's door graph through 12 connected interiors. It passes admission/resource checks for all 54 enabled
  actors in that set; 48 source-disabled references remain disabled. All 14
  enabled hotel NPCs prepare. Preparation does not prove rendering or gameplay.
- Ordinary exported Load/Continue restore genuine copied saves. The upstairs
  hotel continuation publishes its six enabled NPCs. Flat movement and Elliott
  Tate controller input climb the actual broken ramp without jumping; XR also
  returns down the ramp. The initial flat descent meets a real shelving obstacle
  after descending, rather than passing through it.
- Native collision checks climb and descend a 46-degree slope and reject a
  60-degree slope, tall walls, low ceilings, missing support and other floors.
  A raised entry into the walkable slope exercises the join rather than only a
  slope that begins flush with the floor.

Selected private logs, saves and images remain outside Git. Unsupported source
lighting/materials, actor scripts/packages, missing owned resources and broader
world coverage remain visible divergences. This is not all-building, all-actor,
campaign, headset or retail-parity acceptance.
