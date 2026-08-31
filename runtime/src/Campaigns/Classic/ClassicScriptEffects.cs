using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed class ClassicScriptState : IEquatable<ClassicScriptState>
{
    private readonly SortedDictionary<int, int> _locals;
    private readonly HashSet<string> _flags;

    internal ClassicScriptState(
        IEnumerable<KeyValuePair<int, int>>? locals = null,
        IEnumerable<string>? flags = null)
    {
        _locals = [];
        foreach (var row in locals ?? [])
            _locals.Add(row.Key, row.Value);
        _flags = new HashSet<string>(flags ?? [], StringComparer.Ordinal);
    }

    internal int Local(int index) => _locals.GetValueOrDefault(index);
    internal bool Flag(string name) => _flags.Contains(name);
    internal IReadOnlyDictionary<int, int> Locals => _locals;
    internal IReadOnlyCollection<string> Flags => _flags;

    internal object Save() => new
    {
        locals = _locals.Select(row => new { index = row.Key, value = row.Value }).ToArray(),
        flags = _flags.Order(StringComparer.Ordinal).ToArray(),
    };

    internal static ClassicScriptState Restore(JsonElement source)
    {
        var locals = source.GetProperty("locals").EnumerateArray().Select(row =>
            new KeyValuePair<int, int>(
                row.GetProperty("index").GetInt32(),
                row.GetProperty("value").GetInt32())).ToArray();
        if (locals.Select(row => row.Key).Distinct().Count() != locals.Length ||
            locals.Any(row => row.Key < 0))
            throw new InvalidOperationException("Classic script locals are invalid.");
        var flags = source.GetProperty("flags").EnumerateArray()
            .Select(row => row.GetString() ?? "").ToArray();
        if (flags.Any(string.IsNullOrWhiteSpace) ||
            flags.Distinct(StringComparer.Ordinal).Count() != flags.Length)
            throw new InvalidOperationException("Classic script flags are invalid.");
        return new ClassicScriptState(locals, flags);
    }

    internal void SetLocal(int index, int value)
    {
        if (index < 0)
            throw new InvalidOperationException("Classic script local index is invalid.");
        _locals[index] = value;
    }

    internal void SetFlag(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Classic script flag is invalid.");
        _flags.Add(name);
    }

    public bool Equals(ClassicScriptState? other) => other is not null &&
        _locals.SequenceEqual(other._locals) && _flags.SetEquals(other._flags);

    public override bool Equals(object? obj) => Equals(obj as ClassicScriptState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var row in _locals)
        {
            hash.Add(row.Key);
            hash.Add(row.Value);
        }
        foreach (var flag in _flags.Order(StringComparer.Ordinal))
            hash.Add(flag, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

internal readonly record struct ClassicScriptContext(
    bool SourceIsPlayer,
    bool CanSeePlayer,
    int GameTime);

internal sealed class ClassicScriptProgram
{
    private const string Schema = "opennv-classic-script-effects/v1";
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Rule>> _events;

    private ClassicScriptProgram(IReadOnlyDictionary<string, IReadOnlyList<Rule>> events) =>
        _events = events;

    internal static ClassicScriptProgram Parse(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != Schema)
            throw new InvalidOperationException("Unexpected classic script-effect schema.");
        var events = new Dictionary<string, IReadOnlyList<Rule>>(StringComparer.Ordinal);
        foreach (var eventProperty in source.GetProperty("events").EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(eventProperty.Name) ||
                !events.TryAdd(eventProperty.Name,
                    eventProperty.Value.EnumerateArray().Select(ParseRule).ToArray()))
                throw new InvalidOperationException("Classic script event identity is invalid.");
        }
        if (events.Count == 0 || events.Any(row => row.Value.Count == 0))
            throw new InvalidOperationException("Classic script program has no executable rules.");
        return new ClassicScriptProgram(events);
    }

    internal bool Execute(string eventName, ClassicScriptState state, ClassicScriptContext context)
    {
        if (!_events.TryGetValue(eventName, out var rules))
            return false;
        var executed = false;
        foreach (var rule in rules.Where(rule => rule.Conditions.All(condition =>
                     Matches(condition, state, context))))
        {
            foreach (var effect in rule.Effects)
                Apply(effect, state, context);
            executed = true;
        }
        return executed;
    }

    private static Rule ParseRule(JsonElement source)
    {
        var conditions = source.GetProperty("all").EnumerateArray()
            .Select(ParseOperation).ToArray();
        var effects = source.GetProperty("then").EnumerateArray()
            .Select(ParseOperation).ToArray();
        if (conditions.Any(row => row.Name is not
                ("source-is-player" or "can-see-player" or "local-equals")) ||
            effects.Length == 0 || effects.Any(row => row.Name is not
                ("set-local" or "set-flag")))
            throw new InvalidOperationException(
                "Classic script rule mixes conditions and effects.");
        return new Rule(conditions, effects);
    }

    private static Operation ParseOperation(JsonElement source)
    {
        var operation = source.GetProperty("operation").GetString() ?? "";
        if (operation is not ("source-is-player" or "can-see-player" or "local-equals" or
            "set-local" or "set-flag"))
            throw new InvalidOperationException($"Unsupported classic script operation: {operation}");
        int? index = source.TryGetProperty("index", out var indexValue)
            ? indexValue.GetInt32()
            : null;
        int? value = source.TryGetProperty("value", out var valueElement)
            ? valueElement.GetInt32()
            : null;
        var valueFrom = source.TryGetProperty("valueFrom", out var fromElement)
            ? fromElement.GetString()
            : null;
        var flag = source.TryGetProperty("flag", out var flagElement)
            ? flagElement.GetString()
            : null;
        if (index is < 0 ||
            operation is "local-equals" && (index is null || value is null) ||
            operation is "set-local" && (index is null || (value is null) == (valueFrom is null)) ||
            valueFrom is not null && valueFrom != "game-time" ||
            operation is "set-flag" && string.IsNullOrWhiteSpace(flag))
            throw new InvalidOperationException($"Classic script operation is incomplete: {operation}");
        return new Operation(operation, index, value, valueFrom, flag);
    }

    private static bool Matches(
        Operation operation,
        ClassicScriptState state,
        ClassicScriptContext context) => operation.Name switch
        {
            "source-is-player" => context.SourceIsPlayer,
            "can-see-player" => context.CanSeePlayer,
            "local-equals" => state.Local(operation.Index!.Value) == operation.Value,
            _ => throw new InvalidOperationException(
                $"Classic script effect used as a condition: {operation.Name}"),
        };

    private static void Apply(
        Operation operation,
        ClassicScriptState state,
        ClassicScriptContext context)
    {
        switch (operation.Name)
        {
            case "set-local":
                state.SetLocal(operation.Index!.Value,
                    operation.ValueFrom == "game-time" ? context.GameTime : operation.Value!.Value);
                break;
            case "set-flag":
                state.SetFlag(operation.Flag!);
                break;
            default:
                throw new InvalidOperationException(
                    $"Classic script condition used as an effect: {operation.Name}");
        }
    }

    private sealed record Rule(IReadOnlyList<Operation> Conditions, IReadOnlyList<Operation> Effects);
    private sealed record Operation(
        string Name,
        int? Index,
        int? Value,
        string? ValueFrom,
        string? Flag);
}
