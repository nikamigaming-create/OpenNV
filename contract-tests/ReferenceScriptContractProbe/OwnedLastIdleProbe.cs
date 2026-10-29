using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedLastIdleProbe
{
    internal static void Run(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var owner = records.GetEffective(new("FalloutNV.esm", 0x08747d));
        var condition = FalloutCondition.Read(owner).Single(value => value.Kind == Function.IsLastIdlePlayed);
        var actor = new FalloutFormKey("FalloutNV.esm", 0x157c8f);
        var requested = condition.FormArgument1;
        using var world = new FalloutReferenceWorld(records);
        var runtime = new FalloutConditionRuntime(records, new(records), world);
        var state = world.Get(actor);
        state.InitializeIdleRequestHistory(newProcess: true);
        state.QueryLastIdleRequest = () => state.LastIdleRequest;
        if (runtime.Evaluate(condition, new(actor)) != 0)
            throw new InvalidDataException("Owned new process manufactured a last request.");
        state.RecordIdleRequest(records, requested);
        if (runtime.Evaluate(condition, new(actor)) != 1 ||
            runtime.Evaluate(condition with { RunOn = 1 }, new(records.RuntimeFormKey(0x14), actor)) != 1)
            throw new InvalidDataException("Owned request did not satisfy its actual source predicate.");
        var saved = JsonSerializer.Serialize(world.Capture());
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(saved)!);
        cold.Get(actor).QueryLastIdleRequest = () => cold.Get(actor).LastIdleRequest;
        if (new FalloutConditionRuntime(records, new(records), cold).Evaluate(condition, new(actor)) != 1 ||
            JsonSerializer.Serialize(cold.Capture()) != saved)
            throw new InvalidDataException("Owned last request did not cold restore exactly.");
        Console.WriteLine($"OPENNV_OWNED_LAST_IDLE_PASS condition={owner.FormKey} actor={actor} requested={requested} " +
            $"sourceHash={state.LastIdleRequest!.Sha256} newEmpty=true acceptedRequest=true sharedContexts=true coldExact=true gameplay=unverified");
    }
}
