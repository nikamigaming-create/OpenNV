using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed partial class FalloutEarlyControllerFactory
{
    private FalloutEarlyControllerFactorySnapshot? _restoredFactory;
    private readonly Dictionary<Guid, FalloutEarlyControllerSnapshot> _pendingColdControllers = [];
    private readonly List<FalloutEarlyControllerSnapshot> _historicalControllers = [];
    private readonly List<FalloutEarlyControllerColdBinding> _coldBindings = [];
    private readonly HashSet<Guid> _coldInvocations = [];
    private FalloutEarlyControllerColdInvocation? _enteredCold;
    private Action? _requireColdShapeBinding;
    internal FalloutEarlyControllerColdBinding? LastColdBinding { get; private set; }
    internal FalloutEarlyControllerConstructionPhase ColdConstructionPhase { get; private set; }

    private void RestoreFactoryHistory(FalloutEarlyControllerFactorySnapshot saved)
    {
        ValidateColdFactory(saved, _source, Inputs);
        if (saved.Retired || saved.Factory == Identity || saved.Groups != _groups.Capture())
            throw new InvalidDataException("Cold early factory reused a retired/native identity or lost its actual restored collision-group counter.");
        _restoredFactory = saved; Shared.Restore(saved.Shared); _failure = saved.Failure;
        _calls.AddRange(saved.Calls); LastCall = _calls.LastOrDefault();
        foreach (var call in _calls) _invocations.Add(call.Invocation.Initialization);
        _historicalControllers.AddRange(saved.Controllers); _retiredFailedPrefixes.AddRange(saved.FailedPrefixes);
        _coldBindings.AddRange(saved.ColdBindings); LastColdBinding = _coldBindings.LastOrDefault();
        foreach (var binding in _coldBindings) _coldInvocations.Add(binding.Invocation.Binding);
        foreach (var controller in saved.Controllers.Where(value => value.Lineage.Factory == saved.Factory && !value.Retired))
            _pendingColdControllers.Add(controller.Identity, controller);
        _shapeCache.RetainColdHistory(saved.ShapeCalls, saved.ColdShapeCalls);
        // Historical scopes belong to the same fresh Scene thread owner.
        // Root imports them before current reads; this factory never replays them.
    }
    private IReadOnlyList<FalloutEarlyControllerSnapshot> CaptureColdControllerHistory() =>
        _historicalControllers.Concat(_retiredControllers).Concat(_controllers.Select(value => value.Capture())).ToArray();

    internal IFalloutEarlyControllerLease RebindColdController(FalloutEarlyControllerColdInvocation invocation,
        FalloutEarlyControllerSnapshot saved, object actualActorOwner,
        Action<FalloutEarlyControllerColdInvocation> requireActualColdBinding, Action requireActualActorOwner)
    {
        RequireLifetime(); ArgumentNullException.ThrowIfNull(invocation); ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(actualActorOwner); ArgumentNullException.ThrowIfNull(requireActualColdBinding);
        ArgumentNullException.ThrowIfNull(requireActualActorOwner);
        if (_busy) { ++_fault; throw new InvalidOperationException("Actual cold controller resource binding reentered."); }
        if (_failure is not null) throw new InvalidOperationException("Cold early controller retains its original failure: " + _failure);
        if (_restoredFactory is null || !_pendingColdControllers.TryGetValue(saved.Identity, out var expected) ||
            JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(saved))
            throw new InvalidDataException("Cold controller has no exact retained factory/current-state association.");
        ValidateDetachedState(saved, _source, Inputs);
        if (saved.Retired || saved.Failure is not null || !Shared.Capture().CenterAvailable ||
            !Shared.Capture().SlopeAvailable || !Shared.Capture().HelperFlagAvailable)
            throw new InvalidOperationException("Cold controller has no returned live source state to bind.");
        RequireColdArrays(saved.Proxy);
        var current = invocation.Current; var fault = _fault; var counter = _groups.Capture().Counter;
        var callsBefore = _calls.Count; var scopesBefore = _threadContext.CapturePhysicsInitializationScopes();
        if (!scopesBefore.SequenceEqual(_restoredFactory.PhysicsScopes))
            throw new InvalidDataException("Cold controller has no exact imported historical physics-thread scopes.");
        var lineage = new FalloutEarlyControllerLineage(_source.Identity, current.Stack, Identity, current.CommonProcess,
            current.RuntimeProcess, current.ThreadProcess, _threadContext.Factory, current.Actor, current.Epoch,
            current.Level, current.Constructor, current.ThreadId);
        var prefix = new FailedPrefix(); FalloutEarlyController? controller = null;
        _busy = true; _enteredCold = invocation; _requireColdShapeBinding = Guard;
        ColdConstructionPhase = FalloutEarlyControllerConstructionPhase.Entered;
        try
        {
            Guard(); if (!_coldInvocations.Add(invocation.Binding))
                throw new InvalidOperationException("Cold controller resource binding replayed.");
            _manager ??= new FalloutCharacterStateManager();
            prefix.State = _manager.RebindContext(saved.State); Phase(FalloutEarlyControllerConstructionPhase.StateContext);
            prefix.Contact = new(saved.Contact); Phase(FalloutEarlyControllerConstructionPhase.Contact);
            prefix.Shapes = _shapeCache.RebindColdResources(new(invocation.Binding, current.Actor, current.CommonProcess,
                current.Epoch, current.Constructor, current.ThreadId), saved.Descriptor.Player); Phase(FalloutEarlyControllerConstructionPhase.Shapes);
            prefix.Phantom = new(prefix.Shapes, saved.Phantom.Filter, Guard); Phase(FalloutEarlyControllerConstructionPhase.PhantomAllocated);
            prefix.Phantom.Initialize(Guard); prefix.Phantom.RebindTransform(saved.Phantom); Phase(FalloutEarlyControllerConstructionPhase.PhantomInitialized);
            prefix.Proxy = new(prefix.Phantom, saved.Proxy); Phase(FalloutEarlyControllerConstructionPhase.ProxyAllocated); prefix.Proxy.Initialize();
            Phase(FalloutEarlyControllerConstructionPhase.ProxyRetained);
            prefix.Proxy.AddContactListener(prefix.Contact); Phase(FalloutEarlyControllerConstructionPhase.ContactListener);
            if (saved.Phantom.Properties.Any(value => value.Key == FalloutEarlyControllerSource.ActorPropertyKey))
            {
                requireActualActorOwner(); prefix.Phantom.AddPropertyIfAbsent(FalloutEarlyControllerSource.ActorPropertyKey,
                    actualActorOwner, current.Actor); requireActualActorOwner(); Guard();
            }
            controller = new(this, lineage, saved.Descriptor, prefix.State, prefix.Contact, prefix.Shapes, prefix.Phantom, prefix.Proxy);
            controller.RestoreColdFields(saved); _controllers.Add(controller); Phase(FalloutEarlyControllerConstructionPhase.Controller);
            var receipt = Receipt(true, null); var lease = controller.Retain();
            LastColdBinding = receipt; _coldBindings.Add(receipt);
            _pendingColdControllers.Remove(saved.Identity); ColdConstructionPhase = FalloutEarlyControllerConstructionPhase.Returned; return lease;
        }
        catch (Exception error)
        {
            _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            if (controller is null) _failedPrefixes.Add(prefix);
            try { LastColdBinding = Receipt(false, _failure); _coldBindings.Add(LastColdBinding); }
            catch { /* Diagnostic capture cannot replace the original failed child. */ }
            throw;
        }
        finally { _requireColdShapeBinding = null; _enteredCold = null; _busy = false; }

        void Phase(FalloutEarlyControllerConstructionPhase phase) { ColdConstructionPhase = phase; Guard(); }
        void Guard()
        {
            if (_fault != fault || invocation.Binding == Guid.Empty || current.Initialization == Guid.Empty ||
                current.CommonProcess == Guid.Empty || current.RuntimeProcess == Guid.Empty || current.ThreadProcess != _threadContext.Process ||
                current.Actor != saved.Lineage.Actor || current.Stack != saved.Lineage.Stack || current.Level != saved.Lineage.Level ||
                current.Epoch != saved.Lineage.Epoch || current.Constructor == Guid.Empty || current.ThreadId != _thread ||
                current.CommonProcess == saved.Lineage.CommonProcess || current.RuntimeProcess == saved.Lineage.RuntimeProcess ||
                current.ThreadProcess == saved.Lineage.ThreadProcess || current.Constructor == saved.Lineage.Constructor ||
                _threadContext.Factory == saved.Lineage.ThreadFactory || _calls.Count != callsBefore || _groups.Capture().Counter != counter ||
                !_threadContext.CapturePhysicsInitializationScopes().SequenceEqual(scopesBefore))
                throw new InvalidOperationException("Cold controller lost a fresh process/resource owner or replayed source constructor/group/thread stores.");
            requireActualColdBinding(invocation); RequireLifetime();
            if (_fault != fault) throw new InvalidOperationException("Cold controller guard swallowed source/resource reentry.");
        }
        FalloutEarlyControllerColdBinding Receipt(bool returned, string? failure) => new(invocation,
            saved.Lineage.Factory, saved.Identity, saved.Lineage.CommonProcess, saved.Lineage.RuntimeProcess,
            saved.Lineage.ThreadProcess, saved.Lineage.ThreadFactory, saved.State.Manager, saved.Phantom.Identity, saved.Proxy.Identity,
            saved.Phantom.NativeId, saved.Phantom.ShapeNativeIds.ToArray(), lineage, controller?.Identity, prefix.State?.Snapshot.Manager,
            prefix.Phantom?.Identity, prefix.Proxy?.Identity, prefix.Phantom?.NativeId,
            prefix.Phantom?.Capture().ShapeNativeIds ?? [], counter, _groups.Capture().Counter, callsBefore, _calls.Count,
            scopesBefore.Count, _threadContext.CapturePhysicsInitializationScopes().Count, returned, failure);
    }

    internal static void ValidateColdFactory(FalloutEarlyControllerFactorySnapshot saved,
        FalloutEarlyControllerSource source, FalloutPhysicalConstructorInputs inputs)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(saved); ArgumentNullException.ThrowIfNull(inputs);
        if (saved.Source != source.Identity || saved.Factory == Guid.Empty || saved.Calls is null || saved.Controllers is null ||
            saved.FailedPrefixes is null || saved.Shared is null || saved.Groups is null || saved.PhysicsScopes is null ||
            saved.ShapeCalls is null || saved.ColdShapeCalls is null || saved.ColdBindings is null || saved.PendingColdControllers is null ||
            saved.Shared.StandardCenter != FalloutEarlyControllerSource.InitialStandardCenter ||
            saved.Shared.SlopeBits != FalloutEarlyControllerSource.InitialSlopeBits || saved.Shared.HelperFlag != FalloutEarlyControllerSource.InitialHelperFlag ||
            saved.Groups.EngineSha256 != inputs.EngineSha256 || saved.Groups.Schema != FalloutCharacterCollisionGroups.Schema ||
            saved.Groups.Counter < FalloutCharacterCollisionGroups.InitialCounter)
            throw new InvalidDataException("Cold early factory changed its exact source/shared/counter/history declarations.");
        var calls = new HashSet<Guid>(); var controllers = new Dictionary<Guid, FalloutEarlyControllerSnapshot>();
        var bindings = new HashSet<Guid>();
        FalloutPlayerThreadContextState.ValidatePhysicsInitializationScopes(saved.PhysicsScopes, source.Thread);
        foreach (var call in saved.Calls)
        {
            if (call.Invocation is null || call.Lineage is null || !calls.Add(call.Invocation.Initialization) ||
                !InvocationMatches(call.Invocation, call.Lineage) || call.Lineage.Source != source.Identity ||
                call.Returned && (call.Controller is null || call.PhantomNativeId is null || call.Descriptor is null ||
                    call.ThreadScope is not { Returned: true } || call.Failure is not null ||
                    call.ReachedPhase != FalloutEarlyControllerConstructionPhase.Returned) ||
                !call.Returned && string.IsNullOrWhiteSpace(call.Failure))
                throw new InvalidDataException("Cold early factory lost its original actual constructor/failed prefix.");
            if (call.ThreadScope is { } scope && (!saved.PhysicsScopes.Contains(scope) ||
                scope.Process != call.Lineage.ThreadProcess || scope.Factory != call.Lineage.ThreadFactory ||
                scope.OwnerFactory != call.Lineage.Factory || scope.Invocation != call.Invocation.Initialization))
                throw new InvalidDataException("Cold constructor changed its retained actual physics kind scope.");
        }
        foreach (var controller in saved.Controllers)
        {
            if (!controllers.TryAdd(controller.Identity, controller))
                throw new InvalidDataException("Cold controller history duplicated a resource owner.");
            ValidateDetachedState(controller, source, inputs);
            if (!saved.Calls.Any(call => call.Controller == controller.Identity && call.Lineage == controller.Lineage) &&
                !saved.ColdBindings.Any(binding => binding.Controller == controller.Identity && binding.Current == controller.Lineage))
                throw new InvalidDataException("Cold controller has no original constructor or actual resource binding.");
        }
        foreach (var binding in saved.ColdBindings)
        {
            if (binding.Invocation is null || binding.Current is null || binding.Invocation.Binding == Guid.Empty ||
                !bindings.Add(binding.Invocation.Binding) || calls.Contains(binding.Invocation.Binding) ||
                !InvocationMatches(binding.Invocation.Current, binding.Current) || binding.Current.Source != source.Identity ||
                !controllers.TryGetValue(binding.PreviousController, out var previous) ||
                binding.PreviousFactory != previous.Lineage.Factory || binding.PreviousCommonProcess != previous.Lineage.CommonProcess ||
                binding.PreviousRuntimeProcess != previous.Lineage.RuntimeProcess || binding.PreviousThreadProcess != previous.Lineage.ThreadProcess ||
                binding.PreviousThreadFactory != previous.Lineage.ThreadFactory || binding.PreviousManager != previous.State.Manager ||
                binding.PreviousPhantom != previous.Phantom.Identity || binding.PreviousProxy != previous.Proxy.Identity ||
                binding.PreviousNativeArea != previous.Phantom.NativeId || binding.PreviousNativeShapes is null ||
                !binding.PreviousNativeShapes.SequenceEqual(previous.Phantom.ShapeNativeIds) ||
                binding.GroupCounterBefore != binding.GroupCounterAfter || binding.OriginalCallsBefore != binding.OriginalCallsAfter ||
                binding.OriginalCallsBefore != saved.Calls.Count || binding.OriginalScopesBefore != binding.OriginalScopesAfter ||
                binding.OriginalScopesBefore < 0 || binding.OriginalScopesBefore > saved.PhysicsScopes.Count ||
                binding.Current.Factory == binding.PreviousFactory || binding.Current.CommonProcess == binding.PreviousCommonProcess ||
                binding.Current.RuntimeProcess == binding.PreviousRuntimeProcess || binding.Current.ThreadProcess == binding.PreviousThreadProcess ||
                binding.Current.ThreadFactory == binding.PreviousThreadFactory || binding.NativeShapes is null ||
                binding.Returned && (binding.Failure is not null || binding.Controller is not { } id ||
                    !controllers.TryGetValue(id, out var current) || current.Lineage != binding.Current ||
                    binding.Manager != current.State.Manager || binding.Phantom != current.Phantom.Identity ||
                    binding.Proxy != current.Proxy.Identity || binding.NativeArea != current.Phantom.NativeId ||
                    !binding.NativeShapes.SequenceEqual(current.Phantom.ShapeNativeIds)) ||
                !binding.Returned && string.IsNullOrWhiteSpace(binding.Failure))
                throw new InvalidDataException("Cold resource receipt changed its retained/fresh owners or replayed original stores.");
        }
        foreach (var call in saved.ShapeCalls) ValidateShapeCall(call, inputs);
        foreach (var call in saved.ColdShapeCalls)
        {
            ValidateShapeCall(call.Resources, inputs);
            if (!bindings.Contains(call.Resources.Invocation.Initialization))
                throw new InvalidDataException("Cold native shapes lost their actual fresh binding receipt.");
        }
        var pending = new HashSet<Guid>();
        foreach (var id in saved.PendingColdControllers)
            if (!pending.Add(id) || !controllers.ContainsKey(id))
                throw new InvalidDataException("Cold factory duplicated or lost an unresolved native-resource binding.");
        if (saved.Manager is { } manager)
        {
            FalloutCharacterStateSnapshot?[] expectedSlots = new FalloutCharacterStateSnapshot?[FalloutEarlyControllerSource.ManagerSlots];
            expectedSlots[0] = new(0, true, null); expectedSlots[1] = new(1, null, null);
            expectedSlots[2] = new(2, null, FalloutEarlyControllerSource.State2ScalarBits);
            for (byte kind = 3; kind <= 6; ++kind) expectedSlots[kind] = new(kind, null, null);
            byte[] expectedOrder = [0, 5, 1, 2, 3, 4, 6];
            if (manager.Factory == Guid.Empty || manager.Slots is null || manager.RegistrationOrder is null ||
                !manager.Slots.SequenceEqual(expectedSlots) || !manager.RegistrationOrder.SequenceEqual(expectedOrder) ||
                manager.ActiveContexts < 0)
                throw new InvalidDataException("Cold factory changed its actual shared state objects/registrations.");
        }
    }
    private static bool InvocationMatches(FalloutEarlyControllerInvocation invocation, FalloutEarlyControllerLineage lineage) =>
        invocation.Initialization != Guid.Empty && lineage.Factory != Guid.Empty && lineage.ThreadFactory != Guid.Empty &&
        invocation.Actor == lineage.Actor && invocation.Stack == lineage.Stack && invocation.CommonProcess == lineage.CommonProcess &&
        invocation.RuntimeProcess == lineage.RuntimeProcess && invocation.ThreadProcess == lineage.ThreadProcess &&
        invocation.Epoch == lineage.Epoch && invocation.Level == lineage.Level && invocation.Constructor == lineage.Constructor &&
        invocation.ThreadId == lineage.Thread;
    private static void ValidateShapeCall(FalloutEarlyControllerShapeCall call, FalloutPhysicalConstructorInputs inputs)
    {
        if (call is null || call.Invocation is null || call.Inputs is null || call.Allocated is null || call.ReturnedNativeShapes is null ||
            call.Invocation.Initialization == Guid.Empty || call.Invocation.Process == Guid.Empty || call.Invocation.Constructor == Guid.Empty ||
            call.Invocation.Epoch < 1 || call.Invocation.ThreadId < 1 ||
            call.Inputs != inputs.ProjectPlayer(call.Inputs.ReferencePosition, call.Inputs.StandardCenter) ||
            call.Allocated.Any(value => value == 0) || call.Allocated.Distinct().Count() != call.Allocated.Count ||
            call.ReturnedNativeShapes.Any(value => value == 0) ||
            call.Returned && (call.Failure is not null || call.ReturnedNativeShapes.Count != NativeConvexCount(inputs, call.Inputs) + 1 ||
                call.ReturnedNativeShapes.Distinct().Count() != call.ReturnedNativeShapes.Count) ||
            !call.Returned && (string.IsNullOrWhiteSpace(call.Failure) || call.ReturnedNativeShapes.Count != 0))
            throw new InvalidDataException("Cold shape history changed its actual inputs/resources/failed prefix.");
        call.Invocation.Actor.Validate();
    }
    internal static void RequireColdArrays(FalloutEarlyControllerProxySnapshot proxy)
    {
        if (proxy.Arrays.Count != 4 || proxy.ContactListeners != 1)
            throw new InvalidDataException("Cold proxy omitted its actual four arrays/single contact child.");
        string[] roles = ["first", "contact-listeners", "third", "fourth"];
        for (var index = 0; index < proxy.Arrays.Count; ++index)
        {
            var array = proxy.Arrays[index];
            if (array.Role != roles[index] || array.Count < 0 || array.RetainedCapacity < array.Count ||
                (array.CapacityFlags & FalloutEarlyControllerSource.ArrayCapacityMask) != (uint)array.RetainedCapacity)
                throw new InvalidDataException("Cold proxy array lost its actual role/count/retained capacity.");
            if (index == 1)
            {
                if (array.Count != 1 || array.RetainedCapacity != 1 || array.CapacityFlags != 1)
                    throw new NotSupportedException("source-proxy-contact-array-additional-payload-owner-unbound");
            }
            else if (array.Count != 0 || array.RetainedCapacity != 0)
                throw new NotSupportedException("source-proxy-retained-array-payload-owner-unbound:" + array.Role);
            else if (array.CapacityFlags != FalloutEarlyControllerSource.ArrayEmptyFlag)
                throw new InvalidDataException("Cold source-empty proxy array lost its actual null-storage flag.");
        }
    }
    internal static void ValidateDetachedState(FalloutEarlyControllerSnapshot saved,
        FalloutEarlyControllerSource source, FalloutPhysicalConstructorInputs inputs)
    {
        if (saved is null || saved.Schema != FalloutEarlyController.Schema || saved.Lineage is null || saved.Descriptor is null ||
            saved.Descriptor.Player is null || saved.State is null || saved.Contact is null || saved.Phantom is null || saved.Proxy is null ||
            saved.Stores is null || saved.Resets is null || saved.Identity == Guid.Empty || saved.Lineage.Source != source.Identity ||
            saved.Lineage.Factory == Guid.Empty || saved.Lineage.CommonProcess == Guid.Empty || saved.Lineage.RuntimeProcess == Guid.Empty ||
            saved.Lineage.ThreadProcess == Guid.Empty || saved.Lineage.ThreadFactory == Guid.Empty || saved.Lineage.Constructor == Guid.Empty ||
            saved.Lineage.Epoch < 1 || saved.Lineage.Thread < 1 || string.IsNullOrWhiteSpace(saved.Lineage.Stack) ||
            saved.Lineage.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh) || !saved.Lineage.Actor.EnginePlayer ||
            saved.Descriptor.Player != inputs.ProjectPlayer(saved.Descriptor.Player.ReferencePosition, saved.Descriptor.Player.StandardCenter) ||
            saved.Phantom.CurrentWorld is not null || saved.Phantom.RequestedWorld is not null || saved.Descriptor.RequestedWorld is not null ||
            saved.Phantom.NativeId == 0 || saved.Phantom.ShapeNativeIds is null ||
            saved.Phantom.ShapeNativeIds.Count != NativeConvexCount(inputs, saved.Descriptor.Player) ||
            saved.Phantom.ShapeNativeIds.Any(value => value == 0) || saved.Phantom.ShapeNativeIds.Distinct().Count() != saved.Phantom.ShapeNativeIds.Count ||
            saved.Phantom.Properties is null || saved.Proxy.Phantom != saved.Phantom.Identity || saved.Phantom.Identity == Guid.Empty ||
            saved.Proxy.Identity == Guid.Empty || saved.Proxy.Arrays is null || saved.State.Manager == Guid.Empty ||
            saved.State.Current != FalloutEarlyControllerSource.InitialState || saved.State.Previous != FalloutEarlyControllerSource.InitialState ||
            !saved.State.Enabled || saved.State.ScalarABits != FalloutEarlyControllerSource.ContextScalarABits ||
            saved.State.ScalarBBits != FalloutEarlyControllerSource.ContextScalarBBits || saved.State.MultiplierBits != FalloutEarlyControllerSource.PositiveOneBits ||
            !Finite(saved.Phantom.Transform.Translation) ||
            saved.Phantom.Transform.Rotation != IdentityRotation() || saved.CanonicalLane != FalloutEarlyControllerConvexGeometry.CanonicalBoundary ||
            saved.Proxy.Up != new FalloutPhysicalVector4(FalloutPhysicalVector3.FromValues(0, 0, 1), 0) || !Finite(saved.Proxy.Velocity.Position) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(saved.Proxy.Velocity.W)) ||
            saved.Proxy.MaximumSlopeCosineBits != FalloutControllerContactState.MaximumSlopeCosine(FalloutEarlyControllerSource.MaximumSlopeBits))
            throw new InvalidDataException("Cold detached controller lost its source/native/state-context/descriptor lineage.");
        saved.Lineage.Actor.Validate();
        if (saved.Descriptor.Model is null || saved.Descriptor != Descriptor(saved.Descriptor.Player,
            saved.Descriptor.AllocatedFilter, saved.Descriptor.Model) ||
            saved.Descriptor.Model.Model is not null ||
            (saved.Descriptor.AllocatedFilter & 0xffff) != FalloutCharacterCollisionGroups.CharacterLayer ||
            saved.Phantom.Filter != saved.Descriptor.AllocatedFilter && saved.Phantom.Filter !=
                (((uint)FalloutEarlyControllerSource.PlayerCollisionGroup << 16) | FalloutCharacterCollisionGroups.CharacterLayer) ||
            saved.Contact != new FalloutControllerContactState(new(FalloutPhysicalVector3.FromValues(0, 0, 1), 0),
                FalloutEarlyControllerSource.InitialSlopeBits, saved.Descriptor.Player.StepBits).Snapshot ||
            saved.PlayerPropertySelector is not (0 or FalloutEarlyControllerSource.ActorPropertyKey))
            throw new InvalidDataException("Cold detached controller changed its entered descriptor, contact or current filter fields.");
        var keys = new HashSet<uint>();
        foreach (var property in saved.Phantom.Properties)
            if (!keys.Add(property.Key) || property.Key == FalloutEarlyControllerSource.ProxyPropertyKey &&
                (property.Kind != "proxy" || property.Proxy != saved.Proxy.Identity || property.Actor is not null) ||
                property.Key == FalloutEarlyControllerSource.ActorPropertyKey &&
                (property.Kind != "Actor" || property.Actor != saved.Lineage.Actor || property.Proxy is not null || string.IsNullOrWhiteSpace(property.ActorOwnerType)) ||
                property.Key is not (FalloutEarlyControllerSource.ProxyPropertyKey or FalloutEarlyControllerSource.ActorPropertyKey))
                throw new InvalidDataException("Cold phantom property lost its actual proxy/Actor source association.");
        if (!saved.Retired && (!keys.Contains(FalloutEarlyControllerSource.ProxyPropertyKey) || saved.Phantom.Retired || saved.Proxy.Retired))
            throw new InvalidDataException("Living cold controller omitted its actual retained proxy/property resources.");
        long sequence = 0;
        foreach (var store in saved.Stores)
            if (store.Sequence != ++sequence || string.IsNullOrWhiteSpace(store.Field))
                throw new InvalidDataException("Cold controller lost its ordered partial source stores.");
        foreach (var reset in saved.Resets)
            if (reset.Invocation is null || reset.Invocation.Call == Guid.Empty || reset.Invocation.Actor != saved.Lineage.Actor ||
                reset.Invocation.Epoch != saved.Lineage.Epoch || reset.Entered < 0 || reset.Exited < reset.Entered || reset.Exited > sequence ||
                reset.Returned != (reset.Failure is null))
                throw new InvalidDataException("Cold controller lost its actual retained reset/failed prefix.");
    }
    private static int NativeConvexCount(FalloutPhysicalConstructorInputs inputs, FalloutPlayerControllerConstructionInputs player)
    {
        var geometry = new FalloutEarlyControllerConvexGeometry(inputs.ProjectStandardConvex(player, 0));
        return 1 + geometry.Faces.Sum(value => value.Vertices.Count - 2) + geometry.Edges.Count + geometry.Vertices.Count;
    }
    private static FalloutPhysicalRotation IdentityRotation() => new(FalloutPhysicalVector3.FromValues(1, 0, 0),
        FalloutPhysicalVector3.FromValues(0, 1, 0), FalloutPhysicalVector3.FromValues(0, 0, 1));
    private static bool Finite(FalloutPhysicalVector3 value) =>
        float.IsFinite(value.XValue) && float.IsFinite(value.YValue) && float.IsFinite(value.ZValue);
}
