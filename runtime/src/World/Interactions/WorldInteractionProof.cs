using System.Text.Json;
using Godot;


using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Interactions;

internal static class WorldInteractionProof
{
    internal static async Task Run(
        Node host,
        CellSceneLoader.LoadedCell loaded,
        RuntimeConfiguration configuration,
        string scenePath,
        string? reportPath)
    {
        try
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var activePickups = loaded.MainContent.Pickups.Values
                .OrderBy(pickup => pickup.ReferenceFormId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var pickup = activePickups.FirstOrDefault(value => value.CanGrab)
                ?? throw new InvalidOperationException(
                    "World interaction proof found no pickup with owned dynamic collision.");
            var authored = pickup.AuthoredTransform;
            var target = authored.Origin +
                Vector3.Up * configuration.Pickup.HoldDistanceMeters;
            if (!loaded.Player.BeginPickupHoldForProof(pickup) ||
                loaded.Player.HeldPickup != pickup ||
                !pickup.IsHeld ||
                !pickup.Freeze ||
                pickup.CollisionLayer != 0u ||
                pickup.CollisionMask != 0u)
                throw new InvalidOperationException(
                    "Pickup did not enter the shared held state.");
            loaded.Player.MoveHeldPickupForProof(target);
            if (!pickup.GlobalPosition.IsEqualApprox(target))
                throw new InvalidOperationException(
                    "Held pickup did not follow the authoritative target transform.");
            loaded.Player.DropHeldPickupForProof();
            if (loaded.Player.HeldPickup is not null ||
                pickup.IsHeld ||
                pickup.Freeze ||
                pickup.CollisionLayer != configuration.Pickup.CollisionLayer ||
                pickup.CollisionMask != configuration.Pickup.CollisionMask)
                throw new InvalidOperationException(
                    "Dropped pickup did not restore its physical collision state.");

            GameplaySession? cold = null;
            PickupInstance.PickupState restored;
            try
            {
                cold = new GameplaySession();
                cold.Configure(
                    loaded.FormId,
                    loaded.EditorId,
                    loaded.ProofDoor.ReferenceFormId,
                    configuration,
                    loaded.Session.SavePath,
                    loadExistingSave: true,
                    showHud: false);
                if (!cold.TryGetLoadedPickupStateForProof(pickup.ReferenceFormId, out restored))
                    throw new InvalidOperationException(
                        "Cold session did not restore the moved pickup state.");
            }
            finally
            {
                cold?.Free();
            }
            if (!restored.Position.IsEqualApprox(target))
                throw new InvalidOperationException(
                    "Cold-restored pickup transform differs from the dropped transform.");
            pickup.RestoreState(restored);

            var report = new
            {
                schema = "opennv-world-pickup-interaction/v1",
                status = "pass",
                configurationSchema = RuntimeConfiguration.ExpectedSchema,
                configurationSha256 = configuration.Sha256,
                scene = scenePath,
                cellFormId = loaded.FormId,
                pickupReferenceFormId = pickup.ReferenceFormId,
                pickupEditorId = pickup.EditorId,
                physicsSource = pickup.PhysicsSource,
                activePickups = activePickups.Length,
                exactOwnedDynamicPickups = activePickups.Count(value => value.CanGrab),
                unsupportedPickupPhysics = activePickups.Count(value => !value.CanGrab),
                desktopControl = configuration.Player.DesktopInput.Grab.PhysicalKey,
                openXrControl = "right-primary-click",
                collectControl = configuration.Player.DesktopInput.Activate.PhysicalKey,
                heldCollisionSuppressed = true,
                droppedCollisionRestored = true,
                coldSaveRestored = true,
                hardwareValidated = false,
            };
            if (!string.IsNullOrWhiteSpace(reportPath))
                WriteReport(reportPath, report);
            GD.Print(
                $"OPENNV_WORLD_PICKUP_PASS reference={pickup.ReferenceFormId} " +
                $"physics={pickup.PhysicsSource} target={target}");
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_WORLD_PICKUP_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static void WriteReport(string path, object report)
    {
        var resolved = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        var temporary = resolved + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
            System.Environment.NewLine);
        File.Move(temporary, resolved, true);
    }
}
