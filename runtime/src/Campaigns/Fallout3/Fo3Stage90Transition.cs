using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Stage90Variable(
    string Name,
    string Type,
    double Value);

internal sealed record Fo3Stage90FadeKey(float Time, Color Color);

internal sealed record Fo3Stage90ImageSpaceModifier(
    string FormId,
    string EditorId,
    float DurationSeconds,
    IReadOnlyList<Fo3Stage90FadeKey> Fade,
    string RecordSha256);

internal sealed record Fo3Stage90Sound(
    string FormId,
    string EditorId,
    Fo3OwnedDialogueAsset Asset,
    string RecordSha256,
    string SoundDataSha256);

internal sealed record Fo3Stage90Dialogue(
    string InfoFormId,
    Fo3OwnedDialogueResponse Response);

internal sealed record Fo3Stage90State(
    int Stage,
    string AppliedInfoFormId,
    int AppliedCommandCount,
    IReadOnlyList<Fo3Stage90Variable> QuestVariables,
    Fo3Stage90ImageSpaceModifier ImageSpaceModifier,
    Fo3Stage90Sound Sound,
    bool ImageSpaceFadeApplied,
    bool ImageSpaceOtherChannelsApplied,
    bool SoundStarted,
    bool TimerAdvancing,
    string NextBoundary);

