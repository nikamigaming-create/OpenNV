using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg01Vector3(double X, double Y, double Z);

internal sealed record Fo3Cg01Transform(
    Fo3Cg01Vector3 PositionGameUnits,
    Fo3Cg01Vector3 RotationRadians,
    double Scale);

internal sealed record Fo3Cg01Reference(
    string RecordType,
    string FormId,
    string EditorId,
    string RecordSha256,
    string BaseRecordType,
    string BaseFormId,
    string BaseEditorId,
    string BaseRecordSha256,
    string CellFormId,
    int Flags,
    bool InitiallyDisabled,
    Fo3Cg01Transform SourceTransform);

internal sealed record Fo3Cg01ScriptVariable(
    string ReferenceFormId,
    string ReferenceEditorId,
    string ScriptFormId,
    string ScriptEditorId,
    string ScriptRecordSha256,
    string ScriptSourceSha256,
    string Variable,
    int Value);

internal sealed record Fo3Cg01Sound(
    string FormId,
    string EditorId,
    string RecordSha256,
    string SoundDataSha256,
    string LogicalPath,
    string SelectionPolicy);

internal sealed record Fo3Cg01OwnedMovie(
    string LogicalPath,
    IReadOnlyList<int> Arguments,
    string File,
    string Source,
    long Bytes,
    string Sha256,
    string RuntimeOutput,
    long RuntimeOutputBytes,
    string RuntimeOutputSha256);

internal sealed record Fo3Cg01ActorState(
    Fo3Cg01Reference Reference,
    string? MoveTargetFormId,
    Fo3Cg01Transform Transform,
    bool Enabled,
    IReadOnlyList<Fo3Cg01ScriptVariable> ScriptVariables);

internal sealed record Fo3Cg01PlayerState(
    string MoveTargetFormId,
    Fo3Cg01Transform Transform,
    double Scale,
    bool Toddler,
    bool Young);

internal sealed record Fo3Cg01Boundary(
    bool Applied,
    string Blocker);

internal sealed record Fo3Cg01Stage0State(
    int SourceStage,
    bool Cg00BoundaryApplied,
    string ActiveQuestFormId,
    string ActiveQuestEditorId,
    int ActiveStage,
    int AccountedCommandCount,
    int AppliedCommandCount,
    IReadOnlyList<string> AppliedExecutionTrace,
    Fo3Cg01ActorState Dad,
    Fo3Cg01ActorState NextDad,
    Fo3Cg01PlayerState Player,
    bool LocationSpecificLoadScreensOnly,
    bool InCharacterGeneration,
    bool AutoDisplayObjectives,
    IReadOnlyList<int> EnabledPlayerControls,
    IReadOnlyList<int> DisabledPlayerControls,
    Fo3Cg01Sound NoActivationSound,
    Fo3Cg01OwnedMovie TransitionMovie,
    int TransitionMovieRequestCount,
    bool TransitionMovieReplayOnRestore,
    Fo3Cg01Boundary NextBoundary);

