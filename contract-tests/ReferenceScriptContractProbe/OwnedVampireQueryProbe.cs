using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static class OwnedVampireQueryProbe
{
    internal static void Run(string game, string mod, string root, string actorId, string topicId, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var executable = Path.Combine(game, "FalloutNV.exe");
        var before = SHA256.HashData(File.ReadAllBytes(executable));
        var queries = new FalloutActorQueries(); var reads = 0;
        using var binding = queries.BindVampire(() => { reads++; return FalloutExecutableStringTable.ReadVampireQueryDeclaration(executable); });
        var actor = FalloutDialogueTopic.Find(records, "ACHR", actorId);
        var identity = FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        var topic = FalloutDialogueTopic.Read(records, topicId);
        var conditions = topic.Infos.SelectMany(info => info.Conditions.Select(data => FalloutCondition.Read(info.Record, data)))
            .Where(condition => condition.Function == 40).ToArray();
        if (conditions.Length == 0) throw new InvalidDataException("Selected owned topic has no vampire queries.");
        var context = new FalloutDialogueConditions(records, new(records), actor.FormKey, identity,
            vampireQuery: queries.GetVampire);
        foreach (var condition in conditions)
        {
            var source = SHA256.HashData(condition.Owner.ReadData());
            foreach (var scope in new uint[] { 0, 1, 2 })
                if (context.Evaluate(condition with { RunOn = scope }) != 0)
                    throw new InvalidDataException("Owned false actor predicate changed its numeric result.");
            if (!source.SequenceEqual(SHA256.HashData(condition.Owner.ReadData())))
                throw new InvalidDataException("Owned vampire query changed INFO bytes.");
        }
        if (reads != 1 || !before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(executable))))
            throw new InvalidDataException("Owned query was reread per condition or changed the executable.");
        Console.WriteLine($"OPENNV_OWNED_VAMPIRE_QUERY_PASS topic={topic.Topic.FormKey} queries={conditions.Length} " +
            "sourceDeclaration=true sourceReadonly=true result=0 lazyReads=1 ordinaryInput=separate parity=unverified");
    }
}
