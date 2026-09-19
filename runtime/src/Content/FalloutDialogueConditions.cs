namespace OpenNV.Runtime.Content;

internal sealed class FalloutDialogueConditions(FalloutPluginStack records, FalloutQuestState quests,
    FalloutFormKey speaker, FalloutDialogueSpeaker identity, Func<FalloutCondition, float>? runtime = null,
    Func<FalloutCondition, FalloutFormKey?>? currentCell = null)
{
    internal FalloutDialogueConditions(FalloutPluginStack records, FalloutQuestState quests, FalloutFormKey speaker,
        FalloutNpcAppearance appearance, Func<FalloutCondition, float>? runtime = null)
        : this(records, quests, speaker, FalloutDialogueSpeaker.Read(records, appearance.Npc), runtime) { }

    private readonly IReadOnlyDictionary<FalloutFormKey, sbyte> _factions = FalloutAiPackages.ReadFactions(records, identity.Actor);

    internal float Evaluate(FalloutCondition condition)
    {
        // These functions query the explicit QUST argument. Selecting the
        // speaker, listener or another reference does not change that owner.
        if (condition.Function is 56 or 58 or 59 or 79 or 420 or 421 or 546) return quests.Evaluate(condition);
        if (condition.Function == 67 && (condition.RunOn is 0 or 1 or 2) && currentCell is not null)
        {
            var currentCellKey = currentCell(condition);
            if (currentCellKey is null) return 0;
            var requested = FalloutCellSceneReader.ReadDefinition(records, condition.FormArgument1);
            if ((requested.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0) return 0;
            if (requested.EditorId.Length == 0)
                throw new InvalidDataException($"GetInCell argument {requested.FormKey} has no CELL EDID.");
            var actorCell = FalloutCellSceneReader.ReadDefinition(records, currentCellKey.Value);
            if ((actorCell.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0) return 0;
            if (actorCell.EditorId.Length == 0)
                throw new InvalidDataException($"GetInCell actor cell {actorCell.FormKey} has no CELL EDID.");
            // GECK GetInCell matches a valid interior cell name as a prefix;
            // multiple room cells can therefore share one authored prefix.
            return actorCell.EditorId.StartsWith(requested.EditorId, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        if (condition.RunOn == 0)
        {
            return condition.Function switch
            {
                69 => identity.Race == condition.FormArgument1 ? 1 : 0,
                70 when condition.Argument1 <= 1 => identity.Female == (condition.Argument1 == 1) ? 1 : 0,
                71 => _factions.TryGetValue(condition.FormArgument1, out var rank) && rank >= 0 ? 1 : 0,
                72 => identity.Actor == condition.FormArgument1 ? 1 : 0,
                365 => identity.Race is { } race && FalloutRaceProperties.IsChild(records.GetEffective(race)) ? 1 : 0,
                427 => identity.VoiceType == condition.FormArgument1 ? 1 : 0,
                _ => Unbound(condition),
            };
        }
        return Unbound(condition);
    }

    private float Unbound(FalloutCondition condition) => runtime?.Invoke(condition) ??
        throw new NotSupportedException($"Dialogue actor {speaker} condition {condition.Owner.FormKey}/{condition.Function}/{condition.RunOn} is unbound.");
}
