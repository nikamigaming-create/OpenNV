using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutDialogueConditions(FalloutPluginStack records, FalloutQuestState quests,
    FalloutFormKey speaker, FalloutDialogueSpeaker identity, Func<FalloutCondition, float>? runtime = null,
    Func<FalloutCondition, FalloutFormKey?>? currentCell = null,
    Func<FalloutFormKey, float>? healthPercentage = null,
    Func<FalloutFormKey, int, float>? actorValue = null,
    Func<FalloutFormKey, bool>? talkedToPlayer = null,
    Func<FalloutFormKey, IReadOnlyDictionary<FalloutFormKey, sbyte>>? factions = null, Func<bool>? playerFemale = null,
    Func<FalloutFormKey, FalloutFormKey>? actorRace = null,
    FalloutFormKey? listener = null, FalloutDialogueSpeaker? listenerIdentity = null,
    Func<FalloutFormKey, FalloutFormKey?>? currentPackage = null, Func<int>? vampireQuery = null,
    Func<FalloutFormKey, FalloutFormKey, double>? itemCount = null,
    Func<FalloutFormKey, FalloutFormKey, float>? referenceDistance = null,
    Func<FalloutFormKey, FalloutFormKey, bool>? referenceInZone = null, Func<FalloutFormKey, int>? sitting = null,
    Func<FalloutFormKey, int>? deadCount = null, Func<FalloutFormKey, bool>? isDead = null,
    Func<FalloutFormKey, int>? sleeping = null, FalloutReferenceWorld? world = null, Func<uint>? randomPercent = null)
{
    internal FalloutDialogueConditions(FalloutPluginStack records, FalloutQuestState quests, FalloutFormKey speaker,
        FalloutNpcAppearance appearance, Func<FalloutCondition, float>? runtime = null)
        : this(records, quests, speaker, FalloutDialogueSpeaker.Read(records, appearance.Npc), runtime) { }

    private FalloutFormKey Listener => listener ?? records.RuntimeFormKey(0x14);
    private readonly FalloutConditionRuntime _runtime = new(records, quests, world)
    {
        Identity = subject => subject == speaker ? identity : subject == listener ? listenerIdentity : null,
        Race = actorRace,
        PlayerFemale = playerFemale,
        Factions = factions,
        ActorValue = actorValue,
        HealthPercentage = healthPercentage,
        TalkedToPlayer = talkedToPlayer,
        ItemCount = itemCount,
        CurrentPackage = currentPackage,
        Sitting = sitting,
        Sleeping = sleeping,
        DeadCount = deadCount,
        IsDead = isDead,
        Vampire = vampireQuery,
        Distance = referenceDistance,
        InZone = referenceInZone,
        Cell = currentCell is null ? null : (_, condition) => currentCell(condition),
        RandomPercent = randomPercent,
        Global = runtime,
        Native = (subject, condition) => world is not null ? world.QueryNativeCondition(subject, condition) :
            subject == records.RuntimeFormKey(0x14) ? (runtime ?? throw new NotSupportedException("Dialogue has no player query owner."))(condition.Declaration) :
            throw new NotSupportedException($"Dialogue subject {subject} function {condition.Function} has no native owner."),
    };

    internal float Evaluate(FalloutCondition condition) =>
        _runtime.Evaluate(condition, new(speaker, Listener, world is null ? null : world.GetLinkedRef));
}
