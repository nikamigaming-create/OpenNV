using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Stage100Reference(
    string FormId,
    string EditorId);

internal sealed record Fo3Stage100Variable(
    string ReferenceFormId,
    string ReferenceEditorId,
    string Variable,
    int Value);

internal sealed record Fo3Stage100ImageSpaceModifier(
    string FormId,
    string EditorId,
    string RecordSha256);

internal sealed record Fo3Stage100Boundary(
    string QuestFormId,
    string QuestEditorId,
    int Stage,
    string StageResultSourceSha256,
    int StageResultCommandCount,
    Fo3Stage100TransitionContract TransitionContract,
    bool Applied,
    string Blocker);

internal sealed record Fo3Stage100TransitionContract(
    string Schema,
    string Sha256);

internal sealed record Fo3Stage100State(
    int Stage,
    int AccountedCommandCount,
    int AppliedCommandCount,
    double TimerRemainingSeconds,
    bool TimerAdvancing,
    bool PlayerScriptPackageActive,
    IReadOnlyList<Fo3Stage100Variable> ScriptVariables,
    Fo3Stage100ImageSpaceModifier RemovedImageSpaceModifier,
    Fo3Stage100Reference DisabledDad,
    bool Cg00Running,
    bool PlayerYoung,
    Fo3Stage100Boundary NextBoundary);

