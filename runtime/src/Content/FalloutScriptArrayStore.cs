namespace OpenNV.Runtime.Content;

internal enum FalloutScriptArrayKind { Array, Map, StringMap }

internal sealed record FalloutScriptArrayValueSnapshot(FalloutScriptValueKind Kind,
    double? Number = null, string? Text = null, uint? Array = null)
{
    internal static FalloutScriptArrayValueSnapshot Capture(FalloutScriptValue value) => value.Kind switch
    {
        FalloutScriptValueKind.String => new(value.Kind, Text: value.Text),
        FalloutScriptValueKind.Array => new(value.Kind, Array: (uint)value.Number),
        _ => new(value.Kind, Number: value.Number),
    };

    internal FalloutScriptValue Restore() => Kind switch
    {
        FalloutScriptValueKind.Number when Number is { } number && Text is null && Array is null => number,
        FalloutScriptValueKind.Form when Number is { } number && Text is null && Array is null => FalloutScriptValue.Form(number),
        FalloutScriptValueKind.String when Text is { } text && Number is null && Array is null => FalloutScriptValue.String(text),
        FalloutScriptValueKind.Array when Array is { } array && Number is null && Text is null => FalloutScriptValue.Array(array),
        _ => throw new InvalidDataException("Saved script array value has invalid typed storage."),
    };
}

internal sealed record FalloutScriptArrayElementSnapshot(
    FalloutScriptArrayValueSnapshot Key, FalloutScriptArrayValueSnapshot Value);
internal sealed record FalloutScriptArraySnapshot(uint Id, FalloutScriptArrayKind Kind,
    IReadOnlyList<FalloutScriptArrayElementSnapshot> Elements, FalloutScriptArrayNativeOwnership? NativeOwnership = null);

// Array identity belongs to the shared script store. Locals alias identities;
// elements retain nested identities. Execution scopes protect intermediate
// results until assignments finish, then reclaim unreachable graphs and cycles.
internal sealed partial class FalloutScriptArrayStore
{
    // Execution resource limits, independent of source binary formats.
    internal const int MaximumElements = 1_000_000;
    private const int MaximumArrays = 100_000;
    private sealed class ArrayData(FalloutScriptArrayKind kind)
    {
        internal FalloutScriptArrayKind Kind { get; } = kind;
        internal SortedDictionary<double, FalloutScriptValue> Numbers { get; } = [];
        internal SortedDictionary<string, FalloutScriptValue> Strings { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal int Count => Kind == FalloutScriptArrayKind.StringMap ? Strings.Count : Numbers.Count;
        internal IEnumerable<(FalloutScriptValue Key, FalloutScriptValue Value)> Entries =>
            Kind == FalloutScriptArrayKind.StringMap
                ? Strings.Select(pair => ((FalloutScriptValue)pair.Key, pair.Value))
                : Numbers.Select(pair => ((FalloutScriptValue)pair.Key, pair.Value));
    }

    private readonly Dictionary<uint, ArrayData> _arrays = [];
    private readonly Dictionary<string, uint> _roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, int> _transient = [];
    private int _executionDepth;
    private int _elementCount;
    private bool _collectNeeded;
    internal uint LastId { get; private set; }
    internal int Count => _arrays.Count;

    internal FalloutScriptValue Reference(double raw)
    {
        var value = FalloutScriptValue.Array(raw);
        if (raw != 0 && !_arrays.ContainsKey((uint)raw))
            throw new InvalidDataException($"Script array {raw} has no shared value owner.");
        return value;
    }

    internal FalloutScriptValue RequireReference(FalloutScriptValue value)
    {
        if (value.Kind != FalloutScriptValueKind.Array && !(value.Kind == FalloutScriptValueKind.Number && value.Number == 0))
            throw new InvalidDataException("Script array argument is not an array value.");
        return Reference(value.Number);
    }

    internal FalloutScriptValue SetRoot(string owner, FalloutScriptValue value, string? ownerPlugin = null)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new InvalidDataException("Script array local owner is absent.");
        var array = RequireReference(value);
        if (_roots.GetValueOrDefault(owner) != (uint)array.Number) _collectNeeded = true;
        if (array.Number == 0) _roots.Remove(owner);
        else _roots[owner] = (uint)array.Number;
        NativeRootChanged(owner, checked((uint)array.Number), ownerPlugin);
        return array;
    }

    internal void Retain(FalloutScriptValue value)
    {
        var id = (uint)RequireReference(value).Number;
        if (id != 0) _transient[id] = checked(_transient.GetValueOrDefault(id) + 1);
    }

