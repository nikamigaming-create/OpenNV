using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginCrtSupportKind : uint { Buffer = 1, BufferPointers = 2, Lock = 3, Unlock = 4, Position = 5, Close = 6 }
internal enum NativePluginCrtBufferOwner : uint { None = 0, OriginalModule = 1, SharedNativeHeap = 2 }
internal sealed record NativePluginCrtSupportReceipt(ulong Sequence, ulong Generation, ulong Parent,
    ulong Provider, ulong Route, uint Stream, NativePluginCrtSupportKind Kind, long Result, uint LastError,
    uint Buffer, uint Length, NativePluginCrtBufferOwner BufferOwner, ulong Allocation,
    uint BaseCell, uint PointerCell, uint CountCell, uint BaseValue, uint PointerValue, int CountValue,
    uint LockDepth, bool PositionAvailable, ulong PositionBits);
internal sealed record NativePluginCrtSupportState(ulong Provider, ulong Route, uint Stream,
    uint Buffer, uint Length, NativePluginCrtBufferOwner BufferOwner, ulong Allocation,
    uint BaseCell, uint PointerCell, uint CountCell, uint LockDepth);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint CrtSupportCallback = 24;
    private readonly Dictionary<uint, NativePluginCrtSupportState> _crtSupport = [];
    private readonly Dictionary<uint, NativePluginCrtStream> _crtSupportClosing = [];
    private readonly List<NativePluginCrtSupportReceipt> _crtSupportReceipts = [];
    internal IReadOnlyList<NativePluginCrtSupportReceipt> NvseCrtSupportReceipts => _crtSupportReceipts.AsReadOnly();

    private byte[] DispatchPrivateCrtSupport(ulong parent, BinaryReader reader)
    {
        var provider = reader.ReadUInt64(); var route = reader.ReadUInt64(); var stream = reader.ReadUInt32();
        var kind = (NativePluginCrtSupportKind)reader.ReadUInt32(); var result = reader.ReadInt64(); var lastError = reader.ReadUInt32();
        var buffer = reader.ReadUInt32(); var length = reader.ReadUInt32(); var bufferOwner = (NativePluginCrtBufferOwner)reader.ReadUInt32();
        var allocation = reader.ReadUInt64(); var baseCell = reader.ReadUInt32(); var pointerCell = reader.ReadUInt32(); var countCell = reader.ReadUInt32();
        var baseValue = reader.ReadUInt32(); var pointerValue = reader.ReadUInt32(); var countValue = reader.ReadInt32();
        var depth = reader.ReadUInt32(); var hasPosition = reader.ReadUInt32(); var bits = reader.ReadUInt64(); Finish(reader);
        if (provider == 0 || stream == 0 || !_crtProviders.ContainsKey(provider) || !Enum.IsDefined(kind) || !Enum.IsDefined(bufferOwner) ||
            hasPosition > 1 || hasPosition == 0 && bits != 0 || length == 0 != (buffer == 0) ||
            buffer == 0 != (bufferOwner == NativePluginCrtBufferOwner.None) ||
            (bufferOwner == NativePluginCrtBufferOwner.SharedNativeHeap) != (allocation != 0))
            throw new InvalidDataException("CRT buffer/lock receipt lacks its actual selected provider/caller extent.");
        _crtSupport.TryGetValue(stream, out var previous);
        if (kind == NativePluginCrtSupportKind.Close)
        {
            // Close has already invalidated the real FILE even on flush failure.
            // The preceding ordinary close receipt remains the failure authority.
            if (_crtStreams.ContainsKey(route) || !_crtSupportClosing.TryGetValue(stream, out var closed) || closed.Route != route || closed.Provider != provider ||
                previous is not null && (previous.Route != route || previous.Provider != provider) ||
                depth != 0 || buffer != 0 || baseCell != 0 || pointerCell != 0 || countCell != 0)
                throw new InvalidDataException("CRT buffer closure has an absent, still-live or locked FILE owner.");
            _crtSupport.Remove(stream); _crtSupportClosing.Remove(stream);
        }
        else
        {
            if (!_crtStreams.TryGetValue(route, out var file) || file.Address != stream || file.Provider != provider ||
                previous is not null && (previous.Route != route || previous.Provider != provider))
                throw new InvalidDataException("CRT support operation targets a foreign/closed FILE lifetime.");
            if (bufferOwner == NativePluginCrtBufferOwner.SharedNativeHeap &&
                (!_nvseHeapLifetimes.TryGetValue(allocation, out var lease) || lease.Generation != Generation || lease.RetirementCallback is not null ||
                 buffer < lease.Address || (ulong)buffer + length > (ulong)lease.Address + lease.Length))
                throw new InvalidDataException("CRT caller buffer has no exact live authoritative native allocation.");
            if (bufferOwner == NativePluginCrtBufferOwner.OriginalModule &&
                (_nvsePlugin is null || _nvsePlugin.Generation != Generation || buffer < _nvsePlugin.Image))
                throw new InvalidDataException("CRT caller buffer has no retained original module publication.");
            if (bufferOwner == NativePluginCrtBufferOwner.OriginalModule) RequireCrtSourceBuffer(buffer, length);
            var oldDepth = previous?.LockDepth ?? 0;
            if (kind == NativePluginCrtSupportKind.Lock ? depth != checked(oldDepth + 1) :
                kind == NativePluginCrtSupportKind.Unlock ? oldDepth == 0 || depth != oldDepth - 1 : depth != oldDepth)
                throw new InvalidDataException("CRT lock/unlock lost its actual calling-thread nesting.");
            if (previous is not null && kind is not NativePluginCrtSupportKind.Buffer &&
                (previous.Buffer != buffer || previous.Length != length || previous.BufferOwner != bufferOwner || previous.Allocation != allocation))
                throw new InvalidDataException("CRT operation changed a retained caller buffer without setvbuf.");
            if (kind == NativePluginCrtSupportKind.Buffer && result != 0 && previous is not null &&
                (previous.Buffer != buffer || previous.Length != length || previous.BufferOwner != bufferOwner || previous.Allocation != allocation))
                throw new InvalidDataException("A failed actual setvbuf discarded the prior caller-buffer lifetime.");
            if (kind == NativePluginCrtSupportKind.Position && (result == 0) != (hasPosition == 1) ||
                kind != NativePluginCrtSupportKind.Position && hasPosition != 0)
                throw new InvalidDataException("CRT fpos payload is absent or invented for the actual result.");
            // Cells are returned by the real public UCRT accessor, not offsets
            // inferred inside FILE. Optional output cells may independently be null.
            if (baseCell == 0 && baseValue != 0 || pointerCell == 0 && pointerValue != 0 || countCell == 0 && countValue != 0)
                throw new InvalidDataException("CRT alias values have no actual returned native cell.");
            _crtSupport[stream] = new(provider, route, stream, buffer, length, bufferOwner, allocation,
                baseCell, pointerCell, countCell, depth);
        }
        _crtSupportReceipts.Add(new(checked((ulong)_crtSupportReceipts.Count + 1), Generation, parent, provider, route,
            stream, kind, result, lastError, buffer, length, bufferOwner, allocation, baseCell, pointerCell, countCell,
            baseValue, pointerValue, countValue, depth, hasPosition == 1, bits));
        return Payload(writer => writer.Write(1U));
    }
    private void RequirePrivateCrtSupportRetired()
    {
        if (_crtSupport.Count != 0 || _crtSupportClosing.Count != 0) throw new InvalidDataException("Original CRT retains live buffer/alias/lock owners.");
    }
    private void ClearPrivateCrtSupportAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("CRT caller buffer cleanup requires verified child closure.");
        _crtSupport.Clear(); _crtSupportClosing.Clear();
    }
    private void RequireCrtSourceBuffer(uint pointer, uint length)
    {
        var source = _nvseModuleSource ?? throw new InvalidDataException("CRT module buffer lost its exact retained original source.");
        var image = _nvsePlugin ?? throw new InvalidDataException("CRT module buffer has no actual native image.");
        var position = source.Position;
        try
        {
            source.Position = 0;
            using var pe = new PEReader(source, PEStreamOptions.LeaveOpen);
            var header = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("CRT buffer declaration lost its original PE header.");
            var offset = (ulong)pointer - image.Image;
            if (offset + length > (ulong)header.SizeOfImage ||
                !pe.PEHeaders.SectionHeaders.Any(section => section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemWrite) &&
                    offset >= (ulong)section.VirtualAddress && offset + length <= (ulong)section.VirtualAddress + (ulong)Math.Max(section.VirtualSize, section.SizeOfRawData)))
                throw new InvalidDataException("CRT original-module buffer has no complete writable source-declared image extent.");
        }
        finally { source.Position = position; }
    }
}
