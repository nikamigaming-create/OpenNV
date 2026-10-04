using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedDialoguePackageQueryProbe
{
    internal static void TtwActivation(string game, string root, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var actor = records.GetEffective(new("Fallout3.esm", 0x01cfb8));
        world.LoadCell(FalloutCellSceneReader.Read(records, world.Get(actor.FormKey).Cell));
        var body = records.GetEffective(FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        var script = records.GetEffective(FalloutDialogueTopic.RequiredForm(body, "SCRI"));
        var before = SHA256.HashData(script.ReadData());
        var packages = script.ReadSubrecords().Where(field => field.Signature == "SCRO")
            .Select(field => script.Plugin.AdjustFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)))
            .Where(key => records.RuntimeFormId(key) != 0x14 && records.GetEffective(key).Signature == "PACK").ToArray();
        if (packages.Length < 2) throw new InvalidDataException("Selected activation has no source package alternatives.");
        var quests = new FalloutQuestState(records);
        var quest = records.GetEffective(new("Fallout3.esm", 0x014e85)).FormKey;
        quests.SetRunning(quest, true); quests.EnterStage(quest, 20);
        FalloutFormKey? assignment = null;
        var activations = 0; var queries = 0;
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.DefaultActivate || effect.Target != actor.FormKey)
                    throw new InvalidDataException("Source activation acquired an unrelated effect.");
                activations++;
            }, CurrentPackage: reference =>
            {
                if (records.GetEffective(reference).Signature != "ACHR")
                    throw new InvalidDataException("Source activation package query lost its actor.");
                queries++; return assignment;
            }));
        void Activate()
        {
            var result = scripts.Activate(actor.FormKey, records.RuntimeFormKey(0x14));
            if (result.Blocks != 1 || result.Error is not null)
                throw new InvalidDataException(result.Error ?? "Source OnActivate was not dispatched.");
        }
        Activate();
        if (activations != 1 || queries != packages.Length)
            throw new InvalidDataException("Source activation did not inspect the live package alternatives.");
        assignment = packages[0]; activations = 0; queries = 0;
        Activate();
        if (activations != 0 || queries != 1)
            throw new InvalidDataException("Source activation ignored a matching assignment or lost short-circuit evaluation.");
        quests.EnterStage(quest, 60); queries = 0;
        Activate();
        if (activations != 1 || queries != 0 || !before.SequenceEqual(SHA256.HashData(script.ReadData())))
            throw new InvalidDataException("Source activation retained stale stage/assignment state or modified source bytes.");
        Console.WriteLine($"OPENNV_OWNED_TTW_PACKAGE_ACTIVATION_PASS actor={actor.FormKey} sourceScript=true " +
            "livePackage=true shortCircuit=true stageGates=true sourceReadonly=true campaign=unverified");
    }

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
