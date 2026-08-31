using System.Security.Cryptography;
using System.Text.Json;


using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg00AuthoredTransform(
    Godot.Vector3 PositionGameUnits,
    Godot.Vector3 RotationRadians,
    float Scale);

internal sealed record Fo3Cg00PlayerStartMarker(
    string FormId,
    string EditorId,
    Fo3Cg00AuthoredTransform AuthoredTransform);

internal sealed record Fo3Cg00SceneParticipant(
    string Role,
    string ReferenceFormId,
    string ReferenceEditorId,
    Fo3Cg00AuthoredTransform ReferenceTransform,
    string StartMarkerFormId,
    string StartMarkerEditorId,
    Fo3Cg00AuthoredTransform StartMarkerTransform);

internal sealed record Fo3Cg00EarlyAsset(
    string LogicalPath,
    string SourcePath,
    string Sha256);

internal sealed record Fo3Cg00PackageSection(
    string Role,
    int Section,
    string PackageFormId,
    string PackageEditorId,
    string IdleFormId,
    int IdleFlags,
    int IdleCount,
    float IdleTimerSeconds,
    string? BeginIdleFormId,
    string? EndIdleFormId,
    string? ChangeIdleFormId,
    string AnimationLogicalPath,
    string AnimationSha256,
    string AnimationSequenceName,
    double AnimationStartSeconds,
    double AnimationStopSeconds,
    int AnimationCycleType,
    Fo3Cg00PackageStageCondition? ActivationCondition);

internal sealed record Fo3Cg00PackageStageCondition(
    string QuestFormId,
    int Stage,
    int FunctionId,
    int OperatorFlags,
    int RunOn);

internal sealed record Fo3Cg00CameraParentTransform(
    string NodeName,
    Godot.Vector3 TranslationGodotGameUnits,
    Godot.Quaternion Rotation,
    float Scale);

internal sealed record Fo3Cg00CameraSample(
    double TimeSeconds,
    Godot.Vector3 TranslationGodotGameUnits,
    Godot.Quaternion Rotation);

internal sealed record Fo3Cg00AnimatedCameraParentTrack(
    int ParentChainIndex,
    string NodeName,
    IReadOnlyList<Fo3Cg00CameraSample> Samples);

internal sealed record Fo3Cg00PlayerCameraTransform(
    int Section,
    string PackageFormId,
    string PackageEditorId,
    string IdleFormId,
    string TargetNode,
    string PlayerStartMarkerFormId,
    Godot.Quaternion PlayerStartMarkerRotation,
    Fo3Cg00EarlyAsset Animation,
    Fo3Cg00EarlyAsset Skeleton,
    string SampleContractSha256,
    string SequenceName,
    double StartSeconds,
    double StopSeconds,
    int CycleType,
    double SamplesPerSecond,
    IReadOnlyList<Fo3Cg00CameraParentTransform> ParentChain,
    IReadOnlyList<Fo3Cg00AnimatedCameraParentTrack> AnimatedParentTracks,
    IReadOnlyList<Fo3Cg00CameraSample> Samples);

internal sealed record Fo3Cg00DialogueCue(
    string InfoFormId,
    string SpeakerRole,
    string Text,
    string TextSha256,
    Fo3Cg00EarlyAsset Voice,
    Fo3Cg00EarlyAsset Lip,
    IReadOnlyList<string> ResultCommands);

internal sealed record Fo3Cg00StageSource(
    int Stage,
    string SourceSha256,
    IReadOnlyList<string> Commands);

internal sealed record Fo3Cg00ImageSpaceFadeKey(
    float NormalizedTime,
    Godot.Color Color);

internal sealed record Fo3Cg00ImageSpaceModifier(
    string EditorId,
    string FormId,
    string RecordSha256,
    double DurationSeconds,
    IReadOnlyList<Fo3Cg00ImageSpaceFadeKey> Fade);

