using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class CharacterGenerationContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-chargen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Creation.esm");
        try
        {
            var actor = new byte[24]; actor[8] = 1;
            var header = new byte[20]; header[16] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1);
            var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1);
            File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 7, Field("ACBS", actor), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])),
                Setting(100, "fAVDHealthEnduranceMult", BitConverter.GetBytes(20f)),
                Setting(101, "fAVDHealthLevelMult", BitConverter.GetBytes(5f)),
                Setting(102, "fAVDActionPointsBase", BitConverter.GetBytes(65f)),
                Setting(103, "fAVDActionPointsMult", BitConverter.GetBytes(3f)),
                Setting(104, "iXPBase", BitConverter.GetBytes(200)),
                Setting(105, "iXPBumpBase", BitConverter.GetBytes(150)),
                Record("QUST", 0x601, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x600u))),
                Record("SCPT", 0x600, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("sample")),
                    Field("SCTX", Text("short sample\nbegin GameMode\nSetInCharGen 1\nset sample to GetInCharGen\nend"))),
                Record("QUST", 0x630, Field("DATA", [0, 0]), Field("INDX", new byte[2]), Field("QSDT", [0]),
                    Field("SCTX", Text("SetInCharGen 1")))));
            using var records = FalloutPluginStack.Load(directory, ["Creation.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0);
            scripts.Advance(0);
            Require(scripts.Session.InCharGen && quests.Variable(Key(0x601), 1) == 1 && scripts.Capture().Instances.Single().Error is null,
                "Fallback source program did not enter chargen and read the shared flag.");
            var snapshot = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(scripts.Capture()))!;
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(quests.Capture());
            var cold = new FalloutQuestScripts(records, coldQuests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0);
            cold.Restore(snapshot);
            Require(cold.Session.InCharGen, "Cold script session lost character-generation state.");
            var legacy = JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>("{\"Hardcore\":false,\"AutoDisplayObjectives\":true,\"Achievements\":[]}")!;
            Require(!legacy.InCharGen, "Legacy session acquired character generation.");

            var special = new FalloutNativeSpecialState(5, 5, 5, 5, 5, 5, 5);
            var vitals = new FalloutPlayerVitals(records, Key(7), special);
            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.CharacterGeneration) throw new InvalidDataException("Unexpected effect.");
                scripts.Session.SetInCharGen(effect.Enable, vitals.RequireLevelUpOwner);
            }, InCharGen: () => scripts.Session.InCharGen));
            void Run(string body) => executor.ExecuteProgram(records.GetEffective(Key(0x601)), records.GetEffective(Key(0x600)),
                FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
            Run("SetInCharGen 0\nset sample to GetInCharGen");
            Require(!scripts.Session.InCharGen && quests.Variable(Key(0x601), 1) == 0, "Reference result did not share chargen state.");
            Run("SetInCharGen 1");
            foreach (var invalid in new[] { "-1", "2", "0.5" }) Reject(() => Run("SetInCharGen " + invalid + "\nset sample to 99"));
            Require(scripts.Session.InCharGen && quests.Variable(Key(0x601), 1) == 0, "Invalid flag mutated state or executed its suffix.");

            var initial = vitals.State;
            // A real saved chargen player may have earned several levels of XP.
            // Preserve it without incrementing levels or healing the player.
            var earned = initial.Damage(12.25f) with { ExperiencePoints = 1000 };
            vitals.Publish(earned);
            vitals.SetSpecial(special with { Endurance = 6 });
            Require(vitals.State.Level == 1 && vitals.State.ExperiencePoints == 1000 && vitals.State.NextLevelExperiencePoints == 200 &&
                vitals.State.ExactHitPoints == vitals.State.MaximumHitPoints - 12.25f,
                "Deferred XP changed level, disappeared during SPECIAL derivation or healed damage.");
            var coldVitals = new FalloutPlayerVitals(records, Key(7), special,
                JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(vitals.State))!);
            Reject(coldVitals.RequireLevelUpOwner);
            var before = vitals.State;
            Reject(() => Run("SetInCharGen 0\nset sample to 99"));
            Require(scripts.Session.InCharGen && vitals.State == before && quests.Variable(Key(0x601), 1) == 0,
                "Unsupported earned level-up cleared chargen, lost XP or ran its suffix.");
            Reject(() => scripts.Session.SetInCharGen(false, null));
            Require(scripts.Session.InCharGen, "Missing player owner cleared character-generation state.");
            Reject(() => vitals.Publish(before with { ExperiencePoints = -1 }));
            Reject(() => vitals.Publish(before with { NextLevelExperiencePoints = 0 }));
            Require(vitals.State == before, "Invalid vitals partially published.");

            var bootstrap = new FalloutNewGameBootstrap(records,
                FalloutInstallationSettings.ReadLayers([], [new("General", "SCharGenQuest", "00000630")]), quests, scripts, world,
                (_, _, _, _) => throw new InvalidDataException("Unexpected startup command."),
                _ => throw new InvalidDataException("Unexpected startup effect."), () => true);
            scripts.Session.Restore(legacy);
            bootstrap.Start();
            Require(scripts.Session.InCharGen, "Pre-world source result lost its chargen flag.");
            Console.WriteLine("OPENNV_CHARACTER_GENERATION_CONTRACT_PASS sharedScripts=true bootstrap=true cold=true legacyDefault=true deferredXp=true specialDerivation=true invalidAtomic=true levelUpGapVisible=true xpRewards=unbound parity=unverified");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private static FalloutFormKey Key(uint id) => new("Creation.esm", id);
    private static byte[] Setting(uint id, string name, byte[] data) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", data));
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Unsupported or invalid chargen operation was accepted.");
    }
}
