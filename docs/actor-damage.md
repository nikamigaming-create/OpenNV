# Actor damage and detached limbs

The reference world owns health, injury, death inventory and severed-part
identity. The native actor presents that state using the winning source
skeleton, body-part records and dismember skin partitions. Flat and OpenXR
reach the same damage and persistence owners.

## Health and defense

CREA health comes from its resolved stats template. For an NPC with nonzero
authored base health, the endurance contribution is `(END + offset) * mult`,
using the winning `fAVDNPCHealthEnduranceOffset` and
`fAVDNPCHealthEnduranceMult` settings. Only the ACBS Auto Calc Stats path adds
`(max(level, 1) - 1) * fAVDNPCHealthLevelMult`; that derived term is truncated
and clamped to zero before adding the authored base. The manual path preserves
its floating endurance contribution. A zero authored base remains a corpse.
The bounded formula was reduced from private owned executable observation to
this neutral contract; runtime retail matching remains open. The editor's
[NPC stats description](https://geckwiki.com/index.php/Stats_Tab_-_NPC)
also distinguishes authored and derived health.

ACBS PC Level Mult now uses the retained reference selection level. The stored
multiplier is divided by 1000, multiplied by that level and truncated before the
optional minimum/maximum gates. CREA health multiplies its authored health by
the resulting level; NPC autocalculation uses it in the existing level term.
This bounded contract comes from private static observation of the owned
executable, including the creature health dispatch. No executable addresses or
instruction listings are product inputs. Missing selection state, unsupported
encounter zones and out-of-range arithmetic fail explicitly. Recalculating an
already admitted actor after player level advancement remains unbound.

Equipped armor contributes the source DNAM threshold and resistance; resistance
is stored in hundredths. The four-byte layout has no threshold. Retained item
condition applies `fArmorRatingBase + condition * (fArmorRatingMax - base)`.
Constant relevant enchantments and actor abilities contribute separately.
Definitions and modifiers are cached by their winning identities on the world
owner; condition remains live. Mixed stacks use the same first usable condition
variant as equipment use/wear; retail selected-instance correspondence remains
unverified. Unsupported relevant conditional effects still fail explicitly.
See the [ARMO definition](https://tes5edit.github.io/fopdoc/FalloutNV/Records/ARMO.html).
Armor wear, critical/sneak modifiers and complete resistance ordering still
need independent retail acceptance.

## Limb presentation and persistence

A dead actor can detach a source-severable BPTD part. Its named bone selects
the physical subtree; constraints crossing its boundary are released. Original
limb geometry remains on that subtree, source wound caps become visible and
the corresponding connecting torso section is hidden. This uses the
[source partition categories](https://www.niftools.org/nifxml/BSDismemberBodyPartType.html)
without fabricated geometry.

Source palettes can reference bones on both sides of the cut. Leaving those
bindings unchanged stretches the rendered limb between independent bodies.
For each affected partition, crossing bindings now follow an anchor on their
own side. The binding is transformed by `inverse(anchorPose) * oldBonePose *
oldBinding`, preserving the posed vertex at the cut. Bone poses at each cut
are saved in order alongside the source skeleton hash and rigid-body state.
Cold restoration applies the same cuts to fresh source skins. It rejects
missing cut poses, mismatched source bodies, duplicated limbs and invalid
transforms. Injury and cut identities must agree.

The explicit development command `physics.sever` operates on a resident dead
source actor. It records `ordinaryInput: false` in telemetry. It exercises the
shared detachment owner; it does not select a limb through weapon critical or
random dismemberment rules. The current candidate also routes lethal weapon
On Hit behavior through source severability and the saved weapon random owner.
This automatic selection has no ordinary gameplay acceptance yet. Exploded parts
remain unbound. Source blood/decal owners have separate presentation checks.
The Godot joint envelope is not Havok solver parity.

## Selected verification

After selecting Godot's Jolt backend, the September 20 gecko combat/floor and
severed-body checks pass. These bounded fixtures do not establish general
stability or complete combat.

Authored dead actors can carry XRGD: ordered 28-byte entries containing a byte
part id, three unused bytes, a local position and local Euler angles. The
[xEdit format declaration](https://github.com/TES5Edit/TES5Edit/blob/dev-4.1.6/Core/wbDefinitionsCommon.pas)
defines the field layout. Owned skeletons establish traversal order and repeated
part ids; the [NIF filter declaration](https://github.com/niftools/nifxml/blob/develop/nif.xml)
defines the five-bit Havok part field. Positions are NiNode game units, not
Havok metres or body-world transforms. Source binding checks require complete
ordered agreement before applying a pose. Persistent physical transforms take
precedence on reload. XRGB root rotation remains unbound.

The owned damaged-robot fixture checks initial pose, native falling/contact and
cold persistence. Actual flat and Elliott Tate simulator views now show ED-E
on Nash's counter and the repair menu opens through ordinary input. This is
inspection/activation evidence, not completed repair, recruitment or following.

Synthetic contracts cover NPC manual/autocalculated health, fractional
contributions, zero-health corpses, armor layouts, per-reference damage,
once-only death loot, severed-part persistence and invalid saved cut poses.
The owned native sever audit exercises all five declared severable parts of
a gecko and a raider, source collision bodies, floor retention, skin bindings
on the correct side of each cut and cold restoration. A separate transform
check verifies vertex continuity and independent movement after rebinding.
Ordinary shooting, visible falls and captured skin quality require the actual
gameplay check; these component results do not accept a cell or full combat.
