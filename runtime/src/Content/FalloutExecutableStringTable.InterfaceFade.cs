using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutInterfaceFadeDeclaration ReadInterfaceFade(string path)
    {
        var original = File.ReadAllBytes(path);
        var sha = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        var arithmetic = sha switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => FalloutInterfaceFadeArithmetic.WideQuotientThenFloat32,
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => FalloutInterfaceFadeArithmetic.Float32EachOperation,
            _ => throw new NotSupportedException("Selected interface fade consumer is unreviewed."),
        };
        var (code, image) = Load(original);
        var rows = arithmetic == FalloutInterfaceFadeArithmetic.WideQuotientThenFloat32 ?
            ReadConstructedFadeCatalog(code, image.Literal, image.IsWritableExtent, image.CodeBase) : image.ReadStaticFadeCatalog();
        var result = new FalloutInterfaceFadeDeclaration(sha, arithmetic, rows); result.Validate(); return result;
    }

    // Read declarations, not source-specific destinations. Every accepted row
    // initializes one complete writable object; a filename alone is no owner.
    internal static IReadOnlyList<FalloutInterfaceFadeCatalogRow> ReadConstructedFadeCatalog(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, Func<uint, int, bool> writable, uint codeBase)
    {
        var rows = new Dictionary<uint, (bool Primary, string Path, uint Constructor)>();
        for (var at = 0; at <= code.Length - 48; ++at)
        {
            var row = code[at..];
            if (row[0] != 0xc6 || row[1] != 0x05 || row[6] > 1 || row[7] != 0xc7 || row[8] != 0x05) continue;
            var target = U32(row, 2);
            if (!writable(target, 28) || U32(row, 9) != target + 4 || FadeTexturePath(literal(U32(row, 13))) is not { } path) continue;
            if (row[17] is not (0x31 or 0x33) || (row[18] & 0xc0) != 0xc0 || (row[18] & 7) != (row[18] >> 3 & 7)) continue;
            var register = row[18] & 7; var cursor = 19; var complete = true;
            for (var field = 8; field <= 20; field += 4)
            {
                if (register == 0 && row[cursor] == 0xa3)
                { complete &= U32(row, cursor + 1) == target + field; cursor += 5; }
                else if (row[cursor] == 0x89 && row[cursor + 1] == (register << 3 | 5))
                { complete &= U32(row, cursor + 2) == target + field; cursor += 6; }
                else { complete = false; break; }
            }
            if (!complete || row.Length < cursor + 12 || row[cursor] != 0x6a || row[cursor + 1] != 0 ||
                row[cursor + 2] != 0xb9 || U32(row, cursor + 3) != target + 24 || row[cursor + 7] != 0xe8) continue;
            var destination = checked((long)codeBase + at + cursor + 12 + unchecked((int)U32(row, cursor + 8)));
            if (destination < codeBase || destination - codeBase >= code.Length) continue;
            if (!rows.TryAdd(target, (row[6] != 0, path, checked((uint)destination))))
                throw new InvalidDataException("Source interface fade initializer is duplicated.");
        }
        IReadOnlyList<FalloutInterfaceFadeCatalogRow>? result = null;
        foreach (var start in rows.Where(row => row.Value.Primary))
        {
            if (!rows.TryGetValue(start.Key + 28, out var second) || !rows.TryGetValue(start.Key + 56, out var third) ||
                second.Primary || third.Primary || start.Value.Constructor != second.Constructor || second.Constructor != third.Constructor ||
                start.Value.Path != second.Path || second.Path == third.Path) continue;
            if (result is not null) throw new InvalidDataException("Source interface fade catalogs are ambiguous.");
            result = [new(0, FalloutInterfaceFadeRoot.Primary, start.Value.Path),
                new(1, FalloutInterfaceFadeRoot.Secondary, second.Path), new(2, FalloutInterfaceFadeRoot.Secondary, third.Path)];
        }
        return result ?? throw new NotSupportedException("Selected constructed interface fade catalog is unbound.");
    }
    private static string? FadeTexturePath(string? filename)
    {
        if (filename is null || filename.Length == 0 || !filename.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
            filename.IndexOfAny(['/', '\\', ':']) >= 0 || filename.Contains("..", StringComparison.Ordinal)) return null;
        return "textures/interface/faders/" + filename.ToLowerInvariant();
    }
    private sealed partial class Image
    {
        internal IReadOnlyList<FalloutInterfaceFadeCatalogRow> ReadStaticFadeCatalog()
        {
            IReadOnlyList<FalloutInterfaceFadeCatalogRow>? found = null;
            foreach (var section in headers.SectionHeaders.Where(section =>
                (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0))
            {
                var data = bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData);
                for (var at = 0; at <= data.Length - 84; at += 4)
                {
                    var candidate = data.Slice(at, 84);
                    if (U32(candidate, 0) != 1 || U32(candidate, 28) != 0 || U32(candidate, 56) != 0 ||
                        candidate.Slice(8, 20).IndexOfAnyExcept((byte)0) >= 0 ||
                        candidate.Slice(36, 20).IndexOfAnyExcept((byte)0) >= 0 ||
                        candidate.Slice(64, 20).IndexOfAnyExcept((byte)0) >= 0) continue;
                    var first = FadeTexturePath(Literal(U32(candidate, 4)));
                    var second = FadeTexturePath(Literal(U32(candidate, 32)));
                    var third = FadeTexturePath(Literal(U32(candidate, 60)));
                    if (first is null || first != second || third is null || second == third) continue;
                    if (found is not null) throw new InvalidDataException("Source static interface fade catalog is ambiguous.");
                    found = [new(0, FalloutInterfaceFadeRoot.Primary, first), new(1, FalloutInterfaceFadeRoot.Secondary, second),
                        new(2, FalloutInterfaceFadeRoot.Secondary, third)];
                }
            }
            return found ?? throw new NotSupportedException("Selected static interface fade catalog is absent.");
        }
    }
}
