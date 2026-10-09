namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    internal void ConfigureNvseSourceFileMethods(NativeNvsePlugin plugin, NativeNvseFileDeclaration declaration)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); ArgumentNullException.ThrowIfNull(declaration);
        if (_nvseFileDeclaration is not null || _nvseHostSource is null ||
            plugin.Phase is not (NativeNvsePhase.Mapped or NativeNvsePhase.QueriedTrue) ||
            !StringComparer.OrdinalIgnoreCase.Equals(declaration.RuntimeSha256, _nvseHostSource.RuntimeSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(declaration.PluginSha256, plugin.Sha256) ||
            string.IsNullOrWhiteSpace(declaration.DeclarationOwner) || string.IsNullOrWhiteSpace(declaration.LittleEndianOwner) ||
            declaration.Thunks is null || declaration.Thunks.Count is 0 or > 128)
            throw new InvalidDataException("Native source methods need exact selected runtime/module/byte-order declarations before Query/Load.");
        var thunks = declaration.Thunks.ToArray();
        for (var index = 0; index < thunks.Length; ++index)
        {
            var thunk = thunks[index];
            if (thunk is null || thunk.Address == 0 || thunk.Address > uint.MaxValue - 7 ||
                !Enum.IsDefined(thunk.Method) || thunk.Abi != NativePluginAbi.Thiscall ||
                thunks.Where((_, other) => other != index).Any(other => other is null ||
                    (ulong)thunk.Address < (ulong)other.Address + 7 && (ulong)other.Address < (ulong)thunk.Address + 7))
                throw new InvalidDataException("Source file method has no exact nonoverlapping native thiscall declaration.");
        }
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseFileMethods, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(checked((uint)thunks.Length));
                foreach (var thunk in thunks) { writer.Write(thunk.Address); writer.Write((uint)thunk.Method); writer.Write((uint)thunk.Abi); }
            }));
            if (reader.ReadUInt32() != thunks.Length || reader.ReadUInt32() is 0 or > 128)
                throw new InvalidDataException("Native source file methods lack a complete callable mapping receipt.");
            Finish(reader); _nvseFileDeclaration = declaration with { Thunks = Array.AsReadOnly(thunks) };
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    private byte[] DispatchNvseSourceFile(Frame frame)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var callerId = reader.ReadUInt64(); var objectId = reader.ReadUInt64(); var address = reader.ReadUInt32();
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Source file callback has no retained original module.");
        if (thread != NativeThread || module != plugin.Module || handle != plugin.Handle || _nvseFileDeclaration is null ||
            !_nvseExpressionCallers.TryGetValue(callerId, out var caller) || _nvseLocalCallers.Count == 0 ||
            _nvseLocalCallers[^1] != callerId || !_nvseSourceObjects.TryGetValue(objectId, out var value) ||
            value.Address != address || value.Class != NativeNvseSourceClass.ModInfo || !caller.SourceObjects.Contains(value) ||
            value.Authority is not INativeNvseSourceFileAuthority file)
            throw new InvalidDataException("Source file callback lacks its exact caller, contributor image, parser and native generation.");
        if (frame.Operation == SourceFileBegin)
        {
            // A different admitted module may have advanced the one campaign
            // contributor cursor. This method owns that specific refresh; it
            // still compares the full cached image natively and refuses every
            // changed field outside the source-parser byte ranges.
            if (value.Generation != Generation || value.Module != plugin.Module || value.Retired || value.Staged)
                throw new InvalidOperationException("Contributor image has a foreign or retired lifetime.");
            value.Authority.RequireCurrent(); VerifyGuest(value.Image); VerifyGuest(value.Metadata);
        }
        else VerifyNvseSourceObject(plugin, value);
        file.RequireSourceFileCurrent();
        if (!StringComparer.OrdinalIgnoreCase.Equals(file.SourceFileSha256, value.Authority.SourceSha256))
            throw new InvalidDataException("Native contributor parser differs from the source object byte identity.");
        if (frame.Operation == SourceFileBegin)
        {
            var method = (NativeNvseFileMethod)reader.ReadUInt32(); var capacity = reader.ReadUInt32(); Finish(reader);
            if (!Enum.IsDefined(method) || !_nvseFileDeclaration.Thunks.Any(thunk => thunk.Method == method) ||
                method is NativeNvseFileMethod.NextChunk or NativeNvseFileMethod.AdvanceChunk && capacity != 0 ||
                method == NativeNvseFileMethod.Read32 && capacity != 4 ||
                _nvseFileTransfers.Values.Any(transfer => transfer.Caller == callerId || ReferenceEquals(transfer.Object, value)))
                throw new InvalidDataException("Source file operation or output lifetime differs from the actual admitted native call.");
            if (!_nvseSourceGraphs.TryGetValue(value.Id, out var graph) || !graph.NativeImages.TryGetValue(value.Id, out var before))
                throw new NotSupportedException("Native contributor parser needs the actual cyclic campaign image owner.");
            if (!ReadNvseGraphBytes(value.Image).AsSpan().SequenceEqual(before) ||
                !ReadNvseGraphBytes(value.Metadata).AsSpan().SequenceEqual(graph.NativeMetadata[value.Id]))
                throw new InvalidDataException("Contributor image/metadata changed outside its actual source publication owner.");
            var read = file.CallSourceFile(method, capacity);
            file.RequireSourceFileCurrent();
            if (method is NativeNvseFileMethod.NextChunk or NativeNvseFileMethod.AdvanceChunk && !read.Bytes.IsEmpty ||
                method == NativeNvseFileMethod.Read32 && read.Bytes.Length > 4)
                throw new InvalidDataException("Actual source parser returned an invalid native output extent.");
            var (after, _, dependencies, fingerprint) = ComposeNvseGraphObject(value);
            if (after.Length != before.Length || !dependencies.Distinct().OrderBy(row => row.Id).SequenceEqual(value.Dependencies.OrderBy(row => row.Id)))
                throw new NotSupportedException("A source file method changed its contributor topology or complete image extent.");
            // Retain the exact before/after image for the native read-only
            // replacement. Only parser-owned fields may change in this call.
            for (var at = 0; at < after.Length; ++at)
                if (after[at] != before[at] && !(at is >= 0x240 and < 0x260 or >= 0x264 and < 0x270))
                    throw new NotSupportedException("Native source parsing changed an unowned contributor field.");
            ulong transferId = 0;
            var receipt = _nvseFileCalls.Count;
            if (!read.Bytes.IsEmpty)
            {
                transferId = checked(++_nextNvseFileTransfer);
                _nvseFileTransfers.Add(transferId, new(callerId, value, read.Bytes, receipt));
            }
            _nvseFileCalls.Add(new(frame.Id, callerId, value.Id, method, capacity, read.Result,
                checked((uint)read.Bytes.Length), read.Diagnostic, transferId == 0 ? null : transferId, transferId == 0));
            graph.NativeImages[value.Id] = after; value.PublishedSha256 = fingerprint;
            return Payload(writer =>
            {
                writer.Write(read.Result); writer.Write(transferId); writer.Write(checked((uint)read.Bytes.Length));
                writer.Write(checked((uint)before.Length)); writer.Write(before); writer.Write(after);
            });
        }
        var transfer = reader.ReadUInt64();
        if (!_nvseFileTransfers.TryGetValue(transfer, out var pending) || pending.Caller != callerId || !ReferenceEquals(pending.Object, value))
            throw new InvalidDataException("Source bytes belong to another caller, contributor or completed output lifetime.");
        if (frame.Operation == SourceFileSlice)
        {
            var at = reader.ReadUInt32(); var count = reader.ReadUInt32(); Finish(reader);
            if (at != pending.Position || count == 0 || count > MaximumPayload - 64 || count > pending.Bytes.Length - at)
                throw new InvalidDataException("Native source output skipped, repeated or exceeded its actual complete byte extent.");
            pending.Position = checked(at + count);
            return Payload(writer => { writer.Write(count); writer.Write(pending.Bytes.Span.Slice(checked((int)at), checked((int)count))); });
        }
        if (frame.Operation != SourceFileEnd) throw new InvalidDataException("Unknown source file callback.");
        Finish(reader);
        if (pending.Position != pending.Bytes.Length)
            throw new InvalidDataException("Native source output ended before every actual byte was consumed.");
        _nvseFileTransfers.Remove(transfer);
        _nvseFileCalls[pending.Receipt] = _nvseFileCalls[pending.Receipt] with { OutputCompleted = true };
        return Payload(writer => writer.Write(1U));
    }

    internal void RequireNvseSourceFilesIdle()
    {
        VerifyOwner();
        if (_nvseFileTransfers.Count != 0 || _nvseFileCalls.Any(call => !call.OutputCompleted))
            throw new InvalidOperationException("Native source file output retains an incomplete actual caller lifetime.");
    }
    private void RequireNvseSourceFileCallerComplete(ulong caller)
    {
        if (_nvseFileTransfers.Values.Any(transfer => transfer.Caller == caller) ||
            _nvseFileCalls.Any(call => call.Caller == caller && !call.OutputCompleted))
            throw new InvalidDataException("Original caller returned before its complete source-byte output retired.");
    }
    internal string? NvseSourceFileDiagnostics => _nvseFileCalls.Where(call => call.Diagnostic is not null)
        .Select(call => "source file caller " + call.Caller + ", callback " + call.Callback + ": " + call.Diagnostic)
        .ToArray() is { Length: > 0 } warnings ? string.Join("; ", warnings) : null;
    internal void RequireNvseSourceFilesSaveOwned()
    {
        RequireNvseSourceFilesIdle();
        if (_nvseFileCalls.Count != 0)
            throw new NotSupportedException("Reached original contributor parser state needs its complete current cold-capture/restore join.");
    }
    private void ClearNvseSourceFiles()
    {
        // Failed-output receipts stay visible during terminal capability
        // cleanup. Releasing byte leases is never an output completion.
        _nvseFileTransfers.Clear(); _nvseFileDeclaration = null;
    }
}
