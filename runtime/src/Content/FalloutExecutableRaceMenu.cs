using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRaceMenuDevices(string DefaultModel, string GeneProjectorModel)
{
    internal string ModelFor(string command) => command.ToLowerInvariant() switch
    {
        "showracemenu" => DefaultModel,
        "ttw_showgeneprojector" => GeneProjectorModel,
        _ => throw new NotSupportedException("Race-menu model command has no source declaration."),
    };
}

internal static partial class FalloutExecutableStringTable
{
    // Read the plugin's declarations; never load its executable code. Both
    // selectors must copy into the same source buffer and forward to the
    // executable's declared command. The source patch associates that buffer
    // with the original model-path operand, independently of a build address.
    internal static FalloutRaceMenuDevices ReadTtwRaceMenuDevices(string executable, byte[] plugin)
    {
        var (nativeCode, nativeImage) = Load(executable);
        var (code, image) = Load(plugin);
        var command = image.ScriptCommandStart("TTW_ShowGeneProjector", code, 0, registration: true);
        return ReadTtwRaceMenuAssociations(code, image.CodeBase, command, image.Literal,
            image.IsWritableExtent, (address, count) => ReadAsciiBuffer(image.Read(address, count)), image.ImportName,
            nativeCode, nativeImage.CodeBase,
            checked(nativeImage.CodeBase + (uint)nativeImage.ScriptCommandStart("ShowRaceMenu", nativeCode, 0)), nativeImage.Literal);
    }

