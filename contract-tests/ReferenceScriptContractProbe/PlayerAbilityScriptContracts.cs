using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

internal static partial class PlayerAbilityScriptContracts
{
    private const string Plugin = "Effects.esm";
    private static readonly string[] SkillIds = ["Barter", "EnergyWeapons", "Explosives", "Lockpick", "Medicine",
        "MeleeWeapons", "Repair", "Science", "SmallGuns", "Sneak", "Speech", "Throwing", "Unarmed"];

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-active-effect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckHeaderLayout();
            foreach (var duplicate in new[] { false, true }) CheckStartCold(directory, duplicate);
            CheckCompiledExtentMismatch(directory);
            CheckPrefixFailure(directory);
            CheckUnsupportedEvent(directory);
            CheckCompiledAbilityAuthority(directory);
            Console.WriteLine("OPENNV_PLAYER_ABILITY_SCRIPT_START_PASS sourceReader=true unequalHeaderCounts=true compiledExtentRefused=true effectLocals=true genuinePlayer=true independentEffects=true prefixFailure=true cold=true nonStartRefused=true compiledExecution=authoritative_SCDA parity=unverified");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void CheckHeaderLayout()
    {
        var bytes = new byte[20];
        UInt(bytes, 0, 13); UInt(bytes, 4, 2); UInt(bytes, 8, 7); UInt(bytes, 12, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 0x1234);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), 0x5678);
        var header = FalloutScriptHeader.Read(bytes);
        Require(header.PrefixWord == 13 && header.ReferenceCount == 2 && header.CompiledBytes == 7 &&
            header.VariableCount == 1 && header.Type == 0x1234 && header.Flags == 0x5678,
            "SCHR decoder confused independently declared fields or discarded type/flag bits.");
        Reject(() => FalloutScriptHeader.Read(new byte[19]));
        Reject(() => FalloutScriptHeader.Read(new byte[21]));
    }

    private static void CheckCompiledExtentMismatch(string directory)
    {
        // The actual byte count equals the reference count, while the
        // declared compiled extent differs. SCHR[4] would admit this source.
        File.WriteAllBytes(Path.Combine(directory, Plugin), Fixture(false, declaredCompiledBytes: 7, compiledBytes: 2));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
        var effects = Bind(records, world, actor, skills);
        Reject(effects.Synchronize);
        Require(effects.Capture().Effects.Count == 0 && skills.CaptureValues().Pools.Count == 0 && world.InstanceCount == 0,
            "Malformed compiled extent reached Start, committed an effect/local/skill prefix, or fabricated a player.");
    }

    private static void CheckStartCold(string directory, bool duplicate)
    {
        File.WriteAllBytes(Path.Combine(directory, Plugin), Fixture(duplicate));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutPlayerActorValues(records);
        var skills = Skills(records, actor);
        var effects = Bind(records, world, actor, skills);
        var amount = duplicate ? 6 : 3;
        // Begin through an unrelated SPECIAL getter, before its recursive guard.
        Require(actor.ReadPermanent(7) == 5 && skills.ReadSkill("Barter", FalloutActorValueRead.Permanent) == 15 + amount,
            "Unrelated SPECIAL query failed to execute the actual Start or mutated an unrelated SPECIAL.");
        Require(world.InstanceCount == 0 && effects.Capture().Effects.Count == (duplicate ? 2 : 1) &&
            effects.Capture().Effects.All(effect => effect.Started && effect.Error is null &&
                BitConverter.UInt64BitsToDouble(effect.Locals.Single().Payload) == 1),
            "Active effects shared local storage or fabricated a player reference instance.");
        for (var index = 0; index < 10; index++)
            Require(actor.ReadCurrent(10) == 5 && skills.ReadSkill("Barter", FalloutActorValueRead.Current) == 15 + amount,
                "An ordinary getter reapplied ScriptEffectStart.");
        var savedSkills = JsonSerializer.Deserialize<FalloutPlayerSkillValuesSnapshot>(JsonSerializer.Serialize(skills.CaptureValues()))!;
        var savedEffects = JsonSerializer.Deserialize<FalloutPlayerAbilityScriptsSnapshot>(JsonSerializer.Serialize(effects.Capture()))!;
        using var coldWorld = new FalloutReferenceWorld(records);
        var coldActor = new FalloutPlayerActorValues(records, actor.Capture());
        var coldSkills = Skills(records, coldActor);
        var coldEffects = Bind(records, coldWorld, coldActor, coldSkills, savedEffects);
        coldSkills.RestoreValues(savedSkills);
        Require(coldActor.ReadPermanent(7) == 5 && coldSkills.ReadSkill("Barter", FalloutActorValueRead.Current) == 15 + amount &&
            JsonSerializer.Serialize(coldEffects.Capture()) == JsonSerializer.Serialize(savedEffects) && coldWorld.InstanceCount == 0,
            "Cold effect locals/source identity were lost or the committed skill mutation was applied twice.");
        Reject(() => new FalloutPlayerAbilityScripts(records, skills.SelectedConstantEffects, skills.AbilityCondition,
            savedEffects with { Effects = savedEffects.Effects.Select(effect => effect with { ScriptSha256 = new('0', 64) }).ToArray() }));
        Reject(() => new FalloutPlayerAbilityScripts(records, skills.SelectedConstantEffects, skills.AbilityCondition,
            savedEffects with { Effects = savedEffects.Effects.Skip(1).ToArray() }));
        Reject(() => FalloutPlayerAbilityScripts.Validate(savedEffects with
        {
            Effects = savedEffects.Effects.Select(effect => effect with { Started = false, Error = null }).ToArray()
        }));
        Reject(() => new FalloutScriptEffectLocals(records.GetEffective(Key(0x30)), []));
        var authority = new FalloutAbilityModifiers(records);
        Reject(() => authority.Spell(Key(0x21)));
        Require(authority.Spell(Key(0x21), effects).Count == 0, "Owned script-only spell manufactured a constant modifier.");
    }

    private static void CheckPrefixFailure(string directory)
    {
        File.WriteAllBytes(Path.Combine(directory, Plugin), Fixture(false, failure: true));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
        var effects = Bind(records, world, actor, skills);
        Reject(effects.Synchronize);
        var state = effects.Capture().Effects.Single();
        Require(!state.Started && state.Error is not null && BitConverter.UInt64BitsToDouble(state.Locals.Single().Payload) == 1 &&
            skills.CaptureValues().Pools[32].Permanent == 3 && world.InstanceCount == 0 && state.Compiled!.Events is
                [{ Cursor.CommittedInstructions: 3, Receipt.Disposition: "closed-failure", LastReachedOffset: not null }],
            "Failed Start lost its actual local/skill/compiled prefix or marked the script complete.");
        var prefix = JsonSerializer.Serialize(skills.CaptureValues());
        Reject(effects.Synchronize);
        Require(JsonSerializer.Serialize(skills.CaptureValues()) == prefix, "A retained failed Start replayed its committed prefix.");
        var saved = JsonSerializer.Deserialize<FalloutPlayerAbilityScriptsSnapshot>(JsonSerializer.Serialize(effects.Capture()))!;
        var restored = new FalloutPlayerAbilityScripts(records, skills.SelectedConstantEffects, skills.AbilityCondition, saved);
        restored.BindExecutor(_ => throw new InvalidOperationException("Failed cold Start must not execute."));
        Reject(restored.Synchronize);
    }

    private static void CheckUnsupportedEvent(string directory)
    {
        File.WriteAllBytes(Path.Combine(directory, Plugin), Fixture(false, update: true));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
        var effects = Bind(records, world, actor, skills);
        Reject(effects.Synchronize);
        Require(skills.CaptureValues().Pools.Count == 0 && world.InstanceCount == 0,
            "Unowned Update was skipped after a successful-looking Start.");
    }

    private static FalloutPlayerSkills Skills(FalloutPluginStack records, FalloutPlayerActorValues actor) => new(records,
        () => actor.BaseSpecial, _ => false, () => [], null, new FalloutPlayerInventory(), Key(7), () => Key(10), () => false,
        actorValues: actor);
    private static FalloutPlayerAbilityScripts Bind(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutPlayerActorValues actor, FalloutPlayerSkills skills, FalloutPlayerAbilityScriptsSnapshot? restore = null, FalloutQuestState? quests = null)
    {
        var effects = new FalloutPlayerAbilityScripts(records, skills.SelectedConstantEffects, skills.AbilityCondition, restore);
        var executor = new FalloutReferenceScripts(records, world, quests ?? new FalloutQuestState(records),
            new((_, _) => throw new NotSupportedException("Unexpected fixture furniture query."),
                _ => throw new NotSupportedException("Unexpected fixture presentation command."),
                ReadActorValue: (reference, name, kind) =>
                {
                    Require(records.RuntimeFormId(reference) == 0x14, "Effect query used an unrelated actor identity.");
                    return skills.IsSkill(name) ? skills.ReadSkill(name, kind) : actor.Read(FalloutPlayerActorValues.SpecialValue(name), kind);
                }, ChangeActorValue: (reference, name, operation, value) =>
                {
                    Require(records.RuntimeFormId(reference) == 0x14, "Effect mutation used an unrelated actor identity.");
                    if (skills.IsSkill(name)) skills.ChangeSkill(name, operation, value); else actor.Change(name, operation, value);
                }));
        effects.BindExecutor(executor.ExecuteActiveEffect); skills.BindAbilityScripts(effects);
        actor.BindConstantModifiers(skills.Modifiers); actor.BindAbilityLifecycle(effects.Synchronize);
        return effects;
    }

    private static byte[] Fixture(bool duplicate, bool failure = false, bool update = false, uint? declaredCompiledBytes = null, int? compiledBytes = null)
    {
        var acbs = new byte[24]; acbs[8] = 1;
        var spit = new byte[16]; UInt(spit, 0, 4);
        var mgef = new byte[72]; UInt(mgef, 0, 0x70); UInt(mgef, 8, 0x30); UInt(mgef, 64, 1);
        var efit = new byte[20]; BinaryPrimitives.WriteInt32LittleEndian(efit.AsSpan(16), -1);
        var local = new byte[24]; UInt(local, 0, 1); local[16] = 1;
        // Independent canonical SCDA drives actual execution. SCTX is retained
        // only as contradictory/invalid diagnostic evidence in additional cases.
        var compiled = compiledBytes is { } extent ? new byte[extent] : CompiledAbilityBody(failure, update);
        var schr = new byte[20]; UInt(schr, 0, 13); UInt(schr, 4, 2);
        UInt(schr, 8, declaredCompiledBytes ?? (uint)compiled.Length); UInt(schr, 12, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(schr.AsSpan(16), 0x100); schr[18] = 1;
        var source = "short count\nbegin ScriptEffectStart\nif count == 0\nset count to 1\nplayer.ModAV Barter 3\n" +
            (failure ? "player.ModAV UnownedActorValue 1\n" : "player.ModAV Science 2\n") + "endif\nend\n" +
            (update ? "begin ScriptEffectUpdate\nplayer.ModAV Barter 9\nend\n" : "");
        var effect = Join(Field("EFID", BitConverter.GetBytes(0x22u)), Field("EFIT", efit));
        var rows = new List<byte[]>
        {
            Record("TES4", 0, Field("HEDR", new byte[12])),
            Record("NPC_", 7, Field("ACBS", acbs), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5]), Field("SPLO", BitConverter.GetBytes(0x21u))),
            Record("RACE", 10),
            Record("SPEL", 0x21, Field("SPIT", spit), effect, duplicate ? effect : []),
            Record("MGEF", 0x22, Field("DATA", mgef)),
            Record("SCPT", 0x30, Field("SCHR", schr), Field("SCDA", compiled), Field("SLSD", local), Field("SCVR", Text("count")),
                Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCRO", BitConverter.GetBytes(7u)), Field("SCTX", Text(source))),
            Setting(0x100, "fAVDSkillPrimaryBonusMult", 2), Setting(0x101, "fAVDSkillLuckBonusMult", .5f), Setting(0x102, "fAVDTagSkillBonus", 15),
        };
        for (var index = 0; index < SkillIds.Length; index++)
        {
            rows.Add(Record("AVIF", (uint)(0x200 + index), Field("EDID", Text("AV" + SkillIds[index])), Field("FULL", Text("Skill " + index))));
            rows.Add(Setting((uint)(0x300 + index), "fAVDSkill" + (SkillIds[index] == "Throwing" ? "Survival" : SkillIds[index]) + "Base", 2));
        }
        return Join(rows.ToArray());
    }
    private static byte[] Setting(uint id, string name, float value) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Join(params byte[][] pieces) => pieces.SelectMany(piece => piece).ToArray();
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
    private static FalloutFormKey Key(uint id) => new(Plugin, id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unowned or malformed active-effect execution was admitted.");
    }
}
