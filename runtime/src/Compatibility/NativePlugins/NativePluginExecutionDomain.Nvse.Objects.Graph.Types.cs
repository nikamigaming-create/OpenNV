using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// A graph reserves every genuine native identity before any original object
// pointer is written. Staged objects never enter the callable object registry.
internal sealed record NativeNvseSourceGraphNode(string Key, NativeNvseSourceClass Class,
    uint FormId, uint Extent, Func<NativeNvseSourceGraphContext, NativeNvseSourceObjectAuthority> Bind);
internal sealed record NativeNvseSourceGraphMetadata(uint EditorOffset, byte[] Bytes);

internal abstract class NativeNvseGraphDataAuthority : NativeNvseDataAuthority
{
    // Relative string/list pointers are resolved against the actual allocation;
    // callers cannot supply an address from another native generation.
    internal abstract NativeNvseSourceGraphMetadata Metadata(uint basis);
}

internal sealed class NativeNvseSourceGraphContext
{
    private readonly Dictionary<string, NativeNvseSourceObject> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _initial = new(StringComparer.Ordinal);
    private bool _retired;
    internal IReadOnlyDictionary<string, NativeNvseSourceObject> Objects => _objects;

    internal NativeNvseSourceObject Object(string key)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        return _objects.TryGetValue(key, out var value) ? value :
            throw new InvalidDataException("Native source graph has no declared object: " + key);
    }
    internal uint Metadata(string key) => Object(key).Metadata.Address;
    internal ReadOnlyMemory<byte> AllocationBytes(string key, int offset, int length)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        var bytes = _initial.TryGetValue(key, out var value) ? value :
            throw new InvalidDataException("Native graph allocation receipt is absent.");
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException("Native allocation-owned field exceeds its actual image.");
        return bytes.AsMemory(offset, length);
    }
    internal void Add(string key, NativeNvseSourceObject value, byte[] initial)
    { _objects.Add(key, value); _initial.Add(key, initial); }
    internal void Retire() { _retired = true; _objects.Clear(); _initial.Clear(); }
}

internal sealed class NativeNvseSourceGraph
{
    internal ulong Generation { get; }
    internal ulong Module { get; }
    internal ImmutableDictionary<string, NativeNvseSourceObject> Objects { get; }
    internal NativeNvseSourceGraphContext Context { get; }
    internal Dictionary<ulong, byte[]> NativeImages { get; } = [];
    internal Dictionary<ulong, byte[]> NativeMetadata { get; } = [];
    internal bool Retired { get; set; }

    internal NativeNvseSourceGraph(ulong generation, ulong module, NativeNvseSourceGraphContext context)
    {
        Generation = generation; Module = module; Context = context;
        Objects = context.Objects.ToImmutableDictionary(StringComparer.Ordinal);
    }
}
