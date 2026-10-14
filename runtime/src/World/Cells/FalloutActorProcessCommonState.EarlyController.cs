using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessEarlyControllerStore(FalloutEarlyControllerInvocation Invocation,
    long Entered, long Changed, Guid? Before, Guid After, bool PointerStored,
    bool FieldRetained, bool TemporaryReleased, bool Returned, string? Failure);
internal sealed record FalloutProcessEarlyControllerFieldSnapshot(Guid Process, long Epoch,
    Guid Controller, FalloutEarlyControllerSnapshot State,
    IReadOnlyList<FalloutProcessEarlyControllerStore> Stores,
    IReadOnlyList<FalloutProcessEarlyControllerColdStore>? ColdStores = null);
internal sealed record FalloutProcessEarlyControllerColdStore(FalloutEarlyControllerColdBinding Binding,
    long Entered, long Changed, bool PointerStored, bool FieldRetained, bool Returned, string? Failure);

internal sealed partial class FalloutActorProcessCommonState
{
    private readonly Dictionary<FalloutFormKey, EarlyControllerField> _earlyControllerFields = new(FalloutFormKeyComparer.Instance);
    private readonly List<IFalloutEarlyControllerLease> _rejectedEarlyControllerTemporaries = [];
    private FalloutEarlyControllerInvocation? _enteredEarlyControllerStore;
    private string? EarlyControllerSaveBlocker => ReadEarlyControllerSaveBlocker();

