using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

// Source program/message integration only. Skill and inventory observations
// below are fixtures; ordinary repair and native UI are separate acceptance.
internal static class OwnedRepairMessageProbe
{
    internal static void Run(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var broken = records.RuntimeFormKey(0x1572e6);
        var player = records.RuntimeFormKey(0x14);
        world.LoadCell(FalloutCellSceneReader.Read(records, world.Get(broken).Cell));
        var quests = new FalloutQuestState(records);
        var messages = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(),
            new FalloutPlayerInventory(), defaultProcessingDelay: 5, references: world);
        var script = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            if (effect.Kind != FalloutReferenceEffectKind.Message) throw new NotSupportedException($"Unexpected repair effect {effect.Kind}.");
            messages.ShowMessage(effect.Target!.Value, world.Get(effect.Source).Script!.Record.FormKey, effect.Source);
        }, messages.MessageResults.Take, IsInCombat: _ => false));
        void Require(bool accepted, string error) { if (!accepted) throw new InvalidDataException(error); }
        FalloutSourceMessage Take(uint expected)
        {
            Require(messages.TryTakeMessage(out var message) && message!.Form == records.RuntimeFormKey(expected) &&
                !messages.TryTakeMessage(out _), "Owned menu did not expose exactly its current actionable request.");
            return message!.ResolveButtons(records, condition => condition.Function switch
            {
                53 => (float)world.Get(condition.FormArgument1).Read(condition.Argument2),
                14 => 31, // Below the authored Repair/Science thresholds.
                47 => 0, // No invented repair components.
                _ => throw new NotSupportedException($"Unexpected repair condition {condition.Function}.")
            });
        }
        void Select(FalloutSourceMessage message, int index)
        {
            Require(message.ButtonIndices!.Contains(index) && messages.MessageResults.Select(message.Request!, index),
                "Owned visible choice did not own the result slot.");
            var result = script.Dispatch(broken, "GameMode", elapsedSeconds: .1);
            Require(result.Error is null, "Owned menu result failed: " + result.Error);
        }
        var activated = script.Activate(broken, player);
        Require(activated.Error is null && activated.Blocks == 2, "Owned activation lost its two authored blocks.");
        var initial = Take(0x1572ee);
        Require(initial.ButtonIndices!.SequenceEqual(new[] { 0, 1, 3 }), "Initial repair button indices changed.");
        Select(initial, 1);
        var repair = Take(0x1572ef);
        Require(repair.ButtonIndices!.SequenceEqual(new[] { 0, 2, 3 }), "Repair skill gate admitted an ineligible action.");
        Select(repair, 3);
        var parts = Take(0x1717d3);
        Require(parts.ButtonIndices!.SequenceEqual(new[] { 0, 2, 3 }), "Parts gate admitted missing components.");
        Select(parts, 0);
        Require(!messages.TryTakeMessage(out _) && world.Get(broken).Read(world.Get(broken).Script!.Locals["iMenu"]) == 0,
            "Leaving the repair menu repeated a prompt or retained a submenu.");
        Console.WriteLine("OPENNV_OWNED_REPAIR_MESSAGES_PASS duplicateActivation=true firstChoice=true repairGate=true partsGate=true leave=true ordinaryRepair=separate");
    }
}
