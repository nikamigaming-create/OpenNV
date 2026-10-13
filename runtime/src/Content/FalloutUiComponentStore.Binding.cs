namespace OpenNV.Runtime.Content;

internal sealed record FalloutUiComponentBinding(Guid Factory, long Epoch, string Path);

internal sealed partial class FalloutUiComponentStore
{
    internal FalloutUiComponentBinding BindMenuRoot(string menuName)
    {
        if (!_menus.TryGetValue(menuName, out var menu) || IsDetached(menu.Root))
            throw new InvalidDataException("Current source UI menu root is absent: " + menuName);
        return new(Factory, Epoch, menu.Root.Path);
    }

    internal FalloutUiComponentBinding BindComponent(string path)
    {
        if (!TrySplitComponent(path, out var segments) || ResolveTile(segments, alt: false) is not { } tile || IsDetached(tile))
            throw new InvalidDataException("Current source UI component is absent: " + path);
        return new(Factory, Epoch, tile.Path);
    }

    internal void RequireComponent(FalloutUiComponentBinding binding)
    {
        if (binding.Factory != Factory || binding.Epoch != Epoch || !_tiles.TryGetValue(binding.Path, out var tile) || IsDetached(tile))
            throw new InvalidOperationException("Source UI binding outlived its actual component tree.");
    }

    internal float ReadFloat(FalloutUiComponentBinding binding, string trait)
    {
        RequireComponent(binding); return GetFloat(binding.Path + "/" + trait);
    }

    internal void StoreFloat(FalloutUiComponentBinding binding, string trait, float value)
    {
        RequireComponent(binding);
        if (string.IsNullOrWhiteSpace(trait) || trait.Contains('/'))
            throw new InvalidDataException("Source UI trait identity is invalid.");
        var key = Key(_tiles[binding.Path], trait);
        var animated = _animations.Remove(key);
        var changed = !_floatOverrides.TryGetValue(key, out var previous) ||
            BitConverter.SingleToUInt32Bits(previous) != BitConverter.SingleToUInt32Bits(value) || animated;
        // The source stores even an ordered-equal value, including a different
        // signed zero. A direct store cancels its previous gradual writer.
        _floatOverrides[key] = value;
        if (changed) ++Revision;
    }

    internal void StoreString(FalloutUiComponentBinding binding, string trait, string value)
    {
        RequireComponent(binding);
        if (!SetString(binding.Path + "/" + trait, value))
            throw new InvalidDataException("Source UI component rejected its actual string trait store.");
    }
}
