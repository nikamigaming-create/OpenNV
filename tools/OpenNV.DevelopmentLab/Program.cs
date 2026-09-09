using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Formats.Gamebryo;

if (args.Length >= 3 && args[0] == "classic-interactions") return ClassicInteractionProbe.Run(args[1], args[2], args[3..]);
if (args is ["cell-review", var reviewSource, var reviewSnapshot]) return CellReview.Run(reviewSource, reviewSnapshot);

if (args.Length < 2 || args.Length < 3 && args[0] != "classic-movement" || args[0] is not ("route" or "classic-items" or "classic-campaign-start" or "classic-script" or "classic-item-systems" or "classic-inventory" or "classic-resource" or "classic-frm" or "classic-assets" or "classic-scenery" or "classic-movement" or "classic-player" or "cells" or "exterior" or "actors" or "script" or "record" or "settings" or "dialogue" or "replay" or "lifecycle" or "corpus" or "resources" or "resource" or "nif" or "menu"))
{
    Console.Error.WriteLine("Development only; reads owned files in place.\n" +
        "cells <installation-or-source-stack> <CELL editor ID or name fragment> [...]\n" +
        "route <installation-or-source-stack> <request.json: world runtime hex, start/end game units>\n" +
        "cell-review <installation-or-source-stack> <native detailed state.json>\n" +
        "exterior <installation-or-source-stack> <CELL runtime hex ID> [grid diameter]\n" +
        "actors <installation-or-source-stack> <NPC name fragment> [...]\n" +
        "classic-player <Fallout 1 installation> <owned appearance Data root>\n" +
        "classic-frm <Fallout 1 installation> <logical FRM path> [...]\n" +
        "classic-resource <Fallout 1/2 installation> <logical resource path> [...]\n" +
        "classic-script <Fallout 1/2 installation> <logical INT path> [procedure]\n" +
        "classic-campaign-start <Fallout 2 installation> <random contract.json>\n" +
        "classic-inventory <Fallout 1/2 installation> <fallout-1|fallout-2>\n" +
        "classic-item-systems <Fallout 1/2 installation> <fallout-1|fallout-2>\n" +
        "classic-interactions <Fallout 1/2 installation> <fallout-1|fallout-2> [MAP paths | --all]\n" +
        "classic-items <Fallout 1/2 installation> <fallout-1|fallout-2>\n" +
        "classic-scenery <Fallout 1 installation> <authored-scenery recipe.json>\n" +
        "classic-assets <Fallout 1 installation> <Fallout 2 installation> <FNV Data> <FO3 Data> <runtime directory> <private output directory>\n" +
        "classic-movement <Fallout 1 installation>\n" +
        "script <installation-or-source-stack> <SCPT editor ID> (or --contains <source text>)\n" +
        "record <installation-or-source-stack> <signature> <editor ID or runtime hex ID> [...] (or --contains <EDID text>)\n" +
        "settings <installation-or-source-stack> <float-setting name fragment> [...]\n" +
        "dialogue <installation-or-source-stack> <quest editor ID>\n" +
        "replay <installation-or-source-stack> <scenario.json>\n" +
        "lifecycle <installation-or-source-stack> <CELL editor ID> [...] (or --all)\n" +
        "corpus <installation-or-source-stack> <fresh-output-directory>\n" +
        "resources <installation-or-source-stack> <logical-directory>\n" +
        "resource <installation-or-source-stack> <logical-path> [private-output-file]\n" +
        "nif <installation-or-source-stack> <logical-path>\n" +
        "menu <installation-or-source-stack> <logical-path> [tile-name]");
    return 2;
}

