namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginFileMetadataReceipt(ulong Sequence, ulong Generation, ulong Parent,
    ulong Route, uint Handle, uint Api, uint InformationClass, uint Requested, bool SdkReturned,
    uint LastError, byte[]? Output);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint FileMetadataCallback = 26;
    private readonly List<NativePluginFileMetadataReceipt> _fileMetadataReceipts = [];
    internal IReadOnlyList<NativePluginFileMetadataReceipt> NvseFileMetadataReceipts => _fileMetadataReceipts.AsReadOnly();
    private static bool OwnedFileMetadataClass(uint informationClass)
        => informationClass is 0 or 1 or 2 or 7 or 8 or 9 or 12 or 13 or 16 or 17 or 18 or 23 or 24;
    private byte[] DispatchPrivateFileMetadata(ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        var route = reader.ReadUInt64(); var handle = reader.ReadUInt32(); var api = reader.ReadUInt32();
        var informationClass = reader.ReadUInt32(); var requested = reader.ReadUInt32(); var result = reader.ReadUInt32();
        var last = reader.ReadUInt32(); var available = reader.ReadUInt32(); var bytes = ReadEnvironmentBytes(reader); Finish(reader);
        if (route == 0 || result > 1 || available > 1 || (result != 0) != (available != 0) ||
            available == 0 && bytes.Length != 0 || available != 0 && bytes.Length != requested)
            throw new InvalidDataException("File metadata result lacks its actual caller output/status extent.");
        if (api == 7)
        {
            if (handle != 0 || informationClass != 0 || requested != 36)
                throw new InvalidDataException("File attributes changed their public WIN32_FILE_ATTRIBUTE_DATA contract.");
            owner.ValidateResult(route, 7, false);
        }
        else if (api == 13)
        {
            if (!_ioFiles.TryGetValue(route, out var file) || handle == 0 || file.Handle != handle || !OwnedFileMetadataClass(informationClass))
                throw new InvalidDataException("File metadata targets a foreign handle or unowned enumeration/set-information class.");
            owner.RecordHandle(parent, route, 13, 0, result != 0 ? 0 : last);
        }
        else throw new NotSupportedException("File metadata API has no public source owner.");
        // SDK output is the actual caller buffer including its padding. These
        // bytes are observation, not a decoded source or semantic readiness claim.
        _fileMetadataReceipts.Add(new(checked((ulong)_fileMetadataReceipts.Count + 1), Generation, parent,
            route, handle, api, informationClass, requested, result != 0, last, available != 0 ? bytes : null));
        return Payload(writer => writer.Write(1U));
    }
    private void RequireNativeFileMetadataSaveOwned()
    {
        if (_fileMetadataReceipts.Count != 0)
            throw new NotSupportedException("Original metadata caller buffers/results lack a complete current/cold owner.");
    }
}
