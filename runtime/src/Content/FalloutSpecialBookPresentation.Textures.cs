namespace OpenNV.Runtime.Content;

internal sealed record FalloutSpecialBookTextures(IReadOnlyList<string> Digits, IReadOnlyList<string> Messages,
    IReadOnlyDictionary<string, string> Buttons)
{
    internal IEnumerable<string> Paths => Digits.Concat(Messages).Concat(Buttons.Values).Distinct(StringComparer.OrdinalIgnoreCase);
    internal string Message(int remaining) => Messages[remaining > 1 ? 1 : remaining == 1 ? 2 : 3];
}

internal static partial class FalloutExecutableStringTable
{
    // Bind the texture callee reached by this model constructor, its formatted
    // buffers, native loader, receiver fields and indexed resource tables. The
    // Love Tester has similarly named resources and is a different owner.
    internal static FalloutSpecialBookTextures ReadSpecialBookTextureDeclarations(byte[] code, Func<uint, string?> literal)
    {
        var pushes = Enumerable.Range(0, Math.Max(0, code.Length - 5)).Where(at => code[at] == 0x68)
            .Select(at => (At: at, Value: literal(U32(code, at + 1)))).Where(item => item.Value is not null).ToArray();
        var models = pushes.Where(item => item.Value!.EndsWith("Babybook02.NIF", StringComparison.OrdinalIgnoreCase) && item.At >= 10 &&
            code.AsSpan(item.At - 10, 10).SequenceEqual(new byte[] { 0x6a, 0, 0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 0 })).ToArray();
        if (models.Length != 1) throw new NotSupportedException("Owned SPECIAL book texture model owner is unbound.");
        var model = models[0].At;
        var button = pushes.FirstOrDefault(item => item.At > model && item.Value!.EndsWith("_Btn:0", StringComparison.Ordinal)).At;
        var camera = pushes.FirstOrDefault(item => item.At > button && item.Value == "Surgery3DCamera").At;
        if (button <= model || camera <= button) throw new NotSupportedException("Owned SPECIAL book texture caller extent is unbound.");
        var callees = new HashSet<int>();
        for (var at = button; at < camera - 4; at++)
        {
            if (code[at] != 0xe8) continue;
            var target = (long)at + 5 + unchecked((int)U32(code, at + 1));
            if (target < 0 || target > code.Length - 200) continue;
            var start = (int)target;
            if (code.AsSpan(start, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }) &&
                pushes.Any(item => item.At >= start && item.At < start + 200 && item.Value == "BBNumber")) callees.Add(start);
        }
        if (callees.Count != 1) throw new NotSupportedException("Owned SPECIAL book texture callee is ambiguous or absent.");
        var owner = callees.Single();
        var finish = code.AsSpan(owner, Math.Min(4096, code.Length - owner)).IndexOf(new byte[] { 0x8b, 0xe5, 0x5d, 0xc3 });
        if (finish < 0) throw new NotSupportedException("Owned SPECIAL book texture return is unbound.");
        var end = owner + finish + 4;
        var optimized = SpecialBookMatches(code, owner, owner + 32, [0x8b, 0xf9]).Count == 1;
        var receiverSlots = SpecialBookMatches(code, owner, owner + 40, [0x89, 0x8d, -1, -1, -1, -1]);
        if (!optimized && receiverSlots.Count != 1) throw new NotSupportedException("Owned SPECIAL book texture receiver is unbound.");
        var receiver = optimized ? 0 : unchecked((int)U32(code, receiverSlots[0] + 2));
        var names = new[] { "BBNumber", "BBMessage0", "BBRTOff", "BBRTOn", "BBLTOff", "BBLTOn", "BBXOff", "BBXOn",
            "BBDecreaseOff", "BBDecreaseOn", "BBIncreaseOff", "BBIncreaseOn" };
        var declarations = names.Select(name =>
        {
            var found = pushes.Where(item => item.At >= owner && item.At < end && item.Value == name).ToArray();
            if (found.Length != 1) throw new NotSupportedException("Owned SPECIAL book texture declaration is ambiguous or absent: " + name);
            return (Name: name, At: found[0].At);
        }).OrderBy(item => item.At).ToArray();
        var rows = new Dictionary<string, (string Path, int Field, int Count)>(StringComparer.Ordinal);
        int? formatter = null, loader = null, platformFrame = null, pathTransport = null; uint? manager = null;
        for (var index = 0; index < declarations.Length; index++)
        {
            var (name, at) = declarations[index];
            var limit = index + 1 < declarations.Length ? declarations[index + 1].At : end;
            var cursor = at + 5;
            var platform = StackAddress(ref cursor, out var platformSlot, out var platformRegister);
            if (platform) PushRegister(ref cursor, platformRegister);
            var directory = PushLiteral(ref cursor); var format = PushLiteral(ref cursor);
            var numeric = name is "BBNumber" or "BBMessage0";
            if (numeric ? platform || format != "%s%s%i.dds" : platform ? format is not ("%s\\%s\\%s.dds" or "%s%s\\%s.dds") : format != "%s%s.dds")
                throw new NotSupportedException("Owned SPECIAL book texture format is unbound: " + name);
            int buffer, register;
            if (StackAddress(ref cursor, out buffer, out register)) { BufferSize(ref cursor); PushRegister(ref cursor, register); }
            else { BufferSize(ref cursor); if (!StackAddress(ref cursor, out buffer, out register)) throw new NotSupportedException("Owned texture format buffer is unbound."); PushRegister(ref cursor, register); }
            Same(ref formatter, Call(ref cursor));

            var loads = SpecialBookMatches(code, cursor, limit, [0x6a, 0, 0x6a, 0]);
            if (loads.Count != 1) throw new NotSupportedException("Owned SPECIAL book native texture loader is unbound.");
            var load = loads[0]; int field, count = 0; uint nativeManager;
            if (optimized)
            {
                var fields = SpecialBookMatches(code, cursor, load, [0x8b, 0x0d, -1, -1, -1, -1, 0x8d, 0x87, -1, -1, -1, -1]);
                if (fields.Count != 1) throw new NotSupportedException("Owned SPECIAL book texture field/index flow is unbound: " + name);
                var flow = fields[0] + 12;
                // MSVC schedules cdecl stack cleanup either before the manager
                // and field loads or immediately after them. Neither form may
                // interrupt the receiver/index transport to the loader argument.
                if (code.AsSpan(flow, 2).SequenceEqual(new byte[] { 0x83, 0xc4 })) flow += 3;
                else if (fields[0] < cursor + 3 || !code.AsSpan(fields[0] - 3, 2).SequenceEqual(new byte[] { 0x83, 0xc4 }))
                    throw new NotSupportedException("Owned SPECIAL book texture stack cleanup is unbound: " + name);
                if (numeric)
                {
                    if (!code.AsSpan(flow, 3).SequenceEqual(new byte[] { 0x8d, 0x04, 0xb0 }))
                        throw new NotSupportedException("Owned SPECIAL book texture index transport is unbound: " + name);
                    flow += 3;
                }
                if (flow != load) throw new NotSupportedException("Owned SPECIAL book texture field/index flow is unbound: " + name);
                nativeManager = U32(code, fields[0] + 2); field = checked((int)U32(code, fields[0] + 8)); cursor = load + 4;
                PushRegister(ref cursor, 0);
                if (numeric)
                {
                    if (!code.AsSpan(cursor + 12, 4).SequenceEqual(new byte[] { 0x46, 0x83, 0xfe, name == "BBNumber" ? (byte)11 : (byte)5 }))
                        throw new NotSupportedException("Owned SPECIAL book indexed texture range is unbound.");
                    var initializers = name == "BBNumber"
                        ? SpecialBookMatches(code, owner, at, [0x33, 0xf6])
                        : SpecialBookMatches(code, owner, at, [0xbe, 1, 0, 0, 0]);
                    if (initializers.Count != 1) throw new NotSupportedException("Owned SPECIAL book indexed texture origin is unbound.");
                    count = name == "BBNumber" ? 11 : 4;
                }
            }
            else
            {
                cursor = load + 4;
                int indexSlot = 0, indexRegister = 0;
                if (numeric) StackValue(ref cursor, out indexSlot, out indexRegister);
                StackValue(ref cursor, out var sourceSlot, out var sourceRegister);
                if (sourceSlot != receiver) throw new NotSupportedException("Owned SPECIAL book texture field has another receiver.");
                if (numeric)
                {
                    if (code[cursor] != 0x8d || (code[cursor + 1] & 0xc7) != 0x84 ||
                        code[cursor + 2] != (byte)(0x80 | indexRegister << 3 | sourceRegister))
                        throw new NotSupportedException("Owned SPECIAL book indexed field is unbound.");
                    register = code[cursor + 1] >> 3 & 7; field = checked((int)U32(code, cursor + 3)); cursor += 7;
                    var minimum = name == "BBNumber" ? 0 : 1; var maximum = name == "BBNumber" ? 11 : 5;
                    var initializers = SpecialBookMatches(code, owner, at, [0xc7, 0x85, -1, -1, -1, -1, -1, -1, -1, -1])
                        .Where(start => unchecked((int)U32(code, start + 2)) == indexSlot && U32(code, start + 6) == minimum).ToArray();
                    var tests = SpecialBookMatches(code, Math.Max(owner, at - 40), at, [0x83, 0xbd, -1, -1, -1, -1, maximum])
                        .Where(start => unchecked((int)U32(code, start + 2)) == indexSlot).ToArray();
                    if (initializers.Length != 1 || tests.Length != 1) throw new NotSupportedException("Owned SPECIAL book indexed texture range is unbound.");
                    count = maximum - minimum;
                }
                else
                {
                    register = sourceRegister;
                    if (code[cursor] == 0x81 && code[cursor + 1] == 0xc0 + register) { field = checked((int)U32(code, cursor + 2)); cursor += 6; }
                    else if (code[cursor] == 0x83 && code[cursor + 1] == 0xc0 + register) { field = code[cursor + 2]; cursor += 3; }
                    else if (register == 0 && code[cursor] == 0x05) { field = checked((int)U32(code, cursor + 1)); cursor += 5; }
                    else throw new NotSupportedException("Owned SPECIAL book texture field is unbound: " + name);
                }
                PushRegister(ref cursor, register);
                nativeManager = 0;
            }
            if (!StackAddress(ref cursor, out var loadedBuffer, out register)) throw new NotSupportedException("Owned native texture path buffer is unbound.");
            PushRegister(ref cursor, register);
            if (!optimized)
            {
                if (!code.AsSpan(cursor, 2).SequenceEqual(new byte[] { 0x8b, 0x0d })) throw new NotSupportedException("Owned native texture manager is unbound.");
                nativeManager = U32(code, cursor + 2); cursor += 6;
            }
            Same(ref loader, Call(ref cursor));
            if (manager is not null && manager != nativeManager) throw new NotSupportedException("Owned SPECIAL texture managers disagree.");
            manager = nativeManager;
            if (buffer != loadedBuffer)
            {
                var copies = SpecialBookMatches(code, at, load, [0x68, 4, 1, 0, 0]);
                var bound = 0;
                foreach (var copy in copies)
                {
                    var position = copy + 5;
                    if (!StackAddress(ref position, out var destination, out var copyRegister)) continue;
                    if (code[position++] != 0x50 + copyRegister || !StackAddress(ref position, out var input, out copyRegister) ||
                        code[position++] != 0x50 + copyRegister || code[position] != 0xe8) continue;
                    if (input != buffer || destination != loadedBuffer) continue;
                    Same(ref pathTransport, Call(ref position)); bound++;
                }
                if (bound != 1) throw new NotSupportedException("Owned SPECIAL texture buffer copy is unbound.");
            }
            if (platform)
            {
                // PC is the keyboard/pointer presentation branch. Both source
                // literals must be present in this callee; input-mode switching
                // remains a separate runtime behavior owner.
                if (!pushes.Any(item => item.At >= owner && item.At < at && item.Value == "PC") &&
                    !SpecialBookMatches(code, owner, at, [0xba, -1, -1, -1, -1]).Any(start => literal(U32(code, start + 1)) == "PC") ||
                    !pushes.Any(item => item.At >= owner && item.At < at && item.Value == "XBOX") &&
                    !SpecialBookMatches(code, owner, at, [0xb9, -1, -1, -1, -1]).Any(start => literal(U32(code, start + 1)) == "XBOX"))
                    throw new NotSupportedException("Owned SPECIAL book platform texture literals are unbound.");
                Same(ref platformFrame, platformSlot);
                if (optimized)
                {
                    var selections = SpecialBookMatches(code, owner, declarations.First(item => item.Name == "BBRTOff").At,
                        [0xba, -1, -1, -1, -1, 0xb9, -1, -1, -1, -1, 0x8d, 0x45, -1, 0x0f, 0x44, 0xca, 0x51, 0x6a, 5, 0x50, 0xe8, -1, -1, -1, -1]);
                    if (selections.Count != 1 || literal(U32(code, selections[0] + 1)) != "PC" ||
                        literal(U32(code, selections[0] + 6)) != "XBOX" || (sbyte)code[selections[0] + 12] != platformSlot)
                        throw new NotSupportedException("Owned SPECIAL book platform selection buffer is unbound.");
                }
                else
                {
                    int? transport = null;
                    foreach (var platformName in new[] { "PC", "XBOX" })
                    {
                        var selections = SpecialBookMatches(code, owner, declarations.First(item => item.Name == "BBRTOff").At,
                            [0x68, -1, -1, -1, -1, 0x6a, 5, 0x8d, -1, -1, -1, -1, -1, -1, 0xe8, -1, -1, -1, -1])
                            .Where(start => literal(U32(code, start + 1)) == platformName).ToArray();
                        if (selections.Length != 1) throw new NotSupportedException("Owned SPECIAL book platform selection transport is unbound.");
                        var position = selections[0] + 7;
                        if (!StackAddress(ref position, out var destination, out var selectionRegister) || destination != platformSlot)
                            throw new NotSupportedException("Owned SPECIAL book platform selection buffer is unbound.");
                        PushRegister(ref position, selectionRegister); Same(ref transport, Call(ref position));
                    }
                }
            }
            var path = Normalize(format switch
            {
                "%s%s%i.dds" => directory + name + "{0}.dds",
                "%s%s.dds" => directory + name + ".dds",
                "%s\\%s\\%s.dds" => directory + "/PC/" + name + ".dds",
                "%s%s\\%s.dds" => directory + "PC/" + name + ".dds",
                _ => throw new NotSupportedException("Owned SPECIAL book texture format is unbound."),
            });
            rows.Add(name, (path, field, count));

            string PushLiteral(ref int position)
            {
                if (position > limit - 5 || code[position] != 0x68 || literal(U32(code, position + 1)) is not { Length: > 0 } value)
                    throw new NotSupportedException("Owned SPECIAL texture literal flow is unbound.");
                position += 5; return value;
            }
        }
        var digits = rows["BBNumber"]; var messages = rows["BBMessage0"];
        var fieldsByName = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["BBRTOn"] = 15,
            ["BBRTOff"] = 16,
            ["BBLTOff"] = 17,
            ["BBLTOn"] = 18,
            ["BBXOff"] = 19,
            ["BBXOn"] = 20,
            ["BBDecreaseOff"] = 21,
            ["BBDecreaseOn"] = 22,
            ["BBIncreaseOff"] = 23,
            ["BBIncreaseOn"] = 24,
        };
        if (digits.Count != 11 || messages.Count != 4 || messages.Field != digits.Field + 10 * 4 ||
            fieldsByName.Any(pair => rows[pair.Key].Field != digits.Field + pair.Value * 4))
            throw new NotSupportedException("Owned SPECIAL book texture slot associations disagree.");
        if (pathTransport is not null) RequireSpecialBookPathTransport(code, pathTransport.Value);
        return new(Enumerable.Range(0, digits.Count).Select(index => string.Format(System.Globalization.CultureInfo.InvariantCulture, digits.Path, index)).ToArray(),
            Enumerable.Range(1, messages.Count).Select(index => string.Format(System.Globalization.CultureInfo.InvariantCulture, messages.Path, index)).ToArray(),
            fieldsByName.Keys.ToDictionary(name => name, name => rows[name].Path, StringComparer.Ordinal));

        bool StackAddress(ref int position, out int slot, out int register)
        {
            slot = register = 0;
            if (position > end - 7 || code[position] != 0x8d || (code[position + 1] & 0xc7) is not (0x45 or 0x85)) return false;
            register = code[position + 1] >> 3 & 7;
            var wide = (code[position + 1] & 0xc7) == 0x85;
            slot = wide ? unchecked((int)U32(code, position + 2)) : (sbyte)code[position + 2]; position += wide ? 6 : 3; return true;
        }
        void StackValue(ref int position, out int slot, out int register)
        {
            if (position > end - 6 || code[position] != 0x8b || (code[position + 1] & 0xc7) != 0x85)
                throw new NotSupportedException("Owned SPECIAL book texture local flow is unbound.");
            register = code[position + 1] >> 3 & 7; slot = unchecked((int)U32(code, position + 2)); position += 6;
        }
        void PushRegister(ref int position, int register)
        {
            if (position >= end || code[position++] != 0x50 + register) throw new NotSupportedException("Owned SPECIAL book texture argument register is unbound.");
        }
        void BufferSize(ref int position)
        {
            if (position > end - 5 || !code.AsSpan(position, 5).SequenceEqual(new byte[] { 0x68, 4, 1, 0, 0 }))
                throw new NotSupportedException("Owned SPECIAL book texture buffer extent is unbound.");
            position += 5;
        }
        int Call(ref int position)
        {
            if (position > end - 5 || code[position] != 0xe8) throw new NotSupportedException("Owned SPECIAL book texture helper call is unbound.");
            var target = checked(position + 5 + unchecked((int)U32(code, position + 1))); position += 5; return target;
        }
        static void Same(ref int? retained, int value)
        {
            if (retained is not null && retained != value) throw new NotSupportedException("Owned SPECIAL book texture helper owners disagree.");
            retained = value;
        }
        static string Normalize(string value)
        {
            var parts = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].Equals("textures", StringComparison.OrdinalIgnoreCase) || parts.Any(part => part is "." or ".." || part.Contains(':')))
                throw new NotSupportedException("Owned SPECIAL book texture path is unbound.");
            return string.Join('/', parts);
        }
    }

    // The optional call is a source-path transport, not memcpy: its ABI is
    // (formatted source, destination, capacity). Bind the guards, destination
    // initialization and final bounded append, including its typed copy loop.
    // Intermediate platform/language rewriting does not supply menu authority.
    private static void RequireSpecialBookPathTransport(byte[] code, int owner)
    {
        if (owner < 0 || owner > code.Length - 48 || !code.AsSpan(owner, 5).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x83, 0xec }) ||
            code[owner + 5] == 0) Refuse();
        var limit = Math.Min(code.Length, owner + 2048);
        var cursor = owner + 6;
        int tail, call;
        if (code[cursor] == 0x57)
        {
            // Register-retained source, optimized guard failures share a return.
            if (!At(cursor, [0x57, 0x8b, 0x7d, 8, 0x85, 0xff, 0x0f, 0x84, -1, -1, -1, -1,
                0x8b, 0x45, 12, 0x85, 0xc0, 0x0f, 0x84, -1, -1, -1, -1,
                0x83, 0x7d, 16, 0, 0x0f, 0x84, -1, -1, -1, -1, 0xc6, 0, 0])) Refuse();
            var failure = Near(cursor + 6);
            if (Near(cursor + 17) != failure || Near(cursor + 27) != failure ||
                !At(failure, [0x32, 0xc0, 0x5f, 0x8b, 0xe5, 0x5d, 0xc3])) Refuse();
            var tails = SpecialBookMatches(code, cursor + 36, limit,
                [0x57, 0xff, 0x75, 16, 0xff, 0x75, 12, 0xe8, -1, -1, -1, -1,
                    0x8a, 0x45, -1, 0x83, 0xc4, 12, 0x5e, 0x5b, 0x5f, 0x8b, 0xe5, 0x5d, 0xc3]);
            if (tails.Count != 1 || tails[0] + 25 != failure) Refuse();
            tail = tails[0]; call = tail + 7;
            if (SpecialBookMatches(code, cursor + 36, tail, [0xc6, 0x45, code[tail + 14], 0]).Count != 1) Refuse();
        }
        else
        {
            // Frame-retained source, the third nonzero guard enters the body.
            if (!At(cursor, [0x83, 0x7d, 8, 0, 0x74, -1, 0x83, 0x7d, 12, 0, 0x74, -1,
                0x83, 0x7d, 16, 0, 0x75, -1, 0x32, 0xc0, 0xe9, -1, -1, -1, -1,
                0xc6, 0x45, -1, 0, 0x8b, 0x45, 12, 0xc6, 0, 0])) Refuse();
            var invalid = cursor + 18; var body = cursor + 25;
            if (Short(cursor + 4) != invalid || Short(cursor + 10) != invalid || Short(cursor + 16) != body) Refuse();
            var sources = SpecialBookMatches(code, body + 10, Math.Min(limit, body + 100), [0x8b, -1, 8, 0x89, -1, -1])
                .Where(at => (code[at + 1] & 0xc7) == 0x45 && code[at + 4] == code[at + 1] && (sbyte)code[at + 5] < 0).ToArray();
            if (sources.Length != 1) Refuse();
            var sourceSlot = code[sources[0] + 5];
            var tails = SpecialBookMatches(code, body + 10, limit,
                [0x8b, -1, sourceSlot, -1, 0x8b, -1, 16, -1, 0x8b, -1, 12, -1,
                    0xe8, -1, -1, -1, -1, 0x83, 0xc4, 12, 0x8a, 0x45, code[body + 2], 0x8b, 0xe5, 0x5d, 0xc3])
                .Where(at => Enumerable.Range(0, 3).All(index =>
                    (code[at + index * 4 + 1] & 0xc7) == 0x45 && code[at + index * 4 + 3] == 0x50 + (code[at + index * 4 + 1] >> 3 & 7))).ToArray();
            if (tails.Length != 1 || Jump(cursor + 20) != tails[0] + 23) Refuse();
            tail = tails[0]; call = tail + 12;
        }
        RequireSpecialBookBoundedAppend(code, BookTextureCallTarget(code, call));

        bool At(int at, int[] pattern) => at >= owner && at <= limit - pattern.Length &&
            pattern.Select((value, index) => value < 0 || code[at + index] == value).All(value => value);
        int Near(int at) => checked(at + 6 + unchecked((int)U32(code, at + 2)));
        int Jump(int at) => checked(at + 5 + unchecked((int)U32(code, at + 1)));
        int Short(int at) => at + 2 + (sbyte)code[at + 1];
        static void Refuse() => throw new NotSupportedException("Owned SPECIAL book path-transport declaration is unbound.");
    }

    private static void RequireSpecialBookBoundedAppend(byte[] code, int owner)
    {
        // One observed compiler inserts an ABI-preserving cdecl forwarding
        // wrapper. Follow it only when all three argument roles are unchanged.
        if (owner >= 0 && owner <= code.Length - 25 && code.AsSpan(owner, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }))
        {
            for (var index = 0; index < 3; index++)
            {
                var at = owner + 3 + index * 4;
                if (code[at] != 0x8b || (code[at + 1] & 0xc7) != 0x45 || code[at + 2] != 16 - index * 4 ||
                    code[at + 3] != 0x50 + (code[at + 1] >> 3 & 7)) Refuse();
            }
            if (!code.AsSpan(owner + 20, 5).SequenceEqual(new byte[] { 0x83, 0xc4, 12, 0x5d, 0xc3 })) Refuse();
            owner = BookTextureCallTarget(code, owner + 15);
        }
        if (owner < 0 || owner > code.Length - 116 || !code.AsSpan(owner, 5).SequenceEqual(new byte[] { 0x8b, 0xff, 0x55, 0x8b, 0xec })) Refuse();
        var limit = owner + 116;
        // Both bounded append bodies first scan the destination terminator
        // while consuming capacity, then copy source bytes under that capacity.
        var retained = At(owner + 5, [0x56, 0x8b, 0x75, 8, 0x57, 0x85, 0xf6, 0x74, -1,
            0x8b, 0x4d, 12, 0x85, 0xc9, 0x74, -1, 0x8b, 0x7d, 16, 0x85, 0xff, 0x75, -1]);
        var separate = At(owner + 5, [0x8b, 0x45, 8, 0x53, 0x33, 0xdb, 0x56, 0x57, 0x3b, 0xc3, 0x74, -1,
            0x8b, 0x7d, 12, 0x3b, 0xfb, 0x77, -1]);
        List<int> scan = retained ? SpecialBookMatches(code, owner + 27, limit,
            [0x8b, 0xd6, 0x80, 0x3a, 0, 0x74, -1, 0x42, 0x83, 0xe9, 1, 0x75, -1, 0x85, 0xc9, 0x74, -1, 0x2b, 0xfa])
            : separate ? SpecialBookMatches(code, owner + 23, limit,
                [0x8b, 0x75, 16, 0x3b, 0xf3, 0x75, -1, 0x88, 0x18, 0xeb, -1, 0x8b, 0xd0,
                    0x38, 0x1a, 0x74, -1, 0x42, 0x4f, 0x75, -1, 0x3b, 0xfb, 0x74, -1]) : [];
        if (scan.Count != 1) Refuse();
        var start = retained ? scan[0] + 19 : scan[0] + 25;
        int[] pattern = retained ? [0x8a, 4, 0x17, 0x88, 2, 0x42, 0x84, 0xc0, 0x74, -1, 0x83, 0xe9, 1, 0x75, -1]
            : [0x8a, 0x0e, 0x88, 0x0a, 0x42, 0x46, 0x3a, 0xcb, 0x74, -1, 0x4f, 0x75, -1];
        if (!At(start, pattern) || Branch(retained ? scan[0] + 11 : scan[0] + 19) != (retained ? scan[0] + 2 : scan[0] + 13) ||
            Branch(start + pattern.Length - 2) != start) Refuse();
        if (retained ? Branch(owner + 12) != Branch(owner + 19) || Branch(owner + 26) != scan[0] ||
            Branch(scan[0] + 5) != scan[0] + 13 : Branch(owner + 22) != scan[0] ||
            Branch(scan[0] + 5) != scan[0] + 11 || Branch(scan[0] + 9) != Branch(owner + 15) ||
            Branch(scan[0] + 15) != scan[0] + 21) Refuse();
        var tail = start + pattern.Length;
        // Null destination/capacity guards bypass the destination write. The
        // scan and copy exhaustion paths instead clear their valid destination
        // and return the nonzero bounded-string status through the same owner.
        if (retained)
        {
            var invalid = owner + 31; var success = tail + 15;
            if (Branch(owner + 12) != invalid || !At(owner + 28, [0xc6, 6, 0]) ||
                !At(invalid, [0xe8, -1, -1, -1, -1, 0x6a, 22, 0x5e, 0x89, 0x30,
                    0xe8, -1, -1, -1, -1, 0x5f, 0x8b, 0xc6, 0x5e, 0x5d, 0xc3]) ||
                !At(tail, [0x85, 0xc9, 0x75, -1, 0x88, 0x0e, 0xe8, -1, -1, -1, -1, 0x6a, 34, 0xeb, -1]) ||
                !At(success, [0x33, 0xf6, 0xeb, -1]) || Branch(scan[0] + 15) != owner + 28 ||
                Branch(start + 8) != tail || Branch(tail + 2) != success || Branch(tail + 13) != invalid + 7 ||
                Branch(success + 2) != invalid + 15) Refuse();
            _ = BookTextureCallTarget(code, invalid); _ = BookTextureCallTarget(code, invalid + 10);
            _ = BookTextureCallTarget(code, tail + 6);
        }
        else
        {
            // Exhaustion retains its range status and still invokes the
            // common argument-error callback before returning that status.
            var invalid = owner + 24; var success = tail + 20;
            if (Branch(owner + 15) != invalid || !At(invalid,
                [0xe8, -1, -1, -1, -1, 0x6a, 22, 0x5e, 0x89, 0x30, 0x53, 0x53, 0x53, 0x53, 0x53,
                    0xe8, -1, -1, -1, -1, 0x83, 0xc4, 20, 0x8b, 0xc6, 0xeb, -1]) ||
                !At(tail, [0x3b, 0xfb, 0x75, -1, 0x88, 0x18, 0xe8, -1, -1, -1, -1, 0x6a, 34,
                    0x59, 0x89, 8, 0x8b, 0xf1, 0xeb, -1]) || !At(success, [0x33, 0xc0, 0x5f, 0x5e, 0x5b, 0x5d, 0xc3]) ||
                Branch(scan[0] + 23) != scan[0] + 7 || Branch(start + 8) != tail || Branch(tail + 2) != success ||
                Branch(tail + 18) != invalid + 10 || Branch(invalid + 25) != success + 2) Refuse();
            _ = BookTextureCallTarget(code, invalid); _ = BookTextureCallTarget(code, invalid + 15);
            _ = BookTextureCallTarget(code, tail + 6);
        }

        bool At(int at, int[] pattern) => at >= owner && at <= limit - pattern.Length &&
            pattern.Select((value, index) => value < 0 || code[at + index] == value).All(value => value);
        int Branch(int at) => at + 2 + (sbyte)code[at + 1];
        static void Refuse() => throw new NotSupportedException("Owned SPECIAL book bounded texture-path append is unbound.");
    }

    private static int BookTextureCallTarget(byte[] code, int at)
    {
        if (at < 0 || at > code.Length - 5 || code[at] != 0xe8) throw new NotSupportedException("Owned SPECIAL book path helper call is unbound.");
        var target = (long)at + 5 + unchecked((int)U32(code, at + 1));
        if (target < 0 || target >= code.Length) throw new NotSupportedException("Owned SPECIAL book path helper target is unbound.");
        return (int)target;
    }
}
