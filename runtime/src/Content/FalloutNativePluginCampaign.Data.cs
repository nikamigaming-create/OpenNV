using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal abstract class NativeNvseInventoryPointerAuthority
{
    internal abstract ulong Generation { get; }
    internal abstract ulong Module { get; }
    internal abstract string SourceIdentity { get; }
    internal abstract void RequireCurrent();
    internal abstract FalloutFormKey ReadContainer(uint pointer);
    internal abstract FalloutFormKey ReadItem(uint pointer);
    internal abstract int? ReadExtraVariant(uint pointer, FalloutFormKey container, FalloutFormKey item);
    internal abstract NativeNvseInventoryPublication Construct(FalloutInventoryReferenceEntry source);
}

internal sealed record FalloutNativePluginDataBindings(FalloutDirectInputState Input,
    FalloutInventoryReferenceStore Inventory,
    Func<NativePluginExecutionDomain, NativeNvsePlugin, NativeNvseInventoryPointerAuthority?>? InventoryPointers = null);

internal sealed partial class FalloutNativePluginCampaign
{
    internal object DataExecutionState => new
    {
        Source = _source.StackId,
        Input = _dataBindings is { } bindings ? new
        {
            bindings.Input.Source,
            bindings.Input.Window,
            bindings.Input.Sample,
            bindings.Input.Revision,
            bindings.Input.Failure,
            bindings.Input.NativeReaders
        } : null,
        InventoryFrameActive = _dataBindings?.Inventory.HasFrame,
        InventoryEntries = _dataBindings?.Inventory.Entries.Count,
        Modules = _modules.Select(module => new
        {
            module.Domain.Generation,
            module.Plugin.Module,
            State = module.Domain.NvseDataState,
            Callbacks = module.Domain.NvseDataCallbacks,
            Crypto = module.Domain.NvseCryptoReceipts,
            Mappings = module.Domain.NvseMappingReceipts,
            EngineCommands = module.Domain.NvseEngineCommandEvents,
            SharedCryptoViews = module.Domain.CngSharedReceipts,
            SharedCryptoCalls = module.Domain.CngSharedCalls,
            CryptoDetach = module.Domain.CngDetachReceipts
        }).ToArray()
    };
    private sealed class CampaignData(FalloutNativePluginCampaign campaign, NativePluginExecutionDomain domain,
        NativeNvsePlugin plugin, FalloutNativePluginDataBindings bindings) : NativeNvseRuntimeDataAuthority
    {
        internal override string SourceIdentity => campaign._source.StackId;
        internal override IDisposable RetainSource() { RequireCurrent(); return bindings.Input.RetainNativeReader(); }
        internal override void RequireCurrent()
        {
            campaign.RequireCurrent(); bindings.Input.RequireCurrent();
            if (!StringComparer.OrdinalIgnoreCase.Equals(bindings.Input.Source, campaign._source.StackId) ||
                !StringComparer.OrdinalIgnoreCase.Equals(bindings.Inventory.SourceIdentity, campaign._source.StackId) ||
                !ReferenceEquals(bindings.Inventory.WorldIdentity, campaign._world) ||
                !ReferenceEquals(bindings.Input.Controls, campaign._scripts.Controls))
                throw new InvalidDataException("Native Data input/source campaign identity differs.");
        }
        internal override NativeNvseInputSnapshot CaptureInput()
        {
            RequireCurrent(); var value = bindings.Input.Capture();
            return new(value.Source, value.Window, value.Sample, value.Revision, value.Keys.Select(key => new NativeNvseInputKey(
                key.Raw, key.Game, key.Inserted, key.Hold, key.Tap, key.UserDisabled, key.ScriptDisabled)).ToArray(), value.Acquired, value.AcquisitionFailure);
        }
        internal override void PublishInput(IReadOnlyList<NativeNvseInputControlChange> changes)
        {
            RequireCurrent();
            FalloutDirectInputKey Key(NativeNvseInputKey value) => new(value.Raw, value.Game, value.Inserted, value.Hold, value.Tap, value.UserDisabled, value.ScriptDisabled);
            bindings.Input.PublishNativeControls(changes.Select(value => new FalloutDirectInputControlChange(value.Key, Key(value.Before), Key(value.After))).ToArray());
        }
        internal override NativeNvseInventoryPublication CreateInventory(uint container, uint item, int count, uint extra)
        {
            RequireCurrent();
            var pointers = bindings.InventoryPointers?.Invoke(domain, plugin) ?? throw new NotSupportedException(
                "Actual inventory pools exist, but Data InventoryCreateEntry lacks genuine TESObjectREFR/item/ExtraDataList pointer projections and the original temporary-reference constructor.");
            pointers.RequireCurrent();
            if (pointers.Generation != domain.Generation || pointers.Module != plugin.Module ||
                !StringComparer.OrdinalIgnoreCase.Equals(pointers.SourceIdentity, campaign._source.StackId))
                throw new InvalidDataException("Inventory native pointer producer belongs to a foreign module/generation/source.");
            var owner = pointers.ReadContainer(container); var form = pointers.ReadItem(item);
            int? variant = extra == 0 ? null : pointers.ReadExtraVariant(extra, owner, form) ??
                throw new InvalidDataException("Non-null native ExtraDataList has no actual source stack.");
            var entry = bindings.Inventory.PrepareCreate(owner, form, count, variant);
            NativeNvseInventoryPublication publication;
            try { publication = pointers.Construct(entry); }
            catch { bindings.Inventory.RetireEntry(entry); throw; }
            return publication with
            {
                Lifetime = new InventoryEntryLifetime(publication.Lifetime, bindings.Inventory, entry),
                RequireCurrent = () => { pointers.RequireCurrent(); bindings.Inventory.RequireCurrent(entry); publication.RequireCurrent(); }
            };
        }
        internal override void RequireInventoryAbsence(uint formId)
        {
            RequireCurrent();
            if (bindings.Inventory.Entries.Count != 0)
                throw new NotSupportedException("Inventory reference lookup requires the current shared/foreign entry's genuine projection into this native child.");
            // This shared manager has no live entries. Every admitted creation
            // uses it; unowned creators refuse rather than publish another map.
        }
    }
    internal void RequireDataIdleForSave()
    {
        RequireCurrent();
        foreach (var module in _modules.Where(value => !value.Retired)) module.Domain.RequireNvseDataIdleForSave(module.Plugin);
        if (_dataBindings?.Inventory.HasFrame == true) throw new InvalidOperationException("Native inventory frame still owns the campaign save boundary.");
    }
    internal void BeginDataFrame(ulong frame) { RequireCurrent(); _dataBindings?.Inventory.BeginFrame(frame); }
    internal void EndDataFrame(ulong frame)
    {
        RequireCurrent();
        var failures = new List<Exception>();
        foreach (var module in _modules.Where(value => !value.Retired))
            try { module.Domain.RetireNvseInventoryFrame(module.Plugin); } catch (Exception error) { failures.Add(error); }
        try { _dataBindings?.Inventory.EndFrame(frame); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Actual native inventory frame retains independent retirement failures.", failures);
    }
    private sealed class InventoryEntryLifetime(IDisposable native, FalloutInventoryReferenceStore store,
        FalloutInventoryReferenceEntry entry) : IDisposable
    {
        private bool _nativeRetired, _retired;
        public void Dispose()
        {
            if (_retired) return;
            if (!_nativeRetired) { native.Dispose(); _nativeRetired = true; }
            store.RetireEntry(entry); _retired = true;
        }
    }
}
