using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Gameplay.Settings;

public sealed class RuntimeSettingsState
{
    public const string Schema = "opennv-runtime-settings/v1";
    public const float NeutralMouseSensitivityScale = 1.0f;
    private readonly string _path;

    private RuntimeSettingsState(string path, float mouseSensitivityScale)
    {
        _path = path;
        MouseSensitivityScale = RequireScale(mouseSensitivityScale);
    }

    public float MouseSensitivityScale { get; private set; }
    public string Path => _path;

    public static RuntimeSettingsState Load(string? configuredPath = null)
    {
        var path = ResolvePath(configuredPath ?? "user://settings/runtime-settings-v1.json");
        if (!File.Exists(path))
            return new RuntimeSettingsState(path, NeutralMouseSensitivityScale);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != Schema)
            throw new InvalidOperationException("OpenNV settings use an unsupported schema.");
        return new RuntimeSettingsState(
            path,
            root.GetProperty("mouseSensitivityScale").GetSingle());
    }

    public void SetMouseSensitivityScale(float value)
    {
        MouseSensitivityScale = RequireScale(value);
        Save();
    }

    public void RestoreMouseSensitivityDefault() =>
        SetMouseSensitivityScale(NeutralMouseSensitivityScale);

    public float ApplyMouseSensitivity(float configuredRadiansPerPixel)
    {
        if (!float.IsFinite(configuredRadiansPerPixel) || configuredRadiansPerPixel <= 0.0f)
            throw new ArgumentOutOfRangeException(
                nameof(configuredRadiansPerPixel),
                "Configured mouse sensitivity must be positive and finite.");
        return configuredRadiansPerPixel * MouseSensitivityScale;
    }

    private void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(_path) ?? throw new InvalidOperationException(
            "OpenNV settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                new
                {
                    schema = Schema,
                    mouseSensitivityScale = MouseSensitivityScale,
                },
                new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine);
        File.Move(temporary, _path, overwrite: true);
    }

    private static float RequireScale(float value) =>
        !float.IsFinite(value) || value <= 0.0f
            ? throw new InvalidOperationException(
                "OpenNV mouse-sensitivity scale must be positive and finite.")
            : value;

    private static string ResolvePath(string path) =>
        path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : System.IO.Path.GetFullPath(path);
}
