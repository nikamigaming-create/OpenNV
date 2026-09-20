# Companion gameplay

The shared script owner executes recruitment INFO results into retained actor
state. AddToFaction changes one reference; SetFactionRank also changes its actor
base. Removed membership overrides inherited membership without changing peers.
Perks, ignore flags and combat style retain source hashes in campaign save v16.
Older v15/v14 saves remain readable. Cold restoration preflights all overrides
before replacing current state. Dialogue, package conditions and faction threat
queries consume live membership.

MoveTo residency discovers the new source reference and binds its native event
owner before materialization. GetInSameCell uses retained placement and live
exterior coordinates, including negative grids and distinct worldspaces. ResetAI
shares the package reevaluation effect. RemoveScriptPackage accepts the source
compiler's redundant argument and removes only a script override; NPC addition
remains unbound, so removal reevaluates the unchanged base package list. See the
[GECK command contract](https://geckwiki.com/index.php/RemoveScriptPackage).

Explicit INFO response sounds reuse winning SOUN/WAV variants, retained actor
sound randomness, source gain/pitch/attenuation and audio-finished completion.
They can be skipped through the same dialogue control. Source reverb and listener
submersion limitations remain visible. Ordinary voice-file/lip responses retain
their existing path.

Creature and NPC weapons share source weapon/ammunition declarations, inventory,
KF Hit events, BPTD contacts, damage and death. Embedded weapons resolve their
unique authored ProjectileNode. Competing usable weapons are selected by current
equipment, then condition-adjusted base damage, with source combat-style melee/
ranged restrictions. This deterministic selection is not retail tactics parity.
Teammates select a visible resident hostile engaging the player or another
teammate. Source OnCombatEnd runs when the opponent dies; package movement resumes.
Ranged attacks query the muzzle line and the complete source spread against
native actor collision before spending ammunition. An obstructing ally holds
fire. This conservative firing decision is not matched retail combat tactics.
Valid muzzle geometry survives an unsupported light lane; that light remains
absent with an explicit diagnostic.

Sight queries accept the target player's physical body as a successful contact.
An occluded target within weapon range still requires pursuit; a visible target
that needs a turn uses the stationary idle. The spread query uses actor/player
collision layers and ignores dead/disabled allies. World geometry is checked by
the separate muzzle ray, so scenery cannot exhaust all 128 friendly contacts.
Saturation or another held-shot reason remains explicit in attack telemetry.
The native source-player fixture checks turning, a blocked route, actual damage
and 160 irrelevant world contacts. An ordinary flat continuation independently
shows the Fiend firing/reloading, player damage, ED-E's kill and subsequent
Stimpak healing. The older paired showcase predates these corrections.

An ammo-free embedded creature gun with no authored reload clip currently refills
its virtual magazine at the attack boundary. This explicit recovery policy has
unmeasured retail cadence. Weapons that consume inventory ammunition or have
separate weapon geometry still reject missing reload animation ownership.

Source doors collect active, unrestrained followers. Destination residency is
built with those references, then arrivals must lie on connected source NAVM and
pass native capsule/floor clearance. Failed placement rolls back retained
positions. Destination collision remains registered while staged gameplay is
disabled; default Godot body removal would otherwise invalidate all arrival
queries. Ordinary flat and Elliott Tate simulator runs repaired and recruited
ED-E, exited Nash together, let him kill the source hostile, resumed Follow and
looted the corpse. Flat cold continuation also passed. The simulator is not
physical-headset acceptance. Repaired flying actors can initially sit too high
above a raised MoveTo target; interior placement remains a known defect.

Actor aim uses the source BPNT target bone, with BPNN fallback only when BPNT is
empty. Humanoid torso BPNN can name the accumulation root below the chest.
Actor contacts use the flesh material lane; an unresolved world triangle leaves
an explicit impact diagnostic without discarding authoritative damage or AI.

Synthetic checks cover faction scope, perk removal, condition-tab refusal, cold
state and transactional source-drift rejection. The selected owned recruitment
INFO verifies teammate/faction/perk/flag/style effects and combat-end recovery.
The source activation timer and first greeting results are also executed.
The native physics fixture uses ED-E's source weapon and a gecko's source hit
volumes on a labelled synthetic floor: 40 HP becomes zero and combat ownership
ends. A second phase starts with an empty embedded magazine and verifies recovery
and another kill. None of these fixtures are ordinary recruitment or paired gameplay footage.

Run `ReferenceScriptContractProbe <owned Data> --companion-gameplay`. The native
`NativeActorCombatAudit` accepts `<owned Data> --companion-combat <companion ACRE>
<opponent ACRE> <MoveTo reference>`, with runtime FormIDs written as hex.
Its `<owned Data> --player-target <NPC ACHR>` mode exercises player visibility,
stationary turning, occluded pursuit and crowded-scene firing on a labelled
synthetic floor and wall.

Acquisition of Enhanced Sensors is persisted, but its detection/compass effect
is not implemented. Script abilities report reached lifecycle/command failures;
NPC radio remains unbound. Non-actor perk condition tabs cannot silently apply to
damage/spread. Broader perk entry points, complete effect lifecycles, unarmed NPC
fallback, tactics, hit/death script events, XP and exact damage ordering remain
open. Muzzle flicker and particle gravity/turbulence report visual divergence.
