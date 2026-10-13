using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerActorDistanceCall(Guid Main, long MainOrdinal, long Entered, long Changed,
    FalloutPlayerActorDistanceTimer Before, FalloutPlayerActorDistanceTimer After,
    uint? ArgumentBits, uint? SettingBits, FalloutStandaloneActorDistanceSort? Sort, bool Returned, string? Failure);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerActorDistanceTimer _actorDistanceTimer = FalloutPlayerActorDistanceTimer.Allocated;
    private FalloutPlayerActorDistanceCall? _actorDistanceCall;
    private FalloutStandaloneInterfaceState? _distanceInterface;
    private Func<int>? _distanceLimit;
    private Func<FalloutFormKey, float>? _readActorDistance;
    private Func<float>? _readDistanceTimerSetting;
    internal void BindActorDistances(FalloutStandaloneInterfaceState manager, Func<int> readLimit,
        Func<FalloutFormKey, float> readDistance, Func<float> readTimerSetting)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(manager); ArgumentNullException.ThrowIfNull(readLimit);
        ArgumentNullException.ThrowIfNull(readDistance); ArgumentNullException.ThrowIfNull(readTimerSetting);
        if (_distanceInterface is not null || manager.Process != _process || manager.Source.Main != Source.Main || manager.Stack != _stack)
            throw new InvalidOperationException("Player actor sorting changed its actual shared interface/Main owner.");
        _distanceInterface = manager; _distanceLimit = readLimit; _readActorDistance = readDistance; _readDistanceTimerSetting = readTimerSetting;
    }
    private void UpdateActorDistances(Guid main, long ordinal)
    {
        var entered = Next(); var fault = _reentry;
        _actorDistanceCall = new(main, ordinal, entered, entered, _actorDistanceTimer, _actorDistanceTimer, null, null, null, false, null);
        try
        {
            if (_actorDistanceTimer.SubtractsArgument)
            {
                var argument = _clock?.ArgumentBits ?? throw new InvalidOperationException("Actor-distance timer lost its actual scene argument.");
                _actorDistanceTimer = _actorDistanceTimer.Subtract(BitConverter.UInt32BitsToSingle(argument));
                _actorDistanceCall = _actorDistanceCall with { ArgumentBits = argument, After = _actorDistanceTimer, Changed = Next() };
            }
            else
            {
                var manager = _distanceInterface ?? throw new NotSupportedException("source-Player-distance-sort-current-menu-consumer-unbound");
                var limit = _distanceLimit!(); Check();
                try { _ = manager.SortActorDistances(limit, key => { var distance = _readActorDistance!(key); Check(); return distance; }); }
                finally
                {
                    if (manager.LastActorDistanceSort is { } sort)
                        _actorDistanceCall = _actorDistanceCall with { Sort = sort, Changed = Next() };
                }
                Check();
                var setting = _readDistanceTimerSetting!(); Check();
                _actorDistanceTimer = _actorDistanceTimer.Reset(setting);
                _actorDistanceCall = _actorDistanceCall with
                { After = _actorDistanceTimer, SettingBits = BitConverter.SingleToUInt32Bits(setting), Changed = Next() };
            }
            _actorDistanceCall = _actorDistanceCall with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        { _actorDistanceCall = _actorDistanceCall with { After = _actorDistanceTimer, Failure = error.Message, Changed = Next() }; throw; }
        void Check()
        {
            RequireThread();
            if (_reentry != fault) throw new InvalidOperationException("Actor-distance child swallowed a Player owner reentry refusal.");
        }
    }
    private static void ValidateActorDistanceCall(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.ActorDistanceTimer is not { Stores: >= 0 } timer || timer.Stores == 0 && timer != FalloutPlayerActorDistanceTimer.Allocated)
            throw new InvalidDataException("Player actor-distance timer lost its explicit managed allocation or stored bits.");
        if (saved.ActorDistanceCall is not { } call)
        {
            if (timer != FalloutPlayerActorDistanceTimer.Allocated || saved.PostActionsCall is { Returned: true } &&
                saved.Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player actor-distance timer omitted its entered source writer.");
            return;
        }
        if (saved.PostActionsCall is not { Returned: true } actions || call.Main != actions.Main || call.MainOrdinal != actions.MainOrdinal ||
            call.Entered <= actions.Changed || call.Changed <= call.Entered || call.Changed > saved.Sequence ||
            call.Before != FalloutPlayerActorDistanceTimer.Allocated || call.After != timer || call.Returned == (call.Failure is not null) ||
            call.Failure is not null && call.Failure != saved.Failure || call.ArgumentBits is not null && call.ArgumentBits != saved.Clock?.ArgumentBits)
            throw new InvalidDataException("Actor-distance caller lost its actual invocation or partial writer history.");
        var expected = call.Before;
        if (call.Before.SubtractsArgument)
        {
            if (call.Sort is not null || call.SettingBits is not null || call.ArgumentBits is null)
                throw new InvalidDataException("Actor-distance subtraction entered the independently timed sort/reset arm.");
            expected = expected.Subtract(BitConverter.UInt32BitsToSingle(call.ArgumentBits.Value));
        }
        else
        {
            if (call.ArgumentBits is not null || call.SettingBits is not null && call.Sort is not { Returned: true } ||
                call.Returned && (call.SettingBits is null || call.Sort is not { Returned: true }) ||
                call.Sort is { } sort && (sort.Process != saved.CapturedProcess && saved.ColdHandoff?.PreviousProcess != sort.Process ||
                    sort.Source != FalloutStandaloneInterfaceSource.Read(saved.Source.Main).Identity || sort.Stack != saved.Stack))
                throw new InvalidDataException("Actor-distance sort lost its real interface owner or reset-before-return ordering.");
            if (call.SettingBits is { } setting) expected = expected.Reset(BitConverter.UInt32BitsToSingle(setting));
        }
        if (expected != call.After) throw new InvalidDataException("Actor-distance timer invented a store or discarded raw source bits.");
    }
}
