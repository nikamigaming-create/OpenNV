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

Player Clip/ClipExists and native combat/package SelectPath share this owner.
In-memory directory and result reuse avoids repeated archive inventory scans.
Missing player/group errors include their logical path, and player telemetry
reports the actual grip sequence. Retail variant weighting, multiple eligible
variants, broader group inheritance and exact pose/blend parity remain unowned.

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
