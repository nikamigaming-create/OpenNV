using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3PackageAnimationSource(
    string FormId,
    string EditorId,
    string ModelPath,
    string SourceSha256);

internal sealed record Fo3ActivePlayerPackage(
    string FormId,
    string EditorId,
    string LocationReferenceFormId,
    IReadOnlyList<string> IdleFormIds,
    string NextCommand,
    int NextStage);

internal sealed record Fo3PlayerPackageTransition(
    int SourceStage,
    string Command,
    string PackageFormId,
    string PackageEditorId,
    string LocationReferenceFormId,
    IReadOnlyList<string> IdleFormIds,
    IReadOnlyList<Fo3PackageAnimationSource> AnimationSources,
    string NextCommand,
    int NextStage,
    string NextStageSourceSha256,
    string NextStageContractSchema,
    IReadOnlyList<string> NextCommandKinds)
{
    private const string ExpectedSchema = "opennv-fo3-cg00-player-package-transition/v1";
    private const string ExpectedStatus = "source-backed-package-activation";
    private const int ExpectedPackageType = 6;

    internal static Fo3PlayerPackageTransition Load(
        JsonElement source,
        int acceptedStage,
        string acceptedCommand)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus)
            throw new InvalidOperationException("Fallout 3 player-package contract is unsupported.");
        var sourceStage = RequiredInteger(source, "sourceStage");
        var command = RequiredString(source, "command");
        if (sourceStage != acceptedStage || command != acceptedCommand)
            throw new InvalidOperationException(
                "Fallout 3 player-package transition does not join appearance acceptance.");

        var package = RequiredObject(source, "package");
        var packageFormId = RequiredFormId(package, "formId");
        var packageEditorId = RequiredString(package, "editorId");
        if (RequiredInteger(package, "type") != ExpectedPackageType)
            throw new InvalidOperationException("Fallout 3 player-package type is unsupported.");
        _ = RequiredInteger(package, "flags");
        _ = RequiredInteger(package, "procedureFlags");
        _ = RequiredInteger(package, "typeSpecificFlags");

        var location = RequiredObject(package, "location");
        if (RequiredInteger(location, "type") != 0 || RequiredInteger(location, "radius") != 0)
            throw new InvalidOperationException("Fallout 3 player-package location is unsupported.");
        var locationReferenceFormId = RequiredFormId(location, "referenceFormId");
        _ = RequiredString(location, "referenceEditorId");

        var idleSelection = RequiredObject(package, "idleSelection");
        _ = RequiredInteger(idleSelection, "flags");
        if (!RequiredDouble(idleSelection, "timerSeconds", out var idleTimer) ||
            !double.IsFinite(idleTimer))
            throw new InvalidOperationException("Fallout 3 player-package idle timer is invalid.");
        var idleFormIds = RequiredArray(idleSelection, "idles").EnumerateArray()
            .Select(value => RequiredFormId(value, "formId"))
            .ToArray();
        if (idleFormIds.Length == 0 ||
            idleFormIds.Length != RequiredInteger(idleSelection, "count") ||
            idleFormIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != idleFormIds.Length)
            throw new InvalidOperationException("Fallout 3 player-package idle selection is incomplete.");

        var animationSources = RequiredArray(package, "animationSources").EnumerateArray()
            .Select(LoadAnimationSource)
            .ToArray();
        if (!idleFormIds.All(formId => animationSources.Any(value =>
                value.FormId.Equals(formId, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Fallout 3 player-package animation sources are incomplete.");

        var events = RequiredObject(package, "events");
        var eventNames = events.EnumerateObject().Select(value => value.Name).ToHashSet();
        if (!eventNames.SetEquals(new[] { "begin", "end", "change" }))
            throw new InvalidOperationException("Fallout 3 player-package events are incomplete.");
        foreach (var property in events.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
                continue;
            if (property.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Fallout 3 player-package event is invalid.");
            var formId = RequiredFormId(property.Value, "formId");
            if (!animationSources.Any(value =>
                    value.FormId.Equals(formId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "Fallout 3 player-package event animation is absent.");
        }

        var trigger = RequiredObject(source, "nextStageTrigger");
        _ = RequiredString(trigger, "scriptEditorId");
        _ = RequiredFormId(trigger, "scriptFormId");
        _ = RequiredSha256(trigger, "scriptSourceSha256");
        _ = RequiredString(trigger, "condition");
        var thresholdStage = RequiredInteger(trigger, "thresholdStage");
        var nextCommand = RequiredString(trigger, "command");
        var nextStage = RequiredInteger(trigger, "targetStage");
        if (thresholdStage > sourceStage || nextStage <= sourceStage)
            throw new InvalidOperationException("Fallout 3 next CG00 stage is not forward-moving.");

        var result = RequiredObject(source, "nextStageResult");
        if (RequiredInteger(result, "stage") != nextStage || !RequiredBoolean(result, "runtimeReady"))
            throw new InvalidOperationException(
                "Fallout 3 next CG00 stage is not runtime-ready.");
        var nextStageSourceSha256 = RequiredSha256(result, "stageSourceSha256");
        var nextStageContractSchema = RequiredString(result, "contractSchema");
        var commandKinds = RequiredArray(result, "commands").EnumerateArray()
            .Select(value => RequiredString(value, "kind"))
            .ToArray();
        if (commandKinds.Length == 0 || commandKinds.Any(value =>
                value is not "matchRace" and not "matchFaceGeometry"))
            throw new InvalidOperationException(
                "Fallout 3 next CG00 stage contains an unrecognized command.");

        return new Fo3PlayerPackageTransition(
            sourceStage,
            command,
            packageFormId,
            packageEditorId,
            locationReferenceFormId,
            idleFormIds,
            animationSources,
            nextCommand,
            nextStage,
            nextStageSourceSha256,
            nextStageContractSchema,
            commandKinds);
    }

    internal Fo3ActivePlayerPackage Activate() =>
        new(
            PackageFormId,
            PackageEditorId,
            LocationReferenceFormId,
            IdleFormIds,
            NextCommand,
            NextStage);

    internal void ValidateSavedState(JsonElement source)
    {
        if (RequiredString(source, "schema") != "opennv-fo3-player-package-state/v1" ||
            !RequiredBoolean(source, "active") ||
            RequiredFormId(source, "formId") != PackageFormId ||
            RequiredString(source, "editorId") != PackageEditorId ||
            RequiredFormId(source, "locationReferenceFormId") != LocationReferenceFormId ||
            RequiredString(source, "nextCommand") != NextCommand ||
            RequiredInteger(source, "nextStage") != NextStage)
            throw new InvalidOperationException("Saved Fallout 3 player package differs from the profile.");
        var savedIdles = RequiredArray(source, "idleFormIds").EnumerateArray()
            .Select(value => value.GetString() ?? "").ToArray();
        if (savedIdles.Any(string.IsNullOrWhiteSpace) ||
            !IdleFormIds.ToHashSet().SetEquals(savedIdles))
            throw new InvalidOperationException("Saved Fallout 3 player-package idles differ.");
    }

    private static Fo3PackageAnimationSource LoadAnimationSource(JsonElement source) =>
        new(
            RequiredFormId(source, "formId"),
            RequiredString(source, "editorId"),
            RequiredString(source, "modelPath"),
            RequiredSha256(source, "sourceSha256"));

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 player-package field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 player-package field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 player-package field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 player-package field {name} is invalid.");
        return result;
    }

    private static bool RequiredDouble(JsonElement parent, string name, out double result)
    {
        result = 0;
        return parent.TryGetProperty(name, out var value) && value.TryGetDouble(out result);
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 player-package field {name} is invalid.");
        return value.GetBoolean();
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 player-package FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 player-package hash {name} is invalid.");
        return value;
    }
}