internal sealed record Fo3Stage100Transition(
    int SourceStage,
    int Stage,
    int AccountedCommandCount,
    int AppliedCommandCount,
    double TimerInitialSeconds,
    IReadOnlyList<Fo3Stage100Variable> ScriptVariables,
    Fo3Stage100ImageSpaceModifier RemovedImageSpaceModifier,
    Fo3Stage100Reference DisabledDad,
    Fo3Stage100Boundary NextBoundary)
{
    internal const string ExpectedSchema = "opennv-fo3-cg00-stage-100-transition/v1";
    private const string ExpectedStatus =
        "source-backed-timer-stage-result-through-next-quest-boundary";
    private const string ExpectedNextBoundaryBlocker =
        "fo3-cg01-stage-0-runtime-application-not-implemented";
    private const int ExpectedAccountedCommandCount = 8;
    private const int ExpectedAppliedCommandCount = 7;

    internal static Fo3Stage100Transition Load(
        JsonElement source,
        Fo3Stage90Transition stage90,
        string questFormId)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus ||
            RequiredInteger(source, "sourceStage") != stage90.Stage ||
            stage90.NextBoundary != ExpectedSchema)
            throw new InvalidOperationException("Fallout 3 stage-100 transition differs.");
        var stage = RequiredInteger(source, "stage");
        if (stage <= stage90.Stage)
            throw new InvalidOperationException("Fallout 3 stage-100 transition is not forward-moving.");

        var trigger = RequiredObject(source, "trigger");
        if (RequiredFormId(trigger, "questFormId") != questFormId ||
            RequiredString(trigger, "questEditorId") != "CG00" ||
            RequiredString(trigger, "scriptEditorId") != "CG00SCRIPT" ||
            RequiredInteger(trigger, "sourceStage") != stage90.Stage ||
            RequiredInteger(trigger, "targetStage") != stage ||
            RequiredString(trigger, "decrementFunction") != "GetSecondsPassed")
            throw new InvalidOperationException("Fallout 3 stage-100 timer trigger differs.");
        _ = RequiredFormId(trigger, "scriptFormId");
        _ = RequiredSha256(trigger, "scriptSourceSha256");
        var runVariable = RequiredObject(trigger, "runVariable");
        if (RequiredString(runVariable, "name") != "runTimer" ||
            RequiredString(runVariable, "type") != "short" ||
            RequiredInteger(runVariable, "requiredValue") != 1)
            throw new InvalidOperationException("Fallout 3 stage-100 run variable differs.");
        var timerVariable = RequiredObject(trigger, "timerVariable");
        var timerInitialSeconds = RequiredDouble(timerVariable, "initialValue");
        var stage90Timer = stage90.QuestVariables.Single(value => value.Name == "timer");
        if (RequiredString(timerVariable, "name") != stage90Timer.Name ||
            RequiredString(timerVariable, "type") != stage90Timer.Type ||
            timerInitialSeconds != stage90Timer.Value)
            throw new InvalidOperationException("Fallout 3 stage-100 timer variable differs.");

        _ = RequiredSha256(source, "stageSourceSha256");
        var accounted = RequiredInteger(source, "accountedCommandCount");
        var applied = RequiredInteger(source, "appliedCommandCount");
        var commands = RequiredArray(source, "commands").EnumerateArray().ToArray();
        if (accounted != ExpectedAccountedCommandCount ||
            applied != ExpectedAppliedCommandCount ||
            commands.Length != ExpectedAccountedCommandCount)
            throw new InvalidOperationException("Fallout 3 stage-100 commands are incomplete.");
        var expectedKinds = new[]
        {
            "removeScriptPackage",
            "setScriptVariable",
            "setScriptVariable",
            "removeImageSpaceModifier",
            "disable",
            "stopQuest",
            "setPlayerYoung",
            "setStage",
        };
        for (var index = 0; index < commands.Length; index++)
        {
            if (RequiredInteger(commands[index], "index") != index ||
                RequiredString(commands[index], "kind") != expectedKinds[index])
                throw new InvalidOperationException("Fallout 3 stage-100 command order differs.");
        }
        if (RequiredString(commands[0], "subject") != "player")
            throw new InvalidOperationException(
                "Fallout 3 stage-100 package-removal subject differs.");

        var variables = new[]
        {
            LoadVariable(commands[1], "CG00MomREF"),
            LoadVariable(commands[2], "CG00DadREF"),
        };
        var modifier = RequiredObject(commands[3], "modifier");
        var removedModifier = new Fo3Stage100ImageSpaceModifier(
            RequiredFormId(modifier, "formId"),
            RequiredString(modifier, "editorId"),
            RequiredSha256(modifier, "recordSha256"));
        if (removedModifier.EditorId != "CG00BirthBaseISFX")
            throw new InvalidOperationException(
                "Fallout 3 stage-100 removed image-space modifier differs.");

        var disabledDad = LoadReference(commands[4]);
        if (RequiredBoolean(commands[4], "initiallyDisabled") ||
            disabledDad.FormId != variables[1].ReferenceFormId ||
            disabledDad.EditorId != variables[1].ReferenceEditorId)
            throw new InvalidOperationException("Fallout 3 stage-100 Dad disable differs.");
        var stoppedQuest = commands[5];
        if (RequiredFormId(stoppedQuest, "questFormId") != questFormId ||
            RequiredString(stoppedQuest, "questEditorId") != "CG00")
            throw new InvalidOperationException("Fallout 3 stage-100 stopped quest differs.");
        _ = RequiredSha256(stoppedQuest, "questRecordSha256");
        if (RequiredInteger(commands[6], "value") != 1)
            throw new InvalidOperationException("Fallout 3 stage-100 player-young value differs.");

        var nextBoundary = LoadBoundary(RequiredObject(source, "nextBoundary"));
        var nextCommand = commands[7];
        var nextCommandContract = LoadTransitionContract(
            RequiredObject(nextCommand, "stageResultContract"));
        if (RequiredFormId(nextCommand, "questFormId") != nextBoundary.QuestFormId ||
            RequiredString(nextCommand, "questEditorId") != nextBoundary.QuestEditorId ||
            RequiredInteger(nextCommand, "stage") != nextBoundary.Stage ||
            RequiredSha256(nextCommand, "stageResultSourceSha256") !=
                nextBoundary.StageResultSourceSha256 ||
            RequiredInteger(nextCommand, "stageResultCommandCount") !=
                nextBoundary.StageResultCommandCount ||
            nextCommandContract != nextBoundary.TransitionContract ||
            RequiredBoolean(nextCommand, "applied") ||
            nextBoundary.Applied)
            throw new InvalidOperationException("Fallout 3 stage-100 next boundary differs.");

        return new Fo3Stage100Transition(
            stage90.Stage,
            stage,
            accounted,
            applied,
            timerInitialSeconds,
            variables,
            removedModifier,
            disabledDad,
            nextBoundary);
    }

    internal Fo3Stage100State Apply(Fo3Stage90State stage90, double timerRemainingSeconds)
    {
        if (stage90.Stage != SourceStage ||
            !stage90.TimerAdvancing ||
            stage90.QuestVariables.Single(value => value.Name == "runTimer").Value != 1.0 ||
            timerRemainingSeconds > 0.0 ||
            !double.IsFinite(timerRemainingSeconds))
            throw new InvalidOperationException("Fallout 3 stage-100 timer state differs.");
        return new Fo3Stage100State(
            Stage,
            AccountedCommandCount,
            AppliedCommandCount,
            0.0,
            false,
            false,
            ScriptVariables,
            RemovedImageSpaceModifier,
            DisabledDad,
            false,
            true,
            NextBoundary);
    }

    internal void ValidateSavedState(JsonElement source, Fo3Stage100State expected)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredInteger(source, "stage") != expected.Stage ||
            RequiredInteger(source, "accountedCommandCount") !=
                expected.AccountedCommandCount ||
            RequiredInteger(source, "appliedCommandCount") != expected.AppliedCommandCount ||
            RequiredDouble(source, "timerRemainingSeconds") != expected.TimerRemainingSeconds ||
            RequiredBoolean(source, "timerAdvancing") != expected.TimerAdvancing ||
            RequiredBoolean(source, "playerScriptPackageActive") !=
                expected.PlayerScriptPackageActive ||
            RequiredBoolean(source, "cg00Running") != expected.Cg00Running ||
            RequiredBoolean(source, "playerYoung") != expected.PlayerYoung)
            throw new InvalidOperationException("Saved Fallout 3 stage-100 state differs.");
        ValidateVariables(RequiredArray(source, "scriptVariables"), expected.ScriptVariables);
        var modifier = RequiredObject(source, "removedImageSpaceModifier");
        if (RequiredFormId(modifier, "formId") != expected.RemovedImageSpaceModifier.FormId ||
            RequiredString(modifier, "editorId") != expected.RemovedImageSpaceModifier.EditorId ||
            RequiredSha256(modifier, "recordSha256") !=
                expected.RemovedImageSpaceModifier.RecordSha256)
            throw new InvalidOperationException(
                "Saved Fallout 3 stage-100 removed modifier differs.");
        var dad = RequiredObject(source, "disabledDad");
        if (RequiredFormId(dad, "formId") != expected.DisabledDad.FormId ||
            RequiredString(dad, "editorId") != expected.DisabledDad.EditorId)
            throw new InvalidOperationException("Saved Fallout 3 stage-100 Dad differs.");
        var boundary = LoadBoundary(RequiredObject(source, "nextBoundary"));
        if (boundary != expected.NextBoundary)
            throw new InvalidOperationException("Saved Fallout 3 stage-100 boundary differs.");
    }

    private static Fo3Stage100Variable LoadVariable(JsonElement source, string editorId)
    {
        var reference = LoadReference(source);
        _ = RequiredFormId(source, "scriptFormId");
        _ = RequiredString(source, "scriptEditorId");
        _ = RequiredSha256(source, "scriptSourceSha256");
        if (reference.EditorId != editorId ||
            RequiredString(source, "variable") != "doTalk" ||
            RequiredString(source, "variableType") != "short" ||
            RequiredInteger(source, "value") != 0)
            throw new InvalidOperationException(
                "Fallout 3 stage-100 script-variable command differs.");
        return new Fo3Stage100Variable(reference.FormId, reference.EditorId, "doTalk", 0);
    }

    private static Fo3Stage100Reference LoadReference(JsonElement source)
    {
        _ = RequiredSha256(source, "referenceRecordSha256");
        _ = RequiredFormId(source, "baseFormId");
        _ = RequiredString(source, "baseEditorId");
        _ = RequiredSha256(source, "baseRecordSha256");
        return new Fo3Stage100Reference(
            RequiredFormId(source, "referenceFormId"),
            RequiredString(source, "referenceEditorId"));
    }

    private static Fo3Stage100Boundary LoadBoundary(JsonElement source)
    {
        if (RequiredInteger(source, "commandIndex") != ExpectedAccountedCommandCount - 1 ||
            RequiredString(source, "kind") != "setStage" ||
            RequiredBoolean(source, "applied") ||
            RequiredString(source, "blocker") != ExpectedNextBoundaryBlocker)
            throw new InvalidOperationException("Fallout 3 stage-100 boundary is unsupported.");
        return new Fo3Stage100Boundary(
            RequiredFormId(source, "questFormId"),
            RequiredString(source, "questEditorId"),
            RequiredInteger(source, "stage"),
            RequiredSha256(source, "stageResultSourceSha256"),
            RequiredInteger(source, "stageResultCommandCount"),
            LoadTransitionContract(RequiredObject(source, "transitionContract")),
            false,
            ExpectedNextBoundaryBlocker);
    }

    private static Fo3Stage100TransitionContract LoadTransitionContract(JsonElement source)
    {
        var contract = new Fo3Stage100TransitionContract(
            RequiredString(source, "schema"),
            RequiredSha256(source, "sha256"));
        if (contract.Schema != Fo3Cg01Stage0Transition.ExpectedSchema)
            throw new InvalidOperationException(
                "Fallout 3 stage-100 next transition contract is unsupported.");
        return contract;
    }

    private static void ValidateVariables(
        JsonElement source,
        IReadOnlyList<Fo3Stage100Variable> expected)
    {
        var values = source.EnumerateArray().ToArray();
        if (values.Length != expected.Count)
            throw new InvalidOperationException("Saved Fallout 3 stage-100 variables differ.");
        foreach (var variable in expected)
        {
            var value = values.Single(row =>
                RequiredFormId(row, "referenceFormId") == variable.ReferenceFormId);
            if (RequiredString(value, "referenceEditorId") != variable.ReferenceEditorId ||
                RequiredString(value, "variable") != variable.Variable ||
                RequiredInteger(value, "value") != variable.Value)
                throw new InvalidOperationException(
                    "Saved Fallout 3 stage-100 variable differs.");
        }
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 stage-100 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 stage-100 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 stage-100 field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 stage-100 field {name} is invalid.");
        return result;
    }

    private static double RequiredDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 stage-100 field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 stage-100 field {name} is invalid.");
        return value.GetBoolean();
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-100 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-100 hash {name} is invalid.");
        return value;
    }
}
