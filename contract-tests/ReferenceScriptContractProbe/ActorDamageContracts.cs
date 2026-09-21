using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

internal static class ActorDamageContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-actor-damage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; Float(header, 0, 1.34f);
            var stats = new byte[17]; BinaryPrimitives.WriteInt16LittleEndian(stats.AsSpan(4), 50);
            var acbs = new byte[24]; acbs[8] = 1;
            var scaledAcbs = new byte[24]; UInt(scaledAcbs, 0, 0x80);
            BinaryPrimitives.WriteUInt16LittleEndian(scaledAcbs.AsSpan(8), 750);
            BinaryPrimitives.WriteUInt16LittleEndian(scaledAcbs.AsSpan(10), 10);
            BinaryPrimitives.WriteUInt16LittleEndian(scaledAcbs.AsSpan(12), 40);
            var entry = new byte[12]; entry[0] = 1; UInt(entry, 4, 3); entry[8] = 2;
            var references = Join(Reference(0x91), Reference(0x92));
            var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x80); UInt(group, 12, 6); references.CopyTo(group, 24);
            var decal = new byte[36];
            Float(decal, 0, 12); Float(decal, 4, 20); Float(decal, 8, 8); Float(decal, 12, 30); Float(decal, 16, 50);
            Float(decal, 20, 100); decal[29] = 2; decal[32] = 170; decal[34] = 4;
            var impactData = new byte[24]; Float(impactData, 0, .08f);
            byte[] Dataset(uint id, int extent) { var data = new byte[extent]; UInt(data, 24, 0x77); return Record("IPDS", id, Field("DATA", data)); }
            File.WriteAllBytes(Path.Combine(directory, "Test.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Record("CREA", 1, Field("EDID", Text("SourceCreature")), Field("ACBS", acbs), Field("DATA", stats),
                    Field("PNAM", BitConverter.GetBytes(2u)), Field("INAM", BitConverter.GetBytes(4u)), Field("NAM4", BitConverter.GetBytes(6u))),
                Record("CREA", 6, Field("ACBS", scaledAcbs), Field("DATA", stats),
                    Field("PNAM", BitConverter.GetBytes(2u)), Field("NAM4", BitConverter.GetBytes(6u))),
                Record("BPTD", 2, Part(0, 1, "Root"), Part(1, 2, "Neck")),
                Record("MISC", 3, Field("EDID", Text("SourceDeathLoot")), Field("DATA", new byte[8])),
                Record("LVLI", 4, Field("LVLD", [0]), Field("LVLF", [0]), Field("LVLO", entry)),
                Record("BPTD", 5, Field("BPNN", Text("Root")), Field("BPND", new byte[83])),
                Npc(0x10, 30, 3, 8, 0), Npc(0x11, 30, 3, 8, 0x10), Npc(0x12, 0, 20, 8, 0),
                Npc(0x13, 30, 3, 8, 0x90), Npc(0x14, 30, 0, 0, 0x10), Npc(0x15, 30, 0, 8, 0),
                Record("BPTD", 0x1d, Part(0, 1, "Root"), Part(1, 2, "Neck")),
                Setting(0x61, "fAVDNPCHealthEnduranceOffset", -1), Setting(0x62, "fAVDNPCHealthEnduranceMult", 5.25f),
                Setting(0x63, "fAVDNPCHealthLevelMult", 5),
                Armor(0x70, 500, 6, true), Armor(0x71, 1250, 0, false), Armor(0x72, 0, float.NaN, true),
                Dataset(0x73, 36), Dataset(0x74, 40), Dataset(0x75, 48), Dataset(0x76, 44),
                Record("IPCT", 0x77, Field("DATA", impactData), Field("DODT", decal), Field("DNAM", BitConverter.GetBytes(0x78u))),
                Record("TXST", 0x78, Field("TX00", Text("Decals/source.dds")), Field("TX01", Text("Decals/source_n.dds"))),
                Record("CELL", 0x80, Field("EDID", Text("SourceCell")), Field("DATA", [1])), group));
            using var records = FalloutPluginStack.Load(directory, ["Test.esm"]);
            FalloutFormKey Key(uint id) => new("Test.esm", id);
            foreach (var dataset in new uint[] { 0x73, 0x74, 0x75 })
                Check(FalloutImpact.Resolve(records, Key(dataset), 6)?.Decal is
                    { MinimumWidth: 12, MaximumWidth: 20, MinimumHeight: 8, MaximumHeight: 30, Depth: 50,
                        Red: 170, Blue: 4, Diffuse: "textures/Decals/source.dds", Normal: "textures/Decals/source_n.dds" },
                    "Impact lost source decal dimensions, color, texture or a valid legacy material extent.");
            Check(FalloutImpact.Resolve(records, Key(0x73), 11) is null, "An absent late material selected a legacy impact.");
            Reject(() => FalloutImpact.Resolve(records, Key(0x76), 6));
            var invalidDecal = (byte[])decal.Clone(); Float(invalidDecal, 16, -1);
            Reject(() => FalloutImpactDecal.Decode(invalidDecal, "source.dds", null));
            invalidDecal = (byte[])decal.Clone(); Float(invalidDecal, 4, 1);
            Reject(() => FalloutImpactDecal.Decode(invalidDecal, "source.dds", null));
            Check(FalloutActorHealthSource.Read(records, Key(0x10)).Health == 40.5f &&
                FalloutActorHealthSource.Read(records, Key(0x11)).Health == 75 &&
                FalloutActorHealthSource.Read(records, Key(0x12)).Health == 0 &&
                FalloutActorHealthSource.Read(records, Key(0x14)).Health == 30 &&
                FalloutActorHealthSource.Read(records, Key(0x15)).Health == 24.75f,
                "NPC manual/autocalculated health, independent truncation/clamp or zero-health corpse changed.");
            Check(FalloutActorHealthSource.StartsDead(records, Key(0x12)) &&
                !FalloutActorHealthSource.StartsDead(records, Key(0x10)) &&
                !FalloutActorHealthSource.StartsDead(records, Key(1)), "Source corpse admission ignored initial health.");
            Reject(() => FalloutActorHealthSource.Read(records, Key(0x13)));
            Reject(() => FalloutActorHealthSource.Read(records, Key(6)));
            Check(FalloutActorHealthSource.Read(records, Key(6), new(1, 1)).Health == 500 &&
                FalloutActorHealthSource.Read(records, Key(6), new(21, 1)).Health == 750 &&
                FalloutActorHealthSource.Read(records, Key(6), new(100, 1)).Health == 2000,
                "Scaled creature health lost truncation, min/max clamps or the retained selection level.");
            Check(FalloutActorLevel.Resolve(scaledAcbs, 15) == 11, "Scaled level rounded instead of truncating.");
            var fractionalLevel = new byte[24]; UInt(fractionalLevel, 0, 0x80);
            BinaryPrimitives.WriteUInt16LittleEndian(fractionalLevel.AsSpan(8), 10);
            Check(FalloutActorLevel.Resolve(fractionalLevel, 200) == 1,
                "Scaled level rounded the product before truncating the stored Float32 multiplier.");
            var scaledSnapshot = new FalloutActorTemplateSelection(21, 1).Capture();
            Check(FalloutActorHealthSource.Read(records, Key(6), new(scaledSnapshot)).Health == 750,
                "Cold creature scaling used a new player level.");
            var invulnerable = FalloutActorHealthSource.Read(records, Key(1));
            Check((invulnerable with { Flags = 0x40000000 }).Invulnerable &&
                !(invulnerable with { Flags = 0x80000000 }).Invulnerable, "Invulnerability used the wrong source flag.");
            Check(FalloutArmorDefense.Read(records.GetEffective(Key(0x70))) == new FalloutArmorDefense(6, 5) &&
                FalloutArmorDefense.Read(records.GetEffective(Key(0x71))) == new FalloutArmorDefense(0, 12.5f),
                "Armor threshold or hundredths resistance changed.");
            Reject(() => FalloutArmorDefense.Read(records.GetEffective(Key(0x72))));
            var outfit = FalloutActorArmorSelection.Select([
                new(Key(0x70), 4, 2, 0), new(Key(0x71), 4, 5, 0), new(Key(0x72), 2, 1, 0),
                new(Key(0x73), 4, 5, 0)]);
            Check(outfit.SequenceEqual(new[] { Key(0x71), Key(0x72) }),
                "Competing armor did not choose the stronger retained item with independent headwear and stable ties.");
            Reject(() => FalloutActorArmorSelection.Select([new(Key(0x70), 4, float.NaN, 0)]));
            var parts = FalloutBodyPartData.Read(records.GetEffective(Key(2)));
            Check(parts.Parts[1] is { Type: 1, DamageMultiplier: 2, ReplacementModel: "meshes/Gore/source-head.nif", GoreBone: "Neck" },
                "Post-BPND model/attachment fields escaped their source body part.");
            Reject(() => FalloutBodyPartData.Read(records.GetEffective(Key(5))));
            var limbTable = parts with { Parts = [parts.Parts[1] with { Type = 7, ActorValue = 29, HealthPercent = 25 }] };
            Check(limbTable.LimbCondition(29, 200, null) == 100 &&
                limbTable.LimbCondition(29, 200, new Dictionary<byte, float> { [7] = 12.5f }) == 75 &&
                limbTable.LimbCondition(29, 200, new Dictionary<byte, float> { [7] = 60 }) == 0,
                "Limb actor values ignored source identity, fractional damage or cripple clamping.");
            Reject(() => limbTable.LimbCondition(25, 200, null));
            Reject(() => limbTable.LimbCondition(29, 0, null));
            Check(limbTable.CrippledMobilityCount(200, new Dictionary<byte, float> { [7] = 50 }) == 1,
                "Source mobility actor value did not select its damage threshold.");
            var renamed = limbTable with { Parts = [limbTable.Parts[0] with { Name = "Weapon", Node = "Bip01 R Thigh", ActorValue = 28 }] };
            Check(renamed.CrippledMobilityCount(200, new Dictionary<byte, float> { [7] = 100 }) == 0,
                "A legacy thigh bone was mistaken for a source mobility limb.");
            var two = limbTable with { Parts = [limbTable.Parts[0], limbTable.Parts[0] with { Type = 8, ActorValue = 30 }] };
            Check(two.CrippledMobilityCount(200, new Dictionary<byte, float> { [7] = 50, [8] = 50 }) == 2,
                "Independent left/right mobility damage was lost.");
            Reject(() => (limbTable with { Parts = [limbTable.Parts[0], limbTable.Parts[0]] })
                .CrippledMobilityCount(200, new Dictionary<byte, float>()));
            var vitals = new GameplayVitals(1, 200, 200, 70, 70, 0, 100, RadiationRads: 12.5f).Damage(.25f);
            vitals.Validate();
            var restoredVitals = JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(vitals))!;
            Check(restoredVitals.ExactHitPoints == 199.75f && restoredVitals.RadiationRads == 12.5f,
                "Fractional player health or radiation was lost during persistence.");
            var cell = FalloutCellSceneReader.Read(records, Key(0x80));
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(cell);
            Check(world.ActorValue(Key(0x91), "Health") == 50 && !world.IsDead(Key(0x91)), "Source CREA health was not initialized.");
            var first = world.DamageActor(Key(0x91), Key(0x92), 0, 10, 1.5f, 1);
            Check(first is { HealthBefore: 50, HealthAfter: 40, LimbDamage: 15, Died: false } && world.Health(Key(0x92)).Current == 50,
                "Damage pools, limb multiplier or per-reference isolation failed.");
            var killed = world.DamageActor(Key(0x91), Key(0x92), 1, 25, 1, 1);
            Check(killed is { Died: true, Dead: true, HealthAfter: -10 } && world.Inventory(Key(0x91), 1).Contents.Item(Key(3))!.Count == 2,
                "Head multiplier, death transition or source death loot failed.");
            var player = new FalloutPlayerInventory();
            world.Inventory(Key(0x91), 1).Contents.TransferTo(player, Key(3), 1);
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            world.SeverLimb(Key(0x91), 1); world.SeverLimb(Key(0x91), 1);
            Check(world.Get(Key(0x91)).Injury!.SeveredParts!.SequenceEqual(new byte[] { 1 }), "Severed source limb duplicated.");
            using var coldSever = new FalloutReferenceWorld(records);
            coldSever.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            Check(coldSever.Get(Key(0x91)).Injury!.SeveredParts!.Single() == 1, "Cold injury lost its severed limb.");
            Reject(() => world.SeverLimb(Key(0x91), 13));
            using var invalidSever = new FalloutReferenceWorld(records);
            Reject(() => invalidSever.Restore(saved.Select(value => value.Reference == Key(0x92) ?
                value with { Injury = value.Injury! with { SeveredParts = [1] } } : value).ToArray()));
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var corpse = cold.DamageActor(Key(0x91), Key(0x92), 1, 25, 1, 1);
            Check(corpse is { Died: false, Dead: true, HealthDamage: 0, HealthAfter: -10 } &&
                cold.Get(Key(0x91)).Injury!.LimbDamage[1] == 50 && cold.Inventory(Key(0x91), 1).Contents.Item(Key(3))!.Count == 1,
                "Corpse hit resurrected health, lost limb damage or granted death loot twice after reload.");
            Reject(() => cold.DamageActor(Key(0x91), Key(0x92), 14, 1, 1, 1));
            var corrupt = saved.Select(value => value.Reference == Key(0x91) ? value with { Injury = value.Injury! with { Dead = false } } : value).ToArray();
            using var rejected = new FalloutReferenceWorld(records); Reject(() => rejected.Restore(corrupt));
            var body = new FalloutRagdollBodyState(7, [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4], [0, 0, 0], [0, 0, 0], true);
            var pose = new FalloutActorRagdollState(new string('a', 64), [body]); pose.Validate();
            Reject(() => (pose with { Bodies = [body, body] }).Validate());
            Reject(() => (pose with { Bodies = [body with { Transform = new float[12] }] }).Validate());
            Reject(() => (pose with { Bodies = [body with { LinearVelocity = [float.NaN, 0, 0] }] }).Validate());
            Reject(() => (pose with { Bodies = [null!] }).Validate());
            Reject(() => (pose with { SkeletonSha256 = "missing" }).Validate());
            var cut = new FalloutRagdollCutPose(1, [body.Transform]);
            (pose with { Cuts = [cut] }).Validate();
            Reject(() => (pose with { Cuts = [cut, cut] }).Validate());
            Reject(() => (pose with { Cuts = [cut with { Bones = [new float[12]] }] }).Validate());
            using var livingPose = new FalloutReferenceWorld(records);
            Reject(() => livingPose.Restore(saved.Select(value => value.Reference == Key(0x92) ? value with { Ragdoll = pose } : value).ToArray()));
            using var savedPose = new FalloutReferenceWorld(records);
            savedPose.Restore(saved.Select(value => value.Reference == Key(0x91) ? value with { Ragdoll = pose } : value).ToArray());
            RequirePose(savedPose.Get(Key(0x91)).Capture().Ragdoll!, pose);
            Console.WriteLine("OPENNV_ACTOR_DAMAGE_CONTRACT_PASS health=source limbDamage=source deathLoot=once coldRestore=true corpseTransfer=conserved");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Npc(uint id, int health, byte endurance, ushort level, uint flags)
    {
        var data = new byte[11]; BinaryPrimitives.WriteInt32LittleEndian(data, health); data[6] = endurance;
        var acbs = new byte[24]; UInt(acbs, 0, flags); BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(8), level);
        return Record("NPC_", id, Field("ACBS", acbs), Field("DATA", data), Field("NAM4", BitConverter.GetBytes(6u)));
    }
    private static byte[] Setting(uint id, string name, float value) =>
        Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
    private static byte[] Armor(uint id, short rating, float threshold, bool newVegas)
    {
        var data = new byte[newVegas ? 12 : 4]; BinaryPrimitives.WriteInt16LittleEndian(data, rating);
        if (newVegas) Float(data, 4, threshold);
        return Record("ARMO", id, Field("DNAM", data));
    }

    private static byte[] Part(byte type, float multiplier, string node)
    {
        var data = new byte[84]; Float(data, 0, multiplier); data[4] = 1; data[5] = type; data[6] = 25;
        Float(data, 24, 1); Float(data, 40, 1); Float(data, 80, 1);
        return Join(Field("BPNN", Text(node)), Field("BPNT", Text(node)), Field("BPND", data),
            Field("NAM1", Text(type == 1 ? "Gore/source-head.nif" : "")), Field("NAM4", Text(node)));
    }
    private static byte[] Reference(uint id) => Record("ACRE", id, Field("NAME", BitConverter.GetBytes(1u)), Field("DATA", new byte[24]));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void Float(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)data.Length); UInt(bytes, 12, id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void RequirePose(FalloutActorRagdollState actual, FalloutActorRagdollState expected) =>
        Check(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected), "Cold reference restoration lost its death pose.");
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid actor damage state was accepted.");
    }
}
