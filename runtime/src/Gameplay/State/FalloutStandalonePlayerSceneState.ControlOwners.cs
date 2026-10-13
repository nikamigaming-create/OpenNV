using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerControlHudState? _controlHud;
    private FalloutPlayerControlDisplayState? _controlDisplay;
    private FalloutPlayerControlHudSnapshot? _historicalControlHud;
    private FalloutPlayerControlDisplaySnapshot? _historicalControlDisplay;
    private FalloutPlayerReferenceOrientationOwner? _referenceOrientation;
    private FalloutPlayerReferenceOrientationSnapshot? _historicalReferenceOrientation;

    internal FalloutPlayerControlHudSnapshot? PreparedControlHudRestore => _historicalControlHud;
    internal FalloutPlayerControlDisplaySnapshot? PreparedControlDisplayRestore => _historicalControlDisplay;

    internal void BindControlHud(FalloutPlayerControlHudState hud, FalloutPlayerControlDisplayState display)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(hud); ArgumentNullException.ThrowIfNull(display);
        var currentHud = hud.Capture(); var currentDisplay = display.Capture();
        if (_controlHud is not null || hud.Source.Controls != FalloutPlayerMovementControlSource.Reviewed(Source.Main) ||
            hud.Process != _process || hud.Stack != _stack || currentDisplay.Process != _process ||
            currentDisplay.Source != hud.Source.Identity || currentDisplay.Stack != _stack)
            throw new InvalidOperationException("Player scene changed its actual source HUD/display constructors.");
        _controlHud = hud; _controlDisplay = display; _thread ??= Environment.CurrentManagedThreadId;
        _ = currentHud;
    }

    internal void BindPlayerReferenceOrientation(FalloutPlayerReferenceOrientationOwner owner)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(owner); owner.RequireCurrent();
        if (_referenceOrientation is not null || owner.Source != FalloutPlayerMovementControlSource.Reviewed(Source.Main) ||
            owner.Stack != _stack || owner.Process != _process || owner.Factory == Guid.Empty)
            throw new InvalidOperationException("Player scene changed its actual retained source reference orientation owner.");
        owner.RequireControlFields(_controlFields);
        _movementControl?.BindOrientation(owner); _referenceOrientation = owner; _thread ??= Environment.CurrentManagedThreadId;
    }

    internal void PublishPlayerReferencePlacement(Guid invocation, string producer, FalloutReferencePlacementIngress ingress,
        uint xBits, uint yBits, uint zBits)
    {
        RequireIdle();
        var owner = _referenceOrientation ?? throw new NotSupportedException("source-Player-current-reference-placement-owner-unbound");
        owner.RequireControlFields(_controlFields);
        owner.PublishPlacement(invocation, producer, ingress, xBits, yBits, zBits);
    }

    internal void RequireMovementOrientationInvocation(Guid main, long ordinal)
    {
        RequireThread();
        if (_retired || !_writing || _movementCall is not { Step: "movement-control-child", Failure: null } movement ||
            movement.Main != main || movement.MainOrdinal != ordinal ||
            _threadContextEntry is not { ScopeEntered: true, InitializerReturned: true } context ||
            context.Process != _process || context.Main != main || context.MainOrdinal != ordinal ||
            context.Thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Reference orientation query escaped the actual entered Player movement/Main scope.");
    }

    private static void ValidateControlOwners(FalloutStandalonePlayerSceneSnapshot saved)
    {
        var source = FalloutPlayerControlHudSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main));
        if ((saved.ControlHud is null) != (saved.ControlDisplay is null))
            throw new InvalidDataException("Player scene omitted one actual source HUD/display constructor.");
        if (saved.ControlHud is { } hud && saved.ControlDisplay is { } display)
        {
            FalloutPlayerControlHudState.Validate(hud, source, saved.Stack);
            FalloutPlayerControlDisplayState.Validate(display, source, saved.Stack);
            if (hud.Process != display.Process || hud.Process != saved.CapturedProcess && saved.ColdHandoff is null ||
                hud.Display is { } read && (read.Factory != (hud.ColdHandoff?.OriginDisplayFactory ?? display.Factory) || read.Sequence > display.Sequence))
                throw new InvalidDataException("Player HUD/display retained a foreign current/cold source field owner.");
        }
        if (saved.ReferenceOrientation is { } orientation)
        {
            FalloutPlayerReferenceOrientationOwner.Validate(orientation);
            if (orientation.Source.Controls != source.Controls || orientation.Stack != saved.Stack ||
                orientation.CapturedProcess != saved.CapturedProcess && saved.ColdHandoff is null)
                throw new InvalidDataException("Player reference orientation came from another source process/stack.");
        }
    }
}
