using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutTagMenuDeclarations(float ListY, string PluralSuffix);

internal static class FalloutTagMenuDefaults
{
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, FalloutTagMenuDeclarations> Declarations = new();
    internal static FalloutTagMenuDeclarations Read()
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned tag menu declarations are unavailable.");
        return Declarations.GetValue(source, content =>
        {
            if (content.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
                throw new NotSupportedException("This engine's tag menu declaration is unbound.");
            return FalloutExecutableStringTable.ReadTagMenuDeclarations(
                Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe"));
        });
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutTagMenuDeclarations ReadTagMenuDeclarations(string path)
    {
        var (code, image) = Load(path);
        return ReadTagMenuDeclarations(code, image.Literal, ControlDescriptors(code, image),
            address => BitConverter.ToSingle(image.Read(address, sizeof(float))));
    }

    // The skill page assigns a source float to its list Y after selecting the
    // row template. Its remaining-count formatter appends an owned literal for
    // non-singular counts. Neither a build address nor fitted art is an input.
    internal static FalloutTagMenuDeclarations ReadTagMenuDeclarations(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings, Func<uint, float> scalar)
    {
        float? listY = null;
        string? suffix = null;
        for (var at = 0; at <= code.Length - 5; at++)
        {
            if (code[at] == 0x68 && literal(U32(code, at + 1)) == "CGM_SelectItemTemplate")
            {
                var end = Math.Min(code.Length - 23, at + 64);
                for (var next = at + 5; next <= end; next++)
                {
                    var row = code[next..];
                    if (!row[..5].SequenceEqual(new byte[] { 0x6a, 1, 0x51, 0xd9, 0x05 }) ||
                        !row.Slice(9, 4).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x68 }) || U32(row, 13) != 4002 ||
                        row[17] != 0x8b || (row[18] & 0xc7) != 0x45 || row[20] != 0x8b || (row[21] & 0xf8) != 0x48) continue;
                    var value = scalar(U32(row, 5));
                    if (!float.IsFinite(value) || value < 0) throw new InvalidDataException("Owned tag list placement is invalid.");
                    if (listY is not null) throw new InvalidDataException("Owned tag list placement is ambiguous.");
                    listY = value;
                }
            }
            if (at > code.Length - 16 || code[at] != 0xb9 || code[at + 5] != 0xe8 ||
                !settings.TryGetValue(U32(code, at + 1), out var setting) || setting != "sSkillsTitle") continue;
            var tail = code[at..Math.Min(code.Length, at + 400)];
            var countOwner = false;
            for (var next = 10; next <= Math.Min(tail.Length - 10, 64); next++)
                if (tail[next] == 0xb9 && tail[next + 5] == 0xe8 &&
                    settings.TryGetValue(U32(tail, next + 1), out var counter) && counter == "sSkillsCount") countOwner = true;
            if (!countOwner) continue;
            for (var next = 10; next <= tail.Length - 14; next++)
            {
                var row = tail[next..];
                if (row[0] != 0x83 || row[1] != 0xbd || row[6] != 1 || row[7] != 0x74 || row[9] != 0x68) continue;
                var value = literal(U32(row, 10));
                if (string.IsNullOrEmpty(value) || value.Contains('%'))
                    throw new NotSupportedException("Owned tag count suffix needs another format owner.");
                if (suffix is not null) throw new InvalidDataException("Owned tag count suffix is ambiguous.");
                suffix = value;
            }
        }
        return new(listY ?? throw new NotSupportedException("Owned tag list placement is unbound."),
            suffix ?? throw new NotSupportedException("Owned tag count plural declaration is unbound."));
    }
}
