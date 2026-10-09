namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private bool _cngOriginalUnloading;
    internal IReadOnlyList<NativePluginCngSharedReceipt> CngSharedReceipts => _cngService?.SharedReceipts ?? [];
    internal IReadOnlyList<NativePluginCngSharedCallReceipt> CngSharedCalls => _cngService?.SharedCalls ?? [];
    internal IReadOnlyList<NativePluginCngDetachReceipt> CngDetachReceipts => _cngService?.DetachReceipts ?? [];
    internal NativePluginCngSectionHandle RetainCngSharedSource(NativePluginCngSharedSource source)
    {
        VerifyOwner();
        if (source.Kind is not (1 or 2) || source.Object == 0 || source.View == 0 || source.Address == 0 ||
            source.Handle is 0 or uint.MaxValue || source.Length == 0 || source.Maximum == 0 ||
            source.Offset >= source.Maximum || source.Length > source.Maximum - source.Offset ||
            source.Length > (1UL << 32) - source.Address)
            throw new InvalidDataException("Shared CNG source has an incomplete generation/handle/view extent.");
        if (source.Kind == 1)
        {
            if (_nvseHeapDeclaration is null || !_nvseHeapLifetimes.TryGetValue(source.View, out var heap) ||
                source.Object != source.View || source.Offset != 0 || heap.Generation != Generation || heap.RetirementCallback is not null ||
                heap.Address != source.Address || heap.Length != source.Length || source.Maximum < source.Length)
                throw new InvalidDataException("Shared CNG heap is not the genuine selected native allocation lifetime.");
        }
        else
        {
            if (!_mappingViews.TryGetValue(source.View, out var view) || !_mappingObjects.TryGetValue(source.Object, out var obj) ||
                view.Object != source.Object || view.Address != source.Address || view.LogicalBytes != source.Length ||
                view.Offset != source.Offset || obj.Maximum != source.Maximum || !obj.Writable ||
                obj.Protection != 4 || view.State != 0x1000 || view.Protection != 4 || (view.Access & 1) != 0)
                throw new InvalidDataException("Shared CNG mapping is not its actual writable non-copy source view.");
        }
        _process.RequireCngMappedView(source);
        return _process.RetainCngSection(source.Handle);
    }
    internal uint ReceiveCngSharedSource(NativePluginCngSectionHandle retained)
    { VerifyOwner(); return _process.ReceiveCngSection(retained); }
    internal void RequireCngDetachSource(ulong module, uint image, ulong call)
    {
        VerifyOwner();
        if (!_cngOriginalUnloading || _currentCall != call || _nvsePlugin is not { } plugin ||
            plugin.Module != module || plugin.Generation != Generation || plugin.Image != image)
            throw new InvalidDataException("CNG detach lacks the retained original image and actual current unload transaction.");
    }
    internal Exception FailCngSharedLifetime(Exception error) => Fatal(error);
}
