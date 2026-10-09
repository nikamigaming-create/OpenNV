using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateActorPerceptionContinuation(FalloutNativeCampaignState state)
    {
        var snapshot = state.ActorPerception ?? throw new InvalidDataException("Current campaign has no complete actor perception state.");
        FalloutActorPerception.ValidateShape(snapshot);
        if (snapshot.Failures.Count != 0 || snapshot.Actors.Any(actor => actor.ProcessBoundary is not null) ||
            snapshot.Phase != FalloutPerceptionFramePhase.Idle ||
            snapshot.SimulationSeconds > 0 && snapshot.LastSourceFrame is not { Complete: true } ||
            snapshot.LastSourceFrame is { } frame && frame.Seconds != snapshot.SimulationSeconds)
            throw new NotSupportedException("Campaign perception retains an unresolved original producer/frame continuation.");
        var references = state.References?.ToDictionary(reference => reference.Reference, FalloutFormKeyComparer.Instance) ??
            throw new InvalidDataException("Perception has no retained actual reference state.");
        foreach (var actor in snapshot.Actors.Where(actor => !actor.Source.EnginePlayer))
            if (!references.TryGetValue(actor.Source.Reference, out var reference) || reference.Base != actor.Source.Base ||
                reference.Deleted != actor.Retired)
                throw new InvalidDataException("Cold perception actor differs from its actual retained reference/base/deletion owner.");
    }
    private static void ValidateActorPerceptionSource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        ValidateActorPerceptionContinuation(state);
        var source = records.OwnedSource ?? throw new InvalidDataException("Perception has no exact selected owned source.");
        var declaration = FalloutActorPerceptionDeclaration.Read(source.FalloutExecutablePath);
        var groupDeclaration = FalloutCombatGroupDeclaration.ReadExecutable(source.FalloutExecutablePath);
        var snapshot = state.ActorPerception!;
        var player = records.RuntimeFormKey(0x14);
        bool Actor(FalloutFormKey key)
        {
            if (key == player) return true;
            if (!records.TryGetEffective(key, out var placed) || placed.IsDeleted || placed.Signature is not ("ACHR" or "ACRE")) return false;
            var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(placed, "NAME"));
            return !basis.IsDeleted && (placed.Signature == "ACHR" && basis.Signature == "NPC_" || placed.Signature == "ACRE" && basis.Signature == "CREA");
        }
        var count = checked(records.EffectiveRecords("ACHR").Count() + records.EffectiveRecords("ACRE").Count() + 1);
        static Exception Replay() => new InvalidOperationException("Cold perception validator replayed a living producer.");
        using var validated = new FalloutActorPerception(declaration, source.StackId, player, count, Actor,
            key => FalloutCombatActorSource.Read(records, groupDeclaration, key),
            new(_ => throw Replay(), (_, _, _, _) => throw Replay(), _ => throw Replay(), _ => throw Replay(),
                () => throw Replay(), () => throw Replay()), snapshot);
        if (validated.SaveBlocker is not null) throw new InvalidDataException("Cold perception is incomplete after exact selected source validation.");
        foreach (var reference in state.References!)
            if (records.GetEffective(reference.Reference).Signature is "ACHR" or "ACRE" &&
                !snapshot.Actors.Any(actor => actor.Source.Reference == reference.Reference))
                throw new InvalidDataException("Current perception omitted an actual constructed actor reference.");
    }
}
