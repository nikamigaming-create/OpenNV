using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutAiPackages
{
    internal static FalloutPluginRecord TemplateOwner(FalloutPluginStack stack, FalloutPluginRecord record, ushort flag)
    {
        var visited = new HashSet<FalloutFormKey>();
        while (true)
        {
            if (record.Signature != "NPC_" || !visited.Add(record.FormKey))
                throw new NotSupportedException("AI package template is not an acyclic NPC graph.");
            var data = record.ReadSubrecords().Single(field => field.Signature == "ACBS").Data;
            if (data.Length != 24) throw new InvalidDataException("NPC template flags have an invalid extent.");
            if ((BinaryPrimitives.ReadUInt16LittleEndian(data.Span[22..]) & flag) == 0) return record;
            record = stack.GetEffective(FalloutDialogueTopic.RequiredForm(record, "TPLT"));
        }
    }

    internal static FalloutPluginRecord? Select(FalloutPluginStack stack, FalloutFormKey npc,
        Func<FalloutCondition, float> evaluate)
    {
        var owner = TemplateOwner(stack, stack.GetEffective(npc), 32);
        foreach (var field in owner.ReadSubrecords().Where(field => field.Signature == "PKID"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("NPC package identity has an invalid extent.");
            var package = stack.GetEffective(owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)));
            if (package.Signature != "PACK") throw new InvalidDataException("NPC package identity is not PACK.");
            // Evaluate priority in file order. A false candidate need not load its
            // target, animation or event scripts to reject it.
            if (!FalloutCondition.AllPass(FalloutCondition.Read(package), evaluate)) continue;
            var schedules = package.ReadSubrecords().Where(row => row.Signature == "PSDT").ToArray();
            if (schedules.Length != 1 || schedules[0].Data.Length != 8)
                throw new InvalidDataException("Package schedule has an invalid extent.");
            var schedule = schedules[0].Data.Span;
            if (schedule[0] != 255 || schedule[1] != 255 || schedule[2] != 0 || schedule[3] != 255 ||
                BinaryPrimitives.ReadInt32LittleEndian(schedule[4..]) != 0)
                throw new NotSupportedException($"PACK {package.FormKey} requires calendar/schedule evaluation.");
            return package;
        }
        return null;
    }
}
