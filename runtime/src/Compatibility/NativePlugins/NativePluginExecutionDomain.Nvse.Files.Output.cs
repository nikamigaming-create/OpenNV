using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeNvseSourceOutputReceipt(ulong Callback, ulong Caller, ulong Object,
    ulong Transfer, uint Offset, uint Count, string SourceSha256, string? ObservedSha256, bool? Matched);

internal sealed partial class NativePluginExecutionDomain
{
    private readonly List<NativeNvseSourceOutputReceipt> _nvseSourceOutputs = [];
    internal IReadOnlyList<NativeNvseSourceOutputReceipt> NvseSourceOutputs => _nvseSourceOutputs.AsReadOnly();

    private byte[] DispatchSourceFileOutput(Frame frame, BinaryReader reader, ulong transfer,
        NativeSourceFileTransfer pending, INativeNvseSourceFileAuthority file)
    {
        try
        {
            RequireSourcePublicationComplete(pending);
            if (frame.Operation == SourceFileSlice)
            {
                var at = reader.ReadUInt32(); var count = reader.ReadUInt32(); Finish(reader);
                if (at != pending.Position || pending.Position != pending.Delivered || count == 0 ||
                    count > MaximumPayload - 64 || at > pending.Bytes.Length || count > pending.Bytes.Length - at)
                    throw new InvalidDataException("Native source output skipped, repeated or exceeded its actual produced/delivered byte lifetime.");
                var bytes = pending.Bytes.Slice(checked((int)at), checked((int)count)).ToArray();
                pending.LastSlice = bytes; pending.Position = checked(at + count); UpdateSourceFileOutput(pending);
                _nvseSourceOutputs.Add(new(frame.Id, pending.Caller, pending.Object.Id, transfer, at, count,
                    Convert.ToHexString(SHA256.HashData(bytes)), null, null));
                return Payload(writer => { writer.Write(count); writer.Write(bytes); });
            }
            if (frame.Operation == SourceFileOutputAck)
            {
                var at = reader.ReadUInt32(); var count = reader.ReadUInt32();
                if (at != pending.Delivered || count == 0 || count != pending.Position - pending.Delivered ||
                    pending.LastSlice is not { } expected || expected.Length != count)
                    throw new InvalidDataException("Native source acknowledgement has no exact preceding produced slice.");
                var observed = reader.ReadBytes(checked((int)count)); Finish(reader);
                var matches = observed.AsSpan().SequenceEqual(expected);
                _nvseSourceOutputs.Add(new(frame.Id, pending.Caller, pending.Object.Id, transfer, at, count,
                    Convert.ToHexString(SHA256.HashData(expected)), Convert.ToHexString(SHA256.HashData(observed)), matches));
                if (!matches) throw new InvalidDataException("Actual native source destination differs from its complete original produced bytes.");
                pending.Delivered = checked(at + count); pending.LastSlice = null; UpdateSourceFileOutput(pending);
                return Payload(writer => writer.Write(count));
            }
            if (frame.Operation != SourceFileEnd) throw new InvalidDataException("Unknown native source output callback.");
            Finish(reader);
            if (pending.Position != pending.Bytes.Length || pending.Delivered != pending.Bytes.Length || pending.LastSlice is not null)
                throw new InvalidDataException("Native source output ended before every produced byte had its actual destination acknowledgement.");
            _nvseFileTransfers.Remove(transfer);
            _nvseFileCalls[pending.Receipt] = _nvseFileCalls[pending.Receipt] with { OutputCompleted = true };
            return Payload(writer => writer.Write(1U));
        }
        catch (Exception error)
        {
            if (file is not INativeNvseSourceFileCurrentAuthority actual)
                throw new AggregateException("A consumed source output lacks its actual shared failure authority.", error,
                    new NotSupportedException("Native source file failed-output continuation is unowned."));
            try { actual.RetainSourceFileOutputFailure(error); }
            catch (Exception retained) { throw new AggregateException("Native source output and shared-prefix retention failed.", error, retained); }
            throw;
        }
    }

    private void UpdateSourceFileOutput(NativeSourceFileTransfer transfer)
        => _nvseFileCalls[transfer.Receipt] = _nvseFileCalls[transfer.Receipt] with
        { ProducedOutput = transfer.Position, DeliveredOutput = transfer.Delivered };
}
