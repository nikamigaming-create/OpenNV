using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Decodes live owned INT bytes into the shared C# procedure interpreter.</summary>
internal static class ClassicNativeIntReader
{
    internal static ClassicIntProgram Read(byte[] bytes, string identity)
    {
        int Word(int offset)
        {
            if (offset < 0 || offset > bytes.Length - 4) throw new InvalidDataException($"Truncated INT {identity} at {offset:x}.");
            return BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
        }
        var count = Word(42);
        if (count <= 0 || count > (bytes.Length - 46) / 24) throw new InvalidDataException("INT procedure table extent is invalid.");
        var identifierBase = checked(46 + count * 24);
        var identifierEnd = checked(identifierBase + 4 + Word(identifierBase));
        if (identifierEnd < identifierBase + 4 || identifierEnd > bytes.Length) throw new InvalidDataException("INT identifiers exceed the source file.");
        string Text(int start, int end)
        {
            if (start < 0 || start >= end || end > bytes.Length) throw new InvalidDataException("INT text reference is outside its table.");
            var length = bytes.AsSpan(start, end - start).IndexOf((byte)0);
            if (length < 0) throw new InvalidDataException("INT text is unterminated.");
            return Encoding.Latin1.GetString(bytes, start, length);
        }
        var declarations = new List<(string Name, int Body, int Arguments)>();
        for (var index = 0; index < count; index++)
        {
            var row = 46 + index * 24;
            var nameOffset = Word(row); var body = Word(row + 16);
            if (nameOffset < 4 || body < identifierEnd || body >= bytes.Length) throw new InvalidDataException("INT procedure identity/body is invalid.");
            var name = Text(checked(identifierBase + nameOffset), identifierEnd);
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("INT procedure has no name.");
            // Preserve ordinary execution declarations; timed/imported procedures
            // need their own dispatch owner before they can enter this interpreter.
            if (Word(row + 4) != 0 || Word(row + 8) != 0 || Word(row + 12) != 0)
                throw new NotSupportedException($"INT procedure declaration requires timed/imported dispatch: {identity}:{name}.");
            var arguments = Word(row + 20);
            if (arguments < 0) throw new InvalidDataException("INT procedure argument count is negative.");
            // An unrelated parameterized helper does not prevent the module's
            // argument-free events from loading. Calls still validate the ABI.
            declarations.Add((name, body, arguments));
        }
        var boundaries = declarations.Select(row => row.Body).Append(bytes.Length).Distinct().Order().ToArray();
        var instructions = new Dictionary<int, ClassicIntInstruction>();
        var procedures = new Dictionary<string, ClassicIntProcedure>(StringComparer.Ordinal);
        ushort[] tail = [0xc001, 0x800d, 0x8019, 0x802a, 0x8029, 0x800c, 0x801c, 0x802a, 0x8029, 0x801c];
        foreach (var row in declarations)
        {
            var end = boundaries.First(offset => offset > row.Body);
            var code = new List<ClassicIntInstruction>();
            for (var offset = row.Body; offset < end;)
            {
                if (end - offset < 2) throw new InvalidDataException("Truncated INT opcode.");
                var start = offset; var opcode = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)); offset += 2;
                int? operand = null;
                if (opcode is 0xc001 or 0x9001 or 0xa001)
                {
                    if (end - offset < 4) throw new InvalidDataException("Truncated INT push operand.");
                    operand = Word(offset); offset += 4;
                }
                var instruction = new ClassicIntInstruction(start, opcode, operand); code.Add(instruction);
                if (!instructions.TryAdd(start, instruction) && instructions[start] != instruction) throw new InvalidDataException("INT aliased bodies disagree.");
            }
            int? epilogue = code.Count >= tail.Length && code[^tail.Length].Operand == 0 &&
                code.TakeLast(tail.Length).Select(item => item.Opcode).SequenceEqual(tail) ? code[^tail.Length].Offset : null;
            if (!procedures.TryAdd(row.Name, new(row.Name, row.Body, epilogue, code) { ArgumentCount = row.Arguments })) throw new InvalidDataException("INT procedure names are duplicated.");
        }
        Dictionary<int, string> References(int table, int end)
        {
            var result = new Dictionary<int, string>();
            foreach (var reference in instructions.Values.Where(row => row.Opcode == 0x9001).Select(row => row.Operand!.Value).Distinct())
                if (reference >= 4 && reference < end - table) result.Add(reference, Text(table + reference, end));
            return result;
        }
        var identifiers = References(identifierBase, identifierEnd);
        if (Word(identifierEnd) != -1) throw new InvalidDataException("INT identifier terminator is absent.");
        var stringBase = checked(identifierEnd + 4);
        var stringLength = Word(stringBase);
        var stringEnd = checked(stringBase + 4 + (stringLength == -1 ? 0 : stringLength));
        if (stringLength < -1 || stringEnd > boundaries[0]) throw new InvalidDataException("INT strings overlap executable bodies.");
        var strings = stringLength == -1 ? new Dictionary<int, string>() : References(stringBase, stringEnd);
        var cursor = stringEnd;
        if (cursor + 4 <= boundaries[0] && Word(cursor) == -1) cursor += 4;
        ushort Opcode()
        {
            if (cursor + 2 > boundaries[0]) throw new InvalidDataException("INT module initialization is truncated.");
            var value = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor)); cursor += 2; return value;
        }
        if (Opcode() != 0x802c) throw new NotSupportedException("INT module requires a different global-frame initializer.");
        var initialVariables = new Dictionary<int, int>();
        ushort initializer;
        while ((initializer = Opcode()) == 0xc001)
        {
            var value = Word(cursor); cursor += 4;
            if (cursor > boundaries[0]) throw new InvalidDataException("INT module initializer exceeds its source region.");
            initialVariables.Add(initialVariables.Count, value);
        }
        if (initializer != 0x8003 || Opcode() != 0xc001) throw new NotSupportedException("INT module initializer has unsupported instructions.");
        var startup = Word(cursor); cursor += 4;
        if (Opcode() != 0x8004 || cursor != boundaries[0]) throw new InvalidDataException("INT module startup jump is invalid.");
        var startupProcedure = procedures.Values.SingleOrDefault(row => row.BodyOffset == startup && row.Name == "start") ??
            throw new NotSupportedException("INT module startup does not resolve to its source start procedure.");
        return new(identity, procedures.Values.ToArray(), procedures, instructions, identifiers, strings)
        { InitialVariables = initialVariables, StartupProcedure = startupProcedure.Name };
    }
}
