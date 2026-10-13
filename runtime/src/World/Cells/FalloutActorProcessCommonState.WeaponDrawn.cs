using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessWeaponDrawnObservation(Guid Observation, string Producer, long Changed);
internal sealed record FalloutProcessWeaponDrawnFields(byte Value, long Stores, bool Available,
    IReadOnlyList<FalloutProcessWeaponDrawnObservation> Observations)
{
    internal static FalloutProcessWeaponDrawnFields Constructed => new(0, 0, true, []);
}
internal sealed record FalloutProcessWeaponDrawnQuery(FalloutProcessSceneQuery Process, byte Value);
internal sealed record FalloutPlayerFightingDisableCall(FalloutProcessSceneQuery Process, uint MoverFlags,
    uint? ControlMode, bool GetterReturned, bool? Drawn, long Changed, int Stores, bool Returned, string? Failure);

internal sealed partial class FalloutActorProcessCommonState
{
    private sealed class WeaponObservationHistory(long epoch, IReadOnlyList<FalloutProcessWeaponDrawnObservation> observations)
    {
        internal long Epoch { get; } = epoch;
        internal List<FalloutProcessWeaponDrawnObservation> Ordered { get; } = observations.ToList();
        internal HashSet<Guid> Identities { get; } = observations.Select(value => value.Observation).ToHashSet();
    }
    private readonly Dictionary<FalloutFormKey, WeaponObservationHistory> _weaponObservations = new(FalloutFormKeyComparer.Instance);
    internal FalloutProcessWeaponDrawnQuery ReadWeaponDrawn(FalloutFormKey actor, long epoch)
    {
        var process = ReadSceneQueries(actor, epoch);
        if (process.Level is not (FalloutDetectionProcessLevel.MiddleHigh or FalloutDetectionProcessLevel.High))
            throw new NotSupportedException("source-Actor-weapon-drawn-lower-process-virtual-unreviewed");
        var fields = Require(actor).SceneFields?.WeaponDrawn ??
            throw new InvalidDataException("Current process omitted its independent weapon-drawn constructor byte.");
        if (!fields.Available)
            throw new NotSupportedException("source-Actor-weapon-drawn-observed-writer-unbound");
        return new(process, fields.Value);
    }

    internal FalloutPlayerFightingDisableCall DisablePlayerFighting(FalloutFormKey actor, long epoch,
        uint currentMoverFlags, uint? currentSourceControlMode)
    {
        var process = ReadSceneQueries(actor, epoch);
        if (!process.Actor.EnginePlayer)
            throw new InvalidOperationException("Player fighting disable requires the actual current engine Player.");
        // The source false argument skips the enable-only early mover guard.
        // Its separate getter still tests mover800 before process+134.
        if ((currentMoverFlags & 0x800) != 0)
        {
            if (currentSourceControlMode is null)
                throw new InvalidOperationException("Player weapon-drawn mover800 arm omitted its actual source mode read.");
            if (currentSourceControlMode == 4)
                throw new NotSupportedException("source-Player-weapon-drawn-mode-four-context-child-unbound");
            return new(process, currentMoverFlags, currentSourceControlMode, true, false, process.Changed, 0, true, null);
        }
        if (currentSourceControlMode is not null)
            throw new InvalidOperationException("Player weapon-drawn getter invented the skipped mover800 mode read.");
        var read = ReadWeaponDrawn(actor, epoch);
        if (read.Value == 0)
            return new(read.Process, currentMoverFlags, currentSourceControlMode, true, false, read.Process.Changed, 0, true, null);
        // An actual change calls Player action13 before the process byte
        // setter. Constructor false is a pure getter/early return, never a
        // replayed animation or an alias of semantic Fighting permission.
        throw new NotSupportedException("source-Player-fighting-action-thirteen-animation-consumer-unbound");
    }

    internal void ObserveWeaponDrawnWriter(FalloutFormKey actor, long epoch, Guid observation, string producer)
    {
        _ = ReadSceneQueries(actor, epoch);
        if (observation == Guid.Empty || string.IsNullOrWhiteSpace(producer))
            throw new InvalidDataException("Process weapon-drawn observation lost its actual producer identity.");
        var current = Require(actor); var fields = current.SceneFields?.WeaponDrawn ??
            throw new NotSupportedException("source-Actor-weapon-drawn-fields-unconstructed");
        if (!_weaponObservations.TryGetValue(actor, out var history) || history.Epoch != epoch)
            _weaponObservations[actor] = history = new(epoch, fields.Observations);
        if (!history.Identities.Add(observation))
            throw new InvalidOperationException("Process weapon-drawn writer observation was replayed.");
        var changed = Next();
        history.Ordered.Add(new(observation, producer, changed));
        fields = fields with { Available = false, Observations = history.Ordered.AsReadOnly() };
        _current[actor] = current with { SceneFields = current.SceneFields! with { WeaponDrawn = fields }, Changed = changed };
    }
    private static FalloutProcessCommonEntry CaptureWeaponObservationHistory(FalloutProcessCommonEntry entry) =>
        entry.SceneFields?.WeaponDrawn is { } fields ? entry with
        { SceneFields = entry.SceneFields with { WeaponDrawn = fields with { Observations = fields.Observations.ToArray() } } } : entry;

    private static void ValidateWeaponDrawnFields(FalloutProcessCommonEntry entry)
    {
        var fields = entry.SceneFields?.WeaponDrawn;
        if ((entry.SceneFields is not null) != (fields is not null) || fields is not null &&
            (fields.Value != 0 || fields.Stores != 0 || fields.Observations is null ||
                fields.Available != (fields.Observations.Count == 0) ||
                fields.Observations.Select(value => value.Observation).Distinct().Count() != fields.Observations.Count ||
                fields.Observations.Any(value => value.Observation == Guid.Empty || string.IsNullOrWhiteSpace(value.Producer) ||
                    value.Changed < 1 || value.Changed > entry.Changed)))
            throw new InvalidDataException("Process weapon-drawn field lost its independent constructor or observed writer availability.");
    }
}
