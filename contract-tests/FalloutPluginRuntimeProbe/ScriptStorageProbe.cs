using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class ScriptStorageProbe
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opennv-script-storage-{Guid.NewGuid():N}");
        var overlay = Path.Combine(root, "profile", "script-config");
        Directory.CreateDirectory(overlay);
        try
        {
            var source = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Config/Defaults.ini"] = Encoding.UTF8.GetBytes(
                    "; owned source default\n[General]\nExisting=2\nKeep=source ; inline comment\nBad=not-a-float\n[Other]\nRetain=1\n"),
                ["Config/Example.ini"] = Encoding.UTF8.GetBytes("[General]\nDefault=7.5\n"),
            };
            var ini = new FalloutScriptIniStore(path => source.TryGetValue(path, out var bytes) ? bytes : null, overlay);
        Require(ini.GetFloat("general:existing", "Defaults.ini", "Caller.esp") == 2,
            "INI section/key matching is not case-insensitive.");
        Require(ini.GetString("GENERAL/Keep", "Defaults.ini", "Caller.esp") == "source" &&
            ini.GetFloat("General:Bad", "Defaults.ini", "Caller.esp") == 0 &&
            ini.GetFloat("General:Missing", "Defaults.ini", "Caller.esp") == 0 &&
            ini.GetString("General:Missing", "Defaults.ini", "Caller.esp") == string.Empty,
            "INI missing or malformed values do not use the reached zero-value contract.");
        Require(ini.GetFloat("General:Default", null, "Example.esp") == 7.5,
            "INI omitted filename did not select the calling plugin's .ini.");

        ini.SetFloat("General:Existing", 4.25, "Defaults.ini", "Caller.esp");
        ini.SetString("General:Added", "overlay", "Defaults.ini", "Caller.esp");
        var written = File.ReadAllText(Path.Combine(overlay, "Defaults.ini"), Encoding.UTF8);
        Require(written.Contains("Existing=4.25", StringComparison.Ordinal) &&
            written.Contains("Keep=source", StringComparison.Ordinal) &&
            written.Contains("Retain=1", StringComparison.Ordinal) &&
            ini.GetFloat("General:Existing", "Defaults.ini", "Caller.esp") == 4.25,
            "INI writes did not retain source defaults and update the user overlay.");
        Require(source["Config/Defaults.ini"].SequenceEqual(Encoding.UTF8.GetBytes(
                "; owned source default\n[General]\nExisting=2\nKeep=source ; inline comment\nBad=not-a-float\n[Other]\nRetain=1\n")),
            "INI writes changed the selected source graph.");
        ini.SetString("General:Default", "caller", null, "Caller.esp");
        Require(File.Exists(Path.Combine(overlay, "Caller.ini")) &&
            ini.GetString("General:Default", null, "Caller.esp") == "caller",
            "INI default filename writes did not stay in the profile overlay.");
        Reject(() => ini.GetString("General:Bad", "../escape.ini", "Caller.esp"));
        Reject(() => ini.GetString("General:Bad", "C:\\escape.ini", "Caller.esp"));
        Require(!Directory.Exists(Path.Combine(root, "Config")),
            "The synthetic source namespace was used as a write target.");

        var owner = new FalloutFormKey("FalloutNV.esm", 0x14);
        var storedForm = new FalloutFormKey("FalloutNV.esm", 0x123);
        var auxiliary = new FalloutAuxiliaryStore();
        Require(auxiliary.SetFloat(owner, "JAM.esp", "*_Public", 1.5) &&
            auxiliary.GetFloat(owner, "Other.esp", "*_Public") == 1.5,
            "Temporary public auxiliary variables were not shared across callers.");
        Require(auxiliary.SetFloat(owner, "JAM.esp", "*Private", 2.5) &&
            auxiliary.GetFloat(owner, "Other.esp", "*Private") == 0 &&
            auxiliary.GetFloat(owner, "JAM.esp", "*Private") == 2.5,
            "Temporary private auxiliary variables did not follow the calling plugin.");
        Require(auxiliary.SetFloat(owner, "JAM.esp", "_Saved", 3) &&
            auxiliary.SetFloat(owner, "JAM.esp", "Saved", 4) &&
            auxiliary.SetForm(owner, "JAM.esp", "_Form", storedForm) &&
            auxiliary.GetType(owner, "Other.esp", "_Saved") == 1 &&
            auxiliary.GetType(owner, "Other.esp", "Saved") == 0 &&
            auxiliary.GetType(owner, "JAM.esp", "_Form") == 2 &&
            auxiliary.GetForm(owner, "JAM.esp", "_Form") == storedForm,
            "Permanent auxiliary visibility or typed form storage is incorrect.");
        Require(auxiliary.SetFloat(owner, "JAM.esp", "_Array", 10, 0) &&
            auxiliary.SetFloat(owner, "JAM.esp", "_Array", 20, 1) &&
            auxiliary.SetFloat(owner, "JAM.esp", "_Array", 30, -1) &&
            auxiliary.GetFloat(owner, "JAM.esp", "_Array", -1) == 30 &&
            !auxiliary.SetFloat(owner, "JAM.esp", "_Missing", 1, 3),
            "Auxiliary indexed set, append, last-element or bounds semantics changed.");
        Require(auxiliary.Erase(owner, "JAM.esp", "_Array", 1) == 2 &&
            auxiliary.GetFloat(owner, "JAM.esp", "_Array", -1) == 30 &&
            auxiliary.Erase(owner, "JAM.esp", "_Array") == -1 &&
            auxiliary.GetType(owner, "JAM.esp", "_Array") == 0,
            "Auxiliary erase semantics changed.");

        var saved = auxiliary.CapturePermanent();
        Require(saved.Variables.All(variable => !variable.Temporary) &&
            saved.Variables.Any(variable => variable.Name == "_saved") &&
            saved.Variables.Any(variable => variable.Name == "saved") &&
            saved.Variables.Any(variable => variable.Name == "_form") &&
            saved.Variables.All(variable => variable.Name is not "*_public" and not "*private"),
            "Permanent auxiliary capture included temporary or lost visibility identity.");
        var serialized = JsonSerializer.Serialize(saved);
        var decoded = JsonSerializer.Deserialize<FalloutAuxiliaryStoreSnapshot>(serialized) ??
            throw new InvalidDataException("Auxiliary save snapshot did not deserialize.");
        decoded.Validate();
        Require(auxiliary.SetFloat(owner, "JAM.esp", "_Saved", 99), "Auxiliary restore setup failed.");
        auxiliary.RestorePermanent(saved);
        Require(auxiliary.GetFloat(owner, "JAM.esp", "_Saved") == 3 &&
            auxiliary.GetFloat(owner, "Other.esp", "*_Public") == 1.5,
            "Save reload did not restore permanent state while retaining the session state.");
        var cold = new FalloutAuxiliaryStore();
        cold.RestorePermanent(decoded);
        Require(cold.GetFloat(owner, "JAM.esp", "_Saved") == 3 &&
            cold.GetFloat(owner, "Other.esp", "*_Public") == 0,
            "A cold auxiliary owner retained temporary process state.");
        cold.SetFloat(owner, "JAM.esp", "*_Session", 6);
        cold.ResetForNewGame();
        Require(cold.GetType(owner, "JAM.esp", "_Saved") == 0 &&
            cold.GetType(owner, "JAM.esp", "*_Session") == 0,
            "New Game retained auxiliary state from the previous save lifetime.");

            Console.WriteLine("OPENNV_SCRIPT_STORAGE_PASS iniOverlay=true iniPrecedence=true auxiliaryTypes=true " +
                "auxiliaryVisibility=true auxiliaryIndices=true saveReload=true coldRestart=true newGame=true");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("An invalid script-storage path was admitted.");
    }
}
