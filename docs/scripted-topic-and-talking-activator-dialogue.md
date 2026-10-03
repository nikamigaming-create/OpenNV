# Shared scripted topic and talking activator dialogue

Scripted Say/SayTo and player conversations share the winning DIAL type,
running QUST admission, quest header conditions and quest priority. Immutable
headers are cached; eligibility, source speaker/listener context, SayOnce history
and the existing retained random owner are evaluated for each request. Stage and
objective conditions do not become quest header predicates. A stopped quest's
unsupported INFO cannot preempt an eligible response.

A placed TACT uses its winning VNAM VTYP and actual native reference model.
Default activation enters the same conversation, voice index and result owners
as actor dialogue. It does not create an NPC, skeleton, faction or actor package.
Actor-only queries on an unbound talking activator remain explicit failures.

SetTalkingActivatorActor retains an actual NPC/creature reference in shared world
state. Source compiled references select that actor's base, traits and voice for
dialogue; the talking activator retains its physical model and voice channel.
Omitting the actor clears the binding and restores source VNAM identity. The
binding validates and restores with reference snapshots. Each active response
retains its selected subject independently of later binding changes. Result
context, exact runtime query behavior and visual/audio presentation still need
matched retail acceptance.

Native conversations expose physical speaker, dialogue subject and VTYP.
Reference telemetry exposes retained bindings without requiring a complete save
snapshot. Stage telemetry distinguishes entered stages, consumed steps, pending
execution, completed execution and failure; an entered stage alone is not a
completed result program.

AI IsInCombat resolves self, the actual reference package target or an explicit
CTDA reference using the declaring plugin's masters. NPC and creature packages
query retained engagement, applied enable and injury state; the player query
uses actual incoming native engagements. Reference scripts share this combat
state. The reached birthday stage previously aborted during EVP before its
talking actor setter, rather than losing a successfully applied binding.

Synthetic contracts cover source type, active quest/header/priority changes,
source voice identity, non-actor queries, compiled actor binding, cold history,
clear and invalid targets. Selected owned checks execute the actual stage's
compiled binding scope and restore/clear it without changing source bytes. EVP
requests are retained in that isolated fixture; native procedures, audio and
campaign traversal are separate. The selected source greeting audit uses an
explicit stage/package/inventory/sex context and zero random draw; its eligible
random member need not equal the ordinary run's member.

Talking activator LIP/model Talk animation, linked-actor runtime queries,
NPC-to-NPC conversation, spatial dialogue audio, active conversation saving and
matched pixels/timing remain unaccepted. No campaign or universal mod acceptance
is implied by these component checks.

Primary format/behavior references:

- [DIAL](https://tes5edit.github.io/fopdoc/FalloutNV/Records/DIAL.html)
- [INFO](https://tes5edit.github.io/fopdoc/FalloutNV/Records/INFO.html)
- [QUST](https://tes5edit.github.io/fopdoc/FalloutNV/Records/QUST.html)
- [TACT](https://tes5edit.github.io/fopdoc/FalloutNV/Records/TACT.html)
- [SetTalkingActivatorActor](https://geckwiki.com/index.php/SetTalkingActivatorActor)
