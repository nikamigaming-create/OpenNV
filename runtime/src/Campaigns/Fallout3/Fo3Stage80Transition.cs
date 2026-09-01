using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Stage80Reference(
    string FormId,
    string EditorId);

internal sealed record Fo3Stage80Variable(
    string ReferenceFormId,
    string ReferenceEditorId,
    string Variable,
    int Value);

internal sealed record Fo3Stage80Package(
    string FormId,
    string EditorId,
    string LocationReferenceFormId,
    IReadOnlyList<string> IdleFormIds);

internal sealed record Fo3Stage80State(
    int Stage,
    string AppliedInfoFormId,
    int AppliedCommandCount,
    Fo3Stage80Package AddedPlayerPackage,
    IReadOnlyList<Fo3Stage80Variable> ScriptVariables,
    IReadOnlyList<Fo3Stage80Reference> EvaluatedPackageReferences,
    IReadOnlyList<Fo3Stage80Reference> EnabledReferences,
    string NextBoundary);

internal sealed record Fo3Stage80DialogueBranch(
    string EngineSex,
    string InfoFormId,
    Fo3OwnedDialogueResponse Response);

internal sealed record Fo3OwnedDialogueAsset(
    string LogicalPath,
    string SourcePath,
    long Bytes,
    string Sha256);

internal sealed record Fo3OwnedDialogueResponse(
    int Index,
    string Text,
    Fo3OwnedDialogueAsset Voice,
    Fo3OwnedDialogueAsset Lip);

