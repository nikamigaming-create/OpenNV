using System.Globalization;

namespace OpenNV.Runtime.Content;

internal enum FalloutScriptLocalKind
{
    Number,
    Form,
    String,
    Array,
}

internal readonly record struct FalloutScriptLocalDeclaration(uint Index, FalloutScriptLocalKind Kind);

internal enum FalloutScriptValueKind
{
    Number,
    String,
    Form,
}

// Script expressions need to keep forms distinct from numbers. A form still
// has its numeric runtime identity for legacy comparisons and command
// arguments, but `$Form` must use the owned form name rather than formatting
// that identity as a decimal number.
internal readonly record struct FalloutScriptValue
{
    internal const int MaximumStringLength = 16_384;

    private readonly double _number;
    private readonly string? _text;

    internal FalloutScriptValueKind Kind { get; }
    internal double Number => Kind == FalloutScriptValueKind.String
        ? throw new InvalidDataException("A string cannot be used as a numeric script value.")
        : _number;
    internal string Text => Kind == FalloutScriptValueKind.String
        ? _text!
        : throw new InvalidDataException("A script value is not a string.");
    internal bool Truth => Kind == FalloutScriptValueKind.String ? _text!.Length != 0 : _number != 0;
    internal double Logical => Kind == FalloutScriptValueKind.String ? (Truth ? 1 : 0) : _number;

    private FalloutScriptValue(FalloutScriptValueKind kind, double number, string? text)
    {
        if (!double.IsFinite(number)) throw new InvalidDataException("Script value is non-finite.");
        if (text is not null && (text.Length > MaximumStringLength || text.Contains('\0')))
            throw new InvalidDataException("Script string exceeds its extent or contains a null.");
        Kind = kind;
        _number = number;
        _text = text;
    }

    public static implicit operator FalloutScriptValue(double value) =>
        new(FalloutScriptValueKind.Number, value, null);

    public static implicit operator FalloutScriptValue(string value) =>
        new(FalloutScriptValueKind.String, 0, value ?? throw new ArgumentNullException(nameof(value)));

    internal static FalloutScriptValue Form(double value)
    {
        if (value < 0 || value > uint.MaxValue || value != Math.Truncate(value))
            throw new InvalidDataException("Script form identity is invalid.");
        return new(FalloutScriptValueKind.Form, value, null);
    }

    internal static FalloutScriptValue String(string value) => value;

    internal string Stringize(Func<uint, string>? formName)
    {
        return Kind switch
        {
            FalloutScriptValueKind.String => Text,
            FalloutScriptValueKind.Number => _number.ToString("G", CultureInfo.InvariantCulture),
            FalloutScriptValueKind.Form => (formName ?? throw new NotSupportedException(
                "Form string conversion has no name owner."))((uint)_number),
            _ => throw new InvalidDataException("Script value kind is invalid."),
        };
    }
}

internal sealed record FalloutScriptValueContext(
    Func<string, FalloutScriptValue> Read,
    Action<string, FalloutScriptValue> Write,
    Func<uint, string>? FormName = null);

internal sealed record FalloutScriptStringSnapshot(uint Id, string Plugin, string Text);
internal sealed record FalloutScriptValueStoreSnapshot(
    uint LastStringId,
    IReadOnlyList<FalloutScriptStringSnapshot> Strings);

// Compiled string_var locals retain numeric handles for compatibility with
// the source format. Text ownership lives here, so a typed assignment can
// copy text without turning the local into an untyped numeric slot.
internal sealed class FalloutScriptValueStore
{
    private readonly Dictionary<uint, FalloutScriptStringSnapshot> _strings = [];
    private uint _lastStringId;

    internal FalloutScriptValue Read(FalloutScriptLocalKind kind, double raw)
    {
        return kind switch
        {
            FalloutScriptLocalKind.Number => raw,
            FalloutScriptLocalKind.Form => FalloutScriptValue.Form(raw),
            FalloutScriptLocalKind.String => ReadString(raw),
            FalloutScriptLocalKind.Array => throw new NotSupportedException(
                "Array local needs an array value owner."),
            _ => throw new InvalidDataException("Script local kind is invalid."),
        };
    }

