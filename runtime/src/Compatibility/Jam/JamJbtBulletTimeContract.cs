using Godot;

namespace OpenNV.Runtime.Compatibility.Jam;

/// <summary>
/// Source-bound JAM 4.6 JBT toggle and world-time multiplier only.
/// AP, effects, audio, highlighting, events, and native extension calls remain outside it.
/// </summary>
internal sealed record JamJbtBulletTimeContract(
    string ManifestPath,
    string ManifestSha256,
    string ProfileId,
    string SourcePluginSha256,
    string DesktopPhysicalKey,
    float EffectiveTimeMultiplier,
    float ActionPointEntryCost,
    int MissingDependencyCount)
{
    internal const string CapabilityId = "jbt-bullet-time-dilation-v1";
    internal const string InputAction = "jam_jbt_bullet_time";
    private const float InstalledKeyboardScanCode = 45.0f;
    private const int InstalledControllerButton = 0;

    internal static JamJbtBulletTimeContract Load(JamProfileContract profile)
    {
        var capability = profile.Capability(CapabilityId, "jbt");

        JamProfileContract.RequireExactSourceScripts(
            capability,
            "JBTScript",
            "JBTOnKeyEventHandler",
            "JBTMainLoopEventHandler");
        JamProfileContract.RequireExactCommandContracts(
            capability,
            new[] { "DispatchEvent", "GetControl", "IsKeyPressed" },
            new[]
            {
                "GetController",
                "IsButtonPressed",
                "SetGameMainLoopCallback",
                "SetOnKeyDownEventHandler",
            },
            new[] { "JBTStateChange" });

        JamProfileContract.RequireExactStrings(
            capability,
            "supportedSemantics",
            "toggle-bullet-time",
            "authored-world-time-multiplier",
            "authored-standing-time-multiplier");
        var runtime = capability.GetProperty("runtime");
        if (!runtime.GetProperty("enabled").GetBoolean() ||
            !runtime.GetProperty("toggle").GetBoolean())
            throw new InvalidDataException(
                "The bounded JBT transport requires the installed enabled toggle mode.");
        var physicalKey = JamProfileContract.RequiredString(runtime, "desktopPhysicalKey");
        if (physicalKey != "X" ||
            runtime.GetProperty("controllerButton").GetInt32() != InstalledControllerButton)
            throw new InvalidDataException(
                "The bounded JBT transport only maps the installed keyboard setting.");

        var globals = capability.GetProperty("sourceGlobals");
        JamProfileContract.RequireExactNumber(globals, "JBTEnabled", 1.0f);
        JamProfileContract.RequireExactNumber(globals, "JBTKey", InstalledKeyboardScanCode);
        JamProfileContract.RequireExactNumber(globals, "JBTButton", InstalledControllerButton);
        JamProfileContract.RequireExactNumber(globals, "JBTToggle", 1.0f);
        var slow = globals.GetProperty("JBTSlowMult").GetSingle();
        var standing = globals.GetProperty("JBTSlowMultStanding").GetSingle();
        var effective = runtime.GetProperty("effectiveTimeMultiplier").GetSingle();
        if (!float.IsFinite(slow) || slow <= 0.0f ||
            !float.IsFinite(standing) || standing <= 0.0f ||
            !float.IsFinite(effective) || effective <= 0.0f ||
            !Mathf.IsEqualApprox(effective, slow * standing) ||
            !Mathf.IsEqualApprox(runtime.GetProperty("slowMultiplier").GetSingle(), slow) ||
            !Mathf.IsEqualApprox(runtime.GetProperty("standingMultiplier").GetSingle(), standing))
            throw new InvalidDataException("The JBT authored time multiplier is inconsistent.");

        var entryCost = globals.GetProperty("JBTAPDrain").GetSingle();
        if (!float.IsFinite(entryCost) || entryCost < 0.0f ||
            !Mathf.IsEqualApprox(runtime.GetProperty("actionPointEntryCost").GetSingle(), entryCost))
            throw new InvalidDataException("The JBT authored AP entry cost is inconsistent.");

        return new JamJbtBulletTimeContract(
            profile.ManifestPath,
            profile.ManifestSha256,
            profile.ProfileId,
            profile.SourcePluginSha256,
            physicalKey,
            effective,
            entryCost,
            profile.MissingDependencyCount);
    }
}
