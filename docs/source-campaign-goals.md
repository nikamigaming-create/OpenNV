# Read-only campaign source goals and portals

`FalloutQuestTargets.Read(FalloutPluginStack records, FalloutFormKey quest)`
returns `IReadOnlyList<FalloutQuestTarget>`. Each target retains `Quest`,
`Objective` (`uint`), `Ordinal` (`int`), `Reference`, `Flags` (`byte`) and
`Conditions` (`IReadOnlyList<FalloutCondition>`).

The winning QUST is the only target owner. QOBJ and its single NNAM description
bound an objective; each QSTA owns only its following CTDAs. Quest-wide and stage
conditions are not imported into targets. Ordinals are zero-based QSTA order
across that entire winning quest, not numerical objective order. Repeated target
references are separate source declarations and are not deduplicated. Master
arguments and explicit condition references retain the winning plugin's original
namespace, independently of the stack's runtime load indices.

The public [QUST format](https://tes5edit.github.io/fopdoc/FalloutNV/Records/QUST.html)
defines QSTA as eight bytes: a four-byte target FormID, one flags byte and three
unused bytes. Only flag `0x01` (compass marker ignores locks) is admitted. Unused
bytes are not interpreted as flags. This flag is **not** door traversal authority.
Null, missing/deleted or wrong-type targets, duplicate objective indices,
malformed descriptions/extents and misplaced target fields fail visibly with
the winning source context. CTDA decoding uses the existing FO3/FNV owner and
retains its original flags, function, arguments and run-on; unsupported condition
evaluation is not replaced with a guessed boolean or subject.

The runtime controller must choose genuinely displayed, unfinished objectives
from authoritative quest state and supply the existing condition owners with
their actual subjects. Reading these declarations neither sets an active quest
nor changes stages, objectives, references, inventory, locks or enable state.

`new FalloutCampaignPortalGraph(FalloutPluginStack records)` binds winning REFR
XTEL links once, using `FalloutCellSceneReader.ReadTeleport` rather than another
binary decoder. Both endpoints must be winning REFRs with DOOR bases and winning
CELL ancestry. Malformed links, deleted destinations and unowned XTEL flags
remain in the read-only `Issues` lane (`IReadOnlyList<FalloutCampaignPortalIssue>`),
with source reference/CELL, decoded destination when available, winning plugin,
header offset and original error type/message. They are not usable graph edges.
Construction retains all source issues, including unscoped malformed references,
instead of letting a disconnected DLC failure block an unrelated source route.
The graph does not open NIF/DDS/BSA assets, assemble scenes or require persistence,
reciprocity or a particular campaign.

`Find(FalloutFormKey fromCell, FalloutFormKey targetCell,
Func<FalloutFormKey, bool> canUse)` returns ordered **source door reference keys**.
It finds a minimum-door directed path; equal-length alternatives follow winning
runtime FormID order. Eligibility for both source and destination references is
queried afresh on each call. The caller supplies actual current enable/access
admission, including locks; source initially-disabled flags are not substituted
for current saved state. An unavailable eligibility owner remains an explicit
source error. Before expanding a reachable source CELL, `Find` refuses any
retained issue in that CELL, even if another valid outgoing edge or a rejecting
predicate could hide it. A destination CELL's unrelated outgoing links need not
be expanded merely to arrive there. Disconnected and unscoped issues remain
globally visible and never become inferred edges. Unowned XTEL flag `0x01`
still has no traversal support. Repeated queries do not reread or scan plugins.

An empty result means the same winning CELL only. Unreachable/blocked routes
throw with endpoint winners, explored cells and rejected source doors. No
reverse edge, exterior walk, lock bypass or teleport is invented. Same-worldspace
exterior travel remains the ordinary controller's separate movement owner.

## Contract and owned-data commands

Full-reader synthetic fixtures cover master/load-index differences, winning
targets and moved/deleted portal overrides, duplicate target declarations,
20/24/28-byte per-target CTDAs and stage/neighbor scope, malformed inputs, shared
XTEL decoding, one-way/cyclic/unreachable graphs, enable/access predicate changes,
disconnected unsupported/malformed issues, strict reachable-source refusal and
source immutability. Generated synthetic files live under the working directory
and are removed in `finally`.

```powershell
dotnet run --project .\contract-tests\FalloutPluginRuntimeProbe --configuration Release -- --test-campaign-goals
dotnet run --project .\contract-tests\FalloutPluginRuntimeProbe --configuration Release -- --audit-campaign-goals ttw $OwnedTtwRoot $OwnedFnvRoot CG04 Fallout3.esm 054285 @OwnedDependencyFolders
```

The second command reads the selected owned Escape QUST and actual selected
portal. Arguments are mod, mod root, owned game root, quest EDID, source-door
owner plugin, hexadecimal object ID and dependency folders. It reports winning
source identities/hashes, scoped target declarations, the exact directed
selected portal edge and all retained portal graph issues. A successful scoped
edge check does not claim the whole graph was admitted. Its predicate isolates
that edge for a **topology-only**
check; it does not claim current locks/enable parents or target conditions pass.
No gameplay state is created or changed, source script effects are not executed,
native assets are not loaded, and recording/input are off. Source admission is
not autonomous campaign completion, physical traversal or matched retail parity.
