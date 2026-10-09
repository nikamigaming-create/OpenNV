using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Failed construction owns the same creation handle, desktop and capabilities
// as successful construction. A termination request is not an exit receipt.
internal sealed class NativePluginChildConstructionOwner(ulong generation, NativePluginKernelHandle? process,
    NativePluginDesktopOwner? desktop, IDisposable?[] resources)
{
    private NativePluginKernelHandle? _process = process;
    private NativePluginDesktopOwner? _desktop = desktop;
    internal ulong Generation { get; } = generation;
    internal bool Retired { get; private set; }
    internal bool ChildExited => _process is null || !_process.IsClosed && !_process.IsInvalid && WaitForSingleObject(_process, 0) == 0;
    internal void Retire()
    {
        if (Retired) return;
        if (_process is { } child)
        {
            if (child.IsClosed || child.IsInvalid) throw new InvalidOperationException("Failed child construction lost its exact creation handle.");
            var wait = WaitForSingleObject(child, 0);
            if (wait == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (wait != 0)
            {
                if (!TerminateProcess(child, 3))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (WaitForSingleObject(child, 0) != 0) throw new Win32Exception(error, "Failed native construction could not terminate its exact child.");
                }
                wait = WaitForSingleObject(child, 1000);
                if (wait == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (wait != 0) throw new TimeoutException("Failed native construction retains a child without an observed exit.");
            }
        }
        // This owner borrows the actual process handle until desktop closure.
        _desktop?.Retire(); _desktop = null;
        var errors = new List<Exception>();
        for (var index = 0; index < resources.Length; ++index)
        {
            try { resources[index]?.Dispose(); resources[index] = null; }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Failed native construction retains resource retirement failures.", errors);
        _process?.Dispose(); _process = null; Retired = true;
    }
    [DllImport("kernel32", SetLastError = true)] private static extern bool TerminateProcess(NativePluginKernelHandle process, uint exit);
    [DllImport("kernel32", SetLastError = true)] private static extern uint WaitForSingleObject(NativePluginKernelHandle process, uint milliseconds);
}

internal sealed class NativePluginChildConstructionFailure(NativePluginChildConstructionOwner owner, Exception inner)
    : Exception("Restricted native construction retains its exact failed child/resource owners.", inner)
{
    internal NativePluginChildConstructionOwner RetainedOwner { get; } = owner;
}

internal sealed partial class NativePluginPrivateIo
{
    private readonly List<NativePluginChildConstructionOwner> _failedChildConstructions = [];
    internal bool FailedChildrenExited => _failedChildConstructions.All(owner => owner.ChildExited);
    internal bool HasFailedChildConstruction => _failedChildConstructions.Count != 0;
    internal void RetainFailedChildConstruction(NativePluginChildConstructionOwner owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (owner.Generation == 0 || _failedChildConstructions.Contains(owner))
            throw new InvalidOperationException("Failed native construction has no distinct actual generation.");
        _failedChildConstructions.Add(owner);
    }
    internal void RetireFailedChildConstruction(NativePluginChildConstructionOwner owner)
    {
        if (!_failedChildConstructions.Contains(owner)) throw new InvalidOperationException("Failed construction belongs to another source-I/O owner.");
        owner.Retire(); _failedChildConstructions.Remove(owner);
    }
    private void RetireFailedChildConstructions()
    {
        var errors = new List<Exception>();
        foreach (var owner in _failedChildConstructions.ToArray())
        {
            try { RetireFailedChildConstruction(owner); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Native input leases remain owned by failed child construction.", errors);
    }
}
