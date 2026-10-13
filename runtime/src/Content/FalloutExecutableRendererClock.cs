using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutRendererClockSource ReadRendererClock(string path, FalloutMainScriptCallerSource main)
    {
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != main.EngineSha256)
            throw new InvalidDataException("Renderer clock declaration changed the actual selected executable.");
        var (code, image) = Load(bytes);
        var source = FalloutRendererClockSource.Reviewed(main);
        int? selected = null;
        for (var at = 0; at <= code.Length - 0x263; ++at)
        {
            if (!Match(code, at, [0x80, 0x7c, 0x24, 0x0c, 0, 0xf3, 0x0f, 0x10, 0x44, 0x24, 4, 0xf3, 0x0f, 0x11, 5]) ||
                !Match(code, at + 0x24, [0xf3, 0x0f, 0x10, 0x15]) ||
                !Match(code, at + 0x61, [0xa1]) || !Match(code, at + 0x66, [0x83, 0xf8, 0x0a])) continue;
            if (selected is not null) throw new InvalidDataException("Owned renderer clock publisher is ambiguous.");
            selected = at;
        }
        if (selected is not { } start) throw new NotSupportedException("Owned renderer clock publisher is unbound.");
        RequireClockFlow(code, start);
        uint Loaded(int operand) => Word(image.ReadLoaderData(Word(code, start + operand), 4), 0);
        float Scalar(int operand) => BitConverter.UInt32BitsToSingle(Word(image.Read(Word(code, start + operand), 4), 0));
        var value = source with
        {
            MaximumAccumulatedSeconds = Scalar(0x28),
            DeferredSeconds = Scalar(0x1bd),
            FixedStepFraction = Scalar(0x15f),
            LoadedInputBits = Loaded(0x0f),
            LoadedAccumulatedBits = Loaded(0x41),
            LoadedPreviousBits = Loaded(0x9e),
            LoadedRemainderBits = Loaded(0x33),
            LoadedDeltaBits = Loaded(0x91),
            LoadedSteps = Loaded(0x87),
            LoadedForce = image.ReadLoaderData(Word(code, start + 0x17), 1)[0],
        };
        var table = image.Read(Word(code, start + 0x72), 11);
        var targets = image.Read(Word(code, start + 0x79), 20);
        ReadOnlySpan<int> returned = [0xd7, 0x1b5, 0x1a2, 0x9a, 0x1b5];
        for (var index = 0; index < returned.Length; ++index)
            if (Word(targets, index * 4) != image.CodeBase + start + returned[index])
                throw new NotSupportedException("Owned renderer clock jump target changed its source mode.");
        if (!table.AsSpan().SequenceEqual(new byte[] { 0, 1, 2, 4, 4, 4, 4, 4, 4, 4, 3 }))
            throw new NotSupportedException("Owned renderer clock update-type table is unbound.");
        var settings = ReadStaticSettings(image);
        RequireClockSetting(settings, Word(code, start + 0xdb), "fMaxTime:HAVOK");
        RequireClockStartupCopies(code, Word(code, start + 0x62), Word(code, start + 0xba), settings);
        value = RequireMainClockInputs(code, image, start, value);
        value.Validate(); return value;
    }

    private static void RequireClockFlow(byte[] code, int start)
    {
        ReadOnlySpan<(int At, byte[] Bytes)> arms = [
            (0x15, [0x80, 0x3d]), (0x2f, [0xf3, 0x0f, 0x58, 0x0d]),
            (0x37, [0x0f, 0x57, 0xdb, 0x0f, 0x2f, 0xca]), (0x3d, [0xf3, 0x0f, 0x11, 0x0d]),
            (0x52, [0x0f, 0xb6, 0x44, 0x24, 8, 0x83, 0xf0, 1, 0x8d, 0x0c, 0x45, 1, 0, 0, 0]),
            (0x6f, [0x0f, 0xb6, 0x80]), (0x76, [0xff, 0x24, 0x85]),
            (0x85, [0xc7, 5]), (0x8f, [0xc7, 5]), (0x9a, [0xf3, 0x0f, 0x5c, 0x15]),
            (0xb6, [0xf3, 0x0f, 0x59, 0x15]), (0xbe, [0xf3, 0x0f, 0x58, 0x15]),
            (0xd7, [0xf3, 0x0f, 0x10, 0x25]), (0xe5, [0xf3, 0x0f, 0x59, 0x1d]),
            (0xf9, [0xf3, 0x0f, 0x2c, 0xc0]), (0x15b, [0xf3, 0x0f, 0x59, 0x1d]),
            (0x1a2, [0xf3, 0x0f, 0x11, 5]), (0x1b9, [0xf3, 0x0f, 0x10, 5]),
            (0x1fa, [0xf3, 0x0f, 0x5e, 5]), (0x237, [0xf3, 0x0f, 0x5e, 0xd0]),
        ];
        foreach (var arm in arms)
            if (!Match(code, start + arm.At, arm.Bytes))
                throw new NotSupportedException("Owned renderer clock arithmetic/branch consumer is unbound.");
        if (Word(code, start + 0xc2) != Word(code, start + 0x9e) ||
            Word(code, start + 0x1a6) != Word(code, start + 0x91) ||
            Word(code, start + 0x1fe) != Word(code, start + 0xdb))
            throw new NotSupportedException("Owned renderer clock changed an independent source cell association.");
    }

    private static void RequireClockSetting(IReadOnlyList<StaticSetting> settings, uint payload, string name)
    {
        if (settings.Count(row => row.Collection == SettingCollection.Main && row.Name == name && row.Descriptor + 4 == payload) != 1)
            throw new NotSupportedException("Owned renderer clock setting has no exact typed source descriptor: " + name);
    }
    private static void RequireClockStartupCopies(byte[] code, uint mode, uint weight, IReadOnlyList<StaticSetting> settings)
    {
        uint? weightSetting = null, modeSetting = null;
        for (var at = 0; at <= code.Length - 16; ++at)
        {
            if (Match(code, at, [0xf3, 0x0f, 0x10, 5]) && Match(code, at + 8, [0xf3, 0x0f, 0x11, 5]) &&
                Word(code, at + 12) == weight)
            {
                if (weightSetting is not null) throw new InvalidDataException("Renderer smoothing startup copy is ambiguous.");
                weightSetting = Word(code, at + 4);
            }
            if (!Match(code, at, [0xa1]) || !Match(code, at + 5, [0xf3, 0x0f, 0x11, 5]) ||
                !Match(code, at + 13, [0xf3, 0x0f, 0x10, 5]) || at > code.Length - 30 ||
                !Match(code, at + 21, [0xf3, 0x0f, 0x59, 0xc0, 0xa3]) || Word(code, at + 26) != mode) continue;
            if (modeSetting is not null) throw new InvalidDataException("Renderer mode startup copy is ambiguous.");
            modeSetting = Word(code, at + 1);
        }
        RequireClockSetting(settings, weightSetting ?? throw new NotSupportedException("Renderer smoothing startup copy is absent."), "fChaseDeltaMult:HAVOK");
        RequireClockSetting(settings, modeSetting ?? throw new NotSupportedException("Renderer mode startup copy is absent."), "iUpdateType:HAVOK");
    }

    private static FalloutRendererClockSource RequireMainClockInputs(byte[] code, Image image, int renderer, FalloutRendererClockSource source)
    {
        int? caller = null;
        for (var at = 0x46; at <= code.Length - 50; ++at)
        {
            if (!Match(code, at, [0x83, 0x3d]) || code[at + 6] != 0 ||
                !Match(code, at + 7, [0x74, 0x14, 0x56, 0xff, 0x15]) ||
                !Match(code, at + 16, [0xd9, 0x5d, 0xf8, 0xf3, 0x0f, 0x10, 0x45, 0xf8, 0x83, 0xc4, 4]) ||
                !Match(code, at + 41, [0x0f, 0x57, 0xc9, 0x46, 0x83, 0xfe, 4, 0x7c, 0xce])) continue;
            var head = at - 0x46;
            if (!Match(code, head, [0x83, 0x3d]) || code[head + 6] != 4 ||
                !Match(code, head + 0x22, [0x0f, 0xb6, 0xc0, 0x50, 0x0f, 0xb6, 5]) ||
                !Match(code, head + 0x2d, [0x50, 0x51, 0xf3, 0x0f, 0x11, 4, 0x24]) ||
                Target(code, image.CodeBase, head + 0x34) != image.CodeBase + renderer) continue;
            if (caller is not null) throw new InvalidDataException("Renderer clock has ambiguous Main callers.");
            caller = head;
        }
        if (caller is not { } selected) throw new NotSupportedException("Renderer clock has no exact original Main input stores.");
        // The second argument is the SetSceneIsComplex-owned byte. It is distinct
        // from the later cached interface menu byte used by animation time.
        var sceneComplexity = Word(code, selected + 0x29);
        if (image.ReadLoaderData(sceneComplexity, 1)[0] != 0)
            throw new NotSupportedException("Renderer clock scene-complexity constructor changed its actual source byte.");
        RequireClockSceneComplexity(code, image, sceneComplexity);
        RequirePlayerScalarTimerAssociation(code, image, Word(code, selected + 0x1e));
        return RequireRendererChannels(code, image, selected + 0x46, Word(code, selected + 0x1e), source) with
        { LoadedMainMode = Word(image.ReadLoaderData(Word(code, selected + 2), 4), 0) };
    }
}
