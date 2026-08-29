using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.TTW;

internal sealed record TtwReferenceIdentity(
    string Key,
    string FormKey,
    string RuntimeFormId,
    string EditorId,
    string WinnerPlugin,
    string? Role)
{
    internal static TtwReferenceIdentity FromLink(JsonElement source, bool allowPlayerRole)
    {
        if (source.TryGetProperty("role", out var roleSource))
        {
            var role = TtwJson.ValueString(roleSource, "reference role");
            if (!allowPlayerRole || role != "player" || source.EnumerateObject().Count() != 1)
                throw new InvalidOperationException("TTW opening command has an unsupported role.");
            return new TtwReferenceIdentity(
                "role:player",
                "",
                "",
                "Player",
                "runtime-role",
                role);
        }

        var formKey = TtwJson.String(source, "formKey");
        var runtimeFormId = TtwJson.Hex(source, "runtimeFormId", 8);
        var editorId = TtwJson.String(source, "editorId");
        var winnerPlugin = TtwJson.String(source, "winnerPlugin");
        if (!formKey.Contains(':', StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(formKey.Split(':', 2)[0]))
            throw new InvalidOperationException("TTW opening FormKey is invalid.");
        return new TtwReferenceIdentity(
            formKey,
            formKey,
            runtimeFormId,
            editorId,
            winnerPlugin,
            null);
    }
}

internal sealed record TtwOwnedMovie(
    string Name,
    string LogicalPath,
    int SourceRootIndex,
    long Bytes,
    string Sha256);

internal sealed record TtwStageResult(
    string QuestEditorId,
    int Stage,
    IReadOnlyList<JsonElement> Commands);

internal sealed record TtwFo3OpeningContract(
    string Path,
    string Sha256,
    string PluginStackId,
    string SaveCompatibilityId,
    string CacheCompatibilityId,
    string SourceProfileSha256,
    string SourceNamespaceSha256,
    IReadOnlyDictionary<string, TtwReferenceIdentity> References,
    IReadOnlyDictionary<string, TtwStageResult> StageResults,
    IReadOnlyDictionary<string, TtwOwnedMovie> Movies)
{
    private const string ExpectedSchema = "opennv-ttw-fo3-opening-profile/v1";
    private const string ExpectedStatus =
        "transported-bounded-ttw-fo3-opening-command-contract";
    private const string ExpectedSourceProfileSchema = "opennv-ttw-profile/v1";
    private const string ExpectedSourceProfileStatus = "validated-generated-plugin-profile";
    private const string ExpectedNamespaceSchema =
        "opennv-ttw-effective-source-namespace/v1";
    private const string ExpectedNamespaceStatus =
        "validated-neutral-effective-source-namespace";
    private const string ExpectedCacheKind = "dedicated-ttw-opening-profile";
    private const string CachePrefix = "opennv-ttw-fo3-opening-cache-v1\0";
    private const string FlattenedSourceMode =
        "flattened-installer-output-plugin-mtime";

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedStages =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [StageKey("CG00", 0)] =
            [
                "playBink",
                "setLocationSpecificLoadScreensOnly",
                "setInCharGen",
                "moveToReference",
                "moveToReference",
                "moveToReference",
                "setStage",
                "moveToReference",
                "setNumericGameSetting",
                "setNumericGameSetting",
            ],
            [StageKey("CG00", 60)] = ["showTtwGeneProjector", "setScriptVariable"],
            [StageKey("CG00", 100)] =
            [
                "removeScriptPackage",
                "setScriptVariable",
                "setScriptVariable",
                "removeImageSpaceModifier",
                "disable",
                "stopQuest",
                "setPlayerYoung",
                "setStage",
            ],
            [StageKey("CG01", 0)] =
            [
                "setSoundSourceFile",
                "moveToReference",
                "setStage",
                "setPlayerScale",
                "moveToReference",
            ],
            [StageKey("CG01", 5)] =
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
            ],
        };

    internal static TtwFo3OpeningContract Load(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var profileBytes = File.ReadAllBytes(fullPath);
        var profileSha256 = ComputeSha256(profileBytes);
        using var document = JsonDocument.Parse(profileBytes);
        var root = document.RootElement;
        if (TtwJson.String(root, "schema") != ExpectedSchema ||
            TtwJson.String(root, "status") != ExpectedStatus ||
            TtwJson.String(root, "campaign") != "Fallout3" ||
            TtwJson.String(root, "edition") != "TTW")
            throw new InvalidOperationException("TTW Fallout 3 opening profile identity differs.");

        var runtimeCompatibility = TtwJson.Object(root, "runtimeCompatibility");
        if (TtwJson.Boolean(runtimeCompatibility, "ready"))
            throw new InvalidOperationException("TTW opening profile overstates runtime readiness.");
        var unsupported = TtwJson.Array(root, "unsupportedSemantics")
            .EnumerateArray()
            .Select(value => TtwJson.ValueString(value, "unsupported semantic"))
            .ToHashSet(StringComparer.Ordinal);
        if (!unsupported.Contains("ttw-vault101-cell-resource-compilation") ||
            !unsupported.Contains("ttw-save-runtime-and-world-transition"))
            throw new InvalidOperationException("TTW opening unsupported boundary is incomplete.");

        var sourceBinding = TtwJson.Object(root, "sourceProfile");
        var sourceProfilePath = System.IO.Path.GetFullPath(
            TtwJson.String(sourceBinding, "file"));
        var sourceProfileBytes = VerifyFile(
            sourceProfilePath,
            TtwJson.Hex(sourceBinding, "sha256", 64),
            "TTW source profile");
        var sourceProfileSha256 = ComputeSha256(sourceProfileBytes);
        using var sourceDocument = JsonDocument.Parse(sourceProfileBytes);
        var sourceProfile = sourceDocument.RootElement;
        if (TtwJson.String(sourceProfile, "schema") != ExpectedSourceProfileSchema ||
            TtwJson.String(sourceProfile, "status") != ExpectedSourceProfileStatus ||
            TtwJson.String(sourceProfile, "kind") != "ttw" ||
            TtwJson.Boolean(TtwJson.Object(sourceProfile, "runtimeCompatibility"), "ready"))
            throw new InvalidOperationException("TTW source profile identity differs.");
        var pluginStackId = TtwJson.Hex(sourceProfile, "pluginStackId", 64);
        var saveCompatibilityId = TtwJson.String(sourceProfile, "saveCompatibilityId");
        if (saveCompatibilityId != $"ttw:{pluginStackId}" ||
            TtwJson.Hex(sourceBinding, "pluginStackId", 64) != pluginStackId ||
            TtwJson.String(sourceBinding, "saveCompatibilityId") != saveCompatibilityId ||
            TtwJson.String(root, "saveCompatibilityId") != saveCompatibilityId)
            throw new InvalidOperationException("TTW save/plugin-stack identity differs.");
        var sourceRoots = TtwJson.Array(sourceProfile, "sourceRoots")
            .EnumerateArray()
            .Select(value => System.IO.Path.GetFullPath(
                TtwJson.ValueString(value, "source root")))
            .ToArray();
        ValidateSourceRootLayout(sourceProfile, sourceRoots);

        var namespaceBinding = TtwJson.Object(root, "sourceNamespace");
        if (TtwJson.String(namespaceBinding, "schema") != ExpectedNamespaceSchema ||
            TtwJson.String(namespaceBinding, "status") != ExpectedNamespaceStatus)
            throw new InvalidOperationException("TTW effective-source binding differs.");
        var namespacePath = System.IO.Path.GetFullPath(
            TtwJson.String(namespaceBinding, "file"));
        var namespaceBytes = VerifyFile(
            namespacePath,
            TtwJson.Hex(namespaceBinding, "sha256", 64),
            "TTW effective-source namespace");
        var namespaceSha256 = ComputeSha256(namespaceBytes);
        using var namespaceDocument = JsonDocument.Parse(namespaceBytes);
        var sourceNamespace = namespaceDocument.RootElement;
        if (TtwJson.String(sourceNamespace, "schema") != ExpectedNamespaceSchema ||
            TtwJson.String(sourceNamespace, "status") != ExpectedNamespaceStatus ||
            TtwJson.Boolean(
                TtwJson.Object(sourceNamespace, "runtimeCompatibility"),
                "ready"))
            throw new InvalidOperationException("TTW effective-source namespace differs.");
        var namespaceProfile = TtwJson.Object(sourceNamespace, "sourceProfile");
        if (!System.IO.Path.GetFullPath(TtwJson.String(namespaceProfile, "file"))
                .Equals(sourceProfilePath, StringComparison.OrdinalIgnoreCase) ||
            TtwJson.Hex(namespaceProfile, "sha256", 64) != sourceProfileSha256 ||
            TtwJson.Hex(namespaceProfile, "pluginStackId", 64) != pluginStackId ||
            TtwJson.String(namespaceProfile, "saveCompatibilityId") != saveCompatibilityId ||
            !TtwJson.Array(sourceNamespace, "sourceRoots")
                .EnumerateArray()
                .Select(value => System.IO.Path.GetFullPath(
                    TtwJson.ValueString(value, "source root")))
                .SequenceEqual(sourceRoots, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("TTW namespace/profile binding differs.");

        var cache = TtwJson.Object(root, "cacheBoundary");
        if (TtwJson.String(cache, "kind") != ExpectedCacheKind ||
            TtwJson.Boolean(cache, "standaloneFallout3ProfileAccepted") ||
            TtwJson.Boolean(cache, "standaloneFallout3CacheReused") ||
            TtwJson.Boolean(cache, "standaloneNewVegasProfileAccepted") ||
            TtwJson.Boolean(cache, "standaloneNewVegasCacheReused"))
            throw new InvalidOperationException("TTW cache/profile isolation differs.");
        var cacheCompatibilityId = TtwJson.String(cache, "compatibilityId");
        var expectedCacheCompatibilityId = ComputeCacheCompatibilityId(root);
        if (cacheCompatibilityId != expectedCacheCompatibilityId)
            throw new InvalidOperationException("TTW opening cache compatibility ID differs.");

        var references = LoadReferences(root);
        var stages = LoadStages(root, references);
        var movies = new Dictionary<string, TtwOwnedMovie>(StringComparer.Ordinal)
        {
            ["intro"] = LoadMovie(root, "intro", sourceRoots),
            ["cg01Stage5"] = LoadMovie(root, "cg01Stage5", sourceRoots),
        };
        ValidateMovieCommand(stages[StageKey("CG00", 0)].Commands[0], movies["intro"]);
        ValidateMovieCommand(stages[StageKey("CG01", 5)].Commands[^1], movies["cg01Stage5"]);

        return new TtwFo3OpeningContract(
            fullPath,
            profileSha256,
            pluginStackId,
            saveCompatibilityId,
            cacheCompatibilityId,
            sourceProfileSha256,
            namespaceSha256,
            references,
            stages,
            movies);
    }

    internal bool TryGetStage(string questEditorId, int stage, out TtwStageResult result) =>
        StageResults.TryGetValue(StageKey(questEditorId, stage), out result!);

    private static void ValidateSourceRootLayout(
        JsonElement sourceProfile,
        string[] sourceRoots)
    {
        if (sourceRoots.Length == 0 || sourceRoots.Distinct(
                StringComparer.OrdinalIgnoreCase).Count() != sourceRoots.Length ||
            sourceRoots.Any(rootPath => !Directory.Exists(rootPath)))
            throw new InvalidOperationException("TTW source-root boundary differs.");

        var loadOrderSource = TtwJson.Object(sourceProfile, "loadOrderSource");
        if (!loadOrderSource.TryGetProperty("derivation", out var derivation))
        {
            if (sourceRoots.Length < 2)
                throw new InvalidOperationException("TTW layered source-root boundary differs.");
            return;
        }
        if (derivation.ValueKind != JsonValueKind.Object ||
            TtwJson.String(derivation, "mode") != FlattenedSourceMode ||
            !TtwJson.Boolean(derivation, "allPluginsActive") ||
            !TtwJson.Boolean(derivation, "strictlyIncreasingPluginModificationTimes"))
            throw new InvalidOperationException("TTW flattened-source derivation differs.");

        var flattenedIndex = TtwJson.Integer(derivation, "flattenedSourceRootIndex");
        var plugins = TtwJson.Array(sourceProfile, "plugins").EnumerateArray().ToArray();
        var evidence = TtwJson.Array(derivation, "plugins").EnumerateArray().ToArray();
        if (flattenedIndex != sourceRoots.Length - 1 || evidence.Length != plugins.Length)
            throw new InvalidOperationException("TTW flattened-source boundary differs.");

        long previousTimestamp = -1;
        for (var index = 0; index < plugins.Length; index++)
        {
            var timestamp = TtwJson.Long(evidence[index], "lastWriteTimeNs");
            if (TtwJson.Integer(plugins[index], "sourceRootIndex") != flattenedIndex ||
                TtwJson.String(plugins[index], "file") !=
                    TtwJson.String(evidence[index], "file") ||
                timestamp <= previousTimestamp)
                throw new InvalidOperationException("TTW flattened-source evidence differs.");
            previousTimestamp = timestamp;
        }
    }

    internal TtwOwnedMovie MovieForCommand(string logicalPath)
    {
        var normalized = logicalPath.Replace('\\', '/');
        return Movies.Values.Single(movie =>
            movie.LogicalPath.Replace('\\', '/').EndsWith(
                $"/{normalized}",
                StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, TtwReferenceIdentity> LoadReferences(JsonElement root)
    {
        var result = new Dictionary<string, TtwReferenceIdentity>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var sectionName in new[] { "forms", "operands" })
        {
            foreach (var property in TtwJson.Object(root, sectionName).EnumerateObject())
            {
                var source = property.Value;
                var link = new TtwReferenceIdentity(
                    TtwJson.String(source, "formKey"),
                    TtwJson.String(source, "formKey"),
                    TtwJson.Hex(source, "runtimeFormId", 8),
                    TtwJson.String(source, "editorId"),
                    TtwJson.String(TtwJson.Object(source, "winner"), "plugin"),
                    null);
                if (!link.FormKey.Contains(':', StringComparison.Ordinal) ||
                    !result.TryAdd(link.FormKey, link))
                    throw new InvalidOperationException("TTW opening reference closure is invalid.");
            }
        }
        return result;
    }

    private static Dictionary<string, TtwStageResult> LoadStages(
        JsonElement root,
        IReadOnlyDictionary<string, TtwReferenceIdentity> references)
    {
        var stages = TtwJson.Object(root, "stages");
        var result = new Dictionary<string, TtwStageResult>(StringComparer.Ordinal);
        foreach (var expected in ExpectedStages)
        {
            var separator = expected.Key.IndexOf(':', StringComparison.Ordinal);
            var questEditorId = expected.Key[..separator];
            var stage = int.Parse(
                expected.Key[(separator + 1)..],
                System.Globalization.CultureInfo.InvariantCulture);
            var quest = TtwJson.Object(stages, questEditorId);
            ValidateReferenceLink(TtwJson.Object(quest, "quest"), references, allowPlayerRole: false);
            ValidateReferenceLink(TtwJson.Object(quest, "script"), references, allowPlayerRole: false);
            var commands = TtwJson.Object(quest, "results")
                .GetProperty(stage.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .EnumerateArray()
                .Select(command => command.Clone())
                .ToArray();
            if (commands.Length != expected.Value.Length)
                throw new InvalidOperationException($"TTW {expected.Key} command count differs.");
            for (var index = 0; index < commands.Length; index++)
            {
                if (TtwJson.Integer(commands[index], "index") != index ||
                    TtwJson.String(commands[index], "kind") != expected.Value[index])
                    throw new InvalidOperationException($"TTW {expected.Key} command order differs.");
                ValidateCommandLinks(commands[index], references);
            }
            result.Add(expected.Key, new TtwStageResult(questEditorId, stage, commands));
        }

        var actualKeys = stages.EnumerateObject().SelectMany(quest =>
            TtwJson.Object(quest.Value, "results").EnumerateObject().Select(resultRow =>
                StageKey(quest.Name, int.Parse(
                    resultRow.Name,
                    System.Globalization.CultureInfo.InvariantCulture))));
        if (!actualKeys.ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedStages.Keys))
            throw new InvalidOperationException("TTW admitted stage-result closure differs.");
        return result;
    }

    private static void ValidateCommandLinks(
        JsonElement command,
        IReadOnlyDictionary<string, TtwReferenceIdentity> references)
    {
        foreach (var property in new[] { "subject", "target", "quest", "modifier", "sound" })
        {
            if (!command.TryGetProperty(property, out var link))
                continue;
            if (link.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"TTW command {property} link is invalid.");
            ValidateReferenceLink(link, references, property == "subject");
        }
    }

    private static void ValidateReferenceLink(
        JsonElement link,
        IReadOnlyDictionary<string, TtwReferenceIdentity> references,
        bool allowPlayerRole)
    {
        var actual = TtwReferenceIdentity.FromLink(link, allowPlayerRole);
        if (actual.Role == "player")
            return;
        if (!references.TryGetValue(actual.FormKey, out var expected) ||
            actual.RuntimeFormId != expected.RuntimeFormId ||
            actual.EditorId != expected.EditorId ||
            actual.WinnerPlugin != expected.WinnerPlugin)
            throw new InvalidOperationException($"TTW command reference differs: {actual.FormKey}.");
    }

    private static TtwOwnedMovie LoadMovie(
        JsonElement root,
        string name,
        IReadOnlyList<string> sourceRoots)
    {
        var source = TtwJson.Object(TtwJson.Object(root, "movies"), name);
        var winner = TtwJson.Object(source, "winner");
        var logicalPath = TtwJson.String(source, "logicalPath").Replace('/',
            System.IO.Path.DirectorySeparatorChar);
        var sourceRootIndex = TtwJson.Integer(winner, "sourceRootIndex");
        if (sourceRootIndex < 0 || sourceRootIndex >= sourceRoots.Count)
            throw new InvalidOperationException($"TTW movie {name} source root differs.");
        var sourceRoot = sourceRoots[sourceRootIndex];
        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(sourceRoot, logicalPath));
        var rootPrefix = sourceRoot.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"TTW movie {name} escapes its source root.");
        var bytes = TtwJson.Long(winner, "bytes");
        var sha256 = TtwJson.Hex(winner, "sha256", 64);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length != bytes)
            throw new InvalidOperationException($"TTW movie {name} is absent or changed.");
        using var stream = File.OpenRead(fullPath);
        if (ComputeSha256(stream) != sha256)
            throw new InvalidOperationException($"TTW movie {name} hash differs.");
        return new TtwOwnedMovie(
            name,
            TtwJson.String(source, "logicalPath"),
            sourceRootIndex,
            bytes,
            sha256);
    }

    private static void ValidateMovieCommand(JsonElement command, TtwOwnedMovie movie)
    {
        var logicalPath = TtwJson.String(command, "logicalPath").Replace('\\', '/');
        if (!movie.LogicalPath.Replace('\\', '/').EndsWith(
                $"/{logicalPath}",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"TTW movie command differs: {movie.Name}.");
        _ = TtwJson.IntegerArray(command, "arguments");
    }

    private static byte[] VerifyFile(string path, string expectedSha256, string label)
    {
        var bytes = File.ReadAllBytes(path);
        if (ComputeSha256(bytes) != expectedSha256)
            throw new InvalidOperationException($"{label} hash differs.");
        return bytes;
    }

    private static string ComputeCacheCompatibilityId(JsonElement root)
    {
        using var payload = new MemoryStream();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            foreach (var name in new[]
                     {
                         "forms",
                         "movies",
                         "operands",
                         "recipe",
                         "schema",
                         "sourceNamespace",
                         "sourceProfile",
                         "stages",
                     })
            {
                writer.WritePropertyName(name);
                WriteCanonical(writer, root.GetProperty(name));
            }
            writer.WriteEndObject();
        }
        var prefix = Encoding.UTF8.GetBytes(CachePrefix);
        var input = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(input, 0);
        payload.ToArray().CopyTo(input, prefix.Length);
        return $"ttw-fo3-opening:{ComputeSha256(input)}";
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
                foreach (var item in source.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(source.GetString());
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
                throw new InvalidOperationException("TTW canonical profile value is unsupported.");
        }
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ComputeSha256(Stream stream) =>
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    private static string StageKey(string questEditorId, int stage) =>
        $"{questEditorId}:{stage}";
}

internal sealed class TtwRuntimeEntityState
{
    public string Key { get; set; } = "";
    public string FormKey { get; set; } = "";
    public string RuntimeFormId { get; set; } = "";
    public string EditorId { get; set; } = "";
    public string WinnerPlugin { get; set; } = "";
    public string? Role { get; set; }
    public bool? Enabled { get; set; }
    public string? MoveTargetFormKey { get; set; }
    public string? MoveTargetRuntimeFormId { get; set; }
    public Dictionary<string, double> ScriptVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class TtwMovieRequest
{
    public string LogicalPath { get; set; } = "";
    public int[] Arguments { get; set; } = [];
    public int SourceRootIndex { get; set; }
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = "";
}

internal sealed class TtwFo3OpeningState
{
    public Dictionary<string, int> QuestStages { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> QuestRunning { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TtwRuntimeEntityState> Entities { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> NumericGameSettings { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, string> SoundSourceOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<string> RemovedImageSpaceModifiers { get; set; } = [];
    public List<TtwMovieRequest> MovieRequests { get; set; } = [];
    public List<string> AppliedStages { get; set; } = [];
    public List<string> AppliedCommands { get; set; } = [];
    public int LocationSpecificLoadScreensOnly { get; set; }
    public int InCharGen { get; set; }
    public bool TtwGeneProjectorRequested { get; set; }
    public bool PlayerPackageActive { get; set; } = true;
    public double PlayerScale { get; set; } = 1.0;
    public int PlayerToddler { get; set; }
    public int PlayerYoung { get; set; }
    public int AutoDisplayObjectives { get; set; }
    public int[] EnabledPlayerControls { get; set; } = [];
    public int[] DisabledPlayerControls { get; set; } = [];
    public string? NoActivationSoundFormKey { get; set; }

    internal void Normalize()
    {
        QuestStages = new Dictionary<string, int>(QuestStages, StringComparer.OrdinalIgnoreCase);
        QuestRunning = new Dictionary<string, bool>(QuestRunning, StringComparer.OrdinalIgnoreCase);
        Entities = new Dictionary<string, TtwRuntimeEntityState>(
            Entities,
            StringComparer.OrdinalIgnoreCase);
        NumericGameSettings = new Dictionary<string, double>(
            NumericGameSettings,
            StringComparer.Ordinal);
        SoundSourceOverrides = new Dictionary<string, string>(
            SoundSourceOverrides,
            StringComparer.OrdinalIgnoreCase);
        foreach (var entity in Entities.Values)
            entity.ScriptVariables = new Dictionary<string, double>(
                entity.ScriptVariables,
                StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class TtwFo3OpeningSave
{
    public string Schema { get; set; } = "opennv-ttw-fo3-opening-save/v1";
    public string ProfileSha256 { get; set; } = "";
    public string PluginStackId { get; set; } = "";
    public string SaveCompatibilityId { get; set; } = "";
    public string CacheCompatibilityId { get; set; } = "";
    public TtwFo3OpeningState State { get; set; } = new();
}

internal sealed class TtwFo3OpeningExecutor
{
    private readonly TtwFo3OpeningContract _contract;
    private readonly HashSet<string> _activeStages = new(StringComparer.Ordinal);

    internal TtwFo3OpeningExecutor(
        TtwFo3OpeningContract contract,
        TtwFo3OpeningState state)
    {
        _contract = contract;
        State = state;
    }

    internal TtwFo3OpeningState State { get; }

    internal void ApplyBoundedContract()
    {
        ApplyStage("CG00", 0);
        ApplyStage("CG00", 60);
        ApplyStage("CG00", 100);
        ValidateFinalState(State);
    }

    internal void ApplyStage(string questEditorId, int stage)
    {
        if (!_contract.TryGetStage(questEditorId, stage, out var result))
            return;
        var key = $"{questEditorId}:{stage}";
        if (State.AppliedStages.Contains(key, StringComparer.Ordinal))
            return;
        if (!_activeStages.Add(key))
            throw new InvalidOperationException($"TTW nested stage cycle detected: {key}.");
        try
        {
            State.QuestStages[questEditorId] = stage;
            State.QuestRunning[questEditorId] = true;
            State.AppliedStages.Add(key);
            foreach (var command in result.Commands)
                ApplyCommand(questEditorId, stage, command);
        }
        finally
        {
            _activeStages.Remove(key);
        }
    }

    internal static void ValidateFinalState(TtwFo3OpeningState state)
    {
        if (state.AppliedCommands.Count != 38 ||
            !state.AppliedStages.SequenceEqual(
                new[] { "CG00:0", "CG00:60", "CG00:100", "CG01:0", "CG01:5" }) ||
            state.QuestStages.GetValueOrDefault("CG00") != 100 ||
            state.QuestRunning.GetValueOrDefault("CG00", true) ||
            state.QuestStages.GetValueOrDefault("CG01") != 5 ||
            !state.QuestRunning.GetValueOrDefault("CG01") ||
            state.LocationSpecificLoadScreensOnly != 1 ||
            state.InCharGen != 1 ||
            !state.TtwGeneProjectorRequested ||
            state.PlayerPackageActive ||
            state.PlayerScale != 0.4 ||
            state.PlayerToddler != 1 ||
            state.PlayerYoung != 1 ||
            state.AutoDisplayObjectives != 1 ||
            state.MovieRequests.Count != 2 ||
            state.EnabledPlayerControls.Length != 5 ||
            state.DisabledPlayerControls.Length != 7)
            throw new InvalidOperationException("TTW bounded opening final state differs.");
        RequireEntityEnabled(state, "Fallout3.esm:0290a7", false);
        RequireEntityEnabled(state, "Fallout3.esm:02ea4d", true);
        RequireEntityEnabled(state, "Fallout3.esm:0300ef", true);
        var cg01Dad = state.Entities["Fallout3.esm:02ea4d"];
        if (cg01Dad.ScriptVariables.GetValueOrDefault("doTalk") != 1 ||
            cg01Dad.ScriptVariables.GetValueOrDefault("talking") != 0 ||
            state.NoActivationSoundFormKey != "FalloutNV.esm:089b4c")
            throw new InvalidOperationException("TTW CG01 Dad/sound state differs.");
    }

    private void ApplyCommand(string questEditorId, int stage, JsonElement command)
    {
        var index = TtwJson.Integer(command, "index");
        var kind = TtwJson.String(command, "kind");
        State.AppliedCommands.Add($"{questEditorId}:{stage}:{index}:{kind}");
        switch (kind)
        {
            case "playBink":
                {
                    var logicalPath = TtwJson.String(command, "logicalPath");
                    var movie = _contract.MovieForCommand(logicalPath);
                    State.MovieRequests.Add(new TtwMovieRequest
                    {
                        LogicalPath = logicalPath,
                        Arguments = TtwJson.IntegerArray(command, "arguments"),
                        SourceRootIndex = movie.SourceRootIndex,
                        Bytes = movie.Bytes,
                        Sha256 = movie.Sha256,
                    });
                    break;
                }
            case "setLocationSpecificLoadScreensOnly":
                State.LocationSpecificLoadScreensOnly = TtwJson.Integer(command, "value");
                break;
            case "setInCharGen":
                State.InCharGen = TtwJson.Integer(command, "value");
                break;
            case "moveToReference":
                {
                    var subject = Entity(TtwJson.Object(command, "subject"), allowPlayerRole: true);
                    var target = TtwReferenceIdentity.FromLink(
                        TtwJson.Object(command, "target"),
                        allowPlayerRole: false);
                    subject.MoveTargetFormKey = target.FormKey;
                    subject.MoveTargetRuntimeFormId = target.RuntimeFormId;
                    break;
                }
            case "setStage":
                {
                    var quest = TtwReferenceIdentity.FromLink(
                        TtwJson.Object(command, "quest"),
                        allowPlayerRole: false);
                    var nextStage = TtwJson.Integer(command, "stage");
                    State.QuestStages[quest.EditorId] = nextStage;
                    State.QuestRunning[quest.EditorId] = true;
                    ApplyStage(quest.EditorId, nextStage);
                    break;
                }
            case "setNumericGameSetting":
                State.NumericGameSettings[TtwJson.String(command, "setting")] =
                    TtwJson.Double(command, "value");
                break;
            case "showTtwGeneProjector":
                State.TtwGeneProjectorRequested = true;
                break;
            case "setScriptVariable":
                Entity(TtwJson.Object(command, "subject"), allowPlayerRole: false)
                    .ScriptVariables[TtwJson.String(command, "variable")] =
                    TtwJson.Double(command, "value");
                break;
            case "removeScriptPackage":
                _ = Entity(TtwJson.Object(command, "subject"), allowPlayerRole: true);
                State.PlayerPackageActive = false;
                break;
            case "removeImageSpaceModifier":
                {
                    var modifier = TtwReferenceIdentity.FromLink(
                        TtwJson.Object(command, "modifier"),
                        allowPlayerRole: false);
                    if (!State.RemovedImageSpaceModifiers.Contains(
                            modifier.FormKey,
                            StringComparer.OrdinalIgnoreCase))
                        State.RemovedImageSpaceModifiers.Add(modifier.FormKey);
                    break;
                }
            case "disable":
                Entity(TtwJson.Object(command, "subject"), allowPlayerRole: false).Enabled = false;
                break;
            case "stopQuest":
                {
                    var quest = TtwReferenceIdentity.FromLink(
                        TtwJson.Object(command, "quest"),
                        allowPlayerRole: false);
                    State.QuestRunning[quest.EditorId] = false;
                    break;
                }
            case "setPlayerYoung":
                State.PlayerYoung = TtwJson.Integer(command, "value");
                break;
            case "setSoundSourceFile":
                {
                    var sound = TtwReferenceIdentity.FromLink(
                        TtwJson.Object(command, "sound"),
                        allowPlayerRole: false);
                    State.SoundSourceOverrides[sound.FormKey] =
                        TtwJson.String(command, "logicalPath");
                    break;
                }
            case "setPlayerScale":
                State.PlayerScale = TtwJson.Double(command, "value");
                break;
            case "enable":
                Entity(TtwJson.Object(command, "subject"), allowPlayerRole: false).Enabled = true;
                break;
            case "enablePlayerControls":
                State.EnabledPlayerControls = TtwJson.IntegerArray(command, "arguments");
                break;
            case "disablePlayerControls":
                State.DisabledPlayerControls = TtwJson.IntegerArray(command, "arguments");
                break;
            case "autoDisplayObjectives":
                State.AutoDisplayObjectives = TtwJson.Integer(command, "value");
                break;
            case "setNoActivationSound":
                State.NoActivationSoundFormKey = TtwReferenceIdentity.FromLink(
                    TtwJson.Object(command, "sound"),
                    allowPlayerRole: false).FormKey;
                break;
            case "setPlayerToddler":
                State.PlayerToddler = TtwJson.Integer(command, "value");
                break;
            default:
                throw new InvalidOperationException($"Unsupported TTW command: {kind}.");
        }
    }

    private TtwRuntimeEntityState Entity(JsonElement link, bool allowPlayerRole)
    {
        var identity = TtwReferenceIdentity.FromLink(link, allowPlayerRole);
        if (State.Entities.TryGetValue(identity.Key, out var existing))
        {
            if (existing.FormKey != identity.FormKey ||
                existing.RuntimeFormId != identity.RuntimeFormId ||
                existing.EditorId != identity.EditorId ||
                existing.WinnerPlugin != identity.WinnerPlugin ||
                existing.Role != identity.Role)
                throw new InvalidOperationException($"TTW runtime identity drifted: {identity.Key}.");
            return existing;
        }
        var created = new TtwRuntimeEntityState
        {
            Key = identity.Key,
            FormKey = identity.FormKey,
            RuntimeFormId = identity.RuntimeFormId,
            EditorId = identity.EditorId,
            WinnerPlugin = identity.WinnerPlugin,
            Role = identity.Role,
        };
        State.Entities.Add(identity.Key, created);
        return created;
    }

    private static void RequireEntityEnabled(
        TtwFo3OpeningState state,
        string formKey,
        bool expected)
    {
        if (!state.Entities.TryGetValue(formKey, out var entity) || entity.Enabled != expected)
            throw new InvalidOperationException($"TTW enabled state differs: {formKey}.");
    }
}

internal static class TtwFo3OpeningProof
{
    private static readonly JsonSerializerOptions SaveJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions HashJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static void Run(
        TtwFo3OpeningContract contract,
        string phase,
        string savePath,
        string reportPath)
    {
        TtwFo3OpeningState state;
        if (phase == "apply")
        {
            state = new TtwFo3OpeningState();
            new TtwFo3OpeningExecutor(contract, state).ApplyBoundedContract();
            WriteSave(contract, state, savePath);
        }
        else if (phase == "restore")
        {
            state = ReadSave(contract, savePath);
            TtwFo3OpeningExecutor.ValidateFinalState(state);
        }
        else
            throw new ArgumentException("TTW opening proof phase must be apply or restore.");

        var stateSha256 = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(state, HashJson))).ToLowerInvariant();
        OpenNV.Runtime.RuntimeCoordinator.WriteReport(reportPath, new
        {
            schema = "opennv-ttw-fo3-opening-runtime-proof/v1",
            status = "pass",
            phase,
            runtimeReady = false,
            contract = new
            {
                schema = "opennv-ttw-fo3-opening-profile/v1",
                sha256 = contract.Sha256,
                sourceProfileSha256 = contract.SourceProfileSha256,
                sourceNamespaceSha256 = contract.SourceNamespaceSha256,
                pluginStackId = contract.PluginStackId,
                saveCompatibilityId = contract.SaveCompatibilityId,
                cacheCompatibilityId = contract.CacheCompatibilityId,
            },
            execution = new
            {
                nativeCommandStateExecuted = true,
                commandCount = state.AppliedCommands.Count,
                appliedStages = state.AppliedStages,
                synchronouslyNestedCg01Stage5 = true,
                stateSha256,
                savePath,
                saveRestoreBoundary = "dedicated-ttw-identity",
            },
            presentation = new
            {
                nativeWorldExecuted = false,
                moviePlaybackExecuted = false,
                movieRequests = state.MovieRequests,
                vault101PresentationConnected = false,
            },
            blockers = new[]
            {
                "ttw-vault101-cell-resource-compilation",
                "reference-transform-and-world-application",
                "owned-movie-runtime-transcode-and-playback",
                "cg01-stage-10-and-later-gameplay",
                "xnvse-and-jam-native-plugin-execution",
            },
        });
        GD.Print(
            $"OPENNV_TTW_FO3_OPENING_RUNTIME_PASS phase={phase} commands={state.AppliedCommands.Count} " +
            $"runtimeReady=0 save={savePath}");
    }

    private static void WriteSave(
        TtwFo3OpeningContract contract,
        TtwFo3OpeningState state,
        string savePath)
    {
        var fullPath = System.IO.Path.GetFullPath(savePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        var document = new TtwFo3OpeningSave
        {
            ProfileSha256 = contract.Sha256,
            PluginStackId = contract.PluginStackId,
            SaveCompatibilityId = contract.SaveCompatibilityId,
            CacheCompatibilityId = contract.CacheCompatibilityId,
            State = state,
        };
        var temporary = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(document, SaveJson) + System.Environment.NewLine);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static TtwFo3OpeningState ReadSave(
        TtwFo3OpeningContract contract,
        string savePath)
    {
        var document = JsonSerializer.Deserialize<TtwFo3OpeningSave>(
            File.ReadAllText(System.IO.Path.GetFullPath(savePath)),
            SaveJson) ?? throw new InvalidOperationException("TTW opening save is invalid.");
        if (document.Schema != "opennv-ttw-fo3-opening-save/v1" ||
            document.ProfileSha256 != contract.Sha256 ||
            document.PluginStackId != contract.PluginStackId ||
            document.SaveCompatibilityId != contract.SaveCompatibilityId ||
            document.CacheCompatibilityId != contract.CacheCompatibilityId ||
            !document.SaveCompatibilityId.StartsWith("ttw:", StringComparison.Ordinal) ||
            !document.CacheCompatibilityId.StartsWith(
                "ttw-fo3-opening:",
                StringComparison.Ordinal))
            throw new InvalidOperationException("TTW opening save identity differs.");
        document.State.Normalize();
        return document.State;
    }
}

internal static class TtwJson
{
    internal static JsonElement Object(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"TTW field {name} is absent.");
        return value;
    }

    internal static JsonElement Array(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"TTW field {name} is absent.");
        return value;
    }

    internal static string String(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
            throw new InvalidOperationException($"TTW field {name} is absent.");
        return ValueString(value, name);
    }

    internal static string ValueString(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"TTW {label} is invalid.");
        return value.GetString()!;
    }

    internal static string Hex(JsonElement parent, string name, int characters)
    {
        var value = String(parent, name);
        if (value.Length != characters || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"TTW field {name} is not hexadecimal.");
        return value.ToLowerInvariant();
    }

    internal static int Integer(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"TTW field {name} is not an integer.");
        return result;
    }

    internal static long Long(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !value.TryGetInt64(out var result) || result < 0)
            throw new InvalidOperationException($"TTW field {name} is not a non-negative integer.");
        return result;
    }

    internal static double Double(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new InvalidOperationException($"TTW field {name} is not finite.");
        return result;
    }

    internal static bool Boolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"TTW field {name} is not Boolean.");
        return value.GetBoolean();
    }

    internal static int[] IntegerArray(JsonElement parent, string name) =>
        Array(parent, name).EnumerateArray().Select(value =>
            value.TryGetInt32(out var result)
                ? result
                : throw new InvalidOperationException(
                    $"TTW field {name} contains a non-integer.")).ToArray();
}
