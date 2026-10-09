namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private byte[] DispatchNvseBinaryOutput(Frame frame, BinaryReader reader, ulong caller, NativeNvseBinaryBinding binding)
    {
        var id = reader.ReadUInt64();
        if (!_nvseBinaryPending.TryGetValue(id, out var pending) || pending.Caller != caller || !ReferenceEquals(pending.Binding, binding))
            throw new InvalidDataException("Binary transfer belongs to another caller, source or completed lifetime.");
        try
        {
            if (frame.Operation == BinaryEnd)
            {
                Finish(reader);
                if (pending.OutputProduced != pending.OutputDelivered || pending.OutputDelivered != (pending.Output?.Actual ?? 0) ||
                    pending.BufferProduced != pending.BufferDelivered || pending.BufferDelivered != pending.Buffer.Length ||
                    !ReadNvseGraphBytes(binding.Buffer).AsSpan().SequenceEqual(binding.PublishedBuffer))
                    throw new InvalidDataException("Binary member ended before all actual source bytes/native buffer writes completed.");
                pending.Output?.Complete();
                _nvseBinaryCalls[pending.Receipt] = _nvseBinaryCalls[pending.Receipt] with { Complete = true };
                _nvseBinaryPending.Remove(id); return Payload(writer => writer.Write(1U));
            }
            var at = reader.ReadUInt32(); var count = reader.ReadUInt32();
            if (frame.Operation == BinaryOutputSlice)
            {
                Finish(reader);
                if (pending.Output is null || at != pending.OutputProduced || pending.OutputProduced != pending.OutputDelivered ||
                    count == 0 || count > MaximumPayload - 64 || at > pending.Output.Actual || count > pending.Output.Actual - at)
                    throw new InvalidDataException("Native binary output skipped/repeated bytes or an undelivered slice.");
                var bytes = pending.Output.Slice(at, count);
                if (bytes.Length != count || pending.Output.Position != checked(at + count))
                    throw new InvalidDataException("Binary backend returned an incomplete or competing produced extent.");
                pending.LastOutputSlice = bytes; pending.OutputProduced = checked(at + count);
                UpdateBinaryReceipt(pending);
                return Payload(writer => { writer.Write(count); writer.Write(bytes); });
            }
            if (frame.Operation == BinaryOutputAck)
            {
                if (at != pending.OutputDelivered || count == 0 || count != pending.OutputProduced - pending.OutputDelivered ||
                    pending.LastOutputSlice is not { } expected || expected.Length != count)
                    throw new InvalidDataException("Native binary output acknowledged absent/repeated/different produced bytes.");
                var observed = reader.ReadBytes(checked((int)count)); Finish(reader);
                if (!observed.AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException("Actual native destination differs from its source-produced binary output.");
                pending.OutputDelivered = checked(at + count); pending.LastOutputSlice = null; UpdateBinaryReceipt(pending);
                return Payload(writer => writer.Write(count));
            }
            if (frame.Operation == BinaryBufferSlice)
            {
                Finish(reader);
                if (at != pending.BufferProduced || pending.BufferProduced != pending.BufferDelivered || count == 0 ||
                    count > (MaximumPayload - 64) / 2 || at > pending.Buffer.Length || count > pending.Buffer.Length - at)
                    throw new InvalidDataException("Native source buffer slice skipped, repeated or exceeded its complete original written extent.");
                pending.BufferProduced = checked(at + count); UpdateBinaryReceipt(pending);
                return Payload(writer =>
                {
                    writer.Write(count); writer.Write(binding.PublishedBuffer.AsSpan(checked((int)at), checked((int)count)));
                    writer.Write(pending.Buffer.AsSpan(checked((int)at), checked((int)count)));
                });
            }
            if (frame.Operation != BinaryBufferAck) throw new InvalidDataException("Unknown native binary transfer callback.");
            Finish(reader);
            if (at != pending.BufferDelivered || count == 0 || count != pending.BufferProduced - pending.BufferDelivered ||
                !ReadGuest(binding.Buffer, at, checked((int)count)).AsSpan().SequenceEqual(pending.Buffer.AsSpan(checked((int)at), checked((int)count))))
                throw new InvalidDataException("Native source buffer lacks its actual ordered write receipt.");
            pending.Buffer.AsSpan(checked((int)at), checked((int)count)).CopyTo(binding.PublishedBuffer.AsSpan(checked((int)at), checked((int)count)));
            pending.BufferDelivered = checked(at + count); UpdateBinaryReceipt(pending);
            return Payload(writer => writer.Write(count));
        }
        catch (Exception error)
        {
            try { pending.Output?.Fail(error); } catch (Exception retained) { error = new AggregateException(error, retained); }
            try { binding.Authority.File.RetainBinaryFailure(error); } catch (Exception retained) { error = new AggregateException(error, retained); }
            throw error;
        }
    }

    private void UpdateBinaryReceipt(NativeBinaryPending pending)
        => _nvseBinaryCalls[pending.Receipt] = _nvseBinaryCalls[pending.Receipt] with
        {
            ProducedOutput = pending.OutputProduced, DeliveredOutput = pending.OutputDelivered,
            ProducedBuffer = pending.BufferProduced, DeliveredBuffer = pending.BufferDelivered,
        };
}
