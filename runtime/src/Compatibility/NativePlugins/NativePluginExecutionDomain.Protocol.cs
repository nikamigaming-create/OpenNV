using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginAbi : uint { Cdecl = 1, Stdcall = 2, Thiscall = 3 }
internal enum NativePluginDomainOperation : uint
{
    Hello = 1, LoadAuthored = 2, Resolve = 3, Call = 4, Unload = 5, Retire = 6,
    GuestCapabilities = 7, GuestAllocate = 8, GuestRead = 9, GuestWrite = 10, GuestRelease = 11, GuestBindState = 12, GuestStatistics = 13,
    LoadNvse = 14, NvseQuery = 15, NvseLoad = 16, NvseMessage = 17, NvseSerialization = 18, UnloadNvse = 19, NvseExpressionAbi = 20, NvseCommand = 21, NvseExpressionStatistics = 22, NvseValuesAttach = 23, NvseValuesStatistics = 24, NvseValueHeap = 25, PrivateIoPrepare = 26, NvseLocalCreate = 27, NvseLocalFill = 28, NvseLocalSeal = 29, NvseLocalRetire = 30, NvseLocalStatistics = 31, NvseObjectBind = 32, GuestSeal = 33, NvseScriptInterface = 34, NvseObjectRetire = 35, NvseObjectRefresh = 36, NvseLocalAttachScript = 37, NvseFileMethods = 38
}
internal enum NativePluginDomainMessage : uint { Request = 1, Reply = 2, Callback = 3, CallbackReply = 4, Fault = 5, StateQuery = 6, StateReply = 7, NvseCallback = 8, NvseReply = 9, IoCallback = 10, IoReply = 11 }

internal readonly record struct NativePluginModuleLifetime(uint TlsAttach, uint DllAttach, uint TlsOrder, uint DllOrder,
    uint ImportedValue, uint TlsValue, uint TlsDetach, uint DllDetach, uint Calls, uint Callbacks);
internal sealed record NativePluginModule(ulong Generation, ulong Handle, uint GuestBase, uint GuestReceiver,
    NativePluginModuleLifetime Entry, uint ExportCount);
internal sealed record NativePluginFunction(ulong Generation, ulong Module, ulong Handle, NativePluginAbi Abi, string Export, uint GuestAddress);
internal readonly record struct NativePluginCallReceipt(uint Result, int StackDelta, uint PreservedRegisters, uint ExceptionCode,
    uint NativeCalls, uint NativeCallbacks);
internal readonly record struct NativePluginCallback(ulong Generation, ulong Handle, ulong ParentCall, uint Event,
    uint First, uint Second, uint NativeThread);
internal readonly record struct NativePluginUnloadReceipt(NativePluginModuleLifetime Lifetime, bool MappingPresent);
internal sealed record NativePluginDomainFault(ulong Generation, int ProcessId, ulong Call, string Operation, string Reason,
    uint? NativeCode, string Diagnostics, bool DiagnosticsTruncated);

internal sealed class NativePluginDomainFaultException(NativePluginDomainFault fault, NativePluginExecutionDomain owner, Exception? inner = null)
    : InvalidOperationException($"Native domain {fault.Generation} failed in {fault.Operation}: {fault.Reason}", inner)
{
    internal NativePluginDomainFault Fault { get; } = fault;
    // Retain the exact child handle if bounded retirement itself fails, including
    // during startup. A retry never opens a numeric PID supplied by the caller.
    internal NativePluginExecutionDomain Owner { get; } = owner;
}
internal sealed class NativePluginDomainRefusal(uint code, uint liveModules, string reason)
    : InvalidOperationException($"Native domain refused the request ({code}): {reason}")
{
    internal uint Code { get; } = code;
    internal uint LiveModules { get; } = liveModules;
}

internal sealed partial class NativePluginExecutionDomain
{
    internal const uint ProtocolMagic = 0x444e564f;
    internal const uint ProtocolVersion = 9;
    internal const int MaximumPayload = 65536;
    internal const int NativeMaximumDepth = 8;
    private const int HeaderLength = 48;
    private const ulong CallbackBit = 1UL << 63;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly record struct Frame(NativePluginDomainMessage Kind, uint Operation, ulong Generation,
        ulong Id, ulong Parent, byte[] Payload);

    private static byte[] Payload(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Utf8, true);
        write(writer); writer.Flush();
        if (stream.Length > MaximumPayload) throw new InvalidDataException("Native-domain payload exceeds its budget.");
        return stream.ToArray();
    }
    private static void WriteText(BinaryWriter writer, string text)
    {
        if (text.IndexOf('\0') >= 0) throw new ArgumentException("Native-domain text contains NUL.");
        var bytes = Utf8.GetBytes(text); writer.Write(checked((uint)bytes.Length)); writer.Write(bytes);
    }
    private static BinaryReader Reader(byte[] bytes) => new(new MemoryStream(bytes, false), Utf8, false);
    private static string ReadText(BinaryReader reader)
    {
        var count = reader.ReadUInt32();
        if (count > MaximumPayload || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Native-domain text has an invalid extent.");
        var text = Utf8.GetString(reader.ReadBytes(checked((int)count)));
        if (text.IndexOf('\0') >= 0) throw new InvalidDataException("Native-domain text contains NUL.");
        return text;
    }
    private static void Finish(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("Native-domain reply contains unconsumed bytes.");
    }
    private static NativePluginModuleLifetime ReadLifetime(BinaryReader reader) => new(
        reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
        reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());

    private void WriteFrame(Frame frame)
    {
        var header = new byte[HeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(header, ProtocolMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)frame.Kind);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), frame.Operation);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), frame.Generation);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(24), frame.Id);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32), frame.Parent);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), checked((uint)frame.Payload.Length));
        _process.StandardInput.BaseStream.WriteAsync(header, _transaction!.Token).AsTask().GetAwaiter().GetResult();
        if (frame.Payload.Length != 0)
            _process.StandardInput.BaseStream.WriteAsync(frame.Payload, _transaction.Token).AsTask().GetAwaiter().GetResult();
        _process.StandardInput.BaseStream.FlushAsync(_transaction.Token).GetAwaiter().GetResult();
    }
    private Frame ReadFrame()
    {
        var header = new byte[HeaderLength];
        _process.StandardOutput.BaseStream.ReadExactlyAsync(header, _transaction!.Token).AsTask().GetAwaiter().GetResult();
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(40));
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != ProtocolMagic ||
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != ProtocolVersion ||
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(44)) != 0 || count > MaximumPayload)
            throw new InvalidDataException("Native-domain header or frame budget drifted.");
        var bytes = new byte[checked((int)count)];
        if (count != 0)
            _process.StandardOutput.BaseStream.ReadExactlyAsync(bytes, _transaction.Token).AsTask().GetAwaiter().GetResult();
        return new((NativePluginDomainMessage)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)),
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)), BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(16)),
            BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(24)), BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32)), bytes);
    }
}
