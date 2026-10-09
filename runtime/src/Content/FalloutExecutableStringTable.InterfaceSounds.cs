using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutInterfaceSoundCatalogue ReadInterfaceSounds(string executable, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(executable);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedSha256)
            throw new InvalidDataException("Interface sound executable changed from its selected source receipt.");
        var (code, image) = Load(bytes);
        // The real class/click declaration ties discovery to a used caller.
        // Another switch with plausible UI literals cannot become authority.
        var tables = image.SourceClassTables(".?AVSleepWaitMenu@@", 15).ToArray();
        if (tables.Length != 1) throw new NotSupportedException("Interface sound selector has no unique original menu caller.");
        var click = U32(image.Read(checked(tables[0] + 3 * 4), 4), 0);
        var callers = InterfaceSoundInstructions(code, image.CodeBase, click);
        var selected = new HashSet<uint>();
        foreach (var instruction in callers)
        {
            if (instruction.Opcode != 0xe8 || instruction.Previous is not { } prior || prior.Opcode != 0x6a ||
                prior.Immediate is not (1 or 2) || prior.Next != instruction.At) continue;
            selected.Add(InterfaceRelativeTarget(instruction, image.CodeBase));
        }
        if (selected.Count != 1) throw new NotSupportedException("Original menu branches do not share one indexed sound selector.");
        return ReadInterfaceSoundCatalogue(code, image.CodeBase, selected.Single(), expectedSha256,
            image.Read, image.Literal, image.IsExecutableExtent);
    }

    internal static FalloutInterfaceSoundCatalogue ReadInterfaceSoundCatalogue(byte[] code, uint codeBase,
        uint entry, string engineSha256, Func<uint, int, byte[]> read, Func<uint, string?> literal,
        Func<uint, bool> executable)
    {
        var instructions = InterfaceSoundInstructions(code, codeBase, entry);
        var registers = new Dictionary<int, int>(); var locals = new Dictionary<int, int>();
        uint? maximum = null, outside = null, bytesTable = null, targetsTable = null;
        int? selectorRegister = null; uint? compared = null;
        foreach (var row in instructions.OrderBy(row => row.At))
        {
            var mode = row.ModRm >> 6; var register = row.ModRm >> 3 & 7; var operand = row.ModRm & 7;
            int? Value() => mode == 3 ? registers.GetValueOrDefault(operand, int.MinValue) :
                operand == 5 && row.Displacement == 8 ? 0 :
                operand == 5 && row.Displacement < 0 ? locals.GetValueOrDefault(row.Displacement, int.MinValue) : null;
            if (row.Opcode == 0x8b)
            {
                var value = Value();
                if (value is not null && value != int.MinValue) registers[register] = value.Value;
                else registers.Remove(register);
            }
            else if (row.Opcode == 0x89 && operand == 5 && mode != 3 && row.Displacement < 0)
            {
                if (registers.TryGetValue(register, out var value)) locals[row.Displacement] = value;
                else locals.Remove(row.Displacement);
            }
            else if (row.Opcode is >= 0x40 and <= 0x47)
            {
                var target = row.Opcode - 0x40;
                if (registers.TryGetValue(target, out var value)) registers[target] = checked(value + 1);
            }
            else if (row.Opcode is 0x81 or 0x83 && register == 0 && mode == 3 && row.Immediate == 1)
            {
                if (registers.TryGetValue(operand, out var value)) registers[operand] = checked(value + 1);
            }
            else if (row.Opcode is 0x81 or 0x83 && register == 7 && Value() == 1)
                compared = checked((uint)(row.Immediate ?? throw new InvalidDataException("Interface sound range operand is absent.")));
            else if (row.Opcode == 0x3d && registers.GetValueOrDefault(0, int.MinValue) == 1)
                compared = unchecked((uint)(row.Immediate ?? -1));
            else if (row.Opcode == 0x77 || row.Opcode == 0x0f && row.Escaped == 0x87)
            {
                if (compared is null) continue;
                maximum = compared; outside = InterfaceRelativeTarget(row, codeBase);
            }
            else if (row.Opcode == 0x0f && row.Escaped == 0xb6 && mode == 2 && operand != 4 &&
                registers.GetValueOrDefault(operand, int.MinValue) == 1)
            {
                bytesTable = unchecked((uint)row.Displacement); selectorRegister = register;
            }
            else if (row.Opcode == 0xff && register == 4 && mode == 0 && operand == 4 &&
                row.Sib >> 6 == 2 && (row.Sib & 7) == 5 && (row.Sib >> 3 & 7) == selectorRegister)
            { targetsTable = unchecked((uint)row.Displacement); break; }
            else if (row.Opcode == 0xe8)
            { registers.Remove(0); registers.Remove(1); registers.Remove(2); }
        }
        if (maximum is null || outside is null || bytesTable is null || targetsTable is null || maximum >= int.MaxValue)
            throw new NotSupportedException("Original interface sound index/bias/range/dispatch relationship is unbound.");
        var selector = read(bytesTable.Value, checked((int)maximum.Value + 1));
        var branchTargets = selector.Distinct().ToDictionary(index => index,
            index => U32(read(checked(targetsTable.Value + (uint)index * 4), 4), 0));
        if (branchTargets.Values.Any(target => !executable(target)) || !executable(outside.Value))
            throw new InvalidDataException("Interface sound branch leaves its original executable extent.");
        var named = new Dictionary<uint, (string Name, uint Flags, uint Provider, uint? Cleanup)>();
        foreach (var target in branchTargets.Values.Distinct())
        {
            var rows = InterfaceSoundLinearBranch(code, codeBase, target);
            if (rows.Count < 2 || rows[0].Opcode != 0x68 || rows[1].Opcode != 0x68) continue;
            var flags = unchecked((uint)rows[0].Immediate!.Value);
            var name = literal(unchecked((uint)rows[1].Immediate!.Value));
            if (name is null || name.Length == 0 || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                throw new InvalidDataException("Indexed interface sound has no original editor-ID literal.");
            var calls = rows.Where(row => row.Opcode == 0xe8).ToArray();
            // An optimized branch jumps to its shared named constructor. The
            // two original pushes are retained; never guess a name from a call.
            if (calls.Length == 0 && rows[^1].Opcode is 0xe9 or 0xeb)
                calls = InterfaceSoundLinearBranch(code, codeBase, InterfaceRelativeTarget(rows[^1], codeBase))
                    .Where(row => row.Opcode == 0xe8).ToArray();
            if (calls.Length == 0) throw new NotSupportedException("Named interface cue has no original source/voice constructor.");
            var provider = InterfaceRelativeTarget(calls[0], codeBase);
            if (!executable(provider) || flags == 0) throw new InvalidDataException("Named cue has no source request flags/provider.");
            named.Add(target, (name, flags, provider, calls.Length >= 3 ? InterfaceRelativeTarget(calls[^1], codeBase) : null));
        }
        if (named.Count == 0 || named.Values.Select(row => row.Provider).Distinct().Count() != 1 ||
            named.Values.Select(row => row.Flags).Distinct().Count() != 1)
            throw new NotSupportedException("Interface catalogue branches do not share one genuine named-source provider.");
        var cleanups = named.Values.Where(row => row.Cleanup is not null).Select(row => row.Cleanup!.Value).ToHashSet();
        RequireInterfaceSilent(code, codeBase, outside.Value, cleanups);
        var entries = new List<FalloutInterfaceSoundEntry>();
        for (var ordinal = 0; ordinal < selector.Length; ++ordinal)
        {
            var target = branchTargets[selector[ordinal]];
            if (named.TryGetValue(target, out var sound))
                entries.Add(new(ordinal - 1, FalloutInterfaceSoundDisposition.NamedSound, sound.Name, sound.Flags));
            else
            {
                RequireInterfaceSilent(code, codeBase, target, cleanups);
                entries.Add(new(ordinal - 1, FalloutInterfaceSoundDisposition.SourceSilent, null, 0));
            }
        }
        var catalogue = new FalloutInterfaceSoundCatalogue(engineSha256, maximum.Value,
            Array.AsReadOnly(entries.ToArray()), FalloutInterfaceSoundDisposition.SourceSilent);
        catalogue.Validate(); return catalogue;
    }

    private static void RequireInterfaceSilent(byte[] code, uint codeBase, uint target, HashSet<uint> cleanup)
    {
        var seen = new HashSet<uint>();
        while (seen.Add(target))
        {
            var rows = InterfaceSoundLinearBranch(code, codeBase, target);
            foreach (var row in rows)
            {
                if (row.Opcode == 0xe8 && !cleanup.Contains(InterfaceRelativeTarget(row, codeBase)))
                    throw new NotSupportedException("Silent interface branch reaches an unowned call.");
                if (row.Opcode is 0x68 or 0x6a || row.Opcode == 0xff)
                    throw new NotSupportedException("Silent interface branch has an unowned request/indirect effect.");
                if (row.Opcode is 0xc7 or 0x89 && row.ModRm >> 6 != 3 &&
                    !((row.ModRm & 7) == 5 && row.Displacement < 0 || row.Segment == 0x64 &&
                        row.Opcode == 0x89 && row.ModRm >> 6 == 0 && (row.ModRm & 7) == 5 && row.Displacement == 0))
                    throw new NotSupportedException("Silent interface branch writes outside its local cleanup state.");
            }
            if (rows[^1].Opcode is 0xc3 or 0xc2) return;
            if (rows[^1].Opcode is not (0xe9 or 0xeb)) break;
            target = InterfaceRelativeTarget(rows[^1], codeBase);
        }
        throw new NotSupportedException("Silent interface branch has no complete original return.");
    }

    private static uint InterfaceRelativeTarget(InterfaceInstruction row, uint codeBase)
    {
        var value = checked((long)codeBase + row.Next + (row.Immediate ?? throw new InvalidDataException("Source branch displacement is absent.")));
        return value is >= 0 and <= uint.MaxValue ? (uint)value : throw new InvalidDataException("Source interface branch address overflowed.");
    }
}
