# Native guest allocation and first-party state bridge

This proposed next slice uses the existing C# process-generation owner and x86
companion. It adds actual native data allocation, byte transfer and typed state
queries. Its authored fixture mutates the shared GameplayVitals owner through
native thunks. The proposal is uncompiled and unexecuted until the required
native gate confirms the source contract in both configurations.

An allocation capability carries process generation, lifetime ID, native base,
byte extent, committed extent, reserved extent and access. C# retains the exact
capability object; a forged or retired record has no admission. The native owner
uses Windows reserve/commit/protection APIs and observes actual extents before
access. Data pages have no executable permission. Byte transfers are bounded;
raw native instructions still have operating-system page granularity.

Released capabilities decommit their pages while retaining their address
reservation until generation retirement. This prevents an old native pointer
from aliasing a new object in that generation. Final retirement observes each
reservation as free and returns a zero-owner ledger before natural process exit.
The separate committed, reserved, live and total-lifetime budgets refuse work
without publishing a partial allocation.

The read-only 32-byte query view is authored for this bridge. Its capability and
compiled cdecl/stdcall/ECX thunk pointers must match the native owner. Each query
joins the actual call/callback sequence, parent, native thread and C# owner thread.
The C# handler supplies the authoritative operation and may reenter an existing
memory or native call. Creating/releasing lifetimes during an active call refuses.
An unknown operation or missing state owner faults the generation.

The view is not TESForm, Actor, NVSEInterface or an original vtable. No original
DLL admission, eight-pointer command frame, fastcall/variadic/float-result call,
game heap, script/lambda execution, camera publication, engine hook, plugin save,
campaign integration or launcher path is supplied. Such work must remain refused.
Raw native code is not sandboxed by a capability record; unauthorized page or
object mutation becomes a terminal failure at the next owned access. Concurrent
native workers remain outside the admitted callback contract.

The existing NativePluginGuestMemoryProbe broad domain gate calls the authored
arena contracts. Its focused `--test-native-guest-arena` route uses the same owner
and fixture. Memory access, genuine state mutation, consumed reentrant results,
retired pointers, quotas, protection/identity drift and complete retirement each
require actual native execution; a source/build/declaration pass cannot close
them. Every success marker retains absent original interfaces, game object
layouts, hooks and serialization and `NOT_DLL_COMPATIBILITY`.
