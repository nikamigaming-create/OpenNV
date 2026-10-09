using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    // This exercises the actual native result dispatch boundary. It does not
    // claim voice selection, mixer output, campaign input or retail parity.
    private void ExerciseCompiledResultAuthority()
    {
        GD.Print($"OPENNV_NATIVE_COMPILED_RESULT_AUTHORITY_ASSEMBLY mvid={GetType().Module.ModuleVersionId} location={GetType().Assembly.Location}");
        var directory = Path.Combine(Path.GetTempPath(), "opennv-native-scda-result-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RuntimeNativeSpeech? speech = null;
        try
        {
            var path = Path.Combine(directory, "NativeBytecode.esm");
            var assignment = AuthoritySet(1, " 17");
            var failed = AuthorityJoin(AuthoritySet(1, " 31"), AuthorityInstruction(0x2f03), AuthoritySet(2, " 99"));
            var malformed = AuthorityJoin(AuthoritySet(1, " 43"), [0x15, 0, 16, 0, 0, 0]);
            byte[] Scope(byte[] code) => AuthorityJoin(AuthorityField("SCHR", AuthorityHeader(code, 1, 0, 0)),
                AuthorityField("SCDA", code), AuthorityField("SCRO", BitConverter.GetBytes(0x20u)));
            byte[] Info(uint id, params byte[][] fields) => AuthorityRecord("INFO", id,
                AuthorityField("DATA", [0, 0, 0, 0]), AuthorityField("QSTI", BitConverter.GetBytes(0x20u)), AuthorityJoin(fields));
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var standalone = AuthorityInstruction(0x1d);
            File.WriteAllBytes(path, AuthorityJoin(AuthorityRecord("TES4", 0, AuthorityField("HEDR", header)),
                AuthorityRecord("SCPT", 0x30, AuthorityField("SCHR", AuthorityHeader(standalone, 0, 2, 1)),
                    AuthorityField("SCDA", standalone), AuthorityLocal(1, "value"), AuthorityLocal(2, "suffix")),
                AuthorityRecord("QUST", 0x20, AuthorityField("DATA", new byte[8]), AuthorityField("SCRI", BitConverter.GetBytes(0x30u))),
                Info(0x40, Scope(assignment), AuthorityField("NEXT", []), Scope([]), AuthorityField("SCTX", [65, 0, 66, 0])),
                Info(0x41, Scope(failed)), Info(0x42, Scope(malformed))));
            var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["NativeBytecode.esm"]);
            using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Numeric native compiled result dispatched an effect.")));
            speech = new RuntimeNativeSpeech(); AddChild(speech);
            speech.ExecuteOwnedResults = scripts.ExecuteResultOwned;
            var method = typeof(RuntimeNativeSpeech).GetMethod("RunResults", BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException("Native result dispatch owner is absent.");
            FalloutScriptResultReceipt Invoke(FalloutDialogueInfo info, bool begin)
            {
                try
                {
                    return (FalloutScriptResultReceipt?)method.Invoke(speech, [info, AuthorityKey(0x20), begin]) ??
                    throw new InvalidDataException("Native result dispatch returned no owned receipt.");
                }
                catch (TargetInvocationException error) when (error.InnerException is { } actual)
                { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
            }
            var info = FalloutDialogueTopic.Decode(records.GetEffective(AuthorityKey(0x40)));
            AuthorityRequire(info.BeginScript.Length == 0 && info.EndScript.Length == 0,
                "Native compiled scope parsed diagnostic source during admission.");
            var first = Invoke(info, true); var second = Invoke(info, false);
            AuthorityRequire(first is { Authority: FalloutScriptResultAuthority.CompiledVanilla, CommittedSteps: 1, Completed: true } &&
                second is { Authority: FalloutScriptResultAuthority.CompiledVanilla, CommittedSteps: 0, Completed: true } &&
                first.Invocation > 0 && second.Invocation > first.Invocation && quests.Variable(AuthorityKey(0x20), 1) == 17,
                "Native RunResults skipped compiled-only/empty SCDA or invented a receipt.");
            var savedQuests = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!;
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(savedQuests);
            JsonSerializer.Deserialize<FalloutScriptResultReceipt>(JsonSerializer.Serialize(first))!
                .Require(FalloutScriptScope.Dialogue(info.Record, true), AuthorityKey(0x20));
            AuthorityRequire(coldQuests.Variable(AuthorityKey(0x20), 1) == 17, "Typed cold state lost the native result effect.");
            var opaqueCalls = 0;
            speech.ExecuteOwnedResults = null; speech.ExecuteResults = (_, _, _) => ++opaqueCalls;
            AuthorityReject(() => Invoke(info, true));
            AuthorityRequire(opaqueCalls == 0 && quests.Variable(AuthorityKey(0x20), 1) == 17,
                "Opaque source callback admitted compiled authority or mutated state.");
            speech.ExecuteOwnedResults = scripts.ExecuteResultOwned;
            var before = JsonSerializer.Serialize(quests.Capture());
            AuthorityReject(() => Invoke(FalloutDialogueTopic.Decode(records.GetEffective(AuthorityKey(0x42))), true));
            AuthorityRequire(before == JsonSerializer.Serialize(quests.Capture()) && world.ScriptManualSaves.EnteredInvocations == 0,
                "Native structural refusal committed state or leaked its invocation lease.");
            var failure = FalloutDialogueTopic.Decode(records.GetEffective(AuthorityKey(0x41)));
            AuthorityReject(() => Invoke(failure, true));
            AuthorityRequire(quests.Variable(AuthorityKey(0x20), 1) == 31 && quests.Variable(AuthorityKey(0x20), 2) == 0 &&
                world.ScriptManualSaves.EnteredInvocations == 0,
                "Native reached refusal lost its actual prefix or executed the suffix.");
            AuthorityReject(() => Invoke(failure, true));
            AuthorityRequire(quests.Variable(AuthorityKey(0x20), 1) == 31 && quests.Variable(AuthorityKey(0x20), 2) == 0,
                "Native retry replayed the failed result prefix.");
            AuthorityReject(() => world.Capture());
            AuthorityRequire(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), "Fixture source changed.");
            speech.Free();
            AuthorityRequire(!GodotObject.IsInstanceValid(speech), "Actual native result owner survived original-owner Free.");
            speech = null;
            GD.Print("OPENNV_NATIVE_COMPILED_RESULT_AUTHORITY_PASS compiledOnlyRunResults=true emptySCDAOwned=true " +
                "realSharedReceipt=true typedColdEffect=true opaqueCallbackRefused=true structuralAtomic=true " +
                "reachedPrefixRetained=true failedColdCaptureRefused=true nativeOwnerRetired=true voiceAndInput=false");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(speech)) speech!.Free();
            Directory.Delete(directory, true);
        }
    }

    private static void AuthorityRequire(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void AuthorityReject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Native compiled result boundary accepted an unowned operation.");
    }
    private static FalloutFormKey AuthorityKey(uint id) => new("NativeBytecode.esm", id);
    private static byte[] AuthorityLocal(uint index, string name)
    {
        var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, index);
        return AuthorityJoin(AuthorityField("SLSD", local), AuthorityField("SCVR", Encoding.ASCII.GetBytes(name + '\0')));
    }
    private static byte[] AuthorityHeader(byte[] code, uint references, uint locals, ushort type)
    {
        var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), references);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)code.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), locals);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), type); header[18] = 1; return header;
    }
    private static byte[] AuthoritySet(ushort slot, string literal)
    {
        var value = Encoding.ASCII.GetBytes(literal);
        return AuthorityInstruction(0x15, AuthorityJoin([(byte)'r'], BitConverter.GetBytes((ushort)1), [(byte)'f'],
            BitConverter.GetBytes(slot), BitConverter.GetBytes((ushort)value.Length), value));
    }
    private static byte[] AuthorityInstruction(ushort opcode, byte[]? payload = null) =>
        AuthorityJoin(BitConverter.GetBytes(opcode), BitConverter.GetBytes((ushort)(payload?.Length ?? 0)), payload ?? []);
    private static byte[] AuthorityJoin(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] AuthorityField(string signature, byte[] bytes)
    {
        var result = new byte[6 + bytes.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), (ushort)bytes.Length); bytes.CopyTo(result, 6); return result;
    }
    private static byte[] AuthorityRecord(string signature, uint id, params byte[][] fields)
    {
        var bytes = AuthorityJoin(fields); var result = new byte[24 + bytes.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); bytes.CopyTo(result, 24); return result;
    }
}
