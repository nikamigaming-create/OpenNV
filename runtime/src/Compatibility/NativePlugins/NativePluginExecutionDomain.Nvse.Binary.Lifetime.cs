namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    internal void RequireNvseBinaryIdle()
    {
        VerifyOwner();
        if (_nvseBinaryPending.Count != 0 || _nvseBinaryCalls.Any(call => !call.Complete))
            throw new InvalidOperationException("Native binary methods retain an incomplete source/native-output prefix.");
    }
    private void RequireNvseBinaryCallerComplete(ulong caller)
    {
        if (_nvseBinaryPending.Values.Any(pending => pending.Caller == caller) ||
            _nvseBinaryCalls.Any(call => call.Caller == caller && !call.Complete))
            throw new InvalidDataException("Original caller returned before actual binary output/class publication completed.");
    }
    private void RequireNvseBinaryGuestRelease(ulong handle)
    {
        if (_nvseBinaryFiles.Values.Any(binding => binding.Image.Handle == handle || binding.Buffer.Handle == handle))
            throw new InvalidOperationException("Native binary receiver still retains this genuine guest allocation.");
    }
    internal void RequireNvseBinarySaveOwned()
    {
        RequireNvseBinaryIdle();
        if (_nvseBinaryFiles.Count != 0 || _nvseBinaryCalls.Count != 0)
            throw new NotSupportedException("Original binary class/CRT/direct-member state needs its complete current cold graph owner.");
    }

    internal void RetireNvseBinaryFile(NativeNvsePlugin plugin, NativeNvseBinaryBinding binding)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); RequireNvseBinaryIdle(); RequireNvseBinaryBinding(plugin, binding, true);
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseBinaryRetire, Payload(writer =>
            { writer.Write(plugin.Module); writer.Write(binding.Id); writer.Write(binding.Image.Address); }));
            if (reader.ReadUInt64() != binding.Id || reader.ReadUInt32() != 0)
                throw new InvalidDataException("Native binary receiver retirement lacks its absent-callback-owner receipt.");
            Finish(reader); _nvseBinaryFiles.Remove(binding.Id); binding.Retired = true;
            if (binding.Contributor.Dependents == 0) throw new InvalidDataException("Binary contributor lifetime lost its child retainer.");
            --binding.Contributor.Dependents; binding.SourceLease.Dispose();
            // Allocation and original CRT lifetimes belong to the complete
            // constructor owner. Retiring callbacks does not fclose/free them.
        }
        catch (Exception error) { throw Fatal(error); }
    }

    private void ClearNvseBinaryAfterChildClosure()
    {
        // Call only after verified exact child closure. This fault cleanup keeps
        // all failed receipts visible and never fabricates DllMain/CRT closure.
        var failures = new List<Exception>();
        foreach (var binding in _nvseBinaryFiles.Values)
        {
            binding.Retired = true;
            if (binding.Contributor.Dependents != 0) --binding.Contributor.Dependents;
            try { binding.SourceLease.Dispose(); } catch (Exception failure) { failures.Add(failure); }
        }
        _nvseBinaryFiles.Clear(); _nvseBinaryPending.Clear(); _nvseBinaryDeclaration = null;
        if (failures.Count != 0) throw new AggregateException("Native binary source leases failed after verified child closure.", failures);
    }
}
