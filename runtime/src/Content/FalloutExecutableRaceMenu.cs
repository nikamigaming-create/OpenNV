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
    internal static string ReadNativeRaceMenuModel(string executable)
    {
        var (code, image) = Load(executable);
        return ReadNativeRaceMenuModel(code, ControlDescriptors(code, image), image.Literal, image.ScriptCommandStart("ShowRaceMenu", code, 0));
    }

    internal static string ReadNativeRaceMenuModel(ReadOnlyMemory<byte> source, IReadOnlyDictionary<uint, string> settings,
        Func<uint, string?> literal, int command)
    {
        var code = source.Span;
        var headers = CreationHeaders(code, settings);
        var start = code[..headers.Position].LastIndexOf(new byte[] { 0x55, 0x8b, 0xec });
        if (start < 0) throw new NotSupportedException("Race-menu creation constructor is unbound.");
        var tail = code[headers.Position..];
        var extent = tail.IndexOf(new byte[] { 0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0 });
        if (extent < 0) throw new NotSupportedException("Race-menu creation constructor has no admitted boundary.");
        var body = code[start..(headers.Position + extent)];
        var (modes, dispatcher) = RaceMenuCommandModes(code, command);
        if (!RaceMenuForwardsArgument(code, dispatcher, start, []))
            throw new NotSupportedException("Race-menu command does not forward its mode to the creation constructor.");
        var arm = RaceMenuModelArm(body, modes);
        body = body[arm.Start..arm.End];
        string? result = null;
        for (var at = 0; at <= body.Length - 7; at++)
        {
            var input = body[at..];
            string? model = null;
            if (input.Length >= 24 && input[..3].SequenceEqual(new byte[] { 0x6a, 0, 0x68 }) &&
                input.Slice(7, 2).SequenceEqual(new byte[] { 0x8b, 0x8d }) &&
                input.Slice(13, 2).SequenceEqual(new byte[] { 0x81, 0xc1 }) && input[19] == 0xe8)
            {
                var name = literal(U32(input, 3));
                if (name?.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) == true) model = MeshPath(name);
            }
            else if (input[..3].SequenceEqual(new byte[] { 0x0f, 0x10, 0x05 }))
                model = ReadRaceMenuVectorCopy(input, literal);
            if (model is null) continue;
            if (result is not null && result != model) throw new InvalidDataException("Race-menu creation models are ambiguous.");
            result = model;
        }
        return result ?? throw new NotSupportedException("Race-menu has no admitted owned model consumer.");
    }

    private static (IReadOnlySet<int> Modes, int Dispatcher) RaceMenuCommandModes(ReadOnlySpan<byte> code, int command)
    {
        if (command < 0 || command > code.Length - 10) throw new InvalidDataException("Race-menu command exceeds source code.");
        var body = code[command..];
        var plain = body.IndexOf(new byte[] { 0xb0, 1, 0xc3 });
        var framed = body.IndexOf(new byte[] { 0xb0, 1, 0x5d, 0xc3 });
        var end = plain < 0 ? framed : framed < 0 ? plain : Math.Min(plain, framed);
        if (end < 0) throw new NotSupportedException("Race-menu command has no admitted return boundary.");
        body = body[..end];
        var modes = new HashSet<int>(); int? dispatcher = null;
        for (var at = 0; at <= body.Length - 10; at++)
        {
            var input = body[at..];
            if (input[0] != 0x6a || input[2] != 0xe8 || !input.Slice(7, 3).SequenceEqual(new byte[] { 0x83, 0xc4, 4 })) continue;
            var target = command + at + 7 + unchecked((int)U32(input, 3));
            if (dispatcher is not null && dispatcher != target) throw new InvalidDataException("Race-menu mode dispatch has multiple owners.");
            dispatcher = target; modes.Add(unchecked((sbyte)input[1]));
        }
        return modes.Count != 0 && dispatcher is not null ? (modes, dispatcher.Value) :
            throw new NotSupportedException("Race-menu command has no admitted mode dispatch.");
    }

    private static bool RaceMenuForwardsArgument(ReadOnlySpan<byte> code, int function, int constructor, HashSet<int> visited)
    {
        if (function == constructor) return true;
        if (function < 0 || function > code.Length - 3 || !visited.Add(function)) return false;
        var body = code[function..];
        if (!body[..3].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec })) return false;
        var next = body[3..].IndexOf(new byte[] { 0x55, 0x8b, 0xec });
        if (next < 0) return false;
        body = body[..(next + 3)];
        for (var at = 0; at <= body.Length - 6; at++)
        {
            var input = body[at..]; int? call = null;
            if (input[0] == 0x5d && input[1] == 0xe9)
            {
                var target = function + at + 6 + unchecked((int)U32(input, 2));
                if (RaceMenuForwardsArgument(code, target, constructor, visited)) return true;
            }
            else if (input.Length >= 14 && input[..3].SequenceEqual(new byte[] { 0xff, 0x75, 8 }) &&
                input[3] == 0x8b && input[4] == 0x0d && input[9] == 0xe8) call = at + 9;
            else if (input.Length >= 9 && input[0] == 0x8b && (input[1] & 0xc7) == 0x45 && input[2] == 8 &&
                input[3] == 0x50 + ((input[1] >> 3) & 7))
            {
                if (input[4] == 0xe8) call = at + 4;
                else if (input.Length >= 15 && input[4] == 0x8b && input[5] == 0x0d && input[10] == 0xe8) call = at + 10;
            }
            if (call is { } offset && RaceMenuForwardsArgument(code, function + offset + 5 + unchecked((int)U32(body, offset + 1)), constructor, visited)) return true;
        }
        return false;
    }

    private static (int Start, int End) RaceMenuModelArm(ReadOnlySpan<byte> code, IReadOnlySet<int> modes)
    {
        (int Start, int End)? result = null;
        for (var at = 0; at <= code.Length - 18; at++)
        {
            var input = code[at..];
            int? firstMode = null, secondMode = null, first = null, second = null, normal = null;
            // Repeated subtraction emits the switch's fallthrough arm.
            if (input[..2].SequenceEqual(new byte[] { 0x83, 0xe8 }) && input.Slice(3, 2).SequenceEqual(new byte[] { 0x0f, 0x84 }) &&
                input.Slice(9, 2).SequenceEqual(new byte[] { 0x83, 0xe8 }) && input.Slice(12, 2).SequenceEqual(new byte[] { 0x0f, 0x84 }))
            {
                var prefix = code[..at];
                var argument = prefix.LastIndexOf(new byte[] { 0x8b, 0x45, 8 });
                if (argument < 0 || at - argument > 32) continue;
                firstMode = unchecked((sbyte)input[2]); secondMode = firstMode + unchecked((sbyte)input[11]);
                first = at + 9 + unchecked((int)U32(input, 5)); second = at + 18 + unchecked((int)U32(input, 14)); normal = at + 18;
            }
            // Another compiler spills the same argument before comparing it.
            else if (input.Length >= 18 && input[0] == 0x8b && (input[1] & 0xc7) == 0x45 && input[2] == 8 &&
                input[3] == 0x89 && (input[4] & 0xc7) == 0x85 && (input[1] & 0x38) == (input[4] & 0x38) &&
                input.Slice(9, 2).SequenceEqual(new byte[] { 0x83, 0xbd }) && U32(input, 5) == U32(input, 11))
            {
                firstMode = unchecked((sbyte)input[15]); var cursor = 16;
                if (!RaceMenuEquality(input, cursor)) continue;
                first = at + RaceMenuEqualityTarget(input, ref cursor);
                if (cursor > input.Length - 9 || !input.Slice(cursor, 2).SequenceEqual(new byte[] { 0x83, 0xbd }) || U32(input, 5) != U32(input, cursor + 2))
                    continue;
                secondMode = unchecked((sbyte)input[cursor + 6]); cursor += 7;
                if (!RaceMenuEquality(input, cursor)) continue;
                second = at + RaceMenuEqualityTarget(input, ref cursor);
                if (cursor > input.Length - 5 || input[cursor] != 0xe9)
                    continue;
                normal = at + cursor + 5 + unchecked((int)U32(input, cursor + 1));
            }
            if (normal is null) continue;
            var starts = modes.Select(mode => mode == firstMode ? first!.Value : mode == secondMode ? second!.Value : normal.Value).Distinct().ToArray();
            if (starts.Length != 1) throw new NotSupportedException("Race-menu command modes select different model arms.");
            var boundaries = new[] { first!.Value, second!.Value, normal.Value, code.Length };
            var length = code.Length;
            if (boundaries.Any(value => value < 0 || value > length) || boundaries.Take(3).Distinct().Count() != 3)
                throw new InvalidDataException("Race-menu model branch exceeds its creation constructor.");
            var start = starts[0]; var end = boundaries.Where(value => value > start).Min();
            if (result is not null) throw new InvalidDataException("Race-menu model dispatch is ambiguous.");
            result = (start, end);
        }
        return result ?? throw new NotSupportedException("Race-menu model mode association is unbound.");
    }

    private static int RaceMenuEqualityTarget(ReadOnlySpan<byte> code, ref int cursor)
    {
        if (cursor <= code.Length - 2 && code[cursor] == 0x74)
        { cursor += 2; return cursor + unchecked((sbyte)code[cursor - 1]); }
        if (cursor <= code.Length - 6 && code.Slice(cursor, 2).SequenceEqual(new byte[] { 0x0f, 0x84 }))
        { cursor += 6; return cursor + unchecked((int)U32(code, cursor - 4)); }
        throw new NotSupportedException("Race-menu model mode has no admitted equality branch.");
    }

    private static bool RaceMenuEquality(ReadOnlySpan<byte> code, int cursor) =>
        cursor <= code.Length - 2 && code[cursor] == 0x74 ||
        cursor <= code.Length - 6 && code.Slice(cursor, 2).SequenceEqual(new byte[] { 0x0f, 0x84 });

    // Inline string copies retain their contiguous source and destination
    // extents, including the terminator. A literal with a NIF-looking name is
    // insufficient: it must be copied by the source creation constructor.
    private static string? ReadRaceMenuVectorCopy(ReadOnlySpan<byte> code, Func<uint, string?> literal)
    {
        var address = U32(code, 3);
        var name = literal(address);
        if (name is null || !name.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) return null;
        int? destination = null;
        var cursor = 0; var extent = 0;
        while (cursor <= code.Length - 10 && code.Slice(cursor, 3).SequenceEqual(new byte[] { 0x0f, 0x10, 0x05 }))
        {
            if (U32(code, cursor + 3) != address + extent) throw new InvalidDataException("Race-menu inline model copy has a source gap.");
            cursor += 7;
            if (!code.Slice(cursor, 2).SequenceEqual(new byte[] { 0x0f, 0x11 }))
                throw new NotSupportedException("Race-menu inline model copy has no matching vector store.");
            cursor += 2;
            var target = CopyTarget(code, ref cursor, 0);
            if (destination is null && target.Offset != 0) return null;
            if (target.Offset != extent || destination is not null && destination != target.Register)
                throw new InvalidDataException("Race-menu inline model copy has a destination gap.");
            destination = target.Register; extent += 16;
        }
        if (extent != Encoding.ASCII.GetByteCount(name) || cursor >= code.Length)
            throw new InvalidDataException("Race-menu inline model copy does not cover its owned string.");
        int register;
        if (code[cursor] == 0xa0) { register = 0; cursor++; }
        else if (cursor <= code.Length - 6 && code[cursor] == 0x8a && (code[cursor + 1] & 0xc7) == 5)
        { register = (code[cursor + 1] >> 3) & 7; cursor += 2; }
        else throw new NotSupportedException("Race-menu inline model terminator load is unbound.");
        if (register >= 4 || U32(code, cursor) != address + extent)
            throw new InvalidDataException("Race-menu inline model terminator differs from its source extent.");
        cursor += 4;
        if (cursor >= code.Length || code[cursor++] != 0x88)
            throw new NotSupportedException("Race-menu inline model terminator store is unbound.");
        var terminator = CopyTarget(code, ref cursor, register);
        if (terminator.Register != destination || terminator.Offset != extent)
            throw new InvalidDataException("Race-menu inline model terminator has a foreign destination.");
        return MeshPath(name);
    }

    private static (int Register, int Offset) CopyTarget(ReadOnlySpan<byte> code, ref int cursor, int sourceRegister)
    {
        if (cursor >= code.Length) throw new InvalidDataException("Race-menu inline model store is truncated.");
        var operand = code[cursor++]; var mode = operand >> 6; var basis = operand & 7;
        if (((operand >> 3) & 7) != sourceRegister || mode == 3 || basis == 4 || mode == 0 && basis == 5)
            throw new NotSupportedException("Race-menu inline model store uses an unbound address expression.");
        if (mode == 0) return (basis, 0);
        if (cursor >= code.Length) throw new InvalidDataException("Race-menu inline model displacement is truncated.");
        var displacement = mode == 1 ? unchecked((sbyte)code[cursor]) : unchecked((int)U32(code, cursor));
        cursor += mode == 1 ? 1 : 4;
        return (basis, displacement);
    }

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
