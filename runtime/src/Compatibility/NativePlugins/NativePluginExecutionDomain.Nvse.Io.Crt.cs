namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private static void WriteCrtProviders(BinaryWriter writer, NativePluginPrivateIo owner)
    {
        writer.Write(checked((uint)owner.CrtProviders.Count));
        foreach (var row in owner.CrtProviders)
        {
            WriteText(writer, row.Path); WriteText(writer, row.Sha256); WriteText(writer, row.SourceOwner);
            writer.Write(checked((uint)row.Imports.Count));
            foreach (var pair in row.Imports.OrderBy(row => row.Key, StringComparer.OrdinalIgnoreCase))
            {
                WriteText(writer, pair.Key); writer.Write(checked((uint)pair.Value.Count));
                foreach (var name in pair.Value.Order(StringComparer.Ordinal)) WriteText(writer, name);
            }
        }
    }
    private byte[] DispatchPrivateCrt(Frame frame, ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        if (frame.Operation == CrtProvider)
        {
            var provider = reader.ReadUInt64(); var module = reader.ReadUInt32();
            var path = ReadText(reader); var sha = ReadText(reader); Finish(reader);
            var selected = owner.CrtProvider(path, sha);
            if (provider == 0 || module == 0 || _crtProviders.ContainsKey(provider) ||
                _crtProviders.Values.Any(value => value.Module == module))
                throw new InvalidDataException("CRT provider repeats a native module/source lifetime.");
            _crtProviders.Add(provider, (selected, module)); return Payload(writer => writer.Write(1U));
        }
        var providerId = reader.ReadUInt64();
        if (!_crtProviders.TryGetValue(providerId, out var providerOwner))
            throw new InvalidDataException("CRT operation has no current correlated native provider.");
        if (frame.Operation == CrtRoute)
        {
            var api = reader.ReadUInt32(); var mode = ReadText(reader); var writable = reader.ReadUInt32(); var path = ReadText(reader); Finish(reader);
            if (writable > 1 || !(api == 1 && CrtModeWritable(mode) == (writable != 0) || api == 5 && mode.Length == 0 && writable == 1))
                throw new InvalidDataException("CRT route altered its actual path/API/mode access contract.");
            var action = api == 5 ? NativePluginIoAction.Directory : writable != 0 ? NativePluginIoAction.Write : NativePluginIoAction.Read;
            var route = owner.Resolve(parent, api, action, path);
            if (!_crtRoutes.TryAdd(route.Id, (providerId, mode, writable != 0)))
                throw new InvalidDataException("CRT route repeats a live selected path decision.");
            // Source absence/deletion maps to a protected absent private path.
            // Opening the original virtual spelling could expose a loser or
            // resurrect a source-hidden file. The actual CRT still owns errno.
            var actual = route.PhysicalPath ?? (action == NativePluginIoAction.Read ? owner.CrtAbsentRead(route) :
                throw new InvalidDataException("CRT writable/directory route has no private physical destination."));
            return Payload(writer => { writer.Write(route.Id); WriteText(writer, actual); });
        }
        var routeId = reader.ReadUInt64(); var stream = reader.ReadUInt32();
        var operation = (NativePluginCrtOperation)reader.ReadUInt32(); var argument = reader.ReadUInt64(); var requested = reader.ReadUInt64();
        var result = reader.ReadInt64(); var retired = reader.ReadUInt32(); var status = ReadCrtStatus(reader); Finish(reader);
        if (!Enum.IsDefined(operation) || retired > 1)
            throw new InvalidDataException("CRT result has an unknown operation/retirement disposition.");
        var receipt = new NativePluginCrtReceipt(0, Generation, parent, providerId, routeId, stream, operation, argument, requested,
            result, status, retired != 0, providerOwner.Selection.SourceOwner);
        if (frame.Operation == CrtOpened)
        {
            if (operation != NativePluginCrtOperation.Open || retired != 0 || !_crtRoutes.Remove(routeId, out var route) ||
                route.Provider != providerId || (stream == 0) != (result == 0) || result != stream ||
                status.StreamStatusAvailable != (stream != 0))
                throw new InvalidDataException("CRT open lacks its actual selected mode/pointer/result lifetime.");
            owner.ValidateResult(routeId, 1, route.Writable); owner.CompleteCrt(parent, routeId, 1, stream != 0);
            if (stream != 0 && (!_crtStreams.TryAdd(routeId, new(providerId, routeId, stream, route.Writable, route.Mode, providerOwner.Selection.SourceOwner)) ||
                _crtStreams.Values.Count(value => value.Address == stream) != 1))
                throw new InvalidDataException("CRT actual stream aliases another live source capability.");
            owner.RecordCrt(receipt); return Payload(writer => writer.Write(1U));
        }
        if (frame.Operation == CrtPathResult)
        {
            if (stream != 0 || retired != 0 || operation != NativePluginCrtOperation.MakeDirectory ||
                status.StreamStatusAvailable || !_crtRoutes.Remove(routeId, out var route) || route.Provider != providerId ||
                route.Mode.Length != 0 || !route.Writable || result is not (0 or -1))
                throw new InvalidDataException("CRT directory result changed its private path/source disposition.");
            owner.CompleteCrt(parent, routeId, 5, result == 0); owner.RecordCrt(receipt); return Payload(writer => writer.Write(1U));
        }
        if (frame.Operation != CrtEvent || !_crtStreams.TryGetValue(routeId, out var current) || current.Provider != providerId || current.Address != stream ||
            operation is NativePluginCrtOperation.Open or NativePluginCrtOperation.MakeDirectory ||
            (operation == NativePluginCrtOperation.Close) != (retired == 1) ||
            (operation != NativePluginCrtOperation.Close) != status.StreamStatusAvailable ||
            operation is NativePluginCrtOperation.Write or NativePluginCrtOperation.PutCharacter or NativePluginCrtOperation.PutString or NativePluginCrtOperation.FormattedWrite && !current.Writable)
            throw new InvalidDataException("CRT operation lost its true stream/provider/access/call lifetime.");
        if (operation is NativePluginCrtOperation.Read or NativePluginCrtOperation.Write && (result < 0 || (ulong)result > requested))
            throw new InvalidDataException("CRT element result exceeds its actual requested element count.");
        if (retired == 1)
        {
            if (!_crtSupportClosing.TryAdd(stream, current)) throw new InvalidDataException("Actual CRT close repeats a pending alias retirement.");
            _crtStreams.Remove(routeId); RetireCrtDescriptorViews(routeId, stream);
        }
        owner.RecordCrt(receipt); return Payload(writer => writer.Write(1U));
    }
    private static NativePluginCrtStatus ReadCrtStatus(BinaryReader reader)
    {
        var error = reader.ReadInt32(); var dos = reader.ReadUInt32(); var available = reader.ReadUInt32();
        var eof = reader.ReadInt32(); var streamError = reader.ReadInt32();
        if (available > 1 || available == 0 && (eof != 0 || streamError != 0))
            throw new InvalidDataException("CRT status has invented/absent stream indicators.");
        return new(error, dos, available != 0, eof, streamError);
    }
    private static bool CrtModeWritable(string mode)
    {
        if (mode.Length == 0 || mode.IndexOf('\0') >= 0 || mode[0] is not ('r' or 'w' or 'a'))
            throw new NotSupportedException("CRT parameter-handler/access mode ownership is absent.");
        var comma = mode.IndexOf(','); var access = comma < 0 ? mode : mode[..comma];
        if (access.AsSpan(1).ContainsAnyExcept("+btcnNSRTDx ".AsSpan()))
            throw new NotSupportedException("CRT mode has an unowned access/delete/translation option.");
        if (comma >= 0 && mode[(comma + 1)..].Trim() is not ("ccs=UNICODE" or "ccs=UTF-8" or "ccs=UTF-16LE"))
            throw new NotSupportedException("CRT mode has an unowned encoded-stream option.");
        return mode[0] != 'r' || access.Contains('+') || access.Contains('D');
    }
    private void RequirePrivateCrtRetired()
    {
        if (_crtStreams.Count != 0 || _crtRoutes.Count != 0) throw new InvalidDataException("Original module retains genuine CRT stream/path lifetimes.");
        RequireCrtDescriptorsRetired(); RequireCppRuntimeRetired(); RequirePrivateCrtSupportRetired(); _privateIo?.RequireCrtRetired();
    }
    internal void RequirePrivateCrtSaveOwned()
    {
        VerifyOwner(); RequireNativeEnvironmentSaveOwned(); RequireNativeFileMetadataSaveOwned();
        if (_crtStreams.Count != 0 || _crtRoutes.Count != 0 || NvseCrtReceipts.Count != 0 || _crtDescriptorReceipts.Count != 0 || _cppRuntimePublished)
            throw new NotSupportedException("Original CRT current/cold stream/caller/error-state construction has no unified source save owner.");
    }
    private void ClearPrivateCrt()
    {
        ClearCrtDescriptorsAfterChildExit(); ClearCppRuntimeAfterChildExit(); ClearPrivateCrtSupportAfterChildExit();
        _crtStreams.Clear(); _crtRoutes.Clear(); _crtProviders.Clear();
    }
}
