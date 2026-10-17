using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutFormKey? CurrentActorPackage(FalloutFormKey actor) => _pluginStack.RuntimeFormId(actor) == 0x14
        ? (_playerPackage ?? throw new InvalidOperationException("Player has no native package owner.")).CurrentPackage
        : (_scripts.References ?? throw new InvalidOperationException("Actor packages have no shared reference world."))
            .CurrentPackage(actor);

    private void EvaluateActorPackages(FalloutFormKey reference, bool reset)
    {
        var world = _scripts.References!;
        var state = world.Get(reference);
        if (_pluginStack.GetEffective(state.Base).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Package evaluation target is not an actor.");
        if (!world.IsEnabled(reference)) return;
        if (!world.IsResident(reference)) { _ = world.CurrentPackage(reference); return; }
        // Enable-parent changes can make a source actor resident before its
        // next presentation update. Use the same source factory as that update
        // instead of treating the missing node as a missing gameplay actor.
        var actor = ReferencePresentation().Resolve(reference);
        if (actor is RuntimeNativeNpc npc)
            npc.EvaluatePackages(reset);
        else if (actor is RuntimeNativeCreature creature)
            creature.EvaluatePackages(reset);
        else throw new NotSupportedException($"Resident actor {reference} has no package presentation owner.");
    }
}
