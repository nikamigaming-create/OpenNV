using Godot;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private IRuntimeSkyHdrCapability? _nativeSkyHdrCapability;
    private FalloutSkyHdrState? _nativeSkyHdrSource;
    private bool _nativeSkyHdrRetiring;
    private FalloutSkyHdrState BindNativeSkyHdr(RuntimeNativeImageSpace presenter)
    {
        var sky = _nativeSkyLighting?.SourceTransfer ?? throw new NotSupportedException("Image HDR has no actual source Sky.");
        var source = sky.ConstructHdr(_nativePluginStack?.IniSettings ?? throw new InvalidOperationException("HDR has no selected typed INI owner."));
        if (_nativeSkyHdrCapability is null)
        {
            IRuntimeSkyHdrCapability native = RuntimeInputPresentationSelection.UsesHeadlessPresentation(DisplayServer.GetName())
                ? new RuntimeHeadlessSkyHdrCapability(source.Source, source.Process)
                : new RuntimeNativeSkyHdrCapability(source.Source, source.Process);
            _nativeSkyHdrCapability = native; _nativeSkyHdrSource = source;
            source.EnterRendererCapability(native);
            try { native.Begin(); }
            catch (Exception failure) { source.RetainFailure(failure); throw; }
        }
        if (!ReferenceEquals(source, _nativeSkyHdrSource) || _nativeSkyHdrRetiring)
            throw new InvalidOperationException("HDR renderer still owns another actual current Sky/process or retirement.");
        presenter.BindCurrentSkyHdr(source, _nativeSkyHdrCapability); return source;
    }
    private void BeginNativeSkyHdrRetirement()
    {
        if (_nativeSkyHdrCapability is not { } native) return;
        if (!_nativeSkyHdrRetiring)
        {
            _nativeSkyHdrSource!.BeginCapabilityRetirement(native); _nativeSkyHdrRetiring = true;
        }
        native.BeginRetirement();
    }
    private async Task DrainNativeSkyHdrRetirement()
    {
        if (_nativeSkyHdrCapability is not { } native) return;
        BeginNativeSkyHdrRetirement(); await native.Retirement;
        _nativeSkyHdrSource!.CompleteCapabilityRetirement(native, () =>
        {
            if (!native.Retired) throw new InvalidOperationException("HDR native resource retirement did not return.");
        });
        _nativeSkyHdrCapability = null; _nativeSkyHdrSource = null; _nativeSkyHdrRetiring = false;
    }
    private void RequireNativeSkyHdrRetired()
    {
        if (_nativeSkyHdrCapability is not null || _nativeSkyHdrSource is not null || _nativeSkyHdrRetiring)
            throw new NotSupportedException("Actual source HDR still owns native device resources; drain before Sky/source teardown.");
    }
    private async Task RetireNativeCellAndHdrConsumers()
    {
        var failures = new List<Exception>();
        try { RetireNativeSourceCellAttachments(); } catch (Exception failure) { failures.Add(failure); }
        try { await DrainNativeSkyHdrRetirement(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("CELL/HDR retirement retains independent actual owner failures.", failures);
    }
    private void ConsumeActualCellSkyMode(FalloutMainPlayerCellInvocation invocation, FalloutReferenceWorld world,
        OpenNV.Runtime.Content.FalloutFormKey cell)
    {
        var sky = _nativeSkyLighting?.SourceTransfer ?? throw new NotSupportedException("CELL Sky mode has no current source Sky.");
        if (!sky.Source.IsStandalone) return; // FNV has an independent unowned declaration; it is not given FO3 mode semantics.
        var input = world.ReadMainPlayerSourceCell(cell);
        _ = sky.SelectModeForActualCell(invocation, input, () =>
        {
            invocation.Require(invocation.Step);
            if (!ReferenceEquals(_nativeReferences, world) || world.ReadMainPlayerSourceCell(cell) != input)
                throw new InvalidOperationException("CELL Sky mode lost its actual campaign/winning-cell load producer.");
        });
    }
}
