using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class HitReactionProbe
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-hit-reactions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Header()
                .Concat(Idle(1, 0, 0, "IdleAnims", Condition(391, 0x20, -1)))
                .Concat(Idle(2, 1, 0, "IdleAnims/old.kf", Condition(391, 0, 3), Condition(14, 0xa0, 0)))
                .Concat(Idle(3, 1, 2, "IdleAnims/second.kf", Condition(391, 0, 5), Condition(14, 0xa0, 0)))
                .Concat(Idle(4, 1, 3, "IdleAnims/unsupported.kf", Condition(999, 0, 1)))
                .Concat(Idle(5, 0, 0, "Unrelated.kf", Condition(999, 0, 1))).ToArray());
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Header("Base.esm")
                .Concat(Idle(2, 1, 0, "IdleAnims/winning.kf", Condition(391, 0, 3), Condition(14, 0xa0, 0))).ToArray());
            using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Patch.esp"]);
            var tree = new FalloutHitReactionTree(records, "meshes/TestActor/skeleton.nif");
            float hit = 3, limb = 0;
            float Evaluate(FalloutCondition c) => c.Function switch
            {
                391 => hit, 14 => limb, _ => throw new NotSupportedException("Unknown synthetic predicate.")
            };
            var first = tree.Select(Evaluate)!;
            Require(first.Plugin.Name == "Patch.esp" && first.FormKey == new FalloutFormKey("Base.esm", 2),
                "Winning override changed the source sibling position or was ignored.");
            hit = 5;
            Require(tree.Select(Evaluate)!.FormKey == new FalloutFormKey("Base.esm", 3),
                "Overridden sibling prevented a later body part from being selected.");
            hit = -1;
            Require(tree.Select(Evaluate) is null && tree.LastVisited.Count == 1,
                "Absent hit selected an unrelated general idle or visited its children.");
            hit = 3; limb = 100;
            Reject(() => tree.Select(Evaluate)); // The reached unsupported fallback stays visible.
            var reaction = new FalloutActorHitReaction(new("Base.esm", 2), new string('a', 64), 3,
                new("meshes/TestActor/IdleAnims/winning.kf", new string('b', 64), .25, false), [1, 2, 3], [0, 0, 0, 1]);
            reaction.Validate();
            var cold = JsonSerializer.Deserialize<FalloutActorHitReaction>(JsonSerializer.Serialize(reaction))!;
            Require(JsonSerializer.Serialize(cold) == JsonSerializer.Serialize(reaction), "Hit-reaction snapshot lost its source or phase.");
            Reject(() => (reaction with { Part = 15 }).Validate());
            Reject(() => (reaction with { Rotation = [0, 0, 0, 0] }).Validate());
            Reject(() => (reaction with { Animation = reaction.Animation with { StartPending = true } }).Validate());
            Reject(() => (reaction with { Animation = reaction.Animation with { ElapsedSeconds = double.NaN } }).Validate());
            Reject(() => (reaction with { IdleSha256 = "unknown" }).Validate());
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Base.esm")); File.Delete(Path.Combine(directory, "Patch.esp"));
            Directory.Delete(directory);
        }
        Console.WriteLine("OPENNV_HIT_REACTION_CONTRACT_PASS winningOverrides=true siblingOrder=true hitContext=true unknownVisible=true snapshotValidation=true");
    }

    private static byte[] Header(string? master = null) => Record("TES4", 0,
        Field("HEDR", new byte[12]).Concat(master is null ? [] : Field("MAST", Text(master)).Concat(Field("DATA", new byte[8]))).ToArray());

    private static byte[] Idle(uint id, uint parent, uint previous, string model, params byte[][] conditions)
    {
        var related = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(related, parent);
        BinaryPrimitives.WriteUInt32LittleEndian(related.AsSpan(4), previous);
        return Record("IDLE", id, Field("MODL", Text("TestActor/" + model)).Concat(Field("ANAM", related))
            .Concat(Field("DATA", new byte[] { 7, 1, 1, 0, 0, 0, 0, 0 }))
            .Concat(conditions.SelectMany(c => Field("CTDA", c))).ToArray());
    }

    private static byte[] Condition(ushort function, byte flags, float comparison)
    {
        var result = new byte[28]; result[0] = flags;
        BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(4), comparison);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), function); return result;
    }

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + '\0');
    private static byte[] Field(string signature, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string signature, uint id, byte[] data)
    {
        var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid reaction state or unknown condition was admitted.");
    }
}
