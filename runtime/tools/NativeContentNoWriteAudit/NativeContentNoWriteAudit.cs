using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Tools;

public partial class NativeContentNoWriteAudit : Node
{
    private const float GameUnitsToMetres = 0.0142875f;
    private const string DefaultCell = "FalloutNV.esm:103df9";
    private const string AnimationMember = @"meshes\creatures\robobrain\locomotion\mtfastforward.kf";

    public override void _Ready()
    {
        var exitCode = 1;
        try
        {
            var arguments = ParseArguments(OS.GetCmdlineUserArgs());
            var manifestPath = Path.GetFullPath(arguments["source-stack"]);
            var manifestBytes = File.ReadAllBytes(manifestPath);
            using var document = JsonDocument.Parse(manifestBytes);
            var manifest = document.RootElement;
            var roots = manifest.GetProperty("roots").EnumerateArray()
                .Select(row => Path.GetFullPath(row.GetProperty("root").GetString()!))
                .Distinct(PathComparer)
                .ToArray();
            var sourceBefore = SnapshotSources(roots, manifestPath);
            var temporaryRoot = Path.GetFullPath(arguments["temp-root"]);
            var expectedTemporaryRoot = Path.TrimEndingDirectorySeparator(temporaryRoot);
            var actualTemporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
            if (!actualTemporaryRoot.Equals(expectedTemporaryRoot, PathComparison))
                throw new InvalidOperationException(
                    $"Audit TEMP root differs: expected={expectedTemporaryRoot} actual={actualTemporaryRoot}");
            var userRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("user://"));
            var temporaryBefore = SnapshotOutput(temporaryRoot);
            var userBefore = SnapshotUserOutput(userRoot);

            RuntimeOwnedContentSource.Configure(
                roots[0],
                manifestPath,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                manifest.GetProperty("stackId").GetString());
            var (models, built, kfBlocks) = ExerciseOwnedContent(
                arguments.GetValueOrDefault("cell", DefaultCell));
            RuntimeOwnedContentSource.Clear();

            var sourceAfter = SnapshotSources(roots, manifestPath);
            var temporaryAfter = SnapshotOutput(temporaryRoot);
            var userAfter = SnapshotUserOutput(userRoot);
            RequireUnchanged(sourceBefore, sourceAfter, "registered source roots");
            RequireUnchanged(temporaryBefore, temporaryAfter, "runtime TEMP root");
            RequireUnchanged(userBefore, userAfter, "Godot user root");
            GD.Print(
                $"OPENNV_NATIVE_NO_CONTENT_WRITE_OK models={models} built={built} " +
                $"kfBlocks={kfBlocks} sourceFiles={sourceAfter.Count} " +
                $"sourceBytes={sourceAfter.Values.Sum(value => value.Bytes)} " +
                $"tempFiles={temporaryAfter.Count} userFiles={userAfter.Count} forbiddenWrites=0 " +
                "excludedEngineState=logs allowedState=settings,save,mod-install:not-invoked");
            exitCode = 0;
        }
        catch (Exception error)
        {
            GD.PrintErr(
                $"OPENNV_NATIVE_NO_CONTENT_WRITE_ERROR {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            RuntimeOwnedContentSource.Clear();
            GetTree().Quit(exitCode);
        }
    }

