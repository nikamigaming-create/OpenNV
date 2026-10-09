using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// This is the authoritative member/target projection of CombatManager. A
// selected combat target, incoming hostility, and a player personal target are
// distinct state. Unavailable source decisions retain their actual prefix.
internal sealed partial class FalloutCombatGroups : IDisposable
{
    private sealed class Group(ulong identity)
    {
        internal ulong Identity { get; } = identity;
        internal List<FalloutFormKey> Members { get; } = [];
        internal List<FalloutFormKey> Targets { get; } = [];
    }

    private readonly FalloutCombatGroupDeclaration _source;
    private readonly string _stackIdentity;
    private readonly FalloutFormKey _player;
    private readonly FalloutCombatGroupInputs _inputs;
    private readonly Dictionary<ulong, Group> _groups = [];
    private readonly Dictionary<FalloutFormKey, ulong> _memberships = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, List<ulong>> _targetGroups = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, FalloutCombatActorIdentity> _actors = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, FalloutCombatGroupTargetReceipt> _currentTargets = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, FalloutCombatGroupCandidateReceipt> _playerCandidates = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutCombatGroupFailure> _failures = [];
    private readonly List<FalloutCombatGroupListRemoval> _removals = [];
    private ulong _nextGroupIdentity = 1;
    private ulong _lastSequence;
    private float _playerCombatSeconds;
    private bool _playerCombatFlag;
    private int _incomingMembersAtUpdate;
    private ulong _lastPlayerUpdateSequence;
    private bool _mutating;
    private bool _disposed;
    private string? _ownerFailure;

