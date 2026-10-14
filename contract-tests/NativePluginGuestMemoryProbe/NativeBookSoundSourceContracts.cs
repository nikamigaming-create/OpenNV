using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Compatibility.NativePlugins;
using static NativeBookSoundOperandContracts;

internal static class NativeBookSoundSourceContracts
{
    private const uint CodeBase = 0x10000, Table = 0x20000, Direction = 0x21000, ForwardName = 0x22000, BackwardName = 0x22020;
    private sealed class Emitter
    {
        internal readonly List<byte> Bytes = [];
        internal readonly Dictionary<string, int> Labels = [];
        private readonly List<(int At, int Size, string Label)> _fixups = [];
        internal void Emit(params byte[] bytes) => Bytes.AddRange(bytes);
        internal void U32(uint value) => Emit(BitConverter.GetBytes(value));
        internal void Label(string name) => Labels.Add(name, Bytes.Count);
        internal void Jump(string label, params byte[] opcode)
        { Emit(opcode); _fixups.Add((Bytes.Count, opcode.Length == 2 ? 4 : 1, label)); Emit(new byte[_fixups[^1].Size]); }
        internal void Call(uint target) { Emit(0xe8); U32(unchecked(target - CodeBase - (uint)Bytes.Count - 4)); }
        internal byte[] Finish()
        {
            foreach (var (at, size, label) in _fixups)
            {
                var displacement = Labels[label] - at - size;
                if (size == 1)
                {
                    if (displacement is < sbyte.MinValue or > sbyte.MaxValue) throw new InvalidDataException("Authored short branch overflowed.");
                    Bytes[at] = unchecked((byte)(sbyte)displacement);
                }
                else for (var i = 0; i < 4; ++i) Bytes[at + i] = BitConverter.GetBytes(displacement)[i];
            }
            return Bytes.Concat(new byte[2048 - Bytes.Count]).ToArray();
        }
    }
    private sealed record Authored(byte[] Code, uint Case, uint Turn, int ForwardStore, int BackwardStore,
        Dictionary<uint, string> Literals, Dictionary<string, int> Labels);
    private static Authored Build()
    {
        var e = new Emitter(); e.Emit(0x55, 0x8b, 0xec, 0x89, 0x4d, 0xe0, 0x83, 0xf8, 7); e.Jump("outside", 0x77);
        e.Emit(0xff, 0x24, 0x85); e.U32(Table); e.Label("outside"); e.Emit(0xc3);
        e.Label("case"); e.Emit(0x83, 0x7d, 8, 0); e.Jump("back-call", 0x75);
        e.Emit(0x6a, 1, 0x8b, 0x8d); e.U32(unchecked((uint)-32)); e.Emit(0xe8); e.Label("forward-call-rel"); e.U32(0); e.Jump("case-end", 0xeb);
        e.Label("back-call"); e.Emit(0x6a, 0, 0x8b, 0x8d); e.U32(unchecked((uint)-32)); e.Emit(0xe8); e.Label("back-call-rel"); e.U32(0);
        e.Label("case-end"); e.Emit(0xc3);
        e.Label("turn"); e.Emit(0x55, 0x8b, 0xec, 0x89, 0x4d, 0xe0, 0x0f, 0xb6, 0x45, 8, 0x85, 0xc0); e.Jump("back", 0x0f, 0x84);
        e.Emit(0xc6, 5); e.U32(Direction); e.Emit(1, 0x8b, 0x4d, 0xe0, 0x83, 0x79, 72, 3); e.Jump("forward-fail", 0x7d);
        e.Emit(0x8b, 0x55, 0xe0, 0x8b, 0x42, 72, 0x83, 0xc0, 1, 0x8b, 0x4d, 0xe0); e.Label("forward-store"); e.Emit(0x89, 0x41, 72);
        Audio(ForwardName); e.Jump("end", 0xeb); e.Label("forward-fail"); e.Emit(0xc3);
        e.Label("back"); e.Emit(0xc6, 5); e.U32(Direction); e.Emit(0, 0x8b, 0x55, 0xe0, 0x83, 0x7a, 72, 1); e.Jump("back-fail", 0x7c);
        e.Emit(0x8b, 0x45, 0xe0, 0x8b, 0x48, 72, 0x83, 0xe9, 1, 0x8b, 0x55, 0xe0); e.Label("back-store"); e.Emit(0x89, 0x4a, 72);
        Audio(BackwardName); e.Jump("end", 0xeb); e.Label("back-fail"); e.Emit(0xc3); e.Label("end"); e.Emit(0xc3);
        var bytes = e.Finish();
        foreach (var label in new[] { "forward-call-rel", "back-call-rel" })
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(e.Labels[label]), e.Labels["turn"] - e.Labels[label] - 4);
        return new(bytes, CodeBase + (uint)e.Labels["case"], CodeBase + (uint)e.Labels["turn"], e.Labels["forward-store"], e.Labels["back-store"],
            new() { [ForwardName] = "AuthoredForward", [BackwardName] = "AuthoredBackward" }, e.Labels);
        void Audio(uint name)
        {
            e.Call(CodeBase + 1500); e.Emit(0x68); e.U32(0x121); e.Emit(0x68); e.U32(name);
            e.Emit(0x8d, 0x55, 0xc0, 0x52); e.Call(CodeBase + 1520); e.Emit(0x8b, 0xc8); e.Call(CodeBase + 1540);
            e.Call(CodeBase + 1560); e.Call(CodeBase + 1580); e.Emit(0x6a, 0); e.Call(CodeBase + 1600); e.Call(CodeBase + 1580);
        }
    }
    private static FalloutSpecialBookPageSoundSelection Select(Authored source) => FalloutExecutableStringTable.ReadSpecialBookPageSoundSelection(
        source.Code, CodeBase, CodeBase, (address, count) => address == Table && count == 32
            ? Enumerable.Range(0, 8).SelectMany(_ => BitConverter.GetBytes(source.Case)).ToArray() : throw new InvalidDataException("Foreign table read."),
        address => source.Literals.GetValueOrDefault(address));
    internal static void Run()
    {
        var authored = Build(); var selected = Select(authored);
        Require(selected.Turn == authored.Turn && selected.MaximumPage == 3 && selected.PageField == 72 && selected.DirectionByte == Direction &&
            selected.Forward.EditorId == "AuthoredForward" && selected.Backward.EditorId == "AuthoredBackward" &&
            selected.Forward.Instruction != selected.Backward.Instruction && selected.SoundChildren.Length == 7,
            "actual source branches select independent input roles without assuming equal names or fixed bounds");
        var bothNamesEqual = authored with { Literals = new() { [ForwardName] = "AuthoredSame", [BackwardName] = "AuthoredSame" } };
        var equal = Select(bothNamesEqual); Require(equal.Forward.Instruction != equal.Backward.Instruction && equal.Forward.EditorId == equal.Backward.EditorId,
            "same initial text cannot collapse separate pointer operands");
        foreach (var offset in new[] { authored.ForwardStore + 2, authored.BackwardStore + 2, checked((int)(selected.Forward.Instruction - CodeBase)),
            authored.Labels["back"] + 6, authored.Labels["back-call-rel"] })
        {
            var changed = authored.Code.ToArray(); changed[offset] ^= 1;
            NativeBookSoundOperandContracts.Reject<Exception>(() => Select(authored with { Code = changed }));
        }
        NativeBookSoundOperandContracts.Reject<InvalidDataException>(() => Select(authored with { Literals = new() { [ForwardName] = "bad/path", [BackwardName] = "AuthoredBackward" } }));
        var source = new FalloutSpecialBookPageSoundsSource("authored-source", new('A', 64), 500, selected);
        source.CheckSame(new("authored-source", new('A', 64), 500, selected));
        NativeBookSoundOperandContracts.Reject<InvalidDataException>(() => source.CheckSame(new("authored-source", new('B', 64), 500, selected)));
        NativeBookSoundOperandContracts.Reject<InvalidDataException>(() => source.CheckSame(new("authored-source", new('A', 64), 500, selected with { Backward = selected.Backward with { Instruction = selected.Backward.Instruction + 1 } })));
        Module(source); NativeBookSoundDataAccessContracts.Run();
        Console.WriteLine("OPENNV_BOOK_SOUND_SOURCE_CONTRACT_PASS usedClassInput=true boolDirections=true boundsAndPageStores=true orderedSoundChildren=true " +
            "independentEqualText=true exactLoadPair=true writerAbi=true foreignMutationRefused=true nativeCalls=UNEXECUTED");
    }
    private static void Module(FalloutSpecialBookPageSoundsSource source)
    {
        const uint moduleBase = 0x30000, buffer = 0x40000, importSlot = 0x41000;
        var bytes = new byte[1024]; var at = 0;
        void B(params byte[] value) { value.CopyTo(bytes, at); at += value.Length; }
        void Word(uint value) => B(BitConverter.GetBytes(value));
        void CallWriter() { B(0xe8); Word(unchecked((uint)(256 - at - 4))); }
        B(0xbe); Word(buffer); B(0xb9); Word(source.Input(FalloutSpecialBookPageDirection.Forward).Operand); B(0x8b, 0xd6); CallWriter();
        B(0x8b, 0xd6, 0xb9); Word(source.Input(FalloutSpecialBookPageDirection.Backward).Operand); CallWriter(); B(0xc3);
        at = 256;
        B(0x55, 0x8b, 0xec, 0x51, 0x53, 0x57, 0x8d, 0x45, 0xfc, 0x8b, 0xd9, 0x50, 0x6a, 0x40,
            0x6a, 4, 0x53, 0x8b, 0xfa, 0xff, 0x15); Word(importSlot);
        B(0x8d, 0x45, 0xfc, 0x89, 0x3b, 0x50, 0xff, 0x75, 0xfc, 0x6a, 4, 0x53, 0xff, 0x15); Word(importSlot); B(0x5f, 0x5b, 0xc9, 0xc3);
        var data = Encoding.ASCII.GetBytes("AuthoredPageSound\0");
        FalloutSpecialBookPageSoundModuleAssociation? Read(byte[] code, string import = "VirtualProtect", bool readable = true) => FalloutExecutableStringTable.ReadSpecialBookPageSoundModuleAssociation(
            code, moduleBase, moduleBase, source, (address, count) => readable && address == buffer && count == data.Length,
            address => address == buffer ? "AuthoredPageSound" : null, (address, count) => address == buffer && count == data.Length ? data : throw new InvalidDataException(),
            address => address == importSlot ? import : null);
        var selected = Read(bytes)!;
        Require(selected.Buffer == buffer && selected.Capacity == data.Length && selected.Writer == moduleBase + 256 &&
            selected.ForwardCall == moduleBase + 12 && selected.BackwardCall == moduleBase + 24, "exact source Load pair/real helper/shared buffer");
        NativeBookSoundOperandContracts.Reject<NotSupportedException>(() => Read(bytes, "ForeignProtect"));
        NativeBookSoundOperandContracts.Reject<InvalidDataException>(() => Read(bytes, readable: false));
        foreach (var offset in new[] { 6, 20, 256 + 29, 256 + 34, 256 + 39 })
        {
            var changed = bytes.ToArray(); changed[offset] ^= 1; NativeBookSoundOperandContracts.Reject<Exception>(() => Read(changed));
        }
        var noSound = bytes.ToArray(); Array.Clear(noSound, 0, 30); Require(Read(noSound) is null, "unrelated original module receives no pointer permission");
    }
    internal static void RunOwnedSource(string runtime, string plugin)
    {
        var before = new[] { runtime, plugin }.ToDictionary(Path.GetFullPath, Hash, StringComparer.OrdinalIgnoreCase);
        var fields = FalloutSpecialBookPageSoundsSource.Read(runtime); var image = NativePluginImageDeclarations.Read(plugin, dll: true);
        var module = NativePluginModuleBookSoundSource.Read(image, fields) ?? throw new InvalidDataException("Actual selected Book pair is absent.");
        Require(fields.Forward.Instruction != fields.Backward.Instruction && fields.Forward.Pointer != 0 && fields.Backward.Pointer != 0 &&
            fields.Selection.PageField > 0 && fields.Selection.SoundChildren.Length == 7 && module.ForwardCallRva < module.BackwardCallRva &&
            module.ProtectReturnRva < module.RestoreReturnRva && module.InitialBytes.Length == module.Capacity &&
            FalloutExecutableStringTable.ReadSoundEditorIdBytes(module.InitialBytes.AsSpan()) == module.EditorId &&
            module.MappedWriterCode(0x20000000).Length == 47, "actual class/direction/page/audio/helper/module-buffer associations");
        fields.CheckSame(FalloutSpecialBookPageSoundsSource.Read(runtime)); module.Check(image);
        NativeBookSoundOperandContracts.Reject<InvalidDataException>(() => module.Check(image with { Imports = [] }));
        foreach (var row in before) Require(Hash(row.Key) == row.Value, "unchanged actual source input");
        Console.WriteLine("OPENNV_BOOK_SOUND_SOURCE_PASS originalClass=true originalPageBranches=true independentOperands=2 sourceAudioChildren=7 " +
            "actualLoadCalls=true actualSharedModuleBuffer=true exactSdkWriter=true ownedInputsUnchanged=true nativeCallsAndAudio=UNEXECUTED");
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}
