using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeFileMetadataSnapshot(string Plugin, string SourceSha256, ulong Bytes,
    uint Attributes, ulong Created, ulong Accessed, ulong Written, byte[] Name, byte[] AlternateName,
    uint Volume, ulong FileIdentity);

// This is the real OS producer used by the selected contributor-open contract.
// It never substitutes FileInfo timestamps, an authored file, a cached winner,
// or an input path as a writable diagnostic/configuration destination.
internal sealed class FalloutNativePluginLoadedFileMetadata
{
    private readonly FalloutNativePluginBinaryFile _file;
    private FalloutNativeFileMetadataSnapshot _snapshot;
    internal FalloutNativePluginLoadedFileMetadata(FalloutNativePluginBinaryFile file)
    { _file = file; _snapshot = Read(); }
    internal FalloutNativeFileMetadataSnapshot Capture()
    {
        _file.RequireCurrent();
        return _snapshot with { Name = _snapshot.Name.ToArray(), AlternateName = _snapshot.AlternateName.ToArray() };
    }
    internal bool Refresh()
    {
        _file.RequireIdle(); var next = Read();
        if (next.Volume != _snapshot.Volume || next.FileIdentity != _snapshot.FileIdentity || next.Bytes != _snapshot.Bytes ||
            next.Created != _snapshot.Created || next.Written != _snapshot.Written)
            throw new InvalidDataException("Selected contributor OS identity changed during its retained source lifetime.");
        var changed = next.Attributes != _snapshot.Attributes || next.Accessed != _snapshot.Accessed ||
            !next.Name.AsSpan().SequenceEqual(_snapshot.Name) || !next.AlternateName.AsSpan().SequenceEqual(_snapshot.AlternateName);
        _snapshot = next; return changed;
    }
    internal FalloutNativeFileMetadataSnapshot ValidateRestore(FalloutNativeFileMetadataSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved); _file.RequireIdle();
        if (saved.Name is null || saved.AlternateName is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(saved.Plugin, _file.Plugin) ||
            !StringComparer.OrdinalIgnoreCase.Equals(saved.SourceSha256, _file.SourceSha256))
            throw new InvalidDataException("Cold contributor metadata lost its exact original identity.");
        var actual = Read();
        if (saved.Bytes != actual.Bytes || saved.Attributes != actual.Attributes || saved.Created != actual.Created ||
            saved.Accessed != actual.Accessed || saved.Written != actual.Written || saved.Volume != actual.Volume ||
            saved.FileIdentity != actual.FileIdentity || !saved.Name.AsSpan().SequenceEqual(actual.Name) ||
            !saved.AlternateName.AsSpan().SequenceEqual(actual.AlternateName))
            throw new InvalidDataException("Cold contributor find-data differs from its actual current OS producer.");
        return actual;
    }
    internal void RestoreValidated(FalloutNativeFileMetadataSnapshot validated)
    {
        _file.RequireIdle();
        _snapshot = validated with { Name = validated.Name.ToArray(), AlternateName = validated.AlternateName.ToArray() };
    }
    internal IReadOnlyList<NativeNvseDataField> NativeFields(int at = 0x29c)
    {
        _file.RequireCurrent(); var scalar = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(scalar, _snapshot.Attributes);
        BinaryPrimitives.WriteUInt64LittleEndian(scalar.AsSpan(4), _snapshot.Created);
        BinaryPrimitives.WriteUInt64LittleEndian(scalar.AsSpan(12), _snapshot.Accessed);
        BinaryPrimitives.WriteUInt64LittleEndian(scalar.AsSpan(20), _snapshot.Written);
        BinaryPrimitives.WriteUInt32LittleEndian(scalar.AsSpan(28), checked((uint)(_snapshot.Bytes >> 32)));
        BinaryPrimitives.WriteUInt32LittleEndian(scalar.AsSpan(32), unchecked((uint)_snapshot.Bytes));
        return new NativeNvseDataField[] {
            new(at, scalar, "actual-selected-FindFirstFileA/attributes-times-size"),
            new(checked(at + 44), _snapshot.Name.Concat(new byte[] { 0 }).ToArray(), "actual-selected-FindFirstFileA/long-name-prefix"),
            new(checked(at + 304), _snapshot.AlternateName.Concat(new byte[] { 0 }).ToArray(), "actual-selected-FindFirstFileA/short-name-prefix"),
        };
        // API-reserved words, unused string tails and allocation padding remain
        // uncovered; a defined name prefix cannot manufacture these bytes.
    }

    private FalloutNativeFileMetadataSnapshot Read()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original contributor metadata needs Windows FindFirstFileA.");
        _file.RequireCurrent(); var path = _file.SourcePath;
        _ = FalloutNativePluginBinaryFile.AnsiPath(path);
        var bytes = new byte[320]; var search = FindFirstFileA(path, bytes);
        if (search == new IntPtr(-1)) throw new IOException("Selected contributor FindFirstFileA failed.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        Exception? primary = null;
        try
        {
            if (!GetFileInformationByHandle(_file.SourceHandle, out var actual))
                throw new IOException("Selected contributor handle identity is unavailable.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            var attributes = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            var extent = ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28)) << 32) |
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32));
            if ((attributes & 0x10) != 0 || extent != _file.SourceLength || actual.Size != extent ||
                actual.Created != BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4)) ||
                actual.Written != BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(20)))
                throw new NotSupportedException("Original contributor find-data/retained handle alias producer is not joined.");
            var name = CString(bytes.AsSpan(44, 260)); var alternate = CString(bytes.AsSpan(304, 14));
            if (!StringComparer.OrdinalIgnoreCase.Equals(System.Text.Encoding.ASCII.GetString(name), Path.GetFileName(path)))
                throw new InvalidDataException("FindFirstFileA did not return the exact selected contributor name.");
            return new(_file.Plugin, _file.SourceSha256, extent, attributes,
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4)), BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12)),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(20)), name, alternate, actual.Volume, actual.Identity);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (!FindClose(search))
            {
                var cleanup = new IOException("Selected contributor metadata search did not retire.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                if (primary is not null) throw new AggregateException("Contributor metadata producer/retirement failed.", primary, cleanup);
                throw cleanup;
            }
        }
    }
    private static byte[] CString(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (end < 0 || bytes[..end].ContainsAnyInRange((byte)128, byte.MaxValue))
            throw new NotSupportedException("Original file metadata name lacks its selected ANSI conversion/terminator owner.");
        return bytes[..end].ToArray();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInformation
    {
        internal uint Attributes;
        internal uint CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IdentityHigh, IdentityLow;
        internal readonly ulong Created => ((ulong)CreatedHigh << 32) | CreatedLow;
        internal readonly ulong Written => ((ulong)WrittenHigh << 32) | WrittenLow;
        internal readonly ulong Size => ((ulong)SizeHigh << 32) | SizeLow;
        internal readonly ulong Identity => ((ulong)IdentityHigh << 32) | IdentityLow;
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr FindFirstFileA([MarshalAs(UnmanagedType.LPStr)] string path, [Out] byte[] data);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr search);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInformation information);
}
