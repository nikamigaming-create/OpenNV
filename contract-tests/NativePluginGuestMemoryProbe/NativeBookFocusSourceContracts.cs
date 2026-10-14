using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Content;
using static NativeBookSoundOperandContracts;

internal static class NativeBookFocusSourceContracts
{
    private const uint Basis = 0x10000, Table = 0x20000, PreviousName = 0x22000, NextName = 0x22040;
    private sealed class Emitter
    {
        internal readonly List<byte> Data = []; internal readonly Dictionary<string, int> Labels = [];
        private readonly List<(int At, string Label)> _jumps = [];
        internal void B(params byte[] value) => Data.AddRange(value);
        internal void Word(uint value) => B(BitConverter.GetBytes(value));
        internal void Label(string label) => Labels.Add(label, Data.Count);
        internal void Jump(byte opcode, string label) { B(opcode); _jumps.Add((Data.Count, label)); B(0); }
        internal void Call(uint target) { B(0xe8); Word(unchecked(target - Basis - (uint)Data.Count - 4)); }
        internal void Receiver(byte register) { B(0x8b, (byte)(0x85 | register << 3)); Word(unchecked((uint)-192)); }
        internal byte[] Finish()
        {
            foreach (var (at, label) in _jumps)
            { var delta = Labels[label] - at - 1; if (delta is < -128 or > 127) throw new InvalidDataException("Authored jump overflow."); Data[at] = unchecked((byte)(sbyte)delta); }
            return Data.Concat(new byte[2048 - Data.Count]).ToArray();
        }
    }
    private sealed record Authored(byte[] Code, Dictionary<string, int> Labels, Dictionary<uint, string> Literals);
    private static Authored Build(byte[]? beforeStoreReceiver = null, bool clobberNext = false)
    {
        var e = new Emitter(); e.B(0x55, 0x8b, 0xec, 0x89, 0x8d); e.Word(unchecked((uint)-192)); e.B(0x83, 0xf8, 7); e.Jump(0x77, "outside");
        e.B(0xff, 0x24, 0x85); e.Word(Table); e.Label("outside"); e.B(0xc3);
        Arm("previous", PreviousName, false); Arm("next", NextName, true);
        return new(e.Finish(), e.Labels, new() { [PreviousName] = "AuthoredFocusPrevious", [NextName] = "AuthoredFocusNext" });
        void Arm(string label, uint name, bool next)
        {
            e.Label(label); e.Receiver(1); e.B(0x83, 0x79, 72, 8); e.Jump(0x75, label + "-refresh");
            e.Receiver(2); e.B(0x83, 0x7a, 76, next ? (byte)7 : (byte)1); e.Label(label + "-bound"); e.Jump(next ? (byte)0x7d : (byte)0x7e, label + "-refresh");
            e.Receiver(0); e.B(0x8b, 0x48, 76, 0x83, next ? (byte)0xc1 : (byte)0xe9, 1);
            if (beforeStoreReceiver is not null && next == clobberNext) e.B(beforeStoreReceiver);
            e.Receiver(2);
            e.Label(label + "-store"); e.B(0x89, 0x4a, 76, 0x6a, 0, 0x68); e.Word(0x121); e.B(0x68); e.Word(name);
            e.B(0x8d, 0x85); e.Word(unchecked((uint)-176)); e.B(0x50); e.Call(Basis + 1500); e.B(0x8b, 0xc8); e.Call(Basis + 1520);
            e.Call(Basis + 1540); e.Call(Basis + 1560); e.Label(label + "-refresh"); e.Receiver(1); e.Call(Basis + 1580); e.B(0xc3);
        }
    }
    private static FalloutSpecialBookFocusSoundSelection Select(Authored input) => FalloutExecutableStringTable.ReadSpecialBookFocusSoundSelection(
        input.Code, Basis, Basis, (address, count) => address == Table && count == 32 ? Enumerable.Range(0, 8)
            .SelectMany(index => BitConverter.GetBytes(Basis + (uint)input.Labels[index == 6 ? "previous" : index == 7 ? "next" : "outside"])).ToArray()
            : throw new InvalidDataException("Foreign click table."), pointer => input.Literals.GetValueOrDefault(pointer));
    private static FalloutSpecialBookPageSoundsSource Page(FalloutSpecialBookFocusSoundSelection focus) => new("authored-focus-source", new('A', 64), 500,
        new(focus.Click, Basis + 1000, focus.PageField, 9, 0x21000, [Basis + 1480, focus.SoundChildren[0], focus.SoundChildren[1], Basis + 1490,
            focus.SoundChildren[3], focus.SoundChildren[2], focus.SoundChildren[3]],
            new(FalloutSpecialBookPageDirection.Forward, Basis + 1100, 0x23000, "AuthoredPageForward", 0x121),
            new(FalloutSpecialBookPageDirection.Backward, Basis + 1200, 0x23040, "AuthoredPageBackward", 0x121)));
    internal static void Run()
    {
        var authored = Build(); var selected = Select(authored); var page = Page(selected);
        var focus = new FalloutSpecialBookFocusSoundsSource(page.RuntimePath, page.RuntimeSha256, page.ClassTable, selected); focus.CheckPageOwner(page);
        Require(selected.PageField == 72 && selected.IndexField == 76 && selected.ReviewPage == 8 && selected.MinimumIndex == 1 && selected.MaximumIndex == 7 &&
            selected.Previous.Instruction != selected.Next.Instruction && selected.SoundChildren.Length == 4 && selected.Refresh == Basis + 1580,
            "click6/7 select same receiver's bounded current index stores and four original sound children");
        var same = Select(authored with { Literals = new() { [PreviousName] = "AuthoredSame", [NextName] = "AuthoredSame" } });
        Require(same.Previous.Instruction != same.Next.Instruction && same.Previous.EditorId == same.Next.EditorId, "equal names do not merge actual focus input operands");
        foreach (var offset in new[] { authored.Labels["previous-store"] + 2, authored.Labels["next-store"] + 2,
            authored.Labels["previous-bound"], authored.Labels["next-bound"], checked((int)(selected.Previous.Instruction - Basis)) })
        { var changed = authored.Code.ToArray(); changed[offset] ^= 1; RejectSource(() => Select(authored with { Code = changed })); }
        // Keep all source field/call/branch associations and fix every emitted
        // relative target. Only add a real index-register clobber between the
        // matched unit arithmetic and receiver/store; neither arm may admit it.
        foreach (var nextArm in new[] { false, true })
            foreach (var clobber in new[] { new byte[] { 0x31, 0xc9 }, new byte[] { 0x83, 0xc1, 2 } })
                RejectSource(() => Select(Build(clobber, nextArm)));
        RejectSource(() => Select(authored with { Literals = new() { [PreviousName] = "bad/path", [NextName] = "AuthoredFocusNext" } }));
        RejectSource(() => focus.CheckSame(new(page.RuntimePath, page.RuntimeSha256, page.ClassTable, selected with { IndexField = 80 })));
        Module(page, focus);
        Console.WriteLine("OPENNV_BOOK_FOCUS_SOURCE_CONTRACT_PASS realControlRoles=true reviewAndSignedIndexPredicates=true indexStoresBeforeSound=true " +
            "separateEqualNames=true fourAudioChildren=true joinedRefresh=true immediateLoadPredecessor=true indexRegisterClobberRefused=true malformedRefused=true nativeCalls=UNEXECUTED");
    }
    private static void Module(FalloutSpecialBookPageSoundsSource page, FalloutSpecialBookFocusSoundsSource focus)
    {
        const uint moduleBase = 0x30000, pageBuffer = 0x40000, focusBuffer = 0x40040, importSlot = 0x50000;
        var bytes = new byte[1024]; var at = 0;
        void B(params byte[] value) { value.CopyTo(bytes, at); at += value.Length; }
        void Word(uint value) => B(BitConverter.GetBytes(value));
        void Call() { B(0xe8); Word(unchecked((uint)(512 - at - 4))); }
        void Pair(uint buffer, uint first, uint second)
        { B(0xbe); Word(buffer); B(0xb9); Word(first); B(0x8b, 0xd6); Call(); B(0x8b, 0xd6, 0xb9); Word(second); Call(); }
        Pair(pageBuffer, page.Input(FalloutSpecialBookPageDirection.Forward).Operand, page.Input(FalloutSpecialBookPageDirection.Backward).Operand);
        Pair(focusBuffer, focus.Input(FalloutSpecialBookFocusDirection.Previous).Operand, focus.Input(FalloutSpecialBookFocusDirection.Next).Operand); B(0xc3);
        at = 512; B(0x55, 0x8b, 0xec, 0x51, 0x53, 0x57, 0x8d, 0x45, 0xfc, 0x8b, 0xd9, 0x50, 0x6a, 0x40, 0x6a, 4, 0x53, 0x8b, 0xfa, 0xff, 0x15);
        Word(importSlot); B(0x8d, 0x45, 0xfc, 0x89, 0x3b, 0x50, 0xff, 0x75, 0xfc, 0x6a, 4, 0x53, 0xff, 0x15); Word(importSlot); B(0x5f, 0x5b, 0xc9, 0xc3);
        var literals = new Dictionary<uint, string> { [pageBuffer] = "AuthoredPageModule", [focusBuffer] = "AuthoredFocusModule" };
        FalloutSpecialBookPageSoundModuleAssociation? Read(byte[] code, bool readable = true) => FalloutExecutableStringTable.ReadSpecialBookFocusSoundModuleAssociation(
            code, moduleBase, moduleBase, page, focus, (address, count) => readable && literals.TryGetValue(address, out var text) && count == text.Length + 1,
            address => literals.GetValueOrDefault(address), (address, count) => Encoding.ASCII.GetBytes(literals[address] + '\0'), p => p == importSlot ? "VirtualProtect" : null);
        var selected = Read(bytes)!; Require(selected.ForwardCall == moduleBase + 41 && selected.BackwardCall == moduleBase + 53 && selected.Writer == moduleBase + 512,
            "focus pair directly follows actual page pair in the real decoded Load graph");
        RejectSource(() => Read(bytes, false));
        foreach (var offset in new[] { 6, 29 + 6, 29 + 20, 512 + 29, 512 + 34 })
        { var changed = bytes.ToArray(); changed[offset] ^= 1; RejectSource(() => Read(changed)); }
        var gap = bytes.ToArray(); Array.Copy(bytes, 29, gap, 30, 30); gap[29] = 0x90;
        // Preserve the displaced E8 targets so this negative changes the edge,
        // not the helper's identity or instruction decoding.
        foreach (var call in new[] { 42, 54 }) BinaryPrimitives.WriteInt32LittleEndian(gap.AsSpan(call + 1), 512 - call - 5);
        RejectSource(() => Read(gap));
    }
    internal static void RunOwnedSource(string runtime, string plugin)
    {
        var paths = new[] { runtime, plugin }.ToDictionary(Path.GetFullPath, Hash, StringComparer.OrdinalIgnoreCase);
        var page = FalloutSpecialBookPageSoundsSource.Read(runtime); var focus = FalloutSpecialBookFocusSoundsSource.Read(runtime); focus.CheckPageOwner(page);
        var image = NativePluginImageDeclarations.Read(plugin, true); var previous = NativePluginModuleBookSoundSource.Read(image, page)!;
        var source = NativePluginModuleBookSoundSource.ReadFocus(image, page, focus) ?? throw new InvalidDataException("Actual selected focus source pair is absent.");
        Require(source.FirstCallRva == previous.SecondCallRva + 17 && source.WriterRva == previous.WriterRva && source.BufferRva != previous.BufferRva &&
            source.Capacity == source.InitialBytes.Length && FalloutExecutableStringTable.ReadSoundEditorIdBytes(source.InitialBytes.AsSpan()) == source.EditorId &&
            focus.Selection.Previous.Instruction != focus.Selection.Next.Instruction && focus.Selection.IndexField != focus.Selection.PageField,
            "actual independent focus inputs/readonly data/helper and immediate reached Load edge");
        focus.CheckSame(FalloutSpecialBookFocusSoundsSource.Read(runtime)); source.Check(image); RejectSource(() => source.Check(image with { Imports = [] }));
        foreach (var row in paths) Require(Hash(row.Key) == row.Value, "owned source inputs unchanged");
        Console.WriteLine("OPENNV_BOOK_FOCUS_SOURCE_PASS sameOriginalBook=true indexControlRoles=true sourceStoresBeforeSound=true independentOperands=2 " +
            "fourAudioChildren=true actualReadonlyBuffer=true immediateLoadEdge=true nativeCallsAndAudio=UNEXECUTED");
    }
    private static void RejectSource(Action action)
    { try { action(); } catch (InvalidDataException) { return; } catch (NotSupportedException) { return; } throw new InvalidDataException("Invalid focus source was admitted."); }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
}
