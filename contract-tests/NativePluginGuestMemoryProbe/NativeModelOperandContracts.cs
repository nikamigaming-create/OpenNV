using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Content;

internal static class NativeModelOperandContracts
{
    private static FalloutRaceMenuModelPathSource AuthoredSource() => FalloutRaceMenuModelPathSource.FromSelection(
        "authored-model-source", new string('A', 64), 1000, new("meshes/devices/authored.nif", new(100, 2000, 144, 300), [0, 1]));
    private static FalloutRaceMenuModelPathInvocation Call(ulong callback, ulong module = 9, uint thread = 7, ulong parent = 11)
        => new(3, 5, module, thread, parent, callback);
    private static byte[] Stored(uint pointer)
    { var bytes = new byte[5]; bytes[0] = 0x68; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), pointer); return bytes; }
    internal static void Run()
    {
        var source = AuthoredSource(); var owner = new object(); var current = true; var native = source.Model;
        var field = new FalloutRaceMenuModelPath(source, () => { if (!current) throw new InvalidOperationException("Actual authored scene retired."); }, () => { });
        field.Bind(owner, () => { }, () => native);
        using (var consumer = field.BorrowConsumer())
        {
            Require(consumer.Model == source.Model && consumer.Pointer == source.Pointer && field.ActiveConsumers == 1, "actual consumer snapshot/lifetime");
            Reject<InvalidOperationException>(() => field.RequireRetirement(owner));
        }
        Require(field.ActiveConsumers == 0, "returned consumer retirement");
        field.EnterProtect(owner, Call(1), source.Preimage.AsSpan(), 0x1234);
        Reject<InvalidOperationException>(() => field.ReadCurrentModel());
        field.Protected(owner, Call(2), 1, 19, 0x1234, 0x20);
        field.ObserveStore(owner, Call(3), Stored(4000), 4000, source.Model);
        field.Restored(owner, Call(4), 0x20, 1, 29, 0x20, 0x40);
        Require(field.Pointer == 4000 && field.ReadCurrentModel() == source.Model &&
            field.Events.Select(row => row.Operation).SequenceEqual(new[] { "protect-enter", "protect-return", "store-observed", "restore-return" }),
            "equal path still records the actual changed pointer and complete SDK/store order without invented flush");
        native = "meshes/devices/foreign.nif"; Reject<NotSupportedException>(() => field.ReadCurrentModel()); native = source.Model;
        current = false; Reject<InvalidOperationException>(() => field.ReadCurrentModel()); current = true;
        Reject<NotSupportedException>(field.RequireColdOwned);
        field.RequireRetirement(owner); field.Retired(owner, Call(5)); Reject<InvalidOperationException>(() => field.ReadCurrentModel());
        field.ClosedAfterChildExit(owner); Require(field.Phase == FalloutRaceMenuModelPathPhase.Closed, "separate fresh-child closure");

        foreach (var mutation in new Action<FalloutRaceMenuModelPath>[]
        {
            value => value.EnterProtect(new object(), Call(1), source.Preimage.AsSpan(), 8),
            value => value.EnterProtect(owner, Call(0), source.Preimage.AsSpan(), 8),
            value => value.EnterProtect(owner, Call(1), Stored(3000), 8),
            value => value.EnterProtect(owner, Call(1), new byte[] { 0x6a, 1, 2, 3 }, 8),
        })
        {
            var invalid = New(); Reject<InvalidDataException>(() => mutation(invalid));
            Require(invalid.Phase == FalloutRaceMenuModelPathPhase.Bound, "invalid entry does not overwrite the original field");
        }
        foreach (var changed in new[] { Call(2, module: 10), Call(2, thread: 8), Call(2, parent: 12), Call(1) })
        {
            var invalid = New(); invalid.EnterProtect(owner, Call(1), source.Preimage.AsSpan(), 8);
            Reject<InvalidDataException>(() => invalid.Protected(owner, changed, 1, 0, 8, 0x20));
            Require(invalid.Phase == FalloutRaceMenuModelPathPhase.ProtectEntered, "foreign invocation preserves the entered prefix");
        }
        var failed = New(); failed.EnterProtect(owner, Call(1), source.Preimage.AsSpan(), 8);
        failed.Protected(owner, Call(2), 1, 0, 8, 0x20); failed.ObserveStore(owner, Call(3), Stored(4000), 4000, source.Model);
        failed.Restored(owner, Call(4), 0x20, 0, 5, 0x20, 0x20);
        Require(failed.Pointer == 4000 && failed.Phase == FalloutRaceMenuModelPathPhase.Failed && failed.Events[^2].Result == 0,
            "failed restoration preserves its already stored word and actual output/error");
        Reject<InvalidOperationException>(() => failed.ReadCurrentModel()); Reject<InvalidOperationException>(() => failed.RequireRetirement(owner));
        var firstFailed = New(); firstFailed.EnterProtect(owner, Call(1), source.Preimage.AsSpan(), 0x77);
        firstFailed.Protected(owner, Call(2), 0, 5, 0x77, 0x77); firstFailed.Fail(owner, Call(2), "Actual original store fault.");
        Require(firstFailed.Pointer == source.Pointer && firstFailed.Events[1].OldAfter == 0x77 && firstFailed.Events[1].Result == 0,
            "failed first SDK followed by real store fault retains the unchanged field and output word");
        var drift = New(); drift.EnterProtect(owner, Call(1), source.Preimage.AsSpan(), 8);
        Reject<InvalidDataException>(() => drift.Protected(owner, Call(2), 1, 0, 9, 0x20));
        Console.WriteLine("OPENNV_MODEL_OPERAND_CONTRACT_PASS sourcePreimage=true exactInvocation=true realSdkObservations=true partialStores=true " +
            "equalTextChangesPointer=true consumerLifetime=true failedRestoreRetained=true coldRefused=true nativeCalls=UNEXECUTED");
        FalloutRaceMenuModelPath New()
        {
            var value = new FalloutRaceMenuModelPath(source, () => { }, () => { }); value.Bind(owner, () => { }, () => source.Model); return value;
        }
    }
    internal static void RunOwnedSource(string runtime, string plugin)
    {
        var before = new[] { runtime, plugin }.ToDictionary(Path.GetFullPath, Hash, StringComparer.OrdinalIgnoreCase);
        var model = FalloutRaceMenuModelPathSource.Read(runtime);
        var image = NativePluginImageDeclarations.Read(plugin, dll: true);
        var source = NativePluginModuleModelPathSource.Read(image, model) ?? throw new InvalidDataException("The actual selected model producer is absent.");
        Require(model.Preimage.Length == 5 && model.Preimage[0] == 0x68 && model.Operand == model.Instruction + 1 &&
            model.Pointer == BinaryPrimitives.ReadUInt32LittleEndian(model.Preimage.AsSpan()[1..]) && source.ProtectReturnRva < source.RestoreReturnRva &&
            source.DefaultModel == model.Model && source.DefaultModel != source.AlternateModel && source.Capacity == source.InitialBytes.Length,
            "selected command/constructor/input and real module buffer/writer relationships");
        Require(FalloutExecutableStringTable.ReadRaceMenuModelPathBytes(source.InitialBytes.AsSpan()) == model.Model &&
            source.MappedWriterCode(0x20000000).Length == 47, "source string and exact imported writer extent");
        source.Check(image); Reject<InvalidDataException>(() => source.Check(image with { Imports = [] }));
        foreach (var row in before) Require(Hash(row.Key) == row.Value, "unchanged selected input");
        Console.WriteLine("OPENNV_MODEL_OPERAND_SOURCE_PASS selectedCommandModes=true constructorInputOnly=true sourceBuffer=true sourceWriter=true " +
            "sameTextDifferentPointerRequired=true ownedInputsUnchanged=true receiverAndNativeCalls=UNEXECUTED");
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidDataException("The invalid model input contract was admitted.");
    }
}
