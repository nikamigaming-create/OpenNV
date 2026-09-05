using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutGlobalValueSnapshot(FalloutFormKey Form, string SourceSha256, float Value);
internal sealed record FalloutGlobalStateSnapshot(IReadOnlyList<FalloutGlobalValueSnapshot> Values);

/// <summary>Shared Float32 globals from the complete winning plugin graph.</summary>
internal sealed class FalloutGlobalState
{
    private readonly Dictionary<FalloutFormKey, FalloutGlobal> _sources;
    private readonly Dictionary<FalloutFormKey, float> _values;

    internal FalloutGlobalState(IEnumerable<FalloutGlobal> sources)
    {
        _sources = sources.ToDictionary(source => source.Form);
        _values = _sources.ToDictionary(pair => pair.Key, pair => pair.Value.InitialValue);
    }

    internal static FalloutGlobalState Read(FalloutPluginStack records) =>
        new(records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read));
    internal IReadOnlyCollection<FalloutGlobal> Sources => _sources.Values;
    internal float Get(FalloutFormKey form) => _values.TryGetValue(form, out var value) ? value :
        throw new InvalidDataException($"Global {form} has no winning runtime owner.");
    internal void Set(FalloutFormKey form, float value)
    {
        if (!_sources.ContainsKey(form) || !float.IsFinite(value))
            throw new InvalidDataException($"Global {form} cannot accept that value.");
        // FNAM controls script interpretation; the engine's storage and direct
        // setters retain Float32 even for integer-declared globals.
        _values[form] = value;
    }
    internal FalloutGlobalStateSnapshot Capture() => new(_sources.Values
        .OrderBy(source => source.Form.OwnerPlugin, StringComparer.OrdinalIgnoreCase).ThenBy(source => source.Form.ObjectId)
        .Select(source => new FalloutGlobalValueSnapshot(source.Form, source.SourceSha256, _values[source.Form])).ToArray());

    internal void Restore(FalloutGlobalStateSnapshot snapshot)
    {
        if (snapshot.Values.Count != _sources.Count || snapshot.Values.Select(value => value.Form).Distinct().Count() != _sources.Count ||
            snapshot.Values.Any(value => !float.IsFinite(value.Value) || !_sources.TryGetValue(value.Form, out var source) ||
                !string.Equals(value.SourceSha256, source.SourceSha256, StringComparison.Ordinal)))
            throw new InvalidDataException("Saved globals do not match the complete winning source graph.");
        foreach (var value in snapshot.Values) _values[value.Form] = value.Value;
    }
}