internal sealed record Fo3Cg00EarlyBirthSequence(
    string QuestFormId,
    IReadOnlyDictionary<int, Fo3Cg00StageSource> Stages,
    IReadOnlyDictionary<int, int> TimerTransitions,
    IReadOnlyDictionary<string, Fo3Cg00SceneParticipant> SceneParticipants,
    Fo3Cg00PlayerStartMarker PlayerStartMarker,
    IReadOnlyDictionary<string, IReadOnlyList<Fo3Cg00PackageSection>> PackageSections,
    Fo3Cg00PlayerCameraTransform PlayerCamera,
    IReadOnlyDictionary<string, Fo3Cg00ImageSpaceModifier> ImageSpaceModifiers,
    IReadOnlyDictionary<string, Fo3Cg00EarlyAsset[]> Sounds,
    IReadOnlyList<Fo3Cg00DialogueCue> Stage10Dialogue,
    IReadOnlyDictionary<string, IReadOnlyList<Fo3Cg00DialogueCue>> Stage22Dialogue,
    IReadOnlyList<Fo3Cg00DialogueCue> Stage42Dialogue)
{
    internal const string ExpectedSchema = "opennv-fo3-cg00-early-birth-sequence/v1";
    private const string ExpectedStatus = "source-backed-complete-contract-runtime-pending";
    private const string ExpectedSourceClosureRole = "name-and-race-sex-menu-commands";
    private const int Sha256Characters = 64;
    private const int GetStageFunction = 58;
    private const int EqualOperatorFlags = 0x60;

    internal static Fo3Cg00EarlyBirthSequence Load(JsonElement source)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus ||
            !RequiredBoolean(source, "assetsPrepared"))
            throw new InvalidOperationException("Fallout 3 early CG00 contract is not prepared.");
        var closure = RequiredObject(source, "sourceClosure");
        var unaccounted = RequiredArray(closure, "unaccounted");
        if (RequiredInteger(closure, "unaccountedCount") != 0 ||
            unaccounted.GetArrayLength() != 0 ||
            !RequiredArray(closure, "accounted").EnumerateArray()
                .Select(value => value.GetString())
                .Contains(ExpectedSourceClosureRole, StringComparer.Ordinal))
            throw new InvalidOperationException("Fallout 3 early CG00 source closure is incomplete.");

        var questFormId = RequiredFormId(source, "questFormId");
        var stages = RequiredArray(source, "stages").EnumerateArray()
            .Select(LoadStage).ToDictionary(value => value.Stage);
        if (stages.Count == 0 || stages.Values.Any(value => value.Commands.Count == 0 &&
            value.Stage != stages.Keys.Max() - 1))
            throw new InvalidOperationException("Fallout 3 early CG00 stage rows are incomplete.");
        var timers = RequiredArray(source, "timerTransitions").EnumerateArray()
            .ToDictionary(
                value => RequiredInteger(value, "sourceStage"),
                value => RequiredInteger(value, "targetStage"));
        if (timers.Count == 0 || timers.Any(value => !stages.ContainsKey(value.Key) ||
            !stages.ContainsKey(value.Value)))
            throw new InvalidOperationException("Fallout 3 early CG00 timer joins are incomplete.");

        var participants = RequiredArray(source, "sceneParticipants").EnumerateArray()
            .Select(LoadParticipant).ToDictionary(value => value.Role, StringComparer.Ordinal);
        if (!participants.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(["father", "doctor", "mother"]) ||
            participants.Values.Any(value => value.ReferenceFormId == value.StartMarkerFormId))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 scene participant marker closure differs.");
        var playerMarkerSource = RequiredObject(source, "playerStartMarker");
        var playerMarker = new Fo3Cg00PlayerStartMarker(
            RequiredFormId(playerMarkerSource, "formId"),
            RequiredString(playerMarkerSource, "editorId"),
            LoadTransform(RequiredObject(playerMarkerSource, "authoredTransform")));

        var packageSections = RequiredObject(source, "actorPackageSections")
            .EnumerateObject().ToDictionary(
                property => property.Name,
                property => (IReadOnlyList<Fo3Cg00PackageSection>)property.Value
                    .EnumerateArray()
                    .Select(value => LoadPackageSection(
                        property.Name,
                        value,
                        questFormId)).ToArray(),
                StringComparer.Ordinal);
        if (!packageSections.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(["player", "father", "doctor", "mother"]) ||
            packageSections.Values.Any(rows => rows.Count == 0 ||
                !rows.Select(value => value.Section).SequenceEqual(
                    Enumerable.Range(0, rows.Count))))
            throw new InvalidOperationException("Fallout 3 early CG00 package matrix is incomplete.");
        if (packageSections["player"].Any(value => value.ActivationCondition is not null) ||
            packageSections.Where(value => value.Key != "player").Any(value =>
                value.Value.Any(row => row.ActivationCondition is null)) ||
            packageSections.Where(value => value.Key != "player")
                .Select(value => value.Value.Select(row => row.ActivationCondition!.Stage).ToArray())
                .DistinctBy(value => string.Join(",", value)).Count() != 1)
            throw new InvalidOperationException(
                "Fallout 3 early CG00 package stage-condition matrix differs.");
        if (packageSections.Values.Any(rows => rows.Zip(rows.Skip(1)).Any(value =>
                value.First.ChangeIdleFormId != value.Second.IdleFormId)))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 package change-idle chain differs.");
        var playerCamera = LoadPlayerCamera(RequiredObject(source, "playerCamera"));
        var playerCameraPackage = packageSections["player"].SingleOrDefault(value =>
            value.Section == playerCamera.Section);
        if (playerCameraPackage is null ||
            playerCameraPackage.PackageFormId != playerCamera.PackageFormId ||
            playerCameraPackage.PackageEditorId != playerCamera.PackageEditorId ||
            playerCameraPackage.IdleFormId != playerCamera.IdleFormId ||
            playerCameraPackage.AnimationSha256 != playerCamera.Animation.Sha256 ||
            playerMarker.FormId != playerCamera.PlayerStartMarkerFormId)
            throw new InvalidOperationException(
                "Fallout 3 early CG00 player camera source join differs.");

        var sounds = RequiredArray(source, "sounds").EnumerateArray().ToDictionary(
            value => RequiredString(value, "editorId"),
            value => RequiredArray(value, "preparedSources").EnumerateArray()
                .Select(LoadAsset).ToArray(),
            StringComparer.Ordinal);
        if (sounds.Count == 0 || sounds.Values.Any(value => value.Length == 0))
            throw new InvalidOperationException("Fallout 3 early CG00 sounds are incomplete.");
        var imageSpaceModifiers = RequiredArray(source, "imageSpaceModifiers")
            .EnumerateArray()
            .Select(LoadImageSpaceModifier)
            .ToDictionary(value => value.EditorId, StringComparer.Ordinal);
        var commandedImageSpaceModifiers = stages.Values
            .SelectMany(value => value.Commands)
            .Where(value => value.StartsWith("imod ", StringComparison.OrdinalIgnoreCase))
            .Select(value => value["imod ".Length..])
            .ToHashSet(StringComparer.Ordinal);
        if (!imageSpaceModifiers.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(commandedImageSpaceModifiers))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 image-space source closure differs.");
        var dialogue = RequiredObject(source, "dialogue");
        var stage22 = RequiredObject(dialogue, "stage22").EnumerateObject().ToDictionary(
            property => property.Name,
            property => (IReadOnlyList<Fo3Cg00DialogueCue>)property.Value
                .EnumerateArray().Select(LoadCue).ToArray(),
            StringComparer.Ordinal);
        if (!stage22.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(["male", "female"]) ||
            stage22.Values.Any(value => value.Count == 0))
            throw new InvalidOperationException("Fallout 3 early CG00 sex dialogue differs.");
        return new Fo3Cg00EarlyBirthSequence(
            questFormId,
            stages,
            timers,
            participants,
            playerMarker,
            packageSections,
            playerCamera,
            imageSpaceModifiers,
            sounds,
            RequiredArray(dialogue, "stage10").EnumerateArray().Select(LoadCue).ToArray(),
            stage22,
            RequiredArray(dialogue, "stage42").EnumerateArray().Select(LoadCue).ToArray());
    }

    internal int StageWithCommand(string exactCommand) => Stages.Values.Single(value =>
        value.Commands.Contains(exactCommand, StringComparer.OrdinalIgnoreCase)).Stage;

    internal double TimerSeconds(int stage)
    {
        var command = Stages[stage].Commands.Single(value =>
            value.StartsWith("set CG00.timer to ", StringComparison.OrdinalIgnoreCase));
        var token = command["set CG00.timer to ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Fo3Cg00StageSource LoadStage(JsonElement source)
    {
        var commands = RequiredArray(source, "commands").EnumerateArray()
            .Select(value => value.GetString() ?? "").ToArray();
        if (commands.Length != RequiredInteger(source, "commandCount") ||
            commands.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Fallout 3 early CG00 stage commands differ.");
        return new Fo3Cg00StageSource(
            RequiredInteger(source, "stage"),
            RequiredSha256(source, "sourceSha256"),
            commands);
    }

    private static Fo3Cg00PackageSection LoadPackageSection(
        string role,
        JsonElement source,
        string questFormId)
    {
        var animation = RequiredObject(source, "animationSource");
        var selection = RequiredObject(source, "idleSelection");
        var events = RequiredObject(source, "events");
        var playback = RequiredObject(source, "animationPlayback");
        var idleCount = RequiredInteger(selection, "count");
        var idleTimer = RequiredSingle(selection, "timerSeconds");
        var animationStart = RequiredDouble(playback, "startSeconds");
        var animationStop = RequiredDouble(playback, "stopSeconds");
        if (idleCount <= 0 || !float.IsFinite(idleTimer) ||
            animationStart != 0.0 || animationStop <= animationStart)
            throw new InvalidOperationException(
                "Fallout 3 early CG00 package playback clock differs.");
        Fo3Cg00PackageStageCondition? activationCondition = null;
        if (source.TryGetProperty("activationCondition", out var activationSource) &&
            activationSource.ValueKind != JsonValueKind.Null)
        {
            if (RequiredString(activationSource, "function") != "GetStage" ||
                RequiredString(activationSource, "operator") != "equal")
                throw new InvalidOperationException(
                    "Fallout 3 early CG00 package activation operator differs.");
            activationCondition = new Fo3Cg00PackageStageCondition(
                RequiredFormId(activationSource, "questFormId"),
                RequiredInteger(activationSource, "stage"),
                RequiredInteger(activationSource, "functionId"),
                RequiredInteger(activationSource, "operatorFlags"),
                RequiredInteger(activationSource, "runOn"));
            if (activationCondition.QuestFormId != questFormId ||
                activationCondition.FunctionId != GetStageFunction ||
                activationCondition.OperatorFlags != EqualOperatorFlags ||
                activationCondition.RunOn != 0 ||
                activationCondition.Stage < 0)
                throw new InvalidOperationException(
                    "Fallout 3 early CG00 package activation condition differs.");
        }
        return new Fo3Cg00PackageSection(
            role,
            RequiredInteger(source, "section"),
            RequiredFormId(source, "packageFormId"),
            RequiredString(source, "packageEditorId"),
            RequiredFormId(source, "idleFormId"),
            RequiredInteger(selection, "flags"),
            idleCount,
            idleTimer,
            OptionalFormId(events, "begin"),
            OptionalFormId(events, "end"),
            OptionalFormId(events, "change"),
            RequiredString(source, "animationLogicalPath"),
            RequiredSha256(animation, "sourceSha256"),
            RequiredString(playback, "sequenceName"),
            animationStart,
            animationStop,
            RequiredInteger(playback, "cycleType"),
            activationCondition);
    }

    private static string? OptionalFormId(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var value))
            throw new InvalidOperationException(
                $"Required Fallout 3 early CG00 package event is absent: {propertyName}");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException(
                $"Fallout 3 early CG00 package event differs: {propertyName}");
        var formId = value.GetString() ?? "";
        if (formId.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            formId.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Fallout 3 early CG00 package event FormID differs: {propertyName}");
        return formId;
    }

    private static Fo3Cg00PlayerCameraTransform LoadPlayerCamera(JsonElement source)
    {
        if (RequiredString(source, "schema") !=
                "opennv-fo3-cg00-player-camera-transform/v1" ||
            RequiredString(source, "status") !=
                "source-backed-sampled-player-camera-root-transform")
            throw new InvalidOperationException(
                "Fallout 3 early CG00 player camera contract differs.");
        var track = RequiredObject(source, "track");
        var parentChain = RequiredArray(track, "parentChain").EnumerateArray()
            .Select(value => new Fo3Cg00CameraParentTransform(
                RequiredString(value, "nodeName"),
                RequiredVector3(value, "translationGodotGameUnits"),
                RequiredQuaternion(value, "rotationQuaternionXyzw"),
                RequiredUniformScale(value, "scale")))
            .ToArray();
        var samples = LoadCameraSamples(RequiredArray(track, "samples"));
        var animatedParentTracks = RequiredArray(track, "animatedParentTracks")
            .EnumerateArray()
            .Select(value => new Fo3Cg00AnimatedCameraParentTrack(
                RequiredInteger(value, "parentChainIndex"),
                RequiredString(value, "nodeName"),
                LoadCameraSamples(RequiredArray(value, "samples"))))
            .ToArray();
        var start = RequiredDouble(track, "startSeconds");
        var stop = RequiredDouble(track, "stopSeconds");
        var samplesPerSecond = RequiredDouble(track, "samplesPerSecond");
        if (parentChain.Length == 0 || samples.Length < 2 || start != 0.0 ||
            stop <= start || samplesPerSecond <= 0.0 ||
            samples[0].TimeSeconds != start || samples[^1].TimeSeconds != stop ||
            samples.Zip(samples.Skip(1)).Any(value =>
                value.First.TimeSeconds >= value.Second.TimeSeconds) ||
            animatedParentTracks.Length == 0 ||
            animatedParentTracks.Select(value => value.ParentChainIndex).Distinct().Count() !=
                animatedParentTracks.Length ||
            animatedParentTracks.Any(value =>
                value.ParentChainIndex < 0 ||
                value.ParentChainIndex >= parentChain.Length ||
                parentChain[value.ParentChainIndex].NodeName != value.NodeName ||
                value.Samples.Count != samples.Length ||
                !value.Samples.Select(sample => sample.TimeSeconds)
                    .SequenceEqual(samples.Select(sample => sample.TimeSeconds))) ||
            RequiredString(track, "targetNode") != RequiredString(source, "targetNode"))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 player camera samples differ.");
        return new Fo3Cg00PlayerCameraTransform(
            RequiredInteger(source, "section"),
            RequiredFormId(source, "packageFormId"),
            RequiredString(source, "packageEditorId"),
            RequiredFormId(source, "idleFormId"),
            RequiredString(source, "targetNode"),
            RequiredFormId(source, "playerStartMarkerFormId"),
            RequiredQuaternion(source, "playerStartMarkerRotationGodotQuaternion"),
            LoadAsset(RequiredObject(source, "animation")),
            LoadAsset(RequiredObject(source, "skeleton")),
            RequiredSha256(source, "sampleContractSha256"),
            RequiredString(track, "sequenceName"),
            start,
            stop,
            RequiredInteger(track, "cycleType"),
            samplesPerSecond,
            parentChain,
            animatedParentTracks,
            samples);
    }

    private static Fo3Cg00CameraSample[] LoadCameraSamples(JsonElement source) =>
        source.EnumerateArray()
            .Select(value => new Fo3Cg00CameraSample(
                RequiredDouble(value, "timeSeconds"),
                RequiredVector3(value, "translationGodotGameUnits"),
                RequiredQuaternion(value, "rotationQuaternionXyzw")))
            .ToArray();

    private static Fo3Cg00ImageSpaceModifier LoadImageSpaceModifier(JsonElement source)
    {
        var parameters = RequiredObject(source, "parameters");
        var duration = RequiredDouble(parameters, "duration");
        var fade = RequiredArray(parameters, "fade").EnumerateArray()
            .Select(LoadImageSpaceFadeKey)
            .ToArray();
        if (duration <= 0.0 || fade.Length < 2 || fade[0].NormalizedTime != 0.0f ||
            fade[^1].NormalizedTime != 1.0f ||
            fade.Zip(fade.Skip(1)).Any(value =>
                value.First.NormalizedTime >= value.Second.NormalizedTime))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 image-space fade curve differs.");
        return new Fo3Cg00ImageSpaceModifier(
            RequiredString(source, "editorId"),
            RequiredFormId(source, "formId"),
            RequiredSha256(source, "recordSha256"),
            duration,
            fade);
    }

    private static Fo3Cg00ImageSpaceFadeKey LoadImageSpaceFadeKey(JsonElement source)
    {
        var values = source.ValueKind == JsonValueKind.Array
            ? source.EnumerateArray().Select(value => value.GetSingle()).ToArray()
            : [];
        if (values.Length != GamebryoCoordinate.SpatialDimensions + 2 ||
            values.Any(value => !float.IsFinite(value)) ||
            values.Any(value => value < 0.0f || value > 1.0f))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 image-space fade key differs.");
        return new Fo3Cg00ImageSpaceFadeKey(
            values[0],
            new Godot.Color(values[1], values[2], values[3], values[4]));
    }

    private static Fo3Cg00SceneParticipant LoadParticipant(JsonElement source)
    {
        var reference = RequiredObject(source, "reference");
        var marker = RequiredObject(source, "startMarker");
        return new Fo3Cg00SceneParticipant(
            RequiredString(source, "role"),
            RequiredFormId(reference, "formId"),
            RequiredString(reference, "editorId"),
            LoadTransform(RequiredObject(reference, "authoredTransform")),
            RequiredFormId(marker, "formId"),
            RequiredString(marker, "editorId"),
            LoadTransform(RequiredObject(marker, "authoredTransform")));
    }

    private static Fo3Cg00AuthoredTransform LoadTransform(JsonElement source)
    {
        var position = RequiredVector3(source, "positionGameUnits");
        var rotation = RequiredVector3(source, "rotationRadians");
        var scale = RequiredSingle(source, "scale");
        if (!position.IsFinite() || !rotation.IsFinite() || !float.IsFinite(scale) || scale <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 3 early CG00 participant transform is invalid.");
        return new Fo3Cg00AuthoredTransform(position, rotation, scale);
    }

    private static Fo3Cg00DialogueCue LoadCue(JsonElement source)
    {
        var response = RequiredObject(source, "response");
        var audio = RequiredObject(source, "preparedAudio");
        return new Fo3Cg00DialogueCue(
            RequiredFormId(source, "infoFormId"),
            RequiredString(source, "speakerRole"),
            RequiredString(response, "text"),
            RequiredSha256(response, "textSha256"),
            LoadAsset(RequiredObject(audio, "voice")),
            LoadAsset(RequiredObject(audio, "lip")),
            RequiredArray(source, "resultCommands").EnumerateArray()
                .Select(value => value.GetString() ?? "").Where(value => value.Length > 0).ToArray());
    }

    private static Fo3Cg00EarlyAsset LoadAsset(JsonElement source)
    {
        var path = Path.GetFullPath(RequiredString(source, "source"));
        var expected = RequiredSha256(source, "sha256");
        if (!File.Exists(path))
            throw new InvalidOperationException("Fallout 3 early CG00 owned asset is absent.");
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 early CG00 owned asset changed.");
        return new Fo3Cg00EarlyAsset(RequiredString(source, "logicalPath"), path, actual);
    }

    private static string RequiredFormId(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Fallout 3 early CG00 FormID is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        if (value.Length != Sha256Characters || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Fallout 3 early CG00 hash is invalid.");
        return value;
    }

    private static JsonElement RequiredObject(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidOperationException($"Fallout 3 early CG00 object {name} is absent.");

    private static JsonElement RequiredArray(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidOperationException($"Fallout 3 early CG00 array {name} is absent.");

    private static string RequiredString(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"Fallout 3 early CG00 string {name} is absent.");

    private static int RequiredInteger(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException($"Fallout 3 early CG00 integer {name} is absent.");

    private static float RequiredSingle(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.TryGetSingle(out var result)
            ? result
            : throw new InvalidOperationException($"Fallout 3 early CG00 number {name} is absent.");

    private static double RequiredDouble(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) &&
        double.IsFinite(result)
            ? result
            : throw new InvalidOperationException(
                $"Fallout 3 early CG00 number {name} is absent.");

    private static float RequiredUniformScale(JsonElement source, string name)
    {
        var value = RequiredVector3(source, name);
        return value.X > 0.0f && Godot.Mathf.IsEqualApprox(value.X, value.Y) &&
            Godot.Mathf.IsEqualApprox(value.X, value.Z)
            ? value.X
            : throw new InvalidOperationException(
                $"Fallout 3 early CG00 scale {name} is invalid.");
    }

    private static Godot.Quaternion RequiredQuaternion(JsonElement source, string name)
    {
        var values = RequiredArray(source, name).EnumerateArray()
            .Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                $"Fallout 3 early CG00 quaternion {name} differs.");
        var result = new Godot.Quaternion(values[0], values[1], values[2], values[3]);
        if (!Godot.Mathf.IsEqualApprox(result.LengthSquared(), 1.0f))
            throw new InvalidOperationException(
                $"Fallout 3 early CG00 quaternion {name} is not normalized.");
        return result;
    }

    private static Godot.Vector3 RequiredVector3(JsonElement source, string name)
    {
        var values = RequiredArray(source, name).EnumerateArray()
            .Select(value => value.GetSingle()).ToArray();
        if (values.Length != GamebryoCoordinate.SpatialDimensions)
            throw new InvalidOperationException(
                $"Fallout 3 early CG00 vector {name} differs.");
        return new Godot.Vector3(values[0], values[1], values[2]);
    }

    private static bool RequiredBoolean(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : throw new InvalidOperationException($"Fallout 3 early CG00 boolean {name} is absent.");
}
