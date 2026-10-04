# Source light and sequence owners

Parent-controlled static point lights use the same C# reference enable graph and
Godot presentation owner as other placed references. Valid managed NIF sequences
can retain clocks and text keys while containing no controlled channels. Neither
capability introduces a cell-specific presentation or success path.

## Enable authority

`FalloutPlacedLightResolver` resolves immutable source LIGH parameters and signed
REFR radius adjustments. XESP affects the shared world's applied enable state,
including opposite parents, chained parents, cross-cell references and the engine
player constant. Resolving light parameters does not replace that authority.

The ordinary native reference presentation creates an enabled light once, hides
and disables its processing when the applied state changes, and reuses that
instance after enabling. Initially disabled references are constructed only when
their source graph enables them. JSON cold restoration validates the independent
root requests before presentation.

Unit scale and the existing static point-light contract remain required. A LIGH
model now refuses explicitly: its light-node offsets, geometry and controllers
cannot be dropped merely because a point light can be constructed. Unknown flags,
invalid radius, cyclic or malformed enable graphs and independently requested
child state remain visible failures.

## Zero-channel sequences

`RuntimeNativeNifMeshBuilder` no longer rejects a sequence solely because its
controlled-block array is empty. Manager/palette binding, named sequences, text-key
ownership, admitted cycle types, positive frequency and valid time ranges still
apply. Nonempty sequences retain their existing channel validation.

The existing `RuntimeNifControllerPlayer` owns loop/finite advancement, ordered
source text-key dispatch, completion and cold consumed-key state. Each instance
has a separate clock. An empty sequence cannot change geometry or material by
inventing a channel, and a completed cold sequence cannot restart itself.

The build root owns allocated controller nodes before configuration validates
source input. If configuration refuses, existing root cleanup also frees those
nodes. Both build roots remain detached during configuration.

## Verification

The full-reader synthetic light fixture checks parent/opposite/chained/cross-cell
and player relationships, queued changes, repeated toggles and cold validation.
Negative cases retain unsupported models, scale, flags, radius and parent graphs.

The generic native light audit reads an independent XESP oracle directly from the
winning record bytes and declaring master tables. Each admitted light has the
formula `root bit XOR source opposite parity`; the player root is constant true.
For at most 12 independent roots the lab enumerates the complete settled enable
domain in Gray order. Larger domains retain their exact symbolic size and verify
the factorized root states, with exhaustive execution explicitly false. This is
a diagnostic work bound, not a runtime or source-format restriction.

The selected four-cell owned check accounts for 294 lights: 139 parent-controlled
admitted lights, 133 already unparented lights and 22 explicit refusals. The 139
lights have 11 roots and all 2,048 assignments pass 295,792 world/native checks,
including 67 late constructions, warm reuse and cold toggles of every root.
The complete ten-interior source rerun retains 58 light refusals: 18 model/controller
owners, 27 scales and 13 flags. The four-cell native cohort and ten-cell source
denominator are distinct.

The synthetic complete NIF checks looping and finite zero-channel sequences,
independent instance/prototype clocks, consumed-key cold state, completed cold
state and source drift. Eight malformed manager, palette, key, cycle, range,
frequency, name and nonempty-channel inputs refuse without leaked native nodes.
The owned wall-screen check builds four surfaces, 1,058 vertices, 734 emitted
triangles and 16 source collision triangles. Its original projector texture and
emissive material remain source-bound, and its authored empty loop dispatches
structural text keys through the existing sound/event owner.

The full publication gate includes these synthetic contracts. Selected owned
checks run privately through the existing native audit entry:

```powershell
& $godot --headless --path runtime --scene tools/NativeNifInstanceAudit/NativeNifInstanceAudit.tscn -- --empty-managed-sequences
& $godot --headless --path runtime --scene tools/NativeNifInstanceAudit/NativeNifInstanceAudit.tscn -- --owned-wall-screen $game $mod $modRoot $cell $reference $dependencyRoots
& $godot --headless --path runtime --scene tools/NativeNifInstanceAudit/NativeNifInstanceAudit.tscn -- --owned-light-parents $game $mod $modRoot $privateOutput $cells --dependencies $dependencyRoots
```

Owned inputs are read-only. Generated reports stay private and never supply
product assets or gameplay state. These checks establish bounded assembly,
enable and controller behavior; ordinary gameplay pixels, complete level support,
other state permutations and matched retail parity remain separate evidence lanes.
