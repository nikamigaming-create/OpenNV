using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class SubtitleDeclarationsProbe
{
    internal static void Run()
    {
        var code = Enumerable.Repeat((byte)0x90, 750).ToArray();
        var strings = new Dictionary<uint, string> { [1] = "Info", [2] = "Subtitles", [3] = "Next", [4] = "synthetic-centered-text" };
        var numbers = new Dictionary<uint, double> { [100] = 2, [101] = 19, [102] = 43, [103] = 99 };
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value);
        void Write(int at, params byte[] data) => data.CopyTo(code, at);
        void Branch(int at, uint name, uint member)
        {
            code[at] = 0x68; U32(at + 1, name); code[at + 5] = 0x68; U32(at + 6, 1004);
            code[at + 10] = 0xe8; U32(at + 11, unchecked((uint)(1000 - at - 15)));
            Write(at + 15, 0x83, 0xc4, 4, 0x50, 0xe8); U32(at + 20, unchecked((uint)(1100 - at - 24)));
            Write(at + 24, 0x83, 0xc4, 8, 0x8b, 0x0d); U32(at + 29, 900); Write(at + 33, 0x89, 0x81); U32(at + 35, member);
        }
        void Locus(int at, uint member)
        {
            Write(at, 0x6a, 1, 0x68); U32(at + 3, 4008); Write(at + 7, 0x8b, 0x15); U32(at + 9, 900);
            Write(at + 13, 0x8b, 0x8a); U32(at + 15, member); code[at + 19] = 0xe8; U32(at + 20, 0);
        }
        void Operand(int at, byte operation, uint pointer) { Write(at, 0xdc, operation); U32(at + 2, pointer); }
        Branch(32, 1, 12); Branch(220, 2, 16); Branch(500, 3, 20);
        Write(75, 0x99, 0x2b, 0xc2, 0xd1, 0xf8); Operand(90, 0x35, 100); Operand(100, 0x25, 101); Write(110, 0xd1, 0xe1); Locus(120, 12);
        Operand(190, 0x25, 103); // A different Info child must not replace branch placement.
        Write(260, 0x99, 0x2b, 0xc2, 0xd1, 0xf8); Operand(275, 0x35, 100); Operand(285, 0x25, 102); Locus(310, 16);
        Write(350, 0x6a, 0, 0x68); U32(353, 4); Write(357, 0x8b, 0x15); U32(359, 900);
        Write(363, 0x8b, 0x82); U32(365, 16); Write(369, 0x50, 0x8b, 0x4d, 0xe4, 0xe8); U32(374, 0);
        Operand(450, 0x25, 103);
        FalloutHudSubtitleDeclarations Read() => FalloutExecutableStringTable.ReadHudSubtitleDeclarations(code,
            pointer => strings.GetValueOrDefault(pointer), pointer => pointer == 900,
            pointer => numbers.TryGetValue(pointer, out var value) ? value : throw new InvalidDataException("Foreign numeric pointer."));
        var declaration = Read();
        Require(declaration == new FalloutHudSubtitleDeclarations(19, 43, 2, 2, 2, "synthetic-centered-text"),
            "Subtitle association substituted constants, template identity or an unrelated child operand.");
        var at = declaration.Place(1601, 900, 10, 80, 801, 110);
        Require(at == (399.5f, 628f, 400.5f), "Subtitle placement lost integer screen centering, Info height or safe-zone/source insets.");
        Require(declaration.Place(1601.5f, 900.9f, 10.9f, 80, 801, 110) == at, "Source integer screen/safe-zone conversion changed.");
        numbers[100] = 3; var thirds = Read().Place(1600, 900, 10, 80, 600, 110);
        Require(thirds.X == 600 && thirds.TextX == 200, "Tile divisor changed the separately declared integer screen centering."); numbers[100] = 2;
        Reject(() => declaration.Place(1601, float.NaN, 10, 80, 801, 110));
        Reject(() => declaration.Place(1601, 900, -1, 80, 801, 110));
        numbers[102] = double.NaN; Reject(() => Read()); numbers[102] = 43;
        code[310] = 0x90; Reject(() => Read()); code[310] = 0x6a;
        code[260] = 0x90; Reject(() => Read()); code[260] = 0x99;
        U32(359, 901); Reject(() => Read()); U32(359, 900);
        U32(220 + 11, 17); Reject(() => Read()); U32(220 + 11, 1000 - 220 - 15);
        Branch(620, 2, 24); Reject(() => Read());
        SinglePrecision();
        Console.WriteLine("OPENNV_SUBTITLE_DECLARATIONS_PASS associatedBranch=true childExtent=true ownedNumbers=true template=true placement=true ambiguousRejected=true");
    }

    private static void SinglePrecision()
    {
        var code = Enumerable.Repeat((byte)0x90, 900).ToArray();
        var strings = new Dictionary<uint, string> { [1] = "Info", [2] = "Subtitles", [3] = "Next", [4] = "synthetic-float-text" };
        var floats = new Dictionary<uint, float> { [100] = 0.25f, [101] = 12.75f, [102] = 31.5f, [103] = 90 };
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value);
        void Write(int at, params byte[] data) => data.CopyTo(code, at);
        void Branch(int at, uint name, uint member)
        {
            code[at] = 0x68; U32(at + 1, name); code[at + 5] = 0x68; U32(at + 6, 1004);
            code[at + 10] = 0xe8; U32(at + 11, unchecked((uint)(1100 - at - 15)));
            Write(at + 15, 0x83, 0xc4, 4, 0x50, 0xe8); U32(at + 20, unchecked((uint)(1200 - at - 24)));
            Write(at + 24, 0x8b, 0x0d); U32(at + 26, 900); Write(at + 30, 0x83, 0xc4, 8, 0x89, 0x81); U32(at + 35, member);
        }
        void Operand(int at, byte operation, uint pointer) { Write(at, 0xf3, 0x0f, operation, 0x05); U32(at + 4, pointer); }
        void Locus(int at, uint member)
        {
            code[at] = 0xa1; U32(at + 1, 900); Write(at + 5, 0x6a, 1, 0x51, 0xc7, 4, 0x24);
            U32(at + 11, BitConverter.SingleToUInt32Bits(1)); Write(at + 15, 0x8b, 0x88); U32(at + 17, member);
            code[at + 21] = 0x68; U32(at + 22, 4008); code[at + 26] = 0xe8;
        }
        Write(16, 0x55, 0x8b, 0xec); code[24] = 0xe8; Write(29, 0xd9, 0x5d, 0xd0);
        code[32] = 0xe8; Write(37, 0xd9, 0x5d, 0xd4);
        Write(58, 0x8b, 0x0d); U32(60, 900);
        Write(64, 0x8d, 0x04, 0x3f, 0xf3, 0x0f, 0x2c, 0x7d, 0xd4, 0x6a, 1, 0x68); U32(75, 4016);
        Write(79, 0x8b, 0x89); U32(81, 4); Write(85, 0x89, 0x45, 0xcc, 0x2b, 0xf8, 0x89, 0x7d, 0xec,
            0x66, 0x0f, 0x6e, 0xc7, 0x0f, 0x5b, 0xc0);
        Write(144, 0xf3, 0x0f, 0x2c, 0x45, 0xd0, 0x8b, 0x0d); U32(151, 900);
        Write(155, 0x99, 0x2b, 0xc2, 0x8b, 0xf0, 0x8b, 0x89); U32(162, 8);
        Write(166, 0x6a, 1, 0xd1, 0xfe, 0x68); U32(171, 4017); Write(175, 0x89, 0x75, 0xdc, 0xe8);
        Write(208, 0x8b, 0x75, 0xdc); Write(214, 0x8b, 0x7d, 0xec);
        Branch(240, 1, 12); Branch(450, 2, 16); Branch(720, 3, 20);
        Write(280, 0x66, 0x0f, 0x6e, 0xce); Operand(290, 0x59, 100); Operand(300, 0x5c, 101);
        Write(316, 0x66, 0x0f, 0x6e, 0xc7); Locus(330, 12); Operand(380, 0x5c, 103);
        Write(490, 0x66, 0x0f, 0x6e, 0xce); Operand(504, 0x59, 100); Operand(514, 0x5c, 102); Locus(535, 16);
        code[590] = 0xa1; U32(591, 900); Write(595, 0x8b, 0xcf, 0x6a, 0, 0x68); U32(600, 4);
        Write(604, 0xff, 0xb0); U32(606, 16); code[610] = 0xe8;
        FalloutHudSubtitleDeclarations Read() => FalloutExecutableStringTable.ReadHudSubtitleDeclarations(code,
            pointer => strings.GetValueOrDefault(pointer), pointer => pointer == 900,
            _ => throw new InvalidDataException("Float32 placement read a Float64 operand."), pointer => floats[pointer]);
        var declaration = Read();
        Require(declaration == new FalloutHudSubtitleDeclarations(12.75, 31.5, 4, 2, 2, "synthetic-float-text", true),
            "Float32 declarations lost owned operands, cached screen division, safe-zone scale or text association.");
        Require(declaration.Place(1601.5f, 900.9f, 10.9f, 80, 801, 110) == (599.75f, 645.75f, 200.25f),
            "Float32 placement replaced its cached integer screen or source multiplication.");
        floats[102] = float.NaN; Reject(() => Read()); floats[102] = 31.5f;
        U32(151, 901); Reject(() => Read()); U32(151, 900);
        code[31] = 0xd4; Reject(() => Read()); code[31] = 0xd0;
        code[216] = 0xeb; Reject(() => Read()); code[216] = 0xec;
        code[490] = 0x90; Reject(() => Read()); code[490] = 0x66;
        U32(606, 20); Reject(() => Read()); U32(606, 16);
        code.AsSpan(144, 39).CopyTo(code.AsSpan(780)); // A foreign function's half calculation cannot repair a missing cache.
        code[144] = 0x90; Reject(() => Read()); code[144] = 0xf3;
        Branch(780, 2, 24); Reject(() => Read());
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; } throw new InvalidDataException("Invalid subtitle declaration was admitted."); }
}