    internal double Write(FalloutScriptLocalKind kind, double previous, FalloutScriptValue value,
        string ownerPlugin)
    {
        return kind switch
        {
            FalloutScriptLocalKind.Number => value.Number,
            FalloutScriptLocalKind.Form => FalloutScriptValue.Form(value.Number).Number,
            FalloutScriptLocalKind.String => WriteString(previous, value, ownerPlugin),
            FalloutScriptLocalKind.Array => throw new NotSupportedException(
                "Array local needs an array value owner."),
            _ => throw new InvalidDataException("Script local kind is invalid."),
        };
    }

    internal void DestroyString(double raw)
    {
        var id = Handle(raw);
        if (id != 0) _strings.Remove(id);
    }

    internal double DestroyString(FalloutScriptLocalKind kind, double raw)
    {
        if (kind != FalloutScriptLocalKind.String)
            throw new InvalidDataException("Script destruction target is not a string local.");
        DestroyString(raw);
        return 0;
    }

    internal FalloutScriptValueStoreSnapshot Capture() =>
        new(_lastStringId, _strings.Values.OrderBy(value => value.Id).ToArray());

    internal void Restore(FalloutScriptValueStoreSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            _strings.Clear();
            _lastStringId = 0;
            return;
        }
        if (snapshot.LastStringId == uint.MaxValue || snapshot.Strings is null)
            throw new InvalidDataException("Saved script strings are absent or exhausted.");
        var values = new Dictionary<uint, FalloutScriptStringSnapshot>();
        foreach (var value in snapshot.Strings)
        {
            if (value is null || value.Id == 0 || value.Id > snapshot.LastStringId ||
                string.IsNullOrWhiteSpace(value.Plugin) || value.Text is null ||
                !values.TryAdd(value.Id, value))
                throw new InvalidDataException("Saved script string identity is invalid or duplicated.");
            _ = FalloutScriptValue.String(value.Text);
        }
        _strings.Clear();
        foreach (var (id, value) in values) _strings.Add(id, value);
        _lastStringId = snapshot.LastStringId;
    }

    internal void ValidateHandle(double raw)
    {
        var id = Handle(raw);
        if (id != 0 && !_strings.ContainsKey(id))
            throw new InvalidDataException($"Script string handle {id} is not owned by the value store.");
    }

    private FalloutScriptValue ReadString(double raw)
    {
        var id = Handle(raw);
        if (id == 0) return FalloutScriptValue.String(string.Empty);
        return _strings.TryGetValue(id, out var value)
            ? FalloutScriptValue.String(value.Text)
            : throw new InvalidDataException($"Script string handle {id} is not owned by the value store.");
    }

    private double WriteString(double previous, FalloutScriptValue value, string ownerPlugin)
    {
        if (value.Kind != FalloutScriptValueKind.String)
        {
            var handle = Handle(value.Number);
            if (handle != 0 && !_strings.ContainsKey(handle))
                throw new InvalidDataException($"Script string handle {handle} is not owned by the value store.");
            return value.Number;
        }
        var previousId = Handle(previous);
        if (previousId != 0)
        {
            if (!_strings.TryGetValue(previousId, out var existing))
                throw new InvalidDataException($"Script string handle {previousId} is not owned by the value store.");
            _strings[previousId] = existing with { Text = value.Text };
            return previousId;
        }
        if (string.IsNullOrWhiteSpace(ownerPlugin))
            throw new InvalidDataException("Script string has no plugin owner.");
        var id = checked(_lastStringId + 1);
        _strings.Add(id, new(id, ownerPlugin, value.Text));
        _lastStringId = id;
        return id;
    }

    private static uint Handle(double value) =>
        value >= 0 && value <= uint.MaxValue && value == Math.Truncate(value)
            ? (uint)value
            : throw new InvalidDataException("Script string handle is invalid.");
}
