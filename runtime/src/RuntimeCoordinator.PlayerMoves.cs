using Godot;
using OpenNV.Runtime.Content;
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

    private async Task StreamNativePlayerMove(FalloutReferencePlacement placement, Transform3D transform)
    {
        var current = _nativeCurrentCellRoot ?? throw new InvalidOperationException("Player MoveTo has no resident CELL root.");
        var active = _nativeActiveCell ?? throw new InvalidOperationException("Player MoveTo has no active CELL.");
        var sky = _nativeSkyLighting ?? throw new InvalidOperationException("Player MoveTo has no sky owner.");
        var previousSky = sky.Capture();
        var scene = FalloutCellSceneReader.Read(_nativePluginStack!, placement.Cell);
        var grid = scene.Cell.Worldspace is { } world ? ResolveExterior(world, placement.Position) : null;
        scene = _nativeReferences!.ComposeResidency(grid?.Scene ?? scene, grid?.Cells);
        if (grid is not null) grid = grid with { Scene = scene };
        Node3D? root = null;
        try
        {
            sky.EnterCell(scene.Cell, _nativeGlobals, placement.Position);
            SetLoadingStatus("Loading the world");
            root = await BuildNativeCellRootResponsive(scene, null, sourceSide: false);
            root.ProcessMode = ProcessModeEnum.Disabled;
            if (grid is not null) AddExteriorLandscape(root, grid);
            AddChild(root);
            if (scene.Cell.Lighting is not null) AddNativeCellEnvironment(root, scene);
            else AddExteriorEnvironment(root, scene.Cell);
            if (grid is not null)
            {
                SetLoadingStatus("Preparing the distant world");
                await root.GetChildren().OfType<Presentation.Rendering.RuntimeNativeExteriorLod>().Single().PrepareInitialSelection(transform.Origin);
            }
            CommitNativeWorldTransfer(current, active, root, scene, transform);
        }
        catch
        {
            root?.Free();
            sky.Restore(previousSky);
            DiscoverNativeCellReferences(active);
            ObserveNativeResidentReferences(active);
            throw;
        }
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
        try
        {
            player.Teleport(transform);
            driver.EnterWorldCell(scene.Cell.FormKey);
            SetNativeActiveCell(root, scene);
        }
        catch
        {
            player.RestoreTransform([previous.Origin.X, previous.Origin.Y, previous.Origin.Z],
                [rotation.X, rotation.Y, rotation.Z, rotation.W], pitch);
            driver.EnterWorldCell(active.Cell.FormKey);
            if (scene.Cell.FormKey != active.Cell.FormKey && _nativeReferences!.IsCellResident(scene.Cell.FormKey))
                _nativeReferences.UnloadCell(scene.Cell.FormKey);
            SetNativeActiveCell(current, active);
            throw;
        }
        root.ProcessMode = ProcessModeEnum.Inherit;
        NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(current);
        current.ProcessMode = ProcessModeEnum.Disabled;
        current.QueueFree();
    }
}
