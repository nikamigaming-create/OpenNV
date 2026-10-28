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

`script-capability-map` reads a completed private quest-graph audit and joins
its statement operations to literal dispatch guards in the current repository
source. It verifies the statement totals, includes source hashes and reports
whether the audit runtime build matches the current tool runtime. Output goes to
stdout. A listed guard is only a candidate: caller context, arguments, effects,
continuations, dynamic dispatch and retail behavior remain unverified. A stale
build must be re-audited before treating inspection results as current.

`caravan-source` reads every winning card and deck with the runtime source
reader and reports decode failures, raw field distributions and the source and
compiled lanes of referenced card scripts. Parsing a script is not execution;
the audit does not check card rules or whether a game can be played. Both command
outputs can contain owned-derived identifiers and belong in private storage.

`creature-sound-graph <installation>` inventories every winning CREA's CSCR,
TPLT, template flags and local sound-type declarations, including deleted
records, with hashes of their decoded record payloads and linked winners. It
reports invalid links and layouts without treating source links as
proof of inheritance precedence, execution or retail sound behavior. Output is
owned-derived and must stay private.

`find-target-graph <installation>` accounts for winning PACK records, decoding
Find source targets, locations, flags and payload hashes. It reports malformed
or unsupported source declarations and exits nonzero on failure. It does not
establish candidate eligibility, acquisition, combat, execution or retail
behavior. Its owned-derived output stays private.

`sandbox-capability-graph <installation>` reconciles winning PACK records and
reports Sandbox declarations, location inputs and the actual Sandbox reader's
acceptance or refusal for each decoded record. Declaration failures are
separate from reader refusals and cause a nonzero exit. Admission does not
prove actor reachability, execution, save continuity or retail behavior; output
derived from owned data stays private.

`actor-package-graph <installation> [source options]` maps winning actor bases
to their invariant package template owners and ordered PKID declarations, and
winning actor references to their base and parent cell. Unresolved template
choices and invalid links are reported per record; any failure yields a
nonzero exit. The graph does not evaluate conditions, choose leveled templates,
prove runtime reachability, or establish package behavior. Its output stays
private.

`light-graph <installation>` inventories every winning LIGH declaration,
including deleted winners, through the runtime reader. It preserves source
identities and payload hashes, light fields, model paths and flag distributions;
it reports per-record decoding failures and exits nonzero on failure. It does
not inspect placed references, model resolution, animation, runtime ownership
or retail lighting. Output stays private.

`settings <installation> <name-fragment> [...]` reports each matching
executable-name or winning GMST candidate and its resolved value or failure.
It continues after an individual refusal and exits nonzero if any candidate
fails. The name inventory also contains section-qualified executable strings;
a failed numeric lookup for such a string does not by itself establish that a
game setting is missing or that an INI setting is unsupported.

`dialogue-voice-topic <installation> <topic-runtime-hex> <actor-base-runtime-hex>`
lists every decoded response in the selected winning topic and attempts the
runtime's exact voice lookup for that actor base. It preserves source identities,
flags, condition counts, text hashes, resolved paths and refusal reasons. It
does not evaluate conditions, actor eligibility, templates, or retail playback;
lookup failures in this broad cross-product are not counts of broken dialogue.
The output is private owned-derived data and the command exits nonzero on any
lookup refusal.

`item-events` inventories winning inventory-item definitions, resolves attached
scripts through the runtime source owner, and counts parsed event declarations
by distinct script and item identity. It checks its denominators and reports
attachment and source failures. It separately summarizes `OnAdd` filters,
direct statement names and scripts with local declarations; these groups can
overlap, and expression calls are not included. The counts do not establish event delivery,
inventory-instance semantics, compiled execution, or retail behavior.

`menu-audit` tokenizes, parses and expands every winning menu XML resource and
reports the source path and phase of each failure. Tokenization keeps source
order without requiring balanced tags; it is not the live tile loader. The audit
exits nonzero for any failure. It does not exercise tile evaluation, menu input,
or retail behavior.

`menu-graph` independently inventories literal prefab includes from every
winning menu resource, reports unread resources, unrecognized include syntax,
missing targets and cycles, and writes its source-keyed graph to stdout. It does
not interpret tile nesting, expansion order, evaluation or retail behavior.

`menu-source-events <source> <logical-path>` assembles a candidate ordered token
sequence with source entry/exit boundaries for self-closing includes. It rejects
cycles and unsupported syntax. The command reports source and token counts; it
does not establish retail include timing or build a live tile hierarchy.

```powershell
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- quest-graph $owned tmp/lab-quests-01
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- script-capability-map tmp/lab-quests-01 .
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- caravan-source $owned
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- item-events $owned
dotnet run --project tools/OpenNV.DevelopmentLab -c Release -- menu-audit $owned
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
