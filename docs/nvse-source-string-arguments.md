# Source-string script arguments

`GetNumericGameSetting` and `SetNumericGameSetting` declare an explicit
`SourceString` parameter. A simple unqualified source name can be literal text
when pure compiled/frame metadata proves it has no value owner. Quoted strings
remain literals. Declared variables, compiled forms/globals, qualified paths,
grouped expressions, function results and indexed values retain their typed
owners. Numeric, form and array values are refused by this string parameter.
Other `String` parameters and ordinary unknown operands keep their existing
behavior; a failed variable read never becomes literal text.

The expression interpreter builds deferred arguments before executing them.
An inactive branch does not read a value, inspect source-name ownership, run an
index or invoke a function. Active receivers/arguments preserve their existing
order and execute once. Root/result/function execution and fallback quest
execution use the same source-string resolver, including statement setters.

Compiled slot identities and source local kinds still select the value owner.
String text remains in `FalloutScriptValueStore`; saved numeric handles retain
their identity across restoration. This is a runtime argument capability, with
no new lexical admission or parser-version migration. Existing failed prefixes
remain latched rather than restarting earlier stateful statements.

## Numeric default source ownership

`RuntimeLiveContentSource` stamps its in-process `FalloutPluginSource` records
with their source owner and exposes an immutable ordered collection. The link
is internal and is not serialized with file provenance. `FalloutPluginStack`
admits the complete ordered source graph and
rejects mixed, partial or changed owner provenance before opening its readers.
Its numeric-setting owner retains that graph's executable-default source and
never consults an ambient `RuntimeLiveContentSource.Current`.

Winning GMST records take precedence over admitted executable declarations.
Default association failures and malformed/non-finite payloads remain visible.
Source-free synthetic stacks resolve their own GMST records. Per-stack mutations
remain outside campaign saves: another stack reads its own source declarations.
Profile selection or source changes require constructing the selected graph;
existing cached declarations cannot silently change to a different installation.

## Checks and limits

`ScriptSourceStringProbe` checks explicit signature scope, typed refusal,
unresolved inactive names, parsing before effects, once-only grouped/indexed
arguments, winning compiled slots, actual function-frame string parameters,
root/fallback setters, serialized string handles, cold recurrence and retained
failed prefixes. It also verifies a new stack does not retain numeric overrides.

The selected owned JAM 4.6 proof executes `JDCScript` through the real quest,
reference, function and event owners with no ambient content source. It checks
all eleven actual numeric cache slots against nine admitted executable default
associations and two winning GMST payloads, and verifies their compiled literal
arguments. Separate complete source graphs retain independent default reads and
mutations; incomplete, mixed, reordered and changed provenance are refused, and
the source vector's serialization excludes its in-process owner.

The initializer then reaches its authored `GetNumericINISetting` query, which
remains unowned. The admitted callback's equipped-object and later inventory
extra-reference queries remain separate missing capabilities. This component
proof has no player input or visible UI acceptance and does not establish JAM,
TTW, campaign, retail parity or physical OpenXR support.

Focused invocations from the workspace:

```powershell
dotnet run --project contract-tests/FalloutPluginRuntimeProbe/FalloutPluginRuntimeProbe.csproj --configuration Release --no-build -- --test-source-string
dotnet run --project contract-tests/FalloutPluginRuntimeProbe/FalloutPluginRuntimeProbe.csproj --configuration Release --no-build -- --audit-jdc-game-settings 'D:/OpenNV-Mods/jam-4.6' 'D:/SteamLibrary/steamapps/common/Fallout New Vegas' @dependencies
```

The owned invocation uses MCM 1.5.1, JIP LN 57.30, JohnnyGuitar 5.28, kNVSE 37,
Stewie 10.00 and INI 2.1, UIO 2.30 and xNVSE 6.4.9 in separate selected folders.
Owned source text, executable bytes, assets and private audit files are never
repository inputs. Recording stays off throughout these checks.