internal sealed record Fo3Stage80Transition(
    int SourceStage,
    int Stage,
    int AccountedCommandCount,
    IReadOnlyDictionary<string, Fo3Stage80DialogueBranch> DialogueBranches,
    Fo3Stage80Package AddedPlayerPackage,
    IReadOnlyList<Fo3Stage80Variable> ScriptVariables,
    IReadOnlyList<Fo3Stage80Reference> EvaluatedPackageReferences,
    IReadOnlyList<Fo3Stage80Reference> EnabledReferences,
    string NextBoundary)
{
    internal const string ExpectedSchema = "opennv-fo3-cg00-stage-80-transition/v1";
    private const string ExpectedDialogueSchema =
        "opennv-fo3-cg00-post-stage-65-dialogue/v1";
    private const string ExpectedDialogueStatus = "source-backed-info-result-trigger";
    private const string ExpectedStatus = "source-backed-stage-result-application";
    private const int ExpectedPackageType = 6;
    private const int ExpectedCommandCount = 7;
    private const int GetIsSexFunction = 70;
    private const int GetStageFunction = 58;
    private const int GetIsVoiceTypeFunction = 427;

    internal static Fo3Stage80Transition Load(
        JsonElement dialogue,
        JsonElement transition,
        int expectedSourceStage,
        string questFormId)
    {
        if (RequiredString(dialogue, "schema") != ExpectedDialogueSchema ||
            RequiredString(dialogue, "status") != ExpectedDialogueStatus ||
            RequiredInteger(dialogue, "sourceStage") != expectedSourceStage ||
            !RequiredBoolean(dialogue, "dialoguePlaybackPrepared") ||
            !RequiredBoolean(dialogue, "dialoguePlaybackImplemented"))
            throw new InvalidOperationException(
                "Fallout 3 post-stage-65 INFO trigger contract is unsupported.");
        var stage = RequiredInteger(dialogue, "targetStage");
        if (stage <= expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 INFO result is not forward-moving.");

        var topic = RequiredObject(dialogue, "topic");
        _ = RequiredFormId(topic, "formId");
        _ = RequiredString(topic, "editorId");
        _ = RequiredSha256(topic, "recordSha256");
        if (RequiredFormId(topic, "questFormId") != questFormId)
            throw new InvalidOperationException("Fallout 3 INFO topic quest differs.");
        var voice = RequiredObject(dialogue, "voiceType");
        var voiceFormId = RequiredFormId(voice, "formId");
        _ = RequiredString(voice, "editorId");
        _ = RequiredSha256(voice, "recordSha256");

        var branches = RequiredArray(dialogue, "branches").EnumerateArray()
            .Select(value => LoadBranch(value, stage, questFormId, voiceFormId))
            .ToDictionary(value => value.EngineSex, StringComparer.Ordinal);
        if (!branches.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "male", "female" }))
            throw new InvalidOperationException("Fallout 3 INFO result branches are incomplete.");

        var stageResult = RequiredObject(dialogue, "stageResult");
        var stageSourceSha256 = RequiredSha256(stageResult, "stageSourceSha256");
        if (!RequiredBoolean(stageResult, "runtimeReady") ||
            RequiredString(stageResult, "contractSchema") != ExpectedSchema)
            throw new InvalidOperationException("Fallout 3 stage-80 result is not runtime-ready.");
        var sourceCommands = RequiredArray(stageResult, "commands").EnumerateArray().ToArray();
        if (sourceCommands.Length != ExpectedCommandCount)
            throw new InvalidOperationException("Fallout 3 stage-80 source command count differs.");

        if (RequiredString(transition, "schema") != ExpectedSchema ||
            RequiredString(transition, "status") != ExpectedStatus ||
            RequiredInteger(transition, "sourceStage") != expectedSourceStage ||
            RequiredInteger(transition, "stage") != stage ||
            RequiredString(transition, "dialogueTriggerSchema") != ExpectedDialogueSchema ||
            RequiredSha256(transition, "stageSourceSha256") != stageSourceSha256)
            throw new InvalidOperationException("Fallout 3 stage-80 transition contract differs.");
        var accountedCommandCount = RequiredInteger(transition, "accountedCommandCount");
        var commands = RequiredArray(transition, "commands").EnumerateArray().ToArray();
        if (accountedCommandCount != ExpectedCommandCount || commands.Length != ExpectedCommandCount)
            throw new InvalidOperationException("Fallout 3 stage-80 commands are incomplete.");

        var packageSource = RequiredObject(transition, "addedPlayerPackage");
        var package = LoadPackage(packageSource);
        var variables = new List<Fo3Stage80Variable>();
        var evaluated = new List<Fo3Stage80Reference>();
        var enabled = new List<Fo3Stage80Reference>();
        var expectedKinds = new[]
        {
            "addScriptPackage",
            "setScriptVariable",
            "setScriptVariable",
            "evaluatePackage",
            "evaluatePackage",
            "evaluatePackage",
            "enable",
        };
        for (var index = 0; index < commands.Length; index++)
        {
            var sourceCommand = sourceCommands[index];
            var command = commands[index];
            var kind = RequiredString(command, "kind");
            if (RequiredInteger(command, "index") != index || kind != expectedKinds[index] ||
                RequiredString(sourceCommand, "kind") != kind)
                throw new InvalidOperationException("Fallout 3 stage-80 command order differs.");
            if (kind == "addScriptPackage")
            {
                if (RequiredFormId(command, "packageFormId") != package.FormId ||
                    RequiredString(command, "packageEditorId") != package.EditorId ||
                    RequiredString(sourceCommand, "packageEditorId") != package.EditorId)
                    throw new InvalidOperationException("Fallout 3 stage-80 package command differs.");
                continue;
            }

            var reference = LoadReference(command);
            if (RequiredString(sourceCommand, "subject") != reference.EditorId)
                throw new InvalidOperationException("Fallout 3 stage-80 command subject differs.");
            if (kind == "setScriptVariable")
            {
                _ = RequiredFormId(command, "baseFormId");
                _ = RequiredString(command, "baseEditorId");
                _ = RequiredSha256(command, "baseRecordSha256");
                _ = RequiredFormId(command, "scriptFormId");
                _ = RequiredString(command, "scriptEditorId");
                _ = RequiredSha256(command, "scriptSourceSha256");
                var variable = RequiredString(command, "variable");
                var value = RequiredInteger(command, "value");
                if (RequiredString(command, "variableType") != "short" ||
                    RequiredString(sourceCommand, "variable") != variable ||
                    RequiredInteger(sourceCommand, "value") != value)
                    throw new InvalidOperationException(
                        "Fallout 3 stage-80 script-variable command differs.");
                variables.Add(new Fo3Stage80Variable(
                    reference.FormId,
                    reference.EditorId,
                    variable,
                    value));
            }
            else if (kind == "evaluatePackage")
            {
                evaluated.Add(reference);
            }
            else
            {
                if (!RequiredBoolean(command, "initiallyDisabled"))
                    throw new InvalidOperationException(
                        "Fallout 3 stage-80 enable target was not initially disabled.");
                enabled.Add(reference);
            }
        }
        if (variables.Count != 2 || evaluated.Count != 3 || enabled.Count != 1)
            throw new InvalidOperationException("Fallout 3 stage-80 command semantics differ.");

        return new Fo3Stage80Transition(
            expectedSourceStage,
            stage,
            accountedCommandCount,
            branches,
            package,
            variables,
            evaluated,
            enabled,
            RequiredString(transition, "nextBoundary"));
    }

    internal Fo3Stage80State Apply(string engineSex, Fo3Stage65AppearanceState stage65)
    {
        if (stage65.Stage != SourceStage)
            throw new InvalidOperationException("Fallout 3 stage-80 source state differs.");
        var branch = DialogueBranches[engineSex];
        var sourceIndex = 0;
        var commandList = new List<SourceGamebryoStageCommand<object>>
        {
            new(sourceIndex++, GamebryoStageCommandKind.AddScriptPackage, AddedPlayerPackage),
        };
        commandList.AddRange(ScriptVariables.Select(variable =>
            new SourceGamebryoStageCommand<object>(
                sourceIndex++, GamebryoStageCommandKind.SetScriptVariable, variable)));
        commandList.AddRange(EvaluatedPackageReferences.Select(reference =>
            new SourceGamebryoStageCommand<object>(
                sourceIndex++, GamebryoStageCommandKind.ActorIntent, reference)));
        commandList.AddRange(EnabledReferences.Select(reference =>
            new SourceGamebryoStageCommand<object>(
                sourceIndex++, GamebryoStageCommandKind.Enable, reference)));
        var commands = commandList.ToArray();
        Fo3Stage80Package? package = null;
        var variables = new List<Fo3Stage80Variable>();
        var evaluated = new List<Fo3Stage80Reference>();
        var enabled = new List<Fo3Stage80Reference>();
        var applied = 0;
        GamebryoStageCommandExecutor.ExecuteAll(commands, command =>
        {
            switch (command.Kind)
            {
                case GamebryoStageCommandKind.AddScriptPackage:
                    package = (Fo3Stage80Package)command.Value;
                    break;
                case GamebryoStageCommandKind.SetScriptVariable:
                    variables.Add((Fo3Stage80Variable)command.Value);
                    break;
                case GamebryoStageCommandKind.ActorIntent:
                    evaluated.Add((Fo3Stage80Reference)command.Value);
                    break;
                case GamebryoStageCommandKind.Enable:
                    enabled.Add((Fo3Stage80Reference)command.Value);
                    break;
                default:
                    return false;
            }
            applied++;
            return applied == command.SourceIndex + 1;
        });
        return new Fo3Stage80State(
            Stage,
            branch.InfoFormId,
            applied,
            package ?? throw new InvalidOperationException(
                "Fallout 3 stage-80 package mutation was not persisted."),
            variables,
            evaluated,
            enabled,
            NextBoundary);
    }

    internal Fo3Stage80DialogueBranch DialogueFor(string engineSex)
    {
        if (!DialogueBranches.TryGetValue(engineSex, out var branch))
            throw new InvalidOperationException(
                "Fallout 3 post-stage-65 dialogue sex branch is unsupported.");
        return branch;
    }

    internal void ValidateSavedState(JsonElement source, Fo3Stage80State expected)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredInteger(source, "stage") != expected.Stage ||
            RequiredFormId(source, "appliedInfoFormId") != expected.AppliedInfoFormId ||
            RequiredInteger(source, "appliedCommandCount") != expected.AppliedCommandCount ||
            RequiredString(source, "nextBoundary") != expected.NextBoundary)
            throw new InvalidOperationException("Saved Fallout 3 stage-80 state differs.");
        var package = RequiredObject(source, "addedPlayerPackage");
        if (!RequiredBoolean(package, "active") ||
            RequiredFormId(package, "formId") != expected.AddedPlayerPackage.FormId ||
            RequiredString(package, "editorId") != expected.AddedPlayerPackage.EditorId ||
            RequiredFormId(package, "locationReferenceFormId") !=
                expected.AddedPlayerPackage.LocationReferenceFormId ||
            !RequiredArray(package, "idleFormIds").EnumerateArray()
                .Select(value => value.GetString() ?? "")
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(expected.AddedPlayerPackage.IdleFormIds))
            throw new InvalidOperationException("Saved Fallout 3 stage-80 package differs.");
        ValidateVariables(RequiredArray(source, "scriptVariables"), expected.ScriptVariables);
        ValidateReferences(
            RequiredArray(source, "evaluatedPackageReferences"),
            expected.EvaluatedPackageReferences);
        ValidateReferences(RequiredArray(source, "enabledReferences"), expected.EnabledReferences);
    }

    private static Fo3Stage80DialogueBranch LoadBranch(
        JsonElement source,
        int stage,
        string questFormId,
        string voiceFormId)
    {
        var engineSex = RequiredString(source, "engineSex");
        var expectedSex = engineSex switch
        {
            "male" => "00000000",
            "female" => "00000001",
            _ => throw new InvalidOperationException("Fallout 3 INFO sex branch is unsupported."),
        };
        _ = RequiredSha256(source, "recordSha256");
        _ = RequiredSha256(source, "resultSourceSha256");
        if (RequiredInteger(source, "targetStage") != stage)
            throw new InvalidOperationException("Fallout 3 INFO target stage differs.");
        var conditions = RequiredArray(source, "conditions").EnumerateArray()
            .ToDictionary(value => RequiredInteger(value, "function"));
        if (!conditions.Keys.ToHashSet().SetEquals(new[]
            {
                GetIsSexFunction,
                GetStageFunction,
                GetIsVoiceTypeFunction,
            }))
            throw new InvalidOperationException("Fallout 3 INFO conditions differ.");
        ValidateCondition(
            conditions[GetIsSexFunction],
            0,
            1.0,
            expectedSex,
            1);
        ValidateCondition(
            conditions[GetIsVoiceTypeFunction],
            0,
            1.0,
            voiceFormId,
            0);
        ValidateCondition(
            conditions[GetStageFunction],
            128,
            stage,
            questFormId,
            0);
        var infoFormId = RequiredFormId(source, "infoFormId");
        var response = RequiredObject(source, "response");
        var responseIndex = RequiredInteger(response, "index");
        if (responseIndex != 1)
            throw new InvalidOperationException(
                "Fallout 3 post-stage-65 response index is unsupported.");
        var responseText = RequiredString(response, "text");
        var responseTextSha256 = RequiredSha256(response, "textSha256");
        var actualTextSha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(responseText))).ToLowerInvariant();
        if (!actualTextSha256.Equals(responseTextSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 post-stage-65 response text hash differs.");
        var suffix = $"_{infoFormId}_{responseIndex}";
        return new Fo3Stage80DialogueBranch(
            engineSex,
            infoFormId,
            new Fo3OwnedDialogueResponse(
                responseIndex,
                responseText,
                LoadDialogueAsset(RequiredObject(response, "voice"), suffix + ".ogg"),
                LoadDialogueAsset(RequiredObject(response, "lip"), suffix + ".lip")));
    }

    private static Fo3OwnedDialogueAsset LoadDialogueAsset(
        JsonElement source,
        string expectedSuffix)
    {
        var logicalPath = RequiredString(source, "logicalPath");
        var normalizedLogicalPath = logicalPath.Replace('/', '\\');
        if (!normalizedLogicalPath.StartsWith(
                "sound\\voice\\fallout3.esm\\maleuniquedad\\",
                StringComparison.OrdinalIgnoreCase) ||
            !normalizedLogicalPath.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase) ||
            RequiredString(source, "sourceArchive") != "Fallout - Voices.bsa")
            throw new InvalidOperationException(
                "Fallout 3 post-stage-65 dialogue asset identity differs.");
        _ = RequiredSha256(source, "sourceArchiveSha256");
        var path = Path.GetFullPath(RequiredString(source, "source"));
        var expectedBytes = RequiredLong(source, "bytes");
        var expectedSha256 = RequiredSha256(source, "sha256");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedBytes)
            throw new InvalidOperationException(
                "Fallout 3 post-stage-65 dialogue asset is absent or changed.");
        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 post-stage-65 dialogue asset hash differs.");
        return new Fo3OwnedDialogueAsset(
            normalizedLogicalPath,
            path,
            expectedBytes,
            expectedSha256);
    }

    private static void ValidateCondition(
        JsonElement source,
        int operatorFlags,
        double comparisonValue,
        string parameter1,
        int runOn)
    {
        if (RequiredInteger(source, "operatorFlags") != operatorFlags ||
            RequiredDouble(source, "comparisonValue") != comparisonValue ||
            RequiredFormId(source, "parameter1") != parameter1 ||
            RequiredInteger(source, "parameter2") != 0 ||
            RequiredInteger(source, "runOn") != runOn ||
            RequiredFormId(source, "reference") != "00000000")
            throw new InvalidOperationException("Fallout 3 INFO condition differs.");
    }

    private static Fo3Stage80Package LoadPackage(JsonElement source)
    {
        var formId = RequiredFormId(source, "formId");
        var editorId = RequiredString(source, "editorId");
        _ = RequiredSha256(source, "recordSha256");
        _ = RequiredInteger(source, "flags");
        _ = RequiredInteger(source, "procedureFlags");
        _ = RequiredInteger(source, "typeSpecificFlags");
        if (RequiredInteger(source, "type") != ExpectedPackageType)
            throw new InvalidOperationException("Fallout 3 stage-80 package type differs.");
        var location = RequiredObject(source, "location");
        if (RequiredInteger(location, "type") != 0 || RequiredInteger(location, "radius") != 0)
            throw new InvalidOperationException("Fallout 3 stage-80 package location differs.");
        var locationReferenceFormId = RequiredFormId(location, "referenceFormId");
        var idleSelection = RequiredObject(source, "idleSelection");
        _ = RequiredInteger(idleSelection, "flags");
        _ = RequiredDouble(idleSelection, "timerSeconds");
        var idles = RequiredArray(idleSelection, "idles").EnumerateArray()
            .Select(value => RequiredFormId(value, "formId")).ToArray();
        if (idles.Length == 0 || idles.Length != RequiredInteger(idleSelection, "count") ||
            idles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != idles.Length)
            throw new InvalidOperationException("Fallout 3 stage-80 package idles differ.");
        var sources = RequiredArray(source, "animationSources").EnumerateArray().ToArray();
        if (!idles.All(formId => sources.Any(value =>
                RequiredFormId(value, "formId").Equals(formId, StringComparison.OrdinalIgnoreCase) &&
                ValidSha256(value, "sourceSha256"))))
            throw new InvalidOperationException("Fallout 3 stage-80 animation sources are incomplete.");
        var events = RequiredObject(source, "events");
        var eventNames = events.EnumerateObject().Select(value => value.Name).ToHashSet();
        if (!eventNames.SetEquals(new[] { "begin", "end", "change" }))
            throw new InvalidOperationException("Fallout 3 stage-80 package events differ.");
        foreach (var property in events.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
                continue;
            var eventFormId = RequiredFormId(property.Value, "formId");
            if (!idles.Contains(eventFormId, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fallout 3 stage-80 package event differs.");
        }
        return new Fo3Stage80Package(formId, editorId, locationReferenceFormId, idles);
    }

    private static Fo3Stage80Reference LoadReference(JsonElement source)
    {
        _ = RequiredSha256(source, "referenceRecordSha256");
        _ = RequiredFormId(source, "baseFormId");
        _ = RequiredString(source, "baseEditorId");
        _ = RequiredSha256(source, "baseRecordSha256");
        return new Fo3Stage80Reference(
            RequiredFormId(source, "referenceFormId"),
            RequiredString(source, "referenceEditorId"));
    }

    private static void ValidateVariables(
        JsonElement source,
        IReadOnlyList<Fo3Stage80Variable> expected)
    {
        var rows = source.EnumerateArray().ToArray();
        if (rows.Length != expected.Count)
            throw new InvalidOperationException("Saved Fallout 3 stage-80 variables differ.");
        foreach (var expectedVariable in expected)
        {
            var row = rows.Single(value =>
                RequiredFormId(value, "referenceFormId") == expectedVariable.ReferenceFormId);
            if (RequiredString(row, "referenceEditorId") != expectedVariable.ReferenceEditorId ||
                RequiredString(row, "variable") != expectedVariable.Variable ||
                RequiredInteger(row, "value") != expectedVariable.Value)
                throw new InvalidOperationException("Saved Fallout 3 stage-80 variable differs.");
        }
    }

    private static void ValidateReferences(
        JsonElement source,
        IReadOnlyList<Fo3Stage80Reference> expected)
    {
        var rows = source.EnumerateArray().ToArray();
        if (rows.Length != expected.Count)
            throw new InvalidOperationException("Saved Fallout 3 stage-80 references differ.");
        foreach (var expectedReference in expected)
        {
            var row = rows.Single(value =>
                RequiredFormId(value, "formId") == expectedReference.FormId);
            if (RequiredString(row, "editorId") != expectedReference.EditorId)
                throw new InvalidOperationException("Saved Fallout 3 stage-80 reference differs.");
        }
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 stage-80 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 stage-80 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 stage-80 field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 stage-80 field {name} is invalid.");
        return result;
    }

    private static double RequiredDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 stage-80 field {name} is invalid.");
        return result;
    }

    private static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !value.TryGetInt64(out var result) || result <= 0)
            throw new InvalidOperationException($"Fallout 3 stage-80 field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 stage-80 field {name} is invalid.");
        return value.GetBoolean();
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-80 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-80 hash {name} is invalid.");
        return value;
    }

    private static bool ValidSha256(JsonElement parent, string name)
    {
        _ = RequiredSha256(parent, name);
        return true;
    }
}
