using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Both constructors retain their own actual creation handles. No PID lookup,
// process-tree termination or unrestricted fallback admits an original DLL.
internal sealed partial class NativePluginDomainChild : IDisposable
{
    private readonly Process? _regular;
    private readonly NativePluginKernelHandle? _process, _job;
    private readonly NativePluginDesktopOwner? _desktop;
    internal bool ResourcesRetired { get; private set; }
    internal int Id { get; }
    internal StreamWriter StandardInput { get; }
    internal StreamReader StandardOutput { get; }
    internal StreamReader StandardError { get; }
    internal NativePluginTokenObjectReceipt? ObjectSecurity { get; }
    internal NativePluginDesktopReceipt? Desktop => _desktop?.Receipt;
    private NativePluginDomainChild(Process process)
    { _regular = process; Id = process.Id; StandardInput = process.StandardInput; StandardOutput = process.StandardOutput; StandardError = process.StandardError; }
    private NativePluginDomainChild(int id, NativePluginKernelHandle process, NativePluginKernelHandle job,
        StreamWriter input, StreamReader output, StreamReader error, NativePluginTokenObjectReceipt objectSecurity, NativePluginDesktopOwner desktop)
    { Id = id; _process = process; _job = job; _desktop = desktop; StandardInput = input; StandardOutput = output; StandardError = error; ObjectSecurity = objectSecurity; }
    internal static NativePluginDomainChild Start(ProcessStartInfo start, NativePluginPrivateIo? io, ulong generation)
    {
        if (io is null) return new(Process.Start(start) ?? throw new InvalidOperationException("Native companion did not start."));
        using var token = NativePluginIoSecurity.WriteRestrictedToken(io.RestrictingSid);
        var objectSecurity = NativePluginTokenObjects.AdmitDefaultObjects(token, io.RestrictingSid);
        NativePluginIoSecurity.RequireInputsReadOnly(token, io.Selection.OriginalRoots);
        var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null, errorRead = null, errorWrite = null;
        NativePluginKernelHandle? process = null, thread = null, job = null;
        NativePluginDesktopOwner? desktop = null;
        FileStream? stdinStream = null, stdoutStream = null, stderrStream = null;
        nint attributeList = 0, inherited = 0, environment = 0;
        var initialized = false;
        try
        {
            desktop = NativePluginDesktopOwner.Create(token, io.RestrictingSid, generation);
            if (!CreatePipe(out inputRead, out inputWrite, ref attributes, 0) || !CreatePipe(out outputRead, out outputWrite, ref attributes, 0) ||
                !CreatePipe(out errorRead, out errorWrite, ref attributes, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            foreach (var handle in new[] { inputWrite, outputRead, errorRead })
                if (!SetHandleInformation(handle, 1, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            nuint bytes = 0; _ = InitializeProcThreadAttributeList(0, 1, 0, ref bytes);
            if (bytes == 0 || bytes > 65536) throw new InvalidDataException("Native process attribute extent is invalid.");
            attributeList = Marshal.AllocHGlobal(checked((int)bytes));
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref bytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            var handles = new[] { inputRead.DangerousGetHandle(), outputWrite.DangerousGetHandle(), errorWrite.DangerousGetHandle() }
                .Concat(desktop.InheritedHandles).ToArray();
            var inheritedBytes = checked(handles.Length * nint.Size);
            inherited = Marshal.AllocHGlobal(inheritedBytes);
            for (var index = 0; index < handles.Length; ++index) Marshal.WriteIntPtr(inherited, index * nint.Size, handles[index]);
            if (!UpdateProcThreadAttribute(attributeList, 0, (nint)0x20002, inherited, (nuint)inheritedBytes, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var temp = Path.Combine(io.ModuleRoot, NativePluginIoRole.Diagnostic.ToString(), "temp"); Directory.CreateDirectory(temp);
            start.Environment["TEMP"] = temp; start.Environment["TMP"] = temp;
            var env = string.Join('\0', start.Environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
            environment = Marshal.StringToHGlobalUni(env);
            AppendSourceAddressSpaceArguments(start, io.SourceAddressSpace);
            var command = new StringBuilder(Quote(start.FileName));
            foreach (var argument in start.ArgumentList) command.Append(' ').Append(Quote(argument));
            var startup = new StartupInfoEx
            {
                Attributes = attributeList,
                Startup = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoEx>(),
                    Desktop = desktop.StartupDesktop,
                    Flags = 0x100,
                    Input = inputRead.DangerousGetHandle(),
                    Output = outputWrite.DangerousGetHandle(),
                    Error = errorWrite.DangerousGetHandle()
                }
            };
            if (!CreateProcessAsUserW(token, start.FileName, command, 0, 0, true, 0x08080404, environment, io.ModuleRoot, ref startup, out var created))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Write-restricted native process creation failed; unrestricted execution is refused.");
            process = new(created.Process); thread = new(created.Thread);
            desktop.BindCreatedProcess(process, created.ProcessId);
            job = CreateJobObjectW(0, null);
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimit { Basic = new BasicLimit { Flags = 0x2008, ActiveProcessLimit = 1 } };
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimit>()) || !AssignProcessToJobObject(job, process))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (io.SourceAddressSpace is { } sourceAddressSpace) ReserveSourceAddressSpace(process, sourceAddressSpace);
            if (ResumeThread(thread) != 1) throw new Win32Exception(Marshal.GetLastWin32Error(), "Native restricted primary thread did not resume from its owned suspension.");
            thread.Dispose(); thread = null;
            inputRead.Dispose(); inputRead = null; outputWrite.Dispose(); outputWrite = null; errorWrite.Dispose(); errorWrite = null;
            stdinStream = new(inputWrite, FileAccess.Write, 4096, false); inputWrite = null;
            stdoutStream = new(outputRead, FileAccess.Read, 4096, false); outputRead = null;
            stderrStream = new(errorRead, FileAccess.Read, 4096, false); errorRead = null;
            var child = new NativePluginDomainChild(checked((int)created.ProcessId), process, job, new(stdinStream, new UTF8Encoding(false)), new(stdoutStream, Encoding.UTF8), new(stderrStream, Encoding.UTF8), objectSecurity, desktop);
            stdinStream = null; stdoutStream = null; stderrStream = null; process = null; job = null; desktop = null; return child;
        }
        catch (Exception failure)
        {
            desktop ??= (failure as NativePluginDesktopFailure)?.RetainedOwner;
            var retained = new NativePluginChildConstructionOwner(generation, process, desktop,
                [thread, job, stdinStream, stdoutStream, stderrStream, inputRead, inputWrite, outputRead, outputWrite, errorRead, errorWrite]);
            io.RetainFailedChildConstruction(retained);
            // Transfer before retirement: a failed wait or CloseDesktop must
            // preserve every exact child capability and original input lease.
            process = null; thread = null; job = null; desktop = null;
            stdinStream = null; stdoutStream = null; stderrStream = null;
            inputRead = null; inputWrite = null; outputRead = null; outputWrite = null; errorRead = null; errorWrite = null;
            try { io.RetireFailedChildConstruction(retained); }
            catch (Exception cleanup) { throw new NativePluginChildConstructionFailure(retained, new AggregateException(failure, cleanup)); }
            throw;
        }
        finally
        {
            stdinStream?.Dispose(); stdoutStream?.Dispose(); stderrStream?.Dispose(); process?.Dispose(); thread?.Dispose(); job?.Dispose();
            inputRead?.Dispose(); inputWrite?.Dispose(); outputRead?.Dispose(); outputWrite?.Dispose(); errorRead?.Dispose(); errorWrite?.Dispose();
            if (environment != 0) Marshal.FreeHGlobal(environment); if (inherited != 0) Marshal.FreeHGlobal(inherited);
            if (initialized) DeleteProcThreadAttributeList(attributeList); if (attributeList != 0) Marshal.FreeHGlobal(attributeList);
        }
    }
    private static string Quote(string text)
    {
        if (text.IndexOf('\0') >= 0) throw new InvalidDataException("Native command line has a null.");
        var output = new StringBuilder("\""); var slashes = 0;
        foreach (var character in text)
        {
            if (character == '\\') { ++slashes; continue; }
            output.Append('\\', character == '"' ? slashes * 2 + 1 : slashes).Append(character); slashes = 0;
        }
        return output.Append('\\', slashes * 2).Append('"').ToString();
    }
    internal bool HasExited => _regular?.HasExited ?? WaitForSingleObject(_process!, 0) == 0;
    internal bool WaitForExit(int milliseconds) => _regular?.WaitForExit(milliseconds) ?? WaitForSingleObject(_process!, checked((uint)milliseconds)) == 0;
    internal int ExitCode
    {
        get { if (_regular is not null) return _regular.ExitCode; if (!HasExited || !GetExitCodeProcess(_process!, out var code)) throw new InvalidOperationException("Owned native child has no exit receipt."); return unchecked((int)code); }
    }
    internal void Kill(bool entireProcessTree)
    {
        if (entireProcessTree) throw new InvalidOperationException("Native owner cannot terminate a process tree.");
        if (_regular is not null) { _regular.Kill(false); return; }
        if (!HasExited && !TerminateProcess(_process!, 3))
        {
            var error = Marshal.GetLastWin32Error();
            if (WaitForSingleObject(_process!, 0) != 0) throw new Win32Exception(error);
        }
    }
    public void Dispose()
    {
        if (ResourcesRetired) return;
        if (!HasExited) throw new InvalidOperationException("Native process resources remain owned until exact child closure.");
        if (_regular is not null) { _regular.Dispose(); ResourcesRetired = true; return; }
        _desktop!.Retire();
        StandardInput.Dispose(); StandardOutput.Dispose(); StandardError.Dispose(); _job!.Dispose(); _process!.Dispose();
        ResourcesRetired = true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Length; internal nint Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { internal nint Process, Thread; internal uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        internal int Size; internal string? Reserved, Desktop, Title; internal uint X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        internal ushort Show, ReservedLength; internal nint ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { internal StartupInfo Startup; internal nint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        internal long ProcessTime, JobTime; internal uint Flags; internal nuint MinimumWorkingSet, MaximumWorkingSet; internal uint ActiveProcessLimit; internal nuint Affinity; internal uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit { internal BasicLimit Basic; internal IoCounters Io; internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint size);
    [DllImport("kernel32", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint length);
    [DllImport("kernel32", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nuint length, nint previous, nint returned);
    [DllImport("kernel32")] private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessAsUserW(NativePluginKernelHandle token, string app, StringBuilder command, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern NativePluginKernelHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32", SetLastError = true)] private static extern bool SetInformationJobObject(NativePluginKernelHandle job, int kind, ref ExtendedLimit information, uint length);
    [DllImport("kernel32", SetLastError = true)] private static extern bool AssignProcessToJobObject(NativePluginKernelHandle job, NativePluginKernelHandle process);
    [DllImport("kernel32", SetLastError = true)] private static extern uint ResumeThread(NativePluginKernelHandle thread);
    [DllImport("kernel32", SetLastError = true)] private static extern bool TerminateProcess(NativePluginKernelHandle process, uint exit);
    [DllImport("kernel32", SetLastError = true)] private static extern uint WaitForSingleObject(NativePluginKernelHandle process, uint milliseconds);
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetExitCodeProcess(NativePluginKernelHandle process, out uint exit);
}