    internal static FalloutRaceMenuDevices ReadTtwRaceMenuAssociations(ReadOnlySpan<byte> code, uint codeBase, int command,
        Func<uint, string?> literal, Func<uint, int, bool> writable, Func<uint, int, string> initialBuffer,
        Func<uint, string?> import, ReadOnlySpan<byte> nativeCode, uint nativeBase, uint nativeHandler, Func<uint, string?> nativeLiteral)
    {
        var gene = ReadRaceMenuSelector(code, command, literal, writable, import);
        if (gene.Handler != nativeHandler) throw new InvalidDataException("Gene projector forwards to a foreign native command.");
        int? defaultCommand = null;
        for (var at = 0; at <= code.Length - 40; ++at)
        {
            var owner = code[at..];
            if (!owner[..6].SequenceEqual(new byte[] { 0x6a, 3, 0xff, 0x57, 0x18, 0x68 }) ||
                literal(U32(owner, 6)) != "ShowRaceMenu" || owner[10] != 0xa3 ||
                !owner.Slice(15, 6).SequenceEqual(new byte[] { 0xff, 0x50, 0x10, 0x83, 0xc4, 0x14 }) ||
                !owner.Slice(21, 8).SequenceEqual(new byte[] { 0xbe, 0xeb, 0, 0, 0, 0x8b, 0xd6, 0xb9 }) ||
                !owner.Slice(33, 3).SequenceEqual(new byte[] { 0xc7, 0x40, 0x18 })) continue;
            if (!writable(U32(owner, 11), 4)) throw new InvalidDataException("Race-menu registration has no source interface slot.");
            var address = U32(owner, 36);
            if (address < codeBase || (ulong)address - codeBase > (ulong)code.Length - 62)
                throw new InvalidDataException("Race-menu replacement handler exceeds its source code.");
            if (defaultCommand is not null) throw new InvalidDataException("Race-menu selector registration is ambiguous.");
            defaultCommand = checked((int)(address - codeBase));
        }
        if (defaultCommand is null) throw new NotSupportedException("Race-menu default selector has no admitted registration.");
        var normal = ReadRaceMenuSelector(code, defaultCommand.Value, literal, writable, import);
        if (normal.Buffer != gene.Buffer || normal.Capacity != gene.Capacity || normal.Copy != gene.Copy || normal.Handler != gene.Handler)
            throw new InvalidDataException("Race-menu selectors do not share their source buffer and command.");
        if (MeshPath(initialBuffer(gene.Buffer, gene.Capacity)) != normal.Model)
            throw new InvalidDataException("Race-menu initial buffer differs from its default selector.");
        uint? modelOperand = null;
        for (var at = 0; at <= code.Length - 15; ++at)
        {
            var patch = code[at..];
            if (patch[0] != 0xba || U32(patch, 1) != gene.Buffer || patch[5] != 0xb9 || patch[10] != 0xe8) continue;
            var writer = TargetOffset(code, codeBase, at + 11, 47);
            var body = code[writer..];
            if (!body[..6].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0x53, 0x57 }) ||
                !body.Slice(6, 7).SequenceEqual(new byte[] { 0x8d, 0x45, 0xfc, 0x8b, 0xd9, 0x50, 0x6a }) ||
                body[13] != 0x40 || !body.Slice(14, 7).SequenceEqual(new byte[] { 0x6a, 4, 0x53, 0x8b, 0xfa, 0xff, 0x15 }) ||
                !body.Slice(25, 5).SequenceEqual(new byte[] { 0x8d, 0x45, 0xfc, 0x89, 0x3b }) ||
                !body.Slice(30, 7).SequenceEqual(new byte[] { 0x50, 0xff, 0x75, 0xfc, 0x6a, 4, 0x53 }) ||
                !body.Slice(37, 2).SequenceEqual(new byte[] { 0xff, 0x15 }) ||
                U32(body, 21) != U32(body, 39) || import(U32(body, 21)) != "VirtualProtect" ||
                !body.Slice(43, 4).SequenceEqual(new byte[] { 0x5f, 0x5b, 0xc9, 0xc3 }))
                throw new NotSupportedException("Race-menu buffer patch writer is unbound.");
            var operand = U32(patch, 6);
            if (nativeCode.Length < 26 || (ulong)operand < (ulong)nativeBase + 3 || (ulong)operand - nativeBase > (ulong)nativeCode.Length - 26)
                throw new InvalidDataException("Race-menu model operand exceeds native code.");
            var source = nativeCode[(checked((int)(operand - nativeBase)) - 3)..];
            if (!source[..3].SequenceEqual(new byte[] { 0x6a, 0, 0x68 }) ||
                !source.Slice(7, 2).SequenceEqual(new byte[] { 0x8b, 0x8d }) ||
                !source.Slice(13, 2).SequenceEqual(new byte[] { 0x81, 0xc1 }) || source[19] != 0xe8 ||
                MeshPath(nativeLiteral(U32(source, 3))) != normal.Model)
                throw new NotSupportedException("Race-menu buffer has no admitted original model consumer.");
            if (modelOperand is not null) throw new InvalidDataException("Race-menu model consumer is ambiguous.");
            modelOperand = operand;
        }
        if (modelOperand is null) throw new NotSupportedException("Race-menu source buffer has no model consumer.");
        return new(normal.Model, gene.Model);
    }

    private static (string Model, uint Buffer, int Capacity, uint Copy, uint Handler) ReadRaceMenuSelector(ReadOnlySpan<byte> code,
        int at, Func<uint, string?> literal, Func<uint, int, bool> writable, Func<uint, string?> import)
    {
        if (at < 0 || at > code.Length - 62) throw new InvalidDataException("Race-menu selector exceeds its source code.");
        var body = code[at..];
        if (!body[..4].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x68 }) || body[8] != 0x68 || body[13] != 0x68 ||
            !body.Slice(18, 2).SequenceEqual(new byte[] { 0xff, 0x15 }) ||
            !body.Slice(24, 4).SequenceEqual(new byte[] { 0xff, 0x75, 0x24, 0xb8 }) ||
            !body.Slice(53, 9).SequenceEqual(new byte[] { 0xff, 0xd0, 0x83, 0xc4, 0x2c, 0xb0, 1, 0x5d, 0xc3 }))
            throw new NotSupportedException("Race-menu copy/forward selector is unbound.");
        for (var index = 0; index < 7; ++index)
            if (!body.Slice(32 + index * 3, 3).SequenceEqual(new byte[] { 0xff, 0x75, (byte)(0x20 - index * 4) }))
                throw new NotSupportedException("Race-menu native command frame is unbound.");
        var capacity = U32(body, 9); var buffer = U32(body, 14); var copy = U32(body, 20);
        if (capacity is < 2 or > 65536 || !writable(buffer, checked((int)capacity)) || import(copy) != "strcpy_s")
            throw new InvalidDataException("Race-menu copy extent, buffer or import is invalid.");
        var name = literal(U32(body, 4));
        if (name is null || Encoding.ASCII.GetByteCount(name) >= capacity) throw new InvalidDataException("Race-menu model exceeds its source buffer.");
        return (MeshPath(name), buffer, (int)capacity, copy, U32(body, 28));
    }

    private static string ReadAsciiBuffer(byte[] data)
    {
        var end = data.AsSpan().IndexOf((byte)0);
        if (end <= 0 || data.AsSpan(0, end).ContainsAnyExceptInRange((byte)32, (byte)126))
            throw new InvalidDataException("Race-menu initial buffer is not a terminated source path.");
        return Encoding.ASCII.GetString(data, 0, end);
    }

    private static string MeshPath(string? value)
    {
        var parts = value?.Replace('\\', '/').Split('/');
        if (parts is null || parts.Length < 2 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." ||
                part.IndexOfAny([':', '*', '?']) >= 0) || !parts[^1].EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Race-menu source model is not a relative NIF resource.");
        return "meshes/" + string.Join('/', parts).ToLowerInvariant();
    }
}
