# Dialogue spatial queries and actor history scopes

Dialogue GetDistance resolves the real speaker, listener/player or explicit
reference from the declaring plugin's masters. Both operands require actual
placed references, with the runtime player admitted through its player owner.
The same live reference-distance callback serves conversation and scripted
speech; no independent marker position or cached distance is invented. Results
use source game units and require finite nonnegative values. A missing spatial
owner, base-form operand or unsupported scope remains a visible failure.

Synthetic checks cover speaker, player, explicit listener, adjusted explicit
subject, changed live placement and invalid/missing owners. The selected
original TTW Dad range INFO uses its actual inside marker and strict200-unit
predicate. Its near, boundary, far and cold moved-placement checks pass in an
isolated fixture, with the winning bytes and quest state unchanged. Ordinary
INFO end-result and campaign progress remain separate evidence.

GetTalkedToPC package conditions use one subject resolver for native NPCs,
creatures and unloaded actors. Self, source PTDT target and adjusted CTDA
explicit reference read that reference's retained talked-to-player state.
Previously the native actor accepted only self while the unloaded owner
already accepted an explicit subject. The original Amata-to-Beatrice package
exposed the mismatch. Its isolated false/true/live/cold checks pass without
running dialogue results or replacing caller history with subject history.

Source contract: [GECK GetDistance](https://geckwiki.com/index.php/GetDistance).
