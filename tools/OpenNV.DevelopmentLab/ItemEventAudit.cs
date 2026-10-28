using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

// Static source inventory only. It does not run item scripts or prove which
// inventory operations deliver events in the retail engine.
internal static class ItemEventAudit
{
    internal static int Run(FalloutPluginStack records)
    {
        var items = records.EffectiveRecords().Where(record => FalloutReferenceWorld.IsInventoryItem(record.Signature)).ToArray();
        var scripts = new Dictionary<FalloutFormKey, (FalloutPluginRecord Record, int Items)>();
        var failures = new List<object>();
        var noScript = 0; var attachmentFailures = 0;
        foreach (var item in items)
        {
            try
            {
                var script = FalloutScriptLocals.AttachedScript(records, item);
                if (script is null) { ++noScript; continue; }
                if (scripts.TryGetValue(script.FormKey, out var existing))
                    scripts[script.FormKey] = (existing.Record, checked(existing.Items + 1));
                else scripts.Add(script.FormKey, (script, 1));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            {
                ++attachmentFailures;
                failures.Add(new { source = item.FormKey.ToString(), lane = "script-attachment", error = error.Message });
            }
        }
        var eventCounts = new Dictionary<string, (int Scripts, int Items)>(StringComparer.OrdinalIgnoreCase);
        var addCommands = new Dictionary<string, (int Scripts, int Items)>(StringComparer.OrdinalIgnoreCase);
        var addFilters = new Dictionary<string, (int Scripts, int Items)>(StringComparer.OrdinalIgnoreCase);
        var addScriptsWithLocals = 0; var addItemsWithLocals = 0; var localInspectionFailures = 0;
        var parsed = 0; var sourceAbsent = 0; var sourceFailed = 0;
        foreach (var (key, entry) in scripts)
        {
            try
            {
                var fields = entry.Record.ReadSubrecords().Where(field => field.Signature == "SCTX").ToArray();
                if (fields.Length == 0) { ++sourceAbsent; continue; }
                if (fields.Length != 1) throw new InvalidDataException("Script has ambiguous source bodies.");
                var events = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(fields[0].Data.Span));
                ++parsed;
                foreach (var name in events.Select(value => value.Event).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var previous = eventCounts.GetValueOrDefault(name);
                    eventCounts[name] = (checked(previous.Scripts + 1), checked(previous.Items + entry.Items));
                }
                var onAdd = events.Where(value => value.Event.Equals("OnAdd", StringComparison.OrdinalIgnoreCase)).ToArray();
                foreach (var command in onAdd.SelectMany(value => value.Program.CommandNames).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var previous = addCommands.GetValueOrDefault(command);
                    addCommands[command] = (checked(previous.Scripts + 1), checked(previous.Items + entry.Items));
                }
                foreach (var filter in onAdd.Select(value => value.Filter ?? "<none>").Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var previous = addFilters.GetValueOrDefault(filter);
                    addFilters[filter] = (checked(previous.Scripts + 1), checked(previous.Items + entry.Items));
                }
                if (onAdd.Length > 0)
                {
                    try
                    {
                        if (FalloutScriptLocals.ReadDeclarations(entry.Record).Count > 0)
                        { ++addScriptsWithLocals; addItemsWithLocals = checked(addItemsWithLocals + entry.Items); }
                    }
                    catch (Exception error) when (error is InvalidDataException or NotSupportedException or FormatException)
                    {
                        ++localInspectionFailures;
                        failures.Add(new { source = key.ToString(), lane = "script-locals", error = error.Message });
                    }
                }
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or FormatException)
            {
                ++sourceFailed;
                failures.Add(new { source = key.ToString(), lane = "script-source", error = error.Message });
            }
        }
        if (noScript + scripts.Values.Sum(value => value.Items) + attachmentFailures != items.Length ||
            parsed + sourceAbsent + sourceFailed != scripts.Count)
            throw new InvalidDataException("Item event inventory does not account for its source denominator.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-item-event-audit/v1", itemDenominator = items.Length, noScript,
            scriptedItems = scripts.Values.Sum(value => value.Items), attachmentFailures, distinctScripts = scripts.Count,
            parsedScripts = parsed, sourceAbsent, sourceFailed, failures,
            events = eventCounts.OrderByDescending(pair => pair.Value.Items).Select(pair =>
                new { name = pair.Key, scripts = pair.Value.Scripts, items = pair.Value.Items }),
            onAdd = new { scriptsWithLocals = addScriptsWithLocals, itemsWithLocals = addItemsWithLocals,
                localInspectionFailures,
                filters = addFilters.OrderByDescending(pair => pair.Value.Items).Select(pair =>
                    new { filter = pair.Key, scripts = pair.Value.Scripts, items = pair.Value.Items }),
                commands = addCommands.OrderByDescending(pair => pair.Value.Items).Select(pair =>
                    new { name = pair.Key, scripts = pair.Value.Scripts, items = pair.Value.Items }) },
            boundary = "Winning item definitions and attached source event declarations only. Compiled-only bodies, actual event delivery, inventory instances, effects, persistence and retail behavior are unverified."
        }));
        return failures.Count == 0 && sourceAbsent == 0 ? 0 : 1;
    }
}
