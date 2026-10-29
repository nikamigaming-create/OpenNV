namespace OpenNV.Runtime.Content.Definitions;

// Numeric identity, canonical engine identifier and aliases have one source.
// Recognizing a name never allocates a value pool or supplies its initial value.
internal static class FalloutActorValueNames
{
    private static readonly IReadOnlyDictionary<FalloutActorValueId, string> Canonical =
        Enum.GetValues<FalloutActorValueId>().ToDictionary(id => id, id => id.ToString().ToLowerInvariant());
    private static readonly IReadOnlyDictionary<string, FalloutActorValueId> Identities = Build();

    internal static string Name(FalloutActorValueId id) => Canonical.TryGetValue(id, out var name)
        ? name
        : throw new NotSupportedException($"Actor value identity {(int)id} is unknown.");

    internal static FalloutActorValueId Parse(string name) => Identities.TryGetValue(name, out var id) ? id :
        throw new NotSupportedException($"Actor value {name} has no source identity.");

    internal static bool TryParse(string name, out FalloutActorValueId id) => Identities.TryGetValue(name, out id);
    internal static bool IsUser(FalloutActorValueId id) => id is >= FalloutActorValueId.Variable01 and <= FalloutActorValueId.Variable10;
    internal static bool IsLimb(FalloutActorValueId id) => id is >= FalloutActorValueId.PerceptionCondition and <= FalloutActorValueId.BrainCondition;

    private static IReadOnlyDictionary<string, FalloutActorValueId> Build()
    {
        var names = Canonical.ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.OrdinalIgnoreCase);
        names.Add("guns", FalloutActorValueId.SmallGuns);
        names.Add("radlevel", FalloutActorValueId.RadiationRads);
        return names;
    }
}
