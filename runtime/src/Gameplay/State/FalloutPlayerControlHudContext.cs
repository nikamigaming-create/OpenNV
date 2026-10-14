using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed class FalloutPlayerControlHudContext : IFalloutPlayerControlHudContext
{
    public FalloutPlayerControlHudSource Source { get; }
    public string Stack { get; }
    public Guid Process { get; }
    public Guid Factory { get; } = Guid.NewGuid();
    private readonly FalloutStandaloneInterfaceState _interface;
    private readonly FalloutSourceFrameTimerState _timer;
    private readonly FalloutRendererClockState _clock;
    private readonly FalloutDirectInputState _input;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private byte _messageSuppression;
    private bool _suppressionAvailable = true;
    internal FalloutPlayerControlHudContext(FalloutPlayerControlHudSource source, string stack, Guid process,
        FalloutStandaloneInterfaceState currentInterface, FalloutSourceFrameTimerState timer,
        FalloutRendererClockState clock, FalloutDirectInputState input, FalloutPlayerControlFieldsState fields)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(currentInterface); ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(fields);
        if (process == Guid.Empty || currentInterface.Process != process || currentInterface.Stack != stack || currentInterface.Source.Main != source.Controls.Main)
            throw new InvalidDataException("Source HUD context selected a foreign actual interface process.");
        Source = source; Stack = stack; Process = process; _interface = currentInterface; _timer = timer; _clock = clock;
        _input = input;
        fields.RequireActualInput(input); currentInterface.PublishHudControlInput(input);
        RequireCurrent();
    }
    public void PublishMenu(Guid menu, FalloutUiComponentStore ui, FalloutUiComponentBinding root)
    { RequireCurrent(); _interface.PublishHudTransitionMenu(menu, ui, root); }
    public void ReplaceMenuTransition(Guid menu, uint durationBits)
    { RequireCurrent(); _interface.ReplaceHudMenuTransition(menu, durationBits); }
    public IReadOnlyList<FalloutPlayerControlHudTransition> AdvanceMenuTransitions(FalloutPlayerControlHudFrame frame)
    { RequireCurrent(); return _interface.AdvanceHudMenuTransitions(frame); }
    public IReadOnlyList<FalloutPlayerControlHudTransition> CaptureMenuTransitions() => _interface.CaptureHudMenuTransitions();
    public void RequireCurrent()
    {
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_input.ReadManagerOwner(), _input) || _input.Source != Stack ||
            _interface.Retired || _interface.Process != Process || _interface.Stack != Stack)
            throw new InvalidOperationException("HUD context lost its actual source thread, input or interface lifetime.");
    }
    public FalloutPlayerControlHudFrame ReadTransitionFrame()
    {
        RequireCurrent(); var delta = _timer.ReadScaledDelta(); var mode = _clock.ReadMainControlMode(Process);
        if (delta.Source != Source.Controls.Main.EngineSha256 || delta.Process != Process)
            throw new InvalidOperationException("HUD transition selected another actual cached source timer.");
        if (mode == 4) throw new NotSupportedException("source-HUD-control-mode-four-current-divisor-unbound");
        return new(delta, mode, null);
    }
    public bool ReadInterfaceMovementOverride() { RequireCurrent(); return _interface.ReadHudMovementOverride(); }
    public bool ReadMenuActive(uint menu) { RequireCurrent(); return _interface.ReadHudActiveMenu(menu); }
    public bool ReadMessageSuppression()
    {
        RequireCurrent();
        if (!_suppressionAvailable) throw new NotSupportedException("source-HUD-message-suppression-current-writer-unbound");
        return _messageSuppression != 0;
    }
    internal void StoreMessageSuppressionZero() { RequireCurrent(); _messageSuppression = 0; }
    internal void ObserveMessageSuppressionWriter() { RequireCurrent(); _suppressionAvailable = false; }
    public int ReadDefaultMode(ushort disabledWord)
    { RequireCurrent(); throw new NotSupportedException("source-HUD-current-menu-and-Player-status-predicate-unbound"); }
    public int ReadForcedSubtitleMode(int mode)
    { RequireCurrent(); throw new NotSupportedException("source-HUD-current-subtitle-and-Player-context-predicate-unbound"); }
    public void UpdateDynamicActionView()
    { RequireCurrent(); throw new NotSupportedException("source-HUD-dynamic-action-view-current-owner-unbound"); }
    public bool ReadForcedSubtitleAvailable()
    { RequireCurrent(); throw new NotSupportedException("source-HUD-current-subtitle-manager-array-unbound"); }
    public void ChangeForcedSubtitle(bool enabled)
    { RequireCurrent(); throw new NotSupportedException("source-HUD-current-subtitle-manager-flag-writer-unbound"); }
    public bool ReadMessageSound(int handle)
    { RequireCurrent(); throw new NotSupportedException("source-HUD-current-message-sound-handle-getter-unbound"); }
    public void SetMessageSoundPaused(int handle, bool paused)
    { RequireCurrent(); throw new NotSupportedException("source-HUD-current-message-sound-pause-resume-unbound"); }
}
