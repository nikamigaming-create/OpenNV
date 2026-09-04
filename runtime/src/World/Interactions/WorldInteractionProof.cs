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
            var tutorialSkulls = activePickups
                .OfType<ScriptedActivatorInstance>()
                .Where(value =>
                    value.ReferenceFormId.Equals("00104c10", StringComparison.OrdinalIgnoreCase) &&
                    value.EditorId.Equals(
                        "VCG01SkullBrahminActivator",
                        StringComparison.OrdinalIgnoreCase) &&
                    value.Contract.ScriptEditorId.Equals(
                        "VCG01BrahminSkullSCRIPT",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (activePickups.Any(value =>
                    value.EditorId.Equals(
                        "VCG01SkullBrahminActivator",
                        StringComparison.OrdinalIgnoreCase)) &&
                tutorialSkulls.Length != 1)
                throw new InvalidOperationException(
                    "The Doc Mitchell tutorial Brahmin skull source identity is incomplete.");
            var pickup = tutorialSkulls.SingleOrDefault() ??
                activePickups.FirstOrDefault(value => value.CanGrab)
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

            var unresolvedContainerEntries = loaded.Containers.Values
                .SelectMany(container => container.Items)
                .Where(item =>
                    !item.Resolved ||
                    item.Definition is null ||
                    string.IsNullOrWhiteSpace(item.DisplayName) ||
                    item.Definition.Value is null ||
                    item.Definition.Weight is null)
                .ToArray();
            if (unresolvedContainerEntries.Length != 0)
                throw new InvalidOperationException(
                    "Doc-house container loot lacks source identity or sell data.");

            var collectedPickup = activePickups.FirstOrDefault(value =>
                value is not ScriptedActivatorInstance &&
                value.Count > 0 &&
                !string.IsNullOrWhiteSpace(value.DisplayName));
            if (collectedPickup is null)
                throw new InvalidOperationException(
                    "World interaction proof found no collectable loose item.");
            var inventoryBeforeCollection = loaded.Session.BuildUiSnapshot().Inventory
                .SingleOrDefault(item => item.FormId.Equals(
                    collectedPickup.ItemFormId,
                    StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
            loaded.Session.Collect(collectedPickup);
            var inventoryAfterCollection = loaded.Session.BuildUiSnapshot().Inventory
                .Single(item => item.FormId.Equals(
                    collectedPickup.ItemFormId,
                    StringComparison.OrdinalIgnoreCase)).Count;
            if (inventoryAfterCollection !=
                inventoryBeforeCollection + collectedPickup.Count)
                throw new InvalidOperationException(
                    "Loose-item collection did not update authoritative inventory.");

            string? lootedContainerReferenceFormId = null;
            string? lootedContainerItemFormId = null;
            var lootContainer = loaded.Containers.Values
                .Where(container => container.Items.Count > 0)
                .OrderBy(container => container.ReferenceFormId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (lootContainer is null)
                throw new InvalidOperationException(
                    "Doc-house interaction proof found no source-populated container.");
            var containerItem = lootContainer.Items.First();
            var containerInventoryBefore = loaded.Session.BuildUiSnapshot().Inventory
                .SingleOrDefault(item => item.FormId.Equals(
                    containerItem.ItemFormId,
                    StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
            loaded.Session.OpenContainer(lootContainer);
            if (!loaded.Session.IsContainerOpen)
                throw new InvalidOperationException(
                    "Source-populated container did not open its loot UI.");
            loaded.Session.TakeOneFromContainer(
                lootContainer.ReferenceFormId,
                containerItem.ItemFormId);
            loaded.Session.CloseContainerForProof();
            var containerInventoryAfter = loaded.Session.BuildUiSnapshot().Inventory
                .Single(item => item.FormId.Equals(
                    containerItem.ItemFormId,
                    StringComparison.OrdinalIgnoreCase)).Count;
            if (loaded.Session.IsContainerOpen ||
                containerInventoryAfter != containerInventoryBefore + 1)
                throw new InvalidOperationException(
                    "Container loot UI did not transfer one authoritative item.");
            lootedContainerReferenceFormId = lootContainer.ReferenceFormId;
            lootedContainerItemFormId = containerItem.ItemFormId;

            loaded.Session.TogglePipBoy();
            if (!loaded.Session.HasPipBoy || !loaded.Session.IsPipBoyOpen ||
                loaded.Session.BuildUiSnapshot().Inventory.Count == 0)
                throw new InvalidOperationException(
                    "Inventory HUD/Pip-Boy did not expose authoritative loot.");
            loaded.Session.ClosePipBoy();
            if (loaded.Session.IsPipBoyOpen)
                throw new InvalidOperationException("Pip-Boy did not close after inventory proof.");
            var finalInventory = loaded.Session.BuildUiSnapshot().Inventory
                .OrderBy(item => item.FormId, StringComparer.OrdinalIgnoreCase)
                .Select(item => (item.FormId, item.Count))
                .ToArray();

            GameplaySession? inventoryCold = null;
            try
            {
                inventoryCold = new GameplaySession();
                inventoryCold.Configure(
                    loaded.FormId,
                    loaded.EditorId,
                    loaded.ProofDoor.ReferenceFormId,
                    configuration,
                    loaded.Session.SavePath,
                    loadExistingSave: true,
                    showHud: false);
                var coldInventory = inventoryCold.BuildUiSnapshot().Inventory;
                if (!inventoryCold.IsReferenceRemoved(collectedPickup.ReferenceFormId) ||
                    !coldInventory
                        .OrderBy(item => item.FormId, StringComparer.OrdinalIgnoreCase)
                        .Select(item => (item.FormId, item.Count))
                        .SequenceEqual(finalInventory))
                    throw new InvalidOperationException(
                        "Cold Continue did not restore collected and container loot.");
            }
            finally
            {
                inventoryCold?.Free();
            }

            string? furnitureReferenceFormId = null;
            int? furnitureMarkerId = null;
            if (loaded.Furniture.Count > 0)
            {
                var furniture = loaded.Furniture.Values
                    .OrderBy(value => value.ReferenceFormId, StringComparer.OrdinalIgnoreCase)
                    .First();
                var standing = loaded.Player.GlobalTransform;
                loaded.Player.EnterFurnitureForProof(furniture);
                if (loaded.Player.ActiveFurniture != furniture ||
                    loaded.Player.GlobalTransform.IsEqualApprox(standing))
                    throw new InvalidOperationException(
                        "Player did not enter the source-authored furniture marker.");
                loaded.Player.ExitFurnitureForProof();
                if (loaded.Player.ActiveFurniture is not null ||
                    !loaded.Player.GlobalTransform.IsEqualApprox(standing))
                    throw new InvalidOperationException(
                        "Player did not leave furniture at the pre-seat transform.");
                furnitureReferenceFormId = furniture.ReferenceFormId;
                furnitureMarkerId = furniture.MarkerId;
            }

            var report = new
            {
                schema = "opennv-world-pickup-interaction/v2",
                status = "pass",
                configurationSchema = RuntimeConfiguration.ExpectedSchema,
                configurationSha256 = configuration.Sha256,
                scene = scenePath,
                cellFormId = loaded.FormId,
                pickupReferenceFormId = pickup.ReferenceFormId,
                pickupEditorId = pickup.EditorId,
                tutorialBrahminSkullValidated = tutorialSkulls.Length == 1,
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
                loot = new
                {
                    looseReferenceFormId = collectedPickup.ReferenceFormId,
                    looseItemFormId = collectedPickup.ItemFormId,
                    containerReferenceFormId = lootedContainerReferenceFormId,
                    containerItemFormId = lootedContainerItemFormId,
                    containers = loaded.Containers.Count,
                    containerEntries = loaded.Containers.Values.Sum(value => value.Items.Count),
                    unresolvedContainerEntries = unresolvedContainerEntries.Length,
                    hudInventoryEntries = loaded.Session.BuildUiSnapshot().Inventory.Count,
                    pipBoyOpenedAndClosed = true,
                    coldContinueRestored = true,
                },
                furniture = new
                {
                    available = loaded.Furniture.Count,
                    seatedAndStood = furnitureReferenceFormId is not null,
                    referenceFormId = furnitureReferenceFormId,
                    markerId = furnitureMarkerId,
                },
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
