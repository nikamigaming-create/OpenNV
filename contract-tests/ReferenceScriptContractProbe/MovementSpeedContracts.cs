using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class MovementSpeedContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-movement-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Movement.esm"), Fixture());
            using var records = FalloutPluginStack.Load(directory.FullName, ["Movement.esm"]);
            CheckCreatureTurning(records);
            var inventory = new FalloutPlayerInventory();
            inventory.Restore(new(Enumerable.Range(0, 14).Select(type => Item((uint)(1000 + type), "WEAP"))
                .Concat(new[] { Item(20, "ARMO"), Item(21, "ARMO"), Item(22, "ARMO") }).ToArray(), null), []);
            var owner = new FalloutPlayerMovementSpeed(records, inventory);
            var values = new Dictionary<int, float> { [21] = 100, [72] = 0, [46] = 0, [13] = 250 };
            var vitals = new GameplayVitals(1, 100, 100, 100, 100, 0, 100);
            FalloutPlayerMovement Speed(bool run = true, bool drawn = true, bool sneak = false,
                IReadOnlyList<FalloutPerkEntry>? perks = null) => owner.Calculate(run, sneak, drawn, vitals,
                    value => values[value], perks ?? [], _ => throw new InvalidDataException("Unexpected condition."));
            Near(Speed().Speed, 200); Near(Speed(false).Speed, 100); Near(Speed(drawn: false).Speed, 220);
            Near(Speed(sneak: true).Speed, 100);
            values[21] = 125; Near(Speed().Speed, 250); values[21] = 100;
            vitals = vitals with { LimbDamage = new Dictionary<byte, float> { [7] = 50 } };
            Check(Speed().CrippledLegs == 1, "Source actor-value limb was not selected."); Near(Speed().Speed, 160);
            values[72] = 1; Near(Speed().Speed, 200);
            values[46] = 251; var encumbered = Speed();
            Check(encumbered.Overencumbered && !encumbered.Running, "Overencumbrance did not suppress running.");
            Near(encumbered.Speed, 80);
            var strongBack = Speed(perks: [new(22, 2, 50, [])]);
            Check(!strongBack.Overencumbered && strongBack.Running, "Carry perk did not restore running through the shared capacity owner.");
            values[46] = 250.9f; Check(!Speed().Overencumbered, "Carried weight skipped source integer conversion."); values[46] = 0; values[72] = 0;
            vitals = vitals with { LimbDamage = new Dictionary<byte, float> { [7] = 50, [8] = 50 } }; Near(Speed().Speed, 120);
            vitals = vitals with { LimbDamage = null };
            inventory.Equip(records, Key(20)); Near(Speed().Speed, 160);
            inventory.Equip(records, Key(21)); Near(Speed().Speed, 180);
            inventory.Equip(records, Key(22)); Near(Speed().Speed, 200);
            for (uint type = 0; type < 14; type++)
            {
                inventory.Equip(records, Key(1000 + type));
                Near(Speed().Speed, type is 5 or 6 ? 170 : type is 8 or 9 ? 140 : 200);
                Near(Speed(drawn: false).Speed, 220);
            }
            Near(Speed(perks: [new(42, 3, 1.1f, [])]).Speed, 220);
            Near(Speed(run: false, perks: [new(42, 3, 1.1f, [])]).Speed, 100);
            Check(records.NumericSettings.Set("fMoveRunMult", 3), "Source setting update failed."); Near(Speed().Speed, 300);
            values[21] = float.NaN; Reject(() => Speed()); values[21] = 100;
            Reject(() => Speed(perks: [new(42, 3, 1.1f, []), new(42, 3, 1.2f, [])]));
            Console.WriteLine("OPENNV_MOVEMENT_SPEED_PASS sourceSettings=true equipment=true drawn=true limbValues=true ignoreLimbs=true encumbrance=true perk42=true liveSettings=true");
        }
        finally { directory.Delete(true); }
    }

    private static void CheckCreatureTurning(FalloutPluginStack records)
    {
        float Turn(uint id) => FalloutCreatureTurnSpeed.ResolveDegrees(records,
            FalloutCreatureTurnSpeed.ReadAuthoredDegrees(records, records.GetEffective(Key(id))));
        Near(Turn(300), 45);
        Near(Turn(301), 45); // Stats inheritance owns TNAM.
        Near(Turn(302), 70); // Model-only inheritance does not own TNAM.
        Near(Turn(308), 45); // Negative zero also requests the setting.
        Check(records.NumericSettings.Set("fCreatureDefaultTurningSpeed", 90), "Creature default setting was not mutable.");
        Near(Turn(300), 90); Near(Turn(301), 90); Near(Turn(302), 70);
        Check(records.NumericSettings.Set("fCreatureDefaultTurningSpeed", 0), "Creature default zero was not retained.");
        Reject(() => Turn(300)); Near(Turn(302), 70);
        Check(records.NumericSettings.Set("fCreatureDefaultTurningSpeed", -1), "Creature default negative value was not retained.");
        Reject(() => Turn(300)); Near(Turn(302), 70);
        Check(records.NumericSettings.Set("fCreatureDefaultTurningSpeed", 45), "Creature default setting was not restored.");
        foreach (var id in new uint[] { 303, 304, 305, 306, 307 }) Reject(() => Turn(id));
        Reject(() => FalloutCreatureTurnSpeed.ResolveDegrees(records, float.PositiveInfinity));
        Console.WriteLine("OPENNV_CREATURE_TURN_SPEED_PASS zeroNamedDefault=true signedZero=true statsInheritance=true currentSetting=true authoredNonzero=true invalidRefused=true gameplay=false");
    }

    internal static void RunOwned(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var creature = records.GetEffective(records.RuntimeFormKey(0x168bd9));
        var authoredTurn = FalloutCreatureTurnSpeed.ReadAuthoredDegrees(records, creature);
        var selectedTurn = FalloutCreatureTurnSpeed.ResolveDegrees(records, authoredTurn);
        Check(authoredTurn == 0 && selectedTurn == FalloutGameSettingFloats.Read(records, "fCreatureDefaultTurningSpeed"),
            "Reached creature did not use its current source default for authored zero turn speed.");
        Console.WriteLine($"OPENNV_OWNED_CREATURE_TURN_SPEED_PASS creature={creature.FormKey} authored={authoredTurn:R} selected={selectedTurn:R} sourceReadonly=true gameplay=false");
        var inventory = new FalloutPlayerInventory();
        var values = new FalloutPlayerActorValues(records);
        var skills = new FalloutPlayerSkills(records, () => values.BaseSpecial, _ => false, () => [],
            FalloutGlobalState.Read(records), inventory, values.Source.Player,
            () => FalloutDialogueTopic.RequiredForm(records.GetEffective(values.Source.Player), "RNAM"), () => false, actorValues: values);
        values.BindConstantModifiers(skills.Modifiers);
        var vitals = FalloutPlayerVitals.FromActorValues(records, values);
        var owner = new FalloutPlayerMovementSpeed(records, inventory);
        var walk = owner.Calculate(false, false, false, vitals.State, skills.Value, [], skills.AbilityCondition);
        var run = owner.Calculate(true, false, false, vitals.State, skills.Value, [], skills.AbilityCondition);
        Check(values.ReadBase(21) == values.Source.SpeedMultiplier && values.ReadBase(72) == 0 && walk.Speed > 0 && run.Speed > walk.Speed,
            "Owned source movement did not retain source pools/settings or positive walk/run speeds.");
        Near(run.Speed / walk.Speed, FalloutGameSettingFloats.Read(records, "fMoveRunMult"));
        values.Change("SpeedMult", "setav", values.Source.SpeedMultiplier * 2);
        Near(owner.Calculate(true, false, false, vitals.State, skills.Value, [], skills.AbilityCondition).Speed, run.Speed * 2);
        Console.WriteLine($"OPENNV_OWNED_MOVEMENT_SPEED_PASS speedMult={values.Source.SpeedMultiplier} walk={walk.Speed:R} run={run.Speed:R} sourceReadonly=true vanillaSelection=true parity=unverified");
    }

    private static byte[] Fixture()
    {
        var settings = new (string Name, float Value)[] { ("fMoveBaseSpeed", 100), ("fMoveRunMult", 2),
            ("fMoveSneakMult", .5f), ("fMoveNoWeaponMult", 1.1f), ("fMoveOneCrippledLegSpeedMult", .8f),
            ("fMoveTwoCrippledLegsSpeedMult", .6f), ("fMoveMediumArmorPenalty", .1f), ("fMoveHeavyArmorPenalty", .2f),
            ("fMove2HRPenalty", .15f), ("fMove2HBigPenalty", .3f), ("fCreatureDefaultTurningSpeed", 45) };
        byte[] Part(byte type, byte value)
        {
            var data = new byte[84]; data[5] = type; data[6] = 50; data[7] = value;
            return Join(Field("BPTN", Text("Mobility" + type)), Field("BPND", data));
        }
        byte[] Weapon(uint type) { var data = new byte[204]; BitConverter.GetBytes(type).CopyTo(data, 0); return Record("WEAP", 1000 + type, Field("DNAM", data)); }
        byte[] Creature(uint id, float turn, ushort inheritance = 0)
        {
            var data = new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22), inheritance);
            var fields = new List<byte[]> { Field("ACBS", data), Field("TNAM", BitConverter.GetBytes(turn)) };
            if (inheritance != 0) fields.Add(Field("TPLT", BitConverter.GetBytes(300u)));
            return Record("CREA", id, fields.ToArray());
        }
        return Join(Record("TES4", 0, Field("HEDR", new byte[12])), Record("BPTD", 0x1d, Part(7, 29), Part(8, 30)),
            Record("ARMO", 20, Field("BMDT", [4, 0, 0, 0, 0x80, 0, 0, 0])),
            Record("ARMO", 21, Field("BMDT", [4, 0, 0, 0, 8, 0, 0, 0])),
            Record("ARMO", 22, Field("BMDT", [4, 0, 0, 0, 0, 0, 0, 0])),
            Join(settings.Select((setting, index) => Record("GMST", (uint)(100 + index), Field("EDID", Text(setting.Name)), Field("DATA", BitConverter.GetBytes(setting.Value)))).ToArray()),
            Join(Enumerable.Range(0, 14).Select(type => Weapon((uint)type)).ToArray()),
            Creature(300, 0), Creature(301, 60, 2), Creature(302, 70, 64), Creature(303, -1), Creature(304, float.NaN),
            Record("CREA", 305, Field("ACBS", new byte[24]), Field("TNAM", new byte[3])),
            Record("CREA", 306, Field("ACBS", new byte[24]), Field("TNAM", BitConverter.GetBytes(1f)), Field("TNAM", BitConverter.GetBytes(2f))),
            Record("CREA", 307, Field("ACBS", new byte[24])), Creature(308, -0.0f));
    }

    private static FalloutCampaignItem Item(uint id, string type) => new(Key(id), id, "Synthetic", type, 1, 1, 1);
    private static FalloutFormKey Key(uint id) => new("Movement.esm", id);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[data.Length + 6]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[data.Length + 24]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Near(float actual, float expected) => Check(Math.Abs(actual - expected) < .0001f, $"Movement speed {actual:R} differs from {expected:R}.");
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unowned movement state was accepted.");
    }
}
