using Godot;

namespace OpenNV.Runtime.Compatibility.Jam;

/// <summary>
/// One source-bound translation of JAM 4.6's JVS forward sprint speed setting.
/// This is intentionally not an xNVSE interpreter or a complete JVS runtime.
/// </summary>
internal sealed record JamJvsSprintContract(
    string ManifestPath,
    string ManifestSha256,
    string ProfileId,
    string SourcePluginSha256,
    string DesktopPhysicalKey,
    float SpeedBonusPercent,
    float SpeedMultiplier,
    int MissingDependencyCount)
{
    internal const string CapabilityId = "jvs-forward-sprint-speed-v1";
    internal const string InputAction = "jam_jvs_sprint";
    private const float PercentScale = 100.0f;
    private const float InstalledKeyboardScanCode = 42.0f;
    private const int InstalledControllerButton = 64;

    internal static JamJvsSprintContract Load(JamProfileContract profile)
    {
        var capability = profile.Capability(CapabilityId, "jvs");

        JamProfileContract.RequireExactSourceScripts(
            capability,
            "JVSScript",
            "JVSOnKeyDownEventHandler",
            "JVSMainLoopEventHandler");
        JamProfileContract.RequireExactCommandContracts(
            capability,
            new[] { "DispatchEvent", "GetControl", "IsControlPressed", "IsKeyPressed" },
            new[]
            {
                "GetController",
                "IsButtonPressed",
                "SetGameMainLoopCallback",
                "SetNthPerkEntryValue1",
                "SetOnKeyDownEventHandler",
                "SetSpeedMult",
            },
            new[] { "JVSStateChange" });

        JamProfileContract.RequireExactStrings(
            capability,
            "supportedSemantics",
            "hold-to-sprint",
            "forward-movement-only",
            "authored-speed-percent");
        var runtime = capability.GetProperty("runtime");
        if (!runtime.GetProperty("enabled").GetBoolean() ||
            runtime.GetProperty("toggle").GetBoolean())
            throw new InvalidDataException(
                "The bounded JVS sprint transport requires the authored enabled hold mode.");
        var physicalKey = JamProfileContract.RequiredString(runtime, "desktopPhysicalKey");
        if (physicalKey != "Shift" ||
            runtime.GetProperty("controllerButton").GetInt32() != InstalledControllerButton)
            throw new InvalidDataException(
                "The bounded JVS sprint transport only maps the installed keyboard setting.");

        var globals = capability.GetProperty("sourceGlobals");
        JamProfileContract.RequireExactNumber(globals, "JVSEnabled", 1.0f);
        JamProfileContract.RequireExactNumber(globals, "JVSKey", InstalledKeyboardScanCode);
        JamProfileContract.RequireExactNumber(globals, "JVSButton", InstalledControllerButton);
        JamProfileContract.RequireExactNumber(globals, "JVSToggle", 0.0f);
        var speedBonus = globals.GetProperty("JVSSpeedMult").GetSingle();
        var speedMultiplier = runtime.GetProperty("speedMultiplier").GetSingle();
        if (!float.IsFinite(speedBonus) || speedBonus < 0.0f ||
            !float.IsFinite(speedMultiplier) ||
            !Mathf.IsEqualApprox(speedMultiplier, 1.0f + speedBonus / PercentScale) ||
            !Mathf.IsEqualApprox(
                runtime.GetProperty("speedBonusPercent").GetSingle(),
                speedBonus))
            throw new InvalidDataException("The JVS authored sprint speed is inconsistent.");

        return new JamJvsSprintContract(
            profile.ManifestPath,
            profile.ManifestSha256,
            profile.ProfileId,
            profile.SourcePluginSha256,
            physicalKey,
            speedBonus,
            speedMultiplier,
            profile.MissingDependencyCount);
    }

    internal float MovementSpeed(float baseSpeed, Vector2 movement, bool sprintHeld)
    {
        if (!float.IsFinite(baseSpeed) || baseSpeed < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(baseSpeed));
        return sprintHeld && movement.Y > 0.0f
            ? baseSpeed * SpeedMultiplier
            : baseSpeed;
    }

}
