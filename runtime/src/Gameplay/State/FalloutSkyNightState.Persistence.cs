using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSkyNightSnapshot(string SourceContract, Guid CapturedOwner, Guid CapturedSky,
    Guid CapturedProcess, long Changed, uint AlphaBits, FalloutSkyNightCall? Call, FalloutSkyNightFields? Native);

internal sealed partial class FalloutSkyTransferState
{
    private FalloutSkyNightState? _night;
    private FalloutSkyNightSnapshot? _coldNight;
    internal FalloutSkyNightState ConstructNightInActualModeLoad(FalloutSkyModeCall invocation)
    {
        BindPresentationThread(); RequireWriter();
        if (_modeCall != invocation || !_modeExecuting || invocation.EnteredChild != FalloutSkyModeStep.ActiveResourceLoad ||
            invocation.Sky != Identity || invocation.Process != _process)
            throw new InvalidOperationException("NightSky constructor has no actual entered original resource-loader child.");
        if (_night is not null) return _night;
        _night = new(this, _process);
        if (_coldNight is { } cold) { _night.Restore(cold); _coldNight = null; }
        MarkActualModeChild(FalloutSkyModeRole.Night, _night.Identity, "source-NightSky-native-loader-and-mode-release-unowned");
        return _night;
    }
    private FalloutSkyNightSnapshot? CaptureNight()
    {
        if (_coldNight is not null) throw new NotSupportedException("source-NightSky-cold-real-factory-republication-pending");
        return _night?.Capture();
    }
    private void RestoreNight(FalloutSkyTransferSnapshot saved) => _coldNight = saved.Night;
    private void RetireNight()
    {
        if (_night is not null) { _night.Retire(); _night = null; }
    }
    private static void ValidateNight(FalloutSkyTransferSnapshot saved)
    {
        if (saved.Night is not { } night) return;
        if (!saved.Source.IsStandalone || night.SourceContract != saved.Source.Contract ||
            night.CapturedSky != saved.CapturedSky || night.CapturedProcess != saved.CapturedProcess)
            throw new InvalidDataException("Saved NightSky changed its actual source Sky/process constructor.");
        FalloutSkyNightState.Validate(night);
    }
}
