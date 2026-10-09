using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginDesktopReceipt(ulong Generation, string RestrictingSid,
    string Station, string Desktop, uint StationAccess, uint DesktopAccess,
    string StationDaclSha256, string DesktopDaclSha256, uint? Child, uint? ObservedThread, bool Retired);

// These are real Windows USER objects. They supply neither a game window nor
// input/TESForm state. The parent never switches its process window station or
// changes an existing object's ACL. Only the fresh desktop gets a descriptor.
internal sealed class NativePluginDesktopOwner
{
    private const uint StationAccess = 0x00020023; // READ_CONTROL + ENUMDESKTOPS/READATTRIBUTES/GLOBALATOMS.
    private const uint DesktopAccess = 0x000200c7; // READ_CONTROL + READ/WRITEOBJECTS/CREATEWINDOW/CREATEMENU/ENUMERATE.
    private nint _station, _desktop;
    private NativePluginKernelHandle? _creationProcess;
    private NativePluginKernelHandle? _constructionToken;
    private Thread? _constructionWorker;
    private bool _retired;
    internal NativePluginDesktopReceipt Receipt { get; private set; }
    internal string StartupDesktop => Receipt.Station + "\\" + Receipt.Desktop;
    internal IReadOnlyList<nint> InheritedHandles
    {
        get
        {
            if (_retired || _constructionWorker?.IsAlive == true || _station == 0 || _desktop == 0)
                throw new InvalidOperationException("Native desktop inheritance has no living Windows object owner.");
            return [_station, _desktop];
        }
    }
    private NativePluginDesktopOwner(ulong generation, string sid, string station, string desktop)
    { Receipt = new(generation, sid, station, desktop, StationAccess, DesktopAccess, "", "", null, null, false); }

