namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeSpecialAllocation(int RequiredTotal, int MinimumAttribute, int MaximumAttribute)
{
    internal void Validate(FalloutNativeSpecialState state, bool allowUnspent = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        var total = state.Values.Sum(value => (long)value);
        if (MinimumAttribute > MaximumAttribute ||
            RequiredTotal < (long)state.Values.Count * MinimumAttribute ||
            RequiredTotal > (long)state.Values.Count * MaximumAttribute ||
            state.Values.Any(value => value < MinimumAttribute || value > MaximumAttribute) ||
            (allowUnspent ? total > RequiredTotal : total != RequiredTotal))
            throw new InvalidDataException("SPECIAL allocation differs from the selected source menu rules.");
    }
}

internal sealed record FalloutLoveTesterSource(uint MenuId, string MenuPath, string AnimatedModel, string CabinetModel,
    int MinimumAttribute, int MaximumAttribute,
    Func<IReadOnlyCollection<string>, FalloutLoveTesterPresentation> ReadPresentation)
{
    internal FalloutNativeSpecialAllocation Allocate(int requiredTotal) =>
        new(requiredTotal, MinimumAttribute, MaximumAttribute);
}

internal static partial class FalloutExecutableStringTable
{
    internal sealed record LoveTesterBootstrap(uint MenuId, string MenuPath, uint MenuSingleton,
        int InitializerStart, int InitializerEnd, int CameraStart, int CameraEnd,
        (int Offset, string Value) Animated, (int Offset, string Value) Cabinet);

    internal static FalloutLoveTesterSource ReadLoveTesterSource(string path)
    {
        var (code, image) = Load(path);
        var bootstrap = ReadLoveTesterBootstrap(code, image);
        var (minimum, maximum) = ReadLoveTesterAttributeBounds(code, bootstrap.MenuSingleton);
        return new(bootstrap.MenuId, OwnedMenuPath(bootstrap.MenuPath), OwnedLoveTesterMesh(bootstrap.Animated.Value),
            OwnedLoveTesterMesh(bootstrap.Cabinet.Value), minimum, maximum,
            sequences => ReadLoveTesterDeclarations(code, image.Literal, image.Read, sequences, bootstrap));
    }

