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
also distinguishes authored and derived health. Encounter-scaled health still
requires persistent level selection and fails explicitly.

Equipped armor contributes the source DNAM threshold and resistance; resistance
is stored in hundredths. The four-byte layout has no threshold. Retained item
condition applies `fArmorRatingBase + condition * (fArmorRatingMax - base)`.
Constant relevant enchantments and actor abilities contribute separately.
Definitions and modifiers are cached by their winning identities on the world
owner; condition remains live. Mixed condition variants and relevant conditional
effects remain explicit failures. See the [ARMO definition](https://tes5edit.github.io/fopdoc/FalloutNV/Records/ARMO.html).
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
random dismemberment rules. Automatic selection, exploded parts, gore replacement
models and sever-triggered blood/decal effects remain unbound. The current
Godot joint envelope is also not Havok solver parity.

## Selected verification

Synthetic contracts cover NPC manual/autocalculated health, fractional
contributions, zero-health corpses, armor layouts, per-reference damage,
once-only death loot, severed-part persistence and invalid saved cut poses.
The owned native sever audit exercises all five declared severable parts of
a gecko and a raider, source collision bodies, floor retention, skin bindings
on the correct side of each cut and cold restoration. A separate transform
check verifies vertex continuity and independent movement after rebinding.
Ordinary shooting, visible falls and captured skin quality require the actual
gameplay check; these component results do not accept a cell or full combat.
