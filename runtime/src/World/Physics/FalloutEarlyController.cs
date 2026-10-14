using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed partial class FalloutEarlyController : IFalloutPlayerTransferController
{
    internal const string Schema = "source-early-detached-controller/v3";
    private readonly FalloutEarlyControllerFactory _factory;
    private readonly FalloutEarlyControllerShapeLease _shapes;
    private readonly FalloutCharacterStateContext _state;
    private readonly FalloutControllerContactState _contact;
    private readonly HashSet<Lease> _leases = [];
    private readonly List<FalloutEarlyControllerStore> _stores = [];
    private readonly List<FalloutEarlyControllerResetCall> _resets = [];
    private readonly FalloutCharacterControllerTransformState _position;
    private uint _scalar, _flags, _selector;
    private uint? _actorScaleBits;
    private long _sequence, _fault;
    private string? _failure;
    private bool _busy, _retired;
    public Guid Identity { get; } = Guid.NewGuid();
    public Guid SourceProcess => Lineage.RuntimeProcess;
    public long ProcessEpoch => Lineage.Epoch;
    public string Owner => "actual-early-source-Player-controller";
    internal FalloutEarlyControllerLineage Lineage { get; }
    internal FalloutEarlyControllerDescriptorSnapshot Descriptor { get; }
    internal FalloutEarlyControllerPhantom Phantom { get; }
    internal FalloutEarlyControllerProxy Proxy { get; }
    internal FalloutControllerContactState Contact => _contact;
    internal string? SaveBlocker => _busy || _readingPostProcess ? "source-early-controller-consumer-in-flight" : _failure ?? ActorMovementSaveBlocker ?? _moverDeltaFields.QueueBoundary ?? PostProcessFieldSaveBlocker ??
        (_retired ? "source-early-controller-retired" : null);
    internal FalloutEarlyController(FalloutEarlyControllerFactory factory, FalloutEarlyControllerLineage lineage,
        FalloutEarlyControllerDescriptorSnapshot descriptor, FalloutCharacterStateContext state,
        FalloutControllerContactState contact, FalloutEarlyControllerShapeLease shapes,
        FalloutEarlyControllerPhantom phantom, FalloutEarlyControllerProxy proxy)
    {
        _factory = factory; Lineage = lineage; Descriptor = descriptor; _state = state; _contact = contact; _shapes = shapes;
        Phantom = phantom; Proxy = proxy;
        _position = new(factory.Inputs, descriptor.Player.RetainedOffset, () => Proxy.Transform);
    }
    internal IFalloutEarlyControllerLease Retain() { RequireCurrent(); var lease = new Lease(this); _leases.Add(lease); return lease; }
    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_retired, this); _factory.RequireLifetime();
        if (_busy) { ++_fault; throw new InvalidOperationException("Actual early controller reentered its source consumer."); }
        if (_failure is not null) throw new InvalidOperationException("Actual controller retains its failed source prefix: " + _failure);
        _state.RequireCurrent(); Phantom.RequireCurrent(); Proxy.RequireCurrent(); _ = _shapes.Slot(0);
    }
    internal void SetActorProperty(FalloutEarlyControllerInvocation invocation, object actualActorOwner, Action requireActualActor)
    {
        RequireCurrent(); _factory.RequireConstructionAssociation(invocation, Lineage); requireActualActor();
        if (Phantom.AddPropertyIfAbsent(FalloutEarlyControllerSource.ActorPropertyKey, actualActorOwner, Lineage.Actor))
            Store("phantom-Actor-property-1000", null);
        requireActualActor(); _factory.RequireConstructionAssociation(invocation, Lineage);
    }
    internal void SetPlayerPropertySelector(FalloutEarlyControllerInvocation invocation)
    {
        RequireCurrent(); _factory.RequireConstructionAssociation(invocation, Lineage); _selector = FalloutEarlyControllerSource.ActorPropertyKey;
        Store("Player-property-selector", _selector);
    }
    internal void SetPlayerGroup(FalloutEarlyControllerInvocation invocation)
    { RequireCurrent(); _factory.RequireConstructionAssociation(invocation, Lineage); Phantom.SetPlayerGroup(); Store("phantom-filter", Phantom.Filter); }
    internal void StoreActorScale(FalloutEarlyControllerInvocation invocation, uint bits)
    {
        RequireCurrent(); _factory.RequireConstructionAssociation(invocation, Lineage);
        _actorScaleBits = bits; Store("controller-Actor-scale", bits);
    }
    internal void SetHelperFlags(FalloutEarlyControllerInvocation invocation, bool actualSharedFlag)
    {
        RequireCurrent(); _factory.RequireConstructionAssociation(invocation, Lineage);
        if (actualSharedFlag != _factory.Shared.ReadHelperFlag())
            throw new InvalidDataException("Controller helper flag changed its actual shared source cell.");
        _flags = actualSharedFlag ? _flags | FalloutEarlyControllerSource.HelperConditionalFlag : _flags & ~FalloutEarlyControllerSource.HelperConditionalFlag;
        _flags |= FalloutEarlyControllerSource.HelperAlwaysFlag; Store("controller-helper-flags", _flags);
    }
    internal void ResetForPosition(FalloutEarlyControllerResetInvocation invocation, FalloutPhysicalVector3 rawXYZ,
        Action<FalloutEarlyControllerResetInvocation> requireActualReset, uint? scratchFourthWord = null)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(requireActualReset); var fault = _fault; var entered = _sequence;
        _busy = true;
        try
        {
            Guard(); var current = Phantom.Transform;
            // Both actual source pre/post virtuals freshly read this same
            // current proxy/phantom world. Source null skips their world child.
            _ = Phantom.CurrentWorld;
            var xyz = _position.ProxyTranslationForReferencePosition(rawXYZ, current.Rotation);
            Phantom.StoreTranslation(xyz, scratchFourthWord); Store("phantom-translation", null, xyz, scratchFourthWord);
            Guard(); _ = Phantom.CurrentWorld;
            Proxy.StoreVelocity(default); Store("proxy-velocity-four-zero", 0, default(FalloutPhysicalVector3), 0);
            Guard(); _scalar = 0; Store("controller-reset-scalar", 0);
            if ((_flags & FalloutEarlyControllerSource.ResetFlag) != 0)
            { _flags &= ~FalloutEarlyControllerSource.ResetFlag; Store("controller-flags", _flags); }
            Guard(); Proxy.ClearFirstArrayCount(); Store("proxy-first-array-count", null, count: 0); Guard();
            _resets.Add(new(invocation, rawXYZ, entered, _sequence, true, null));
        }
        catch (Exception error)
        { _failure ??= Failure(error); _factory.RetainFailure(_failure); _resets.Add(new(invocation, rawXYZ, entered, _sequence, false, _failure)); throw; }
        finally { _busy = false; }

        void Guard()
        {
            if (_fault != fault || invocation.Call == Guid.Empty || invocation.Actor != Lineage.Actor || invocation.CommonProcess != Lineage.CommonProcess ||
                invocation.RuntimeProcess != Lineage.RuntimeProcess || invocation.Epoch != Lineage.Epoch || invocation.Constructor != Lineage.Constructor ||
                invocation.ThreadId != Lineage.Thread || invocation.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Actual controller reset lost its source Actor/Common/constructor/thread association.");
            requireActualReset(invocation); _factory.RequireLifetime();
            if (_fault != fault) throw new InvalidOperationException("Controller reset guard swallowed a source reentry refusal.");
        }
    }
    public void StoreScalar(FalloutMainPlayerCellInvocation invocation, Guid request, uint bits)
    {
        RequireCurrent(); invocation.Require(FalloutMainPlayerCellStep.PendingSceneScalar);
        if (invocation.Main.Process != SourceProcess || request == Guid.Empty)
            throw new InvalidDataException("Actual early controller scalar has a foreign Main/request owner.");
        _scalar = bits; Store("pending-controller-scalar", bits);
    }
    public uint ReadScalarBits() { RequireCurrent(); return _scalar; }
    internal uint ReadPositionZBits() { RequireCurrent(); return _position.ReadPositionZBits(); }
    internal uint ReadHeightDeltaBits() { RequireCurrent(); return _position.ReadHeightDeltaBits(_scalar); }
    private void Store(string field, uint? bits, FalloutPhysicalVector3? xyz = null, uint? fourth = null, int? count = null) =>
        _stores.Add(new(checked(++_sequence), field, bits, xyz, fourth, count));
    internal FalloutEarlyControllerSnapshot Capture() => new(Schema, Lineage, Identity, Descriptor, _state.Snapshot, _contact.Snapshot,
        Phantom.Capture(), Proxy.Capture(), _scalar, _flags, _selector, _actorScaleBits, _stores.ToArray(), _resets.ToArray(), _failure, _retired,
        FalloutEarlyControllerConvexGeometry.CanonicalBoundary, CapturePostProcessFields(), CaptureMoverDelta(), CaptureActorMovement());
    internal void RetireUnleased()
    {
        if (_retired) return; _factory.RequireLifetime();
        if (_busy || _readingPostProcess || _leases.Count != 0)
            throw new InvalidOperationException("Actual controller must outlive its entered consumers and retained leases.");
        Proxy.Retire(); Phantom.Retire(); _shapes.Dispose(); _state.Dispose(); _retired = true; _factory.ControllerRetired(this);
    }
    private void Release(Lease lease)
    {
        _factory.RequireLifetime();
        if (!_leases.Contains(lease)) throw new InvalidOperationException("Actual controller released a foreign retained lease.");
        if (_leases.Count == 1)
        {
            // Keep the actual last borrower until genuine native retirement
            // returns; a rejected release can retry this same retained lease.
            if (_busy) { ++_fault; throw new InvalidOperationException("Controller retired inside its actual entered source reset."); }
            if (_readingPostProcess)
            { ++_postProcessReadFault; throw new InvalidOperationException("Controller retired inside its actual context field getter."); }
            Proxy.Retire(); Phantom.Retire(); _shapes.Dispose(); _state.Dispose(); _retired = true; _factory.ControllerRetired(this);
        }
        _leases.Remove(lease);
    }
    private static string Failure(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    private sealed class Lease(FalloutEarlyController owner) : IFalloutEarlyControllerLease
    {
        private bool _retired;
        public FalloutEarlyController Controller
        {
            get
            {
                ObjectDisposedException.ThrowIf(_retired, this); owner._factory.RequireLifetime();
                if (!owner._leases.Contains(this) || owner._retired) throw new InvalidOperationException("Actual controller pointer lost its retained lease.");
                // A pure Common pointer read is valid inside this controller's
                // actual guarded child; it does not reenter a field consumer.
                return owner;
            }
        }
        public IFalloutEarlyControllerLease Retain() { RequireCurrent(); return owner.Retain(); }
        public void RequireCurrent()
        {
            ObjectDisposedException.ThrowIf(_retired, this); if (!owner._leases.Contains(this))
                throw new InvalidOperationException("Actual controller lease lost its owner."); owner.RequireCurrent();
        }
        public void Dispose() { if (_retired) return; owner.Release(this); _retired = true; }
    }
}
