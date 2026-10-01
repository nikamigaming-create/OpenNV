using System.Globalization;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutInstallationSettings
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RuntimeLiveContentSource, FalloutInstallationSettings> Instances = new();
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private Lazy<IReadOnlyDictionary<string, float>> _floatDefaults = null!;
    private Lazy<FalloutRendererConfiguration> _renderer = null!;
    internal FalloutRendererConfiguration Renderer => _renderer.Value;

    internal static FalloutInstallationSettings Read(RuntimeLiveContentSource source) => Instances.GetValue(source, ReadInstallation);

    private static FalloutInstallationSettings ReadInstallation(RuntimeLiveContentSource source)
    {
        var settings = new FalloutInstallationSettings();
        settings._floatDefaults = new(() => FalloutExecutableStringTable.ReadFloatDefaults(
            Path.Combine(Path.GetDirectoryName(source.ContentRoot)!,
                source.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV.exe" : "Fallout3.exe")));
        settings.Add(Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "Fallout_default.ini"), true);
        var gameFolder = source.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV" : "Fallout3";
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var user = Path.Combine(documents, "My Games", gameFolder);
        settings._renderer = new(() => FalloutRendererConfiguration.Read(File.ReadAllText(Path.Combine(user, "RendererInfo.txt"))));
        settings.Add(Path.Combine(user, "Fallout.ini"), false);
        settings.Add(Path.Combine(user, "FalloutPrefs.ini"), false);
        settings.Add(Path.Combine(user, "FalloutCustom.ini"), false);
        settings.Apply(source.Settings);
        return settings;
    }

    internal static FalloutInstallationSettings ReadLayers(IEnumerable<string> paths, IEnumerable<FalloutInstallationSetting>? profile = null)
    {
        var settings = new FalloutInstallationSettings();
        foreach (var path in paths) settings.Add(path, true);
        settings._floatDefaults = new(() => new Dictionary<string, float>());
        settings.Apply(profile ?? []);
        return settings;
    }

    private void Apply(IEnumerable<FalloutInstallationSetting> profile)
    {
        foreach (var setting in profile)
        {
            if (string.IsNullOrWhiteSpace(setting.Section) || string.IsNullOrWhiteSpace(setting.Key) || setting.Value is null ||
                setting.Section.IndexOfAny(['/', '\r', '\n']) >= 0 || setting.Key.IndexOfAny(['/', '=', '\r', '\n']) >= 0 ||
                setting.Value.IndexOfAny(['\r', '\n']) >= 0)
                throw new InvalidDataException("Profile installation setting has an invalid identity or value.");
            _values[setting.Section + "/" + setting.Key] = setting.Value;
        }
    }

    internal string Require(string section, string key) => _values.TryGetValue(section + "/" + key, out var value)
        ? value : throw new InvalidDataException($"Owned installation setting is missing: [{section}] {key}.");
    internal float Number(string section, string key)
    {
        if (_values.TryGetValue(section + "/" + key, out var value)) return float.Parse(value, CultureInfo.InvariantCulture);
        return _floatDefaults.Value.TryGetValue(key + ":" + section, out var number)
            ? number : throw new NotSupportedException($"Owned float setting has no admitted default: [{section}] {key}.");
    }
    internal uint Unsigned(string section, string key) => uint.Parse(Require(section, key), CultureInfo.InvariantCulture);
    internal bool Contains(string section, string key) => _values.ContainsKey(section + "/" + key);
    internal float Number(string identity)
    {
        var separator = identity.LastIndexOf(':');
        if (separator <= 0 || separator == identity.Length - 1) throw new InvalidDataException("INI setting identity requires a section.");
        return Number(identity[(separator + 1)..], identity[..separator]);
    }

    private void Add(string path, bool required)
    {
        if (!File.Exists(path))
        {
            if (required) throw new FileNotFoundException("Owned installation defaults are missing.", path);
            return;
        }
        var section = "";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            var separator = line.IndexOf('=');
            if (separator > 0) _values[section + "/" + line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
    }
}
