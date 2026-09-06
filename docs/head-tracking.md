# Head-tracking source contracts

The selected source declarations are bound; actor aiming is still unbound.
Reading a target node and limits does not complete Look, StopLook, a package
event, or the couch interaction. The live package event still fails visibly.

## Owned declarations

Float defaults retain the compiler's value/name/descriptor relationship in
both framed and optimized initializers. Optimized x87 constant loads encode
their zero/unit values directly. Values are never substituted by setting name.
Existing INI precedence remains authoritative; missing or unrecognized defaults
still fail. The five LookIK distance, angular-step and easing declarations now
read through this path.

The BPTD reader selects parts with both IK-data and head-tracking flags. BPNT
declares the tracking node; BPNN has a different role. The admitted BPND extent
retains its body-part slot, flags and degree-valued cone. Selection follows the
body-part table's slot order. A display name is optional, so BPTN cannot be a
mandatory part delimiter. Missing selected target names, invalid extents,
repeated/out-of-range slots and non-finite limits remain errors. Combat,
dismemberment and other body-part behavior are outside this reader's scope.

## Evidence and limits

Synthetic tests vary target names, cones, part ordering, optional display names,
source scalar bits and initializer layouts. They reject malformed, truncated,
ambiguous and non-finite declarations. The owned audit reads all 58 loaded BPTD
records: 15 select tracking declarations and 43 do not. These are declaration
counts only.

Private Win32 observe-mode measurements agree with all five selected setting
bits, the selected humanoid body-part slot, BPNT node and cone bits. Retail
addresses, code, records and captures remain private. The full runtime gate
passes with these readers. No new ordinary-gameplay or pose-parity result is
claimed.

## Remaining runtime owner

Look and StopLook need compiled-reference binding and actor-owned target
selection/lifetime. Head pose publication must use the source tracking bone,
its parent, authored pose and body-part cone, then apply the original angular
step/easing behavior. A request must not be converted into whole-body facing or
silently admitted without this presentation owner. Head and eye behavior,
source animation overrides, frame timing and persistence remain independent
open requirements.

Run the selected source audit with the local owned data root:

```powershell
dotnet run --project contract-tests/FalloutDialogueProbe -c Release -- --audit-look-sources $DataRoot
```
