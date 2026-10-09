namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginCrtDescriptorOperation : uint
{ FileNumber = 1, OsHandle = 2, Duplicate = 3, Close = 4, Read = 5, Write = 6, Seek = 7, Length = 8, EndOfFile = 9 }
internal sealed record NativePluginCrtDescriptorReceipt(ulong Generation, ulong Parent, ulong Provider, ulong Route,
    uint Stream, int Descriptor, uint Handle, NativePluginCrtDescriptorOperation Operation, long Result, int Related,
    NativePluginCrtStatus Status, uint LastError);
internal sealed record NativePluginCrtStandardStream(uint Index, ulong Provider, ulong Route, uint Stream, int Descriptor, uint Handle);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint CrtStandardCallback = 27;
    private readonly Dictionary<uint, NativePluginCrtStandardStream> _crtStandardStreams = [];
    private readonly Dictionary<int, (ulong Provider, ulong Route, uint Stream, uint Handle, bool Independent)> _crtDescriptors = [];
    private readonly List<NativePluginCrtDescriptorReceipt> _crtDescriptorReceipts = [];
    internal IReadOnlyList<NativePluginCrtDescriptorReceipt> CrtDescriptorReceipts => _crtDescriptorReceipts.AsReadOnly();
    private byte[] DispatchPrivateCrtStandard(ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        var stage = reader.ReadUInt32(); var provider = reader.ReadUInt64();
        if (!_crtProviders.ContainsKey(provider)) throw new InvalidDataException("Standard FILE/descriptor has no actual UCRT source mapping.");
        if (stage == 1)
        {
            var index = reader.ReadUInt32(); var stream = reader.ReadUInt32(); var descriptor = reader.ReadInt32(); var handle = reader.ReadUInt32();
            var kernelEqual = reader.ReadUInt32(); var status = ReadCrtStatus(reader); Finish(reader);
            if (index is not (1 or 2) || descriptor != index || stream == 0 || handle is 0 or uint.MaxValue || kernelEqual != 1 ||
                !status.StreamStatusAvailable || _crtStandardStreams.ContainsKey(index) || _crtDescriptors.ContainsKey(descriptor) ||
                _crtStreams.Values.Any(row => row.Address == stream))
                throw new InvalidDataException("Standard output lacks its genuine public FILE/descriptor/private kernel identity.");
            var route = owner.BindStandardRoute(parent, index);
            _crtStreams.Add(route.Id, new(provider, route.Id, stream, true, "a", "actual-UCRT-standard-output:" + index));
            _crtStandardStreams.Add(index, new(index, provider, route.Id, stream, descriptor, handle));
            _crtDescriptors.Add(descriptor, (provider, route.Id, stream, handle, false));
            return Payload(writer => writer.Write(route.Id));
        }
        if (stage == 3)
        {
            var route = reader.ReadUInt64(); var stream = reader.ReadUInt32(); var descriptor = reader.ReadInt32(); var handle = reader.ReadUInt32();
            var writable = reader.ReadUInt32(); Finish(reader);
            if (!_crtStreams.TryGetValue(route, out var publishedFile) || publishedFile.Address != stream || publishedFile.Provider != provider || descriptor < 0 ||
                handle is 0 or uint.MaxValue || writable > 1 || publishedFile.Writable != (writable == 1) ||
                !_crtDescriptors.TryAdd(descriptor, (provider, route, stream, handle, false)))
                throw new InvalidDataException("Actual FILE descriptor publication lacks its retained stream/access identity.");
            return Payload(writer => writer.Write(1U));
        }
        if (stage != 2) throw new InvalidDataException("Descriptor receipt has an unknown original operation stage.");
        var routeId = reader.ReadUInt64(); var pointer = reader.ReadUInt32(); var fd = reader.ReadInt32(); var osHandle = reader.ReadUInt32();
        var operation = (NativePluginCrtDescriptorOperation)reader.ReadUInt32(); var result = reader.ReadInt64(); var related = reader.ReadInt32();
        var returnedStatus = ReadCrtStatus(reader); var last = reader.ReadUInt32(); Finish(reader);
        if (!Enum.IsDefined(operation) || !_crtDescriptors.TryGetValue(fd, out var current) || current.Provider != provider || current.Route != routeId ||
            current.Stream != pointer || current.Handle != osHandle)
            throw new InvalidDataException("Descriptor call targets a foreign/closed actual UCRT object.");
        if (!current.Independent && (!_crtStreams.TryGetValue(routeId, out var file) || file.Address != pointer || file.Provider != provider))
            throw new InvalidDataException("FILE descriptor view outlived its genuine parent stream.");
        if (operation == NativePluginCrtDescriptorOperation.Duplicate)
        {
            if (result >= 0)
            {
                var duplicated = checked((int)result);
                if (duplicated < 3 || related is 0 or -1 || !_crtDescriptors.TryAdd(duplicated, (provider, routeId, pointer, unchecked((uint)related), true)))
                    throw new InvalidDataException("Actual duplicated descriptor lost its independent kernel-handle lifetime.");
            }
            else if (result != -1 || related != 0) throw new InvalidDataException("Failed descriptor duplication invented an alias.");
        }
        if (operation == NativePluginCrtDescriptorOperation.Close)
        {
            if (!current.Independent || result is not (0 or -1)) throw new InvalidDataException("Descriptor close cannot invalidate a borrowed live FILE owner.");
            if (result == 0) _crtDescriptors.Remove(fd);
        }
        if (operation == NativePluginCrtDescriptorOperation.FileNumber && result != fd ||
            operation == NativePluginCrtDescriptorOperation.OsHandle && result != (int)osHandle)
            throw new InvalidDataException("Public CRT descriptor/OS-handle getter changed its retained actual identity.");
        _crtDescriptorReceipts.Add(new(Generation, parent, provider, routeId, pointer, fd, osHandle, operation, result, related, returnedStatus, last));
        return Payload(writer => writer.Write(1U));
    }
    private void RetireCrtDescriptorViews(ulong route, uint stream)
    {
        foreach (var row in _crtDescriptors.Where(row => !row.Value.Independent && row.Value.Route == route && row.Value.Stream == stream).ToArray())
            _crtDescriptors.Remove(row.Key);
        foreach (var row in _crtStandardStreams.Where(row => row.Value.Route == route && row.Value.Stream == stream).ToArray())
            _crtStandardStreams.Remove(row.Key);
    }
    private void RequireCrtDescriptorsRetired()
    {
        if (_crtStandardStreams.Count != 0 || _crtDescriptors.Count != 0) throw new InvalidDataException("Original UCRT retains a standard FILE or independently duplicated descriptor.");
    }
    private void ClearCrtDescriptorsAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Standard FILE/descriptor cleanup requires exact child closure.");
        _crtStandardStreams.Clear(); _crtDescriptors.Clear();
    }
}
