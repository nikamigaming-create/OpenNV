namespace OpenNV.Runtime.Gameplay.State;

// Activity admission reads living owners. An absent owner differs from an
// owned condition which is currently holding the pending request.
internal enum FalloutAdvancementActivityState { Satisfied, Held, Unowned }
internal enum FalloutAdvancementActivityFact
{
    AdmittedPlayerUpdate, CharacterGenerationEnded, NoActiveMenu, PlayerAlive,
    PlayerAwake, PlayerUpright, CombatEnded, NotificationSequenceSettled,
    OriginalUiFrameGate, OriginalPlayerFrameGate,
    SelectedDependencyAdvancementEffects,
}

internal sealed record FalloutAdvancementActivityObservation(FalloutAdvancementActivityState State, string Owner)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(State) || string.IsNullOrWhiteSpace(Owner))
            throw new InvalidDataException("Player advancement activity observation has no living owner.");
    }
}

internal sealed class FalloutAdvancementActivity
{
    private readonly IReadOnlyList<FalloutAdvancementActivityFact> _required;
    private readonly Func<FalloutAdvancementActivityFact, FalloutAdvancementActivityObservation> _observe;
    internal FalloutAdvancementActivity(IReadOnlyList<FalloutAdvancementActivityFact> required,
        Func<FalloutAdvancementActivityFact, FalloutAdvancementActivityObservation> observe)
    {
        ArgumentNullException.ThrowIfNull(required); ArgumentNullException.ThrowIfNull(observe);
        if (required.Count == 0 || required.Any(fact => !Enum.IsDefined(fact)) || required.Distinct().Count() != required.Count)
            throw new InvalidDataException("Player advancement activity requirements are absent or ambiguous.");
        _required = Array.AsReadOnly(required.ToArray()); _observe = observe;
    }

    internal FalloutLevelUpAdmission Read()
    {
        var held = false;
        foreach (var fact in _required)
        {
            var observed = _observe(fact) ?? throw new InvalidDataException("Player advancement activity observation is absent.");
            observed.Validate();
            if (observed.State == FalloutAdvancementActivityState.Unowned)
                return new(false, $"Player advancement activity {fact} is unowned: {observed.Owner}.");
            held |= observed.State == FalloutAdvancementActivityState.Held;
        }
        return new(!held);
    }
}
