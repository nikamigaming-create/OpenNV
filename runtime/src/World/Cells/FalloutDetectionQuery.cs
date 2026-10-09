using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutDetectionProcessPresence(bool HasProcess, FalloutDetectionProcessLevel? Level)
{
    internal void Validate()
    {
        if (HasProcess != (Level is not null) || Level is { } level && !Enum.IsDefined(level))
            throw new InvalidDataException("Detection process presence/class is inconsistent.");
    }
}

internal sealed record FalloutDetectionQueryActor(FalloutFormKey Actor, FalloutDetectionProcessPresence? Process,
    float? DistanceToPlayer, bool? PlayerTeammate, bool? RawInCombat, bool? HasSource3D,
    bool? CommandInCombat = null);

internal sealed record FalloutDetectionPlayerQuery(bool? Sneaking, bool? RawInCombat);
internal sealed record FalloutDetectionRead(int Score, bool Visible);

// Mode-zero query consumes an already committed directional entry. Producer
// inputs are never inferred or recomputed here, and unknown absence refuses.
internal sealed class FalloutDetectionQuery(Func<FalloutFormKey, bool> actor,
    Func<FalloutFormKey, FalloutDetectionQueryActor> state, Func<FalloutDetectionPlayerQuery> player,
    Func<FalloutFormKey, FalloutDetectionCache?> processCache, Action<FalloutFormKey> preparePerkRead)
{
    internal int GetDetected(FalloutFormKey receiver, FalloutFormKey target)
    {
        if (!actor(receiver) || !actor(target)) return 0;
        var from = state(receiver);
        if (from.Actor != receiver)
            throw new InvalidDataException("Detection query state belongs to another placed actor.");
        var fromProcess = Process(from);
        if (!fromProcess.HasProcess) return 0;
        var to = state(target);
        if (to.Actor != target) throw new InvalidDataException("Detection query state belongs to another placed actor.");
        var toProcess = Process(to);
        if (!toProcess.HasProcess) return 0;
        // The wrapper reads the target's command combat state before mode zero.
        // For the reserved player that is a distinct special accessor; the raw
        // actor combat flag cannot substitute for it. Mode zero does not use
        // this value to manufacture a score, but the read must still be owned.
        _ = Known(to.CommandInCombat, "target command combat");
        return ReadCommitted(from, to, fromProcess).Score > 0 ? 1 : 0;
    }

    internal FalloutDetectionRead ReadCommitted(FalloutDetectionQueryActor receiver, FalloutDetectionQueryActor target,
        FalloutDetectionProcessPresence receiverProcess)
    {
        receiverProcess.Validate();
        if (!actor(receiver.Actor) || !actor(target.Actor) || !receiverProcess.HasProcess ||
            Process(receiver) != receiverProcess || !Process(target).HasProcess)
            throw new InvalidDataException("Detection pair query requires its source receiver process.");
        // The original pair preparation precedes range/cache access. This is a
        // real bound read owner, not a callback which produces a fake zero score.
        preparePerkRead(receiver.Actor);
        if (Distance(receiver) >= 8192 || Distance(target) >= 8192) return new(-100, false);
        if (Known(target.PlayerTeammate, "target teammate") && !Known(target.RawInCombat, "target raw combat"))
        {
            var currentPlayer = player();
            if (Known(currentPlayer.Sneaking, "player sneaking") && !Known(currentPlayer.RawInCombat, "player raw combat"))
                return new(-100, false);
        }
        FalloutDetectionCacheEntry? entry = null;
        if (receiverProcess.Level == FalloutDetectionProcessLevel.High)
        {
            var cache = processCache(receiver.Actor) ??
                throw new NotSupportedException("GetDetected has no authoritative committed HighProcess cache.");
            if (cache.Owner != receiver.Actor || cache.Process != receiverProcess.Level)
                throw new InvalidDataException("GetDetected cache belongs to another placed actor/process class.");
            entry = cache.Read(target.Actor, FalloutDetectionDirection.Detected);
        }
        // A known non-High getter has a source null result. Unknown process/3D
        // state must not be represented as that result or a missing Godot node.
        if (!Known(target.HasSource3D, "target source 3D")) return new(-100, entry?.Visible ?? false);
        return new(entry is null || entry.Score == int.MaxValue ? -100 : entry.Score, entry?.Visible ?? false);
    }

    private static FalloutDetectionProcessPresence Process(FalloutDetectionQueryActor actor)
    {
        var result = actor.Process ?? throw new NotSupportedException("GetDetected actor has no authoritative process owner.");
        result.Validate(); return result;
    }
    private static float Distance(FalloutDetectionQueryActor actor)
    {
        var value = actor.DistanceToPlayer ?? throw new NotSupportedException("GetDetected actor has no source spatial distance owner.");
        return float.IsFinite(value) && value >= 0 ? value : throw new InvalidDataException("GetDetected source distance is invalid.");
    }
    private static bool Known(bool? value, string field) => value ??
        throw new NotSupportedException("GetDetected has no authoritative " + field + " owner.");
}
