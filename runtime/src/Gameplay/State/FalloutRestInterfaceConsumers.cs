using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Narrow actual host joins. The existing rest owner retains the attempted
// Begin/Close prefix, and the independent fade owner retains its own clock.
// These methods do not complete a save, hour, world list or process callback.
internal sealed class FalloutRestInterfaceConsumers
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutSleepWait _rest;
    private readonly FalloutInterfaceFade _fade;
    private readonly FalloutConsoleActivity _console;
    private readonly Func<FalloutInterfaceFadeForceRetirement> _forceRetirement;

    internal FalloutRestInterfaceConsumers(FalloutPluginStack records, FalloutSleepWait rest, FalloutInterfaceFade fade,
        FalloutConsoleActivity console, Func<FalloutInterfaceFadeForceRetirement> actualGlobalForceRetirement)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(actualGlobalForceRetirement);
        rest.Source.Validate(); fade.Source.Validate(); console.Source.Validate();
        if (rest.Source.EngineSha256 != fade.Source.Declaration.EngineSha256 || rest.Source.RuntimeSha256 != fade.Source.RuntimeSha256 ||
            rest.Source.EngineSha256 != console.Source.EngineSha256 || rest.Source.RuntimeSha256 != console.Source.RuntimeSha256)
            throw new InvalidDataException("Rest interface consumers do not share the selected source lifetime.");
        _records = records; _rest = rest; _fade = fade; _console = console; _forceRetirement = actualGlobalForceRetirement;
    }
    internal FalloutRestObservation ObserveSourceClose() => _console.ObserveRestClose();

    // Original order: cue, policy autosave, sleep-only fade, actual native
    // counting/control mutations, then signed hours/independent sleep flag.
    internal void BeforeMenuPlayerHours(FalloutRestRequest request)
    {
        RequireRequest(request);
        if (_rest.Phase != FalloutRestPhase.Choosing)
            throw new InvalidOperationException("Source sleep fade cannot replay after the countdown began.");
        if (request.Kind == FalloutRestKind.Sleep)
            _fade.Start(0, _records.NumericSettings.Float("fFadeToBlackFadeSeconds"), decreasing: false);
    }
    // Caller must complete the actual world-end consumer BEFORE invoking this,
    // and must retire the actual source menu AFTER it returns. No substitute
    // cleanup event may admit the still-unowned world-end process-list arm.
    internal void BeforeNativeMenuRetirement(FalloutRestRequest request, bool completed)
    {
        RequireRequest(request);
        var expected = completed ? FalloutRestPhase.Completed : FalloutRestPhase.Cancelled;
        if (_rest.Phase != expected)
            throw new InvalidOperationException("Source fade release has no genuine completed/cancelled rest prefix.");
        if (request.Kind == FalloutRestKind.Sleep) _fade.End(0, force: false, _forceRetirement);
    }
    private void RequireRequest(FalloutRestRequest request)
    {
        _rest.RequireHealthy(); _fade.RequireHealthy(); request.Validate();
        if (request.Origin == FalloutRestOrigin.ScriptHours || !_rest.Published || !_rest.MenuPending || request != _rest.Request)
            throw new InvalidOperationException("Rest interface action has no genuine current native source-menu request.");
    }
}
