using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Queue, AP reservation and one-time shot publication are shared gameplay state.
// Presentation supplies a current source-backed target sample; attacks remain
// ordinary weapon actions through the existing projectile and damage owners.
internal sealed class FalloutVatsSession(FalloutPlayerVitals vitals)
{
    internal enum Phase { Selecting, Executing, Completed, Cancelled, Faulted }
    internal sealed record Target(FalloutFormKey Reference, byte Part, string ActorName, string PartName,
        float Chance, float ActionPointCost, bool Visible);
    internal sealed record Attack(long Ordinal, FalloutFormKey Weapon, Guid WeaponInstance, Target Target, bool Success, double? Roll);
    private readonly List<Attack> _queue = [];
    private readonly HashSet<long> _committed = [];
    private long _ordinal;
    private int _next;
    internal Phase CurrentPhase { get; private set; }
    internal string? Error { get; private set; }
    internal IReadOnlyList<Attack> Queue => _queue.ToArray();
    internal float AvailableActionPoints => vitals.State.ExactActionPoints;
    internal float ReservedActionPoints => _queue.Skip(_next).Sum(attack => attack.Target.ActionPointCost);
    internal Attack? CurrentAttack => CurrentPhase == Phase.Executing && _next < _queue.Count ? _queue[_next] : null;
    internal int CompletedAttacks => _next;
    internal bool CurrentShotCommitted => CurrentAttack is { } attack && _committed.Contains(attack.Ordinal);

    internal void Add(FalloutFormKey weapon, Guid weaponInstance, Target target, Func<uint> nextRandom, bool alwaysHit)
    {
        Require(Phase.Selecting);
        if (weaponInstance == Guid.Empty || !target.Visible || !float.IsFinite(target.Chance) || target.Chance is < 0 or > 100 ||
            !float.IsFinite(target.ActionPointCost) || target.ActionPointCost < 0 || target.Reference.ObjectId == 0 || target.Part > 14)
            throw new InvalidOperationException("The selected VATS body part is unavailable.");
        if (ReservedActionPoints + target.ActionPointCost > AvailableActionPoints)
            throw new InvalidOperationException("Not enough action points for another attack.");
        var roll = alwaysHit ? (double?)null : nextRandom() / 4294967296.0;
        _queue.Add(new(checked(++_ordinal), weapon, weaponInstance, target, alwaysHit || target.Chance / 100 >= roll!.Value, roll));
    }

    internal void RemoveLast()
    {
        Require(Phase.Selecting);
        if (_queue.Count > 0) _queue.RemoveAt(_queue.Count - 1);
    }

    internal void Execute()
    {
        Require(Phase.Selecting);
        if (_queue.Count == 0) throw new InvalidOperationException("Queue at least one VATS attack.");
        if (ReservedActionPoints > AvailableActionPoints) throw new InvalidOperationException("Queued action points are no longer available.");
        CurrentPhase = Phase.Executing;
    }

    internal void CommitShot(long ordinal)
    {
        var attack = CurrentAttack ?? throw new InvalidOperationException("VATS has no current queued attack.");
        if (attack.Ordinal != ordinal || _committed.Contains(ordinal))
            throw new InvalidOperationException("This VATS attack has already published its action-point cost.");
        if (attack.Target.ActionPointCost > 0) vitals.SpendActionPoints(attack.Target.ActionPointCost);
        _committed.Add(ordinal);
    }

    internal void CompleteAttack(bool fired)
    {
        var attack = CurrentAttack ?? throw new InvalidOperationException("VATS has no current queued attack.");
        if (fired != _committed.Contains(attack.Ordinal)) throw new InvalidOperationException("VATS shot and AP publication disagree.");
        ++_next;
        if (_next == _queue.Count) CurrentPhase = Phase.Completed;
    }

    internal void Cancel()
    {
        if (CurrentPhase is Phase.Completed or Phase.Cancelled or Phase.Faulted) return;
        CurrentPhase = Phase.Cancelled;
    }

    internal void Fail(Exception error) { Error = error.Message; CurrentPhase = Phase.Faulted; }
    private void Require(Phase phase)
    {
        if (CurrentPhase != phase) throw new InvalidOperationException("VATS action is unavailable in this phase.");
    }
}
