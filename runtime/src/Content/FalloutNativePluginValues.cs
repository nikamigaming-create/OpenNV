using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

// Joins the public plugin value ABI to the campaign's existing authoritative
// locals/array graph. No native Script/TESForm or retail heap is fabricated.
internal sealed class FalloutNativePluginValues : NativeNvseValueAuthority
{
    private readonly FalloutScriptValueStore _store;
    internal override object StoreIdentity => _store;
    internal FalloutNativePluginValues(FalloutScriptValueStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));
    private static readonly Encoding TextEncoding = CreateEncoding();
    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        var text = TextEncoding.GetString(bytes);
        if (!TextEncoding.GetBytes(text).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Native string is not reversible Windows-1252.");
        _ = FalloutScriptValue.String(text); return text;
    }
    private static byte[] Encode(string text)
    {
        var bytes = TextEncoding.GetBytes(text);
        if (!string.Equals(TextEncoding.GetString(bytes), text, StringComparison.Ordinal)) throw new InvalidDataException("Script string is not reversible Windows-1252.");
        return bytes;
    }
    internal override byte[]? GetString(uint id) => _store.NativeString(id) is { } text ? Encode(text) : null;
    internal override void SetString(uint id, ReadOnlySpan<byte> text) => _store.NativeSetString(id, Decode(text));
    internal override uint CreateString(ReadOnlySpan<byte> text, string ownerPlugin) => _store.NativeCreateString(Decode(text), ownerPlugin);
    internal override IDisposable BeginExecution() => _store.Arrays.BeginExecution();
    internal override bool ContainsArray(uint id) => _store.Arrays.NativeContains(id);
    internal override void RetainArray(uint id) => _store.Arrays.Retain(_store.Arrays.Reference(id));
    internal override void ReleaseArray(uint id) => _store.Arrays.Release(_store.Arrays.Reference(id));
    internal override int ArraySize(uint id) => ContainsArray(id) ? _store.Arrays.Size(_store.Arrays.Reference(id)) : 0;
    internal override int ArrayKind(uint id) => ContainsArray(id) ? (int)_store.Arrays.Kind(_store.Arrays.Reference(id)) : -1;

    private FalloutScriptValue Input(NativeNvseElementValue value) => value.Type switch
    {
        NativeNvseElementType.Number => value.Number,
        NativeNvseElementType.String => Decode(value.Text.AsSpan()),
        NativeNvseElementType.Array => _store.Arrays.Reference(value.Identity),
        NativeNvseElementType.Form when value.Identity == 0 => FalloutScriptValue.Form(0),
        NativeNvseElementType.Form => throw new NotSupportedException("Non-null native TESForm input has no authoritative projection."),
        _ => throw new NotSupportedException("Invalid native array element has no typed script storage."),
    };
    private static NativeNvseElementValue Output(FalloutScriptValue value) => value.Kind switch
    {
        FalloutScriptValueKind.Number => NativeNvseElementValue.Numeric(value.Number),
        FalloutScriptValueKind.String => NativeNvseElementValue.String(Encode(value.Text)),
        FalloutScriptValueKind.Array => NativeNvseElementValue.Reference(NativeNvseElementType.Array, checked((uint)value.Number)),
        FalloutScriptValueKind.Form => NativeNvseElementValue.Reference(NativeNvseElementType.Form, checked((uint)value.Number)),
        _ => throw new NotSupportedException("Transient pair cannot be published as a basic native array element."),
    };
    // xNVSE's packed API converts a numeric key to signed int, wraps negative
    // indices from the end, and appends for an out-of-range insertion. This is
    // an ABI adapter over the same dense store, not a second array implementation.
    private FalloutScriptValue? Key(uint id, NativeNvseElementValue key, bool insertion)
    {
        var kind = ArrayKind(id);
        if (kind == -1) return null;
        if (kind == 2) return key.Type == NativeNvseElementType.String ? Input(key) : (FalloutScriptValue?)null;
        if (key.Type != NativeNvseElementType.Number) return null;
        if (kind == 1) return key.Number;
        if (key.Number < int.MinValue || key.Number > int.MaxValue)
            throw new NotSupportedException("Packed native key exceeds the source signed-index conversion domain.");
        var index = (int)key.Number;
        if (index < 0) index += ArraySize(id);
        if (index < 0 || index >= ArraySize(id)) return insertion ? (FalloutScriptValue)ArraySize(id) : (FalloutScriptValue?)null;
        return index;
    }
    internal override uint CreateArray(int kind, IReadOnlyList<NativeNvseArrayEntry> entries)
    {
        if (kind is < 0 or > 2) throw new InvalidDataException("Native array container kind is invalid.");
        // All identities, bytes and key categories admit before allocation.
        var values = entries.Select(entry => (Key: Input(entry.Key), Value: Input(entry.Value))).ToArray();
        if (values.Any(pair => kind == 2 ? pair.Key.Kind != FalloutScriptValueKind.String : pair.Key.Kind != FalloutScriptValueKind.Number))
            throw new InvalidDataException("Native array constructor key domain is invalid.");
        var array = _store.Arrays.NativeConstruct((FalloutScriptArrayKind)kind);
        foreach (var pair in values) _store.Arrays.Set(array, pair.Key, pair.Value);
        return checked((uint)array.Number);
    }
    internal override bool ArrayHasKey(uint id, NativeNvseElementValue key) => Key(id, key, false) is { } actual && _store.Arrays.HasKey(_store.Arrays.Reference(id), actual);
    internal override NativeNvseElementValue? ArrayGet(uint id, NativeNvseElementValue key) =>
        Key(id, key, false) is { } actual && _store.Arrays.HasKey(_store.Arrays.Reference(id), actual)
            ? Output(_store.Arrays.Get(_store.Arrays.Reference(id), actual)) : null;
    internal override void ArraySet(uint id, NativeNvseElementValue key, NativeNvseElementValue value)
    {
        if (Key(id, key, true) is { } actual) _store.Arrays.Set(_store.Arrays.Reference(id), actual, Input(value));
    }
    internal override void ArrayAppend(uint id, NativeNvseElementValue value)
    {
        if (ArrayKind(id) == 0) _store.Arrays.Append(_store.Arrays.Reference(id), Input(value));
    }
    internal override IReadOnlyList<NativeNvseArrayEntry> ArrayEntries(uint id) => ContainsArray(id)
        ? _store.Arrays.NativeEntries(id).Select(pair => new NativeNvseArrayEntry(Output(pair.Key), Output(pair.Value))).ToArray() : [];

    internal NativeNvseValueResultTarget BindResult(FalloutScriptLocalKind kind, string ownerPlugin, string ownerLocal,
        Func<double> read, Action<double> write)
    {
        if (kind is not (FalloutScriptLocalKind.String or FalloutScriptLocalKind.Array) || string.IsNullOrWhiteSpace(ownerPlugin) ||
            string.IsNullOrWhiteSpace(ownerLocal)) throw new InvalidDataException("Native typed result lacks an actual source local declaration/owner.");
        ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(write);
        _store.ValidateLocal(kind, read(), ownerLocal);
        return new ResultTarget(this, kind, ownerPlugin, ownerLocal, read, write);
    }
    private sealed class ResultTarget(FalloutNativePluginValues owner, FalloutScriptLocalKind kind,
        string plugin, string local, Func<double> read, Action<double> write) : NativeNvseValueResultTarget
    {
        internal override NativeNvseCommandReturn Kind => kind == FalloutScriptLocalKind.String ? NativeNvseCommandReturn.String : NativeNvseCommandReturn.Array;
        internal override string SourceOwner => local;
        internal override object StoreIdentity => owner._store;
        internal override uint Publish(NativeNvseElementValue value)
        {
            if (kind == FalloutScriptLocalKind.String && value.Type != NativeNvseElementType.String ||
                kind == FalloutScriptLocalKind.Array && value.Type != NativeNvseElementType.Array)
                throw new InvalidDataException("Native result type differs from its declared source local.");
            var raw = owner._store.Write(kind, read(), owner.Input(value), plugin, local);
            write(raw); return checked((uint)raw);
        }
    }
}
