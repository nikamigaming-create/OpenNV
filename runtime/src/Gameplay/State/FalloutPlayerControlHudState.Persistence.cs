using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerControlHudState
{
    internal FalloutPlayerControlHudSnapshot Capture()
    {
        // Diagnostic capture preserves entered/failed command prefixes. It
        // does not query or advance clocks, transitions, input or source UI.
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("HUD capture changed its actual source thread.");
        var saved = new FalloutPlayerControlHudSnapshot(_source.Identity, _stack, _process, _factory,
            _ui.Factory, _root.Epoch, _sequence, _menuMode, _mode, _visibilityMask, _allowed, _messageHandle,
            _subtitle, _visibilityChanged, _pendingMessage, _displayInitialized, _initializationReturned, _display,
            _historicalTransitions ?? _context.CaptureMenuTransitions(), _call, _failure, _initialization,
            _ui.CaptureComponentValues(CurrentTiles()), _message.Path, _cold, _retired, _coldBindingFailure, _lifetimes.ToArray(), _localTimerConstructor);
        Validate(saved, _source, _stack); return saved;
    }
    internal static void Validate(FalloutPlayerControlHudSnapshot saved, FalloutPlayerControlHudSource source, string stack)
    {
        source.Validate();
        if (saved.Source != source.Identity || saved.Stack != stack || saved.Process == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.UiFactory == Guid.Empty || saved.UiEpoch < 0 || saved.Sequence < 0 || saved.MenuMode is not (4 or 8) ||
            saved.Mode is < 1 or > 23 || saved.AllowedMask is not (0xffbf or 0xffff) || saved.MessageHandle < -1 ||
            saved.ForcedSubtitle > 1 || saved.VisibilityChanged > 1 || saved.PendingMessage == Guid.Empty || saved.Transitions is null ||
            saved.DisplayInitialized != (saved.Initialization?.Writes.Count == 3) || saved.InitializationReturned && !saved.DisplayInitialized ||
            saved.Failure is not null && string.IsNullOrWhiteSpace(saved.Failure))
            throw new InvalidDataException("Source HUD omitted an actual constructor, display, tile or failed field prefix.");
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.PreviousProcess == cold.CurrentProcess ||
            cold.CurrentProcess != saved.Process || cold.PreviousFactory == Guid.Empty || cold.PreviousFactory == cold.CurrentFactory ||
            cold.CurrentFactory != saved.Factory || cold.OriginProcess == Guid.Empty || cold.OriginFactory == Guid.Empty ||
            cold.OriginProcess == saved.Process || cold.OriginFactory == saved.Factory || cold.OriginDisplayFactory == Guid.Empty))
            throw new InvalidDataException("Source HUD lost its exact cold publication and original entered-call identity.");
        if (saved.ColdBindingFailure is not null && (saved.ColdHandoff is null || string.IsNullOrWhiteSpace(saved.ColdBindingFailure)))
            throw new InvalidDataException("Source HUD cold refusal omitted its actual historical constructor.");
        if (saved.Lifetimes is not { Count: > 0 } lifetimes ||
            lifetimes.Any(lifetime => lifetime.Process == Guid.Empty || lifetime.Factory == Guid.Empty) ||
            lifetimes.Select(lifetime => lifetime.Process).Distinct().Count() != lifetimes.Count ||
            lifetimes.Select(lifetime => lifetime.Factory).Distinct().Count() != lifetimes.Count ||
            lifetimes[^1] != new FalloutPlayerControlHudLifetime(saved.Process, saved.Factory) ||
            (saved.ColdHandoff is { } transfer ? lifetimes.Count < 2 ||
                lifetimes[0] != new FalloutPlayerControlHudLifetime(transfer.OriginProcess, transfer.OriginFactory) ||
                lifetimes[^2] != new FalloutPlayerControlHudLifetime(transfer.PreviousProcess, transfer.PreviousFactory) : lifetimes.Count != 1))
            throw new InvalidDataException("Source HUD lost actual constructor/cold process and factory lifetimes.");
        if (saved.LocalTimerConstructor is { } timer)
        {
            ValidateLocalTimerConstructor(timer);
            if (timer.Process != lifetimes[0].Process || timer.HudFactory != lifetimes[0].Factory)
                throw new InvalidDataException("Source HUD local timer replaced its original constructor process or owner.");
        }
        if (saved.Tiles is not { Count: 18 } || saved.MessagePath is null || !saved.MessagePath.StartsWith("HUDMainMenu/Messages/", StringComparison.Ordinal))
            throw new InvalidDataException("Source HUD omitted its retained actual XML component values.");
        var paths = new[] { FalloutPlayerControlHudSource.Menu }.Concat(FalloutPlayerControlHudSource.VisibilityTiles.Select(name =>
            FalloutPlayerControlHudSource.Menu + "/" + name)).Append(saved.MessagePath).ToArray();
        for (var index = 0; index < paths.Length; ++index)
        {
            FalloutUiComponentStore.ValidateComponentValues(saved.Tiles[index]);
            if (saved.Tiles[index].Path != paths[index]) throw new InvalidDataException("Source HUD changed its actual tile row order.");
        }
        foreach (var transition in saved.Transitions)
            if (transition.Identity == Guid.Empty || transition.Menu == Guid.Empty || transition.Removed ||
                transition.DurationBits != FalloutStandaloneMenuTransitions.Duration(transition.DurationBits))
                throw new InvalidDataException("Source HUD transition lost its actual menu, duration or current queue membership.");
        if (saved.Initialization is { } initial)
        {
            if (initial.Invocation == Guid.Empty || initial.Entered < 1 || initial.Changed < initial.Entered || initial.Changed > saved.Sequence ||
                string.IsNullOrWhiteSpace(initial.Step) || initial.Writes is null || initial.Writes.Count > 3 ||
                initial.Display != saved.Display || initial.Returned != saved.InitializationReturned ||
                initial.Returned != (initial.Step == "returned" && initial.Failure is null) ||
                initial.Failure is not null && initial.Failure != saved.Failure)
                throw new InvalidDataException("Source HUD initialization lost its real display read, stores or failed child prefix.");
            string[] traits = ["x", "y", "user7"]; long priorInitial = initial.Entered;
            for (var index = 0; index < initial.Writes.Count; ++index)
            {
                var write = initial.Writes[index];
                if (initial.Display is null || write.Path != FalloutPlayerControlHudSource.Menu + "/XPMeter" || write.Trait != traits[index] ||
                    write.Order <= priorInitial || write.Order > initial.Changed || write.UiRevision < 0 ||
                    index == 2 && write.ValueBits != initial.Writes[1].ValueBits)
                    throw new InvalidDataException("Source HUD initialization changed its exact ordered XP trait stores.");
                priorInitial = write.Order;
            }
        }
        else if (saved.Display is not null || saved.InitializationReturned || saved.DisplayInitialized || saved.Call is not null || saved.Sequence != 0)
            throw new InvalidDataException("Source HUD acquired display or command state without its original initialization.");
        if (saved.Display is { } display)
        {
            var (width, height) = FalloutPlayerControlDisplayState.LogicalDimensions(display.Width, display.Height);
            if (display.Process != (saved.ColdHandoff?.OriginProcess ?? saved.Process) || display.Factory == Guid.Empty ||
                saved.ColdHandoff is { } displayHandoff && display.Factory != displayHandoff.OriginDisplayFactory || display.Sequence < 0 || display.Wide > 1 ||
                display.LogicalWidthBits != BitConverter.SingleToUInt32Bits(width) || display.LogicalHeightBits != BitConverter.SingleToUInt32Bits(height))
                throw new InvalidDataException("Source HUD display read belongs to a foreign process or changed normalized field getter.");
        }
        if (saved.Call is not { } call)
        {
            if (saved.MenuMode != 4 || saved.Mode != 4 || saved.VisibilityMask is not null ||
                saved.Failure != saved.Initialization?.Failure || saved.InitializationReturned)
                throw new InvalidDataException("Uncalled source HUD acquired a returned menu or visibility update.");
            return;
        }
        if (call.Invocation == Guid.Empty || call.Process == Guid.Empty || call.HudFactory == Guid.Empty ||
            (call.Process == saved.Process ? call.HudFactory != saved.Factory :
                !lifetimes.Contains(new(call.Process, call.HudFactory))) ||
            call.Entered < 1 || call.Changed <= call.Entered || call.Changed > saved.Sequence ||
            string.IsNullOrWhiteSpace(call.Step) || call.Writes is null || call.Failure != saved.Failure ||
            call.Returned != (call.Failure is null && call.Step == "returned") ||
            call.Returned && (saved.Mode != call.Selected || saved.VisibilityMask is null || call.Writes.Count < 17))
            throw new InvalidDataException("Source HUD lost its ordered entered service, tile writes or actual return.");
        if (call.Allocation is { } allocation)
        {
            FalloutPlayerThreadContextState.ValidateKindScopes([allocation], FalloutPlayerThreadContextSource.Reviewed(source.Controls.Main));
            if (allocation.Process != call.Process || allocation.OwnerFactory != call.HudFactory || allocation.Invocation != call.Invocation ||
                call.DurationBits is null || call.Frame is not null && !allocation.Returned)
                throw new InvalidDataException("Source HUD transition does not belong to the actual entered menu allocation.");
        }
        else if (call.Frame is not null || call.DurationBits is not null && call.Failure is null)
            throw new InvalidDataException("Source HUD advance invented a returned allocator/TLS scope.");
        long prior = call.Entered;
        foreach (var write in call.Writes)
        {
            if (write.Order <= prior || write.Order > call.Changed || string.IsNullOrWhiteSpace(write.Path) ||
                string.IsNullOrWhiteSpace(write.Trait) || write.UiRevision < 0)
                throw new InvalidDataException("Source HUD lost exact ordered XML trait stores.");
            prior = write.Order;
        }
        if (call.Returned)
        {
            var visibility = call.Writes.TakeLast(FalloutPlayerControlHudSource.VisibilityTiles.Count).ToArray();
            for (var index = 0; index < visibility.Length; ++index)
                if (visibility[index].Path != FalloutPlayerControlHudSource.Menu + "/" + FalloutPlayerControlHudSource.VisibilityTiles[index] ||
                    visibility[index].Trait != "visible" || visibility[index].ValueBits != BitConverter.SingleToUInt32Bits(saved.VisibilityMask!.Value & (1u << index)))
                    throw new InvalidDataException("Source HUD visibility changed a tile, source bit or Float32 numeric transport.");
        }
    }

    private FalloutUiComponentBinding[] CurrentTiles() => [_root, .. _visibility, _message];

    private void Restore(FalloutPlayerControlHudSnapshot saved)
    {
        Validate(saved, _source, _stack);
        if (saved.Process == _process || saved.Factory == _factory || saved.Retired || saved.AllowedMask != _allowed)
            throw new InvalidDataException("Cold HUD reused its old source process or changed constructor declarations.");
        _cold = new(saved.Process, _process, saved.Factory, _factory,
            saved.ColdHandoff?.OriginProcess ?? saved.Process, saved.ColdHandoff?.OriginFactory ?? saved.Factory,
            saved.ColdHandoff?.OriginDisplayFactory ?? saved.Display?.Factory);
        _lifetimes.AddRange(saved.Lifetimes!); _lifetimes.Add(new(_process, _factory));
        _sequence = saved.Sequence; _menuMode = saved.MenuMode; _mode = saved.Mode; _visibilityMask = saved.VisibilityMask;
        _messageHandle = saved.MessageHandle; _subtitle = saved.ForcedSubtitle; _visibilityChanged = saved.VisibilityChanged;
        _pendingMessage = saved.PendingMessage; _displayInitialized = saved.DisplayInitialized;
        _initializationReturned = saved.InitializationReturned; _display = saved.Display; _initialization = saved.Initialization;
        _call = saved.Call; _failure = saved.Failure; _coldBindingFailure = saved.ColdBindingFailure;
        _localTimerConstructor = saved.LocalTimerConstructor;
        if (_call is not null) _invocations.Add(_call.Invocation);
        if (saved.Transitions.Count != 0)
        {
            _historicalTransitions = saved.Transitions.ToArray();
            _coldBindingFailure ??= "source-HUD-cold-current-menu-transition-queue-unbound";
        }
        var current = CurrentTiles();
        try
        {
            for (var index = 0; index < current.Length; ++index) _ui.RestoreComponentValues(current[index], saved.Tiles![index]);
        }
        catch (Exception error) { _coldBindingFailure ??= error.Message; throw; }
        // Historical calls retain their actual source process, TLS scope and
        // XML-write order. No input-type call, clock query or command is replayed.
    }

    internal static void ValidateLocalTimerConstructor(FalloutPlayerControlHudTimerConstructor timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        if (timer.Process == Guid.Empty || timer.HudFactory == Guid.Empty || timer.PauseCount != 0 ||
            timer.FixedMillisecondsBits != 0 || timer.FractionalMillisecondsBits != 0 ||
            timer.DeltaSecondsBits != 0 || timer.CachedMilliseconds != 0 || timer.StatusWord != 0)
            throw new InvalidDataException("Source HUD local timer omitted its independent zero stores and actual wall tick construction.");
    }
}
