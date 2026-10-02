# NVSE postfix reference calls

The shared C# expression owner can call an already owned reference method on a
typed form returned by a grouped expression, array element, or preceding method.
For example, the synthetic contracts exercise `(Producer).Link.Read` and
`references[Index].Read`. Static compiled names such as `Quest.local` and
`PlayerRef.GetAV` retain their existing binding path.

## Execution contract

- `FalloutScriptValueContext.ReferenceFunction` supplies argument metadata
  separately from invocation. Parsing a complete expression does not execute
  the receiver, method, argument functions, or assignments.
- Optional argument classification uses compiled form/local-kind metadata,
  rather than reading a variable's current value during parsing. Inactive
  optional arguments, including unresolved inactive names, remain unevaluated.
- The receiver evaluates once, before method arguments. A receiver must retain
  a non-null `Form` value; strings, arrays, ordinary numbers and arithmetic
  results cannot substitute a reference identity. An inactive logical branch
  does not evaluate its receiver, index, arguments, or method.
- The reference executor resolves the value through the selected plugin stack
  and existing world owners. It requires a placed reference or the engine
  player for its current reference methods. The quest fallback supports its
  existing auxiliary owner and player actor-value host; another actor has no
  fallback actor-value authority.
- `GetSelf`, `GetSelfAlt`, `GetActionRef` and `GetKiller` preserve typed form
  results. Existing numeric comparisons still read their numeric identities.
  Method argument kinds, variadic extent and read-only metadata retain the
  function owner's existing contract.
- Grouped, indexed and signed command arguments use the same expression
  parser to determine the complete postfix extent. The statement host receives
  one result, rather than separate receiver and method tokens.
- Parser version 9 allows an omitted legacy quest owner only when its source
  had a lexical postfix dot rejected before version 9. Quoted text, comments,
  decimal points and static qualified names do not grant migration. Legacy
  failure recovery refuses postfix prefixes whose evaluation it cannot prove
  safe to repeat. Existing error and executed-prefix state remain authoritative.

Missing methods still fail visibly. This change does not provide inventory
stack references, equipped-item queries, weapon-stat queries, iterator
dereferencing, key/value pairs, anonymous function captures, or dynamic
reference callers for `Call`.

## Proof and limits

`ScriptPostfixProbe` exercises once-only receiver/index/argument order,
short-circuit behavior, chaining, malformed refusal, command grouping and
conservative legacy retry. Synthetic ESM/ESP fixtures execute through the real
winning compiled slots, reference enable state, auxiliary typed forms and
activation identity. Serialized reference/value/auxiliary and fallback quest
owners continue identically after restoration. An unowned equipped-item method
retains its completed prefix and error without recurrence replay.

The selected private JAM 4.6 audit uses `JustAssortedMods.esp` SHA-256
`cfdc2b1807a57c8861335858df96a2d08e546f93dfd8c390e8f5905f2694d8de`, the owned
New Vegas installation, MCM 1.5.1, and the selected xNVSE/JIP/JohnnyGuitar/kNVSE/
Stewie/UIO input packages. Source admission changes from 41 to 46 of 52 scripts.
The newly admitted definitions are `JDCMainLoopEventHandler`,
`JWHMainLoopEventHandler`, `JWHMenuOnKeyDownEventHandler`, `JWHDropMenuUDF`, and
`JLMySICustomSortUDF`. This is an admission result, not module gameplay support.

The six remaining parser failures are the anonymous-function block ownership
in `JVOScript`, `JWHScript`, and `JLMInventoryEventHandler`, and key/value-pair
syntax in `JWHMenuEventHandler`, `JLMCrosshairEventHandler`, and
`JLMScrollingEventHandler`. The later iterator dereference in the loot callback
also lacks execution semantics.

The 360-frame owned initialization audit now passes the old JDC callback
registration barrier and reports the next reached initializer failure for the
bare-name numeric setting `fUnaimedSpreadPenalty`. The admitted JDC callback
reports an unbound expression at its equipped-object query before its later
postfix equipped-item query can execute. Hit-event registration and `Dispel`
remain unowned for the other previously faulted modules. The nine MCM
initializers each complete 360 executions in this headless audit, which has no
ordinary player input, player gameplay host, or visible MCM acceptance.

No matched retail/OpenNV parity, ordinary gameplay, physical OpenXR, full JAM,
TTW campaign, or Benny behavior acceptance follows from these checks. Owned
source text and private audit files are not repository inputs.
