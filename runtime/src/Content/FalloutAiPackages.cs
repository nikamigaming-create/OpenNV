using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutAiPackages
{
    internal static bool HasTalkedToPlayer(FalloutCondition condition, FalloutFormKey caller,
        Func<FalloutFormKey, bool> query, FalloutFormKey? target = null)
    {
        if (condition.Function != 50) throw new InvalidDataException("Talked-to-player query has a different source function.");
        return query(ConditionSubject(condition, caller, target));
    }

    internal static bool IsCurrentPackage(FalloutCondition condition, FalloutFormKey caller,
        FalloutFormKey? currentPackage, Func<FalloutFormKey, FalloutFormKey?> query)
        => IsCurrentPackage(condition, caller, reference => reference == caller ? currentPackage : query(reference));

    internal static bool IsCurrentPackage(FalloutCondition condition, FalloutFormKey caller,
        Func<FalloutFormKey, FalloutFormKey?> query, FalloutFormKey? target = null)
    {
        if (condition.Function != 161) throw new InvalidDataException("Current-package query has a different source function.");
        return query(ConditionSubject(condition, caller, target)) == condition.FormArgument1;
    }

    internal static FalloutFormKey ConditionSubject(FalloutCondition condition, FalloutFormKey caller,
        FalloutFormKey? target = null)
    {
        var subject = condition.RunOn switch
        {
            0 => caller,
            1 => target ?? Target(),
            2 => condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference) ??
                throw new InvalidDataException("Actor condition has no explicit reference."),
            _ => throw new NotSupportedException($"Actor condition run-on {condition.RunOn} has no subject owner."),
        };
        return subject;

        FalloutFormKey Target()
        {
            if (condition.Owner.Signature != "PACK")
                throw new NotSupportedException("Actor condition has no package target owner.");
            var targets = condition.Owner.ReadSubrecords().Where(field => field.Signature == "PTDT").ToArray();
            if (targets.Length != 1 || targets[0].Data.Length != 16)
                throw new InvalidDataException("Actor condition has an invalid package target extent.");
            var target = targets[0].Data.Span;
            if (BinaryPrimitives.ReadInt32LittleEndian(target) != 0)
                throw new NotSupportedException("Actor condition requires its reference package target owner.");
            return condition.Owner.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(target[4..])) ??
                throw new InvalidDataException("Actor condition has no package target reference.");
        }
    }

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