    internal static NativePluginDesktopOwner Create(NativePluginKernelHandle restrictedToken,
        string restrictingSid, ulong generation)
    {
        if (!OperatingSystem.IsWindows() || generation == 0)
            throw new InvalidOperationException("Native desktop creation has no actual Windows process generation.");
        RequireRestriction(restrictedToken, restrictingSid);
        var parentStation = GetProcessWindowStation();
        if (parentStation == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var stationName = Name(parentStation);
        var desktopName = "OpenNV." + generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + Guid.NewGuid().ToString("N");
        var owner = new NativePluginDesktopOwner(generation, restrictingSid, stationName, desktopName);
        Exception? failure = null;
        if (!DuplicateToken(restrictedToken, 2, out var workerToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Native desktop worker could not retain the exact restricted token.");
        owner._constructionToken = workerToken;
        // CreateDesktop may attach its calling thread. A dedicated new thread
        // prevents a UI/render/COM thread from ever owning that temporary state.
        Thread worker;
        try
        {
            worker = new Thread(() =>
            {
                try { owner.ConstructOnWorker(workerToken, parentStation); }
                catch (Exception error) { failure = error; }
                finally { workerToken.Dispose(); owner._constructionToken = null; }
            })
            { IsBackground = true, Name = "OpenNV native desktop construction" };
            owner._constructionWorker = worker;
            worker.Start();
        }
        catch (Exception error)
        {
            try { owner.Retire(); }
            catch (Exception cleanup) { throw new NativePluginDesktopFailure("Native desktop worker start/retirement failed.", owner, new AggregateException(error, cleanup)); }
            throw new NativePluginDesktopFailure("Native desktop worker did not start.", owner, error);
        }
        if (!worker.Join(TimeSpan.FromSeconds(10)))
            throw new NativePluginDesktopFailure("Native desktop construction did not finish its bounded worker; resource owners remain retained.", owner, new TimeoutException());
        if (GetProcessWindowStation() != parentStation || Name(parentStation) != stationName)
            failure = failure is null ? new InvalidDataException("Native desktop construction changed the parent station.") :
                new AggregateException(failure, new InvalidDataException("Native desktop construction changed the parent station."));
        if (failure is not null)
        {
            try { owner.Retire(); }
            catch (Exception cleanup) { throw new NativePluginDesktopFailure("Native desktop construction/retirement failed.", owner, new AggregateException(failure, cleanup)); }
            throw new NativePluginDesktopFailure("Native desktop construction failed before child creation.", owner, failure);
        }
        return owner;
    }

    private void ConstructOnWorker(NativePluginKernelHandle restrictedToken, nint parentStation)
    {
        var originalDesktop = GetThreadDesktop(GetCurrentThreadId());
        if (originalDesktop == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var originalName = Name(originalDesktop);
        nint descriptor = 0;
        Exception? failure = null;
        try
        {
            if (GetProcessWindowStation() != parentStation)
                throw new InvalidDataException("Native desktop worker has a different parent station.");
            _station = OpenWindowStationW(Receipt.Station, true, StationAccess);
            if (_station == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Selected inherited station access failed.");
            RequireInherited(_station);
            if (Name(_station) != Receipt.Station) throw new InvalidDataException("Inherited station identity drifted.");
            var stationHash = DaclHash(_station);
            var collision = OpenDesktopW(Receipt.Desktop, 0, false, DesktopAccess);
            if (collision != 0)
            {
                if (!CloseDesktop(collision)) throw new Win32Exception(Marshal.GetLastWin32Error());
                throw new InvalidDataException("Fresh native desktop name already exists.");
            }
            var absent = Marshal.GetLastWin32Error();
            if (absent != 2) throw new Win32Exception(absent, "Fresh native desktop absence could not be established.");
            var user = TokenUser(restrictedToken);
            var sddl = "D:P(A;;GA;;;SY)(A;;GA;;;" + user + ")(A;;GA;;;" + Receipt.RestrictingSid + ")";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out descriptor, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor, Inherit = true };
            _desktop = CreateDesktopW(Receipt.Desktop, null, 0, 0, DesktopAccess, ref attributes);
            if (_desktop == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Fresh hidden native desktop creation failed.");
            RequireInherited(_desktop);
            if (Name(_desktop) != Receipt.Desktop || DaclHash(_station) != stationHash)
                throw new InvalidDataException("Native desktop/station identity or unchanged station security drifted.");
            RequireDesktopAccess(_desktop, restrictedToken);
            Receipt = Receipt with { StationDaclSha256 = stationHash, DesktopDaclSha256 = DaclHash(_desktop) };
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (descriptor != 0) LocalFree(descriptor);
            try
            {
                var currentDesktop = GetThreadDesktop(GetCurrentThreadId());
                if (currentDesktop == 0 || (currentDesktop != originalDesktop && !SetThreadDesktop(originalDesktop)) ||
                    GetThreadDesktop(GetCurrentThreadId()) != originalDesktop || Name(originalDesktop) != originalName)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Native desktop worker did not restore its borrowed original thread desktop.");
            }
            catch (Exception restore) { failure = failure is null ? restore : new AggregateException(failure, restore); }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    // Bind before ResumeThread, using the exact CreateProcessAsUser handle.
    // A failed verification retains that actual handle reference, never a PID
    // lookup. Root must preserve this owner if exact child termination fails.
    internal void BindCreatedProcess(NativePluginKernelHandle process, uint processId)
    {
        if (_retired || _constructionWorker?.IsAlive == true || _creationProcess is not null || process is null || process.IsInvalid || process.IsClosed)
            throw new InvalidOperationException("Native desktop lacks one exact new child lifetime.");
        _creationProcess = process;
        if (processId == 0 || GetProcessId(process) != processId)
            throw new InvalidDataException("Native desktop child differs from its actual creation handle.");
        Receipt = Receipt with { Child = processId };
    }
    internal void RequireNativeObservation(ulong generation, uint processId, uint threadId, string station, string desktop)
    {
        if (_retired || _creationProcess is null || _creationProcess.IsClosed || _creationProcess.IsInvalid ||
            generation != Receipt.Generation || Receipt.Child != processId || GetProcessId(_creationProcess) != processId ||
            threadId == 0 || station != Receipt.Station || desktop != Receipt.Desktop)
            throw new InvalidDataException("Actual native USER object observation has a different child/generation/station/desktop.");
        var wait = WaitForSingleObject(_creationProcess, 0);
        if (wait != 258)
            throw new NativePluginDesktopFailure("Native desktop observation has no living exact child.", this,
                wait == uint.MaxValue ? new Win32Exception(Marshal.GetLastWin32Error()) : new InvalidOperationException());
        if (Receipt.ObservedThread is not null && Receipt.ObservedThread != threadId)
            throw new InvalidDataException("Native desktop observation changed its admitted child thread.");
        Receipt = Receipt with { ObservedThread = threadId };
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_constructionWorker?.IsAlive == true)
            throw new NativePluginDesktopFailure("Native desktop construction thread still owns its Windows resources.", this, new InvalidOperationException());
        if (_creationProcess is not null)
        {
            if (_creationProcess.IsClosed || _creationProcess.IsInvalid)
                throw new NativePluginDesktopFailure("Native desktop retirement lost its exact created child handle.", this, new InvalidOperationException());
            var wait = WaitForSingleObject(_creationProcess, 0);
            if (wait != 0)
                throw new NativePluginDesktopFailure("Native desktop handles remain leased until observed exact child closure.", this,
                    wait == uint.MaxValue ? new Win32Exception(Marshal.GetLastWin32Error()) : new InvalidOperationException());
        }
        if (_desktop != 0)
        {
            if (!CloseDesktop(_desktop)) throw new NativePluginDesktopFailure("Owned native desktop did not retire.", this, new Win32Exception(Marshal.GetLastWin32Error()));
            _desktop = 0;
        }
        if (_station != 0)
        {
            if (!CloseWindowStation(_station)) throw new NativePluginDesktopFailure("Owned inherited station handle did not retire.", this, new Win32Exception(Marshal.GetLastWin32Error()));
            _station = 0;
        }
        _constructionToken?.Dispose(); _constructionToken = null;
        _retired = true; Receipt = Receipt with { Retired = true };
    }

    private static string Name(nint handle)
    {
        _ = GetUserObjectInformationW(handle, 2, 0, 0, out var length);
        if (length < 2 || length > 65536 || length % 2 != 0) throw new InvalidDataException("Windows USER object name has no complete UTF16 extent.");
        var buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            if (!GetUserObjectInformationW(handle, 2, buffer, length, out var actual))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (actual < 2 || actual > length || actual % 2 != 0 || Marshal.ReadInt16(buffer, checked((int)actual - 2)) != 0)
                throw new InvalidDataException("Windows USER object name has an incomplete returned UTF16 extent.");
            var name = Marshal.PtrToStringUni(buffer, checked((int)actual / 2 - 1)) ?? throw new InvalidDataException("Windows USER object name is absent.");
            if (name.Length == 0 || name.Contains('\\')) throw new InvalidDataException("Windows USER object name is malformed.");
            return name;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static void RequireInherited(nint handle)
    {
        var flags = new UserObjectFlags();
        if (!GetUserObjectFlags(handle, 1, ref flags, (uint)Marshal.SizeOf<UserObjectFlags>(), out var actual) ||
            actual != Marshal.SizeOf<UserObjectFlags>() || flags.Inherit == 0)
            throw new InvalidDataException("Windows USER object handle did not retain actual inheritance.");
    }
    private static byte[] Descriptor(nint handle, uint information = 4)
    {
        _ = GetUserObjectSecurity(handle, ref information, 0, 0, out var length);
        if (length < 20 || length > 1024 * 1024) throw new InvalidDataException("Windows USER object DACL has no bounded descriptor.");
        var buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            if (!GetUserObjectSecurity(handle, ref information, buffer, length, out var actual))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (actual < 20 || actual > length) throw new InvalidDataException("Windows USER object security has an incomplete returned extent.");
            var result = new byte[actual]; Marshal.Copy(buffer, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static string DaclHash(nint handle) => Convert.ToHexString(SHA256.HashData(Descriptor(handle)));
    private static void RequireDesktopAccess(nint desktop, NativePluginKernelHandle impersonationToken)
    {
        // ConstructOnWorker already owns the exact DuplicateToken result.
        // Its impersonation handle is intentionally not TOKEN_DUPLICATE;
        // AccessCheck consumes that actual token without duplicating it.
        var bytes = Descriptor(desktop, 7); var descriptor = Marshal.AllocHGlobal(bytes.Length); var privileges = Marshal.AllocHGlobal(4096);
        try
        {
            Marshal.Copy(bytes, 0, descriptor, bytes.Length); uint needed = 4096;
            var mapping = new GenericMapping { Read = 0x00020041, Write = 0x000200be, Execute = 0x00020100, All = 0x000f01ff };
            if (!AccessCheck(descriptor, impersonationToken, DesktopAccess, ref mapping, privileges, ref needed, out var granted, out var allowed) ||
                !allowed || (granted & DesktopAccess) != DesktopAccess)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual restricted child has no selected new desktop access.");
        }
        finally { Marshal.FreeHGlobal(privileges); Marshal.FreeHGlobal(descriptor); }
    }
    private static string TokenUser(NativePluginKernelHandle token)
    {
        var information = TokenInformation(token, 1); nint text = 0;
        try
        {
            if (!ConvertSidToStringSidW(Marshal.ReadIntPtr(information), out text)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return Marshal.PtrToStringUni(text) ?? throw new InvalidDataException("Selected token user identity is absent.");
        }
        finally { if (text != 0) LocalFree(text); Marshal.FreeHGlobal(information); }
    }
    private static void RequireRestriction(NativePluginKernelHandle token, string sid)
    {
        if (!IsTokenRestricted(token) || !ConvertStringSidToSidW(sid, out var expected))
            throw new InvalidDataException("Native desktop has no actual selected restricted token/SID.");
        try
        {
            var information = TokenInformation(token, 11);
            try
            {
                var offset = Marshal.OffsetOf<TokenGroups>(nameof(TokenGroups.First)).ToInt32();
                if (Marshal.ReadInt32(information) != 1 || !EqualSid(expected, Marshal.ReadIntPtr(information, offset)))
                    throw new InvalidDataException("Native desktop restricting SID differs from its actual module token.");
            }
            finally { Marshal.FreeHGlobal(information); }
        }
        finally { LocalFree(expected); }
    }
    private static nint TokenInformation(NativePluginKernelHandle token, int kind)
    {
        _ = GetTokenInformation(token, kind, 0, 0, out var length);
        if (length <= 0 || length > 1024 * 1024) throw new InvalidDataException("Selected native token has no bounded information.");
        var result = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, kind, result, length, out var actual) || actual > length) throw new Win32Exception(Marshal.GetLastWin32Error());
            return result;
        }
        catch { Marshal.FreeHGlobal(result); throw; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Length; internal nint Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct UserObjectFlags { internal int Inherit, Reserved; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { internal nint Sid; internal uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenGroups { internal uint Count; internal SidAndAttributes First; }
    [StructLayout(LayoutKind.Sequential)] private struct GenericMapping { internal uint Read, Write, Execute, All; }
    [DllImport("user32", SetLastError = true)] private static extern nint GetProcessWindowStation();
    [DllImport("user32", SetLastError = true)] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("user32", SetLastError = true)] private static extern bool SetThreadDesktop(nint desktop);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenWindowStationW(string name, bool inherit, uint access);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateDesktopW(string name, string? device, nint mode, uint flags, uint access, ref SecurityAttributes attributes);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenDesktopW(string name, uint flags, bool inherit, uint access);
    [DllImport("user32", SetLastError = true)] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32", SetLastError = true)] private static extern bool CloseWindowStation(nint station);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetUserObjectInformationW(nint handle, int kind, nint information, uint length, out uint actual);
    [DllImport("user32", EntryPoint = "GetUserObjectInformationW", SetLastError = true)] private static extern bool GetUserObjectFlags(nint handle, int kind, ref UserObjectFlags information, uint length, out uint actual);
    [DllImport("user32", SetLastError = true)] private static extern bool GetUserObjectSecurity(nint handle, ref uint information, nint descriptor, uint length, out uint actual);
    [DllImport("advapi32", SetLastError = true)] private static extern bool DuplicateToken(NativePluginKernelHandle token, int level, out NativePluginKernelHandle duplicate);
    [DllImport("advapi32", SetLastError = true)] private static extern bool AccessCheck(nint descriptor, NativePluginKernelHandle token, uint desired, ref GenericMapping mapping, nint privileges, ref uint size, out uint granted, out bool allowed);
    [DllImport("advapi32", SetLastError = true)] private static extern bool GetTokenInformation(NativePluginKernelHandle token, int kind, nint information, int length, out int actual);
    [DllImport("advapi32")] private static extern bool IsTokenRestricted(NativePluginKernelHandle token);
    [DllImport("advapi32")] private static extern bool EqualSid(nint first, nint second);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSidToSidW(string sid, out nint result);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertSidToStringSidW(nint sid, out nint text);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out nint descriptor, out uint size);
    [DllImport("kernel32")] private static extern nint LocalFree(nint memory);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32", SetLastError = true)] private static extern uint GetProcessId(NativePluginKernelHandle process);
    [DllImport("kernel32", SetLastError = true)] private static extern uint WaitForSingleObject(NativePluginKernelHandle process, uint milliseconds);
}

internal sealed class NativePluginDesktopFailure(string message, NativePluginDesktopOwner owner, Exception inner)
    : Exception(message, inner)
{
    internal NativePluginDesktopOwner RetainedOwner { get; } = owner;
}
