using System.Globalization;
using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.AnimationCompare;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static int Main(string[] arguments)
    {
        try
        {
            if (arguments.Length == 0) throw new ArgumentException(Usage);
            switch (arguments[0])
            {
                case "catalog":
                    var command = DevelopmentLabSource.ParseCommand(arguments);
                    if (command.Arguments.Length != 3) throw new ArgumentException(Usage);
                    DevelopmentLabSource.Configure(command.Arguments[1], command.Selection);
                    using (var content = RuntimeLiveContentSource.Current!)
                    using (var records = FalloutPluginStack.Load(content.PluginSources))
                    {
                        var catalog = Catalog.Build(records, content, command.Arguments[2], value => Console.Error.WriteLine(value));
                        Console.WriteLine(JsonSerializer.Serialize(catalog, Json));
                        return catalog.Failures == 0 ? 0 : 2;
                    }
                case "compare":
                    return Compare(arguments);
                case "run":
                    return Run(arguments).GetAwaiter().GetResult();
                case "observe":
                    return ObservationCommand.Run(arguments).GetAwaiter().GetResult();
                default:
                    throw new ArgumentException(Usage);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "failed", error = error.Message }, Json));
            return 1;
        }
    }

    private sealed record RunConfiguration(ParityRigPlan Plan, OpenNvRigConfiguration OpenNv, ExternalRigConfiguration Retail);

    private static async Task<int> Run(string[] arguments)
    {
        if (arguments.Length != 3) throw new ArgumentException(Usage);
        var input = new FileInfo(arguments[1]);
        if (!input.Exists || input.Length is <= 0 or > 16 * 1024 * 1024)
            throw new InvalidDataException("A bounded explicit rig configuration is required.");
        var configuration = JsonSerializer.Deserialize<RunConfiguration>(File.ReadAllText(input.FullName), Json)
            ?? throw new InvalidDataException("Rig configuration is empty.");
        var output = Path.GetFullPath(arguments[2]);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Use a fresh rig report path.");
        if (configuration.Retail.Engine != "retail") throw new ArgumentException("The external control must identify retail.");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            using var native = new ExternalRigEngineControl(configuration.Retail);
            using var opennv = new OpenNvRigEngineControl(configuration.OpenNv);
            var result = await new ParityRigRunner().RunAsync(configuration.Plan, [native, opennv], cancellation.Token);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(result, Json));
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                output,
                cases = result.Cases.Count,
                dispositions = result.Cases.GroupBy(item => item.Disposition).ToDictionary(group => group.Key, group => group.Count()),
                errors = result.Errors,
                worldPolicy = result.WorldPolicy,
                parityCertified = false,
                boundary = "Only the explicit plan was attempted. Observed actions require comparison; source catalog coverage remains separate."
            }, Json));
            return result.Errors.Count == 0 && result.Cases.All(item => item.Disposition == "observed") ? 0 : 2;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static int Compare(string[] arguments)
    {
        if (arguments.Length < 6) throw new ArgumentException(Usage);
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 4; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal) ||
                !options.TryAdd(arguments[index], arguments[index + 1])) throw new ArgumentException("Invalid or duplicate comparison option.");
        }
        string[] allowed = ["--plugins", "--stage", "--units", "--phase-tolerance", "--angle-tolerance",
            "--position-tolerance", "--retail-video", "--opennv-video"];
        if (options.Keys.Except(allowed, StringComparer.Ordinal).Any() || !options.TryGetValue("--plugins", out var plugins))
            throw new ArgumentException("Explicit --plugins in native load order is required. " + Usage);
        double Number(string key, double fallback) => options.TryGetValue(key, out var value)
            ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
        var configuration = new PoseComparisonOptions(PhaseToleranceSeconds: Number("--phase-tolerance", .025),
            DifferenceDegrees: Number("--angle-tolerance", .5), DifferenceMeters: Number("--position-tolerance", .001));
        var native = PoseCaptureReader.ReadNative(arguments[1], plugins.Split(',', StringSplitOptions.TrimEntries), Number("--units", .0142875));
        var opennv = PoseCaptureReader.ReadOpenNv(arguments[2], options.GetValueOrDefault("--stage", "post-draw"));
        var report = PoseComparison.Compare(native, opennv, configuration);
        var output = Path.GetFullPath(arguments[3]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Use a fresh comparison output directory.");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "comparison.json"), JsonSerializer.Serialize(report, Json));
        ComparisonHtml.Write(Path.Combine(output, "comparison.html"), report,
            options.GetValueOrDefault("--retail-video"), options.GetValueOrDefault("--opennv-video"));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "compared; review findings and unjoined lanes",
            output,
            actors = report.Actors.Count,
            bones = report.Actors.Sum(actor => actor.Bones.Count),
            movingBones = report.Actors.Sum(actor => actor.Bones.Count(bone => bone.OpenNvMotion.Moving || bone.RetailMotion?.Moving == true)),
            investigationRows = report.Actors.Sum(actor => actor.Bones.Count(bone => bone.Status == "investigate")),
            phaseJoinedSamples = report.Actors.Sum(actor => actor.PhaseJoinedSamples),
            parityCertified = report.ParityCertified
        }, Json));
        return 0;
    }

    private const string Usage = "run <explicit-private-rig-configuration.json> <fresh-report.json>\n" +
        "catalog <owned-installation> <fresh-output-directory> [--mod-stack <file>]\n" +
        "compare <native.pose.jsonl> <opennv.pose.json> <fresh-output-directory> --plugins <native-load-order-comma-list> " +
        "[--stage post-draw|post-physics] [--units 0.0142875] [--retail-video <file>] [--opennv-video <file>]";
}
