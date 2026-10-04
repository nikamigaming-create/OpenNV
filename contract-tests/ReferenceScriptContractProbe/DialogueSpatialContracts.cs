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
                Record("NPC_", 7), Record("NPC_", 0x100), Record("CREA", 0x101), Record("STAT", 0x110),
                Record("ACHR", 0x900, Field("NAME", U32(0x100))),
                Record("REFR", 0x901, Field("NAME", U32(0x110))),
                Record("ACRE", 0x902, Field("NAME", U32(0x101))),
                Record("FLST", 0x120, Field("LNAM", U32(0x110))),
                Record("FLST", 0x121, Field("LNAM", U32(0x900))),
                Record("FLST", 0x122, Field("LNAM", U32(0x120))),
                Record("FLST", 0x123, Field("LNAM", U32(7))),
                Record("FLST", 0x124, Field("LNAM", [1, 2]))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Other.esm"), Header());
            var bytes = new byte[28]; BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0x01000901);
            var target = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(4), 0x01000902);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Queries.esp"), Join(Header("Other.esm", "Places.esm"),
                Record("INFO", 0x02000810, Field("CTDA", bytes)), Record("PACK", 0x02000811, Field("PTDT", target)),
                Record("FLST", 0x01000120, Field("LNAM", U32(0x01000100)), Field("LNAM", U32(0x01000101)))));
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

            var membership = condition with { Function = 372, Argument1 = 0x01000120 };
            Check(own.Evaluate(membership) == 1 && Context(Key(0x902)).Evaluate(membership with { RunOn = 1 }) == 1 &&
                own.Evaluate(membership with { RunOn = 2, Reference = 0x01000902 }) == 1,
                "IsInList lost the winning list, adjusted source masters or dialogue subject.");
            Check(own.Evaluate(membership with { Argument1 = 0x01000121 }) == 0 &&
                own.Evaluate(membership with { Argument1 = 0x01000122 }) == 0 &&
                own.Evaluate(membership with { Argument1 = 0x01000123, RunOn = 1 }) == 1,
                "IsInList compared a placed reference, expanded a nested list or lost the engine player base.");
            Reject(() => own.Evaluate(membership with { Argument1 = 0x01000124 }));
            Reject(() => own.Evaluate(membership with { Argument1 = 0x01000100 }));
            Reject(() => own.Evaluate(membership with { RunOn = 2 }));
            Reject(() => own.Evaluate(membership with { RunOn = 3 }));
            Reject(() => FalloutReferenceIdentity.IsInList(records, Key(0x100), Key(0x120)));

            var zone = condition with { Function = 446, Argument1 = 0x01000130 };
            bool QueryZone(FalloutFormKey subject, FalloutFormKey target)
            {
                queried = (subject, target); return subject == Key(0x900);
            }
            FalloutDialogueConditions Zone(FalloutFormKey? listener = null) =>
                new(records, new(records), Key(0x900), identity, listener: listener, referenceInZone: QueryZone);
            Check(Zone().Evaluate(zone) == 1 && queried == (Key(0x900), Key(0x130)) &&
                Zone().Evaluate(zone with { RunOn = 1 }) == 0 && queried == (records.RuntimeFormKey(0x14), Key(0x130)) &&
                Zone(Key(0x902)).Evaluate(zone with { RunOn = 1 }) == 0 && queried == (Key(0x902), Key(0x130)) &&
                Zone().Evaluate(zone with { RunOn = 2, Reference = 0x01000900 }) == 1,
                "Dialogue encounter-zone query lost speaker, actual listener, explicit subject or source masters.");
            Reject(() => Zone().Evaluate(zone with { RunOn = 2 }));
            Reject(() => Zone().Evaluate(zone with { RunOn = 3 }));
            Reject(() => own.Evaluate(zone));

            var scriptVariable = condition with { Function = 53, Argument2 = 5 };
            FalloutFormKey? variableOwner = null; uint variableIndex = 0;
            var scriptValue = 3f;
            var variableContext = new FalloutDialogueConditions(records, new(records), Key(0x900), identity,
                runtime: value => { variableOwner = value.FormArgument1; variableIndex = value.Argument2; return scriptValue; },
                listener: Key(0x902));
            foreach (var scope in new uint[] { 0, 1, 2 })
                Check(variableContext.Evaluate(scriptVariable with { RunOn = scope }) == 3 &&
                    variableOwner == Key(0x901) && variableIndex == 5,
                    "Explicit script-variable arguments were replaced by dialogue participants or lost source masters.");
            scriptValue = 4;
            Check(variableContext.Evaluate(scriptVariable) == 4, "Script variable query retained a stale result.");
            Reject(() => variableContext.Evaluate(scriptVariable with { RunOn = 3 }));
            Reject(() => own.Evaluate(scriptVariable));

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
                "liveChanges=true invalidDistanceRefused=true actualReferences=true talkedToPlayerScope=true missingOwnerRefused=true " +
                "formListBaseMembership=true winningListMasters=true referenceAndNestedEntriesDistinct=true");
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
