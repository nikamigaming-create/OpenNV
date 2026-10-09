using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private sealed class NativePerceptionBinding(long lease, string owner,
        Func<long, FalloutPerceptionNativeObservation> observe,
        Func<FalloutDetectionLightSample>? light)
    {
        internal long Lease { get; } = lease;
        internal string Owner { get; } = owner;
        internal Func<long, FalloutPerceptionNativeObservation> Observe { get; } = observe;
        internal Func<FalloutDetectionLightSample>? Light { get; } = light;
    }
    private FalloutActorPerception? _actorPerception;
    private FalloutActorPerceptionDeclaration? _perceptionDeclaration;
    private string? _perceptionStack;
    private FalloutActorPerceptionSnapshot? _perceptionRestore;
    private long _perceptionNativeEpoch;
    private readonly Dictionary<FalloutFormKey, NativePerceptionBinding> _nativePerception = new(FalloutFormKeyComparer.Instance);
    private object? _perceptionSourceLease;
    private Func<FalloutFormKey, FalloutReferencePlacement>? _perceptionPlacement;
    private Func<FalloutFormKey, FalloutFormKey, long, long, FalloutPerceptionPairInput>? _perceptionPair;
    private Func<FalloutFormKey, FalloutPerceptionControllerNotification>? _perceptionController;
    private Func<float, FalloutPerceptionSourceFrame>? _perceptionScheduler;

    internal FalloutActorPerception ActorPerception => _actorPerception ??
        throw new NotSupportedException("Original actor perception process/cache owner is absent.");
    internal bool ActorPerceptionConfigured => _actorPerception is not null;
    internal object? ActorPerceptionState => _actorPerception?.State;
    internal string? ActorPerceptionSaveBlocker => _actorPerception is null ? "source-actor-perception-owner-absent" :
        ActorPerception.SaveBlocker;

    private bool SelectedPerceptionActor(FalloutFormKey key)
    {
        if (key == _enginePlayer) return true;
        if (!records.TryGetEffective(key, out var placed) || placed.IsDeleted || placed.Signature is not ("ACHR" or "ACRE")) return false;
        var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(placed, "NAME"));
        return !basis.IsDeleted && (placed.Signature == "ACHR" && basis.Signature == "NPC_" ||
            placed.Signature == "ACRE" && basis.Signature == "CREA");
    }

    internal void ConfigureActorPerception(FalloutActorPerceptionDeclaration declaration, string stack,
        FalloutActorPerceptionSnapshot? restore = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_actorPerception is not null) throw new InvalidOperationException("Actor perception is already configured.");
        if (!CombatGroupsConfigured) throw new InvalidOperationException("Perception identity requires the actual CombatManager source owner.");
        var count = checked(records.EffectiveRecords("ACHR").Count() + records.EffectiveRecords("ACRE").Count() + 1);
        var candidate = new FalloutActorPerception(declaration, stack, _enginePlayer, count, SelectedPerceptionActor,
            ReadCombatActorIdentity, new(ReadPerceptionObservation, ReadPerceptionPair, NotifyPerceptionController,
                ReadPerceptionLight, () => FalloutDetectionScoreSettings.Read(records, declaration.Scalar),
                () => FalloutGameSettingFloats.Read(records, "fActorAlertSoundTimer")), restore);
        _actorPerception = candidate; _perceptionDeclaration = declaration; _perceptionStack = stack; _perceptionRestore = restore;
        try
        {
            foreach (var instance in _instances.Values.ToArray()) BindActorPerceptionInstance(instance);
            if (!_combatGroupDecisionsBound) BindCombatGroupDecisions(ReadPerceptionCombatDetection, ReadPerceptionReaction, null);
        }
        catch
        {
            candidate.Dispose(); _actorPerception = null; _perceptionDeclaration = null; _perceptionStack = null; _perceptionRestore = null;
            throw;
        }
    }
    internal void RequireActorPerceptionBinding(FalloutActorPerceptionDeclaration declaration, string stack,
        FalloutActorPerceptionSnapshot? restore)
    {
        if (_actorPerception is null || _perceptionDeclaration != declaration || _perceptionStack != stack ||
            !ReferenceEquals(_perceptionRestore, restore))
            throw new InvalidDataException("Attached perception differs from its selected source/cold lifetime.");
    }
    private void BindActorPerceptionInstance(FalloutReferenceInstance instance)
    {
        if (_actorPerception is not null && SelectedPerceptionActor(instance.Reference) && !ActorPerception.HasActor(instance.Reference))
            ActorPerception.Construct(instance.Reference);
        BindSourceActorProcessInstance(instance);
    }
    private void EnsurePerceptionActor(FalloutFormKey actor)
    {
        // Materialize the real reference before entering the perception
        // constructor. Get's join then cannot recursively construct itself.
        if (actor != _enginePlayer) _ = Actor(actor);
        if (!ActorPerception.HasActor(actor)) ActorPerception.Construct(actor);
        if (_actorProcesses is not null && !ActorProcesses.HasActor(actor)) ActorProcesses.Construct(actor);
    }

    internal IDisposable BindPerceptionSourceInputs(Func<FalloutFormKey, FalloutReferencePlacement> placement,
        Func<FalloutFormKey, FalloutFormKey, long, long, FalloutPerceptionPairInput>? pair = null,
        Func<FalloutFormKey, FalloutPerceptionControllerNotification>? controller = null,
        Func<float, FalloutPerceptionSourceFrame>? scheduler = null)
    {
        if (_perceptionPlacement is not null) throw new InvalidOperationException("Perception source inputs are already bound.");
        _perceptionPlacement = placement ?? throw new ArgumentNullException(nameof(placement));
        _perceptionPair = pair; _perceptionController = controller; _perceptionScheduler = scheduler;
        var lease = new object(); _perceptionSourceLease = lease;
        return new SourcePerceptionLease(this, lease);
    }
    private sealed class SourcePerceptionLease(FalloutReferenceWorld world, object lease) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (!ReferenceEquals(world._perceptionSourceLease, lease)) return;
            world._perceptionSourceLease = null;
            world._perceptionPlacement = null; world._perceptionPair = null;
            world._perceptionController = null; world._perceptionScheduler = null;
        }
    }

    // Each native binding carries a process-local epoch. An older door/body
    // retirement cannot detach a newer binding, and no cold delegate survives.
    internal IDisposable BindNativePerception(FalloutFormKey actor, string owner,
        Func<long, FalloutPerceptionNativeObservation> observe, Func<FalloutDetectionLightSample>? light = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner); ArgumentNullException.ThrowIfNull(observe);
        if (!SelectedPerceptionActor(actor)) throw new InvalidDataException("Native perception actor is outside the winning source domain.");
        EnsurePerceptionActor(actor);
        var epoch = checked(++_perceptionNativeEpoch);
        var candidate = new NativePerceptionBinding(epoch, owner, observe, light);
        // Validate before replacing the currently owned producer.
        var observation = ReadNativePerception(actor, candidate);
        _nativePerception.TryGetValue(actor, out var previous);
        _nativePerception[actor] = candidate;
        try { ActorPerception.ObserveNative(observation); }
        catch
        {
            if (previous is null) _nativePerception.Remove(actor); else _nativePerception[actor] = previous;
            throw;
        }
        ObserveActorProcessSource3D(actor);
        return new NativePerceptionLease(this, actor, candidate);
    }
    private sealed class NativePerceptionLease(FalloutReferenceWorld world, FalloutFormKey actor,
        NativePerceptionBinding binding) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!world._nativePerception.TryGetValue(actor, out var current) || !ReferenceEquals(current, binding)) return;
            world._nativePerception.Remove(actor);
            if (world._actorPerception is not null)
            {
                world.ActorPerception.RetireNative(actor, binding.Owner);
                world.ObserveActorProcessSource3D(actor);
            }
        }
    }
    private static FalloutPerceptionNativeObservation ReadNativePerception(FalloutFormKey actor, NativePerceptionBinding binding)
    {
        var observation = binding.Observe(binding.Lease); observation.Validate();
        if (observation.Actor != actor || observation.NativeLease != binding.Lease || observation.Owner != binding.Owner || !observation.HasSource3D)
            throw new InvalidDataException("Native perception publication has a foreign or retired lease.");
        return observation;
    }
    private FalloutPerceptionNativeObservation ReadPerceptionObservation(FalloutFormKey actor)
    {
        if (_nativePerception.TryGetValue(actor, out var native)) return ReadNativePerception(actor, native);
        if (ActorPerception.RequiresNativePublication(actor)) throw new NotSupportedException("Cold perception still requires its genuine new native actor publication: " + actor);
        var placement = (_perceptionPlacement ?? throw new NotSupportedException("Unloaded perception has no current source placement owner."))(actor);
        placement.Validate();
        // A constructed process with no native 3D is known absence. It is not
        // inferred from an unavailable/cold binding that formerly owned 3D.
        var inCombat = actor == _enginePlayer ? PlayerInCombat() : IsInCombat(actor);
        return new(actor, "source-constructed-unloaded-actor", 0, placement.Cell, placement.Position.ToArray(), placement.RotationRadians[2],
            false, false, inCombat, false, false, false, actor != _enginePlayer && IsDead(actor));
    }
    private FalloutPerceptionPairInput ReadPerceptionPair(FalloutFormKey from, FalloutFormKey to, long fromEpoch, long toEpoch) =>
        _perceptionPair?.Invoke(from, to, fromEpoch, toEpoch) ?? new(from, to, fromEpoch, toEpoch, null,
            new(null, null, "source-collision-filter-cone-owner-absent"), "source-pair-sensory-inputs",
            "Original current values, filtered visibility, effect flags, affecting-light membership and source scheduler remain required.");
    private FalloutPerceptionControllerNotification NotifyPerceptionController(FalloutFormKey actor) =>
        _perceptionController?.Invoke(actor) ?? new(null, null, "source-detection-controller-notification-absent");
    private FalloutDetectionLightSample ReadPerceptionLight(FalloutFormKey actor) =>
        _nativePerception.TryGetValue(actor, out var native) && native.Light is not null ? native.Light() :
            throw new NotSupportedException("Actor detection requires its actual affecting-light membership: " + actor);

    internal void AdvanceActorPerception(float seconds)
    {
        ActorPerception.AdvanceClocks(seconds);
        foreach (var instance in _instances.Values.Where(instance => instance.Deleted && SelectedPerceptionActor(instance.Reference)).ToArray())
        {
            _nativePerception.Remove(instance.Reference);
            ActorProcesses.Retire(instance.Reference, "actual-reference-deletion-retirement");
        }
        foreach (var (actor, native) in _nativePerception.ToArray()) ActorPerception.ObserveNative(ReadNativePerception(actor, native));
        // This actual scheduler owns only its reached component. It does
        // not publish a completed whole-Main/light/pair frame receipt.
        AdvanceActualActorProcessSchedule(seconds);
    }
    internal FalloutActorPerceptionSnapshot CaptureActorPerception() => ActorPerception.Capture();
    internal FalloutDetectionProcessLevel? ReadSourceActorProcessLevel(FalloutFormKey actor)
    {
        return ReadAdmittedSourceActorProcessLevel(actor);
    }
    internal FalloutCombatGroupDetection ReadPerceptionCombatDetection(FalloutFormKey target) => ActorPerception.ReadCombatDetection(target);
    internal int GetDetected(FalloutFormKey receiver, FalloutFormKey target, Action<FalloutFormKey> preparePerkRead)
    {
        var player = ReadPerceptionObservation(_enginePlayer);
        if (!SelectedPerceptionActor(receiver) || !SelectedPerceptionActor(target)) return 0;
        EnsurePerceptionActor(receiver); EnsurePerceptionActor(target);
        return ActorPerception.GetDetected(receiver, target, target != _enginePlayer && Actor(target).PlayerTeammate,
            player.Sneaking, player.RawInCombat, preparePerkRead);
    }
    private void RetireActorPerception()
    {
        _nativePerception.Clear(); _actorPerception?.Dispose(); _actorPerception = null;
        _perceptionDeclaration = null; _perceptionStack = null; _perceptionRestore = null;
        _perceptionSourceLease = null;
        _perceptionPlacement = null; _perceptionPair = null; _perceptionController = null; _perceptionScheduler = null;
        _perceptionPairPerkPreparation = null; _perceptionPerk15 = null;
    }
}
