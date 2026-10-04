using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static class PlayerActorValueContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-player-values-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Values.esm"), Fixture());
            using var records = FalloutPluginStack.Load(directory, ["Values.esm"]);
            var globals = new FalloutGlobalState([new(Key(19), "Switch", (byte)'f', 0, "synthetic")]);
            var inventory = new FalloutPlayerInventory();
            var owner = new FalloutPlayerActorValues(records);
            var skills = Skills(records, owner, inventory, globals);
            var vitals = FalloutPlayerVitals.FromActorValues(records, owner);
            Require(owner.Source.Player == Key(7) && owner.Source.StatsOwner == Key(7) && owner.ReadBase(5) == 5 &&
                owner.ReadPermanent(5) == 7 && owner.ReadCurrent(5) == 7, "Winning player source or unconditional ability permanent pool failed.");
            inventory.Add(records, Key(23), 1, 1, true); inventory.Equip(records, Key(23));
            Require(owner.ReadCurrent(6) == 8 && owner.ReadPermanent(6) == 5 && skills.Value("Perception") == 8,
                "Apparel was included in permanent SPECIAL or omitted from the current skill consumer.");
            globals.Set(Key(19), 1);
            Require(owner.ReadCurrent(10) == 6 && owner.ReadPermanent(10) == 5 && skills.Value("Guns") == 17 &&
                vitals.State.MaximumActionPoints == 83 && vitals.State.MaximumHitPoints == 200,
                "Conditional ability, live skill or current-Agility AP derivation failed.");
            owner.AddModifier(5, FalloutActorValuePool.Permanent, .5f);
            owner.AddModifier(5, FalloutActorValuePool.Temporary, 2.25f);
            owner.AddModifier(5, FalloutActorValuePool.Damage, -3);
            Require(owner.ReadPermanent(5) == 7.5f && owner.ReadCurrent(5) == 6.75f,
                "Player getters conflated permanent, temporary or damage pools.");
            var before = owner.Capture();
            var book = new FalloutSpecialAllocationSession(40, owner.AllocationBinding);
            Require(book.Change(5, 1) && owner.ReadBase(5) == 8 && owner.ReadPermanent(5) == 10 &&
                owner.Capture().Values[5] == before.Values[5] with { Base = 8 } &&
                owner.Capture().Values.Where(pair => pair.Key != 5).All(pair => pair.Value == before.Values[pair.Key]),
                "Book editing did not floor permanent first, write only BASE or retain modifier pools.");
            // Retiring this session has no rollback or author-script stage effect.
            book = new(40, owner.AllocationBinding);
            Require(book.Values[0] == 10 && owner.ReadBase(5) == 8, "A second book session lost its immediate mutation.");
            vitals.Damage(12.25f);
            vitals.Publish(vitals.State with { ActionPoints = vitals.State.ActionPoints - 7, ExperiencePoints = 15 });
            owner.AddModifier(7, FalloutActorValuePool.Temporary, 4);
            Require(vitals.State.MaximumHitPoints == 200, "Temporary Endurance changed the permanent HP formula.");
            owner.AddModifier(7, FalloutActorValuePool.Permanent, 1);
            owner.WriteBaseInteger(7, 6);
            Require(vitals.State.MaximumHitPoints == 240 && vitals.State.ExactHitPoints == 227.75f && vitals.State.ExperiencePoints == 15,
                "Base/permanent Endurance did not update vitals or healed the retained damage/XP.");
            owner.WriteBaseInteger(10, 6);
            Require(skills.Value("Guns") == 19 && vitals.State.MaximumActionPoints == 86 && vitals.State.ActionPoints == 79,
                "Immediate base edit was not shared by skills and AP or lost the spent-AP deficit.");
            VerifyScripts(records);
            VerifyCold(records, owner, inventory, globals, vitals);
            VerifyTemplate(directory);
            Console.WriteLine("OPENNV_PLAYER_ACTOR_VALUES_CONTRACT_PASS enginePlayer=true sourceTemplate=true pools=true sourceConditions=true immediateBase=true scriptAuthority=true derivedConsumers=true cold=true legacy=true atomic=true parity=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static FalloutPlayerSkills Skills(FalloutPluginStack records, FalloutPlayerActorValues owner,
        FalloutPlayerInventory inventory, FalloutGlobalState globals)
    {
        var skills = new FalloutPlayerSkills(records, () => owner.BaseSpecial, _ => false, () => [], globals, inventory,
            Key(7), () => Key(10), () => false, actorValues: owner);
        owner.BindConstantModifiers(skills.Modifiers);
        return skills;
    }

    private static void VerifyScripts(FalloutPluginStack records)
    {
        foreach (var shared in new[] { false, true })
        {
            var owner = new FalloutPlayerActorValues(records); owner.BindConstantModifiers((_, _) => []);
            var vitals = FalloutPlayerVitals.FromActorValues(records, owner);
            vitals.Damage(23.75f, 6, 1);
            vitals.Publish(vitals.State with { ActionPoints = 3, ExperiencePoints = 17, RadiationRads = 75 });
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ => throw new InvalidDataException("Unexpected player effect."),
                ReadActorValue: (actor, name, kind) => actor == Key(0x14) ? owner.Read(FalloutPlayerActorValues.SpecialValue(name), kind) :
                    throw new InvalidDataException("Player values fabricated a placed reference."),
                ChangeActorValue: (actor, name, operation, value) =>
                {
                    Require(actor == Key(0x14), "Source player write had a different receiver."); owner.Change(name, operation, value);
                }, ResetPlayerHealth: vitals.ResetHealth));
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0);
            scripts.Host = new((_, _) => throw new InvalidOperationException("Unexpected player stage."), name => owner.ReadCurrent(FalloutPlayerActorValues.SpecialValue(name)),
                shared ? executor.ExecuteProgram : null,
                ReadPlayerActorValue: (name, kind) => owner.Read(FalloutPlayerActorValues.SpecialValue(name), kind), ChangePlayerActorValue: owner.Change,
                ResetPlayerHealth: vitals.ResetHealth);
            scripts.Advance(0);
            Require(scripts.Capture().Instances.Single() is { Error: null } && quests.Variable(Key(0x501), 1) == 14 &&
                quests.Variable(Key(0x501), 2) == 10 && quests.Variable(Key(0x501), 3) == 20 && owner.ReadBase(5) == 14 &&
                owner.ReadCurrent(5) == 20 && owner.ReadPermanent(5) == 10 && world.InstanceCount == 0,
                "Shared/fallback scripts did not preserve raw current, bounded permanent or BASE/ModAV/ForceAV authority.");
            Require(vitals.State.ExactHitPoints == vitals.State.MaximumHitPoints && vitals.State.LimbDamage is null &&
                vitals.State.ActionPoints == 3 && vitals.State.ExperiencePoints == 17 && vitals.State.RadiationRads == 75,
                "Shared/fallback ResetHealth failed to cure HP and limbs or altered AP, XP or radiation.");
            var script = records.GetEffective(Key(0x500)); var quest = records.GetEffective(Key(0x501));
            var reset = FalloutGameModeProgram.Read("begin GameMode\nplayer.ResetHealth\nend");
            var unbound = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ => throw new InvalidDataException("Unbound reset fabricated a player.")));
            var retained = JsonSerializer.Serialize(vitals.State);
            Reject(() => unbound.ExecuteProgram(quest, script, reset, 0));
            Require(retained == JsonSerializer.Serialize(vitals.State) && world.InstanceCount == 0,
                "Unbound ResetHealth mutated vitals or created a placed player reference.");
            vitals.Damage(vitals.State.MaximumHitPoints);
            var dead = JsonSerializer.Serialize(vitals.State);
            Reject(vitals.ResetHealth);
            Require(dead == JsonSerializer.Serialize(vitals.State), "ResetHealth substituted for player resurrection.");
            var failing = FalloutGameModeProgram.Read("begin GameMode\nplayer.SetAV Strength 6\nplayer.ModAV Strength .25\nplayer.SetAV Strength 9\nend");
            Reject(() => executor.ExecuteProgram(quest, script, failing, 0));
            Require(owner.ReadBase(5) == 6 && owner.Capture().Values[5].Permanent == 6,
                "Unsupported integer coercion replayed or rolled back the executed base prefix.");
        }
    }

    private static void VerifyCold(FalloutPluginStack records, FalloutPlayerActorValues owner, FalloutPlayerInventory inventory,
        FalloutGlobalState globals, FalloutPlayerVitals vitals)
    {
        var snapshot = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
        var cold = new FalloutPlayerActorValues(records, snapshot); var coldSkills = Skills(records, cold, inventory, globals);
        var coldVitals = FalloutPlayerVitals.FromActorValues(records, cold,
            JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(vitals.State))!);
        Require(Enumerable.Range(5, 7).All(value => owner.ReadBase(value) == cold.ReadBase(value) &&
            owner.ReadCurrent(value) == cold.ReadCurrent(value) && owner.ReadPermanent(value) == cold.ReadPermanent(value)) &&
            coldSkills.Value("Guns") == 19 && coldVitals.State == vitals.State && snapshot.Values[5].Permanent == .5f,
            "Cold player values lost pools, double-counted source effects or restored stale derived consumers.");
        var before = JsonSerializer.Serialize(cold.Capture());
        foreach (var invalid in new[]
        {
            snapshot with { Reference = 0x15 }, snapshot with { PlayerSha256 = new string('0', 64) },
            snapshot with { StatsWinner = "Wrong.esp" }, snapshot with { Values = snapshot.Values.Where(pair => pair.Key != 11).ToDictionary() },
            snapshot with { Values = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Key == 6 ? pair.Value with { Damage = float.NaN } : pair.Value) },
            snapshot with { Values = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Key == 6 ? pair.Value with { Base = 3.25f } : pair.Value) },
            snapshot with { Values = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Key == 6 ? pair.Value with { Base = MathF.BitIncrement(2147483648f) } : pair.Value) },
        })
        {
            Reject(() => cold.Restore(invalid)); Require(JsonSerializer.Serialize(cold.Capture()) == before, "Failed pool restore partially changed live values.");
        }
        Reject(() => cold.AddModifier(5, FalloutActorValuePool.Temporary, float.PositiveInfinity));
        Reject(() => cold.WriteBaseInteger(4, 5)); Reject(() => cold.Change("Guns", "setav", 5));
        Require(JsonSerializer.Serialize(cold.Capture()) == before, "Invalid mutation changed a supported player pool.");
        var legacy = new FalloutPlayerActorValues(records, legacy: new(4, 5, 6, 7, 8, 9, 10));
        legacy.BindConstantModifiers((_, _) => []);
        Require(legacy.BaseSpecial == new FalloutNativeSpecialState(4, 5, 6, 7, 8, 9, 10) &&
            legacy.Capture().Values.Values.All(value => value.Permanent == 0 && value.Temporary == 0 && value.Damage == 0),
            "Legacy seven-integer migration created current values or modifier history.");
        cold.WriteBaseInteger(5, -5);
        Require(cold.ReadBase(5) == -5 && cold.ReadPermanent(5) == 1 && cold.ReadCurrent(5) == -3.25f && cold.ReadBoundedCurrent(5) == 1,
            "A menu allocation clamp was imposed on the source BASE setter or raw script current getter.");
        foreach (var boundary in new[] { int.MinValue, int.MaxValue })
        {
            cold.WriteBaseInteger(5, boundary);
            var boundarySnapshot = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(cold.Capture()))!;
            var boundaryCold = new FalloutPlayerActorValues(records, boundarySnapshot);
            Require(cold.ReadBase(5) == (float)boundary && cold.BaseSpecial.Strength == boundary &&
                boundaryCold.ReadBase(5) == cold.ReadBase(5) && boundaryCold.BaseSpecial == cold.BaseSpecial,
                "Signed BASE boundary failed its Float32 storage or legacy integer/cold projection.");
        }
    }

    private static void VerifyTemplate(string directory)
    {
        var acbs = new byte[24]; acbs[8] = 1; acbs[22] = 2;
        File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Values.esm"),
            Record("NPC_", 7, Field("ACBS", acbs), Field("TPLT", BitConverter.GetBytes(28u))),
            Record("NPC_", 28, Field("ACBS", Actor()), Field("DATA", [100, 0, 0, 0, 3, 4, 5, 6, 7, 8, 9]))));
        using var records = FalloutPluginStack.Load(directory, ["Values.esm", "Patch.esp"]);
        var owner = new FalloutPlayerActorValues(records);
        Require(owner.Source.PlayerWinner == "Patch.esp" && owner.Source.StatsOwner == Key(28) && owner.ReadBase(5) == 3 && owner.ReadBase(11) == 9,
            "Winning player template stats were replaced with the direct NPC bytes.");
    }

    private static byte[] Fixture()
    {
        var condition = new byte[28]; Float(condition, 4, 1); UInt(condition, 8, 74); UInt(condition, 12, 19);
        var header = new byte[20]; UInt(header, 12, 3); header[16] = 1;
        var script = "short first\nshort second\nshort third\nbegin GameMode\nplayer.SetAV Strength 14\nplayer.ModAV Strength 2\n" +
            "set first to player.GetBaseAV Strength\nset second to player.GetPermanentActorValue Strength\n" +
            "player.ForceAV Strength 20\nset third to player.GetAV Strength\nplayer.ResetHealth\nend";
        return Join(Header(), Record("NPC_", 7, Field("ACBS", Actor()), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5]),
                Field("SPLO", BitConverter.GetBytes(21u)), Field("SPLO", BitConverter.GetBytes(24u))), Record("RACE", 10),
            Record("NPC_", 28, Field("ACBS", Actor()), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])),
            Record("GLOB", 19, Field("EDID", Text("Switch")), Field("FNAM", [(byte)'f']), Field("FLTV", BitConverter.GetBytes(0f))),
            Modifier(16, 5), Modifier(17, 6), Modifier(18, 10),
            Record("SPEL", 21, Field("SPIT", Declaration(4)), Effect(16, 2)),
            Record("SPEL", 24, Field("SPIT", Declaration(4)), Effect(18, 1), Field("CTDA", condition)),
            Record("ENCH", 22, Field("ENIT", Declaration(3)), Effect(17, 3)),
            Record("ARMO", 23, Field("EDID", Text("Outfit")), Field("DATA", new byte[12]),
                Field("BMDT", [4, 0, 0, 0, 0, 0, 0, 0]), Field("EITM", BitConverter.GetBytes(22u))),
            Setting(100, "fAVDHealthEnduranceOffset", 5), Setting(101, "fAVDHealthEnduranceMult", 20), Setting(102, "fAVDHealthLevelMult", 5),
            Setting(103, "fAVDActionPointsBase", 65), Setting(104, "fAVDActionPointsMult", 3),
            Record("GMST", 105, Field("EDID", Text("iXPBase")), Field("DATA", BitConverter.GetBytes(200))),
            Record("GMST", 106, Field("EDID", Text("iXPBumpBase")), Field("DATA", BitConverter.GetBytes(150))),
            Setting(107, "fAVDSkillSmallGunsBase", 2), Setting(108, "fAVDSkillPrimaryBonusMult", 2), Setting(109, "fAVDSkillLuckBonusMult", .5f),
            Record("SCPT", 0x500, Field("SCHR", header), Local(1, "first"), Local(2, "second"), Local(3, "third"),
                Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCTX", Text(script))),
            Record("QUST", 0x501, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x500u))));
    }

    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Actor() { var result = new byte[24]; result[8] = 1; return result; }
    private static byte[] Declaration(uint type) { var result = new byte[16]; UInt(result, 0, type); return result; }
    private static byte[] Modifier(uint id, uint value) { var result = new byte[72]; UInt(result, 0, 2); UInt(result, 68, value); return Record("MGEF", id, Field("DATA", result)); }
    private static byte[] Effect(uint id, uint magnitude) { var result = new byte[20]; UInt(result, 0, magnitude); return Join(Field("EFID", BitConverter.GetBytes(id)), Field("EFIT", result)); }
    private static byte[] Setting(uint id, string name, float value) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
    private static byte[] Local(uint index, string name) { var result = new byte[24]; UInt(result, 0, index); return Join(Field("SLSD", result), Field("SCVR", Text(name))); }
    private static FalloutFormKey Key(uint id) => new("Values.esm", id);
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Float(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        UInt(result, 4, (uint)data.Length); UInt(result, 12, id); data.CopyTo(result, 24); return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported player actor value state was accepted.");
    }
}
