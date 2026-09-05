namespace OpenNV.Runtime.Content;

internal sealed record FalloutLoveTesterPresentation(
    string AnimatedModel, string CabinetModel,
    IReadOnlyList<float> RotationRadians, float WideDepth, float NarrowDepth, float VerticalOffset,
    float LogicalWidthBoundary, float ReferenceSlope, float FieldOfViewMultiplier,
    float LightIntensity, float LightRadiusMultiple,
    IReadOnlyList<string> ForwardSequences, IReadOnlyList<string> BackwardSequences)
{
    internal float HorizontalSlope(float fieldOfView) =>
        MathF.Tan(fieldOfView * FieldOfViewMultiplier * MathF.PI / 180) * ReferenceSlope;

    internal string Transition(int page, int direction)
    {
        if (direction == 1 && page > 0 && page <= ForwardSequences.Count) return ForwardSequences[page - 1];
        if (direction == -1 && page >= 0 && page < BackwardSequences.Count) return BackwardSequences[page];
        throw new ArgumentOutOfRangeException(nameof(page));
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutLoveTesterPresentation ReadLoveTester(string path, IReadOnlyCollection<string> sequences)
    {
        var (code, image) = Load(path);
        return ReadLoveTesterDeclarations(code, image.Literal, image.Read, sequences);
    }

    // Read compiler-emitted resource arguments, numeric declarations and the
    // ordered animation table. No owned code is executed and no addresses or
    // extracted resources become a persistent launch input.
    internal static FalloutLoveTesterPresentation ReadLoveTesterDeclarations(byte[] code,
        Func<uint, string?> literal, Func<uint, int, byte[]> read, IReadOnlyCollection<string> sequences)
    {
        var pushes = new List<(int Offset, string Value)>();
        for (var at = 0; at < code.Length - 5; at++)
            if (code[at] == 0x68 && literal(U32(code, at + 1)) is { Length: > 0 } value)
                pushes.Add((at, value));
        var models = pushes.Where(item => item.Value.EndsWith("NV_VitoMaticVigorTester_Activate.NIF", StringComparison.OrdinalIgnoreCase) &&
            item.Offset >= 10 && code.AsSpan(item.Offset - 10, 10).SequenceEqual(new byte[] { 0x6a, 0, 0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 0 })).ToArray();
        if (models.Length != 1) throw new NotSupportedException("Owned LoveTester model declarations are unbound.");
        var animated = models[0];
        var cabinet = pushes.First(item => item.Offset > animated.Offset && item.Value.EndsWith(".NIF", StringComparison.OrdinalIgnoreCase));
        if (!cabinet.Value.EndsWith("NV_VitoMaticVigorTester_Cabinet.NIF", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Owned LoveTester cabinet declaration is unbound.");
        var initial = pushes.First(item => item.Offset > cabinet.Offset && sequences.Contains(item.Value));
        var cameraName = pushes.First(item => item.Offset > initial.Offset && item.Value == "Surgery3DCamera");
        var cameraStart = cameraName.Offset;
        while (cameraStart > initial.Offset && !code.AsSpan(cameraStart, 5).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x6a, 0xff })) cameraStart--;
        if (cameraStart <= initial.Offset) throw new NotSupportedException("Owned LoveTester camera owner is unbound.");
        float F32(uint address) => BitConverter.ToSingle(read(address, 4));
        float F64(uint address) => (float)BitConverter.ToDouble(read(address, 8));
        List<(int Offset, float Value)> References(int start, int end, byte first, byte second, Func<uint, float> number)
        {
            var values = new List<(int, float)>();
            for (var at = start; at < end - 5; at++)
                if (code[at] == first && code[at + 1] == second) values.Add((at, number(U32(code, at + 2))));
            return values;
        }
        var width = References(cabinet.Offset, initial.Offset, 0xdc, 0x1d, F64);
        if (width.Count != 1) throw new NotSupportedException("Owned LoveTester width branch is unbound.");
        var angles = References(cabinet.Offset, width[0].Offset, 0xd9, 0x05, F32).Select(item => item.Value).ToArray();
        var offsets = References(width[0].Offset, initial.Offset, 0xd9, 0x05, F32).Select(item => item.Value).ToArray();
        if (angles.Length != 3 || offsets.Length != 8 || offsets[0] != offsets[2] || offsets[1] != offsets[3] ||
            offsets.Skip(4).Any(value => value != offsets[4]) || offsets[1] != offsets[4])
            throw new NotSupportedException("Owned LoveTester transform declaration layout is unbound.");
        var cameraFloats = References(cameraStart, cameraName.Offset, 0xd9, 0x05, F32);
        var cameraFactors = References(cameraStart, cameraName.Offset, 0xdc, 0x0d, F64);
        if (cameraFloats.Count != 1 || cameraFactors.Count != 2 || Math.Abs(cameraFactors[0].Value - MathF.PI / 180) > 1e-7f)
            throw new NotSupportedException("Owned LoveTester projection declaration layout is unbound.");
        var firstButton = pushes.First(item => item.Offset > initial.Offset && item.Value.EndsWith("_Btn:0", StringComparison.Ordinal));
        var lightColors = References(initial.Offset, firstButton.Offset, 0xd9, 0x05, F32);
        var lightRadius = References(initial.Offset, firstButton.Offset, 0xdc, 0x0d, F64);
        if (lightColors.Count != 3 || lightColors.Any(item => item.Value != lightColors[0].Value) || lightRadius.Count != 1)
            throw new NotSupportedException("Owned LoveTester menu light declaration is unbound.");
        if (sequences.Count == 0 || sequences.Count % 2 != 0) throw new InvalidDataException("LoveTester requires paired source sequences.");
        var count = sequences.Count / 2;
        var tables = new List<(uint Address, string[] Names)>();
        for (var at = cameraName.Offset; at < code.Length - 7; at++)
        {
            if (!code.AsSpan(at, 3).SequenceEqual(new byte[] { 0x8b, 0x0c, 0x85 })) continue;
            var address = U32(code, at + 3);
            foreach (var skip in new[] { 0, 1 })
            {
                try
                {
                    var names = Enumerable.Range(skip, count).Select(index => literal(U32(read(address + (uint)(index * 4), 4), 0))).ToArray();
                    if (names.All(name => name is not null && sequences.Contains(name)) && names.Distinct().Count() == count)
                        tables.Add((address + (uint)(skip * 4), names.Select(name => name!).ToArray()));
                }
                catch (InvalidDataException) { /* Non-resource operands are not animation tables. */ }
            }
        }
        var pairs = (from forward in tables.DistinctBy(item => item.Address)
                     from backward in tables.DistinctBy(item => item.Address)
                     where backward.Address == forward.Address + count * 4 && forward.Names[0] == initial.Value &&
                         forward.Names.Concat(backward.Names).Distinct().Count() == sequences.Count
                     select (forward, backward)).ToArray();
        if (pairs.Length != 1) throw new NotSupportedException("Owned LoveTester page animation table is unbound.");
        var numbers = angles.Concat(offsets).Concat([width[0].Value, cameraFloats[0].Value, cameraFactors[1].Value, lightColors[0].Value, lightRadius[0].Value]);
        if (numbers.Any(value => !float.IsFinite(value)) || width[0].Value <= 0 || cameraFloats[0].Value <= 0 ||
            cameraFactors[1].Value <= 0 || lightColors[0].Value < 0 || lightRadius[0].Value <= 0)
            throw new InvalidDataException("Owned LoveTester declaration has invalid numeric values.");
        return new(animated.Value, cabinet.Value, angles, offsets[0], offsets[4], offsets[1], width[0].Value,
            cameraFloats[0].Value, cameraFactors[1].Value, lightColors[0].Value, lightRadius[0].Value,
            pairs[0].forward.Names, pairs[0].backward.Names);
    }
}
