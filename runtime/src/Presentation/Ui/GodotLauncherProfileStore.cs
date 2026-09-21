using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record GodotLauncherProfile(
    string CampaignId,
    string InstallRoot,
    string SavePath,
    string? BaseInstallRoot = null,
    IReadOnlyList<string>? DependencyRoots = null,
    IReadOnlyList<string>? EnabledMods = null,
    bool AutomaticModOrder = true);

/// <summary>
/// Stores only user-selected installation roots and OpenNV save locations.
/// Retail files remain read-only inputs and are never copied into this file.
/// </summary>
internal sealed class GodotLauncherProfileStore
{
    private const string Schema = "opennv-godot-launcher-profiles/v1";
    private readonly string _path;
    private readonly string _saveRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
    private readonly Dictionary<string, GodotLauncherProfile> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    private GodotLauncherProfileStore(string path, string saveRoot) => (_path, _saveRoot) = (path, saveRoot);

    internal static GodotLauncherProfileStore Load()
    {
        var path = ProjectSettings.GlobalizePath("user://launcher/profiles-v1.json");
        var store = Open(path, ProjectSettings.GlobalizePath("user://profiles"));
        store.ImportLegacyRegistrations();
        return store;
    }

    internal static GodotLauncherProfileStore Open(string path, string saveRoot)
    {
        var store = new GodotLauncherProfileStore(Path.GetFullPath(path), Path.GetFullPath(saveRoot));
        store.Read();
        return store;
    }

    internal bool TryGet(string campaignId, out GodotLauncherProfile profile) =>
        _profiles.TryGetValue(campaignId, out profile!);

    internal GodotLauncherProfile Save(string campaignId, string installRoot, string? baseInstallRoot = null,
        IReadOnlyList<string>? dependencyRoots = null)
    {
        campaignId = campaignId.ToLowerInvariant();
        var previous = _profiles.GetValueOrDefault(campaignId);
        var profile = new GodotLauncherProfile(
            campaignId,
            Path.GetFullPath(installRoot),
            SavePath(campaignId),
            baseInstallRoot is null ? null : Path.GetFullPath(baseInstallRoot),
            dependencyRoots?.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            previous?.EnabledMods,
            previous?.AutomaticModOrder ?? true);
        _profiles[campaignId] = profile;
        try { Persist(); }
        catch
        {
            if (previous is null) _profiles.Remove(campaignId);
            else _profiles[campaignId] = previous;
            throw;
        }
        return profile;
    }

    internal IReadOnlyList<string> EnabledMods(string campaignId) =>
        _profiles.GetValueOrDefault(campaignId)?.EnabledMods ?? [];

    internal bool AutomaticModOrder(string campaignId) => _profiles.GetValueOrDefault(campaignId)?.AutomaticModOrder ?? true;

    internal void SetAutomaticModOrder(string campaignId, bool automatic)
    {
        if (!_profiles.TryGetValue(campaignId, out var previous))
            throw new InvalidOperationException("Choose your game folder first.");
        var ids = previous.EnabledMods ?? [];
        if (!automatic && previous.AutomaticModOrder && ModStack(campaignId) is { } stack)
            ids = FalloutModLoadOrder.OrderMods(stack.Mods).Select(mod => mod.Id).ToArray();
        _profiles[campaignId] = previous with { AutomaticModOrder = automatic, EnabledMods = ids };
        try { Persist(); }
        catch { _profiles[campaignId] = previous; throw; }
    }

    internal void SetEnabledMods(string campaignId, IReadOnlyList<string> ids)
    {
        if (campaignId != "newvegas") throw new ArgumentException("These mods require New Vegas.");
        if (!_profiles.TryGetValue(campaignId, out var previous))
            throw new InvalidOperationException("Choose your New Vegas game folder first.");
        foreach (var id in ids) _ = FalloutModCatalog.Get(id);
        if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
            throw new ArgumentException("A mod can only be enabled once.");
        _profiles[campaignId] = previous with { EnabledMods = ids.ToArray() };
        try { Persist(); }
        catch { _profiles[campaignId] = previous; throw; }
    }

    internal FalloutModStackSelection? ModStack(string campaignId)
    {
        var ids = EnabledMods(campaignId);
        if (ids.Count == 0) return null;
        if (campaignId != "newvegas") throw new InvalidDataException("This game cannot use New Vegas mods.");
        return new(ids.Select(id =>
        {
            _ = FalloutModCatalog.Get(id);
            if (!_profiles.TryGetValue(id, out var profile))
                throw new InvalidDataException($"Choose a folder for {FalloutModCatalog.Get(id).Title}.");
            return new FalloutModSelection(id, profile.InstallRoot, profile.DependencyRoots ?? []);
        }).ToArray(), AutomaticModOrder(campaignId));
    }

