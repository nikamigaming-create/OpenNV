using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    private static void ValidatePackageRegistryAssociations(FalloutProcessCommonSnapshot saved)
    {
        var entries = saved.Current.Concat(saved.Retired).Concat(saved.Pending is null ? [] : [saved.Pending]);
        var calls = new Dictionary<(Guid Factory, Guid Command, FalloutProcessPackageSlot Slot, FalloutPackageStateOperation Operation), FalloutPackageStateMutation>();
        var observers = new Dictionary<(Guid Observation, OpenNV.Runtime.Gameplay.State.FalloutCombatActorIdentity Actor, Guid Process, long Epoch),
            (FalloutProcessPackageWriterObservation Observation, HashSet<FalloutFormKey> KnownPackages, bool RequiresGlobal)>();
        foreach (var entry in entries)
            foreach (var fields in new[] { entry.Packages?.Current, entry.Packages?.Primary, entry.Packages?.RetiredInitialLow }.OfType<FalloutProcessPackageFields>())
            {
                var mutationIndex = 0; var beforeAvailable = true; var beforeWords = FalloutProcessPackageWords.Constructed;
                foreach (var observation in fields.Observations)
                {
                    while (mutationIndex < (fields.Mutations?.Count ?? 0) && fields.Mutations![mutationIndex].Changed < observation.Changed)
                    {
                        var before = fields.Mutations[mutationIndex++]; beforeAvailable = before.Step == FalloutPackageStateStep.Returned;
                        beforeWords = before.After;
                    }
                    var key = (observation.Observation, entry.Source, observation.Process, observation.Epoch);
                    if (!observers.TryGetValue(key, out var observed))
                    { observed = (observation, new(FalloutFormKeyComparer.Instance), false); }
                    else if (observed.Observation != observation) throw new InvalidDataException("Package slots changed a shared original ingress observation.");
                    if (beforeWords.Package is { } old) observed.KnownPackages.Add(old);
                    if (observation.RequestedPackage is { } requested) observed.KnownPackages.Add(requested);
                    observed.RequiresGlobal |= !beforeAvailable || observation.RequestedPackage is null && !observation.Remove;
                    observers[key] = observed; beforeAvailable = false;
                }
                foreach (var call in fields.Mutations ?? [])
                {
                    if (call.Invocation.Actor != entry.Source || call.Invocation.Epoch != entry.Packages!.ConstructedEpoch ||
                        call.Process.Stack != saved.Stack || call.Process.ExecutableSha256 != saved.ExecutableSha256)
                        throw new InvalidDataException("Package setter has a foreign actual Actor/epoch/source Common owner.");
                    var key = (call.Factory, call.Invocation.SourceCommand, call.Slot, call.Operation);
                    if (calls.TryGetValue(key, out var prior))
                    { if (!FalloutProcessPackageState.MutationEquivalent(prior, call)) throw new InvalidDataException("Historical package fields changed an actual mutation receipt."); }
                    else calls.Add(key, call);
                }
            }
        if (saved.PackageRegistry is not { } registry)
        {
            if (calls.Count != 0) throw new InvalidDataException("Mutated process package fields omitted their actual shared registry.");
            return;
        }
        if (!FalloutSourceMainFamily.IsFallout3(saved.ExecutableSha256))
            throw new InvalidDataException("An unreviewed source family acquired the FO3 mutable package registry.");
        FalloutPackageRuntimeRegistry.Validate(registry, saved.ExecutableSha256, saved.Stack);
        var packages = registry.Packages.ToDictionary(package => package.Form, FalloutFormKeyComparer.Instance);
        var history = registry.Packages.SelectMany(package => package.History).ToArray();
        var lifetimes = history.Where(item => item.Kind is FalloutPackageRuntimeMutationKind.Retain or FalloutPackageRuntimeMutationKind.Release)
            .ToLookup(item => (item.Invocation.SourceCommand, item.Invocation.Actor, item.Invocation.Process, item.Invocation.Epoch, item.Package, item.Kind));
        var consumed = new HashSet<long>();
        var lineage = saved.Publications?.Select(publication => publication.Process).ToHashSet() ?? [];
        var actors = entries.ToLookup(entry => (entry.Source, entry.Epoch));
        foreach (var mutation in history)
        {
            if (!lineage.Contains(mutation.Invocation.Process) || !actors.Contains((mutation.Invocation.Actor, mutation.Invocation.Epoch)))
                throw new InvalidDataException("Mutable PACK writer belongs to a foreign actual Common process or Actor epoch.");
            if (mutation.Kind == FalloutPackageRuntimeMutationKind.UnownedLifetime &&
                (!TryObserver(mutation.Invocation, mutation.Producer, out var observed) || !observed.KnownPackages.Contains(mutation.Package)))
                throw new InvalidDataException("Unavailable package count lost its exact known current/primary/requested ingress association.");
        }
        foreach (var barrier in registry.CountAuthorityObservations)
            if (!TryObserver(barrier.Invocation, barrier.Producer, out _))
                throw new InvalidDataException("Global count authority barrier has no actual Common unknown-pointer ingress.");
        var unavailableCounts = history.Where(item => item.Kind == FalloutPackageRuntimeMutationKind.UnownedLifetime)
            .ToLookup(item => (item.Invocation.SourceCommand, item.Invocation.Actor, item.Invocation.Process, item.Invocation.Epoch));
        var barriers = registry.CountAuthorityObservations.Select(item =>
            (item.Invocation.SourceCommand, item.Invocation.Actor, item.Invocation.Process, item.Invocation.Epoch)).ToHashSet();
        foreach (var (key, observed) in observers)
        {
            if (barriers.Contains(key)) continue;
            if (observed.RequiresGlobal || observed.KnownPackages.Any(form => !packages.ContainsKey(form)))
                throw new InvalidDataException("An unknown old package pointer retained global count authority.");
            var marked = unavailableCounts[key].Select(item => item.Package).ToHashSet(FalloutFormKeyComparer.Instance);
            if (!observed.KnownPackages.IsSubsetOf(marked))
                throw new InvalidDataException("Known current/primary/requested package counts omitted their unowned writer.");
        }
        foreach (var call in calls.Values)
        {
            if (call.Before.Package is { } before && !packages.ContainsKey(before) ||
                call.RequestedPackage is { } requested && !packages.ContainsKey(requested))
                throw new InvalidDataException("Package setter borrowed an absent winning mutable PACK object.");
            RequireCounter(call, call.Before.Package, FalloutPackageRuntimeMutationKind.Release, call.ReleasedCount);
            RequireCounter(call, call.RequestedPackage, FalloutPackageRuntimeMutationKind.Retain, call.RetainedCount);
            if (call.RequestedPackage is { } form && FalloutProcessPackageState.AllocatesContext(packages[form].Kind) &&
                (call.Step == FalloutPackageStateStep.Returned && !call.Equal || call.Stores.Any(store => store.Step == FalloutPackageStateStep.ContextStore)))
                throw new InvalidDataException("Allocating package kind acquired an unowned returned context or null shortcut.");
        }
        if (lifetimes.SelectMany(group => group).Any(item => !consumed.Contains(item.Sequence)))
            throw new InvalidDataException("Mutable package count has no exact committed process-field setter child.");

        bool TryObserver(FalloutPackageCommandFieldInvocation invocation, string producer,
            out (FalloutProcessPackageWriterObservation Observation, HashSet<FalloutFormKey> KnownPackages, bool RequiresGlobal) observed) =>
            observers.TryGetValue((invocation.SourceCommand, invocation.Actor, invocation.Process, invocation.Epoch), out observed) &&
            observed.Observation.Producer == producer;

        void RequireCounter(FalloutPackageStateMutation call, FalloutFormKey? form, FalloutPackageRuntimeMutationKind kind, int? count)
        {
            if (count is null) return;
            if (form is not { } package) throw new InvalidDataException("Package counter child lost its actual borrowed object.");
            var invocation = call.Invocation;
            var candidates = lifetimes[(invocation.SourceCommand, invocation.Actor, invocation.Process, invocation.Epoch, package, kind)]
                .Where(item => item.AfterCount == count && item.Invocation.ThreadId == invocation.ThreadId && !consumed.Contains(item.Sequence)).ToArray();
            if (candidates is not [var actual]) throw new InvalidDataException("Package setter counter differs from its exact shared registry mutation.");
            consumed.Add(actual.Sequence);
        }
    }
}
