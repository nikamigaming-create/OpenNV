using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutAiPackages
{
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
        Func<FalloutCondition, float> evaluate, FalloutActorTemplateSelection? selection = null, FalloutGameTime? clock = null)
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
            if (!FalloutCondition.AllPass(FalloutCondition.Read(package), evaluate)) continue;
            return package;
        }
        return null;
    }
}
