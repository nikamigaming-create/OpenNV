namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private byte[] DispatchNvseBinary(Frame frame)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var callerId = reader.ReadUInt64(); var binaryId = reader.ReadUInt64(); var address = reader.ReadUInt32();
        var contributorId = reader.ReadUInt64(); var contributorAddress = reader.ReadUInt32();
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Binary callback has no actual retained module.");
        if (thread != NativeThread || module != plugin.Module || handle != plugin.Handle ||
            !_nvseExpressionCallers.TryGetValue(callerId, out var caller) || _nvseLocalCallers.Count == 0 ||
            _nvseLocalCallers[^1] != callerId || !_nvseBinaryFiles.TryGetValue(binaryId, out var binding) ||
            binding.Image.Address != address || binding.Contributor.Id != contributorId ||
            binding.Contributor.Address != contributorAddress || !caller.SourceObjects.Contains(binding.Contributor))
            throw new InvalidDataException("Binary callback lacks its exact native thread/module/caller/source-child lifetime.");
        RequireNvseBinaryBinding(plugin, binding, frame.Operation == BinaryBegin);
        if (frame.Operation != BinaryBegin) return DispatchNvseBinaryOutput(frame, reader, callerId, binding);
        var method = (NativeNvseBinaryMethod)reader.ReadUInt32(); var first = reader.ReadUInt32(); var second = reader.ReadUInt32(); Finish(reader);
        var declaration = _nvseBinaryDeclaration ?? throw new NotSupportedException("Binary original source methods are unbound.");
        if (!Enum.IsDefined(method) || !declaration.Methods.Any(row => row.Method == method) ||
            _nvseBinaryPending.Values.Any(pending => pending.Caller == callerId || ReferenceEquals(pending.Binding, binding)))
            throw new InvalidOperationException("Binary operation has no declared method or overlaps an unfinished native output.");
        INativeNvseBinaryOutput? output = null;
        try
        {
            var read = binding.Authority.File.CallBinary(method, first, second); output = read.Output;
            binding.Authority.File.RequireBinaryCurrent(); binding.Authority.RequireOriginalCrtFileCurrent();
            if (method is NativeNvseBinaryMethod.Read or NativeNvseBinaryMethod.ReadInherited)
            {
                if (output is null || output.Requested != first || output.Position != 0 || output.Actual > first || output.Actual != read.Result)
                    throw new InvalidDataException("Binary source read lacks its complete original actual-count/output owner.");
            }
            else if (output is not null) throw new InvalidDataException("A scalar binary member invented byte output.");
            if (read.WrittenBuffer.Length > binding.Buffer.Length)
                throw new InvalidDataException("Binary source writes exceed its genuine native buffer allocation.");
            var alternate = method == NativeNvseBinaryMethod.SelectProcedures ? first != 0 : binding.AlternateProcedures;
            var before = binding.PublishedImage;
            var after = binding.Authority.ReadCompleteImage(binding.Addresses, read.Fields, alternate);
            RequireCompleteBinaryImage(after, binding.Addresses, alternate, declaration.Extent);
            RequireBinaryFieldProjection(after, read.Fields);
            for (var at = 0; at < after.Length; ++at)
                if (before[at] != after[at] && !(at is >= 4 and < 16 or >= 0x14 and < 0x20 or 0x2c or >= 0x150 and < 0x158))
                    throw new NotSupportedException("Binary member changed an unowned original class field.");
            var pendingId = checked(++_nextNvseBinaryCall); var receipt = _nvseBinaryCalls.Count;
            var pending = new NativeBinaryPending(callerId, binding, output, read.WrittenBuffer.ToArray(), receipt);
            _nvseBinaryPending.Add(pendingId, pending);
            _nvseBinaryCalls.Add(new(frame.Id, callerId, binding.Id, method, first, second, read.Result,
                output?.Actual ?? 0, 0, 0, 0, 0, false, read.Diagnostic));
            binding.PublishedImage = after; binding.AlternateProcedures = alternate;
            return Payload(writer =>
            {
                writer.Write(read.Result); writer.Write(pendingId); writer.Write(output?.Actual ?? 0);
                writer.Write(checked((uint)after.Length)); writer.Write(checked((uint)pending.Buffer.Length));
                writer.Write(before); writer.Write(after);
            });
        }
        catch (Exception error)
        {
            try { output?.Fail(error); } catch (Exception retained) { error = new AggregateException(error, retained); }
            try { binding.Authority.File.RetainBinaryFailure(error); } catch (Exception retained) { error = new AggregateException(error, retained); }
            throw error;
        }
    }

    private void RequireNvseBinaryBinding(NativeNvsePlugin plugin, NativeNvseBinaryBinding binding, bool inspectBuffer)
    {
        if (binding.Generation != Generation || binding.Module != plugin.Module || binding.Retired ||
            !_nvseBinaryFiles.TryGetValue(binding.Id, out var retained) || !ReferenceEquals(retained, binding))
            throw new InvalidOperationException("Binary receiver is foreign or retired.");
        VerifyNvseSourceObject(plugin, binding.Contributor); VerifyGuest(binding.Image); VerifyGuest(binding.Buffer);
        binding.Authority.RequireCurrent(); binding.Authority.RequireOriginalCrtFileCurrent(); binding.Authority.File.RequireBinaryCurrent();
        if (!StringComparer.OrdinalIgnoreCase.Equals(binding.Authority.SourceSha256, binding.Contributor.Authority.SourceSha256) ||
            !ReadNvseGraphBytes(binding.Image).AsSpan().SequenceEqual(binding.PublishedImage) ||
            inspectBuffer && !ReadNvseGraphBytes(binding.Buffer).AsSpan().SequenceEqual(binding.PublishedBuffer))
            throw new InvalidDataException("Binary class/source/buffer changed outside its actual publication owner.");
    }
}
