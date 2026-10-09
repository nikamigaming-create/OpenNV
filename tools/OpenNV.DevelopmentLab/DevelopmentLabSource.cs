using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class DevelopmentLabSource
{
    internal sealed record Command(string[] Arguments, FalloutModStackSelection? Selection);

    internal static Command ParseCommand(string[] arguments)
    {
        var first = Array.FindIndex(arguments, IsOption);
        if (first is >= 0 and < 3) throw new ArgumentException("Owned source options follow the command, installation and command argument.");
        if (arguments.Length > 3 && (arguments[0] is "corpus" or "quest-graph") && first != 3)
            throw new ArgumentException("Unknown owned source option or unexpected audit argument: " + arguments[3]);
        if (first < 0) return new(arguments, null);
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = first; index < arguments.Length; index++) ReadOption(arguments, ref index, options);
        return new(arguments[..first], ReadSelection(options));
    }

    internal static bool IsOption(string option) => option is "--mod" or "--mod-stack" or "--mod-order";

    internal static void ReadOption(string[] arguments, ref int index, IDictionary<string, string> options)
    {
        var option = arguments[index];
        var key = option switch
        {
            "--mod" => "mod-id",
            "--mod-stack" => "mod-stack",
            "--mod-order" => "mod-order",
            _ => throw new ArgumentException("Unknown owned source option: " + option),
        };
        if (options.ContainsKey(key)) throw new ArgumentException("Repeated owned source option: " + option);
        if (key == "mod-id" && options.ContainsKey("mod-stack") || key == "mod-stack" && options.ContainsKey("mod-id"))
            throw new ArgumentException("Select one mod stack instead of combining it with --mod.");
        var value = Value(arguments, ref index, option);
        if (key == "mod-id")
        {
            options.Add(key, value);
            options.Add("mod-root", Value(arguments, ref index, option));
            var dependencies = arguments[(index + 1)..];
            if (dependencies.Any(dependency => dependency.StartsWith("--", StringComparison.Ordinal)))
                throw new ArgumentException("--mod and its dependency roots must be last; source selections cannot be combined.");
            options.Add("mod-additional-roots", JsonSerializer.Serialize(dependencies));
            index = arguments.Length;
        }
        else options.Add(key, key == "mod-stack" ? File.ReadAllText(Path.GetFullPath(value)) : value);
    }

    internal static FalloutModStackSelection? ReadSelection(IReadOnlyDictionary<string, string> options)
    {
        if (options.ContainsKey("mod-order") && !options.ContainsKey("mod-stack"))
            throw new ArgumentException("--mod-order requires --mod-stack.");
        return FalloutModStackSelection.ReadOptions(options);
    }

    internal static RuntimeLiveContentSource Open(string selectedRoot, FalloutModStackSelection? selection) =>
        selection is null ? RuntimeLiveContentSource.Open(selectedRoot, Campaign(selectedRoot)) : selection.Resolve(selectedRoot).OpenSource();

    internal static void Configure(string selectedRoot, FalloutModStackSelection? selection)
    {
        if (selection is null) RuntimeLiveContentSource.Configure(selectedRoot, Campaign(selectedRoot));
        else
        {
            var resolved = selection.Resolve(selectedRoot);
            RuntimeLiveContentSource.Configure(resolved.BaseInstallation.InstallRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                resolved.ContentRoots.Skip(1).ToArray(), resolved.ActivePlugins, resolved.Settings);
            var dependency = resolved.Dependencies.SingleOrDefault(row =>
                row.LogicalPath.Equals("nvse_1_4.dll", StringComparison.OrdinalIgnoreCase));
            if (dependency?.SourcePath is { } host)
            {
                var source = RuntimeLiveContentSource.Current ??
                    throw new InvalidOperationException("Selected native dependency has no configured content owner.");
                source.BindNativePluginHostDependency(new(host,
                    "actual-development-lab-selected-stack-dependency:" + source.StackId));
            }
        }
    }

    private static string Value(string[] arguments, ref int index, string option) =>
        ++index < arguments.Length && !string.IsNullOrWhiteSpace(arguments[index]) && !arguments[index].StartsWith("--", StringComparison.Ordinal)
            ? arguments[index] : throw new ArgumentException("Missing value for " + option);

    internal static string Campaign(string selectedRoot) => NativeGameInstallation.Detect(selectedRoot).Game switch
    {
        NativeGame.FalloutNewVegas => RuntimeLiveContentSource.FalloutNewVegasGame,
        NativeGame.Fallout3 => RuntimeLiveContentSource.Fallout3Game,
        _ => throw new InvalidDataException("The direct ESM/BSA development lab requires a Fallout 3 or Fallout: New Vegas installation."),
    };
}
