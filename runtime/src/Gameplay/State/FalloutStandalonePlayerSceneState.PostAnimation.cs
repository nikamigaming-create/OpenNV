using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerPostAnimationPrefixStep
{ InitialView, PreparationBeforeToggle, CurrentView, FirstToggle, PreparationAfterToggle, Status, Failed }
internal sealed record FalloutPlayerPostAnimationPrefixCall(string Source, FalloutPlayerLateFrameInvocation Frame,
    Guid Consumer, FalloutPlayerPostAnimationPrefixStep Step, FalloutPlayerPostAnimationPrefixStep? FailedAt = null,
    FalloutPlayerViewRead? InitialView = null, FalloutPlayerViewRead? CurrentView = null,
    FalloutPlayerViewStore? FirstToggle = null, FalloutPlayerAnimationPreparationCall? Preparation = null,
    string? Failure = null);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerAnimationPreparationState? _lateAnimationPreparation;
    private Func<FalloutPlayerLateFrameInvocation, FalloutPlayerAnimationPreparationCall>? _prepareLateAnimation;
    internal void BindLateAnimationPreparation(FalloutPlayerAnimationPreparationState preparation,
        Func<FalloutPlayerLateFrameInvocation, FalloutPlayerAnimationPreparationCall> prepare)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(preparation); ArgumentNullException.ThrowIfNull(prepare);
        if (_lateAnimationPreparation is not null || preparation.Source.Tail.Scene != Source || preparation.Stack != _stack)
            throw new InvalidOperationException("Player preparation replaced its actual Scene/source owner.");
        if (_historicalLateFrame?.LastCall?.PostAnimation is not null)
            throw new NotSupportedException("source-Player-cold-post-animation-owner-rebinding-unowned");
        _lateAnimationPreparation = preparation; _prepareLateAnimation = prepare;
    }
    internal void RequirePostAnimationInvocation(FalloutPlayerLateFrameInvocation frame)
    {
        RequireThread();
        if (!_writing || _retired || _lateFrameCall is not { Step: FalloutPlayerLateFrameStep.AnimationPreparation, Failure: null } call ||
            call.Invocation != frame || call.ReturnTail is not { Returned: true, Failure: null } ||
            frame.PlayerProcess != _process || frame.PlayerConstructor != _constructor || frame.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player post-animation child lost its actual returned updater/frame.");
    }
    private void UpdatePostAnimationPrefix(FalloutPlayerLateFrameInvocation frame, Action<FalloutPlayerPostAnimationPrefixCall> observe)
    {
        RequirePostAnimationInvocation(frame);
        var preparation = _lateAnimationPreparation ?? throw new NotSupportedException("source-Player-post-updater-animation-preparation-current-owner-unbound");
        var prepare = _prepareLateAnimation ?? throw new NotSupportedException("source-Player-animation-preparation-current-delegate-unbound");
        var source = preparation.Source;
        var call = new FalloutPlayerPostAnimationPrefixCall(source.Identity, frame, preparation.Factory, FalloutPlayerPostAnimationPrefixStep.InitialView);
        observe(call);
        try
        {
            call = call with { InitialView = ReadTailView(source, frame, RequirePostAnimationInvocation) }; observe(call);
            if (call.InitialView.Value != 0) Prepare(FalloutPlayerPostAnimationPrefixStep.PreparationBeforeToggle);
            Step(FalloutPlayerPostAnimationPrefixStep.CurrentView);
            call = call with { CurrentView = ReadTailView(source, frame, RequirePostAnimationInvocation) }; observe(call);
            Step(FalloutPlayerPostAnimationPrefixStep.FirstToggle);
            call = call with
            {
                FirstToggle = StoreFirstTailViewToggle(source, frame, call.Consumer, call.InitialView,
                call.CurrentView, RequirePostAnimationInvocation)
            }; observe(call);
            if (call.CurrentView.Value == 0) Prepare(FalloutPlayerPostAnimationPrefixStep.PreparationAfterToggle);
            Step(FalloutPlayerPostAnimationPrefixStep.Status);
            throw new NotSupportedException("source-Player-post-animation-first-status-current-owner-unbound");
        }
        catch (Exception error)
        {
            call = call with { FailedAt = call.Step, Step = FalloutPlayerPostAnimationPrefixStep.Failed, Failure = error.Message };
            observe(call); throw;
        }
        void Step(FalloutPlayerPostAnimationPrefixStep step)
        { RequirePostAnimationInvocation(frame); call = call with { Step = step }; observe(call); }
        void Prepare(FalloutPlayerPostAnimationPrefixStep step)
        {
            Step(step);
            try { prepare(frame); }
            finally { call = call with { Preparation = preparation.LastCall?.Frame == frame ? preparation.LastCall : null }; observe(call); }
        }
    }
    internal static void ValidatePostAnimationPrefix(FalloutPlayerPostAnimationPrefixCall call,
        FalloutPlayerLateFrameCall parent, FalloutPlayerReturnTailSource tail, string stack)
    {
        var source = FalloutPlayerPostAnimationSource.Reviewed(tail);
        if (parent.FailedAt != FalloutPlayerLateFrameStep.AnimationPreparation || call.Frame != parent.Invocation ||
            call.Source != source.Identity || call.Consumer == Guid.Empty || call.Step != FalloutPlayerPostAnimationPrefixStep.Failed ||
            call.FailedAt is null or FalloutPlayerPostAnimationPrefixStep.Failed || !Enum.IsDefined(call.FailedAt.Value) ||
            string.IsNullOrWhiteSpace(call.Failure) || call.Failure != parent.Failure)
            throw new InvalidDataException("Player post-animation prefix lost its actual reached frame/failure.");
        var at = call.FailedAt.Value;
        if (at > FalloutPlayerPostAnimationPrefixStep.InitialView && call.InitialView is null ||
            at > FalloutPlayerPostAnimationPrefixStep.CurrentView && call.CurrentView is null ||
            at > FalloutPlayerPostAnimationPrefixStep.FirstToggle && call.FirstToggle is null ||
            at < FalloutPlayerPostAnimationPrefixStep.CurrentView && call.CurrentView is not null ||
            at < FalloutPlayerPostAnimationPrefixStep.FirstToggle && call.FirstToggle is not null)
            throw new InvalidDataException("Player post-animation prefix omitted or invented a reached view child.");
        foreach (var read in new[] { call.InitialView, call.CurrentView }.OfType<FalloutPlayerViewRead>())
            if (read.Frame != call.Frame || read.Load is not null || read.Source != tail.Scene.Identity || read.Stack != stack ||
                read.Process != call.Frame.PlayerProcess || read.Constructor != call.Frame.PlayerConstructor)
                throw new InvalidDataException("Player post-animation read borrowed another Scene field.");
        if (call.FirstToggle is { } toggle && (toggle.Consumer != call.Consumer || toggle.Read != call.CurrentView ||
            toggle.Writer != FalloutPlayerViewWriter.FirstTailToggle || toggle.After != (call.CurrentView!.Value == 0 ? 1 : 0)))
            throw new InvalidDataException("Player post-animation prefix changed its actual fresh-read toggle.");
        if (call.Preparation is { } preparation)
        {
            FalloutPlayerAnimationPreparationState.Validate(preparation, source, stack);
            if (preparation.Frame != call.Frame || preparation.Factory != call.Consumer ||
                (call.InitialView?.Value != 0 ? at < FalloutPlayerPostAnimationPrefixStep.PreparationBeforeToggle :
                    at < FalloutPlayerPostAnimationPrefixStep.PreparationAfterToggle) ||
                !preparation.Returned && preparation.Failure != call.Failure)
                throw new InvalidDataException("Player post-animation prefix replaced its selected preparation child.");
        }
        if (call.InitialView is { Value: not 0 } && at > FalloutPlayerPostAnimationPrefixStep.PreparationBeforeToggle &&
                call.Preparation is not { Returned: true } ||
            call.CurrentView is { Value: 0 } && at > FalloutPlayerPostAnimationPrefixStep.PreparationAfterToggle &&
                call.Preparation is not { Returned: true })
            throw new InvalidDataException("Player post-animation prefix passed an unreturned mandatory preparation.");
    }
}
