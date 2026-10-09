using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static partial class ImageEffectsDeclarationsProbe
{
    internal static void Run()
    {
        SinglePhase();
        var phaseCode = new byte[38];
        Store(phaseCode, 0, [0x83, 0xec, 0x08, 0xd9, 0x05]);
        Store(phaseCode, 9, [0x8b, 0x41, 0x1c, 0xdc, 0x35]);
        Store(phaseCode, 18, [0x56, 0x8b, 0x70, 0x0c, 0x83, 0xec, 0x10, 0x8b, 0xce, 0xdc, 0x0d]);
        Store(phaseCode, 33, [0xd9, 0x5c, 0x24, 0x14]);
        Word(phaseCode, 14, 0x1122); Word(phaseCode, 29, 0x3344);
        double Scalar(uint address) => address switch { 0x1122 => 7200, 0x3344 => 5.875, _ => throw new Exception("Unexpected declaration address.") };
        var phase = FalloutExecutableStringTable.ReadDoubleVisionPhase(phaseCode, Scalar);
        Require(phase.SecondsPerHour == 7200 && phase.RadiansPerTurn == 5.875 && phase.Angle(0.25f) == 1.46875f,
            "Phase declaration substituted a clock divisor or turn constant.");
        Expect<InvalidDataException>(() => FalloutExecutableStringTable.ReadDoubleVisionPhase(phaseCode.Concat(phaseCode).ToArray(), Scalar));
        Expect<InvalidDataException>(() => FalloutExecutableStringTable.ReadDoubleVisionPhase(phaseCode, _ => double.NaN));
        phaseCode[0] ^= 1;
        Expect<NotSupportedException>(() => FalloutExecutableStringTable.ReadDoubleVisionPhase(phaseCode, Scalar));

        var selector = new byte[168];
        Store(selector, 0, [0x55, 0x8b, 0xec]);
        Store(selector, 90, [0x81, 0x7d, 0xfc]); Word(selector, 93, 1515); selector[97] = 0x75;
        selector[117] = 0x68; Word(selector, 118, 0x4321); selector[122] = 0xe8;
        Store(selector, 141, [0x85, 0xc0, 0x75, 0x10]);
        Store(selector, 161, [0x8b, 0x45, 0xf8, 0x8b, 0xe5, 0x5d, 0xc3]);
        foreach (var (offset, form) in new[] { (36, 0x910u), (72, 0x920u), (99, 0x930u), (145, 0x940u) })
        {
            selector[offset] = 0x68; Word(selector, offset + 1, form); selector[offset + 5] = 0xe8;
            Word(selector, offset + 6, checked((uint)(2500 - offset - 10)));
            Store(selector, offset + 10, [0x83, 0xc4, 0x04, 0x89, 0x45, 0xf8]);
        }
        string? Literal(uint address) => address == 0x4321 ? "Player Name Entry Menu" : null;
        var menus = FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(selector, Literal);
        Require(menus.Form(FalloutMenuBackgroundKind.Popup) == 0x940 && menus.Interface == 0x930 &&
            menus.Pause == 0x910 && menus.PipBoy == 0x920 && menus.InterfaceMenu == 1515,
            "Menu background substituted an identity or selected another branch.");
        Expect<InvalidDataException>(() => FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(selector.Concat(selector).ToArray(), Literal));
        Word(selector, 145 + 6, 0);
        Expect<NotSupportedException>(() => FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(selector, Literal));
        var direct = new byte[139];
        Store(direct, 0, [0x56, 0xe8]); Store(direct, 6, [0x8b, 0xf0, 0xa1]);
        Store(direct, 13, [0x85, 0xc0, 0x74, 24, 0xf6, 0x80]); direct[23] = 1; Store(direct, 24, [0x74, 15]);
        Store(direct, 41, [0x8b, 0x0d]); direct[47] = 0xe8; Store(direct, 52, [0x84, 0xc0, 0x74, 15]);
        Store(direct, 71, [0x81, 0xfe]); Word(direct, 73, 1800); Store(direct, 77, [0x75, 15]);
        Store(direct, 94, [0x8b, 0x0d]); direct[100] = 0x68; Word(direct, 101, 0x4321);
        Store(direct, 105, [0x8b, 0x89]); direct[111] = 0xe8; Store(direct, 116, [0x85, 0xc0, 0x75, 15]);
        Store(direct, 135, [0x33, 0xc0, 0x5e, 0xc3]);
        foreach (var (offset, form) in new[] { (26, 0x510u), (56, 0x520u), (79, 0x530u), (120, 0x540u) })
        {
            direct[offset] = 0x68; Word(direct, offset + 1, form); direct[offset + 5] = 0xe8;
            Word(direct, offset + 6, checked((uint)(3000 - offset - 10)));
            Store(direct, offset + 10, [0x83, 0xc4, 4, 0x5e, 0xc3]);
        }
        menus = FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(direct, Literal);
        Require(menus.Popup == 0x540 && menus.Interface == 0x530 && menus.Pause == 0x510 && menus.PipBoy == 0x520 && menus.InterfaceMenu == 1800,
            "Register selector must retain its source predicates, forms and selected menu.");
        Expect<InvalidDataException>(() => FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(direct.Concat(direct).ToArray(), Literal));
        direct[119]++; Expect<NotSupportedException>(() => FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(direct, Literal)); direct[119]--;
        Word(direct, 126, 0); Expect<NotSupportedException>(() => FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(direct, Literal));
        Word(direct, 126, 2870); Word(direct, 80, 0); Expect<InvalidDataException>(() => FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(direct, Literal));
        Expect<NotSupportedException>(() => FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(direct[..^1], Literal));
        Console.WriteLine("OPENNV_IMAGE_EFFECT_DECLARATIONS_PASS phaseConstants=true sourceMenuForms=true registerSelector=true predicateTargets=true directReturns=true ambiguousRejected=true");
    }
    private static void Store(byte[] bytes, int at, byte[] value) => value.CopyTo(bytes, at);
    private static void Word(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
