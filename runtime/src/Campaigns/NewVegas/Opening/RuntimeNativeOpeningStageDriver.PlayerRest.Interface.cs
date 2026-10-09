using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutInterfaceFade? _sourceInterfaceFade;
    private FalloutConsoleActivity? _sourceRestConsole;
    private RuntimeNativeInterfaceFade? _sourceInterfaceFadeNative;
    private FalloutRestInterfaceConsumers? _sourceRestInterface;
    private bool _sourceRestInterfaceRetired;
    internal object? SourceRestInterfaceState => new
    {
        fade = _sourceInterfaceFade?.State,
        console = _sourceRestConsole?.State,
        native = _sourceInterfaceFadeNative?.State,
        retired = _sourceRestInterfaceRetired,
    };
    internal string? SourceRestInterfaceSaveBlocker => _sourceInterfaceFade?.SaveBlocker ??
        (_sourceInterfaceFade is null ? "source-interface-fade-owner" : null);
    internal FalloutRestInterfaceConsumers SourceRestInterface => _sourceRestInterface ??
        throw new NotSupportedException("Rest interface consumer is not configured from the actual selected source.");

    // Call after constructing current PlayerRest, before any source request or
    // cold rest-menu publication. Missing native roots/clock/console producers
    // are separately observed; this does not invent them during configuration.
    internal void ConfigureSourceRestInterface(FalloutInterfaceFadeSnapshot? restore,
        Func<FalloutInterfaceFadeForceRetirement> actualGlobalForceRetirement)
    {
        if (_sourceInterfaceFade is not null || _sourceRestInterfaceRetired)
            throw new InvalidOperationException("Rest interface source lifetime cannot be replaced.");
        var runtime = _advancementRuntimeSource ?? throw new NotSupportedException("Rest interface has no selected runtime source.");
        var source = FalloutInterfaceFadeSource.Read(runtime);
        var fade = new FalloutInterfaceFade(source, restore);
        var console = new FalloutConsoleActivity(FalloutConsoleActivitySource.Read(runtime.Receipt));
        var consumer = new FalloutRestInterfaceConsumers(_pluginStack, PlayerRest, fade, console, actualGlobalForceRetirement);
        _sourceInterfaceFade = fade; _sourceRestConsole = console; _sourceRestInterface = consumer;
    }
    internal void BindSourceRestConsole(Func<FalloutConsoleActivitySample> actualConsoleController)
    {
        if (_sourceRestInterfaceRetired) throw new ObjectDisposedException(nameof(FalloutConsoleActivity));
        (_sourceRestConsole ?? throw new NotSupportedException("Rest console source declaration is absent.")).Bind(actualConsoleController);
    }
    internal void PublishSourceRestInterface(Func<FalloutInterfaceFadeClock> actualSourceUiClock,
        IReadOnlyList<RuntimeNativeInterfaceFadeRoot> actualSourceRoots)
    {
        if (_sourceRestInterfaceRetired || _sourceInterfaceFadeNative is not null || !IsInsideTree())
            throw new InvalidOperationException("Rest interface has no new actual native publication lifetime.");
        _sourceInterfaceFadeNative = RuntimeNativeInterfaceFade.Attach(this,
            _sourceInterfaceFade ?? throw new NotSupportedException("Rest fade source is absent."),
            _advancementRuntimeSource ?? throw new NotSupportedException("Rest selected source lease is absent."),
            actualSourceUiClock, actualSourceRoots, RetainSourceRestInterfaceFailure);
    }
    internal FalloutRestObservation ObserveSourceRestCloseGate() => _sourceRestConsole?.ObserveRestClose() ??
        new(FalloutRestFactState.Unowned, "actual-IsConsoleOpen", "Rest has no admitted source interface/console predicate owner.");
    internal FalloutRestObservation ObserveSourceRestFadePublication()
    {
        if (_sourceInterfaceFade is null || _sourceInterfaceFadeNative is null || _sourceRestInterfaceRetired)
            return new(FalloutRestFactState.Unowned, "actual-source-interface-fade", "Actual native fade roots and source UI timer are not bound.");
        return _sourceInterfaceFadeNative.ObserveSourceStart();
    }
    internal FalloutInterfaceFadeSnapshot CaptureSourceInterfaceFade() => (_sourceInterfaceFade ??
        throw new NotSupportedException("Current campaign has no source interface fade owner.")).Capture();
    private void RetainSourceRestInterfaceFailure(Exception error)
    {
        if (_playerRest is not null) RetainCurrentPlayerRestFailure(error);
        else if (ExecutionError is null) RetainDriverFailure(error);
    }
    internal void RetireSourceRestInterface()
    {
        if (_sourceRestInterfaceRetired) return;
        _sourceRestInterfaceRetired = true;
        var failures = new List<Exception>();
        void Release(Action release)
        {
            try { release(); }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
            { failures.Add(error); RetainSourceRestInterfaceFailure(error); }
        }
        var native = _sourceInterfaceFadeNative; _sourceInterfaceFadeNative = null;
        if (native is not null)
        {
            Release(native.Retire);
            Release(() => { if (Godot.GodotObject.IsInstanceValid(native)) native.Free(); });
        }
        if (_sourceRestConsole is { } console) Release(console.Retire);
        if (_sourceInterfaceFade is { } fade) Release(fade.RetireNativeLifetime);
        if (failures.Count != 0) throw new AggregateException("Rest interface lifetime retained native/source retirement failures.", failures);
    }
}
