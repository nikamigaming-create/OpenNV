using System.Text.Json;
using OpenNV.Runtime.Content;

if (args.Length < 3 || args[0] is not ("cells" or "script" or "replay" or "lifecycle" or "corpus"))
{
    Console.Error.WriteLine("Development only; reads owned files in place.\n" +
        "cells <installation-or-source-stack> <CELL editor ID or name fragment> [...]\n" +
        "script <installation-or-source-stack> <SCPT editor ID>\n" +
        "replay <installation-or-source-stack> <scenario.json>\n" +
        "lifecycle <installation-or-source-stack> <CELL editor ID> [...] (or --all)\n" +
        "corpus <installation-or-source-stack> <fresh-output-directory>");
    return 2;
}

RuntimeLiveContentSource.Configure(args[1], RuntimeLiveContentSource.FalloutNewVegasGame);
using var content = RuntimeLiveContentSource.Current!;
using var records = FalloutPluginStack.Load(content.PluginSources);
var json = new JsonSerializerOptions { WriteIndented = true };
if (args[0] == "corpus") return CorpusInventory.Run(records, content, args[2]);
if (args[0] == "replay") return ReferenceReplay.Run(records, args[2]);
if (args[0] == "lifecycle") return ReferenceReplay.Lifecycle(records, args[2..]);
if (args[0] == "script")
{
    var script = uint.TryParse(args[2], System.Globalization.NumberStyles.HexNumber, null, out var scriptId) ?
        records.GetEffective(records.RuntimeFormKey(scriptId)) : FalloutDialogueTopic.Find(records, "SCPT", args[2]);
    foreach (var field in script.ReadSubrecords().Where(field => field.Signature is "SLSD" or "SCVR"))
        Console.Error.WriteLine(field.Signature == "SCVR" ? "name=" + FalloutDialogueTopic.Text(field.Data.Span) :
            "slot=" + System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
    Console.WriteLine(FalloutOpeningPlayerControlResolver.ReadSource(script,
        script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span));
    return 0;
}

var failures = new List<object>();
var cells = new List<object>();
foreach (var record in records.EffectiveRecords("CELL"))
{
    var id = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "EDID");
    var name = id.Data.IsEmpty ? "" : FalloutDialogueTopic.Text(id.Data.Span);
    if (!args[2..].Any(selector => name.Contains(selector, StringComparison.OrdinalIgnoreCase) ||
        selector.Equals(records.RuntimeFormId(record.FormKey).ToString("x8"), StringComparison.OrdinalIgnoreCase))) continue;
    try
    {
        var scene = FalloutCellSceneReader.Read(records, record.FormKey);
        cells.Add(new
        {
            scene.Cell, references = scene.References.Select(reference =>
            {
                var baseObject = scene.BaseObjects[reference.Base];
                var baseRecord = records.TryGetEffective(reference.Base, out var found) ? found : null;
                var script = baseRecord?.ReadSubrecords().Any(field => field.Signature == "SCRI") == true ?
                    FalloutDialogueTopic.RequiredForm(baseRecord, "SCRI") : (FalloutFormKey?)null;
                return new
                {
                    reference.FormKey, reference.EditorId, reference.Base, baseObject.Signature,
                    baseEditorId = baseObject.EditorId, model = baseObject.ModelPath,
                    reference.EnableParent, reference.Flags, destination = reference.Teleport?.Door,
                    script, scriptEditorId = script is { } key ?
                        FalloutDialogueTopic.Text(records.GetEffective(key).ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span) : null,
                };
            }).ToArray(),
        });
    }
    catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
    {
        failures.Add(new { record.FormKey, name, error = error.Message });
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-development-cell-inventory/v1", cells, failures,
    limitation = "Source inventory only; runtime and ordinary gameplay acceptance are unverified." }, json));
return failures.Count == 0 && cells.Count != 0 ? 0 : 1;
