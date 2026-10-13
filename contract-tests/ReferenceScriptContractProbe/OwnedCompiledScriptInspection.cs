using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class OwnedCompiledScriptInspection
{
    internal static void Run(string dataRoot, string game, string plugin, uint id)
    {
        RuntimeLiveContentSource.Configure(dataRoot, game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var record = records.GetEffective(new(plugin, id));
        var fields = record.ReadSubrecords().ToArray();
        var rows = new List<object>();
        foreach (var field in fields.Where(field => field.Signature == "SCDA"))
        {
            var code = field.Data.Span;
            var offset = 0;
            while (offset <= code.Length - 4)
            {
                var begin = offset;
                var opcode = BinaryPrimitives.ReadUInt16LittleEndian(code[offset..]);
                var extent = BinaryPrimitives.ReadUInt16LittleEndian(code[(offset + 2)..]);
                offset += 4;
                if (opcode == 0x1c && offset <= code.Length - 4)
                {
                    var receiver = extent;
                    opcode = BinaryPrimitives.ReadUInt16LittleEndian(code[offset..]);
                    extent = BinaryPrimitives.ReadUInt16LittleEndian(code[(offset + 2)..]);
                    offset += 4;
                    if (opcode < 0x1000)
                        rows.Add(new { offset = begin, receiver, nestedOpcode = opcode, extent,
                            sourcePrefix = Convert.ToHexString(code.Slice(begin, Math.Min(40, code.Length - begin))) });
                }
                if (extent > code.Length - offset) { rows.Add(new { malformedAt = begin, opcode, extent }); break; }
                offset += extent;
            }
        }
        Console.WriteLine("OPENNV_OWNED_COMPILED_INSPECTION " + JsonSerializer.Serialize(new
        {
            source = record.FormKey.ToString(), record.Signature,
            nonCommandReferenceHeaders = rows,
            referenceRows = fields.Where(field => field.Signature == "SCRO").Take(2).Select(field =>
                record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)).ToString()).ToArray(),
            diagnosticQualifiedAssignments = fields.Where(field => field.Signature == "SCTX").SelectMany(field =>
                System.Text.Encoding.UTF8.GetString(field.Data.Span).Split('\n').Where(line => line.Contains(".set ", StringComparison.OrdinalIgnoreCase)))
                .Take(2).ToArray(),
            binaryFields = fields.Where(field => field.Signature is "SCHR" or "SCDA").Select(field => new { field.Signature, bytes = field.Data.Length,
                header = field.Signature == "SCHR" ? Convert.ToHexString(field.Data.Span) : null }).ToArray(),
            fieldLayout = fields.Select(field => new { field.Signature, bytes = field.Data.Length }).ToArray(),
            textPresent = fields.Any(field => field.Signature == "SCTX"),
            executed = false, filesChanged = false,
        }));
    }
}
