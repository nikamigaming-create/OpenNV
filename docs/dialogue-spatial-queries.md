# Dialogue spatial queries and actor history scopes

Dialogue GetDistance resolves the real speaker, listener/player or explicit
reference from the declaring plugin's masters. Both operands require actual
placed references, with the runtime player admitted through its player owner.
The same live reference-distance callback serves conversation and scripted
speech; no independent marker position or cached distance is invented. Results
use source game units and require finite nonnegative values. A missing spatial
owner, base-form operand or unsupported scope remains a visible failure.

References in different interiors, an interior and exterior, or different
worldspaces return the finite `float.MaxValue` no-distance result. Different
exterior cells in the same worldspace retain their shared 3D coordinates.
Private owned executable inspection verifies the original query's default
result and its cell/worldspace checks; private addresses and code remain outside
the repository. Synthetic scope checks and owned interior/cold queries cover
the distinction. Missing placement ownership still fails visibly.

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

Dialogue IsInList uses the speaker, actual listener or explicitly adjusted
reference's winning base against direct FLST LNAM members. Reference identities
and nested FormLists do not become base membership. Explicit player HasPerk reads
the shared current perks; NPC listeners do not borrow player results. GetInZone
reads the subject's actual CELL encounter zone with its worldspace fallback,
without initializing encounter levels or using an actor's spawn-zone override.
Synthetic and original unrelated dialogue quest filters cover these distinctions.

GetScriptVariable resolves its explicit adjusted reference/quest argument and
declaration index through the same C# variable owner used by source scripts and
AI. Speaker/listener selection does not replace those arguments. NPC speech,
player conversations and source message conditions share this resolution;
missing declarations, owners and unsupported scopes remain visible failures.