if (args[0] == "classic-campaign-start") return ClassicCampaignStartProbe.Run(args[1], args[2]);
if (args[0] == "classic-items")
{
    using IFalloutClassicOwnedSource classic = args[2] == "fallout-1" ? Fallout1OwnedContentSource.LoadInstall(args[1])
        : OpenNV.Runtime.Campaigns.Fallout2.Native.Fo2NativeOwnedSource.LoadInstall(args[1]);
    using var catalog = new ClassicMapCatalog(classic, args[2]);
    var definitions = new ClassicItemDefinitions(catalog);
    var count = Fallout1NativeLists.Read(catalog.Read("proto/items/items.lst", out _)).Count;
    for (var pid = 1; pid <= count; pid++)
    {
        var item = definitions.Read(pid);
        var prototype = Fallout1NativeObjectGraphReader.ResolvePrototype(catalog, pid);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            pid,
            item.Name,
            item.Icon,
            item.Subtype,
            art = Fallout1NativePrototypeReader.ResolveArt(catalog, prototype.Fid!.Value)
        }));
    }
    return 0;
}
if (args[0] == "classic-script")
{
    using IFalloutClassicOwnedSource classic = System.IO.Path.GetFileName(args[1]).Contains("2", StringComparison.Ordinal)
        ? OpenNV.Runtime.Campaigns.Fallout2.Native.Fo2NativeOwnedSource.LoadInstall(args[1]) : Fallout1OwnedContentSource.LoadInstall(args[1]);
    var sourceBytes = classic.Read(args[2], out _);
    var program = ClassicNativeIntReader.Read(sourceBytes, args[2]);
    foreach (var procedure in program.ProcedureOrder.Where(row => args.Length < 4 || row.Name == args[3]))
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            procedure.Name,
            procedure.BodyOffset,
            procedure.CanonicalEpilogueOffset,
            count = procedure.Instructions.Count,
            instructions = args.Length < 4 ? null : procedure.Instructions
        }));
    return 0;
}
if (args[0] == "classic-resource")
{
    IFalloutClassicOwnedSource classic = System.IO.Path.GetFileName(args[1]).Contains("2", StringComparison.Ordinal)
        ? OpenNV.Runtime.Campaigns.Fallout2.Native.Fo2NativeOwnedSource.LoadInstall(args[1]) : Fallout1OwnedContentSource.LoadInstall(args[1]);
    foreach (var path in args[2..])
    {
        var bytes = classic.Read(path, out _);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            path,
            length = bytes.Length,
            words = Enumerable.Range(0, Math.Min(20, bytes.Length / 4))
            .Select(i => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(i * 4))).ToArray()
        }));
        if (path.EndsWith(".msg", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(System.Text.Encoding.Latin1.GetString(bytes));
    }
    return 0;
}
if (args[0] == "classic-inventory") return ClassicInventoryProbe.Run(args[1], args[2]);
if (args[0] == "classic-item-systems") return ClassicItemSystemsProbe.Run(args[1], args[2]);
if (args[0] == "classic-player") return ClassicPlayerInventory.Run(args[1], args[2]);
if (args[0] == "classic-frm")
{
    var classic = Fallout1OwnedContentSource.LoadInstall(args[1]);
    var classicFrameFailures = 0;
    foreach (var path in args[2..])
    {
        var bytes = classic.Read(path).Bytes;
        Console.WriteLine(JsonSerializer.Serialize(new { path, length = bytes.Length, header = Convert.ToHexString(bytes.AsSpan(0, Math.Min(62, bytes.Length))) }));
        var sequential = 62;
        for (var direction = 0; direction < 6 && sequential < bytes.Length; direction++)
        {
            var start = sequential;
            var frames = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8));
            for (var frame = 0; frame < frames && sequential <= bytes.Length - 12; frame++)
            {
                var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(sequential + 4));
                if (size > bytes.Length - sequential - 12) break;
                sequential += 12 + (int)size;
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                path,
                direction,
                sequentialStart = start - 62,
                sequentialEnd = sequential - 62,
                declaredStart = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(34 + direction * 4))
            }));
        }
        for (var direction = 0; direction < 6; direction++)
        {
            try
            {
                var first = Fallout1NativeFrmReader.ReadFirstFrame(bytes, direction);
                for (var frame = 0; frame < first.FramesPerDirection; frame++) _ = Fallout1NativeFrmReader.ReadFrame(bytes, direction, frame);
                Console.WriteLine(JsonSerializer.Serialize(new { path, direction, first.Width, first.Height, first.FramesPerDirection, allFrames = true }));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or OverflowException)
            { classicFrameFailures++; Console.WriteLine(JsonSerializer.Serialize(new { path, direction, error = error.Message })); }
        }
    }
    return classicFrameFailures == 0 ? 0 : 1;
}
if (args[0] == "classic-scenery") return ClassicSceneryInventory.Run(args[1], args[2]);
if (args[0] == "classic-assets") return ClassicAssetInventory.Run(args[1..]);
if (args[0] == "classic-movement") return ClassicMovementProbe.Run(args[1]);
RuntimeLiveContentSource.Configure(args[1], RuntimeLiveContentSource.FalloutNewVegasGame);
using var content = RuntimeLiveContentSource.Current!;
using var records = FalloutPluginStack.Load(content.PluginSources);
var json = new JsonSerializerOptions { WriteIndented = true };
if (args[0] is "resource" or "nif")
{
    if (!content.TryRead(args[2], null, out var bytes, out var identity)) throw new FileNotFoundException(args[2]);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        identity,
        length = bytes.Length,
        sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
    }, json));
    if (args[0] == "resource")
    {
        if (args.Length == 4)
        {
            using var output = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write);
            output.Write(bytes);
        }
        else Console.WriteLine(Convert.ToHexString(bytes.AsSpan(0, Math.Min(256, bytes.Length))));
    }
    else
    {
        var nif = FalloutNifFile.Read(bytes);
        var nifJson = new JsonSerializerOptions(json) { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        foreach (var block in nif.Blocks)
        {
            try { Console.WriteLine(JsonSerializer.Serialize(nif.ReadObject(block.Index), nif.ReadObject(block.Index).GetType(), nifJson)); }
            catch (NotSupportedException error) { Console.WriteLine(JsonSerializer.Serialize(new { block, unsupported = error.Message }, json)); }
        }
    }
    return 0;
}
if (args[0] == "resources")
{
    foreach (var path in content.ResourcePathsUnder(args[2])) Console.WriteLine(path);
    return 0;
}
if (args[0] == "menu")
{
    if (args.Length == 4 && args[3] == "--raw")
    {
        if (!content.TryRead(args[2], null, out var bytes, out _)) throw new FileNotFoundException(args[2]);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
        return 0;
    }
    var source = FalloutMenuXml.Expand(FalloutMenuXml.Read(args[2]));
    if (args.Length == 4) Console.WriteLine(source.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == args[3]));
    else Console.WriteLine(JsonSerializer.Serialize(source.DescendantsAndSelf().Where(tile => tile.Attribute("name") is not null)
        .Select(tile => new
        {
            kind = tile.Name.LocalName,
            name = (string?)tile.Attribute("name"),
            parent = (string?)tile.Parent?.Attribute("name"),
            traits = tile.Elements().Where(value => !value.HasAttributes).Select(value => value.Name.LocalName).ToArray()
        }), json));
    return 0;
}
if (args[0] == "dialogue")
{
    var quest = FalloutDialogueTopic.Find(records, "QUST", args[2]);
    var infos = records.EffectiveRecords("INFO").Where(info => FalloutDialogueTopic.RequiredForm(info, "QSTI") == quest.FormKey);
    Console.WriteLine(JsonSerializer.Serialize(infos.Select(info => new
    {
        info = info.FormKey.ToString(),
        topic = info.Groups.Where(group => group.Type == 7).Select(group => records.GetEffective(info.Plugin.AdjustFormId(group.LabelAsUInt32)))
            .Select(topic => new { form = topic.FormKey.ToString(), id = FalloutDialogueTopic.Text(topic.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span) }).Single(),
        conditions = FalloutCondition.Read(info).Select(condition => new { condition.Function, condition.RunOn, condition.Argument1, condition.Argument2, condition.Comparison, condition.Flags }),
        fields = info.ReadSubrecords().Where(field => field.Signature is "DATA" or "TCLT" or "TCLF" or "NAME" or "SCTX" or "RNAM")
            .Select(field => new { field.Signature, text = field.Signature == "SCTX" ? FalloutDialogueTopic.ScriptText(field.Data.Span) : field.Signature == "RNAM" ? FalloutDialogueTopic.Text(field.Data.Span) : Convert.ToHexString(field.Data.Span) }),
    }), json));
    return 0;
}
if (args[0] == "settings")
{
    var defaults = FalloutExecutableStringTable.ReadFloatDefaults(Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe"));
    var names = defaults.Keys.Concat(records.EffectiveRecords("GMST").SelectMany(record => record.ReadSubrecords()
        .Where(field => field.Signature == "EDID").Select(field => FalloutDialogueTopic.Text(field.Data.Span))))
        .Where(name => name.StartsWith('f') && args[2..].Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase)))
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
    foreach (var name in names) Console.WriteLine(JsonSerializer.Serialize(new { name, value = FalloutGameSettingFloats.Read(records, name) }, json));
    return 0;
}
if (args[0] == "record")
{
    if (args.Length < 4) throw new ArgumentException("record requires a signature and an identity.");
    var selectedRecords = args[3] == "--contains" && args.Length == 5
        ? records.EffectiveRecords(args[2]).Where(record => record.ReadSubrecords().Any(field => field.Signature == "EDID" &&
            FalloutDialogueTopic.Text(field.Data.Span).Contains(args[4], StringComparison.OrdinalIgnoreCase)))
        : args[3..].Select(selector => uint.TryParse(selector, System.Globalization.NumberStyles.HexNumber, null, out var id)
            ? records.GetEffective(records.RuntimeFormKey(id)) : FalloutDialogueTopic.Find(records, args[2], selector));
    foreach (var record in selectedRecords)
    {
        if (record.Signature != args[2]) throw new InvalidDataException("Selected record has a different signature.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            record.FormKey,
            record.Signature,
            fields = record.ReadSubrecords().Select(field => new
            {
                field.Signature,
                length = field.Data.Length,
                text = field.Signature is "EDID" or "FULL" or "MODL" or "SCVR" ||
                    record.Signature == "INFO" && field.Signature is "NAM1" or "RNAM"
                    ? FalloutDialogueTopic.Text(field.Data.Span) : field.Signature == "SCTX"
                        ? FalloutDialogueTopic.ScriptText(field.Data.Span) : null,
                hex = field.Signature is "SCDA" or "SCTX" ? null : Convert.ToHexString(field.Data.Span),
            }),
        }, json));
    }
    return 0;
}
if (args[0] == "actors") return ActorInventory.Run(records, args[2..]);
if (args[0] == "route") return WorldRoute.Run(records, args[2]);
if (args[0] == "corpus") return CorpusInventory.Run(records, content, args[2]);
if (args[0] == "replay") return ReferenceReplay.Run(records, args[2]);
if (args[0] == "lifecycle") return ReferenceReplay.Lifecycle(records, args[2..]);
if (args[0] == "script")
{
    if (args[2] == "--contains" && args.Length == 4)
    {
        foreach (var record in records.EffectiveRecords("SCPT"))
            foreach (var field in record.ReadSubrecords().Where(field => field.Signature == "SCTX"))
            {
                var source = FalloutDialogueTopic.ScriptText(field.Data.Span);
                if (source.Contains(args[3], StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine(JsonSerializer.Serialize(new { record.FormKey, source }, json));
            }
        return 0;
    }
    var script = uint.TryParse(args[2], System.Globalization.NumberStyles.HexNumber, null, out var scriptId) ?
        records.GetEffective(records.RuntimeFormKey(scriptId)) : FalloutDialogueTopic.Find(records, "SCPT", args[2]);
    foreach (var field in script.ReadSubrecords().Where(field => field.Signature is "SLSD" or "SCVR"))
        Console.Error.WriteLine(field.Signature == "SCVR" ? "name=" + FalloutDialogueTopic.Text(field.Data.Span) :
            "slot=" + System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
    Console.WriteLine(FalloutOpeningPlayerControlResolver.ReadSource(script,
        script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span));
    return 0;
}

if (args[0] == "exterior")
{
    var center = FalloutCellSceneReader.Read(records, records.RuntimeFormKey(Convert.ToUInt32(args[2], 16))).Cell;
    if (center.Worldspace is not { } world || center.Coordinates is not { } coordinates)
        throw new InvalidDataException("Exterior inventory requires a worldspace grid cell.");
    var grid = new FalloutExteriorGrid(records);
    var selected = grid.Resolve(world, grid.PersistentCell(world), (coordinates.X + 0.5f) * 4096,
        (coordinates.Y + 0.5f) * 4096, args.Length == 4 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 5);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schema = "opennv-development-exterior-inventory/v1",
        cells = selected.Cells.Select(cell => new
        {
            key = cell.FormKey.ToString(),
            cell.EditorId,
            x = cell.Coordinates?.X,
            y = cell.Coordinates?.Y
        }),
        scene = DescribeScene(selected.Scene),
        landscapes = selected.Cells.Select(cell =>
        {
            var land = FalloutLandscapeTransportResolver.ResolveCell(records, cell, selected.PersistentCell);
            return new
            {
                cell = cell.FormKey.ToString(),
                land = land.Landscape.ToString(),
                textures = land.Textures.Select(pair => new { key = pair.Key.ToString(), pair.Value.DiffusePath, pair.Value.NormalPath }),
                bases = land.BaseLayers,
                alpha = land.AlphaLayers
            };
        }),
        limitation = "Source inventory only; runtime presence and visual parity are unverified."
    }, json));
    return 0;
}

