using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutDefaultStatusActorValue(int Index, string Name, bool HasChangeCallback = false);
internal sealed record FalloutDefaultStatusActorValues(string ExecutableSha256,
    IReadOnlyList<FalloutDefaultStatusActorValue> Values, string Contract)
{
    private const string Rules = "source-status-actor-values/v1;actual-named-descriptor-registration;" +
        "NPC-base-dispatch-zero-default-and-no-derived-formula;independent-mutable-base-override;" +
        "Player-three-Float32-modifier-pools-constructor-zero;" +
        "current-base-then-temporary-then-permanent-then-damage-Float32-additions;" +
        "source-constant-effects-in-separate-pools-not-serialized-twice;" +
        "independent-descriptor-change-callback-presence;unbound-command-callback-refuses-before-mutation";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(ExecutableSha256 + "\0" + Contract);
    internal static FalloutDefaultStatusActorValues Create(string executable, IReadOnlyList<FalloutDefaultStatusActorValue> values)
    {
        var ordered = values.OrderBy(value => value.Index).ToArray();
        var source = new FalloutDefaultStatusActorValues(executable, ordered,
            FalloutAdvancementRuntimeReceipt.Hash(Rules + "\0" + JsonSerializer.Serialize(ordered)));
        source.Validate(); return source;
    }
    internal void Validate()
    {
        if (!FalloutSourceMainFamily.IsFallout3(ExecutableSha256) || Values is not { Count: > 0 } ||
            Values.Any(value => value is null || value.Index is < 0 or >= 73 || value.Index is >= 5 and <= 11 ||
                string.IsNullOrWhiteSpace(value.Name) || !value.Name.All(char.IsAsciiLetterOrDigit)) ||
            Values.Select(value => value.Index).Distinct().Count() != Values.Count ||
            Values.Select(value => value.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Values.Count ||
            !Values.SequenceEqual(Values.OrderBy(value => value.Index)) ||
            Contract != FalloutAdvancementRuntimeReceipt.Hash(Rules + "\0" + JsonSerializer.Serialize(Values)))
            throw new InvalidDataException("Status actor values lost their selected executable/default/formula declaration.");
    }
}
