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
Payload bytes are opaque to this reader. An opcode-looking word inside a payload
does not begin another instruction.

Incomplete headers, negative lengths, payload overruns and repeated reference
prefixes refuse the entire field. Unknown opcodes remain explicit inventory
entries; framing them does not admit their semantics. Empty fields have no
instructions. No editor source is consulted and no source bytes are modified.

This contract is supported by the reference runner's ParseOutput behavior and
owned FO3/FNV instruction extents. Synthetic checks cover framing, explicit
references, opaque operands, truncation and invalid extents. The audit retains
every failed body and separates framing from operand decoding and execution.
It does not introduce a compiled VM or make SCTX execution compiled authority.

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
