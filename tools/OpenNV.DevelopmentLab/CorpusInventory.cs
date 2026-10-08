using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CorpusInventory
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    private static readonly StringComparison FileComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed class Layout
    {
        internal long Count, Bytes;
        internal int Minimum = int.MaxValue, Maximum;
        internal readonly Dictionary<int, long> Lengths = [];
        internal void Add(int length)
        {
            ++Count; Bytes += length; Minimum = Math.Min(Minimum, length); Maximum = Math.Max(Maximum, length);
            Lengths[length] = Lengths.GetValueOrDefault(length) + 1;
        }
        internal object Report() => new { Count, Bytes, Minimum, Maximum, distinctLengths = Lengths.Count,
            allLengths = Lengths.OrderBy(pair => pair.Key).Select(pair => new { length = pair.Key, count = pair.Value }),
            commonLengths = Lengths.OrderByDescending(pair => pair.Value).Take(12).Select(pair => new { length = pair.Key, count = pair.Value }) };
    }

    private sealed class Failures : IDisposable
    {
        private readonly StreamWriter _rows;
        private readonly Dictionary<string, (long Count, object First)> _groups = new(StringComparer.Ordinal);
        internal long Count { get; private set; }
        internal int GroupCount => _groups.Count;
        internal Failures(string directory) => _rows = new(Path.Combine(directory, "failure-instances.jsonl"), append: false);
        internal void Add(string lane, string source, Exception error, object? owner = null)
        {
            var row = new { lane, source, owner, errorType = error.GetType().FullName, error = error.ToString() };
            WriteRow(_rows, row);
            var key = lane + ": " + error.Message;
            _groups[key] = _groups.TryGetValue(key, out var existing) ? (existing.Count + 1, existing.First) : (1, row);
            ++Count;
        }
        internal object[] Groups() => _groups.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (object)new { failure = pair.Key, affectedInstances = pair.Value.Count, firstInstance = pair.Value.First,
                allInstances = "failure-instances.jsonl" }).ToArray();
        public void Dispose() => _rows.Dispose();
    }

    internal static int Run(FalloutPluginStack records, RuntimeLiveContentSource content, string outputDirectory)
    {
        using var reuse = new CorpusByteEvidenceCache();
        return Run(records, content, outputDirectory, reuse);
    }

    private static int Run(FalloutPluginStack records, RuntimeLiveContentSource content, string outputDirectory,
        CorpusByteEvidenceCache reuse, IReadOnlyList<FalloutModDependency>? dependencies = null)
    {
        var directory = FreshDirectory(outputDirectory, content.ContentRoots);
        WriteReport(directory, "audit-state.json", new { schema = "opennv-corpus-audit-state/v1", complete = false,
            auditBuild = typeof(CorpusInventory).Module.ModuleVersionId, runtimeBuild = typeof(FalloutPluginStack).Module.ModuleVersionId,
            registeredWinnerDenominator = records.WinnerRecordCount, winningResourceDenominator = "uninspected", runtimeReady = false });
        var watch = Stopwatch.StartNew();
        using var failures = new Failures(directory);
        var dependencyReports = new List<object>();
        foreach (var dependency in dependencies ?? [])
        {
            try
            {
                var source = dependency.SourcePath ?? throw new FileNotFoundException("Selected dependency has no declared source: " + dependency.LogicalPath);
                dependencyReports.Add(new { dependency.Name, dependency.LogicalPath, dependency.SourcePath,
                    byteEvidence = reuse.File(source), decoding = "uninspected", nativeExecution = "uninspected" });
            }
            catch (Exception error) when (SourceFailure(error))
            {
                failures.Add("selected-dependency-bytes", dependency.SourcePath ?? dependency.LogicalPath, error, dependency);
                dependencyReports.Add(new { dependency.Name, dependency.LogicalPath, dependency.SourcePath, byteOutcome = "failed" });
            }
        }
        var pluginReports = new List<object>();
        foreach (var plugin in records.Plugins)
        {
            try
            {
                var bytes = reuse.File(plugin.Plugin.Path);
                if (bytes.Bytes != plugin.Bytes || bytes.MtimeUnixMilliseconds != plugin.MtimeUnixMilliseconds || bytes.Sha256 != plugin.Sha256)
                    throw new InvalidDataException("Loaded plugin provenance changed before the corpus byte audit.");
                pluginReports.Add(new { name = plugin.Plugin.Name, source = plugin.Plugin.Path, byteEvidence = bytes,
                    selected = true, headerAndUnselectedRecordSemantics = "uninspected" });
            }
            catch (Exception error) when (SourceFailure(error))
            {
                failures.Add("plugin-container-bytes", plugin.Plugin.Path, error, new { name = plugin.Plugin.Name });
                pluginReports.Add(new { name = plugin.Plugin.Name, source = plugin.Plugin.Path, byteRead = "failed" });
            }
        }
        var recordReport = ReadRecords(records, directory, failures, watch);
        var resourceReport = ReadResources(content, directory, failures, reuse, watch);
        var summary = new
        {
            schema = "opennv-owned-corpus-inventory/v2", content.SaveCompatibilityId, content.Game, content.ContentRoots, content.Settings,
            auditBuild = typeof(CorpusInventory).Module.ModuleVersionId, runtimeBuild = typeof(FalloutPluginStack).Module.ModuleVersionId,
            plugins = pluginReports,
            selectedDependencyBytes = dependencyReports,
            winningRecords = recordReport.Winners, effectiveRecords = recordReport.Effective, deletedRecords = recordReport.Deleted,
            recordPayloadByteReads = recordReport.Payloads, recordLayoutReads = recordReport.Layouts,
            recordInventory = recordReport.Details, resourceInventory = resourceReport.Details,
            winningResources = resourceReport.Candidates, winningResourceByteEvidence = resourceReport.Bytes,
            resourceDiscoveryComplete = resourceReport.DiscoveryComplete,
            failureGroups = failures.GroupCount, failedInstances = failures.Count,
            sourceReadOutcome = failures.Count == 0 && resourceReport.DiscoveryComplete ? "accounted" : "failed",
            execution = "uninspected", semanticAcceptance = "uninspected", resourceDecoding = "uninspected",
            runtimeReady = false, gameplay = "uninspected", parity = "uninspected", reuse = reuse.State,
            externalSourceDeclarations = new { content.FalloutExecutablePath, executableDecoding = "uninspected",
                installationIniAndActiveProfileSemantics = "uninspected" },
            seconds = watch.Elapsed.TotalSeconds,
            boundary = "Every registered winning record, including deleted winners, is a source row. Every discovered winning loose/BSA path is resolved by the ordinary owner and fully read or retains its failure. Deleted/static source parsing is not execution. Stored extents, payload bytes, subrecord layout, format decoding, behavior, native presentation and pixel acceptance are independent lanes. An incomplete discovery has an unknown remaining denominator. Container bytes include inactive loose plugins/archives, whose internal behavior remains uninspected."
        };
        WriteReport(directory, "summary.json", summary);
        WriteReport(directory, "failures.json", failures.Groups());
        WriteReport(directory, "audit-state.json", new { schema = "opennv-corpus-audit-state/v1", complete = true,
            winningRecords = recordReport.Winners, discoveredResources = resourceReport.Candidates,
            resourceReport.DiscoveryComplete, summary.sourceReadOutcome, summary.failedInstances, runtimeReady = false });
        Console.WriteLine(JsonSerializer.Serialize(new { directory, summary.winningRecords, summary.deletedRecords,
            summary.winningResources, summary.winningResourceByteEvidence, summary.resourceDiscoveryComplete,
            summary.failureGroups, summary.failedInstances, summary.sourceReadOutcome, summary.runtimeReady, summary.seconds }));
        return failures.Count == 0 && resourceReport.DiscoveryComplete ? 0 : 1;
    }

    private static bool SourceFailure(Exception error) => error is IOException or InvalidDataException or NotSupportedException
        or ArgumentException or OverflowException or UnauthorizedAccessException ||
        error is AggregateException aggregate && aggregate.InnerExceptions.All(SourceFailure);

    private static string FreshDirectory(string outputDirectory, IEnumerable<string> sourceRoots)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        ValidateOutputLocation(directory, sourceRoots);
        if (Directory.Exists(directory) || System.IO.File.Exists(directory))
            throw new IOException("Corpus output must be a fresh directory so previous failures remain available.");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void ValidateOutputLocation(string directory, IEnumerable<string> sourceRoots)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        foreach (var sourceRoot in sourceRoots)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (directory.Equals(root, FileComparison) || directory.StartsWith(prefix, FileComparison))
                throw new ArgumentException("Corpus output must be outside every selected owned content root.", nameof(directory));
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void WriteRow(TextWriter output, object value) => output.WriteLine(JsonSerializer.Serialize(value));
    private static void WriteReport(string directory, string name, object value) =>
        System.IO.File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, ReportOptions));
}
