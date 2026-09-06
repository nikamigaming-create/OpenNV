using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class IdleConditionProbe
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-idle-conditions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string[] names = ["Base.esm", "Child.esp", "Override.esp"];
        try
        {
            File.WriteAllBytes(Path.Combine(directory, names[0]), Header().Concat(
                Idle(0x100, 0, Condition(999, 0, 0)))
                .Concat(Record("FACT", 0x200, []))
                .Concat(Record("FACT", 0x201, []))
                .Concat(Npc(0x300, 0, (0x200, 0)))
                .ToArray());
            File.WriteAllBytes(Path.Combine(directory, names[1]), Header("Base.esm")
                .Concat(Idle(0x01000101, 0x100, Condition(77, 0x80, 30)))
                .Concat(Idle(0x01000102, 0x999, Condition(77, 0x80, 30)))
                .Concat(Idle(0x01000103, 0x01000104))
                .Concat(Idle(0x01000104, 0x01000103))
                .Concat(Record("IDLE", 0x01000105, Field("ANAM", new byte[4])))
                .Concat(Idle(0x01000106, 0x100, Condition(999, 0, 0)))
                .Concat(Npc(0x01000301, 0x300, (0x201, 5)))
                .ToArray());
            File.WriteAllBytes(Path.Combine(directory, names[2]), Header("Base.esm").Concat(
                Idle(0x100, 0, Condition(91, 0, 0)))
                .Concat(Npc(0x300, 0, (0x200, 3), (0x201, -1))).ToArray());
            using var stack = FalloutPluginStack.Load(directory, names);
            var conditions = new FalloutIdleConditions(stack);
            var visits = new List<string>();
            var chance = 0f;
            var activity = new FalloutActorActivityState();
            float Evaluate(FalloutCondition condition)
            {
                visits.Add(condition.Owner.Plugin.Name + "/" + condition.Function);
                return condition.Function switch
                {
                    77 => chance,
                    91 => activity.Alerted ? 1 : 0,
                    _ => throw new NotSupportedException("Synthetic condition owner is absent."),
                };
            }
            var leaf = new FalloutFormKey("Child.esp", 0x101);
            Require(conditions.AllPass(leaf, Evaluate) && visits.SequenceEqual(["Child.esp/77", "Override.esp/91"]),
                "Package admission lost candidate-first order, master adjustment or the winning parent.");
            visits.Clear(); chance = 30;
            Require(!conditions.AllPass(new("Child.esp", 0x102), Evaluate) && visits.SequenceEqual(["Child.esp/77"]),
                "A false candidate evaluated its inaccessible parent or changed a strict comparison.");
            chance = 0; activity.SetAlerted(true);
            var replay = new FalloutIdleReplayState();
            var package = new FalloutScriptPackage(new("Child.esp", 0x200), "Synthetic", 0, 0,
                [leaf], new Dictionary<string, FalloutFormKey?>());
            var collection = new FalloutIdleCollectionPlayback(package, replay, form => conditions.AllPass(form, Evaluate));
            Require(collection.Select() is null && collection.Cursor == 0,
                "A failed parent started a package idle or consumed its position.");
            activity.SetAlerted(false); replay.Started(leaf, 1); visits.Clear();
            Require(collection.Select() is null && visits.Count == 0, "Cooldown rejection consumed condition random state.");
            replay.Advance(1);
            Require(collection.Select() == leaf, "An idle did not resume after its actor and source predicates became eligible.");
            Expect<InvalidDataException>(() => conditions.AllPass(new("Child.esp", 0x103), Evaluate));
            Expect<InvalidDataException>(() => conditions.AllPass(new("Child.esp", 0x105), Evaluate));
            Expect<NotSupportedException>(() => conditions.AllPass(new("Child.esp", 0x106), Evaluate));
            var factions = FalloutAiPackages.ReadFactions(stack, new("Child.esp", 0x301));
            Require(factions.Count == 2 && factions[new("Base.esm", 0x200)] == 3 && factions[new("Base.esm", 0x201)] == -1,
                "Faction predicates lost template ownership, winning ranks or the signed nonmember rank.");
        }
        finally
        {
            foreach (var name in names) File.Delete(Path.Combine(directory, name));
            Directory.Delete(directory);
        }
        Console.WriteLine("OPENNV_IDLE_CONDITIONS_PASS candidateFirst=true winningParents=true cooldownBeforePredicates=true unknownVisible=true");
    }

    private static byte[] Header(string? master = null)
    {
        var header = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var fields = Field("HEDR", header);
        if (master is not null) fields = fields.Concat(Field("MAST", Encoding.ASCII.GetBytes(master + '\0')))
            .Concat(Field("DATA", new byte[8])).ToArray();
        return Record("TES4", 0, fields);
    }

    private static byte[] Idle(uint id, uint parent, params byte[][] conditions)
    {
        var related = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(related, parent);
        return Record("IDLE", id, Field("ANAM", related).Concat(conditions.SelectMany(value => Field("CTDA", value))).ToArray());
    }

    private static byte[] Condition(ushort function, byte flags, float comparison)
    {
        var result = new byte[28]; result[0] = flags;
        BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(4), comparison);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), function);
        return result;
    }

    private static byte[] Npc(uint id, uint template, params (uint Faction, sbyte Rank)[] factions)
    {
        var actor = new byte[24];
        if (template != 0) BinaryPrimitives.WriteUInt16LittleEndian(actor.AsSpan(22), 4);
        var fields = Field("ACBS", actor).Concat(Field("TPLT", BitConverter.GetBytes(template)));
        foreach (var (faction, rank) in factions)
        {
            var data = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(data, faction); data[4] = unchecked((byte)rank);
            fields = fields.Concat(Field("SNAM", data));
        }
        return Record("NPC_", id, fields.ToArray());
    }

    private static byte[] Field(string signature, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length));
        data.CopyTo(result, 6); return result;
    }

    private static byte[] Record(string signature, uint id, byte[] data)
    {
        var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Invalid source eligibility was silently accepted.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
