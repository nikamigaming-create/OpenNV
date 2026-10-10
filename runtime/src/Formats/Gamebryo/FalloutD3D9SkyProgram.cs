using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal enum FalloutSkyProgramStage { Vertex, Pixel }
internal sealed record FalloutSkyProgramSemantic(int Register, int Usage, int Index, uint Mask);

// Straight-line SM3 is an input format. Shader operations and declarations are
// read from the selected SDP; neither executable code nor derivative assets are
// copied into this owner. A different instruction/input/constant requires its
// actual consumer before it can be published to Godot.
internal sealed class FalloutD3D9SkyProgram
{
    private sealed record Instruction(uint Opcode, uint Destination, uint[] Sources);
    private readonly List<Instruction> _instructions = [];
    private readonly Dictionary<int, uint[]> _definitions = [];
    private readonly List<FalloutSkyProgramSemantic> _inputs = [], _outputs = [];
    private readonly HashSet<int> _constants = [], _samplers = [];
    internal FalloutSkyProgramStage Stage { get; }
    internal IReadOnlyList<FalloutSkyProgramSemantic> Inputs => _inputs;
    internal IReadOnlyList<FalloutSkyProgramSemantic> Outputs => _outputs;
    internal IReadOnlyList<int> Constants => _constants.Except(_definitions.Keys).Order().ToArray();
    internal IReadOnlyList<int> Samplers => _samplers.Order().ToArray();
    internal FalloutD3D9SkyProgram(FalloutSkyProgramStage stage) => Stage = stage;
    private static int Bank(uint token) => (int)(((token >> 28) & 7) | ((token >> 8) & 0x18));
    private static int Index(uint token) => (int)(token & 0x7ff);
    private static uint Mask(uint token) => (token >> 16) & 15;
    private static string Register(uint token)
    {
        if ((token & 0x80002000) != 0x80000000) throw new NotSupportedException("Sky register token/relative addressing is unowned.");
        return Bank(token) switch
        {
            0 => "r" + Index(token),
            1 => "v" + Index(token),
            2 => "c" + Index(token),
            6 or 8 => "o" + Index(token),
            10 => "s" + Index(token),
            _ => throw new NotSupportedException("Sky register bank is unowned."),
        };
    }
    internal static FalloutD3D9SkyProgram Read(ReadOnlyMemory<byte> payload, FalloutSkyProgramStage stage)
    {
        var bytes = payload.Span;
        if (bytes.Length < 8 || bytes.Length % 4 != 0) throw new InvalidDataException("Sky program is truncated.");
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(payload.Span[(at * 4)..]);
        if (Word(0) != (stage == FalloutSkyProgramStage.Vertex ? 0xfffe0300U : 0xffff0300U))
            throw new NotSupportedException("Sky program requires its actual shader-model-3 stage.");
        var result = new FalloutD3D9SkyProgram(stage);
        var declared = new Dictionary<string, uint>(); var initialized = new Dictionary<string, uint>();
        var ended = false;
        for (var at = 1; at < bytes.Length / 4;)
        {
            var token = Word(at++); var opcode = token & 0xffff;
            if (opcode == 0xffff) { ended = at == bytes.Length / 4; break; }
            var count = opcode == 0xfffe ? (int)((token >> 16) & 0x7fff) : (int)((token >> 24) & 15);
            if (count > bytes.Length / 4 - at) throw new InvalidDataException("Sky instruction exceeds its declared program.");
            var operands = Enumerable.Range(at, count).Select(Word).ToArray(); at += count;
            if (opcode == 0xfffe) continue;
            if ((token & 0xf0ff0000) != 0) throw new NotSupportedException("Sky instruction controls are unowned.");
            if (opcode == 31)
            {
                if (count != 2) throw new InvalidDataException("Sky declaration has an invalid extent.");
                var name = Register(operands[1]); var bank = Bank(operands[1]); var mask = Mask(operands[1]);
                if (mask == 0 || !declared.TryAdd(name, mask)) throw new InvalidDataException("Sky declaration is empty or repeated.");
                if (bank == 10)
                {
                    if (stage != FalloutSkyProgramStage.Pixel || ((operands[0] >> 27) & 15) != 2)
                        throw new NotSupportedException("Sky sampler has no admitted 2D pixel owner.");
                    result._samplers.Add(Index(operands[1])); initialized[name] = mask;
                }
                else
                {
                    var row = new FalloutSkyProgramSemantic(Index(operands[1]), (int)(operands[0] & 15),
                        (int)((operands[0] >> 16) & 15), mask);
                    if (bank == 1) { result._inputs.Add(row); initialized[name] = mask; }
                    else if (bank == 6 && stage == FalloutSkyProgramStage.Vertex) result._outputs.Add(row);
                    else throw new NotSupportedException("Sky declaration has another stage/register owner.");
                }
                continue;
            }
            if (opcode == 81)
            {
                if (count != 5 || Bank(operands[0]) != 2 || Mask(operands[0]) != 15 ||
                    operands.Skip(1).Any(value => !float.IsFinite(BitConverter.UInt32BitsToSingle(value))) ||
                    !result._definitions.TryAdd(Index(operands[0]), operands[1..]))
                    throw new InvalidDataException("Sky literal constant is malformed/repeated/non-finite.");
                _ = Register(operands[0]); initialized[Register(operands[0])] = 15; continue;
            }
            var extent = opcode switch { 1 => 2, 2 or 3 or 5 or 8 or 9 or 66 => 3, 4 or 18 or 88 => 4, _ => 0 };
            if (extent == 0) throw new NotSupportedException($"Sky instruction {opcode} has no actual consumer.");
            if (count != extent) throw new InvalidDataException("Sky instruction operand extent changed.");
            var destination = Register(operands[0]); var destinationBank = Bank(operands[0]); var maskWrite = Mask(operands[0]);
            if (destinationBank is not (0 or 6 or 8) || destinationBank == 6 && stage != FalloutSkyProgramStage.Vertex ||
                destinationBank == 8 && (stage != FalloutSkyProgramStage.Pixel || Index(operands[0]) != 0) || maskWrite == 0 ||
                (operands[0] & 0x0fe00000) != 0)
                throw new NotSupportedException("Sky destination mask/modifier/stage is unowned.");
            foreach (var source in operands.Skip(1))
            {
                var name = Register(source); var bank = Bank(source);
                if (((source >> 24) & 15) is not (0 or 1 or 11 or 12))
                    throw new NotSupportedException("Sky source modifier is unowned.");
                if (bank == 2) { result._constants.Add(Index(source)); continue; }
                if (bank == 10)
                {
                    if (opcode != 66 || !declared.ContainsKey(name)) throw new InvalidDataException("Sky sampler was not declared for this sample.");
                    continue;
                }
                uint used = 0;
                var consumed = opcode == 9 ? 15U : opcode == 8 ? 7U : opcode == 66 ? 3U : maskWrite;
                for (var component = 0; component < 4; component++)
                    if ((consumed & 1U << component) != 0) used |= 1U << (int)((source >> (16 + component * 2)) & 3);
                if (!initialized.TryGetValue(name, out var available) || (available & used) != used)
                    throw new InvalidDataException("Sky program reads a component before its actual writer.");
            }
            if (opcode == 66 && (stage != FalloutSkyProgramStage.Pixel || Bank(operands[2]) != 10))
                throw new NotSupportedException("Sky texture instruction has no admitted pixel sampler.");
            initialized[destination] = initialized.GetValueOrDefault(destination) | maskWrite;
            result._instructions.Add(new(opcode, operands[0], operands[1..]));
        }
        if (!ended || result._instructions.Count == 0) throw new InvalidDataException("Sky program lacks its complete terminal instruction.");
        if (stage == FalloutSkyProgramStage.Pixel && initialized.GetValueOrDefault("o0") != 15 ||
            stage == FalloutSkyProgramStage.Vertex && result._outputs.Any(row =>
                (initialized.GetValueOrDefault("o" + row.Register) & row.Mask) != row.Mask))
            throw new InvalidDataException("Sky program omitted a declared output writer.");
        return result;
    }
    internal uint DefinedWord(int constant, int component) => _definitions.TryGetValue(constant, out var row) && component is >= 0 and < 4
        ? row[component] : throw new InvalidDataException("Sky program has no such literal constant component.");
    internal string Emit(string name, IReadOnlyDictionary<int, string> inputs, IReadOnlyDictionary<int, string> constants,
        IReadOnlyDictionary<int, string> samplers, IReadOnlyDictionary<int, string> outputs, string prologue = "", string epilogue = "")
    {
        if (!inputs.Keys.ToHashSet().SetEquals(_inputs.Select(row => row.Register)) ||
            !constants.Keys.ToHashSet().SetEquals(Constants) || !samplers.Keys.ToHashSet().SetEquals(Samplers) ||
            !outputs.Keys.ToHashSet().SetEquals(Stage == FalloutSkyProgramStage.Pixel ? [0] : _outputs.Select(row => row.Register)))
            throw new NotSupportedException("Sky program publication omitted/introduced an actual input, constant, sampler or output owner.");
        var text = new StringBuilder("void " + name + "() {\n");
        text.AppendLine(prologue);
        var registers = _instructions.SelectMany(row => row.Sources.Prepend(row.Destination)).Select(Register)
            .Where(row => row.StartsWith('r') || row.StartsWith('o')).Distinct().Order();
        foreach (var register in registers) text.AppendLine($"    vec4 {register} = vec4(0.0);");
        foreach (var (register, value) in inputs) text.AppendLine($"    vec4 v{register} = {value};");
        foreach (var (register, values) in _definitions)
            text.AppendLine($"    vec4 c{register} = vec4({string.Join(", ", values.Select(value => Number(BitConverter.UInt32BitsToSingle(value))))});");
        foreach (var (register, value) in constants) text.AppendLine($"    vec4 c{register} = {value};");
        foreach (var row in _instructions)
        {
            string Source(int index)
            {
                var token = row.Sources[index]; var register = Register(token);
                if (Bank(token) == 10) return samplers[Index(token)];
                var swizzle = new string(Enumerable.Range(0, 4).Select(i => "xyzw"[(int)((token >> (16 + i * 2)) & 3)]).ToArray());
                var value = register + "." + swizzle;
                return ((token >> 24) & 15) switch { 1 => "(-" + value + ")", 11 => "abs(" + value + ")", 12 => "(-abs(" + value + "))", _ => value };
            }
            var value = row.Opcode switch
            {
                1 => Source(0),
                2 => "(" + Source(0) + " + " + Source(1) + ")",
                3 => "(" + Source(0) + " - " + Source(1) + ")",
                5 => "(" + Source(0) + " * " + Source(1) + ")",
                4 => "(" + Source(0) + " * " + Source(1) + " + " + Source(2) + ")",
                8 => "vec4(dot((" + Source(0) + ").xyz, (" + Source(1) + ").xyz))",
                9 => "vec4(dot(" + Source(0) + ", " + Source(1) + "))",
                18 => "(" + Source(0) + " * " + Source(1) + " + (vec4(1.0) - " + Source(0) + ") * " + Source(2) + ")",
                66 => "texture(" + Source(1) + ", (" + Source(0) + ").xy)",
                88 => "mix(" + Source(2) + ", " + Source(1) + ", step(vec4(0.0), " + Source(0) + "))",
                _ => throw new InvalidOperationException("Sky decoder retained an unowned instruction."),
            };
            if ((row.Destination & 0x00100000) != 0) value = "clamp(" + value + ", vec4(0.0), vec4(1.0))";
            var mask = new string(Enumerable.Range(0, 4).Where(i => (Mask(row.Destination) & 1U << i) != 0).Select(i => "xyzw"[i]).ToArray());
            text.AppendLine($"    {Register(row.Destination)}.{mask} = ({value}).{mask};");
        }
        foreach (var (register, target) in outputs) text.AppendLine($"    {target} = o{register};");
        text.AppendLine(epilogue);
        return text.AppendLine("}").ToString();
    }
    private static string Number(float value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : text + ".0";
    }
}
