# Reference weapon-hit events

The shared reference world retains projectile and melee contact marks separately
from animation, impact presentation and health ownership. Damage owners mark
actual source target/attacker/weapon identities after applying their damage.
A projectile contact on an activator also admits its script when no actor
health owner exists. Impact effects are not a prerequisite for script events.

The normal reference script frame freezes its pending marks, admits OnHit and
OnHitWith alongside GameMode and executes blocks in authored declaration order.
Repeated pellets with the same identities coalesce on that frame. Each original
block runs once when its filter matches the admitted membership. OnHit filters
require actual actor references and retain the native non-actor filter behavior.
OnHitWith resolves compiled WEAP or direct source FLST membership with the
declaring master's adjustment. Activators admit projectile weapon events and
exclude melee weapon events. Hit blocks do not acquire an action reference.

Receipts retain revisions and owner/retirement identity. Consumption preserves
new identical marks created during source effects. Unloaded references retain
pending admission; paused native frames neither execute nor consume it. A
reached source failure retains its executed prefix and blocks later GameMode
execution. Saving pending hit events fails visibly until admission settles;
consumed locals and quest outcomes use the existing cold-state owners.

Synthetic script checks cover source order, actor/reference isolation,
master-adjusted weapon/list filters, pellet coalescence, reentrant marks,
unloaded references, invalid payloads, retained faults and consumed cold state.
A native adapter fixture checks collision descendant identity, pause/resume,
source execution and once-only consumption. The selected original TTW target
script passes three isolated contact frames, its animation/count/tutorial/stage
request order, guards and cold continuation. Its supplied stage50 fixture does
not execute campaign or radroach results. Ordinary ballistic contacts and
campaign progress remain separate checks. Fresh ordinary TTW input fires six
shots, three contacting an original target. Its original block requests the
animation, increments targetCount and enters tutorial62. The third target hit
completes the original CG02:55 and60 results and admits the source radroach.
Radroach combat and subsequent campaign progress remain separate work.

Weapon-attached OnHit, player object-script admission, native-plugin hit
callbacks, explosive direct-contact events, exploded-limb extra events,
nested hit-list membership and matched retail frame timing remain open.
Complete weapon and mod compatibility is not established by this owner.

Source contracts: [GECK OnHitWith](https://geckwiki.com/index.php/OnHitWith),
[GECK OnHit](https://geckwiki.com/index.php/OnHit) and
[GECK GetActionRef](https://geckwiki.com/index.php?title=GetActionRef).
