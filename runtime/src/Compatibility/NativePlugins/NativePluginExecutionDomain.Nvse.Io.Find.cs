namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint FindBeginCallback = 11, FindCandidateCallback = 12, FindResultCallback = 13, FindCloseCallback = 14, FindBackendCallback = 15;
    private readonly List<NativePluginFindReceipt> _retiredFindReceipts = [];
    internal IReadOnlyList<NativePluginFindReceipt> NvseFindReceipts => _privateIo?.FindReceipts ?? _retiredFindReceipts.AsReadOnly();
    private void RetainPrivateFindReceipts()
    {
        if (_privateIo is null) return;
        _retiredFindReceipts.Clear(); _retiredFindReceipts.AddRange(_privateIo.FindReceipts);
    }
    internal void RequirePrivateProfileDirectorySaveOwned()
    {
        VerifyOwner();
        if (NvseFindReceipts.Count != 0 || NvseProfileReceipts.Count != 0)
            throw new NotSupportedException("Original native directory/profile caller buffers, handles and state lack their current/cold campaign owner.");
    }
    private byte[] DispatchPrivateFind(Frame frame, ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        if (frame.Operation == FindBeginCallback)
        {
            var pattern = ReadText(reader); Finish(reader); var selection = owner.FindBegin(parent, pattern);
            return Payload(writer =>
            {
                writer.Write(selection.Id); WriteText(writer, selection.Pattern);
                writer.Write(checked((uint)selection.Sources.Count));
                foreach (var source in selection.Sources) WriteText(writer, source.PhysicalDirectory);
            });
        }
        var id = reader.ReadUInt64();
        if (frame.Operation == FindBackendCallback)
        {
            var source = reader.ReadUInt32(); var operation = reader.ReadUInt32(); var handle = reader.ReadUInt32();
            var result = reader.ReadUInt32(); var last = reader.ReadUInt32(); Finish(reader);
            owner.FindBackend(parent, id, source, operation, handle, result, last); return Payload(writer => writer.Write(1U));
        }
        if (frame.Operation == FindCandidateCallback)
        {
            var source = reader.ReadUInt32(); var handle = reader.ReadUInt32(); var wide = reader.ReadUInt32();
            if (wide > 1) throw new InvalidDataException("Native find candidate has an unknown string ABI.");
            var data = new NativePluginFindData(reader.ReadUInt32(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(),
                reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadUInt32(), ReadText(reader), ReadText(reader)); Finish(reader);
            return Payload(writer => writer.Write(owner.FindCandidate(parent, id, source, handle, wide == 1, data) ? 1U : 0U));
        }
        if (frame.Operation == FindResultCallback)
        {
            var operation = reader.ReadUInt32(); var handle = reader.ReadUInt32(); var result = reader.ReadUInt32(); var last = reader.ReadUInt32(); Finish(reader);
            owner.FindResult(parent, id, operation, handle, result, last); return Payload(writer => writer.Write(1U));
        }
        if (frame.Operation == FindCloseCallback)
        {
            var publicHandle = reader.ReadUInt32(); var count = reader.ReadUInt32();
            if (count > (reader.BaseStream.Length - reader.BaseStream.Position) / 12) throw new InvalidDataException("Native find close exceeds its actual backend extent.");
            var handles = new List<(uint, uint, uint)>();
            for (var at = 0U; at < count; ++at) handles.Add((reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()));
            Finish(reader); owner.FindClosed(parent, id, publicHandle, handles); return Payload(writer => writer.Write(1U));
        }
        throw new NotSupportedException("Native directory callback has no first-party source/handle owner.");
    }
}
