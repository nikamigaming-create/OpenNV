using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private object? _lastAcquisition;

    private bool TryAcquireThreat(RuntimeNativePlayer? player)
    {
        _detectRange = ThreatRadius(_threat!);
        if (!float.IsFinite(_detectRange) || _detectRange <= 0)
            throw new InvalidDataException("Actor threat radius is invalid.");
        var maximumDistanceSquared = _detectRange * _detectRange;
        var candidates = new List<(FalloutFormKey Reference, RuntimeNativeActorCombat? Actor, float DistanceSquared)>();
        foreach (var candidate in CombatActors)
        {
            if (candidate == this || candidate.Dead || !candidate._state.Enabled ||
                !_context!.Resident(candidate._actor.GlobalPosition)) continue;
            var distance = _actor.GlobalPosition.DistanceSquaredTo(candidate._actor.GlobalPosition);
            if (distance <= maximumDistanceSquared) candidates.Add((candidate._state.Reference, candidate, distance));
        }
        if (player is { CollisionResident: true } && _context!.Vitals().HitPoints > 0)
        {
            var distance = _actor.GlobalPosition.DistanceSquaredTo(player.GlobalPosition);
            if (distance <= maximumDistanceSquared) candidates.Add((_records.RuntimeFormKey(0x14), null, distance));
        }
        // Eligibility comes from the winning actor and faction state, including
        // script changes. Nearest visible eligibility is a deterministic initial
        // selection policy; retail threat weighting remains a separate owner.
        var decisions = new List<object>();
        var selected = false;
        foreach (var candidate in candidates.OrderBy(value => value.DistanceSquared)
                     .ThenBy(value => value.Reference.ToString(), StringComparer.Ordinal))
        {
            var relation = _world.ActorRelation(_state.Reference, candidate.Reference);
            var initiates = _threat!.Initiates(relation);
            var warns = _threat.Warns(relation);
            var eligible = initiates || warns;
            bool? visible = eligible ? CanSeePoint(candidate.Actor?.BodyTargetPoint() ?? player!.CombatTargetPoint, candidate.Actor) : null;
            decisions.Add(new { reference = candidate.Reference, candidate.DistanceSquared, relation, eligible, visible, warns });
            if (visible != true) continue;
            selected = true;
            _relation = relation;
            if (initiates || candidate.DistanceSquared <= maximumDistanceSquared / 4)
            {
                _state.AggroWarning = null;
                _state.Engagement = new(candidate.Reference);
            }
            else if (_state.AggroWarning?.Target != candidate.Reference)
            {
                _state.AggroWarning = new(candidate.Reference, RemainingSeconds: WarningInterval);
            }
            break;
        }
        if (!selected) ClearRadiusWarning();
        _lastAcquisition = new
        {
            radius = _detectRange,
            candidates = candidates.Count,
            decisions,
            selected = _state.Engagement?.Target,
            warning = _state.AggroWarning,
            boundary = "source-faction-and-aggression-eligibility;nearest-visible-selection;retail-threat-priority-unmatched"
        };
        return _state.Engagement is not null;
    }
}
