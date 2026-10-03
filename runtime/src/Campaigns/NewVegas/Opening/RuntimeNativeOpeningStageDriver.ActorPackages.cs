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
        // An unloaded/disabled actor selects its packages when materialized.
        if (!world.IsResident(reference) || !world.IsEnabled(reference)) return;
        var nodes = GetTree().Root.FindChildren("*", "", true, false);
        if (nodes.OfType<RuntimeNativeNpc>().SingleOrDefault(actor => actor.Appearance.Reference == reference) is { } npc)
            npc.EvaluatePackages(reset);
        else if (nodes.OfType<RuntimeNativeCreature>().SingleOrDefault(actor => actor.Appearance.Reference == reference) is { } creature)
            creature.EvaluatePackages(reset);
        else throw new NotSupportedException($"Resident actor {reference} has no package presentation owner.");
    }
}