    internal FalloutCombatGroups(FalloutCombatGroupDeclaration source, string stackIdentity,
        FalloutFormKey player, FalloutCombatGroupInputs inputs, FalloutCombatGroupsSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stackIdentity);
        _source = source; _stackIdentity = stackIdentity; _player = player; _inputs = inputs;
        _ = Actor(player);
        if (!_actors[player].EnginePlayer) throw new InvalidDataException("Combat-group player is not the engine-created player.");
        if (restore is not null) Restore(restore);
    }

    internal string? SaveBlocker => _ownerFailure ?? (_failures.FirstOrDefault() is { } failure ?
        $"combat-group-{failure.Sequence}:{failure.Owner}:{failure.Error}" : null);
    internal IReadOnlyList<FalloutCombatGroupFailure> Failures => _failures.ToArray();
    internal IReadOnlyList<FalloutFormKey> ActorReferences => _actors.Keys.ToArray();
    internal bool HasActiveLists => _groups.Values.Any(group => group.Members.Count != 0 || group.Targets.Count != 0);
    internal object State => new
    {
        personalGroup = Membership(_player),
        playerCombatSeconds = _playerCombatSeconds,
        playerCombatFlag = _playerCombatFlag,
        incomingMembersAtUpdate = _incomingMembersAtUpdate,
        lastPlayerUpdateSequence = _lastPlayerUpdateSequence,
        groups = CaptureLists(),
        currentTargets = _currentTargets.Values.ToArray(),
        playerCandidates = _playerCandidates.Values.ToArray(),
        failures = Failures,
        ownerFailure = _ownerFailure,
        removals = _removals.ToArray(),
        boundary = "source-ordered-member-target-lists;distinct-cached-incoming-combat-flag;detection-reaction-perk-and-target-retirement-require-real-inputs;combat-tactics-and-group-auxiliary-fields-and-optional-detection-classification-output-unowned"
    };
    internal ulong? Membership(FalloutFormKey actor) => _memberships.TryGetValue(actor, out var group) ? group : null;

    internal FalloutExperienceLevelIntroInput ReadPlayerTargets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequireStableRead();
        if (_ownerFailure is { } error) return new(null, "source-combat-group-owner: " + error);
        if (_failures.FirstOrDefault() is { } failure)
            return new(null, $"source-combat-group-transition-{failure.Sequence}/{failure.Owner}: {failure.Error}");
        var count = Membership(_player) is { } group ? _groups[group].Targets.Count : 0;
        return new(count, "source-player-personal-combat-group-target-list/" + _source.Contract);
    }

    // The incoming query counts members of groups containing this target,
    // excluding the player itself in the player's personal group. It is never
    // used as the personal target count or as a substitute for detection.
    internal int IncomingMemberCount(FalloutFormKey target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); _ = Actor(target);
        RequireStableRead();
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        return IncomingMemberCountCore(target);
    }

    internal bool ReadPlayerCombatFlag()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); RequireStableRead();
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        return _playerCombatFlag;
    }

    private int IncomingMemberCountCore(FalloutFormKey target)
    {
        if (!_targetGroups.TryGetValue(target, out var groups)) return 0;
        var count = 0;
        foreach (var id in groups)
        {
            var group = _groups[id];
            count = checked(count + group.Members.Count -
                (Membership(_player) == id && Contains(group.Members, _player) ? 1 : 0));
        }
        return count;
    }

    internal void ObserveTarget(FalloutFormKey actor, FalloutFormKey? previous,
        FalloutFormKey? target, FalloutCombatGroupTransitionKind kind, FalloutFormKey? assistanceOwner = null)
    {
        Mutate(() =>
        {
            if (kind is not (FalloutCombatGroupTransitionKind.ActorTarget or FalloutCombatGroupTransitionKind.AssistanceJoin or
                FalloutCombatGroupTransitionKind.CompanionJoin or FalloutCombatGroupTransitionKind.OpaqueTargetChange or
                FalloutCombatGroupTransitionKind.OpaqueTargetEnd))
                throw new InvalidDataException("Actual target publication has no supported source operation kind.");
            _ = Actor(actor);
            if (previous is { } oldTarget) _ = Actor(oldTarget);
            if (target is { } newTarget) _ = Actor(newTarget);
            if (Same(previous, target)) return;
            if (_currentTargets.TryGetValue(actor, out var existing) && !Same(existing.Target, previous) ||
                !_currentTargets.ContainsKey(actor) && previous is not null)
                throw new InvalidDataException("Combat target transition is not the actual current actor target.");
            var sequence = NextSequence();
            _currentTargets[actor] = new(actor, target, sequence, kind);
            if (kind is FalloutCombatGroupTransitionKind.OpaqueTargetChange or FalloutCombatGroupTransitionKind.OpaqueTargetEnd)
            {
                Fail(sequence, kind, actor, target, "actual-target-change-source-arm-unowned",
                    "An actor target changed without its source group operation.");
                return;
            }
            if (target is null)
            {
                // An ended rendered engagement does not by itself retire all
                // source group targets. Only the real removal operation can.
                Fail(sequence, kind, actor, previous, "source-combat-end-controller-retirement-unowned",
                    "Combat end lacks its current source controller/member retirement decision.");
                return;
            }
            if (actor == _player)
            {
                Fail(sequence, kind, actor, target, "player-controller-target-is-not-personal-list",
                    "A player target selection cannot bypass the original personal target candidate path.");
                return;
            }
            if (kind == FalloutCombatGroupTransitionKind.CompanionJoin)
                Fail(sequence, kind, actor, target, "source-companion-controller-group-choice-unowned",
                    "The actual companion target lacks its original controller group-election input.");
            else if (kind == FalloutCombatGroupTransitionKind.AssistanceJoin && assistanceOwner is null)
                Fail(sequence, kind, actor, target, "source-group-join-owner-absent", "The source group join has no actual assisted actor.");
            else if (assistanceOwner is { } ally)
            {
                _ = Actor(ally);
                if (Membership(ally) is not { } allyGroup)
                    Fail(sequence, kind, actor, target, "source-assisted-actor-group-absent", "Assistance has no current group to join.");
                else if (Join(actor, allyGroup, sequence, kind) && !Contains(_groups[allyGroup].Targets, target.Value))
                    Fail(sequence, kind, actor, target, "source-assisted-target-not-in-group", "The assistance target differs from the source group's actual targets.");
            }
            else BeginActor(actor, target.Value, sequence, kind);
            if (target == _player) PlayerTargetCandidate(actor, sequence);
        });
    }

    internal void ObservePlayerTargetCandidate(FalloutFormKey target)
    {
        Mutate(() => PlayerTargetCandidate(target, NextSequence()));
    }

    private void PlayerTargetCandidate(FalloutFormKey target, ulong sequence)
    {
        _ = Actor(target);
        if (target == _player) throw new InvalidDataException("The player cannot be its own combat target.");
        var detection = _inputs.PlayerDetection(target); detection.Validate();
        // A rejected later candidate does not erase a previously admitted
        // target. Admission and the latest detection decision have independent
        // source lifetimes; only actual list removal retires the admission.
        var admission = _playerCandidates.TryGetValue(target, out var previous) ? previous.Admission : null;
        _playerCandidates[target] = new(target, sequence, detection.Level, detection.Owner, admission);
        if (detection.Level is not { } level)
        {
            Fail(sequence, FalloutCombatGroupTransitionKind.PlayerTargetCandidate, _player, target,
                detection.Owner, "Original player target admission requires the actual detection entry level.");
            return;
        }
        if (level <= 0) return;
        var group = Membership(_player) is { } existing ? _groups[existing] : NewGroup();
        if (!AddMember(group, _player, sequence, FalloutCombatGroupTransitionKind.PlayerTargetCandidate)) return;
        _memberships[_player] = group.Identity;
        var added = AddTarget(group, target, sequence, FalloutCombatGroupTransitionKind.PlayerTargetCandidate);
        if (added) _playerCandidates[target] = _playerCandidates[target] with
        { Admission = new(sequence, level, detection.Owner) };
    }

    private void BeginActor(FalloutFormKey actor, FalloutFormKey target, ulong sequence,
        FalloutCombatGroupTransitionKind kind)
    {
        if (Membership(actor) is { } id)
        {
            _ = AddTarget(_groups[id], target, sequence, kind);
            return;
        }
        // Original new-controller creation checks hostility before allocation.
        if (!Predicate(actor, target, sequence, kind)) return;
        var group = NewGroup();
        if (!AddMember(group, actor, sequence, kind)) return;
        _memberships[actor] = group.Identity;
        _ = AddTarget(group, target, sequence, kind);
    }

    private Group NewGroup()
    {
        var id = _nextGroupIdentity; _nextGroupIdentity = checked(id + 1);
        var group = new Group(id); _groups.Add(id, group); return group;
    }

    private bool AddTarget(Group group, FalloutFormKey target, ulong sequence, FalloutCombatGroupTransitionKind kind)
    {
        _ = Actor(target);
        if (Contains(group.Targets, target)) return true;
        foreach (var member in group.Members)
            if (!Predicate(member, target, sequence, kind)) return false;
        if (Contains(group.Members, target))
        {
            Fail(sequence, kind, target, target, "source-member-target-disjoint", "A member cannot also enter the same group's targets.");
            return false;
        }
        group.Targets.Add(target);
        if (!_targetGroups.TryGetValue(target, out var groups)) _targetGroups.Add(target, groups = []);
        if (groups.Contains(group.Identity)) throw new InvalidDataException("Combat target index already contains an absent group target.");
        groups.Add(group.Identity); return true;
    }

    private bool AddMember(Group group, FalloutFormKey actor, ulong sequence, FalloutCombatGroupTransitionKind kind)
    {
        _ = Actor(actor);
        if (Contains(group.Members, actor)) return true;
        foreach (var target in group.Targets)
            if (!Predicate(actor, target, sequence, kind)) return false;
        if (Contains(group.Targets, actor))
        {
            Fail(sequence, kind, actor, actor, "source-member-target-disjoint", "A target cannot also enter the same group's members.");
            return false;
        }
        group.Members.Add(actor); return true;
    }

    private bool Predicate(FalloutFormKey member, FalloutFormKey target, ulong sequence, FalloutCombatGroupTransitionKind kind)
    {
        var value = _inputs.TargetPredicate(member, target); value.Validate();
        if (value.Allowed == true) return true;
        Fail(sequence, kind, member, target, value.Owner, value.Allowed == false ?
            "The actual target transition conflicts with the original group's target predicate." :
            "The original member/target predicate has no admitted living owner.");
        return false;
    }

    private FalloutCombatActorIdentity Actor(FalloutFormKey actor)
    {
        var current = _inputs.Actor(actor); current.Validate();
        if (!FalloutFormKeyComparer.Instance.Equals(current.Reference, actor))
            throw new InvalidDataException("Combat actor lookup substituted another reference.");
        if (_actors.TryGetValue(actor, out var previous) && previous != current)
            throw new InvalidDataException("Combat actor identity drifted from the selected winning records.");
        _actors[actor] = current; return current;
    }

    private ulong NextSequence() => _lastSequence = checked(_lastSequence + 1);
    private void Fail(ulong sequence, FalloutCombatGroupTransitionKind kind, FalloutFormKey actor,
        FalloutFormKey? target, string owner, string error)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(error))
            throw new InvalidDataException("Combat-group failure lost its original owner/error.");
        if (!_failures.Any(failure => failure.Sequence == sequence && failure.Kind == kind &&
            Same(failure.Actor, actor) && Same(failure.Target, target) && failure.Owner == owner))
            _failures.Add(new(sequence, kind, actor, target, owner, error));
    }
    private void Mutate(Action mutation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mutating)
        {
            _ownerFailure ??= "Combat-group source input reentered its active list mutation.";
            throw new InvalidOperationException(_ownerFailure);
        }
        _mutating = true;
        try { mutation(); }
        catch (Exception error)
        {
            _ownerFailure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            throw;
        }
        finally { _mutating = false; }
    }
    private static bool Same(FalloutFormKey? left, FalloutFormKey? right) =>
        left is null || right is null ? left is null && right is null : FalloutFormKeyComparer.Instance.Equals(left.Value, right.Value);
    private static bool Contains(IEnumerable<FalloutFormKey> list, FalloutFormKey actor) => list.Contains(actor, FalloutFormKeyComparer.Instance);

    private void RequireStableRead()
    {
        if (!_mutating) return;
        _ownerFailure ??= "Combat-group count was queried inside an unfinished source mutation.";
        throw new InvalidOperationException(_ownerFailure);
    }

    public void Dispose()
    {
        if (_mutating) throw new InvalidOperationException("Combat-group owner cannot retire inside its source input callback.");
        _disposed = true;
    }
}
