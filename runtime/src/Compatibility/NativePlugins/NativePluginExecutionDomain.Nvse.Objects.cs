using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private readonly Dictionary<ulong, NativeNvseSourceObject> _nvseSourceObjects = [];
    private readonly List<NativeNvseObjectMethodReceipt> _nvseObjectMethods = [];
    private ulong _nextNvseSourceObject;
    private bool _nvseScriptInterface;
    internal IReadOnlyList<NativeNvseObjectMethodReceipt> NvseObjectMethods => _nvseObjectMethods.AsReadOnly();

    internal void AttachNvseScriptInterface(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall();
        if (_nvseScriptInterface || plugin.Phase is not (NativeNvsePhase.Mapped or NativeNvsePhase.QueriedTrue))
            throw new InvalidOperationException("Script interface attaches once before genuine original Load.");
        using var reader = Exchange(NativePluginDomainOperation.NvseScriptInterface, Payload(writer => writer.Write(plugin.Module)));
        if (reader.ReadUInt32() != 32) throw new InvalidDataException("Public Script interface extent drifted.");
        Finish(reader); _nvseScriptInterface = true;
    }

    internal NativeNvseSourceObject PublishNvseScript(NativeNvsePlugin plugin, NativeNvseScriptAuthority authority)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); ArgumentNullException.ThrowIfNull(authority);
        authority.RequireCurrent(); var snapshot = authority.Read();
        ValidateNvseScriptSnapshot(snapshot);
        if (string.IsNullOrWhiteSpace(authority.SourceOwner) || authority.SourceSha256.Length != 64 ||
            !authority.SourceSha256.All(Uri.IsHexDigit) || !StringComparer.OrdinalIgnoreCase.Equals(authority.CodeSha256,
                Convert.ToHexString(SHA256.HashData(snapshot.Code.Span))))
            throw new InvalidDataException("Script publication has no complete source/code identity.");
        if (_nvseSourceObjects.Values.Any(value => value.FormId == snapshot.FormId))
            throw new InvalidOperationException("This native generation already owns the Script identity.");
        var dependencies = snapshot.Contributors.Concat(snapshot.References.Where(row => row.Form is not null).Select(row => row.Form!))
            .Concat(snapshot.Quest is { } quest ? new[] { quest } : Array.Empty<NativeNvseSourceObject>()).Distinct().ToArray();
        foreach (var dependency in dependencies) VerifyNvseSourceObject(plugin, dependency);
        var lease = authority.RetainSource(); NativePluginGuestAllocation? image = null, metadata = null, code = null;
        NativeNvseSourceObject? result = null;
        try
        {
            if (!snapshot.Code.IsEmpty) code = PublishNvseBytes(snapshot.Code.Span);
            var builder = new NativeNvseScriptMetadata(snapshot); var bytes = builder.Bytes;
            metadata = AllocateGuest(checked((uint)bytes.Length), NativePluginGuestAccess.ReadWrite);
            builder.Relocate(bytes, metadata.Address);
            WriteGuestChunks(metadata, bytes); metadata = SealNvseGuest(metadata);
            var fields = new byte[84];
            fields[4] = 17; snapshot.FormRuntimeBytes.Span.CopyTo(fields.AsSpan(5, 3));
            BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(8), snapshot.Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(12), snapshot.FormId);
            builder.List(fields, 16, builder.Contributors, metadata.Address);
            snapshot.Info.Span.CopyTo(fields.AsSpan(24, 20));
            Put(44, builder.Text is { } text ? checked(metadata.Address + text) : 0);
            Put(48, code?.Address ?? 0); Put(52, snapshot.RuntimeWord);
            Put(56, BitConverter.SingleToUInt32Bits(snapshot.DelayCounter)); Put(60, BitConverter.SingleToUInt32Bits(snapshot.SecondsPassed));
            Put(64, snapshot.Quest?.Address ?? 0);
            builder.List(fields, 68, builder.References, metadata.Address); builder.List(fields, 76, builder.Variables, metadata.Address);
            image = AllocateGuest(84, NativePluginGuestAccess.ReadWrite, fields);
            result = new(Generation, plugin.Module, checked(++_nextNvseSourceObject), snapshot.FormId,
                image, metadata, code, authority, lease, dependencies, FingerprintNvseScript(authority, snapshot));
            using var reader = Exchange(NativePluginDomainOperation.NvseObjectBind, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(result.Id); writer.Write((uint)NativeNvseSourceClass.Script); writer.Write(image.Handle); writer.Write(image.Address); writer.Write(image.Length);
                writer.Write(snapshot.FormId); writer.Write(metadata.Handle); writer.Write(checked(metadata.Address + builder.EditorId));
            }));
            if (reader.ReadUInt64() != result.Id || reader.ReadUInt32() != image.Address || reader.ReadUInt32() == 0)
                throw new InvalidDataException("Source Script lacks its actual native object/vtable receipt.");
            Finish(reader);
            // Native binding installs only first-party callable vtable entries,
            // then seals the entire source object. No source bytes execute.
            result.Image = image with { Access = NativePluginGuestAccess.ReadOnly };
            _guestAllocations[image.Handle] = result.Image;
            authority.RequireCurrent(); _nvseSourceObjects.Add(result.Id, result);
            foreach (var dependency in dependencies) ++dependency.Dependents;
            return result;

            void Put(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(offset), value);
        }
        catch (Exception error)
        {
            // A partially registered native object cannot be retried or made
            // invisible by disposing only its C# fields. Retire the generation.
            lease.Dispose(); throw Fatal(error);
        }
    }

    private static void ValidateNvseScriptSnapshot(NativeNvseScriptSnapshot value)
    {
        if (value.FormId == 0 || value.FormRuntimeBytes.Length != 3 || value.Info.Length != 20 ||
            string.IsNullOrWhiteSpace(value.RuntimeFieldOwner) || !float.IsFinite(value.DelayCounter) || !float.IsFinite(value.SecondsPassed) ||
            value.Contributors is null || value.Contributors.Count == 0 || value.Contributors.Any(row => row.Class != NativeNvseSourceClass.ModInfo) ||
            value.Quest is { Class: not NativeNvseSourceClass.Quest } || value.References is null || value.Variables is null ||
            value.EditorId.Span.Contains((byte)0) || value.Variables.Any(row => row.ScalarBytes.Length != 24 || row.Name.Length >= ushort.MaxValue || row.Name.Span.Contains((byte)0)) ||
            value.References.Any(row => row.Name.Length >= ushort.MaxValue || row.Name.Span.Contains((byte)0) || row.Form is not null && row.Variable != 0))
            throw new NotSupportedException("Native Script lacks complete source fields, contributor objects or runtime construction facts.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(value.Info.Span[4..]) != value.References.Count ||
            BinaryPrimitives.ReadUInt32LittleEndian(value.Info.Span[8..]) != value.Code.Length ||
            BinaryPrimitives.ReadUInt16LittleEndian(value.Info.Span[16..]) != 1 && value.Quest is not null)
            throw new InvalidDataException("Native Script header/link extent differs from the original declaration.");
    }

    private NativePluginGuestAllocation PublishNvseBytes(ReadOnlySpan<byte> bytes)
    {
        var allocation = AllocateGuest(checked((uint)bytes.Length), NativePluginGuestAccess.ReadWrite);
        WriteGuestChunks(allocation, bytes); return SealNvseGuest(allocation);
    }
    private void WriteGuestChunks(NativePluginGuestAllocation allocation, ReadOnlySpan<byte> bytes)
    {
        for (var offset = 0; offset < bytes.Length; offset += checked((int)MaximumGuestTransfer))
            WriteGuest(allocation, checked((uint)offset), bytes.Slice(offset, Math.Min(bytes.Length - offset, checked((int)MaximumGuestTransfer))));
    }
    private NativePluginGuestAllocation SealNvseGuest(NativePluginGuestAllocation allocation)
    {
        VerifyGuest(allocation);
        using var reader = Exchange(NativePluginDomainOperation.GuestSeal, Payload(writer => writer.Write(allocation.Handle)));
        if (reader.ReadUInt32() != allocation.Address || reader.ReadUInt32() != allocation.Length)
            throw new InvalidDataException("Native source buffer sealing extent drifted.");
        Finish(reader); var sealedAllocation = allocation with { Access = NativePluginGuestAccess.ReadOnly };
        _guestAllocations[allocation.Handle] = sealedAllocation; return sealedAllocation;
    }

    private void VerifyNvseSourceObject(NativeNvsePlugin plugin, NativeNvseSourceObject value)
    {
        VerifyNvse(plugin);
        if (value.Generation != Generation || value.Module != plugin.Module || value.Retired || value.Staged ||
            !_nvseSourceObjects.TryGetValue(value.Id, out var current) || !ReferenceEquals(current, value))
            throw new InvalidOperationException("Original source object is forged, foreign or retired.");
        value.Authority.RequireCurrent(); VerifyGuest(value.Image); VerifyGuest(value.Metadata);
        if (value.Code is { } code) VerifyGuest(code);
        var actual = value.Authority switch
        {
            NativeNvseScriptAuthority script => FingerprintNvseScript(script, script.Read()),
            NativeNvseDataAuthority data => FingerprintNvseData(data, data.Read()),
            _ => throw new NotSupportedException("Native class has no actual field authority.")
        };
        if (!StringComparer.Ordinal.Equals(actual, value.PublishedSha256))
            throw new NotSupportedException("Authoritative native object fields changed; this read-only projection requires its real refresh/mutation owner.");
    }

    internal void RetireNvseSourceObject(NativeNvsePlugin plugin, NativeNvseSourceObject value)
    {
        VerifyNvseSourceObject(plugin, value); RequireNvseEmptyCall();
        if (_nvseSourceGraphs.ContainsKey(value.Id))
            throw new InvalidOperationException("A cyclic native source graph must retire through its complete group owner.");
        if (value.Calls != 0 || value.Dependents != 0 || _nvseLocals.Values.Any(local => ReferenceEquals(local.Script, value)))
            throw new InvalidOperationException("An actual object, event list or call still owns this native Script.");
        using (var reader = Exchange(NativePluginDomainOperation.NvseObjectRetire, Payload(writer => { writer.Write(plugin.Module); writer.Write(value.Id); })))
        { if (reader.ReadUInt32() != 1) throw new InvalidDataException("Native Script object did not retire."); Finish(reader); }
        ReleaseGuest(value.Image); ReleaseGuest(value.Metadata); if (value.Code is { } code) ReleaseGuest(code);
        foreach (var dependency in value.Dependencies) --dependency.Dependents;
        _nvseSourceObjects.Remove(value.Id); value.Retired = true; value.SourceLease.Dispose();
    }

    private byte[] DispatchNvseSourceObject(Frame frame)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64(); var handle = reader.ReadUInt32(); var callerId = reader.ReadUInt64();
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Source object callback has no retained module.");
        if (thread != NativeThread || module != plugin.Module || handle != plugin.Handle ||
            !_nvseExpressionCallers.TryGetValue(callerId, out var caller) || _nvseLocalCallers.Count == 0 || _nvseLocalCallers[^1] != callerId)
            throw new InvalidDataException("Source object callback has no exact active campaign caller.");
        if (frame.Operation == 0x403)
        {
            var formId = reader.ReadUInt32(); Finish(reader);
            if (formId == 0) return Payload(writer => writer.Write(0U));
            var target = caller.SourceObjects.SingleOrDefault(value => value.FormId == formId) ??
                throw new NotSupportedException($"Native TESForm {formId:x8} has no complete admitted source projection in this call.");
            VerifyNvseSourceObject(plugin, target); return Payload(writer => writer.Write(target.Address));
        }
        var id = reader.ReadUInt64(); var method = reader.ReadUInt32(); var address = reader.ReadUInt32(); Finish(reader);
        if (!_nvseSourceObjects.TryGetValue(id, out var value) || value.Address != address || !caller.SourceObjects.Contains(value))
            throw new InvalidDataException("TESForm virtual callback is outside its retained source graph/call.");
        VerifyNvseSourceObject(plugin, value);
        var result = method switch
        {
            0x8c => value.Class switch
            {
                NativeNvseSourceClass.Script => 17U,
                NativeNvseSourceClass.Quest => 71U,
                _ => throw new NotSupportedException("A ModInfo object is not TESForm.")
            },
            0xf0 or 0x100 => 0U,
            0x130 => 1U,
            0x138 when value.Class == NativeNvseSourceClass.Quest => 1U,
            _ => throw new NotSupportedException($"Native TESForm virtual method {method:x} has no source engine owner.")
        };
        _nvseObjectMethods.Add(new(frame.Id, callerId, id, method, result)); return Payload(writer => writer.Write(result));
    }

    private void ClearNvseSourceObjects()
    {
        ClearNvseSourceGraphs();
        var errors = new List<Exception>();
        foreach (var value in _nvseSourceObjects.Values)
        {
            value.Retired = true;
            try { value.SourceLease.Dispose(); } catch (Exception error) { errors.Add(error); }
        }
        _nvseSourceObjects.Clear(); _nvseScriptInterface = false;
        if (errors.Count != 0) throw new AggregateException("Native source lease retirement retained failures.", errors);
    }
}
