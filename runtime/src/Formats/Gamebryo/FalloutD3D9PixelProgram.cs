using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Straight-line shader-model-2 instructions are decoded from owned bytecode.
// Register masks, swizzles and constants are data, never a fitted UI layout.
internal sealed record FalloutD3D9PixelProgram(string GodotSource, IReadOnlyList<int> Constants,
    IReadOnlyList<int> Samplers, bool PartialPrecision)
{
    private sealed record Operation(uint Opcode, uint Destination, uint[] Sources);
    private IReadOnlyList<Operation> _operations = [];
    private IReadOnlyDictionary<int, Vector4> _definitions = new Dictionary<int, Vector4>();

    internal static FalloutD3D9PixelProgram Read(ReadOnlyMemory<byte> payload, IReadOnlySet<int>? repeatingSamplers = null)
    {
        var bytes = payload.Span;
        if (bytes.Length < 8 || bytes.Length % 4 != 0) throw new InvalidDataException("Pixel program is truncated.");
        var words = new uint[bytes.Length / 4];
        for (var i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * 4)..]);
        if (words[0] is not (0xffff0200 or 0xffff0201))
            throw new NotSupportedException("Pixel program requires the shader-model-2 owner.");
        var declarations = new Dictionary<string, string>();
        var definitions = new Dictionary<int, string>();
        var literalValues = new Dictionary<int, Vector4>();
        var operations = new List<Operation>();
        var initialized = new Dictionary<string, uint> { ["t0"] = 15 };
        var referenced = new HashSet<int>(); var samplers = new HashSet<int>();
        var body = new StringBuilder(); var partial = false; var ended = false;
        string Register(uint token)
        {
            if ((token & 0x80000000) == 0 || (token & 0x2000) != 0)
                throw new NotSupportedException("Pixel register token or relative addressing is unbound.");
            var type = ((token >> 28) & 7) | ((token >> 8) & 0x18);
            var index = (int)(token & 0x7ff);
            if (type == 2) { referenced.Add(index); return $"c{index}"; }
            if (type == 10) { samplers.Add(index); return $"s{index}"; }
            var name = type switch
            {
                0 => $"r{index}",
                3 when index == 0 => "t0",
                8 when index == 0 => "o0",
                _ => throw new NotSupportedException($"Pixel register bank {type}/{index} is unbound."),
            };
            declarations.TryAdd(name, name == "t0" ? "vec4(coordinate, 0.0, 1.0)" : "vec4(0.0)");
            return name;
        }
        string Source(uint token)
        {
            var register = Register(token);
            var swizzle = new string(Enumerable.Range(0, 4).Select(i => "xyzw"[(int)((token >> (16 + i * 2)) & 3)]).ToArray());
            var value = $"{register}.{swizzle}";
            return ((token >> 24) & 15) switch
            {
                0 => value,
                1 => $"(-{value})",
                11 => $"abs({value})",
                12 => $"(-abs({value}))",
                _ => throw new NotSupportedException("Pixel source modifier is unbound."),
            };
        }
        for (var at = 1; at < words.Length;)
        {
            var instruction = words[at++]; var opcode = instruction & 0xffff;
            if (opcode == 0xffff) { ended = at == words.Length; break; }
            var count = opcode == 0xfffe ? (int)((instruction >> 16) & 0x7fff) : (int)((instruction >> 24) & 15);
            if (count > words.Length - at) throw new InvalidDataException("Pixel instruction is truncated.");
            var arguments = words.AsSpan(at, count).ToArray(); at += count;
            if (opcode == 0xfffe) continue;
            if ((instruction & 0xf0ff0000) != 0) throw new NotSupportedException("Pixel instruction controls are unbound.");
            if (opcode == 31)
            {
                if (count != 2) throw new InvalidDataException("Pixel declaration extent is invalid.");
                var register = Register(arguments[1]);
                if (register.StartsWith('s') && ((arguments[0] >> 27) & 15) != 2)
                    throw new NotSupportedException("Pixel sampler requires a 2D texture owner.");
                continue;
            }
            if (opcode == 81)
            {
                if (count != 5 || !Register(arguments[0]).StartsWith('c'))
                    throw new InvalidDataException("Pixel constant declaration is invalid.");
                var values = arguments.Skip(1).Select(word => BitConverter.UInt32BitsToSingle(word)).ToArray();
                if (values.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Pixel constant is non-finite.");
                definitions.Add((int)(arguments[0] & 0x7ff), $"vec4({string.Join(", ", values.Select(Literal))})");
                literalValues.Add((int)(arguments[0] & 0x7ff), new(values[0], values[1], values[2], values[3]));
                continue;
            }
            var expected = opcode switch { 1 or 6 or 7 or 35 => 2, 2 or 3 or 5 or 8 or 9 or 10 or 11 or 66 => 3, 4 or 88 or 90 => 4, _ => 0 };
            if (expected == 0) throw new NotSupportedException($"Pixel opcode {opcode} is unbound.");
            if (count != expected) throw new InvalidDataException("Pixel instruction operand count is invalid.");
            var destination = Register(arguments[0]);
            if (!destination.StartsWith('r') && destination != "o0") throw new InvalidDataException("Pixel destination is not writable.");
            if ((arguments[0] & 0x0f000000) != 0) throw new NotSupportedException("Pixel destination shift is unbound.");
            var modifier = (arguments[0] >> 20) & 15;
            if ((modifier & ~3U) != 0) throw new NotSupportedException("Pixel destination modifier is unbound.");
            partial |= (modifier & 2) != 0;
            var destinationMask = (arguments[0] >> 16) & 15;
            for (var operand = 1; operand < arguments.Length; operand++)
            {
                var token = arguments[operand]; var register = Register(token);
                if (opcode == 66 && operand == 2)
                {
                    if (!register.StartsWith('s')) throw new InvalidDataException("Texture instruction requires a sampler register.");
                    continue;
                }
                if (register.StartsWith('s')) throw new InvalidDataException("A sampler cannot be an arithmetic operand.");
                var needed = opcode switch
                {
                    6 or 7 => 1U,
                    8 => 7U,
                    9 => 15U,
                    66 => 3U,
                    90 => operand == 3 ? 1U : 3U,
                    _ => destinationMask,
                };
                uint readMask = 0;
                for (var component = 0; component < 4; component++)
                    if ((needed & (1U << component)) != 0) readMask |= 1U << (int)((token >> (16 + component * 2)) & 3);
                if (!register.StartsWith('c') && (initialized.GetValueOrDefault(register) & readMask) != readMask)
                    throw new InvalidDataException($"Pixel program reads an unwritten component of {register}.");
            }
            var a = Source(arguments[1]);
            var b = count > 2 && opcode != 66 ? Source(arguments[2]) : "";
            var c = count > 3 ? Source(arguments[3]) : "";
            var expression = opcode switch
            {
                1 => a,
                2 => $"({a} + {b})",
                3 => $"({a} - {b})",
                4 => $"({a} * {b} + {c})",
                5 => $"({a} * {b})",
                6 => $"vec4(1.0 / ({a}).x)",
                7 => $"vec4(inversesqrt(abs(({a}).x)))",
                8 => $"vec4(dot(({a}).xyz, ({b}).xyz))",
                9 => $"vec4(dot({a}, {b}))",
                10 => $"min({a}, {b})",
                11 => $"max({a}, {b})",
                35 => $"abs({a})",
                66 => $"texture({Register(arguments[2])}, ({a}).xy)",
                88 => $"mix({c}, {b}, greaterThanEqual({a}, vec4(0.0)))",
                90 => $"vec4(dot(({a}).xy, ({b}).xy) + ({c}).x)",
                _ => throw new InvalidDataException("Unbound pixel expression."),
            };
            if ((modifier & 1) != 0) expression = $"clamp({expression}, vec4(0.0), vec4(1.0))";
            var mask = new string(Enumerable.Range(0, 4).Where(i => (arguments[0] & (1U << (16 + i))) != 0).Select(i => "xyzw"[i]).ToArray());
            if (mask.Length == 0) throw new InvalidDataException("Pixel destination mask is empty.");
            body.AppendLine($"    {destination}.{mask} = ({expression}).{mask};");
            initialized[destination] = initialized.GetValueOrDefault(destination) | destinationMask;
            operations.Add(new(opcode, arguments[0], arguments[1..]));
        }
        if (!ended || initialized.GetValueOrDefault("o0") != 15) throw new InvalidDataException("Pixel program has no complete output/end.");
        var external = referenced.Except(definitions.Keys).Order().ToArray();
        var result = new StringBuilder();
        foreach (var index in external) result.AppendLine($"uniform vec4 c{index};");
        foreach (var index in samplers.Order()) result.AppendLine($"uniform sampler2D s{index} : filter_linear, {(repeatingSamplers?.Contains(index) == true ? "repeat_enable" : "repeat_disable")};");
        result.AppendLine("vec4 owned_pixel_program(vec2 coordinate) {");
        foreach (var (name, value) in declarations) result.AppendLine($"    vec4 {name} = {value};");
        foreach (var (index, value) in definitions) result.AppendLine($"    vec4 c{index} = {value};");
        result.Append(body).AppendLine("    return o0;\n}");
        return new(result.ToString(), external, samplers.Order().ToArray(), partial)
        { _operations = operations, _definitions = literalValues };
    }

    // The input adapter evaluates the same admitted instruction stream to map
    // model UVs into source-canvas samples. This is also a CPU oracle for the
    // translator; it never substitutes sampled retail state for game state.
    internal Vector4 Evaluate(Vector2 coordinate, IReadOnlyDictionary<int, Vector4> constants,
        Func<int, Vector2, Vector4> sample)
    {
        var registers = new Dictionary<(uint Bank, int Index), Vector4> { [(3, 0)] = new(coordinate, 0, 1) };
        static (uint Bank, int Index) Key(uint token) => (((token >> 28) & 7) | ((token >> 8) & 0x18), (int)(token & 0x7ff));
        Vector4 Read(uint token)
        {
            var key = Key(token);
            var value = key.Bank == 2
                ? _definitions.TryGetValue(key.Index, out var literal) ? literal : constants[key.Index]
                : registers[key];
            var result = new Vector4(value[(int)((token >> 16) & 3)], value[(int)((token >> 18) & 3)],
                value[(int)((token >> 20) & 3)], value[(int)((token >> 22) & 3)]);
            return ((token >> 24) & 15) switch { 0 => result, 1 => -result, 11 => Vector4.Abs(result), 12 => -Vector4.Abs(result), _ => throw new InvalidDataException("Unbound source modifier.") };
        }
        foreach (var operation in _operations)
        {
            var a = Read(operation.Sources[0]);
            var b = operation.Sources.Length > 1 && operation.Opcode != 66 ? Read(operation.Sources[1]) : Vector4.Zero;
            var c = operation.Sources.Length > 2 ? Read(operation.Sources[2]) : Vector4.Zero;
            var result = operation.Opcode switch
            {
                1 => a,
                2 => a + b,
                3 => a - b,
                4 => a * b + c,
                5 => a * b,
                6 => new Vector4(1 / a.X),
                7 => new Vector4(1 / MathF.Sqrt(MathF.Abs(a.X))),
                8 => new Vector4(a.X * b.X + a.Y * b.Y + a.Z * b.Z),
                9 => new Vector4(Vector4.Dot(a, b)),
                10 => Vector4.Min(a, b),
                11 => Vector4.Max(a, b),
                35 => Vector4.Abs(a),
                66 => sample(Key(operation.Sources[1]).Index, new(a.X, a.Y)),
                88 => new Vector4(a.X >= 0 ? b.X : c.X, a.Y >= 0 ? b.Y : c.Y, a.Z >= 0 ? b.Z : c.Z, a.W >= 0 ? b.W : c.W),
                90 => new Vector4(a.X * b.X + a.Y * b.Y + c.X),
                _ => throw new InvalidDataException("Unbound pixel operation."),
            };
            if ((operation.Destination & (1U << 20)) != 0) result = Vector4.Clamp(result, Vector4.Zero, Vector4.One);
            var destination = Key(operation.Destination);
            var previous = registers.GetValueOrDefault(destination);
            for (var component = 0; component < 4; component++)
                if ((operation.Destination & (1U << (16 + component))) != 0) previous[component] = result[component];
            registers[destination] = previous;
        }
        return registers[(8, 0)];
    }

    internal Vector2 VisibleSampleCoordinate(int sampler, Vector2 coordinate, IReadOnlyDictionary<int, Vector4> constants,
        Func<int, Vector2, Vector4> otherSample)
    {
        var coordinates = new List<Vector2>();
        var baseline = Evaluate(coordinate, constants, (index, uv) =>
        {
            if (index != sampler) return otherSample(index, uv);
            coordinates.Add(uv); return Vector4.Zero;
        });
        Vector2? selected = null;
        for (var candidate = 0; candidate < coordinates.Count; candidate++)
        {
            var ordinal = 0;
            var output = Evaluate(coordinate, constants, (index, uv) =>
            {
                if (index != sampler) return otherSample(index, uv);
                if (ordinal >= coordinates.Count || uv != coordinates[ordinal])
                    throw new NotSupportedException("Screen sample coordinates depend on sampled content.");
                return ordinal++ == candidate ? Vector4.One : Vector4.Zero;
            });
            if (ordinal != coordinates.Count) throw new InvalidDataException("Screen sampling changed across evaluation.");
            if (new Vector3(output.X, output.Y, output.Z) == new Vector3(baseline.X, baseline.Y, baseline.Z)) continue;
            if (selected is { } previous && previous != coordinates[candidate])
                throw new NotSupportedException("Screen effects combine different source coordinates; input mapping needs their interaction policy.");
            selected = coordinates[candidate];
        }
        return selected ?? throw new NotSupportedException("Screen sample has no visible contribution.");
    }

    private static string Literal(float value)
    {
        var result = value.ToString("R", CultureInfo.InvariantCulture);
        return result.Contains('.') || result.Contains('E') ? result : result + ".0";
    }
}
