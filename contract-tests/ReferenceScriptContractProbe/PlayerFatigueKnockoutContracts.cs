using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

internal static class PlayerFatigueKnockoutContracts
{
    internal static void Run()
    {
        RecoveryRoleSelection();
        var ragdoll = new FalloutActorRagdollState(new string('a', 64),
            [new(1, [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0], [0, 0, 0], [0, 0, 0], true)]);
        var recovery = new FalloutActorHitReaction(new("Synthetic.esm", 0x800), new string('b', 64), 0,
            new("meshes/synthetic/getup.kf", new string('c', 64), .25, false), [0, 0, 0], [0, 0, 0, 1]);
        foreach (var phase in new[] { FalloutFatigueKnockoutPhase.Falling, FalloutFatigueKnockoutPhase.Unconscious })
        {
            var source = new FalloutPlayerFatigueKnockout(new(phase), ragdoll);
            source.Validate();
            var restored = JsonSerializer.Deserialize<FalloutPlayerFatigueKnockout>(JsonSerializer.Serialize(source))!;
            restored.Validate();
            if (restored.State.Phase != phase || restored.Ragdoll!.Bodies.Single().Sleeping != true)
                throw new InvalidDataException("Player fatigue capture lost its actual physical phase or settlement state.");
            Reject(source with { Ragdoll = null });
            Reject(source with { Recovery = recovery });
        }
        var gettingUp = new FalloutPlayerFatigueKnockout(new(FalloutFatigueKnockoutPhase.GettingUp), Recovery: recovery);
        gettingUp.Validate();
        var resumed = JsonSerializer.Deserialize<FalloutPlayerFatigueKnockout>(JsonSerializer.Serialize(gettingUp))!;
        resumed.Validate();
        if (resumed.Recovery!.Animation.ElapsedSeconds != .25 || resumed.Recovery.Animation.StartPending)
            throw new InvalidDataException("Player get-up restore rewound its source clock/events.");
        Reject(gettingUp with { Recovery = null });
        Reject(gettingUp with { Ragdoll = ragdoll });
        Reject(gettingUp with { State = new((FalloutFatigueKnockoutPhase)99) });
        Reject(new(new(FalloutFatigueKnockoutPhase.Unconscious), ragdoll with
        { Cuts = [new(1, [[1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0]])] }));
    }

    private static void RecoveryRoleSelection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-recovery-role-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Synthetic.esm");
            File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Idle(0x800, "Creatures\\Synthetic\\IdleAnims", 0, 0, Condition(107, 0, 0x20)),
                Idle(0x801, "Creatures\\Synthetic\\IdleAnims\\left.kf", 0x800, 0, Condition(285, 1)),
                Idle(0x802, "Creatures\\Synthetic\\IdleAnims\\right.kf", 0x800, 0x801, Condition(285, 0)),
                Idle(0x810, "Creatures\\Neutral\\IdleAnims", 0, 0, Condition(107, 0, 0x20)),
                Idle(0x811, "Creatures\\Neutral\\IdleAnims\\recover.kf", 0x810, 0)));
            using var records = FalloutPluginStack.Load([new FalloutPluginSource("Synthetic.esm", path)]);
            var sideways = new FalloutHitReactionTree(records, "meshes/creatures/synthetic/skeleton.nif", 107);
            foreach (var left in new[] { false, true })
            {
                var selected = sideways.Select(condition => condition.Function switch
                {
                    107 => 1,
                    285 => left ? 1 : 0,
                    _ => throw new InvalidDataException("Recovery invented an unrequested orientation predicate.")
                });
                if (selected?.FormKey.ObjectId != (left ? 0x801u : 0x802u))
                    throw new InvalidDataException("Recovery role index lost the source IsLeftUp branch.");
            }
            var neutral = new FalloutHitReactionTree(records, "meshes/creatures/neutral/skeleton.nif", 107);
            if (neutral.Select(condition => condition.Function == 107 ? 1 :
                    throw new InvalidDataException("Unconditional source recovery requested an orientation."))?.FormKey.ObjectId != 0x811 ||
                neutral.AnimationPaths().Single() != "meshes/Creatures/Neutral/IdleAnims/recover.kf" ||
                sideways.AnimationPaths().Count() != 2)
                throw new InvalidDataException("Recovery preflight lost an actual source leaf or required an undeclared orientation.");
            var withoutIdle = new FalloutHitReactionTree(records, "meshes/creatures/without-idle/skeleton.nif", 107);
            if (withoutIdle.AnimationPaths().Any() || withoutIdle.Select(_ =>
                    throw new InvalidDataException("An absent recovery tree invented a predicate.")) is not null)
                throw new InvalidDataException("No-IDLE source recovery must retain a null selection for the explicit living reset path.");
        }
        finally { Directory.Delete(directory, true); }

        static byte[] Condition(ushort function, float comparison, byte flags = 0)
        {
            var bytes = new byte[28]; bytes[0] = flags;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), comparison);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), function);
            return Field("CTDA", bytes);
        }
        static byte[] Idle(uint id, string model, uint parent, uint previous, params byte[][] conditions)
        {
            var links = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(links, parent);
            BinaryPrimitives.WriteUInt32LittleEndian(links.AsSpan(4), previous);
            return Record("IDLE", id, Join(Field("EDID", Encoding.ASCII.GetBytes("Synthetic" + id + "\0")),
                Field("MODL", Encoding.ASCII.GetBytes(model + "\0")), Join(conditions), Field("ANAM", links), Field("DATA", new byte[6])));
        }
        static byte[] Field(string tag, byte[] bytes)
        {
            var field = new byte[6 + bytes.Length]; Encoding.ASCII.GetBytes(tag).CopyTo(field, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(field.AsSpan(4), checked((ushort)bytes.Length)); bytes.CopyTo(field, 6);
            return field;
        }
        static byte[] Record(string tag, uint id, params byte[][] fields)
        {
            var bytes = Join(fields); var record = new byte[24 + bytes.Length]; Encoding.ASCII.GetBytes(tag).CopyTo(record, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)bytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), id); bytes.CopyTo(record, 24);
            return record;
        }
        static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    }

    private static void Reject(FalloutPlayerFatigueKnockout state)
    {
        try { state.Validate(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid player fatigue physical/animation ownership was accepted.");
    }
}
