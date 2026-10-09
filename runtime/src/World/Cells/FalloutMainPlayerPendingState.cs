using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutMainPlayerPendingState
{
    internal const string Schema = "opennv-source-Player-pending-consumers/v2";
    private readonly FalloutMainPlayerPendingSource _source;
    private readonly string _stack;
    private readonly Guid _process;
    private IFalloutPlayerOwnedChild? _ownedChild;
    private FalloutPlayerOwnedChildMutation? _childMutation;
    private FalloutPlayerControllerScalarReceipt? _scalar;
    private byte _flags;
    private long _sequence, _callbacks, _flagStores;
    private Guid? _lastCallback, _lastFurniture;
    private string? _failureType, _error;
    private bool _busy, _retired;
    private long _callbackFault;
    private FalloutActorProcessRuntimeHandoff? _cold;
    internal FalloutExteriorCellLoaderState ExteriorLoaders { get; }
    internal string? SaveBlocker => _busy ? "source-Player-pending-tail-consumer-entered" :
        _error is not null ? "source-Player-pending-tail:" + _error :
        _ownedChild is not null ? "source-Player-refcount-child-cold-rebind-unowned" :
        _scalar is not null && _characterController is null ? "source-Player-character-controller-field-owner-absent" :
        CharacterControllerSaveBlocker ?? ExteriorLoaders.SaveBlocker;
    internal object State => new
    {
        source = _source,
        stack = _stack,
        process = _process,
        sequence = _sequence,
        ownedChild = _ownedChild?.Identity,
        childMutation = _childMutation,
        scalar = _scalar,
        characterController = _characterController?.State,
        characterControllerColdPending = _characterControllerRestore is not null,
        flags = _flags,
        callbacks = _callbacks,
        lastCallback = _lastCallback,
        lastFurniture = _lastFurniture,
        flagStores = _flagStores,
        error = _error,
        retired = _retired,
        cold = _cold,
        exteriorLoaders = ExteriorLoaders.State,
        blocker = SaveBlocker
    };
    internal FalloutMainPlayerPendingState(FalloutMainPlayerPendingSource source, string stack, Guid process,
        FalloutMainPlayerPendingSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new InvalidDataException("Player pending constructor omitted its actual shared Main process.");
        _source = source; _stack = stack; _process = process;
        ExteriorLoaders = new(source, stack, process, restore?.ExteriorLoaders);
        // These are the selected Player constructor's actual null pointer and
        // zero byte. Pause, body presence and activity cannot produce either.
        if (restore is not null) Restore(restore);
    }
    internal void ResetExteriorLoaders(FalloutMainPlayerCellInvocation invocation) =>
        Run(invocation, FalloutMainPlayerCellStep.PendingWorldPrelude, () => ExteriorLoaders.CancelAll(invocation));
    internal void ReleaseOwnedChild(FalloutMainPlayerCellInvocation invocation) =>
        Run(invocation, FalloutMainPlayerCellStep.PendingOwnedChildRelease, () => AssignOwnedChild(null, "actual-Player-pending-null-owned-child-store"));
    internal void PublishOwnedChild(IFalloutPlayerOwnedChild child, string sourceWriter)
    {
        Require(); ArgumentNullException.ThrowIfNull(child); ArgumentException.ThrowIfNullOrWhiteSpace(sourceWriter);
        if (child.Identity == Guid.Empty || child.Process != _process || child.SourceContract != _source.Contract ||
            string.IsNullOrWhiteSpace(child.Owner) || _ownedChild is not null && child.Identity == _ownedChild.Identity && !ReferenceEquals(child, _ownedChild))
            throw new InvalidDataException("Player refcounted child lacks its actual source factory and lifetime.");
        _busy = true; var faults = _callbackFault;
        try
        {
            AssignOwnedChild(child, sourceWriter);
            if (_callbackFault != faults)
                throw new InvalidOperationException("Player owned-child writer swallowed an actual callback reentry refusal.");
        }
        catch (Exception failure) { Retain(failure); throw; }
        finally { _busy = false; }
    }
    private void AssignOwnedChild(IFalloutPlayerOwnedChild? next, string owner)
    {
        if (ReferenceEquals(_ownedChild, next)) return;
        var before = _ownedChild;
        var faults = _callbackFault;
        _childMutation = new(Next(), before?.Identity, next?.Identity, owner, false, null, null);
        try
        {
            // A failed release retains the still-owned old pointer. A failed
            // acquire retains the actually stored new pointer. Neither may be
            // replayed as a clean null store or forgotten during shutdown.
            before?.Release();
            if (_callbackFault != faults)
                throw new InvalidOperationException("Player child release swallowed a real owner reentry refusal.");
            _ownedChild = next;
            next?.Acquire();
            if (_callbackFault != faults)
                throw new InvalidOperationException("Player child acquire swallowed a real owner reentry refusal.");
            _childMutation = _childMutation with { Changed = Next(), Returned = true };
        }
        catch (Exception failure)
        {
            _childMutation = _childMutation with { Changed = Next(), FailureType = Type(failure), Error = Message(failure) };
            Retain(failure); throw;
        }
    }
    internal void StoreScalar(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request,
        IFalloutPlayerTransferController controller)
    {
        Run(invocation, FalloutMainPlayerCellStep.PendingSceneScalar, () =>
        {
            var payload = Payload(request);
            ArgumentNullException.ThrowIfNull(controller);
            if (controller.Identity == Guid.Empty || controller.SourceProcess != _process || controller.ProcessEpoch < 1 ||
                string.IsNullOrWhiteSpace(controller.Owner))
                throw new InvalidDataException("Pending scalar's actual High-process character controller is foreign or retired.");
            controller.StoreScalar(invocation, request.Identity, payload.ControllerScalarBits);
            if (controller.ReadScalarBits() != payload.ControllerScalarBits)
                throw new InvalidDataException("Pending Float32 scalar changed bits before its actual source physics field store returned.");
            _scalar = new(invocation.Main.Identity, request.Identity, payload.ControllerScalarBits, controller.Identity,
                _process, controller.ProcessEpoch, Next(), controller.Owner);
        });
    }
    internal void InvokeCallback(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request,
        Func<FalloutPlayerTransferCallback, IFalloutPlayerTransferCallback> resolve)
    {
        Run(invocation, FalloutMainPlayerCellStep.PendingCallback, () =>
        {
            var payload = Payload(request);
            if (payload.Callback is null) { _lastCallback = request.Identity; _ = Next(); return; }
            var callback = resolve(payload.Callback);
            if (callback is null || callback.Process != _process || callback.Source != payload.Callback || string.IsNullOrWhiteSpace(callback.Owner))
                throw new InvalidDataException("Pending callback lost its declared factory/context and actual lease.");
            callback.Invoke(invocation, payload.Callback.Context);
            _callbacks = checked(_callbacks + 1); _lastCallback = request.Identity; _ = Next();
        });
    }
    internal void ConsumeFurniture(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request,
        Action<FalloutFormKey> consume)
    {
        Run(invocation, FalloutMainPlayerCellStep.PendingFurniture, () =>
        {
            var payload = Payload(request);
            if (payload.Target == FalloutPlayerTransferTarget.Reference)
                throw new InvalidDataException("Reference travel cannot enter the separate pending furniture tail.");
            if (payload.Furniture is { } furniture) consume(furniture);
            _lastFurniture = request.Identity; _ = Next();
        });
    }
    internal bool ReadFinalFlagQueries(FalloutMainPlayerCellInvocation invocation, Func<uint> managerWord)
    {
        var result = false;
        Run(invocation, FalloutMainPlayerCellStep.PendingFlagQueries, () => result =
            (managerWord() & 2u) == 0 && (_flags & 2) == 0);
        return result;
    }
    internal void StoreFinalFlag(FalloutMainPlayerCellInvocation invocation) => Run(invocation,
        FalloutMainPlayerCellStep.PendingFlagChild, () => { _flags |= 1; _flagStores = checked(_flagStores + 1); _ = Next(); });
    internal FalloutPlayerTransferPayload Payload(FalloutPlayerPendingRequest request)
    {
        var payload = request.SourcePayload ?? throw new NotSupportedException("source-Player-pending-raw-payload-factory-unowned:" + request.Owner);
        payload.Validate(_source); payload.RequireRole(request.Kind);
        return payload;
    }
    private void Run(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellStep step, Action action)
    {
        Require(); invocation.Require(step);
        if (invocation.Main.Process != _process || invocation.Owner.MainPlayerCellSource != _source.Player)
            throw new InvalidDataException("Pending child lost its actual shared Main/source process.");
        _busy = true; var faults = _callbackFault;
        try
        {
            action(); invocation.Require(step);
            if (_callbackFault != faults) throw new InvalidOperationException("Pending child swallowed an actual owner reentry refusal.");
        }
        catch (Exception failure) { Retain(failure); throw; }
        finally { _busy = false; }
    }
    private void Require()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { _callbackFault = checked(_callbackFault + 1); throw new InvalidOperationException("Player pending source owner reentered its live child."); }
        if (_error is not null) throw new InvalidOperationException("Player pending owner retains its entered prefix: " + _error);
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private void Retain(Exception failure) { _failureType ??= Type(failure); _error ??= Message(failure); }
    private static string Type(Exception failure) => failure.GetType().FullName ?? failure.GetType().Name;
    private static string Message(Exception failure) => string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message;
}
