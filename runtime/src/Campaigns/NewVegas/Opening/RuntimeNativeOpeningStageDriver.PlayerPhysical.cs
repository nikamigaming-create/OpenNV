using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal void ConfigureCurrentPlayerPhysicalActivity(FalloutPlayerPhysicalSnapshot? restore,
        Func<FalloutFormKey, Transform3D> furniturePlacement)
    {
        var source = _advancementRuntimeSource ??
            throw new InvalidOperationException("Physical player initialization requires its living selected executable owner.");
        _player.ConfigurePlayerPhysicalActivity(_pluginStack, _quests, _scripts.References!, source.Receipt,
            () => Vitals, name => ReadPlayerActorValue(name, FalloutActorValueRead.Current),
            PlayerProgressCondition, restore, furniturePlacement);
        _player.PublishRequiredPlayerPhysicalBody();
    }

    private int ActorSleeping(FalloutFormKey actor) => _pluginStack.RuntimeFormId(actor) == 0x14
        ? _player.GetPlayerSleeping()
        : throw new NotSupportedException($"GetSleeping actor {actor} has no source sleep procedure continuation owner.");

    private int ActorKnockedState(FalloutFormKey actor)
    {
        if (_pluginStack.RuntimeFormId(actor) == 0x14) return _player.GetPlayerKnockedState();
        var node = ReferencePresentation().TryResolve(actor) ??
            throw new NotSupportedException("GetKnockedState subject has no attached native actor.");
        return (RuntimeNativeActorCombat.Find(node) ??
            throw new NotSupportedException("GetKnockedState subject has no actual physical combat owner.")).GetKnockedState();
    }
}
