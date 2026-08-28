using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed record OpeningCampaignState(
    string Schema,
    string QuestFormId,
    string QuestEditorId,
    int Stage,
    bool Completed,
    string PlayerName,
    int SexIndex,
    int DocReaction,
    IReadOnlyDictionary<string, int> SpecialValues,
    IReadOnlyList<string> TagSkillFormIds,
    IReadOnlyList<string> TraitFormIds,
    IReadOnlyDictionary<string, int> PsychologyScores,
    IReadOnlyDictionary<string, float> QuestVariables,
    IReadOnlyList<OpeningQuestState> Quests,
    IReadOnlyList<OpeningGlobalState> Globals,
    IReadOnlyList<OpeningObjectiveState> Objectives,
    bool AutoDisplayObjectives,
    IReadOnlyList<int> Achievements,
    IReadOnlyList<OpeningInventoryState> Inventory,
    IReadOnlyList<string> EquippedItemFormIds,
    IReadOnlyList<string> DestroyedReferenceFormIds,
    IReadOnlyDictionary<string, bool> ReferenceEnabledStates,
    IReadOnlyList<int> PlayerControls,
    OpeningTransformState PlayerTransform,
    OpeningTransformState GuideTransform)
{
    internal const string ExpectedSchema = "opennv-opening-campaign-state/v1";

    internal static OpeningCampaignState Parse(JsonElement source)
    {
        var result = new OpeningCampaignState(
            source.GetProperty(nameof(Schema)).GetString()!,
            source.GetProperty(nameof(QuestFormId)).GetString()!,
            source.GetProperty(nameof(QuestEditorId)).GetString()!,
            source.GetProperty(nameof(Stage)).GetInt32(),
            source.GetProperty(nameof(Completed)).GetBoolean(),
            source.GetProperty(nameof(PlayerName)).GetString()!,
            source.GetProperty(nameof(SexIndex)).GetInt32(),
            source.GetProperty(nameof(DocReaction)).GetInt32(),
            ReadIntDictionary(source.GetProperty(nameof(SpecialValues))),
            ReadStrings(source.GetProperty(nameof(TagSkillFormIds))),
            ReadStrings(source.GetProperty(nameof(TraitFormIds))),
            ReadIntDictionary(source.GetProperty(nameof(PsychologyScores))),
            ReadFloatDictionary(source.GetProperty(nameof(QuestVariables))),
            source.GetProperty(nameof(Quests)).EnumerateArray()
                .Select(OpeningQuestState.Parse)
                .ToArray(),
            source.GetProperty(nameof(Globals)).EnumerateArray()
                .Select(OpeningGlobalState.Parse)
                .ToArray(),
            source.GetProperty(nameof(Objectives)).EnumerateArray()
                .Select(OpeningObjectiveState.Parse)
                .ToArray(),
            source.GetProperty(nameof(AutoDisplayObjectives)).GetBoolean(),
            source.GetProperty(nameof(Achievements)).EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray(),
            source.GetProperty(nameof(Inventory)).EnumerateArray()
                .Select(OpeningInventoryState.Parse)
                .ToArray(),
            ReadStrings(source.GetProperty(nameof(EquippedItemFormIds))),
            ReadStrings(source.GetProperty(nameof(DestroyedReferenceFormIds))),
            ReadBoolDictionary(source.GetProperty(nameof(ReferenceEnabledStates))),
            source.GetProperty(nameof(PlayerControls)).EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray(),
            OpeningTransformState.Parse(source.GetProperty(nameof(PlayerTransform))),
            OpeningTransformState.Parse(source.GetProperty(nameof(GuideTransform))));
        result.Validate();
        return result;
    }

    internal void Validate()
    {
        if (Schema != ExpectedSchema ||
            FalloutFormId.Normalize(QuestFormId) != QuestFormId ||
            string.IsNullOrWhiteSpace(QuestEditorId) ||
            Stage < 0 ||
            SexIndex < 0 ||
            SpecialValues.Any(value =>
                FalloutFormId.Normalize(value.Key) != value.Key || value.Value < 0) ||
            !UniqueFormIds(TagSkillFormIds) ||
            !UniqueFormIds(TraitFormIds) ||
            !UniqueFormIds(EquippedItemFormIds) ||
            !UniqueFormIds(DestroyedReferenceFormIds) ||
            ReferenceEnabledStates.Keys.Any(value => FalloutFormId.Normalize(value) != value) ||
            PlayerControls.Any(value => value is not 0 and not 1) ||
            Quests.Select(value => value.FormId).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                Quests.Count ||
            Globals.Select(value => value.FormId).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                Globals.Count ||
            Inventory.Select(value => value.FormId).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                Inventory.Count ||
            Objectives.Select(value => (value.QuestFormId, value.Index)).Distinct().Count() !=
                Objectives.Count ||
            !PsychologyScores.Values.All(value => value >= 0) ||
            !QuestVariables.Values.All(float.IsFinite) ||
            Achievements.Distinct().Count() != Achievements.Count)
            throw new InvalidOperationException("Saved opening campaign state is invalid.");
        foreach (var quest in Quests)
            quest.Validate();
        foreach (var global in Globals)
            global.Validate();
        foreach (var objective in Objectives)
            objective.Validate();
        foreach (var item in Inventory)
            item.Validate();
        PlayerTransform.Validate();
        GuideTransform.Validate();
    }

    private static bool UniqueFormIds(IReadOnlyList<string> values) =>
        values.All(value => FalloutFormId.Normalize(value) == value) &&
        values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Count;

    private static IReadOnlyDictionary<string, int> ReadIntDictionary(JsonElement source) =>
        source.EnumerateObject().ToDictionary(
            value => value.Name,
            value => value.Value.GetInt32(),
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, float> ReadFloatDictionary(JsonElement source) =>
        source.EnumerateObject().ToDictionary(
            value => value.Name,
            value => value.Value.GetSingle(),
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, bool> ReadBoolDictionary(JsonElement source) =>
        source.EnumerateObject().ToDictionary(
            value => value.Name,
            value => value.Value.GetBoolean(),
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ReadStrings(JsonElement source) =>
        source.EnumerateArray().Select(value => value.GetString()!).ToArray();
}

internal sealed record OpeningQuestState(
    string FormId,
    string EditorId,
    int Stage,
    bool Running,
    bool Stopped)
{
    internal static OpeningQuestState Parse(JsonElement source) => new(
        source.GetProperty(nameof(FormId)).GetString()!,
        source.GetProperty(nameof(EditorId)).GetString()!,
        source.GetProperty(nameof(Stage)).GetInt32(),
        source.GetProperty(nameof(Running)).GetBoolean(),
        source.GetProperty(nameof(Stopped)).GetBoolean());

    internal void Validate()
    {
        if (FalloutFormId.Normalize(FormId) != FormId ||
            string.IsNullOrWhiteSpace(EditorId) || Stage < 0 || Running && Stopped)
            throw new InvalidOperationException("Saved opening quest state is invalid.");
    }
}

internal sealed record OpeningGlobalState(
    string FormId,
    string EditorId,
    float Value)
{
    internal static OpeningGlobalState Parse(JsonElement source) => new(
        source.GetProperty(nameof(FormId)).GetString()!,
        source.GetProperty(nameof(EditorId)).GetString()!,
        source.GetProperty(nameof(Value)).GetSingle());

    internal void Validate()
    {
        if (FalloutFormId.Normalize(FormId) != FormId ||
            string.IsNullOrWhiteSpace(EditorId) || !float.IsFinite(Value))
            throw new InvalidOperationException("Saved opening global state is invalid.");
    }
}

internal sealed record OpeningObjectiveState(
    string QuestFormId,
    string QuestEditorId,
    int Index,
    string State,
    bool Enabled,
    string Text)
{
    internal static OpeningObjectiveState Parse(JsonElement source) => new(
        source.GetProperty(nameof(QuestFormId)).GetString()!,
        source.GetProperty(nameof(QuestEditorId)).GetString()!,
        source.GetProperty(nameof(Index)).GetInt32(),
        source.GetProperty(nameof(State)).GetString()!,
        source.GetProperty(nameof(Enabled)).GetBoolean(),
        source.GetProperty(nameof(Text)).GetString()!);

    internal void Validate()
    {
        if (FalloutFormId.Normalize(QuestFormId) != QuestFormId ||
            string.IsNullOrWhiteSpace(QuestEditorId) || Index < 0 ||
            State is not "displayed" and not "completed" || string.IsNullOrWhiteSpace(Text))
            throw new InvalidOperationException("Saved opening objective state is invalid.");
    }
}

internal sealed record OpeningInventoryState(
    string FormId,
    string EditorId,
    string RecordType,
    int Count)
{
    internal static OpeningInventoryState Parse(JsonElement source) => new(
        source.GetProperty(nameof(FormId)).GetString()!,
        source.GetProperty(nameof(EditorId)).GetString()!,
        source.GetProperty(nameof(RecordType)).GetString()!,
        source.GetProperty(nameof(Count)).GetInt32());

    internal void Validate()
    {
        if (FalloutFormId.Normalize(FormId) != FormId ||
            string.IsNullOrWhiteSpace(EditorId) || string.IsNullOrWhiteSpace(RecordType) ||
            Count <= 0)
            throw new InvalidOperationException("Saved opening inventory state is invalid.");
    }
}

internal sealed record OpeningTransformState(
    IReadOnlyList<float> Position,
    IReadOnlyList<float> Rotation)
{
    private const int VectorComponents = 3;
    private const int QuaternionComponents = 4;

    internal static OpeningTransformState Capture(Node3D node)
    {
        var transform = node.GlobalTransform;
        var rotation = transform.Basis.GetRotationQuaternion().Normalized();
        return new OpeningTransformState(
            [transform.Origin.X, transform.Origin.Y, transform.Origin.Z],
            [rotation.X, rotation.Y, rotation.Z, rotation.W]);
    }

    internal static OpeningTransformState Parse(JsonElement source) => new(
        source.GetProperty(nameof(Position)).EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray(),
        source.GetProperty(nameof(Rotation)).EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray());

    internal void Apply(Node3D node)
    {
        Validate();
        var position = new Vector3(Position[0], Position[1], Position[2]);
        var rotation = new Quaternion(Rotation[0], Rotation[1], Rotation[2], Rotation[3])
            .Normalized();
        var scale = node.GlobalTransform.Basis.Scale;
        node.GlobalTransform = new Transform3D(new Basis(rotation).Scaled(scale), position);
    }

    internal void Validate()
    {
        if (Position.Count != VectorComponents || Rotation.Count != QuaternionComponents ||
            Position.Any(value => !float.IsFinite(value)) ||
            Rotation.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Saved opening transform state is invalid.");
        var rotation = new Quaternion(Rotation[0], Rotation[1], Rotation[2], Rotation[3]);
        if (!rotation.IsNormalized())
            throw new InvalidOperationException("Saved opening transform rotation is not normalized.");
    }
}
