using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedReferenceDoorProbe
{
    internal static void Run(string game, string mod, string root, string referenceId, string questId,
        short deniedStage, short admittedStage, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var reference = FalloutDialogueTopic.Find(records, "REFR", referenceId);
        var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(reference, "NAME"));
        if (basis.Signature != "DOOR") throw new InvalidDataException("Selected reference is not an owned door.");
        var script = FalloutScriptLocals.AttachedScript(records, reference) ?? throw new InvalidDataException("Selected door has no attached script.");
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
        var source = new[] { reference, basis, script, quest };
        var hashes = source.ToDictionary(value => value.FormKey, value => SHA256.HashData(value.ReadData()));
        var player = records.RuntimeFormKey(0x14);
        foreach (var stage in new[] { deniedStage, admittedStage })
        {
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, world.Get(reference.FormKey).Cell); world.LoadCell(cell);
            var quests = new FalloutQuestState(records); quests.EnterStage(quest.FormKey, stage);
            // Selected source-condition fixture, not stage execution or campaign movement.
            var effects = new List<FalloutReferenceScriptEffect>();
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effects.Add));
            var result = scripts.Activate(reference.FormKey, player);
            if (result.Error is not null || result.Blocks != 1 || world.Get(reference.FormKey).ScriptError is not null)
                throw new InvalidDataException(result.Error ?? "Owned door activation did not execute its source block.");
            if (stage == deniedStage)
            {
                if (effects.Count != 1 || effects[0].Kind != FalloutReferenceEffectKind.Message)
                    throw new InvalidDataException("Source early-stage door guard did not suppress default activation.");
            }
            else if (effects.Count != 1 || effects[0].Kind != FalloutReferenceEffectKind.DefaultActivate ||
                effects[0].Target != reference.FormKey || effects[0].Argument != player)
                throw new InvalidDataException("Source admitted-stage door activation changed the physical door or activator.");
            if (world.Get(reference.FormKey).DoorOpen)
                throw new InvalidDataException("Script fixture invented native animation/collision completion.");
        }
        if (source.Any(value => !hashes[value.FormKey].AsSpan().SequenceEqual(SHA256.HashData(value.ReadData()))))
            throw new InvalidDataException("Owned door condition audit mutated source bytes.");
        Console.WriteLine($"OPENNV_OWNED_REFERENCE_DOOR_PASS reference={reference.FormKey} script={script.FormKey} " +
            $"stages={deniedStage},{admittedStage} sourceGuard=true defaultActivation=true physicalIdentity=true playerIdentity=true " +
            "sourceReadonly=true fixture=isolated-source-conditions nativeMotionAndCampaign=unverified");
    }
}
