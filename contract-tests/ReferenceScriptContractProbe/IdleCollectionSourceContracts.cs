using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class IdleCollectionSourceContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-idle-collection-source-").FullName;
        var path = Path.Combine(directory, "Collections.esm");
        try
        {
            var packageData = new byte[12]; packageData[4] = 6;
            var idleData = new byte[8]; BinaryPrimitives.WriteUInt16LittleEndian(idleData.AsSpan(4), 4);
            byte[] Package(uint id, byte[] count) => Record("PACK", id, Field("EDID", Text("Package" + id)),
                Field("PKDT", packageData), Field("IDLF", [1]), Field("IDLC", count),
                Field("IDLT", BitConverter.GetBytes(2f)), Field("IDLA", Join(U32(0x200), U32(0x201))));
            byte[] Marker(uint id, byte[] count) => Record("IDLM", id, Field("IDLF", [1]), Field("IDLC", count),
                Field("IDLT", BitConverter.GetBytes(2f)), Field("IDLA", Join(U32(0x200), U32(0x201))));
            byte[] Idle(uint id, string name) => Record("IDLE", id,
                Field("DATA", idleData), Field("MODL", Text("fixture/" + name + ".kf")));
            File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Package(0x100, U32(2)), Marker(0x101, [2]), Marker(0x102, [2]), Marker(0x103, U32(2)),
                Package(0x104, [2]), Record("PACK", 0x105, Field("EDID", Text("EmptyPackage")), Field("PKDT", packageData)),
                Idle(0x200, "first"), Idle(0x201, "second"), Idle(0x202, "third"),
                Record("ANIO", 0x300, Field("DATA", U32(0x200)), Field("MODL", Text("fixture/prop.nif")))));
            using var records = FalloutPluginStack.Load(directory, ["Collections.esm"]);
            FalloutFormKey Key(uint id) => new("Collections.esm", id);
            string Hash(uint id) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(id)));
            foreach (var id in new uint[] { 0x100, 0x101, 0x104 })
            {
                var source = FalloutIdleCollection.ReadSource(records.GetEffective(Key(id)));
                Require(source.Form == Key(id) && source.RunInSequence && source.IdleTimer == 2 &&
                    source.Idles.SequenceEqual(new[] { Key(0x200), Key(0x201) }),
                    "Collection dispatch changed its source identity, count extent, order or timer.");
            }
            Require(FalloutIdleCollection.ReadSource(records.GetEffective(Key(0x105))).Idles.Count == 0,
                "An empty PACK acquired marker-only field requirements.");
            Reject(() => FalloutIdleCollection.ReadSource(records.GetEffective(Key(0x103))));
            Reject(() => FalloutIdleCollection.ReadSource(records.GetEffective(Key(0x200))));
            var cooldown = new FalloutIdleReplayCooldown(Key(0x200), Hash(0x200), 1);
            var objects = new[] { new FalloutActorIdleObjectBinding(Key(0x300), Hash(0x300),
                "meshes/fixture/prop.nif", new string('e', 64)) };
            var animation = new FalloutActorPackageIdleAnimation(Key(0x200), Hash(0x200),
                "meshes/fixture/first.kf", new string('a', 64), new(.25, 0, 0, 0, false, false), 1,
                new("meshes/fixture/skeleton.nif", new string('d', 64), []), objects);
            foreach (var (id, signature) in new[] { (0x100u, "PACK"), (0x101u, "IDLM") })
            {
                var state = new FalloutActorIdleCollectionState(Key(id), Hash(id), new(1, 2, 0, false),
                    [cooldown], null, animation);
                state.Validate(records, Key(id), signature);
                var cold = JsonSerializer.Deserialize<FalloutActorIdleCollectionState>(JsonSerializer.Serialize(state))!;
                cold.Validate(records, Key(id), signature);
                Require(JsonSerializer.Serialize(cold) == JsonSerializer.Serialize(state),
                    "Collection roundtrip changed source, cursor, cooldown or active animation ownership.");
                Reject(() => state.Validate(records, Key(id), signature == "PACK" ? "IDLM" : "PACK"));
                Reject(() => state.Validate(records, Key(id), "IDLE"));
                Reject(() => (state with { Sha256 = new string('0', 64) }).Validate(records, Key(id), signature));
                Reject(() => (state with { Collection = new(1, 3, 0, false) }).Validate(records, Key(id), signature));
                Reject(() => (state with { Collection = new(1, 2, .5, false) }).Validate(records, Key(id), signature));
                Reject(() => (state with { ActiveAnimation = null, Collection = new(2, 2, 3, false) }).Validate(records, Key(id), signature));
                Reject(() => (state with { Cooldowns = [cooldown, cooldown] }).Validate(records, Key(id), signature));
                Reject(() => (state with { Cooldowns = [cooldown with { Remaining = 5 }] }).Validate(records, Key(id), signature));
                Reject(() => (state with { Cooldowns = [cooldown with { Sha256 = new string('0', 64) }] }).Validate(records, Key(id), signature));
                Reject(() => (state with { ActiveAnimation = animation with { IdleSha256 = new string('0', 64) } })
                    .Validate(records, Key(id), signature));
                Reject(() => (state with { ActiveAnimation = animation with { Objects = [objects[0] with { RecordSha256 = new string('0', 64) }] } })
                    .Validate(records, Key(id), signature));
                Reject(() => (state with { ActiveAnimation = animation with
                { Idle = Key(0x202), IdleSha256 = Hash(0x202), Resource = "meshes/fixture/third.kf", Objects = null } })
                    .Validate(records, Key(id), signature));
                var waiting = state with { ActiveAnimation = null, Collection = new(2, 2, 1.25, false) };
                waiting.Validate(records, Key(id), signature);
                Require(waiting.Copy().Collection.WaitSeconds == 1.25,
                    "Collection validation advanced the source wait instead of retaining it.");
            }
            var marker = new FalloutActorIdleCollectionState(Key(0x101), Hash(0x101), new(0, 0, 0, false), [], null);
            Reject(() => marker.Validate(records, Key(0x102), "IDLM"));
            Reject(() => (marker with { Source = Key(0x102), Sha256 = Hash(0x102) }).Validate(records, Key(0x101), "IDLM"));
            var package = new FalloutActorPackageIdleState(Key(0x100), Hash(0x100), new(1, 2, 0, false), [cooldown], null, animation);
            package.Validate(records, Key(0x100));
            var serialized = JsonSerializer.Serialize(package);
            using var document = JsonDocument.Parse(serialized);
            Require(document.RootElement.TryGetProperty("Package", out _) && !document.RootElement.TryGetProperty("Source", out _),
                "The strict PACK wrapper changed its serialized ownership shape.");
            JsonSerializer.Deserialize<FalloutActorPackageIdleState>(serialized)!.Validate(records, Key(0x100));
            Reject(() => (package with { Package = Key(0x101), Sha256 = Hash(0x101) }).Validate(records, Key(0x101)));
            Reject(() => animation.Validate(records, Key(0x101)));
            Console.WriteLine("OPENNV_IDLE_COLLECTION_SOURCE_CONTRACT_PASS packageAndMarker=true strictPackageWrapper=true " +
                "sourceHashAndIdentity=true activeIdleAndAnio=true retainedCursorAndWait=true restoreDoesNotSelect=true gameplay=unverified");
        }
        finally { File.Delete(path); Directory.Delete(directory, recursive: false); }
    }

    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6);
        return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24);
        return bytes;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    private static void Reject(Action request)
    {
        try { request(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid collection source or continuation was admitted.");
    }
}
