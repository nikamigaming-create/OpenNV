namespace OpenNV.Runtime.Compatibility.NativePlugins;

// No original code runs in the service before these memory operations. The
// parent uses both actual creation handles; the real section was created under
// the unchanged original-module token. Nothing copies or relocates its bytes.
internal sealed partial class NativePluginSharedPlacements
{
    private readonly NativePluginDomainChild _original, _service;
    private readonly NativePluginExecutionDomain _originalDomain, _serviceDomain;
    private readonly ulong _module;
    private readonly uint _thread, _page, _granularity;
    private readonly ulong _minimum;
    private readonly Dictionary<(uint Kind, ulong View), NativePluginSharedPlacementLease> _leases = [];
    private readonly List<NativePluginSharedPlacementApiReceipt> _apis = [];
    private ulong _next, _searchCursor;
    private Exception? _failure;
    internal IReadOnlyList<NativePluginSharedPlacementApiReceipt> ApiReceipts => _apis.AsReadOnly();
    internal IReadOnlyList<NativePluginSharedPlacementReceipt> Receipts => _leases.Values.Select(Project).ToArray();
    internal bool ResourcesRetired => _leases.Values.All(value =>
        (value.Section is null or { IsClosed: true }) &&
        (value.OriginalMemory is NativePluginSharedPlacementMemory.None or NativePluginSharedPlacementMemory.ClosedProcess) &&
        (value.ServiceMemory is NativePluginSharedPlacementMemory.None or NativePluginSharedPlacementMemory.ClosedProcess));
    internal NativePluginSharedPlacements(NativePluginExecutionDomain original, NativePluginExecutionDomain service,
        ulong module, uint thread)
    {
        if (module == 0 || thread == 0 || original.Generation == service.Generation)
            throw new ArgumentException("Shared placement requires distinct actual process generations and an original caller.");
        _originalDomain = original; _serviceDomain = service;
        _original = original.SharedPlacementProcess; _service = service.SharedPlacementProcess;
        _module = module; _thread = thread;
        (_page, _granularity, _minimum) = NativePluginDomainChild.SharedSystemGranularity();
    }
    internal NativePluginSharedPlacementResult Allocate(ulong call, NativePluginSharedPlacementRequest source)
    {
        RequireLiving();
        if (_failure is not null) throw new InvalidOperationException("Failed common-address placement cannot replay.", _failure);
        if (call == 0 || source.Kind is not (1 or 2) || source.Object == 0 || source.View == 0 ||
            source.Handle is 0 or uint.MaxValue || source.Length == 0 || source.Maximum == 0 ||
            source.Offset >= source.Maximum || source.Length > source.Maximum - source.Offset || source.Offset % _granularity != 0 ||
            source.Kind == 1 && (source.Object != source.View || source.Offset != 0 || source.Preferred != 0))
            throw new InvalidDataException("Common placement has no complete original data-section declaration.");
        var lease = new NativePluginSharedPlacementLease(checked(++_next), call, source);
        if (!_leases.TryAdd((source.Kind, source.View), lease))
            throw new InvalidDataException("Common placement repeats an already entered source allocation/view.");
        try
        {
            lease.Extent = RoundPages(source.Length);
            if (lease.Extent > RoundPages64(source.Maximum) - source.Offset)
                throw new InvalidDataException("Common placement extends past the actual rounded section extent.");
            lease.Section = _original.RetainCngSection(source.Handle);
            foreach (var prior in _leases.Values)
                if (prior.Id != lease.Id && prior.Source.Kind == source.Kind && prior.Source.Object == source.Object &&
                    prior.Section is { IsClosed: false, IsInvalid: false } section)
                    NativePluginDomainChild.RequireSameCngSection(section, lease.Section);
            var start = _searchCursor == 0 ? _minimum : _searchCursor;
            ulong cursor = source.Preferred != 0 ? source.Preferred : start;
            var wrapped = false;
            while (cursor < 1UL << 32 || !wrapped && source.Preferred == 0 && start > _minimum)
            {
                if (cursor >= 1UL << 32) { wrapped = true; cursor = _minimum; }
                uint candidate;
                if (source.Preferred != 0)
                {
                    // Fixed-address requests are exact. Even a known occupied
                    // address goes through the real reservation API, so the
                    // caller receives its actual Windows error.
                    candidate = source.Preferred;
                }
                else if (!FindCommonFree(cursor, wrapped ? start : 1UL << 32, lease.Extent, out candidate))
                {
                    if (!wrapped && start > _minimum) { wrapped = true; cursor = _minimum; continue; }
                    throw new InvalidOperationException("Both actual address-space observations have no complete common free extent; no Windows result is fabricated.");
                }
                lease.Address = candidate;
                var first = Reserve(lease, true);
                if (first != 0)
                {
                    if (source.Preferred == 0 && first == 487) { cursor = (ulong)candidate + _granularity; continue; }
                    return FailedResult(lease, first);
                }
                var second = Reserve(lease, false);
                if (second != 0)
                {
                    Rollback(lease);
                    if (source.Preferred == 0 && second == 487) { cursor = (ulong)candidate + _granularity; continue; }
                    return FailedResult(lease, second);
                }
                var mappedOriginal = Map(lease, true);
                if (mappedOriginal != 0) { Rollback(lease); return FailedResult(lease, mappedOriginal); }
                var mappedService = Map(lease, false);
                if (mappedService != 0) { Rollback(lease); return FailedResult(lease, mappedService); }
                RequireMapped(lease, true); RequireMapped(lease, false);
                // Publication is the return edge after both actual mappings.
                // Native source consumers separately publish their own typed
                // heap/view rows before returning to the unchanged caller.
                lease.Published = true;
                if (source.Preferred == 0)
                {
                    _searchCursor = Round((ulong)candidate + lease.Extent, _granularity);
                    if (_searchCursor >= 1UL << 32) _searchCursor = _minimum;
                }
                return new(lease.Id, candidate, lease.Extent, 0);
            }
            throw new InvalidOperationException("Common placement exhausted the observed x86 address domain; no Windows error is manufactured.");
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            try { Rollback(lease); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (lease.OriginalMemory == NativePluginSharedPlacementMemory.None && lease.ServiceMemory == NativePluginSharedPlacementMemory.None)
            {
                try { lease.Section?.CloseChecked(); } catch (Exception cleanup) { failures.Add(cleanup); }
            }
            lease.Failure = _failure = failures.Count == 1 ? error : new AggregateException(failures);
            throw new InvalidOperationException("Common placement failed with retained independent section/VM owners.", lease.Failure);
        }
    }
    private bool FindCommonFree(ulong cursor, ulong stop, uint extent, out uint candidate)
    {
        candidate = 0;
        while (cursor < stop)
        {
            cursor = Round(cursor, _granularity);
            if (cursor >= 1UL << 32 || extent > (1UL << 32) - cursor) return false;
            var first = _original.ObserveSharedAddress(cursor, true);
            var second = _service.ObserveSharedAddress(cursor, true);
            if (first is null || second is null) return false;
            if (first.State == 0x10000 && second.State == 0x10000 &&
                extent <= Math.Min(first.End, second.End) - cursor)
            { candidate = checked((uint)cursor); return true; }
            // Merge actual intervals. No valid free region is capped away and
            // no occupied page is overwritten or hidden.
            var next = first.State != 0x10000 ? first.End : second.State != 0x10000 ? second.End : Math.Min(first.End, second.End);
            if (next <= cursor) throw new InvalidDataException("Shared address observations failed to advance.");
            cursor = next;
        }
        return false;
    }
    private uint Reserve(NativePluginSharedPlacementLease lease, bool original)
    {
        var process = original ? _original : _service;
        var result = process.ReserveSharedAddress(lease.Address, lease.Extent);
        Api(lease, process, "VirtualAlloc2/placeholder", lease.Address, result.Address, result.Address != 0, result.Error);
        if (result.Address == 0) return result.Error != 0 ? result.Error : throw new InvalidDataException("Windows reservation omitted its failure error.");
        if (original) { lease.OriginalAddress = result.Address; lease.OriginalMemory = NativePluginSharedPlacementMemory.Placeholder; }
        else { lease.ServiceAddress = result.Address; lease.ServiceMemory = NativePluginSharedPlacementMemory.Placeholder; }
        if (result.Address != lease.Address) throw new InvalidDataException("Actual fixed reservation returned another address.");
        var observed = process.ObserveSharedAddress(result.Address)!;
        if (observed.Base != result.Address || observed.AllocationBase != result.Address ||
            observed.Extent != lease.Extent || observed.State != 0x2000 || observed.AllocationProtection != 1)
            throw new InvalidDataException("Actual placeholder extent/protection differs from the acquired reservation.");
        return 0;
    }
    private uint Map(NativePluginSharedPlacementLease lease, bool original)
    {
        var process = original ? _original : _service;
        var result = process.MapSharedAddress(lease.Section!, lease.Address, lease.Extent, lease.Source.Offset);
        Api(lease, process, "MapViewOfFile3/replace-placeholder", lease.Address, result.Address, result.Address != 0, result.Error);
        if (result.Address == 0) return result.Error != 0 ? result.Error : throw new InvalidDataException("Windows mapping omitted its failure error.");
        if (original) { lease.OriginalAddress = result.Address; lease.OriginalMemory = NativePluginSharedPlacementMemory.Mapped; }
        else { lease.ServiceAddress = result.Address; lease.ServiceMemory = NativePluginSharedPlacementMemory.Mapped; }
        if (result.Address != lease.Address) throw new InvalidDataException("Actual replacement mapping relocated its reserved pointer.");
        return 0;
    }
    private void RequireMapped(NativePluginSharedPlacementLease lease, bool original, uint protection = 4)
    {
        var observed = (original ? _original : _service).ObserveSharedAddress(lease.Address)!;
        if (observed.Base != lease.Address || observed.AllocationBase != lease.Address || observed.Extent != lease.Extent ||
            observed.State != 0x1000 || observed.Type != 0x40000 || observed.Protection != protection)
            throw new InvalidDataException("Actual common section mapping lost its exact address/extent/protection.");
    }
    private NativePluginSharedPlacementResult FailedResult(NativePluginSharedPlacementLease lease, uint error)
    {
        if (error == 0 || lease.Published || lease.OriginalMemory != NativePluginSharedPlacementMemory.None ||
            lease.ServiceMemory != NativePluginSharedPlacementMemory.None)
            throw new InvalidDataException("Common allocation failure retained an unreported acquired range.");
        lease.Section?.CloseChecked();
        return new(lease.Id, 0, lease.Extent, error);
    }
    private uint RoundPages(uint value) => checked((uint)Round(value, _page));
    private ulong RoundPages64(ulong value) => Round(value, _page);
    private static ulong Round(ulong value, uint granularity) => checked((value + granularity - 1) / granularity * granularity);
    private void Api(NativePluginSharedPlacementLease lease, NativePluginDomainChild process, string operation,
        uint requested, ulong returned, bool success, uint error, uint previous = 0)
        => _apis.Add(new(lease.Id, process.Id, operation, requested, returned, lease.Extent, success, error, previous));
    private void RequireLiving()
    {
        _ = _originalDomain.SharedPlacementProcess; _ = _serviceDomain.SharedPlacementProcess;
        if (_originalDomain.ChildExited || _serviceDomain.ChildExited)
            throw new InvalidOperationException("Common placement cannot borrow a retired process generation.");
    }
    private NativePluginSharedPlacementReceipt Project(NativePluginSharedPlacementLease value)
        => new(value.Id, _originalDomain.Generation, _serviceDomain.Generation, _originalDomain.ProcessId, _serviceDomain.ProcessId,
            _module, _thread, value.Call, value.Source, value.Address, value.Extent, value.Published, value.SourceReleased,
            value.CngBorrowEntered, value.CngBorrowRetired, value.OriginalMemory, value.ServiceMemory,
            value.Section is null or { IsClosed: true }, value.Failure?.ToString());
}
