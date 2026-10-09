using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class PlayerAbilitySupplementContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-ability-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Cells.esm"), Fixture());
            using var records = FalloutPluginStack.Load(directory, ["Cells.esm"]);
            CheckOrderedSourceCells(records);
            CheckStrictCurrentAdmission(records, Path.Combine(directory, "current-save.json"));
            Console.WriteLine("OPENNV_PLAYER_ABILITY_CURRENT_AUTHORITY_PASS currentMissingRefused=true originalV49Receipt=true sourceBoundInitialAuthority=true rawInitialPayload=true orderedDuplicates=true firstSlot=true formLow32=true paddingIgnored=true nativeReplay=unverified");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void CheckOrderedSourceCells(FalloutPluginStack records)
    {
        // The actual EffectLocals wrapper must delegate to the complete ordered
        // owner. These fail on the former duplicate-payload dictionary owner.
        var script = records.GetEffective(Key(0x30));
        var locals = new FalloutScriptEffectLocals(script);
        Require(locals.Read("amount").Number == 7.25 && locals.Capture().Count == 2 &&
            locals.Capture()[0].Payload == BitConverter.DoubleToUInt64Bits(7.25) &&
            locals.Capture()[1].Payload == BitConverter.DoubleToUInt64Bits(-13.5),
            "SLSD source payload was zeroed, mistaken for padding or coalesced by slot.");
        locals.Write("amount", 9.5);
        var saved = locals.Capture();
        Require(saved[0].Ordinal == 0 && saved[1].Ordinal == 1 && saved[0].Index == saved[1].Index &&
            saved[0].Payload == BitConverter.DoubleToUInt64Bits(9.5) && saved[1].Payload == BitConverter.DoubleToUInt64Bits(-13.5),
            "A first-slot write mutated the unrelated duplicate tail or lost ordinal identity.");
        var cold = new FalloutScriptEffectLocals(script, saved);
        Require(cold.Read("amount").Number == 9.5 && cold.Capture().SequenceEqual(saved),
            "Cold effect cells lost the complete ordered source/payload state.");
        Reject(() => new FalloutScriptEffectLocals(script, saved.Reverse().ToArray()));
        Reject(() => new FalloutScriptEffectLocals(script, saved.Take(1).ToArray()));
        var changedPadding = new FalloutScriptEffectLocals(records.GetEffective(Key(0x31)));
        Require(changedPadding.Capture().SequenceEqual(new FalloutScriptEffectLocals(script).Capture()),
            "Unused bytes4..7/17..23 changed scalar cell authority.");
        var form = new FalloutScriptEffectLocals(records.GetEffective(Key(0x32)));
        Require(form.Read("target").Kind == FalloutScriptValueKind.Form && form.Read("target").Number == 0x14 &&
            form.Capture().Single().Payload == 0xABCDEF1200000014ul,
            "A form view interpreted UInt64 payload as a floating-point value or discarded high bits before mutation.");
        form.Write("target", FalloutScriptValue.Form(0x20));
        Require(form.Capture().Single().Payload == 0x20, "A form write did not publish a zero-extended identity payload.");
        var signedZero = new FalloutScriptEffectLocals(records.GetEffective(Key(0x33)));
        Require(BitConverter.DoubleToUInt64Bits(signedZero.Read("amount").Number) == 0x8000000000000000ul &&
            signedZero.Capture().Single().Payload == 0x8000000000000000ul, "Initial signed zero bits were lost.");
    }

    private static void CheckStrictCurrentAdmission(FalloutPluginStack records, string path)
    {
        var actor = new FalloutPlayerActorValues(records);
        var original = new FalloutNativeCampaignState(FalloutNativeCampaignSave.BeforeAbilityScriptsSchema,
            "authored-source", Key(0x800), "AuthoredQuest", 0, "Authored Player", null!, actor.BaseSpecial,
            [], [], [], [], [], [], [], PlayerActorValues: actor.Capture(),
            TagSkillSlots: FalloutPlayerTagSkills.FromLegacy([]), FactionRelations: []);
        var receipt = FalloutPlayerAbilitySaveAdmission.FromOriginalHeader(original) ??
            throw new InvalidOperationException("A genuine prior-schema input did not obtain its read receipt.");
        var missing = original with { Schema = FalloutNativeCampaignSave.ExpectedSchema };
        Require(FalloutPlayerAbilitySaveAdmission.FromOriginalHeader(missing) is null,
            "A malformed current save fabricated a pre-owner admission receipt.");
        Reject(() => FalloutPlayerAbilitySaveAdmission.Require(missing), "Current campaign save is missing");
        FalloutPlayerAbilitySaveAdmission.Require(missing, receipt);
        var current = receipt.CompleteOriginalRead(missing, records);
        FalloutPlayerAbilitySaveAdmission.Require(current);
        Require(receipt.PermitsInitialEffects(current.PlayerAbilityScripts!) &&
            !receipt.PermitsInitialEffects(current.PlayerAbilityScripts! with { }),
            "Original read admission escaped its exact source-published initial effect owner.");
        Require(current.PlayerSkillValues is { Pools.Count: 0, Sources.Count: 0 } &&
            current.PlayerAbilityScripts is { Effects.Count: 0, LastGeneration: 0 } &&
            current.PlayerSkillValues.PlayerSha256 == actor.Source.PlayerSha256 &&
            current.PlayerAbilityScripts.PlayerSha256 == actor.Source.PlayerSha256,
            "Source-proven absent owners did not become actual source-bound initial authorities before current publication.");
        Reject(() => FalloutPlayerAbilitySaveAdmission.Require(current with { PlayerSkillValues = null }));
        Reject(() => FalloutPlayerAbilitySaveAdmission.Require(current with { PlayerAbilityScripts = null }));
        Reject(() => FalloutPlayerAbilitySaveAdmission.Require(current with { PlayerSkillValues = null, PlayerAbilityScripts = null }));
        Reject(() => FalloutPlayerAbilitySaveAdmission.FromOriginalHeader(original with { PlayerSkillValues = current.PlayerSkillValues }));
        Reject(() => FalloutPlayerAbilitySaveAdmission.Require(missing with { PlayerName = "Different" }, receipt));
        // Exercise the real Save.Write root hook, not only the new helper. The
        // The fixture supplies the existing current-schema authorities so that
        // this check reaches the new authority rejection at its normal position.
        Reject(() => FalloutNativeCampaignSave.Write(path, missing), "Current campaign save is missing");
        Require(!File.Exists(path), "Rejected current authority produced a save file.");
    }

    private static byte[] Fixture()
    {
        var acbs = new byte[24]; acbs[8] = 1;
        return Join(Record("TES4", 0, Field("HEDR", new byte[12])),
            Record("NPC_", 7, Field("ACBS", acbs), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])),
            NumericScript(0x30, 0x11, 0x22), NumericScript(0x31, 0xAA, 0xCC),
            Record("SCPT", 0x32, Local(1, "target", 0, 0xABCDEF1200000014ul, 0x44), Field("SCTX", Text("ref target"))),
            Record("SCPT", 0x33, Local(1, "amount", 0, 0x8000000000000000ul, 0x33), Field("SCTX", Text("float amount"))));
    }
    private static byte[] NumericScript(uint id, byte firstPadding, byte secondPadding) => Record("SCPT", id,
        Local(1, "amount", 0, BitConverter.DoubleToUInt64Bits(7.25), firstPadding),
        Local(1, "amount", 0, BitConverter.DoubleToUInt64Bits(-13.5), secondPadding), Field("SCTX", Text("float amount")));
    private static byte[] Local(uint index, string name, byte flags, ulong payload, byte padding)
    {
        var bytes = Enumerable.Repeat(padding, 24).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, index);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), payload); bytes[16] = flags;
        return Join(Field("SLSD", bytes), Field("SCVR", Text(name)));
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
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
    private static FalloutFormKey Key(uint id) => new("Cells.esm", id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string? message = null)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            if (message is not null && !error.Message.Contains(message, StringComparison.Ordinal)) throw;
            return;
        }
        throw new InvalidOperationException("Invalid current authority or ordered local state was admitted.");
    }
}
