# Compiled script contracts

The private FNV reference checkout is pinned by the knowledge graph, separately
from the owned plugin hashes and OpenNV working-source hashes. It is a research
input, not a dependency or shipped source. Its matching claims have not been
independently reproduced. No reference implementation text is imported.

## Instruction framing

The C# reader walks the complete SCDA field in declaration order. Each instruction
has a little-endian 16-bit opcode and a signed 16-bit payload length. The reference
call prefix instead carries a 16-bit reference-table index, then the actual opcode
and payload length. Offsets include every header and payload byte exactly once.
Payload bytes are decoded separately; see operand and expression decoding below.
An opcode-looking word inside a payload does not begin another instruction.

Incomplete headers, negative lengths, payload overruns and repeated reference
prefixes refuse the entire field. Unknown opcodes remain explicit inventory
entries; framing them does not admit their semantics. Empty fields have no
instructions. No editor source is consulted and no source bytes are modified.

This contract is supported by the reference runner's ParseOutput behavior and
owned FO3/FNV instruction extents. Synthetic checks cover framing, explicit
references, opaque operands, truncation and invalid extents. The audit retains
every failed body and separates framing from operand decoding and execution.

## Operand and expression decoding

`FalloutCompiledScript.Decode` turns every framed instruction into a typed
statement: block begin/end with its filter operand, local declarations,
`Set` targets (local, foreign reference variable or global), `If`/`ElseIf`/
`Else`/`EndIf` skip counts, `While`/`EndWhile`, `Return`, and command or
function calls with typed parameters. Expressions are postfix token streams of
literals, locals, globals, reference variables, operators and nested calls.
`FalloutCompiledScriptCommands` holds the neutral metadata table: 38 block
types, console opcodes 0x0100–0x01CC, script opcodes 0x1000–0x127F and the
animation-group parameter names. Unknown opcodes, parameter types, stack
underflow or residual stack values refuse the body with its offset.

Reference operands are 1-based indices into the program's ordered `SCRO`/`SCRV`
table. A `SCRO` entry binds an exact form even when it has no editor ID, so
unnamed placed markers are reachable. A `SCRV` entry reads the named local at
execution time and fails closed unless it holds a valid form identity.

## Execution

`FalloutCompiledScriptVm` evaluates postfix expressions with integer/double
promotion and executes statements through the same runtime owners as source
text: quest variables, reference locals, globals, inventory, factions, actor
flags, placement and package events. Branch skip counts exclude local
declaration instructions. `Set` truncates toward zero for integer slots
(`SLSD` integer flag or short/long storage). Every invocation is entered through
the script manual-save ledger with the program SHA-256. An unsupported command
or missing owner throws with the script, offset and caller; nothing is skipped.

Dialogue results, package begin/end/change events, patrol arrival programs,
quest stages, terminals and reference scripts select compiled execution when
their source text is empty. Source text remains authoritative when present.

Form-list item operands to AddItem/RemoveItem apply the same count to each
current list member in order, using the live form-list state.

Synthetic checks cover expressions, branch skips with interleaved declarations,
foreign variables and globals. Owned checks execute all four compiled-only
programs in the selected official stacks through real state owners:

- `FalloutNV.esm:0f246d` INFO result sets its target quest variable.
- `FalloutNV.esm:1377d1` patrol arrival moves the actor to its marker.
- `LonesomeRoad.esm:00d9d5` PACK begin sets ghost state; its end program
  updates quest variables, inventory, perks, factions, teammate state,
  placement, restraint and enable state.
- `Fallout3.esm:063bfc` patrol arrival evaluates its quest-variable branch.

Run them with `--audit-compiled-scripts <installation>` in the
FalloutPluginRuntimeProbe. These checks prove individual program effects, not
event timing, every command's guards, campaign behavior or retail parity.

## Integer assignment in effect-local cells

A numeric assignment to an integer local truncates toward zero and stores the
result as a double-valued integer cell. Zero is canonical positive zero.
Floating locals retain fractions. A reference-typed local retains its separate
form representation. Assignment changes only the first matching local slot;
duplicate entries retain their own original bits and order.

The reference runner's SET behavior supports this conversion for short and long
destinations. The effect-local storage owner applies the conversion before
publishing a write. Nonfinite or out-of-Int32-domain results remain refused with
the old cell unchanged; overflow behavior has not been qualified. Initial reads
and cold restoration preserve original payloads and do not silently normalize
them. This distinction prevents a write fix from concealing malformed saves.

Synthetic checks cover positive/negative fractions, fractional values adjacent
to the Int32 bounds, negative zero, float preservation, refusal without mutation
and exact cold-restored bits. The owned probe applies those same values to
transient cells selected from actual script-effect declarations, without
executing events, spawning actors or changing the installation.

The knowledge graph links these contracts to their reference entry points,
C# owners and test sources. Those reviewed relationships do not establish
campaign, event-order or retail parity.
