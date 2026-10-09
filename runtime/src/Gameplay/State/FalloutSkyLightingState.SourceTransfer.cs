using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSkyLightingProjection(FalloutFormKey Climate, IReadOnlyList<FalloutRegionWeatherSnapshot> Regions,
    FalloutFormKey? ForcedWeather, FalloutFormKey? ExteriorWeather, ulong RandomState, FalloutFormKey? ClimateWeather);

internal sealed partial class FalloutSkyLightingState
{
    private FalloutSkyTransferState? _sourceTransfer;
    private FalloutSkyTransferDeclaration? _transferDeclaration;
    private FalloutImageSpaceState? _transferImages;
    private string? _transferStack;
    private Guid _transferProcess;
    private string? _transferUnowned = "source-Sky-selected-reset-factory-not-configured";
    private bool _projectionRestore;
    internal FalloutSkyTransferState? SourceTransfer => _sourceTransfer;
    internal string? SourceTransferSaveBlocker => _sourceTransfer?.SaveBlocker;
    internal object SourceTransferState => _sourceTransfer?.State ?? new { unowned = _transferUnowned ?? "source-Sky-transfer-not-constructed" };
    internal void ConfigureSourceTransfer(string executable, string stack, Guid process, FalloutImageSpaceState images)
    {
        if (_sourceTransfer is not null || _transferDeclaration is not null) throw new InvalidOperationException("Source Sky is already constructed.");
        _transferImages = images; _transferStack = stack; _transferProcess = process;
        try
        {
            _transferDeclaration = FalloutSkyTransferDeclaration.Read(executable);
            _sourceTransfer = new(_transferDeclaration, _records, images, stack, _daytimeExtension);
            _sourceTransfer.BindProcess(process);
            _transferUnowned = null;
        }
        catch (NotSupportedException error) { _transferUnowned = error.Message; }
    }
    internal FalloutSkyTransferReturn ResetSourceTransfer(FalloutMainPlayerCellInvocation invocation,
        FalloutReferenceWorld world, FalloutPlayerPendingRequest request, FalloutPlayerTransferPayload payload)
    {
        world.RequireCampaignMainPlayerInvocation(invocation, FalloutMainPlayerCellStep.PendingDestination);
        var state = _sourceTransfer ?? throw new NotSupportedException(_transferUnowned ?? "Source Sky reset owner is absent.");
        return state.ResetForTransfer(invocation, request, payload, () =>
        {
            invocation.Require(FalloutMainPlayerCellStep.PendingDestination);
            world.PlayerMoves.SourcePending.Require(request); world.RequireSourcePlayerRawTransfer(payload);
        }, PublishResetField);
    }
    private void PublishResetField(FalloutSkyResetStep step)
    {
        switch (step)
        {
            case FalloutSkyResetStep.OverrideNull: ForcedWeather = null; break;
            case FalloutSkyResetStep.PreviousNull: break; // No second logical WTHR is manufactured by the lighting sampler.
            case FalloutSkyResetStep.CurrentNull: ExteriorWeather = null; break;
            default: throw new InvalidDataException("Sky reset attempted to write another owner through the lighting projection.");
        }
    }
    internal FalloutSkyLightingProjection CaptureLightingProjection()
    {
        RequireBound();
        return new(_climate.Form, _regions.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new FalloutRegionWeatherSnapshot(pair.Key, pair.Value)).ToArray(), ForcedWeather,
            ExteriorWeather, _random.State, _climateWeather);
    }
    internal void RestoreLightingProjection(FalloutSkyLightingProjection projection)
    {
        if (_projectionRestore) throw new InvalidOperationException("Sky lighting projection restore reentered.");
        _projectionRestore = true;
        try
        {
            Restore(new(projection.Climate, projection.Regions, projection.ForcedWeather, projection.ExteriorWeather,
            projection.RandomState, projection.ClimateWeather));
        }
        finally { _projectionRestore = false; }
    }
    internal void CommitLightingProjection(FalloutSkyLightingProjection projection)
    {
        RestoreLightingProjection(projection);
        _sourceTransfer?.ResetClimate(_climate.Form, PublishResetField);
    }
    private void SourceClimateSelected() => _sourceTransfer?.ResetClimate(_climate.Form, PublishResetField);
    private FalloutSkyTransferSnapshot? CaptureSourceTransfer()
    {
        if (_sourceTransfer is not null) return _sourceTransfer.Capture();
        return null;
    }
    private void RestoreSourceTransfer(FalloutSkyLightingSnapshot snapshot)
    {
        if (_projectionRestore) return;
        if (_transferDeclaration is null)
        {
            if (snapshot.SourceTransfer is not null || snapshot.TransferUnowned != _transferUnowned || _transferUnowned is null)
                throw new InvalidDataException("Saved Sky changed an unowned selected executable into an admitted source owner.");
            return;
        }
        if (snapshot.SourceTransfer is not { } saved || snapshot.TransferUnowned is not null)
            throw new InvalidDataException("Current Sky save omitted its actual source reset/child/manager continuation.");
        _sourceTransfer?.Retire();
        var next = new FalloutSkyTransferState(_transferDeclaration, _records,
            _transferImages ?? throw new InvalidOperationException("Sky image manager is absent."),
            _transferStack ?? throw new InvalidOperationException("Sky selection is absent."), _daytimeExtension);
        // Publish the new C# ownership before any manager registration. A
        // failed restore retains its partial owners for independent retirement.
        _sourceTransfer = next;
        next.Restore(saved, _transferProcess);
    }
    internal void RebindSourceTransferProcess(Guid process)
    {
        if (process == Guid.Empty) throw new InvalidDataException("Sky current process is absent.");
        _transferProcess = process;
    }
    internal void RetireSourceTransfer() => _sourceTransfer?.Retire();
    private string? CaptureSourceTransferUnowned() => _sourceTransfer is not null ? null :
        _transferUnowned ?? "source-Sky-selected-reset-factory-not-configured";
}
