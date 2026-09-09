using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class ActorInventory
{
    internal static int Run(FalloutPluginStack stack, string[] selectors)
    {
        static string Text(FalloutPluginRecord record, string signature)
        {
            var data = record.ReadSubrecords().SingleOrDefault(field => field.Signature == signature).Data;
            return data.IsEmpty ? "" : FalloutDialogueTopic.Text(data.Span);
        }
        var actors = stack.EffectiveRecords("NPC_").Select(record => new
        {
            record, name = Text(record, "FULL"), editorId = Text(record, "EDID"),
        }).Where(actor => selectors.Any(selector => actor.name.Contains(selector, StringComparison.OrdinalIgnoreCase) ||
            actor.editorId.Contains(selector, StringComparison.OrdinalIgnoreCase))).ToDictionary(actor => actor.record.FormKey);
        var rows = new List<object>();
        foreach (var reference in stack.EffectiveRecords("ACHR"))
        {
            var npc = FalloutDialogueTopic.RequiredForm(reference, "NAME");
            if (!actors.TryGetValue(npc, out var actor) || reference.IsDeleted) continue;
            var cell = FalloutCellSceneReader.ParentCell(reference);
            string[] blockers;
            object[] inventory = [];
            object? resolvedAppearance = null;
            try
            {
                var appearance = FalloutNpcAppearanceResolver.Resolve(stack, npc, reference.FormKey);
                blockers = appearance.Blockers.ToArray();
                resolvedAppearance = new { appearance.Race, appearance.Eyes, appearance.SkeletonPath, appearance.Female,
                    appearance.Models, appearance.RaceParts, appearance.Armor };
                inventory = appearance.Inventory.Select(item => (object)new
                {
                    form = item.Item.ToString(), item.Signature, item.Count,
                    name = Text(stack.GetEffective(item.Item), "FULL"),
                }).ToArray();
            }
            catch (Exception error) when (error is IOException or NotSupportedException or InvalidOperationException or KeyNotFoundException)
            {
                blockers = [error.Message];
            }
            rows.Add(new
            {
                actor.name, actor.editorId, reference = reference.FormKey.ToString(), npc = npc.ToString(),
                cell = cell?.ToString(), cellEditorId = cell is { } key ? Text(stack.GetEffective(key), "EDID") : null,
                reference.Flags, initiallyDisabled = (reference.Flags & 0x800) != 0,
                referenceSha256 = Convert.ToHexString(SHA256.HashData(reference.ReadData())),
                appearanceBlockers = blockers, inventory, resolvedAppearance,
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-development-actor-inventory/v1", rows,
            limitation = "Winning source references and appearance resolution only; placement, equipment, animation and final rendering remain unverified.",
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return rows.Count == 0 ? 1 : 0;
    }
}
