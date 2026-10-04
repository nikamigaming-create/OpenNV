using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedQuestObjectProbe
{
    internal static void Run(string mod, string root, string game, string questId, short stage, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        using var content = setup.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
        var original = SHA256.HashData(quest.ReadData());
        var fields = quest.ReadSubrecords().ToArray();
        var begin = Array.FindIndex(fields, field => field.Signature == "INDX" && BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span) == stage);
        if (begin < 0) throw new InvalidDataException("Selected source stage is absent.");
        var end = begin + 1; while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) end++;
        var count = 0;
        using var world = new FalloutReferenceWorld(records);
        for (var index = begin + 1; index < end;)
        {
            var next = index + 1; while (next < end && fields[next].Signature != "QSDT") next++;
            var entry = fields[index..next]; index = next;
            var sources = entry.Where(field => field.Signature == "SCTX").ToArray();
            if (sources.Length == 0) continue;
            var source = sources.Single();
            var commands = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(source.Data.Span)).Where(line =>
                line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].Equals("SetQuestObject", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (commands.Length == 0) continue;
            var bindings = new FalloutScriptBindings(records, quest, quest, entry);
            var targets = commands.Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Select(parts => (Form: bindings.Form(parts[1]), Value: parts[2] switch
                { "0" => false, "1" => true, _ => throw new NotSupportedException("Owned flag fixture needs an explicit source integer.") })).ToArray();
            var hashes = targets.Select(target => SHA256.HashData(target.Form.ReadData())).ToArray();
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new NotSupportedException("Isolated quest-object fixture reached another effect.")));
            executor.ExecuteStage(quest, entry, string.Join('\n', commands));
            if (targets.Any(target => records.QuestObjects.IsQuestObject(target.Form.FormKey) != target.Value))
                throw new InvalidDataException("Original source result did not publish its form flag.");
            var snapshot = JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(
                new FalloutScriptSession(questObjects: records.QuestObjects).Capture()))!;
            using var coldRecords = FalloutPluginStack.Load(content.PluginSources);
            new FalloutScriptSession(questObjects: coldRecords.QuestObjects).Restore(snapshot);
            foreach (var (target, hash) in targets.Zip(hashes))
                if (coldRecords.QuestObjects.IsQuestObject(target.Form.FormKey) != target.Value ||
                    !hash.AsSpan().SequenceEqual(SHA256.HashData(target.Form.ReadData())))
                    throw new InvalidDataException("Cold flags or original item bytes diverged.");
            count += commands.Length;
        }
        if (count == 0 || !original.AsSpan().SequenceEqual(SHA256.HashData(quest.ReadData())))
            throw new InvalidDataException("Owned fixture has no quest-object command or changed source bytes.");
        Console.WriteLine($"OPENNV_OWNED_QUEST_OBJECT_PASS commands={count} winningBindings=true sharedForm=true sessionCold=true sourceReadonly=true fixture=isolated-source-result-subset campaignAndParity=unverified");
    }
}
