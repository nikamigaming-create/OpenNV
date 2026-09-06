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

`corpus` streams every winning plugin payload through the runtime reader,
groups subrecord layouts and lengths, parses standalone and embedded source
script bodies, and inventories every member of each selected BSA. It writes
`summary.json`, `record-layouts.json`, and `failures.json` in a fresh output
directory. Failures retain their source identities; successful inventory does
not mean the reported unsupported cases passed. Source declarations are not
compiled-bytecode execution, and BSA directory inspection is not asset decoding.
Loose-file contents and independent presentation evidence remain separate lanes.

`lifecycle` admits all references, including model-less and initially disabled
objects, to the real world state owner. It assigns distinct disposable local
values, tears down/reassembles each selected cell 30 times, and checks a JSON
roundtrip into a fresh world. `--all` visits every winning CELL, including unnamed
cells. It reports failures instead of silently dropping cells. Warm timing
measures admission of the decoded cell, not graphics or physics loading.

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
