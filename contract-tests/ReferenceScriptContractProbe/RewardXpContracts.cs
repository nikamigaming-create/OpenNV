using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class RewardXpContracts
{
    internal static void Run()
    {
        OwnerArithmetic();
        ObjectAndCold();
        RepeatedContact();
        foreach (var shared in new[] { false, true }) QuestAndCold(shared);
        InvalidAndMissing();
        GlobalFunction();
        Console.WriteLine("OPENNV_REWARD_XP_CONTRACT_PASS playerOwner=true sourceCap=true cappedTotal=true " +
            "signedDecrease=true float32=true noHeal=true object=true questSharedAndFallback=true " +
            "globalFunction=true consumedPrefix=true cold=true activeXpPerkApplied=true " +
            "levelUpGapVisible=true xpPresentation=unverified");
    }

    private static void OwnerArithmetic()
    {
        using var fixture = new XpFixture("RewardXP 25");
        var before = fixture.Vitals.State.Damage(12.25f) with { ActionPoints = 40 };
        fixture.Vitals.Publish(before);
        Require(fixture.Experience.Reward(25) == 25 && fixture.Experience.Reward(-5) == -5,
            "RewardXP did not use the shared signed XP amount.");
        Require(fixture.Vitals.State == (before with { ExperiencePoints = 20 }), "XP award healed, advanced level or changed AP.");
        Require(fixture.Experience.Reward(1000) == 530 && fixture.Vitals.State.ExperiencePoints == 550,
            "XP award did not clamp to the source level-three threshold.");
        fixture.Records.NumericSettings.Set("iMaxCharacterLevel", 1);
        Require(fixture.Experience.Reward(50) == 0 && fixture.Vitals.State.ExperiencePoints == 550,
            "Already capped source player gained or lost XP.");
        fixture.Records.NumericSettings.Set("iMaxCharacterLevel", 4000);
        fixture.Vitals.Publish(before);
        Require(fixture.Experience.Reward(100000003) == 100000000, "RewardXP lost the engine Float32 conversion.");
        var retained = fixture.Vitals.State;
        fixture.Perks.Add(new(9, 3, 1.1f, []));
        Require(fixture.Experience.Reward(10) == 11 && fixture.Vitals.State.ExperiencePoints == retained.ExperiencePoints + 11,
            "XP perk multiplier did not apply before upward rounding.");
        fixture.Perks[0] = new(9, 3, .9f, []);
        Require(fixture.Experience.Reward(10) == 9, "XP-reducing perk did not modify a discovery award.");
        fixture.Perks.Clear();
        fixture.Vitals.Publish(before);
        Reject(() => fixture.Experience.Reward(-1));
        foreach (var amount in new[] { double.NaN, double.PositiveInfinity, .5, 2147483648d, -2147483649d })
            Reject(() => fixture.Experience.Reward(amount));
        Require(fixture.Vitals.State == before, "Invalid XP operand partially mutated player vitals.");
        fixture.Experience.Reward(200);
        var beforeOverflow = fixture.Vitals.State;
        Reject(() => fixture.Experience.Reward(2147483520));
        Require(fixture.Vitals.State == beforeOverflow, "Signed XP overflow was hidden by cap clamping or partially published.");
        var session = new FalloutScriptSession(); session.SetInCharGen(true, null);
        Reject(() => session.SetInCharGen(false, fixture.Vitals.RequireLevelUpOwner));
        Require(session.InCharGen && fixture.Vitals.State.ExperiencePoints == 200 && fixture.Vitals.State.Level == 1,
            "Level-up refusal discarded consumed XP or falsely left character generation.");
    }

    private static void ObjectAndCold()
    {
        using var fixture = new XpFixture("set saved to 1\nOtherRef.RewardXP 30\nUnownedSuffix\nset suffix to 9");
        var result = fixture.Scripts.Activate(fixture.Caller, fixture.Player);
        Require(result.Error is not null && fixture.World.Get(fixture.Caller).Read(1) == 1 &&
            fixture.World.Get(fixture.Caller).Read(2) == 0 && fixture.Vitals.State.ExperiencePoints == 30,
            "Object XP command lost its calling-reference validation, player owner or stopped prefix.");
        Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error == result.Error &&
            fixture.Vitals.State.ExperiencePoints == 30, "Repeated activation replayed a consumed failed prefix.");
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(fixture.World.Capture()))!;
        var state = JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(fixture.Vitals.State))!;
        using var cold = new FalloutReferenceWorld(fixture.Records); cold.Restore(saved);
        cold.LoadCell(FalloutCellSceneReader.Read(fixture.Records, fixture.Cell));
        var coldVitals = new FalloutPlayerVitals(fixture.Records, fixture.PlayerBase, fixture.Special, state);
        var xp = new FalloutPlayerExperience(fixture.Records, coldVitals, () => []);
        var scripts = new FalloutReferenceScripts(fixture.Records, cold, new(fixture.Records),
            new((_, _) => false, _ => throw new InvalidDataException("Unexpected XP presentation effect."), RewardXp: value => xp.Reward(value)));
        var coldResult = scripts.Activate(fixture.Caller, fixture.Player);
        Require(coldResult.Error == result.Error &&
            coldVitals.State.ExperiencePoints == 30 && cold.Get(fixture.Caller).Read(2) == 0,
            "Cold stopped object script changed: " + JsonSerializer.Serialize(new { result.Error, coldError = coldResult.Error,
                xp = coldVitals.State.ExperiencePoints, suffix = cold.Get(fixture.Caller).Read(2) }));
    }

    private static void RepeatedContact()
    {
        using var fixture = new XpFixture("RewardXP 30\nUnownedSuffix", eventName: "OnTriggerEnter");
        var result = fixture.Scripts.Dispatch(fixture.Caller, "OnTriggerEnter", fixture.Player);
        Require(result.Error is not null && fixture.Vitals.State.ExperiencePoints == 30,
            "Contact fixture did not consume its XP prefix before failure.");
        Require(fixture.Scripts.Dispatch(fixture.Caller, "OnTriggerEnter", fixture.Player).Error == result.Error &&
            fixture.Vitals.State.ExperiencePoints == 30, "Repeated contact replayed a consumed failed prefix.");
    }

    private static void QuestAndCold(bool shared)
    {
        using var fixture = new XpFixture("", "if saved == 0\nset saved to 1\nRewardXP 50\nUnownedSuffix\nset suffix to 9\nendif");
        var quests = new FalloutQuestState(fixture.Records);
        var scripts = fixture.QuestScripts(quests, shared, fixture.Experience);
        scripts.Advance(0);
        var error = scripts.Capture().Instances.Single().Error;
        Require(error is not null && quests.Variable(fixture.Quest, 1) == 1 && quests.Variable(fixture.Quest, 2) == 0 &&
            fixture.Vitals.State.ExperiencePoints == 50, "Quest award lost its player owner or consumed failure prefix.");
        var state = JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(fixture.Vitals.State))!;
        var snapshot = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(scripts.Capture()))!;
        var coldQuests = new FalloutQuestState(fixture.Records); coldQuests.Restore(quests.Capture());
        var coldVitals = new FalloutPlayerVitals(fixture.Records, fixture.PlayerBase, fixture.Special, state);
        var xp = new FalloutPlayerExperience(fixture.Records, coldVitals, () => []);
        var cold = fixture.QuestScripts(coldQuests, shared, xp); cold.Restore(snapshot); cold.Advance(0);
        Require(cold.Capture().Instances.Single().Error == error && coldVitals.State.ExperiencePoints == 50 &&
            coldQuests.Variable(fixture.Quest, 2) == 0, "Cold quest script replayed XP or consumed its failed suffix.");
    }

    private static void InvalidAndMissing()
    {
        foreach (var command in new[] { "RewardXP", "RewardXP 1 2", "RewardXP .5", "UnboundRef.RewardXP 25" })
        {
            using var fixture = new XpFixture("set saved to 1\n" + command + "\nset suffix to 9");
            Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is not null &&
                fixture.World.Get(fixture.Caller).Read(1) == 1 && fixture.World.Get(fixture.Caller).Read(2) == 0 &&
                fixture.Vitals.State.ExperiencePoints == 0, "Invalid XP command changed player state or ran its suffix.");
        }
        using var missing = new XpFixture("set saved to 1\nRewardXP 25\nset suffix to 9", bind: false);
        Require(missing.Scripts.Activate(missing.Caller, missing.Player).Error is not null &&
            missing.World.Get(missing.Caller).Read(1) == 1 && missing.Vitals.State.ExperiencePoints == 0,
            "An absent shared player XP owner silently waived the command.");
    }

    private static void GlobalFunction()
    {
        using var fixture = new XpFixture("", function: "RewardXP 30");
        var program = fixture.Records.GetEffective(new("Experience.esm", 0x52));
        fixture.Scripts.InvokeFunction(program.FormKey, null, [], 0);
        Require(fixture.Vitals.State.ExperiencePoints == 30 && fixture.World.ScriptManualSaves.EnteredInvocations == 0,
            "Global source function XP required a fake calling actor or lost its invocation lifetime.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("Unsupported XP behavior was admitted.");
    }

    private sealed class XpFixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("opennv-xp-");
        internal FalloutPluginStack Records { get; }
        internal FalloutReferenceWorld World { get; }
        internal FalloutReferenceScripts Scripts { get; }
        internal FalloutPlayerVitals Vitals { get; }
        internal FalloutPlayerExperience Experience { get; }
        internal List<FalloutPerkEntry> Perks { get; } = [];
        internal FalloutFormKey Caller => Key(0x90);
        internal FalloutFormKey Player => Records.RuntimeFormKey(0x14);
        internal FalloutFormKey PlayerBase => Key(7);
        internal FalloutFormKey Cell => Key(0x80);
        internal FalloutFormKey Quest => Key(0x60);
        internal FalloutNativeSpecialState Special => new(5, 5, 5, 5, 5, 5, 5);
        internal XpFixture(string body, string quest = "", string? function = null, bool bind = true, string eventName = "OnActivate")
        {
            var actor = new byte[24]; actor[8] = 1;
            var header = new byte[20]; U32(2).CopyTo(header, 12);
            var questHeader = header.ToArray(); questHeader[16] = 1;
            byte[][] Scope(byte[] declaration, string source) => [Field("SCHR", declaration),
                Field("SLSD", Local(1)), Field("SCVR", Text("saved")), Field("SLSD", Local(2)), Field("SCVR", Text("suffix")),
                Field("SCRO", U32(0x14)), Field("SCRO", U32(0x91)), Field("SCTX", Text(source))];
            File.WriteAllBytes(Path.Combine(_directory.FullName, "Experience.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 7, Field("ACBS", actor), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])),
                Setting(100, "fAVDHealthEnduranceMult", BitConverter.GetBytes(20f)),
                Setting(101, "fAVDHealthLevelMult", BitConverter.GetBytes(5f)),
                Setting(102, "fAVDActionPointsBase", BitConverter.GetBytes(65f)),
                Setting(103, "fAVDActionPointsMult", BitConverter.GetBytes(3f)),
                Setting(104, "iXPBase", U32(200)), Setting(105, "iXPBumpBase", U32(150)), Setting(106, "iMaxCharacterLevel", U32(3)),
                Record("ACTI", 1, Field("SCRI", U32(0x50))),
                Record("SCPT", 0x50, Scope(header, "float saved\nfloat suffix\nbegin " + eventName + "\n" + body + "\nend")),
                Record("QUST", 0x60, Field("DATA", [1, 0]), Field("SCRI", U32(0x51))),
                Record("SCPT", 0x51, Scope(questHeader, "float saved\nfloat suffix\nbegin GameMode\n" + quest + "\nend")),
                function is null ? [] : Record("SCPT", 0x52, Scope(header, "float saved\nfloat suffix\nbegin Function {}\n" + function + "\nend")),
                Record("CELL", 0x80, Field("DATA", [1, 0])), Group(0x80,
                    Record("REFR", 0x90, Field("NAME", U32(1)), Field("DATA", new byte[24])),
                    Record("REFR", 0x91, Field("EDID", Text("OtherRef")), Field("NAME", U32(1)), Field("DATA", new byte[24])))));
            Records = FalloutPluginStack.Load(_directory.FullName, ["Experience.esm"]);
            World = new(Records); World.LoadCell(FalloutCellSceneReader.Read(Records, Cell));
            Vitals = new(Records, PlayerBase, Special); Experience = new(Records, Vitals, () => Perks);
            Scripts = new(Records, World, new(Records), new((_, _) => false,
                _ => throw new InvalidDataException("Unexpected XP presentation effect."),
                RewardXp: bind ? value => Experience.Reward(value) : null));
        }
        internal FalloutQuestScripts QuestScripts(FalloutQuestState quests, bool shared, FalloutPlayerExperience xp)
        {
            var source = new FalloutReferenceScripts(Records, World, quests, new((_, _) => false, _ => { }, RewardXp: value => xp.Reward(value)));
            return new(Records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), references: World, defaultProcessingDelay: 0)
            {
                Host = new((_, _) => () => throw new InvalidDataException("Unexpected XP SetStage."), _ => 0,
                    ExecuteProgram: shared ? source.ExecuteProgram : null, RewardXp: value => xp.Reward(value)),
            };
        }
        public void Dispose() { World.Dispose(); Records.Dispose(); _directory.Delete(true); }
        private static FalloutFormKey Key(uint id) => new("Experience.esm", id);
        private static byte[] U32(uint value) => BitConverter.GetBytes(value);
        private static byte[] Local(uint index) { var value = new byte[24]; U32(index).CopyTo(value, 0); return value; }
        private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
        private static byte[] Setting(uint id, string name, byte[] data) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", data));
        private static byte[] Record(string tag, uint id, params byte[][] fields)
        {
            var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(tag).CopyTo(result, 0);
            U32((uint)data.Length).CopyTo(result, 4); U32(id).CopyTo(result, 12); data.CopyTo(result, 24); return result;
        }
        private static byte[] Field(string tag, byte[] data)
        {
            var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(tag).CopyTo(result, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
        }
        private static byte[] Group(uint cell, params byte[][] entries)
        {
            var data = Join(entries); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
            U32((uint)result.Length).CopyTo(result, 4); U32(cell).CopyTo(result, 8); U32(6).CopyTo(result, 12); data.CopyTo(result, 24); return result;
        }
        private static byte[] Join(params byte[][] parts) => parts.SelectMany(value => value).ToArray();
    }
}