    private static LoveTesterBootstrap ReadLoveTesterBootstrap(byte[] code, Image image)
    {
        var commandStart = image.ScriptCommandStart("ShowLoveTesterMenuParams", code, 1);
        var command = LoveTesterBody(code, commandStart);
        var openers = new List<int>();
        for (var at = commandStart; at <= command.End - 12; at++)
        {
            var input = code.AsSpan(at);
            // The actual parsed integer local is forwarded to the menu factory.
            if (input[..2].SequenceEqual(new byte[] { 0x8b, 0x45 }) && input[3] == 0x50 && input[4] == 0xe8 &&
                input.Slice(9, 3).SequenceEqual(new byte[] { 0x83, 0xc4, 4 }))
                openers.Add(LoveTesterCallTarget(code, at + 4));
        }
        if (openers.Count != 1) throw new NotSupportedException("SPECIAL menu integer forwarding is unbound.");
        var factory = LoveTesterBody(code, openers[0]);
        var xml = new List<string>();
        var identities = new List<uint>();
        uint? singleton = null;
        int? initializer = null, camera = null;
        for (var at = factory.Start; at <= factory.End - 15; at++)
        {
            var input = code.AsSpan(at);
            if (input[0] == 0x68 && image.Literal(U32(input, 1)) is { } literal &&
                literal.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) xml.Add(literal);
            // The admitted menu's virtual type identity is compared with its source getter.
            if (input[..5].SequenceEqual(new byte[] { 0xff, 0xd0, 0x8b, 0xf0, 0xe8 }) &&
                input.Slice(9, 2).SequenceEqual(new byte[] { 0x3b, 0xf0 }))
            {
                var target = LoveTesterCallTarget(code, at + 4);
                if (target > code.Length - 6 || code[target] != 0xb8 || code[target + 5] != 0xc3)
                    throw new NotSupportedException("SPECIAL menu type declaration is unbound.");
                identities.Add(U32(code, target + 1));
            }
            // The factory writes its integer argument into the actual menu singleton,
            // then calls that same object's model and camera owners.
            if (input[0] != 0xa1 || !input.Slice(5, 5).SequenceEqual(new byte[] { 0x8b, 0x4d, 8, 0x89, 0x48 })) continue;
            if (singleton is not null) throw new InvalidDataException("SPECIAL menu request has ambiguous owners.");
            singleton = U32(input, 1);
            var calls = new List<int>();
            for (var next = at + 11; next <= factory.End - 11; next++)
            {
                var candidate = code.AsSpan(next);
                if (candidate[..2].SequenceEqual(new byte[] { 0x8b, 0x0d }) && U32(candidate, 2) == singleton && candidate[6] == 0xe8)
                    calls.Add(LoveTesterCallTarget(code, next + 6));
            }
            if (calls.Count < 2) throw new NotSupportedException("SPECIAL menu presentation dispatch is unbound.");
            initializer = calls[0]; camera = calls[1];
        }
        if (xml.Count != 1 || identities.Count != 1 || identities[0] <= 0 ||
            singleton is null || initializer is null || camera is null)
            throw new NotSupportedException("SPECIAL menu factory declarations are incomplete.");
        var modelsOwner = LoveTesterBody(code, initializer.Value);
        var cameraOwner = LoveTesterBody(code, camera.Value);
        var models = new List<(int Offset, string Value)>();
        for (var at = modelsOwner.Start + 10; at <= modelsOwner.End - 5; at++)
            if (code[at] == 0x68 && code.AsSpan(at - 10, 10).SequenceEqual(new byte[] { 0x6a, 0, 0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 0 }) &&
                image.Literal(U32(code, at + 1)) is { } model && model.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                models.Add((at, model));
        if (models.Count != 2 || models[0].Value.Equals(models[1].Value, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SPECIAL menu model arguments are unbound.");
        return new(identities[0], xml[0], singleton.Value, modelsOwner.Start, modelsOwner.End,
            cameraOwner.Start, cameraOwner.End, models[0], models[1]);
    }

    private static (int Minimum, int Maximum) ReadLoveTesterAttributeBounds(byte[] code, uint singleton)
    {
        var minima = new HashSet<int>(); var maxima = new HashSet<int>();
        for (var at = 3; at <= code.Length - 6; at++)
        {
            var input = code.AsSpan(at);
            // Original menu button consumers compare the same permanent-value
            // getter's integer result and publish disabled state at either bound.
            if (!input[..4].SequenceEqual(new byte[] { 0xff, 0xd0, 0x83, 0xf8 }) || input[5] is not (0x7e or 0x7d) ||
                code[at - 3] != 0x8b || code[at - 2] != 0x42) continue;
            var start = code.AsSpan(0, at).LastIndexOf(new byte[] { 0x55, 0x8b, 0xec });
            if (start < 0) continue;
            var next = code.AsSpan(start + 3).IndexOf(new byte[] { 0x55, 0x8b, 0xec });
            var provisionalEnd = next < 0 ? code.Length : start + 3 + next;
            if (!LoveTesterReferencesSingleton(code.AsSpan(start, provisionalEnd - start), singleton)) continue;
            var owner = LoveTesterBody(code, start);
            if (at >= owner.End || !LoveTesterReferencesSingleton(code.AsSpan(start, owner.End - start), singleton)) continue;
            if (input[5] == 0x7e) minima.Add(unchecked((sbyte)input[4]));
            else maxima.Add(unchecked((sbyte)input[4]));
        }
        if (minima.Count != 1 || maxima.Count != 1 || minima.Single() > maxima.Single())
            throw new NotSupportedException("SPECIAL menu attribute bounds are unbound or inconsistent.");
        return (minima.Single(), maxima.Single());
    }

    private static bool LoveTesterReferencesSingleton(ReadOnlySpan<byte> body, uint singleton)
    {
        for (var at = 0; at <= body.Length - 6; at++)
            if (body[at] == 0xa1 && U32(body, at + 1) == singleton ||
                body[at] == 0x8b && body[at + 1] is 0x0d or 0x15 && U32(body, at + 2) == singleton) return true;
        return false;
    }

    private static (int Start, int End) LoveTesterBody(byte[] code, int start)
    {
        if (start < 0 || start > code.Length - 3 || !code.AsSpan(start, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }))
            throw new NotSupportedException("SPECIAL menu owner has an unbound native frame.");
        var end = code.AsSpan(start).IndexOf(new byte[] { 0x8b, 0xe5, 0x5d, 0xc3 });
        if (end < 0) throw new NotSupportedException("SPECIAL menu owner has no admitted return boundary.");
        return (start, checked(start + end + 4));
    }

    private static int LoveTesterCallTarget(byte[] code, int call)
    {
        if (call < 0 || call > code.Length - 5 || code[call] != 0xe8) throw new InvalidDataException("SPECIAL menu call exceeds source code.");
        var target = (long)call + 5 + unchecked((int)U32(code, call + 1));
        if (target < 0 || target >= code.Length) throw new InvalidDataException("SPECIAL menu call leaves source code.");
        return checked((int)target);
    }

    private static string OwnedMenuPath(string path)
    {
        path = path.Replace('\\', '/');
        return path.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) ? path[5..] : path;
    }

    private static string OwnedLoveTesterMesh(string path)
    {
        path = OwnedMenuPath(path);
        return MeshPath(path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ? path[7..] : path);
    }
}
