using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCaravanItemEventProbe
{
    internal static void Run(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var player = records.RuntimeFormKey(0x14); var inspected = 0;
        foreach (var item in records.EffectiveRecords("CCRD"))
        {
            var calls = new List<string>();
            var values = new FalloutScriptValueContext(name => FalloutScriptBindings.IsPlayer(name)
                    ? FalloutScriptValue.Form(0x14) : throw new NotSupportedException($"Owned item event value {name} is unbound."),
                (_, _) => throw new NotSupportedException("Owned item event local mutation is unbound."),
                IsForm: FalloutScriptBindings.IsPlayer);
            var execution = new FalloutInventoryScriptExecution(values,
                name => name.Equals("GetContainer", StringComparison.OrdinalIgnoreCase)
                    ? FalloutScriptFunction.Typed([], _ => FalloutScriptValue.Form(0x14), readOnly: true) : null,
                (name, _) => calls.Add(name.ToLowerInvariant()));
            var blocks = FalloutInventoryScriptEvents.Dispatch(records, item, "OnAdd", player, execution);
            if (blocks != 1 || !calls.SequenceEqual(["addcardtoplayer", "removeme"]))
                throw new InvalidDataException($"Owned card {item.FormKey} has a different OnAdd command sequence.");
            ++inspected;
        }
        if (inspected == 0) throw new InvalidDataException("Owned stack has no Caravan card records.");
        Console.WriteLine($"OPENNV_OWNED_CARAVAN_ITEM_EVENT_PASS cards={inspected} sourceBranches=true commandOrder=true stagedHostOnly=true liveDeliveryAndRetail=unverified");
    }
}
