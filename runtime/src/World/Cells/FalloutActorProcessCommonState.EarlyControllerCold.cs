using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    internal void RebindColdEarlyController(FalloutEarlyControllerColdBinding binding,
        IFalloutEarlyControllerLease supplied, Action<FalloutEarlyControllerColdInvocation> requireActualColdBinding)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(supplied);
        ArgumentNullException.ThrowIfNull(requireActualColdBinding);
        var invocation = binding.Invocation.Current;
        RequireEarlyControllerAssociation(invocation); requireActualColdBinding(binding.Invocation); supplied.RequireCurrent();
        var entry = Require(invocation.Actor.Reference);
        var previous = entry.EarlyController ?? throw new InvalidDataException("Cold Common omitted its original controller field.");
        var controller = supplied.Controller;
        var state = controller.Capture();
        if (!binding.Returned || binding.Failure is not null || binding.Controller != controller.Identity ||
            binding.Current != controller.Lineage || binding.Current.CommonProcess != _process ||
            binding.Current.Actor != entry.Source || binding.PreviousController != previous.Controller ||
            binding.PreviousCommonProcess != previous.Process || binding.NativeArea != state.Phantom.NativeId ||
            entry.SceneFields?.PostSceneContext != previous.Controller ||
            _earlyControllerFields.ContainsKey(invocation.Actor.Reference))
            throw new InvalidDataException("Cold Common changed its original field or returned fresh resource binding.");
        var field = new EarlyControllerField(controller, invocation.Epoch, previous.Stores.ToList());
        field.ColdStores.AddRange(previous.ColdStores ?? []);
        var call = new FalloutProcessEarlyControllerColdStore(binding, Next(), _sequence, false, false, false, null);
        _busy = true; _enteredEarlyControllerStore = invocation;
        try
        {
            _earlyControllerFields.Add(invocation.Actor.Reference, field);
            _current[entry.Source.Reference] = entry with
            { SceneFields = entry.SceneFields! with { PostSceneContext = controller.Identity }, Changed = Next() };
            call = call with { PointerStored = true, Changed = _sequence };
            field.Lease = supplied.Retain();
            call = call with { FieldRetained = true, Changed = Next() };
            RequireEarlyControllerAssociation(invocation); requireActualColdBinding(binding.Invocation); supplied.RequireCurrent();
            call = call with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            field.Failure ??= error.Message;
            call = call with { Failure = error.Message, Changed = Next() };
            throw;
        }
        finally
        {
            field.ColdStores.Add(call); _enteredEarlyControllerStore = null; _busy = false;
        }
    }
}
