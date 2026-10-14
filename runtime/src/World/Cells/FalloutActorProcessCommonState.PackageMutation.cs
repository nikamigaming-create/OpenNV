using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    private int? _packageThread;
    private FalloutPackageRuntimeRegistry? _packageRegistry;
    private FalloutPackageRegistrySnapshot? _preparedColdPackageRegistry;
    private FalloutPackageCommandFieldInvocation? _packageMutationInvocation;
    internal FalloutPackageRegistrySnapshot? PreparedColdPackageRegistry => _preparedColdPackageRegistry;

    internal void BindSourcePackageRegistry(FalloutPackageRuntimeRegistry registry)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(registry); registry.RequireCurrent();
        if (_packageRegistry is not null || registry.Executable != _source.ExecutableSha256 || registry.Stack != _stack ||
            registry.RestoredFactory != _preparedColdPackageRegistry?.Factory)
            throw new InvalidOperationException("Common package fields require their one actual winning registry and exact fresh cold association.");
        if (_preparedColdPackageRegistry is null && _current.Values.Any(entry => entry.Packages is { } packages &&
            (packages.Current.Observations.Count != 0 || packages.Primary?.Observations.Count > 0)))
            throw new NotSupportedException("source-package-registry-prebind-count-writer-unowned");
        _packageRegistry = registry;
        _packageThread = Environment.CurrentManagedThreadId;
    }

    internal FalloutProcessSceneQuery ReadSceneQueriesForPackageCommand(FalloutPackageCommandFieldInvocation invocation)
    {
        FalloutPackageRuntimeRegistry.RequireInvocation(invocation);
        if (_packageThread is { } thread && invocation.ThreadId != thread || invocation.Process != _process)
            throw new InvalidOperationException("Package command belongs to another actual Common process/thread.");
        if (!_busy)
        {
            var process = ReadSceneQueries(invocation.Actor.Reference, invocation.Epoch);
            if (process.Actor != invocation.Actor) throw new InvalidOperationException("Package command selected a foreign Actor source owner.");
            return process;
        }
        if (_packageMutationInvocation != invocation)
        {
            _callbackFault = checked(_callbackFault + 1);
            throw new InvalidOperationException("Package command reentered another Common transaction.");
        }
        var current = Require(invocation.Actor.Reference);
        if (current.Source != invocation.Actor || current.Epoch != invocation.Epoch || current.Boundary is not null ||
            current.Phase is FalloutProcessCommonPhase.Retired or FalloutProcessCommonPhase.OldRetired ||
            current.Gameplay is not { Retired: false } lease || Identity(invocation.Actor.Reference) != current.Source)
            throw new NotSupportedException("source-package-command-current-Actor-process-lease-unavailable");
        var fields = current.SceneFields ?? FalloutProcessSceneFields.Constructed;
        return new(_process, _stack, _source.ExecutableSha256, current.Source, current.Epoch, current.Level, lease.Ownership,
            current.Changed, fields.WeaponEntry, fields.AnimationContext, fields.IdlePrimaryPackage, fields.ActorValueCache);
    }

    internal FalloutPackageStateMutation SetSourcePackageState(FalloutPackageCommandFieldInvocation invocation,
        FalloutProcessPackageSlot slot, FalloutFormKey? requested, FalloutPackageExtraReference? extra,
        IFalloutPackageStateServices services) => MutateSourcePackageState(invocation, slot,
            FalloutPackageStateOperation.Set, requested, extra, services);

    internal FalloutPackageStateMutation ClearSourcePackageState(FalloutPackageCommandFieldInvocation invocation,
        FalloutProcessPackageSlot slot, IFalloutPackageStateServices services) => MutateSourcePackageState(invocation, slot,
            FalloutPackageStateOperation.Clear, null, null, services);

    private FalloutPackageStateMutation MutateSourcePackageState(FalloutPackageCommandFieldInvocation invocation,
        FalloutProcessPackageSlot slot, FalloutPackageStateOperation operation, FalloutFormKey? requested,
        FalloutPackageExtraReference? extra, IFalloutPackageStateServices services)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(services);
        if (!Enum.IsDefined(slot)) throw new InvalidDataException("Package setter requires its actual current/primary field owner.");
        var process = ReadSceneQueriesForPackageCommand(invocation);
        var current = Require(invocation.Actor.Reference);
        var fields = RequireSourcePackageFields(current, process, slot);
        var registry = _packageRegistry ?? throw new NotSupportedException("source-process-package-winning-registry-unbound");
        var package = requested is { } form ? registry.Require(form) : null;
        extra?.Validate();
        if (!_packageLedgers.TryGetValue(invocation.Actor.Reference, out var epochs))
        { epochs = []; _packageLedgers.Add(invocation.Actor.Reference, epochs); }
        if (!epochs.TryGetValue(invocation.Epoch, out var ledger)) { ledger = new(current.Packages!); epochs.Add(invocation.Epoch, ledger); }
        if (!ledger.Commands.Add((invocation.SourceCommand, slot, operation)))
            throw new InvalidOperationException("Source package setter replayed its actual command/slot/operation.");
        var mutations = slot == FalloutProcessPackageSlot.Current ? ledger.CurrentMutations : ledger.PrimaryMutations;
        var mutationIndex = mutations.Count;
        _busy = true; _packageMutationInvocation = invocation;
        try
        {
            return FalloutProcessPackageState.Mutate(invocation, process, fields, operation, registry, package, extra,
                new PackageMutationServices(this, services), Next, (words, call) =>
                {
                    if (mutations.Count == mutationIndex) mutations.Add(call); else mutations[mutationIndex] = call;
                    var live = Require(invocation.Actor.Reference); var packages = live.Packages!;
                    var changedFields = fields with
                    {
                        Fields = words,
                        Changed = call.Changed,
                        Available = call.Step == FalloutPackageStateStep.Returned,
                        Mutations = mutations.AsReadOnly()
                    };
                    _current[invocation.Actor.Reference] = live with
                    {
                        Changed = call.Changed,
                        Packages = slot == FalloutProcessPackageSlot.Current ? packages with { Current = changedFields } : packages with { Primary = changedFields }
                    };
                });
        }
        finally { _packageMutationInvocation = null; _busy = false; }
    }

    private sealed class PackageMutationServices(FalloutActorProcessCommonState owner, IFalloutPackageStateServices source) : IFalloutPackageStateServices
    {
        public void RequireCurrent(FalloutPackageCommandFieldInvocation invocation) => Call(() => source.RequireCurrent(invocation));
        public FalloutPackageRuntimeObject? ReadPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation) =>
            owner.Callback(() => source.ReadPlayerSpecialPackage(invocation));
        public void ClearPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject expected) =>
            Call(() => source.ClearPlayerSpecialPackage(invocation, expected));
        public FalloutPackageGameHourRead ReadGameHour(FalloutPackageCommandFieldInvocation invocation) => owner.Callback(() => source.ReadGameHour(invocation));
        public Guid ConstructContext(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package) => owner.Callback(() => source.ConstructContext(invocation, package));
        public void DestroyContext(FalloutPackageCommandFieldInvocation invocation, Guid context) => Call(() => source.DestroyContext(invocation, context));
        public void DeleteTemporaryPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package) => Call(() => source.DeleteTemporaryPackage(invocation, package));
        public void RequireExtra(FalloutPackageCommandFieldInvocation invocation, FalloutPackageExtraReference extra) => Call(() => source.RequireExtra(invocation, extra));
        private void Call(Action callback) => owner.Callback(() => { callback(); return true; });
    }
}
