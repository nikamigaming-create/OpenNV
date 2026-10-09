using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class ImageEffectsDeclarationsProbe
{
    private static void SinglePhase()
    {
        var code = SingleFixture();
        const uint codeBase = 0x600000;
        float Scalar(uint value) => value switch { 0x710004 => 25f, 0x710008 => 5.875f, 0x71000c => 5f, _ => throw new InvalidDataException("Unknown authored phase operand.") };
        bool Writable(uint value) => value is 0x720000 or 0x720020 or 0x720024;
        FalloutDoubleVisionPhase Read(byte[] bytes) => FalloutExecutableStringTable.ReadSingleDoubleVisionPhase(bytes, codeBase, Scalar, Writable)
            ?? throw new NotSupportedException("Authored Float32 source declaration is not admitted.");
        var phase = Read(code) with { LoadedClockBits = 0 };
        Require(phase.SinglePrecisionArithmetic && phase.SourceHourFactor == 5 && phase.SecondsPerHour == 25 && phase.RadiansPerTurn == 5.875,
            "Phase substituted the authored scale, turn or precision family.");
        static void Mutate(byte[] input, int offset, byte value, Func<byte[], FalloutDoubleVisionPhase> read)
        { var bad = input.ToArray(); bad[offset] = value; Expect<NotSupportedException>(() => read(bad)); }
        Mutate(code, 32 + 11, 0x59, Read); // Multiply cannot replace the actual source division.
        Mutate(code, 360 + 43, 0x59, Read); // Actual vector callee must store, not multiply.
        var changedCall = code.ToArray(); Call(changedCall, 32 + 159, 448);
        Expect<NotSupportedException>(() => Read(changedCall));
        Mutate(code, 864 + 31, 0xff, Read); // Sine's genuine ISA consumer changed to cosine.
        Mutate(code, 1040 + 97, 0x14, Read); // Index-zero guard must reach the actual zero-return arm.
        Mutate(code, 960 + 49, 0xcf, Read); // Callback writer no longer returns to the same four-slot loop.
        Mutate(code, 32 + 201, 0x33, Read); // A phase store is not a completed parameter publication.
        Mutate(code, 32 + 197, 0x01, Read);
        var installer = code.ToArray(); Word(installer, 1396, codeBase + 1280);
        Expect<NotSupportedException>(() => Read(installer));
        Expect<NotSupportedException>(() => Read(code[..1290]));
        Expect<InvalidDataException>(() => FalloutExecutableStringTable.ReadSingleDoubleVisionPhase(code, codeBase,
            value => value == 0x710008 ? float.NaN : Scalar(value), Writable));
        ClockLifecycle(phase);
        Console.WriteLine("OPENNV_IMAGE_SINGLE_PHASE_PASS actualCallees=true indexedHourWriter=true Float32Stores=true guardDriftRefused=true clockColdNewEpoch=true actualThread=true nativeAndPixels=UNEXECUTED");
    }

    private static void ClockLifecycle(FalloutDoubleVisionPhase phase)
    {
        var forms = Enumerable.Range(0, 6).Select(index => new FalloutFormKey("Phase.esm", (uint)(0x300 + index))).ToArray();
        var bindings = new FalloutGameTimeBindings(forms[0], forms[1], forms[2], forms[3], forms[4], forms[5]);
        float[] values = [2250, 0, 1, 3.125f, 0, 1];
        var sources = forms.Select((form, index) => new FalloutGlobal(form, $"Authored{index}", (byte)'f', values[index], "phase-global-" + index)).ToArray();
        var globals = new FalloutGlobalState(sources);
        var calendar = new FalloutCalendar([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], "authored-phase-calendar");
        var time = new FalloutGameTime(globals, bindings, calendar);
        var process = Guid.NewGuid();
        var clock = new FalloutImageSpacePhaseClock(phase, time, process);
        Expect<NotSupportedException>(() => clock.Capture());
        clock.Sample(time, process); var saved = clock.Capture();
        Require(saved.SecondsBits == BitConverter.SingleToUInt32Bits((float)((double)values[3] * 5 * 5)), "Phase cache did not store its actual source hour product.");
        globals.Set(forms[3], 4.25f);
        Require(clock.Capture() == saved, "A draw/capture reread the hour without the actual indexed writer.");
        var coldTime = new FalloutGameTime(globals, bindings, calendar);
        var coldProcess = Guid.NewGuid();
        var cold = new FalloutImageSpacePhaseClock(phase, coldTime, coldProcess);
        var transported = JsonSerializer.Deserialize<FalloutImageSpacePhaseClockSnapshot>(JsonSerializer.Serialize(saved))!;
        Require(transported.Source == saved.Source && transported.HourSource == saved.HourSource,
            "Current cold transport lost source precision, loaded-cell or hour provenance.");
        cold.Restore(transported); Expect<NotSupportedException>(() => cold.Capture());
        cold.BindWriter(coldTime, coldProcess);
        Require(cold.Capture().SecondsBits == saved.SecondsBits && cold.Capture().Samples == saved.Samples, "Cold bind replayed the cached source-hour writer.");
        cold.Sample(coldTime, coldProcess);
        Require(cold.Capture().Samples == saved.Samples + 1 && cold.Capture().SecondsBits == BitConverter.SingleToUInt32Bits(4.25f * 25), "Cold next writer lost its genuine new epoch/current global.");
        Expect<InvalidDataException>(() => cold.Sample(time, coldProcess));
        Expect<InvalidDataException>(() => FalloutImageSpacePhaseClock.Validate(saved with { SecondsBits = saved.SecondsBits + 1 }, phase, time));
        Expect<InvalidOperationException>(() => new FalloutImageSpacePhaseClock(phase, time, process).Restore(saved));
        Exception? otherThread = null;
        var thread = new Thread(() => { try { cold.Sample(coldTime, coldProcess); } catch (Exception error) { otherThread = error; } });
        thread.Start(); thread.Join();
        Require(otherThread is InvalidOperationException, "Another actual managed thread sampled a bound presentation cache.");
        clock.Retire(); Expect<InvalidOperationException>(() => clock.Sample(time, process));
        cold.Retire();
    }

    // Independent authored machine transports. Synthetic addresses have no
    // relationship to an installation or an original instruction location.
    private static byte[] SingleFixture()
    {
        var code = Enumerable.Repeat((byte)0xcc, 1536).ToArray(); const int start = 32;
        Store(code, start, [0x51, 0xf3, 0x0f, 0x10, 5]); Word(code, start + 5, 0x720000);
        Store(code, start + 9, [0xf3, 0x0f, 0x5e, 5]); Word(code, start + 13, 0x710004);
        Store(code, start + 17, [0x8b, 0x41, 0x1c, 0x56, 0x83, 0xec, 0x10, 0x8b, 0x70, 0x0c, 0x8b, 0xce, 0xf3, 0x0f, 0x10, 0x0d]); Word(code, start + 33, 0x730000);
        Store(code, start + 37, [0xf3, 0x0f, 0x59, 5]); Word(code, start + 41, 0x710008);
        Store(code, start + 45, [0xf3, 0x0f, 0x11, 0x4c, 0x24, 0x0c, 0xf3, 0x0f, 0x11, 0x44, 0x24, 0x14, 0xf3, 0x0f, 0x10, 5]); Word(code, start + 61, 0x730004);
        Store(code, start + 65, [0xf3, 0x0f, 0x59, 0xc1, 0xf3, 0x0f, 0x11, 0x44, 0x24, 8,
            0xc7, 0x44, 0x24, 4, 0, 0, 0, 0, 0xc7, 4, 0x24, 0, 0, 0, 0, 0x6a, 0]); Call(code, start + 92, 360);
        Store(code, start + 97, [0xf3, 0x0f, 0x10, 0x4c, 0x24, 4, 0x83, 0xec, 8, 0x0f, 0x5a, 0xc1,
            0xc7, 0x44, 0x24, 4, 0, 0, 0x80, 0x3f, 0xc7, 4, 0x24, 0, 0, 0x80, 0x3f]); Call(code, start + 124, 448);
        Store(code, start + 129, [0x0f, 0x57, 0xc9, 0xf2, 0x0f, 0x5a, 0xc8, 0x51, 0xf3, 0x0f, 0x59, 0x0d]); Word(code, start + 141, 0x730008);
        Store(code, start + 145, [0xf3, 0x0f, 0x11, 0x0c, 0x24, 0xf3, 0x0f, 0x10, 0x4c, 0x24, 0x10, 0x0f, 0x5a, 0xc1]); Call(code, start + 159, 640);
        Store(code, start + 164, [0xf2, 0x0f, 0x5a, 0xc0, 0x51, 0x8b, 0xce, 0xf3, 0x0f, 0x59, 5]); Word(code, start + 175, 0x730008);
        Store(code, start + 179, [0xf3, 0x0f, 0x11, 4, 0x24, 0x6a, 1]); Call(code, start + 186, 360);
        Store(code, start + 191, [0xc7, 5]); Word(code, start + 193, 0x720024); Word(code, start + 197, 0x40000000); Store(code, start + 201, [0xb0, 1, 0x5e, 0x59, 0xc2, 4, 0]);
        Store(code, 360, [0x8b, 0x44, 0x24, 4, 0xf3, 0x0f, 0x10, 0x44, 0x24, 8, 0x8d, 0x14, 0x85, 0, 0, 0, 0, 0x8b, 0x41, 0x44,
            0xf3, 0x0f, 0x11, 4, 0x90, 0xf3, 0x0f, 0x10, 0x44, 0x24, 0x0c, 0xf3, 0x0f, 0x11, 0x44, 0x90, 4,
            0xf3, 0x0f, 0x10, 0x44, 0x24, 0x10, 0xf3, 0x0f, 0x11, 0x44, 0x90, 8, 0xf3, 0x0f, 0x10, 0x44, 0x24, 0x14, 0xf3, 0x0f, 0x11, 0x44, 0x90, 0x0c, 0xc2, 0x14, 0]);
        foreach (var (at, fallback, primitive, operation) in new[] { (448, 560, 800, (byte)0xff), (640, 752, 864, (byte)0xfe) })
        {
            Store(code, at, [0x83, 0xec, 8, 0x0f, 0xae, 0x5c, 0x24, 4, 0x8b, 0x44, 0x24, 4, 0x25, 0x80, 0x7f, 0, 0,
                0x3d, 0x80, 0x1f, 0, 0, 0x75, 0x0f, 0xd9, 0x3c, 0x24, 0x66, 0x8b, 4, 0x24, 0x66, 0x83, 0xe0, 0x7f, 0x66, 0x83, 0xf8, 0x7f, 0x8d, 0x64, 0x24, 8, 0x0f, 0x85]);
            Word(code, at + 45, unchecked((uint)(fallback - at - 49)));
            Store(code, fallback, [0x83, 0xec, 8, 0x66, 0x0f, 0xd6, 4, 0x24]); Call(code, fallback + 8, primitive);
            Store(code, fallback + 13, [0xdd, 0x1c, 0x24, 0xf3, 0x0f, 0x7e, 4, 0x24, 0x83, 0xc4, 8, 0xc3]);
            Store(code, primitive, [0x8d, 0x54, 0x24, 4]); Call(code, primitive + 4, 928);
            Store(code, primitive + 9, [0x52, 0x9b, 0xd9, 0x3c, 0x24, 0x74, 0x49, 0x66, 0x81, 0x3c, 0x24, 0x7f, 2, 0x74, 6, 0xd9, 0x2d]); Word(code, primitive + 26, 0x730020);
            Store(code, primitive + 30, [0xd9, operation, 0x9b, 0xdf, 0xe0, 0x9e]);
        }
        Store(code, 928, [0x8b, 0x42, 4, 0x25, 0, 0, 0xf0, 0x7f, 0x3d, 0, 0, 0xf0, 0x7f, 0x74, 3, 0xdd, 2, 0xc3]);
        Store(code, 960, [0x83, 0x3d]); Word(code, 962, 0x720020); Store(code, 966, [0, 0x74, 0x14, 0x56, 0xff, 0x15]); Word(code, 972, 0x720020);
        Store(code, 976, [0xd9, 0x5d, 0xf8, 0xf3, 0x0f, 0x10, 0x45, 0xf8, 0x83, 0xc4, 4, 0xeb, 3, 0x0f, 0x57, 0xc0, 0xf3, 0x0f, 0x11, 4, 0xb5]); Word(code, 997, 0x720000);
        Store(code, 1001, [0x0f, 0x57, 0xc9, 0x46, 0x83, 0xfe, 4, 0x7c, 0xce]);
        Store(code, 1040, [0x55, 0x8b, 0xec, 0x8b, 0x45, 8, 0x83, 0xf8, 3, 0x74, 0x4b, 0x83, 0xf8, 2, 0x75, 0x38]);
        Store(code, 1112, [0x83, 0xf8, 1, 0x75, 0x11]); Store(code, 1134, [0x85, 0xc0, 0x75, 0x16, 0xb9]); Word(code, 1139, 0x730030); Call(code, 1143, 1280);
        Store(code, 1148, [0xd9, 5]); Word(code, 1150, 0x71000c); Store(code, 1154, [0xdc, 0xc9, 0xde, 0xc9, 0x5d, 0xc3, 0xd9, 0xee, 0x5d, 0xc3]);
        Store(code, 1280, [0x55, 0x8b, 0xec, 0x51, 0x8b, 0x41, 0x0c, 0x85, 0xc0, 0x74, 0x11, 0xf3, 0x0f, 0x10, 0x40, 0x24,
            0xf3, 0x0f, 0x11, 0x45, 0xfc, 0xd9, 0x45, 0xfc, 0x8b, 0xe5, 0x5d, 0xc3, 0xc7, 0x45, 0xfc]); Word(code, 1311, 0x41400000); Store(code, 1315, [0xd9, 0x45, 0xfc, 0x8b, 0xe5, 0x5d, 0xc3]);
        Store(code, 1390, [0xc7, 5]); Word(code, 1392, 0x720020); Word(code, 1396, 0x600000 + 1040);
        return code;
    }
    private static void Call(byte[] bytes, int at, int target)
    { bytes[at] = 0xe8; Word(bytes, at + 1, unchecked((uint)(target - at - 5))); }
}
