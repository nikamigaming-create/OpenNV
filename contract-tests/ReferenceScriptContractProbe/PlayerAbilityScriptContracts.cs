using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

internal static class PlayerAbilityScriptContracts
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
            CheckOriginalAdmissionStart(directory);
            CheckCompiledExtentMismatch(directory);
            CheckPrefixFailure(directory);
            CheckUnsupportedEvent(directory);
            CheckDeliveredLifecycle(directory);
            CheckEffectShaderLayout(directory);
            Console.WriteLine("OPENNV_PLAYER_ABILITY_SCRIPT_START_PASS sourceReader=true unequalHeaderCounts=true compiledExtentRefused=true effectLocals=true genuinePlayer=true independentEffects=true prefixFailure=true cold=true nonStartRefused=true compiledExecution=unverified parity=unverified");
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
        var coldSkills = Skills(records, coldActor); coldSkills.RestoreValues(savedSkills);
        var coldEffects = Bind(records, coldWorld, coldActor, coldSkills, savedEffects);
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
            skills.CaptureValues().Pools[32].Permanent == 3 && world.InstanceCount == 0,
            "Failed Start lost its actual local/skill prefix or marked the script complete.");
        var prefix = JsonSerializer.Serialize(skills.CaptureValues());
        Reject(effects.Synchronize);
        Require(JsonSerializer.Serialize(skills.CaptureValues()) == prefix, "A retained failed Start replayed its committed prefix.");
        var saved = JsonSerializer.Deserialize<FalloutPlayerAbilityScriptsSnapshot>(JsonSerializer.Serialize(effects.Capture()))!;
        var restored = new FalloutPlayerAbilityScripts(records, skills.SelectedConstantEffects, skills.AbilityCondition, saved);
        restored.BindExecutor((_, _, _, _) => throw new InvalidOperationException("Failed cold Start must not execute."));
        Reject(restored.Synchronize);
    }

    private static void CheckOriginalAdmissionStart(string directory)
    {
        File.WriteAllBytes(Path.Combine(directory, Plugin), Fixture(false));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutPlayerActorValues(records);
        var original = new FalloutNativeCampaignState(FalloutNativeCampaignSave.BeforeAbilityScriptsSchema,
            "authored-source", Key(0x800), "AuthoredQuest", 0, "Authored Player", null!, actor.BaseSpecial,
            [], [], [], [], [], [], [], PlayerActorValues: actor.Capture());
        var receipt = FalloutPlayerAbilitySaveAdmission.FromOriginalHeader(original)!;
        var current = receipt.CompleteOriginalRead(original with { Schema = FalloutNativeCampaignSave.ExpectedSchema }, records);
        var skills = Skills(records, actor);
        skills.RestoreValues(current.PlayerSkillValues!);
        Reject(() => new FalloutPlayerAbilityScripts(records, skills.SelectedConstantEffects, skills.AbilityCondition,
            current.PlayerAbilityScripts));
        var effects = Bind(records, world, actor, skills, current.PlayerAbilityScripts, receipt);
        effects.Synchronize();
        Require(effects.Capture().Effects is [{ Started: true, Generation: 1 }] &&
            skills.ReadSkill("Barter", FalloutActorValueRead.Permanent) == 18,
            "The exact source-proven original admission failed to run its first real ability instance.");
        effects.Synchronize();
        Require(skills.ReadSkill("Barter", FalloutActorValueRead.Permanent) == 18,
            "Original admission reapplied its completed effect prefix.");
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
        FalloutPlayerActorValues actor, FalloutPlayerSkills skills, FalloutPlayerAbilityScriptsSnapshot? restore = null,
        FalloutPlayerAbilitySaveAdmission? originalAdmission = null)
    {
        var effects = new FalloutPlayerAbilityScripts(records, skills.SelectedConstantEffects, skills.AbilityCondition, restore, originalAdmission);
        var executor = new FalloutReferenceScripts(records, world, new FalloutQuestState(records),
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

    private static void CheckDeliveredLifecycle(string directory)
    {
        File.WriteAllBytes(Path.Combine(directory, Plugin), Fixture(false, finish: true));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        var source = FalloutIngestible.ReadWeaponEnchantment(records, Key(0x31));
        var target = Key(0x14);
        var events = 0;
        var order = new List<long>();
        void Execute(FalloutFormKey actor, FalloutPluginRecord script, FalloutGameModeProgram program,
            FalloutScriptEffectLocals locals)
        {
            Require(actor == target && script.FormKey == Key(0x30) && locals.Script == script.FormKey,
                "Delivered lifecycle lost its calling actor or script-local scope.");
            events++;
        }
        var owner = new FalloutDeliveredEffects(records, Execute, null);
        Reject(() => owner.Prepare(target, target, source));
        var refuseFinish = true;
        owner.BindPreflight(batch =>
        {
            Require(batch.All(call => call.State.Target == target && call.Script.FormKey == Key(0x30)), "Delivered preflight lost source context.");
            if (refuseFinish && batch.Any(call => call.Phase == FalloutDeliveredEffectPhase.Finish))
                throw new NotSupportedException("Unowned Finish command.");
        }, _ => FalloutDeliveredTargetStatus.Resident, _ => throw new InvalidOperationException("Fresh delivery required restore."));
        Reject(() => owner.Prepare(target, target, source));
        Require(events == 0 && owner.Capture() is null, "Unowned Finish admitted a Start prefix.");
        refuseFinish = false;
        var apply = owner.Prepare(target, target, source with { Effects = source.Effects.Reverse().ToArray() });
        Require(events == 0, "Preparation invoked Start before contact commit.");
        apply();
        Reject(() => owner.Prepare(target, target, source));
        owner.Advance(1.25);
        var saved = JsonSerializer.Deserialize<FalloutDeliveredEffectsSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
        Require(events == 3 && saved.Active.All(active => active.Elapsed == 1.25) && saved.Active.Select(active => active.Ordinal).SequenceEqual(new[] { 0, 1, 2 }),
            "Start or source ordinal order differed from the retained activation.");
        var cold = new FalloutDeliveredEffects(records, Execute, saved);
        var status = FalloutDeliveredTargetStatus.Unloaded;
        var restored = false;
        cold.BindPreflight(batch =>
        {
            Require(batch.All(call => call.Phase == FalloutDeliveredEffectPhase.Finish), "Cold restore repeated Start.");
            order.AddRange(batch.Select(call => call.State.Generation));
        }, _ => status, active => restored = active.Count == 3);
        Require(restored && events == 3, "Cold bind failed to revalidate presentation or replayed Start.");
        order.Clear();
        status = FalloutDeliveredTargetStatus.Retired;
        Reject(() => cold.Advance(10));
        Require(cold.Capture()!.Active.All(active => active.Elapsed == 1.25), "Unqualified target retirement consumed elapsed state.");
        status = FalloutDeliveredTargetStatus.Unloaded;
        cold.Advance(10);
        cold.Advance(10);
        Require(events == 6 && order.SequenceEqual(new[] { 2L, 3L, 1L }) && cold.Capture()!.Active.Count == 0,
            "Cold unloaded activation lost Finish chronology, a stable tie or exactly-once dispatch.");
        Reject(() => new FalloutDeliveredEffects(records, Execute, saved with
        { Active = [saved.Active[0] with { ScriptHash = new string('0', 64) }] }));
    }

    private static void CheckEffectShaderLayout(string directory)
    {
        var shader = new byte[308]; shader[0] = 1;
        UInt(shader, 24 * 4, 5); UInt(shader, 27 * 4, 6);
        UInt(shader, 33 * 4, BitConverter.SingleToUInt32Bits(3.5f));
        UInt(shader, 47 * 4, 0x00403020); UInt(shader, 50 * 4, BitConverter.SingleToUInt32Bits(.8f));
        UInt(shader, 61 * 4, 0x42); UInt(shader, 69 * 4, 4); UInt(shader, 70 * 4, 2);
        UInt(shader, 76 * 4, BitConverter.SingleToUInt32Bits(1.5f));
        File.WriteAllBytes(Path.Combine(directory, Plugin), Join(Record("TES4", 0, Field("HEDR", new byte[12])),
            Record("DEBR", 0x42), Record("EFSH", 0x43, Field("DATA", shader), Field("ICO2", Text("Effects/Particle.dds"))),
            Record("EFSH", 0x44, Field("DATA", shader[..284])), Record("EFSH", 0x45, Field("DATA", shader[..300])),
            Record("EFSH", 0x46, Field("DATA", shader[..299]))));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        var full = FalloutEffectShader.Read(records, Key(0x43));
        Require(!full.HasMembrane && full.HasParticles && full.ParticleTexture == "textures\\effects\\particle.dds" &&
            full.Particles.Blend.Source == 5 && full.Particles.Blend.Destination == 6 && full.Particles.Lifetime == 3.5f &&
            full.Particles.ColorKeys[0] == new FalloutEffectShaderColorKey(new(0x20, 0x30, 0x40, 0), .8f, 0) &&
            full.Addons.Debris == Key(0x42) && full.Addons.ScaleOutTime == 1.5f && full.TextureColumns == 4 && full.TextureRows == 2,
            "EFSH source flags, particle channels, debris or atlas were decoded at the wrong offsets.");
        Require(FalloutEffectShader.Read(records, Key(0x44)).Addons.FadeIn is null &&
            FalloutEffectShader.Read(records, Key(0x45)).Addons.ScaleOutTime is null,
            "Short EFSH forms invented omitted add-on fields.");
        Reject(() => FalloutEffectShader.Read(records, Key(0x46)));
    }

    private static byte[] Fixture(bool duplicate, bool failure = false, bool update = false, uint? declaredCompiledBytes = null,
        int compiledBytes = 7, bool finish = false)
    {
        var acbs = new byte[24]; acbs[8] = 1;
        var spit = new byte[16]; UInt(spit, 0, 4);
        var mgef = new byte[72]; UInt(mgef, 0, 0x70); UInt(mgef, 8, 0x30); UInt(mgef, 64, 1);
        var efit = new byte[20]; BinaryPrimitives.WriteInt32LittleEndian(efit.AsSpan(16), -1);
        var local = new byte[24]; UInt(local, 0, 1); local[16] = 1;
        // Authored source and its separately retained compiled extent. This
        // source-owner probe makes no bytecode execution acceptance assertion.
        var compiled = new byte[compiledBytes];
        var schr = new byte[20]; UInt(schr, 0, 13); UInt(schr, 4, 2);
        UInt(schr, 8, declaredCompiledBytes ?? (uint)compiled.Length); UInt(schr, 12, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(schr.AsSpan(16), 0x100); schr[18] = 1;
        var source = "short count\nbegin ScriptEffectStart\nif count == 0\nset count to 1\nplayer.ModAV Barter 3\n" +
            (failure ? "player.ModAV UnownedActorValue 1\n" : "player.ModAV Science 2\n") + "endif\nend\n" +
            (update ? "begin ScriptEffectUpdate\nplayer.ModAV Barter 9\nend\n" : "") +
            (finish ? "begin ScriptEffectFinish\nplayer.ModAV Science 1\nend\n" : "");
        var effect = Join(Field("EFID", BitConverter.GetBytes(0x22u)), Field("EFIT", efit));
        var enchantment = new byte[16]; UInt(enchantment, 0, 2);
        var touch = (byte[])efit.Clone(); UInt(touch, 8, 4); UInt(touch, 12, 1);
        var shorterTouch = (byte[])touch.Clone(); UInt(shorterTouch, 8, 2);
        var touchMgef = (byte[])mgef.Clone(); UInt(touchMgef, 16, uint.MaxValue);
        var rows = new List<byte[]>
        {
            Record("TES4", 0, Field("HEDR", new byte[12])),
            Record("NPC_", 7, Field("ACBS", acbs), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5]), Field("SPLO", BitConverter.GetBytes(0x21u))),
            Record("RACE", 10),
            Record("SPEL", 0x21, Field("SPIT", spit), effect, duplicate ? effect : []),
            Record("ENCH", 0x31, Field("ENIT", enchantment), Field("EFID", BitConverter.GetBytes(0x23u)), Field("EFIT", touch),
                Field("EFID", BitConverter.GetBytes(0x24u)), Field("EFIT", shorterTouch), Field("EFID", BitConverter.GetBytes(0x25u)), Field("EFIT", shorterTouch)),
            Record("MGEF", 0x22, Field("DATA", mgef)),
            Record("MGEF", 0x23, Field("DATA", touchMgef)),
            Record("MGEF", 0x24, Field("DATA", touchMgef)),
            Record("MGEF", 0x25, Field("DATA", touchMgef)),
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
