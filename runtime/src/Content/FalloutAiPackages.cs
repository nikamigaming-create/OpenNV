using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;
using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutAiPackages
{
    internal static float Sleeping(FalloutPluginStack records, FalloutCondition condition,
        FalloutFormKey caller, Func<FalloutFormKey, int> query, FalloutFormKey? target = null)
    {
        if (condition.Kind != Function.GetSleeping || condition.Argument1 != 0 || condition.Argument2 != 0)
            throw new InvalidDataException("Sleeping condition differs from the source GetSleeping declaration.");
        var subject = ConditionSubject(condition, caller, target);
        if (records.GetEffective(subject).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("GetSleeping condition subject is not a placed actor.");
        var phase = query(subject);
        return phase is >= 0 and <= 4 ? phase :
            throw new InvalidDataException("GetSleeping condition owner returned an invalid physical phase.");
    }

    internal static float Sitting(FalloutPluginStack records, FalloutCondition condition,
        FalloutFormKey caller, Func<FalloutFormKey, int>? query, FalloutFormKey? target = null)
    {
        if (condition.Kind != Function.GetSitting || condition.Argument1 != 0 || condition.Argument2 != 0)
            throw new InvalidDataException("Sitting condition differs from the source GetSitting declaration.");
        var subject = ConditionSubject(condition, caller, target);
        if (records.RuntimeFormId(subject) != 0x14 &&
            (!records.TryGetEffective(subject, out var source) || source.Signature is not ("ACHR" or "ACRE")))
            throw new InvalidDataException("GetSitting condition subject is not a placed actor.");
        var phase = (query ?? throw new NotSupportedException("GetSitting condition has no physical furniture owner."))(subject);
        return phase is >= 0 and <= 4 ? phase :
            throw new InvalidDataException("GetSitting condition owner returned an invalid physical phase.");
    }

    internal static float QuestRunning(FalloutCondition condition, FalloutQuestState quests)
    {
        if (condition.Kind != Function.GetQuestRunning)
            throw new InvalidDataException("Quest-running package query has a different source function.");
        if (condition.RunOn != 0)
            throw new NotSupportedException("Quest-running package condition requires its source subject scope owner.");
        return quests.Evaluate(condition);
    }

    internal static bool IsCurrentPackage(FalloutPluginStack records, FalloutFormKey actor,
        FalloutFormKey package, Func<FalloutFormKey, FalloutFormKey?> query)
    {
        if (records.GetEffective(package).Signature != "PACK")
            throw new InvalidDataException("Current-package argument is not PACK.");
        if (records.RuntimeFormId(actor) != 0x14 && records.GetEffective(actor).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Current-package subject is not an actor.");
        return query(actor) == package;
    }

    internal static bool HasTalkedToPlayer(FalloutCondition condition, FalloutFormKey caller,
        Func<FalloutFormKey, bool> query, FalloutFormKey? target = null)
    {
        if (condition.Kind != Function.GetTalkedToPC) throw new InvalidDataException("Talked-to-player query has a different source function.");
        return query(ConditionSubject(condition, caller, target));
    }

    internal static bool IsCurrentPackage(FalloutCondition condition, FalloutFormKey caller,
        FalloutFormKey? currentPackage, Func<FalloutFormKey, FalloutFormKey?> query)
        => IsCurrentPackage(condition, caller, reference => reference == caller ? currentPackage : query(reference));

    internal static bool IsCurrentPackage(FalloutCondition condition, FalloutFormKey caller,
        Func<FalloutFormKey, FalloutFormKey?> query, FalloutFormKey? target = null)
    {
        if (condition.Kind != Function.GetIsCurrentPackage) throw new InvalidDataException("Current-package query has a different source function.");
        return query(ConditionSubject(condition, caller, target)) == condition.FormArgument1;
    }

    internal static FalloutFormKey ConditionSubject(FalloutCondition condition, FalloutFormKey caller,
        FalloutFormKey? target = null)
        => new FalloutConditionScope(caller, target).Resolve(condition);

    internal static IReadOnlyDictionary<FalloutFormKey, sbyte> ReadFactions(FalloutPluginStack stack, FalloutFormKey npc,
        FalloutActorTemplateSelection? selection = null)
    {
        var owner = FalloutActorTemplateOwner.Resolve(stack, stack.GetEffective(npc), 4, selection);
        var result = new Dictionary<FalloutFormKey, sbyte>();
        foreach (var field in owner.ReadSubrecords().Where(field => field.Signature == "SNAM"))
        {
            if (field.Data.Length != 8) throw new InvalidDataException("NPC faction rank has an invalid extent.");
            var faction = owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            if (stack.GetEffective(faction).Signature != "FACT")
                throw new InvalidDataException("NPC faction membership does not reference FACT.");
            if (!result.TryAdd(faction, unchecked((sbyte)field.Data.Span[4])))
                throw new InvalidDataException("NPC declares duplicate faction ranks.");
        }
        return result;
    }

    internal static FalloutPluginRecord TemplateOwner(FalloutPluginStack stack, FalloutPluginRecord record, ushort flag)
        => FalloutActorTemplateOwner.Resolve(stack, record, flag);

    internal static FalloutPluginRecord? Select(FalloutPluginStack stack, FalloutFormKey npc,
        Func<FalloutCondition, float> evaluate, FalloutActorTemplateSelection? selection = null, FalloutGameTime? clock = null,
        bool evaluateRunOn = false, Func<FalloutPluginRecord, bool>? eligible = null)
    {
        var owner = FalloutActorTemplateOwner.Resolve(stack, stack.GetEffective(npc), 32, selection);
        foreach (var field in owner.ReadSubrecords().Where(field => field.Signature == "PKID"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("NPC package identity has an invalid extent.");
            var package = stack.GetEffective(owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)));
            if (package.Signature != "PACK") throw new InvalidDataException("NPC package identity is not PACK.");
            // Schedules precede conditions, in authored priority order. An
            // inactive candidate cannot run condition queries or side effects.
            if (!FalloutPackageSchedule.Read(package).IsActive(clock)) continue;
            if (eligible?.Invoke(package) == false) continue;
            if (!FalloutCondition.AllPass(FalloutCondition.Read(package), evaluate, evaluateRunOn)) continue;
            return package;
        }
        return null;
    }
}
