using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal enum FalloutSkyProgramStage { Vertex, Pixel }
internal sealed record FalloutSkyProgramSemantic(int Register, int Usage, int Index, uint Mask);

// Straight-line SM2/SM3 are input formats. Shader operations and declarations are
// read from the selected SDP; neither executable code nor derivative assets are
// copied into this owner. A different instruction/input/constant requires its
// actual consumer before it can be published to Godot.
internal sealed class FalloutD3D9SkyProgram
{
    private const uint SaturateModifier = 0x00100000;
    private const uint IdentitySwizzle = 0xe4;
    private const uint DeclarationParameterBit = 0x80000000;
    private const uint SemanticDeclarationBits = 0x800f001f;
    private const uint SamplerDeclarationBits = 0xf8000000;
    private const uint LastDeclarationUsage = 13;
    private sealed record Instruction(uint Opcode, uint Destination, uint[] Sources);
    private readonly List<Instruction> _instructions = [];
    private readonly Dictionary<int, uint[]> _definitions = [];
    private readonly List<FalloutSkyProgramSemantic> _inputs = [], _outputs = [];
    private readonly HashSet<int> _constants = [], _samplers = [];
    private readonly Dictionary<int, FalloutSkyProgramSemantic> _fixedOutputs = [];
    internal FalloutSkyProgramStage Stage { get; }
    internal int MajorVersion { get; }
    internal int MinorVersion { get; }
    internal IReadOnlyList<FalloutSkyProgramSemantic> Inputs => _inputs;
    internal IReadOnlyList<FalloutSkyProgramSemantic> Outputs => _outputs;
    internal IReadOnlyList<int> Constants => _constants.Except(_definitions.Keys).Order().ToArray();
    internal IReadOnlyList<int> Samplers => _samplers.Order().ToArray();
    private FalloutD3D9SkyProgram(FalloutSkyProgramStage stage, int major, int minor)
    { Stage = stage; MajorVersion = major; MinorVersion = minor; }
    private static int Bank(uint token) => (int)(((token >> 28) & 7) | ((token >> 8) & 0x18));
    private static int Index(uint token) => (int)(token & 0x7ff);
    private static uint Mask(uint token) => (token >> 16) & 15;
    private static uint Swizzle(uint token) => (token >> 16) & 255;
    private static uint SourceModifier(uint token) => (token >> 24) & 15;
    // SM2 has separate fixed colour/texture banks. Keep their identities
    // distinct while exposing one register key to the semantic consumer.
    private int RegisterKey(uint token) => MajorVersion == 2 ? (Stage, Bank(token)) switch
    {
        (FalloutSkyProgramStage.Pixel, 1) => 8 + Index(token),
        (FalloutSkyProgramStage.Vertex, 5) => 9 + Index(token),
        (FalloutSkyProgramStage.Vertex, 6) => 1 + Index(token),
        _ => Index(token),
    } : Index(token);

