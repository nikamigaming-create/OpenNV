using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutCombatGroups
{
    internal bool JoinActor(FalloutFormKey actor, ulong group)
    {
        var result = false;
        Mutate(() => result = Join(actor, group, NextSequence(), FalloutCombatGroupTransitionKind.AssistanceJoin));
        return result;
    }

    private bool Join(FalloutFormKey actor, ulong destinationId, ulong sequence, FalloutCombatGroupTransitionKind kind)
    {
        _ = Actor(actor);
        var destination = RequireGroup(destinationId);
        if (Membership(actor) is { } sourceId)
            return sourceId == destinationId || Merge(destination, RequireGroup(sourceId), sequence, kind);
        if (!AddMember(destination, actor, sequence, kind)) return false;
        _memberships[actor] = destinationId; return true;
    }

    internal bool MergeGroups(ulong destination, ulong source)
    {
        var result = false;
        Mutate(() => result = destination == source || Merge(RequireGroup(destination), RequireGroup(source),
            NextSequence(), FalloutCombatGroupTransitionKind.AssistanceJoin));
        return result;
    }

    private bool Merge(Group destination, Group source, ulong sequence, FalloutCombatGroupTransitionKind kind)
    {
        // A directed merge first checks both existing target sets against the
        // other members. This is not a connected-component or faction union.
        foreach (var target in source.Targets)
            foreach (var member in destination.Members)
                if (!MergePredicate(member, target, sequence, kind)) return false;
        foreach (var target in destination.Targets)
            foreach (var member in source.Members)
                if (!MergePredicate(member, target, sequence, kind)) return false;
        if (source.Members.Any(actor => Contains(destination.Targets, actor)) ||
            destination.Members.Any(actor => Contains(source.Targets, actor))) return false;

        foreach (var actor in source.Members.ToArray())
        {
            if (!AddMember(destination, actor, sequence, kind)) return false;
            _memberships[actor] = destination.Identity;
        }
        foreach (var target in source.Targets.ToArray())
            if (!AddTarget(destination, target, sequence, kind)) return false;
        // Original successful controller merge clears the old member list.
        // Its targets and manager registration have their own retirement owner.
        source.Members.Clear();
        return true;
    }

    private bool MergePredicate(FalloutFormKey member, FalloutFormKey target, ulong sequence, FalloutCombatGroupTransitionKind kind)
    {
        var actual = _inputs.TargetPredicate(member, target); actual.Validate();
        if (actual.Allowed is { } allowed) return allowed;
        Fail(sequence, kind, member, target, actual.Owner, "Group merge lacks its source cross-member target predicate.");
        return false;
    }

    // The source player/ally split retains nonteammates in the old group,
    // copies its existing target list, then moves individually admitted allies.
    internal ulong SplitPlayerGroup(FalloutFormKey opponent)
    {
        ulong result = 0;
        Mutate(() =>
        {
            _ = Actor(opponent);
            if (opponent == _player) throw new InvalidDataException("Player group cannot split against the player itself.");
            var sequence = NextSequence();
            var playerGroup = Membership(_player) is { } playerId ? RequireGroup(playerId) :
                throw new InvalidOperationException("Original player split has no current personal group.");
            if (!Contains(playerGroup.Members, opponent) || Membership(opponent) != playerGroup.Identity)
                throw new InvalidOperationException("Player split does not own the opposing actor's current membership.");
            RemoveMember(playerGroup, opponent);
            if (playerGroup.Members.Count > 1)
            {
                var old = playerGroup;
                RemoveMember(old, _player);
                playerGroup = NewGroup();
                if (!AddMember(playerGroup, _player, sequence, FalloutCombatGroupTransitionKind.PlayerSplit)) return;
                _memberships[_player] = playerGroup.Identity;
                // The original copies target entries before moving allies.
                foreach (var target in old.Targets)
                    if (!AddTarget(playerGroup, target, sequence, FalloutCombatGroupTransitionKind.PlayerSplit)) return;
                foreach (var member in old.Members.ToArray())
                {
                    if (!_inputs.PlayerTeammate(member)) continue;
                    if (!AddMember(playerGroup, member, sequence, FalloutCombatGroupTransitionKind.PlayerSplit)) continue;
                    RemoveMember(old, member); _memberships[member] = playerGroup.Identity;
                }
            }
            _ = AddTarget(playerGroup, opponent, sequence, FalloutCombatGroupTransitionKind.PlayerSplit);
            var opposingGroup = NewGroup();
            if (!AddMember(opposingGroup, opponent, sequence, FalloutCombatGroupTransitionKind.PlayerSplit)) return;
            _memberships[opponent] = opposingGroup.Identity; result = opposingGroup.Identity;
        });
        return result;
    }

    internal void RemoveGroupTarget(ulong group, FalloutFormKey target)
    {
        Mutate(() => { _ = Actor(target); RemoveTarget(RequireGroup(group), target); });
    }
    internal void RemoveActorMembership(FalloutFormKey actor)
    {
        Mutate(() =>
        {
            _ = Actor(actor);
            if (Membership(actor) is { } group) RemoveMember(RequireGroup(group), actor);
        });
    }

    internal void ObserveOpaqueTeammateChange(FalloutFormKey actor)
    {
        Mutate(() =>
        {
            _ = Actor(actor);
            if (Membership(actor) is { } || _groups.Values.Any(group => Contains(group.Targets, actor)))
                Fail(NextSequence(), FalloutCombatGroupTransitionKind.OpaqueTeammateChange, actor, null,
                    "source-teammate-group-transition-unowned", "Changing teammate state lacks its reached source group split/merge operation.");
        });
    }

    internal void ObserveActorRetirement(FalloutFormKey actor, string owner)
    {
        Mutate(() =>
        {
            _ = Actor(actor); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
            if (Membership(actor) is null && !_groups.Values.Any(group => Contains(group.Targets, actor)) &&
                (!_currentTargets.TryGetValue(actor, out var target) || target.Target is null)) return;
            if (_failures.Any(failure => failure.Kind == FalloutCombatGroupTransitionKind.ActorRetirement &&
                Same(failure.Actor, actor) && failure.Owner == owner)) return;
            Fail(NextSequence(), FalloutCombatGroupTransitionKind.ActorRetirement, actor, null, owner,
                "Actual actor retirement lacks its original controller/member/target retirement transaction.");
        });
    }

    internal void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Mutate(() =>
        {
            foreach (var group in _groups.Values.ToArray())
            {
                foreach (var target in group.Targets.ToArray())
                {
                    var actual = _inputs.TargetRetirement(group.Identity, target); actual.Validate();
                    if (actual.Allowed == true) RemoveTarget(group, target);
                    else if (actual.Allowed is null && !_failures.Any(failure =>
                        failure.Kind == FalloutCombatGroupTransitionKind.RetirementUpdate &&
                        Same(failure.Actor, target) && failure.Owner == actual.Owner))
                        Fail(NextSequence(), FalloutCombatGroupTransitionKind.RetirementUpdate, target, target,
                            actual.Owner, "Current group target retirement lacks its original timer/detection decision.");
                }
            }
            if (Membership(_player) is { } playerGroup && RequireGroup(playerGroup).Targets.Count == 0)
                RemoveMember(RequireGroup(playerGroup), _player);
            var next = Membership(_player) is null ? 0 : _playerCombatSeconds + seconds;
            if (!float.IsFinite(next)) throw new InvalidDataException("Player group combat clock is non-finite.");
            _playerCombatSeconds = next;
            // The public Boolean is a cached player-update result. Target-list
            // mutation does not publish it early, and it is not the personal
            // target count consumed independently by the XP scheduler.
            _incomingMembersAtUpdate = IncomingMemberCountCore(_player);
            _playerCombatFlag = _incomingMembersAtUpdate > 0;
            _lastPlayerUpdateSequence = NextSequence();
        });
    }

    private void RemoveMember(Group group, FalloutFormKey actor)
    {
        var index = group.Members.FindIndex(value => Same(value, actor));
        if (index < 0) return;
        _removals.Add(new(group.Identity, actor, false, NextSequence()));
        group.Members.RemoveAt(index);
        if (Membership(actor) == group.Identity) _memberships.Remove(actor);
    }
    private void RemoveTarget(Group group, FalloutFormKey target)
    {
        var index = group.Targets.FindIndex(value => Same(value, target));
        if (index < 0) return;
        var sequence = NextSequence();
        _removals.Add(new(group.Identity, target, true, sequence));
        if (Membership(_player) == group.Identity && _playerCandidates.TryGetValue(target, out var candidate) && candidate.Admission is { } admission)
            _playerCandidates[target] = candidate with { Admission = admission with { RemovedSequence = sequence } };
        group.Targets.RemoveAt(index);
        if (!_targetGroups.TryGetValue(target, out var groups) || !groups.Remove(group.Identity))
            throw new InvalidDataException("Source target removal has no exact inverse target index.");
        if (groups.Count == 0) _targetGroups.Remove(target);
    }
    private Group RequireGroup(ulong identity) => _groups.TryGetValue(identity, out var group) ? group :
        throw new InvalidDataException("Combat group is absent from its actual manager.");
}
