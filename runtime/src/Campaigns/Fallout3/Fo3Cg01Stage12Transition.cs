using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg01Stage12Trigger(
    string ScriptFormId,
    string ScriptEditorId,
    string ActivatorFormId,
    string ActivatorEditorId,
    string ReferenceFormId,
    string CellFormId,
    Fo3Cg01Transform SourceTransform,
    int CollisionLayers,
    Fo3Cg01Vector3 DimensionsGameUnits);

internal sealed record Fo3Cg01Stage12Boundary(bool Applied, string Blocker);

internal sealed record Fo3Cg01Stage12State(
    int SourceStage,
    string ActiveQuestFormId,
    string ActiveQuestEditorId,
    int ActiveStage,
    string TriggerReferenceFormId,
    bool ActionReferenceWasPlayer,
    int CompletedObjectiveIndex,
    IReadOnlyList<int> DisabledPlayerControls,
    int DadDoTalk,
    double DadTimerSeconds,
    int AccountedCommandCount,
    int AppliedCommandCount,
    IReadOnlyList<string> AppliedExecutionTrace,
    Fo3Cg01Stage12Boundary NextBoundary);

internal sealed record Fo3Cg01Stage12Transition(
    int SourceStage,
    int TargetStage,
    string QuestFormId,
    string QuestEditorId,
    int ObjectiveIndex,
    string ObjectiveText,
    Fo3Cg01Stage12Trigger Trigger,
    IReadOnlyList<int> DisabledPlayerControls,
    int DadDoTalk,
    double DadTimerSeconds,
    string NextBoundaryBlocker)
{
    internal const string ExpectedSchema =
        "opennv-fo3-cg01-stage-10-to-12-trigger-transition/v1";
    internal const string ExpectedSavedStateSchema =
        "opennv-fo3-cg01-stage-10-to-12-trigger-state/v1";

    private const string ExpectedStatus =
        "source-backed-player-trigger-and-stage-result-runtime-unapplied";
    private const string ExpectedBoundaryBlocker =
        "fo3-cg01-stage-12-dad-response-not-implemented";
    private const int ExpectedSourceStage = 10;
    private const int ExpectedTargetStage = 12;
    private const int ExpectedObjectiveIndex = 10;
    private const int ExpectedCommandCount = 4;
    private const int ExpectedCollisionPrimitiveType = 2;
    private const int ExpectedDadDoTalk = 1;
    private const double ExpectedDadTimerSeconds = 0.0;

    private static readonly int[] ExpectedDisabledPlayerControls = [1, 1, 1, 1, 0, 0, 1];

    internal static Fo3Cg01Stage12Transition Load(
        JsonElement source,
        Fo3Cg01Stage0Transition stage0,
        Fo3Cg01Stage10Transition stage10)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus ||
            RequiredInteger(source, "sourceStage") != ExpectedSourceStage ||
            RequiredInteger(source, "targetStage") != ExpectedTargetStage ||
            stage10.TargetStage != ExpectedSourceStage)
            throw new InvalidOperationException(
                "Fallout 3 CG01 walk-to-Dad transition identity differs.");

        var objective = RequiredObject(source, "objective");
        var questFormId = RequiredFormId(objective, "questFormId");
        var questEditorId = RequiredString(objective, "questEditorId");
        var objectiveIndex = RequiredInteger(objective, "index");
        var objectiveText = RequiredString(objective, "text");
        var expectedTextSha256 = RequiredSha256(objective, "textSha256");
        var actualTextSha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(objectiveText))).ToLowerInvariant();
        if (questFormId != stage0.QuestFormId ||
            questEditorId != stage0.QuestEditorId ||
            objectiveIndex != ExpectedObjectiveIndex ||
            stage10.ObjectiveIndex != objectiveIndex ||
            actualTextSha256 != expectedTextSha256)
            throw new InvalidOperationException(
                "Fallout 3 CG01 walk-to-Dad objective differs.");

        var triggerSource = RequiredObject(source, "trigger");
        if (RequiredString(triggerSource, "event") != "onTriggerEnter" ||
            RequiredString(triggerSource, "actionReference") != "player" ||
            RequiredFormId(triggerSource, "cellFormId") != stage0.CellFormId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 walk-to-Dad trigger event differs.");
        _ = RequiredSha256(triggerSource, "scriptRecordSha256");
        _ = RequiredSha256(triggerSource, "scriptSourceSha256");
        _ = RequiredSha256(triggerSource, "activatorRecordSha256");
        _ = RequiredSha256(triggerSource, "referenceRecordSha256");
        var sourceTransform = LoadTransform(RequiredObject(triggerSource, "sourceTransform"));
        var primitive = RequiredObject(triggerSource, "primitive");
        if (RequiredString(primitive, "shape") != "box" ||
            RequiredInteger(primitive, "type") != ExpectedCollisionPrimitiveType)
            throw new InvalidOperationException(
                "Fallout 3 CG01 walk-to-Dad trigger primitive differs.");
        var dimensions = ReadVector3(primitive, "dimensionsGameUnits");
        if (dimensions.X <= 0.0 || dimensions.Y <= 0.0 || dimensions.Z <= 0.0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 walk-to-Dad trigger dimensions differ.");
        var trigger = new Fo3Cg01Stage12Trigger(
            RequiredFormId(triggerSource, "scriptFormId"),
            RequiredString(triggerSource, "scriptEditorId"),
            RequiredFormId(triggerSource, "activatorFormId"),
            RequiredString(triggerSource, "activatorEditorId"),
            RequiredFormId(triggerSource, "referenceFormId"),
            RequiredFormId(triggerSource, "cellFormId"),
            sourceTransform,
            RequiredInteger(triggerSource, "collisionLayers"),
            dimensions);

        var stageResult = RequiredObject(source, "stageResult");
        _ = RequiredSha256(stageResult, "stageSourceSha256");
        if (RequiredInteger(stageResult, "accountedCommandCount") != ExpectedCommandCount)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 command count differs.");
        var commands = RequiredArray(stageResult, "commands").EnumerateArray().ToArray();
        if (commands.Length != ExpectedCommandCount)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 commands are incomplete.");
        RequireCommand(commands[0], 0, "setObjectiveCompleted");
        if (RequiredFormId(commands[0], "questFormId") != questFormId ||
            RequiredString(commands[0], "questEditorId") != questEditorId ||
            RequiredInteger(commands[0], "objectiveIndex") != objectiveIndex ||
            !RequiredBoolean(commands[0], "completed"))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 objective result differs.");
        RequireCommand(commands[1], 1, "disablePlayerControls");
        var disabledControls = RequiredIntegerArray(commands[1], "arguments");
        if (!disabledControls.SequenceEqual(ExpectedDisabledPlayerControls))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 control mask differs.");
        RequireDadVariable(commands[2], 2, stage0, "doTalk", "short", ExpectedDadDoTalk);
        RequireDadVariable(commands[3], 3, stage0, "timer", "float", ExpectedDadTimerSeconds);

        var boundary = RequiredObject(source, "nextBoundary");
        if (RequiredBoolean(boundary, "applied") ||
            RequiredString(boundary, "blocker") != ExpectedBoundaryBlocker)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 boundary differs.");
        return new Fo3Cg01Stage12Transition(
            ExpectedSourceStage,
            ExpectedTargetStage,
            questFormId,
            questEditorId,
            objectiveIndex,
            objectiveText,
            trigger,
            disabledControls,
            ExpectedDadDoTalk,
            ExpectedDadTimerSeconds,
            ExpectedBoundaryBlocker);
    }

    internal Fo3Cg01Stage12State ApplyAuthoredTrigger(
        Fo3Cg01Stage10State stage10,
        string triggerReferenceFormId,
        bool actionReferenceWasPlayer)
    {
        if (stage10.ActiveStage != SourceStage ||
            stage10.ActiveQuestFormId != QuestFormId ||
            stage10.ActiveQuestEditorId != QuestEditorId ||
            stage10.DisplayedObjectiveIndex != ObjectiveIndex ||
            stage10.NextBoundary.Applied ||
            triggerReferenceFormId != Trigger.ReferenceFormId ||
            !actionReferenceWasPlayer)
            throw new InvalidOperationException(
                "Fallout 3 CG01 walk-to-Dad trigger activation differs.");
        var trace = new List<string>
        {
            "trigger:onTriggerEnter:player",
        };
        var commands = new[]
        {
            (Kind: GamebryoStageCommandKind.Objective, Trace: "s12:0:setObjectiveCompleted"),
            (Kind: GamebryoStageCommandKind.PlayerControls, Trace: "s12:1:disablePlayerControls"),
            (Kind: GamebryoStageCommandKind.SetScriptVariable,
                Trace: "s12:2:setScriptVariable"),
            (Kind: GamebryoStageCommandKind.SetScriptVariable,
                Trace: "s12:3:setScriptVariable"),
        }.Select((command, sourceIndex) => new SourceGamebryoStageCommand<string>(
            sourceIndex,
            command.Kind,
            command.Trace)).ToArray();
        var applied = 0;
        GamebryoStageCommandExecutor.ExecuteAll(commands, command =>
        {
            trace.Add(command.Value);
            applied++;
            return applied == command.SourceIndex + 1;
        });
        return new Fo3Cg01Stage12State(
            SourceStage,
            QuestFormId,
            QuestEditorId,
            TargetStage,
            Trigger.ReferenceFormId,
            true,
            ObjectiveIndex,
            DisabledPlayerControls,
            DadDoTalk,
            DadTimerSeconds,
            commands.Length,
            applied,
            trace,
            new Fo3Cg01Stage12Boundary(false, NextBoundaryBlocker));
    }

    internal object SavedState(Fo3Cg01Stage12State state) => new
    {
        schema = ExpectedSavedStateSchema,
        sourceStage = state.SourceStage,
        activeQuest = new
        {
            formId = state.ActiveQuestFormId,
            editorId = state.ActiveQuestEditorId,
            stage = state.ActiveStage,
        },
        triggerReferenceFormId = state.TriggerReferenceFormId,
        actionReferenceWasPlayer = state.ActionReferenceWasPlayer,
        completedObjectiveIndex = state.CompletedObjectiveIndex,
        disabledPlayerControls = state.DisabledPlayerControls,
        dadDoTalk = state.DadDoTalk,
        dadTimerSeconds = state.DadTimerSeconds,
        accountedCommandCount = state.AccountedCommandCount,
        appliedCommandCount = state.AppliedCommandCount,
        appliedExecutionTrace = state.AppliedExecutionTrace,
        nextBoundary = new
        {
            applied = state.NextBoundary.Applied,
            blocker = state.NextBoundary.Blocker,
        },
    };

    internal void ValidateSavedState(JsonElement source, Fo3Cg01Stage12State expected)
    {
        var activeQuest = RequiredObject(source, "activeQuest");
        var boundary = RequiredObject(source, "nextBoundary");
        if (RequiredString(source, "schema") != ExpectedSavedStateSchema ||
            RequiredInteger(source, "sourceStage") != expected.SourceStage ||
            RequiredFormId(activeQuest, "formId") != expected.ActiveQuestFormId ||
            RequiredString(activeQuest, "editorId") != expected.ActiveQuestEditorId ||
            RequiredInteger(activeQuest, "stage") != expected.ActiveStage ||
            RequiredFormId(source, "triggerReferenceFormId") !=
                expected.TriggerReferenceFormId ||
            RequiredBoolean(source, "actionReferenceWasPlayer") !=
                expected.ActionReferenceWasPlayer ||
            RequiredInteger(source, "completedObjectiveIndex") !=
                expected.CompletedObjectiveIndex ||
            !RequiredIntegerArray(source, "disabledPlayerControls").SequenceEqual(
                expected.DisabledPlayerControls) ||
            RequiredInteger(source, "dadDoTalk") != expected.DadDoTalk ||
            RequiredDouble(source, "dadTimerSeconds") != expected.DadTimerSeconds ||
            RequiredInteger(source, "accountedCommandCount") !=
                expected.AccountedCommandCount ||
            RequiredInteger(source, "appliedCommandCount") != expected.AppliedCommandCount ||
            !RequiredStringArray(source, "appliedExecutionTrace").SequenceEqual(
                expected.AppliedExecutionTrace) ||
            RequiredBoolean(boundary, "applied") != expected.NextBoundary.Applied ||
            RequiredString(boundary, "blocker") != expected.NextBoundary.Blocker)
            throw new InvalidOperationException(
                "Saved Fallout 3 CG01 stage-12 state differs.");
    }

    private static void RequireDadVariable(
        JsonElement source,
        int index,
        Fo3Cg01Stage0Transition stage0,
        string variable,
        string variableType,
        double value)
    {
        RequireCommand(source, index, "setScriptVariable");
        var dadVariable = stage0.DadVariables.Single(candidate => candidate.Variable == "doTalk");
        if (RequiredFormId(source, "referenceFormId") != stage0.Dad.FormId ||
            RequiredString(source, "referenceEditorId") != stage0.Dad.EditorId ||
            RequiredFormId(source, "scriptFormId") != dadVariable.ScriptFormId ||
            RequiredString(source, "scriptEditorId") != dadVariable.ScriptEditorId ||
            RequiredString(source, "variable") != variable ||
            RequiredString(source, "variableType") != variableType ||
            RequiredDouble(source, "value") != value)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad variable differs.");
    }

    private static Fo3Cg01Transform LoadTransform(JsonElement source) => new(
        ReadVector3(source, "positionGameUnits"),
        ReadVector3(source, "rotationRadians"),
        RequiredDouble(source, "scale"));

    private static Fo3Cg01Vector3 ReadVector3(JsonElement parent, string name)
    {
        var values = RequiredArray(parent, name).EnumerateArray().Select(value =>
            value.TryGetDouble(out var result) && double.IsFinite(result)
                ? result
                : throw new InvalidOperationException(
                    $"Fallout 3 CG01 stage-12 vector {name} differs.")).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 vector {name} differs.");
        return new Fo3Cg01Vector3(values[0], values[1], values[2]);
    }

    private static void RequireCommand(JsonElement source, int index, string kind)
    {
        if (RequiredInteger(source, "index") != index || RequiredString(source, "kind") != kind)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 command order differs.");
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 field {name} is invalid.");
        return result;
    }

    private static double RequiredDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 field {name} is invalid.");
        return value.GetBoolean();
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 hash {name} is invalid.");
        return value;
    }

    private static int[] RequiredIntegerArray(JsonElement parent, string name) =>
        RequiredArray(parent, name).EnumerateArray().Select(value =>
            value.TryGetInt32(out var result)
                ? result
                : throw new InvalidOperationException(
                    $"Fallout 3 CG01 stage-12 field {name} contains an invalid value.")).ToArray();

    private static string[] RequiredStringArray(JsonElement parent, string name) =>
        RequiredArray(parent, name).EnumerateArray().Select(value =>
            value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw new InvalidOperationException(
                    $"Fallout 3 CG01 stage-12 field {name} contains an invalid value.")).ToArray();
}
