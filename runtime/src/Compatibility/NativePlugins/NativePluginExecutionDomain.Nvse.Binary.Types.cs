namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeNvseBinaryMethod : uint
{
    Read = 1, ReadInherited = 2, Seek = 3, SeekCurrent = 4,
    Size = 5, Cursor = 6, FlushReadBuffer = 7, SelectProcedures = 8,
}

internal sealed record NativeNvseBinaryMethodDeclaration(uint Address, NativeNvseBinaryMethod Method,
    NativePluginAbi Abi, int? VirtualSlot, string Owner);
internal sealed record NativeNvseBinaryProcedures(uint Read, uint Write, uint AlternateRead, uint AlternateWrite,
    string Owner);
internal sealed record NativeNvseBinaryDeclaration(string RuntimeSha256, string PluginSha256,
    string LayoutOwner, uint Extent, uint VirtualSlots, IReadOnlyList<NativeNvseBinaryMethodDeclaration> Methods,
    NativeNvseBinaryProcedures Procedures);
internal sealed record NativeNvseBinaryAddresses(uint Image, uint Table, uint Buffer, uint Capacity,
    uint ReadProcedure, uint WriteProcedure, uint AlternateReadProcedure, uint AlternateWriteProcedure,
    ReadOnlyMemory<byte> InitialNativeImage);

// This is an original byte-output lifetime, not a byte[] pretending to be a
// completed native write. Produced and delivered prefixes are distinct.
internal interface INativeNvseBinaryOutput
{
    ulong Id { get; }
    uint Requested { get; }
    uint Actual { get; }
    uint Position { get; }
    byte[] Slice(uint position, uint count);
    void Complete();
    void Fail(Exception error);
}

internal sealed record NativeNvseBinaryResult(uint Result, INativeNvseBinaryOutput? Output,
    IReadOnlyList<NativeNvseDataField> Fields, ReadOnlyMemory<byte> WrittenBuffer, string? Diagnostic);
internal interface INativeNvseBinaryFileAuthority
{
    string BinarySourceSha256 { get; }
    string BinaryRuntimeSha256 { get; }
    uint BinaryCapacity { get; }
    void RequireBinaryCurrent();
    NativeNvseBinaryResult CallBinary(NativeNvseBinaryMethod method, uint first, uint second);
    NativeNvseBinaryResult InspectBinary();
    void RetainBinaryFailure(Exception error);
}

// A complete source class needs its genuine native CRT and constructor fields.
// The selected pure reader supplies only its buffer/cursors. It cannot implement
// this abstract owner with a HANDLE, managed pointer, null FILE* or padded image.
internal abstract class NativeNvseBinaryImageAuthority : NativeNvseSourceObjectAuthority
{
    internal abstract INativeNvseBinaryFileAuthority File { get; }
    internal abstract void RequireOriginalCrtFileCurrent();
    internal abstract byte[] ReadCompleteImage(NativeNvseBinaryAddresses addresses,
        IReadOnlyList<NativeNvseDataField> fileFields, bool alternateProcedures);
}

internal sealed class NativeNvseBinaryBinding
{
    internal ulong Generation { get; }
    internal ulong Module { get; }
    internal ulong Id { get; }
    internal NativeNvseSourceObject Contributor { get; }
    internal NativePluginGuestAllocation Image { get; set; }
    internal NativePluginGuestAllocation Buffer { get; set; }
    internal NativeNvseBinaryImageAuthority Authority { get; }
    internal IDisposable SourceLease { get; }
    internal NativeNvseBinaryAddresses Addresses { get; }
    internal byte[] PublishedImage { get; set; }
    internal byte[] PublishedBuffer { get; set; }
    internal bool AlternateProcedures { get; set; }
    internal bool Retired { get; set; }

    internal NativeNvseBinaryBinding(ulong generation, ulong module, ulong id, NativeNvseSourceObject contributor,
        NativePluginGuestAllocation image, NativePluginGuestAllocation buffer, NativeNvseBinaryImageAuthority authority,
        IDisposable lease, NativeNvseBinaryAddresses addresses, byte[] publishedImage, byte[] publishedBuffer)
    {
        Generation = generation; Module = module; Id = id; Contributor = contributor;
        Image = image; Buffer = buffer; Authority = authority; SourceLease = lease; Addresses = addresses;
        PublishedImage = publishedImage; PublishedBuffer = publishedBuffer;
    }
}

internal sealed record NativeNvseBinaryCallReceipt(ulong Callback, ulong Caller, ulong Binary,
    NativeNvseBinaryMethod Method, uint First, uint Second, uint Result, uint ActualOutput,
    uint ProducedOutput, uint DeliveredOutput, uint ProducedBuffer, uint DeliveredBuffer,
    bool Complete, string? Diagnostic);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint BinaryBegin = 0x450, BinaryOutputSlice = 0x451, BinaryOutputAck = 0x452,
        BinaryBufferSlice = 0x453, BinaryBufferAck = 0x454, BinaryEnd = 0x455;
    private readonly Dictionary<ulong, NativeNvseBinaryBinding> _nvseBinaryFiles = [];
    private readonly Dictionary<ulong, NativeBinaryPending> _nvseBinaryPending = [];
    private readonly List<NativeNvseBinaryCallReceipt> _nvseBinaryCalls = [];
    private NativeNvseBinaryDeclaration? _nvseBinaryDeclaration;
    private ulong _nextNvseBinaryFile, _nextNvseBinaryCall;
    internal IReadOnlyList<NativeNvseBinaryCallReceipt> NvseBinaryCalls => _nvseBinaryCalls.AsReadOnly();

    private sealed class NativeBinaryPending(ulong caller, NativeNvseBinaryBinding binding,
        INativeNvseBinaryOutput? output, byte[] buffer, int receipt)
    {
        internal readonly ulong Caller = caller;
        internal readonly NativeNvseBinaryBinding Binding = binding;
        internal readonly INativeNvseBinaryOutput? Output = output;
        internal readonly byte[] Buffer = buffer;
        internal readonly int Receipt = receipt;
        internal byte[]? LastOutputSlice;
        internal uint OutputProduced, OutputDelivered, BufferProduced, BufferDelivered;
    }
}
