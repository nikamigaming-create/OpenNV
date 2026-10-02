using System.Globalization;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutInstallationSettings
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RuntimeLiveContentSource, FalloutInstallationSettings> Instances = new();
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private Lazy<IReadOnlyDictionary<string, float>> _floatDefaults = null!;
    private Lazy<IReadOnlyDictionary<string, bool>> _booleanDefaults = null!;
    private Lazy<FalloutRendererConfiguration> _renderer = null!;
    private readonly List<FalloutIniLayer> _iniLayers = [];
    private Lazy<FalloutNumericIniSettings> _numericIni = null!;
    internal FalloutRendererConfiguration Renderer => _renderer.Value;
    internal FalloutNumericIniSettings NumericIni => _numericIni.Value;

    internal static FalloutInstallationSettings Read(RuntimeLiveContentSource source) => Instances.GetValue(source, ReadInstallation);

    private static FalloutInstallationSettings ReadInstallation(RuntimeLiveContentSource source)
    {
        var settings = new FalloutInstallationSettings();
        settings._floatDefaults = new(() => FalloutExecutableStringTable.ReadFloatDefaults(
            Path.Combine(Path.GetDirectoryName(source.ContentRoot)!,
                source.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV.exe" : "Fallout3.exe")));
        settings._booleanDefaults = new(() => FalloutExecutableStringTable.ReadBooleanDefaults(
            Path.Combine(Path.GetDirectoryName(source.ContentRoot)!,
                source.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV.exe" : "Fallout3.exe")));
        settings._numericIni = new(() => settings.ReadNumericIni(source));
        settings.Add(Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "Fallout_default.ini"), true, FalloutIniCollection.Main);
        var gameFolder = source.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV" : "Fallout3";
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var user = Path.Combine(documents, "My Games", gameFolder);
        settings._renderer = new(() => FalloutRendererConfiguration.Read(File.ReadAllText(Path.Combine(user, "RendererInfo.txt"))));
        settings.Add(Path.Combine(user, "Fallout.ini"), false, FalloutIniCollection.Main);
        settings.Add(Path.Combine(user, "FalloutPrefs.ini"), false, FalloutIniCollection.Prefs, FalloutIniCollection.Renderer);
        settings.Add(Path.Combine(user, "FalloutCustom.ini"), false, FalloutIniCollection.Main);
        settings.Apply(source.Settings);
        return settings;
    }

    private FalloutNumericIniSettings ReadNumericIni(RuntimeLiveContentSource source)
    {
        if (source.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
            throw new NotSupportedException("This engine's executable INI-setting layout has not been admitted.");
        var executable = Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "FalloutNV.exe");
        var bytes = File.ReadAllBytes(executable);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        return new(FalloutExecutableStringTable.ReadIniDeclarations(bytes), _iniLayers, source.StackId + ":" + executable + ":" + hash);
    }

    internal static FalloutInstallationSettings ReadLayers(IEnumerable<string> paths, IEnumerable<FalloutInstallationSetting>? profile = null)
    {
        var settings = new FalloutInstallationSettings();
        foreach (var path in paths) settings.Add(path, true);
        settings._floatDefaults = new(() => new Dictionary<string, float>());
        settings._booleanDefaults = new(() => new Dictionary<string, bool>());
        settings._numericIni = new(() => throw new NotSupportedException("INI declarations have no bound executable owner."));
        settings.Apply(profile ?? []);
        return settings;
    }

    internal static FalloutInstallationSettings ReadIniLayers(Func<IReadOnlyList<FalloutIniDeclaration>> declarations,
        IReadOnlyList<FalloutIniLayer> layers, string source)
    {
        var settings = new FalloutInstallationSettings();
        var captured = layers.Select(layer => layer with { Values = Array.AsReadOnly(layer.Values.ToArray()) }).ToArray();
        settings._numericIni = new(() => new(declarations(), captured, source));
        return settings;
    }

    private void Apply(IEnumerable<FalloutInstallationSetting> profile)
    {
        var rows = profile.ToArray();
        foreach (var setting in rows)
        {
            if (string.IsNullOrWhiteSpace(setting.Section) || string.IsNullOrWhiteSpace(setting.Key) || setting.Value is null ||
                setting.Section.IndexOfAny(['/', '\r', '\n']) >= 0 || setting.Key.IndexOfAny(['/', '=', '\r', '\n']) >= 0 ||
                setting.Value.IndexOfAny(['\r', '\n']) >= 0)
                throw new InvalidDataException("Profile installation setting has an invalid identity or value.");
            _values[setting.Section + "/" + setting.Key] = setting.Value;
        }
        if (rows.Length != 0) _iniLayers.Add(new("profile", null, Array.AsReadOnly(rows)));
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
    internal bool Boolean(string section, string key)
    {
        if (_values.TryGetValue(section + "/" + key, out var value))
            return int.Parse(value, CultureInfo.InvariantCulture) != 0;
        return _booleanDefaults.Value.TryGetValue(key + ":" + section, out var enabled)
            ? enabled : throw new NotSupportedException($"Owned Boolean setting has no admitted default: [{section}] {key}.");
    }
    internal bool Contains(string section, string key) => _values.ContainsKey(section + "/" + key);
    internal float Number(string identity)
    {
        var separator = identity.LastIndexOf(':');
        if (separator <= 0 || separator == identity.Length - 1) throw new InvalidDataException("INI setting identity requires a section.");
        return Number(identity[(separator + 1)..], identity[..separator]);
    }

    private void Add(string path, bool required, params FalloutIniCollection[] collections)
    {
        if (!File.Exists(path))
        {
            if (required) throw new FileNotFoundException("Owned installation defaults are missing.", path);
            return;
        }
        var section = "";
        var rows = new Dictionary<string, FalloutInstallationSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim(); var value = line[(separator + 1)..].Trim();
            _values[section + "/" + key] = value;
            rows[section + "/" + key] = new(section, key, value);
        }
        foreach (var collection in collections) _iniLayers.Add(new(Path.GetFullPath(path), collection, Array.AsReadOnly(rows.Values.ToArray())));
    }
}
