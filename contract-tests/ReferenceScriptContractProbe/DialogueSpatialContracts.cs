using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenNV.Runtime.Content;

internal static class DialogueSpatialContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-dialogue-spatial-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Places.esm"), Join(Header(),
                Record("NPC_", 0x100), Record("CREA", 0x101), Record("STAT", 0x110),
                Record("ACHR", 0x900, Field("NAME", U32(0x100))),
                Record("REFR", 0x901, Field("NAME", U32(0x110))),
                Record("ACRE", 0x902, Field("NAME", U32(0x101)))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Other.esm"), Header());
            var bytes = new byte[28]; BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0x01000901);
            var target = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(4), 0x01000902);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Queries.esp"), Join(Header("Other.esm", "Places.esm"),
                Record("INFO", 0x02000810, Field("CTDA", bytes)), Record("PACK", 0x02000811, Field("PTDT", target))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Places.esm", "Other.esm", "Queries.esp"]);
            FalloutFormKey Key(uint id) => new("Places.esm", id);
            var condition = FalloutCondition.Read(records.GetEffective(new("Queries.esp", 0x810))).Single();
            var identity = new FalloutDialogueSpeaker(Key(0x100), Key(0x100), Key(0x850), "Synthetic", null, false);
            var positions = new Dictionary<FalloutFormKey, Vector3>
            {
                [Key(0x900)] = Vector3.Zero, [Key(0x901)] = new(3, 4, 12),
                [Key(0x902)] = new(3, 4, 32), [records.RuntimeFormKey(0x14)] = new(3, 4, 2),
            };
            (FalloutFormKey Subject, FalloutFormKey Target)? queried = null;
            float Query(FalloutFormKey subject, FalloutFormKey goal)
            {
                queried = (subject, goal); return Vector3.Distance(positions[subject], positions[goal]);
            }
            FalloutDialogueConditions Context(FalloutFormKey? listener = null,
                Func<FalloutFormKey, FalloutFormKey, float>? query = null) =>
                new(records, new(records), Key(0x900), identity, listener: listener, referenceDistance: query ?? Query);
            var own = Context();
            Check(condition.FormArgument1 == Key(0x901) && own.Evaluate(condition) == 13 && queried == (Key(0x900), Key(0x901)),
                "Distance lost its speaker, source units or adjusted target.");
            Check(own.Evaluate(condition with { RunOn = 1 }) == 10 && queried == (records.RuntimeFormKey(0x14), Key(0x901)),
                "Implicit listener distance did not query the actual player.");
            Check(Context(Key(0x902)).Evaluate(condition with { RunOn = 1 }) == 20 && queried == (Key(0x902), Key(0x901)),
                "Explicit listener distance queried the player instead.");
            Check(own.Evaluate(condition with { RunOn = 2, Reference = 0x01000902 }) == 20 && queried == (Key(0x902), Key(0x901)),
                "Explicit subject lost its declaring master's reference.");
            positions[Key(0x900)] = new(3, 4, 11);
            Check(own.Evaluate(condition) == 1, "Spatial query retained a stale live placement.");
            Reject(() => own.Evaluate(condition with { RunOn = 2 }));
            Reject(() => own.Evaluate(condition with { RunOn = 3 }));
            Reject(() => own.Evaluate(condition with { Argument1 = 0x01000110 }));
            Reject(() => own.Evaluate(condition with { Reference = 0x01000999, RunOn = 2 }));
            Reject(() => new FalloutDialogueConditions(records, new(records), Key(0x900), identity).Evaluate(condition));
            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
                Reject(() => Context(query: (_, _) => invalid).Evaluate(condition));

            var talked = new Dictionary<FalloutFormKey, bool> { [Key(0x900)] = false, [Key(0x902)] = true };
            var package = records.GetEffective(new("Queries.esp", 0x811));
            var spoken = condition with { Owner = package, Function = 50, Argument1 = 0 };
            bool Talked(FalloutFormKey subject) => talked[subject];
            Check(!FalloutAiPackages.HasTalkedToPlayer(spoken, Key(0x900), Talked) &&
                FalloutAiPackages.HasTalkedToPlayer(spoken with { RunOn = 1 }, Key(0x900), Talked) &&
                FalloutAiPackages.HasTalkedToPlayer(spoken with { RunOn = 2, Reference = 0x01000902 }, Key(0x900), Talked),
                "Talked-to-player query lost self, source target or adjusted explicit subject.");
            talked[Key(0x902)] = false;
            Check(!FalloutAiPackages.HasTalkedToPlayer(spoken with { RunOn = 1 }, Key(0x900), Talked),
                "Talked-to-player query retained stale reference state.");
            Reject(() => FalloutAiPackages.HasTalkedToPlayer(spoken with { RunOn = 2 }, Key(0x900), Talked));
            Reject(() => FalloutAiPackages.HasTalkedToPlayer(spoken with { RunOn = 3 }, Key(0x900), Talked));
            Reject(() => FalloutAiPackages.HasTalkedToPlayer(condition, Key(0x900), Talked));
            Console.WriteLine("OPENNV_DIALOGUE_SPATIAL_CONTRACT_PASS self=true player=true listener=true explicit=true sourceMasters=true " +
                "liveChanges=true invalidDistanceRefused=true actualReferences=true talkedToPlayerScope=true missingOwnerRefused=true");
        }
        finally { directory.Delete(true); }
    }

    private static void Check(bool valid, string error) { if (!valid) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Unbound spatial/reference query was admitted.");
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Header(params string[] masters) => Record("TES4", 0,
        Join(masters.Select(master => Join(Field("MAST", Encoding.ASCII.GetBytes(master + '\0')), Field("DATA", new byte[8]))).ToArray()));
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)data.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
}
