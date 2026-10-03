using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static class OwnedDialoguePackageQueryProbe
{
    internal static void Run(string game, string mod, string root, string actorId, string topicId, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var actor = FalloutDialogueTopic.Find(records, "ACHR", actorId);
        var identity = FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        var topic = FalloutDialogueTopic.Read(records, topicId);
        var conditions = topic.Infos.SelectMany(info => info.Conditions.Select(data => FalloutCondition.Read(info.Record, data)))
            .Where(condition => condition.Function == 161).ToArray();
        if (conditions.Length == 0) throw new InvalidDataException("Selected source topic has no current-package conditions.");
        var quests = new FalloutQuestState(records); var player = records.RuntimeFormKey(0x14);
        foreach (var condition in conditions)
        {
            var before = SHA256.HashData(condition.Owner.ReadData());
            if (records.GetEffective(condition.FormArgument1).Signature != "PACK")
                throw new InvalidDataException("Source current-package argument is not PACK.");
            var expected = condition.RunOn switch
            {
                0 => actor.FormKey,
                1 => player,
                2 => condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference) ??
                    throw new InvalidDataException("Source current-package query has no explicit reference."),
                _ => throw new NotSupportedException("Selected source current-package query has an unbound scope."),
            };
            if (expected != player && records.GetEffective(expected).Signature is not ("ACHR" or "ACRE"))
                throw new InvalidDataException("Source current-package subject is not an actor reference.");
            FalloutFormKey? queried = null, fixturePackage = condition.FormArgument1;
            FalloutFormKey? Query(FalloutFormKey reference) { queried = reference; return fixturePackage; }
            var context = new FalloutDialogueConditions(records, quests, actor.FormKey, identity,
                runtime: _ => throw new InvalidOperationException("Source package query escaped its actor owner."), currentPackage: Query);
            if (context.Evaluate(condition) != 1 || queried != expected)
                throw new InvalidDataException("Source package query selected the wrong actor or package identity.");
            fixturePackage = null;
            if (context.Evaluate(condition) != 0 || queried != expected)
                throw new InvalidDataException("Source package query invented an assignment for an empty owner.");
            fixturePackage = condition.FormArgument1;
            var directed = new FalloutDialogueConditions(records, quests, actor.FormKey, identity,
                listener: actor.FormKey, listenerIdentity: identity, currentPackage: Query);
            if (directed.Evaluate(condition with { RunOn = 1 }) != 1 || queried != actor.FormKey ||
                !before.SequenceEqual(SHA256.HashData(condition.Owner.ReadData())))
                throw new InvalidDataException("NPC listener ownership or source bytes changed.");
        }
        Console.WriteLine($"OPENNV_OWNED_DIALOGUE_PACKAGE_QUERY_PASS actor={actor.FormKey} topic={topic.Topic.FormKey} " +
            $"queries={conditions.Length} self={conditions.Count(value => value.RunOn == 0)} " +
            $"target={conditions.Count(value => value.RunOn == 1)} explicit={conditions.Count(value => value.RunOn == 2)} " +
            "npcListener=true sourceReadonly=true packageState=fixture ordinaryInput=separate parity=unverified");
    }
}
