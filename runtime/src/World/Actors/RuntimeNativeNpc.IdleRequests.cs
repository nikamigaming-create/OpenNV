using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private sealed class IdleRequest(FalloutPluginStack stack, FalloutFormKey idle, string owner)
    {
        internal FalloutPluginStack Stack { get; } = stack;
        internal FalloutFormKey Idle { get; } = idle;
        internal string Owner { get; } = owner;
        internal int State { get; private set; } = 1;
        internal FalloutNifControllerSequence? Sequence { get; private set; }
        internal int SequenceState { get; private set; }
        internal string? Resource { get; private set; }
        internal string? Sha256 { get; private set; }
        internal FalloutActorPackageIdleAnimation? Saved { get; init; }
        internal bool RestoreCompletePose { get; init; }

        internal void Activate(RuntimeNativeNifAnimation animation, string resource)
        {
            if (State != 1 || Sequence is not null) throw new InvalidOperationException("Idle request is not pending activation.");
            Sequence = animation.Sequence; Resource = resource; Sha256 = animation.Source.Sha256;
            SequenceState = 1; State = 2;
        }

        internal void EaseIn() => SequenceState = 2;
        internal void SnapshotBlend() => SequenceState = 5;
        internal void FinishTransition() { if (SequenceState is 2 or 5) SequenceState = 1; }

        internal void EaseOut() => SequenceState = Sequence is not null && SequenceState != 0 ? 3 : 0;

        internal void Inactivate()
        {
            SequenceState = 0; State = 3;
            // Keep the actual immutable sequence identity for request readiness;
            // its pose, ANIO and sound resources belong to separate owners.
        }

        internal void RestoreTerminal(FalloutNifControllerSequence sequence, string resource, string hash)
        { Sequence = sequence; Resource = resource; Sha256 = hash; Inactivate(); }
    }

    private sealed record OutgoingIdle(IdleRequest Request, RuntimeNativeNifAnimation Animation,
        FalloutIdleAnimationPlayback Clock, FalloutIdleAnimationData Timing, long Revision,
        IReadOnlyList<Node3D> Objects, IReadOnlyList<FalloutActorIdleObjectBinding>? Bindings,
        FalloutIdleDeactivation Deactivation, FalloutFurnitureClipSnapshot? Incoming);

    private IdleRequest? _currentIdleRequest, _queuedIdleRequest;
    private readonly List<OutgoingIdle> _outgoingIdles = [];
    private FalloutIdleDeactivation? _idleEaseIn;
    private FalloutIdleDeactivation? _currentIdleEaseOut;
    private bool _idleNaturalEndPending;
    private bool _idleRetirementPending;
    private Func<bool>? _idleRequestsReady;
    private Func<FalloutActorIdleRequestsSnapshot?>? _idleRequestsCapture;
    private bool _restoringIdleRequests;
    private readonly List<(FalloutPluginStack Stack, FalloutFormKey Form, string Owner,
        FalloutActorPackageIdleAnimation? Saved, bool CompletePose)> _deferredIdleInputs = [];
    private bool IdleRequestsReady => _queuedIdleRequest is null &&
        (_currentIdleRequest is null || _currentIdleRequest is { State: 3, Sequence: not null });

    private void PlayIdle(FalloutPluginStack stack, FalloutFormKey form, string owner,
        FalloutActorPackageIdleAnimation? saved = null, bool restoreCompletePose = false)
    {
        if (_restoringIdleRequests)
        { _deferredIdleInputs.Add((stack, form, owner, saved, restoreCompletePose)); return; }
        // Retiring a pending request does not activate or redraw it. The actual
        // source bindings/clocks are acquired only on queued promotion.
        _queuedIdleRequest?.Inactivate();
        _queuedIdleRequest = new(stack, form, owner) { Saved = saved, RestoreCompletePose = restoreCompletePose };
        ProcessQueuedIdle(stack);
    }

    private void ProcessQueuedIdle(FalloutPluginStack? stack = null)
    {
        if (_queuedIdleRequest is not { } request || _currentIdleRequest?.SequenceState is 2 or 5) return;
        ActivateIdleRequest(stack ?? _aiStack ?? throw new InvalidOperationException("Queued idle has no source stack."),
            request.Idle, request.Owner, request, request.Saved, request.RestoreCompletePose);
    }

    private FalloutIdleTransition.Settings IdleTransitionSettings()
    {
        var settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Idle requests have no owned installation settings."));
        return new(settings.Number("General", "fAnimationDefaultBlend"), settings.Number("General", "fAnimationMult"));
    }

    private void RetainOutgoingIdle(RuntimeNativeNifAnimation? incoming = null)
    {
        if (_animation is not { } animation) return;
        var request = _currentIdleRequest ?? throw new InvalidOperationException("Active idle has no current request.");
        var source = FalloutIdleGroupTextKeys.Read(animation.Sequence.Name, animation.TextKeys).Blend;
        var next = incoming is null ? (FalloutIdleTransition.Metadata?)null :
            FalloutIdleGroupTextKeys.Read(incoming.Sequence.Name, incoming.TextKeys).Blend;
        var fade = incoming is null && _currentIdleEaseOut is not null ? _currentIdleEaseOut :
            new FalloutIdleDeactivation(source, IdleTransitionSettings(), next);
        _currentIdleEaseOut = null;
        _idleEaseIn = incoming is null || fade.Complete ? null : new(source, IdleTransitionSettings(), next);
        request.EaseOut();
        var incomingSource = incoming is null ? null : new FalloutFurnitureClipSnapshot(_queuedIdleRequest!.Idle,
            FalloutActorFurnitureContinuation.RecordHash(_queuedIdleRequest.Stack.GetEffective(_queuedIdleRequest.Idle)),
            FalloutActorIdleSource.Resolve(_queuedIdleRequest.Stack, _queuedIdleRequest.Stack.GetEffective(_queuedIdleRequest.Idle)).AnimationPath, incoming.Source.Sha256);
        if (fade.Complete)
        {
            request.Inactivate();
            foreach (var item in _animationObjects) item.Free();
        }
        else _outgoingIdles.Add(new(request, animation, _idlePlayback ?? throw new InvalidOperationException("Idle has no playback clock."),
            _idleData!, _idleRevision, _animationObjects.ToArray(), _idleObjectBindings, fade, incomingSource));
        _animationObjects.Clear(); _idleObjectBindings = [];
        _animation = null; _idlePlayback = null; _idleData = null; _idleForm = null; _idleOwner = null;
        _idleAnimationResource = null; _idleAnimationSha256 = null; _currentIdleRequest = null;
    }

    private void AdvanceOutgoingIdles(double delta)
    {
        _idleEaseIn?.Advance(delta);
        if (_idleEaseIn?.Complete == true) { _idleEaseIn = null; _currentIdleRequest?.FinishTransition(); }
        _currentIdleEaseOut?.Advance(delta);
        if (_currentIdleEaseOut?.Complete == true) CompleteCurrentIdle(publishBasePose: false);
        foreach (var outgoing in _outgoingIdles.ToArray())
        {
            outgoing.Deactivation.Advance(delta);
            if (outgoing.Deactivation.Complete)
            {
                outgoing.Request.Inactivate();
                foreach (var item in outgoing.Objects) if (GodotObject.IsInstanceValid(item)) item.Free();
                _outgoingIdles.Remove(outgoing);
                continue;
            }
            outgoing.Clock.Advance(delta, interval => PublishIdleTextKeys(outgoing.Animation, interval, outgoing.Request.Idle, outgoing.Clock));
        }
    }

    private void CancelOutgoingIdles()
    {
        foreach (var outgoing in _outgoingIdles)
        {
            outgoing.Request.Inactivate();
            foreach (var item in outgoing.Objects) if (GodotObject.IsInstanceValid(item)) item.Free();
        }
        _outgoingIdles.Clear(); _queuedIdleRequest = null; _idleEaseIn = null;
    }

    private void RequestIdleRetirement() => _idleRetirementPending = true;

    private void ConsumeIdleRetirement()
    {
        var pending = _idleRetirementPending;
        _idleRetirementPending = false;
        if (!pending) return;
        if (_currentIdleRequest?.SequenceState is 2 or 5 || _queuedIdleRequest is { Sequence: null } ||
            _queuedIdleRequest?.SequenceState is 2 or 5)
        { _idleRetirementPending = true; return; }
        RetainOutgoingIdle();
        _currentIdleRequest = null;
    }

    private void PublishIdleRequestLayers()
    {
        var layers = new List<(RuntimeNativeNifAnimation Animation, float Time, float Ease)>();
        if (Combat?.PackageOwnsPose != true && _baseAnimation is { } background)
            layers.Add((background, _baseAnimationSeconds, 1));
        foreach (var outgoing in _outgoingIdles)
            layers.Add((outgoing.Animation, outgoing.Clock.SourceSeconds, outgoing.Deactivation.Weight));
        if (_animation is { } current)
            layers.Add((current, _animationSeconds, _currentIdleEaseOut?.Weight ?? (_idleEaseIn is { } incoming ? 1 - incoming.Weight : 1)));
        RuntimeNativeNifAnimation.ApplyWeightedLayers(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(layers));
    }

    private void BeginCurrentIdleDeactivation()
    {
        if (_animation is not { } animation || _currentIdleEaseOut is not null) return;
        _idleNaturalEndPending = false; _idleEaseIn = null;
        _currentIdleEaseOut = new(FalloutIdleGroupTextKeys.Read(animation.Sequence.Name, animation.TextKeys).Blend,
            IdleTransitionSettings());
        _currentIdleRequest!.EaseOut();
        // The source End handler runs during this animation update. Anchor its
        // newly requested deactivation now; playback itself is not truncated.
        _currentIdleEaseOut.Advance(0);
        if (_currentIdleEaseOut.Complete) CompleteCurrentIdle(publishBasePose: false);
    }

    private void AdvanceInterruptedMarkerIdle(double delta)
    {
        if (_animation is not { } animation || _idlePlayback is not { } clock) return;
        clock.Advance(delta, interval => PublishIdleTextKeys(animation, interval));
        _animationSeconds = clock.SourceSeconds;
        if (_idleNaturalEndPending) BeginCurrentIdleDeactivation();
    }
}
