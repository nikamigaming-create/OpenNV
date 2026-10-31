namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    private readonly FalloutFormKey? _creatureSoundSource;
    private readonly Dictionary<uint, FalloutCreatureAnimationSounds.Declaration> _creatureDeclarations = [];

    private string DispatchCreatureEnum(string text, uint type)
    {
        var creature = _creatureSoundSource ?? throw new InvalidOperationException("Creature enum has no source sound declaration owner.");
        if (!_creatureDeclarations.TryGetValue(type, out var declaration))
            _creatureDeclarations.Add(type, declaration = FalloutCreatureAnimationSounds.ReadSingleSound(_records, creature, text));
        var selected = declaration.Select(FalloutSharedUnsignedRandom.Process.NextBounded);
        if (selected.Kind != FalloutCreatureAnimationSounds.SelectionKind.Selected)
        {
            var disposition = selected.Kind == FalloutCreatureAnimationSounds.SelectionKind.DeclaredNull
                ? "source-creature-sound-declared-null" : "source-creature-sound-chance-miss";
            LastEvent = new { ordinal = ++_eventCount, textKey = text, disposition, creature = creature.ToString(), type, declaration.Chance, selected.Roll };
            ObserveEvent();
            return disposition;
        }
        var sound = selected.Sound ?? throw new InvalidDataException("Selected creature enum lost its actual SOUN identity.");
        // Native creature contacts emit at the actor's current world position,
        // without attaching to a guessed foot bone or following future motion.
        var dispositionPlayed = Dispatch(text, _actor, ownsLoopStop: true, sound, followEmitter: type > 3);
        if (type <= 3)
        {
            dispositionPlayed += ";creature-footstep-additional-mix-partial";
        }
        return dispositionPlayed + $";creatureType={type};creatureChance={declaration.Chance};roll={selected.Roll}";
    }
}
