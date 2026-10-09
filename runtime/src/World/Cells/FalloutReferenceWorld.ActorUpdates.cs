using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutActorUpdateState? _actorUpdates;
    private FalloutActorUpdateDeclaration? _actorUpdateDeclaration;
    private string? _actorUpdateStack;
    private FalloutActorUpdateSnapshot? _actorUpdateRestore;
    internal bool ActorUpdatesConfigured => _actorUpdates is not null;
    internal object? ActorUpdateState => _actorUpdates?.State;
    internal string? ActorUpdateSaveBlocker => _actorUpdates is null ? "original-actor-update-owner-absent" : _actorUpdates.SaveBlocker;
    private FalloutActorUpdateState ActorUpdates => _actorUpdates ??
        throw new NotSupportedException("Actual source Actor update-byte owner is absent.");

    internal void ConfigureActualActorCellProducers(string stack, FalloutActorUpdateSnapshot? actorRestore = null,
        FalloutCellProcessesSnapshot? cellRestore = null)
    {
        var executable = (_combatGroupDeclaration ?? throw new NotSupportedException(
            "Actor/CELL constructors have no actual selected reference factory.")).ExecutableSha256;
        var actors = FalloutActorUpdateDeclaration.ForExecutable(executable);
        var cells = FalloutCellProcessDeclaration.ForExecutable(executable);
        if (ActorUpdatesConfigured) RequireActorUpdateBinding(actors, stack, actorRestore);
        else ConfigureActorUpdates(actors, stack, actorRestore);
        if (CellProcessesConfigured) RequireCellProcessBinding(cells, stack, cellRestore);
        else ConfigureCellProcesses(cells, stack, cellRestore);
    }

    internal void ConfigureActorUpdates(FalloutActorUpdateDeclaration source, string stack,
        FalloutActorUpdateSnapshot? restore = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_actorUpdates is not null) throw new InvalidOperationException("Source actor update lifetime is already configured.");
        if (_combatGroupDeclaration?.ExecutableSha256 != source.ExecutableSha256 || _combatGroupStackIdentity != stack ||
            records.OwnedSource is { } owned && owned.StackId != stack)
            throw new InvalidDataException("Actor update constructor differs from the actual source combat/reference factory.");
        var candidate = new FalloutActorUpdateState(source, stack, ReadCombatActorIdentity, restore);
        try
        {
            if (restore is null)
                foreach (var actor in ActualActorUpdateReferences()) candidate.Construct(actor);
            else candidate.RequireConstructedActors(ActualActorUpdateReferences());
        }
        catch { candidate.Dispose(); throw; }
        _actorUpdates = candidate; _actorUpdateDeclaration = source; _actorUpdateStack = stack; _actorUpdateRestore = restore;
    }
    internal void RequireActorUpdateBinding(FalloutActorUpdateDeclaration source, string stack,
        FalloutActorUpdateSnapshot? restore)
    {
        if (_actorUpdates is null || _actorUpdateDeclaration != source || _actorUpdateStack != stack || !ReferenceEquals(_actorUpdateRestore, restore))
            throw new InvalidDataException("Attached source actor update lifetime differs from its current/cold owner.");
    }
    private IEnumerable<FalloutFormKey> ActualActorUpdateReferences() => _instances.Keys.Where(reference =>
        records.GetEffective(reference).Signature is "ACHR" or "ACRE").Prepend(_enginePlayer).Distinct(FalloutFormKeyComparer.Instance);
    private void ConstructActualActorUpdate(FalloutReferenceInstance instance)
    {
        if (_actorUpdates is not null && records.GetEffective(instance.Reference).Signature is "ACHR" or "ACRE")
            ActorUpdates.Construct(instance.Reference);
    }
    internal FalloutActorProcessFact<bool> ReadSourceActorUpdate(FalloutFormKey actor)
    {
        if (actor != _enginePlayer) _ = Actor(actor);
        return ActorUpdates.Read(actor);
    }
    internal bool SourceActorAiEnabled(FalloutFormKey actor) => ReadSourceActorUpdate(actor).Require();
    internal int IsActorsAiOff(FalloutFormKey actor)
    {
        if (actor != _enginePlayer) _ = Actor(actor);
        return ActorUpdates.IsOff(actor);
    }
    internal void SetActorsAi(FalloutFormKey actor, double signed, FalloutFormKey caller)
    {
        if (actor != _enginePlayer) _ = Actor(actor);
        ActorUpdates.Set(actor, FalloutActorUpdateDeclaration.Integer(signed), caller, "actual-source-SetActorsAI");
    }
    internal void ToggleActorsAi(FalloutFormKey actor, FalloutFormKey caller)
    {
        if (actor != _enginePlayer) _ = Actor(actor);
        ActorUpdates.Toggle(actor, caller, "actual-source-ToggleActorsAI");
    }
    internal FalloutActorUpdateSnapshot CaptureActorUpdates()
    {
        ActorUpdates.RequireConstructedActors(ActualActorUpdateReferences());
        return ActorUpdates.Capture();
    }
    private void RetireActualActorUpdate(FalloutReferenceInstance instance, string owner)
    {
        if (_actorUpdates?.HasActor(instance.Reference) == true) ActorUpdates.Retire(instance.Reference, owner);
    }
    private void RetireActorUpdates()
    {
        _actorUpdates?.Dispose(); _actorUpdates = null;
        _actorUpdateDeclaration = null; _actorUpdateStack = null; _actorUpdateRestore = null;
    }
}
