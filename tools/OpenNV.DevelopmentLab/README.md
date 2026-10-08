# OpenNV development lab

This command-line tool calls the shared C# runtime directly. It reads a live
installation or the launcher's source-stack manifest. It is separate from the
game's menus and never changes the selected installation or player saves.

Run from the repository root:

```powershell
$owned = 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- corpus $owned tmp/lab-corpus-01
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- lifecycle $owned --all
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- cells $owned GSDoc GSProspector Goodsprings
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- replay $owned tools/OpenNV.DevelopmentLab/scenarios/couch-before-doc.json
```

`corpus` streams every winning plugin payload, including deleted winners,
through the runtime reader. It retains complete subrecord length histograms and
individual standalone/embedded source-body outcomes. Every discovered winning
loose/BSA resource resolves through the ordinary owner and retains complete byte
evidence, with independent original container and stored-member extents. The
fresh private output includes `winning-records.jsonl`, `winning-resources.jsonl`,
`archive-stored-extents.jsonl`, `summary.json`, `record-layouts.json`, grouped
`failures.json` and every failed identity in `failure-instances.jsonl`.
Unknown discovery remains unknown. Byte/layout accounting does not certify
decoding, command effects or runtime readiness. See [corpus accounting](../../docs/corpus-audit.md).

Add `--selection-variants` to enumerate every on/off subset of the supplied
configured catalog selections through the ordinary dependency/order/settings
owners. Each admitted subset independently audits its winning graph. Refused
selections retain their original refusal. Unconfigured packages, arbitrary
orders/settings, module toggles and native execution remain separate uninspected
dimensions; the subset sweep cannot certify them.

`quest-graph` discovers every script/condition-bearing winning record signature,
including deleted winners, orphan/empty/repeated fields and compiled-only bodies.
Each raw program field retains its original ordinal/extent/hash and must match
the independent grouped denominator. Every SCTX body has its own disposition.
It checks every SCHR/SCDA/reference extent and QUST attachment, and retains all
authored stage/objective identities and SetStage edges. It inspects every parsed
statement and both outcomes of every branch/loop predicate through the actual
runtime expression declarations and function signatures without reading values,
executing scripts, loading cells or changing saves. A statement-dispatch boundary
or absent runtime caller context remains explicit; signature admission does not
certify command effects or feasible game states. Compiled-only programs and SCDA
execution ownership are reported independently. Numeric values and loop counts
are unbounded, so this is authored alternative coverage, not an exhaustive
simulation of every possible game state. See [the audit contract](../../docs/quest-graph-audit.md).

```powershell
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- quest-graph $owned tmp/lab-quests-01
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- quest-graph $owned tmp/lab-ttw-quests-01 --mod ttw D:\OwnedMods\TTW D:\OwnedMods\xNVSE D:\OwnedMods\JIP
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- corpus $owned tmp/lab-selected-corpus-01 --mod-stack tmp/launcher-mods.private.json
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- corpus $owned tmp/lab-subsets-01 --selection-variants --mod-stack tmp/launcher-mods.private.json
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- quest-graph $owned tmp/lab-selected-quests-01 --mod-stack tmp/launcher-mods.private.json --mod-order manual
```

`--mod-stack` reads the launcher's existing JSON list of mod identities, roots
and additional roots through the same selection, dependency, order and settings
owners as launch. Use the same list for every audit of a selected game. The
default order is `automatic`; pass `--mod-order manual` when the launcher uses
manual order. These source options follow each command's positional arguments
and also work with `cell-graph`. Legacy `--mod` and its dependency roots remain
supported as the last arguments; combining it with `--mod-stack`, repeating a
source option or omitting a value is refused. Standalone sources retain their
detected Fallout 3 or New Vegas engine identity.

The explicit selections do not generate a persistent launch input. Reports include
source-stack hashes, per-program compiled hashes, all statements/predicate
outcomes and separate decoding/declaration failures. Keep outputs private in a
fresh ignored directory outside every owned input root. Exit 1 retains failures
and, for quest/cell audits, unmeasured required behavior; it does not stop
enumeration or turn recorded failures into passes. A zero corpus result is only
the measured byte/layout lane and keeps runtime readiness false.

Compare `SaveCompatibilityId` in the corpus/quest summaries and cell component
report with the selected launcher's source identity before interpreting their
results. Equal mod names without equal selected source identities do not bind
an audit to that launch.

`lifecycle` admits all references, including model-less and initially disabled
objects, to the real world state owner. It assigns distinct disposable local
values, tears down/reassembles each selected cell 30 times, and checks a JSON
roundtrip into a fresh world. `--all` visits every winning CELL, including unnamed
cells. It reports failures instead of silently dropping cells. Warm timing
measures admission of the decoded cell, not graphics or physics loading.

`cell-graph` selects every effective winning CELL by default, including exterior,
disconnected and empty cells. Its complete winner denominator retains deleted
CELL/WRLD/reference/NAVM/LAND records, disabled sources, orphan/group failures,
directed exterior portals and finite authored actor/resource alternatives.
`--seed` only anchors an optional native snapshot/sample; it does not filter the
full source scope. Use explicit `--scope component --seed plugin:hex-object`
for the previous interior diagnostic selection. Required runtime/native/state
coverage remains explicit, so source-only success cannot return readiness.
See [cell graph coverage](../../docs/cell-graph-audit.md).

Use `--compress-reports` to stream the complete CELL and component JSON into
`.json.gz` files. Every field/failure remains in the report; the compact
`summary.json` remains plain JSON and points to the complete component. No source
or scene denominator is reduced by this output option.

```powershell
dotnet tools/OpenNV.DevelopmentLab/bin/Debug/net8.0/OpenNV.DevelopmentLab.dll cell-graph $owned $freshPrivateOutput --runtime-config $configuration
dotnet tools/OpenNV.DevelopmentLab/bin/Debug/net8.0/OpenNV.DevelopmentLab.dll cell-graph $owned $freshPrivateSelectedOutput --runtime-config $configuration --mod-stack $existingLauncherModList
```

`replay` executes a small JSON scenario through `FalloutReferenceScripts`.
Available operations are `load`, `unload`, `objective`, `quest-variable`,
`reference-variable`, `furniture`, `event`, `cold-restore`, `assert-reference`,
`assert-quest`, and `assert-effects`. The supplied scenario exercises the player
sitting before Doc and restores the reference/quest state during the chair
timer. Furniture facts are test inputs, and conversation/control commands are
inspected outputs; the replay does not play dialogue or establish physical
furniture, ordinary input, a cold game process, or retail equivalence.

`script` prints the selected SCPT's owned source for local inspection. Keep
owned script text and generated reports private; they are not release assets.

Reference locals belong to the world, not to their shared SCPT definition or
rendered node. Cell unload suspends residency while retaining world/save state.
Event bytecode/source admission remains incomplete: unknown reached operations
freeze the affected instance with the executed prefix and exact error retained.
Other instances continue independently.

Record lookup uses the stack's existing indexes. Cell admission and teardown
visit that cell's references; repeat admission performs no installation scan.
Local slots and compiled event statements are reused in process. Mutable
reference state is bounded by referenced winning identities and lives until
world disposal; event programs are released on cell teardown. These are not
persistent transformed launch inputs.
