using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal enum FalloutEngineFieldOperation
{
    Receiver, GlobalAddress, AddAddress, Read, Constant, And, Or, Xor,
    ShiftRight, ShiftLeft, EqualZero, NotEqualZero, SignExtend
}

// A field expression contains semantic reads and UInt32 operations only.
// Original instructions are inspected once; they never become runtime code.
internal sealed record FalloutEngineFieldExpression(FalloutEngineFieldOperation Operation,
    uint Operand, int Width, IReadOnlyList<FalloutEngineFieldExpression> Inputs)
{
    internal bool UsesReceiver => Operation == FalloutEngineFieldOperation.Receiver || Inputs.Any(value => value.UsesReceiver);
}

internal sealed record FalloutEngineFieldMethod(string RuntimeSha256, uint ImageBase, uint Entry,
    int BodyBytes, string BodySha256, FalloutEngineFieldExpression Result,
    IReadOnlyList<FalloutEngineFieldExpression> Reads,
    IReadOnlyList<FalloutEngineFieldMethod> Children);

internal sealed class FalloutEngineFieldMethodSource(string identity, Func<uint, FalloutEngineFieldMethod> inspect,
    Func<OpenNV.Runtime.Compatibility.NativePlugins.NativeNvseSourceClass, int, FalloutEngineFieldMethod> inspectVirtual)
{
    private readonly Dictionary<uint, FalloutEngineFieldMethod> _methods = [];
    internal string RuntimeSha256 { get; } = identity;
    internal FalloutEngineFieldMethod Read(uint entry)
    {
        if (_methods.TryGetValue(entry, out var retained)) return retained;
        var value = inspect(entry);
        if (value.Entry != entry || !StringComparer.OrdinalIgnoreCase.Equals(value.RuntimeSha256, RuntimeSha256))
            throw new InvalidDataException("Source field method changed its selected executable identity.");
        _methods.Add(entry, value); return value;
    }
    private readonly Dictionary<(OpenNV.Runtime.Compatibility.NativePlugins.NativeNvseSourceClass, int), FalloutEngineFieldMethod> _virtuals = [];
    internal FalloutEngineFieldMethod ReadVirtual(OpenNV.Runtime.Compatibility.NativePlugins.NativeNvseSourceClass kind, int byteSlot)
    {
        if (_virtuals.TryGetValue((kind, byteSlot), out var actual)) return actual;
        actual = inspectVirtual(kind, byteSlot);
        if (!StringComparer.OrdinalIgnoreCase.Equals(actual.RuntimeSha256, RuntimeSha256))
            throw new InvalidDataException("Source virtual field method changed its exact selected runtime.");
        _virtuals.Add((kind, byteSlot), actual); return actual;
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutEngineFieldMethodSource ReadEngineFieldMethodSource(string path, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(path); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!StringComparer.OrdinalIgnoreCase.Equals(hash, expectedSha256))
            throw new InvalidDataException("Source field method executable changed before declaration admission.");
        var (code, image) = Load(bytes);
        return new(hash, entry => ReadEngineFieldMethod(code, image.CodeBase, image.Base, hash,
            entry, image.IsExecutableExtent, image.IsWritableObject), (kind, slot) =>
        {
            var (name, count) = kind switch
            {
                OpenNV.Runtime.Compatibility.NativePlugins.NativeNvseSourceClass.Script => (".?AVScript@@", 78),
                OpenNV.Runtime.Compatibility.NativePlugins.NativeNvseSourceClass.Quest => (".?AVTESQuest@@", 79),
                _ => throw new NotSupportedException("Native source class has no reviewed original virtual table.")
            };
            if (slot is not (0x8c or 0xf0 or 0x100))
                throw new NotSupportedException("Source virtual requires its separate method signature/argument/result/effect owner.");
            var tables = image.SourceClassTables(name, count).ToArray();
            if (tables.Length != 1) throw new NotSupportedException("Selected source virtual class association is absent or ambiguous.");
            var entry = BinaryPrimitives.ReadUInt32LittleEndian(image.Read(checked(tables[0] + (uint)slot), 4));
            return ReadEngineFieldMethodCore(code, image.CodeBase, image.Base, hash, entry, image.IsExecutableExtent,
                image.IsWritableObject, [], 0, slot is 0xf0 or 0x100);
        });
    }

    internal static FalloutEngineFieldMethod ReadEngineFieldMethod(byte[] code, uint codeBase, uint imageBase,
        string runtimeSha256, uint entry, Func<uint, bool> executable, Func<uint, bool> writable)
        => ReadEngineFieldMethodCore(code, codeBase, imageBase, runtimeSha256, entry, executable, writable, [], 0, false);

    private static FalloutEngineFieldMethod ReadEngineFieldMethodCore(byte[] code, uint codeBase, uint imageBase,
        string hash, uint entry, Func<uint, bool> executable, Func<uint, bool> writable, HashSet<uint> active, int depth, bool byteReturn)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || depth >= 8 || !active.Add(entry))
            throw new NotSupportedException("Source field method has an absent identity or recursive/uninspected call domain.");
        try
        {
            var start = CallSiteOffset(entry, codeBase, code.Length, 1); var at = start;
            var registers = new FalloutEngineFieldExpression?[8]; registers[1] = Node(FalloutEngineFieldOperation.Receiver);
            var locals = new Dictionary<int, FalloutEngineFieldExpression?>();
            var frameSaved = false; var frame = false; var retiring = false; var localBytes = 0;
            FalloutEngineFieldExpression? comparison = null;
            var reads = new List<FalloutEngineFieldExpression>();
            var children = new List<FalloutEngineFieldMethod>(); InterfaceInstruction? previous = null;
            FalloutEngineFieldExpression Register(int index)
            {
                if ((index is 0 or 1 or 2) && registers[index] is { } value) return value;
                throw new NotSupportedException("Source field method reads an unowned register/caller value.");
            }
            void Write(int index, FalloutEngineFieldExpression? value)
            {
                if (index is not (0 or 1 or 2)) throw new NotSupportedException("Source field method mutates a callee-saved register.");
                registers[index] = value;
            }
            bool Local(InterfaceInstruction instruction) => (instruction.ModRm & 7) == 5 &&
                (instruction.ModRm >> 6 is 1 or 2) && instruction.Sib == 0;
            FalloutEngineFieldExpression Address(InterfaceInstruction instruction)
            {
                var mode = instruction.ModRm >> 6; var basis = instruction.ModRm & 7;
                if (instruction.Sib != 0 || basis == 4 || mode == 3)
                    throw new NotSupportedException("Source field method address has an unowned index/SIB domain.");
                if (mode == 0 && basis == 5)
                {
                    var absolute = unchecked((uint)instruction.Displacement);
                    if (!writable(absolute)) throw new NotSupportedException("Source field read has no selected writable global declaration.");
                    return Node(FalloutEngineFieldOperation.GlobalAddress, absolute);
                }
                return Add(Register(basis), instruction.Displacement);
            }
            FalloutEngineFieldExpression Operand(InterfaceInstruction instruction, int width)
            {
                if (instruction.ModRm >> 6 == 3) return Register(instruction.ModRm & 7);
                if (Local(instruction))
                {
                    if (!frame || retiring || width != 4 || !locals.TryGetValue(instruction.Displacement, out var value) || value is null)
                        throw new NotSupportedException("Source field method stack read is not an initialized receiver local.");
                    return value;
                }
                var read = Node(FalloutEngineFieldOperation.Read, width: width, inputs: [Address(instruction)]);
                reads.Add(read); return read;
            }
            for (var count = 0; count < 128 && at - start < 512; ++count)
            {
                var row = InterfaceDecode(code, at, previous);
                if (code[row.At] != row.Opcode || row.Segment != 0 || row.Next - start > 512)
                    throw new NotSupportedException("Source field method has an unowned instruction prefix/static-TLS domain.");
                for (var cursor = row.At; cursor < row.Next; ++cursor)
                    if (!executable(checked(codeBase + (uint)cursor)))
                        throw new InvalidDataException("Source field method leaves its complete executable extent.");
                var mode = row.ModRm >> 6; var destination = row.ModRm >> 3 & 7;
                var immediate = row.Immediate is { } signed ? unchecked((uint)signed) : 0U;
                if (row.Opcode == 0x90) { }
                else if (row.Opcode == 0x55 && count == 0) frameSaved = true;
                else if (row.Opcode is 0x8b or 0x89 && mode == 3)
                {
                    var target = row.Opcode == 0x8b ? destination : row.ModRm & 7;
                    var source = row.Opcode == 0x8b ? row.ModRm & 7 : destination;
                    if (target == 5 && source == 4 && frameSaved && !frame) frame = true;
                    else if (target == 4 && source == 5 && frame && !retiring)
                    { retiring = true; localBytes = 0; locals.Clear(); }
                    else Write(target, Register(source));
                }
                else if (row.Opcode == 0x51)
                {
                    if (!frame || retiring) throw new NotSupportedException("Source field local allocation escaped its original frame.");
                    localBytes = checked(localBytes + 4); locals.Add(-localBytes, registers[1]);
                }
                else if (row.Opcode is 0x81 or 0x83 && row.ModRm == 0xec)
                {
                    if (!frame || retiring || immediate == 0 || immediate > 256 || immediate % 4 != 0)
                        throw new NotSupportedException("Source field stack extent has no complete bounded local allocation.");
                    var end = checked(localBytes + (int)immediate);
                    for (var cursor = localBytes + 4; cursor <= end; cursor += 4) locals.Add(-cursor, null);
                    localBytes = end; comparison = null;
                }
                else if (row.Opcode == 0x89 && Local(row))
                {
                    if (!frame || retiring || !locals.ContainsKey(row.Displacement))
                        throw new NotSupportedException("Source field local write is not an original allocated field.");
                    locals[row.Displacement] = Register(destination);
                }
                else if (row.Opcode == 0xc7 && destination == 0 && Local(row))
                {
                    if (!frame || retiring || !locals.ContainsKey(row.Displacement))
                        throw new NotSupportedException("Source field constant write is not an allocated local.");
                    locals[row.Displacement] = Node(FalloutEngineFieldOperation.Constant, immediate);
                }
                else if (row.Opcode == 0x8b) Write(destination, Operand(row, 4));
                else if (row.Opcode == 0x8d && mode != 3) Write(destination, Address(row));
                else if (row.Opcode == 0xa1)
                {
                    if (!writable(immediate)) throw new NotSupportedException("Source absolute field has no selected writable declaration.");
                    var read = Node(FalloutEngineFieldOperation.Read, width: 4,
                        inputs: [Node(FalloutEngineFieldOperation.GlobalAddress, immediate)]);
                    reads.Add(read); Write(0, read);
                }
                else if (row.Opcode is >= 0xb8 and <= 0xba) Write(row.Opcode - 0xb8, Node(FalloutEngineFieldOperation.Constant, immediate));
                else if (row.Opcode == 0xb0)
                {
                    var low = Node(FalloutEngineFieldOperation.Constant, immediate & 255U);
                    var high = registers[0] is { } value ? Mask(value, 0xffffff00U) : null;
                    Write(0, high is null ? Node(FalloutEngineFieldOperation.Or, width: 1, inputs: [low]) :
                        Node(FalloutEngineFieldOperation.Or, inputs: [high, low]));
                }
                else if (row.Opcode == 0x0f && row.Escaped is 0xb6 or 0xb7 or 0xbe or 0xbf)
                {
                    var width = row.Escaped is 0xb6 or 0xbe ? 1 : 2;
                    var sourceValue = mode == 3 ? Register(row.ModRm & 7) : null;
                    var value = sourceValue is null ? Operand(row, width) :
                        width == 1 && sourceValue is { Operation: FalloutEngineFieldOperation.Or, Width: 1, Inputs.Count: 1 } ? sourceValue.Inputs[0] :
                        Mask(sourceValue, width == 1 ? 255U : 65535U);
                    Write(destination, row.Escaped is 0xbe or 0xbf ?
                        Node(FalloutEngineFieldOperation.SignExtend, width: width, inputs: [value]) : value);
                }
                else if (row.Opcode is 0x81 or 0x83 && mode == 3 && destination == 0)
                { Write(row.ModRm & 7, Add(Register(row.ModRm & 7), unchecked((int)immediate))); comparison = Register(row.ModRm & 7); }
                else if (row.Opcode is 0x25 or 0x0d or 0x35)
                {
                    Write(0, row.Opcode == 0x25 ? immediate == 0 ? Node(FalloutEngineFieldOperation.Constant) : Mask(Register(0), immediate) :
                        Node(row.Opcode == 0x0d ? FalloutEngineFieldOperation.Or : FalloutEngineFieldOperation.Xor, immediate, inputs: [Register(0)]));
                    comparison = Register(0);
                }
                else if (row.Opcode is 0x81 or 0x83 && mode == 3 && destination is 1 or 4 or 6)
                {
                    Write(row.ModRm & 7, destination == 4 ? immediate == 0 ? Node(FalloutEngineFieldOperation.Constant) : Mask(Register(row.ModRm & 7), immediate) :
                        Node(destination == 1 ? FalloutEngineFieldOperation.Or : FalloutEngineFieldOperation.Xor, immediate, inputs: [Register(row.ModRm & 7)]));
                    comparison = Register(row.ModRm & 7);
                }
                else if (row.Opcode is 0x31 or 0x33 && mode == 3 && destination == (row.ModRm & 7))
                { Write(destination, Node(FalloutEngineFieldOperation.Constant)); comparison = Register(destination); }
                else if (row.Opcode == 0xc1 && mode == 3 && destination is 4 or 5)
                {
                    Write(row.ModRm & 7, Node(destination == 4 ? FalloutEngineFieldOperation.ShiftLeft :
                        FalloutEngineFieldOperation.ShiftRight, immediate & 31, inputs: [Register(row.ModRm & 7)]));
                    if ((immediate & 31) != 0) comparison = Register(row.ModRm & 7);
                }
                else if (row.Opcode == 0x85 && mode == 3 && destination == (row.ModRm & 7))
                    comparison = Register(destination);
                else if (row.Opcode == 0x3d && immediate == 0) comparison = Register(0);
                else if (row.Opcode is 0x81 or 0x83 && destination == 7 && immediate == 0) comparison = Operand(row, 4);
                else if (row.Opcode == 0xf7 && destination == 0)
                    comparison = Node(FalloutEngineFieldOperation.And, immediate, inputs: [Operand(row, 4)]);
                else if (row.Opcode == 0x0f && row.Escaped is 0x94 or 0x95 && mode == 3 && (row.ModRm & 7) == 0)
                {
                    // SETcc defines only AL. Preserve the real high bits; an
                    // uninitialized EAX must still be refused unless a later
                    // MOVZX explicitly discards them.
                    var low = Node(row.Escaped == 0x94 ? FalloutEngineFieldOperation.EqualZero :
                        FalloutEngineFieldOperation.NotEqualZero, inputs: [comparison ??
                            throw new NotSupportedException("Source field Boolean result lost its exact comparison.")]);
                    var high = registers[0] is { } old ? Node(FalloutEngineFieldOperation.And, 0xffffff00U, inputs: [old]) : null;
                    Write(0, high is null ? Node(FalloutEngineFieldOperation.Or, 0, width: 1, inputs: [low]) :
                        Node(FalloutEngineFieldOperation.Or, inputs: [high, low]));
                }
                else if (row.Opcode == 0xe8)
                {
                    var target = unchecked(codeBase + (uint)row.Next + immediate);
                    var child = ReadEngineFieldMethodCore(code, codeBase, imageBase, hash, target, executable, writable, active, depth + 1, false);
                    var receiver = child.Result.UsesReceiver || child.Reads.Any(value => value.UsesReceiver) ? Register(1) : null;
                    var substitutions = new Dictionary<FalloutEngineFieldExpression, FalloutEngineFieldExpression>(ReferenceEqualityComparer.Instance);
                    reads.AddRange(child.Reads.Select(value => Substitute(value, receiver, substitutions)));
                    children.Add(child); Write(0, Substitute(child.Result, receiver, substitutions)); Write(1, null); Write(2, null); comparison = null;
                }
                else if (row.Opcode == 0x5d)
                {
                    if (!frameSaved || !frame || !retiring || localBytes != 0)
                        throw new NotSupportedException("Source field method does not retire its original frame.");
                    frameSaved = frame = retiring = false;
                }
                else if (row.Opcode == 0xc3)
                {
                    var result = Register(0);
                    if (byteReturn) result = Mask(result, 255U);
                    if (frameSaved || frame || localBytes != 0 || depth == 0 && ContainsPartial(result) || Count(result) > 256 || reads.Count > 4096)
                        throw new NotSupportedException("Source field method has an incomplete zero-stack-argument return.");
                    var extent = row.Next - start;
                    return new(hash, imageBase, entry, extent,
                        Convert.ToHexString(SHA256.HashData(code.AsSpan(start, extent))), result, reads.ToArray(), children.ToArray());
                }
                else throw new NotSupportedException("Source field method requires its other branch, write, argument, floating-result or effect owner.");
                previous = row; at = row.Next;
            }
            throw new NotSupportedException("Source field method exceeds its explicit complete-source inspection budget.");
        }
        finally { active.Remove(entry); }
    }

    private static FalloutEngineFieldExpression Node(FalloutEngineFieldOperation operation, uint operand = 0,
        int width = 4, IReadOnlyList<FalloutEngineFieldExpression>? inputs = null) => new(operation, operand, width, inputs ?? []);
    private static FalloutEngineFieldExpression Add(FalloutEngineFieldExpression value, int delta) => delta == 0 ? value :
        Node(FalloutEngineFieldOperation.AddAddress, unchecked((uint)delta), inputs: [value]);
    private static FalloutEngineFieldExpression Mask(FalloutEngineFieldExpression value, uint mask) =>
        Node(FalloutEngineFieldOperation.And, mask, inputs: [(mask & 0xffffff00U) == 0 && value is
            { Operation: FalloutEngineFieldOperation.Or,Width:1,Inputs.Count:1 } ? value.Inputs[0] : value]);
    private static FalloutEngineFieldExpression Substitute(FalloutEngineFieldExpression value, FalloutEngineFieldExpression? receiver,
        Dictionary<FalloutEngineFieldExpression, FalloutEngineFieldExpression> cache)
    {
        if (cache.TryGetValue(value, out var actual)) return actual;
        actual = value.Operation == FalloutEngineFieldOperation.Receiver ? receiver ??
            throw new NotSupportedException("Source nested field method has no complete receiver.") :
            value with { Inputs = value.Inputs.Select(child => Substitute(child, receiver, cache)).ToArray() };
        cache.Add(value, actual); return actual;
    }
    private static int Count(FalloutEngineFieldExpression value) => checked(1 + value.Inputs.Sum(Count));
    private static bool ContainsPartial(FalloutEngineFieldExpression value) =>
        value.Width == 1 && value.Operation == FalloutEngineFieldOperation.Or && value.Inputs.Count == 1 || value.Inputs.Any(ContainsPartial);
}
