using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class PerkParameterContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-perk-parameters-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var basePath = Path.Combine(directory, "Perks.esm");
            var patchPath = Path.Combine(directory, "Patch.esp");
            var scriptHeader = new byte[20]; scriptHeader[16] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(scriptHeader.AsSpan(12), 3);
            var baseBytes = Join(Header(), MixedPerk(2, 1),
                Record("NPC_", 7), Record("RACE", 8), Record("SPEL", 40),
                Record("PERK", 2, Numeric(0, 5, 2, 7, 9)),
                Record("PERK", 3, Field("PRKE", [0, 0, 0]), Field("DATA", [0x10, 0, 0, 0, 12, 0xaa, 0xbb, 0xcc]), Field("PRKF", [])),
                Record("PERK", 4, Field("PRKE", [1, 0, 0]), Field("DATA", BitConverter.GetBytes(40u)), Field("PRKF", [])),
                Record("PERK", 5, Numeric(0, 3, 2, 1)),
                Record("PERK", 6, Numeric(0, 3, 1, float.NaN)),
                Record("PERK", 9, Numeric(0, 3, 9, 1)), Record("MISC", 33),
                Record("QUST", 0x10, Field("EDID", Text("TestQuest")), Field("DATA", [1, 0, 0, 0, 0, 0, 0, 0]),
                    Field("SCRI", BitConverter.GetBytes(0x11u))),
                Record("SCPT", 0x11, Field("SCHR", scriptHeader), Local(1, "currentPerk"), Local(2, "seen"), Local(3, "once"),
                    Field("SCRO", BitConverter.GetBytes(1u)), Field("SCTX", Text("ref currentPerk\nfloat seen\nshort once\n" +
                    "begin GameMode\nif once == 0\nSetNthPerkEntryValue1 TestPerk 1 5\n" +
                    "set seen to GetNthPerkEntryValue1 TestPerk 1\nset once to 1\nendif\nend"))),
                Weapon(), Setting(0x30, "fDamageWeaponMult", 1), Setting(0x31, "fDamageSkillBase", 1),
                Setting(0x32, "fDamageSkillMult", 0));
            var patchBytes = Join(Header("Perks.esm"), MixedPerk(3, 2));
            File.WriteAllBytes(basePath, baseBytes); File.WriteAllBytes(patchPath, patchBytes);
            using var records = FalloutPluginStack.Load(directory, ["Perks.esm", "Patch.esp"]);
            FalloutFormKey Key(uint id) => new("Perks.esm", id);
            var owner = records.PerkParameters;
            var reader = new FalloutAbilityModifiers(records);
            var peer = new FalloutAbilityModifiers(records);
            Require(owner.Count(Key(1)) == 3 && owner.Type(Key(1), 0) == 5 && owner.Type(Key(1), 1) == 1 &&
                owner.EntryPoint(Key(1), 2) == 34 && reader.Perk(Key(1)).Entries[0].Value == 3 &&
                reader.Perk(Key(1)).Entries.Select(entry => entry.SourceIndex).SequenceEqual([1, 2]),
                "Winning source values or mixed entry indices were lost.");
            _ = peer.Perk(Key(1)); // A decoded reader already exists before mutation.
            using var world = new FalloutReferenceWorld(records);
            world.ChangePerk(Key(0x14), Key(1), true);
            var inventory = new FalloutPlayerInventory();
            inventory.Publish([new(Key(0x20), 0x20, "Weapon", "WEAP", 1, 1, 1)]);
            var skills = new FalloutPlayerSkills(records, () => new(5, 5, 5, 5, 5, 5, 5), _ => false,
                () => [], null, inventory, Key(7), () => Key(8), () => false, () => world.AcquiredPerks(Key(0x14)));
            var damage = new FalloutWeaponDamageResolver(records, inventory, _ => 100, () => skills.PerkEntries);
            Require(damage.Resolve(Key(0x20), 10).Amount == 30, "Winning perk value did not reach weapon damage.");
            var quests = new FalloutQuestState(records);
            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidOperationException("Perk commands invented a presentation effect.")));
            var quest = records.GetEffective(Key(0x10)); var definition = records.GetEffective(Key(0x11));
            void Run(string body) => executor.ExecuteProgram(quest, definition,
                FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
            Run("set currentPerk to TestPerk\nSetNthPerkEntryValue1 (currentPerk) 1 4\n" +
                "SetNthPerkEntryValue1 currentPerk 2 0.5\nset seen to GetNthPerkEntryValue1 currentPerk 1");
            Require(quests.Variable(quest.FormKey, 2) == 4 && damage.Resolve(Key(0x20), 10).Amount == 40 &&
                peer.Perk(Key(1)).Entries[1].Value == .5f && world.PerkEntries(Key(0x14)).First().Value == 4,
                "Source commands, typed form variables, cached peer readers or live gameplay retained old parameters.");
            Run("set seen to GetPerkEntryCount currentPerk + GetNthPerkEntryType currentPerk 1 + GetNthPerkEntryFunction currentPerk 2");
            Require(quests.Variable(quest.FormKey, 2) == 38, "Perk metadata queries changed arity or source index.");
            Reject(() => Run("SetNthPerkEntryValue1 currentPerk 1.5 9"));
            Require(owner.Get(Key(1), 1) == 4, "A rejected script write changed a perk parameter.");
            var fallback = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), inventory,
                defaultProcessingDelay: .01f, references: world);
            fallback.Advance(1);
            Require(fallback.Capture().Instances.Single().Error is null && quests.Variable(quest.FormKey, 2) == 5 &&
                damage.Resolve(Key(0x20), 10).Amount == 50, "Fallback quests changed a private perk copy.");
            using var replacement = new FalloutReferenceWorld(records);
            replacement.RestoreActorOverrides(world.CaptureActorOverrides());
            Require(replacement.PerkEntries(Key(0x14)).First().Value == 5 && reader.Perk(Key(1)).Entries[0].Value == 5,
                "Replacing a world or reader reset a loaded-form mutation.");
            Require(owner.Get(Key(2), 0) == 7 && owner.Get(Key(2), 0, 1) == 9 &&
                owner.Set(Key(2), 0, 11, 1) && owner.Get(Key(2), 0) == 7 && owner.Get(Key(2), 0, 1) == 11,
                "Two-value parameters lost their independent slots.");
            Require(owner.Type(Key(3), 0) == 6 && owner.Get(Key(3), 0) == 12 && owner.Set(Key(3), 0, 21.9f) &&
                owner.Get(Key(3), 0) == 21 && !owner.Set(Key(3), 0, 1, 1), "Quest-stage parameter lost its byte/slot semantics.");
            Require(owner.Get(Key(4), 0) == -1 && !owner.Set(Key(4), 0, 1) && owner.Get(Key(1), uint.MaxValue) == -1 &&
                !owner.Set(Key(1), uint.MaxValue, 1), "Non-numeric or absent entries acquired parameters.");
            foreach (var action in new Action[] { () => owner.Set(Key(1), 1, float.PositiveInfinity),
                () => owner.Set(Key(3), 0, 256), () => owner.Get(Key(5), 0), () => owner.Get(Key(6), 0),
                () => owner.Get(Key(9), 0), () => owner.Get(Key(33), 0), () => FalloutPerkParameters.Index(-1),
                () => FalloutPerkParameters.Index(0.5), () => FalloutPerkParameters.Index(double.NaN) }) Reject(action);
            Require(owner.Get(Key(1), 1) == 5 && owner.Get(Key(3), 0) == 21, "Invalid parameters partially replaced live values.");
            using var coldStack = FalloutPluginStack.Load(directory, ["Perks.esm", "Patch.esp"]);
            Require(coldStack.PerkParameters.Get(Key(1), 1) == 3 && File.ReadAllBytes(basePath).SequenceEqual(baseBytes) &&
                File.ReadAllBytes(patchPath).SequenceEqual(patchBytes), "Loaded-form changes escaped their source-stack lifetime or wrote owned inputs.");
            Console.WriteLine("OPENNV_PERK_PARAMETER_CONTRACT_PASS winning=true mixedIndices=true typedForms=true reference=true fallbackQuest=true cachedReaders=live damage=true twoValues=true questStage=true replacement=true sourceReadonly=true invalidAtomic=true lifetime=selected-stack");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Perks.esm")); File.Delete(Path.Combine(directory, "Patch.esp"));
            Directory.Delete(directory);
        }
    }

    private static byte[] MixedPerk(float damage, float spread) => Record("PERK", 1, Field("EDID", Text("TestPerk")),
        Field("FULL", Text("Display name differs from the compiled identity")),
        Field("PRKE", [1, 0, 0]), Field("DATA", BitConverter.GetBytes(40u)), Field("PRKF", []),
        Numeric(0, 3, 1, damage), Numeric(34, 3, 1, spread));
    private static byte[] Numeric(byte entry, byte function, byte type, params float[] values) => Join(
        Field("PRKE", [2, 0, 0]), Field("DATA", [entry, function, 1]), Field("EPFT", [type]),
        Field("EPFD", values.SelectMany(BitConverter.GetBytes).ToArray()), Field("PRKF", []));
    private static byte[] Weapon()
    {
        var data = new byte[120]; BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(104), 41);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(116), 1);
        return Record("WEAP", 0x20, Field("DNAM", data));
    }
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Setting(uint id, string name, float value) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
    private static byte[] Local(uint index, string name)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException) { return; }
        throw new InvalidOperationException("Unsupported perk state was accepted.");
    }
}