    private static (int Models, int Built, int KfBlocks) ExerciseOwnedContent(string cellText)
    {
        var separator = cellText.LastIndexOf(':');
        if (separator <= 0 || !uint.TryParse(
                cellText[(separator + 1)..],
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var objectId))
            throw new ArgumentException("CELL key must be plugin:hex-object-id.", nameof(cellText));
        var source = RuntimeOwnedContentSource.Current!;
        using var stack = FalloutPluginStack.Load(source.PluginSources);
        var cell = FalloutCellSceneReader.Read(
            stack, new FalloutFormKey(cellText[..separator], objectId));
        var models = cell.BaseObjects.Values.Select(value => value.ModelPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var built = 0;
        foreach (var model in models)
        {
            if (!source.TryRead(model, null, out var payload, out _))
                throw new FileNotFoundException($"Winning model is missing: {model}");
            _ = FalloutNifFile.Read(payload);
            try
            {
                var scene = RuntimeNativeNifMeshBuilder.Build(payload, GameUnitsToMetres);
                scene.Root.Free();
                built++;
            }
            catch (NotSupportedException)
            {
                // The no-write invariant covers both supported and fail-closed formats.
            }
            catch (InvalidDataException)
            {
                // Known strict format blockers must also remain memory-only.
            }
        }
        if (!source.TryRead(AnimationMember, null, out var animation, out _))
            throw new FileNotFoundException($"Registered animation member is missing: {AnimationMember}");
        var kf = FalloutNifFile.Read(animation);
        if (!kf.Blocks.Any(block => block.TypeName == "NiControllerSequence"))
            throw new InvalidDataException("Registered KF probe has no controller sequence.");
        return (models.Length, built, kf.Blocks.Count);
    }

    private static Dictionary<string, FileState> SnapshotSources(
        IReadOnlyList<string> roots,
        string manifestPath)
    {
        var result = new Dictionary<string, FileState>(PathComparer);
        AddFile(result, manifestPath, hashContents: true);
        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                AddFile(result, file, hashContents: false);
        }
        return result;
    }

    private static Dictionary<string, FileState> SnapshotOutput(string root)
    {
        var result = new Dictionary<string, FileState>(PathComparer);
        if (!Directory.Exists(root))
            return result;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            AddFile(result, file, hashContents: true);
        return result;
    }

    private static Dictionary<string, FileState> SnapshotUserOutput(string root)
    {
        var result = new Dictionary<string, FileState>(PathComparer);
        if (!Directory.Exists(root))
            return result;
        var engineLogRoot = Path.Combine(root, "logs") + Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var resolved = Path.GetFullPath(file);
            if (resolved.StartsWith(engineLogRoot, PathComparison))
                continue;
            AddFile(result, resolved, hashContents: true);
        }
        return result;
    }

    private static void AddFile(
        IDictionary<string, FileState> result,
        string path,
        bool hashContents)
    {
        var resolved = Path.GetFullPath(path);
        var info = new FileInfo(resolved);
        var hash = string.Empty;
        if (hashContents)
        {
            try
            {
                hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(resolved)))
                    .ToLowerInvariant();
            }
            catch (IOException)
            {
                hash = "<locked>";
            }
            catch (UnauthorizedAccessException)
            {
                hash = "<inaccessible>";
            }
        }
        result.Add(resolved, new FileState(
            info.Length,
            info.LastWriteTimeUtc.Ticks,
            hash));
    }

    private static void RequireUnchanged(
        IReadOnlyDictionary<string, FileState> before,
        IReadOnlyDictionary<string, FileState> after,
        string label)
    {
        if (before.Count == after.Count && before.All(pair =>
                after.TryGetValue(pair.Key, out var current) && current == pair.Value))
            return;
        var added = after.Keys.Except(before.Keys, PathComparer).Order(PathComparer).Take(3);
        var removed = before.Keys.Except(after.Keys, PathComparer).Order(PathComparer).Take(3);
        var changed = before.Keys.Intersect(after.Keys, PathComparer)
            .Where(path => before[path] != after[path]).Order(PathComparer).Take(3);
        throw new InvalidOperationException(
            $"Native content load changed {label}: " +
            $"added=[{string.Join(',', added)}] removed=[{string.Join(',', removed)}] " +
            $"changed=[{string.Join(',', changed)}]");
    }

    private static Dictionary<string, string> ParseArguments(string[] source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < source.Length; ++index)
        {
            if (!source[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= source.Length)
                continue;
            result[source[index][2..]] = source[++index];
        }
        if (!result.ContainsKey("source-stack") || !result.ContainsKey("temp-root"))
            throw new ArgumentException("--source-stack and --temp-root are required.");
        return result;
    }

    private readonly record struct FileState(long Bytes, long LastWriteTicks, string Sha256);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
