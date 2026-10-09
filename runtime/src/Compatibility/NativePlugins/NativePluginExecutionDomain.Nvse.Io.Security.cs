using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed class NativePluginKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativePluginKernelHandle() : base(true) { }
    internal NativePluginKernelHandle(nint handle) : base(true) => SetHandle(handle);
    protected override bool ReleaseHandle() => NativePluginIoSecurity.CloseHandle(handle);
}
internal static class NativePluginIoSecurity
{
    internal static string PhysicalPath(string path)
    {
        var ancestor = path;
        while (!File.Exists(ancestor) && !Directory.Exists(ancestor))
            ancestor = Path.GetDirectoryName(ancestor) ?? throw new InvalidDataException("Native path has no existing local filesystem ancestor.");
        NativePluginPrivateIo.NoReparse(ancestor);
        using var file = CreateFileW(ancestor, 0x80, 7, 0, 3, 0x02000000, 0);
        if (file.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(32768); var count = GetFinalPathNameByHandleW(file, buffer, (uint)buffer.Capacity, 2);
        if (count == 0 || count >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error(), "Native source/private volume identity is incomplete.");
        var physical = buffer.ToString().TrimEnd('\\');
        var relative = Path.GetRelativePath(ancestor, path);
        return relative == "." ? physical : physical + "\\" + relative;
    }
    internal static void RequirePrivateTreeOwned(string root)
    {
        foreach (var path in new[] { root }.Concat(Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        })))
        {
            NativePluginPrivateIo.NoReparse(path);
            using var file = CreateFileW(path, 0x80, 7, 0, 3, 0x02000000, 0);
            if (file.IsInvalid || !GetFileInformationByHandle(file, out var information)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (information.Links != 1) throw new NotSupportedException("Private native state has an unowned shared hard-link identity.");
        }
    }
    internal static string ModuleSid(string stack, string module)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(stack.ToUpperInvariant() + ":" + module.ToUpperInvariant()));
        return "S-1-5-21-" + BinaryPrimitives.ReadUInt32LittleEndian(hash) + "-" +
            BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(4)) + "-" + BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(8)) + "-" +
            BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(12));
    }
    internal static void ProtectDirectory(string path, string sid)
    {
        NativePluginPrivateIo.NoReparse(path);
        using var token = CurrentToken();
        var size = 0;
        _ = GetTokenInformation(token, 1, 0, 0, out size);
        if (size <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = Marshal.AllocHGlobal(size);
        nint userSidText = 0, descriptor = 0;
        try
        {
            if (!GetTokenInformation(token, 1, buffer, size, out _) || !ConvertSidToStringSidW(Marshal.ReadIntPtr(buffer), out userSidText))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var userSid = Marshal.PtrToStringUni(userSidText) ?? throw new InvalidDataException("Current token has no user SID.");
            var sddl = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;" + userSid + ")(A;OICI;FA;;;" + sid + ")";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out descriptor, out _) ||
                !SetFileSecurityW(path, 0x80000004, descriptor)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { if (descriptor != 0) LocalFree(descriptor); if (userSidText != 0) LocalFree(userSidText); Marshal.FreeHGlobal(buffer); }
    }
    internal static NativePluginKernelHandle CurrentToken()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x000f01ff, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return token;
    }
    internal static NativePluginKernelHandle WriteRestrictedToken(string restrictingSid)
    {
        using var original = CurrentToken();
        if (!ConvertStringSidToSidW(restrictingSid, out var sid)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var restrict = new SidAndAttributes { Sid = sid };
            // WRITE_RESTRICTED limits native write access to the selected module
            // SID. Source read access keeps the current user's existing checks.
            if (!CreateRestrictedToken(original, 0x9, 0, 0, 0, 0, 1, ref restrict, out var token))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!IsTokenRestricted(token)) { token.Dispose(); throw new InvalidDataException("Windows did not retain the native restricting token."); }
            return token;
        }
        finally { LocalFree(sid); }
    }
    internal static void RequireInputsReadOnly(NativePluginKernelHandle token, IReadOnlyList<string> roots)
    {
        if (!DuplicateToken(token, 2, out var checkToken)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (checkToken)
        {
            var visitedRoots = new List<string>();
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path.Length))
            {
                if (visitedRoots.Any(parent => Directory.Exists(parent) && NativePluginPrivateIo.Within(parent, root))) continue;
                visitedRoots.Add(root);
                NativePluginPrivateIo.NoReparse(root); Check(root);
                if (Directory.Exists(root))
                    foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = false,
                        AttributesToSkip = 0,
                        ReturnSpecialDirectories = false
                    }))
                    { NativePluginPrivateIo.NoReparse(path); Check(path); }
            }
        }
        void Check(string path)
        {
            // Include the real owner/group: a DACL-only descriptor cannot prove
            // whether implicit owner WRITE_DAC access remains available.
            var result = GetNamedSecurityInfoW(path, 1, 7, out _, out _, out _, out _, out var descriptor);
            if (result != 0) throw new Win32Exception((int)result, "Original native input security ownership is unreadable.");
            nint privileges = 0;
            try
            {
                var mapping = new GenericMapping { Read = 0x120089, Write = 0x120116, Execute = 0x1200a0, All = 0x1f01ff };
                uint size = 4096; privileges = Marshal.AllocHGlobal((int)size);
                if (!AccessCheck(descriptor, checkToken, 0x02000000, ref mapping, privileges, ref size, out var granted, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Original native input access check has no kernel result.");
                if ((granted & 0x000d0156) != 0)
                    throw new NotSupportedException("Selected native input remains writable under its restricted token: " + path);
            }
            finally { if (privileges != 0) Marshal.FreeHGlobal(privileges); LocalFree(descriptor); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { internal nint Sid; internal uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct GenericMapping { internal uint Read, Write, Execute, All; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume,
            SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32", SetLastError = true)] internal static extern bool CloseHandle(nint handle);
    [DllImport("kernel32")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32")] private static extern nint LocalFree(nint memory);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern NativePluginKernelHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetFileInformationByHandle(NativePluginKernelHandle file, out FileInformation information);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(NativePluginKernelHandle file, StringBuilder path, uint length, uint flags);
    [DllImport("advapi32", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out NativePluginKernelHandle token);
    [DllImport("advapi32", SetLastError = true)] private static extern bool GetTokenInformation(NativePluginKernelHandle token, int kind, nint data, int length, out int required);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertSidToStringSidW(nint sid, out nint text);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSidToSidW(string sid, out nint value);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out nint descriptor, out uint length);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetFileSecurityW(string file, uint information, nint descriptor);
    [DllImport("advapi32", SetLastError = true)] private static extern bool CreateRestrictedToken(NativePluginKernelHandle existing, uint flags, uint disableCount, nint disabled, uint privilegeCount, nint privileges, uint restrictCount, ref SidAndAttributes restricted, out NativePluginKernelHandle token);
    [DllImport("advapi32")] private static extern bool IsTokenRestricted(NativePluginKernelHandle token);
    [DllImport("advapi32", SetLastError = true)] private static extern bool DuplicateToken(NativePluginKernelHandle token, int level, out NativePluginKernelHandle duplicate);
    [DllImport("advapi32", CharSet = CharSet.Unicode)] private static extern uint GetNamedSecurityInfoW(string file, uint kind, uint info, out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);
    [DllImport("advapi32", SetLastError = true)] private static extern bool AccessCheck(nint descriptor, NativePluginKernelHandle token, uint access, ref GenericMapping mapping, nint privileges, ref uint privilegeBytes, out uint granted, out bool allowed);
}
