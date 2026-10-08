using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAudit
{
    internal static int RunCommand(string[] arguments)
    {
        try { return Run(arguments); }
        catch (Exception error) when (error is IOException or ArgumentException or JsonException or FormatException or
            InvalidOperationException or NotSupportedException or KeyNotFoundException)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { audit = "cell-graph-command", exitCode = 2,
                lane = "command-or-component-failure", error = error.Message, completeComponentInventoryProduced = false }));
            return 2;
        }
    }

    internal sealed record Options(FalloutFormKey? Seed, string Output, string Configuration)
    {
        internal bool CompleteSource { get; init; } = true;
        internal bool CompressReports { get; init; }
        internal string? Metadata { get; init; }
        internal string? Checkpoint { get; init; }
        internal string? Snapshot { get; init; }
        internal Vector3? SampleNative { get; init; }
        internal FalloutModStackSelection? SourceSelection { get; init; }
    }

    internal static int Run(string[] arguments)
    {
        var options = ParseOptions(arguments);
        if (Directory.Exists(options.Output) || File.Exists(options.Output))
            throw new IOException("Cell-graph output must be a fresh directory.");
        var payload = File.ReadAllBytes(options.Configuration);
        using var configuration = JsonDocument.Parse(payload);
        if (configuration.RootElement.GetProperty("schema").GetString() != RuntimeConfiguration.ExpectedSchema)
            throw new InvalidDataException("Cell-graph configuration has a different runtime schema.");
        var units = configuration.RootElement.GetProperty("world").GetProperty("gameUnitsToMeters").GetSingle();
        if (!float.IsFinite(units) || units <= 0) throw new InvalidDataException("Cell-graph units are invalid.");
        using var source = DevelopmentLabSource.Open(arguments[0], options.SourceSelection);
        RequireOutputOutsideInputs(options.Output, source.ContentRoots.Append(arguments[0]));
        using var records = FalloutPluginStack.Load(source.PluginSources);
        Directory.CreateDirectory(options.Output);
        return RunComponent(source, records, options, units, Convert.ToHexString(SHA256.HashData(payload))) ? 0 : 1;
    }

    private static void RequireOutputOutsideInputs(string output, IEnumerable<string> inputs)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        foreach (var input in inputs)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (destination.Equals(root, comparison) || destination.StartsWith(prefix, comparison))
                throw new IOException("Cell-graph output must remain outside every owned input root.");
        }
    }

    internal static Options ParseOptions(string[] arguments)
    {
        if (arguments.Length < 2) throw new ArgumentException("cell-graph needs an installation and fresh output directory.");
        string? seed = null, configuration = null, metadata = null, checkpoint = null, snapshot = null;
        var completeSource = true;
        var compressReports = false;
        Vector3? sample = null;
        var sourceOptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 2; index < arguments.Length; index++)
        {
            var option = arguments[index];
            if (!seen.Add(option)) throw new ArgumentException("Repeated cell-graph option: " + option);
            string Value() => ++index < arguments.Length && !arguments[index].StartsWith("--", StringComparison.Ordinal)
                ? arguments[index] : throw new ArgumentException("Missing value for " + option);
            switch (option)
            {
                case "--seed": seed = Value(); break;
                case "--compress-reports": compressReports = true; break;
                case "--scope":
                    completeSource = Value() switch
                    {
                        "all" => true, "component" => false,
                        _ => throw new ArgumentException("Cell-graph scope must be all or component."),
                    };
                    break;
                case "--runtime-config": configuration = Value(); break;
                case "--metadata": metadata = Value(); break;
                case "--checkpoint": checkpoint = Value(); break;
                case "--snapshot": snapshot = Value(); break;
                case "--sample-native":
                    sample = new(float.Parse(Value(), CultureInfo.InvariantCulture), float.Parse(Value(), CultureInfo.InvariantCulture),
                        float.Parse(Value(), CultureInfo.InvariantCulture));
                    if (!sample.Value.IsFinite()) throw new ArgumentException("Native sample must be finite.");
                    break;
                case "--mod" or "--mod-stack" or "--mod-order":
                    DevelopmentLabSource.ReadOption(arguments, ref index, sourceOptions); break;
                default: throw new ArgumentException("Unknown cell-graph option: " + option);
            }
        }
        if (configuration is null) throw new ArgumentException("cell-graph requires --runtime-config.");
        if (seed is null && (!completeSource || snapshot is not null || sample is not null))
            throw new ArgumentException("cell-graph requires --seed for component scope, snapshot or native sample.");
        return new(seed is null ? null : ParseForm(seed), Path.GetFullPath(arguments[1]), Path.GetFullPath(configuration))
        {
            CompleteSource = completeSource,
            CompressReports = compressReports,
            Metadata = metadata, Checkpoint = checkpoint is null ? null : Path.GetFullPath(checkpoint),
            Snapshot = snapshot is null ? null : Path.GetFullPath(snapshot), SampleNative = sample,
            SourceSelection = DevelopmentLabSource.ReadSelection(sourceOptions),
        };
    }

    private static FalloutFormKey ParseForm(string token)
    {
        var split = token.LastIndexOf(':');
        if (split <= 0 || !uint.TryParse(token[(split + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ||
            id is 0 or > FalloutFormKey.ObjectIdMask) throw new ArgumentException("Use plugin:hex-object for a cell-graph seed.");
        return new(token[..split], id);
    }
}