    internal void Release(FalloutScriptValue value)
    {
        var id = (uint)RequireReference(value).Number;
        if (id == 0) return;
        _collectNeeded = true;
        if (!_transient.TryGetValue(id, out var count)) throw new InvalidOperationException("Script array frame reference is unbalanced.");
        if (count == 1) _transient.Remove(id);
        else _transient[id] = count - 1;
    }

    internal IDisposable BeginExecution()
    {
        ++_executionDepth;
        return new ExecutionScope(this);
    }

    private sealed class ExecutionScope(FalloutScriptArrayStore store) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (--store._executionDepth == 0 && store._collectNeeded)
            {
                var retained = store.Reachable(store._roots.Values.Concat(store._transient.Keys));
                foreach (var id in store._arrays.Keys.Where(id => !retained.Contains(id)).ToArray())
                {
                    store._elementCount -= store._arrays[id].Count;
                    store._arrays.Remove(id);
                }
                store.NativePruneReferences();
                store._collectNeeded = false;
            }
        }
    }

    internal FalloutScriptValue Construct(string kind) => Construct(kind.ToLowerInvariant() switch
    {
        "array" => FalloutScriptArrayKind.Array,
        "map" => FalloutScriptArrayKind.Map,
        "stringmap" => FalloutScriptArrayKind.StringMap,
        _ => throw new InvalidDataException("Script array construction type is unknown."),
    });

    private FalloutScriptValue Construct(FalloutScriptArrayKind kind)
    {
        if (_arrays.Count >= MaximumArrays) throw new NotSupportedException("Script array count exceeds the runtime allocation budget.");
        var id = checked(LastId + 1);
        if (id == uint.MaxValue) throw new NotSupportedException("Script array identities are exhausted.");
        _arrays.Add(id, new(kind));
        _collectNeeded = true;
        LastId = id;
        return FalloutScriptValue.Array(id);
    }

    private ArrayData Data(FalloutScriptValue array)
    {
        var id = (uint)RequireReference(array).Number;
        return id != 0 ? _arrays[id] : throw new InvalidDataException("Null script array has no elements.");
    }

    internal int Size(FalloutScriptValue array) => RequireReference(array).Number == 0 ? -1 : Data(array).Count;
    internal FalloutScriptArrayKind Kind(FalloutScriptValue array) => Data(array).Kind;

    internal bool SurfaceEquals(FalloutScriptValue first, FalloutScriptValue second)
    {
        var firstId = RequireReference(first).Number;
        var secondId = RequireReference(second).Number;
        if (firstId == secondId) return true;
        if (firstId == 0 || secondId == 0) return false;
        var left = Data(first);
        var right = Data(second);
        if (left.Count != right.Count) return false;
        return left.Entries.Zip(right.Entries).All(pair =>
            EqualElement(pair.First.Key, pair.Second.Key) && EqualElement(pair.First.Value, pair.Second.Value));

        // xNVSE 6.2.1+ compares surface entries, retaining the identities of
        // nested arrays. It does not recursively compare their contents.
        static bool EqualElement(FalloutScriptValue a, FalloutScriptValue b) => a.Kind == b.Kind &&
            (a.Kind == FalloutScriptValueKind.String ? StringComparer.OrdinalIgnoreCase.Equals(a.Text, b.Text) :
                a.Number == b.Number);
    }

    private static void ValidateKey(ArrayData data, FalloutScriptValue key)
    {
        if (data.Kind == FalloutScriptArrayKind.StringMap)
        {
            if (key.Kind != FalloutScriptValueKind.String) throw new InvalidDataException("StringMap requires a string key.");
        }
        else if (key.Kind != FalloutScriptValueKind.Number || data.Kind == FalloutScriptArrayKind.Array &&
            (key.Number < 0 || key.Number > int.MaxValue || key.Number != Math.Truncate(key.Number)))
            throw new InvalidDataException("Script array requires a valid numeric key.");
    }

    internal bool HasKey(FalloutScriptValue array, FalloutScriptValue key)
    {
        if (RequireReference(array).Number == 0) return false;
        var data = Data(array);
        ValidateKey(data, key);
        return data.Kind == FalloutScriptArrayKind.StringMap ? data.Strings.ContainsKey(key.Text) : data.Numbers.ContainsKey(key.Number);
    }

    internal FalloutScriptValue Get(FalloutScriptValue array, FalloutScriptValue key)
    {
        var data = Data(array);
        ValidateKey(data, key);
        if (data.Kind == FalloutScriptArrayKind.StringMap && data.Strings.TryGetValue(key.Text, out var textValue)) return textValue;
        if (data.Kind != FalloutScriptArrayKind.StringMap && data.Numbers.TryGetValue(key.Number, out var numberValue)) return numberValue;
        throw new InvalidDataException("Script array key is absent.");
    }

    internal void Set(FalloutScriptValue array, FalloutScriptValue key, FalloutScriptValue value)
    {
        var data = Data(array);
        ValidateKey(data, key);
        if (value.Kind == FalloutScriptValueKind.Pair)
            throw new InvalidDataException("Transient pairs cannot be stored as array elements.");
        if (value.Kind == FalloutScriptValueKind.Array) _ = RequireReference(value);
        if (data.Kind == FalloutScriptArrayKind.Array && key.Number > data.Count)
            throw new InvalidDataException("Packed script array assignment would create a gap.");
        var newKey = !HasKey(array, key);
        if (newKey && _elementCount >= MaximumElements)
            throw new NotSupportedException("Script array elements exceed the runtime allocation budget.");
        var previous = data.Kind == FalloutScriptArrayKind.StringMap ? data.Strings.GetValueOrDefault(key.Text) :
            data.Numbers.GetValueOrDefault(key.Number);
        if (previous.Kind == FalloutScriptValueKind.Array || value.Kind == FalloutScriptValueKind.Array) _collectNeeded = true;
        if (data.Kind == FalloutScriptArrayKind.StringMap) data.Strings[key.Text] = value;
        else data.Numbers[key.Number] = value;
        if (newKey) ++_elementCount;
        NativeElementChanged(checked((uint)array.Number), key, value);
    }

    internal FalloutScriptValue Map(IReadOnlyList<FalloutScriptPair> pairs)
    {
        if (pairs.Count == 0) return FalloutScriptValue.Array(0);
        var keyKind = pairs[0].Key.Kind;
        if (keyKind is not (FalloutScriptValueKind.String or FalloutScriptValueKind.Number))
            throw new InvalidDataException("Map construction requires a numeric or string first key.");
        var map = Construct(keyKind == FalloutScriptValueKind.String ? FalloutScriptArrayKind.StringMap : FalloutScriptArrayKind.Map);
        foreach (var pair in pairs)
        {
            // The producer's map contract selects the first key domain and
            // ignores other key domains. All argument expressions have already
            // executed in order; no skipped key can suppress an operand effect.
            if (pair.Key.Kind != keyKind) continue;
            Set(map, pair.Key, pair.Value);
        }
        return map;
    }

    internal void Append(FalloutScriptValue array, FalloutScriptValue value)
    {
        if (Kind(array) != FalloutScriptArrayKind.Array) throw new InvalidDataException("Ar_Append requires a packed array.");
        Set(array, Size(array), value);
    }

    internal int Erase(FalloutScriptValue array, FalloutScriptValue? key = null)
    {
        var data = Data(array);
        _collectNeeded = true;
        if (key is null)
        {
            var count = data.Count;
            _elementCount -= count;
            data.Numbers.Clear();
            data.Strings.Clear();
            NativePruneReferences();
            return count;
        }
        ValidateKey(data, key.Value);
        if (data.Kind == FalloutScriptArrayKind.StringMap)
        {
            if (!data.Strings.Remove(key.Value.Text)) return 0;
            --_elementCount;
            NativePruneReferences();
            return 1;
        }
        if (!data.Numbers.Remove(key.Value.Number)) return 0;
        --_elementCount;
        if (data.Kind == FalloutScriptArrayKind.Array)
        {
            var remaining = data.Numbers.Values.ToArray();
            data.Numbers.Clear();
            for (var index = 0; index < remaining.Length; ++index) data.Numbers.Add(index, remaining[index]);
            NativeErasePackedEntry(checked((uint)array.Number), checked((int)key.Value.Number));
        }
        NativePruneReferences();
        return 1;
    }

    internal void Resize(FalloutScriptValue array, double size, FalloutScriptValue padding)
    {
        var data = Data(array);
        if (data.Kind != FalloutScriptArrayKind.Array || size < 0 || size > int.MaxValue || size != Math.Truncate(size))
            throw new InvalidDataException("Ar_Resize requires a packed array and a nonnegative integer size.");
        if (padding.Kind == FalloutScriptValueKind.Array) _ = RequireReference(padding);
        if ((long)_elementCount + (long)size - data.Count > MaximumElements)
            throw new NotSupportedException("Script array resize exceeds the runtime allocation budget.");
        _collectNeeded = true;
        _elementCount += (int)size - data.Count;
        for (var index = data.Count - 1; index >= size; --index) data.Numbers.Remove(index);
        NativePruneReferences();
        for (var index = data.Count; index < size; ++index)
        {
            data.Numbers.Add(index, padding);
            NativeElementChanged(checked((uint)array.Number), index, padding);
        }
    }

    internal FalloutScriptValue Copy(FalloutScriptValue array, bool deep = false)
    {
        if (RequireReference(array).Number == 0) return FalloutScriptValue.Array(0);
        var copies = new Dictionary<uint, FalloutScriptValue>();
        var pending = new Queue<(FalloutScriptValue Source, FalloutScriptValue Copy)>();
        FalloutScriptValue Admit(FalloutScriptValue source)
        {
            var id = (uint)source.Number;
            if (id == 0) return source;
            if (copies.TryGetValue(id, out var existing)) return existing;
            var copy = Construct(Kind(source));
            copies.Add(id, copy);
            pending.Enqueue((source, copy));
            return copy;
        }
        var result = Admit(array);
        while (pending.TryDequeue(out var pair))
            foreach (var (key, value) in Data(pair.Source).Entries)
                Set(pair.Copy, key, deep && value.Kind == FalloutScriptValueKind.Array ? Admit(value) : value);
        return result;
    }

    private HashSet<uint> Reachable(IEnumerable<uint> roots)
    {
        var reachable = new HashSet<uint>();
        var pending = new Stack<uint>(roots);
        while (pending.TryPop(out var id))
        {
            if (id == 0 || !reachable.Add(id)) continue;
            if (!_arrays.TryGetValue(id, out var data)) throw new InvalidDataException("Script array graph contains a missing identity.");
            foreach (var (_, value) in data.Entries)
                if (value.Kind == FalloutScriptValueKind.Array) pending.Push((uint)value.Number);
        }
        return reachable;
    }

    internal IReadOnlyList<FalloutScriptArraySnapshot> Capture() => Reachable(_roots.Values).Order()
        .Select(id => new FalloutScriptArraySnapshot(id, _arrays[id].Kind, _arrays[id].Entries.Select(pair =>
            new FalloutScriptArrayElementSnapshot(FalloutScriptArrayValueSnapshot.Capture(pair.Key),
                FalloutScriptArrayValueSnapshot.Capture(pair.Value))).ToArray(), NativeCaptureOwnership(id))).ToArray();

    internal void ValidateRestoredRoots()
    {
        if (!Reachable(_roots.Values).SetEquals(_arrays.Keys))
            throw new InvalidDataException("Saved script arrays include a graph without a declared local owner.");
        NativeValidateRestoredReferences();
    }

    internal IEnumerable<uint> Forms => _arrays.Values.SelectMany(data => data.Entries)
        .Where(pair => pair.Value.Kind == FalloutScriptValueKind.Form).Select(pair => (uint)pair.Value.Number);

    internal void Restore(uint? lastId, IReadOnlyList<FalloutScriptArraySnapshot>? snapshots)
    {
        if (_executionDepth != 0 || _transient.Count != 0) throw new InvalidOperationException("Array restore needs an idle script owner.");
        if ((lastId is null) != (snapshots is null) || lastId == uint.MaxValue)
            throw new InvalidDataException("Saved script array extent is invalid.");
        if (snapshots?.Count > MaximumArrays) throw new NotSupportedException("Saved arrays exceed the runtime allocation budget.");
        var candidate = new FalloutScriptArrayStore { LastId = lastId ?? 0, _nativeArrayRecords = _nativeArrayRecords };
        foreach (var snapshot in snapshots ?? [])
        {
            if (snapshot is null || snapshot.Id == 0 || snapshot.Id > candidate.LastId || snapshot.Elements is null ||
                !Enum.IsDefined(snapshot.Kind) || !candidate._arrays.TryAdd(snapshot.Id, new(snapshot.Kind)))
                throw new InvalidDataException("Saved script array identity is invalid or duplicated.");
        }
        foreach (var snapshot in snapshots ?? [])
        {
            var array = candidate.Reference(snapshot.Id);
            foreach (var element in snapshot.Elements)
            {
                if (element?.Key is null || element.Value is null) throw new InvalidDataException("Saved script array element is absent.");
                var key = element.Key.Restore();
                if (candidate.HasKey(array, key)) throw new InvalidDataException("Saved script array key is duplicated.");
                candidate.Set(array, key, element.Value.Restore());
            }
        }
        candidate.NativeRestoreOwnership(snapshots ?? []);
        foreach (var creation in candidate._nativeArrayCreation.Values) candidate.RequireNativeCreation(creation);
        _arrays.Clear();
        foreach (var (id, data) in candidate._arrays) _arrays.Add(id, data);
        _nativeArrayCreation.Clear(); foreach (var pair in candidate._nativeArrayCreation) _nativeArrayCreation.Add(pair.Key, pair.Value);
        _nativeArrayReferences.Clear(); foreach (var pair in candidate._nativeArrayReferences) _nativeArrayReferences.Add(pair.Key, pair.Value);
        _nativeReferenceOrder = candidate._nativeReferenceOrder;
        _roots.Clear();
        _elementCount = candidate._elementCount;
        _collectNeeded = false;
        LastId = candidate.LastId;
    }
}
