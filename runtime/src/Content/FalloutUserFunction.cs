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

    internal FalloutScriptLocalKind Kind(string name) => Types[name] switch
    {
        "ref" => FalloutScriptLocalKind.Form,
        "string_var" => FalloutScriptLocalKind.String,
        "array_var" => FalloutScriptLocalKind.Array,
        _ => FalloutScriptLocalKind.Number,
    };
}

internal sealed class FalloutUserFunctionFrame(FalloutUserFunction definition, FalloutScriptArrayStore arrays) : IDisposable
{
    internal FalloutUserFunction Definition { get; } = definition;
    private readonly Dictionary<uint, FalloutScriptValue> _values = [];
    private FalloutScriptValue _result;
    private bool _disposed;
    internal double Result { get => ResultValue.Number; set => ResultValue = value; }
    internal FalloutScriptValue ResultValue
    {
        get => _result;
        set
        {
            if (value.Kind == FalloutScriptValueKind.Array) arrays.Retain(value);
            if (_result.Kind == FalloutScriptValueKind.Array) arrays.Release(_result);
            _result = value;
        }
    }
    internal bool Contains(string name) => Definition.Slots.ContainsKey(name);
    internal FalloutScriptValue ReadValue(string name)
    {
        return _values.GetValueOrDefault(Definition.Slots[name], Default(Definition.Kind(name)));
    }
    internal double Read(string name) => ReadValue(name).Number;
    internal void Write(string name, double value) => WriteValue(name, value);
    internal void WriteValue(string name, FalloutScriptValue value)
    {
        var kind = Definition.Kind(name);
        if (value.Kind == FalloutScriptValueKind.Array && kind != FalloutScriptLocalKind.Array)
            throw new InvalidDataException("Array identity cannot be stored in a scalar function local.");
        var stored = kind switch
        {
            FalloutScriptLocalKind.Number => value.Number,
            FalloutScriptLocalKind.Form => FalloutScriptValue.Form(value.Number),
            FalloutScriptLocalKind.String when value.Kind == FalloutScriptValueKind.String => value,
            FalloutScriptLocalKind.String => throw new InvalidDataException("Function string local needs text."),
            FalloutScriptLocalKind.Array => arrays.RequireReference(value),
            _ => throw new InvalidDataException("Function local kind is invalid."),
        };
        if (kind == FalloutScriptLocalKind.Array)
        {
            arrays.Retain(stored);
            arrays.Release(ReadValue(name));
        }
        _values[Definition.Slots[name]] = stored;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var value in _values.Values)
            if (value.Kind == FalloutScriptValueKind.Array) arrays.Release(value);
        if (_result.Kind == FalloutScriptValueKind.Array) arrays.Release(_result);
    }

    private static FalloutScriptValue Default(FalloutScriptLocalKind kind) => kind switch
    {
        FalloutScriptLocalKind.Form => FalloutScriptValue.Form(0),
        FalloutScriptLocalKind.String => FalloutScriptValue.String(string.Empty),
        FalloutScriptLocalKind.Array => FalloutScriptValue.Array(0),
        _ => 0,
    };
}
