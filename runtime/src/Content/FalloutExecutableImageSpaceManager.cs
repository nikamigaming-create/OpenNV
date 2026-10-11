using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceImageSpaceDefaults(uint[] Words, string DeclarationSha256);

internal static partial class FalloutExecutableStringTable
{
    // Values are read from the selected owned file. The anonymous engine
    // profile is not a FormID and is not the legacy IMGS insertion default.
    internal static FalloutSourceImageSpaceDefaults ReadStandaloneImageSpaceDefaults(string path)
    {
        using (var stream = File.OpenRead(path))
            if (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != FalloutSourceMainFamily.Fallout3)
                throw new NotSupportedException("Image-space manager default constructor is not this selected source.");
        var (code, image) = Load(path);
        return ReadStandaloneImageSpaceDefaults(code, image.ReadLoaderData, image.IsWritableExtent);
    }

    internal static FalloutSourceImageSpaceDefaults ReadStandaloneImageSpaceDefaults(ReadOnlySpan<byte> code,
        Func<uint, int, byte[]> read, Func<uint, int, bool> writable)
    {
        uint? profile = null;
        for (var at = 0; at <= code.Length - 60; at++)
        {
            var body = code.Slice(at, 60);
            if (!body[..13].SequenceEqual(new byte[] { 0x56, 0x8b, 0xb1, 0xb0, 0, 0, 0, 0x57, 0x8d, 0x79, 0x14, 0x85, 0xf6 }) ||
                !body.Slice(13, 12).SequenceEqual(new byte[] { 0x75, 0x0f, 0x8b, 0xb1, 0xac, 0, 0, 0, 0x85, 0xf6, 0x75, 5 }) ||
                body[25] != 0xbe || !body.Slice(30, 10).SequenceEqual(new byte[] { 0xb9, 0x26, 0, 0, 0, 0xf3, 0xa5, 0x5f, 0xc7, 5 }) ||
                U32(body, 44) != 0 || !body.Slice(48, 2).SequenceEqual(new byte[] { 0xc7, 5 }) ||
                U32(body, 54) != 0 || body[58] != 0x5e || body[59] != 0xc3) continue;
            var selected = U32(body, 26);
            if (!writable(selected, 152) || !writable(U32(body, 40), 4) || !writable(U32(body, 50), 4))
                throw new InvalidDataException("Image-space copy/reset fields are not actual writable source cells.");
            if (profile is not null) throw new InvalidDataException("Image-space selector is ambiguous.");
            profile = selected;
        }
        var address = profile ?? throw new NotSupportedException("Image-space selector/copy/reset declaration is absent.");
        var loader = read(address, 152);
        var initial = Enumerable.Range(0, 38).Select(index => BinaryPrimitives.ReadUInt32LittleEndian(loader.AsSpan(index * 4))).ToArray();
        var candidates = new List<uint[]>();
        // A source reset routine has two literal-vector reads followed by the
        // complete ordered scalar stores. Its final return bounds the walk.
        for (var at = 0; at <= code.Length - 15; at++)
        {
            if (!code.Slice(at, 3).SequenceEqual(new byte[] { 0x0f, 0x28, 5 }) ||
                !code.Slice(at + 7, 3).SequenceEqual(new byte[] { 0x0f, 0x11, 5 }) || U32(code, at + 10) != address + 12) continue;
            var words = (uint[])initial.Clone(); var written = new HashSet<int>(); uint[]? vector = null;
            var cursor = at; var returned = false;
            for (var count = 0; count < 64 && cursor < code.Length; count++)
            {
                var rest = code[cursor..];
                if (rest[0] == 0xc3) { returned = true; break; }
                if (rest.Length >= 7 && rest[..3].SequenceEqual(new byte[] { 0x0f, 0x28, 5 }))
                {
                    var bytes = read(U32(rest, 3), 16);
                    vector = Enumerable.Range(0, 4).Select(index => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4))).ToArray();
                    cursor += 7; continue;
                }
                if (rest.Length >= 7 && rest[..3].SequenceEqual(new byte[] { 0x0f, 0x11, 5 }) && vector is not null)
                {
                    var destination = U32(rest, 3);
                    if (destination < address || destination > address + 136 || (destination - address) % 4 != 0) break;
                    var first = checked((int)((destination - address) / 4));
                    if (Enumerable.Range(first, 4).Any(written.Contains)) break;
                    for (var index = 0; index < 4; index++)
                    { var target = first + index; written.Add(target); words[target] = vector[index]; }
                    cursor += 7; continue;
                }
                if (rest.Length >= 10 && rest[0] == 0xc7 && rest[1] == 5)
                {
                    var destination = U32(rest, 2);
                    if (destination < address || destination > address + 148 || (destination - address) % 4 != 0) break;
                    var target = checked((int)((destination - address) / 4));
                    if (!written.Add(target)) break;
                    words[target] = U32(rest, 6); cursor += 10; continue;
                }
                break;
            }
            if (returned && written.SetEquals(Enumerable.Range(0, 38).Where(index => index is not (14 or 37)))) candidates.Add(words);
        }
        if (candidates.Count != 1) throw new NotSupportedException("Image-space anonymous default constructor/reset is absent or ambiguous.");
        var result = candidates[0];
        return new(result, Convert.ToHexString(SHA256.HashData(result.SelectMany(BitConverter.GetBytes).ToArray())).ToLowerInvariant());
    }
}
