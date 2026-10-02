using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutChallengeHudDeclaration(string Format, float Seconds);

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutChallengeHudDeclaration ReadChallengeHudDeclaration(string path)
    {
        var (code, image) = Load(path);
        var command = image.ScriptCommandStart("IncrementScriptedChallenge", code, 1);
        var body = image.ScriptCommandBody("IncrementScriptedChallenge", code);
        var invocation = body.IndexOf(new byte[] { 0x8b, 0x45, 0xf8, 0x50, 0x8b, 0x4d, 0xfc, 0x51, 0xe8 });
        if (invocation < 0 || invocation + 13 > body.Length)
            throw new NotSupportedException("Scripted challenge dispatch declaration is unbound.");
        var target = checked(command + invocation + 13 + BinaryPrimitives.ReadInt32LittleEndian(body[(invocation + 9)..]));
        if (target < 0 || target >= code.Length) throw new InvalidDataException("Challenge dispatch lies outside source code.");
        var tail = code.AsSpan(target);
        var end = tail.IndexOf(new byte[] { 0x8b, 0xe5, 0x5d, 0xc3 });
        if (end < 0) throw new NotSupportedException("Challenge dispatch boundary is unbound.");
        return ReadChallengeHudDeclaration(tail[..end], image.Literal,
            address => BitConverter.ToSingle(image.Read(address, sizeof(float))));
    }

    internal static FalloutChallengeHudDeclaration ReadChallengeHudDeclaration(ReadOnlySpan<byte> body,
        Func<uint, string?> literal, Func<uint, float> scalar)
    {
        var declarations = new List<FalloutChallengeHudDeclaration>();
        for (var at = 0; at + 10 <= body.Length; at++)
        {
            if (body[at] != 0x68 || body[at + 5] != 0x68 || U32(body, at + 6) != 512 ||
                literal(U32(body, at + 1)) is not { } format || !format.Contains('%')) continue;
            var conversions = System.Text.RegularExpressions.Regex.Matches(format, "%[a-z]").Select(match => match.Value).ToArray();
            if (!conversions.SequenceEqual(new[] { "%s", "%d", "%d", "%s" }))
                throw new NotSupportedException("Challenge notice conversions are unbound.");
            float? seconds = null;
            for (var next = at + 10; next + 6 <= Math.Min(body.Length, at + 48); next++)
                if (body[next] == 0xd9 && body[next + 1] == 0x05) seconds = scalar(U32(body, next + 2));
            if (seconds is not { } duration || !float.IsFinite(duration) || duration <= 0)
                throw new NotSupportedException("Challenge notice duration is unbound.");
            declarations.Add(new(format, duration));
        }
        var unique = declarations.Distinct().ToArray();
        return unique.Length == 1 ? unique[0] : throw new NotSupportedException("Challenge notice declaration is absent or ambiguous.");
    }
}
