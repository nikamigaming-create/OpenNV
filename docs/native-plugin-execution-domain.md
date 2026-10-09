# Native x86 execution domain

This first-party owner admits an authored diagnostic module in a separate x86
Windows process. C# owns process generations, source identities, function and
module capabilities, call/callback nesting, budgets, faults and retirement.
The C++ companion adapts the Windows loader and a narrow x86 ABI. It supplies no
gameplay, game object layout, script interpreter or replacement plugin.

The existing `X86GuestMemory` owner remains detached managed data. Those byte
copies are not executable pages and do not become native addresses. This domain
uses the operating system's actual loader; it neither copies a game executable
into executable memory nor runs retail game code as gameplay authority.

## Authored diagnostic boundary

`LoadAuthoredModule` requires an unmanaged PE32/I386 DLL and an exact SHA256,
holds its original file read-only until retirement, and validates a first-party
diagnostic declaration after actual loader admission. The companion uses an
absolute path and `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32`.
Windows performs imports, relocations, TLS and entrypoint execution. A module
with failed imports or false DLL entry receives no module capability.

This diagnostic module ABI is not NVSE. Unchanged third-party plugin admission
requires a separate source-bound ABI owner, rather than rewriting that DLL to
export the diagnostic declaration. Campaign startup now starts this companion
through its separate source-bound plugin owner after real input admission.
Completed original Load, PostLoad, game-object, hook and serialization behavior
remain unaccepted.

Calls admit exactly two DWORD arguments and a DWORD return through cdecl,
stdcall or thiscall. Thiscall uses a real module-owned ECX receiver. The shim
observes stack cleanup and EBX/ESI/EDI preservation. An ABI violation or structured
exception faults the generation; recovery of the shim's local stack only permits
reporting failure. Wild frame-pointer corruption may terminate the child before
a structured receipt, which remains a transport/native failure.

Callback pointers reside in the x86 companion, not the x64 C# process. Both
cdecl and stdcall callbacks carry scalar messages into the creating C# thread.
The native callback pump accepts nested calls while waiting for the C# reply.
Every message retains generation, call, callback and parent identity. Unload,
callback replacement and process retirement refuse an active call. Foreign
native callback threads, missing handlers, handler failures, stale capabilities,
malformed IPC, excessive nesting and excessive callback work never produce a
valid completed-call receipt.

Fault envelopes must match generation, waiting request ID, callback parent and
operation before C# accepts their nonzero native error code and nonempty reason.
Native failures retain the first failed nested request through callback unwind;
they cannot be relabeled as the outer call. Seven separately compiled authored
child fixtures exercise a correlated fault, wrong ID/parent/operation/generation,
zero code and empty reason through the actual C# process/pipe owner.

Each complete transaction has one deadline including all nested calls. Its
watchdog can stop only the exact `Process.Start` child. Trusted C# callbacks run
synchronously on their owner thread: a watchdog can retire the native child
but cannot preempt a blocked managed callback. Asynchronous callback queues,
native worker scheduling, generalized signatures, allocation quotas and engine
state/object synchronization are outside this slice.

Normal unload requires actual TLS and DllMain detach once and an absent image
mapping after `FreeLibrary`. Natural process retirement additionally requires
exit code zero and a fully drained clean diagnostic stream. Fault termination
does not claim orderly DLL detach. A failed bounded retirement retains the exact
child handle through its fault exception for another bounded cleanup attempt;
no arbitrary PID, process tree, debugger or other game is stopped.

## Native gate

The existing `NativePluginGuestMemoryProbe` links this same owner. The required
runtime gate builds and executes its `--test-native-domain` lane in Debug and
Release, using only authored fixtures, with a
genuine TLS scalar/callback, import dependency, DLL entry, scalar ABI calls,
mutated ECX receiver and nested cdecl/stdcall callbacks. It separately checks
missing imports, false entry, declared-convention mismatch, bad cleanup,
register corruption, native exception, foreign-thread callback, absent/throwing
C# callback, reentrant depth, callback budget, native timeout, stale generations,
malformed transport and natural module/process retirement. It verifies the
unchanged bytes of the compiled authored inputs; compilation itself is not an
execution result.

Both actual x64 managed configurations and the full required runtime gate pass.
The native build restores all
changed process environment variables before the managed build/run, including
the x86 toolchain's Platform selection. Its generated directory carries an
import exclusion before compilation, so binary objects cannot enter Godot's
mesh importer. Selected owned xNVSE/JIP/JohnnyGuitar
source-memory audits pass with unchanged hashes and without loading those DLLs.

```powershell
.\scripts\Build-NativePluginDomain.ps1 -Configuration Release
dotnet run --project .\contract-tests\NativePluginGuestMemoryProbe --configuration Release -- --test-native-domain .\runtime\generated\native-plugins\Release\opennv_plugin_domain.exe .\runtime\generated\native-plugins\Release\fixtures
```

The gate's pass marker explicitly retains `unchangedPluginInitialization=absent`,
`gameObjects=absent`, `engineHooks=absent`, `serialization=absent` and
`NOT_DLL_COMPATIBILITY`. It cannot close JIP, JohnnyGuitar, TTW, JAM, compiled
SCDA, a campaign or a parity requirement.

## Required next unchanged-plugin proof

Original JIP runtime Load requires expression utilities, StringVar, ArrayVar,
CommandTable, Script, Serialization, messaging and Data/inventory/lambda
interfaces. Its real PostLoad installs broad hooks and game/command patches.
Neither an editor-mode bypass nor Query/command registration satisfies Load.
JohnnyGuitar likewise needs its actual runtime patches, settings, events and
serialization, including render-hook continuation.

The next owner must reduce versioned address, memory, object and ABI obligations
from selected owned inputs and reviewed author declarations. All native backing
objects and first-party callable thunks must join authoritative C# owners. Retail
instruction bytes are not executable authority. An unknown layout, interface,
patch target, original callback or continuation remains a refusal.

The first actual plugin demonstration needs unchanged original Load and PostLoad,
a genuine C#-owned player/weapon/ammunition projection, an original command read
before/after ordinary actions, one original registered event delivered in its
source order, and a used original installed engine hook with real continuation.
JIP's ammo/fire path joins shared player and NPC `ConsumeShot` owners; object
identities, callback cardinality, authoritative ammunition and cold retirement
must all agree. The domain gate is preparation for that proof, not its substitute.

Primary API references: [Windows process interoperability](https://learn.microsoft.com/en-us/windows/win32/winprog64/process-interoperability),
[LoadLibraryExW](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexw),
[DLL entry restrictions](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-entry-point-function),
[cdecl](https://learn.microsoft.com/en-us/cpp/cpp/cdecl?view=msvc-170),
[stdcall](https://learn.microsoft.com/en-us/cpp/cpp/stdcall?view=msvc-170),
[thiscall](https://learn.microsoft.com/en-us/cpp/cpp/thiscall?view=msvc-170),
and [PE format](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format).
