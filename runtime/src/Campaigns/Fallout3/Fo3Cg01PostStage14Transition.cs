using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg01PostStage14Package(
    string FormId,
    string EditorId,
    string TargetFormId,
    Fo3Cg01Transform TargetTransform,
    int TargetRadiusGameUnits,
    int? CompletionStage);

internal sealed record Fo3Cg01PostStage14Cue(
    int Sequence,
    string InfoFormId,
    string? EngineSex,
    Fo3OwnedDialogueResponse Response);

internal sealed record Fo3Cg01Stage20State(
    int SourceStage,
    string ActiveQuestFormId,
    string ActiveQuestEditorId,
    int ActiveStage,
    IReadOnlyList<string> AppliedInfoFormIds,
    IReadOnlyList<string> AppliedPackageFormIds,
    string PlaypenGateReferenceFormId,
    bool PlaypenGateOpen,
    string PlayroomDoorReferenceFormId,
    bool PlayroomDoorOpen,
    int PlayroomDoorLockLevel,
    bool PlayerMovementEnabled,
    int DisplayedObjectiveIndex,
    int AccountedCommandCount,
    int AppliedCommandCount,
    Fo3Cg01Stage12Boundary NextBoundary);

internal sealed record Fo3Cg01Stage20Interaction(
    int SourceStage,
    int GateStage,
    int ExitStage,
    int BookStage,
    string GateReferenceFormId,
    string ExitTriggerReferenceFormId,
    Fo3Cg01Transform ExitTriggerTransform,
    Fo3Cg01Vector3 ExitTriggerDimensionsGameUnits,
    string BookReferenceFormId,
    string BookDisplayName,
    int MenuPoints,
    string MenuDocument,
    string NextBoundaryBlocker)
{
    internal const string ExpectedSchema = "opennv-fo3-cg01-stage-20-special-runtime/v1";

    internal static Fo3Cg01Stage20Interaction Load(JsonElement source, int expectedSourceStage)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != "source-backed-physical-interaction-runtime-ready" ||
            RequiredInteger(source, "sourceStage") != expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 CG01 stage-20 interaction identity differs.");
        var gate = RequiredObject(source, "gate");
        var exit = RequiredObject(source, "exitTrigger");
        var book = RequiredObject(source, "specialBook");
        var gateStage = RequiredInteger(gate, "targetStage");
        var exitStage = RequiredInteger(exit, "targetStage");
        var bookStage = RequiredInteger(book, "targetStage");
        if (!(expectedSourceStage < gateStage && gateStage < exitStage && exitStage < bookStage) ||
            RequiredInteger(book, "menuPoints") <= 0 ||
            RequiredInteger(exit, "primitiveType") != 1)
            throw new InvalidOperationException("Fallout 3 CG01 stage-20 interaction sequence differs.");
        var dimensions = RequiredArray(exit, "dimensionsGameUnits").EnumerateArray()
            .Select(value => value.GetDouble()).ToArray();
        if (dimensions.Length != 3 || dimensions.Any(value => !double.IsFinite(value) || value <= 0))
            throw new InvalidOperationException("Fallout 3 CG01 crib-exit dimensions differ.");
        var transformSource = RequiredObject(exit, "sourceTransform");
        var position = RequiredArray(transformSource, "positionGameUnits").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var rotation = RequiredArray(transformSource, "rotationRadians").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        if (position.Length != 3 || rotation.Length != 3)
            throw new InvalidOperationException("Fallout 3 CG01 crib-exit transform differs.");
        var boundary = RequiredObject(source, "nextBoundary");
        if (RequiredBoolean(boundary, "applied"))
            throw new InvalidOperationException("Fallout 3 CG01 stage-50 boundary differs.");
        return new Fo3Cg01Stage20Interaction(
            expectedSourceStage, gateStage, exitStage, bookStage,
            RequiredFormId(gate, "referenceFormId"), RequiredFormId(exit, "referenceFormId"),
            new Fo3Cg01Transform(new Fo3Cg01Vector3(position[0], position[1], position[2]),
                new Fo3Cg01Vector3(rotation[0], rotation[1], rotation[2]),
                RequiredDouble(transformSource, "scale")),
            new Fo3Cg01Vector3(dimensions[0], dimensions[1], dimensions[2]),
            RequiredFormId(book, "referenceFormId"), RequiredString(book, "displayName"),
            RequiredInteger(book, "menuPoints"), RequiredString(book, "menuDocument"),
            RequiredString(boundary, "blocker"));
    }

    private static double RequiredDouble(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) && double.IsFinite(result)
            ? result : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static int RequiredInteger(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static bool RequiredBoolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        return value.Length == 8 && value.All(Uri.IsHexDigit) ? value : throw new InvalidOperationException($"Fallout 3 CG01 interaction FormID {name} differs.");
    }
    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
}

