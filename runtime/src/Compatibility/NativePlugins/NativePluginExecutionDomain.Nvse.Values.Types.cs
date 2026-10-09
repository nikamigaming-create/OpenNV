using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// These are wire values, never TESForm, Script, native Element storage, or a
// parallel script-state map. The C# authority below owns every nonzero identity.
internal enum NativeNvseElementType : uint { Invalid, Number, Form, String, Array }
internal sealed record NativeNvseElementValue
{
    internal NativeNvseElementType Type { get; }
    internal double Number { get; }
    internal uint Identity { get; }
    internal ImmutableArray<byte> Text { get; }
    private NativeNvseElementValue(NativeNvseElementType type, double number, uint identity, ImmutableArray<byte> text)
    { Type = type; Number = number; Identity = identity; Text = text; }
    internal static NativeNvseElementValue Numeric(double number) => double.IsFinite(number)
        ? new(NativeNvseElementType.Number, number, 0, []) : throw new InvalidDataException("Native array number is non-finite.");
    internal static NativeNvseElementValue String(ReadOnlySpan<byte> text) => text.Length <= 16384 && !text.Contains((byte)0)
        ? new(NativeNvseElementType.String, 0, 0, ImmutableArray.CreateRange(text.ToArray()))
        : throw new InvalidDataException("Native array string has an invalid byte extent.");
    internal static NativeNvseElementValue Reference(NativeNvseElementType type, uint id) =>
        (type is NativeNvseElementType.Form or NativeNvseElementType.Array) && (type != NativeNvseElementType.Array || id != uint.MaxValue)
            ? new(type, 0, id, []) : throw new InvalidDataException("Native array identity category/extent is invalid.");
}
internal sealed record NativeNvseArrayEntry(NativeNvseElementValue Key, NativeNvseElementValue Value);

// The runtime supplies the existing script store. An authored ABI fixture may
// supply its own first-party state; it is not a game object or compatibility gate.
internal abstract class NativeNvseValueAuthority
{
    internal virtual object StoreIdentity => this;
    internal abstract byte[]? GetString(uint id);
    internal abstract void SetString(uint id, ReadOnlySpan<byte> text);
    internal abstract uint CreateString(ReadOnlySpan<byte> text, string ownerPlugin);
    internal virtual string ScriptOwner(uint nativeScript) => throw new NotSupportedException(
        $"Native Script projection has no authoritative source owner (pointer {nativeScript}).");
    internal abstract IDisposable BeginExecution();
    internal abstract bool ContainsArray(uint id);
    internal abstract uint CreateArray(int kind, IReadOnlyList<NativeNvseArrayEntry> entries);
    internal virtual uint CreateArrayForScript(int kind, IReadOnlyList<NativeNvseArrayEntry> entries, uint script) => CreateArray(kind, entries);
    internal virtual NativeNvseArrayObjectSnapshot? ArrayObject(uint id) => throw new NotSupportedException("Internal ArrayVar lacks its actual source creation/reference/object authority.");
    internal abstract void RetainArray(uint id);
    internal abstract void ReleaseArray(uint id);
    internal abstract int ArraySize(uint id);
    internal abstract int ArrayKind(uint id);
    internal abstract bool ArrayHasKey(uint id, NativeNvseElementValue key);
    internal abstract NativeNvseElementValue? ArrayGet(uint id, NativeNvseElementValue key);
    internal abstract void ArraySet(uint id, NativeNvseElementValue key, NativeNvseElementValue value);
    internal abstract void ArrayAppend(uint id, NativeNvseElementValue value);
    internal abstract IReadOnlyList<NativeNvseArrayEntry> ArrayEntries(uint id);
}

// A result target binds an actual C# assignment. Nothing infers a variable from
// an arbitrary writable native double, Script pointer, or result-address guess.
internal abstract class NativeNvseValueResultTarget
{
    internal virtual object? StoreIdentity => null;
    internal abstract NativeNvseCommandReturn Kind { get; }
    internal abstract string SourceOwner { get; }
    internal abstract uint Publish(NativeNvseElementValue value);
}
internal sealed record NativeNvseValueCallbackReceipt(ulong Generation, ulong Callback, ulong Parent,
    ulong Caller, uint Operation, uint Identity);
internal readonly record struct NativeNvseValueStatistics(uint Callbacks, uint CachedStrings,
    uint LivePages, uint RetiredPages, uint CommittedBytes, uint ReservedBytes,
    uint HeapCreated, uint HeapDestroyed, uint HeapLive);

internal sealed class NativeNvseValueCall(IDisposable execution) : IDisposable
{
    internal HashSet<uint> Arrays { get; } = [];
    private bool _disposed;
    internal void Retain(NativeNvseValueAuthority authority, uint id)
    {
        if (_disposed) throw new InvalidOperationException("Native value call is retired.");
        if (id != 0 && !Arrays.Contains(id)) { authority.RetainArray(id); Arrays.Add(id); }
    }
    internal void Retire(NativeNvseValueAuthority authority)
    {
        if (_disposed) return;
        _disposed = true;
        try { foreach (var id in Arrays) authority.ReleaseArray(id); }
        finally { Arrays.Clear(); execution.Dispose(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (Arrays.Count != 0) throw new InvalidOperationException("Native array references require their store retirement owner.");
        _disposed = true; execution.Dispose();
    }
}
