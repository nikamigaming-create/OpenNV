using OpenNV.Runtime.Gameplay.Settings;

var directory = Path.Combine(
    Path.GetTempPath(),
    "opennv-runtime-settings-" + Guid.NewGuid().ToString("N"));
var path = Path.Combine(directory, "settings.json");
try
{
    var settings = RuntimeSettingsState.Load(path);
    if (settings.MouseSensitivityScale != RuntimeSettingsState.NeutralMouseSensitivityScale ||
        File.Exists(path))
        throw new InvalidOperationException(
            "Missing settings did not resolve to the non-writing neutral default.");

    var configured = 0.0022f;
    var exercised = RuntimeSettingsState.NeutralMouseSensitivityScale +
        RuntimeSettingsState.NeutralMouseSensitivityScale;
    settings.SetMouseSensitivityScale(exercised);
    if (!File.Exists(path) || settings.ApplyMouseSensitivity(configured) != configured + configured)
        throw new InvalidOperationException(
            "Persisted sensitivity did not affect the shared runtime value.");

    var restored = RuntimeSettingsState.Load(path);
    if (restored.MouseSensitivityScale != exercised ||
        restored.ApplyMouseSensitivity(configured) != configured + configured)
        throw new InvalidOperationException(
            "Runtime settings did not cold-restore their effective value.");

    restored.RestoreMouseSensitivityDefault();
    if (RuntimeSettingsState.Load(path).MouseSensitivityScale !=
        RuntimeSettingsState.NeutralMouseSensitivityScale)
        throw new InvalidOperationException("Configured sensitivity default did not persist.");

    Console.WriteLine("OPENNV_RUNTIME_SETTINGS_PASS persistence=atomic consumer=mouse-look");
}
finally
{
    if (Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
}