internal sealed record Fo3Stage90Transition(
    int SourceStage,
    int Stage,
    int AccountedCommandCount,
    Fo3Stage90Dialogue Dialogue,
    IReadOnlyList<Fo3Stage90Variable> QuestVariables,
    Fo3Stage90ImageSpaceModifier ImageSpaceModifier,
    Fo3Stage90Sound Sound,
    string NextBoundary)
{
    internal const string ExpectedSchema = "opennv-fo3-cg00-stage-90-transition/v1";
    private const string ExpectedDialogueSchema =
        "opennv-fo3-cg00-post-stage-85-dialogue/v1";
    private const string ExpectedDialogueStatus = "source-backed-info-result-trigger";
    private const string ExpectedStatus = "source-backed-stage-result-contract";
    private const int ExpectedCommandCount = 4;
    private const int GetStageFunction = 58;
    private const int GetIsVoiceTypeFunction = 427;

    internal static Fo3Stage90Transition Load(
        JsonElement dialogue,
        JsonElement transition,
        int expectedSourceStage,
        int expectedMinimumStage,
        string questFormId)
    {
        if (RequiredString(dialogue, "schema") != ExpectedDialogueSchema ||
            RequiredString(dialogue, "status") != ExpectedDialogueStatus ||
            RequiredInteger(dialogue, "sourceStage") != expectedSourceStage ||
            RequiredInteger(dialogue, "minimumQuestStage") != expectedMinimumStage ||
            !RequiredBoolean(dialogue, "dialoguePlaybackPrepared") ||
            !RequiredBoolean(dialogue, "dialoguePlaybackImplemented"))
            throw new InvalidOperationException(
                "Fallout 3 post-stage-85 INFO trigger contract is unsupported.");
        var stage = RequiredInteger(dialogue, "targetStage");
        if (stage <= expectedSourceStage)
            throw new InvalidOperationException(
                "Fallout 3 stage-90 INFO result is not forward-moving.");

        var topic = RequiredObject(dialogue, "topic");
        _ = RequiredFormId(topic, "formId");
        _ = RequiredString(topic, "editorId");
        _ = RequiredSha256(topic, "recordSha256");
        if (RequiredFormId(topic, "questFormId") != questFormId)
            throw new InvalidOperationException("Fallout 3 stage-90 INFO topic quest differs.");
        var voice = RequiredObject(dialogue, "voiceType");
        var voiceFormId = RequiredFormId(voice, "formId");
        var voiceEditorId = RequiredString(voice, "editorId");
        _ = RequiredSha256(voice, "recordSha256");

        var branches = RequiredArray(dialogue, "branches").EnumerateArray().ToArray();
        if (branches.Length != 1)
            throw new InvalidOperationException(
                "Fallout 3 stage-90 progression INFO is absent or ambiguous.");
        var branch = branches[0];
        _ = RequiredSha256(branch, "recordSha256");
        _ = RequiredSha256(branch, "resultSourceSha256");
        if (RequiredInteger(branch, "targetStage") != stage ||
            RequiredInteger(branch, "continuationMarkerCount") != 1)
            throw new InvalidOperationException(
                "Fallout 3 stage-90 progression INFO continuation differs.");
        var conditions = RequiredArray(branch, "conditions").EnumerateArray()
            .ToDictionary(value => RequiredInteger(value, "function"));
        if (!conditions.Keys.ToHashSet().SetEquals(new[]
            {
                GetStageFunction,
                GetIsVoiceTypeFunction,
            }))
            throw new InvalidOperationException("Fallout 3 stage-90 INFO conditions differ.");
        ValidateCondition(
            conditions[GetIsVoiceTypeFunction],
            0,
            1.0,
            voiceFormId);
        ValidateCondition(
            conditions[GetStageFunction],
            0x60,
            expectedMinimumStage,
            questFormId);
        var infoFormId = RequiredFormId(branch, "infoFormId");
        var response = LoadResponse(
            RequiredObject(branch, "response"),
            infoFormId,
            voiceEditorId);

        var stageResult = RequiredObject(dialogue, "stageResult");
        var stageSourceSha256 = RequiredSha256(stageResult, "stageSourceSha256");
        if (!RequiredBoolean(stageResult, "runtimeReady") ||
            RequiredString(stageResult, "contractSchema") != ExpectedSchema)
            throw new InvalidOperationException("Fallout 3 stage-90 result is not runtime-ready.");
        var sourceCommands = RequiredArray(stageResult, "commands").EnumerateArray().ToArray();
        if (sourceCommands.Length != ExpectedCommandCount)
            throw new InvalidOperationException("Fallout 3 stage-90 source commands differ.");

        if (RequiredString(transition, "schema") != ExpectedSchema ||
            RequiredString(transition, "status") != ExpectedStatus ||
            RequiredInteger(transition, "sourceStage") != expectedSourceStage ||
            RequiredInteger(transition, "stage") != stage ||
            RequiredString(transition, "dialogueTriggerSchema") != ExpectedDialogueSchema ||
            RequiredSha256(transition, "stageSourceSha256") != stageSourceSha256 ||
            RequiredInteger(transition, "accountedCommandCount") != ExpectedCommandCount)
            throw new InvalidOperationException("Fallout 3 stage-90 transition differs.");
        var commands = RequiredArray(transition, "commands").EnumerateArray().ToArray();
        if (commands.Length != ExpectedCommandCount)
            throw new InvalidOperationException("Fallout 3 stage-90 command count differs.");
        var expectedKinds = new[]
        {
            "setQuestVariable",
            "setQuestVariable",
            "applyImageSpaceModifier",
            "playSound",
        };
        for (var index = 0; index < commands.Length; index++)
        {
            if (RequiredInteger(commands[index], "index") != index ||
                RequiredString(commands[index], "kind") != expectedKinds[index] ||
                RequiredString(sourceCommands[index], "kind") != expectedKinds[index])
                throw new InvalidOperationException("Fallout 3 stage-90 command order differs.");
        }

        var variables = new[]
        {
            LoadVariable(commands[0], sourceCommands[0], questFormId, "timer", "float", 2.2),
            LoadVariable(commands[1], sourceCommands[1], questFormId, "runTimer", "short", 1.0),
        };
        var modifier = LoadModifier(
            RequiredObject(commands[2], "modifier"),
            RequiredString(sourceCommands[2], "modifierEditorId"));
        var sound = LoadSound(
            RequiredObject(commands[3], "sound"),
            RequiredString(sourceCommands[3], "soundEditorId"));
        return new Fo3Stage90Transition(
            expectedSourceStage,
            stage,
            ExpectedCommandCount,
            new Fo3Stage90Dialogue(infoFormId, response),
            variables,
            modifier,
            sound,
            RequiredString(transition, "nextBoundary"));
    }

    internal Fo3Stage90State Apply(Fo3Stage85State stage85)
    {
        if (stage85.Stage != SourceStage)
            throw new InvalidOperationException("Fallout 3 stage-90 source state differs.");
        var variables = new List<Fo3Stage90Variable>();
        Fo3Stage90ImageSpaceModifier? modifier = null;
        Fo3Stage90Sound? sound = null;
        var commands = QuestVariables
            .Select((variable, sourceIndex) => new SourceGamebryoStageCommand<object>(
                sourceIndex,
                GamebryoStageCommandKind.SetQuestVariable,
                variable))
            .Append(new SourceGamebryoStageCommand<object>(
                QuestVariables.Count,
                GamebryoStageCommandKind.ImageSpaceModifier,
                ImageSpaceModifier))
            .Append(new SourceGamebryoStageCommand<object>(
                QuestVariables.Count + 1,
                GamebryoStageCommandKind.PlaySound,
                Sound))
            .ToArray();
        GamebryoStageCommandExecutor.ExecuteAll(commands, command =>
        {
            switch (command.Kind)
            {
                case GamebryoStageCommandKind.SetQuestVariable:
                    variables.Add((Fo3Stage90Variable)command.Value);
                    return true;
                case GamebryoStageCommandKind.ImageSpaceModifier:
                    modifier = (Fo3Stage90ImageSpaceModifier)command.Value;
                    return true;
                case GamebryoStageCommandKind.PlaySound:
                    sound = (Fo3Stage90Sound)command.Value;
                    return true;
                default:
                    return false;
            }
        });
        return new Fo3Stage90State(
            Stage,
            Dialogue.InfoFormId,
            commands.Length,
            variables,
            modifier ?? throw new InvalidOperationException(
                "Fallout 3 stage-90 image-space mutation was not persisted."),
            sound ?? throw new InvalidOperationException(
                "Fallout 3 stage-90 sound mutation was not persisted."),
            modifier is not null,
            false,
            sound is not null,
            true,
            NextBoundary);
    }

    internal void ValidateSavedState(JsonElement source, Fo3Stage90State expected)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredInteger(source, "stage") != expected.Stage ||
            RequiredFormId(source, "appliedInfoFormId") != expected.AppliedInfoFormId ||
            RequiredInteger(source, "appliedCommandCount") != expected.AppliedCommandCount ||
            RequiredBoolean(source, "imageSpaceFadeApplied") !=
                expected.ImageSpaceFadeApplied ||
            RequiredBoolean(source, "imageSpaceOtherChannelsApplied") !=
                expected.ImageSpaceOtherChannelsApplied ||
            RequiredBoolean(source, "soundStarted") != expected.SoundStarted ||
            RequiredBoolean(source, "timerAdvancing") != expected.TimerAdvancing ||
            RequiredString(source, "nextBoundary") != expected.NextBoundary)
            throw new InvalidOperationException("Saved Fallout 3 stage-90 state differs.");
        var variables = RequiredArray(source, "questVariables").EnumerateArray().ToArray();
        if (variables.Length != expected.QuestVariables.Count)
            throw new InvalidOperationException("Saved Fallout 3 stage-90 variables differ.");
        for (var index = 0; index < variables.Length; index++)
        {
            var expectedVariable = expected.QuestVariables[index];
            if (RequiredString(variables[index], "name") != expectedVariable.Name ||
                RequiredString(variables[index], "type") != expectedVariable.Type ||
                RequiredDouble(variables[index], "value") != expectedVariable.Value)
                throw new InvalidOperationException("Saved Fallout 3 stage-90 variable differs.");
        }
        var modifier = RequiredObject(source, "imageSpaceModifier");
        if (RequiredFormId(modifier, "formId") != expected.ImageSpaceModifier.FormId ||
            RequiredString(modifier, "editorId") != expected.ImageSpaceModifier.EditorId ||
            RequiredSha256(modifier, "recordSha256") !=
                expected.ImageSpaceModifier.RecordSha256)
            throw new InvalidOperationException(
                "Saved Fallout 3 stage-90 image-space modifier differs.");
        var sound = RequiredObject(source, "sound");
        if (RequiredFormId(sound, "formId") != expected.Sound.FormId ||
            RequiredString(sound, "editorId") != expected.Sound.EditorId ||
            RequiredSha256(sound, "assetSha256") != expected.Sound.Asset.Sha256)
            throw new InvalidOperationException("Saved Fallout 3 stage-90 sound differs.");
    }

    private static Fo3Stage90Variable LoadVariable(
        JsonElement command,
        JsonElement sourceCommand,
        string questFormId,
        string expectedName,
        string expectedType,
        double expectedValue)
    {
        if (RequiredFormId(command, "questFormId") != questFormId ||
            RequiredString(command, "questEditorId") != "CG00" ||
            RequiredString(command, "scriptEditorId") != "CG00SCRIPT" ||
            RequiredString(command, "variable") != expectedName ||
            RequiredString(command, "variableType") != expectedType ||
            RequiredDouble(command, "value") != expectedValue ||
            RequiredString(sourceCommand, "subject") != "CG00" ||
            RequiredString(sourceCommand, "variable") != expectedName ||
            RequiredDouble(sourceCommand, "value") != expectedValue)
            throw new InvalidOperationException("Fallout 3 stage-90 quest variable differs.");
        _ = RequiredFormId(command, "scriptFormId");
        _ = RequiredSha256(command, "scriptSourceSha256");
        return new Fo3Stage90Variable(expectedName, expectedType, expectedValue);
    }

    private static Fo3Stage90ImageSpaceModifier LoadModifier(
        JsonElement source,
        string expectedEditorId)
    {
        var formId = RequiredFormId(source, "formId");
        var editorId = RequiredString(source, "editorId");
        var duration = (float)RequiredDouble(source, "duration");
        if (editorId != expectedEditorId || duration <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 3 stage-90 image-space modifier identity differs.");
        var keys = RequiredArray(source, "fade").EnumerateArray().Select(value =>
        {
            var components = value.EnumerateArray()
                .Select(component => component.GetSingle())
                .ToArray();
            if (components.Length != 5 || components.Any(component => !float.IsFinite(component)))
                throw new InvalidOperationException(
                    "Fallout 3 stage-90 fade key is invalid.");
            return new Fo3Stage90FadeKey(
                components[0],
                new Color(components[1], components[2], components[3], components[4]));
        }).ToArray();
        if (keys.Length < 2 || keys[0].Time != 0.0f || keys[^1].Time != 1.0f ||
            keys.Any(key => key.Time < 0.0f || key.Time > 1.0f) ||
            keys.Zip(keys.Skip(1)).Any(pair => pair.First.Time > pair.Second.Time))
            throw new InvalidOperationException("Fallout 3 stage-90 fade keys differ.");
        return new Fo3Stage90ImageSpaceModifier(
            formId,
            editorId,
            duration,
            keys,
            RequiredSha256(source, "recordSha256"));
    }

    private static Fo3Stage90Sound LoadSound(JsonElement source, string expectedEditorId)
    {
        var formId = RequiredFormId(source, "formId");
        var editorId = RequiredString(source, "editorId");
        if (editorId != expectedEditorId)
            throw new InvalidOperationException("Fallout 3 stage-90 sound identity differs.");
        var logicalPath = RequiredString(source, "logicalPath").Replace('/', '\\');
        var asset = LoadAsset(RequiredObject(source, "asset"), logicalPath);
        return new Fo3Stage90Sound(
            formId,
            editorId,
            asset,
            RequiredSha256(source, "recordSha256"),
            RequiredSha256(source, "soundDataSha256"));
    }

    private static Fo3OwnedDialogueResponse LoadResponse(
        JsonElement source,
        string infoFormId,
        string voiceEditorId)
    {
        var index = RequiredInteger(source, "index");
        if (index != 1)
            throw new InvalidOperationException("Fallout 3 stage-90 response index differs.");
        var text = RequiredString(source, "text");
        var expectedTextHash = RequiredSha256(source, "textSha256");
        var actualTextHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        if (!actualTextHash.Equals(expectedTextHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 stage-90 response text hash differs.");
        var suffix = $"_{infoFormId}_{index}";
        var namespacePrefix = $"sound\\voice\\fallout3.esm\\{voiceEditorId}\\";
        return new Fo3OwnedDialogueResponse(
            index,
            text,
            LoadAsset(RequiredObject(source, "voice"), suffix + ".ogg", namespacePrefix),
            LoadAsset(RequiredObject(source, "lip"), suffix + ".lip", namespacePrefix));
    }

    private static Fo3OwnedDialogueAsset LoadAsset(
        JsonElement source,
        string expectedPathOrSuffix,
        string? expectedPrefix = null)
    {
        var logicalPath = RequiredString(source, "logicalPath").Replace('/', '\\');
        if ((expectedPrefix is null &&
                !logicalPath.Equals(expectedPathOrSuffix, StringComparison.OrdinalIgnoreCase)) ||
            (expectedPrefix is not null &&
                (!logicalPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
                 !logicalPath.EndsWith(expectedPathOrSuffix, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Fallout 3 stage-90 owned asset identity differs.");
        var sourceArchive = RequiredString(source, "sourceArchive");
        if (sourceArchive != (expectedPrefix is null ? "Fallout - Sound.bsa" : "Fallout - Voices.bsa"))
            throw new InvalidOperationException("Fallout 3 stage-90 owned archive differs.");
        _ = RequiredSha256(source, "sourceArchiveSha256");
        var path = Path.GetFullPath(RequiredString(source, "source"));
        var expectedBytes = RequiredLong(source, "bytes");
        var expectedSha256 = RequiredSha256(source, "sha256");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedBytes)
            throw new InvalidOperationException("Fallout 3 stage-90 owned asset is absent.");
        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 stage-90 owned asset hash differs.");
        return new Fo3OwnedDialogueAsset(logicalPath, path, expectedBytes, expectedSha256);
    }

    private static void ValidateCondition(
        JsonElement source,
        int operatorFlags,
        double comparisonValue,
        string parameter1)
    {
        if (RequiredInteger(source, "operatorFlags") != operatorFlags ||
            RequiredDouble(source, "comparisonValue") != comparisonValue ||
            RequiredFormId(source, "parameter1") != parameter1 ||
            RequiredInteger(source, "parameter2") != 0 ||
            RequiredInteger(source, "runOn") != 0 ||
            RequiredFormId(source, "reference") != "00000000")
            throw new InvalidOperationException("Fallout 3 stage-90 INFO condition differs.");
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 stage-90 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 stage-90 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 stage-90 field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 stage-90 field {name} is invalid.");
        return result;
    }

    private static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result))
            throw new InvalidOperationException($"Fallout 3 stage-90 field {name} is invalid.");
        return result;
    }

    private static double RequiredDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 stage-90 field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 stage-90 field {name} is invalid.");
        return value.GetBoolean();
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-90 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-90 hash {name} is invalid.");
        return value;
    }
}
