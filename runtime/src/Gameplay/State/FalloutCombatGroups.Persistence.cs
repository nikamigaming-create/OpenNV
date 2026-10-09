using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutCombatGroups
{
    private FalloutCombatGroupLists[] CaptureLists() => _groups.Values.OrderBy(group => group.Identity)
        .Select(group => new FalloutCombatGroupLists(group.Identity, group.Members.ToArray(), group.Targets.ToArray())).ToArray();

    internal FalloutCombatGroupsSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mutating) throw new InvalidOperationException("Combat-group capture cannot interleave a source list mutation.");
        foreach (var actor in _actors.Keys.ToArray()) _ = Actor(actor);
        var snapshot = new FalloutCombatGroupsSnapshot(_source.Contract, _stackIdentity, _player,
            _nextGroupIdentity, _lastSequence, _actors.Values.OrderBy(actor => actor.Reference.ToString(), StringComparer.Ordinal).ToArray(),
            CaptureLists(), _memberships.Select(pair => new FalloutCombatGroupMembership(pair.Key, pair.Value)).ToArray(),
            _currentTargets.Values.OrderBy(receipt => receipt.Sequence).ToArray(),
            _playerCandidates.Values.OrderBy(receipt => receipt.Sequence).ToArray(), _removals.ToArray(),
            _failures.ToArray(), _playerCombatSeconds, _playerCombatFlag, _incomingMembersAtUpdate,
            _lastPlayerUpdateSequence, _ownerFailure);
        Validate(snapshot); return snapshot;
    }

    private void Restore(FalloutCombatGroupsSnapshot snapshot)
    {
        Validate(snapshot);
        if (snapshot.Contract != _source.Contract || snapshot.StackIdentity != _stackIdentity || !Same(snapshot.Player, _player))
            throw new InvalidDataException("Cold combat groups differ from the selected original declaration or source stack.");
        // Read all exact current source identities before publishing any list.
        foreach (var actor in snapshot.Actors)
            if (Actor(actor.Reference) != actor)
                throw new InvalidDataException("Cold combat actor differs from its exact winning source record/base.");
        var groups = new Dictionary<ulong, Group>();
        var targets = new Dictionary<FalloutFormKey, List<ulong>>(FalloutFormKeyComparer.Instance);
        foreach (var list in snapshot.Groups)
        {
            var group = new Group(list.Identity);
            group.Members.AddRange(list.Members); group.Targets.AddRange(list.Targets); groups.Add(group.Identity, group);
            foreach (var target in list.Targets)
            {
                if (!targets.TryGetValue(target, out var incoming)) targets.Add(target, incoming = []);
                incoming.Add(group.Identity);
            }
        }
        foreach (var (id, group) in groups) _groups.Add(id, group);
        foreach (var membership in snapshot.Memberships) _memberships.Add(membership.Actor, membership.Group);
        foreach (var (actor, incoming) in targets) _targetGroups.Add(actor, incoming);
        foreach (var receipt in snapshot.CurrentTargets) _currentTargets.Add(receipt.Actor, receipt);
        foreach (var receipt in snapshot.PlayerCandidates) _playerCandidates.Add(receipt.Target, receipt);
        _nextGroupIdentity = snapshot.NextGroupIdentity; _lastSequence = snapshot.LastSequence;
        _playerCombatSeconds = snapshot.PlayerCombatSeconds; _ownerFailure = snapshot.OwnerFailure;
        _playerCombatFlag = snapshot.PlayerCombatFlag; _incomingMembersAtUpdate = snapshot.IncomingMembersAtUpdate;
        _lastPlayerUpdateSequence = snapshot.LastPlayerUpdateSequence;
        _failures.AddRange(snapshot.Failures);
        _removals.AddRange(snapshot.Removals);
    }

    // A snapshot cannot invent fresh groups from fighting Booleans or replay
    // target additions. The actual retained actor targets must match receipts.
    internal void RequireCurrentTargets(IEnumerable<(FalloutFormKey Actor, FalloutFormKey? Target)> actors)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var actual = new Dictionary<FalloutFormKey, FalloutFormKey?>(FalloutFormKeyComparer.Instance);
        foreach (var (actor, target) in actors)
        {
            if (!actual.TryAdd(actor, target)) throw new InvalidDataException("Current combat actor was enumerated twice.");
            if (target is not null || _currentTargets.ContainsKey(actor)) _ = Actor(actor);
            if (target is not null && (!_currentTargets.TryGetValue(actor, out var receipt) || !Same(receipt.Target, target)))
                throw new InvalidDataException("A genuine actor engagement has no current source group transition receipt.");
        }
        foreach (var receipt in _currentTargets.Values)
            if (!actual.TryGetValue(receipt.Actor, out var target) || !Same(receipt.Target, target))
                throw new InvalidDataException("A combat group transition no longer matches its actual retained actor engagement.");
    }

    internal static void Validate(FalloutCombatGroupsSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        static bool Digest(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (!Digest(state.Contract) || string.IsNullOrWhiteSpace(state.StackIdentity) || state.NextGroupIdentity == 0 ||
            state.Actors is null || state.Groups is null || state.Memberships is null || state.CurrentTargets is null ||
            state.PlayerCandidates is null || state.Removals is null || state.Failures is null ||
            state.IncomingMembersAtUpdate < 0 || state.PlayerCombatFlag != (state.IncomingMembersAtUpdate > 0) ||
            state.LastPlayerUpdateSequence > state.LastSequence || state.LastPlayerUpdateSequence == 0 &&
                (state.PlayerCombatFlag || state.IncomingMembersAtUpdate != 0 || state.PlayerCombatSeconds != 0) ||
            !float.IsFinite(state.PlayerCombatSeconds) || state.PlayerCombatSeconds < 0 || state.OwnerFailure is not null && string.IsNullOrWhiteSpace(state.OwnerFailure))
            throw new InvalidDataException("Combat-group snapshot is incomplete.");
        var actors = new Dictionary<FalloutFormKey, FalloutCombatActorIdentity>(FalloutFormKeyComparer.Instance);
        foreach (var actor in state.Actors)
        {
            if (actor is null) throw new InvalidDataException("Combat actor source identity is absent.");
            actor.Validate();
            if (!actors.TryAdd(actor.Reference, actor)) throw new InvalidDataException("Combat actor source identity is duplicated.");
        }
        if (!actors.TryGetValue(state.Player, out var player) || !player.EnginePlayer ||
            actors.Values.Count(actor => actor.EnginePlayer) != 1)
            throw new InvalidDataException("Combat groups have no unique actual engine player.");
        var groups = new Dictionary<ulong, FalloutCombatGroupLists>();
        foreach (var group in state.Groups)
        {
            if (group is null || group.Identity == 0 || group.Identity >= state.NextGroupIdentity ||
                group.Members is null || group.Targets is null || !groups.TryAdd(group.Identity, group) ||
                group.Members.Distinct(FalloutFormKeyComparer.Instance).Count() != group.Members.Count ||
                group.Targets.Distinct(FalloutFormKeyComparer.Instance).Count() != group.Targets.Count ||
                group.Members.Any(member => !actors.ContainsKey(member) || Contains(group.Targets, member)) ||
                group.Targets.Any(target => !actors.ContainsKey(target)))
                throw new InvalidDataException("Combat group list identity, actor source or disjointness is invalid.");
        }
        var memberships = new Dictionary<FalloutFormKey, ulong>(FalloutFormKeyComparer.Instance);
        foreach (var membership in state.Memberships)
            if (membership is null || !actors.ContainsKey(membership.Actor) || !memberships.TryAdd(membership.Actor, membership.Group) ||
                !groups.TryGetValue(membership.Group, out var group) || !Contains(group.Members, membership.Actor))
                throw new InvalidDataException("Combat actor membership differs from its actual group member list.");
        foreach (var group in state.Groups)
            foreach (var member in group.Members)
                if (!memberships.ContainsKey(member)) throw new InvalidDataException("Combat member has no current group pointer owner.");
        if (state.Failures.Count == 0 && state.OwnerFailure is null &&
            state.Groups.SelectMany(group => group.Members).Distinct(FalloutFormKeyComparer.Instance).Count() != state.Groups.Sum(group => group.Members.Count))
            throw new InvalidDataException("Settled combat actors belong to multiple groups.");
        var currentTargets = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        foreach (var receipt in state.CurrentTargets)
            if (receipt is null || !currentTargets.Add(receipt.Actor) || !actors.ContainsKey(receipt.Actor) ||
                receipt.Target is { } target && (!actors.ContainsKey(target) || Same(target, receipt.Actor)) ||
                receipt.Sequence == 0 || receipt.Sequence > state.LastSequence || !Enum.IsDefined(receipt.Kind))
                throw new InvalidDataException("Current combat target transition is invalid.");
        foreach (var failure in state.Failures)
            if (failure is null || failure.Sequence == 0 || failure.Sequence > state.LastSequence || !Enum.IsDefined(failure.Kind) ||
                !actors.ContainsKey(failure.Actor) || failure.Target is { } target && !actors.ContainsKey(target) ||
                string.IsNullOrWhiteSpace(failure.Owner) || string.IsNullOrWhiteSpace(failure.Error))
                throw new InvalidDataException("Combat-group failure lost its genuine source transition.");
        if (state.Failures.Distinct().Count() != state.Failures.Count)
            throw new InvalidDataException("Combat-group failure receipt is duplicated.");
        foreach (var removal in state.Removals)
            if (removal is null || !groups.ContainsKey(removal.Group) || !actors.ContainsKey(removal.Actor) ||
                removal.Sequence == 0 || removal.Sequence > state.LastSequence)
                throw new InvalidDataException("Combat-group removal lost its actual list owner/sequence.");
        if (state.Removals.Select(removal => removal.Sequence).Distinct().Count() != state.Removals.Count)
            throw new InvalidDataException("Combat-group list removals share an impossible transition sequence.");
        var candidates = new Dictionary<FalloutFormKey, FalloutCombatGroupCandidateReceipt>(FalloutFormKeyComparer.Instance);
        foreach (var candidate in state.PlayerCandidates)
        {
            if (candidate is null || !actors.ContainsKey(candidate.Target) || Same(candidate.Target, state.Player) ||
                !candidates.TryAdd(candidate.Target, candidate) || candidate.Sequence == 0 || candidate.Sequence > state.LastSequence ||
                string.IsNullOrWhiteSpace(candidate.Owner))
                throw new InvalidDataException("Player combat target admission lost its actual detection decision/order.");
            if (candidate.Admission is { } admission &&
                (admission.Sequence == 0 || admission.Sequence > candidate.Sequence || admission.DetectionLevel <= 0 ||
                string.IsNullOrWhiteSpace(admission.Owner) || admission.RemovedSequence is { } removed &&
                (removed <= admission.Sequence || removed > state.LastSequence || !state.Removals.Any(removal =>
                    removal.Target && Same(removal.Actor, candidate.Target) && removal.Sequence == removed))))
                throw new InvalidDataException("Personal target admission lost its separate original detection/removal receipt.");
            if (candidate.DetectionLevel is null && !state.Failures.Any(failure =>
                failure.Kind == FalloutCombatGroupTransitionKind.PlayerTargetCandidate && failure.Sequence == candidate.Sequence &&
                Same(failure.Target, candidate.Target)))
                throw new InvalidDataException("An unowned detection decision has no retained original failure.");
        }
        if (state.Failures.Count == 0 && state.OwnerFailure is null)
        {
            foreach (var receipt in state.CurrentTargets.Where(receipt => receipt.Target is not null))
            {
                if (memberships.TryGetValue(receipt.Actor, out var group) ?
                    !Contains(groups[group].Targets, receipt.Target!.Value) && !state.Removals.Any(removal =>
                        removal.Target && removal.Group == group && Same(removal.Actor, receipt.Target) && removal.Sequence >= receipt.Sequence) :
                    !state.Removals.Any(removal => !removal.Target && Same(removal.Actor, receipt.Actor) && removal.Sequence >= receipt.Sequence))
                    throw new InvalidDataException("An active combat target has no genuine group member/target state.");
                if (Same(receipt.Target, state.Player) && (!candidates.TryGetValue(receipt.Actor, out var candidate) ||
                    candidate.Sequence < receipt.Sequence))
                    throw new InvalidDataException("Player-targeting combat has no actual personal-list admission decision.");
            }
            foreach (var candidate in state.PlayerCandidates.Where(candidate => candidate.Admission is { RemovedSequence: null }))
                if (!memberships.TryGetValue(state.Player, out var group) || !Contains(groups[group].Targets, candidate.Target))
                    throw new InvalidDataException("An admitted personal combat target disappeared without its source removal receipt.");
        }
        if (!memberships.ContainsKey(state.Player) && state.PlayerCombatSeconds != 0)
            throw new InvalidDataException("An absent player group retains an active combat clock.");
    }
}
