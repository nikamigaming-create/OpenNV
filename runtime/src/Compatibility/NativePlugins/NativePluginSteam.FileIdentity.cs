using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Public Windows handle metadata, not a path guess or a pointer imported from
// the x86 child. Both retained handles must describe this same physical file.
internal sealed record NativePluginSteamFileIdentity(uint Type, uint Attributes, ulong Created, ulong Written,
    uint Volume, ulong Index, ulong Bytes, ulong ExtendedVolume, ulong IdLow, ulong IdHigh)
{
    internal const ulong MaximumImageBytes = 512UL * 1024 * 1024;
    internal void Validate()
    {
        if (Type != 1 || (Attributes & 0x10) != 0 || Bytes is < 1 or > MaximumImageBytes)
            throw new InvalidDataException("Platform source has no bounded ordinary disk-file identity.");
    }
    internal static NativePluginSteamFileIdentity Read(FileStream actualInput)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Platform original-file identity requires the actual Windows handle.");
        var handle = actualInput.SafeFileHandle;
        ObjectDisposedException.ThrowIf(handle.IsClosed || handle.IsInvalid, actualInput);
        var type = GetFileType(handle);
        if (type != 1) throw new IOException("Actual original input is not a disk file.", new Win32Exception(Marshal.GetLastWin32Error()));
        if (!GetFileInformationByHandle(handle, out var ordinary))
            throw new IOException("Original retained BY_HANDLE_FILE_INFORMATION query failed.", new Win32Exception(Marshal.GetLastWin32Error()));
        if (!GetFileInformationByHandleEx(handle, 18, out var extended, Marshal.SizeOf<ExtendedInformation>()))
            throw new IOException("Original retained 128-bit FileIdInfo query failed.", new Win32Exception(Marshal.GetLastWin32Error()));
        var identity = new NativePluginSteamFileIdentity(type, ordinary.Attributes, ordinary.Created, ordinary.Written,
            ordinary.Volume, ordinary.Index, ordinary.Bytes, extended.Volume, extended.Low, extended.High);
        identity.Validate();
        if ((ulong)actualInput.Length != identity.Bytes) throw new InvalidDataException("Retained original handle and stream extent disagree.");
        return identity;
    }
    internal static NativePluginSteamFileIdentity ReadTransport(BinaryReader reader)
    {
        var result = new NativePluginSteamFileIdentity(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt64(), reader.ReadUInt64(),
            reader.ReadUInt32(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64());
        result.Validate(); return result;
    }
    internal void WriteTransport(BinaryWriter writer)
    {
        Validate(); writer.Write(Type); writer.Write(Attributes); writer.Write(Created); writer.Write(Written);
        writer.Write(Volume); writer.Write(Index); writer.Write(Bytes); writer.Write(ExtendedVolume); writer.Write(IdLow); writer.Write(IdHigh);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct OrdinaryInformation
    {
        internal uint Attributes, CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        internal readonly ulong Created => ((ulong)CreatedHigh << 32) | CreatedLow;
        internal readonly ulong Written => ((ulong)WrittenHigh << 32) | WrittenLow;
        internal readonly ulong Index => ((ulong)IndexHigh << 32) | IndexLow;
        internal readonly ulong Bytes => ((ulong)SizeHigh << 32) | SizeLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedInformation { internal ulong Volume, Low, High; }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle file);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out OrdinaryInformation information);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out ExtendedInformation information, int bytes);
}
