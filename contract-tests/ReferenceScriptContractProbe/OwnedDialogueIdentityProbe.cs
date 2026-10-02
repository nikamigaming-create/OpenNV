using OpenNV.Runtime.Content;

internal static class OwnedDialogueIdentityProbe
{
    internal static void Run(string game, string mod, string root, string actorId, string topicId, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var actor = FalloutDialogueTopic.Find(records, "ACHR", actorId);
        var identity = FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        var quests = new FalloutQuestState(records);
        var player = records.RuntimeFormKey(0x14);
        var conditions = new FalloutDialogueConditions(records, quests, actor.FormKey, identity,
            runtime: _ => throw new InvalidOperationException("Source GetIsID escaped its shared identity owner."), listener: player);
        var topic = FalloutDialogueTopic.Read(records, topicId);
        var selected = topic.Infos.SelectMany(info => info.Conditions.Select(data => FalloutCondition.Read(info.Record, data)))
            .Where(condition => condition.Function == 72 && condition.RunOn == 1).ToArray();
        if (selected.Length == 0) throw new InvalidDataException("Selected source topic has no target GetIsID condition.");
        var npcListener = new FalloutDialogueConditions(records, quests, actor.FormKey, identity,
            runtime: _ => throw new InvalidOperationException("NPC source GetIsID used a player fallback."),
            listener: actor.FormKey, listenerIdentity: identity);
        foreach (var condition in selected)
        {
            var expected = condition.FormArgument1 == records.RuntimeFormKey(7) ? 1 : 0;
            if (conditions.Evaluate(condition) != expected ||
                npcListener.Evaluate(condition) != (condition.FormArgument1 == identity.Actor ? 1 : 0))
                throw new InvalidDataException("Owned target GetIsID selected the wrong base form.");
            // Explicit source target uses the same adjusted primitive reference.
            if (conditions.Evaluate(condition with { RunOn = 2, Reference = 0x14 }) != expected)
                throw new InvalidDataException("Explicit PlayerRef changed target base identity.");
        }
        Console.WriteLine($"OPENNV_OWNED_DIALOGUE_IDENTITY_PASS actor={actor.FormKey} topic={topic.Topic.FormKey} " +
            $"queries={selected.Length} playerBase=true npcListener=true explicitPlayer=true sourceReadonly=true campaign=unverified parity=unverified");
    }
}
