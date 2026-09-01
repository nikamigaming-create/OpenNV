using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicIntTimerContract(int TicksPerSecond)
{
    private const string Schema = "opennv-classic-int-time/v1";

    internal static ClassicIntTimerContract Parse(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != Schema ||
            source.GetProperty("timeUnit").GetString() != "retail-decisecond-tick" ||
            source.GetProperty("delayBasis").GetString() !=
                "current-authoritative-tick" ||
            !source.GetProperty("dueOrdering").EnumerateArray()
                .Select(row => row.GetString()).SequenceEqual(
                    ["due-tick-ascending", "insertion-sequence-ascending"]))
            throw new InvalidOperationException(
                "Classic INT time contract identity drifted.");
        var ticksPerSecond = source.GetProperty("ticksPerSecond").GetInt32();
        if (ticksPerSecond <= 0)
            throw new InvalidOperationException(
                "Classic INT ticks-per-second contract is invalid.");
        return new ClassicIntTimerContract(ticksPerSecond);
    }
}

internal sealed record ClassicIntTimerEvent(
    long Sequence,
    int TargetHandle,
    int DueTick,
    int FixedParameter);

internal sealed record ClassicIntTimerState(
    int CurrentTick,
    long NextSequence,
    IReadOnlyList<ClassicIntTimerEvent> Pending)
{
    internal static ClassicIntTimerState Initial { get; } = new(0, 0, []);

    internal void Validate()
    {
        if (CurrentTick < 0 || NextSequence < 0 || Pending.Any(row =>
                row.Sequence < 0 || row.Sequence >= NextSequence ||
                row.TargetHandle == 0 || row.DueTick < CurrentTick) ||
            Pending.Select(row => row.Sequence).Distinct().Count() != Pending.Count ||
            !Pending.SequenceEqual(Pending.OrderBy(row => row.DueTick)
                .ThenBy(row => row.Sequence)))
            throw new InvalidOperationException(
                "Classic INT timer state is invalid.");
    }
}

internal sealed record ClassicIntTimerDelivery(
    ClassicIntTimerState State,
    ClassicIntTimerEvent Event);

internal static class ClassicIntTimerOwner
{
    internal static int GameTicks(
        ClassicIntTimerContract contract,
        int seconds)
    {
        if (seconds < 0)
            throw new InvalidOperationException(
                "Classic INT game_ticks seconds are negative.");
        return checked(seconds * contract.TicksPerSecond);
    }

    internal static ClassicIntTimerState Schedule(
        ClassicIntTimerState source,
        int targetHandle,
        int delayTicks,
        int fixedParameter)
    {
        source.Validate();
        if (targetHandle == 0 || delayTicks < 0)
            throw new InvalidOperationException(
                "Classic INT timer event is invalid.");
        var scheduled = new ClassicIntTimerEvent(
            source.NextSequence,
            targetHandle,
            checked(source.CurrentTick + delayTicks),
            fixedParameter);
        var result = source with
        {
            NextSequence = checked(source.NextSequence + 1),
            Pending = source.Pending.Append(scheduled)
                .OrderBy(row => row.DueTick).ThenBy(row => row.Sequence).ToArray(),
        };
        result.Validate();
        return result;
    }

    internal static ClassicIntTimerDelivery TakeNextDue(
        ClassicIntTimerState source,
        int throughTick)
    {
        source.Validate();
        if (throughTick < source.CurrentTick || source.Pending.Count == 0 ||
            source.Pending[0].DueTick > throughTick)
            throw new InvalidOperationException(
                "Classic INT timer has no event due at the requested tick.");
        var next = source.Pending[0];
        var result = source with
        {
            CurrentTick = next.DueTick,
            Pending = source.Pending.Skip(1).ToArray(),
        };
        result.Validate();
        return new ClassicIntTimerDelivery(result, next);
    }

    internal static ClassicIntTimerState AdvanceIdle(
        ClassicIntTimerState source,
        int throughTick)
    {
        source.Validate();
        if (throughTick < source.CurrentTick ||
            source.Pending.Any(row => row.DueTick <= throughTick))
            throw new InvalidOperationException(
                "Classic INT timer cannot skip a due event.");
        var result = source with { CurrentTick = throughTick };
        result.Validate();
        return result;
    }
}