    // The Common field and the post-process scene getter retain this same real
    // controller. A null constructor observation cannot outlive publication.
    internal void RequireEarlyControllerAssociation(FalloutEarlyControllerInvocation invocation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = Require(invocation.Actor.Reference);
        if (_busy && _enteredEarlyControllerStore != invocation || invocation.CommonProcess != _process ||
            invocation.Stack != _stack || invocation.Actor != entry.Source || invocation.Epoch != entry.Epoch ||
            invocation.Level != entry.Level || entry.SceneFields is null ||
            entry.Phase is FalloutProcessCommonPhase.OldRetired or FalloutProcessCommonPhase.Retired ||
            invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Early controller left its actual current Common field/class/epoch/thread.");
    }

    internal void PublishEarlyController(FalloutEarlyControllerInvocation invocation,
        IFalloutEarlyControllerLease supplied, Action<FalloutEarlyControllerInvocation> requireActualConstruction)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(supplied); ArgumentNullException.ThrowIfNull(requireActualConstruction);
        RequireEarlyControllerAssociation(invocation); requireActualConstruction(invocation); supplied.RequireCurrent();
        var controller = supplied.Controller; var lineage = controller.Lineage;
        if (lineage.Actor != invocation.Actor || lineage.CommonProcess != _process || lineage.Stack != _stack ||
            lineage.RuntimeProcess != invocation.RuntimeProcess || lineage.ThreadProcess != invocation.ThreadProcess ||
            lineage.Epoch != invocation.Epoch || lineage.Level != invocation.Level || lineage.Constructor != invocation.Constructor)
            throw new InvalidDataException("Common setter received another actual controller construction.");
        var temporary = supplied.Retain();
        _earlyControllerFields.TryGetValue(invocation.Actor.Reference, out var old);
        var call = new FalloutProcessEarlyControllerStore(invocation, Next(), _sequence, old?.Controller.Identity,
            controller.Identity, false, false, false, false, null);
        var current = old;
        Exception? failure = null;
        _busy = true; _enteredEarlyControllerStore = invocation;
        try
        {
            if (old is null || !ReferenceEquals(old.Controller, controller))
            {
                old?.Lease?.Dispose();
                current = new(controller, invocation.Epoch, old?.Stores.ToList() ?? []);
                _earlyControllerFields[invocation.Actor.Reference] = current;
                var entry = Require(invocation.Actor.Reference);
                _current[entry.Source.Reference] = entry with
                { SceneFields = entry.SceneFields! with { PostSceneContext = controller.Identity }, Changed = Next() };
                call = call with { PointerStored = true, Changed = _sequence };
                current.Lease = temporary.Retain();
            }
            call = call with { FieldRetained = true, Changed = Next() };
            RequireEarlyControllerAssociation(invocation); requireActualConstruction(invocation);
            temporary.Dispose();
            call = call with { TemporaryReleased = true, Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            failure = error;
            call = call with { Failure = error.Message, Changed = Next() };
            if (current is not null) current.Failure ??= error.Message;
        }
        finally
        {
            if (!call.TemporaryReleased)
            {
                try { temporary.Dispose(); call = call with { TemporaryReleased = true, Changed = Next() }; }
                catch (Exception cleanup)
                {
                    _rejectedEarlyControllerTemporaries.Add(temporary);
                    failure = failure is null ? cleanup : new AggregateException(
                        "Common controller publication and temporary release failed.", failure, cleanup);
                    call = call with { Returned = false, Failure = failure.Message, Changed = Next() };
                    if (current is not null) current.Failure ??= failure.Message;
                }
            }
            current?.Stores.Add(call); _enteredEarlyControllerStore = null; _busy = false;
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal IFalloutEarlyControllerLease? BorrowEarlyController(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy(); var entry = Require(actor);
        if (entry.Epoch != epoch) throw new InvalidDataException("Early controller getter changed its actual process epoch.");
        if (!_earlyControllerFields.TryGetValue(actor, out var field))
        {
            if (entry.SceneFields?.PostSceneContext is not null)
                throw new NotSupportedException("source-cold-current-controller-resource-publication-unbound");
            return null;
        }
        if (field.Epoch != epoch || field.Failure is not null || field.Lease is null ||
            entry.SceneFields?.PostSceneContext != field.Controller.Identity)
            throw new NotSupportedException(field.Failure ?? "source-current-controller-pointer-retain-unreturned");
        field.Lease.RequireCurrent(); return field.Lease.Retain();
    }

    private FalloutProcessCommonEntry CaptureEarlyControllerField(FalloutProcessCommonEntry entry) =>
        _earlyControllerFields.TryGetValue(entry.Source.Reference, out var field) && field.Epoch == entry.Epoch
            ? entry with
            {
                EarlyController = new(_process, entry.Epoch, field.Controller.Identity,
                field.Controller.Capture(), field.Stores.ToArray(),
                field.ColdStores.Count == 0 ? null : field.ColdStores.ToArray())
            } : entry;

    private void RequireCurrentSceneController(FalloutFormKey actor, long epoch, Guid? context)
    {
        if (context is null)
        {
            if (_earlyControllerFields.ContainsKey(actor))
                throw new InvalidDataException("Scene context lost its actual retained controller pointer.");
            return;
        }
        if (!_earlyControllerFields.TryGetValue(actor, out var field) || field.Controller.Identity != context ||
            field.Epoch != epoch || field.Lease is null || field.Failure is not null)
            throw new NotSupportedException(field?.Failure ?? "source-current-scene-controller-resource-owner-unbound");
        field.Lease.RequireCurrent();
    }

    private static void ValidateEarlyControllerField(FalloutProcessCommonEntry entry, string executable, string stack)
    {
        if (entry.EarlyController is not { } field)
        {
            if (entry.SceneFields?.PostSceneContext is not null)
                throw new InvalidDataException("Saved scene context omitted its genuine controller field/resource history.");
            return;
        }
        var state = field.State; var lineage = state.Lineage;
        if (field.Process == Guid.Empty || field.Epoch != entry.Epoch || field.Controller == Guid.Empty ||
            state.Identity != field.Controller || entry.SceneFields?.PostSceneContext != field.Controller ||
            lineage.CommonProcess != field.Process || lineage.Actor != entry.Source || lineage.Stack != stack ||
            lineage.Epoch != entry.Epoch || lineage.Level != entry.Level ||
            executable != FalloutPhysicalConstructorInputs.ReviewedEngineSha256 ||
            field.Stores.Count == 0 || field.Stores.Any(store => store.Invocation.Actor != entry.Source ||
                store.Invocation.CommonProcess != field.Stores[0].Invocation.CommonProcess || store.Invocation.Epoch != entry.Epoch ||
                store.Entered < 1 || store.Changed <= store.Entered || store.Returned != (store.Failure is null) ||
                store.Returned && (!store.FieldRetained || !store.TemporaryReleased)))
            throw new InvalidDataException("Common controller snapshot changed its actual pointer, source or ordered setter history.");
        var process = field.Stores[0].Invocation.CommonProcess;
        var controller = field.Stores[^1].After;
        foreach (var cold in field.ColdStores ?? [])
        {
            var binding = cold.Binding;
            if (cold.Entered < 1 || cold.Changed <= cold.Entered || cold.Returned != (cold.Failure is null) ||
                cold.Returned && (!cold.PointerStored || !cold.FieldRetained) ||
                !binding.Returned || binding.Failure is not null || binding.PreviousCommonProcess != process ||
                binding.PreviousController != controller || binding.Controller is null ||
                binding.Current.Actor != entry.Source || binding.Current.Epoch != entry.Epoch ||
                binding.Current.Stack != stack || binding.Current.CommonProcess == process ||
                binding.Controller == controller || binding.Invocation.Current.CommonProcess != binding.Current.CommonProcess ||
                binding.GroupCounterBefore != binding.GroupCounterAfter || binding.OriginalCallsBefore != binding.OriginalCallsAfter ||
                binding.OriginalScopesBefore != binding.OriginalScopesAfter)
                throw new InvalidDataException("Cold Common controller lost its real fresh pointer chain or replayed source construction.");
            process = binding.Current.CommonProcess; controller = binding.Controller.Value;
        }
        if (process != field.Process || controller != field.Controller)
            throw new InvalidDataException("Current controller pointer does not match its original and cold publication history.");
    }

    private void RetireEarlyControllerFields()
    {
        foreach (var temporary in _rejectedEarlyControllerTemporaries.ToArray())
        { temporary.Dispose(); _rejectedEarlyControllerTemporaries.Remove(temporary); }
        foreach (var key in _earlyControllerFields.Keys.ToArray())
        {
            var field = _earlyControllerFields[key];
            field.Lease?.Dispose(); field.Lease = null; _earlyControllerFields.Remove(key);
        }
    }
    private sealed class EarlyControllerField(FalloutEarlyController controller, long epoch,
        List<FalloutProcessEarlyControllerStore> stores)
    {
        internal FalloutEarlyController Controller { get; } = controller;
        internal long Epoch { get; } = epoch;
        internal IFalloutEarlyControllerLease? Lease { get; set; }
        internal List<FalloutProcessEarlyControllerStore> Stores { get; } = stores;
        internal List<FalloutProcessEarlyControllerColdStore> ColdStores { get; } = [];
        internal string? Failure { get; set; }
    }
}
