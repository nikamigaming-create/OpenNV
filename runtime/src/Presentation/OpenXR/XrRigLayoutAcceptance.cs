using Godot;


using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.OpenXR;

internal static class XrRigLayoutAcceptance
{
    internal static void Run(
        RuntimeCoordinator host,
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration)
    {
        var contract = configuration.Xr.Contract;
        var proof = configuration.Xr.DiagnosticRigProof;
        var actionMap = ResourceLoader.Load(contract.ActionMapResourcePath)
            ?? throw new InvalidOperationException("OpenNV OpenXR action map could not be loaded.");
        var actionSets = actionMap.Get("action_sets").AsGodotArray();
        if (actionSets.Count != contract.ExpectedActionSetCount)
            throw new InvalidOperationException(
                "OpenNV OpenXR action-map set count disagrees with configuration.");
        var actionSet = actionSets[0].AsGodotObject() as Resource
            ?? throw new InvalidOperationException("OpenNV OpenXR gameplay action set is invalid.");
        var actions = actionSet.Get("actions").AsGodotArray();
        if (actions.Count != contract.ActionNames.Count)
            throw new InvalidOperationException(
                "OpenNV OpenXR action count disagrees with configuration.");
        var actionNames = actions
            .Select(value => value.AsGodotObject() as Resource
                ?? throw new InvalidOperationException("OpenNV OpenXR action is invalid."))
            .Select(action => action.ResourceName)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expectedActions = contract.ActionNames
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!actionNames.SequenceEqual(expectedActions, StringComparer.Ordinal))
            throw new InvalidOperationException("OpenNV OpenXR action names are incomplete.");
        var interactionProfiles = actionMap.Get("interaction_profiles").AsGodotArray()
            .Select(value => value.AsGodotObject() as Resource
                ?? throw new InvalidOperationException(
                    "OpenNV OpenXR interaction profile is invalid."))
            .Select(profile => profile.Get("interaction_profile_path").AsString())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expectedProfiles = contract.InteractionProfilePaths
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!interactionProfiles.SequenceEqual(expectedProfiles, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "OpenNV OpenXR interaction profile set is incomplete.");

        Engine.PhysicsTicksPerSecond = configuration.Simulation.PhysicsTicksPerSecond;
        var session = new GameplaySession();
        session.Configure(
            proof.SessionId,
            proof.SessionId,
            proof.SessionId,
            configuration,
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            true);
        host.AddChild(session);
        var player = new CellPlayer();
        player.Configure(0.0f, session, configuration, true);
        host.AddChild(player);
        session.PrepareStartingLoadout(new GameplaySession.StartingWeapon(
            proof.WeaponFormId,
            proof.WeaponEditorId,
            proof.AmmoFormId,
            proof.AmmoEditorId,
            proof.Damage,
            proof.ClipSize,
            proof.ReserveRounds));
        if (!session.Fire(player.RightAim!, player.CollisionMask) || !session.Reload())
            throw new InvalidOperationException("OpenNV OpenXR fire/reload contract failed.");
        if (session.ShotsFired != proof.ExpectedShotsFired ||
            session.AmmoInMagazine != proof.ExpectedAmmoInMagazineAfterReload ||
            session.ReserveAmmo != proof.ExpectedReserveRoundsAfterReload)
            throw new InvalidOperationException(
                "OpenNV OpenXR ammunition outcome disagrees with configuration.");
        if (!player.UsesXr || player.Camera is not XRCamera3D || player.XrOrigin is null ||
            player.LeftGrip is null || player.RightGrip is null ||
            player.LeftAim is null || player.RightAim is null || !session.HasXrHud ||
            !Mathf.IsEqualApprox(player.XrOrigin.WorldScale, configuration.Xr.WorldScale))
            throw new InvalidOperationException("OpenNV OpenXR rig hierarchy is incomplete.");

        var report = new
        {
            schema = "opennv-openxr-rig/v3",
            status = "pass",
            evidenceLevel = "layout-only",
            hardwareHeadsetValidated = false,
            windowsAppControlUsed = false,
            foregroundInputInjected = false,
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = configuration.Sha256,
            initializedRuntimeRequiredForPlay = true,
            viewportXrEnabledDuringProof = host.GetViewport().UseXR,
            actionMap = contract.ActionMapResourcePath,
            actionSets = actionSets.Count,
            actions = actions.Count,
            actionNames,
            testedInteractionProfiles = interactionProfiles,
            originType = player.XrOrigin.GetClass().ToString(),
            cameraType = player.Camera.GetClass().ToString(),
            leftControllerType = player.LeftGrip.GetClass().ToString(),
            rightControllerType = player.RightGrip.GetClass().ToString(),
            visibleProvider = "owned-data-required-at-cell-load",
            leftTracker = player.LeftGrip.Tracker.ToString(),
            rightTracker = player.RightGrip.Tracker.ToString(),
            gripPose = player.RightGrip.Pose.ToString(),
            aimPose = player.RightAim.Pose.ToString(),
            worldScale = player.XrOrigin.WorldScale,
            desiredEyeHeightMeters = player.DesiredEyeHeightMeters,
            physicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
            worldSpaceHud = session.HasXrHud,
            sharedSaveSchema = session.Report(),
        };
        if (options.TryGetValue("report", out var reportPath))
            RuntimeCoordinator.WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_OPENXR_RIG_PASS profiles=generic,oculus-touch " +
            $"worldScale={configuration.Xr.WorldScale} " +
            $"physicsHz={configuration.Simulation.PhysicsTicksPerSecond}");
    }
}
