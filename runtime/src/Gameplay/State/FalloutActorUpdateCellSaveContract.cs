using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// Mandatory current state joins. This validates first-party save state against
// its exact immutable original source; it does not import a retail save format
// or simulate old native callbacks during validation.
internal static class FalloutActorUpdateCellSaveContract
{
    internal static void Validate(FalloutPluginStack records, string executableSha256, string stack,
        IReadOnlyList<FalloutReferenceSnapshot> references, FalloutActorUpdateSnapshot? actors,
        FalloutCellProcessesSnapshot? cells)
    {
        if (actors is null || cells is null) throw new InvalidDataException("Current save omitted mandatory actor-update/CELL-process state.");
        FalloutReferenceSnapshot.Validate(references);
        var group = FalloutCombatGroupDeclaration.ForExecutable(executableSha256);
        var actualActors = references.Where(reference => records.GetEffective(reference.Reference).Signature is "ACHR" or "ACRE")
            .Select(reference => reference.Reference).Prepend(records.RuntimeFormKey(0x14)).Distinct(FalloutFormKeyComparer.Instance).ToArray();
        var actorSet = actualActors.ToHashSet(FalloutFormKeyComparer.Instance);
        using var restoredActors = new FalloutActorUpdateState(FalloutActorUpdateDeclaration.ForExecutable(executableSha256), stack,
            actor => FalloutCombatActorSource.Read(records, group, actor), actors);
        if (restoredActors.SaveBlocker is { } actorFailure) throw new NotSupportedException(actorFailure);
        restoredActors.RequireConstructedActors(actualActors);
        var current = actors.Actors.ToDictionary(actor => actor.Source.Reference, FalloutFormKeyComparer.Instance);
        foreach (var reference in references.Where(reference => actorSet.Contains(reference.Reference)))
            if (current[reference.Reference].Retired != reference.Deleted)
                throw new InvalidDataException("Saved actor-update lifetime differs from actual reference deletion/retirement.");
        using var restoredCells = new FalloutCellProcesses(FalloutCellProcessDeclaration.ForExecutable(executableSha256), records, stack, cells);
        FalloutCellProcesses.RequireCommittedSnapshot(cells);
        // The validation-only new process has no native object ownership. Its
        // pending rebind is deliberately not a satisfied runtime activity fact.
    }
}