    private string Register(uint token)
    {
        if ((token & 0x8000e000) != 0x80000000) throw new NotSupportedException("Sky register marker, reserved controls or relative addressing is unowned.");
        return Bank(token) switch
        {
            0 => "r" + Index(token),
            1 when Stage == FalloutSkyProgramStage.Vertex || MajorVersion == 3 || Index(token) < 2 => "v" + RegisterKey(token),
            3 when Stage == FalloutSkyProgramStage.Pixel && MajorVersion == 2 && Index(token) < 8 => "v" + RegisterKey(token),
            2 => "c" + Index(token),
            4 when Stage == FalloutSkyProgramStage.Vertex && MajorVersion == 2 && Index(token) == 0 => "o0",
            5 when Stage == FalloutSkyProgramStage.Vertex && MajorVersion == 2 && Index(token) < 2 => "o" + RegisterKey(token),
            6 when Stage == FalloutSkyProgramStage.Vertex && (MajorVersion == 3 || Index(token) < 8) => "o" + RegisterKey(token),
            8 when Stage == FalloutSkyProgramStage.Pixel && Index(token) == 0 => "o0",
            10 => "s" + Index(token),
            _ => throw new NotSupportedException("Sky register bank is unowned."),
        };
    }
    internal static FalloutD3D9SkyProgram Read(ReadOnlyMemory<byte> payload, FalloutSkyProgramStage stage)
    {
        var bytes = payload.Span;
        if (bytes.Length < 8 || bytes.Length % 4 != 0) throw new InvalidDataException("Sky program is truncated.");
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(payload.Span[(at * 4)..]);
        var version = Word(0); var major = (int)((version >> 8) & 255); var minor = (int)(version & 255);
        if (!Enum.IsDefined(stage) || (version >> 16) != (stage == FalloutSkyProgramStage.Vertex ? 0xfffeU : 0xffffU) ||
            !(major == 3 && minor == 0 || major == 2 && (minor == 0 || stage == FalloutSkyProgramStage.Pixel && minor == 1)))
            throw new NotSupportedException("Sky program version or stage has no actual SM2/SM3 consumer.");
        var result = new FalloutD3D9SkyProgram(stage, major, minor);
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
                var name = result.Register(operands[1]); var bank = Bank(operands[1]); var mask = Mask(operands[1]);
                if ((operands[0] & DeclarationParameterBit) == 0 || (operands[1] & 0x0ff0e000) != 0)
                    throw new NotSupportedException("Sky declaration omitted its parameter marker or has unowned reserved/destination controls.");
                if (mask == 0 || !declared.TryAdd(name, mask)) throw new InvalidDataException("Sky declaration is empty or repeated.");
                if (bank == 10)
                {
                    if (stage != FalloutSkyProgramStage.Pixel || (operands[0] & ~SamplerDeclarationBits) != 0 ||
                        ((operands[0] >> 27) & 15) != 2)
                        throw new NotSupportedException("Sky sampler has no admitted 2D pixel owner.");
                    result._samplers.Add(Index(operands[1])); initialized[name] = mask;
                }
                else
                {
                    if ((operands[0] & ~SemanticDeclarationBits) != 0 || (operands[0] & 31) > LastDeclarationUsage)
                        throw new NotSupportedException("Sky semantic declaration has reserved bits or an unsupported full usage field.");
                    var row = major == 2 && stage == FalloutSkyProgramStage.Pixel
                        ? operands[0] != 0x80000000U || bank is not (1 or 3)
                            ? throw new NotSupportedException("SM2 pixel input requires its actual fixed colour/texture declaration.")
                            : new FalloutSkyProgramSemantic(result.RegisterKey(operands[1]), bank == 1 ? 10 : 5, Index(operands[1]), mask)
                        : new FalloutSkyProgramSemantic(Index(operands[1]), (int)(operands[0] & 31),
                            (int)((operands[0] >> 16) & 15), mask);
                    if (bank == 1 || bank == 3 && major == 2 && stage == FalloutSkyProgramStage.Pixel)
                    { result._inputs.Add(row); initialized[name] = mask; }
                    else if (bank == 6 && major == 3 && stage == FalloutSkyProgramStage.Vertex) result._outputs.Add(row);
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
                _ = result.Register(operands[0]); initialized[result.Register(operands[0])] = 15; continue;
            }
            var extent = opcode switch { 1 => 2, 2 or 3 or 5 or 8 or 9 or 66 => 3, 4 or 18 or 88 => 4, _ => 0 };
            if (extent == 0) throw new NotSupportedException($"Sky instruction {opcode} has no actual consumer.");
            if (count != extent) throw new InvalidDataException("Sky instruction operand extent changed.");
            var destination = result.Register(operands[0]); var destinationBank = Bank(operands[0]); var maskWrite = Mask(operands[0]);
            var vertexOutput = stage == FalloutSkyProgramStage.Vertex &&
                (major == 3 && destinationBank == 6 || major == 2 && destinationBank is 4 or 5 or 6);
            if (destinationBank != 0 && !vertexOutput && !(destinationBank == 8 && stage == FalloutSkyProgramStage.Pixel) || maskWrite == 0 ||
                (operands[0] & 0x0fe00000) != 0)
                throw new NotSupportedException("Sky destination mask/modifier/stage is unowned.");
            if (vertexOutput && major == 3 && !declared.ContainsKey(destination))
                throw new InvalidDataException("Sky vertex output was not declared.");
            if (major == 2 && stage == FalloutSkyProgramStage.Pixel && destinationBank == 8 &&
                (operands[0] & SaturateModifier) != 0)
                throw new NotSupportedException("SM2 pixel output cannot carry a saturation modifier.");
            if (opcode == 66)
            {
                // The emitter consumes a sampler identity. Its independent
                // source modifiers/swizzle cannot be silently discarded.
                if (stage != FalloutSkyProgramStage.Pixel || Bank(operands[2]) != 10 ||
                    SourceModifier(operands[2]) != 0 || Swizzle(operands[2]) != IdentitySwizzle ||
                    (operands[0] & SaturateModifier) != 0)
                    throw new NotSupportedException("Sky TEXLD sampler modifier/swizzle or saturation has no actual consumer.");
                if (major == 2 && (destinationBank != 0 || maskWrite != 15 ||
                    Bank(operands[1]) is not (0 or 3) || SourceModifier(operands[1]) != 0 ||
                    Swizzle(operands[1]) != IdentitySwizzle))
                    throw new NotSupportedException("SM2 TEXLD requires its full temporary destination and direct texture/temporary coordinates.");
            }
            foreach (var source in operands.Skip(1))
            {
                var name = result.Register(source); var bank = Bank(source);
                if (bank is not (0 or 1 or 2 or 10) && !(major == 2 && stage == FalloutSkyProgramStage.Pixel && bank == 3))
                    throw new NotSupportedException("Sky source bank has no actual read-only input owner.");
                var modifier = SourceModifier(source);
                if (modifier is not (0 or 1 or 11 or 12) ||
                    major == 2 && stage == FalloutSkyProgramStage.Pixel && modifier is 11 or 12)
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
            if (vertexOutput && major == 2)
            {
                var key = result.RegisterKey(operands[0]);
                result._fixedOutputs[key] = new(key, destinationBank == 4 ? 0 : destinationBank == 5 ? 10 : 5,
                    Index(operands[0]), initialized[destination]);
            }
            result._instructions.Add(new(opcode, operands[0], operands[1..]));
        }
        if (!ended || result._instructions.Count == 0) throw new InvalidDataException("Sky program lacks its complete terminal instruction.");
        if (major == 2 && stage == FalloutSkyProgramStage.Vertex)
        {
            result._outputs.AddRange(result._fixedOutputs.OrderBy(row => row.Key).Select(row => row.Value));
            if (initialized.GetValueOrDefault("o0") != 15 || result._outputs.Where(row => row.Usage == 5)
                .Any(row => row.Mask is not (1 or 3 or 7 or 15) ||
                    Enumerable.Range(0, row.Index).Any(index => !result._fixedOutputs.ContainsKey(index + 1))))
                throw new InvalidDataException("SM2 vertex program omitted its fixed position or preceding texture output.");
        }
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
        foreach (var (register, value) in inputs)
        {
            // SM2 colour interpolators arrive saturated. SM3 generic inputs
            // do not inherit this independent fixed-function range rule.
            var expression = Stage == FalloutSkyProgramStage.Pixel && MajorVersion == 2 &&
                _inputs.Single(row => row.Register == register).Usage == 10
                ? "clamp(" + value + ", vec4(0.0), vec4(1.0))" : value;
            text.AppendLine($"    vec4 v{register} = {expression};");
        }
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
            if ((row.Destination & SaturateModifier) != 0) value = "clamp(" + value + ", vec4(0.0), vec4(1.0))";
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
