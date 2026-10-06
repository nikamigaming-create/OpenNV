using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class ActorInventoryConditionContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var owner = records.EffectiveRecords().First();
        var caller = new FalloutFormKey(owner.Plugin.Name, 0x901);
        var player = records.RuntimeFormKey(0x14);
        var item = new FalloutFormKey(owner.Plugin.Name, 0x700);
        var ownIndex = (uint)owner.Plugin.Masters.Count << FalloutFormKey.ObjectIdBits;
        var condition = new FalloutCondition(owner, 0, 0, 47, ownIndex | item.ObjectId, 0, 0, 0);
        FalloutFormKey? selected = null;
        double value = 7;
        double Count(FalloutFormKey subject, FalloutFormKey requested)
        {
            selected = subject;
            Require(requested == item, "Actor count borrowed the runtime load index for a declaring-master item.");
            return value;
        }
        Require(FalloutActorInventoryConditions.ItemCount(condition, caller, Count) == 7 && selected == caller,
            "Actor item count used the player inventory for its own subject.");
        var explicitCondition = condition with { RunOn = 2, Reference = ownIndex | caller.ObjectId };
        Require(FalloutActorInventoryConditions.ItemCount(explicitCondition, player, Count) == 7 && selected == caller,
            "Actor explicit-subject item count lost its source namespace.");
        var playerIndex = player.OwnerPlugin == owner.Plugin.Name ? ownIndex :
            (uint)owner.Plugin.Masters.ToList().FindIndex(name => name.Equals(player.OwnerPlugin, StringComparison.OrdinalIgnoreCase))
            << FalloutFormKey.ObjectIdBits;
        Require(FalloutActorInventoryConditions.ItemCount(condition with { RunOn = 2, Reference = playerIndex | player.ObjectId },
            caller, Count) == 7 && selected == player, "Actor item count did not resolve its explicit source player subject.");
        value = 0;
        Require(FalloutActorInventoryConditions.ItemCount(condition, caller, Count) == 0,
            "An actual absent item acquired an invented count.");
        foreach (var invalid in new[] { double.NaN, -1, 1.5, (double)int.MaxValue + 1 })
        {
            value = invalid; Reject(() => FalloutActorInventoryConditions.ItemCount(condition, caller, Count));
        }
        Reject(() => FalloutActorInventoryConditions.ItemCount(condition, caller, null));
        Reject(() => FalloutActorInventoryConditions.ItemCount(condition with { RunOn = 3 }, caller, Count));
        Reject(() => FalloutActorInventoryConditions.ItemCount(condition with { Function = 46 }, caller, Count));
        Console.WriteLine("OPENNV_ACTOR_INVENTORY_CONDITION_PASS self=true player=true explicitSubject=true declaringNamespace=true currentCounts=true missingOwnerRefused=true invalidCountsRefused=true");
    }

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidDataException(message); }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Unowned actor inventory predicate was admitted.");
    }
}
