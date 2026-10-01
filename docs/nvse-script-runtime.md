# NVSE script runtime

OpenNV implements shared script semantics against its existing quest, reference
and global state owners. A mod function name or successfully loaded plugin is
not sufficient evidence of working behavior. Native DLLs that hook the original
executable need an independently implemented capability or an OpenNV port;
their presence in a selected folder does not execute them.

## Numeric expressions

Quest GameMode and MenuMode blocks share a source-ordered invocation and SCPT
clock. Multiple blocks retain locals and one instruction budget; Return ends the
remaining invocation. [MenuMode filters](https://geckwiki.com/index.php/MenuMode)
may be literal codes or current integer locals. Queries read the same published
menu frame as reference and fallback-quest execution. Supported native paused
panels and source messages supply codes; missing filtered identity fails visibly.
Transient frames retire with the world and are not campaign snapshots. The
claimed opening's menu handoff, title bootstrap and exact retail cadence remain
separate boundaries. Parser 8 migration admits only newly supported multi-block
owners while retaining existing clocks, variables and faults.

The source interpreter executes numeric `let` assignments, the direct `=` macro,
right-associative `:=` chains, `+=`, `-=`, `*=` and `/=`. Numeric `eval`
conditions and parenthesized expressions use the same variable and registered
function bindings as ordinary quest/reference scripts. There is no mod-specific
state dictionary or second executor.

Integer `%`, `&`, `|`, `<<`, `>>`, `%=`, `&=` and `|=` use signed 64-bit
operands truncated toward zero. Modulo shares multiplication/division precedence;
bitwise OR, AND and shifts bind above comparisons and below addition. Binary
`0b` and hexadecimal `0x` literals accept unsigned 32-bit values. These contracts
follow [the xNVSE 6.4.9 author implementation](https://github.com/xNVSE/NVSE/tree/6.4.9)
and [binary notation](https://geckwiki.com/index.php?title=Binary_Notation).
Undefined integer conversions, remainder and shift counts fail visibly; they
cannot wrap accidentally through host-language conversion or shift masking.

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

Parser version 7 admits the additional integer operators; version 6 added indexed
array expressions; version 4 added `$`/`ToString` string syntax in addition to named
Function headers. Previously omitted quest owners require syntax that their saved
parser version rejected: single `%`/`&`/`|` operators and their assignments before
version 7, brackets before version 6, `$` before version 4, braces before version 3,
assignments before version 2, or argument commas before version 1. Quotes and
comments cannot authorize migration. Existing locals,
quest progression and clocks are retained. Current-version saves still require
all admitted owners, and previously faulted executions are not silently retried.

## Functions, loops and events

Named [user functions](https://geckwiki.com/index.php/User_Defined_Function)
resolve through compiled SCRO bindings and winning SCPT records. Object-type
Function blocks validate parameters against declared slots. Numeric, reference
and string/array locals have independent typed invocation frames, including nested
calls; shared quest, reference and global writes still reach the existing
authoritative owners. `Call` works as a statement and inside expressions, with
typed arguments and results. Array parameters and returns retain alias identity;
temporary frame references release on successful return and failure.
`SetFunctionValue` retains the latest return value without ending execution.
Calls support up to 30 nested frames. Dynamic function references and lambdas
remain unsupported instead of becoming untyped numeric stand-ins.

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

[JohnnyGuitar render callbacks](https://geckwiki.com/index.php?title=SetJohnnyOnRenderUpdateEventHandler)
and their alias accept zero-argument source functions with a null caller.
Default registrations are process-owned and idempotent, survive menu/load
transitions, and resolve the current executor on each invocation. Removal can
unregister a function whose source body is still unsupported. Registration
requires a valid Function block; faulted handlers retain errors until explicit
removal and registration. Dispatch preserves admission order and applies
removals before pending calls; newly registered handlers start on a later frame.
The native adapter uses Godot's global pre-draw signal, including paused menus,
and disconnects on scene retirement. Headless processing does not invent render
events. A native rendered fixture checks source expressions, two extra viewports,
pause, inactivity and cold world replacement without recording frames.
The reserved flag remains zero. Nonzero fourth-argument flags in
[JohnnyGuitar 5.28](https://github.com/carxt/JohnnyGuitarNVSE/tree/5.28) select
distinct retail render phases; those phases remain unbound. Exact retail phase
ordering and cadence, final-pixel synchronization and complete JAM gameplay
still require their own evidence.

## Shared array values

[Array variables](https://geckwiki.com/index.php/Array_Variable) use distinct typed
identities in the shared value store. Packed arrays require consecutive integer
keys; numeric maps allow sparse, negative and fractional keys; string maps match
keys case-insensitively. Elements preserve numbers, strings, forms and nested
array identities. Local assignment aliases an array; `Ar_Copy` copies one level
and `Ar_DeepCopy` retains the nested graph's relationships in a new graph.
Following xNVSE 6.2.1 and later, equality compares keys and typed surface values;
nested array elements compare by identity rather than recursively.

Indexed reads, nested indexing and assignment reach that owner directly. An
indexed compound assignment evaluates its container/key once. Complete syntax
validation and short circuit behavior precede mutation. `Ar_Null`, `Ar_Construct`,
`Ar_List`, `Ar_Size`, `Ar_HasKey`, `Ar_Append`, single-key/all-element `Ar_Erase`,
`Ar_Resize`, copying and `TypeOf` share one implementation across quest, reference,
result and user-function execution. See the source contracts for
[construction](https://geckwiki.com/index.php/Ar_Construct),
[resize](https://geckwiki.com/index.php/Ar_Resize) and
[type queries](https://geckwiki.com/index.php/TypeOf).

Declared quest/reference locals retain roots independently of presentation.
Nested execution scopes protect intermediate results until caller assignments
finish, then reclaim unreachable arrays, including cyclic graphs. Scalar-only
invocations do not scan the array graph. Save v23 stores typed elements and
identities; cold restoration rebuilds roots from winning declarations and rejects
duplicate keys, invalid types, missing nested identities and unowned graphs.
Earlier supported saves, including v22 object-animation state, still load.
The store limits live allocation to 100,000 arrays and 1,000,000 total elements;
resize preflights that budget before mutation. These are OpenNV execution policy,
not a retail format-limit claim.
Array iteration, slices, sorting, pair/range syntax and further extension commands
remain visible missing capabilities. This is bounded runtime coverage, not full
NVSE or mod compatibility.

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

## Source-owned UI component state

The selected live source graph resolves UIO manifests and applies their declared
XML fragments to the owned start, HUD and inventory menu documents before prefab
expansion. Conditions are evaluated against the active plugin set, optional
fragments are admitted only when their owned resource exists, and malformed source
XML is corrected only at the narrow source boundary where the selected retail
resource requires it. The source audit verifies the selected MCM resource,
`uio/supported.txt`, `MCM_ModList` and `MCM_Options`; this does not imply that
Godot has rendered the menu.

Menu-session UI state is owned by a shared C# component store. It resolves named
and indexed tile paths, evaluates the reached source traits and copy expressions,
keeps float/string overrides in the component owner, and detaches a component and
its descendants for `UnloadUIComponent`. `GetUIFloat`, `GetUIFloatAlt`,
`GetUIString`, `SetUIFloat`, `SetUIFloatAlt`, `SetUIString`, `SetUIStringAlt` and
`SetUIStringEx` are bound for both quest and reference script execution. The UI
store is deliberately not part of campaign saves: menu components are recreated
with the menu source and later presentation owner.

Indexed perk parameter commands share the selected stack's C# owner.
[SetNthPerkEntryValue1](https://geckwiki.com/index.php/SetNthPerkEntryValue1),
`SetNthPerkEntryValue2`, matching value queries, type/entry-point queries and
`GetPerkEntryCount` preserve winning PRKE list indices, including ability entries.
One/two-float parameters and byte-sized quest stages have independent values;
non-numeric/absent slots retain their defined failure values. Unknown layouts,
non-finite values, fractional indices and undefined stage conversions stop before
mutation. Cached ability readers project current values into existing damage and
spread consumers. Parameter mutation does not supply missing entry-point
consumers, ranks, quest-perk effects or weapon/target condition scopes.

Loaded-form mutations live with the selected source stack, across world/reader
replacement; they are not written to owned files or campaign snapshots. Reopening
the stack reads its winning source defaults. Mod load/restart scripts still own
their initialization and setting changes. Typed form arguments keep reference
identity, including grouped statement operands; explicit `$` conversion retains
its separate display-name behavior.

[SetUIFloatGradual](https://geckwiki.com/index.php?title=SetUIFloatGradual) uses
the same float owner for its four documented modes: one-way interpolation,
ramp/hold/return, repeating oscillation and repeating one-way interpolation.
Omitting the duration stops a previous animation; a supplied start value also
sets the trait. Direct float writes do not stop an existing gradual clock.
Signed bare, grouped and indexed statement arguments preserve their value and
arity. Mode 4's additional engine behavior is not bound; undefined durations,
endpoints and modes fail before changing existing state.

The native UI clock uses monotonic elapsed time before this frame's scripts,
independent of gameplay Time Mult and menu pause. Unload/reset removes affected
animations; they are menu-session state and are not included in saves. XML
copy expressions now read live float overrides, including animation values.
Supported native HUD tiles read those same float overrides by canonical source
path and redraw when their revision changes. A rendered owned-reticle fixture
checks changed pixels under pause and zero time scale without retaining frames.
It establishes that bounded float bridge, not full HUD/MCM rendering or retail
timing/pixel parity. String/filename presentation, dynamic tiles and remaining
authored HUD/menu branches still need their general owners.

The current store is a source/state contract, not a complete UI implementation.
MCM registration and option APIs, dynamic component creation, screen/global
presentation bindings, rendering, ordinary flat/XR input, menu callbacks and
visible gameplay effects remain open. A passing source or synthetic probe must not
be reported as a working MCM menu or JAM gameplay.

## Evidence and remaining work

Keyboard/mouse [GetControl](https://geckwiki.com/index.php/GetControl) and
[SetControl](https://geckwiki.com/index.php/SetControl) share a C# profile table
with reference and fallback-quest execution. Source INI keyboard/mouse bytes are
read-only; -1 denotes an unassigned control, and mouse queries use the 256-based
DirectInput domain. `GetAltControl` retains the raw mouse-byte result of the
selected [xNVSE 6.4.9 input contract](https://github.com/xNVSE/NVSE/blob/6.4.9/nvse/nvse/Commands_Input.cpp).
`SetAltControl` targets that lane. Remapping swaps the first occupied binding
within the device lane. Invalid numeric arguments and unavailable device lanes
fail before mutation. A native adapter updates the ordinary movement, activation,
fire, reload, grab, jump, Pip-Boy, quick-save and aim/POV actions, releases affected
held actions and disconnects on retirement. Unknown physical keys retain the
previous owner/map. Source-bound flat input no longer intercepts Q/H for the
diagnostic wheels; complete Classic/Nikami selection and stock action behavior
remain separate work.

Changes persist through an atomic `script-config/input-controls.json` profile
overlay at session retirement, including load/title transitions and application
exit. They do not belong to campaign saves or overwrite Fallout INIs. Missing
executable defaults, joystick/gamepad adapters, GetController, additional stock
actions and full mod/controller input remain unbound. The native fixture checks
actual input-map state and events, not campaign playability or retail matching.

Synthetic execution checks cover chained writes, operator precedence, numeric and
typed string function arguments, concatenation, form naming, short-circuit effects,
invalid expressions, winning compiled quest slots, migration and cold recurrence.
Function/event checks cover recursive locals, typed return values, reference
callers, loop control, consumptive lifecycle queries, frame delays, key edges,
mutation during dispatch, failure retention and replacement with restored owners.
The native Godot audit dispatches physical key events into source functions and
verifies GameMode/paused MenuMode callbacks mutating actual reference slots.

The selected JAM source audit has 11 parser rejections among 52 scripts.
Reached execution still fails visibly.
Its reached configuration initializers now execute through the shared INI and
auxiliary owners; the next failures are concrete hit-event, actor-effect,
perk-consumer and remaining parser gaps. The synthetic storage probe covers
source-script write/readback, source precedence, overlay atomicity,
typed/public/private/indexed auxiliary values, save reload, cold restart and
New Game clearing. The UI organizer/component probes cover source injection,
scoped paths, source traits, typed writes and component detachment. The existing
mod probe's `--audit-mod-scripts` option reports the reached state and failures
against selected owned folders; the MCM source probe separately verifies the
selected MCM/UIO resources. Both are headless and have no substitute player or
presentation host. These checks do not establish JAM gameplay or a working MCM
menu.

Remaining array operations, extended operators and unbound extension calls still
need owners. JBT passes its control query; JHM passes source UI interpolation.
Both now reach `SetOnHitEventHandler`; this does not establish working bullet
time, hit markers or menus.
MCM's complete menu/settings behavior, render/hit/fire events, XR control mapping,
focus-loss input handling and callbacks while no world is active remain open.
Compiled scripts without source also need a bytecode execution path. Source
admission does not establish complete command coverage, event order, settings,
UI, animation, AP behavior or save/load correctness for a mod. Ordinary flat/XR
and matched retail acceptance remain required before support claims.
