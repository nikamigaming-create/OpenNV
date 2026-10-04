using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class PlacedLightEnableContracts
{
    private const string Plugin = "LightParents.esm";
    private static FalloutFormKey Key(uint id) => new(Plugin, id);

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-light-parents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, Plugin), Fixture());
            using var records = FalloutPluginStack.Load(directory, [Plugin]);
            var scene = FalloutCellSceneReader.Read(records, Key(0x800));
            var other = FalloutCellSceneReader.Read(records, Key(0x801));
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(scene); world.LoadCell(other);
            var lights = scene.References.Where(reference => reference.Base == Key(0x100)).ToArray();
            Require(lights.Length == 9 && lights.Count(reference => reference.EnableParent is not null) == 8,
                "The full reader lost source light references or XESP declarations.");
            Require(lights.Single(reference => reference.FormKey == Key(0x821)).EnableParentOpposite,
                "The light's opposite parent declaration was lost.");

            void Check(FalloutReferenceWorld subject, bool rootEnabled)
            {
                var expected = new[] { rootEnabled, !rootEnabled, rootEnabled, !rootEnabled, !rootEnabled, true, true, false, true };
                foreach (var (reference, enabled) in lights.Zip(expected))
                {
                    Require(subject.IsEnabled(reference.FormKey) == enabled,
                        $"Light {reference.FormKey} did not derive enable state from the actual parent graph.");
                    var light = FalloutPlacedLightResolver.Resolve(reference, scene.BaseObjects[reference.Base], records);
                    Require(light.Reference == reference.FormKey && light.Base == reference.Base &&
                        light.RadiusGameUnits == 160 && light.Intensity == 1.5f && light.ColorRgb.SequenceEqual(new byte[] { 80, 120, 160 }),
                        "Parent state changed a light's immutable source identity, signed radius, color or intensity.");
                }
            }
            Check(world, false);
            Require(!world.SetEnabled(Key(0x820), true), "A child light acquired independent enable authority.");
            foreach (var enabled in new[] { true, false, true, false })
            {
                var before = world.IsEnabled(Key(0x820));
                Require(world.SetEnabled(Key(0x810), enabled), "The independent source parent did not enqueue an enable change.");
                Require(world.IsEnabled(Key(0x820)) == before, "A queued parent command bypassed the shared update.");
                world.AdvanceEnableChanges(0, new(1, 1), _ => false);
                Check(world, enabled);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(world.Capture());
                var snapshot = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(bytes)!;
                using var cold = new FalloutReferenceWorld(records);
                cold.Restore(snapshot); cold.LoadCell(scene); cold.LoadCell(other);
                Check(cold, enabled);
                var corrupt = snapshot.Select(value => value.Reference == Key(0x820)
                    ? value with { EnableRequest = new(true, false) } : value).ToArray();
                using var invalidCold = new FalloutReferenceWorld(records);
                Reject(() => invalidCold.Restore(corrupt), "independent enable request");
            }

            var source = scene.BaseObjects[Key(0x100)];
            var first = lights[0];
            Reject(() => FalloutPlacedLightResolver.Resolve(first, source with { ModelPath = "meshes/source-light.nif" }), "light/model/controller owner");
            Reject(() => FalloutPlacedLightResolver.Resolve(first with { Scale = 1.39f }, source), "unsupported XSCL");
            foreach (var flags in new uint[] { 1, 8, 0x600, 0x80000000 })
                Reject(() => FalloutPlacedLightResolver.Resolve(first, source with { Light = source.Light! with { Flags = flags } }), "static point-light contract");
            Reject(() => FalloutPlacedLightResolver.Resolve(first with { RadiusAdjustmentGameUnits = -200 }, source), "invalid effective radius");
            Reject(() => FalloutPlacedLightResolver.Resolve(first with { RadiusAdjustmentGameUnits = float.NaN }, source), "invalid effective radius");

            using var invalidWorld = new FalloutReferenceWorld(records);
            Reject(() => invalidWorld.IsEnabled(Key(0x840)), "cycle");
            Reject(() => invalidWorld.IsEnabled(Key(0x842)), "No record exists");
            Reject(() => invalidWorld.IsEnabled(Key(0x843)), "NAME");
            Reject(() => invalidWorld.IsEnabled(Key(0x844)), "XESP flags");
            Console.WriteLine("OPENNV_PLACED_LIGHT_ENABLE_CONTRACTS_PASS fullReader=true parent=true opposite=true chained=true crossCell=true enginePlayer=true queued=true repeated=true cold=true failClosed=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Fixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        return Join(Record("TES4", 0, 0, Field("HEDR", header)),
            Record("STAT", 0x101, 0, Field("EDID", Text("ParentBase"))),
            Record("LIGH", 0x100, 0, Field("EDID", Text("StaticPoint")), Field("DATA", Light()), Field("FNAM", BitConverter.GetBytes(1.5f))),
            Cell(0x800), Group(0x800,
                Reference(0x810, 0x101, 0x800),
                Reference(0x820, 0x100, 0, 0x810), Reference(0x821, 0x100, 0x800, 0x810, 1),
                Reference(0x822, 0x100, 0, 0x820), Reference(0x823, 0x100, 0x800, 0x821),
                Reference(0x824, 0x100, 0, 0x820, 1), Reference(0x825, 0x100, 0x800, 0x811),
                Reference(0x826, 0x100, 0x800, 0x14), Reference(0x827, 0x100, 0, 0x14, 1), Reference(0x828, 0x100, 0)),
            Cell(0x801), Group(0x801, Reference(0x811, 0x101, 0)),
            Cell(0x802), Group(0x802,
                Reference(0x840, 0x100, 0, 0x841), Reference(0x841, 0x100, 0, 0x840),
                Reference(0x842, 0x100, 0, 0xdead), Reference(0x843, 0x100, 0, 0x100), Reference(0x844, 0x100, 0, 0x810, 4)));
    }

    private static byte[] Light()
    {
        var bytes = new byte[32]; BinaryPrimitives.WriteInt32LittleEndian(bytes, -1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 200);
        bytes[8] = 80; bytes[9] = 120; bytes[10] = 160;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(20), 90);
        return bytes;
    }

    private static byte[] Reference(uint id, uint basis, uint flags, uint? parent = null, byte parentFlags = 0)
    {
        var fields = new List<byte[]> { Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]) };
        if (basis == 0x100) fields.Add(Field("XRDS", BitConverter.GetBytes(-40f)));
        if (parent is { } key)
        {
            var bytes = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, key); bytes[4] = parentFlags;
            fields.Add(Field("XESP", bytes));
        }
        return Record("REFR", id, flags, fields.ToArray());
    }

    private static byte[] Cell(uint id) => Record("CELL", id, 0, Field("DATA", [1]));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string signature, byte[] bytes)
    {
        var result = new byte[6 + bytes.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)bytes.Length)); bytes.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string signature, uint id, uint flags, params byte[][] fields)
    {
        var bytes = Join(fields); var result = new byte[24 + bytes.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)bytes.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20), 15);
        bytes.CopyTo(result, 24); return result;
    }
    private static byte[] Group(uint id, params byte[][] records)
    {
        var bytes = Join(records); var result = new byte[24 + bytes.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), id);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), 6); bytes.CopyTo(result, 24); return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException &&
            error.Message.Contains(message, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidDataException($"Expected failure '{message}' was not reported.");
    }
}
