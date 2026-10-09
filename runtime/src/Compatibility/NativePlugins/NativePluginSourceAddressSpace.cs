using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Reserve the selected PE's address identity, without mapping its bytes or
// making any unimplemented object/code address readable or executable.
internal sealed record NativePluginSourceAddressSpace
{
    internal string RuntimePath { get; }
    internal string RuntimeSha256 { get; }
    internal uint ImageBase { get; }
    internal uint ImageBytes { get; }
    private NativePluginSourceAddressSpace(string path, string sha256, uint imageBase, uint imageBytes)
    { RuntimePath = path; RuntimeSha256 = sha256; ImageBase = imageBase; ImageBytes = imageBytes; }

    internal static NativePluginSourceAddressSpace Read(FileStream retainedSource, string path, string sha256)
    {
        var position = retainedSource.Position;
        try
        {
            retainedSource.Position = 0;
            using var pe = new PEReader(retainedSource, PEStreamOptions.LeaveOpen);
            var header = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Source address space has no PE header.");
            if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || header.Magic != PEMagic.PE32 ||
                pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll) || header.ImageBase == 0 ||
                header.SizeOfImage <= 0 || header.ImageBase + (ulong)header.SizeOfImage > 1UL << 32)
                throw new InvalidDataException("Source address reservation has no complete selected executable PE32 extent.");
            return new(path, sha256, checked((uint)header.ImageBase), checked((uint)header.SizeOfImage));
        }
        finally { retainedSource.Position = position; }
    }
}

internal sealed partial class NativePluginPrivateIo
{
    internal NativePluginSourceAddressSpace? SourceAddressSpace { get; private set; }
    internal void BindSourceAddressSpace(NativeNvseHostSource host)
    {
        ArgumentNullException.ThrowIfNull(host);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_generation is not null || SourceAddressSpace is not null ||
            !StringComparer.OrdinalIgnoreCase.Equals(host.StackIdentity, Selection.StackSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(Canonical(host.RuntimeDirectory), Canonical(Selection.RuntimeDirectory)) ||
            !Selection.OriginalRoots.Any(root => Within(root, host.RuntimePath)))
            throw new InvalidDataException("Source address reservation has no unique pre-creation selected host lifetime.");
        SourceAddressSpace = host.SourceAddressSpace;
    }
}
