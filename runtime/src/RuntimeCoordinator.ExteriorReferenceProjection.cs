using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void QueueNativeExteriorReferenceProjection(FalloutReferenceWorld world, Node3D root, FalloutCellScene scene)
    {
        if (_nativeSharedGridChange is not null || _nativeGridRead is not null || _nativeGridUploads is not null)
        {
            CancelNativeGridRead();
            _nativeGridUploads = null; _nativeGridPending = null; _nativeGridTarget = null;
            _nativeGridStaged.Clear(); _nativeGridModels.Clear();
            if (AdvanceNativeSharedGridCancellation()) return;
        }
        var owner = RequireNativeSourceCellAttachment(root);
        var attachment = world.CellProcesses.ReadAttachment(owner.Identity);
        var cells = attachment.CellConsumers.Where(cell => cell.Source.Landscape is not null)
            .Select(cell => FalloutCellSceneReader.ReadDefinition(_nativePluginStack!, cell.Source.Cell.Cell)).ToArray();
        var updated = world.ComposeResidency(scene, cells);
        var source = _nativeExteriorGrid ??= new(_nativePluginStack!);
        var radius = root.GetMeta("opennv_exterior_grid_radius").AsInt32();
        if (radius < 0) throw new InvalidDataException("Exterior reference replacement lost its original presentation grid inputs.");
        var grid = new FalloutExteriorGridScene(updated, cells, source.PersistentCell(scene.Cell.Worldspace!.Value), radius);
        _nativeGridReadCancellation?.Dispose();
        _nativeGridReadCancellation = new(); _nativeGridTarget = scene.Cell.Coordinates;
        _nativeStreamRoot = root;
        StageNativeExteriorGrid(new(grid, new Dictionary<FalloutFormKey, FalloutLandscapeTransport>(),
            new Dictionary<string, OpenNV.Runtime.Formats.Gamebryo.FalloutNifFile>(StringComparer.OrdinalIgnoreCase)));
    }
    private bool InvalidateChangedNativeSharedPlacementInput()
    {
        if (_nativeSharedGridRoot is not { } root || !_nativeCellProcessAttachments.TryGetValue(root, out var owner) ||
            _nativeReferences is not { } world || _nativeSharedGridPlacementRevision == world.PlacementRevision ||
            world.CellProcesses.ReadSharedGraph(owner.Identity)?.Phase != FalloutCellSharedGraphPhase.Preparing) return false;
        // The source command has already changed authoritative state. Cancel
        // only obsolete prepared native work, never rewind the script or pose.
        CancelNativeGridRead(); _nativeGridUploads = null; _nativeGridPending = null; _nativeGridTarget = null;
        _nativeGridStaged.Clear(); _nativeGridModels.Clear();
        _ = AdvanceNativeSharedGridCancellation(); return true;
    }
    private void ApplyNativeSharedReferencePlacements(FalloutCellScene scene)
    {
        if (_nativeReferences is not { } world || _nativeReferencePresentation is not { } presentation) return;
        foreach (var instance in world.MovedSince(_nativePlacementRevision).Where(instance => world.IsResident(instance.Reference)))
        {
            var reference = scene.References.Single(value => value.FormKey == instance.Reference);
            if (presentation.Nodes.GetValueOrDefault(instance.Reference) is { } node ||
                world.IsEnabled(instance.Reference) && (node = presentation.Resolve(instance.Reference)) is not null)
            {
                node.Transform = ReferenceTransform(reference);
                if (node is CharacterBody3D body) body.Velocity = Vector3.Zero;
            }
        }
    }
}
