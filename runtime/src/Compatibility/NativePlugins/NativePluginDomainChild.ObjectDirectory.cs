using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    internal uint RequireMutexNamespaceToken(string sid)
    {
        RequireMutexProcess();
        if (!OpenNamespaceToken(CngCreationHandle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            if (!NamespaceTokenRestricted(token) || !NamespaceConvertSid(sid, out var expected))
                throw new InvalidDataException("Mutex namespace lost the exact restricted source token.");
            try
            {
                var first = Marshal.OffsetOf<NamespaceTokenGroups>(nameof(NamespaceTokenGroups.First)).ToInt32();
                var restrictions = NamespaceInformation(token, 11, first + Marshal.SizeOf<NamespaceSid>());
                try
                {
                    if (Marshal.ReadInt32(restrictions) != 1 || !NamespaceEqualSid(Marshal.ReadIntPtr(restrictions, first), expected))
                        throw new InvalidDataException("Mutex namespace token no longer has its one exact module restriction.");
                }
                finally { Marshal.FreeHGlobal(restrictions); }
                var session = NamespaceNumber(token, 12);
                if (!NamespaceProcessSession(checked((uint)Id), out var childSession) ||
                    !NamespaceProcessSession(NamespaceCurrentProcessId(), out var parentSession)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (session != childSession || session != parentSession) throw new NotSupportedException("Named mutex namespace requires the exact shared current Windows session.");
                RequireOrdinaryNamespaceToken(token);
                if (!OpenCurrentNamespaceToken(CurrentProcess(), 8, out var parent)) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (parent)
                {
                    if (NamespaceNumber(parent, 12) != session) throw new InvalidDataException("Actual parent named-object root belongs to another session.");
                    RequireOrdinaryNamespaceToken(parent);
                }
                return session;
            }
            finally { NamespaceLocalFree(expected); }
        }
    }
    private static void RequireOrdinaryNamespaceToken(NativePluginKernelHandle token)
    {
        if (NamespaceNumber(token, 29) != 0 || NamespaceNumber(token, 42) != 0)
            throw new NotSupportedException("AppContainer/private token namespace construction has no source owner.");
        var isolation = NamespaceInformation(token, 44, Marshal.SizeOf<NamespaceIsolation>());
        try
        {
            if (Marshal.PtrToStructure<NamespaceIsolation>(isolation).Enabled != 0)
                throw new NotSupportedException("Token BNO isolation cannot be replaced with another process's directory.");
        }
        finally { Marshal.FreeHGlobal(isolation); }
    }
    internal (uint Status, uint Error) ProbeMutexDirectory(string name)
    {
        RequireMutexProcess();
        if (!OpenNamespaceToken(CngCreationHandle, 0xa, out var primary)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (primary)
        {
            if (!NamespaceDuplicateToken(primary, 2, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using (token)
            {
                uint status = 0, error = 0; Exception? failure = null;
                var worker = new Thread(() =>
                {
                    var impersonated = false;
                    try
                    {
                        if (!NamespaceImpersonate(token)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        impersonated = true;
                        var result = NativePluginObjectDirectory.OpenRaw(0, name, NativePluginObjectDirectory.CallerAccess);
                        status = unchecked((uint)result.Status); error = result.Status < 0 ? NativePluginObjectDirectory.DosError(status) : 0;
                        if (result.Handle != 0)
                        {
                            CloseMutexVerificationRef(new NativePluginKernelHandle(result.Handle));
                        }
                    }
                    catch (Exception found) { failure = found; }
                    finally
                    {
                        if (impersonated && !NamespaceRevert())
                        {
                            var cleanup = new Win32Exception(Marshal.GetLastWin32Error(), "Namespace probe worker impersonation did not retire.");
                            failure = failure is null ? cleanup : new AggregateException(failure, cleanup);
                        }
                    }
                })
                { IsBackground = true, Name = "OpenNV object-directory access observation" };
                worker.Start(); worker.Join();
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                return (status, error);
            }
        }
    }
    internal uint ReceiveMutexDirectory(NativePluginObjectDirectoryHandle parent)
    {
        RequireMutexProcess();
        if (!DuplicateToService(CurrentProcess(), parent, CngCreationHandle, out var remote, NativePluginObjectDirectory.CallerAccess, false, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual source process did not acquire its directory capability.");
        var value = unchecked((ulong)remote);
        if (value > uint.MaxValue && (value >> 32) != uint.MaxValue || unchecked((uint)value) is 0 or uint.MaxValue)
            throw new InvalidDataException("Duplicated object-directory reference has no actual x86 handle extent.");
        return unchecked((uint)value);
    }
    internal void RequireMutexDirectory(uint remote, NativePluginObjectDirectoryHandle parent)
    {
        RequireMutexProcess(); var borrowed = DuplicateMutexRef(remote);
        try
        {
            if (!CompareSections(parent, borrowed))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Original source's directory handle differs from the exact retained kernel object.");
        }
        finally { CloseMutexVerificationRef(borrowed); }
    }
    internal void RequireMutexWindowsImage(NativePluginMutexWindowsCaller caller, NativePluginMutexWindowsSource source)
    {
        RequireMutexProcess(); source.RequireCurrent();
        if (caller.Image == 0) throw new InvalidDataException("Mutex native constructor lacks its actual Windows provider.");
        foreach (var (name, pointer) in new[] { ("NtCreateMutant", caller.Create), ("NtOpenMutant", caller.Open), ("RtlNtStatusToDosError", caller.ConvertError) })
        {
            if ((ulong)caller.Image + NativePluginMutexWindowsSource.Export(source.Native, name).Rva != pointer)
                throw new InvalidDataException("Mutex native constructor address differs from the exact installed export declaration.");
            if (QueryOriginal(CngCreationHandle, (nint)(nuint)pointer, out var memory, (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (memory.Type != 0x1000000 || memory.State != 0x1000 || (nuint)memory.AllocationBase != caller.Image || memory.Protection is not (0x10 or 0x20 or 0x40 or 0x80))
                throw new InvalidDataException("Mutex native Windows callable is not the actual executable image extent.");
        }
        var path = new System.Text.StringBuilder(32768);
        var count = MappedEngineImage(CngCreationHandle, (nint)(nuint)caller.Image, path, (uint)path.Capacity);
        if (count == 0 || count >= path.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var mapped = new FileStream("\\\\?\\GLOBALROOT" + path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (NativePluginSteamFileIdentity.Read(mapped) != source.NativeIdentity)
            throw new InvalidDataException("Mutex constructor's mapped Windows provider has a different actual kernel file identity.");
    }
    private static uint NamespaceNumber(NativePluginKernelHandle token, int kind)
    { var data = NamespaceInformation(token, kind); try { return unchecked((uint)Marshal.ReadInt32(data)); } finally { Marshal.FreeHGlobal(data); } }
    private static nint NamespaceInformation(NativePluginKernelHandle token, int kind, int minimum = 4)
    {
        _ = NamespaceGetInformation(token, kind, 0, 0, out var needed);
        if (needed < minimum || needed > 1048576) throw new InvalidDataException("Namespace token observation has no complete bounded extent.");
        var memory = Marshal.AllocHGlobal(needed);
        try
        {
            if (!NamespaceGetInformation(token, kind, memory, needed, out var actual)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (actual < minimum || actual > needed) throw new InvalidDataException("Actual token field has no complete native returned extent.");
            return memory;
        }
        catch { Marshal.FreeHGlobal(memory); throw; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NamespaceSid { internal nint Sid; internal uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct NamespaceTokenGroups { internal uint Count; internal NamespaceSid First; }
    [StructLayout(LayoutKind.Sequential)] private struct NamespaceIsolation { internal nint Prefix; internal byte Enabled; }
    [DllImport("advapi32", EntryPoint = "OpenProcessToken", SetLastError = true)] private static extern bool OpenNamespaceToken(SafeHandle process, uint access, out NativePluginKernelHandle token);
    [DllImport("advapi32", EntryPoint = "OpenProcessToken", SetLastError = true)] private static extern bool OpenCurrentNamespaceToken(nint process, uint access, out NativePluginKernelHandle token);
    [DllImport("advapi32", EntryPoint = "GetTokenInformation", SetLastError = true)] private static extern bool NamespaceGetInformation(NativePluginKernelHandle token, int kind, nint info, int bytes, out int needed);
    [DllImport("advapi32", EntryPoint = "IsTokenRestricted")] private static extern bool NamespaceTokenRestricted(NativePluginKernelHandle token);
    [DllImport("advapi32", EntryPoint = "EqualSid")] private static extern bool NamespaceEqualSid(nint first, nint second);
    [DllImport("advapi32", EntryPoint = "ConvertStringSidToSidW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool NamespaceConvertSid(string value, out nint sid);
    [DllImport("advapi32", EntryPoint = "DuplicateToken", SetLastError = true)] private static extern bool NamespaceDuplicateToken(NativePluginKernelHandle token, int level, out NativePluginKernelHandle duplicate);
    [DllImport("advapi32", EntryPoint = "ImpersonateLoggedOnUser", SetLastError = true)] private static extern bool NamespaceImpersonate(NativePluginKernelHandle token);
    [DllImport("advapi32", EntryPoint = "RevertToSelf", SetLastError = true)] private static extern bool NamespaceRevert();
    [DllImport("kernel32", EntryPoint = "ProcessIdToSessionId", SetLastError = true)] private static extern bool NamespaceProcessSession(uint pid, out uint session);
    [DllImport("kernel32", EntryPoint = "GetCurrentProcessId")] private static extern uint NamespaceCurrentProcessId();
    [DllImport("kernel32", EntryPoint = "LocalFree")] private static extern nint NamespaceLocalFree(nint memory);
}
