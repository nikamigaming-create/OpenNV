using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class PlayerTagSkillContracts
{
    private static readonly string[] Names = ["Barter", "EnergyWeapons", "Explosives", "Lockpick", "Medicine", "MeleeWeapons",
        "Repair", "Science", "SmallGuns", "Sneak", "Speech", "Throwing", "Unarmed"];

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-tag-slots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Tags.esm"), Fixture());
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Tags.esm"),
                Record("AVIF", 0x107, Field("EDID", Text("RenamedScience")), Field("FULL", Text("Winning science")))));
            using var records = FalloutPluginStack.Load(directory, ["Tags.esm", "Patch.esp"]);
            var identities = Names.Select((name, index) => new FalloutNativeSkillIdentity((uint)(0x100 + index),
                index == 7 ? "RenamedScience" : "AV" + name, index == 7 ? "Winning science" : name)).ToArray();
            var contract = new FalloutNativeTagSkillContract(identities, 3, 80, 85, 90);
            var tags = new FalloutPlayerTagSkills(records, contract);
            var skills = new FalloutPlayerSkills(records, () => new(5, 5, 5, 5, 5, 5, 5), tags.IsTagged, () => [], null,
                new(), Key(7), () => Key(8), () => false);
            var baseScience = skills.Value("Science");
            tags.Set("sCiEnCe", 3);
            Require(tags.Capture().Slots.Take(3).All(skill => skill is null) && tags.Capture().Slots[3] == identities[7] &&
                skills.Value("Science") == baseScience + 11, "Sparse fourth slot lost its winning AVIF or live source bonus.");
            tags.Set("Science", 0);
            Require(tags.Selection.Count == 1 && skills.Value("Science") == baseScience + 11,
                "Two indexed memberships duplicated the skill projection or bonus.");
            tags.Set("\"SmallGuns\"", 0);
            Require(tags.IsTagged("Guns") && tags.IsTagged("SmallGuns") && tags.IsTagged("Science"), "Indexed replacement or skill alias failed.");
            var snapshot = JsonSerializer.Deserialize<FalloutPlayerTagSkillsSnapshot>(JsonSerializer.Serialize(tags.Capture()))!;
            var cold = new FalloutPlayerTagSkills(records, contract, snapshot);
            Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(tags.Capture()) && cold.Selection.Count == 2,
                "Cold tags compacted holes, reordered slots or lost membership.");
            var retained = JsonSerializer.Serialize(tags.Capture());
            foreach (var index in new[] { -1, .5, 4, double.NaN, double.PositiveInfinity }) Reject(() => tags.Set("Science", index));
            Reject(() => tags.Set("Health", 0));
            Reject(() => new FalloutPlayerTagSkills(records, contract, new([null, null, null])));
            Reject(() => new FalloutPlayerTagSkills(records, contract, new([identities[7] with { EditorId = "Stale" }, null, null, null])));
            Reject(() => FalloutPlayerTagSkills.Validate(snapshot, [identities[7]]));
            Require(retained == JsonSerializer.Serialize(tags.Capture()), "Invalid tag write partially mutated player state.");
            Reject(() => tags.AcceptMenu([identities[0], identities[1]]));
            tags.AcceptMenu(identities.Take(3).ToArray());
            Require(tags.Capture().Slots[3] is null && !tags.IsTagged("Science") && skills.Value("Science") == baseScience,
                "Menu acceptance kept a fourth slot or stale skill bonus.");
            var accepted = new FalloutPlayerTagSkills(records, contract);
            accepted.AcceptMenu(identities.Take(3).ToArray());
            Require(JsonSerializer.Serialize(accepted.Capture()) == JsonSerializer.Serialize(tags.Capture()), "Menu tags did not retain submitted selection order.");
            foreach (var shared in new[] { false, true })
            {
                using var world = new FalloutReferenceWorld(records);
                var quests = new FalloutQuestState(records);
                var sourceTags = new FalloutPlayerTagSkills(records, contract);
                var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                    _ => throw new InvalidOperationException("Tag command escaped C# player state."), TagSkills: sourceTags));
                var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(), defaultProcessingDelay: 0);
                scripts.Host = new((_, _) => throw new InvalidOperationException("Tag fixture changed a stage."), _ => 0,
                    shared ? executor.ExecuteProgram : null, TagSkills: sourceTags);
                scripts.Advance(0);
                Require(scripts.Capture().Instances.Single().Error is null && sourceTags.Capture().Slots[3] == identities[8] &&
                    quests.Variable(Key(0x501), 2) == 1 && world.InstanceCount == 0,
                    "Shared/fallback source tag writes, variable index or query fabricated an engine player or failed.");
                var unbound = new FalloutReferenceScripts(records, world, quests,
                    new((_, _) => false, _ => throw new InvalidOperationException("Unbound tag escaped its owner.")));
                Reject(() => unbound.ExecuteProgram(records.GetEffective(Key(0x501)), records.GetEffective(Key(0x500)),
                    FalloutGameModeProgram.Read("begin GameMode\nSetPlayerTagSkill Science 1\nend"), 0));
            }
            Console.WriteLine("OPENNV_PLAYER_TAG_SLOTS_CONTRACT_PASS indexedFour=true sparse=true replacement=true winningAvif=true " +
                "sourceBonus=true membershipOnce=true sharedAndFallbackScripts=true cold=true legacy=true menuCountRetained=true atomicInvalid=true");
        }
        finally { foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path); Directory.Delete(directory); }
        Fallout3Skills();
    }

    private static void Fallout3Skills()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-fo3-skills-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var names = Names.Append("BigGuns").ToArray();
            File.WriteAllBytes(Path.Combine(directory, "Fallout3.esm"), Join(Header(),
                Join(names.Select((name, index) => Record("AVIF", (uint)(0x100 + index),
                    Field("EDID", Text("AV" + name)), Field("FULL", Text(name)))).ToArray()),
                Record("NPC_", 7, Field("ACBS", new byte[24])), Record("RACE", 8),
                Setting(0x200, "fAVDSkillBigGunsBase", 3), Setting(0x201, "fAVDSkillSmallGunsBase", 4),
                Setting(0x202, "fAVDSkillPrimaryBonusMult", 2), Setting(0x203, "fAVDSkillLuckBonusMult", .5f),
                Setting(0x204, "fAVDTagSkillBonus", 11)));
            using var records = FalloutPluginStack.Load(directory, ["Fallout3.esm"]);
            var catalog = FalloutNativeTagSkillResolver.ResolveSkills(records);
            Require(catalog.Count == 13 && catalog.Any(skill => skill.EditorId == "AVBigGuns") &&
                catalog.All(skill => skill.EditorId != "AVThrowing"), "FO3 inherited New Vegas's active skill slots.");
            var tags = new FalloutPlayerTagSkills(records, catalog);
            var skills = new FalloutPlayerSkills(records, () => new(5, 5, 5, 5, 5, 5, 5), tags.IsTagged, () => [], null,
                new(), new("Fallout3.esm", 7), () => new("Fallout3.esm", 8), () => false);
            var baseline = skills.Value("SmallGuns");
            tags.Set("SmallGuns", 3);
            Require(tags.IsTagged("SmallGuns") && !tags.IsTagged("Guns") && skills.Value(41) == baseline + 11 &&
                skills.Value("BigGuns") == skills.Value(33), "FO3 skill aliases or source tag bonuses were replaced.");
            Reject(() => tags.Set("Survival", 0));
            Reject(() => tags.Set("Throwing", 0));
            Reject(() => skills.Value("Guns"));
            Reject(() => tags.AcceptMenu(catalog.Take(3).ToArray()));
            tags.AcceptMenu(catalog.Take(2).ToArray(), 2);
            var cold = new FalloutPlayerTagSkills(records, catalog, snapshot: tags.Capture());
            Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(tags.Capture()),
                "FO3 cold tag slots required a New Vegas creation-stage contract.");
            Console.WriteLine("OPENNV_FO3_SKILLS_CONTRACT_PASS activeSlots=true aliases=true sourceBonus=true explicitMenuCount=true cold=true");
        }
        finally { foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path); Directory.Delete(directory); }
    }

    private static byte[] Fixture()
    {
        var header = new byte[20]; UInt(header, 12, 2); header[16] = 1;
        var local = new byte[24]; UInt(local, 0, 1);
        var second = new byte[24]; UInt(second, 0, 2);
        return Join(Header(), Join(Names.Select((name, index) => Record("AVIF", (uint)(0x100 + index),
                Field("EDID", Text("AV" + name)), Field("FULL", Text(name)))).ToArray()),
            Record("NPC_", 7, Field("ACBS", new byte[24])), Record("RACE", 8),
            Setting(0x200, "fAVDSkillScienceBase", 2), Setting(0x201, "fAVDSkillPrimaryBonusMult", 2),
            Setting(0x202, "fAVDSkillLuckBonusMult", .5f), Setting(0x203, "fAVDTagSkillBonus", 11),
            Record("SCPT", 0x500, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("slot")),
                Field("SLSD", second), Field("SCVR", Text("picked")),
                Field("SCTX", Text("short slot\nshort picked\nbegin GameMode\nset slot to 3\nSetPlayerTagSkill \"SmallGuns\" slot\nset picked to IsPlayerTagSkill Guns\nend"))),
            Record("QUST", 0x501, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x500u))));
    }
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Setting(uint id, string name, float value) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
    private static FalloutFormKey Key(uint id) => new("Tags.esm", id);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
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
        throw new InvalidOperationException("Unsupported tag state was accepted.");
    }
}
