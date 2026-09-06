using System.Diagnostics;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class CorpusInventory
{
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
            commonLengths = Lengths.OrderByDescending(pair => pair.Value).Take(12).Select(pair => new { length = pair.Key, count = pair.Value }) };
    }

    internal static int Run(FalloutPluginStack records, RuntimeLiveContentSource content, string outputDirectory)
    {
        var directory = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(directory)) throw new IOException("Corpus output must be a fresh directory so previous failures remain available.");
        Directory.CreateDirectory(directory);
        var watch = Stopwatch.StartNew();
        var layouts = new Dictionary<(string Record, string Field), Layout>();
        var events = new Dictionary<string, HashSet<FalloutFormKey>>(StringComparer.OrdinalIgnoreCase);
        var failures = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var recordCounts = new Dictionary<string, long>();
        var all = records.EffectiveRecords();
        var visited = 0;
        var scripts = 0;
        var bodies = 0;
        var parsedBodies = 0;
        void Failure(string lane, FalloutPluginRecord record, Exception error)
        {
            var key = lane + ": " + error.Message;
            if (!failures.TryGetValue(key, out var instances)) failures.Add(key, instances = []);
            instances.Add(new { record.Signature, record.FormKey, winningPlugin = record.Plugin.Name });
        }
        foreach (var record in all)
        {
            recordCounts[record.Signature] = recordCounts.GetValueOrDefault(record.Signature) + 1;
            try
            {
                var fields = record.ReadSubrecords().ToArray();
                foreach (var field in fields)
                {
                    var key = (record.Signature, field.Signature);
                    if (!layouts.TryGetValue(key, out var layout)) layouts.Add(key, layout = new());
                    layout.Add(field.Data.Length);
                }
                if (record.Signature == "SCPT")
                {
                    ++scripts;
                    try { _ = FalloutScriptLocals.Read(record); }
                    catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                    { Failure("script-locals", record, error); }
                }
                foreach (var body in fields.Where(field => field.Signature == "SCTX"))
                {
                    ++bodies;
                    try
                    {
                        var source = FalloutDialogueTopic.ScriptText(body.Data.Span);
                        var programs = record.Signature == "SCPT" ? FalloutGameModeProgram.ReadEvents(source) :
                            [new FalloutScriptEventProgram("Result", null, FalloutGameModeProgram.Read("begin GameMode\n" + source + "\nend"))];
                        foreach (var program in programs)
                        {
                            if (!events.TryGetValue(program.Event, out var owners)) events.Add(program.Event, owners = []);
                            owners.Add(record.FormKey);
                        }
                        ++parsedBodies;
                    }
                    catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                    { Failure("script-parser", record, error); }
                }
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { Failure("record-reader", record, error); }
            if (++visited % 100000 == 0) Console.Error.WriteLine($"CORPUS records={visited}/{all.Count} seconds={watch.Elapsed.TotalSeconds:F1}");
        }
        var archiveReports = new List<object>();
        var assetTypes = new Dictionary<string, Layout>(StringComparer.OrdinalIgnoreCase);
        long members = 0;
        foreach (var path in content.ArchivePaths)
        {
            using var archive = new FalloutBsaArchive(path);
            var count = 0;
            long bytes = 0;
            foreach (var member in archive.MemberPaths)
            {
                var extent = archive.StoredExtent(member);
                var extension = Path.GetExtension(member);
                if (!assetTypes.TryGetValue(extension, out var layout)) assetTypes.Add(extension, layout = new());
                layout.Add(extent.Bytes);
                ++count; bytes += extent.Bytes;
            }
            members += count;
            archiveReports.Add(new { archive = Path.GetFileName(path), members = count, storedMemberBytes = bytes });
        }
        var groups = failures.OrderByDescending(pair => pair.Value.Count).Select(pair => new
        { failure = pair.Key, affectedRecords = pair.Value.Count, instances = pair.Value }).ToArray();
        var summary = new
        {
            schema = "opennv-owned-corpus-inventory/v1", content.SaveCompatibilityId,
            plugins = records.Plugins.Select(plugin => new { name = plugin.Plugin.Name, plugin.Sha256, plugin.Bytes }),
            winningRecords = visited, recordTypes = recordCounts, scripts, sourceBodies = bodies, parsedBodies,
            scriptEvents = events.OrderByDescending(pair => pair.Value.Count).Select(pair => new { name = pair.Key, owners = pair.Value.Count }),
            archives = archiveReports, archiveMembers = members,
            assetTypes = assetTypes.OrderByDescending(pair => pair.Value.Count).Select(pair => new { extension = pair.Key, layout = pair.Value.Report() }),
            failureGroups = groups.Length, failedInstances = groups.Sum(group => group.affectedRecords),
            seconds = watch.Elapsed.TotalSeconds,
            boundary = "Full loaded plugin payload/layout and selected BSA directory scan. Parsing is not execution; embedded bytecode, asset decoding, reachable branches, loose-file content, gameplay and parity are separate unverified lanes.",
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, options));
        File.WriteAllText(Path.Combine(directory, "failures.json"), JsonSerializer.Serialize(groups, options));
        File.WriteAllText(Path.Combine(directory, "record-layouts.json"), JsonSerializer.Serialize(layouts.OrderBy(pair => pair.Key.Record)
            .ThenBy(pair => pair.Key.Field).Select(pair => new { record = pair.Key.Record, field = pair.Key.Field, layout = pair.Value.Report() }), options));
        Console.WriteLine(JsonSerializer.Serialize(new { directory, summary.winningRecords, scripts, bodies, parsedBodies,
            members, summary.failureGroups, summary.failedInstances, summary.seconds,
            largestFailures = groups.Take(8).Select(group => new { group.failure, group.affectedRecords }) }));
        return 0; // Inventory succeeds while retaining unsupported cases in its report.
    }
}
