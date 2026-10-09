namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginSourceAddressSpaceReceipt(ulong Generation, int ProcessId,
    string RuntimeSha256, uint ImageBase, uint ImageBytes, uint ReservedBytes);

internal sealed partial class NativePluginExecutionDomain
{
    internal NativePluginSourceAddressSpaceReceipt? SourceAddressSpaceReceipt { get; private set; }

    private void ReadSourceAddressSpace(NativePluginPrivateIo? io)
    {
        if (io?.SourceAddressSpace is not { } source) return;
        using var reader = Exchange(NativePluginDomainOperation.SourceAddressSpace,
            Payload(writer => { writer.Write(source.ImageBase); writer.Write(source.ImageBytes); }));
        var imageBase = reader.ReadUInt32(); var imageBytes = reader.ReadUInt32(); var reservedBytes = reader.ReadUInt32();
        Finish(reader);
        if (imageBase != source.ImageBase || imageBytes != source.ImageBytes || reservedBytes < imageBytes ||
            (ulong)imageBase + reservedBytes > 1UL << 32)
            throw new InvalidDataException("Native source reservation changed its selected PE address identity.");
        SourceAddressSpaceReceipt = new(Generation, ProcessId, source.RuntimeSha256, imageBase, imageBytes, reservedBytes);
    }

    private void RequireSourceAddressSpace(NativeNvseHostSource host)
    {
        var source = host.SourceAddressSpace;
        if (SourceAddressSpaceReceipt is not { } receipt || receipt.Generation != Generation || receipt.ProcessId != ProcessId ||
            receipt.ImageBase != source.ImageBase || receipt.ImageBytes != source.ImageBytes ||
            !StringComparer.OrdinalIgnoreCase.Equals(receipt.RuntimeSha256, host.RuntimeSha256))
            throw new NotSupportedException("Original NVSE admission requires the selected source image reservation before child startup.");
    }
}
