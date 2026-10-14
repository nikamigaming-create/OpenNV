using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class NativeBookSoundOperandContracts
{
    private static readonly FalloutSpecialBookPageDirection Forward = FalloutSpecialBookPageDirection.Forward;
    private static readonly FalloutSpecialBookPageDirection Backward = FalloutSpecialBookPageDirection.Backward;
    internal static FalloutSpecialBookPageSoundsSource AuthoredSource() => new("authored-book-source", new('A', 64), 900,
        new(1000, 1100, 72, 3, 5000, [1500, 1600, 1700, 1800, 1900, 2000, 1900],
            new(FalloutSpecialBookPageDirection.Forward, 1200, 6000, "AuthoredForward", 0x121),
            new(FalloutSpecialBookPageDirection.Backward, 1300, 6100, "AuthoredBackward", 0x121)));
    private static FalloutSourceCStringInputInvocation Call(ulong callback, ulong module = 9, uint thread = 7, ulong parent = 11)
        => new(3, 5, module, thread, parent, callback);
    private static byte[] Push(uint pointer)
    { var bytes = new byte[5]; bytes[0] = 0x68; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), pointer); return bytes; }
    internal static void Run()
    {
        var source = AuthoredSource(); var owner = new object(); var current = true;
        var native = new Dictionary<FalloutSpecialBookPageDirection, string> { [Forward] = source.Forward.EditorId, [Backward] = source.Backward.EditorId };
        var fields = new FalloutSpecialBookPageSounds(source, () => { if (!current) throw new InvalidOperationException("Scene retired."); }, () => { });
        Require(!fields.HasNativeLifetime, "unbound source-only input cannot create a native cold-save blocker");
        fields.Bind(owner, () => { }, direction => native[direction]);
        Require(fields.HasNativeLifetime, "actual shared-domain binding creates the independent native lifetime");
        using (var consumer = fields.BorrowConsumer())
        {
            Require(fields.ActiveConsumers == 1 && consumer.ReadCurrent(1).EditorId == source.Forward.EditorId &&
                consumer.ReadCurrent(-1).EditorId == source.Backward.EditorId, "independent actual consumer fields");
            Reject<InvalidOperationException>(() => fields.RequireRetirement(owner));
        }
        Require(fields.ActiveConsumers == 0, "actual consumer release");
        Complete(fields.Field(Forward), source.Forward.EditorId, 8000, 1);
        Require(fields.ReadCurrent(1).Pointer == 8000 && fields.ReadCurrent(-1).Pointer == source.Backward.Pointer && fields.Field(Backward).Events.Count == 0,
            "first returned operand cannot synthesize a second write, even if later names match");
        Complete(fields.Field(Backward), source.Backward.EditorId, 8100, 5);
        foreach (var direction in new[] { Forward, Backward })
        {
            var slot = fields.Field(direction); var json = JsonSerializer.Serialize(slot.Events);
            var restored = JsonSerializer.Deserialize<FalloutSourceCStringInputEvent[]>(json)!;
            Require(restored.Length == 4 && restored.SequenceEqual(slot.Events) &&
                restored.Select(row => row.Operation).SequenceEqual(["protect-enter", "protect-return", "store-observed", "restore-return"]),
                "JSON preserves each real partial store and SDK output history; it does not admit native cold restore");
        }
        Require(fields.ReadCurrent(1).EditorId == source.Forward.EditorId && fields.ReadCurrent(-1).EditorId == source.Backward.EditorId,
            "equal text still requires each changed pointer and complete restoration");
        Reject<NotSupportedException>(fields.RequireColdOwned);
        current = false; Reject<InvalidOperationException>(() => fields.ReadCurrent(1)); current = true;
        native[Backward] = "UnownedBufferWriter"; Reject<NotSupportedException>(() => fields.ReadCurrent(-1)); native[Backward] = source.Backward.EditorId;
        Require(Task.Run(() => { try { fields.ReadCurrent(1); } catch (InvalidOperationException) { return true; } return false; }).GetAwaiter().GetResult(),
            "foreign product thread rejected");
        fields.RequireRetirement(owner); fields.Field(Backward).Retired(owner, Call(9)); fields.Field(Forward).Retired(owner, Call(10));
        fields.ClosedAfterChildExit(owner); Reject<InvalidOperationException>(() => fields.ReadCurrent(1));

        foreach (var bad in new Action<FalloutSourceCStringInput>[]
        {
            field => field.EnterProtect(new object(), Call(1), source.Input(Forward).Preimage.AsSpan(), 8),
            field => field.EnterProtect(owner, Call(0), source.Input(Forward).Preimage.AsSpan(), 8),
            field => field.EnterProtect(owner, Call(1), source.Input(Backward).Preimage.AsSpan(), 8),
            field => field.EnterProtect(owner, Call(1), Push(9000), 8),
            field => field.EnterProtect(owner, Call(1), new byte[4], 8)
        })
        {
            var field = New(); Reject<InvalidDataException>(() => bad(field));
            Require(field.Phase == FalloutSourceCStringInputPhase.Bound && field.Pointer == source.Forward.Pointer, "invalid entry preserves original word");
        }
        foreach (var bad in new[] { Call(2, module: 10), Call(2, thread: 8), Call(2, parent: 12), Call(1) })
        {
            var field = New(); field.EnterProtect(owner, Call(1), source.Input(Forward).Preimage.AsSpan(), 8);
            Reject<InvalidDataException>(() => field.Protected(owner, bad, 1, 0, 8, 0x20));
            Require(field.Phase == FalloutSourceCStringInputPhase.ProtectEntered, "foreign return preserves entered prefix");
        }
        var firstFailed = New(); firstFailed.EnterProtect(owner, Call(1), source.Input(Forward).Preimage.AsSpan(), 0x77);
        firstFailed.Protected(owner, Call(2), 0, 5, 0x77, 0x77); firstFailed.Fail(owner, Call(2), "Original store raised an actual child fault.");
        Require(firstFailed.Pointer == source.Forward.Pointer && firstFailed.Events[1].Result == 0 && firstFailed.Events[1].OldAfter == 0x77,
            "failed SDK and subsequent real store fault retain actual unchanged output and pointer");
        var failedRestore = New(); failedRestore.EnterProtect(owner, Call(1), source.Input(Forward).Preimage.AsSpan(), 8);
        failedRestore.Protected(owner, Call(2), 1, 0, 8, 0x20); failedRestore.ObserveStore(owner, Call(3), Push(8000), 8000, source.Forward.EditorId);
        failedRestore.Restored(owner, Call(4), 0x20, 0, 5, 0x20, 0x20);
        Require(failedRestore.Pointer == 8000 && failedRestore.Phase == FalloutSourceCStringInputPhase.Failed && failedRestore.Events[^2].Result == 0,
            "failed restore preserves the already committed DWORD and failed SDK receipt");
        Reject<InvalidOperationException>(() => failedRestore.RequireRetirement(owner));
        var missingRestore = New(); missingRestore.EnterProtect(owner, Call(1), source.Input(Forward).Preimage.AsSpan(), 8);
        missingRestore.Protected(owner, Call(2), 1, 0, 8, 0x20); missingRestore.ObserveStore(owner, Call(3), Push(8000), 8000, source.Forward.EditorId);
        Reject<InvalidOperationException>(() => missingRestore.ReadCurrentValue()); Reject<InvalidOperationException>(() => missingRestore.RequireRetirement(owner));
        LiveRecords();
        Console.WriteLine("OPENNV_BOOK_SOUND_OPERAND_CONTRACT_PASS independentFields=true equalTextPointerStores=true exactInvocation=true partialSdkStores=true " +
            "currentWinningRecords=true liveSoundPath=true wrongOwnerRefused=true consumerLifetime=true jsonReceipts=true nativeColdRefused=true nativeCalls=UNEXECUTED");

        FalloutSourceCStringInput New()
        { var value = new FalloutSourceCStringInput(source.Input(Forward), () => { }, () => { }); value.Bind(owner, () => { }, () => source.Forward.EditorId); return value; }
        void Complete(FalloutSourceCStringInput field, string text, uint pointer, ulong first)
        {
            field.EnterProtect(owner, Call(first), field.Source.Preimage.AsSpan(), 8); field.Protected(owner, Call(first + 1), 1, 19, 8, 0x20);
            field.ObserveStore(owner, Call(first + 2), Push(pointer), pointer, text); field.Restored(owner, Call(first + 3), 0x20, 1, 29, 0x20, 0x40);
        }
    }
    private static void LiveRecords()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-book-sound-input-");
        var basePath = Path.Combine(directory.FullName, "BookBase.esm"); var patchPath = Path.Combine(directory.FullName, "BookPatch.esp");
        var baseBytes = Join(Record("TES4", 0, Field("HEDR", Join(BitConverter.GetBytes(1.34f), new byte[8]))),
            Sound(0x810, "AuthoredForward", "ui/base-forward.wav"), Sound(0x811, "AuthoredBackward", "ui/backward.wav"));
        var patchBytes = Join(Record("TES4", 0, Field("HEDR", Join(BitConverter.GetBytes(1.34f), new byte[8])),
            Field("MAST", Encoding.ASCII.GetBytes("BookBase.esm\0")), Field("DATA", new byte[8])),
            Sound(0x810, "AuthoredForward", "ui/winning-forward.wav"));
        File.WriteAllBytes(basePath, baseBytes); File.WriteAllBytes(patchPath, patchBytes);
        try
        {
            using var records = FalloutPluginStack.Load(directory.FullName, ["BookBase.esm", "BookPatch.esp"]);
            using var foreign = FalloutPluginStack.Load(directory.FullName, ["BookBase.esm", "BookPatch.esp"]);
            var field = new FalloutSpecialBookPageSounds(AuthoredSource(), () => { }, () => { }, records);
            using var consumer = field.BorrowConsumer();
            var forward = consumer.ResolveCurrent(1, records); var back = consumer.ResolveCurrent(-1, records);
            Require(forward.FormKey == new FalloutFormKey("BookBase.esm", 0x810) && forward.LogicalPath.EndsWith("winning-forward.wav", StringComparison.Ordinal) &&
                back.FormKey == new FalloutFormKey("BookBase.esm", 0x811), "actual full reader uses current winning SOUN for each direction");
            records.SoundPaths.Set(forward.FormKey, "ui/current-forward.wav");
            Require(consumer.ResolveCurrent(1, records).LogicalPath.EndsWith("current-forward.wav", StringComparison.Ordinal) &&
                consumer.ResolveCurrent(-1, records).LogicalPath == back.LogicalPath, "current sound path mutation is neither cached nor copied to its sibling");
            Reject<InvalidDataException>(() => consumer.ResolveCurrent(1, foreign));
            Require(SHA256.HashData(File.ReadAllBytes(basePath)).AsSpan().SequenceEqual(SHA256.HashData(baseBytes)) &&
                SHA256.HashData(File.ReadAllBytes(patchPath)).AsSpan().SequenceEqual(SHA256.HashData(patchBytes)), "authored input files unchanged");
        }
        finally { Directory.Delete(directory.FullName, true); }
    }
    private static byte[] Sound(uint id, string name, string path)
    {
        var data = new byte[36]; BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)FalloutSoundFlags.MenuSound);
        for (var index = 0; index < 5; ++index) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12 + index * 2), 100);
        return Record("SOUN", id, Field("EDID", Encoding.ASCII.GetBytes(name + '\0')), Field("FNAM", Encoding.ASCII.GetBytes(path + '\0')), Field("SNDD", data));
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), data.Length); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id); return Join(header, data);
    }
    private static byte[] Field(string signature, byte[] data) => Join(Encoding.ASCII.GetBytes(signature), BitConverter.GetBytes(checked((ushort)data.Length)), data);
    private static byte[] Join(params byte[][] pieces) => pieces.SelectMany(piece => piece).ToArray();
    internal static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    internal static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Book input admitted an invalid " + typeof(T).Name + " case."); }
}
