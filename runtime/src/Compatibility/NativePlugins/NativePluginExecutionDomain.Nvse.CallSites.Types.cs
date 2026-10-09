using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeSourceCallSitePhase { Declared, Published, Writable, Restored, Flushed, Retired, Closed }
internal sealed class NativeSourceCallSite(uint id, ulong generation, ulong module, uint thread, FalloutEngineCallSite source)
{
    internal uint Id { get; } = id;
    internal ulong Generation { get; } = generation;
    internal ulong Module { get; } = module;
    internal uint Thread { get; } = thread;
    internal FalloutEngineCallSite Source { get; } = source;
    internal NativeSourceCallSitePhase Phase { get; set; }
    internal ulong Lease { get; set; }
    internal uint DefaultTarget { get; set; }
    internal uint CurrentTarget { get; set; }
    internal uint? PreviousProtection { get; set; }
    internal uint Mutations { get; set; }
    internal uint Calls { get; set; }
    internal string? Failure { get; set; }
    internal bool Replacement => CurrentTarget != DefaultTarget;
}
internal sealed record NativeSourceCallSiteEvent(ulong Generation, ulong Module, uint Thread, ulong Callback,
    ulong Parent, NativeNvseHostCall Event, uint Site, uint Address, string RuntimeSha256,
    NativeSourceCallSitePhase Phase, uint Target, uint Action, uint? SdkResult, uint? LastError,
    uint? PreviousProtection, ulong? Receiver, uint? PointerResult, string? Failure);

// A complete source-class owner must join this method/body/member declaration
// to its actual receiver type and nullable pointer role. A readable four-byte
// cell or matching object extent alone cannot prove that semantic contract.
internal interface INativeNvseSourcePointerGetterAuthority
{
    void RequireSourcePointerGetter(FalloutEngineCallSite source, NativeNvseSourceObject receiver);
}

internal sealed record NativeSourceCallSiteInvocation(NativeSourceCallSite Site, NativeNvseSourceObject Receiver,
    uint ExpectedPointer, NativeNvseSourceObject? PointerOwner);
internal sealed record NativeSourceCallSiteReceipt(ulong Generation, ulong Module, uint Site,
    ulong Call, ulong Receiver, uint Result, int StackDelta, uint PreservedRegisters, uint NativeGetterCalls);
