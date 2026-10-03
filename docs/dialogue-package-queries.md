# Dialogue current-package queries

Dialogue and scripted Say/SayTo conditions share IsCurrentPackage function161
with AI package selection. Self means the speaker reference; target means the
actual listener, including a non-player actor. Explicit references and PACK
arguments use the declaring plugin's master order. A query reads only its
selected subject; it cannot substitute the speaker or player for another actor.

The reference world reads an NPC or creature's active native package owner.
Player queries read the existing player package lifecycle, including its current
assignment while a replacement waits. An active owner with no package answers
false. Missing actors, missing package owners and unsupported scopes remain
visible failures. Resident queries read the native assignment. An unloaded
actor uses the shared source assignment owner: winning PKID/template, live quest,
schedule and conditions select its package and retain actual Start/Change
effects. Selection cannot invent native movement, arrival or completion. New
procedures without valid native continuation block saves. NPC and creature
retirement retain their actual assignment and clear only their own callbacks;
native reentry transfers that lifecycle without replaying consumed effects.
Complete unloaded-actor procedure simulation and matched retail query timing
remain unaccepted.

Synthetic checks distinguish speaker, player, NPC listener and explicit source
references, deliberately reorder declaring-plugin masters, replace assignments
and refuse absent owners. The selected installed TTW GREETING graph contains230
current-package predicates:227 self and three explicit-reference queries. Its
PACK arguments and source scope pass against a declared package-state fixture;
source bytes remain unchanged. That fixture is separate from ordinary gameplay.
The native Dialogue audit passes the real source actor's current assignment and
refuses its query after retirement. The required integrated gate also passes.
The fresh ordinary retry cold-loads the genuine closed-door stage80 checkpoint,
opens the authored door and follows Dad through CG01 completion and birthday12.
Ordinary Amata approach reaches its goal without a bot error and passes the prior
IsCurrentPackage failure. Greeting selection then retains its next unbound
GetVampire condition. The previous failed conversations remain retained without
replay; no conversation or campaign completion is implied.

Source declaration: [xEdit FNV conditions and records](https://github.com/TES5Edit/TES5Edit/blob/dev-4.1.5/Core/wbDefinitionsFNV.pas).
Owned records, saves and private diagnostics remain local. Complete conversation,
campaign and retail parity acceptance remain open.
