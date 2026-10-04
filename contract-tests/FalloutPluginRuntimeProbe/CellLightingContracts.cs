using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class CellLightingContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-cell-lighting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FalloutCellLighting Read(byte[] lighting, bool inherit = false, byte[]? template = null, ushort version = 5)
            {
                var path = Path.Combine(directory, "Lighting.esm");
                var fields = new List<byte[]> { Field("EDID", Text("SourceLighting")), Field("DATA", [1]), Field("XCLL", lighting) };
                if (inherit) fields.AddRange([Field("LTMP", BitConverter.GetBytes(0x200u)), Field("LNAM", BitConverter.GetBytes(0x100u))]);
                var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
                var bytes = Record("TES4", 0, 15, Field("HEDR", header)).Concat(Record("CELL", 0x100, version, fields.ToArray()));
                if (inherit) bytes = bytes.Concat(Record("LGTM", 0x200, 15, Field("DATA", template ?? Lighting(40))));
                File.WriteAllBytes(path, bytes.ToArray());
                using var records = FalloutPluginStack.Load(directory, ["Lighting.esm"]);
                var cell = FalloutCellSceneReader.Read(records, new("Lighting.esm", 0x100));
                Require(cell.References.Count == 0, "An empty authored cell acquired synthetic references.");
                return cell.Cell.Lighting ?? throw new InvalidDataException("Source XCLL was omitted.");
            }
            var legacy = Read(Lighting(36));
            Require(legacy.FogPowerSource is null && legacy.FogNear == 40 && legacy.FogFar == 400 &&
                legacy.DirectionalFade == 0.75f && legacy.FogClipDistance == 800 && legacy.DirectionalXDegrees == -12 &&
                legacy.DirectionalZDegrees == 270 && legacy.AmbientRgb.SequenceEqual(new byte[] { 3, 7, 11 }), "The legacy XCLL prefix was changed.");
            Reject(() => _ = legacy.FogPower);
            var complete = Read(Lighting(40), version: 15);
            Require(complete.FogPowerSource == 2.25f && complete.FogPower == 2.25f, "Encoded fog power was lost.");
            var inherited = Read(Lighting(36), inherit: true);
            Require(inherited.FogPower == 2.25f && inherited.FogNear == legacy.FogNear && inherited.AmbientRgb.SequenceEqual(legacy.AmbientRgb),
                "Template inheritance did not supply only the selected missing field.");
            Require(Read(Lighting(36), version: 15).FogPowerSource is null, "A modern plugin cannot retain a valid optional prefix.");
            foreach (var length in new[] { 35, 37, 39, 41 }) Reject(() => Read(new byte[length]));
            var malformed = Lighting(40); BinaryPrimitives.WriteSingleLittleEndian(malformed.AsSpan(36), float.NaN); Reject(() => Read(malformed));
            Reject(() => Read(Lighting(36), inherit: true, template: Lighting(36)));
            Console.WriteLine("OPENNV_CELL_LIGHTING_CONTRACTS_PASS scope=legacy-prefix-presence-inheritance-and-fail-closed-use");
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static void Owned(string installation, string output, string[] options)
    {
        var mods = options.Length == 0 ? Array.Empty<FalloutModSelection>() :
            options.Length >= 3 && options[0] == "--mod" ? [new FalloutModSelection(options[1], options[2], options[3..])] :
            throw new ArgumentException("--audit-cell-lighting <installation> <private-output> [--mod <id> <root> <dependency-root> ...]");
        using var content = new FalloutModStackSelection(mods).Resolve(installation).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var layouts = records.EffectiveRecords("CELL").SelectMany(record => record.ReadSubrecords().Where(field => field.Signature == "XCLL")
            .Select(field => new { Record = record, Bytes = field.Data.Length })).ToArray();
        var legacy = new List<object>();
        foreach (var layout in layouts.Where(layout => layout.Bytes == 36))
        {
            var scene = FalloutCellSceneReader.Read(records, layout.Record.FormKey);
            var lighting = scene.Cell.Lighting ?? throw new InvalidDataException("Owned legacy XCLL was omitted.");
            var source = layout.Record.ReadSubrecords().Single(field => field.Signature == "XCLL").Data;
            Require(lighting.DirectionalFade == BinaryPrimitives.ReadSingleLittleEndian(source.Span[28..]) &&
                lighting.FogClipDistance == BinaryPrimitives.ReadSingleLittleEndian(source.Span[32..]), "Owned optional prefix values changed.");
            Require(lighting.FogPowerSource is null, "Absent owned fog power was fabricated.");
            Reject(() => _ = lighting.FogPower);
            legacy.Add(new { reference = scene.Cell.FormKey.ToString(), scene.Cell.EditorId, layout.Record.FormVersion,
                winner = layout.Record.Plugin.Name, sha256 = Convert.ToHexString(SHA256.HashData(layout.Record.ReadData())),
                sourceExtent = source.Length, references = scene.References.Count, lighting, presentationDefaultOwned = false });
        }
        Require(legacy.Count != 0, "Owned audit did not reach a legacy XCLL.");
        File.WriteAllText(output, JsonSerializer.Serialize(new { content.SaveCompatibilityId, runtimeBuild = typeof(FalloutCellLighting).Module.ModuleVersionId,
            layouts = layouts.GroupBy(layout => new { layout.Bytes, layout.Record.FormVersion }).Select(group => new { group.Key, count = group.Count() }),
            legacy, sourceReadOnly = true, nativePresentationVerified = false }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        Console.WriteLine("OPENNV_OWNED_CELL_LIGHTING_PASS scope=all-winning-legacy-XCLL count=" + legacy.Count);
    }

    private static byte[] Lighting(int length)
    {
        var bytes = new byte[length]; bytes[0] = 3; bytes[1] = 7; bytes[2] = 11;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(12), 40); BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(16), 400);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), -12); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 270);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(28), 0.75f); BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(32), 800);
        if (length == 40) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(36), 2.25f);
        return bytes;
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Field(string signature, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string signature, uint form, ushort version, params byte[][] fields)
    {
        var data = fields.SelectMany(field => field).ToArray(); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), form);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20), version); data.CopyTo(result, 24); return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unbound or malformed lighting was admitted.");
    }
}
