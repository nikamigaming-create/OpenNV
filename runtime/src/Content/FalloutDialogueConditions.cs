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
    Func<FalloutFormKey, FalloutFormKey, double>? itemCount = null)
{
    internal FalloutDialogueConditions(FalloutPluginStack records, FalloutQuestState quests, FalloutFormKey speaker,
        FalloutNpcAppearance appearance, Func<FalloutCondition, float>? runtime = null)
        : this(records, quests, speaker, FalloutDialogueSpeaker.Read(records, appearance.Npc), runtime) { }

    private IReadOnlyDictionary<FalloutFormKey, sbyte>? _factions;
    private FalloutFormKey Listener => listener ?? records.RuntimeFormKey(0x14);

    internal float Evaluate(FalloutCondition condition)
    {
        if (condition.Function == 47)
        {
            var subject = condition.RunOn switch
            {
                0 => speaker,
                1 => Listener,
                2 => condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference)
                    ?? throw new InvalidDataException("Dialogue item count has no explicit reference."),
                _ => throw new NotSupportedException("Dialogue item-count scope is unbound."),
            };
            return (float)(itemCount ?? throw new NotSupportedException("Dialogue has no shared inventory query owner."))
                (subject, condition.FormArgument1);
        }
        if (condition.Function == 40)
        {
            if (condition.RunOn > 2) throw new NotSupportedException("Dialogue vampire query scope is unbound.");
            return (vampireQuery ?? throw new NotSupportedException("Dialogue has no owned vampire query declaration."))();
        }
        if (condition.Function == 161)
            return FalloutAiPackages.IsCurrentPackage(condition, speaker, currentPackage ??
                throw new NotSupportedException("Dialogue current-package query has no active actor package owner."), Listener) ? 1 : 0;
        if (condition.Function == 72)
        {
            var subject = condition.RunOn switch
            {
                0 => speaker,
                1 => Listener,
                2 => condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference)
                    ?? throw new InvalidDataException("Dialogue identity query has no explicit reference."),
                _ => throw new NotSupportedException($"Dialogue identity query run-on {condition.RunOn} is unbound."),
            };
            return FalloutReferenceIdentity.Base(records, subject) == condition.FormArgument1 ? 1 : 0;
        }
        if (condition.Function == 131 && playerFemale is not null)
        {
            if (condition.Argument1 > 1) throw new InvalidDataException("GetPCIsSex has an invalid sex argument.");
            return playerFemale() == (condition.Argument1 == 1) ? 1 : 0;
        }
        if ((condition.Function == 431 && healthPercentage is not null) ||
            (condition.Function == 14 && actorValue is not null))
        {
            var actor = condition.RunOn switch
            {
                0 => speaker,
                1 => Listener,
                2 => condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference)
                    ?? throw new InvalidDataException("Dialogue actor query has no explicit reference."),
                _ => throw new NotSupportedException($"Dialogue actor query run-on {condition.RunOn} is unbound."),
            };
            return condition.Function == 431 ? healthPercentage!(actor) : actorValue!(actor, checked((int)condition.Argument1));
        }
        // These functions query the explicit QUST argument. Selecting the
        // speaker, listener or another reference does not change that owner.
        if (condition.Function is 56 or 58 or 59 or 79 or 420 or 421 or 546) return quests.Evaluate(condition);
        if (condition.RunOn == 1 && Listener != records.RuntimeFormKey(0x14))
            return EvaluateActor(condition, Listener, listenerIdentity ??
                throw new NotSupportedException("Dialogue listener has no authoritative actor identity."));
        if (condition.Function == 67 && (condition.RunOn is 0 or 1 or 2) && currentCell is not null)
        {
            var currentCellKey = currentCell(condition);
            return InInteriorCell(records, currentCellKey, condition.FormArgument1);
        }
        if (condition.RunOn == 0) return EvaluateActor(condition, speaker, identity);
        return Unbound(condition);
    }

    private float EvaluateActor(FalloutCondition condition, FalloutFormKey actor, FalloutDialogueSpeaker actorIdentity)
    {
        if (actorIdentity.RecordType == "TACT" && condition.Function is 69 or 70 or 71 or 73 or 365)
            throw new NotSupportedException($"Talking activator {actor} condition {condition.Function} requires its non-actor query contract.");
        FalloutFormKey? Race() => actorIdentity.Race is null ? null : actorRace?.Invoke(actor) ?? actorIdentity.Race;
        return condition.Function switch
        {
            50 when talkedToPlayer is not null => talkedToPlayer(actor) ? 1 : 0,
            69 => Race() == condition.FormArgument1 ? 1 : 0,
            70 when condition.Argument1 <= 1 => actorIdentity.Female == (condition.Argument1 == 1) ? 1 : 0,
            71 => ActorFactions(actor, actorIdentity).GetValueOrDefault(condition.FormArgument1, (sbyte)-1) >= 0 ? 1 : 0,
            73 => ActorFactions(actor, actorIdentity).GetValueOrDefault(condition.FormArgument1, (sbyte)-1),
            365 => Race() is { } selected && FalloutRaceProperties.IsChild(records.GetEffective(selected)) ? 1 : 0,
            427 => actorIdentity.VoiceType == condition.FormArgument1 ? 1 : 0,
            _ => Unbound(condition),
        };
    }

    private IReadOnlyDictionary<FalloutFormKey, sbyte> ActorFactions(FalloutFormKey actor, FalloutDialogueSpeaker actorIdentity) =>
        factions?.Invoke(actor) ?? (actor == speaker ? _factions ??= FalloutAiPackages.ReadFactions(records, identity.Actor, identity.Templates) :
            FalloutAiPackages.ReadFactions(records, actorIdentity.Actor, actorIdentity.Templates));

    private float Unbound(FalloutCondition condition)
    {
        // The older fallback's target context is the player. It must never
        // answer an unsupported NPC-listener query with the player's state.
        if (condition.RunOn == 1 && Listener != records.RuntimeFormKey(0x14))
            throw new NotSupportedException($"Dialogue listener {Listener} condition {condition.Owner.FormKey}/{condition.Function} is unbound.");
        return runtime?.Invoke(condition) ??
            throw new NotSupportedException($"Dialogue actor {speaker} condition {condition.Owner.FormKey}/{condition.Function}/{condition.RunOn} is unbound.");
    }

    internal static float InInteriorCell(FalloutPluginStack records, FalloutFormKey? current, FalloutFormKey requestedForm)
    {
        if (current is null) return 0;
        var requested = FalloutCellSceneReader.ReadDefinition(records, requestedForm);
        if ((requested.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0) return 0;
        if (requested.EditorId.Length == 0)
            throw new InvalidDataException($"GetInCell argument {requested.FormKey} has no CELL EDID.");
        var actorCell = FalloutCellSceneReader.ReadDefinition(records, current.Value);
        if ((actorCell.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0) return 0;
        if (actorCell.EditorId.Length == 0)
            throw new InvalidDataException($"GetInCell actor cell {actorCell.FormKey} has no CELL EDID.");
        // GECK GetInCell matches a valid interior cell name as a prefix.
        return actorCell.EditorId.StartsWith(requested.EditorId, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }
}
