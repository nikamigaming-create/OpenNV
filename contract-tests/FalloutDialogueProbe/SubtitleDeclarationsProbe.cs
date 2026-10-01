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
        Console.WriteLine("OPENNV_SUBTITLE_DECLARATIONS_PASS associatedBranch=true childExtent=true ownedNumbers=true template=true placement=true ambiguousRejected=true");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; } throw new InvalidDataException("Invalid subtitle declaration was admitted."); }
}