internal sealed record Fo3Cg01PostStage14Transition(
    int SourceStage,
    int Stage16,
    int Stage18,
    int TargetStage,
    string DadReferenceFormId,
    Fo3Cg01PostStage14Package CloseGatePackage,
    Fo3Cg01PostStage14Package CloseDoorPackage,
    Fo3Cg01PostStage14Package LeaveRoomPackage,
    string PlaypenGateReferenceFormId,
    string PlayroomDoorReferenceFormId,
    int PlayroomDoorLockLevel,
    IReadOnlyList<int> EnabledPlayerControls,
    int ObjectiveIndex,
    IReadOnlyList<Fo3Cg01PostStage14Cue> Cues,
    int AccountedCommandCount,
    Fo3Cg01Stage20Interaction Stage20Interaction,
    string NextBoundaryBlocker)
{
    internal const string ExpectedSchema = "opennv-fo3-cg01-stage-14-to-20-runtime/v1";
    internal const string ExpectedSavedStateSchema =
        "opennv-fo3-cg01-stage-14-to-20-runtime-state/v1";

    private const string ExpectedStatus = "source-backed-package-dialogue-runtime-ready";
    private const int GetPcIsSexFunction = 131;
    private const int GetIsIdFunction = 72;
    private const int ExpectedCueRows = 3;
    private const int ExpectedAppliedCues = 2;
    private const int ExpectedPackageCount = 3;
    private const int FormIdRadix = 16;
    private const uint MaleSexValue = 0;
    private const uint FemaleSexValue = 1;

    internal static Fo3Cg01PostStage14Transition Load(
        JsonElement source,
        Fo3Cg01Stage0Transition stage0,
        Fo3Cg01Stage12DadResponse stage14)
    {
        var sourceStage = RequiredInteger(source, "sourceStage");
        var stage16 = RequiredInteger(source, "stage16");
        var stage18 = RequiredInteger(source, "stage18");
        var targetStage = RequiredInteger(source, "targetStage");
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus ||
            sourceStage != stage14.TargetStage ||
            !(sourceStage < stage16 && stage16 < stage18 && stage18 < targetStage) ||
            RequiredFormId(source, "dadReferenceFormId") != stage0.Dad.FormId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-14 identity differs.");

        var packages = RequiredObject(source, "packages");
        var closeGate = LoadPackage(
            RequiredObject(packages, "closeGate"), stage16);
        var closeDoor = LoadPackage(
            RequiredObject(packages, "closeDoor"), stage18);
        var leaveRoom = LoadPackage(
            RequiredObject(packages, "leaveRoom"), null);
        if (new[] { closeGate.FormId, closeDoor.FormId, leaveRoom.FormId }
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != ExpectedPackageCount)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-14 package identities differ.");

        var stage16Commands = LoadCommands(RequiredObject(source, "stage16Result"));
        var stage18Commands = LoadCommands(RequiredObject(source, "stage18Result"));
        var stage20Commands = LoadCommands(RequiredObject(source, "stage20Result"));
        var allCommands = stage16Commands.Concat(stage18Commands).Concat(stage20Commands).ToArray();
        var playpen = allCommands.Where(value => value.Kind == "setOpenState")
            .GroupBy(value => value.ReferenceFormId, StringComparer.OrdinalIgnoreCase)
            .Single(value => value.Count() == 2).Key;
        var playroom = allCommands.Single(value => value.Kind == "lock").ReferenceFormId;
        var lockLevel = allCommands.Single(value => value.Kind == "lock").Value;
        var controls = stage20Commands.Single(value => value.Kind == "enablePlayerControls")
            .Arguments;
        var objective = stage20Commands.Single(value => value.Kind == "setObjectiveDisplayed")
            .Value;
        if (!stage16Commands.Any(value => value.Kind == "setScriptVariable") ||
            !stage18Commands.Any(value => value.Kind == "setStage" && value.Value == targetStage) ||
            !stage20Commands.Any(value => value.Kind == "evaluatePackage" &&
                value.ReferenceFormId == stage0.Dad.FormId) ||
            controls.Count == 0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-14 commands differ.");

        var dialogue = RequiredObject(source, "dialogue");
        if (!RequiredBoolean(dialogue, "dialoguePlaybackPrepared") ||
            !RequiredBoolean(dialogue, "dialoguePlaybackImplemented") ||
            RequiredFormId(dialogue, "topicFormId") != stage14.TopicFormId ||
            RequiredString(dialogue, "topicEditorId") != stage14.TopicEditorId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 Dad dialogue is not prepared.");
        var rows = RequiredArray(dialogue, "branches").EnumerateArray()
            .OrderBy(value => RequiredInteger(value, "sequence")).ToArray();
        if (rows.Length != ExpectedCueRows)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 Dad dialogue coverage differs.");
        var cues = rows.Select((row, index) => LoadCue(row, index, stage0)).ToArray();
        if (cues.Count(value => value.EngineSex is null) != 1 ||
            cues.Count(value => value.EngineSex == "male") != 1 ||
            cues.Count(value => value.EngineSex == "female") != 1)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 Dad sex selection differs.");

        var boundary = RequiredObject(source, "nextBoundary");
        if (!RequiredBoolean(boundary, "applied") ||
            boundary.GetProperty("blocker").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-20 boundary differs.");
        var interaction = Fo3Cg01Stage20Interaction.Load(
            RequiredObject(source, "stage20Interaction"), targetStage);
        return new Fo3Cg01PostStage14Transition(
            sourceStage,
            stage16,
            stage18,
            targetStage,
            stage0.Dad.FormId,
            closeGate,
            closeDoor,
            leaveRoom,
            playpen,
            playroom,
            lockLevel,
            controls,
            objective,
            cues,
            allCommands.Length + ExpectedPackageCount,
            interaction,
            interaction.NextBoundaryBlocker);
    }

    internal IReadOnlyList<Fo3Cg01PostStage14Cue> SelectCues(string engineSex)
    {
        if (engineSex is not ("male" or "female"))
            throw new InvalidOperationException("Fallout 3 CG01 Dad response sex differs.");
        var selected = Cues.Where(value => value.EngineSex is null || value.EngineSex == engineSex)
            .OrderBy(value => value.Sequence).ToArray();
        if (selected.Length != ExpectedAppliedCues)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad response selection is incomplete.");
        return selected;
    }

    internal Fo3Cg01Stage20State Apply(Fo3Cg01Stage14State stage14, string engineSex)
    {
        if (stage14.ActiveStage != SourceStage || !stage14.DadPackageEvaluated ||
            stage14.NextBoundary.Applied)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-14 source state differs.");
        var cues = SelectCues(engineSex);
        return new Fo3Cg01Stage20State(
            SourceStage,
            stage14.ActiveQuestFormId,
            stage14.ActiveQuestEditorId,
            TargetStage,
            cues.Select(value => value.InfoFormId).ToArray(),
            [CloseGatePackage.FormId, CloseDoorPackage.FormId, LeaveRoomPackage.FormId],
            PlaypenGateReferenceFormId,
            false,
            PlayroomDoorReferenceFormId,
            false,
            PlayroomDoorLockLevel,
            true,
            ObjectiveIndex,
            AccountedCommandCount,
            AccountedCommandCount,
            new Fo3Cg01Stage12Boundary(false, NextBoundaryBlocker));
    }

    internal object SavedState(Fo3Cg01Stage20State state) => new
    {
        schema = ExpectedSavedStateSchema,
        sourceStage = state.SourceStage,
        activeQuest = new
        {
            formId = state.ActiveQuestFormId,
            editorId = state.ActiveQuestEditorId,
            stage = state.ActiveStage,
        },
        appliedInfoFormIds = state.AppliedInfoFormIds,
        appliedPackageFormIds = state.AppliedPackageFormIds,
        playpenGate = new { referenceFormId = state.PlaypenGateReferenceFormId, open = state.PlaypenGateOpen },
        playroomDoor = new
        {
            referenceFormId = state.PlayroomDoorReferenceFormId,
            open = state.PlayroomDoorOpen,
            lockLevel = state.PlayroomDoorLockLevel,
        },
        playerMovementEnabled = state.PlayerMovementEnabled,
        displayedObjectiveIndex = state.DisplayedObjectiveIndex,
        accountedCommandCount = state.AccountedCommandCount,
        appliedCommandCount = state.AppliedCommandCount,
        nextBoundary = new { applied = false, blocker = state.NextBoundary.Blocker },
    };

    internal void ValidateSavedState(JsonElement source, Fo3Cg01Stage20State expected)
    {
        var active = RequiredObject(source, "activeQuest");
        var gate = RequiredObject(source, "playpenGate");
        var door = RequiredObject(source, "playroomDoor");
        var boundary = RequiredObject(source, "nextBoundary");
        if (RequiredString(source, "schema") != ExpectedSavedStateSchema ||
            RequiredInteger(active, "stage") != expected.ActiveStage ||
            RequiredFormId(active, "formId") != expected.ActiveQuestFormId ||
            !RequiredArray(source, "appliedInfoFormIds").EnumerateArray()
                .Select(value => value.GetString()).SequenceEqual(expected.AppliedInfoFormIds) ||
            !RequiredArray(source, "appliedPackageFormIds").EnumerateArray()
                .Select(value => value.GetString()).SequenceEqual(expected.AppliedPackageFormIds) ||
            RequiredFormId(gate, "referenceFormId") != expected.PlaypenGateReferenceFormId ||
            RequiredBoolean(gate, "open") != expected.PlaypenGateOpen ||
            RequiredFormId(door, "referenceFormId") != expected.PlayroomDoorReferenceFormId ||
            RequiredBoolean(door, "open") != expected.PlayroomDoorOpen ||
            RequiredInteger(door, "lockLevel") != expected.PlayroomDoorLockLevel ||
            !RequiredBoolean(source, "playerMovementEnabled") ||
            RequiredInteger(source, "displayedObjectiveIndex") != expected.DisplayedObjectiveIndex ||
            RequiredInteger(source, "accountedCommandCount") != expected.AccountedCommandCount ||
            RequiredInteger(source, "appliedCommandCount") != expected.AppliedCommandCount ||
            RequiredBoolean(boundary, "applied") ||
            RequiredString(boundary, "blocker") != expected.NextBoundary.Blocker)
            throw new InvalidOperationException(
                "Saved Fallout 3 CG01 stage-20 state differs.");
    }

    private static Fo3Cg01PostStage14Package LoadPackage(
        JsonElement source,
        int? completionStage)
    {
        _ = RequiredSha256(source, "recordSha256");
        var target = RequiredObject(source, "target");
        if (RequiredString(target, "kind") != "referenceMarker")
            throw new InvalidOperationException(
                "Fallout 3 CG01 package target kind differs.");
        _ = RequiredSha256(target, "recordSha256");
        var actualCompletion = source.TryGetProperty("completionStage", out var completion)
            ? completion.GetInt32()
            : (int?)null;
        if (actualCompletion != completionStage)
            throw new InvalidOperationException(
                "Fallout 3 CG01 package completion stage differs.");
        var radius = RequiredInteger(target, "radiusGameUnits");
        if (radius < 0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 package target radius differs.");
        return new Fo3Cg01PostStage14Package(
            RequiredFormId(source, "formId"),
            RequiredString(source, "editorId"),
            RequiredFormId(target, "formId"),
            Fo3Cg01Stage12Transition.LoadTransform(RequiredObject(target, "sourceTransform")),
            radius,
            completionStage);
    }

    private static Fo3Cg01PostStage14Cue LoadCue(
        JsonElement source,
        int sequence,
        Fo3Cg01Stage0Transition stage0)
    {
        if (RequiredInteger(source, "sequence") != sequence ||
            !RequiredBoolean(source, "sayOnce"))
            throw new InvalidOperationException("Fallout 3 CG01 stage-16 cue order differs.");
        var infoFormId = RequiredFormId(source, "infoFormId");
        _ = RequiredSha256(source, "recordSha256");
        string? engineSex = null;
        var conditions = RequiredArray(source, "conditions").EnumerateArray().ToArray();
        foreach (var condition in conditions)
        {
            var function = RequiredInteger(condition, "function");
            if (function == GetPcIsSexFunction)
                engineSex = Convert.ToUInt32(
                    RequiredFormId(condition, "parameter1"),
                    FormIdRadix) switch
                {
                    MaleSexValue => "male",
                    FemaleSexValue => "female",
                    _ => throw new InvalidOperationException(
                        "Fallout 3 CG01 stage-16 cue sex differs."),
                };
        }
        if (!conditions.Any(condition =>
                RequiredInteger(condition, "function") == GetIsIdFunction &&
                RequiredFormId(condition, "parameter1") == stage0.Dad.BaseFormId))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 Dad cue identity differs.");
        var effects = RequiredArray(source, "effects").EnumerateArray().ToArray();
        if (sequence == 0)
        {
            if (effects.Length != 0 || engineSex is not null)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 stage-16 opening cue differs.");
        }
        else if (effects.Length != 1 || engineSex is null ||
            RequiredString(effects[0], "kind") != "setScriptVariable" ||
            RequiredFormId(effects[0], "referenceFormId") != stage0.Dad.FormId ||
            RequiredString(effects[0], "variable") != "doTalk" ||
            RequiredInteger(effects[0], "value") != 0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 closing cue effect differs.");
        var response = RequiredObject(source, "response");
        var responseIndex = RequiredInteger(response, "index");
        var text = RequiredString(response, "text");
        var actualHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        if (responseIndex != 1 || actualHash != RequiredSha256(response, "textSha256"))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 response text differs.");
        var suffix = $"_{infoFormId}_{responseIndex}";
        return new Fo3Cg01PostStage14Cue(
            sequence,
            infoFormId,
            engineSex,
            new Fo3OwnedDialogueResponse(
                responseIndex,
                text,
                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                    RequiredObject(response, "voice"), suffix + ".ogg"),
                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                    RequiredObject(response, "lip"), suffix + ".lip")));
    }

    private static IReadOnlyList<CompiledCommand> LoadCommands(JsonElement source)
    {
        _ = RequiredSha256(source, "sourceSha256");
        return RequiredArray(source, "commands").EnumerateArray().Select((row, index) =>
        {
            if (RequiredInteger(row, "index") != index)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 post-stage-14 command order differs.");
            var kind = RequiredString(row, "kind");
            var reference = row.TryGetProperty("referenceFormId", out var referenceValue)
                ? referenceValue.GetString() ?? ""
                : "";
            var value = row.TryGetProperty("stage", out var stageValue)
                ? stageValue.GetInt32()
                : row.TryGetProperty("objectiveIndex", out var objectiveValue)
                    ? objectiveValue.GetInt32()
                    : row.TryGetProperty("value", out var rawValue) && rawValue.TryGetInt32(out var integer)
                        ? integer
                        : 0;
            var arguments = row.TryGetProperty("arguments", out var rawArguments)
                ? rawArguments.EnumerateArray().Select(item => item.GetInt32()).ToArray()
                : Array.Empty<int>();
            return new CompiledCommand(kind, reference, value, arguments);
        }).ToArray();
    }

    private sealed record CompiledCommand(
        string Kind,
        string ReferenceFormId,
        int Value,
        IReadOnlyList<int> Arguments);

    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is absent.");
    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is absent.");
    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is absent.");
    private static int RequiredInteger(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
    private static bool RequiredBoolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            !value.All(Uri.IsHexDigit))
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
        return value.ToLowerInvariant();
    }
    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            !value.All(Uri.IsHexDigit))
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
        return value.ToLowerInvariant();
    }
}
