using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutActorProcessConstructionSource? _actorConstructorSource;
    private Func<Guid, FalloutActorRegistrationThreadObservation>? _actorRegistrationThreads;
    private object? _actorRegistrationThreadLease;

    private void PrepareActualActorConstructorSource(FalloutActorProcessManager manager,
        FalloutActorProcessDeclaration declaration, string stack, FalloutActorProcessesSnapshot? restore)
    {
        if (_actorConstructorSource is not null)
            throw new InvalidOperationException("Actual actor constructor source is already bound.");
        var source = new FalloutActorProcessConstructionSource(records, declaration, stack,
            manager.ProcessIdentity, ReadCombatActorIdentity, () => ReadActualActorRegistrationThreads(manager.ProcessIdentity));
        if (restore is not null) source.ValidateRetained(restore);
        _actorConstructorSource = source;
    }

    private FalloutActorRegistrationThreadObservation ReadActualActorRegistrationThreads(Guid process) =>
        _actorRegistrationThreads?.Invoke(process) ??
        throw new NotSupportedException("original-actor-registration-thread-task-and-reference-processing-owner-unbound");

    internal IDisposable BindActualActorRegistrationThreads(Func<Guid, FalloutActorRegistrationThreadObservation> observe)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(observe);
        if (_actorRegistrationThreadLease is not null)
            throw new InvalidOperationException("Actor registration threads already have a living producer lease.");
        var lease = new object(); _actorRegistrationThreadLease = lease; _actorRegistrationThreads = observe;
        return new ActorRegistrationThreadLease(this, lease);
    }

    private sealed class ActorRegistrationThreadLease(FalloutReferenceWorld world, object owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (!ReferenceEquals(world._actorRegistrationThreadLease, owner)) return;
            world._actorRegistrationThreadLease = null; world._actorRegistrationThreads = null;
        }
    }

    private FalloutActorProcessConstruction ReadActualSourceActorConstruction(FalloutFormKey actor) =>
        (_actorConstructorSource ?? throw new NotSupportedException("Actual winning actor constructor source is absent.")).Read(actor);

    private void RetireFailedActorConstructorSetup(FalloutActorProcessManager manager,
        FalloutActorProcessDeclaration declaration, string stack, FalloutActorProcessesSnapshot? restore,
        Exception original)
    {
        try { manager.Dispose(); }
        catch (Exception retirement)
        {
            // A refused consumer still owns its source provider and exact
            // failed prefix. Retain both errors and its actual lifetime.
            _actorProcesses = manager; _actorProcessDeclaration = declaration;
            _actorProcessStack = stack; _actorProcessRestore = restore;
            throw new AggregateException("Actor constructor setup and retirement both failed.", original, retirement);
        }
        // No source instance was published. Preserve any independently bound
        // thread producer lease so an explicit retry does not fabricate one.
        _actorConstructorSource = null;
    }

    private void RetireActualActorConstructorSource()
    {
        // The process manager has retired first. No old OS thread IDs, native
        // callbacks or eligibility facts survive as a new-process producer.
        _actorConstructorSource = null; _actorRegistrationThreads = null; _actorRegistrationThreadLease = null;
    }
}
