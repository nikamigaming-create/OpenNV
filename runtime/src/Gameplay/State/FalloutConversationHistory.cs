using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutConversationHistory(float RemainingSeconds, IReadOnlyList<FalloutFormKey> Partners)
{
    internal void Validate()
    {
        if (!float.IsFinite(RemainingSeconds) || Partners is null || Partners.Any(partner =>
            string.IsNullOrWhiteSpace(partner.OwnerPlugin) || partner.ObjectId is 0 or > FalloutFormKey.ObjectIdMask))
            throw new InvalidDataException("Conversation partner history is invalid.");
    }

    internal FalloutConversationHistory Copy() => this with { Partners = Partners.ToArray() };

    internal static FalloutConversationHistory Create(FalloutNumericGameSettings settings)
    {
        var history = new FalloutConversationHistory(settings.Float("fAItalktosameNPCtimer"), []);
        history.Validate();
        return history;
    }

    // This clock belongs to candidate evaluation, not the ordinary frame tick.
    // Empty history refreshes its interval; populated history counts down and
    // expires as a group before the candidate's membership is checked.
    internal FalloutConversationHistory Evaluate(FalloutNumericGameSettings settings, double seconds)
    {
        Validate();
        var remaining = Partners.Count == 0 ? Create(settings).RemainingSeconds :
            FalloutConversationSuppression.Advance(RemainingSeconds, seconds);
        return new(remaining, remaining <= 0 ? [] : Partners.ToArray());
    }

    internal FalloutConversationHistory Remember(FalloutFormKey partner)
    {
        var result = new FalloutConversationHistory(RemainingSeconds, [partner, .. Partners]);
        result.Validate();
        return result;
    }
}
