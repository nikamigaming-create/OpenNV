# Source dialogue packages

The shared PACK owner reads Dialogue procedure15 with PTDT reference targets,
positive activation distance and PKDD conversation/SayTo behavior. A start
location is optional. Location-free procedures use the same NAVM, owned KF and
native capsule owner as other actor travel; range and floor support precede the
speech request. Package Running and WeaponDrawn flags remain source inputs.

Explicit zero-radius reference waits and current-location waits are supported.
PLD2 retains reference, current or editor trigger locations. Repeated identical
declarations are accepted; differing declarations fail. A zero-radius wait with
a trigger holds the actor at its wait and requires the target within activation
distance. Player targets move through ordinary controls. Unsupported location
selection or automatic non-player trigger-target movement remains visible.
PKDD field of view belongs to conversation presentation; it does not invent an
angle gate for range arrival.

Type1 uses source topic INFO admission and the actual voice/LIP owner. Begin
results precede playback; end results follow the last real audio completion.
Package completion belongs to the requesting procedure. A callback cannot
complete a replaced owner, including a newer assignment of the same PACK form.
An empty eligible response queues completion for a later normal source frame.
Script Say/SayTo completion events retain their separate lane; package speech
does not manufacture an attached SayToDone event. Native event/timing parity
still requires matched observation.

Shared talked-to-player and script-variable state drives package conditions.
GetIsCurrentPackage resolves self, the package's explicit reference target or a
CTDA explicit reference using the declaring plugin's master adjustment. It
queries that reference's active owner. A missing live package owner fails
explicitly; it cannot be replaced with the querying actor's package.

Pending speech/conversation continuation remains save-blocked. Supported
completed Type1/native-motion procedures retain DialogueCompleted in campaign
schema29. Cold validation checks the winning PACK and supported location
before restoration, and does not replay its begin event. Schemas28/27 and the
genuine26 checkpoint remain readable; older schemas reject the new completion
field. Type0 and active voice restoration remain unbound.

Synthetic contracts cover optional waits, source types, malformed fields,
repeated/conflicting triggers, target/explicit subject queries, pause and
deferred completion. The owned native fixture uses the real source actor,
NAVM/KF, voice and lip over an isolated floor/wall/player. It checks blocked
refusal, supported approach, completion after audio, once-only dispatch, stale
replacement callbacks and pending-save refusal. That fixture is separate from
ordinary campaign progress.

The ordinary genuine checkpoint run completes CG01 and reaches birthday12.
Dad's source finishing result now uses the shared radio refresh owner. Amata's
source wait/trigger admits her conversation after ordinary player approach.
The optional INFO reader and current-package subject binding pass their earlier
failures; greeting selection retains the next unbound GetVampire query.
Birthday completion, full dialogue continuation, complete cold
acceptance and matched retail behavior remain unverified.

Source references: [GECK Dialogue Package](https://geckwiki.com/index.php/Dialogue_Package),
[xEdit FNV record definitions](https://github.com/TES5Edit/TES5Edit/blob/dev-4.1.5/Core/wbDefinitionsFNV.pas).
Owned source records and private diagnostics remain local.
