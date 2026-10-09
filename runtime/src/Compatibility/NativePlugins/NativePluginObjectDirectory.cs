using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed class NativePluginObjectDirectoryHandle(nint value) : SafeHandleZeroOrMinusOneIsInvalid(true)
{
    internal void Initialize() => SetHandle(value);
    internal void CloseChecked()
    {
        if (IsClosed || IsInvalid) return;
        if (!NativePluginIoSecurity.CloseHandle(handle)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual object-directory reference did not close.");
        SetHandleAsInvalid(); Dispose();
    }
    protected override bool ReleaseHandle() => NativePluginIoSecurity.CloseHandle(handle);
}

// A failed metadata observation must not lose an allocated directory reference
// when its independent CloseHandle also fails.
internal sealed class NativePluginObjectDirectoryFailure(NativePluginObjectDirectoryHandle retained,
    Exception primary, Exception cleanup) : AggregateException("Object-directory construction and its independent retirement failed.", primary, cleanup)
{
    internal NativePluginObjectDirectoryHandle Retained { get; } = retained;
}

// Directory names come from the actual installed Windows getter and NT object
// lookup, never a guessed Session0 path or an alias replacing a source name.
internal static class NativePluginObjectDirectory
{
    internal const uint CallerAccess = 6; // DIRECTORY_TRAVERSE | DIRECTORY_CREATE_OBJECT.
    internal static (NativePluginObjectDirectoryHandle Handle, string Name, string Descriptor) Open(string scope)
    {
        var status = BaseGetNamedObjectDirectory(out var borrowed);
        if (status < 0 || borrowed is 0 or -1) throw Error(status, "Windows did not supply its actual current named-object directory.");
        var rootName = Name(borrowed);
        var relative = scope switch
        {
            "default" => rootName,
            "local" => "Local",
            "global" => "Global",
            _ => throw new InvalidDataException("Unknown original named-object namespace.")
        };
        var actual = OpenRaw(scope == "default" ? 0 : borrowed, relative, 0x20007);
        if (actual.Status < 0 || actual.Handle is 0 or -1) throw Error(actual.Status, "Actual selected named-object directory capability was denied.");
        var owner = new NativePluginObjectDirectoryHandle(actual.Handle); owner.Initialize();
        try { return (owner, Name(actual.Handle), DescriptorHash(actual.Handle)); }
        catch (Exception original)
        {
            try { owner.CloseChecked(); } catch (Exception cleanup) { throw new NativePluginObjectDirectoryFailure(owner, original, cleanup); }
            throw;
        }
    }
    internal static (int Status, nint Handle) OpenRaw(nint root, string name, uint access)
    {
        if (name.Length == 0 || name.Length >= 32767 || name.Contains('\0')) throw new InvalidDataException("Object-directory name has no complete Unicode extent.");
        var text = Marshal.StringToHGlobalUni(name); var stringMemory = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            Marshal.StructureToPtr(new UnicodeString
            {
                Length = checked((ushort)(name.Length * 2)),
                Maximum = checked((ushort)((name.Length + 1) * 2)),
                Buffer = text
            }, stringMemory, false);
            var attributes = new ObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<ObjectAttributes>(),
                Root = root,
                Name = stringMemory,
                Attributes = 0x40
            };
            var status = NtOpenDirectoryObject(out var handle, access, ref attributes); return (status, handle);
        }
        finally { Marshal.FreeHGlobal(stringMemory); Marshal.FreeHGlobal(text); }
    }
    internal static uint DosError(uint status) => RtlNtStatusToDosError(unchecked((int)status));
    internal static Exception Error(int status, string message) => new IOException(message + $" NTSTATUS=0x{unchecked((uint)status):x8}",
        new Win32Exception(unchecked((int)DosError(unchecked((uint)status)))));
    internal static string Name(nint handle)
    {
        var first = NtQueryObject(handle, 1, 0, 0, out var size);
        if (size < Marshal.SizeOf<UnicodeString>() || size > 131072)
            throw Error(first, "NT object name has no bounded returned extent.");
        var memory = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            var status = NtQueryObject(handle, 1, memory, size, out var actual);
            if (status < 0) throw Error(status, "NT object-name read failed.");
            var name = Marshal.PtrToStructure<UnicodeString>(memory);
            var beginning = unchecked((ulong)(nuint)memory); var pointer = unchecked((ulong)(nuint)name.Buffer);
            if (actual > size || name.Length == 0 || (name.Length & 1) != 0 || name.Maximum < name.Length ||
                pointer < beginning || pointer + name.Length > beginning + size)
                throw new InvalidDataException("Actual NT object name does not belong to its complete returned extent.");
            return Marshal.PtrToStringUni(name.Buffer, name.Length / 2) ?? throw new InvalidDataException("NT object name is absent.");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static string DescriptorHash(nint directory)
    {
        var error = GetSecurityInfo(directory, 6, 7, out _, out _, out _, out _, out var descriptor);
        if (error != 0) throw new Win32Exception(unchecked((int)error), "Actual object-directory security is unreadable.");
        try
        {
            var size = GetSecurityDescriptorLength(descriptor);
            if (size < 20 || size > 1048576) throw new InvalidDataException("Object-directory descriptor is incomplete.");
            var bytes = new byte[size]; Marshal.Copy(descriptor, bytes, 0, bytes.Length); return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { LocalFree(descriptor); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { internal ushort Length, Maximum; internal nint Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes { internal uint Length; internal nint Root, Name; internal uint Attributes; internal nint Descriptor, Quality; }
    [DllImport("kernelbase", ExactSpelling = true)] private static extern int BaseGetNamedObjectDirectory(out nint directory);
    [DllImport("ntdll", ExactSpelling = true)] private static extern int NtOpenDirectoryObject(out nint directory, uint access, ref ObjectAttributes attributes);
    [DllImport("ntdll", ExactSpelling = true)] private static extern int NtQueryObject(nint handle, uint kind, nint information, uint length, out uint returned);
    [DllImport("ntdll", ExactSpelling = true)] private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("advapi32", ExactSpelling = true)]
    private static extern uint GetSecurityInfo(nint handle, int type, uint information,
        out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);
    [DllImport("advapi32", ExactSpelling = true)] private static extern uint GetSecurityDescriptorLength(nint descriptor);
    [DllImport("kernel32", ExactSpelling = true)] private static extern nint LocalFree(nint memory);
}
