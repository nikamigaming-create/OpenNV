using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedTalkingActivatorProbe
{
    internal static void Run(string game, string mod, string root, string questId, short stage,
        string referenceId, string actorId, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
        var reference = FalloutDialogueTopic.Find(records, "REFR", referenceId);
        var actor = FalloutDialogueTopic.Find(records, "ACHR", actorId);
        var sourceRecords = new[] { quest, reference, actor, records.GetEffective(FalloutDialogueTopic.RequiredForm(reference, "NAME")) };
        var hashes = sourceRecords.ToDictionary(record => record.FormKey, record => SHA256.HashData(record.ReadData()));
        var quests = new FalloutQuestState(records);
        var effects = new List<FalloutReferenceScriptEffect>();
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            if (effect.Kind != FalloutReferenceEffectKind.EvaluatePackages)
                throw new NotSupportedException($"Isolated talking activator fixture cannot own {effect.Kind}.");
            effects.Add(effect); // The fixture retains EVP requests; it does not run native procedures.
        }));
        new FalloutQuestStages(records, quests, scripts.StageSteps,
            condition => throw new NotSupportedException($"Isolated stage condition {condition.Function} is unbound.")).Enter(quest.FormKey, stage);
        if (world.DialogueSubject(reference.FormKey) != actor.FormKey || effects.Count == 0)
            throw new InvalidDataException("Authored stage did not bind its real talking activator actor.");
        var combatPredicates = 0;
        foreach (var effect in effects)
        {
            var owner = records.GetEffective(effect.Target ?? effect.Source);
            var actorBase = records.GetEffective(FalloutDialogueTopic.RequiredForm(owner, "NAME"));
            foreach (var package in actorBase.ReadSubrecords().Where(field => field.Signature == "PKID")
                .Select(field => records.GetEffective(actorBase.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)))))
            {
                foreach (var condition in FalloutCondition.Read(package).Where(value => value.Function == 289))
                {
                    var subject = FalloutAiPackages.ConditionSubject(condition, owner.FormKey);
                    if (world.IsInCombat(subject)) throw new InvalidDataException("Fresh owned combat fixture acquired an engagement.");
                    ++combatPredicates;
                }
            }
        }
        var identity = world.DialogueIdentity(reference.FormKey);
        if (identity.Actor != FalloutDialogueTopic.RequiredForm(actor, "NAME") || identity.RecordType != "NPC_")
            throw new InvalidDataException("Borrowed dialogue did not retain the actual actor base/voice.");
        using var cold = new FalloutReferenceWorld(records); cold.Restore(world.Capture());
        if (cold.DialogueSubject(reference.FormKey) != actor.FormKey || cold.DialogueIdentity(reference.FormKey) != identity)
            throw new InvalidDataException("Talking activator source binding did not restore cold.");
        // Execute the same compiled reference scope with the command's absent
        // optional actor, then verify that the source VNAM is authoritative again.
        var fields = quest.ReadSubrecords().ToArray();
        var begin = Array.FindIndex(fields, field => field.Signature == "INDX" &&
            BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span) == stage);
        var end = begin + 1;
        while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) ++end;
        foreach (var _ in scripts.StageSteps(quest, fields[(begin + 1)..end], referenceId + ".SetTalkingActivatorActor")) { }
        if (world.DialogueSubject(reference.FormKey) != reference.FormKey || world.DialogueIdentity(reference.FormKey).RecordType != "TACT")
            throw new InvalidDataException("Absent optional actor did not restore the source talking activator identity.");
        foreach (var (key, hash) in hashes)
            if (!hash.SequenceEqual(SHA256.HashData(records.GetEffective(key).ReadData())))
                throw new InvalidDataException("Talking activator execution mutated owned input.");
        Console.WriteLine($"OPENNV_OWNED_TALKING_ACTIVATOR_PASS reference={reference.FormKey} actor={actor.FormKey} " +
            $"voice={identity.VoiceType} sourceStageScope=true coldBinding=true clearBinding=true sourceReadonly=true " +
            $"combatPredicates={combatPredicates} fixture=explicit-stage-retained-EVP-requests nativeProceduresAudioAndCampaign=separate parity=unverified");
    }
}