    internal string LaunchSavePath(string campaignId)
    {
        var stack = ModStack(campaignId);
        if (stack is null) return _profiles[campaignId].SavePath;
        var identity = JsonSerializer.Serialize(stack.AutomaticOrder ? FalloutModLoadOrder.OrderMods(stack.Mods) : stack.Mods);
        if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..24];
        return Path.Combine(_saveRoot, campaignId, "mod-stacks", hash, SaveFileName(campaignId));
    }

    internal static string DefaultSavePath(string campaignId)
        => ProjectSettings.GlobalizePath($"user://profiles/{campaignId.ToLowerInvariant()}/{SaveFileName(campaignId)}");

    private string SavePath(string campaignId) => Path.Combine(_saveRoot, campaignId.ToLowerInvariant(), SaveFileName(campaignId));

    private static string SaveFileName(string campaignId)
    {
        var fileName = campaignId.ToLowerInvariant() switch
        {
            "fallout1" => "vault-dweller-v1.json",
            "fallout2" => "chosen-v1.json",
            "fallout3" => "capital-wasteland-v1.json",
            "newvegas" => "courier-v1.json",
            "jam" => "courier-jam-v1.json",
            "ttw" => "wastelands-v1.json",
            _ when FalloutModInstallation.IsMod(campaignId) => $"courier-{campaignId}-v1.json",
            _ => throw new ArgumentException($"Unknown campaign: {campaignId}", nameof(campaignId)),
        };
        return fileName;
    }

    private void Read()
    {
        if (!File.Exists(_path))
            return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
            var root = document.RootElement;
            if (!root.TryGetProperty("schema", out var schema) || schema.GetString() != Schema)
                return;
            if (!root.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
                return;
            foreach (var property in profiles.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                    continue;
                // Previous v1 writers emitted PascalCase record properties,
                // although the reader expected camelCase. Accept both.
                GodotLauncherProfile? profile;
                try { profile = property.Value.Deserialize<GodotLauncherProfile>(JsonOptions); }
                catch (JsonException) { continue; }
                if (profile is null || string.IsNullOrWhiteSpace(profile.CampaignId) ||
                    !property.Name.Equals(profile.CampaignId, StringComparison.OrdinalIgnoreCase) ||
                    !Path.IsPathFullyQualified(profile.InstallRoot ?? string.Empty) ||
                    !Path.IsPathFullyQualified(profile.SavePath ?? string.Empty) ||
                    profile.BaseInstallRoot is { } baseRoot && !Path.IsPathFullyQualified(baseRoot) ||
                    profile.DependencyRoots?.Any(path => !Path.IsPathFullyQualified(path ?? string.Empty)) == true)
                    continue;
                if (profile.EnabledMods is { } enabled && (property.Name != "newvegas" ||
                    enabled.Distinct(StringComparer.OrdinalIgnoreCase).Count() != enabled.Count ||
                    enabled.Any(id => !FalloutModInstallation.IsMod(id)))) continue;
                try { _ = SaveFileName(profile.CampaignId); }
                catch (ArgumentException) { continue; }
                _profiles[profile.CampaignId] = profile;
            }
        }
        catch (JsonException)
        {
            // A damaged launcher file must not prevent the user from selecting
            // a fresh installation. The old file remains recoverable on disk.
        }
        catch (IOException)
        {
            // Read-only or transient profile storage is reported when a user
            // explicitly registers a folder; startup stays usable.
        }
        catch (InvalidOperationException)
        {
            // A structurally invalid JSON value is treated like a stale profile
            // file; the picker remains available for a clean registration.
        }
    }

    private void ImportLegacyRegistrations()
    {
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            return;
        var legacy = Path.Combine(appData, "@open-nevada", "launcher");
        var changed = false;
        changed |= ImportDataRegistration(legacy, "newvegas", "newvegas-data-registration.json");
        changed |= ImportDataRegistration(legacy, "fallout3", "fallout3-data-registration.json");
        changed |= ImportFo1Profile(legacy);
        changed |= ImportFo2Profile(legacy);
        if (changed)
            Persist();
    }

    private bool ImportDataRegistration(string legacyRoot, string campaignId, string fileName)
    {
        if (_profiles.ContainsKey(campaignId))
            return false;
        var path = Path.Combine(legacyRoot, fileName);
        var root = ReadJson(path);
        if (root is null || !root.Value.TryGetProperty("dataRoot", out var dataRoot))
            return false;
        var selected = dataRoot.GetString();
        if (string.IsNullOrWhiteSpace(selected))
            return false;
        _profiles[campaignId] = new GodotLauncherProfile(campaignId, selected, SavePath(campaignId));
        return true;
    }

    private bool ImportFo1Profile(string legacyRoot)
    {
        if (_profiles.ContainsKey("fallout1"))
            return false;
        var profile = ReadJson(Path.Combine(legacyRoot, "profiles", "fallout1", "fallout1-profile.json"));
        if (profile is null || !profile.Value.TryGetProperty("install", out var install) ||
            !install.TryGetProperty("root", out var root))
            return false;
        var selected = root.GetString();
        if (string.IsNullOrWhiteSpace(selected))
            return false;
        _profiles["fallout1"] = new GodotLauncherProfile("fallout1", selected, SavePath("fallout1"));
        return true;
    }

    private bool ImportFo2Profile(string legacyRoot)
    {
        if (_profiles.ContainsKey("fallout2"))
            return false;
        var registration = ReadJson(Path.Combine(legacyRoot, "fallout2-profile-registration.json"));
        if (registration is null || !registration.Value.TryGetProperty("manifest", out var manifest))
            return false;
        var manifestPath = manifest.GetString();
        if (string.IsNullOrWhiteSpace(manifestPath))
            return false;
        var profile = ReadJson(manifestPath);
        if (profile is null || !profile.Value.TryGetProperty("install", out var install) ||
            !install.TryGetProperty("root", out var root))
            return false;
        var selected = root.GetString();
        if (string.IsNullOrWhiteSpace(selected))
            return false;
        _profiles["fallout2"] = new GodotLauncherProfile("fallout2", selected, SavePath("fallout2"));
        return true;
    }

    private static JsonElement? ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException(
            "Godot launcher profile path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = _path + ".next";
        var document = new
        {
            schema = Schema,
            profiles = _profiles,
        };
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(document, JsonOptions) + System.Environment.NewLine);
        File.Move(temporary, _path, overwrite: true);
    }
}
