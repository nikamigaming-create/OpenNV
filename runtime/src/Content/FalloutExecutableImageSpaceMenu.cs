using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceImageSpaceMenuDeclaration(uint Strength, uint InitialStart, uint UnsetBoundary,
    uint AuxiliaryForm, string Source)
{
    internal void Validate()
    {
        if (Strength != BitConverter.SingleToUInt32Bits(1) ||
            InitialStart != BitConverter.SingleToUInt32Bits(FalloutSourceImageSpaceFloat.InitialStart) ||
            UnsetBoundary != BitConverter.SingleToUInt32Bits(FalloutSourceImageSpaceFloat.UnsetStartBoundary) ||
            AuxiliaryForm == 0 || Source.Length != 64 || !Source.All(Uri.IsHexDigit))
            throw new InvalidDataException("Source menu image factory/update declaration changed its admitted fields.");
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutSourceImageSpaceMenuDeclaration ReadStandaloneImageSpaceMenu(string path)
    {
        using (var stream = File.OpenRead(path))
            if (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != FalloutSourceMainFamily.Fallout3)
                throw new NotSupportedException("Menu image caller is not this selected standalone source.");
        var (code, image) = Load(path);
        return ReadStandaloneImageSpaceMenu(code, image.CodeBase, image.Read);
    }

    internal static FalloutSourceImageSpaceMenuDeclaration ReadStandaloneImageSpaceMenu(ReadOnlySpan<byte> code,
        uint codeBase, Func<uint, int, byte[]> read)
    {
        ReadOnlySpan<byte> Body(uint address, int count) => read(address, count);
        FalloutSourceImageSpaceMenuDeclaration? result = null;
        for (var at = 64; at <= code.Length - 28; at++)
        {
            var body = code[at..];
            if (!body[..6].SequenceEqual(new byte[] { 0x6a, 0, 0x51, 0xc7, 4, 0x24 }) || body[10] != 0x50 || body[11] != 0xe8 ||
                !body.Slice(16, 7).SequenceEqual(new byte[] { 0x83, 0xc4, 0x0c, 0x8b, 0xcf, 0x6a, 0 }) || body[23] != 0xe8 ||
                !code.Slice(at - 19, 2).SequenceEqual(new byte[] { 0x50, 0xe8 }) || code[at - 46] != 0x68) continue;
            var factory = Body(MenuCallTarget(code, codeBase, at + 11), 218);
            if (!factory[..3].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }) ||
                !factory.Slice(117, 3).SequenceEqual(new byte[] { 0xc7, 0x46, 0x14 }) ||
                !factory.Slice(126, 2).SequenceEqual(new byte[] { 0xc7, 6 }) ||
                !factory.Slice(160, 4).SequenceEqual(new byte[] { 0xc6, 0x46, 8, 0 }) ||
                !factory.Slice(210, 4).SequenceEqual(new byte[] { 0x8d, 0x49, 0x6c, 0xe8 })) continue;
            var update = MenuCallTarget(code, codeBase, at + 23); var remove = MenuCallTarget(code, codeBase, at - 18); var restore = -1;
            for (var next = at + 28; next <= Math.Min(code.Length - 18, at + 1024); next++)
            {
                var tail = code[next..];
                if (tail[0] != 0x50 || tail[1] != 0xe8 ||
                    !tail.Slice(6, 8).SequenceEqual(new byte[] { 0x83, 0xc4, 4, 0x8b, 0xcf, 0x6a, 0, 0xe8 })) continue;
                if (MenuCallTarget(code, codeBase, next + 1) != remove || MenuCallTarget(code, codeBase, next + 13) != update) continue;
                if (restore >= 0) throw new InvalidDataException("Menu source has ambiguous restoration calls.");
                restore = next;
            }
            if (restore < 0) continue;
            var table = U32(factory, 128);
            var updateVirtual = U32(read(table + 0x90, 4), 0);
            var retirementVirtual = U32(read(table + 0x8c, 4), 0);
            var instanceUpdate = Body(updateVirtual, 55);
            if (!instanceUpdate.Slice(43, 4).SequenceEqual(new byte[] { 0xf3, 0x0f, 0x10, 5 }) ||
                !instanceUpdate.Slice(51, 3).SequenceEqual(new byte[] { 0x0f, 0x2f, 0xc1 })) continue;
            var retirement = Body(retirementVirtual, 25);
            if (retirement[20] != 0xe8) continue;
            var formRetire = retirementVirtual + 25 + unchecked(U32(retirement, 21));
            var predicate = Body(formRetire, 28);
            if (!predicate.Slice(9, 4).SequenceEqual(new byte[] { 0xf3, 0x0f, 0x10, 5 }) ||
                U32(predicate, 13) != U32(instanceUpdate, 47)) continue;
            var strength = U32(body, 6); var initial = U32(factory, 120);
            var boundary = U32(read(U32(instanceUpdate, 47), 4), 0); var auxiliary = U32(code, at - 45);
            var declaration = new[] { strength, initial, boundary, auxiliary }.SelectMany(BitConverter.GetBytes)
                .Concat(Encoding.UTF8.GetBytes("shared-manager-menu-capture-remove-restore/v1")).ToArray();
            var value = new FalloutSourceImageSpaceMenuDeclaration(strength, initial, boundary, auxiliary,
                Convert.ToHexString(SHA256.HashData(declaration)).ToLowerInvariant());
            value.Validate();
            if (result is not null) throw new InvalidDataException("Menu shared-manager caller is ambiguous.");
            result = value;
        }
        return result ?? throw new NotSupportedException("Source menu shared-manager factory/update/restoration declaration is unbound.");
    }

    private static uint MenuCallTarget(ReadOnlySpan<byte> code, uint codeBase, int at) =>
        codeBase + (uint)(at + 5) + unchecked(U32(code, at + 1));
}
