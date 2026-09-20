using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record GodotLauncherProfile(
    string CampaignId,
    string InstallRoot,
    string SavePath);

/// <summary>
/// Stores only user-selected installation roots and OpenNV save locations.
/// Retail files remain read-only inputs and are never copied into this file.
/// </summary>
internal sealed class GodotLauncherProfileStore
{
    private const string Schema = "opennv-godot-launcher-profiles/v1";
    private readonly string _path;
    private readonly Dictionary<string, GodotLauncherProfile> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    private GodotLauncherProfileStore(string path) => _path = path;

    internal static GodotLauncherProfileStore Load()
    {
        var path = ProjectSettings.GlobalizePath("user://launcher/profiles-v1.json");
        var store = new GodotLauncherProfileStore(path);
        store.Read();
        store.ImportLegacyRegistrations();
        return store;
    }

    internal bool TryGet(string campaignId, out GodotLauncherProfile profile) =>
        _profiles.TryGetValue(campaignId, out profile!);

    internal GodotLauncherProfile Save(string campaignId, string installRoot)
    {
        var profile = new GodotLauncherProfile(
            campaignId,
            Path.GetFullPath(installRoot),
            DefaultSavePath(campaignId));
        _profiles[campaignId] = profile;
        Persist();
        return profile;
    }

    internal static string DefaultSavePath(string campaignId)
    {
        var fileName = campaignId.ToLowerInvariant() switch
        {
            "fallout1" => "vault-dweller-v1.json",
            "fallout2" => "chosen-v1.json",
            "fallout3" => "capital-wasteland-v1.json",
            "newvegas" => "courier-v1.json",
            _ => throw new ArgumentException($"Unknown standalone campaign: {campaignId}", nameof(campaignId)),
        };
        return ProjectSettings.GlobalizePath($"user://profiles/{campaignId.ToLowerInvariant()}/{fileName}");
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
                if (property.Value.ValueKind != JsonValueKind.Object ||
                    !property.Value.TryGetProperty("campaignId", out var campaign) ||
                    !property.Value.TryGetProperty("installRoot", out var install) ||
                    !property.Value.TryGetProperty("savePath", out var save))
                    continue;
                var campaignId = campaign.GetString();
                var installRoot = install.GetString();
                var savePath = save.GetString();
                if (string.IsNullOrWhiteSpace(campaignId) ||
                    string.IsNullOrWhiteSpace(installRoot) ||
                    string.IsNullOrWhiteSpace(savePath))
                    continue;
                _profiles[campaignId] = new GodotLauncherProfile(campaignId, installRoot, savePath);
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
        _profiles[campaignId] = new GodotLauncherProfile(campaignId, selected, DefaultSavePath(campaignId));
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
        _profiles["fallout1"] = new GodotLauncherProfile("fallout1", selected, DefaultSavePath("fallout1"));
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
        _profiles["fallout2"] = new GodotLauncherProfile("fallout2", selected, DefaultSavePath("fallout2"));
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
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine);
        File.Move(temporary, _path, overwrite: true);
    }
}
