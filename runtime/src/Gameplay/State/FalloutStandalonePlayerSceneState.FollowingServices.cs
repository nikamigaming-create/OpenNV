using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutStandaloneFollowingServicesStep { MovementMerge, TeleportFade, Returned, Failed }
internal sealed record FalloutStandaloneFollowingServicesCall(Guid Main, long MainOrdinal, Guid Invocation,
    long Entered, long Changed, FalloutStandaloneFollowingServicesStep Step,
    FalloutStandaloneFollowingServicesStep? FailedAt, byte InitialMovementByte, bool? ControlResult,
    uint? MovementWord, bool? TeleportSkipped, FalloutProcessTeleportFadeUpdate? Teleport,
    string? Failure);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerOpacityState? _playerOpacity;
    private FalloutPlayerOpacitySnapshot? _historicalPlayerOpacity;
    private FalloutStandaloneFollowingServicesCall? _followingServicesCall;
    private Func<Guid, long, Guid, FalloutPlayerOpacityState, FalloutProcessTeleportFadeUpdate?>? _updateTeleportFade;
    private Func<float>? _followingReadScaledTimer;

    internal FalloutPlayerOpacitySnapshot? PreparedPlayerOpacityRestore => _historicalPlayerOpacity;
    internal FalloutPlayerOpacityState? CurrentPlayerOpacity => _playerOpacity;

    internal void ObservePlayerTeleportFade(FalloutProcessTeleportFadeUpdate receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var call = _followingServicesCall ?? throw new InvalidOperationException("Teleport receipt has no actual Player caller.");
        RequireOpacityInvocation(call.Main, call.MainOrdinal);
        if (call.Step != FalloutStandaloneFollowingServicesStep.TeleportFade || receipt.Trace.Invocation != call.Invocation ||
            receipt.Before.Stack != _stack || !receipt.Before.Actor.EnginePlayer)
            throw new InvalidOperationException("Teleport receipt changed its actual entered Player/source invocation.");
        _followingServicesCall = call with { Teleport = receipt, Changed = Next() };
    }

    internal void BindPlayerOpacity(FalloutPlayerOpacityState owner,
        Func<Guid, long, Guid, FalloutPlayerOpacityState, FalloutProcessTeleportFadeUpdate?> update)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(update);
        if (_playerOpacity is not null || owner.Process != _process || owner.Stack != _stack ||
            owner.Source != FalloutPlayerOpacitySource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(Source.Main)))
            throw new InvalidOperationException("Player opacity changed its actual source/process constructor.");
        _playerOpacity = owner; _updateTeleportFade = update; _thread ??= Environment.CurrentManagedThreadId;
    }

    internal uint ReadOpacityScaledTimerBits()
    {
        var (main, ordinal) = RequireCurrentOpacityInvocation();
        if (_followingServicesCall is not { Step: FalloutStandaloneFollowingServicesStep.TeleportFade } call ||
            call.Main != main || call.MainOrdinal != ordinal || _followingReadScaledTimer is null)
            throw new InvalidOperationException("Opacity timer escaped its actual entered Main caller.");
        return BitConverter.SingleToUInt32Bits(_followingReadScaledTimer());
    }

    private void UpdateFollowingServices(Guid main, long ordinal, Func<float>? readScaledTimer)
    {
        RequireOpacityInvocation(main, ordinal);
        var entered = Next();
        _followingServicesCall = new(main, ordinal, Guid.NewGuid(), entered, entered,
            FalloutStandaloneFollowingServicesStep.MovementMerge, null, 0, null, null, null, null, null);
        try
        {
            if (_movementCall?.Control?.Call is not { Returned: true, Failure: null, Result: not null } controls ||
                controls.Main != main || controls.MainOrdinal != ordinal)
                throw new InvalidDataException("Following Player services lost the actual control Boolean return.");
            // The source initializes this independent caller-local byte to zero;
            // no preceding child writes or takes its address. A nonzero control
            // return publishes one, then the caller stores the separate word.
            var movement = controls.Result.Value ? 1U : _followingServicesCall.InitialMovementByte;
            _followingServicesCall = _followingServicesCall with
            { ControlResult = controls.Result, MovementWord = movement, Changed = Next() };
            var opacity = _playerOpacity ??
                throw new NotSupportedException("source-Player-following-movement-frame-services-unbound");
            var update = _updateTeleportFade ??
                throw new NotSupportedException("source-Player-current-High-teleport-update-owner-unbound");
            _followingServicesCall = _followingServicesCall with
            { Step = FalloutStandaloneFollowingServicesStep.TeleportFade, Changed = Next() };
            _followingReadScaledTimer = readScaledTimer;
            var teleport = update(main, ordinal, _followingServicesCall.Invocation, opacity);
            _followingServicesCall = _followingServicesCall with
            { Teleport = teleport, TeleportSkipped = teleport is null, Step = FalloutStandaloneFollowingServicesStep.Returned, Changed = Next() };
        }
        catch (Exception error)
        {
            _followingServicesCall = _followingServicesCall with
            {
                FailedAt = _followingServicesCall.Step,
                Step = FalloutStandaloneFollowingServicesStep.Failed,
                Changed = Next(),
                Failure = error.Message
            };
            throw;
        }
        finally { _followingReadScaledTimer = null; }
    }

    private static void ValidateFollowingServices(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.Opacity is { } opacity)
        {
            FalloutPlayerOpacityState.Validate(opacity);
            if (opacity.Stack != saved.Stack || opacity.Source.Controls.Main != saved.Source.Main ||
                !ChildBelongsToParentProcessLineage(saved, opacity.CapturedProcess) ||
                opacity.RestoredProcess is { } previous && !ChildBelongsToParentProcessLineage(saved, previous))
                throw new InvalidDataException("Player scene retained another opacity source/process owner.");
        }
        if (saved.WeaponContinuation is { } weapon && !ChildBelongsToParentProcessLineage(saved, weapon.PlayerProcess))
            throw new InvalidDataException("Player weapon services retained another parent process lifetime.");
        if (saved.FollowingServices is not { } call) return;
        if (call.Teleport is { } teleport)
        {
            FalloutProcessTeleportFadeReceipt.Validate(teleport, saved.Stack, saved.Source.Main.EngineSha256, enginePlayer: true);
            // Borrow the actual earlier process query from this Main invocation.
            // A standalone snapshot cannot manufacture a World/common entry.
            if (saved.ActorProcessQuery is not { Result: { } process, AnimationReturned: true, Failure: null } query ||
                query.Main != call.Main || query.MainOrdinal != call.MainOrdinal ||
                teleport.Before.Process != process.Process || teleport.Before.Actor != process.Actor ||
                teleport.Before.Epoch != process.Epoch || teleport.Before.Level != process.Level ||
                teleport.Before.GameplayOwnership != process.GameplayOwnership || teleport.Before.Changed < process.Changed)
                throw new InvalidDataException("Following teleport receipt changed the actual current High process queried by its caller.");
        }
        var movement = saved.MovementCall;
        if (call.Main == Guid.Empty || call.MainOrdinal < 1 || call.Invocation == Guid.Empty || call.Entered < 1 ||
            call.Changed < call.Entered || call.Changed > saved.Sequence || !Enum.IsDefined(call.Step) ||
            call.InitialMovementByte != 0 || movement is not { Returned: true, Failure: null } ||
            movement.Main != call.Main || movement.MainOrdinal != call.MainOrdinal || call.Entered <= movement.Changed ||
            call.ControlResult is { } result && (movement.Control?.Call?.Result != result || call.MovementWord != (result ? 1U : 0U)) ||
            call.Step == FalloutStandaloneFollowingServicesStep.Returned &&
                (call.ControlResult is null || call.TeleportSkipped is null || call.TeleportSkipped == (call.Teleport is not null)) ||
            call.Teleport is { Trace: { } trace } && (trace.Invocation != call.Invocation ||
                (trace.Step == FalloutActorTeleportFadeStep.Failed
                    ? call.Step != FalloutStandaloneFollowingServicesStep.Failed || trace.Failure != call.Failure
                    : trace.Step != FalloutActorTeleportFadeStep.Returned)) ||
            (call.Step == FalloutStandaloneFollowingServicesStep.Failed) != (call.Failure is not null) ||
            call.Failure is not null && (call.FailedAt is null || string.IsNullOrWhiteSpace(call.Failure)) ||
            call.FailedAt is { } failed && (!Enum.IsDefined(failed) || failed is FalloutStandaloneFollowingServicesStep.Failed or FalloutStandaloneFollowingServicesStep.Returned))
            throw new InvalidDataException("Following Player services lost their actual movement/fade caller prefix.");
    }
}
