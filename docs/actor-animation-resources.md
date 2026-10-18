# Actor animation resources

Actor group selection retains the actual owned KF resource and its exported
sequence name. An unsuffixed logical group does not imply an unsuffixed filename:
owned first-person and third-person grip resources include authored variants.

`FalloutActorAnimationResources` belongs to the mounted content source. Exact
resources retain their existing priority. If absent, the owner indexes immediate
KF resources in the requested folder and requires an underscore variant boundary
plus one compatible exported group. A unique variant retains its actual filename
and sequence identity; missing, malformed or ambiguous selection stays visible.
It neither substitutes a named weapon nor crosses skeleton/view directories.
Nested movement directories retain independent indexes.

Suffixed files can export the unsuffixed action group: the owned deathclaw
`h2hattackleft_a.kf` exports `AttackLeft`. Catalog validation first prefers an
exact compatible variant group, then admits one compatible base group at the
requested underscore boundary. It retains the resolved sequence name alongside
the source path; creature and player playback use that binding when a KF exports
several groups. Unrelated suffix groups cannot redirect playback. Ambiguous or
wrong-family groups fail, and a failed catalog publishes no partial bindings.
This resolves source metadata without choosing an action variant or advancing RNG.

Player Clip/ClipExists and native combat/package SelectPath share this owner.
In-memory directory and result reuse avoids repeated archive inventory scans.
Missing player/group errors include their logical path, and player telemetry
reports the actual grip sequence. Explicit attack owners select from the complete
validated variant catalog and retain their source path/hash, clock and random
state. Retail weighting/global RNG alignment, broader group inheritance and exact
pose/blend parity remain unowned.

`ReferenceScriptContractProbe --animation-resource-contracts` checks case,
authored suffixes, exported metadata, exact priority, folder isolation, caching,
ambiguity, malformed declarations and missing-path telemetry. Native owned
weapon assembly/action checks separately bind real models and controller channels.
The NativePlayerPresentationAudit `--owned-weapon-action` mode selects a winning
WEAP by editor identity against a genuine unchanged checkpoint and its complete
mod/dependency stack. It checks both views, source grip/equip/unequip/attack/reload,
the authored discharge key, finite bone poses, native attachment and projectile
sockets while hashing source resources. These isolated checks do not establish
ordinary campaign progress, complete persistence or matched retail pixels.

The native `--attack-range-cold` melee fixture serializes a source-selected attack
during windup, constructs fresh source skins in an independent world and resumes
the exact resource/hash, clock and random state. Its first source Hit damages
only that world's target once. The original target then moves outside reach
during its retained windup; the genuine Hit key executes without damage. Moving
the target back allows a subsequent source hit. Owned deathclaw 0e745f and coyote
169b7a pass against target 104e85, and both player weapon-preparation checks pass.
The fixture prints original owned placement separately from its test placement;
it uses synthetic floor geometry and does not establish an ordinary terrain
encounter, retail timing or physical VR playability.
