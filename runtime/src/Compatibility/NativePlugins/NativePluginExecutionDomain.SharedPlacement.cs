namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint SharedPlacementCallback = 23;
    private NativePluginSharedPlacements? _sharedPlacements;
    private bool SharedPlacementOwnersRetired => _sharedPlacements is null || _sharedPlacements.ResourcesRetired;
    internal NativePluginDomainChild SharedPlacementProcess { get { VerifyOwner(); return _process; } }
    internal IReadOnlyList<NativePluginSharedPlacementReceipt> SharedPlacementReceipts => _sharedPlacements?.Receipts ?? [];
    internal IReadOnlyList<NativePluginSharedPlacementApiReceipt> SharedPlacementApiReceipts => _sharedPlacements?.ApiReceipts ?? [];
    private byte[] DispatchSharedPlacement(ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        var module = CurrentOriginalIoModule(parent);
        var action = reader.ReadUInt32();
        if (action == 1)
        {
            if (_cngOriginalUnloading) throw new NotSupportedException("Original detach cannot publish a new common allocation.");
            var source = new NativePluginSharedPlacementRequest(reader.ReadUInt32(), reader.ReadUInt64(), reader.ReadUInt64(),
                reader.ReadUInt32(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadUInt32()); Finish(reader);
            if (source.Kind == 1)
            {
                if (_nvseHeapDeclaration is null || source.Object != source.View || _nvseHeapLifetimes.ContainsKey(source.View))
                    throw new InvalidDataException("Common heap construction lacks its exact current allocation declaration.");
            }
            else if (source.Kind == 2)
            {
                if (!_mappingPlans.TryGetValue(source.View, out var plan) || plan.Operation != NativePluginMappingOperation.Map ||
                    plan.Object != source.Object || plan.Handle != source.Handle || plan.Maximum != source.Maximum ||
                    plan.Offset != source.Offset || plan.Preferred != source.Preferred || plan.Bytes != 0 && plan.Bytes != source.Length ||
                    !_mappingObjects.TryGetValue(source.Object, out var mapping) || !mapping.Writable || mapping.Protection != 4 ||
                    (plan.ProtectionOrAccess & 1) != 0 && plan.ProtectionOrAccess != 0x000f001f || (plan.ProtectionOrAccess & 2) == 0)
                    throw new InvalidDataException("Common mapping construction lacks its exact writable non-copy source plan.");
            }
            else throw new NotSupportedException("Original private/stack/executable memory has no common data-section construction owner.");
            var service = RequireCngMemoryService(parent, owner, module);
            _sharedPlacements ??= new(this, service.MemoryDomain, module, NativeThread);
            var result = _sharedPlacements.Allocate(parent, source);
            return Payload(writer => { writer.Write(result.Id); writer.Write(result.Address); writer.Write(result.Extent); writer.Write(result.Error); });
        }
        if (action == 2)
        {
            var kind = reader.ReadUInt32(); var view = reader.ReadUInt64(); var address = reader.ReadUInt32(); Finish(reader);
            var placements = _sharedPlacements ?? throw new InvalidDataException("Source release lacks its actual common-placement owner.");
            placements.SourceReleased(parent, kind, view, address);
            return Payload(writer => writer.Write(1U));
        }
        throw new NotSupportedException("Original shared-placement request has no source construction/release owner.");
    }
    internal ulong BorrowSharedPlacement(NativePluginCngSharedSource source, NativePluginCngSectionHandle reference)
        => _sharedPlacements?.BorrowForCng(source, reference) ?? 0;
    internal void RetireSharedPlacementBorrow(NativePluginCngSharedSource source, ulong placement)
    {
        if (placement == 0) return;
        (_sharedPlacements ?? throw new InvalidDataException("CNG borrow lost its common-placement collection."))
            .CngBorrowRetired(source.Kind, source.View, source.Address, placement);
    }
    private void RequireSharedPlacementsSourceReleased() => _sharedPlacements?.RequireSourceReleased();
    private void RequireSharedPlacementSaveOwned()
    {
        if (_sharedPlacements is not null && _sharedPlacements.Receipts.Count != 0)
            throw new NotSupportedException("Original common-address section/process/caller memory has no admitted cold reconstruction owner.");
    }
    private void ClearSharedPlacementsAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Common placement cannot release source leases before exact original closure.");
        _sharedPlacements?.ReleaseAfterBothProcessExit();
    }
}
