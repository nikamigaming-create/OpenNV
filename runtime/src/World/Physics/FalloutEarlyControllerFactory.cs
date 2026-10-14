using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed partial class FalloutEarlyControllerFactory
{
    private readonly FalloutEarlyControllerSource _source;
    private readonly FalloutCharacterCollisionGroups _groups;
    private readonly FalloutPlayerThreadContextState _threadContext;
    private readonly Action<FalloutEarlyControllerInvocation> _requireActualConstruction;
    private readonly Func<FalloutEarlyControllerInvocation, FalloutSourceSceneNode?> _readNullableReferenceModel;
    private readonly FalloutEarlyControllerShapeCache _shapeCache;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<FalloutEarlyControllerConstructionCall> _calls = [];
    private readonly Dictionary<Guid, FalloutEarlyControllerConstructionCall> _returnedCalls = [];
    private readonly HashSet<Guid> _invocations = [];
    private readonly HashSet<FalloutEarlyController> _controllers = [];
    private readonly List<FalloutEarlyControllerSnapshot> _retiredControllers = [];
    private readonly List<FailedPrefix> _failedPrefixes = [];
    private readonly List<FalloutEarlyControllerFailedPrefix> _retiredFailedPrefixes = [];
    private FalloutCharacterStateManager? _manager;
    private FalloutEarlyControllerInvocation? _entered;
    private long _fault;
    private bool _busy, _retired;
    private string? _failure;
    private object? _retiredShapeHistory;
    private FalloutCharacterCollisionGroupsSnapshot? _retiredGroups;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal FalloutPhysicalConstructorInputs Inputs { get; }
    internal FalloutEarlyControllerSharedFields Shared { get; }
    internal FalloutEarlyControllerConstructionCall? LastCall { get; private set; }
    internal FalloutEarlyControllerConstructionPhase ConstructionPhase { get; private set; }
    internal string? SaveBlocker => _busy ? "source-early-controller-construction-in-flight" : _failure ??
        Shared.SaveBlocker ?? (_retired ? "source-early-controller-factory-retired" :
        _pendingColdControllers.Count != 0 ? "source-early-controller-cold-native-resources-unbound" : null);
    internal FalloutEarlyControllerFactory(FalloutEarlyControllerSource source, FalloutPhysicalConstructorInputs inputs,
        FalloutCharacterCollisionGroups groups, FalloutPlayerThreadContextState threadContext, float sourceUnitsToMetres,
        Action<FalloutEarlyControllerInvocation> requireActualConstruction,
        Func<FalloutEarlyControllerInvocation, FalloutSourceSceneNode?> readNullableReferenceModel,
        FalloutEarlyControllerFactorySnapshot? saved = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(threadContext); ArgumentNullException.ThrowIfNull(requireActualConstruction);
        ArgumentNullException.ThrowIfNull(readNullableReferenceModel);
        if (inputs.EngineSha256 != source.Fields.Controls.Main.EngineSha256 || threadContext.Source != source.Thread)
            throw new InvalidDataException("Early physical factory changed its selected input/current-thread source owners.");
        _source = source; Inputs = inputs; _groups = groups; _threadContext = threadContext;
        Shared = new(source);
        _requireActualConstruction = requireActualConstruction; _readNullableReferenceModel = readNullableReferenceModel;
        _shapeCache = new(inputs, sourceUnitsToMetres, invocation =>
        {
            if (_enteredCold is { } cold)
            {
                var current = cold.Current;
                if (invocation.Initialization != cold.Binding || invocation.Actor != current.Actor || invocation.Process != current.CommonProcess ||
                    invocation.Epoch != current.Epoch || invocation.Constructor != current.Constructor || invocation.ThreadId != current.ThreadId)
                    throw new InvalidOperationException("Cold native shape construction lost its actual current binding.");
                _requireColdShapeBinding!(); return;
            }
            if (_entered is not { } actual || invocation.Initialization != actual.Initialization || invocation.Actor != actual.Actor ||
                invocation.Process != actual.CommonProcess || invocation.Epoch != actual.Epoch || invocation.Constructor != actual.Constructor ||
                invocation.ThreadId != actual.ThreadId) throw new InvalidOperationException("Shape factory lost its actual source controller construction.");
            _requireActualConstruction(actual);
        });
        if (saved is not null) RestoreFactoryHistory(saved);
    }
    internal IFalloutEarlyControllerLease Construct(FalloutEarlyControllerInvocation invocation, FalloutPlayerControllerConstructionInputs player)
    {
        RequireLifetime(); ArgumentNullException.ThrowIfNull(invocation); ArgumentNullException.ThrowIfNull(player);
        if (_restoredFactory is not null) throw new InvalidOperationException("source-early-controller-original-constructor-replay-on-cold");
        if (_busy) { ++_fault; throw new InvalidOperationException("Actual controller factory reentered."); }
        if (_failure is not null) throw new InvalidOperationException("Actual controller construction retains its original failure: " + _failure);
        var fault = _fault; _busy = true; _entered = invocation; ConstructionPhase = FalloutEarlyControllerConstructionPhase.Entered;
        var lineage = new FalloutEarlyControllerLineage(_source.Identity, invocation.Stack, Identity, invocation.CommonProcess,
            invocation.RuntimeProcess, invocation.ThreadProcess, _threadContext.Factory, invocation.Actor, invocation.Epoch,
            invocation.Level, invocation.Constructor, invocation.ThreadId);
        var prefix = new FailedPrefix(); FalloutEarlyControllerDescriptorSnapshot? descriptor = null;
        FalloutSourceThreadKindScope? scope = null; FalloutEarlyController? controller = null;
        try
        {
            Guard(); if (!_invocations.Add(invocation.Initialization)) throw new InvalidOperationException("Actual controller constructor invocation replayed.");
            var filter = _groups.AllocateCreationInfoFilter(); Phase(FalloutEarlyControllerConstructionPhase.GroupAllocated);
            _manager ??= new FalloutCharacterStateManager();
            var cache = _threadContext.ReadEarlyControllerModelCache(invocation.Initialization, Identity, invocation.Actor.Reference, Guard);
            var model = cache.Matched ? cache.Model : _readNullableReferenceModel(invocation); Guard();
            if (model is not null) throw new NotSupportedException("source-early-controller-current-model-motion-consumer-unbound");
            descriptor = Descriptor(player, filter, new(invocation, cache, model)); Phase(FalloutEarlyControllerConstructionPhase.Descriptor);
            prefix.State = _manager.ConstructContext(descriptor.State); Phase(FalloutEarlyControllerConstructionPhase.StateContext);
            prefix.Contact = new(new(FalloutPhysicalVector3.FromValues(0, 0, 1), 0), Shared.ReadSlope(),
                BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Multiply(BitConverter.UInt32BitsToSingle(player.StepBits),
                    BitConverter.UInt32BitsToSingle(FalloutEarlyControllerSource.PositiveOneBits))));
            Phase(FalloutEarlyControllerConstructionPhase.Contact);
            scope = _threadContext.EnterPhysicsInitialization(invocation.Initialization, Identity, Guard); Phase(FalloutEarlyControllerConstructionPhase.ThreadScope);
            prefix.Shapes = _shapeCache.Acquire(new(invocation.Initialization, invocation.Actor, invocation.CommonProcess,
                invocation.Epoch, invocation.Constructor, invocation.ThreadId), player); Phase(FalloutEarlyControllerConstructionPhase.Shapes);
            prefix.Phantom = new(prefix.Shapes, filter, Guard); Phase(FalloutEarlyControllerConstructionPhase.PhantomAllocated);
            prefix.Phantom.Initialize(Guard); Phase(FalloutEarlyControllerConstructionPhase.PhantomInitialized);
            prefix.Proxy = new(prefix.Phantom, descriptor); Phase(FalloutEarlyControllerConstructionPhase.ProxyAllocated);
            prefix.Proxy.Initialize(); Phase(FalloutEarlyControllerConstructionPhase.ProxyRetained);
            prefix.Proxy.AddContactListener(prefix.Contact); Phase(FalloutEarlyControllerConstructionPhase.ContactListener);
            controller = new(this, lineage, descriptor, prefix.State, prefix.Contact, prefix.Shapes, prefix.Phantom, prefix.Proxy);
            _controllers.Add(controller); Phase(FalloutEarlyControllerConstructionPhase.Controller);
            scope = _threadContext.ReturnPhysicsInitialization(scope, Guard); Phase(FalloutEarlyControllerConstructionPhase.RestoredThread);
            var lease = controller.Retain();
            ConstructionPhase = FalloutEarlyControllerConstructionPhase.Returned;
            LastCall = new(invocation, lineage, descriptor, scope, prefix.Phantom.NativeId, controller.Identity, true, null, ConstructionPhase);
            _calls.Add(LastCall); _returnedCalls.Add(invocation.Initialization, LastCall); return lease;
        }
        catch (Exception error)
        {
            _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            if (scope is { Returned: false })
                try { scope = _threadContext.RetainPhysicsInitializationFailure(scope, error); }
                catch { /* The original failed child remains the primary error. */ }
            if (controller is null) _failedPrefixes.Add(prefix);
            LastCall = new(invocation, lineage, descriptor, scope, prefix.Phantom?.NativeId, controller?.Identity, false, _failure, ConstructionPhase);
            _calls.Add(LastCall); throw;
        }
        finally { _entered = null; _busy = false; }

        void Phase(FalloutEarlyControllerConstructionPhase phase) { ConstructionPhase = phase; Guard(); }
        void Guard()
        {
            if (_fault != fault || invocation.Initialization == Guid.Empty || invocation.CommonProcess == Guid.Empty ||
                invocation.RuntimeProcess == Guid.Empty || invocation.ThreadProcess != _threadContext.Process || invocation.Constructor == Guid.Empty ||
                invocation.Epoch < 1 || invocation.ThreadId != _thread || !invocation.Actor.EnginePlayer ||
                invocation.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh) ||
                string.IsNullOrWhiteSpace(invocation.Stack) || player.StandardCenter != Shared.ReadCenter() ||
                player != Inputs.ProjectPlayer(player.ReferencePosition, player.StandardCenter))
                throw new InvalidOperationException("Controller construction lost its actual Actor/process/class/descriptor/thread source scope.");
            invocation.Actor.Validate(); _requireActualConstruction(invocation); RequireLifetime();
            if (_fault != fault) throw new InvalidOperationException("Controller construction guard swallowed a source reentry refusal.");
        }
    }
    private static FalloutEarlyControllerDescriptorSnapshot Descriptor(FalloutPlayerControllerConstructionInputs player, uint filter,
        FalloutEarlyControllerModelRead model) => new(player, filter, FalloutEarlyControllerSource.PositiveOneBits,
        FalloutEarlyControllerSource.PositiveOneBits, FalloutEarlyControllerSource.PositiveZeroBits,
        FalloutPhysicalConstructorInputs.ContactToleranceBits, FalloutPhysicalConstructorInputs.KeepDistanceBits,
        FalloutEarlyControllerSource.PositiveZeroBits, FalloutEarlyControllerSource.ContactIterations,
        FalloutEarlyControllerSource.StrengthBits, FalloutEarlyControllerSource.MaximumBoundBits,
        FalloutEarlyControllerSource.MaximumSlopeBits, FalloutEarlyControllerSource.PositiveOneBits, FalloutEarlyControllerSource.UserPlanes,
        new(FalloutPhysicalVector3.FromValues(0, 1, 0), 0), FalloutEarlyControllerSource.InitialState,
        FalloutPhysicalConstructorInputs.SelectedConvexShapeSlot, false, false, null, model);
    internal void RequireConstructionAssociation(FalloutEarlyControllerInvocation invocation, FalloutEarlyControllerLineage lineage)
    {
        RequireLifetime();
        if (lineage.Factory != Identity || lineage.Source != _source.Identity || invocation.Actor != lineage.Actor ||
            invocation.CommonProcess != lineage.CommonProcess || invocation.RuntimeProcess != lineage.RuntimeProcess ||
            invocation.ThreadProcess != lineage.ThreadProcess || invocation.Epoch != lineage.Epoch || invocation.Constructor != lineage.Constructor ||
            invocation.ThreadId != _thread || !_returnedCalls.TryGetValue(invocation.Initialization, out var returned) ||
            returned.Invocation != invocation || returned.Lineage != lineage || !returned.Returned || returned.ThreadScope is not { Returned: true })
            throw new InvalidOperationException("Actual controller constructor effect changed its retained source association.");
        _requireActualConstruction(invocation);
    }
    internal void RequireLifetime()
    {
        ObjectDisposedException.ThrowIf(_retired, this); if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Actual controller factory escaped its source physics thread.");
    }
    internal void ControllerRetired(FalloutEarlyController controller)
    { _retiredControllers.Add(controller.Capture()); _controllers.Remove(controller); }
    internal void RetainFailure(string failure) => _failure ??= failure;
    internal void ObserveUnownedSlopeWriter()
    { RequireLifetime(); Shared.ObserveUnownedWriter("slope"); }
    internal FalloutEarlyControllerFactorySnapshot Capture() => new(_source.Identity, Identity, _manager?.Capture(),
        _calls.ToArray(), CaptureColdControllerHistory(),
        _retiredFailedPrefixes.Concat(_failedPrefixes.Select(value => value.Capture())).ToArray(), Shared.Capture(), _failure, _retired,
        _retiredGroups ?? _groups.Capture(), _threadContext.CapturePhysicsInitializationScopes(), _shapeCache.CaptureOriginalCalls(),
        _shapeCache.CaptureColdCalls(), _coldBindings.ToArray(), _pendingColdControllers.Keys.ToArray());
    internal object CaptureFailedNativePrefixes() => new
    {
        shapeCache = _retiredShapeHistory ?? _shapeCache.Capture(),
        prefixes = _retiredFailedPrefixes.Concat(_failedPrefixes.Select(value => value.Capture())).ToArray()
    };
    internal void Retire()
    {
        if (_retired) return; RequireLifetime();
        if (_busy) { ++_fault; throw new InvalidOperationException("Controller factory retired inside its actual construction."); }
        foreach (var controller in _controllers.ToArray()) controller.RetireUnleased();
        for (var index = _failedPrefixes.Count - 1; index >= 0; --index)
        { _failedPrefixes[index].Retire(); _retiredFailedPrefixes.Add(_failedPrefixes[index].Capture()); _failedPrefixes.RemoveAt(index); }
        var shapeHistory = _shapeCache.Capture(); _shapeCache.Retire(); _retiredShapeHistory = shapeHistory;
        _manager?.Retire(); _retiredGroups = _groups.Capture(); _retired = true;
    }
    private sealed class FailedPrefix
    {
        internal FalloutCharacterStateContext? State;
        internal FalloutControllerContactState? Contact;
        internal FalloutEarlyControllerShapeLease? Shapes;
        internal FalloutEarlyControllerPhantom? Phantom;
        internal FalloutEarlyControllerProxy? Proxy;
        internal void Retire() { Proxy?.Retire(); Phantom?.Retire(); Shapes?.Dispose(); State?.Dispose(); }
        internal FalloutEarlyControllerFailedPrefix Capture() => new(State?.Snapshot, Contact?.Snapshot, Phantom?.Capture(), Proxy?.Capture());
    }
}
