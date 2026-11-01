using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void AdvanceNativePlayerMoves()
    {
        if (_nativeDoorLoading || _nativeSessionTransitioning || _retiringNativeSession ||
            _nativeReferences is not { } references || references.PlayerMoves.Next is not { } move ||
            _nativePlayer is not { } player || _nativeOpeningStageDriver is not { } driver || _nativeActiveCell is not { } active)
            return;
        _nativePlayerMoveRead = ConsumeNativePlayerMove(references, player, driver, active, move);
    }

    private async Task ConsumeNativePlayerMove(FalloutReferenceWorld references, RuntimeNativePlayer player,
        OpenNV.Runtime.Campaigns.NewVegas.Opening.RuntimeNativeOpeningStageDriver driver, FalloutCellScene active, FalloutPlayerMove move)
    {
        var previousModal = player.ModalInput;
        _nativeDoorLoading = true;
        player.SetModalInput(true);
        try
        {
            var destination = _nativePluginStack!.RuntimeFormId(move.Destination) == 0x14 ? active.Cell.FormKey : references.Placement(move.Destination).Cell;
            if (destination != active.Cell.FormKey) await ShowNativeLoadingScreens(destination);
            await RuntimeNativePlayerMoves.ApplyNextAsync(references, player, active.Cell.FormKey, StreamNativePlayerMove);
            driver.FastTravel.CompleteRelocation();
            GD.Print($"OPENNV_NATIVE_PLAYER_MOVETO source={move.Source} target={move.Destination} " +
                $"from={active.Cell.FormKey} to={driver.ActiveCell} owner=queued-source-command parity=unverified");
        }
        catch (Exception error)
        {
            if (references.PlayerMoves.Error is null) references.PlayerMoves.Fail(move, error);
            SetMeta("opennv_player_move_error", error.Message);
            GD.PushError($"OPENNV_NATIVE_PLAYER_MOVETO_FAIL source={move.Source} target={move.Destination} {error}");
        }
        finally
        {
            CloseNativeLoadingScreens();
            _nativeDoorLoading = false;
            player.SetModalInput(previousModal);
        }
    }

    private Task StreamNativePlayerMove(FalloutReferencePlacement placement, Transform3D transform) =>
        StreamNativePlayerMove(placement, transform, transferFollowers: false);

    private async Task StreamNativePlayerMove(FalloutReferencePlacement placement, Transform3D transform, bool transferFollowers)
    {
        var current = _nativeCurrentCellRoot ?? throw new InvalidOperationException("Player MoveTo has no resident CELL root.");
        var active = _nativeActiveCell ?? throw new InvalidOperationException("Player MoveTo has no active CELL.");
        var sky = _nativeSkyLighting ?? throw new InvalidOperationException("Player MoveTo has no sky owner.");
        var previousSky = sky.Capture();
        var previousMode = current.ProcessMode;
        var scripts = _nativeQuestScripts!;
        var previousScriptMode = scripts.ProcessMode;
        var scene = FalloutCellSceneReader.Read(_nativePluginStack!, placement.Cell);
        var grid = scene.Cell.Worldspace is { } world ? ResolveExterior(world, placement.Position) : null;
        IReadOnlyList<TravelFollower> followers = transferFollowers ? BeginFollowerTransfer(current, placement) : [];
        Node3D? root = null;
        var previousPresentation = _nativeReferencePresentation!;
        FalloutReferenceWorld.RegionCellEntry? regionEntry = null;
        IReadOnlyList<(FalloutFormKey Reference, Transform3D Pose, Vector3 Velocity)> retiredActors = [];
        (CollisionObject3D Body, CollisionObject3D.DisableModeEnum Mode)[] arrivalCollision = [];
        try
        {
            regionEntry = _nativeReferences!.BeginRegionCellEntry((grid?.Scene ?? scene).Cell, placement.Position);
            scene = _nativeReferences!.ComposeResidency(grid?.Scene ?? scene, _configuration.World.GameUnitsToMeters, grid?.Cells);
            if (grid is not null) grid = grid with { Scene = scene };
            sky.EnterCell(scene.Cell, _nativeGlobals, placement.Position);
            SetLoadingStatus("Loading the world");
            RetireNativeGridPreparation();
            await previousPresentation.PrepareActorAudioTransferAsync([scripts]);
            current.ProcessMode = ProcessModeEnum.Disabled;
            scripts.ProcessMode = ProcessModeEnum.Disabled;
            previousPresentation.RetireOverlappingActors(scene.References, out retiredActors);
            root = await BuildNativeCellRootResponsive(scene, null, sourceSide: false, placement.Position);
            root.ProcessMode = ProcessModeEnum.Disabled;
            if (grid is not null) await AddExteriorLandscapeResponsive(root, grid);
            AddChild(root);
            RequireNativeActorPublication(root);
            if (scene.Cell.Lighting is not null) AddNativeCellEnvironment(root, scene);
            else AddExteriorEnvironment(root, scene.Cell);
            arrivalCollision = await PrepareFollowerArrival(current, root, scene, grid?.Cells, placement, transform, followers);
            if (grid is not null)
            {
                SetLoadingStatus("Preparing the distant world");
                await root.GetChildren().OfType<Presentation.Rendering.RuntimeNativeExteriorLod>().Single().PrepareInitialSelection(transform.Origin);
            }
            CommitNativeWorldTransfer(current, active, root, scene, transform);
            regionEntry.Commit();
        }
        catch (Exception error) when (root is not null && ReferenceEquals(_nativeCurrentCellRoot, root))
        {
            regionEntry?.Commit();
            // Publication committed. Retiring the outgoing presentation is
            // irreversible; never free the authoritative incoming root here.
            _nativeOpeningStageDriver!.RetainDriverFailure(error);
            foreach (var (body, mode) in arrivalCollision) body.DisableMode = mode;
            GetTree().Paused = true;
            throw;
        }
        catch (Exception error)
        {
            regionEntry?.Dispose();
            var retirementError = DiscardNativeStagedSubtree(root);
            sky.Restore(previousSky);
            // Staged environment construction also changes shared material
            // uniforms. Republish their surviving source owner on rollback.
            current.GetChildren().OfType<RuntimeNativeCellLighting>().SingleOrDefault()?.Publish();
            current.GetChildren().OfType<RuntimeNativeExteriorEnvironment>().SingleOrDefault()?.Republish();
            RollbackFollowerTransfer(followers);
            previousPresentation.RestoreRetiredActors(retiredActors);
            current.GetChildren().OfType<RuntimeNativeReferenceEvents>().Single().SetResidency(active, current);
            current.ProcessMode = previousMode;
            DiscoverNativeCellReferences(active);
            ObserveNativeResidentReferences(active);
            if (retirementError is not null)
                throw new AggregateException("Player transfer and staged actor retirement failed after rollback.", error, retirementError);
            throw;
        }
        finally { regionEntry?.Dispose(); scripts.ProcessMode = previousScriptMode; }
        foreach (var (body, mode) in arrivalCollision) body.DisableMode = mode;
    }

    private void CommitNativeWorldTransfer(Node3D current, FalloutCellScene active,
        Node3D root, FalloutCellScene scene, Transform3D transform)
    {
        var player = _nativePlayer!;
        if (player.FurnitureActive)
            throw new NotSupportedException("Moving an active furniture interaction requires its interruption owner.");
        var previous = player.GlobalTransform;
        var rotation = previous.Basis.GetRotationQuaternion().Normalized();
        var pitch = player.ViewPitchRadians;
        var driver = _nativeOpeningStageDriver!;
        var previousTravel = driver.FastTravel.State;
        try
        {
            player.Teleport(transform);
            driver.EnterWorldCell(scene.Cell.FormKey);
            SetNativeActiveCell(root, scene);
            driver.FastTravel.CompleteRelocation();
        }
        catch
        {
            player.RestoreTransform([previous.Origin.X, previous.Origin.Y, previous.Origin.Z],
                [rotation.X, rotation.Y, rotation.Z, rotation.W], pitch);
            driver.EnterWorldCell(active.Cell.FormKey);
            driver.FastTravel.Restore(previousTravel);
            if (scene.Cell.FormKey != active.Cell.FormKey && _nativeReferences!.IsCellResident(scene.Cell.FormKey))
                _nativeReferences.UnloadCell(scene.Cell.FormKey);
            SetNativeActiveCell(current, active);
            throw;
        }
        root.ProcessMode = ProcessModeEnum.Inherit;
        Exception? retirementFailure = null;
        try
        {
            // Capture/unbind while the old bodies are still in the tree. Child
            // exit order cannot provide a live physics continuation during Free.
            try { current.GetChildren().OfType<RuntimeNativeReferencePresentation>().Single().RetireBindings(); }
            catch (Exception error) { retirementFailure ??= error; }
            try { RetireNativeActorBindings(current); }
            catch (Exception error) { retirementFailure ??= error; }
            try { NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(current); }
            catch (Exception error) { retirementFailure ??= error; }
        }
        finally
        {
            current.ProcessMode = ProcessModeEnum.Disabled;
            current.QueueFree();
        }
        if (retirementFailure is not null)
            throw new InvalidOperationException("Committed world transfer could not retire its outgoing owners.", retirementFailure);
    }
}
