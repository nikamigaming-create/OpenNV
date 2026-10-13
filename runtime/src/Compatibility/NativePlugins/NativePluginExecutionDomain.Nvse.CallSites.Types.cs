using OpenNV.Runtime.Content;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeSourceCallSitePhase { Declared, Published, Writable, Restored, Flushed, Retired, Closed }
internal enum NativeSourceCallSiteCodeKind : uint { Unbound, Call, Suppressed, Retired }
internal sealed record NativeSourceCallSiteCode(NativeSourceCallSiteCodeKind Kind, uint Target, string Sha256)
{
    // The source's five-byte NOP is one instruction, not an executable target
    // or a synthetic returned value. Only first-party getter sites own a RET.
    internal static NativeSourceCallSiteCode Read(uint address, ReadOnlySpan<byte> bytes, bool getter, bool retired = false)
    {
        if (bytes.Length != (getter ? 6 : 5)) throw new InvalidDataException("Code-site readback lost its complete declared extent.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var trap = true; foreach (var value in bytes) trap &= value == 0xcc;
        if (retired)
        {
            if (!trap) throw new InvalidDataException("Retired code site has no actual trap extent.");
            return new(NativeSourceCallSiteCodeKind.Retired, 0, hash);
        }
        if (trap && !getter) return new(NativeSourceCallSiteCodeKind.Unbound, 0, hash);
        if (getter && bytes[5] != 0xc3) throw new InvalidDataException("Pointer-getter publication lost its authored return.");
        if (bytes[..5].SequenceEqual(new byte[] { 0x0f, 0x1f, 0x44, 0, 0 }))
            return new(NativeSourceCallSiteCodeKind.Suppressed, 0, hash);
        if (bytes[0] != 0xe8) throw new NotSupportedException("Source code mutation has no admitted CALL or exact five-byte suppression owner.");
        return new(NativeSourceCallSiteCodeKind.Call,
            unchecked(address + 5U + BinaryPrimitives.ReadUInt32LittleEndian(bytes[1..5])), hash);
    }
}
internal sealed record NativeSourceSuppressionCold(string RuntimeSha256, string SourceIdentity, string DeclarationSha256, string CodeSha256, uint Protection);
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
    internal NativeSourceCallSiteCode? Code { get; set; }
    internal bool Suppressed => Code?.Kind == NativeSourceCallSiteCodeKind.Suppressed;
    internal int Extent => Source.Getter is null ? 5 : 6;
    internal uint? PreviousProtection { get; set; }
    internal uint Protection { get; set; }
    internal bool CachePublished { get; set; }
    internal int? SdkFailureDiagnosticStart { get; set; }
    internal uint Mutations { get; set; }
    internal uint Calls { get; set; }
    internal string? Failure { get; set; }
    internal bool Replacement => Code?.Kind == NativeSourceCallSiteCodeKind.Call && CurrentTarget != DefaultTarget;
}
internal sealed record NativeSourceCallSiteEvent(ulong Generation, ulong Module, uint Thread, ulong Callback,
    ulong Parent, NativeNvseHostCall Event, uint Site, uint Address, string RuntimeSha256,
    NativeSourceCallSitePhase Phase, uint Target, uint Action, uint? SdkResult, uint? LastError,
    uint? PreviousProtection, ulong? Receiver, uint? PointerResult, string? Failure,
    NativeSourceCallSiteCodeKind? CodeKind = null, string? CodeSha256 = null,
    uint? RequestedAddress = null, uint? RequestedExtent = null, uint? RequestedProtection = null,
    uint? Protection = null, uint? CohortAddress = null, uint? CohortExtent = null);

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
    ulong Call, ulong? Receiver, uint? Result, int? StackDelta, uint? PreservedRegisters, uint NativeGetterCalls,
    bool Suppressed = false, string? CodeSha256 = null);
