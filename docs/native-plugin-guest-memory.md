# X86 guest-memory boundary

`X86GuestMemory` owns detached managed byte extents at unsigned 32-bit guest
addresses. It never converts them into host pointers. Mapping rejects null,
empty, overlapping, wrapping and over-budget extents. Reads and writes validate
the entire range before changing any destination, including unaligned accesses
across adjacent owners. Permissions are fixed for the mapping's lifetime.
Only the creating thread may map, read, write or dispose the owner. Disposal
clears the owned copies and refuses all later access.

The byte budget is explicit OpenNV policy, not a retail allocator contract.
No individual unmap/address reuse, cross-thread dispatch, executable-page
transition, CPU execution or engine-object projection is implemented.

The dedicated probe links this same source without Godot. Its owned-DLL audit
maps only original PE32 headers and file-backed section bytes, read-only, at
their source preferred addresses. It verifies exact byte reads, write refusal
and unchanged file identity. Virtual zero-fill, alignment padding, imports,
relocations, TLS and entry points are **not** mapped or executed. The audit is
source-memory evidence, not Windows loader admission or DLL compatibility.

```powershell
dotnet run --project .\contract-tests\NativePluginGuestMemoryProbe --configuration Release
dotnet run --project .\contract-tests\NativePluginGuestMemoryProbe --configuration Release -- --audit-owned-memory $OwnedPluginDll
```

## Unavailable execution contract

Recovered private JIP 57.30 author-source observations require expression utility
initialization; copied StringVar, ArrayVar, CommandTable, Script and Serialization
interfaces; save/load and messaging callbacks; Data interface singleton/functions;
and post-load hook installation and continuation. Its hook writes request memory
protection changes and instruction-cache flushing. An observer or decoder does
not supply execution.

There is no verified first-party isolated x86 CPU/Windows execution boundary,
NVSE ABI/callback marshalling, live engine-object layout/vtable/lifetime bridge,
or used hook continuation bound to authoritative C# gameplay owners. Those
contracts block an unchanged-original-plugin callback/object/hook demonstration.
This owner does not advertise interfaces, invoke plugin code, fabricate actors,
return callback success, install hooks or change any gameplay/save/startup owner.
