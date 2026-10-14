namespace OpenNV.Runtime.Content;

internal sealed record FalloutUiFloatValue(string Trait, uint Bits);
internal sealed record FalloutUiStringValue(string Trait, string Value);
internal sealed record FalloutUiComponentValues(string Path, bool Alive, bool Animated,
    IReadOnlyList<FalloutUiFloatValue> Floats, IReadOnlyList<FalloutUiStringValue> Strings);

internal sealed partial class FalloutUiComponentStore
{
    // Cold reconstruction imports retained values into fresh source components.
    // It does not execute a script, gradual writer, control effect or UI callback.
    internal IReadOnlyList<FalloutUiComponentValues> CaptureComponentValues(IReadOnlyList<FalloutUiComponentBinding> components)
    {
        var values = components.ToDictionary(binding => binding.Path, binding =>
            (Alive: HasLivingComponent(binding),
                Floats: new List<FalloutUiFloatValue>(), Strings: new List<FalloutUiStringValue>()), StringComparer.OrdinalIgnoreCase);
        var animated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _floatOverrides)
        {
            var slash = pair.Key.LastIndexOf('/');
            if (slash > 0 && values.TryGetValue(pair.Key[..slash], out var value))
                value.Floats.Add(new(pair.Key[(slash + 1)..], BitConverter.SingleToUInt32Bits(pair.Value)));
        }
        foreach (var pair in _stringOverrides)
        {
            var slash = pair.Key.LastIndexOf('/');
            if (slash > 0 && values.TryGetValue(pair.Key[..slash], out var value))
                value.Strings.Add(new(pair.Key[(slash + 1)..], pair.Value));
        }
        foreach (var key in _animations.Keys)
        {
            var slash = key.LastIndexOf('/');
            if (slash > 0 && values.ContainsKey(key[..slash])) animated.Add(key[..slash]);
        }
        return components.Select(binding => new FalloutUiComponentValues(binding.Path, values[binding.Path].Alive,
            animated.Contains(binding.Path), values[binding.Path].Floats.ToArray(), values[binding.Path].Strings.ToArray())).ToArray();
    }

    internal void RestoreComponentValues(FalloutUiComponentBinding current, FalloutUiComponentValues saved)
    {
        RequireComponent(current); ValidateComponentValues(saved);
        if (!saved.Alive || saved.Animated || ComponentName(current.Path) != ComponentName(saved.Path))
            throw new NotSupportedException("source-UI-cold-component-lifetime-or-gradual-writer-unbound");
        var prefix = current.Path + "/";
        if (_floatOverrides.Keys.Concat(_stringOverrides.Keys).Concat(_animations.Keys).Any(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Fresh source UI component already contains mutable state before cold import.");
        foreach (var value in saved.Floats) _floatOverrides.Add(prefix + value.Trait, BitConverter.UInt32BitsToSingle(value.Bits));
        foreach (var value in saved.Strings) _stringOverrides.Add(prefix + value.Trait, value.Value);
        ++Revision;
    }

    internal static void ValidateComponentValues(FalloutUiComponentValues saved)
    {
        if (saved is null || string.IsNullOrWhiteSpace(saved.Path) || saved.Floats is null || saved.Strings is null ||
            saved.Floats.Select(value => value.Trait).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Floats.Count ||
            saved.Strings.Select(value => value.Trait).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Strings.Count ||
            saved.Floats.Any(value => !ValidTrait(value.Trait)) || saved.Strings.Any(value => !ValidTrait(value.Trait) || value.Value is null))
            throw new InvalidDataException("Source UI cold values lost their exact component/trait identity.");
        static bool ValidTrait(string trait) => !string.IsNullOrWhiteSpace(trait) && !trait.Contains('/');
    }
    private static string ComponentName(string path)
    {
        var leaf = path[(path.LastIndexOf('/') + 1)..]; var ordinal = leaf.LastIndexOf(':');
        return ordinal >= 0 ? leaf[..ordinal] : leaf;
    }
}
