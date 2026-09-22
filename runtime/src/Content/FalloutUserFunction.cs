using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutUserFunction(FalloutPluginRecord Script, FalloutGameModeProgram Program,
    IReadOnlyList<string> Parameters, IReadOnlyDictionary<string, uint> Slots, IReadOnlyDictionary<string, string> Types)
{
    internal static FalloutUserFunction Read(FalloutPluginRecord script)
    {
        if (script.Signature != "SCPT") throw new InvalidDataException("User function target is not SCPT.");
        var fields = script.ReadSubrecords().ToArray();
        var header = fields.Single(field => field.Signature == "SCHR").Data;
        if (header.Length != 20 || BinaryPrimitives.ReadUInt16LittleEndian(header.Span[16..]) != 0)
            throw new NotSupportedException("User function must be an object-type source script.");
        var source = FalloutDialogueTopic.ScriptText(fields.Single(field => field.Signature == "SCTX").Data.Span);
        var blocks = FalloutGameModeProgram.ReadEvents(source);
        if (blocks.Count != 1 || blocks[0].Parameters is not { } parameters)
            throw new InvalidDataException("User function needs exactly one Function block.");
        var slots = FalloutScriptLocals.Read(script);
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in source.Split('\n'))
        {
            var tokens = FalloutGameModeProgram.Tokens(FalloutGameModeProgram.StripComment(line).Trim());
            if (tokens.Length == 0) continue;
            var kind = tokens[0].ToLowerInvariant();
            if (kind == "reference") kind = "ref";
            if (kind is not ("int" or "short" or "long" or "float" or "ref" or "string_var" or "array_var")) continue;
            if (tokens.Length != 2 || !slots.ContainsKey(tokens[1]) || !types.TryAdd(tokens[1], kind))
                throw new NotSupportedException("User function local declaration is ambiguous or unbound.");
        }
        if (types.Count != slots.Count || parameters.Any(name => !slots.ContainsKey(name)))
            throw new InvalidDataException("Function source locals differ from compiled slots.");
        return new(script, blocks[0].Program, parameters, slots, types);
    }

    internal void RequireScalar(string name)
    {
        if (Types[name] is "array_var")
            throw new NotSupportedException($"Function local {name} needs an array value owner.");
    }

    internal FalloutScriptLocalKind Kind(string name) => Types[name] switch
    {
        "ref" => FalloutScriptLocalKind.Form,
        "string_var" => FalloutScriptLocalKind.String,
        "array_var" => FalloutScriptLocalKind.Array,
        _ => FalloutScriptLocalKind.Number,
    };
}

internal sealed class FalloutUserFunctionFrame(FalloutUserFunction definition)
{
    internal FalloutUserFunction Definition { get; } = definition;
    private readonly Dictionary<uint, FalloutScriptValue> _values = [];
    private FalloutScriptValue _result;
    internal double Result { get => ResultValue.Number; set => ResultValue = value; }
    internal FalloutScriptValue ResultValue
    {
        get => _result;
        set => _result = value;
    }
    internal bool Contains(string name) => Definition.Slots.ContainsKey(name);
    internal FalloutScriptValue ReadValue(string name)
    {
        Definition.RequireScalar(name);
        return _values.GetValueOrDefault(Definition.Slots[name], Default(Definition.Kind(name)));
    }
    internal double Read(string name) => ReadValue(name).Number;
    internal void Write(string name, double value) => WriteValue(name, value);
    internal void WriteValue(string name, FalloutScriptValue value)
    {
        Definition.RequireScalar(name);
        var kind = Definition.Kind(name);
        _values[Definition.Slots[name]] = kind switch
        {
            FalloutScriptLocalKind.Number => value.Number,
            FalloutScriptLocalKind.Form => FalloutScriptValue.Form(value.Number),
            FalloutScriptLocalKind.String when value.Kind == FalloutScriptValueKind.String => value,
            FalloutScriptLocalKind.String => throw new InvalidDataException("Function string local needs text."),
            FalloutScriptLocalKind.Array => throw new NotSupportedException("Array local needs an array value owner."),
            _ => throw new InvalidDataException("Function local kind is invalid."),
        };
    }

    private static FalloutScriptValue Default(FalloutScriptLocalKind kind) => kind switch
    {
        FalloutScriptLocalKind.Form => FalloutScriptValue.Form(0),
        FalloutScriptLocalKind.String => FalloutScriptValue.String(string.Empty),
        FalloutScriptLocalKind.Array => throw new NotSupportedException("Array local needs an array value owner."),
        _ => 0,
    };
}
