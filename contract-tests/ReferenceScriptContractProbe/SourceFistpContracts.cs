using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class SourceFistpContracts
{
    internal static void Run()
    {
        Check(0x3f000000, [0, 0, 1, 0]); Check(0xbf000000, [0, -1, 0, 0]);
        Check(0x3fc00000, [2, 1, 2, 1]); Check(0xbfc00000, [-2, -2, -1, -1]);
        Check(0x40200000, [2, 2, 3, 2]); Check(0xc0200000, [-2, -3, -2, -2]);
        Check(0x00000001, [0, 0, 1, 0]); Check(0x80000001, [0, -1, 0, 0]);
        Check(0x00000000, [0, 0, 0, 0]); Check(0x80000000, [0, 0, 0, 0]);
        Check(0x4effffff, [2147483520, 2147483520, 2147483520, 2147483520]);
        Check(0xceffffff, [-2147483520, -2147483520, -2147483520, -2147483520]);
        foreach (var bits in new uint[] { 0x4f000000, 0xcf000001, 0x7f800000, 0xff800000, 0x7fc00000, 0x7f800001 })
            foreach (var mode in Enum.GetValues<FalloutFistpRounding>())
            {
                var value = FalloutFistp32Semantics.Read(bits, mode);
                Require(value is { Integer: int.MinValue, Invalid: true, RaisedExceptions: 1 }, "Masked invalid did not retain integer indefinite/IE.");
            }
        var lower = FalloutFistp32Semantics.Read(0xcf000000, FalloutFistpRounding.NearestEven);
        Require(lower is { Integer: int.MinValue, Invalid: false, RaisedExceptions: 0 }, "Exact INT_MIN was conflated with masked invalid.");
        Require(FalloutFistp32Semantics.Read(0x457ff800, FalloutFistpRounding.NearestEven).Integer >> 12 == 1 &&
            FalloutFistp32Semantics.Read(0x457ff800, FalloutFistpRounding.Down).Integer >> 12 == 0 &&
            FalloutFistp32Semantics.Read(0xc5800400, FalloutFistpRounding.Down).Integer >> 12 == -2,
            "Original signed grid shift lost its rounding-boundary or negative-floor result.");
        var source = FalloutMainFrameDeclaration.ForExecutable(FalloutMainFrameDeclaration.Executables.First());
        using var state = new FalloutSourceFistpState(source, "authored-FISTP-source", Guid.NewGuid());
        var empty = state.Capture(); FalloutSourceFistpState.Validate(empty);
        using var cold = new FalloutSourceFistpState(source, empty.Stack, Guid.NewGuid(), empty);
        var saved = cold.Capture();
        Require(!saved.Bound && saved.Conversions == 0 && saved.LastBinding is null && saved.ColdHandoff?.PreviousProcess == empty.CapturedProcess,
            "Cold construction invented a native thread, default control word or completed conversion.");
        Reject(() => new FalloutSourceFistpState(source, empty.Stack, empty.CapturedProcess, empty));
        Reject(() => FalloutSourceFistpState.Validate(empty with { Bound = true }));
        Reject(() => FalloutSourceFistpState.Validate(empty with { Contract = new('0', 64) }));
        var context = new FalloutSourceFistpContext(empty.CapturedProcess, Guid.NewGuid(), 1,
            FalloutMainPlayerCellStep.ContainmentQuery, FalloutSourceFistpSite.PlayerContainment, null);
        Reject(() => state.ConvertPair(context with { Process = Guid.NewGuid() }, 0, 0, () => { }));
        Require(state.Capture().Pairs == 0, "Foreign source epoch entered a native conversion.");
        Reject(() => state.ConvertPair(context, 0, 0, () => { }));
        var failed = state.Capture();
        Require(failed.Last is { Host: null, Returned: false, Failure: not null, Operands.Count: 0 } && failed.Conversions == 0,
            "Missing actual provider became default nearest rounding or fabricated native success.");
        Reject(() => state.ConvertPair(context, 0, 0, () => { }));
        Require(state.Capture().Pairs == 1, "A failed original source conversion prefix was replayed.");
        Console.WriteLine("OPENNV_SOURCE_FISTP_CONTRACTS_PASS bitSemantics=true roundingModes=4 maskedInvalid=true signedGrid=true coldNoNativeReplay=true missingHostRefused=true nativeExecution=UNEXECUTED");
    }
    private static void Check(uint bits, int[] expected)
    {
        for (var index = 0; index < expected.Length; index++)
            Require(FalloutFistp32Semantics.Read(bits, (FalloutFistpRounding)index).Integer == expected[index], "Independent Float32/FISTP rounding case failed.");
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidOperationException("FISTP admitted malformed source/current/cold ownership.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
