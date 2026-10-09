using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutCombatGroups? _combatGroups;
    private FalloutCombatGroupDeclaration? _combatGroupDeclaration;
    private string? _combatGroupStackIdentity;
    private FalloutCombatGroupsSnapshot? _combatGroupRestoreIdentity;
    private Func<FalloutFormKey, FalloutCombatGroupDetection>? _playerGroupDetection;
    private Func<FalloutFormKey, FalloutFormKey, FalloutCombatGroupPredicate>? _groupReaction;
    private Func<ulong, FalloutFormKey, FalloutCombatGroupPredicate>? _groupTargetRetirement;
    private bool _combatGroupDecisionsBound;
    private (FalloutFormKey Actor, FalloutCombatGroupTransitionKind Kind, FalloutFormKey? Assistance)? _combatTargetOperation;
    private readonly Dictionary<FalloutFormKey, FalloutCombatActorIdentity> _combatActorSources = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutReferenceInstance, Action<FalloutActorEngagement?, FalloutActorEngagement?>> _combatGroupCallbacks = [];
    internal FalloutCombatGroups CombatGroups => _combatGroups ??
        throw new NotSupportedException("The shared source CombatManager list owner is absent.");
    internal bool CombatGroupsConfigured => _combatGroups is not null;
    internal object? CombatGroupState => _combatGroups?.State;
    internal string? CombatGroupSaveBlocker
    {
        get
        {
            if (_combatGroups is null) return "source-combat-group-owner-absent";
            ObserveRetiredCombatActors();
            return _combatGroups.SaveBlocker ?? (_combatGroups.HasActiveLists ?
                "source-combat-group-controller-auxiliary-and-target-timer-continuation-unowned" : null);
        }
    }

    internal void ConfigureCombatGroups(FalloutCombatGroupDeclaration declaration, string stackIdentity,
        FalloutCombatGroupsSnapshot? restore = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_combatGroups is not null) throw new InvalidOperationException("Combat group owner is already configured.");
        _combatGroupDeclaration = declaration;
        FalloutCombatGroups? candidate = null;
        try
        {
            candidate = new(declaration, stackIdentity, _enginePlayer,
                new(ReadCombatActorIdentity, ReadCombatGroupPredicate,
                    actor => actor != _enginePlayer && Actor(actor).PlayerTeammate,
                    actor => _playerGroupDetection?.Invoke(actor) ?? new(null, "source-player-detection-entry-level-unowned"),
                    (group, target) => _groupTargetRetirement?.Invoke(group, target) ??
                        new(null, "source-group-target-timer-detection-retirement-unowned")), restore);
            candidate.RequireCurrentTargets(CurrentCombatTargets());
        }
        catch
        {
            candidate?.Dispose(); _combatGroupDeclaration = null; _combatActorSources.Clear();
            throw;
        }
        _combatGroups = candidate;
        _combatGroupStackIdentity = stackIdentity;
        _combatGroupRestoreIdentity = restore;
        foreach (var instance in _instances.Values) BindCombatGroupInstance(instance);
    }

    internal void RequireCombatGroupBinding(FalloutCombatGroupDeclaration declaration, string stackIdentity,
        FalloutCombatGroupsSnapshot? restore)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_combatGroups is null || _combatGroupDeclaration != declaration || _combatGroupStackIdentity != stackIdentity ||
            !ReferenceEquals(_combatGroupRestoreIdentity, restore))
            throw new InvalidOperationException("Attached combat group owner differs from its original source/cold lifetime.");
    }

    // These inputs may bind only their actual source-backed living producers.
    // No current target or sight ray is converted into a detection level.
    internal void BindCombatGroupDecisions(Func<FalloutFormKey, FalloutCombatGroupDetection>? playerDetection,
        Func<FalloutFormKey, FalloutFormKey, FalloutCombatGroupPredicate>? reaction,
        Func<ulong, FalloutFormKey, FalloutCombatGroupPredicate>? retirement)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_combatGroupDecisionsBound)
            throw new InvalidOperationException("Source combat group decisions are already bound.");
        _playerGroupDetection = playerDetection; _groupReaction = reaction; _groupTargetRetirement = retirement;
        _combatGroupDecisionsBound = true;
    }

    private IEnumerable<(FalloutFormKey Actor, FalloutFormKey? Target)> CurrentCombatTargets() =>
        _instances.Values.Where(instance => records.GetEffective(instance.Reference).Signature is "ACHR" or "ACRE")
            .Select(instance => (instance.Reference, instance.Engagement?.Target));

    internal FalloutCombatGroupsSnapshot CaptureCombatGroups()
    {
        ObserveRetiredCombatActors();
        CombatGroups.RequireCurrentTargets(CurrentCombatTargets()); return CombatGroups.Capture();
    }
    internal FalloutExperienceLevelIntroInput ReadPlayerCombatGroupTargets()
    {
        ObserveRetiredCombatActors(); return CombatGroups.ReadPlayerTargets();
    }
    internal void AdvanceCombatGroups(float seconds)
    {
        ObserveRetiredCombatActors(); CombatGroups.Advance(seconds);
        if (CombatGroups.SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
    }

    private void ObserveRetiredCombatActors()
    {
        foreach (var actor in CombatGroups.ActorReferences)
            if (actor != _enginePlayer && _instances.TryGetValue(actor, out var instance) && instance.Deleted)
                CombatGroups.ObserveActorRetirement(actor, "actual-retained-reference-deleted-group-owner-unowned");
    }

    private void BindCombatGroupInstance(FalloutReferenceInstance instance)
    {
        if (_combatGroups is null) return;
        Action<FalloutActorEngagement?, FalloutActorEngagement?> callback =
            (previous, next) => OnCombatGroupTargetChanged(instance, previous, next);
        _combatGroupCallbacks[instance] = callback;
        instance.CombatGroupTargetChanged = callback;
    }

    private void OnCombatGroupTargetChanged(FalloutReferenceInstance instance,
        FalloutActorEngagement? previous, FalloutActorEngagement? next)
    {
        if (_combatGroups is null) return;
        var operation = _combatTargetOperation;
        var actual = operation is { } current && current.Actor == instance.Reference;
        CombatGroups.ObserveTarget(instance.Reference, previous?.Target, next?.Target,
            actual ? operation!.Value.Kind : next is null ? FalloutCombatGroupTransitionKind.OpaqueTargetEnd :
                FalloutCombatGroupTransitionKind.OpaqueTargetChange,
            actual ? operation!.Value.Assistance : null);
    }

    internal void SelectSourceCombatTarget(FalloutFormKey actor, FalloutFormKey target,
        FalloutCombatGroupTransitionKind kind = FalloutCombatGroupTransitionKind.ActorTarget,
        FalloutFormKey? assistanceOwner = null)
    {
        _ = CombatGroups;
        var instance = Actor(actor); _ = ReadCombatActorIdentity(target);
        if (_combatTargetOperation is not null) throw new InvalidOperationException("Combat target publication reentered its source operation.");
        if (kind is not (FalloutCombatGroupTransitionKind.ActorTarget or FalloutCombatGroupTransitionKind.AssistanceJoin or
            FalloutCombatGroupTransitionKind.CompanionJoin)) throw new InvalidDataException("Combat start operation is invalid.");
        _combatTargetOperation = (actor, kind, assistanceOwner);
        try { instance.Engagement = instance.Engagement is { } current ? current with { Target = target } : new(target); }
        finally { _combatTargetOperation = null; }
    }

    internal void ObserveCombatHit(FalloutFormKey target, FalloutFormKey attacker)
    {
        // The original player-hit candidate path is distinct from controller
        // acquisition. Trap/projectile owners are not fabricated actor members.
        if (attacker != _enginePlayer) return;
        _ = ReadCombatActorIdentity(target); _ = ReadCombatActorIdentity(attacker);
        CombatGroups.ObservePlayerTargetCandidate(target);
    }

    private FalloutCombatGroupPredicate ReadCombatGroupPredicate(FalloutFormKey member, FalloutFormKey target)
    {
        if (member == target) return new(false, "source-group-member-target-disjoint");
        if (target != _enginePlayer && (member == _enginePlayer || Actor(member).PlayerTeammate))
            return new(true, "source-player-or-teammate-nonplayer-target-fast-path");
        // Both original compilers return directly for Aggression=3 before
        // their reaction/perk entry-point arm. Confidence is not this getter.
        if (member != _enginePlayer && ActorValue(member, "aggression") == 3)
            return new(true, "source-frenzied-group-target-fast-path");
        return _groupReaction?.Invoke(member, target) ??
            new(null, "source-group-reaction-player-hostility-and-perk-entry15-unowned");
    }

    private FalloutCombatActorIdentity ReadCombatActorIdentity(FalloutFormKey actor)
    {
        if (_combatActorSources.TryGetValue(actor, out var cached)) return cached;
        var declaration = _combatGroupDeclaration ?? throw new InvalidOperationException("Combat actor source declaration is absent.");
        var identity = FalloutCombatActorSource.Read(records, declaration, actor);
        if (actor != _enginePlayer && Actor(actor).Base != identity.Base)
            throw new InvalidDataException("Current combat actor base differs from its exact winning placed declaration.");
        identity.Validate(); _combatActorSources.Add(actor, identity); return identity;
    }

    private void RetireCombatGroups()
    {
        _combatGroups?.Dispose(); _combatGroups = null; _combatGroupDeclaration = null;
        _combatGroupStackIdentity = null; _combatGroupRestoreIdentity = null;
        _playerGroupDetection = null; _groupReaction = null; _groupTargetRetirement = null;
        _combatGroupDecisionsBound = false;
        _combatTargetOperation = null; _combatActorSources.Clear();
        // Cold validation transfers actual reference instances to a different
        // world. Retire only callbacks still owned by this world; disposing the
        // temporary validator must not detach the destination's live callback.
        foreach (var (instance, callback) in _combatGroupCallbacks)
            if (ReferenceEquals(instance.CombatGroupTargetChanged, callback)) instance.CombatGroupTargetChanged = null;
        _combatGroupCallbacks.Clear();
    }
}