object DescribeScene(FalloutCellScene scene) => new
{
    scene.Cell,
    references = scene.References.Select(reference =>
    {
        var baseObject = scene.BaseObjects[reference.Base];
        var baseRecord = records.TryGetEffective(reference.Base, out var found) ? found : null;
        var script = baseRecord?.ReadSubrecords().Any(field => field.Signature == "SCRI") == true ?
            FalloutDialogueTopic.RequiredForm(baseRecord, "SCRI") : (FalloutFormKey?)null;
        return new
        {
            reference.FormKey,
            reference.EditorId,
            reference.Base,
            baseObject.Signature,
            baseEditorId = baseObject.EditorId,
            model = baseObject.ModelPath,
            reference.Position,
            reference.RotationRadians,
            reference.Scale,
            reference.EnableParent,
            reference.Flags,
            destination = reference.Teleport?.Door,
            script,
            scriptEditorId = script is { } key ?
                FalloutDialogueTopic.Text(records.GetEffective(key).ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span) : null,
        };
    }).ToArray(),
};

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
        cells.Add(DescribeScene(scene));
    }
    catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
    {
        failures.Add(new { record.FormKey, name, error = error.Message });
    }
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    schema = "opennv-development-cell-inventory/v1",
    cells,
    failures,
    limitation = "Source inventory only; runtime and ordinary gameplay acceptance are unverified."
}, json));
return failures.Count == 0 && cells.Count != 0 ? 0 : 1;
