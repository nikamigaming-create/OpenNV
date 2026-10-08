using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class NumericIniSettingProbe
{
    internal static void Owned(string installation, string identity, string[] selection)
    {
        Require(RuntimeLiveContentSource.Current is null, "The INI audit requires no ambient content source.");
        using var content = OpenIniSource(installation, selection);
        content.ArchiveWarmup.GetAwaiter().GetResult();
        var executable = content.FalloutExecutablePath;
        var executableBytes = File.ReadAllBytes(executable);
        var executableHash = Convert.ToHexString(SHA256.HashData(executableBytes)).ToLowerInvariant();
        var declarations = FalloutExecutableStringTable.ReadIniDeclarations(executableBytes);
        var candidates = declarations.Where(row => row.Name.Equals(identity, StringComparison.OrdinalIgnoreCase)).ToArray();
        var declaration = candidates.SingleOrDefault(row => row.Collection == FalloutIniCollection.Prefs) ??
            candidates.SingleOrDefault(row => row.Collection == FalloutIniCollection.Main) ??
            throw new NotSupportedException("The selected source has no admitted Main/Prefs INI declaration: " + identity);
        Require(declaration.Kind == 'f' && declaration.NumericDefault is { }, "This native Float32 audit requires a canonical float declaration.");
        var gameFolder = content.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV" : "Fallout3";
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", gameFolder);
        var main = new[] { Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "Fallout_default.ini"),
            Path.Combine(user, "Fallout.ini"), Path.Combine(user, "FalloutCustom.ini") };
        var prefs = new[] { Path.Combine(user, "FalloutPrefs.ini") };
        var iniHashes = main.Concat(prefs).Where(File.Exists).ToDictionary(Path.GetFullPath,
            path => SHA256.HashData(File.ReadAllBytes(path)), StringComparer.OrdinalIgnoreCase);
        var expected = (float)declaration.NumericDefault!.Value;
        var origin = content.StackId + ":" + executable + ":" + executableHash;
        foreach (var path in declaration.Collection == FalloutIniCollection.Prefs ? prefs : main)
            if (ReadIniFloat(path, identity) is { } value) { expected = value; origin = Path.GetFullPath(path); }
        foreach (var row in content.Settings)
            if ((row.Key + ":" + row.Section).Equals(identity, StringComparison.OrdinalIgnoreCase))
            { expected = float.Parse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture); origin = "profile"; }
        Require(float.IsFinite(expected), "Owned INI input has a non-finite Float32.");

        using var records = FalloutPluginStack.Load(content.PluginSources);
        var pluginHashes = records.Plugins.ToDictionary(plugin => plugin.Plugin.Path,
            plugin => Convert.FromHexString(plugin.Sha256), StringComparer.OrdinalIgnoreCase);
        var actual = records.IniSettings.Float(identity);
        var resolved = records.IniSettings.Find(FalloutIniCollection.Prefs, identity) ??
            records.IniSettings.Find(FalloutIniCollection.Main, identity);
        Require(actual == expected && records.IniSettings.Read(identity) == (double)expected &&
            records.IniSettings.Get(identity) == (double)expected && resolved is not null &&
            resolved.Declaration == declaration && resolved.Origin == origin,
            "Native INI lookup differs from independently read executable/file/profile precedence.");
        if (identity.Contains(':'))
        {
            Require(records.NumericSettings.Get(identity) == -1, "An INI identity entered the game-setting script registry.");
            Reject(() => FalloutGameSettingFloats.Read(records, identity));
        }
        using var cold = FalloutPluginStack.Load(content.PluginSources);
        Require(cold.IniSettings.Float(identity.ToUpperInvariant()) == actual && RuntimeLiveContentSource.Current is null,
            "Cold source lookup changed the canonical type/value or required ambient state.");
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant() == executableHash &&
            iniHashes.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).AsSpan().SequenceEqual(pair.Value)) &&
            pluginHashes.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).AsSpan().SequenceEqual(pair.Value)),
            "Owned executable, INI or plugin bytes changed during the read-only audit.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-numeric-ini-native-read/v1",
            content.Game,
            content.SaveCompatibilityId,
            content.ContentRoots,
            activePlugins = content.PluginSources.Select(plugin => plugin.Name),
            profile = content.Settings,
            executableSha256 = executableHash,
            iniFiles = iniHashes.Select(pair => new { path = pair.Key, sha256 = Convert.ToHexString(pair.Value).ToLowerInvariant() }),
            setting = new { name = declaration.Name, kind = declaration.Kind.ToString(), collection = declaration.Collection.ToString(),
                executableDefault = declaration.NumericDefault, expected, actual, origin },
            sourceReadonly = true,
            coldLookup = true,
            ambientSource = false,
            gameSettingRegistrySeparate = true,
            boundary = "typed-setting-resolution-only;native-decal-lifetime-pixels-and-retail-parity-unverified",
        }));
    }

    private static RuntimeLiveContentSource OpenIniSource(string installation, string[] selection)
    {
        if (selection.Length == 0) return RuntimeLiveContentSource.Open(installation, DevelopmentLabSource.Campaign(installation));
        if (selection is ["--mod-stack", var file])
        {
            var mods = JsonSerializer.Deserialize<FalloutModSelection[]>(File.ReadAllText(file)) ??
                throw new InvalidDataException("The selected mods must be a list.");
            return new FalloutModStackSelection(mods).Resolve(installation).OpenSource();
        }
        if (selection.Length >= 3 && selection[0] == "--mod" &&
            selection[1..].All(value => !value.StartsWith("--", StringComparison.Ordinal)))
            return new FalloutModStackSelection([new(selection[1], selection[2], selection[3..])]).Resolve(installation).OpenSource();
        throw new ArgumentException("Select an installation, --mod id root [additional roots...], or --mod-stack selection-list.json.");
    }

    private static float? ReadIniFloat(string path, string identity)
    {
        var separator = identity.LastIndexOf(':');
        if (!File.Exists(path) || separator <= 0 || separator == identity.Length - 1) return null;
        var key = identity[..separator]; var wantedSection = identity[(separator + 1)..];
        var section = ""; float? found = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            var equals = line.IndexOf('=');
            if (equals > 0 && section.Equals(wantedSection, StringComparison.OrdinalIgnoreCase) &&
                line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                found = float.Parse(line[(equals + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        return found;
    }
}
