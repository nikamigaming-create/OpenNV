using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using static NativeBookSoundOperandContracts;

internal static class NativeBookFocusSoundContracts
{
    private static readonly FalloutSpecialBookFocusDirection Previous = FalloutSpecialBookFocusDirection.Previous, Next = FalloutSpecialBookFocusDirection.Next;
    internal static FalloutSpecialBookFocusSoundsSource Focus(FalloutSpecialBookPageSoundsSource page) => new(page.RuntimePath, page.RuntimeSha256, page.ClassTable,
        new(page.Selection.Click, page.Selection.PageField, 8, 76, 1, 7, 2100,
            [page.Selection.SoundChildren[1], page.Selection.SoundChildren[2], page.Selection.SoundChildren[5], page.Selection.SoundChildren[4]],
            new(FalloutSpecialBookFocusDirection.Previous, 1400, 6200, "AuthoredFocusPrevious", 0x121),
            new(FalloutSpecialBookFocusDirection.Next, 1450, 6300, "AuthoredFocusNext", 0x121)));
    internal static void Run()
    {
        var page = AuthoredSource(); var focus = Focus(page); var owner = new object(); var current = true;
        var native = new Dictionary<FalloutSpecialBookFocusDirection, string> { [Previous] = focus.Selection.Previous.EditorId, [Next] = focus.Selection.Next.EditorId };
        var fields = new FalloutSpecialBookPageSounds(page, () => { if (!current) throw new InvalidOperationException("Product retired."); }, () => { }, focusSource: focus);
        Require(!fields.HasNativeLifetime, "source-only focus creates no native cold blocker");
        Reject<InvalidDataException>(() => fields.Bind(owner, () => { }, d => page.For(d).EditorId));
        Require(fields.Field(FalloutSpecialBookPageDirection.Forward).Phase == FalloutSourceCStringInputPhase.Source,
            "missing focus getter does not partially bind the page fields");
        fields.Bind(owner, () => { }, d => page.For(d).EditorId, d => native[d]);
        using (var consumer = fields.BorrowConsumer())
        {
            Require(fields.ActiveConsumers == 1 && fields.FocusField(Previous).ActiveConsumers == 1 && fields.FocusField(Next).ActiveConsumers == 1 &&
                fields.Field(FalloutSpecialBookPageDirection.Forward).ActiveConsumers == 1 && fields.Field(FalloutSpecialBookPageDirection.Backward).ActiveConsumers == 1,
                "one actual menu lease retains all four independent inputs");
            Reject<InvalidOperationException>(() => fields.RequireRetirement(owner));
        }
        var previous = fields.FocusField(Previous); Complete(previous, 8200, 1);
        Require(fields.ReadFocusCurrent(-1).Pointer == 8200 && fields.ReadFocusCurrent(1).Pointer == focus.Selection.Next.Pointer &&
            fields.FocusField(Next).Events.Count == 0 && fields.Field(FalloutSpecialBookPageDirection.Forward).Events.Count == 0,
            "equal source text first store does not synthesize its sibling or overwrite page receipts");
        var json = JsonSerializer.Serialize(previous.Events);
        Require(JsonSerializer.Deserialize<FalloutSourceCStringInputEvent[]>(json)!.SequenceEqual(previous.Events), "focus partial receipts preserve exact JSON values");
        native[Previous] = "UnownedCStringWriter"; Reject<NotSupportedException>(() => fields.ReadFocusCurrent(-1)); native[Previous] = focus.Selection.Previous.EditorId;
        current = false; Reject<InvalidOperationException>(() => fields.ReadFocusCurrent(-1)); current = true;
        var next = fields.FocusField(Next); next.EnterProtect(owner, Call(5), next.Source.Preimage.AsSpan(), 8);
        next.Protected(owner, Call(6), 1, 0, 8, 0x20); next.ObserveStore(owner, Call(7), Push(8300), 8300, native[Next]);
        next.Restored(owner, Call(8), 0x20, 0, 5, 0x20, 0x20);
        Require(next.Pointer == 8300 && next.Phase == FalloutSourceCStringInputPhase.Failed && previous.Phase == FalloutSourceCStringInputPhase.Returned &&
            previous.Events.Count == 4, "failed next restoration preserves both earlier returned input and its own actual committed word");
        Reject<InvalidOperationException>(() => fields.RequireRetirement(owner)); Reject<NotSupportedException>(fields.RequireColdOwned);
        foreach (var changed in new[] { Call(2, 10), Call(2, thread: 8), Call(2, parent: 12), Call(1) })
        {
            var candidate = new FalloutSourceCStringInput(focus.Input(Previous), () => { }, () => { }); candidate.Bind(owner, () => { }, () => native[Previous]);
            candidate.EnterProtect(owner, Call(1), candidate.Source.Preimage.AsSpan(), 8);
            Reject<InvalidDataException>(() => candidate.Protected(owner, changed, 1, 0, 8, 0x20));
            Require(candidate.Phase == FalloutSourceCStringInputPhase.ProtectEntered, "foreign focus return retains original entered prefix");
        }
        focus.CheckPageOwner(page); Reject<InvalidDataException>(() => Focus(new(page.RuntimePath, new('B', 64), page.ClassTable, page.Selection)).CheckPageOwner(page));
        LiveRecords(page, focus);
        Console.WriteLine("OPENNV_BOOK_FOCUS_SOUND_CONTRACT_PASS independentFocus=true pageHistorySeparate=true fourInputConsumer=true " +
            "partialFailurePreserved=true sourceOnlyColdGuard=true currentSoundPath=true wrongRecordOwner=true jsonReceipts=true nativeCalls=UNEXECUTED");

        void Complete(FalloutSourceCStringInput field, uint pointer, ulong first)
        {
            field.EnterProtect(owner, Call(first), field.Source.Preimage.AsSpan(), 8); field.Protected(owner, Call(first + 1), 1, 0, 8, 0x20);
            field.ObserveStore(owner, Call(first + 2), Push(pointer), pointer, field.Source.Value); field.Restored(owner, Call(first + 3), 0x20, 1, 0, 0x20, 0x40);
        }
    }
    private static void LiveRecords(FalloutSpecialBookPageSoundsSource page, FalloutSpecialBookFocusSoundsSource focus)
    {
        var folder = Directory.CreateTempSubdirectory("opennv-book-focus-sounds-"); var file = Path.Combine(folder.FullName, "Focus.esm");
        var bytes = Join(Record("TES4", 0, Field("HEDR", Join(BitConverter.GetBytes(1.34f), new byte[8]))),
            Sound(0x810, focus.Selection.Previous.EditorId, "ui/previous.wav"), Sound(0x811, focus.Selection.Next.EditorId, "ui/next.wav"));
        File.WriteAllBytes(file, bytes); var hash = SHA256.HashData(bytes);
        try
        {
            using var records = FalloutPluginStack.Load(folder.FullName, ["Focus.esm"]); using var foreign = FalloutPluginStack.Load(folder.FullName, ["Focus.esm"]);
            var fields = new FalloutSpecialBookPageSounds(page, () => { }, () => { }, records, focus); using var consumer = fields.BorrowConsumer();
            var previous = consumer.ResolveFocusCurrent(-1, records); var next = consumer.ResolveFocusCurrent(1, records);
            Require(previous.FormKey == new FalloutFormKey("Focus.esm", 0x810) && next.FormKey == new FalloutFormKey("Focus.esm", 0x811), "real winning SOUN lookup per index direction");
            records.SoundPaths.Set(previous.FormKey, "ui/current-previous.wav");
            Require(consumer.ResolveFocusCurrent(-1, records).LogicalPath.EndsWith("current-previous.wav", StringComparison.Ordinal) &&
                consumer.ResolveFocusCurrent(1, records).LogicalPath == next.LogicalPath, "live path update affects only the selected sound winner");
            Reject<InvalidDataException>(() => consumer.ResolveFocusCurrent(-1, foreign)); consumer.Dispose();
            Reject<ObjectDisposedException>(() => consumer.ResolveFocusCurrent(-1, records));
            Require(SHA256.HashData(File.ReadAllBytes(file)).AsSpan().SequenceEqual(hash), "authored files remain unchanged");
        }
        finally { Directory.Delete(folder.FullName, true); }
    }
    private static FalloutSourceCStringInputInvocation Call(ulong callback, ulong module = 9, uint thread = 7, ulong parent = 11) => new(3, 5, module, thread, parent, callback);
    private static byte[] Push(uint pointer) { var bytes = new byte[5]; bytes[0] = 0x68; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), pointer); return bytes; }
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
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Invalid focus sound contract was admitted."); }
}
