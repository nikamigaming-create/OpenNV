# NVSE script runtime

OpenNV implements shared script semantics against its existing quest, reference
and global state owners. A mod function name or successfully loaded plugin is
not sufficient evidence of working behavior. Native DLLs that hook the original
executable need an independently implemented capability or an OpenNV port;
their presence in a selected folder does not execute them.

## Numeric expressions

The source interpreter executes numeric `let` assignments, the direct `=` macro,
right-associative `:=` chains, `+=`, `-=`, `*=` and `/=`. Numeric `eval`
conditions and parenthesized expressions use the same variable and registered
function bindings as ordinary quest/reference scripts. There is no mod-specific
state dictionary or second executor.

The contract follows the documented [NVSE expression precedence and numeric
logical results](https://geckwiki.com/index.php/NVSE_Expressions),
[Let](https://geckwiki.com/index.php/Let) and
[Eval](https://geckwiki.com/index.php/Eval). The implemented logical contract is
xNVSE 6.2.5 and later: numeric OR returns the selected operand, and numeric AND
returns zero or its right operand. Existing vanilla `set`/`if` expressions retain
their boolean results. Inactive branches do not invoke functions or touch state.

The whole numeric expression is parsed before any function or assignment runs.
A failed calculation does not write its destination, including non-finite
intermediate arithmetic that would otherwise be hidden by a comparison. Reached
runtime failures remain visible and retain the executed statement prefix; they
are not converted into successful no-ops.

Parser version 4 also admits `$`/`ToString` string syntax in addition to named
Function headers. Previously omitted quest owners require syntax that their saved
parser version rejected: `$` before version 4, braces before version 3,
assignments before version 2, or argument commas before version 1. Quotes and
comments cannot authorize migration. Existing locals,
quest progression and clocks are retained. Current-version saves still require
all admitted owners, and previously faulted executions are not silently retried.

## Functions, loops and events

Named [user functions](https://geckwiki.com/index.php/User_Defined_Function)
resolve through compiled SCRO bindings and winning SCPT records. Object-type
Function blocks validate parameters against declared slots. Numeric, reference
and string locals have independent typed invocation frames, including nested
calls; shared quest, reference and global writes still reach the existing
authoritative owners. `Call` works as a statement and inside expressions, with
typed string arguments and results. `SetFunctionValue` retains the latest return
value without ending execution. Calls support up to 30 nested frames. Arrays,
dynamic function references and lambdas remain unsupported instead of becoming
untyped numeric stand-ins.

`while`/`loop`, nested `break` and `continue` execute with branch-stack restoration.
Malformed nesting is rejected before execution. A shared 100,000-statement
budget bounds a top-level invocation including nested calls. Exceeding it retains
the reached prefix and reports divergence; this guard is OpenNV policy, not a
retail instruction-limit claim.

[GetGameRestarted](https://geckwiki.com/index.php/GetGameRestarted) consumption
belongs to each source script and application session. Replacing Godot scenes or
restoring saved state cannot reset it. [GetGameLoaded](https://geckwiki.com/index.php/GetGameLoaded)
is consumed per script after a successful saved-game load. A changed selected
plugin graph creates a new logical runtime session. New-game-only load signalling
still needs retail evidence. Lifecycle query state is separate from saved quest
locals and clocks.

[Main-loop callbacks](https://geckwiki.com/index.php/SetGameMainLoopCallback)
retain script/caller identities, delay and mode flags. Each eligible native frame
advances the delay; GameMode and paused MenuMode select their respective callbacks.
Flag 8 removes registrations on load/main-menu entry. Surviving registrations
bind to the newly restored executor, without retaining delegates into retired
worlds. Registration during dispatch begins on a later frame; removal takes
effect before a pending call. Exact retail ordering and mixed-mode delay cadence
remain unverified.

[Key-down](https://geckwiki.com/index.php/SetOnKeyDownEventHandler) and
[key-up](https://geckwiki.com/index.php/SetOnKeyUpEventHandler) handlers receive
physical DirectInput IDs through a shared C# event owner and a Godot adapter.
Repeated down events do not create extra edges. The adapter observes keyboard
and mouse input independently of gameplay control consumption. The owned JAM
source also removes all keys for a handler by omitting the key argument; that
removal form is implemented. Failed callbacks retain a visible error and stop
retrying their prefix. Explicit re-registration can replace the failed entry.

## INI and auxiliary state

[GetINIFloat](https://geckwiki.com/index.php/GetINIFloat),
[GetINIString](https://geckwiki.com/index.php/GetINIString),
[SetINIFloat](https://geckwiki.com/index.php/SetINIFloat) and
[SetINIString](https://geckwiki.com/index.php/SetINIString) use the selected
source graph's logical `Data/Config/{filename}` resource for defaults. An
omitted filename is the calling script plugin's filename with an `.ini`
extension. `Section:Key` (and the equivalent admitted separators) is matched
case-insensitively. Missing strings and malformed/missing floats return the
reached zero values. Writes are copy-on-write into `script-config` beside the
profile save, retain unrelated source lines and use a relative-path check plus
an atomic replacement; the selected game and mod folders remain read-only.

[JIP auxiliary variables](https://geckwiki.com/index.php?title=Auxiliary_Variable)
are owned by a form/reference and the winning calling script plugin. `*_name`
is temporary/public, `_name` permanent/public, `*name` temporary/private and
`name` permanent/private. Float, form and string elements retain their types;
missing or wrong-type reads return the reached zero value. Setters use index 0
by default, index `-1` appends, and getters use `-1` for the last element.
Explicit owner forms and the reached alias commands are bound in both quest and
reference execution through the same storage object.

Permanent variables are included in the validated quest-script save snapshot.
Restoring a save replaces permanent variables while retaining temporary state
in the current session; a new process starts with no temporary variables, and
New Game clears both auxiliary lifetimes. INI overlays intentionally survive
process restart because they are profile configuration rather than save state.

## Evidence and remaining work

Synthetic execution checks cover chained writes, operator precedence, numeric and
typed string function arguments, concatenation, form naming, short-circuit effects,
invalid expressions, winning compiled quest slots, migration and cold recurrence.
Function/event checks cover recursive locals, typed return values, reference
callers, loop control, consumptive lifecycle queries, frame delays, key edges,
mutation during dispatch, failure retention and replacement with restored owners.
The native Godot audit dispatches physical key events into source functions and
verifies GameMode/paused MenuMode callbacks mutating actual reference slots.

The selected JAM source audit still has 21 parser rejections among 52 scripts.
Its reached configuration initializers now execute through the shared INI and
auxiliary owners; the next failures are concrete render-event, actor-effect,
perk-mutation, UI-component and remaining parser gaps. The synthetic storage
probe covers source-script write/readback, source precedence, overlay atomicity,
typed/public/private/indexed auxiliary values, save reload, cold restart and
New Game clearing. The existing mod probe's `--audit-mod-scripts` option reports
the reached state and failures against selected owned folders. It is headless
and has no substitute player or presentation host. These checks do not establish
JAM gameplay or a working MCM menu.

Arrays, extended operators and unbound extension calls still need owners.
MCM's complete menu/settings behavior, render/hit/fire events, XR control mapping,
focus-loss input handling and callbacks while no world is active remain open.
Compiled scripts without source also need a bytecode execution path. Source
admission does not establish complete command coverage, event order, settings,
UI, animation, AP behavior or save/load correctness for a mod. Ordinary flat/XR
and matched retail acceptance remain required before support claims.
