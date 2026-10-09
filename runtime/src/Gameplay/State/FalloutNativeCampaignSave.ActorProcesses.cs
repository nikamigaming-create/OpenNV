using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateActorProcessContinuation(FalloutNativeCampaignState state)
    {
        var saved = state.ActorProcesses ?? throw new InvalidDataException("Current campaign has no complete original actor process/cohort/cadence state.");
        FalloutActorProcessManager.ValidateShape(saved);
        if (saved.Faults.Count != 0 || saved.Actors.Any(actor => actor.Boundary is not null) ||
            saved.Factory is { Phase: not FalloutActorProcessFactoryPhase.Complete } ||
            saved.Seconds > 0 && saved.Schedule is not { Complete: true } ||
            saved.Schedule is { } schedule && (schedule.Seconds != saved.Seconds || schedule.CohortRevision != saved.Cohort.Revision))
            throw new NotSupportedException("Current source process graph retains an actual incomplete factory/detection continuation.");
        var perception = state.ActorPerception ?? throw new InvalidDataException("Actual process graph has no shared perception/cache state.");
        if (perception.SimulationSeconds != saved.Seconds)
            throw new InvalidDataException("Source process and perception clocks did not consume the same actual frame.");
        var actors = perception.Actors.ToDictionary(actor => actor.Source.Reference, FalloutFormKeyComparer.Instance);
        if (actors.Count != saved.Actors.Count || saved.Actors.Any(actor => !actors.TryGetValue(actor.Source.Reference, out var pair) ||
            pair.Source != actor.Source || pair.ProcessEpoch != actor.Epoch || pair.Process?.Level != actor.Level || pair.Retired != actor.Retired))
            throw new InvalidDataException("Cold process/cadence graph differs from its actual shared actor/perception epochs.");
    }
    private static void ValidateActorProcessSource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        ValidateActorProcessContinuation(state);
        var source = records.OwnedSource ?? throw new InvalidDataException("Actor process continuation has no selected original owner.");
        var declaration = FalloutActorProcessDeclaration.Read(source.FalloutExecutablePath);
        var groups = FalloutCombatGroupDeclaration.ReadExecutable(source.FalloutExecutablePath);
        var saved = state.ActorProcesses!;
        if (saved.Contract != declaration.Contract || saved.Stack != source.StackId || saved.Player != records.RuntimeFormKey(0x14))
            throw new InvalidDataException("Cold actor process declaration differs from the exact selected executable/stack/player.");
        foreach (var actor in saved.Actors)
            if (FalloutCombatActorSource.Read(records, groups, actor.Source.Reference) != actor.Source)
                throw new InvalidDataException("Cold actor process changed an exact winning reference/master/base declaration.");
        ValidateActorConstructorSource(records, declaration, groups, saved);
    }
}
