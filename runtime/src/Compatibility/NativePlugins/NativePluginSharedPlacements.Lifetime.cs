namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginSharedPlacements
{
    private void Rollback(NativePluginSharedPlacementLease lease)
    {
        if (lease.Published) throw new InvalidOperationException("Published original buffers cannot be relocated or rolled back.");
        var failures = new List<Exception>();
        try { RollbackSide(lease, false); } catch (Exception error) { failures.Add(error); }
        try { RollbackSide(lease, true); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Independent acquired common-address ranges did not roll back.", failures);
    }
    private void RollbackSide(NativePluginSharedPlacementLease lease, bool original)
    {
        var process = original ? _original : _service;
        var memory = original ? lease.OriginalMemory : lease.ServiceMemory;
        var address = original ? lease.OriginalAddress : lease.ServiceAddress;
        if (memory is NativePluginSharedPlacementMemory.None or NativePluginSharedPlacementMemory.ClosedProcess) return;
        if (process.HasExited)
        {
            if (original) lease.OriginalMemory = NativePluginSharedPlacementMemory.ClosedProcess;
            else lease.ServiceMemory = NativePluginSharedPlacementMemory.ClosedProcess;
            throw new InvalidOperationException("Exact process exited during common placement; no successful rollback is inferred.");
        }
        var result = memory == NativePluginSharedPlacementMemory.Placeholder
            ? process.FreeSharedPlaceholder(address) : process.UnmapSharedAddress(address);
        Api(lease, process, memory == NativePluginSharedPlacementMemory.Placeholder ? "VirtualFreeEx/rollback-placeholder" :
            "UnmapViewOfFile2/rollback-view", address, 0, result.Success, result.Error);
        if (!result.Success) throw new System.ComponentModel.Win32Exception(unchecked((int)result.Error), "Actual independent common-placement rollback failed.");
        if (original) lease.OriginalMemory = NativePluginSharedPlacementMemory.None;
        else lease.ServiceMemory = NativePluginSharedPlacementMemory.None;
        if (process.ObserveSharedAddress(address)!.State != 0x10000)
            throw new InvalidDataException("Actual rollback address remains occupied; failure is retained.");
    }
    internal ulong BorrowForCng(NativePluginCngSharedSource source, NativePluginCngSectionHandle actualReference)
    {
        RequireLiving();
        var lease = RequireLease(source.Kind, source.View, source.Address);
        if (lease.SourceReleased || lease.CngBorrowEntered || lease.Failure is not null ||
            lease.Source.Object != source.Object || lease.Source.Length != source.Length ||
            lease.Source.Maximum != source.Maximum || lease.Source.Offset != source.Offset)
            throw new InvalidDataException("CNG cannot adopt an unrelated, changed, retired or replayed common placement.");
        NativePluginDomainChild.RequireSameCngSection(lease.Section!, actualReference);
        RequireMapped(lease, true); RequireMapped(lease, false);
        // Retain the entered prefix before the service receives a real borrowed
        // view. A later failure cannot pretend that bind never happened.
        lease.CngBorrowEntered = true;
        return lease.Id;
    }
    internal void CngBorrowRetired(uint kind, ulong view, uint address, ulong placement)
    {
        var lease = RequireLease(kind, view, address);
        if (lease.Id != placement || !lease.CngBorrowEntered || lease.CngBorrowRetired)
            throw new InvalidDataException("CNG shared-view retirement lost its actual entered placement borrow.");
        lease.CngBorrowRetired = true;
        RequireMapped(lease, false);
    }
    internal void SourceReleased(ulong call, uint kind, ulong view, uint address)
    {
        var lease = RequireLease(kind, view, address);
        if (call == 0 || lease.ReleaseEntered || lease.SourceReleased || lease.Failure is not null ||
            lease.CngBorrowEntered && !lease.CngBorrowRetired)
            throw new InvalidDataException("Source release is absent, repeated, failed or retained by a real CNG borrow.");
        lease.ReleaseEntered = true;
        try
        {
            if (_originalDomain.ChildExited)
                throw new InvalidOperationException("Ordinary source release cannot stand in for exact child closure.");
            var observed = _original.ObserveSharedAddress(address)!;
            if (kind == 1)
            {
                RequireMapped(lease, true, 1);
                lease.OriginalMemory = NativePluginSharedPlacementMemory.SourceQuarantined;
            }
            else
            {
                if (observed.State != 0x10000)
                    throw new InvalidDataException("Original source UnmapViewOfFile did not leave its published address free.");
                lease.OriginalMemory = NativePluginSharedPlacementMemory.None;
            }
            if (_serviceDomain.ChildExited)
                lease.ServiceMemory = NativePluginSharedPlacementMemory.ClosedProcess;
            else if (kind == 1)
            {
                var result = _service.QuarantineSharedAddress(address, lease.Extent);
                Api(lease, _service, "VirtualProtectEx/source-quarantine", address, address,
                    result.Success, result.Error, result.Previous);
                if (!result.Success)
                    throw new System.ComponentModel.Win32Exception(unchecked((int)result.Error), "Service alias could not quarantine with its original heap lifetime.");
                lease.ServiceMemory = NativePluginSharedPlacementMemory.SourceQuarantined;
                if (result.Previous != 4) throw new InvalidDataException("Service quarantine changed an unexpected original protection.");
                RequireMapped(lease, false, 1);
            }
            else
            {
                var result = _service.UnmapSharedAddress(address);
                Api(lease, _service, "UnmapViewOfFile2/source-release", address, 0, result.Success, result.Error);
                if (!result.Success)
                    throw new System.ComponentModel.Win32Exception(unchecked((int)result.Error), "Service mapping alias did not independently retire.");
                lease.ServiceMemory = NativePluginSharedPlacementMemory.None;
                if (_service.ObserveSharedAddress(address)!.State != 0x10000)
                    throw new InvalidDataException("Service source-unmap address remains occupied.");
            }
            lease.SourceReleased = true;
            if (kind == 2) lease.Section!.CloseChecked();
        }
        catch (Exception error) { lease.Failure = _failure = error; throw; }
    }
    internal void RequireSourceReleased()
    {
        if (_failure is not null) throw new InvalidOperationException("Common placement retains a failed independent lifetime.", _failure);
        if (_leases.Values.Any(lease => lease.Published && (!lease.SourceReleased || lease.CngBorrowEntered && !lease.CngBorrowRetired)))
            throw new InvalidDataException("Original module still owns a published common allocation or entered CNG borrow.");
    }
    internal void ReleaseAfterBothProcessExit()
    {
        if (!_originalDomain.ChildExited || !_serviceDomain.ChildExited)
            throw new InvalidOperationException("Common section references must survive both exact child closures.");
        var failures = new List<Exception>();
        foreach (var lease in _leases.Values)
        {
            if (lease.OriginalMemory != NativePluginSharedPlacementMemory.None)
                lease.OriginalMemory = NativePluginSharedPlacementMemory.ClosedProcess;
            if (lease.ServiceMemory != NativePluginSharedPlacementMemory.None)
                lease.ServiceMemory = NativePluginSharedPlacementMemory.ClosedProcess;
            try { lease.Section?.CloseChecked(); } catch (Exception error) { lease.Failure ??= error; failures.Add(error); }
        }
        // A successful terminal cleanup never clears an earlier failed source
        // prefix or relabels process-exit release as an ordinary unmap receipt.
        if (failures.Count != 0) { _failure ??= new AggregateException(failures); throw _failure; }
    }
    private NativePluginSharedPlacementLease RequireLease(uint kind, ulong view, uint address)
    {
        if (!_leases.TryGetValue((kind, view), out var lease) || !lease.Published || lease.Address != address)
            throw new InvalidDataException("Shared view is not the exact previously published source placement.");
        return lease;
    }
}
