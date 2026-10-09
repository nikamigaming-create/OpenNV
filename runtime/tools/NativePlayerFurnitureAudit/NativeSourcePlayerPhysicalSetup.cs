using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

// Diagnostic setup uses the product's actual player body and shared state. It
// cannot stand in for New Game, ordinary input, campaign or visual acceptance.
internal static class NativeSourcePlayerPhysicalSetup
{
    internal static void Configure(RuntimeNativePlayer player, FalloutPluginStack records,
        FalloutQuestState quests, FalloutReferenceWorld world, FalloutNpcAppearance appearance,
        FalloutAdvancementRuntimeReceipt runtime, Func<FalloutFormKey, Transform3D> furniturePlacement)
    {
        var values = new FalloutPlayerActorValues(records);
        var vitals = FalloutPlayerVitals.FromActorValues(records, values);
        var globals = FalloutGlobalState.Read(records);
        var inventory = new FalloutPlayerInventory();
        var playerKey = records.RuntimeFormKey(0x14);
        inventory.Replace(world.Inventory(playerKey, values.Source.Level, globals).Contents.Capture());
        world.BindPlayerInventory(inventory);
        player.ConfigurePresentation(records, inventory, () => appearance with { Reference = playerKey }, () => Colors.White);
        player.ConfigureLocomotion(records, () => vitals.State);
        player.ConfigurePlayerPhysicalActivity(records, quests, world, runtime, () => vitals.State,
            name => values.ReadCurrent(FalloutPlayerActorValues.SpecialValue(name)),
            condition => throw new NotSupportedException($"Physical diagnostic condition {condition.Function}/{condition.RunOn} requires its actual gameplay consumer."),
            liveFurniturePlacement: furniturePlacement);
        player.PublishRequiredPlayerPhysicalBody();
    }
}
