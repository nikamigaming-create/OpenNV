namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeNvseFileMethod : uint { NextChunk = 1, AdvanceChunk = 2, ReadChunk = 3, Read32 = 4 }
internal sealed record NativeNvseFileThunk(uint Address, NativeNvseFileMethod Method, NativePluginAbi Abi);
internal sealed record NativeNvseFileDeclaration(string RuntimeSha256, string PluginSha256,
    string DeclarationOwner, string LittleEndianOwner, IReadOnlyList<NativeNvseFileThunk> Thunks);
internal sealed record NativeNvseFileReadResult(uint Result, ReadOnlyMemory<byte> Bytes, string? Diagnostic);

// A native ModInfo image is not a file authority. The same actual selected
// source lease and record decoder must supply its cursor and every read.
internal interface INativeNvseSourceFileAuthority
{
    string SourceFileSha256 { get; }
    void RequireSourceFileCurrent();
    NativeNvseFileReadResult CallSourceFile(NativeNvseFileMethod method, uint capacity);
}

internal sealed record NativeNvseFileCallReceipt(ulong Callback, ulong Caller, ulong Object,
    NativeNvseFileMethod Method, uint Capacity, uint Result, uint OutputBytes,
    string? Diagnostic, ulong? Transfer, bool OutputCompleted);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint SourceFileBegin = 0x420, SourceFileSlice = 0x421, SourceFileEnd = 0x422;
    private sealed class NativeSourceFileTransfer(ulong caller, NativeNvseSourceObject value,
        ReadOnlyMemory<byte> bytes, int receipt)
    {
        internal readonly ulong Caller = caller;
        internal readonly NativeNvseSourceObject Object = value;
        internal readonly ReadOnlyMemory<byte> Bytes = bytes;
        internal readonly int Receipt = receipt;
        internal uint Position;
    }
    private readonly Dictionary<ulong, NativeSourceFileTransfer> _nvseFileTransfers = [];
    private readonly List<NativeNvseFileCallReceipt> _nvseFileCalls = [];
    private NativeNvseFileDeclaration? _nvseFileDeclaration;
    private ulong _nextNvseFileTransfer;
    internal IReadOnlyList<NativeNvseFileCallReceipt> NvseFileCalls => _nvseFileCalls.AsReadOnly();
}
