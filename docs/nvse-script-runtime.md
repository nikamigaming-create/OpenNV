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

Parser version 2 permits previously omitted quest owners only when their source
contains assignment syntax rejected by version 1, or argument commas rejected
by version 0. Quotes and comments cannot authorize migration. Existing locals,
quest progression and clocks are retained. Current-version saves still require
all admitted owners, and previously faulted executions are not silently retried.

## Evidence and remaining work

Synthetic execution checks cover chained writes, operator precedence, numeric
function arguments, short-circuit effects, invalid expressions, winning compiled
quest slots, migration and cold recurrence. The selected owned-source audit opens
all ten mod targets. JAM has 46 parser rejections among its 52 scripts; TTW has
61 among 1,263. These counts measure source admission, not gameplay execution.

Strings, arrays, user-defined functions, loops, callback registration and
scheduling, extended operators and unbound extension calls still need owners.
Compiled scripts without source also need a bytecode execution path. Source
admission does not establish complete command coverage, event order, settings,
UI, animation, AP behavior or save/load correctness for a mod. Ordinary flat/XR
and matched retail acceptance remain required before support claims.
