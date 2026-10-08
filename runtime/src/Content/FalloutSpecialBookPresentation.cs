namespace OpenNV.Runtime.Content;

internal sealed record FalloutSpecialBookPresentation(
    string AnimatedModel, int DefaultBudget, float ModelScale, float Depth, float RotationRadians,
    float ReferenceSlope, float FieldOfViewMultiplier, float LightIntensity, float LightRadiusMultiple,
    IReadOnlyList<string> ForwardSequences, IReadOnlyList<string> BackwardSequences)
{
    internal const int MenuId = 1060;
    internal const int ReviewPage = 8;
    internal int LastPage => ForwardSequences.Count;
    internal bool SinglePrecisionProjection { get; init; }
    internal float RadiansMultiplier { get; init; } = MathF.PI / 180;
    internal FalloutSpecialBookTextures? Textures { get; init; }
    internal float HorizontalSlope(float fieldOfView) =>
        SinglePrecisionProjection
            ? (float)Math.Tan(fieldOfView * RadiansMultiplier * FieldOfViewMultiplier) * ReferenceSlope
            : MathF.Tan(fieldOfView * FieldOfViewMultiplier * RadiansMultiplier) * ReferenceSlope;

    internal string Transition(int page, int direction) => direction switch
    {
        1 when page > 0 && page <= LastPage => ForwardSequences[page - 1],
        -1 when page >= 0 && page < LastPage => BackwardSequences[page],
        _ => throw new ArgumentOutOfRangeException(nameof(page)),
    };
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutSpecialBookPresentation ReadSpecialBook(string path, IReadOnlyCollection<string> sequences)
    {
        var (code, image) = Load(path);
        return ReadSpecialBookDeclarations(code, image.Literal, image.Read, sequences) with
        {
            Textures = ReadSpecialBookTextureDeclarations(code, image.Literal),
        };
    }

    // Resource arguments, scalar references and paired sequence tables are
    // read from the owned executable. No native code is executed or exported.
    internal static FalloutSpecialBookPresentation ReadSpecialBookDeclarations(byte[] code,
        Func<uint, string?> literal, Func<uint, int, byte[]> read, IReadOnlyCollection<string> sequences)
    {
        var pushes = new List<(int Offset, string Value)>();
        for (var at = 0; at < code.Length - 5; at++)
            if (code[at] == 0x68 && literal(U32(code, at + 1)) is { Length: > 0 } value) pushes.Add((at, value));
        var models = pushes.Where(item => item.Value.EndsWith("Babybook02.NIF", StringComparison.OrdinalIgnoreCase) &&
            item.Offset >= 10 && code.AsSpan(item.Offset - 10, 10).SequenceEqual(new byte[] { 0x6a, 0, 0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 0 })).ToArray();
        if (models.Length != 1) throw new NotSupportedException("Owned SPECIAL book model declaration is unbound.");
        var model = models[0];
        var initial = pushes.FirstOrDefault(item => item.Offset > model.Offset && sequences.Contains(item.Value));
        if (initial.Value is null) throw new NotSupportedException("Owned SPECIAL book initial sequence is unbound.");
        var camera = pushes.FirstOrDefault(item => item.Offset > initial.Offset && item.Value == "Surgery3DCamera");
        var firstButton = pushes.FirstOrDefault(item => item.Offset > initial.Offset && item.Value.EndsWith("_Btn:0", StringComparison.Ordinal));
        if (camera.Value is null || firstButton.Value is null) throw new NotSupportedException("Owned SPECIAL book camera or button declaration is unbound.");
        var cameraStart = camera.Offset;
        while (cameraStart > firstButton.Offset && !code.AsSpan(cameraStart, 5).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x6a, 0xff })) cameraStart--;
        if (cameraStart <= firstButton.Offset) throw new NotSupportedException("Owned SPECIAL book camera owner is unbound.");
        float F32(uint address) => BitConverter.ToSingle(read(address, 4));
        float F64(uint address) => (float)BitConverter.ToDouble(read(address, 8));
        float[] References(int start, int end, byte first, byte second, Func<uint, float> number)
        {
            var values = new List<float>();
            for (var at = start; at < end - 5; at++)
                if (code[at] == first && code[at + 1] == second) values.Add(number(U32(code, at + 2)));
            return values.ToArray();
        }
        var pose = References(model.Offset, initial.Offset, 0xd9, 0x05, F32);
        var colors = References(initial.Offset, firstButton.Offset, 0xd9, 0x05, F32);
        var radius = References(initial.Offset, firstButton.Offset, 0xdc, 0x0d, F64);
        var slope = References(cameraStart, camera.Offset, 0xd9, 0x05, F32);
        var factors = References(cameraStart, camera.Offset, 0xdc, 0x0d, F64);
        var menus = pushes.Where(item => item.Value.Replace('\\', '/').EndsWith("/chargen/specialbookmenu.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
        var singlePrecision = false;
        int defaultBudget;
        if (pose.Length == 0 && colors.Length == 0 && radius.Length == 0 && slope.Length == 0 && factors.Length == 0)
        {
            var optimized = ReadOptimizedSpecialBook(code, read, model.Offset, initial.Offset, firstButton.Offset,
                cameraStart, camera.Offset, menus.Select(item => item.Offset).ToArray());
            (pose, colors, radius, slope, factors, defaultBudget) = optimized;
            singlePrecision = true;
        }
        else
        {
            if (menus.Length != 1) throw new NotSupportedException("Owned SPECIAL book factory is ambiguous or absent.");
            var factory = menus[0].Offset;
            while (factory > 0 && !code.AsSpan(factory, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec })) factory--;
            var defaults = new List<int>();
            for (var at = Math.Max(0, factory - 32); at < factory - 9; at++)
                if (code.AsSpan(at, 4).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x6a }) && code[at + 5] == 0xe8 &&
                    at + 10 + unchecked((int)U32(code, at + 6)) == factory) defaults.Add((sbyte)code[at + 4]);
            if (defaults.Count != 1) throw new NotSupportedException("Owned SPECIAL book default budget is unbound.");
            defaultBudget = defaults[0];
        }
        if (pose.Length != 3 || colors.Length != 3 || colors.Any(value => value != colors[0]) || radius.Length != 1 ||
            slope.Length != 1 || factors.Length != 2 || Math.Abs(factors[0] - MathF.PI / 180) > 1e-7f)
            throw new NotSupportedException("Owned SPECIAL book transform, light or projection layout is unbound.");

        if (sequences.Count < 18 || sequences.Distinct(StringComparer.Ordinal).Count() != sequences.Count)
            throw new NotSupportedException("SPECIAL book needs the complete eighteen paired source sequences.");
        const int count = 9;
        var tables = new List<(uint Address, string[] Names)>();
        for (var at = camera.Offset; at < code.Length - 7; at++)
        {
            if (code[at] != 0x8b || (code[at + 1] & 0xc7) != 4 || (code[at + 2] & 0xc7) != 0x85) continue;
            var address = U32(code, at + 3);
            foreach (var skip in new[] { 0, 1 })
                try
                {
                    var names = Enumerable.Range(skip, count).Select(index => literal(U32(read(address + (uint)(index * 4), 4), 0))).ToArray();
                    if (names.All(name => name is not null && sequences.Contains(name)) && names.Distinct().Count() == count)
                        tables.Add((address + (uint)(skip * 4), names.Select(name => name!).ToArray()));
                }
                catch (InvalidDataException) { /* Other indexed operands are not source sequence tables. */ }
        }
        var pairs = (from forward in tables.DistinctBy(item => item.Address)
                     from backward in tables.DistinctBy(item => item.Address)
                     where backward.Address + count * 4 == forward.Address && forward.Names[0] == initial.Value &&
                         forward.Names.Concat(backward.Names).Distinct().Count() == count * 2
                     select (forward, backward)).ToArray();
        if (pairs.Length != 1) throw new NotSupportedException("Owned SPECIAL book page tables are unbound.");
        if (pose.Concat(colors).Concat(radius).Concat(slope).Concat(factors).Any(value => !float.IsFinite(value)) ||
            pose[0] <= 0 || slope[0] <= 0 || factors[1] <= 0 || colors[0] < 0 || radius[0] <= 0 ||
            defaultBudget is < FalloutSpecialAllocationSession.AttributeCount or > FalloutSpecialAllocationSession.AttributeCount * FalloutSpecialAllocationSession.Maximum)
            throw new InvalidDataException("Owned SPECIAL book contains invalid numeric declarations.");
        return new(model.Value, defaultBudget, pose[0], pose[1], pose[2], slope[0], factors[1], colors[0], radius[0], pairs[0].forward.Names, pairs[0].backward.Names)
        {
            SinglePrecisionProjection = singlePrecision,
            RadiansMultiplier = factors[0],
        };
    }
}
