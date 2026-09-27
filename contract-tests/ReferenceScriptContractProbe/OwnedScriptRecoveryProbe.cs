using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedScriptRecoveryProbe
{
    internal static void Run(string root, string savePath, string output)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var save = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(savePath))!;
        using var world = new FalloutReferenceWorld(records);
        world.RestoreEncounterZones(save.EncounterZones);
        world.Restore(save.References!);
        world.RestoreActorOverrides(save.ActorOverrides);
        world.ScriptValues.Restore(save.Scripts?.Values);
        var globals = FalloutGlobalState.Read(records); globals.Restore(save.Globals!);
        var quests = new FalloutQuestState(records); quests.Restore(save.Quests!);
        var selected = save.References!.Where(state => state.ScriptError is { } error &&
            (error.EndsWith(": Script operand GetCurrentTime has no variable owner.", StringComparison.OrdinalIgnoreCase) ||
             error.EndsWith(": Script operand GetRandomPercent has no variable owner.", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (selected.Length == 0) throw new InvalidDataException("Selected checkpoint contains no legacy clock/random read failures.");
        foreach (var cell in selected.Select(state => state.Placement?.Cell ?? state.Cell).Distinct())
            world.LoadCell(world.ComposeResidency(FalloutCellSceneReader.Read(records, cell)));
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            effect => throw new NotSupportedException($"Headless recovery audit does not execute native effect {effect.Kind}."),
            Globals: globals, IsInInterior: actor => world.IsInInterior(actor, save.ActiveCell)));
        var reports = new List<object>();
        foreach (var state in selected)
        {
            var previousError = state.ScriptError!;
            var eventName = previousError[..previousError.IndexOf(':')];
            var result = scripts.Dispatch(state.Reference, eventName);
            if (result.RecoveredError != state.ScriptError || result.Error == state.ScriptError)
                throw new InvalidDataException($"Source read failure stayed latched for {state.Reference}: {result.Error}");
            reports.Add(new { reference = state.Reference.ToString(), previous = state.ScriptError,
                next = result.Error, completedBlocks = result.Blocks });
        }
        var after = world.Capture();
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(after))!);
        if (JsonSerializer.Serialize(after) != JsonSerializer.Serialize(cold.Capture()))
            throw new InvalidDataException("Recovered reference state changed across cold save.");
        var nextValues = new FalloutScriptValueStore(); nextValues.Restore(world.ScriptValues.Capture());
        for (var index = 0; index < 100; index++)
            if (world.ScriptValues.RandomPercent() != nextValues.RandomPercent())
                throw new InvalidDataException("Owned-script random stream changed across cold save.");
        foreach (var state in save.References!.Except(selected))
            if (world.Get(state.Reference).ScriptError != state.ScriptError && state.ScriptError?.StartsWith("Parse:", StringComparison.OrdinalIgnoreCase) != true)
                throw new InvalidDataException("Recovery altered an unrelated script failure.");
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            recovered = reports.Count,
            reports,
            coldState = true,
            boundary = "Owned checkpoint query recovery; native commands, routines and presentation retain their separate failures."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"OPENNV_OWNED_SCRIPT_RECOVERY_PASS recovered={reports.Count} coldState=true coldRandom=true remainingCommands=reported");
    }
}
