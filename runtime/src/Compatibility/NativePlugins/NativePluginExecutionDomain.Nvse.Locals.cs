namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private readonly Dictionary<ulong, NativeNvseLocalContext> _nvseLocals = [];
    private ulong _nextNvseLocalContext, _nextNvseLocalTransfer;
    private readonly List<ulong> _nvseLocalCallers = [];
    private const int NvseLocalChunk = 2048;
    private const uint LocalBegin = 0x300, LocalChunk = 0x301, LocalCommit = 0x302, LocalRead = 0x303, LocalEnd = 0x304;

    internal NativeNvseLocalContext BindNvseLocalContext(NativeNvsePlugin plugin, NativeNvseLocalAuthority authority, NativeNvseSourceObject? script = null)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); ArgumentNullException.ThrowIfNull(authority);
        authority.RequireCurrent();
        if (string.IsNullOrWhiteSpace(authority.SourceOwner) || authority.SourceSha256.Length != 64 || !authority.SourceSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Native event-list lacks an exact campaign/source declaration owner.");
        if (script is not null)
        {
            VerifyNvseSourceObject(plugin, script);
            if (script.Class != NativeNvseSourceClass.Script || script.Authority is not NativeNvseScriptAuthority source ||
                !StringComparer.OrdinalIgnoreCase.Equals(authority.SourceSha256, source.SourceSha256) ||
                !StringComparer.OrdinalIgnoreCase.Equals(authority.CodeSha256, source.CodeSha256))
                throw new InvalidDataException("Actual Script/event-list source identities differ.");
        }
        var context = new NativeNvseLocalContext(Generation, plugin.Module, checked(++_nextNvseLocalContext), authority, script);
        try
        {
            if (_nvseValues is not null && !ReferenceEquals(_nvseValues.StoreIdentity, authority.ValueStoreIdentity) ||
                _nvseValues is null && context.Declarations.Any(row => row.Kind is NativeNvseLocalKind.String or NativeNvseLocalKind.Array))
                throw new InvalidDataException("Native local and StringVar/ArrayVar owners do not share the actual campaign value store.");
            using (var reader = Exchange(NativePluginDomainOperation.NvseLocalCreate, Payload(writer =>
                { writer.Write(plugin.Module); writer.Write(context.Id); writer.Write(checked((uint)context.Declarations.Length)); writer.Write(script?.Id ?? 0); })))
            { if (reader.ReadUInt64() != context.Id) throw new InvalidDataException("Native event-list construction identity drifted."); Finish(reader); }
            for (var offset = 0; offset < context.Declarations.Length; offset += NvseLocalChunk)
            {
                var count = Math.Min(NvseLocalChunk, context.Declarations.Length - offset);
                using var reader = Exchange(NativePluginDomainOperation.NvseLocalFill, Payload(writer =>
                {
                    writer.Write(plugin.Module); writer.Write(context.Id); writer.Write(checked((uint)offset)); writer.Write(checked((uint)count));
                    for (var index = offset; index < offset + count; ++index)
                    { writer.Write(context.Declarations[index].Index); writer.Write((uint)context.Declarations[index].Kind); writer.Write((uint)context.Declarations[index].StorageFlags); writer.Write(context.Baseline[index]); }
                }));
                if (reader.ReadUInt32() != offset + count) throw new InvalidDataException("Native event-list source slot extent drifted."); Finish(reader);
            }
            using (var reader = Exchange(NativePluginDomainOperation.NvseLocalSeal, Payload(writer => { writer.Write(plugin.Module); writer.Write(context.Id); })))
            {
                context.EventList = reader.ReadUInt32(); context.Variables = reader.ReadUInt32();
                if (context.EventList == 0 || context.Variables == 0 || reader.ReadUInt32() != context.Declarations.Length)
                    throw new InvalidDataException("Native event-list has no complete constructed storage receipt.");
                Finish(reader);
            }
            authority.RequireCurrent(); _nvseLocals.Add(context.Id, context); return context;
        }
        catch (Exception error) { context.SourceLease.Dispose(); throw Fatal(error); }
    }

    internal void RetireNvseLocalContext(NativeNvsePlugin plugin, NativeNvseLocalContext context)
    {
        VerifyNvseLocalContext(plugin, context); RequireNvseEmptyCall();
        if (context.Active != 0 || context.Transfer is not null || context.Retainers != 0) throw new InvalidOperationException("An actual command still owns this event-list storage.");
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseLocalRetire, Payload(writer => { writer.Write(plugin.Module); writer.Write(context.Id); }));
            if (reader.ReadUInt32() != 1) throw new InvalidDataException("Native event-list lacks its decommit/quarantine receipt.");
            Finish(reader); context.Retired = true; _nvseLocals.Remove(context.Id); context.SourceLease.Dispose();
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    internal NativeNvseLocalStatistics NvseLocalStatistics(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin);
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseLocalStatistics, Payload(writer => writer.Write(plugin.Module)));
            var actual = new NativeNvseLocalStatistics(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()); Finish(reader);
            if (actual.LiveContexts != _nvseLocals.Count || actual.ActiveCalls != _nvseLocals.Values.Sum(row => row.Active) ||
                actual.LiveRegions != _nvseLocals.Count * 4)
                throw new InvalidDataException("Native event-list lifetimes/pages differ from the genuine C# owners.");
            return actual;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    private void VerifyNvseLocalContext(NativeNvsePlugin plugin, NativeNvseLocalContext context)
    {
        VerifyNvse(plugin); ArgumentNullException.ThrowIfNull(context);
        if (context.Generation != Generation || context.Module != plugin.Module || context.Retired ||
            !_nvseLocals.TryGetValue(context.Id, out var retained) || !ReferenceEquals(retained, context))
            throw new InvalidOperationException("Native event-list is forged, foreign or retired.");
        context.Authority.RequireCurrent();
    }

    private byte[] DispatchNvseLocals(Frame frame)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var id = reader.ReadUInt64(); var callerId = reader.ReadUInt64(); var sequence = reader.ReadUInt64();
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Native local callback has no actual module.");
        if (thread != NativeThread || module != plugin.Module || handle != plugin.Handle ||
            !_nvseLocals.TryGetValue(id, out var context) || context.Active <= 0 ||
            _nvseLocalCallers.Count == 0 || _nvseLocalCallers[^1] != callerId ||
            !_nvseExpressionCallers.TryGetValue(callerId, out var caller) || !ReferenceEquals(caller.LocalContext, context))
            throw new InvalidDataException("Native local callback has a foreign generation/thread/current caller.");
        VerifyNvseLocalContext(plugin, context);
        if (frame.Operation == LocalBegin)
        {
            var entry = reader.ReadUInt32(); var count = reader.ReadUInt32(); var pointer = reader.ReadUInt32(); Finish(reader);
            if (entry > 1 || count != context.Declarations.Length || pointer != context.EventList || context.Transfer is not null || sequence <= _nextNvseLocalTransfer)
                throw new InvalidDataException("Native local transfer lacks its exact ordered source extent.");
            _nextNvseLocalTransfer = sequence; context.Transfer = new(sequence, callerId, entry == 1, context.Declarations.Length);
            return Payload(writer => writer.Write(1U));
        }
        var transfer = context.Transfer ?? throw new InvalidDataException("Native local callback has no open transfer.");
        if (sequence != transfer.Sequence || callerId != transfer.Caller) throw new InvalidDataException("Native local transfer identity drifted.");
        switch (frame.Operation)
        {
            case LocalChunk:
                {
                    var offset = reader.ReadUInt32(); var count = reader.ReadUInt32();
                    if (transfer.Canonical is not null || offset != transfer.Received || count == 0 || count > NvseLocalChunk || count > transfer.Native.Length - transfer.Received)
                        throw new InvalidDataException("Native local transfer is incomplete, repeated or out of order.");
                    for (var index = transfer.Received; index < transfer.Received + count; ++index)
                    {
                        if (reader.ReadUInt32() != context.Declarations[index].Index) throw new InvalidDataException("Native local slot identity changed.");
                        transfer.Native[index] = reader.ReadUInt64();
                    }
                    Finish(reader); transfer.Received += checked((int)count); return Payload(writer => writer.Write(checked((uint)transfer.Received)));
                }
            case LocalCommit:
                {
                    Finish(reader);
                    if (transfer.Canonical is not null || transfer.Received != transfer.Native.Length) throw new InvalidDataException("Native local publication lacks every declared slot.");
                    var changes = new List<NativeNvseLocalEntryChange>();
                    for (var index = 0; index < context.Declarations.Length; ++index)
                    {
                        var actual = context.Authority.ReadEntry(index); var baseline = context.Baseline[index]; var native = transfer.Native[index];
                        if (native == baseline) continue;
                        if (transfer.Entry) throw new InvalidDataException("Native event-list changed outside an owned command invocation.");
                        if (actual != baseline && actual != native) throw new InvalidDataException("Native and campaign local writes conflict across callback reentry.");
                        if (actual != native) changes.Add(new(index, context.Declarations[index].Index, actual, native));
                    }
                    var publish = context.Authority.PrepareEntries(changes); publish(); context.Authority.RequireCurrent();
                    transfer.Canonical = Enumerable.Range(0, context.Declarations.Length).Select(context.Authority.ReadEntry).ToArray();
                    return Payload(writer => writer.Write(checked((uint)transfer.Canonical.Length)));
                }
            case LocalRead:
                {
                    var offset = reader.ReadUInt32(); var count = reader.ReadUInt32(); Finish(reader);
                    var canonical = transfer.Canonical ?? throw new InvalidDataException("Native local output preceded actual C# publication.");
                    if (offset != transfer.Sent || count == 0 || count > NvseLocalChunk || count > canonical.Length - transfer.Sent)
                        throw new InvalidDataException("Native local canonical read crosses its ordered extent.");
                    var response = Payload(writer =>
                    { writer.Write(count); for (var index = transfer.Sent; index < transfer.Sent + count; ++index) writer.Write(canonical[index]); });
                    transfer.Sent += checked((int)count); return response;
                }
            case LocalEnd:
                {
                    Finish(reader);
                    var canonical = transfer.Canonical ?? throw new InvalidDataException("Native local transfer ended before publication.");
                    if (transfer.Sent != canonical.Length) throw new InvalidDataException("Native local transfer lost a canonical output extent.");
                    canonical.CopyTo(context.Baseline, 0); context.Transfer = null; return Payload(writer => writer.Write(1U));
                }
            default: throw new InvalidDataException("Unknown native local callback operation.");
        }
    }

    private void ClearNvseLocalCapabilities()
    {
        foreach (var context in _nvseLocals.Values) { context.Retired = true; context.Transfer = null; context.SourceLease.Dispose(); }
        _nvseLocals.Clear(); _nvseLocalCallers.Clear(); _nextNvseLocalTransfer = 0;
    }

    private void BeginNvseLocalCall(NativeNvseExpressionCaller caller)
    {
        if (caller.LocalContext is not { } context) return;
        VerifyNvseLocalContext(_nvsePlugin!, context);
        if (caller.ResultTarget is not null && !ReferenceEquals(caller.ResultTarget.StoreIdentity, context.Authority.ValueStoreIdentity))
            throw new InvalidDataException("Native local result target belongs to another campaign value store.");
        if (context.Transfer is not null) throw new InvalidOperationException("A native local transfer cannot be reentered before its complete publication.");
        _nvseLocalCallers.Add(caller.Id); ++context.Active;
    }

    private void RequireNvseLocalCode(NativeNvseLocalContext context, NativePluginGuestAllocation code)
    {
        VerifyNvseLocalContext(_nvsePlugin!, context);
        if (code.Access != NativePluginGuestAccess.ReadOnly || code.Length == 0 || code.Length != context.Authority.CodeBytes ||
            context.Authority.CodeSha256.Length != 64 || !context.Authority.CodeSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Native local caller lacks its exact attached SCPT byte extent.");
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        for (uint offset = 0; offset < code.Length;)
        {
            var count = checked((int)Math.Min(MaximumGuestTransfer, code.Length - offset));
            hash.AppendData(ReadGuest(code, offset, count)); offset += checked((uint)count);
        }
        if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), context.Authority.CodeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native local caller code belongs to another source program or changed bytes.");
    }

    private void EndNvseLocalCall(NativeNvseExpressionCaller caller)
    {
        if (caller.LocalContext is not { } context) return;
        if (_nvseLocalCallers.Count == 0 || _nvseLocalCallers[^1] != caller.Id || context.Active <= 0)
            throw Fatal(new InvalidDataException("Native event-list caller retirement is not nested in owner order."));
        _nvseLocalCallers.RemoveAt(_nvseLocalCallers.Count - 1); --context.Active;
        if (context.Transfer is not null && Fault is null) throw Fatal(new InvalidDataException("Native command lost an event-list synchronization extent."));
    }
}