internal sealed record Fo3Cg01Stage0Transition(
    string QuestFormId,
    string QuestEditorId,
    string QuestRecordSha256,
    string QuestScriptFormId,
    string QuestScriptEditorId,
    string QuestScriptRecordSha256,
    string QuestScriptSourceSha256,
    string CellFormId,
    int EntryStage,
    int ResultingStage,
    string Stage0SourceSha256,
    string Stage5SourceSha256,
    Fo3Cg01Reference Dad,
    Fo3Cg01Reference DadStartMarker,
    Fo3Cg01Reference PlayerStartMarker,
    Fo3Cg01Reference NextDad,
    IReadOnlyList<Fo3Cg01ScriptVariable> DadVariables,
    IReadOnlyList<int> EnabledPlayerControls,
    IReadOnlyList<int> DisabledPlayerControls,
    Fo3Cg01Sound NoActivationSound,
    Fo3Cg01OwnedMovie TransitionMovie)
{
    internal const string ExpectedSchema = "opennv-fo3-cg01-stage-0-to-5-transition/v1";
    internal const string ExpectedSavedStateSchema =
        "opennv-fo3-cg01-stage-0-to-5-state/v1";
    internal const string NextBoundaryBlocker =
        "fo3-cg01-post-stage-5-world-ai-not-implemented";

    private const string ExpectedStatus = "source-backed-nested-stage-result-runtime-unapplied";
    private const string ExpectedSourceBoundaryBlocker =
        "fo3-cg01-stage-0-runtime-application-not-implemented";
    private const string ExpectedQuestFormId = "00014e83";
    private const string ExpectedQuestEditorId = "CG01";
    private const string ExpectedCellFormId = "00028138";
    private const string ExpectedDadFormId = "0002ea4d";
    private const string ExpectedDadMarkerFormId = "0002ea4e";
    private const string ExpectedPlayerMarkerFormId = "0002ea4f";
    private const string ExpectedNextDadFormId = "000300ef";
    private const string ExpectedNoActivationSoundFormId = "00089b4c";
    private const string ExpectedMoviePath = "1 year later.bik";
    private const int ExpectedEntryStage = 0;
    private const int ExpectedResultingStage = 5;
    private const int ExpectedStage0CommandCount = 4;
    private const int ExpectedStage5CommandCount = 13;
    private const int ExpectedAccountedCommandCount =
        ExpectedStage0CommandCount + ExpectedStage5CommandCount;
    private const double ExpectedPlayerScale = 0.4;

    private static readonly int[] ExpectedEnabledPlayerControls = [0, 0, 0, 0, 1];
    private static readonly int[] ExpectedDisabledPlayerControls = [1, 1, 1, 1, 0, 0, 1];
    private static readonly int[] ExpectedMovieArguments = [0, 0, 1, 0];
    private static readonly string[] ExpectedStage0Kinds =
        ["moveToReference", "setStage", "setPlayerScale", "moveToReference"];
    private static readonly string[] ExpectedStage5Kinds =
    [
        "setLocationSpecificLoadScreensOnly",
        "setInCharGen",
        "enable",
        "enable",
        "setScriptVariable",
        "setScriptVariable",
        "enablePlayerControls",
        "disablePlayerControls",
        "autoDisplayObjectives",
        "setNoActivationSound",
        "setPlayerToddler",
        "setPlayerYoung",
        "playBink",
    ];

    internal static Fo3Cg01Stage0Transition Load(
        JsonElement source,
        Fo3Stage100Transition stage100)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus)
            throw new InvalidOperationException("Fallout 3 CG01 transition identity differs.");
        var sourceSha256 = CanonicalSha256(source);
        if (stage100.NextBoundary.TransitionContract.Schema != ExpectedSchema ||
            stage100.NextBoundary.TransitionContract.Sha256 != sourceSha256)
            throw new InvalidOperationException(
                "Fallout 3 CG01 canonical transition identity differs.");

        var trigger = RequiredObject(source, "trigger");
        if (RequiredString(trigger, "sourceSchema") != Fo3Stage100Transition.ExpectedSchema ||
            RequiredInteger(trigger, "commandIndex") != 7 ||
            stage100.Stage != 100 ||
            stage100.NextBoundary.Applied ||
            stage100.NextBoundary.QuestFormId != ExpectedQuestFormId ||
            stage100.NextBoundary.QuestEditorId != ExpectedQuestEditorId ||
            stage100.NextBoundary.Stage != ExpectedEntryStage)
            throw new InvalidOperationException("Fallout 3 CG01 trigger boundary differs.");

        var quest = RequiredObject(source, "quest");
        var questFormId = RequiredFormId(quest, "formId");
        var questEditorId = RequiredString(quest, "editorId");
        if (questFormId != ExpectedQuestFormId || questEditorId != ExpectedQuestEditorId)
            throw new InvalidOperationException("Fallout 3 CG01 quest differs.");
        var questRecordSha256 = RequiredSha256(quest, "recordSha256");
        var questScriptFormId = RequiredFormId(quest, "scriptFormId");
        var questScriptEditorId = RequiredString(quest, "scriptEditorId");
        var questScriptRecordSha256 = RequiredSha256(quest, "scriptRecordSha256");
        var questScriptSourceSha256 = RequiredSha256(quest, "scriptSourceSha256");

        var cellFormId = RequiredFormId(source, "cellFormId");
        var entryStage = RequiredInteger(source, "entryStage");
        var resultingStage = RequiredInteger(source, "resultingStage");
        if (cellFormId != ExpectedCellFormId ||
            entryStage != ExpectedEntryStage ||
            resultingStage != ExpectedResultingStage ||
            RequiredInteger(source, "accountedCommandCount") != ExpectedAccountedCommandCount)
            throw new InvalidOperationException("Fallout 3 CG01 stage join differs.");

        var stage0 = RequiredObject(source, "stage0Result");
        var stage0SourceSha256 = RequiredSha256(stage0, "stageSourceSha256");
        if (RequiredInteger(stage0, "stage") != entryStage ||
            RequiredInteger(stage0, "accountedCommandCount") != ExpectedStage0CommandCount ||
            stage100.NextBoundary.StageResultSourceSha256 != stage0SourceSha256 ||
            stage100.NextBoundary.StageResultCommandCount != ExpectedStage0CommandCount)
            throw new InvalidOperationException("Fallout 3 CG01 stage-0 source join differs.");
        var stage0Commands = OrderedCommands(stage0, ExpectedStage0Kinds, "stage-0");

        var dad = LoadReference(
            RequiredObject(stage0Commands[0], "subject"),
            "ACHR",
            ExpectedDadFormId,
            initiallyDisabled: true);
        var dadMarker = LoadReference(
            RequiredObject(stage0Commands[0], "target"),
            "REFR",
            ExpectedDadMarkerFormId,
            initiallyDisabled: false);

        var setStage = stage0Commands[1];
        if (RequiredFormId(setStage, "questFormId") != questFormId ||
            RequiredString(setStage, "questEditorId") != questEditorId ||
            RequiredInteger(setStage, "stage") != resultingStage)
            throw new InvalidOperationException("Fallout 3 CG01 nested stage command differs.");
        var stage5 = RequiredObject(setStage, "stageResult");
        var stage5SourceSha256 = RequiredSha256(stage5, "stageSourceSha256");
        if (RequiredString(stage5, "schema") != "opennv-fo3-cg01-stage-5-result/v1" ||
            RequiredFormId(stage5, "questFormId") != questFormId ||
            RequiredString(stage5, "questEditorId") != questEditorId ||
            RequiredInteger(stage5, "stage") != resultingStage ||
            RequiredInteger(stage5, "accountedCommandCount") != ExpectedStage5CommandCount)
            throw new InvalidOperationException("Fallout 3 CG01 stage-5 result differs.");
        var stage5Commands = OrderedCommands(stage5, ExpectedStage5Kinds, "stage-5");

        var playerScale = RequiredDouble(stage0Commands[2], "value");
        if (playerScale != ExpectedPlayerScale ||
            RequiredString(RequiredObject(stage0Commands[3], "subject"), "role") != "player")
            throw new InvalidOperationException("Fallout 3 CG01 player command differs.");
        var playerMarker = LoadReference(
            RequiredObject(stage0Commands[3], "target"),
            "REFR",
            ExpectedPlayerMarkerFormId,
            initiallyDisabled: false);

        RequireValueCommand(stage5Commands[0], 1, "location-specific load screens");
        RequireValueCommand(stage5Commands[1], 1, "character generation");
        var enabledDad = LoadReference(
            RequiredObject(stage5Commands[2], "reference"),
            "ACHR",
            ExpectedDadFormId,
            initiallyDisabled: true);
        var nextDad = LoadReference(
            RequiredObject(stage5Commands[3], "reference"),
            "ACHR",
            ExpectedNextDadFormId,
            initiallyDisabled: true);
        RequireSameReference(dad, enabledDad, "moved and enabled Dad");

        var dadVariables = new[]
        {
            LoadVariable(stage5Commands[4], dad, "doTalk", 1),
            LoadVariable(stage5Commands[5], dad, "talking", 0),
        };
        var enabledControls = RequiredIntegerArray(stage5Commands[6], "arguments");
        var disabledControls = RequiredIntegerArray(stage5Commands[7], "arguments");
        RequireSequence(enabledControls, ExpectedEnabledPlayerControls, "enabled-control mask");
        RequireSequence(disabledControls, ExpectedDisabledPlayerControls, "disabled-control mask");
        RequireValueCommand(stage5Commands[8], 1, "automatic objectives");
        var sound = LoadSound(RequiredObject(stage5Commands[9], "sound"));
        RequireValueCommand(stage5Commands[10], 1, "player toddler");
        RequireValueCommand(stage5Commands[11], 1, "player young");

        var movieCommand = stage5Commands[12];
        var moviePath = RequiredString(movieCommand, "logicalPath");
        var movieArguments = RequiredIntegerArray(movieCommand, "arguments");
        if (!moviePath.Equals(ExpectedMoviePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 CG01 transition movie differs.");
        RequireSequence(movieArguments, ExpectedMovieArguments, "movie arguments");
        var movie = LoadOwnedMovie(
            RequiredObject(movieCommand, "video"),
            moviePath,
            movieArguments);

        var nested = RequiredObject(source, "nestedExecution");
        if (RequiredInteger(nested, "stage0CommandIndex") != 1 ||
            RequiredInteger(nested, "stage") != resultingStage ||
            RequiredString(nested, "resultSchema") != "opennv-fo3-cg01-stage-5-result/v1")
            throw new InvalidOperationException("Fallout 3 CG01 nested execution differs.");
        var boundary = RequiredObject(source, "nextBoundary");
        if (RequiredBoolean(boundary, "applied") ||
            RequiredString(boundary, "blocker") != ExpectedSourceBoundaryBlocker)
            throw new InvalidOperationException("Fallout 3 CG01 source boundary differs.");

        return new Fo3Cg01Stage0Transition(
            questFormId,
            questEditorId,
            questRecordSha256,
            questScriptFormId,
            questScriptEditorId,
            questScriptRecordSha256,
            questScriptSourceSha256,
            cellFormId,
            entryStage,
            resultingStage,
            stage0SourceSha256,
            stage5SourceSha256,
            dad,
            dadMarker,
            playerMarker,
            nextDad,
            dadVariables,
            enabledControls,
            disabledControls,
            sound,
            movie);
    }

    internal Fo3Cg01Stage0State Apply(Fo3Stage100State stage100)
    {
        if (stage100.Stage != 100 ||
            stage100.AccountedCommandCount != 8 ||
            stage100.AppliedCommandCount != 7 ||
            stage100.TimerAdvancing ||
            stage100.PlayerScriptPackageActive ||
            stage100.Cg00Running ||
            !stage100.PlayerYoung ||
            stage100.NextBoundary.Applied ||
            stage100.NextBoundary.QuestFormId != QuestFormId ||
            stage100.NextBoundary.QuestEditorId != QuestEditorId ||
            stage100.NextBoundary.Stage != EntryStage ||
            stage100.NextBoundary.StageResultSourceSha256 != Stage0SourceSha256 ||
            stage100.NextBoundary.StageResultCommandCount != ExpectedStage0CommandCount)
            throw new InvalidOperationException("Fallout 3 CG01 runtime trigger state differs.");

        var execution = new List<(string Label, GamebryoStageCommandKind Kind)>
        {
            ("s0:0:moveToReference", GamebryoStageCommandKind.MoveToReference),
            ("s0:1:setStage", GamebryoStageCommandKind.SetStage),
        };
        execution.AddRange(ExpectedStage5Kinds.Select((kind, index) =>
            ($"s5:{index}:{kind}", Cg01StageCommandKind(kind))));
        execution.Add(("s0:2:setPlayerScale", GamebryoStageCommandKind.SetPlayerScale));
        execution.Add(("s0:3:moveToReference", GamebryoStageCommandKind.MoveToReference));
        if (execution.Count != ExpectedAccountedCommandCount)
            throw new InvalidOperationException("Fallout 3 CG01 execution trace differs.");
        var trace = new List<string>(ExpectedAccountedCommandCount);
        GamebryoStageCommandExecutor.ExecuteAll(
            execution.Select((command, sourceIndex) =>
                new SourceGamebryoStageCommand<string>(
                    sourceIndex,
                    command.Kind,
                    command.Label)).ToArray(),
            command =>
            {
                trace.Add(command.Value);
                return trace.Count == command.SourceIndex + 1;
            });

        return new Fo3Cg01Stage0State(
            stage100.Stage,
            true,
            QuestFormId,
            QuestEditorId,
            ResultingStage,
            ExpectedAccountedCommandCount,
            ExpectedAccountedCommandCount,
            trace,
            new Fo3Cg01ActorState(
                Dad,
                DadStartMarker.FormId,
                DadStartMarker.SourceTransform,
                true,
                DadVariables),
            new Fo3Cg01ActorState(
                NextDad,
                null,
                NextDad.SourceTransform,
                true,
                Array.Empty<Fo3Cg01ScriptVariable>()),
            new Fo3Cg01PlayerState(
                PlayerStartMarker.FormId,
                PlayerStartMarker.SourceTransform,
                ExpectedPlayerScale,
                true,
                true),
            true,
            true,
            true,
            EnabledPlayerControls,
            DisabledPlayerControls,
            NoActivationSound,
            TransitionMovie,
            1,
            false,
            new Fo3Cg01Boundary(false, NextBoundaryBlocker));
    }

    private static GamebryoStageCommandKind Cg01StageCommandKind(string kind) => kind switch
    {
        "setLocationSpecificLoadScreensOnly" =>
            GamebryoStageCommandKind.SetLocationSpecificLoadScreensOnly,
        "setInCharGen" => GamebryoStageCommandKind.SetInCharacterGeneration,
        "enable" => GamebryoStageCommandKind.Enable,
        "setScriptVariable" => GamebryoStageCommandKind.SetScriptVariable,
        "enablePlayerControls" => GamebryoStageCommandKind.PlayerControls,
        "disablePlayerControls" => GamebryoStageCommandKind.PlayerControls,
        "autoDisplayObjectives" => GamebryoStageCommandKind.AutoDisplayObjectives,
        "setNoActivationSound" => GamebryoStageCommandKind.SetNoActivationSound,
        "setPlayerToddler" => GamebryoStageCommandKind.SetPlayerToddler,
        "setPlayerYoung" => GamebryoStageCommandKind.SetPlayerYoung,
        "playBink" => GamebryoStageCommandKind.PlayMovie,
        _ => throw new InvalidOperationException(
            $"Fallout 3 CG01 stage command is unsupported: {kind}"),
    };

    internal object SavedState(Fo3Cg01Stage0State state) => new
    {
        schema = ExpectedSavedStateSchema,
        sourceStage = state.SourceStage,
        cg00BoundaryApplied = state.Cg00BoundaryApplied,
        activeQuest = new
        {
            formId = state.ActiveQuestFormId,
            editorId = state.ActiveQuestEditorId,
            stage = state.ActiveStage,
        },
        accountedCommandCount = state.AccountedCommandCount,
        appliedCommandCount = state.AppliedCommandCount,
        appliedExecutionTrace = state.AppliedExecutionTrace,
        dad = SavedActor(state.Dad),
        nextDad = SavedActor(state.NextDad),
        player = new
        {
            moveTargetFormId = state.Player.MoveTargetFormId,
            transform = SavedTransform(state.Player.Transform),
            scale = state.Player.Scale,
            toddler = state.Player.Toddler,
            young = state.Player.Young,
        },
        locationSpecificLoadScreensOnly = state.LocationSpecificLoadScreensOnly,
        inCharacterGeneration = state.InCharacterGeneration,
        autoDisplayObjectives = state.AutoDisplayObjectives,
        enabledPlayerControls = state.EnabledPlayerControls,
        disabledPlayerControls = state.DisabledPlayerControls,
        noActivationSound = new
        {
            formId = state.NoActivationSound.FormId,
            editorId = state.NoActivationSound.EditorId,
            recordSha256 = state.NoActivationSound.RecordSha256,
            soundDataSha256 = state.NoActivationSound.SoundDataSha256,
            logicalPath = state.NoActivationSound.LogicalPath,
            selectionPolicy = state.NoActivationSound.SelectionPolicy,
        },
        transitionMovie = new
        {
            logicalPath = state.TransitionMovie.LogicalPath,
            arguments = state.TransitionMovie.Arguments,
            file = state.TransitionMovie.File,
            source = state.TransitionMovie.Source,
            bytes = state.TransitionMovie.Bytes,
            sha256 = state.TransitionMovie.Sha256,
            runtimeOutput = state.TransitionMovie.RuntimeOutput,
            runtimeOutputBytes = state.TransitionMovie.RuntimeOutputBytes,
            runtimeOutputSha256 = state.TransitionMovie.RuntimeOutputSha256,
            requestCount = state.TransitionMovieRequestCount,
            replayOnRestore = state.TransitionMovieReplayOnRestore,
        },
        nextBoundary = new
        {
            applied = state.NextBoundary.Applied,
            blocker = state.NextBoundary.Blocker,
        },
    };

    internal void ValidateSavedState(JsonElement source, Fo3Cg01Stage0State expected)
    {
        var expectedSource = JsonSerializer.SerializeToElement(SavedState(expected));
        if (!Equivalent(source, expectedSource))
            throw new InvalidOperationException("Saved Fallout 3 CG01 stage-0/5 state differs.");
    }

    private static object SavedActor(Fo3Cg01ActorState actor) => new
    {
        referenceFormId = actor.Reference.FormId,
        referenceEditorId = actor.Reference.EditorId,
        moveTargetFormId = actor.MoveTargetFormId,
        transform = SavedTransform(actor.Transform),
        enabled = actor.Enabled,
        scriptVariables = actor.ScriptVariables.Select(variable => new
        {
            referenceFormId = variable.ReferenceFormId,
            referenceEditorId = variable.ReferenceEditorId,
            scriptFormId = variable.ScriptFormId,
            scriptEditorId = variable.ScriptEditorId,
            scriptRecordSha256 = variable.ScriptRecordSha256,
            scriptSourceSha256 = variable.ScriptSourceSha256,
            variable = variable.Variable,
            value = variable.Value,
        }),
    };

    private static object SavedTransform(Fo3Cg01Transform transform) => new
    {
        positionGameUnits = new[]
        {
            transform.PositionGameUnits.X,
            transform.PositionGameUnits.Y,
            transform.PositionGameUnits.Z,
        },
        rotationRadians = new[]
        {
            transform.RotationRadians.X,
            transform.RotationRadians.Y,
            transform.RotationRadians.Z,
        },
        scale = transform.Scale,
    };

    private static JsonElement[] OrderedCommands(
        JsonElement result,
        IReadOnlyList<string> expectedKinds,
        string label)
    {
        var commands = RequiredArray(result, "commands").EnumerateArray().ToArray();
        if (commands.Length != expectedKinds.Count)
            throw new InvalidOperationException($"Fallout 3 CG01 {label} commands are incomplete.");
        for (var index = 0; index < commands.Length; index++)
        {
            if (RequiredInteger(commands[index], "index") != index ||
                RequiredString(commands[index], "kind") != expectedKinds[index])
                throw new InvalidOperationException($"Fallout 3 CG01 {label} command order differs.");
        }
        return commands;
    }

    private static Fo3Cg01Reference LoadReference(
        JsonElement source,
        string expectedRecordType,
        string expectedFormId,
        bool initiallyDisabled)
    {
        var recordType = RequiredString(source, "recordType");
        var formId = RequiredFormId(source, "formId");
        var editorId = RequiredString(source, "editorId");
        var recordSha256 = RequiredSha256(source, "recordSha256");
        var baseRecord = RequiredObject(source, "base");
        var baseRecordType = RequiredString(baseRecord, "recordType");
        var baseFormId = RequiredFormId(baseRecord, "formId");
        var baseEditorId = RequiredString(baseRecord, "editorId");
        var baseRecordSha256 = RequiredSha256(baseRecord, "recordSha256");
        var cellFormId = RequiredFormId(source, "cellFormId");
        var flags = RequiredInteger(source, "flags");
        var disabled = RequiredBoolean(source, "initiallyDisabled");
        if (recordType != expectedRecordType ||
            formId != expectedFormId ||
            cellFormId != ExpectedCellFormId ||
            disabled != initiallyDisabled ||
            (recordType == "ACHR" ? baseRecordType != "NPC_" : baseRecordType != "STAT"))
            throw new InvalidOperationException($"Fallout 3 CG01 reference differs: {expectedFormId}.");
        return new Fo3Cg01Reference(
            recordType,
            formId,
            editorId,
            recordSha256,
            baseRecordType,
            baseFormId,
            baseEditorId,
            baseRecordSha256,
            cellFormId,
            flags,
            disabled,
            LoadTransform(RequiredObject(source, "sourceTransform")));
    }

    private static Fo3Cg01Transform LoadTransform(JsonElement source)
    {
        var position = RequiredDoubleArray(source, "positionGameUnits", 3);
        var rotation = RequiredDoubleArray(source, "rotationRadians", 3);
        var scale = RequiredDouble(source, "scale");
        if (scale <= 0.0)
            throw new InvalidOperationException("Fallout 3 CG01 reference scale is invalid.");
        return new Fo3Cg01Transform(
            new Fo3Cg01Vector3(position[0], position[1], position[2]),
            new Fo3Cg01Vector3(rotation[0], rotation[1], rotation[2]),
            scale);
    }

    private static Fo3Cg01ScriptVariable LoadVariable(
        JsonElement source,
        Fo3Cg01Reference dad,
        string expectedVariable,
        int expectedValue)
    {
        var reference = LoadReference(
            RequiredObject(source, "reference"),
            "ACHR",
            ExpectedDadFormId,
            initiallyDisabled: true);
        RequireSameReference(dad, reference, $"Dad {expectedVariable} variable");
        var script = RequiredObject(source, "script");
        var variable = RequiredString(source, "variable");
        var value = RequiredInteger(source, "value");
        if (RequiredString(source, "variableType") != "short" ||
            variable != expectedVariable ||
            value != expectedValue)
            throw new InvalidOperationException($"Fallout 3 CG01 Dad {expectedVariable} differs.");
        return new Fo3Cg01ScriptVariable(
            dad.FormId,
            dad.EditorId,
            RequiredFormId(script, "formId"),
            RequiredString(script, "editorId"),
            RequiredSha256(script, "recordSha256"),
            RequiredSha256(script, "sourceSha256"),
            variable,
            value);
    }

    private static Fo3Cg01Sound LoadSound(JsonElement source)
    {
        var sound = new Fo3Cg01Sound(
            RequiredFormId(source, "formId"),
            RequiredString(source, "editorId"),
            RequiredSha256(source, "recordSha256"),
            RequiredSha256(source, "soundDataSha256"),
            RequiredString(source, "logicalPath"),
            RequiredString(source, "selectionPolicy"));
        if (sound.FormId != ExpectedNoActivationSoundFormId ||
            sound.SelectionPolicy != "source-folder-variant-set-not-yet-bound")
            throw new InvalidOperationException("Fallout 3 CG01 no-activation sound differs.");
        return sound;
    }

    internal static Fo3Cg01OwnedMovie LoadOwnedMovie(
        JsonElement source,
        string logicalPath,
        IReadOnlyList<int> arguments)
    {
        var file = RequiredString(source, "file");
        var sourcePath = Path.GetFullPath(RequiredString(source, "source"));
        var bytes = RequiredLong(source, "bytes");
        var sha256 = RequiredSha256(source, "sha256");
        if (!file.Equals(Path.GetFileName(logicalPath), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourcePath))
            throw new InvalidOperationException("Fallout 3 CG01 transition movie binding is absent.");
        var info = new FileInfo(sourcePath);
        if (info.Length != bytes)
            throw new InvalidOperationException("Fallout 3 CG01 transition movie size differs.");
        using var stream = File.OpenRead(sourcePath);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (actualSha256 != sha256)
            throw new InvalidOperationException("Fallout 3 CG01 transition movie hash differs.");

        var runtime = RequiredObject(source, "runtime");
        var runtimeInputs = RequiredObject(runtime, "inputs");
        if (RequiredString(runtime, "schema") != "opennv-owned-opening-video/v1" ||
            RequiredString(runtime, "status") != "deterministic-owned-video-transcode" ||
            Path.GetFullPath(RequiredString(runtimeInputs, "source")) != sourcePath ||
            RequiredSha256(runtimeInputs, "sourceSha256") != sha256 ||
            !runtimeInputs.TryGetProperty("policy", out var policy) ||
            policy.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                "Fallout 3 CG01 runtime transition movie identity differs.");
        var runtimeOutput = Path.GetFullPath(RequiredString(runtime, "output"));
        var runtimeOutputBytes = RequiredLong(runtime, "outputBytes");
        var runtimeOutputSha256 = RequiredSha256(runtime, "outputSha256");
        if (!File.Exists(runtimeOutput) || new FileInfo(runtimeOutput).Length != runtimeOutputBytes)
            throw new InvalidOperationException(
                "Fallout 3 CG01 runtime transition movie output is absent.");
        using var runtimeStream = File.OpenRead(runtimeOutput);
        var actualRuntimeSha256 = Convert.ToHexString(SHA256.HashData(runtimeStream))
            .ToLowerInvariant();
        if (actualRuntimeSha256 != runtimeOutputSha256)
            throw new InvalidOperationException(
                "Fallout 3 CG01 runtime transition movie output hash differs.");
        return new Fo3Cg01OwnedMovie(
            logicalPath,
            arguments,
            file,
            sourcePath,
            bytes,
            sha256,
            runtimeOutput,
            runtimeOutputBytes,
            runtimeOutputSha256);
    }

    private static void RequireSameReference(
        Fo3Cg01Reference expected,
        Fo3Cg01Reference actual,
        string label)
    {
        if (expected.FormId != actual.FormId ||
            expected.RecordSha256 != actual.RecordSha256 ||
            expected.BaseFormId != actual.BaseFormId ||
            expected.BaseRecordSha256 != actual.BaseRecordSha256)
            throw new InvalidOperationException($"Fallout 3 CG01 {label} identity differs.");
    }

    private static void RequireValueCommand(JsonElement command, int expected, string label)
    {
        if (RequiredInteger(command, "value") != expected)
            throw new InvalidOperationException($"Fallout 3 CG01 {label} value differs.");
    }

    private static void RequireSequence(
        IReadOnlyList<int> actual,
        IReadOnlyList<int> expected,
        string label)
    {
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException($"Fallout 3 CG01 {label} differs.");
    }

    private static bool Equivalent(JsonElement actual, JsonElement expected)
    {
        if (actual.ValueKind != expected.ValueKind)
            return actual.ValueKind == JsonValueKind.Number &&
                expected.ValueKind == JsonValueKind.Number &&
                actual.TryGetDouble(out var actualNumber) &&
                expected.TryGetDouble(out var expectedNumber) &&
                actualNumber == expectedNumber;
        return actual.ValueKind switch
        {
            JsonValueKind.Object => EquivalentObjects(actual, expected),
            JsonValueKind.Array => EquivalentArrays(actual, expected),
            JsonValueKind.String => actual.GetString() == expected.GetString(),
            JsonValueKind.Number => actual.GetRawText() == expected.GetRawText() ||
                (actual.TryGetDouble(out var actualNumber) &&
                 expected.TryGetDouble(out var expectedNumber) &&
                 actualNumber == expectedNumber),
            JsonValueKind.True or JsonValueKind.False =>
                actual.GetBoolean() == expected.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false,
        };
    }

    private static bool EquivalentObjects(JsonElement actual, JsonElement expected)
    {
        var actualProperties = actual.EnumerateObject().ToArray();
        var expectedProperties = expected.EnumerateObject().ToArray();
        if (actualProperties.Length != expectedProperties.Length)
            return false;
        foreach (var property in expectedProperties)
        {
            if (!actual.TryGetProperty(property.Name, out var actualValue) ||
                !Equivalent(actualValue, property.Value))
                return false;
        }
        return true;
    }

    private static bool EquivalentArrays(JsonElement actual, JsonElement expected)
    {
        var actualValues = actual.EnumerateArray().ToArray();
        var expectedValues = expected.EnumerateArray().ToArray();
        return actualValues.Length == expectedValues.Length &&
            actualValues.Zip(expectedValues).All(pair => Equivalent(pair.First, pair.Second));
    }

    private static string CanonicalSha256(JsonElement source)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = false }))
            WriteCanonical(writer, source);
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement source)
    {
        switch (source.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in source.EnumerateObject().OrderBy(
                    property => property.Name,
                    StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in source.EnumerateArray())
                    WriteCanonical(writer, value);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteRawValue(
                    JsonQuotedString(source.GetString()!),
                    skipInputValidation: false);
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(source.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException(
                    "Fallout 3 CG01 canonical transition contains an unsupported JSON value.");
        }
    }

    private static string JsonQuotedString(string value)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    result.Append("\\\"");
                    break;
                case '\\':
                    result.Append("\\\\");
                    break;
                case '\b':
                    result.Append("\\b");
                    break;
                case '\f':
                    result.Append("\\f");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\r':
                    result.Append("\\r");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                default:
                    if (character < ' ' || character > '~')
                        result.Append("\\u").Append(((int)character).ToString("x4"));
                    else
                        result.Append(character);
                    break;
            }
        }
        return result.Append('"').ToString();
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
        return result;
    }

    private static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result) || result < 1)
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
        return result;
    }

    private static double RequiredDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
        return value.GetBoolean();
    }

    private static int[] RequiredIntegerArray(JsonElement parent, string name) =>
        RequiredArray(parent, name).EnumerateArray().Select(value =>
            value.TryGetInt32(out var result)
                ? result
                : throw new InvalidOperationException(
                    $"Fallout 3 CG01 field {name} contains an invalid value.")).ToArray();

    private static double[] RequiredDoubleArray(JsonElement parent, string name, int count)
    {
        var result = RequiredArray(parent, name).EnumerateArray().Select(value =>
            value.TryGetDouble(out var number) && double.IsFinite(number)
                ? number
                : throw new InvalidOperationException(
                    $"Fallout 3 CG01 field {name} contains an invalid value.")).ToArray();
        if (result.Length != count)
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} length differs.");
        return result;
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 CG01 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 CG01 hash {name} is invalid.");
        return value;
    }
}
