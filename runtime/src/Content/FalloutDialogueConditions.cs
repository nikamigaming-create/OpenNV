namespace OpenNV.Runtime.Content;

internal sealed class FalloutDialogueConditions(FalloutPluginStack records, FalloutQuestState quests,
    FalloutFormKey speaker, FalloutDialogueSpeaker identity, Func<FalloutCondition, float>? runtime = null,
    Func<FalloutCondition, FalloutFormKey?>? currentCell = null,
    Func<FalloutFormKey, float>? healthPercentage = null,
    Func<FalloutFormKey, int, float>? actorValue = null,
    Func<FalloutFormKey, bool>? talkedToPlayer = null,
    Func<FalloutFormKey, IReadOnlyDictionary<FalloutFormKey, sbyte>>? factions = null, Func<bool>? playerFemale = null,
    Func<FalloutFormKey, FalloutFormKey>? actorRace = null)
{
    internal FalloutDialogueConditions(FalloutPluginStack records, FalloutQuestState quests, FalloutFormKey speaker,
        FalloutNpcAppearance appearance, Func<FalloutCondition, float>? runtime = null)
        : this(records, quests, speaker, FalloutDialogueSpeaker.Read(records, appearance.Npc), runtime) { }

    private readonly IReadOnlyDictionary<FalloutFormKey, sbyte> _factions = FalloutAiPackages.ReadFactions(records, identity.Actor, identity.Templates);
    private FalloutFormKey? CurrentRace => identity.Race is null ? null : actorRace?.Invoke(speaker) ?? identity.Race;

    internal float Evaluate(FalloutCondition condition)
    {
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
                1 => records.RuntimeFormKey(0x14),
                2 => condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference)
                    ?? throw new InvalidDataException("Dialogue actor query has no explicit reference."),
                _ => throw new NotSupportedException($"Dialogue actor query run-on {condition.RunOn} is unbound."),
            };
            return condition.Function == 431 ? healthPercentage!(actor) : actorValue!(actor, checked((int)condition.Argument1));
        }
        // These functions query the explicit QUST argument. Selecting the
        // speaker, listener or another reference does not change that owner.
        if (condition.Function is 56 or 58 or 59 or 79 or 420 or 421 or 546) return quests.Evaluate(condition);
        if (condition.Function == 67 && (condition.RunOn is 0 or 1 or 2) && currentCell is not null)
        {
            var currentCellKey = currentCell(condition);
            return InInteriorCell(records, currentCellKey, condition.FormArgument1);
        }
        if (condition.RunOn == 0)
        {
            return condition.Function switch
            {
                50 when talkedToPlayer is not null => talkedToPlayer(speaker) ? 1 : 0,
                69 => CurrentRace == condition.FormArgument1 ? 1 : 0,
                70 when condition.Argument1 <= 1 => identity.Female == (condition.Argument1 == 1) ? 1 : 0,
                71 => (factions?.Invoke(speaker) ?? _factions).GetValueOrDefault(condition.FormArgument1, (sbyte)-1) >= 0 ? 1 : 0,
                73 => (factions?.Invoke(speaker) ?? _factions).GetValueOrDefault(condition.FormArgument1, (sbyte)-1),
                72 => identity.Actor == condition.FormArgument1 ? 1 : 0,
                365 => CurrentRace is { } race && FalloutRaceProperties.IsChild(records.GetEffective(race)) ? 1 : 0,
                427 => identity.VoiceType == condition.FormArgument1 ? 1 : 0,
                _ => Unbound(condition),
            };
        }
        return Unbound(condition);
    }

    private float Unbound(FalloutCondition condition) => runtime?.Invoke(condition) ??
        throw new NotSupportedException($"Dialogue actor {speaker} condition {condition.Owner.FormKey}/{condition.Function}/{condition.RunOn} is unbound.");

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
