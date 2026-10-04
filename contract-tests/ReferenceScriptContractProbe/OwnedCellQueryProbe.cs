using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCellQueryProbe
{
    internal static void Run(string game, string root, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", "CG04");
        var script = records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
        var fields = script.ReadSubrecords().ToArray();
        var program = FalloutGameModeProgram.Read(fields.Single(field => field.Signature == "SCTX").Data.Span);
        var bindings = new FalloutScriptBindings(records, quest, script, fields);
        var cellA = FalloutDialogueTopic.Find(records, "CELL", "Vault101a").FormKey;
        var cellB = bindings.Form("Vault101b").FormKey;
        var vault = bindings.Form("Vault101").FormKey;
        var outside = FalloutDialogueTopic.Find(records, "CELL", "Megaton").FormKey;
        var sources = new[] { quest, script, records.GetEffective(cellA), records.GetEffective(cellB),
            records.GetEffective(vault), records.GetEffective(outside) };
        var hashes = sources.Select(record => SHA256.HashData(record.ReadData())).ToArray();
        foreach (var (current, expectedStage) in new[] { (cellA, (short)0), (cellB, (short)33), (outside, (short)200) })
        {
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            quests.EnterStage(quest.FormKey, 20);
            foreach (var (name, value) in new[] { ("runTimer", 1d), ("timer", 1d), ("RadioTimer", 60d), ("autosaveDone", 1d) })
            {
                var variable = bindings.Variable(name);
                quests.SetVariable(variable.Owner, variable.Index, value);
            }
            var effects = new List<FalloutReferenceScriptEffect>();
            var queries = new List<FalloutFormKey>();
            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effects.Add,
                IsInCell: (actor, target) =>
                {
                    if (actor != records.RuntimeFormKey(0x14)) throw new InvalidDataException("Owned Escape changed its cell-query subject.");
                    queries.Add(target);
                    return world.IsInCell(actor, target, current);
                }));
            executor.ExecuteProgram(quest, script, program, .25);
            if (!queries.SequenceEqual(new[] { cellB, vault }) ||
                effects.Count != (expectedStage == 0 ? 0 : 1) ||
                effects.Any(effect => effect.Kind != FalloutReferenceEffectKind.SetStage || effect.Target != quest.FormKey || effect.Stage != expectedStage) ||
                quests.Variable(quest.FormKey, bindings.Variable("timer").Index) != .75 ||
                quests.Variable(quest.FormKey, bindings.Variable("RadioTimer").Index) != 59.75)
                throw new InvalidDataException($"Original Escape cell queries or consumed timer prefix changed for {current}.");
        }
        if (sources.Where((record, index) => !SHA256.HashData(record.ReadData()).SequenceEqual(hashes[index])).Any())
            throw new InvalidDataException("Cell query audit changed winning source bytes.");
        Console.WriteLine($"OPENNV_OWNED_CELL_QUERY_PASS quest={quest.FormKey} script={script.FormKey} " +
            "originalGameMode=true originalOperands=true firstCellAndVaultPrefix=true originalStageRequests=true " +
            "timerPrefixOnce=true sourceUnchanged=true fixture=isolated-original-Escape-script campaign=false framesRecorded=false");
    }
}
