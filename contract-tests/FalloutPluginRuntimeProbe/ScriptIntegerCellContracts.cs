using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class ScriptIntegerCellContracts
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "opennv-integer-cell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] Field(string name, byte[] payload)
            {
                var bytes = new byte[payload.Length + 6]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)payload.Length);
                payload.CopyTo(bytes, 6); return bytes;
            }
            byte[] Record(string name, uint id, params byte[][] fields)
            {
                var payload = fields.SelectMany(value => value).ToArray();
                var bytes = new byte[payload.Length + 24]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); payload.CopyTo(bytes, 24); return bytes;
            }
            var integer = new byte[24]; integer[0] = 1; integer[16] = 1;
            var floating = new byte[24]; floating[0] = 2;
            var data = Record("TES4", 0, Field("HEDR", new byte[12])).Concat(Record("SCPT", 1,
                Field("SLSD", integer), Field("SCVR", Encoding.ASCII.GetBytes("counter\0")),
                Field("SLSD", floating), Field("SCVR", Encoding.ASCII.GetBytes("fraction\0")),
                Field("SCTX", Encoding.ASCII.GetBytes("short counter\nfloat fraction\0")))).ToArray();
            File.WriteAllBytes(Path.Combine(root, "Fixture.esm"), data);
            using var records = FalloutPluginStack.Load(root, ["Fixture.esm"]);
            var script = records.GetEffective(new("Fixture.esm", 1));
            Check(script, "counter");
            var storage = new FalloutScriptEffectLocalStorage(script);
            storage.Write("fraction", -3.75);
            Require(storage.Read("fraction").Number == -3.75, "Float assignment was truncated.");
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
                         (double)int.MaxValue + 1, (double)int.MinValue - 1 })
            {
                storage.Write("counter", 7);
                try { storage.Write("counter", invalid); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                {
                    Require(storage.Read("counter").Number == 7, "Refused integer assignment changed the cell.");
                    continue;
                }
                throw new InvalidOperationException("Invalid integer assignment was admitted.");
            }
            Require(File.ReadAllBytes(Path.Combine(root, "Fixture.esm")).SequenceEqual(data), "Integer cell tests changed source data.");
            Console.WriteLine("Script integer cell truncation, refusal and cold-state contracts passed.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(FalloutPluginRecord script, string name)
    {
        var storage = new FalloutScriptEffectLocalStorage(script);
        foreach (var value in new[] { 3.75, -3.75, .75, -.75, (double)int.MaxValue + .75, (double)int.MinValue - .75 })
        {
            var before = storage.Capture();
            storage.Write(name, value);
            Require(storage.Read(name).Number == Math.Truncate(value), "Integer cell did not truncate toward zero.");
            var cells = storage.Capture();
            var index = FalloutScriptLocals.ReadDeclarations(script)[name].Index;
            var first = cells.First(cell => cell.Index == index).Ordinal;
            Require(cells[first].Payload == BitConverter.DoubleToUInt64Bits((double)(int)value),
                "Integer assignment retained a fractional or negative-zero payload.");
            Require(cells.Where(cell => cell.Ordinal != first).SequenceEqual(before.Where(cell => cell.Ordinal != first)),
                "Integer assignment mutated another or duplicate local cell.");
            var saved = JsonSerializer.Deserialize<FalloutScriptEffectLocalCell[]>(JsonSerializer.Serialize(cells))!;
            var restored = new FalloutScriptEffectLocalStorage(script, saved);
            Require(restored.Read(name).Number == Math.Truncate(value) && restored.Capture().SequenceEqual(cells),
                "Integer cell lost exact bits across cold restoration.");
        }
    }

    internal static void Owned(string installation)
    {
        using var content = RuntimeLiveContentSource.Open(installation, DevelopmentLabSource.Campaign(installation),
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(installation), includeImplicitPlugins: false);
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var scripts = 0; var locals = 0;
        foreach (var script in records.EffectiveRecords("SCPT"))
        {
            var fields = script.ReadSubrecords().ToArray();
            var headers = fields.Where(field => field.Signature == "SCHR").ToArray();
            if (headers.Length != 1 || FalloutScriptHeader.Read(headers[0].Data.Span).Type != 0x100) continue;
            var declarations = FalloutScriptLocals.ReadDeclarations(script);
            var integerSlots = fields.Where(field => field.Signature == "SLSD" && field.Data.Length == 24 && field.Data.Span[16] == 1)
                .Select(field => BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)).ToHashSet();
            var names = declarations.Where(pair => pair.Value.Kind == FalloutScriptLocalKind.Number && integerSlots.Contains(pair.Value.Index))
                .Select(pair => pair.Key).ToArray();
            if (names.Length == 0) continue;
            var hash = SHA256.HashData(script.ReadData());
            foreach (var name in names) { Check(script, name); ++locals; }
            Require(hash.SequenceEqual(SHA256.HashData(script.ReadData())), "Owned integer audit changed a script.");
            ++scripts;
        }
        Require(locals > 0, "Owned integer audit selected no effect locals.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            content.Game,
            content.SaveCompatibilityId,
            scripts,
            integerLocals = locals,
            casesPerLocal = 6,
            sourceUnchanged = true,
            coldBitsPreserved = true,
            scope = "transient-storage-only-no-event-or-world-execution",
            retailParity = "unverified"
        }));
    }

    private static void Require(bool value, string error)
    {
        if (!value) throw new InvalidOperationException(error);
    }
}
