using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutDoubleVisionPhase(double SecondsPerHour, double RadiansPerTurn, string SourceSha256)
{
    internal float Angle(float gameHour)
    {
        // The game-time callback publishes Float32 seconds. The effect then
        // stores its converted angle as Float32 before evaluating sin/cos.
        var seconds = (float)(gameHour * SecondsPerHour);
        return (float)(seconds / SecondsPerHour * RadiansPerTurn);
    }
}

internal enum FalloutMenuBackgroundKind { Popup, Interface, Pause, PipBoy }

internal sealed record FalloutMenuBackgroundDeclarations(uint Popup, uint Interface, uint Pause, uint PipBoy,
    uint InterfaceMenu, string ExcludedMenuTileName, string SourceSha256)
{
    internal uint Form(FalloutMenuBackgroundKind kind) => kind switch
    {
        FalloutMenuBackgroundKind.Popup => Popup,
        FalloutMenuBackgroundKind.Interface => Interface,
        FalloutMenuBackgroundKind.Pause => Pause,
        FalloutMenuBackgroundKind.PipBoy => PipBoy,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutMenuBackgroundDeclarations ReadMenuBackgroundDeclarations(string path)
    {
        var (code, image) = Load(path);
        return ReadMenuBackgroundDeclarations(code, image.Literal);
    }

    internal static FalloutMenuBackgroundDeclarations ReadMenuBackgroundDeclarations(ReadOnlySpan<byte> code,
        Func<uint, string?> literal)
    {
        FalloutMenuBackgroundDeclarations? result = null;
        for (var at = 117; at <= code.Length - 51; at++)
        {
            if (code[at] != 0x68) continue;
            var excluded = literal(U32(code, at + 1));
            if (excluded != "Player Name Entry Menu") continue;
            var declaration = code.Slice(at - 117, 168);
            // The admitted selector has three conditional form lookups, then
            // a tile-absence branch. Every returned identity and the interface
            // menu ID are immediate data read from this declaration.
            if (!declaration[..3].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }) ||
                !declaration.Slice(90, 3).SequenceEqual(new byte[] { 0x81, 0x7d, 0xfc }) ||
                declaration[97] != 0x75 || declaration[122] != 0xe8 ||
                !declaration.Slice(141, 4).SequenceEqual(new byte[] { 0x85, 0xc0, 0x75, 0x10 }) ||
                !declaration.Slice(161, 7).SequenceEqual(new byte[] { 0x8b, 0x45, 0xf8, 0x8b, 0xe5, 0x5d, 0xc3 }))
                continue;
            static uint ReadForm(ReadOnlySpan<byte> declaration, int offset)
            {
                var branch = declaration.Slice(offset, 16);
                if (branch[0] != 0x68 || branch[5] != 0xe8 ||
                    !branch.Slice(10, 6).SequenceEqual(new byte[] { 0x83, 0xc4, 0x04, 0x89, 0x45, 0xf8 }))
                    throw new NotSupportedException("Owned menu-background form branch is unbound.");
                var form = U32(branch, 1);
                if (form == 0) throw new InvalidDataException("Owned menu-background declaration has an empty form.");
                return form;
            }
            var pause = ReadForm(declaration, 36); var pipBoy = ReadForm(declaration, 72);
            var menu = ReadForm(declaration, 99); var popup = ReadForm(declaration, 145);
            var interfaceMenu = U32(declaration, 93);
            var resolver = 36 + 10 + unchecked((int)U32(declaration, 42));
            foreach (var offset in new[] { 72, 99, 145 })
                if (offset + 10 + unchecked((int)U32(declaration, offset + 6)) != resolver)
                    throw new NotSupportedException("Owned menu-background branches do not share their form resolver.");
            if (result is not null) throw new InvalidDataException("Owned menu-background selector is ambiguous.");
            var source = new[] { popup, menu, pause, pipBoy, interfaceMenu }.SelectMany(BitConverter.GetBytes)
                .Concat(Encoding.UTF8.GetBytes(excluded)).ToArray();
            result = new(popup, menu, pause, pipBoy, interfaceMenu, excluded,
                Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant());
        }
        return result ?? throw new NotSupportedException("Owned menu-background selector is absent.");
    }

    internal static FalloutDoubleVisionPhase ReadDoubleVisionPhase(string path)
    {
        var (code, image) = Load(path);
        return ReadDoubleVisionPhase(code, address => BitConverter.ToDouble(image.Read(address, sizeof(double))));
    }

    internal static FalloutDoubleVisionPhase ReadDoubleVisionPhase(ReadOnlySpan<byte> code, Func<uint, double> scalar)
    {
        FalloutDoubleVisionPhase? result = null;
        for (var offset = 0; offset <= code.Length - 38; offset++)
        {
            var candidate = code[offset..];
            // Admitted parameter initializer: global Float32 clock, Float64
            // hour divisor, parameter receiver and Float64 angle multiplier.
            // Addresses and both scalar values come from the selected file.
            if (!candidate[..5].SequenceEqual(new byte[] { 0x83, 0xec, 0x08, 0xd9, 0x05 }) ||
                !candidate.Slice(9, 5).SequenceEqual(new byte[] { 0x8b, 0x41, 0x1c, 0xdc, 0x35 }) ||
                !candidate.Slice(18, 11).SequenceEqual(new byte[] { 0x56, 0x8b, 0x70, 0x0c, 0x83, 0xec, 0x10, 0x8b, 0xce, 0xdc, 0x0d }) ||
                !candidate.Slice(33, 4).SequenceEqual(new byte[] { 0xd9, 0x5c, 0x24, 0x14 })) continue;
            var divisor = scalar(U32(candidate, 14));
            var turn = scalar(U32(candidate, 29));
            if (!double.IsFinite(divisor) || divisor <= 0 || !double.IsFinite(turn) || turn <= 0)
                throw new InvalidDataException("Owned double-vision phase declaration is invalid.");
            if (result is not null) throw new InvalidDataException("Owned double-vision phase declaration is ambiguous.");
            var declaration = BitConverter.GetBytes(divisor).Concat(BitConverter.GetBytes(turn)).ToArray();
            result = new(divisor, turn, Convert.ToHexString(SHA256.HashData(declaration)).ToLowerInvariant());
        }
        return result ?? throw new NotSupportedException("Owned double-vision phase declaration is unbound.");
    }
}
