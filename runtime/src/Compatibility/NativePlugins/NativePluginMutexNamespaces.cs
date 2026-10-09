namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Only the directory capability crosses from the parent. Mutex allocation and
// initial ownership are genuine NT calls on the original native caller thread.
internal sealed class NativePluginMutexNamespaces(NativePluginDomainChild process,
    ulong generation, ulong module, uint thread, string restrictingSid)
{
    private NativePluginMutexWindowsSource? _windows;
    private readonly Dictionary<string, NativePluginMutexDirectoryLease> _directories = new(StringComparer.Ordinal);
    private readonly List<NativePluginMutexDirectoryReceipt> _receipts = [];
    private readonly List<NativePluginMutexNativeStatusReceipt> _statuses = [];
    private readonly List<NativePluginObjectDirectoryHandle> _failedDirectories = [];
    private ulong _next;
    private uint? _session;
    private bool _retired;
    private Exception? _failure;
    internal IReadOnlyList<NativePluginMutexDirectoryReceipt> Receipts => _receipts.AsReadOnly();
    internal IReadOnlyList<NativePluginMutexNativeStatusReceipt> Statuses => _statuses.AsReadOnly();
    internal bool ResourcesRetired => _retired;
    internal NativePluginMutexDirectoryPublication Acquire(ulong call, NativePluginMutexName name,
        NativePluginMutexWindowsCaller caller)
    {
        if (_retired || call == 0 || process.HasExited) throw new InvalidOperationException("Mutex namespace cannot publish after its actual source lifetime.");
        if (_failure is not null) throw new InvalidOperationException("An entered namespace capability failure cannot replay.", _failure);
        try { return AcquireEntered(call, name, caller); }
        catch (NativePluginObjectDirectoryFailure error) { _failedDirectories.Add(error.Retained); _failure = error; throw; }
        catch (Exception error) { _failure = error; throw; }
    }
    private NativePluginMutexDirectoryPublication AcquireEntered(ulong call, NativePluginMutexName name,
        NativePluginMutexWindowsCaller caller)
    {
        _windows ??= new(); _windows.Prepare(); process.RequireMutexWindowsImage(caller, _windows);
        var session = process.RequireMutexNamespaceToken(restrictingSid);
        if (_session is { } original && session != original) throw new InvalidDataException("Mutex namespace session changed during its process generation.");
        _session = session;
        var (scope, tail) = Translate(name.WindowsName);
        if (!_directories.TryGetValue(scope, out var lease))
        {
            var opened = NativePluginObjectDirectory.Open(scope);
            // Strong ownership precedes any subsequent observation/transfer.
            lease = new(checked(++_next), scope, opened.Name, opened.Handle, opened.Descriptor);
            _directories.Add(scope, lease); Record(call, lease);
            var observed = process.ProbeMutexDirectory(opened.Name);
            lease.RestrictedStatus = observed.Status; lease.RestrictedError = observed.Error; Record(call, lease);
            lease.Remote = process.ReceiveMutexDirectory(lease.Parent); Record(call, lease);
            process.RequireMutexDirectory(lease.Remote, lease.Parent); lease.Published = true;
            Record(call, lease);
        }
        if (!lease.Published || lease.NativeClosed || lease.CloseResult is not null)
            throw new InvalidOperationException("An entered or failed directory lifetime cannot replay publication.");
        process.RequireMutexDirectory(lease.Remote, lease.Parent);
        return new(lease.Id, lease.Remote, lease.Directory, NativePluginMutexSource.EncodeWide(tail));
    }
    internal static (string Scope, string Tail) Translate(string name)
    {
        var scope = "default"; var tail = name;
        if (name.StartsWith("Local\\", StringComparison.Ordinal)) { scope = "local"; tail = name[6..]; }
        else if (name.StartsWith("Global\\", StringComparison.Ordinal)) { scope = "global"; tail = name[7..]; }
        if (tail.Length == 0 || tail.Contains('\\') || tail.Contains('\0'))
            throw new NotSupportedException("Original mutex private/empty namespace has no admitted source translation.");
        return (scope, tail);
    }
    internal void ObserveStatus(ulong call, ulong mutex, ulong directory, uint status, uint result,
        uint lastError, uint incomingError, bool open)
    {
        var lease = _directories.Values.SingleOrDefault(value => value.Id == directory);
        if (lease is null || !lease.Published || lease.NativeClosed || mutex == 0 || call == 0)
            throw new InvalidDataException("NT mutex return has no exact actual source directory/call.");
        _statuses.Add(new(call, mutex, directory, status, result, lastError));
        if (unchecked((int)status) < 0)
        {
            if (result != 0 || lastError != NativePluginObjectDirectory.DosError(status))
                throw new InvalidDataException("Native mutex failure lost its actual NTSTATUS/Windows translation.");
        }
        else if (result == 0 || lastError != (open ? incomingError : status == 0x40000000 ? 183U : 0U))
            throw new InvalidDataException("Native mutex success changed the installed Windows creation/open error semantics.");
    }
    internal void ObserveClose(ulong call, ulong directory, uint handle, uint result, uint error)
    {
        var lease = _directories.Values.SingleOrDefault(value => value.Id == directory);
        if (lease is null || !lease.Published || lease.Remote != handle || lease.CloseResult is not null || call == 0)
            throw new InvalidDataException("Namespace closure lacks its once-only exact source capability.");
        lease.CloseResult = result; lease.CloseError = error; lease.NativeClosed = result != 0; Record(call, lease);
        // Acknowledging a retained failure is not SDK success. Let the child
        // independently close later directory refs before reporting the fault.
    }
    internal void RequireNativeRetired()
    {
        if (_directories.Values.Any(value => !value.NativeClosed)) throw new InvalidDataException("Native source retirement retains an actual directory reference or unfinished publication.");
    }
    internal void RetireAfterChildExit()
    {
        if (_retired) return;
        if (!process.HasExited) throw new InvalidOperationException("Directory/provider retirement requires actual original child closure.");
        List<Exception> failures = [];
        foreach (var lease in _directories.Values)
        {
            lease.ChildClosed = true;
            try { lease.Parent.CloseChecked(); lease.ParentClosed = true; Record(0, lease); } catch (Exception error) { failures.Add(error); }
        }
        for (var index = _failedDirectories.Count - 1; index >= 0; --index)
            try { _failedDirectories[index].CloseChecked(); _failedDirectories.RemoveAt(index); }
            catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Independent object-directory parent refs failed retirement.", failures);
        _windows?.Dispose(); _windows = null; _retired = true;
    }
    private void Record(ulong call, NativePluginMutexDirectoryLease lease) => _receipts.Add(new(generation, module, call,
        thread, lease.Id, lease.Scope, lease.Directory, _session ?? throw new InvalidOperationException("Namespace has no actual session."),
        lease.Remote, _windows?.Native.Sha256 ?? throw new InvalidOperationException("Namespace Windows source lease is absent."),
        lease.DescriptorSha256, lease.RestrictedStatus, lease.RestrictedError,
        lease.Published, lease.NativeClosed, lease.CloseResult, lease.CloseError, lease.ChildClosed, lease.ParentClosed));
}
