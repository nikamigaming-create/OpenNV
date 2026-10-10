using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeSourceFieldMethodPhase { Declared, Published, Retired, Closed }
internal enum NativeSourceFieldValueRole { Scalar, Object, Buffer, Global }
internal sealed class NativeSourceFieldMethod(uint id, ulong generation, ulong module, uint thread,
    FalloutEngineFieldMethod source, NativePluginAbi abi, NativeNvseSourceClass? receiverClass)
{
    internal uint Id { get; } = id;
    internal ulong Generation { get; } = generation;
    internal ulong Module { get; } = module;
    internal uint Thread { get; } = thread;
    internal FalloutEngineFieldMethod Source { get; } = source;
    internal NativePluginAbi Abi { get; } = abi;
    internal NativeNvseSourceClass? ReceiverClass { get; } = receiverClass;
    internal NativeSourceFieldMethodPhase Phase { get; set; }
    internal ulong Lease { get; set; }
    internal uint Target { get; set; }
    internal uint Calls { get; set; }
    internal string? Failure { get; set; }
    internal List<NativeSourceFieldBorrow> Borrows { get; } = [];
}

// This value keeps native aliases as aliases. No allocation or copied buffer
// is substituted for a pointer returned by an actually published source field.
internal sealed record NativeSourceFieldValue(uint Bits, NativeSourceFieldValueRole Role,
    NativeNvseSourceObject? Object = null, NativePluginGuestAllocation? Buffer = null,
    NativeEngineGlobal? Global = null);
internal sealed class NativeSourceFieldMethodInvocation(NativeSourceFieldMethod method,
    NativeNvseSourceObject? receiver, NativeSourceFieldValue expected)
{
    internal NativeSourceFieldMethod Method { get; } = method;
    internal NativeNvseSourceObject? Receiver { get; } = receiver;
    internal NativeSourceFieldValue Expected { get; } = expected;
    internal bool Returned { get; set; }
    internal ulong Callback { get; set; }
}
internal sealed record NativeSourceFieldBorrow(NativeSourceFieldMethod Method, NativeNvseSourceObject Owner,
    NativePluginGuestAllocation Allocation, uint Pointer, NativeSourceFieldValueRole Role, string SourceOwner);
internal sealed record NativeSourceFieldMethodEvent(ulong Generation, ulong Module, uint Thread, ulong Callback,
    ulong Parent, NativeNvseHostCall Operation, uint Method, uint Entry, string RuntimeSha256,
    string BodySha256, NativeSourceFieldMethodPhase Phase, ulong? Receiver, uint? Result,
    NativeSourceFieldValueRole? Role, ulong? Allocation, string? Failure);
internal sealed record NativeSourceFieldMethodReceipt(ulong Generation, ulong Module, uint Method,
    ulong Call, ulong Callback, ulong? Receiver, uint Result, NativeSourceFieldValueRole Role,
    ulong? Buffer, int StackDelta, uint PreservedRegisters);

internal sealed record NativeSourceFieldRegion(int Offset, int Bytes, NativeSourceFieldValueRole Role,
    string Owner, bool Nullable = false);
