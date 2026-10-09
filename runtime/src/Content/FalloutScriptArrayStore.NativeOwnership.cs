using System.Globalization;
using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptArrayNativeReference(ulong Order, string Key,
    string? Root, uint? Parent, FalloutScriptArrayValueSnapshot? ElementKey, string? Plugin);
internal sealed record FalloutScriptArrayNativeOwnership(int Version, NativeNvseArrayCreationOwner? Creation,
    IReadOnlyList<FalloutScriptArrayNativeReference> References);

// This is reference/creation metadata on the actual array store, not a second
// element map. Unknown creation or referring-mod producers remain unknown.
internal sealed partial class FalloutScriptArrayStore
{
    private sealed record NativeReference(uint Target, FalloutScriptArrayNativeReference Source);
    private readonly Dictionary<uint, NativeNvseArrayCreationOwner> _nativeArrayCreation = [];
    private readonly Dictionary<string, NativeReference> _nativeArrayReferences = new(StringComparer.OrdinalIgnoreCase);
    private FalloutPluginStack? _nativeArrayRecords;
    private ulong _nativeReferenceOrder;

    internal void BindNativeOwnershipSource(FalloutPluginStack records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (_nativeArrayRecords is not null && !ReferenceEquals(_nativeArrayRecords, records))
            throw new InvalidDataException("Array creation/reference metadata changed its exact selected source.");
        _nativeArrayRecords = records;
        foreach (var creation in _nativeArrayCreation.Values) RequireNativeCreation(creation);
    }
    internal FalloutScriptValue NativeConstructOwned(FalloutScriptArrayKind kind, NativeNvseArrayCreationOwner owner)
    {
        RequireNativeCreation(owner);
        var value = Construct(kind); _nativeArrayCreation.Add(checked((uint)value.Number), owner); return value;
    }
    private void RequireNativeCreation(NativeNvseArrayCreationOwner owner)
    {
        var records = _nativeArrayRecords ?? throw new NotSupportedException("Array creation has no current selected source namespace.");
        if (records.OwnedSource is null) throw new NotSupportedException("Array creator lacks a retained owned source stack.");
        if (owner.ScriptForm == 0 || string.IsNullOrWhiteSpace(owner.Plugin) || string.IsNullOrWhiteSpace(owner.SourceOwner) ||
            owner.SourceSha256.Length != 64 || !StringComparer.OrdinalIgnoreCase.Equals(owner.StackId, records.OwnedSource.StackId))
            throw new InvalidDataException("Array creation lacks its actual Script/source identity.");
        var key = records.RuntimeFormKey(owner.ScriptForm); var script = records.GetEffective(key);
        if (script.Signature != "SCPT" || script.IsDeleted || !StringComparer.OrdinalIgnoreCase.Equals(key.OwnerPlugin, owner.Plugin) ||
            !StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(script.ReadData())), owner.SourceSha256))
            throw new InvalidDataException("Array creator is not the unchanged winning source Script.");
    }
    private static string NativeRootKey(string root) => "root:" + root;
    private static string NativeElementKey(uint parent, FalloutScriptValue key) => "element:" + parent.ToString(CultureInfo.InvariantCulture) + ":" +
        (key.Kind == FalloutScriptValueKind.String ? "s:" + key.Text : "n:" + BitConverter.DoubleToUInt64Bits(key.Number == 0 ? 0 : key.Number).ToString("X16", CultureInfo.InvariantCulture));

    // Called only by the actual successful root assignment/reconstruction.
    private void NativeRootChanged(string root, uint id, string? plugin)
    {
        var key = NativeRootKey(root);
        if (plugin is null && _nativeArrayReferences.TryGetValue(key, out var same) && same.Target == id) return;
        _nativeArrayReferences.Remove(key);
        if (id != 0) _nativeArrayReferences.Add(key, new(id,
            new(checked(++_nativeReferenceOrder), key, root, null, null, plugin)));
    }
    // Called after the existing store has actually accepted/published an entry.
    private void NativeElementChanged(uint parent, FalloutScriptValue key, FalloutScriptValue value)
    {
        var identity = NativeElementKey(parent, key); _nativeArrayReferences.Remove(identity);
        if (value.Kind == FalloutScriptValueKind.Array && value.Number != 0)
            _nativeArrayReferences.Add(identity, new(checked((uint)value.Number),
                new(checked(++_nativeReferenceOrder), identity, null, parent,
                    FalloutScriptArrayValueSnapshot.Capture(key), _nativeArrayCreation.GetValueOrDefault(parent)?.Plugin)));
    }
    private bool NativeReferenceExists(NativeReference reference, bool rootsPending)
    {
        var source = reference.Source;
        if (!_arrays.ContainsKey(reference.Target)) return false;
        if (source.Root is { } root) return rootsPending || _roots.GetValueOrDefault(root) == reference.Target;
        if (source.Parent is not { } parent || source.ElementKey is null || !_arrays.ContainsKey(parent)) return false;
        var array = Reference(parent); var key = source.ElementKey.Restore();
        return HasKey(array, key) && Get(array, key) is { Kind: FalloutScriptValueKind.Array } value && value.Number == reference.Target;
    }
    private void NativePruneReferences()
    {
        foreach (var key in _nativeArrayReferences.Where(pair => !NativeReferenceExists(pair.Value, rootsPending: false)).Select(pair => pair.Key).ToArray())
            _nativeArrayReferences.Remove(key);
        foreach (var id in _nativeArrayCreation.Keys.Where(id => !_arrays.ContainsKey(id)).ToArray()) _nativeArrayCreation.Remove(id);
    }
    private void NativeErasePackedEntry(uint parent, int removed)
    {
        var affected = _nativeArrayReferences.Values.Where(reference => reference.Source.Parent == parent &&
            reference.Source.ElementKey?.Kind == FalloutScriptValueKind.Number).ToArray();
        foreach (var reference in affected) _nativeArrayReferences.Remove(reference.Source.Key);
        foreach (var reference in affected)
        {
            var key = reference.Source.ElementKey!.Restore(); if (key.Number == removed) continue;
            if (key.Number > removed) key = key.Number - 1;
            var identity = NativeElementKey(parent, key);
            _nativeArrayReferences.Add(identity, reference with
            {
                Source = reference.Source with
                { Key = identity, ElementKey = FalloutScriptArrayValueSnapshot.Capture(key) }
            });
        }
    }
    private byte NativeMod(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new NotSupportedException("Internal array reference has no actual referring Script/mod producer.");
        var records = _nativeArrayRecords ?? throw new NotSupportedException("Internal array reference has no selected runtime namespace.");
        var context = records.Plugins.SingleOrDefault(value => StringComparer.OrdinalIgnoreCase.Equals(value.Plugin.Name, name)) ??
            throw new InvalidDataException("Internal array reference's source mod is absent from the selected graph.");
        if (!context.Plugin.NativeSourceAvailable) throw new InvalidOperationException("Internal array reference's original reader retired.");
        return checked((byte)context.LoadOrderIndex);
    }
    internal (byte Mod, string SourceOwner, byte[] References) NativeObjectOwnership(uint id)
    {
        if (!_nativeArrayCreation.TryGetValue(id, out var creation))
            throw new NotSupportedException("Internal ArrayVar pointer lacks its actual source Script creation owner.");
        RequireNativeCreation(creation); NativePruneReferences();
        // Account for every actual graph edge; a lost metadata row cannot become
        // a smaller reference vector. Temporary execution pins are independent.
        foreach (var pair in _roots.Where(pair => pair.Value == id))
            if (!_nativeArrayReferences.ContainsKey(NativeRootKey(pair.Key)))
                throw new NotSupportedException("Internal array root lacks its source reference metadata.");
        foreach (var pair in _arrays)
            foreach (var entry in pair.Value.Entries.Where(entry => entry.Value.Kind == FalloutScriptValueKind.Array && entry.Value.Number == id))
                if (!_nativeArrayReferences.ContainsKey(NativeElementKey(pair.Key, entry.Key)))
                    throw new NotSupportedException("Internal nested array edge lacks its source reference metadata.");
        var references = _nativeArrayReferences.Values.Where(reference => reference.Target == id)
            .OrderBy(reference => reference.Source.Order).Select(reference => NativeMod(reference.Source.Plugin)).ToArray();
        return (NativeMod(creation.Plugin), creation.SourceOwner, references);
    }
    private FalloutScriptArrayNativeOwnership NativeCaptureOwnership(uint id) => new(1,
        _nativeArrayCreation.GetValueOrDefault(id), _nativeArrayReferences.Values.Where(reference => reference.Target == id)
            .OrderBy(reference => reference.Source.Order).Select(reference => reference.Source).ToArray());
    private void NativeRestoreOwnership(IReadOnlyList<FalloutScriptArraySnapshot> snapshots)
    {
        _nativeArrayCreation.Clear(); _nativeArrayReferences.Clear(); _nativeReferenceOrder = 0;
        var orders = new HashSet<ulong>();
        foreach (var snapshot in snapshots)
        {
            var native = snapshot.NativeOwnership;
            if (native is null) continue; // Explicitly unowned pointer publication.
            if (native.Version != 1 || native.References is null) throw new InvalidDataException("Saved internal array ownership is malformed.");
            if (native.Creation is { } creation) _nativeArrayCreation.Add(snapshot.Id, creation);
            foreach (var reference in native.References)
            {
                if (reference is null || reference.Order == 0 || !orders.Add(reference.Order) || string.IsNullOrWhiteSpace(reference.Key) ||
                    (reference.Root is null) == (reference.Parent is null) ||
                    reference.Root is not null && reference.ElementKey is not null || reference.Parent is not null && reference.ElementKey is null)
                    throw new InvalidDataException("Saved array reference identity/order is incomplete or duplicated.");
                var expected = reference.Root is { } root ? NativeRootKey(root) : NativeElementKey(reference.Parent!.Value, reference.ElementKey!.Restore());
                if (!StringComparer.OrdinalIgnoreCase.Equals(expected, reference.Key) || !_nativeArrayReferences.TryAdd(reference.Key, new(snapshot.Id, reference)) ||
                    !NativeReferenceExists(_nativeArrayReferences[reference.Key], rootsPending: true))
                    throw new InvalidDataException("Saved array reference does not join its real stored graph edge.");
                _nativeReferenceOrder = Math.Max(_nativeReferenceOrder, reference.Order);
            }
        }
    }
    private void NativeValidateRestoredReferences()
    {
        if (_nativeArrayReferences.Values.Any(reference => !NativeReferenceExists(reference, rootsPending: false)))
            throw new InvalidDataException("Saved array references do not match the actual reconstructed local roots.");
        if (_nativeArrayCreation.Count != 0 && _nativeArrayRecords is null)
            throw new NotSupportedException("Saved native array creation requires exact current source validation before cold admission.");
        foreach (var creation in _nativeArrayCreation.Values) RequireNativeCreation(creation);
        foreach (var id in _nativeArrayCreation.Keys.ToArray()) _ = NativeObjectOwnership(id);
    }
}
